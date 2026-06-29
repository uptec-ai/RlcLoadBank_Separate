using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;
using SciChart.Charting.Model.DataSeries;
using SciChart.Data.Model;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    public class PanelSelectItem
    {
        public string Label { get; set; }
        public int    Index { get; set; }
    }

    public class MeterDataRow
    {
        public string Category { get; set; }
        public string Name     { get; set; }
        public string Value    { get; set; }
        public string Unit     { get; set; }
    }

    public class MeteringViewModel : ViewModelBase
    {
        // Buffer capacities
        private const int MaxRaw  = 300;   // ~2.5 min at 500ms (+ processing overhead)
        private const int MaxMin  = 120;   // 2 h  of 1-min averages
        private const int MaxHour = 48;    // 2 d  of 1-hr  averages
        private const int MaxDay  = 14;    // 2 wk of 1-day averages

        // Aggregation periods
        private const double MinuteSec = 60.0;
        private const double HourMin   = 60.0;
        private const double DayHr     = 24.0;

        // Raw / aggregated buffers [panelIdx 0/1/2]
        private readonly Queue<(DateTime ts, double kw)>[] _rawBuf  = MakeQueues();
        private readonly Queue<(DateTime ts, double kw)>[] _minBuf  = MakeQueues();
        private readonly Queue<(DateTime ts, double kw)>[] _hourBuf = MakeQueues();
        private readonly Queue<(DateTime ts, double kw)>[] _dayBuf  = MakeQueues();

        // Time-based aggregation tracking (replaces sample-count modulo)
        private readonly DateTime[] _lastMinAgg  = new DateTime[3];
        private readonly DateTime[] _lastHourAgg = new DateTime[3];
        private readonly DateTime[] _lastDayAgg  = new DateTime[3];

        // Latest readings
        private readonly GimacReading[]               _lastGimac = new GimacReading[3];
        private readonly Dictionary<int, IsemReading> _lastIsem  = new();

        // Delta series [periodIdx: 0=1m, 1=1h, 2=1day][panelIdx 0/1/2]
        // FIFO: 1m=60pts(1h history), 1h=24pts(1d history), 1day=7pts(1w history)
        private readonly XyDataSeries<DateTime, double>[][] _delta;

        // X축 슬라이딩 창 크기 (SelectedPeriod의 10포인트 분량)
        private TimeSpan _xWindowSize = TimeSpan.FromMinutes(10);

        // ── Panel selector ────────────────────────────────────────────────────
        public ObservableCollection<PanelSelectItem> Panels { get; }

        public PanelSelectItem SelectedPanel
        {
            get => GetValue<PanelSelectItem>();
            set { SetValue(value); OnPanelChanged(); }
        }

        // ── KPI cards (top section, selected panel GIMAC) ─────────────────────
        public double KpiVoltage       { get => GetValue<double>(); set => SetValue(value); }
        public double KpiCurrent       { get => GetValue<double>(); set => SetValue(value); }
        public double KpiActivePower   { get => GetValue<double>(); set => SetValue(value); }
        public double KpiReactivePower { get => GetValue<double>(); set => SetValue(value); }
        public double KpiPowerFactor   { get => GetValue<double>(); set => SetValue(value); }
        public double KpiFrequency     { get => GetValue<double>(); set => SetValue(value); }
        public double KpiVoltThd       { get => GetValue<double>(); set => SetValue(value); }

        // ── Detail DataGrid (middle section) ──────────────────────────────────
        public ObservableCollection<MeterDataRow> DataRows { get; } = new();

        // ── Delta trend (bottom section) ──────────────────────────────────────
        public string SelectedPeriod
        {
            get => GetValue<string>();
            set { SetValue(value); OnPeriodChanged(); }
        }
        public DelegateCommand<string> SelectPeriodCommand { get; }

        // Exposed series — swapped on period change
        public XyDataSeries<DateTime, double> Pnl1DeltaSeries
        { get => GetValue<XyDataSeries<DateTime, double>>(); set => SetValue(value); }
        public XyDataSeries<DateTime, double> Pnl2DeltaSeries
        { get => GetValue<XyDataSeries<DateTime, double>>(); set => SetValue(value); }
        public XyDataSeries<DateTime, double> Pnl3DeltaSeries
        { get => GetValue<XyDataSeries<DateTime, double>>(); set => SetValue(value); }

        public string    DeltaChartTitle  { get => GetValue<string>();    set => SetValue(value); }
        public string    DeltaXAxisFormat { get => GetValue<string>();    set => SetValue(value); }
        // X축 VisibleRange: delta append 시 갱신하여 최신 10포인트 창으로 슬라이딩
        public DateRange DeltaXRange      { get => GetValue<DateRange>(); set => SetValue(value); }

        // GIMAC connection dots in trend legend
        public bool Pnl1GimacConnected { get => GetValue<bool>(); set => SetValue(value); }
        public bool Pnl2GimacConnected { get => GetValue<bool>(); set => SetValue(value); }
        public bool Pnl3GimacConnected { get => GetValue<bool>(); set => SetValue(value); }

        // ── Constructor ───────────────────────────────────────────────────────
        public MeteringViewModel()
        {
            Panels = new ObservableCollection<PanelSelectItem>
            {
                new() { Label = "PNL-1", Index = 0 },
                new() { Label = "PNL-2", Index = 1 },
                new() { Label = "PNL-3", Index = 2 },
            };

            // 3 periods × 3 panels = 9 series
            int[] fifos   = { 60, 24, 7 };
            string[] plab = { "PNL-1", "PNL-2", "PNL-3" };
            string[] per  = { "1m", "1h", "1day" };
            _delta = new XyDataSeries<DateTime, double>[3][];
            for (int p = 0; p < 3; p++)
            {
                _delta[p] = new XyDataSeries<DateTime, double>[3];
                for (int i = 0; i < 3; i++)
                    _delta[p][i] = new XyDataSeries<DateTime, double>
                    {
                        SeriesName   = $"{plab[i]} Δ{per[p]}",
                        FifoCapacity = fifos[p],
                    };
            }

            SelectPeriodCommand = new DelegateCommand<string>(p => SelectedPeriod = p);

            // Init time trackers so aggregation starts after first full period
            var now = DateTime.Now;
            for (int i = 0; i < 3; i++)
            {
                _lastMinAgg[i]  = now;
                _lastHourAgg[i] = now;
                _lastDayAgg[i]  = now;
            }

            SelectedPanel  = Panels[0];
            SelectedPeriod = "1m";   // → OnPeriodChanged() sets _xWindowSize

            // Initial X range: empty state placeholder until first data arrives
            DeltaXRange = new DateRange(now.AddMinutes(-10), now.AddMinutes(1));

            SyncGimacStates();
            ServiceHub.Metering.ConnectionChanged += OnConnectionChanged;
            ServiceHub.Metering.GimacDataReceived += OnGimacData;
            ServiceHub.Metering.IsemDataReceived  += OnIsemData;
        }

        // ── GIMAC connection state ─────────────────────────────────────────────

        private void SyncGimacStates()
        {
            foreach (var d in ServiceHub.Devices.LoadDevices().Where(d => d.Type == DeviceType.GIMAC))
                SetGimacDot(d.UnitId, ServiceHub.Metering.IsConnected(d.Ip, d.Port, d.UnitId));
        }

        private void SetGimacDot(int uid, bool v)
        {
            switch (uid)
            {
                case 1: Pnl1GimacConnected = v; break;
                case 2: Pnl2GimacConnected = v; break;
                case 3: Pnl3GimacConnected = v; break;
            }
        }

        private void OnConnectionChanged(object s, DeviceRecord rec)
        {
            if (rec.Type != DeviceType.GIMAC) return;
            SetGimacDot(rec.UnitId, ServiceHub.Metering.IsConnected(rec.Ip, rec.Port, rec.UnitId));
        }

        // ── GIMAC data received (UI thread, ~500 ms + device response time) ────
        //
        // Aggregation is TIME-BASED, not sample-count-based, so polling overhead
        // (FC4 round-trip × 2) does not cause time drift in the reported deltas.
        //
        // Each period emits exactly 1 delta point per aggregation window:
        //   1m  → 1 pt / min   (Δ = avg(last 60 s) − avg(prev 60 s))
        //   1h  → 1 pt / hour  (Δ = avg(last 60 min) − avg(prev 60 min))
        //   1day→ 1 pt / day   (Δ = avg(last 24 h) − avg(prev 24 h))

        private void OnGimacData(object s, GimacReading r)
        {
            int idx = r.Device.UnitId - 1;
            if ((uint)idx >= 3) return;

            _lastGimac[idx] = r;
            double kw = r.ActivePower / 1000.0;
            var    ts = r.Timestamp;

            // ── Raw buffer ────────────────────────────────────────────────────
            var raw = _rawBuf[idx];
            raw.Enqueue((ts, kw));
            while (raw.Count > MaxRaw) raw.Dequeue();

            // ── 1-minute aggregate (time-based) ───────────────────────────────
            if ((ts - _lastMinAgg[idx]).TotalSeconds >= MinuteSec)
            {
                _lastMinAgg[idx] = ts;

                // Average only the samples from the last 60 s
                var cut60s = ts.AddSeconds(-MinuteSec);
                var win = raw.Where(e => e.ts >= cut60s).ToArray();
                double minAvg = win.Length > 0 ? win.Average(e => e.kw) : kw;

                var minQ = _minBuf[idx];
                minQ.Enqueue((ts, minAvg));
                while (minQ.Count > MaxMin) minQ.Dequeue();

                // 1m Δ: consecutive 1-min averages → 1 pt per minute
                if (minQ.Count >= 2)
                {
                    var m = minQ.ToArray();
                    _delta[0][idx].Append(ts, m[m.Length - 1].kw - m[m.Length - 2].kw);
                    UpdateXAxisRange(ts, 0);
                }

                // ── 1-hour aggregate (time-based) ─────────────────────────────
                if ((ts - _lastHourAgg[idx]).TotalMinutes >= HourMin)
                {
                    _lastHourAgg[idx] = ts;

                    // Average only the 1-min entries from the last 60 min
                    var cut1h = ts.AddMinutes(-HourMin);
                    var mArr  = minQ.ToArray();
                    var wm    = mArr.Where(e => e.ts >= cut1h).ToArray();
                    double hrAvg = wm.Length > 0 ? wm.Average(e => e.kw) : minAvg;

                    var hourQ = _hourBuf[idx];
                    hourQ.Enqueue((ts, hrAvg));
                    while (hourQ.Count > MaxHour) hourQ.Dequeue();

                    // 1h Δ: consecutive 1-hr averages → 1 pt per hour
                    if (hourQ.Count >= 2)
                    {
                        var h = hourQ.ToArray();
                        _delta[1][idx].Append(ts, h[h.Length - 1].kw - h[h.Length - 2].kw);
                        UpdateXAxisRange(ts, 1);
                    }

                    // ── 1-day aggregate (time-based) ──────────────────────────
                    if ((ts - _lastDayAgg[idx]).TotalHours >= DayHr)
                    {
                        _lastDayAgg[idx] = ts;

                        // Average only the 1-hr entries from the last 24 h
                        var cut1d = ts.AddHours(-DayHr);
                        var hArr  = hourQ.ToArray();
                        var wd    = hArr.Where(e => e.ts >= cut1d).ToArray();
                        double dayAvg = wd.Length > 0 ? wd.Average(e => e.kw) : hrAvg;

                        var dayQ = _dayBuf[idx];
                        dayQ.Enqueue((ts, dayAvg));
                        while (dayQ.Count > MaxDay) dayQ.Dequeue();

                        // 1day Δ: consecutive 1-day averages → 1 pt per day
                        if (dayQ.Count >= 2)
                        {
                            var d = dayQ.ToArray();
                            _delta[2][idx].Append(ts, d[d.Length - 1].kw - d[d.Length - 2].kw);
                            UpdateXAxisRange(ts, 2);
                        }
                    }
                }
            }

            // Update KPI + DataGrid only for the selected panel
            if (idx != (SelectedPanel?.Index ?? 0)) return;
            ApplyKpi(r);
            RefreshDataRows();
        }

        // ── ISEM data received (UI thread, 500 ms) ────────────────────────────

        private void OnIsemData(object s, IsemReading r)
        {
            _lastIsem[r.Device.UnitId] = r;
        }

        // ── KPI helpers ───────────────────────────────────────────────────────

        private void ApplyKpi(GimacReading r)
        {
            KpiVoltage       = Math.Round(r.AvgVoltage,              1);
            KpiCurrent       = Math.Round(r.AvgCurrent,              1);
            KpiActivePower   = Math.Round(r.ActivePower   / 1000.0,  2);
            KpiReactivePower = Math.Round(r.ReactivePower / 1000.0,  2);
            KpiPowerFactor   = Math.Round(r.PowerFactor,             3);
            KpiFrequency     = Math.Round(r.Frequency,               2);
            KpiVoltThd       = Math.Round((r.VoltThdA + r.VoltThdB + r.VoltThdC) / 3.0, 1);
        }

        private void ClearKpi() =>
            KpiVoltage = KpiCurrent = KpiActivePower = KpiReactivePower =
            KpiPowerFactor = KpiFrequency = KpiVoltThd = 0;

        // ── Panel ComboBox change ─────────────────────────────────────────────

        private void OnPanelChanged()
        {
            int idx = SelectedPanel?.Index ?? 0;
            if (_lastGimac[idx] != null) ApplyKpi(_lastGimac[idx]);
            else ClearKpi();
            RefreshDataRows();
        }

        // ── Detail DataGrid ───────────────────────────────────────────────────

        private void RefreshDataRows()
        {
            int idx = SelectedPanel?.Index ?? 0;
            DataRows.Clear();

            var g = _lastGimac[idx];
            if (g != null) AddGimacRows(g);

            foreach (var kv in _lastIsem
                .Where(kv => IsemBelongsToPanel(kv.Key, idx))
                .OrderBy(kv => kv.Key))
                AddIsemRows(kv.Key, kv.Value);
        }

        private void AddGimacRows(GimacReading r)
        {
            string cat = $"GIMAC {r.Device.UnitId}";
            void Row(string n, string v, string u) =>
                DataRows.Add(new MeterDataRow { Category = cat, Name = n, Value = v, Unit = u });

            Row("평균 전압",        r.AvgVoltage.ToString("F1"),                  "V");
            Row("평균 전류",        r.AvgCurrent.ToString("F1"),                  "A");
            Row("유효전력",         (r.ActivePower   / 1000f).ToString("F2"),     "kW");
            Row("무효전력",         (r.ReactivePower / 1000f).ToString("F2"),     "kVAr");
            Row("피상전력",         (r.ApparentPower / 1000f).ToString("F2"),     "kVA");
            Row("역률",             r.PowerFactor.ToString("F3"),                  "");
            Row("주파수",           r.Frequency.ToString("F2"),                    "Hz");
            Row("전압 R상 (Va-n)",  r.VoltA.ToString("F1"),                       "V");
            Row("전압 S상 (Vb-n)",  r.VoltB.ToString("F1"),                       "V");
            Row("전압 T상 (Vc-n)",  r.VoltC.ToString("F1"),                       "V");
            Row("선간전압 RS",      r.VoltAB.ToString("F1"),                      "V");
            Row("선간전압 ST",      r.VoltBC.ToString("F1"),                      "V");
            Row("선간전압 TR",      r.VoltCA.ToString("F1"),                      "V");
            Row("전류 R상",         r.CurrA.ToString("F1"),                        "A");
            Row("전류 S상",         r.CurrB.ToString("F1"),                        "A");
            Row("전류 T상",         r.CurrC.ToString("F1"),                        "A");
            Row("전압 THD R상",     r.VoltThdA.ToString("F1"),                    "%");
            Row("전압 THD S상",     r.VoltThdB.ToString("F1"),                    "%");
            Row("전압 THD T상",     r.VoltThdC.ToString("F1"),                    "%");
            Row("전류 THD R상",     r.CurrThdA.ToString("F1"),                    "%");
            Row("전류 THD S상",     r.CurrThdB.ToString("F1"),                    "%");
            Row("전류 THD T상",     r.CurrThdC.ToString("F1"),                    "%");
        }

        private void AddIsemRows(int uid, IsemReading r)
        {
            string cat = $"ISEM #{uid}";
            void Row(string n, string v, string u) =>
                DataRows.Add(new MeterDataRow { Category = cat, Name = n, Value = v, Unit = u });

            Row("선간전압 L3-L1",   r.VoltL3L1.ToString("F1"),         "V");
            Row("선간전압 L1-L2",   r.VoltL1L2.ToString("F1"),         "V");
            Row("선간전압 L2-L3",   r.VoltL2L3.ToString("F1"),         "V");
            Row("평균 전압",        r.AvgVoltage.ToString("F1"),        "V");
            Row("전류 L1",          r.CurrL1.ToString("F2"),            "A");
            Row("전류 L2",          r.CurrL2.ToString("F2"),            "A");
            Row("전류 L3",          r.CurrL3.ToString("F2"),            "A");
            Row("접지전류",         r.GroundCurrent.ToString("F1"),     "mA");
            Row("유효전력",         r.ActivePower.ToString("F1"),       "kW");
            Row("무효전력",         r.ReactivePower.ToString("F1"),     "kVAr");
            Row("역률",             r.PowerFactor.ToString("F3"),        "");
            Row("전류 주파수",      r.CurrentFrequency.ToString("F2"),  "Hz");
            Row("전압 주파수",      r.VoltageFrequency.ToString("F2"),  "Hz");
            Row("평균 전류 THD",    r.AvgCurrentThd.ToString("F1"),     "%");
            Row("전류 THD L1",      r.CurrThdL1.ToString("F1"),         "%");
            Row("전류 THD L2",      r.CurrThdL2.ToString("F1"),         "%");
            Row("전류 THD L3",      r.CurrThdL3.ToString("F1"),         "%");
            Row("평균 전압 THD",    r.AvgVoltageThd.ToString("F1"),     "%");
        }

        // ── Period selector ───────────────────────────────────────────────────

        private void OnPeriodChanged()
        {
            int p = SelectedPeriod switch { "1h" => 1, "1day" => 2, _ => 0 };
            Pnl1DeltaSeries = _delta[p][0];
            Pnl2DeltaSeries = _delta[p][1];
            Pnl3DeltaSeries = _delta[p][2];
            DeltaChartTitle = SelectedPeriod switch
            {
                "1h"   => "유효전력 변화량  (ΔkW · 1분 평균 기준 / 1시간 조회)",
                "1day" => "유효전력 변화량  (ΔkW · 1시간 평균 기준 / 1일 조회)",
                _      => "유효전력 변화량  (ΔkW · 1분 평균 기준 / 60분 조회)",
            };
            DeltaXAxisFormat = SelectedPeriod switch
            {
                "1h"   => "HH:mm",
                "1day" => "MM/dd HH:mm",
                _      => "HH:mm",
            };
            // 슬라이딩 창 크기: 기간별 10포인트 분량
            _xWindowSize = SelectedPeriod switch
            {
                "1h"   => TimeSpan.FromHours(10),
                "1day" => TimeSpan.FromDays(10),
                _      => TimeSpan.FromMinutes(10),
            };
            // 기간 전환 시 X축 즉시 재조정
            UpdateXAxisRange(DateTime.Now, SelectedPeriod switch { "1h" => 1, "1day" => 2, _ => 0 }, force: true);
        }

        // ── X-axis sliding window ─────────────────────────────────────────────

        // 선택된 기간의 delta가 append될 때마다 호출.
        // force=true이면 기간 전환 즉시 강제 갱신.
        private void UpdateXAxisRange(DateTime latest, int periodIdx, bool force = false)
        {
            int selectedP = SelectedPeriod switch { "1h" => 1, "1day" => 2, _ => 0 };
            if (!force && periodIdx != selectedP) return;
            DeltaXRange = new DateRange(latest - _xWindowSize, latest.AddSeconds(5));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool IsemBelongsToPanel(int uid, int panelIdx) =>
            panelIdx == 0 ? uid <= 3 :
            panelIdx == 1 ? uid is >= 4 and <= 6 :
                            uid >= 7;

        private static Queue<(DateTime, double)>[] MakeQueues()
        {
            var q = new Queue<(DateTime, double)>[3];
            for (int i = 0; i < 3; i++) q[i] = new();
            return q;
        }
    }
}
