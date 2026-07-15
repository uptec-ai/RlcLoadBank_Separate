using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Npgsql;
using RLC_LoadBank_SeparateVer.Models;
using SciChart.Charting.Model.DataSeries;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// App-lifetime collector/aggregator for metering trend history.
    /// Subscribes to IMeteringService events from construction (created as a
    /// ServiceHub singleton), so active-power delta trends accumulate from the
    /// moment devices connect — regardless of whether MeteringView has ever
    /// been opened. MeteringViewModel only binds/displays the series owned here.
    ///
    /// Persistence (Phase 3): every completed 1-min window is also written to
    /// tb_gimac_agg_1m / tb_isem_agg_1m through DbWriterService (Normal
    /// category — dropped while the dashboard "DB 기록" toggle is OFF), and on
    /// startup the last 2 h of GIMAC 1-min averages are backfilled from the DB
    /// so the 1m delta chart survives app restarts.
    ///
    /// Threading: MeteringService raises its data events on the UI thread and
    /// ViewModels read from the UI thread, so no locking is needed. The DB
    /// backfill runs on a background task and marshals its result back to the
    /// UI thread before touching any buffer.
    ///
    /// Aggregation is TIME-BASED, not sample-count-based, so polling overhead
    /// (FC4 round-trip × 2) does not cause time drift in the reported deltas.
    /// Each period emits exactly 1 delta point per aggregation window:
    ///   1m  → 1 pt / min   (Δ = avg(last 60 s) − avg(prev 60 s))
    ///   1h  → 1 pt / hour  (Δ = avg(last 60 min) − avg(prev 60 min))
    ///   1day→ 1 pt / day   (Δ = avg(last 24 h) − avg(prev 24 h))
    /// </summary>
    public class MeteringHistoryService
    {
        private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        // Buffer capacities
        private const int MaxRaw  = 300;   // ~2.5 min at 500ms (+ processing overhead)
        private const int MaxMin  = 120;   // 2 h  of 1-min averages
        private const int MaxHour = 48;    // 2 d  of 1-hr  averages
        private const int MaxDay  = 14;    // 2 wk of 1-day averages

        // Aggregation periods
        private const double MinuteSec = 60.0;
        private const double HourMin   = 60.0;
        private const double DayHr     = 24.0;

        private readonly DbWriterService _db;   // null/disabled → in-memory only

        // Raw / aggregated buffers [panelIdx 0/1/2]
        private readonly Queue<(DateTime ts, double kw)>[] _rawBuf  = MakeQueues();
        private readonly Queue<(DateTime ts, double kw)>[] _minBuf  = MakeQueues();
        private readonly Queue<(DateTime ts, double kw)>[] _hourBuf = MakeQueues();
        private readonly Queue<(DateTime ts, double kw)>[] _dayBuf  = MakeQueues();

        // Time-based aggregation tracking (anchored per panel on first sample)
        private readonly DateTime[] _lastMinAgg  = new DateTime[3];
        private readonly DateTime[] _lastHourAgg = new DateTime[3];
        private readonly DateTime[] _lastDayAgg  = new DateTime[3];

        // Per-minute DB accumulators (all channels, min/max for kW)
        private readonly GimacAcc[] _gimacAcc = { new GimacAcc(), new GimacAcc(), new GimacAcc() };
        private readonly Dictionary<int, IsemAcc>   _isemAcc    = new();
        private readonly Dictionary<int, DateTime>  _isemAnchor = new();

        // Latest readings — lets a late-created MeteringViewModel fill its
        // KPI cards / detail grid immediately instead of waiting for a poll
        private readonly GimacReading[]               _lastGimac = new GimacReading[3];
        private readonly Dictionary<int, IsemReading> _lastIsem  = new();

        // Delta series [periodIdx: 0=1m, 1=1h, 2=1day][panelIdx 0/1/2]
        // FIFO: 1m=60pts(1h history), 1h=24pts(1d history), 1day=7pts(1w history)
        private readonly XyDataSeries<DateTime, double>[][] _delta;

        // Latest delta timestamp per period — lets a late-created VM position
        // its X window over the data collected before the VM existed
        private readonly DateTime?[] _lastDeltaTs = new DateTime?[3];

        /// <summary>(periodIdx, timestamp) — raised on the UI thread after a
        /// delta point is appended; VMs slide their X window on this.</summary>
        public event Action<int, DateTime> DeltaAppended;

        public MeteringHistoryService(IMeteringService metering, DbWriterService db)
        {
            _db = db;

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

            metering.GimacDataReceived += OnGimacData;
            metering.IsemDataReceived  += OnIsemData;

            if (_db != null && _db.Enabled)
                Task.Run(BackfillFromDb);   // 재시작 후 1m 델타 차트 복원 (UI 차단 금지)
        }

        // ── Read access (UI thread) ───────────────────────────────────────────

        public XyDataSeries<DateTime, double> GetDeltaSeries(int periodIdx, int panelIdx) =>
            _delta[periodIdx][panelIdx];

        public GimacReading GetLastGimac(int panelIdx) =>
            (uint)panelIdx < 3 ? _lastGimac[panelIdx] : null;

        public IReadOnlyDictionary<int, IsemReading> LastIsem => _lastIsem;

        public DateTime? GetLastDeltaTime(int periodIdx) => _lastDeltaTs[periodIdx];

        // ── GIMAC data received (UI thread, ~500 ms + device response time) ────

        private void OnGimacData(object s, GimacReading r)
        {
            int idx = r.Device.UnitId - 1;
            if ((uint)idx >= 3) return;

            _lastGimac[idx] = r;
            double kw = r.ActivePower / 1000.0;
            var    ts = r.Timestamp;

            _gimacAcc[idx].Add(r);

            // ── Raw buffer ────────────────────────────────────────────────────
            var raw = _rawBuf[idx];
            if (raw.Count == 0)
            {
                // First-ever sample for this panel: anchor the aggregation
                // clocks here so windows start after one full period, even
                // when the device connects long after app startup.
                _lastMinAgg[idx]  = ts;
                _lastHourAgg[idx] = ts;
                _lastDayAgg[idx]  = ts;
            }
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

                // 완성된 1분 창을 DB에 저장하고 누산기 리셋
                EnqueueGimacAgg(idx + 1, ts, _gimacAcc[idx]);
                _gimacAcc[idx] = new GimacAcc();

                // 1m Δ: consecutive 1-min averages → 1 pt per minute
                if (minQ.Count >= 2)
                {
                    var m = minQ.ToArray();
                    AppendDelta(0, idx, ts, m[m.Length - 1].kw - m[m.Length - 2].kw);
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
                        AppendDelta(1, idx, ts, h[h.Length - 1].kw - h[h.Length - 2].kw);
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
                            AppendDelta(2, idx, ts, d[d.Length - 1].kw - d[d.Length - 2].kw);
                        }
                    }
                }
            }
        }

        // ── ISEM data received (UI thread, 500 ms) ────────────────────────────

        private void OnIsemData(object s, IsemReading r)
        {
            int uid = r.Device.UnitId;
            _lastIsem[uid] = r;

            if (_db == null || !_db.Enabled) return;   // 집계는 DB 저장 전용

            var ts = r.Timestamp;
            if (!_isemAcc.TryGetValue(uid, out var acc))
            {
                _isemAcc[uid]    = acc = new IsemAcc();
                _isemAnchor[uid] = ts;   // 유닛별 첫 샘플에 분 창 앵커
            }
            acc.Add(r);

            if ((ts - _isemAnchor[uid]).TotalSeconds >= MinuteSec)
            {
                _isemAnchor[uid] = ts;
                EnqueueIsemAgg(uid, ts, acc);
                _isemAcc[uid] = new IsemAcc();
            }
        }

        // ── DB persistence (1-min aggregates) ─────────────────────────────────

        private void EnqueueGimacAgg(int unitId, DateTime ts, GimacAcc a)
        {
            if (_db == null || !_db.Enabled || a.N == 0) return;
            _db.Enqueue(DbLogCategory.Normal,
                @"INSERT INTO tb_gimac_agg_1m
                    (ts, unit_id, panel_no, volt_avg, curr_avg, kw_avg, kw_min, kw_max,
                     kvar_avg, kva_avg, pf_avg, hz_avg, thd_v_avg, thd_i_avg, samples)
                  VALUES (@ts,@u,@p,@va,@ca,@kw,@kn,@kx,@kq,@ks,@pf,@hz,@tv,@ti,@n)
                  ON CONFLICT (unit_id, ts) DO NOTHING",
                ("ts", ts), ("u", (short)unitId), ("p", (short)unitId),
                ("va", (float)(a.Volt / a.N)), ("ca", (float)(a.Curr / a.N)),
                ("kw", (float)(a.Kw / a.N)),   ("kn", (float)a.KwMin), ("kx", (float)a.KwMax),
                ("kq", (float)(a.Kvar / a.N)), ("ks", (float)(a.Kva / a.N)),
                ("pf", (float)(a.Pf / a.N)),   ("hz", (float)(a.Hz / a.N)),
                ("tv", (float)(a.ThdV / a.N)), ("ti", (float)(a.ThdI / a.N)),
                ("n", (short)Math.Min(a.N, short.MaxValue)));
        }

        private void EnqueueIsemAgg(int unitId, DateTime ts, IsemAcc a)
        {
            if (a.N == 0) return;
            int? panel = DbLogService.PanelOf(DeviceType.ISEM, unitId);
            _db.Enqueue(DbLogCategory.Normal,
                @"INSERT INTO tb_isem_agg_1m
                    (ts, unit_id, panel_no, volt_avg, curr_l1_avg, curr_l2_avg, curr_l3_avg,
                     ground_ma_avg, ground_ma_max, kw_avg, kw_min, kw_max,
                     kvar_avg, pf_avg, hz_avg, thd_i_avg, thd_v_avg, samples)
                  VALUES (@ts,@u,@p,@va,@c1,@c2,@c3,@ga,@gx,@kw,@kn,@kx,@kq,@pf,@hz,@ti,@tv,@n)
                  ON CONFLICT (unit_id, ts) DO NOTHING",
                ("ts", ts), ("u", (short)unitId),
                ("p", panel.HasValue ? (object)(short)panel.Value : null),
                ("va", (float)(a.Volt / a.N)),
                ("c1", (float)(a.C1 / a.N)), ("c2", (float)(a.C2 / a.N)), ("c3", (float)(a.C3 / a.N)),
                ("ga", (float)(a.Gnd / a.N)), ("gx", (float)a.GndMax),
                ("kw", (float)(a.Kw / a.N)), ("kn", (float)a.KwMin), ("kx", (float)a.KwMax),
                ("kq", (float)(a.Kvar / a.N)), ("pf", (float)(a.Pf / a.N)), ("hz", (float)(a.Hz / a.N)),
                ("ti", (float)(a.ThdI / a.N)), ("tv", (float)(a.ThdV / a.N)),
                ("n", (short)Math.Min(a.N, short.MaxValue)));
        }

        // ── Startup backfill (background task → UI thread) ────────────────────

        /// <summary>
        /// 최근 2h의 GIMAC 1분 평균(kw_avg)을 읽어 1m 버퍼·델타 시리즈를 복원한다.
        /// 2h = MaxMin(120)과 동일 창. 1h/1day 버퍼는 데이터가 부족해 복원하지 않는다
        /// (live 수집이 다시 채움). DB 불가 시 조용히 건너뛴다 (best-effort).
        /// </summary>
        private void BackfillFromDb()
        {
            try
            {
                var rows = new List<(short uid, DateTime ts, double kw)>();
                using (var conn = new NpgsqlConnection(ServiceHub.ConnectionString))
                {
                    conn.Open();
                    using var cmd = new NpgsqlCommand(
                        @"SELECT unit_id, ts, kw_avg FROM tb_gimac_agg_1m
                           WHERE ts >= now() - interval '2 hours' AND unit_id BETWEEN 1 AND 3
                           ORDER BY ts", conn);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        // timestamptz는 UTC로 읽히므로 차트 축(로컬 시간)에 맞춰 변환
                        rows.Add((r.GetInt16(0), r.GetDateTime(1).ToLocalTime(), r.GetFloat(2)));
                }
                if (rows.Count == 0) return;

                var disp = Application.Current?.Dispatcher;
                if (disp != null) disp.BeginInvoke(new Action(() => ApplyBackfill(rows)));
                else ApplyBackfill(rows);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "MeteringHistory: DB backfill skipped: {0}", ex.Message);
            }
        }

        private void ApplyBackfill(List<(short uid, DateTime ts, double kw)> rows)
        {
            int applied = 0;
            foreach (var g in rows.GroupBy(e => e.uid))
            {
                int idx = g.Key - 1;
                if ((uint)idx >= 3) continue;
                // live 데이터가 이미 흐르기 시작했으면 그 판넬은 건너뜀 (순서 보장 불가)
                if (_rawBuf[idx].Count > 0 || _minBuf[idx].Count > 0) continue;

                var minQ = _minBuf[idx];
                (DateTime ts, double kw)? prev = null;
                foreach (var e in g.OrderBy(e => e.ts))
                {
                    minQ.Enqueue((e.ts, e.kw));
                    while (minQ.Count > MaxMin) minQ.Dequeue();
                    if (prev.HasValue) AppendDelta(0, idx, e.ts, e.kw - prev.Value.kw);
                    prev = (e.ts, e.kw);
                    applied++;
                }
            }
            if (applied > 0)
                Log.Info("MeteringHistory: backfilled {0} 1-min rows from DB.", applied);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AppendDelta(int periodIdx, int panelIdx, DateTime ts, double deltaKw)
        {
            _delta[periodIdx][panelIdx].Append(ts, deltaKw);
            _lastDeltaTs[periodIdx] = ts;
            DeltaAppended?.Invoke(periodIdx, ts);
        }

        private static Queue<(DateTime, double)>[] MakeQueues()
        {
            var q = new Queue<(DateTime, double)>[3];
            for (int i = 0; i < 3; i++) q[i] = new();
            return q;
        }

        // ── Per-minute accumulators (DB rows) ─────────────────────────────────

        private sealed class GimacAcc
        {
            public int N;
            public double Volt, Curr, Kw, Kvar, Kva, Pf, Hz, ThdV, ThdI;
            public double KwMin = double.MaxValue, KwMax = double.MinValue;

            public void Add(GimacReading r)
            {
                N++;
                Volt += r.AvgVoltage;
                Curr += r.AvgCurrent;
                double kw = r.ActivePower / 1000.0;
                Kw += kw;
                if (kw < KwMin) KwMin = kw;
                if (kw > KwMax) KwMax = kw;
                Kvar += r.ReactivePower / 1000.0;
                Kva  += r.ApparentPower / 1000.0;
                Pf   += r.PowerFactor;
                Hz   += r.Frequency;
                ThdV += (r.VoltThdA + r.VoltThdB + r.VoltThdC) / 3.0;
                ThdI += (r.CurrThdA + r.CurrThdB + r.CurrThdC) / 3.0;
            }
        }

        private sealed class IsemAcc
        {
            public int N;
            public double Volt, C1, C2, C3, Gnd, Kw, Kvar, Pf, Hz, ThdI, ThdV;
            public double GndMax = double.MinValue, KwMin = double.MaxValue, KwMax = double.MinValue;

            public void Add(IsemReading r)
            {
                N++;
                Volt += r.AvgVoltage;
                C1 += r.CurrL1; C2 += r.CurrL2; C3 += r.CurrL3;
                Gnd += r.GroundCurrent;
                if (r.GroundCurrent > GndMax) GndMax = r.GroundCurrent;
                Kw += r.ActivePower;                  // IsemReading.ActivePower는 이미 kW
                if (r.ActivePower < KwMin) KwMin = r.ActivePower;
                if (r.ActivePower > KwMax) KwMax = r.ActivePower;
                Kvar += r.ReactivePower;
                Pf   += r.PowerFactor;
                Hz   += r.CurrentFrequency;
                ThdI += r.AvgCurrentThd;
                ThdV += r.AvgVoltageThd;
            }
        }
    }
}
