using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    // ── 그리드 행 모델 (표시 전용) ────────────────────────────────────────────

    public class OperationRow
    {
        public string TimeText   { get; set; }
        public string PanelText  { get; set; }
        public string ModeText   { get; set; }
        public string OpText     { get; set; }
        public string LoadText   { get; set; }
        public string TargetText { get; set; }
        public string CapText    { get; set; }
        public string Result     { get; set; }
    }

    public class AlarmRow
    {
        public string TimeText    { get; set; }   // 발생
        public string ClearedText { get; set; }   // 해제 (활성이면 "—")
        public string PanelText   { get; set; }
        public string TypeText    { get; set; }   // 알람 종류 (한글)
        public string Detail      { get; set; }
        public string Status      { get; set; }   // 활성 | 해제
    }

    /// <summary>GIMAC 1분 집계 행 — 피상전력 포함, 전류는 3상 평균.</summary>
    public class GimacRow
    {
        public string TimeText   { get; set; }
        public string DeviceText { get; set; }   // GIMAC-1
        public string PanelText  { get; set; }
        public string VoltText   { get; set; }
        public string CurrText   { get; set; }
        public string KwText     { get; set; }
        public string KwRange    { get; set; }   // min ~ max
        public string KvarText   { get; set; }
        public string KvaText    { get; set; }   // 피상전력 (GIMAC 고유)
        public string PfText     { get; set; }
        public string HzText     { get; set; }
    }

    /// <summary>ISEM 1분 집계 행 — 상별 전류·접지전류(보호) 포함.</summary>
    public class IsemRow
    {
        public string TimeText   { get; set; }
        public string DeviceText { get; set; }   // ISEM-3
        public string PanelText  { get; set; }
        public string VoltText   { get; set; }
        public string CurrL1     { get; set; }
        public string CurrL2     { get; set; }
        public string CurrL3     { get; set; }
        public string GroundText { get; set; }   // 접지전류 avg~max (ISEM 고유)
        public string KwText     { get; set; }
        public string KwRange    { get; set; }
        public string KvarText   { get; set; }
        public string PfText     { get; set; }
        public string HzText     { get; set; }
    }

    public class ConnRow
    {
        public string TimeText   { get; set; }
        public string DeviceText { get; set; }
        public string PanelText  { get; set; }
        public string Status     { get; set; }   // 연결 | 해제
        public string Detail     { get; set; }
    }

    public class PanelFilterItem
    {
        public string Label   { get; set; }
        public int?   PanelNo { get; set; }      // null = 전체
    }

    /// <summary>이력 종류 콤보 항목. Key: OP | ALARM | METER | CONN</summary>
    public class HistoryCategoryItem
    {
        public string Label { get; set; }
        public string Key   { get; set; }
    }

    /// <summary>이력 화면: 운전 이력 / 알람 / 데이터(ISEM·GIMAC) / 연결 이력을
    /// 콤보로 전환 조회. 공통 필터 = 판넬 + 기간(+선택 시간) + 퀵버튼.
    /// RLC_DB_CONN 미설정 시 운전 이력만 세션 in-memory로 폴백.</summary>
    public class OperationHistoryViewModel : ViewModelBase
    {
        public ObservableCollection<OperationRow> OpRows    { get; } = new ObservableCollection<OperationRow>();
        public ObservableCollection<AlarmRow>     AlarmRows { get; } = new ObservableCollection<AlarmRow>();
        public ObservableCollection<GimacRow>     GimacRows { get; } = new ObservableCollection<GimacRow>();
        public ObservableCollection<IsemRow>      IsemRows  { get; } = new ObservableCollection<IsemRow>();
        public ObservableCollection<ConnRow>      ConnRows  { get; } = new ObservableCollection<ConnRow>();

        public string SourceText { get => GetValue<string>(); set => SetValue(value); }
        public bool   IsLoading  { get => GetValue<bool>();   set => SetValue(value); }

        // ── 이력 종류 ─────────────────────────────────────────────────────────
        public ObservableCollection<HistoryCategoryItem> Categories { get; }
        public HistoryCategoryItem SelectedCategory
        { get => GetValue<HistoryCategoryItem>(); set => SetValue(value, OnCategoryChanged); }

        public bool IsOpView    => SelectedCategory?.Key == "OP";
        public bool IsAlarmView => SelectedCategory?.Key == "ALARM";
        public bool IsGimacView => SelectedCategory?.Key == "GIMAC";
        public bool IsIsemView  => SelectedCategory?.Key == "ISEM";
        public bool IsConnView  => SelectedCategory?.Key == "CONN";

        public int Count => SelectedCategory?.Key switch
        {
            "ALARM" => AlarmRows.Count,
            "GIMAC" => GimacRows.Count,
            "ISEM"  => IsemRows.Count,
            "CONN"  => ConnRows.Count,
            _       => OpRows.Count,
        };

        // ── 검색 필터 ─────────────────────────────────────────────────────────
        public ObservableCollection<PanelFilterItem> PanelFilters { get; }
        public PanelFilterItem SelectedPanelFilter
        { get => GetValue<PanelFilterItem>(); set => SetValue(value); }

        public DateTime? FromDate { get => GetValue<DateTime?>(); set => SetValue(value); }
        public DateTime? ToDate   { get => GetValue<DateTime?>(); set => SetValue(value); }
        public string    FromTime { get => GetValue<string>();    set => SetValue(value); }  // "HH:mm" (선택)
        public string    ToTime   { get => GetValue<string>();    set => SetValue(value); }

        public DelegateCommand         RefreshCommand    { get; }
        public DelegateCommand         SearchCommand     { get; }
        public DelegateCommand<string> QuickRangeCommand { get; }   // "1" | "7" | "30" (일)

        public OperationHistoryViewModel()
        {
            Categories = new ObservableCollection<HistoryCategoryItem>
            {
                new HistoryCategoryItem { Label = "운전 이력",     Key = "OP" },
                new HistoryCategoryItem { Label = "알람",          Key = "ALARM" },
                new HistoryCategoryItem { Label = "데이터 (GIMAC)", Key = "GIMAC" },
                new HistoryCategoryItem { Label = "데이터 (ISEM)",  Key = "ISEM" },
                new HistoryCategoryItem { Label = "연결 이력",     Key = "CONN" },
            };
            PanelFilters = new ObservableCollection<PanelFilterItem>
            {
                new PanelFilterItem { Label = "전체",  PanelNo = null },
                new PanelFilterItem { Label = "PNL-1", PanelNo = 1 },
                new PanelFilterItem { Label = "PNL-2", PanelNo = 2 },
                new PanelFilterItem { Label = "PNL-3", PanelNo = 3 },
            };
            SelectedPanelFilter = PanelFilters[0];

            RefreshCommand    = new DelegateCommand(() => _ = LoadAsync(), () => !IsLoading);
            SearchCommand     = new DelegateCommand(() => _ = LoadAsync(), () => !IsLoading);
            QuickRangeCommand = new DelegateCommand<string>(QuickRange);

            SelectedCategory = Categories[0];   // → OnCategoryChanged → 첫 조회
        }

        // 카테고리 전환: 그리드 가시성 갱신 + 즉시 재조회
        private void OnCategoryChanged()
        {
            RaisePropertyChanged(nameof(IsOpView));
            RaisePropertyChanged(nameof(IsAlarmView));
            RaisePropertyChanged(nameof(IsGimacView));
            RaisePropertyChanged(nameof(IsIsemView));
            RaisePropertyChanged(nameof(IsConnView));
            _ = LoadAsync();
        }

        /// <summary>퀵 버튼: Today/Week/Month = 1/7/30일. 기간 자동 설정 후 즉시 검색.</summary>
        private void QuickRange(string days)
        {
            int n = int.TryParse(days, out int d) && d > 0 ? d : 1;
            ToDate   = DateTime.Today;
            FromDate = DateTime.Today.AddDays(-(n - 1));
            FromTime = null;
            ToTime   = null;
            _ = LoadAsync();
        }

        // 날짜 + (선택) 시간 → 검색 구간. 시간 미입력 시 시작 00:00 / 종료 23:59:59.
        private (DateTime? from, DateTime? to) BuildRange()
        {
            DateTime? from = null, to = null;
            if (FromDate.HasValue)
                from = FromDate.Value.Date + (ParseTime(FromTime) ?? TimeSpan.Zero);
            if (ToDate.HasValue)
                to = ToDate.Value.Date + (ParseTime(ToTime) ?? new TimeSpan(23, 59, 59));
            return (from, to);
        }

        private static TimeSpan? ParseTime(string text) =>
            TimeSpan.TryParse(text, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)
                ? t : (TimeSpan?)null;

        private async Task LoadAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            RefreshCommand?.RaiseCanExecuteChanged();
            SearchCommand?.RaiseCanExecuteChanged();
            try
            {
                var (from, to) = BuildRange();
                int? panel = SelectedPanelFilter?.PanelNo;
                string key = SelectedCategory?.Key ?? "OP";
                bool dbOn = ServiceHub.DbWriter.Enabled;

                switch (key)
                {
                    case "ALARM":
                    {
                        var recs = dbOn
                            ? await Task.Run(() => ServiceHub.DbLog.QueryAlarms(500, from, to, panel))
                            : (IReadOnlyList<AlarmEventRecord>)Array.Empty<AlarmEventRecord>();
                        AlarmRows.Clear();
                        foreach (var e in recs) AlarmRows.Add(MapAlarm(e));
                        SourceText = dbOn ? "PostgreSQL · tb_alarm_event" : "DB 미설정 (RLC_DB_CONN)";
                        break;
                    }
                    case "GIMAC":
                    {
                        var recs = dbOn
                            ? await Task.Run(() => ServiceHub.DbLog.QueryGimacAggs(500, from, to, panel))
                            : (IReadOnlyList<GimacAggRecord>)Array.Empty<GimacAggRecord>();
                        GimacRows.Clear();
                        foreach (var e in recs) GimacRows.Add(MapGimac(e));
                        SourceText = dbOn ? "PostgreSQL · tb_gimac_agg_1m (1분 집계)" : "DB 미설정 (RLC_DB_CONN)";
                        break;
                    }
                    case "ISEM":
                    {
                        var recs = dbOn
                            ? await Task.Run(() => ServiceHub.DbLog.QueryIsemAggs(500, from, to, panel))
                            : (IReadOnlyList<IsemAggRecord>)Array.Empty<IsemAggRecord>();
                        IsemRows.Clear();
                        foreach (var e in recs) IsemRows.Add(MapIsem(e));
                        SourceText = dbOn ? "PostgreSQL · tb_isem_agg_1m (1분 집계)" : "DB 미설정 (RLC_DB_CONN)";
                        break;
                    }
                    case "CONN":
                    {
                        var recs = dbOn
                            ? await Task.Run(() => ServiceHub.DbLog.QueryConnections(500, from, to, panel))
                            : (IReadOnlyList<ConnectionEventRecord>)Array.Empty<ConnectionEventRecord>();
                        ConnRows.Clear();
                        foreach (var e in recs) ConnRows.Add(MapConn(e));
                        SourceText = dbOn ? "PostgreSQL · tb_connection_event" : "DB 미설정 (RLC_DB_CONN)";
                        break;
                    }
                    default:   // OP
                    {
                        if (dbOn)
                        {
                            var recs = await Task.Run(() => ServiceHub.DbLog.QueryOperations(500, from, to, panel));
                            OpRows.Clear();
                            foreach (var e in recs) OpRows.Add(MapOp(e));
                            SourceText = "PostgreSQL · tb_operation_event";
                        }
                        else
                        {
                            IEnumerable<HistoryEntry> src = ServiceHub.History.Query(500);
                            if (from.HasValue)  src = src.Where(e => e.Time >= from.Value);
                            if (to.HasValue)    src = src.Where(e => e.Time <= to.Value);
                            if (panel.HasValue) src = src.Where(e => e.Panel != null && e.Panel.Contains($"PNL-{panel}"));
                            OpRows.Clear();
                            foreach (var e in src) OpRows.Add(MapLegacy(e));
                            SourceText = "In-Memory (세션 한정 — RLC_DB_CONN 미설정)";
                        }
                        break;
                    }
                }
                RaisePropertyChanged(nameof(Count));
            }
            finally
            {
                IsLoading = false;
                RefreshCommand?.RaiseCanExecuteChanged();
                SearchCommand?.RaiseCanExecuteChanged();
            }
        }

        // ── 표시 매핑 ─────────────────────────────────────────────────────────

        private static string PanelText(int? p) => p.HasValue ? $"PNL-{p}" : "전체";

        private static OperationRow MapOp(OperationEventRecord e) => new OperationRow
        {
            TimeText   = e.Ts.ToString("yyyy-MM-dd HH:mm:ss"),
            PanelText  = PanelText(e.PanelNo),
            ModeText   = e.Mode switch
            {
                "MANUAL" => "수동", "AUTO" => "자동", "SYSTEM" => "시스템", _ => e.Mode,
            },
            OpText     = e.OpType switch
            {
                "MC_ON"         => "MC 투입",
                "MC_OFF"        => "MC 개방",
                "SEQ_ON"        => "부하 ON 시퀀스",
                "SEQ_OFF"       => "부하 OFF 시퀀스",
                "ALL_OFF"       => "전체 개방",
                "AUTO_COMPLETE" => "자동운전 완료",
                "MCCB_ON"       => "MCCB ON",
                "MCCB_OFF"      => "MCCB OFF",
                "MCCB_TRIP"     => "MCCB TRIP",
                "MODE_CHANGE"   => "Local/Remote 전환",
                _               => e.OpType,
            },
            LoadText   = e.LoadType,
            TargetText = e.Target == null ? null
                       : e.Phase == null ? e.Target : $"{e.Target} ({e.Phase})",
            CapText    = e.RkW.HasValue || e.LkVar.HasValue || e.CkVar.HasValue
                       ? $"R {e.RkW ?? 0:0.#} / L {e.LkVar ?? 0:0.#} / C {e.CkVar ?? 0:0.#}"
                       : null,
            Result     = e.Result,
        };

        private static OperationRow MapLegacy(HistoryEntry e) => new OperationRow
        {
            TimeText  = e.TimeText,
            PanelText = e.Panel,
            OpText    = e.Event,
            Result    = e.Result,
        };

        private static AlarmRow MapAlarm(AlarmEventRecord e) => new AlarmRow
        {
            TimeText    = e.RaisedTs.ToString("yyyy-MM-dd HH:mm:ss"),
            ClearedText = e.ClearedTs?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—",
            PanelText   = PanelText(e.PanelNo),
            TypeText    = e.AlarmType switch
            {
                "EMG"             => "비상정지 (EMG)",
                "MCCB_TRIP"       => "MCCB 트립",
                "MCCB_TRIP_CMD"   => "MCCB TRIP 명령",
                "OVR"             => "과전압 (OVR)",
                "OCR"             => "과전류 (OCR)",
                "HT"              => "과열 (HT)",
                "DOOR"            => "도어 열림",
                "C_MC1"           => "C부하 MC1 알람",
                "C_MC2"           => "C부하 MC2 알람",
                "C_SCR"           => "C부하 SCR 알람",
                "CMD_FB_MISMATCH" => "지령/피드백 불일치",
                "COMM_LOST"       => "통신 단절",
                _                 => e.AlarmType,
            },
            Detail = e.Detail,
            Status = e.ClearedTs.HasValue ? "해제" : "활성",
        };

        private static GimacRow MapGimac(GimacAggRecord e) => new GimacRow
        {
            TimeText   = e.Ts.ToString("yyyy-MM-dd HH:mm"),
            DeviceText = $"GIMAC-{e.UnitId}",
            PanelText  = PanelText(e.PanelNo),
            VoltText   = e.VoltAvg.ToString("F1"),
            CurrText   = e.CurrAvg.ToString("F1"),
            KwText     = e.KwAvg.ToString("F2"),
            KwRange    = $"{e.KwMin:F1} ~ {e.KwMax:F1}",
            KvarText   = e.KvarAvg.ToString("F2"),
            KvaText    = e.KvaAvg.ToString("F2"),
            PfText     = e.PfAvg.ToString("F3"),
            HzText     = e.HzAvg.ToString("F2"),
        };

        private static IsemRow MapIsem(IsemAggRecord e) => new IsemRow
        {
            TimeText   = e.Ts.ToString("yyyy-MM-dd HH:mm"),
            DeviceText = $"ISEM-{e.UnitId}",
            PanelText  = PanelText(e.PanelNo),
            VoltText   = e.VoltAvg.ToString("F1"),
            CurrL1     = e.CurrL1.ToString("F1"),
            CurrL2     = e.CurrL2.ToString("F1"),
            CurrL3     = e.CurrL3.ToString("F1"),
            GroundText = $"{e.GroundAvg:F1} ~ {e.GroundMax:F1}",
            KwText     = e.KwAvg.ToString("F2"),
            KwRange    = $"{e.KwMin:F1} ~ {e.KwMax:F1}",
            KvarText   = e.KvarAvg.ToString("F2"),
            PfText     = e.PfAvg.ToString("F3"),
            HzText     = e.HzAvg.ToString("F2"),
        };

        private static ConnRow MapConn(ConnectionEventRecord e) => new ConnRow
        {
            TimeText   = e.Ts.ToString("yyyy-MM-dd HH:mm:ss"),
            DeviceText = $"{e.DeviceType}-{e.UnitId}",
            PanelText  = PanelText(e.PanelNo),
            Status     = e.Connected ? "연결" : "해제",
            Detail     = e.Detail,
        };
    }
}
