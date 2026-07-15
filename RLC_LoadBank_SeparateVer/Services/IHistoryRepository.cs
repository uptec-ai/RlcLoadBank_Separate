using System.Collections.Generic;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>Session-scoped in-memory operation history (dashboard grid +
    /// HISTORY-view fallback). Persistent history lives in tb_operation_event
    /// via <see cref="DbLogService"/>.</summary>
    public interface IHistoryRepository
    {
        void Add(HistoryEntry entry);
        IReadOnlyList<HistoryEntry> Query(int max = 500);
    }
}
