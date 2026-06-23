using System.Collections.Generic;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>Target setpoints for automatic operation (R/L/C individual targets).</summary>
    public class AutoTargets
    {
        public double RkW { get; set; }
        public double LkVar { get; set; }
        public double CkVar { get; set; }
    }

    /// <summary>
    /// One selectable load increment. For PLC2/3 this is a single 3-phase step;
    /// for PLC1 (single-phase) it is a balanced step = the same step on R-N/S-N/T-N
    /// (so <see cref="Tags"/> holds the 3 phase MC tags switched together).
    /// </summary>
    public class AutoStep
    {
        public int PanelIndex { get; set; }
        public LoadType Load { get; set; }
        public double Value { get; set; }   // 3-phase-equivalent magnitude (kW or kvar)
        public string[] Tags { get; set; }
    }

    /// <summary>Planning result: the MC tags that should be ON (all others OFF).</summary>
    public class AutoPlan
    {
        public List<string> OnTags { get; } = new List<string>();
    }

    /// <summary>
    /// Computes which MC steps to switch in to reach the R/L/C targets and tracks
    /// run state. Pure computation — the ViewModel applies the returned plan
    /// (state + PLC commands). See <see cref="AutoOperationService"/>.
    /// </summary>
    public interface IAutoOperationService
    {
        bool IsRunning { get; }
        /// <summary>목표 계산만 수행 (IsRunning 변경 없음). 미리보기용.</summary>
        AutoPlan Preview(AutoTargets targets, IReadOnlyList<AutoStep> available);
        AutoPlan Start(AutoTargets targets, IReadOnlyList<AutoStep> available);
        void Stop();
    }
}
