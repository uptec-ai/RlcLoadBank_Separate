using System.Collections.Generic;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>Operation-history persistence. In-memory by default; Postgres
    /// (Npgsql) when <see cref="ServiceHub.UseDatabase"/> is enabled.</summary>
    public interface IHistoryRepository
    {
        void Add(HistoryEntry entry);
        IReadOnlyList<HistoryEntry> Query(int max = 500);
    }
}
