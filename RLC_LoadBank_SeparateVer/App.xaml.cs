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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // DB 라이터 조기 기동 (RLC_DB_CONN 미설정이면 내부적으로 비활성).
            // 첫 배치 전에 EnsureSchema(임베디드 db/schema.sql)가 백그라운드에서 실행된다.
            _ = ServiceHub.DbWriter;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 종료 전 큐 잔여분 드레인 (최대 3초)
            ServiceHub.DbWriter.Shutdown(System.TimeSpan.FromSeconds(3));
            base.OnExit(e);
        }

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
