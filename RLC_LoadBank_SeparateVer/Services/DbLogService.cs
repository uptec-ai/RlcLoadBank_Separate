using System;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Phase 2 producer: writes tb_app_session and tb_connection_event rows
    /// through DbWriterService. Created as a ServiceHub singleton so the app
    /// session starts as soon as ServiceHub is first touched.
    ///
    /// Categories: session rows are Critical (FK anchor for alarms — always
    /// stored); connection events are Normal (dropped while the dashboard
    /// "DB 기록" toggle is OFF).
    /// </summary>
    public sealed class DbLogService
    {
        private readonly DbWriterService _db;
        private IPlcService _plc;   // 현재 구독 중인 PLC 서비스 (ResetPlcService 대응)

        public DbLogService(DbWriterService db, IMeteringService metering, IPlcService plc)
        {
            _db = db;
            if (!db.Enabled) return;

            StartSession();
            RunRetention();
            metering.ConnectionChanged += OnMeteringConnectionChanged;
            _plc = plc;
            _plc.ConnectionChanged += OnPlcConnectionChanged;
        }

        /// <summary>보존정책 (schema.sql과 일치): 1분 집계 2년. 앱 시작 시 1회 실행.</summary>
        private void RunRetention()
        {
            _db.Enqueue(DbLogCategory.Critical,
                "DELETE FROM tb_gimac_agg_1m WHERE ts < now() - interval '2 years'");
            _db.Enqueue(DbLogCategory.Critical,
                "DELETE FROM tb_isem_agg_1m WHERE ts < now() - interval '2 years'");
        }

        // ── 앱 세션 (tb_app_session) ──────────────────────────────────────────

        private void StartSession()
        {
            _db.EnqueueSessionStart(
                "INSERT INTO tb_app_session (started_ts, app_version) VALUES (@t, @v) RETURNING id",
                ("t", DateTimeOffset.UtcNow),
                ("v", typeof(DbLogService).Assembly.GetName().Version?.ToString()));
        }

        /// <summary>App.OnExit에서 호출 — 정상 종료 표시. 비정상 종료 시 ended_ts는 NULL로 남는다.</summary>
        public void EndSession()
        {
            if (!_db.Enabled) return;
            _db.Enqueue(DbLogCategory.Critical,
                "UPDATE tb_app_session SET ended_ts = @t WHERE id = @id",
                ("t", DateTimeOffset.UtcNow),
                ("id", DbWriterService.SessionIdRef));
        }

        private void NotePanelUsed(int panelNo)
        {
            _db.Enqueue(DbLogCategory.Critical,   // 세션 메타 — FK 앵커의 일부라 항상 기록
                @"UPDATE tb_app_session
                     SET panels_used = array_append(panels_used, @p)
                   WHERE id = @id AND NOT (@p = ANY(panels_used))",
                ("p", (short)panelNo),
                ("id", DbWriterService.SessionIdRef));
        }

        // ── 장비 연결 이벤트 (tb_connection_event) ────────────────────────────

        private void OnPlcConnectionChanged(object s, int panelIndex)
        {
            int panel = panelIndex + 1;
            bool connected = ServiceHub.Plc.IsConnected(panelIndex);
            InsertConnectionEvent("PLC", panel, panel, connected, null);
            if (connected) NotePanelUsed(panel);
        }

        private void OnMeteringConnectionChanged(object s, DeviceRecord rec)
        {
            bool connected = ServiceHub.Metering.IsConnected(rec.Ip, rec.Port, rec.UnitId);
            int? panel = PanelOf(rec.Type, rec.UnitId);
            InsertConnectionEvent(rec.Type.ToString(), rec.UnitId, panel, connected, rec.Name);
            if (connected && panel is int p) NotePanelUsed(p);
        }

        private void InsertConnectionEvent(string deviceType, int unitId, int? panelNo, bool connected, string detail)
        {
            _db.Enqueue(DbLogCategory.Normal,
                @"INSERT INTO tb_connection_event (ts, session_id, device_type, unit_id, panel_no, connected, detail)
                  VALUES (@ts, @sid, @dt, @uid, @p, @c, @d)",
                ("ts",  DateTimeOffset.UtcNow),
                ("sid", DbWriterService.SessionIdRef),
                ("dt",  deviceType),
                ("uid", (short)unitId),
                ("p",   panelNo.HasValue ? (object)(short)panelNo.Value : null),
                ("c",   connected),
                ("d",   detail));
        }

        // ── PLC 인스턴스 교체 대응 ────────────────────────────────────────────

        /// <summary>
        /// ServiceHub.ResetPlcService()가 새 IPlcService 인스턴스를 만들 때 호출 —
        /// 구 인스턴스 구독을 해제하고 새 인스턴스로 갈아탄다
        /// (RlcStatusViewModel.OpenConnection의 재구독과 같은 이유).
        /// </summary>
        public void RewirePlc()
        {
            if (!_db.Enabled) return;
            if (_plc != null) _plc.ConnectionChanged -= OnPlcConnectionChanged;
            _plc = ServiceHub.Plc;
            _plc.ConnectionChanged += OnPlcConnectionChanged;
        }

        // ── 판넬 매핑 (기록 시점 비정규화용) ──────────────────────────────────

        /// <summary>
        /// device → panel mapping. ISEM은 기존 코드 규칙(MeteringViewModel.IsemBelongsToPanel:
        /// uid 1–3→PNL-1, 4–6→PNL-2, 7–10→PNL-3)과 동일하게 유지. GIMAC 4는 통합/BUS로 보고 NULL.
        /// </summary>
        public static int? PanelOf(DeviceType type, int unitId) => type switch
        {
            DeviceType.PLC   => unitId is >= 1 and <= 3 ? unitId : null,
            DeviceType.GIMAC => unitId is >= 1 and <= 3 ? unitId : null,
            DeviceType.ISEM  => unitId <= 3 ? 1 : unitId <= 6 ? 2 : unitId <= 10 ? 3 : (int?)null,
            _                => null,
        };
    }
}
