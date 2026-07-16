using System;
using System.Collections.Generic;
using Npgsql;
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
        private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

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

        // ── 운전 이벤트 (tb_operation_event) — Phase 4 ───────────────────────

        /// <summary>
        /// MC 명령의 운전 맥락. McLoggingPlcService가 명령 시점에 캡처해 tb_mc_event.mode로
        /// 기록한다. RlcStatusViewModel이 각 흐름 시작 시 설정: MANUAL(기본)/AUTO/SYSTEM.
        /// 시퀀스는 동시에 1개만 실행되므로(IsSequenceRunning 게이트) 단일 값으로 충분.
        /// </summary>
        public string McCommandContext { get; set; } = "MANUAL";

        /// <summary>동작 완료 1건 = 1 row. opType: MC_ON/MC_OFF/SEQ_ON/SEQ_OFF/ALL_OFF/
        /// AUTO_COMPLETE/MODE_CHANGE/MCCB_ON/MCCB_OFF/MCCB_TRIP ... (자유 텍스트)</summary>
        public void LogOperation(int? panelNo, string mode, string opType, string result,
            string loadType = null, string phase = null, string target = null,
            double? appliedRkW = null, double? appliedLkVar = null, double? appliedCkVar = null,
            string detailJson = null)
        {
            if (!_db.Enabled) return;
            _db.Enqueue(DbLogCategory.Normal,
                @"INSERT INTO tb_operation_event
                    (ts, session_id, panel_no, mode, op_type, load_type, phase, target,
                     applied_r_kw, applied_l_kvar, applied_c_kvar, result, detail)
                  VALUES (@ts,@sid,@p::smallint,@m,@o,@l,@ph,@tg,@r,@lk,@c,@res,@d::jsonb)",
                ("ts",  DateTimeOffset.UtcNow),
                ("sid", DbWriterService.SessionIdRef),
                ("p",   panelNo.HasValue ? (object)(short)panelNo.Value : null),
                ("m",   mode), ("o", opType), ("l", loadType), ("ph", phase), ("tg", target),
                ("r",   appliedRkW), ("lk", appliedLkVar), ("c", appliedCkVar),
                ("res", result), ("d", detailJson));
        }

        // ── MC 상태 변화 (tb_mc_event) — McLoggingPlcService가 호출 ──────────

        public void LogMcEvent(int panelNo, string mcTag, bool on, string mode,
            DateTimeOffset? cmdTs, DateTimeOffset? fbTs, bool confirmed, string detail = null)
        {
            if (!_db.Enabled) return;
            _db.Enqueue(DbLogCategory.Normal,
                @"INSERT INTO tb_mc_event
                    (ts, session_id, panel_no, mc_tag, action, mode, cmd_ts, fb_ts, confirmed, detail)
                  VALUES (@ts,@sid,@p,@tag,@act,@m,@c,@f,@conf,@d)",
                ("ts",   fbTs ?? cmdTs ?? DateTimeOffset.UtcNow),
                ("sid",  DbWriterService.SessionIdRef),
                ("p",    (short)panelNo),
                ("tag",  mcTag),
                ("act",  on ? "ON" : "OFF"),
                ("m",    mode),
                ("c",    cmdTs.HasValue ? (object)cmdTs.Value : null),
                ("f",    fbTs.HasValue ? (object)fbTs.Value : null),
                ("conf", confirmed),
                ("d",    detail));
        }

        // ── 알람 에피소드 (tb_alarm_event) — Critical: 토글과 무관하게 항상 저장 ──

        /// <summary>알람 발생. instant=true면 지속시간 없는 단발 이벤트(발생=해제).
        /// 지속형은 같은 (type, panel)의 open 에피소드가 있으면 중복 insert하지 않는다.</summary>
        public void AlarmRaised(int? panelNo, string alarmType, string detail = null, bool instant = false)
        {
            if (!_db.Enabled) return;
            object p = panelNo.HasValue ? (object)(short)panelNo.Value : null;
            if (instant)
                _db.Enqueue(DbLogCategory.Critical,
                    @"INSERT INTO tb_alarm_event (session_id, panel_no, alarm_type, raised_ts, cleared_ts, detail)
                      VALUES (@sid,@p::smallint,@a,@t,@t,@d)",
                    ("sid", DbWriterService.SessionIdRef), ("p", p), ("a", alarmType),
                    ("t", DateTimeOffset.UtcNow), ("d", detail));
            else
                _db.Enqueue(DbLogCategory.Critical,
                    @"INSERT INTO tb_alarm_event (session_id, panel_no, alarm_type, raised_ts, detail)
                      SELECT @sid::bigint, @p::smallint, @a, @t, @d
                       WHERE NOT EXISTS (SELECT 1 FROM tb_alarm_event
                                          WHERE cleared_ts IS NULL AND alarm_type = @a
                                            AND panel_no IS NOT DISTINCT FROM @p::smallint)",
                    ("sid", DbWriterService.SessionIdRef), ("p", p), ("a", alarmType),
                    ("t", DateTimeOffset.UtcNow), ("d", detail));
        }

        /// <summary>지속형 알람 해제 — 같은 (type, panel)의 open 에피소드를 닫는다.</summary>
        public void AlarmCleared(int? panelNo, string alarmType)
        {
            if (!_db.Enabled) return;
            _db.Enqueue(DbLogCategory.Critical,
                @"UPDATE tb_alarm_event SET cleared_ts = @t
                   WHERE cleared_ts IS NULL AND alarm_type = @a
                     AND panel_no IS NOT DISTINCT FROM @p::smallint",
                ("t", DateTimeOffset.UtcNow), ("a", alarmType),
                ("p", panelNo.HasValue ? (object)(short)panelNo.Value : null));
        }

        // ── 운전 이벤트 조회 (OperationHistoryView) ──────────────────────────

        /// <summary>최근 max건의 tb_operation_event를 읽는다 (동기 — UI 스레드에서
        /// 직접 부르지 말고 Task.Run으로 감쌀 것). 실패/비활성 시 빈 리스트.
        /// from/to는 로컬 시간(내부에서 UTC 변환), panelNo 지정 시 해당 판넬 +
        /// 공통 이벤트(panel_no IS NULL — EMG 전체 차단 등)를 함께 반환한다.</summary>
        public IReadOnlyList<OperationEventRecord> QueryOperations(
            int max = 500, DateTime? fromLocal = null, DateTime? toLocal = null, int? panelNo = null)
        {
            var list = new List<OperationEventRecord>();
            if (!_db.Enabled) return list;
            try
            {
                string sql =
                    @"SELECT ts, panel_no, mode, op_type, load_type, phase, target,
                             applied_r_kw, applied_l_kvar, applied_c_kvar, result, detail::text
                        FROM tb_operation_event WHERE TRUE";
                if (fromLocal.HasValue) sql += " AND ts >= @f";
                if (toLocal.HasValue)   sql += " AND ts <= @t";
                if (panelNo.HasValue)   sql += " AND (panel_no = @p OR panel_no IS NULL)";
                sql += " ORDER BY ts DESC LIMIT @m";

                using var conn = new NpgsqlConnection(ServiceHub.ConnectionString);
                conn.Open();
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("m", max);
                if (fromLocal.HasValue) cmd.Parameters.AddWithValue("f", fromLocal.Value.ToUniversalTime());
                if (toLocal.HasValue)   cmd.Parameters.AddWithValue("t", toLocal.Value.ToUniversalTime());
                if (panelNo.HasValue)   cmd.Parameters.AddWithValue("p", (short)panelNo.Value);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new OperationEventRecord
                    {
                        Ts       = r.GetDateTime(0).ToLocalTime(),   // timestamptz(UTC) → 로컬
                        PanelNo  = r.IsDBNull(1)  ? (int?)null : r.GetInt16(1),
                        Mode     = r.GetString(2),
                        OpType   = r.GetString(3),
                        LoadType = r.IsDBNull(4)  ? null : r.GetString(4),
                        Phase    = r.IsDBNull(5)  ? null : r.GetString(5),
                        Target   = r.IsDBNull(6)  ? null : r.GetString(6),
                        RkW      = r.IsDBNull(7)  ? (decimal?)null : r.GetDecimal(7),
                        LkVar    = r.IsDBNull(8)  ? (decimal?)null : r.GetDecimal(8),
                        CkVar    = r.IsDBNull(9)  ? (decimal?)null : r.GetDecimal(9),
                        Result   = r.GetString(10),
                        Detail   = r.IsDBNull(11) ? null : r.GetString(11),
                    });
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "QueryOperations failed: {0}", ex.Message);
            }
            return list;
        }

        /// <summary>알람 에피소드 조회 (raised_ts 기준 필터, 최신순).</summary>
        public IReadOnlyList<AlarmEventRecord> QueryAlarms(
            int max = 500, DateTime? fromLocal = null, DateTime? toLocal = null, int? panelNo = null)
        {
            var list = new List<AlarmEventRecord>();
            if (!_db.Enabled) return list;
            try
            {
                string sql = @"SELECT raised_ts, cleared_ts, panel_no, alarm_type, detail
                                 FROM tb_alarm_event WHERE TRUE";
                if (fromLocal.HasValue) sql += " AND raised_ts >= @f";
                if (toLocal.HasValue)   sql += " AND raised_ts <= @t";
                if (panelNo.HasValue)   sql += " AND (panel_no = @p OR panel_no IS NULL)";
                sql += " ORDER BY raised_ts DESC LIMIT @m";

                using var conn = new NpgsqlConnection(ServiceHub.ConnectionString);
                conn.Open();
                using var cmd = BuildFilteredCommand(conn, sql, max, fromLocal, toLocal, panelNo);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new AlarmEventRecord
                    {
                        RaisedTs  = r.GetDateTime(0).ToLocalTime(),
                        ClearedTs = r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1).ToLocalTime(),
                        PanelNo   = r.IsDBNull(2) ? (int?)null : r.GetInt16(2),
                        AlarmType = r.GetString(3),
                        Detail    = r.IsDBNull(4) ? null : r.GetString(4),
                    });
            }
            catch (Exception ex) { Log.Warn(ex, "QueryAlarms failed: {0}", ex.Message); }
            return list;
        }

        /// <summary>계측 1분 집계 조회 — GIMAC과 ISEM을 합쳐 최신순으로 반환.
        /// ISEM 전류는 L1~L3 평균으로 단일화.</summary>
        public IReadOnlyList<MeterAggRecord> QueryMeterAggs(
            int max = 500, DateTime? fromLocal = null, DateTime? toLocal = null, int? panelNo = null)
        {
            var list = new List<MeterAggRecord>();
            if (!_db.Enabled) return list;
            try
            {
                string cond = "";
                if (fromLocal.HasValue) cond += " AND ts >= @f";
                if (toLocal.HasValue)   cond += " AND ts <= @t";
                if (panelNo.HasValue)   cond += " AND (panel_no = @p OR panel_no IS NULL)";
                string sql = $@"SELECT * FROM (
                        SELECT ts, 'GIMAC' AS dt, unit_id, panel_no, volt_avg, curr_avg,
                               kw_avg, kw_min, kw_max, pf_avg, hz_avg
                          FROM tb_gimac_agg_1m WHERE TRUE{cond}
                        UNION ALL
                        SELECT ts, 'ISEM', unit_id, panel_no, volt_avg,
                               ((curr_l1_avg + curr_l2_avg + curr_l3_avg) / 3)::real,
                               kw_avg, kw_min, kw_max, pf_avg, hz_avg
                          FROM tb_isem_agg_1m WHERE TRUE{cond}
                    ) u ORDER BY ts DESC LIMIT @m";

                using var conn = new NpgsqlConnection(ServiceHub.ConnectionString);
                conn.Open();
                using var cmd = BuildFilteredCommand(conn, sql, max, fromLocal, toLocal, panelNo);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new MeterAggRecord
                    {
                        Ts         = r.GetDateTime(0).ToLocalTime(),
                        DeviceType = r.GetString(1),
                        UnitId     = r.GetInt16(2),
                        PanelNo    = r.IsDBNull(3) ? (int?)null : r.GetInt16(3),
                        VoltAvg    = ToD(r.GetValue(4)),
                        CurrAvg    = ToD(r.GetValue(5)),
                        KwAvg      = ToD(r.GetValue(6)),
                        KwMin      = ToD(r.GetValue(7)),
                        KwMax      = ToD(r.GetValue(8)),
                        PfAvg      = ToD(r.GetValue(9)),
                        HzAvg      = ToD(r.GetValue(10)),
                    });
            }
            catch (Exception ex) { Log.Warn(ex, "QueryMeterAggs failed: {0}", ex.Message); }
            return list;

            static double ToD(object v) => v is DBNull ? 0 : Convert.ToDouble(v);
        }

        /// <summary>장비 연결/해제 이벤트 조회 (최신순).</summary>
        public IReadOnlyList<ConnectionEventRecord> QueryConnections(
            int max = 500, DateTime? fromLocal = null, DateTime? toLocal = null, int? panelNo = null)
        {
            var list = new List<ConnectionEventRecord>();
            if (!_db.Enabled) return list;
            try
            {
                string sql = @"SELECT ts, device_type, unit_id, panel_no, connected, detail
                                 FROM tb_connection_event WHERE TRUE";
                if (fromLocal.HasValue) sql += " AND ts >= @f";
                if (toLocal.HasValue)   sql += " AND ts <= @t";
                if (panelNo.HasValue)   sql += " AND (panel_no = @p OR panel_no IS NULL)";
                sql += " ORDER BY ts DESC LIMIT @m";

                using var conn = new NpgsqlConnection(ServiceHub.ConnectionString);
                conn.Open();
                using var cmd = BuildFilteredCommand(conn, sql, max, fromLocal, toLocal, panelNo);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new ConnectionEventRecord
                    {
                        Ts         = r.GetDateTime(0).ToLocalTime(),
                        DeviceType = r.GetString(1),
                        UnitId     = r.GetInt16(2),
                        PanelNo    = r.IsDBNull(3) ? (int?)null : r.GetInt16(3),
                        Connected  = r.GetBoolean(4),
                        Detail     = r.IsDBNull(5) ? null : r.GetString(5),
                    });
            }
            catch (Exception ex) { Log.Warn(ex, "QueryConnections failed: {0}", ex.Message); }
            return list;
        }

        // 공통 필터 파라미터 바인딩 (from/to는 로컬 → UTC 변환)
        private static NpgsqlCommand BuildFilteredCommand(NpgsqlConnection conn, string sql,
            int max, DateTime? fromLocal, DateTime? toLocal, int? panelNo)
        {
            var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("m", max);
            if (fromLocal.HasValue) cmd.Parameters.AddWithValue("f", fromLocal.Value.ToUniversalTime());
            if (toLocal.HasValue)   cmd.Parameters.AddWithValue("t", toLocal.Value.ToUniversalTime());
            if (panelNo.HasValue)   cmd.Parameters.AddWithValue("p", (short)panelNo.Value);
            return cmd;
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
