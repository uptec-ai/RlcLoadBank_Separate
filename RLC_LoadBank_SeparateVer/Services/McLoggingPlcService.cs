using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// IPlcService 데코레이터 — 모든 WriteMcCommand/FeedbackReceived를 가로채
    /// tb_mc_event를 생산한다 (Phase 4). ServiceHub.Plc가 이 타입으로 감싸서
    /// 반환하므로 VM 코드는 수정 없이 전 명령 경로가 커버된다.
    ///
    /// 동작:
    ///  - WriteMcCommand: (panel, baseTag)로 pending 등록 (cmd_ts, 당시
    ///    DbLog.McCommandContext를 mode로 캡처) 후 inner로 전달.
    ///  - FeedbackReceived: 기대 상태와 일치하는 FB가 오면 confirmed=true로
    ///    1 row 기록. FB 태그 매칭: base(R/L MC) · base_RESULT(C부하) · base_FB(MCCB 등).
    ///  - 타임아웃(8s): FB 미도착 pending을 confirmed=false(CMD/FB 불일치)로 기록.
    ///  - LOCAL 감지: pending 없는 R/L MC 태그의 "상태 변화"는 현장조작으로 간주
    ///    (mode=LOCAL). 연결 직후 첫 상태 보고는 기준값으로만 삼고 기록하지 않는다.
    ///
    /// 스레딩: 명령·피드백·스윕 타이머 모두 UI 스레드(모의/실서비스 공통 규약)라
    /// 잠금이 필요 없다.
    /// </summary>
    public sealed class McLoggingPlcService : IPlcService
    {
        private const int PendingTimeoutMs = 8000;   // C부하 RESULT 타임아웃(5s) + 여유

        private sealed class Pending
        {
            public bool   On;
            public string Mode;
            public DateTimeOffset CmdTs;
        }

        private static readonly Regex RlMcTag = new Regex(@"^P\d+_[RL]_", RegexOptions.Compiled);

        private readonly IPlcService _inner;
        private readonly Dictionary<(int panel, string baseTag), Pending> _pending = new();
        private readonly Dictionary<(int panel, string tag), bool>        _lastState = new();
        private DispatcherTimer _sweep;

        public event EventHandler<int>        ConnectionChanged;
        public event EventHandler<McFeedback> FeedbackReceived;

        public McLoggingPlcService(IPlcService inner)
        {
            _inner = inner;
            _inner.ConnectionChanged += (s, e) => ConnectionChanged?.Invoke(this, e);
            _inner.FeedbackReceived  += OnInnerFeedback;
        }

        public bool IsConnected(int panelIndex) => _inner.IsConnected(panelIndex);
        public void Connect(int panelIndex)     => _inner.Connect(panelIndex);
        public void Disconnect(int panelIndex)  => _inner.Disconnect(panelIndex);

        public void WriteMcCommand(int panelIndex, string mcTag, bool on)
        {
            TrackCommand(panelIndex, mcTag, on);
            _inner.WriteMcCommand(panelIndex, mcTag, on);
        }

        // ── CMD side ──────────────────────────────────────────────────────────

        private void TrackCommand(int panelIndex, string mcTag, bool on)
        {
            if (!ServiceHub.DbWriter.Enabled) return;

            string baseTag = mcTag.EndsWith("_CMD", StringComparison.OrdinalIgnoreCase)
                ? mcTag.Substring(0, mcTag.Length - 4)
                : mcTag;
            // FB가 원천적으로 없는 명령(RESET 등)은 타임아웃 노이즈만 만들므로 제외
            if (baseTag.EndsWith("_RESET", StringComparison.OrdinalIgnoreCase)) return;

            var key = (panelIndex, baseTag);
            if (_pending.TryGetValue(key, out var old))
            {
                // 같은 대상에 새 명령 → 이전 명령은 미확인으로 마감
                ServiceHub.DbLog.LogMcEvent(panelIndex + 1, baseTag, old.On, old.Mode,
                    old.CmdTs, null, confirmed: false, detail: "superseded by new command");
            }
            _pending[key] = new Pending
            {
                On    = on,
                Mode  = ServiceHub.DbLog.McCommandContext,
                CmdTs = DateTimeOffset.UtcNow,
            };
            EnsureSweep();
        }

        // ── FB side ───────────────────────────────────────────────────────────

        private void OnInnerFeedback(object s, McFeedback fb)
        {
            if (ServiceHub.DbWriter.Enabled)
            {
                string baseTag = fb.McTag.EndsWith("_RESULT", StringComparison.OrdinalIgnoreCase)
                    ? fb.McTag.Substring(0, fb.McTag.Length - 7)
                    : fb.McTag.EndsWith("_FB", StringComparison.OrdinalIgnoreCase)
                        ? fb.McTag.Substring(0, fb.McTag.Length - 3)
                        : fb.McTag;

                var key = (fb.PanelIndex, baseTag);
                if (_pending.TryGetValue(key, out var p) && fb.On == p.On)
                {
                    _pending.Remove(key);
                    ServiceHub.DbLog.LogMcEvent(fb.PanelIndex + 1, baseTag, p.On, p.Mode,
                        p.CmdTs, DateTimeOffset.UtcNow, confirmed: true);
                }
                else if (RlMcTag.IsMatch(fb.McTag) && fb.McTag == baseTag)
                {
                    // pending 없는 R/L MC 상태 변화 = 현장(Local) 조작.
                    // 첫 보고는 기준값으로만 저장 (연결 직후 초기 상태 일괄 보고 대응).
                    var stateKey = (fb.PanelIndex, fb.McTag);
                    if (_lastState.TryGetValue(stateKey, out bool prev) && prev != fb.On
                        && !_pending.ContainsKey(key))
                    {
                        ServiceHub.DbLog.LogMcEvent(fb.PanelIndex + 1, baseTag, fb.On, "LOCAL",
                            null, DateTimeOffset.UtcNow, confirmed: true, detail: "FB change without HMI command");
                    }
                    _lastState[stateKey] = fb.On;
                }
            }

            FeedbackReceived?.Invoke(this, fb);
        }

        // ── Timeout sweep (UI thread, 2 s) ────────────────────────────────────

        private void EnsureSweep()
        {
            if (_sweep != null || Application.Current == null) return;
            _sweep = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _sweep.Tick += (s, e) =>
            {
                if (_pending.Count == 0) return;
                var now = DateTimeOffset.UtcNow;
                foreach (var kv in _pending.Where(kv => (now - kv.Value.CmdTs).TotalMilliseconds > PendingTimeoutMs).ToList())
                {
                    _pending.Remove(kv.Key);
                    ServiceHub.DbLog.LogMcEvent(kv.Key.panel + 1, kv.Key.baseTag, kv.Value.On, kv.Value.Mode,
                        kv.Value.CmdTs, null, confirmed: false, detail: "FB timeout");
                }
            };
            _sweep.Start();
        }
    }
}
