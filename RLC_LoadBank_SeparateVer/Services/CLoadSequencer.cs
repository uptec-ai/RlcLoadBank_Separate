using System;
using System.Threading.Tasks;

namespace RLC_LoadBank_SeparateVer.Services
{
    public interface ICLoadSequencer
    {
        /// <summary>Runs the C-load energize/de-energize sequence for one stage,
        /// returning true on success (all feedback confirmed within timeout).</summary>
        Task<bool> RunAsync(int panelIndex, int stage, bool on, Action<string> log = null);
    }

    /// <summary>
    /// C-load switching sequence + interlocks (system spec §8). Each command waits
    /// for the MC/SCR aux-contact feedback before the next step, which enforces the
    /// interlocks (no direct-MC close until resistor MC confirmed; no resistor open
    /// until direct MC confirmed); a per-step timeout fails the sequence (alarm).
    ///
    /// Sub-device tags per panel/stage: C{s}_R_MC (resistor-path), C{s}_DIR_MC
    /// (direct), C{s}_SCR (gating) — confirm against the real PLC map. Detailed
    /// timing values are placeholders (spec says timing TBD).
    /// </summary>
    public class CLoadSequencer : ICLoadSequencer
    {
        public int StepTimeoutMs { get; set; } = 3000;
        public int TransferDelayMs { get; set; } = 300;

        public async Task<bool> RunAsync(int panelIndex, int stage, bool on, Action<string> log = null)
        {
            int p = panelIndex + 1;
            string rMc = $"P{p}_C{stage}_R_MC";    // resistor-path MC (inrush limiting)
            string dMc = $"P{p}_C{stage}_DIR_MC";  // direct (bypass) MC
            string scr = $"P{p}_C{stage}_SCR";     // SCR gating

            if (on)
            {
                // ① resistor MC ON → ② SCR ON → ③ (delay) direct MC ON → ④ resistor MC OFF
                if (!await Cmd(panelIndex, rMc, true, log)) return false;
                if (!await Cmd(panelIndex, scr, true, log)) return false;
                await Task.Delay(TransferDelayMs);
                if (!await Cmd(panelIndex, dMc, true, log)) return false;   // gated by rMc feedback above
                if (!await Cmd(panelIndex, rMc, false, log)) return false;  // gated by dMc feedback above
                return true;
            }

            // de-energize: direct MC OFF → SCR OFF → resistor MC OFF
            if (!await Cmd(panelIndex, dMc, false, log)) return false;
            if (!await Cmd(panelIndex, scr, false, log)) return false;
            if (!await Cmd(panelIndex, rMc, false, log)) return false;
            return true;
        }

        private async Task<bool> Cmd(int panel, string tag, bool val, Action<string> log)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<McFeedback> handler = (s, fb) =>
            {
                if (fb.PanelIndex == panel && fb.McTag == tag && fb.On == val) tcs.TrySetResult(true);
            };
            ServiceHub.Plc.FeedbackReceived += handler;
            try
            {
                log?.Invoke($"{tag} <= {(val ? "ON" : "OFF")}");
                ServiceHub.Plc.WriteMcCommand(panel, tag, val);
                var done = await Task.WhenAny(tcs.Task, Task.Delay(StepTimeoutMs));
                bool ok = done == tcs.Task;
                if (!ok) log?.Invoke($"{tag} 피드백 타임아웃");
                return ok;
            }
            finally { ServiceHub.Plc.FeedbackReceived -= handler; }
        }
    }
}
