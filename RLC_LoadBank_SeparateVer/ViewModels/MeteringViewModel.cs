using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;
using SciChart.Charting.Model.DataSeries;
using SciChart.Data.Model;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>
    /// 계측현황 screen: GIMAC bus KPIs + EOCR line meters (#1–#10) + live power trend.
    ///
    /// Power trend (bottom chart): shows ActivePower (kW) for each panel GIMAC.
    ///   PNL-1 → GIMAC1000_01 (UnitId=1)  blue  #2F6FE0
    ///   PNL-2 → GIMAC1000_02 (UnitId=2)  orange #E8943A
    ///   PNL-3 → GIMAC1000_03 (UnitId=3)  green  #27A86A
    ///
    /// KPI cards and EOCR line data: still fed by DispatcherTimer (mock) until
    /// real ISEM integration is added.
    /// </summary>
    public class MeteringViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _timer;
        private double _t;

        // ── Source selector (KPI cards) ──────────────────────────────────────
        public ObservableCollection<string> Sources { get; } =
            new ObservableCollection<string> { "BUS IN", "BUS OUT 1", "BUS OUT 2", "BUS OUT 3" };
        public string SelectedSource { get => GetValue<string>(); set => SetValue(value); }

        // ── GIMAC KPI cards (mock — replace when real GIMAC data wired here) ─
        public double AvgVoltage    { get => GetValue<double>(); set => SetValue(value); }
        public double AvgCurrent    { get => GetValue<double>(); set => SetValue(value); }
        public double ActivePower   { get => GetValue<double>(); set => SetValue(value); }
        public double ReactivePower { get => GetValue<double>(); set => SetValue(value); }
        public double Pf            { get => GetValue<double>(); set => SetValue(value); }
        public double Frequency     { get => GetValue<double>(); set => SetValue(value); }
        public double VoltageThd    { get => GetValue<double>(); set => SetValue(value); }

        // ── EOCR line meters ─────────────────────────────────────────────────
        public ObservableCollection<MeterLine> Lines { get; } = new ObservableCollection<MeterLine>();

        // ── GIMAC connection states (for trend header status dots) ────────────
        public bool Pnl1GimacConnected { get => GetValue<bool>(); set => SetValue(value); }
        public bool Pnl2GimacConnected { get => GetValue<bool>(); set => SetValue(value); }
        public bool Pnl3GimacConnected { get => GetValue<bool>(); set => SetValue(value); }

        // ── Power trend series — one per panel GIMAC (FIFO 120 samples ≈ 60 s) ─
        public XyDataSeries<DateTime, double> Pnl1PowerSeries { get; }
        public XyDataSeries<DateTime, double> Pnl2PowerSeries { get; }
        public XyDataSeries<DateTime, double> Pnl3PowerSeries { get; }

        // Y-axis visible range: auto-grows when real data exceeds default.
        // Minimum span of 10 kW ensures flat-0 line is always visible.
        public DoubleRange PowerYAxisRange { get => GetValue<DoubleRange>(); set => SetValue(value); }

        public MeteringViewModel()
        {
            SelectedSource = Sources[0];

            Pnl1PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-1 유효전력(kW)", FifoCapacity = 120 };
            Pnl2PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-2 유효전력(kW)", FifoCapacity = 120 };
            Pnl3PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-3 유효전력(kW)", FifoCapacity = 120 };
            PowerYAxisRange  = new DoubleRange(-1, 10);  // default: ensures 0 kW line is visible

            for (int i = 1; i <= 10; i++)
                Lines.Add(new MeterLine { No = i, Voltage = 380, Current = 120, Power = 70, Pf = 0.95, Thd = 2.5 });

            // Sync GIMAC connection states from the MeteringService singleton
            SyncGimacStates();
            ServiceHub.Metering.ConnectionChanged  += OnMeteringConnectionChanged;
            ServiceHub.Metering.GimacDataReceived  += OnGimacDataReceived;

            // DispatcherTimer: drives KPI cards + EOCR line mock data
            Tick();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        // ── GIMAC state sync ──────────────────────────────────────────────────

        private void SyncGimacStates()
        {
            foreach (var d in ServiceHub.Devices.LoadDevices().Where(d => d.Type == DeviceType.GIMAC))
                SetGimacConnected(d.UnitId, ServiceHub.Metering.IsConnected(d.Ip, d.Port, d.UnitId));
        }

        private void SetGimacConnected(int unitId, bool connected)
        {
            switch (unitId)
            {
                case 1: Pnl1GimacConnected = connected; break;
                case 2: Pnl2GimacConnected = connected; break;
                case 3: Pnl3GimacConnected = connected; break;
            }
        }

        private void OnMeteringConnectionChanged(object sender, DeviceRecord rec)
        {
            if (rec.Type != DeviceType.GIMAC) return;
            bool connected = ServiceHub.Metering.IsConnected(rec.Ip, rec.Port, rec.UnitId);
            SetGimacConnected(rec.UnitId, connected);
        }

        // ── GIMAC data → power trend series ──────────────────────────────────

        private void OnGimacDataReceived(object sender, GimacReading r)
        {
            // ActivePower from GIMAC register is in W; convert to kW for chart
            double kw = r.ActivePower / 1000.0;
            var ts = r.Timestamp;

            switch (r.Device.UnitId)
            {
                case 1: Pnl1PowerSeries.Append(ts, kw); break;
                case 2: Pnl2PowerSeries.Append(ts, kw); break;
                case 3: Pnl3PowerSeries.Append(ts, kw); break;
            }

            UpdatePowerYAxisRange(kw);
        }

        // Expand Y-axis range when data exceeds the current max.
        // Never shrinks below the initial -1..10 default (keeps 0 visible).
        private void UpdatePowerYAxisRange(double latestKw)
        {
            const double minTop  = 10.0;  // minimum top (kW), keeps 0 visible
            const double padding = 0.1;   // 10% head-room above the peak

            double currentMax = PowerYAxisRange.Max;
            double needed = latestKw * (1.0 + padding);

            if (needed > currentMax)
                PowerYAxisRange = new DoubleRange(-1, Math.Max(minTop, needed));
        }

        // ── Mock KPI + EOCR line data (DispatcherTimer) ──────────────────────

        private void Tick()
        {
            _t += 1;
            double sin = Math.Sin(_t / 6.0);
            double cos = Math.Cos(_t / 9.0);

            AvgVoltage    = 380 + 3 * sin;
            AvgCurrent    = 150 + 18 * cos;
            ActivePower   = Math.Round(AvgVoltage * AvgCurrent * 1.732 * 0.95 / 1000.0, 1);
            ReactivePower = Math.Round(ActivePower * 0.33, 1);
            Pf            = Math.Round(0.95 + 0.02 * sin, 3);
            Frequency     = Math.Round(60.0 + 0.05 * sin, 2);
            VoltageThd    = Math.Round(2.5 + 0.6 * cos, 1);

            for (int i = 0; i < Lines.Count; i++)
            {
                var ph = Math.Sin((_t + i * 3) / 7.0);
                Lines[i].Current = Math.Round(120 + 15 * ph, 2);
                Lines[i].Power   = Math.Round(70 + 9 * ph, 1);
            }
        }
    }
}
