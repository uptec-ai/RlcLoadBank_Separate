using System;
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

    /// <summary>
    /// Display-only VM: all buffering/aggregation lives in
    /// ServiceHub.MeteringHistory (app-lifetime), so trend data collected
    /// before this view is first opened is already in the bound series.
    /// </summary>
    public class MeteringViewModel : ViewModelBase
    {
        private readonly MeteringHistoryService _history = ServiceHub.MeteringHistory;

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
        public DelegateCommand<string> SelectPeriodCommand  { get; }
        /// <summary>더블클릭 시 현재 슬라이딩 창(_xWindowSize, 10포인트)으로 X축 리셋 — 전체 FIFO 표시 방지.</summary>
        public DelegateCommand        ResetDeltaZoomCommand { get; }

        // Exposed series — owned by MeteringHistoryService, swapped on period change
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

            SelectPeriodCommand   = new DelegateCommand<string>(p => SelectedPeriod = p);
            ResetDeltaZoomCommand = new DelegateCommand(() =>
            {
                int pi = SelectedPeriod switch { "1h" => 1, "1day" => 2, _ => 0 };
                UpdateXAxisRange(_history.GetLastDeltaTime(pi) ?? DateTime.Now, pi, force: true);
            });

            // OnPanelChanged / OnPeriodChanged fill KPI·grid·series from the
            // history service, so data collected before this VM existed shows
            // immediately.
            SelectedPanel  = Panels[0];
            SelectedPeriod = "1m";   // → OnPeriodChanged() sets _xWindowSize + X range

            SyncGimacStates();
            ServiceHub.Metering.ConnectionChanged += OnConnectionChanged;
            ServiceHub.Metering.GimacDataReceived += OnGimacData;
            _history.DeltaAppended                += OnDeltaAppended;
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
        // Aggregation/buffering happens in MeteringHistoryService; here we only
        // refresh the KPI cards + detail grid for the selected panel.

        private void OnGimacData(object s, GimacReading r)
        {
            int idx = r.Device.UnitId - 1;
            if (idx != (SelectedPanel?.Index ?? 0)) return;
            ApplyKpi(r);
            RefreshDataRows();
        }

        // ── Delta appended by the history service (UI thread) ────────────────

        private void OnDeltaAppended(int periodIdx, DateTime ts) =>
            UpdateXAxisRange(ts, periodIdx);

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
            var g = _history.GetLastGimac(idx);
            if (g != null) ApplyKpi(g);
            else ClearKpi();
            RefreshDataRows();
        }

        // ── Detail DataGrid ───────────────────────────────────────────────────

        private void RefreshDataRows()
        {
            int idx = SelectedPanel?.Index ?? 0;
            DataRows.Clear();

            var g = _history.GetLastGimac(idx);
            if (g != null) AddGimacRows(g);

            foreach (var kv in _history.LastIsem
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
            Pnl1DeltaSeries = _history.GetDeltaSeries(p, 0);
            Pnl2DeltaSeries = _history.GetDeltaSeries(p, 1);
            Pnl3DeltaSeries = _history.GetDeltaSeries(p, 2);
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
            // 기간 전환 시 X축 즉시 재조정 — 이미 수집된 데이터가 있으면 그 마지막
            // 포인트 기준으로 창을 잡는다 (뷰를 늦게 열어도 과거 트렌드가 보이도록).
            UpdateXAxisRange(_history.GetLastDeltaTime(p) ?? DateTime.Now, p, force: true);
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
    }
}
