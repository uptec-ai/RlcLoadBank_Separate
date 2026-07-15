using System;
using System.Collections.ObjectModel;
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

    /// <summary>운전 이력 (full): tb_operation_event 조회.
    /// RLC_DB_CONN 미설정 시 세션 한정 in-memory 이력으로 폴백.</summary>
    public class OperationHistoryViewModel : ViewModelBase
    {
        public ObservableCollection<OperationRow> Rows { get; } = new ObservableCollection<OperationRow>();
        public int    Count      => Rows.Count;
        public string SourceText { get => GetValue<string>(); set => SetValue(value); }
        public bool   IsLoading  { get => GetValue<bool>();   set => SetValue(value); }

        public DelegateCommand RefreshCommand { get; }

        public OperationHistoryViewModel()
        {
            RefreshCommand = new DelegateCommand(() => _ = LoadAsync(), () => !IsLoading);
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                if (ServiceHub.DbWriter.Enabled)
                {
                    // DB 조회는 백그라운드에서, 컬렉션 갱신은 UI 스레드에서
                    var recs = await Task.Run(() => ServiceHub.DbLog.QueryOperations(500));
                    Rows.Clear();
                    foreach (var e in recs) Rows.Add(Map(e));
                    SourceText = "PostgreSQL · tb_operation_event";
                }
                else
                {
                    Rows.Clear();
                    foreach (var e in ServiceHub.History.Query(500)) Rows.Add(MapLegacy(e));
                    SourceText = "In-Memory (세션 한정 — RLC_DB_CONN 미설정)";
                }
                RaisePropertyChanged(nameof(Count));
            }
            finally
            {
                IsLoading = false;
                RefreshCommand.RaiseCanExecuteChanged();
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
