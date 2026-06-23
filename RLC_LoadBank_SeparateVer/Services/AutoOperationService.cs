using System.Collections.Generic;
using System.Linq;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Auto-operation planner. v1 heuristic: per load type, greedily pick the
    /// largest steps whose running sum does not exceed the target
    /// (≈ nearest-without-overshoot across the connected panels' step inventory).
    ///
    /// Deliberately a baseline:
    ///  - Not an optimal subset-sum (good enough for the discrete RLC step set).
    ///  - A "target power + power factor" mode can be layered on later.
    ///  - C-load is planned at the STAGE level (50 kvar each). The detailed
    ///    SCR + resistor-MC + direct-MC timed sequence and interlocks (spec §8,
    ///    timing TBD) are a TODO for when the real PLC sequence is confirmed.
    /// </summary>
    public class AutoOperationService : IAutoOperationService
    {
        public bool IsRunning { get; private set; }

        public AutoPlan Preview(AutoTargets targets, IReadOnlyList<AutoStep> available)
            => Compute(targets, available);

        public AutoPlan Start(AutoTargets targets, IReadOnlyList<AutoStep> available)
        {
            IsRunning = true;
            return Compute(targets, available);
        }

        public void Stop() => IsRunning = false;

        private static AutoPlan Compute(AutoTargets targets, IReadOnlyList<AutoStep> available)
        {
            var plan = new AutoPlan();
            plan.OnTags.AddRange(SelectClosest(available, LoadType.R, targets.RkW));
            plan.OnTags.AddRange(SelectClosest(available, LoadType.L, targets.LkVar));
            plan.OnTags.AddRange(SelectClosest(available, LoadType.C, targets.CkVar));
            return plan;
        }

        private static IEnumerable<string> SelectClosest(IReadOnlyList<AutoStep> steps, LoadType load, double target)
        {
            var chosen = new List<string>();
            double sum = 0;
            foreach (var s in steps.Where(s => s.Load == load).OrderByDescending(s => s.Value))
            {
                if (sum + s.Value <= target + 1e-6)
                {
                    sum += s.Value;
                    chosen.AddRange(s.Tags);
                }
            }
            return chosen;
        }
    }
}
