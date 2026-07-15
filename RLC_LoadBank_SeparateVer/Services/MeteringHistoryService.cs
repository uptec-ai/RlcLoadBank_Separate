using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Threading: MeteringService raises its data events on the UI thread and
    /// ViewModels read from the UI thread, so no locking is needed.
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

        // Time-based aggregation tracking (anchored per panel on first sample)
        private readonly DateTime[] _lastMinAgg  = new DateTime[3];
        private readonly DateTime[] _lastHourAgg = new DateTime[3];
        private readonly DateTime[] _lastDayAgg  = new DateTime[3];

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

        public MeteringHistoryService(IMeteringService metering)
        {
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
            _lastIsem[r.Device.UnitId] = r;
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
    }
}
