using System.Collections.Generic;
using System.Linq;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>Default history store (process memory). Sorted newest-first on query.</summary>
    public class InMemoryHistoryRepository : IHistoryRepository
    {
        private readonly List<HistoryEntry> _items = new List<HistoryEntry>();
        private readonly object _gate = new object();

        public void Add(HistoryEntry entry)
        {
            lock (_gate) _items.Add(entry);
        }

        public IReadOnlyList<HistoryEntry> Query(int max = 500)
        {
            lock (_gate)
                return _items.OrderByDescending(e => e.Time).Take(max).ToList();
        }
    }
}
