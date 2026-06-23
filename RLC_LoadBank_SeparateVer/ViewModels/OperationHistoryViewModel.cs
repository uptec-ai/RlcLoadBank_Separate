using System.Collections.ObjectModel;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>운전 이력 (full): reads from the history repository
    /// (in-memory or Postgres per ServiceHub.UseDatabase).</summary>
    public class OperationHistoryViewModel : ViewModelBase
    {
        public ObservableCollection<HistoryEntry> Rows { get; } = new ObservableCollection<HistoryEntry>();
        public int Count => Rows.Count;
        public string SourceText => ServiceHub.UseDatabase ? "PostgreSQL" : "In-Memory";

        public DelegateCommand RefreshCommand { get; }

        public OperationHistoryViewModel()
        {
            RefreshCommand = new DelegateCommand(Load);
            Load();
        }

        private void Load()
        {
            Rows.Clear();
            foreach (var e in ServiceHub.History.Query(500)) Rows.Add(e);
            RaisePropertyChanged(nameof(Count));
        }
    }
}
