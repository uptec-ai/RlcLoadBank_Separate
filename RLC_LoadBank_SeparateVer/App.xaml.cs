using DevExpress.Xpf.Core;
using System.Windows;
using RLC_LoadBank_SeparateVer.Services;
using RLC_LoadBank_SeparateVer.ViewModels;
using RLC_LoadBank_SeparateVer.Views;

namespace RLC_LoadBank_SeparateVer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            CompatibilitySettings.UseLightweightThemes = true;
            var sciKey = System.Environment.GetEnvironmentVariable("EMS_SCICHART_LICENSE_KEY");
            if (!string.IsNullOrEmpty(sciKey))
                SciChart.Charting.Visuals.SciChartSurface.SetRuntimeLicenseKey(sciKey);
        }
        /// <summary>Backs the MainWindow status-bar bindings (Application.Current.StatusManager.*).</summary>
        public StatusManager StatusManager { get; } = new StatusManager();

        // ---- Navigable views, created once on first access (lazy singletons). ----
        // Single instances on purpose: their VMs run timers / subscribe to events,
        // so re-creating on every nav would leak. App holds them; MainWindow swaps
        // via NaviFrame. Nav mapping (their HamburgerMenu has 3 content buttons):
        //   DASHBOARD -> HomeView, SYSTEM STATUS -> MeteringView, HISTORY -> HistoryView.
        private RlcStatusView _homeView;
        public RlcStatusView HomeView => _homeView ??= new RlcStatusView();

        private MeteringView _meteringView;
        public MeteringView MeteringView => _meteringView ??= new MeteringView();

        private OperationHistoryView _historyView;
        public OperationHistoryView HistoryView => _historyView ??= new OperationHistoryView();
    }
}
