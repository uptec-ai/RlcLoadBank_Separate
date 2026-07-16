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
    /// <summary>운전 이력 그리드 1행 (표시 전용).</summary>
    public class OperationRow
    {
        public string TimeText   { get; set; }
        public string PanelText  { get; set; }   // PNL-1 / 전체
        public string ModeText   { get; set; }   // 수동 / 자동 / 시스템
        public string OpText     { get; set; }   // MC 투입 / 자동운전 완료 ...
        public string LoadText   { get; set; }   // R / L / C
        public string TargetText { get; set; }   // P1_R_RN_01 (RN) ...
        public string CapText    { get; set; }   // R 10.5 / L 0 / C 0
        public string Result     { get; set; }   // 성공 / 실패 / 중단
    }

    /// <summary>판넬 필터 콤보 항목.</summary>
    public class PanelFilterItem
    {
        public string Label   { get; set; }
        public int?   PanelNo { get; set; }      // null = 전체
    }

    /// <summary>운전 이력 (full): tb_operation_event 조회 + 판넬/기간 검색.
    /// RLC_DB_CONN 미설정 시 세션 한정 in-memory 이력으로 폴백(필터는 클라이언트측 적용).</summary>
    public class OperationHistoryViewModel : ViewModelBase
    {
        public ObservableCollection<OperationRow> Rows { get; } = new ObservableCollection<OperationRow>();
        public int    Count      => Rows.Count;
        public string SourceText { get => GetValue<string>(); set => SetValue(value); }
        public bool   IsLoading  { get => GetValue<bool>();   set => SetValue(value); }

        // ── 검색 필터 (판넬 선택 → 날짜 → (선택)시간 → 검색) ──────────────────
        public ObservableCollection<PanelFilterItem> PanelFilters { get; }
        public PanelFilterItem SelectedPanelFilter
        { get => GetValue<PanelFilterItem>(); set => SetValue(value); }

        public DateTime? FromDate { get => GetValue<DateTime?>(); set => SetValue(value); }
        public DateTime? ToDate   { get => GetValue<DateTime?>(); set => SetValue(value); }
        public string    FromTime { get => GetValue<string>();    set => SetValue(value); }  // "HH:mm" (선택)
        public string    ToTime   { get => GetValue<string>();    set => SetValue(value); }  // "HH:mm" (선택)

        public DelegateCommand         RefreshCommand    { get; }
        public DelegateCommand         SearchCommand     { get; }
        public DelegateCommand<string> QuickRangeCommand { get; }   // "1" | "7" | "30" (일)

        public OperationHistoryViewModel()
        {
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
            RefreshCommand.RaiseCanExecuteChanged();
            SearchCommand.RaiseCanExecuteChanged();
            try
            {
                var (from, to) = BuildRange();
                int? panel = SelectedPanelFilter?.PanelNo;

                if (ServiceHub.DbWriter.Enabled)
                {
                    // DB 조회는 백그라운드에서, 컬렉션 갱신은 UI 스레드에서
                    var recs = await Task.Run(() => ServiceHub.DbLog.QueryOperations(500, from, to, panel));
                    Rows.Clear();
                    foreach (var e in recs) Rows.Add(Map(e));
                    SourceText = "PostgreSQL · tb_operation_event";
                }
                else
                {
                    IEnumerable<HistoryEntry> src = ServiceHub.History.Query(500);
                    if (from.HasValue)  src = src.Where(e => e.Time >= from.Value);
                    if (to.HasValue)    src = src.Where(e => e.Time <= to.Value);
                    if (panel.HasValue) src = src.Where(e => e.Panel != null && e.Panel.Contains($"PNL-{panel}"));
                    Rows.Clear();
                    foreach (var e in src) Rows.Add(MapLegacy(e));
                    SourceText = "In-Memory (세션 한정 — RLC_DB_CONN 미설정)";
                }
                RaisePropertyChanged(nameof(Count));
            }
            finally
            {
                IsLoading = false;
                RefreshCommand.RaiseCanExecuteChanged();
                SearchCommand.RaiseCanExecuteChanged();
            }
        }

        // ── 표시 매핑 ─────────────────────────────────────────────────────────

        private static OperationRow Map(OperationEventRecord e) => new OperationRow
        {
            TimeText   = e.Ts.ToString("yyyy-MM-dd HH:mm:ss"),
            PanelText  = e.PanelNo.HasValue ? $"PNL-{e.PanelNo}" : "전체",
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
    }
}
