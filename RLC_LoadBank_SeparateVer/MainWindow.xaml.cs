using DevExpress.Xpf.Core;
using DevExpress.Xpf.WindowsUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RLC_LoadBank_SeparateVer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : ThemedWindow
    {
        private App App => (App)Application.Current;

        public MainWindow()
        {
            InitializeComponent();
            App.MainWindow = this;

            // Drive the nav-button selection ourselves (single source of truth) so a
            // single click selects + navigates. Set the initial selection after the
            // HamburgerMenu has finished initializing (Background priority), otherwise
            // the menu clears it during its own load.
            Loaded += (s, e) => Dispatcher.BeginInvoke(
                new Action(() => Select(Btn_Dashboard, App.HomeView)),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>Selects exactly one nav button and navigates the frame to its view.</summary>
        private void Select(HamburgerMenuNavigationButton button, object view)
        {
            Btn_Dashboard.IsSelected = ReferenceEquals(button, Btn_Dashboard);
            Btn_SystemStatus.IsSelected = ReferenceEquals(button, Btn_SystemStatus);
            Btn_History.IsSelected = ReferenceEquals(button, Btn_History);
            NaviFrame.Navigate(view);
        }

        private void Btn_Dashboard_Click(object sender, RoutedEventArgs e) => Select(Btn_Dashboard, App.HomeView);

        private void Btn_SystemStatus_Click(object sender, RoutedEventArgs e) => Select(Btn_SystemStatus, App.MeteringView);

        private void Btn_History_Click(object sender, RoutedEventArgs e) => Select(Btn_History, App.HistoryView);

        private void InfoBtn_Click(object sender, RoutedEventArgs e)
        {
            DXMessageBox.Show("RLC Load Bank\nUPTEC", "정보");
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult msgResult = WinUIMessageBox.Show(
                GetWindow(App.MainWindow),
                "모니터링 중입니다.\r\n그래도 종료 하시겠습니까?",
                null,
                MessageBoxButton.YesNo,
                MessageBoxImage.None,
                MessageBoxResult.None,
                MessageBoxOptions.None,
                FloatingMode.Window);

            if (msgResult == MessageBoxResult.Yes)
            {
                this.Close();
            }
        }

    }
}
