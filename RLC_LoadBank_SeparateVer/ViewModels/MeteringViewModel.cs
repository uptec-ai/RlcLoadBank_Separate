using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;
using SciChart.Charting.Model.DataSeries;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>
    /// 계측현황 screen: GIMAC bus KPIs + EOCR line meters (#1–#10) + a live
    /// SciChart trend. Mock data for now (real values come from the ISEM/GIMAC
    /// Modbus reads later). Measurement items: .claude/docs/measurement-items.md.
    /// </summary>
    public class MeteringViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _timer;
        private double _t;

        public ObservableCollection<string> Sources { get; } =
            new ObservableCollection<string> { "BUS IN", "BUS OUT 1", "BUS OUT 2", "BUS OUT 3" };
        public string SelectedSource { get => GetValue<string>(); set => SetValue(value); }

        // GIMAC KPIs
        public double AvgVoltage { get => GetValue<double>(); set => SetValue(value); }
        public double AvgCurrent { get => GetValue<double>(); set => SetValue(value); }
        public double ActivePower { get => GetValue<double>(); set => SetValue(value); }
        public double ReactivePower { get => GetValue<double>(); set => SetValue(value); }
        public double Pf { get => GetValue<double>(); set => SetValue(value); }
        public double Frequency { get => GetValue<double>(); set => SetValue(value); }
        public double VoltageThd { get => GetValue<double>(); set => SetValue(value); }

        public ObservableCollection<MeterLine> Lines { get; } = new ObservableCollection<MeterLine>();

        // SciChart live trend (FIFO)
        public XyDataSeries<DateTime, double> VoltageSeries { get; }
        public XyDataSeries<DateTime, double> CurrentSeries { get; }
        public XyDataSeries<DateTime, double> PowerSeries { get; }

        public MeteringViewModel()
        {
            SelectedSource = Sources[0];

            VoltageSeries = new XyDataSeries<DateTime, double> { SeriesName = "전압(V)", FifoCapacity = 120 };
            CurrentSeries = new XyDataSeries<DateTime, double> { SeriesName = "전류(A)", FifoCapacity = 120 };
            PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "유효전력(kW)", FifoCapacity = 120 };

            for (int i = 1; i <= 10; i++)
                Lines.Add(new MeterLine { No = i, Voltage = 380, Current = 120, Power = 70, Pf = 0.95, Thd = 2.5 });

            Tick();   // seed first sample
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();
        }

        private void Tick()
        {
            _t += 1;
            double sin = Math.Sin(_t / 6.0);
            double cos = Math.Cos(_t / 9.0);

            AvgVoltage = 380 + 3 * sin;
            AvgCurrent = 150 + 18 * cos;
            ActivePower = Math.Round(AvgVoltage * AvgCurrent * 1.732 * 0.95 / 1000.0, 1);
            ReactivePower = Math.Round(ActivePower * 0.33, 1);
            Pf = Math.Round(0.95 + 0.02 * sin, 3);
            Frequency = Math.Round(60.0 + 0.05 * sin, 2);
            VoltageThd = Math.Round(2.5 + 0.6 * cos, 1);

            var now = DateTime.Now;
            VoltageSeries.Append(now, AvgVoltage);
            CurrentSeries.Append(now, AvgCurrent);
            PowerSeries.Append(now, ActivePower);

            for (int i = 0; i < Lines.Count; i++)
            {
                var ph = Math.Sin((_t + i * 3) / 7.0);
                Lines[i].Current = Math.Round(120 + 15 * ph, 2);
                Lines[i].Power = Math.Round(70 + 9 * ph, 1);
            }
        }
    }
}
