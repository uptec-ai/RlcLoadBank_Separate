using RLC_LoadBank_SeparateVer.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RLC_LoadBank_SeparateVer.Views
{
    public partial class RlcStatusView : UserControl
    {
        // ComboBox 항목을 프로그래밍으로 설정하는 중 SelectionChanged가 VM에 다시 쓰지 않도록 차단.
        private bool _settingSelection;
        private RlcStatusViewModel _vm;
        private bool _isPowerTrendUserNavigating;
        public RlcStatusView()
        {
            InitializeComponent();
            // DataContext는 XAML(<vm:RlcStatusViewModel/>)에 의해 InitializeComponent() 내부에서
            // 이미 설정된다. DataContextChanged 핸들러를 그 이후 등록하면 초기 알림을 놓치므로
            // InitializeComponent() 완료 후 DataContext를 직접 읽어 초기화한다.
            if (DataContext is RlcStatusViewModel vm)
            {
                _vm = vm;
                _vm.PropertyChanged += OnVmPropertyChanged;
                SyncPanelComboBox();
            }
            // 이후 DataContext가 교체될 경우를 대비해 핸들러 등록 유지
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = e.NewValue as RlcStatusViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                SyncPanelComboBox();
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RlcStatusViewModel.ConnectedPanels) ||
                e.PropertyName == nameof(RlcStatusViewModel.SelectedAutoPanel))
                SyncPanelComboBox();
        }

        // ComboBox Items를 직접 재구성. XAML 바인딩 대신 이 메서드 하나가 UI←VM 방향을 담당.
        // "전체 판넬" 항목을 항상 맨 위에 두어 disconnect 시 자동으로 선택되도록 한다.
        private void SyncPanelComboBox()
        {
            if (_vm == null) return;
            _settingSelection = true;
            try
            {
                PanelComboBox.Items.Clear();
                // index 0: "전체 판넬" (SelectedAutoPanel = null 에 해당)
                PanelComboBox.Items.Add(new ComboBoxItem { Content = "전체 판넬", Tag = (object)null });
                foreach (var p in _vm.ConnectedPanels)
                    PanelComboBox.Items.Add(new ComboBoxItem { Content = p.Title, Tag = p });

                if (_vm.SelectedAutoPanel == null)
                {
                    PanelComboBox.SelectedIndex = 0;
                }
                else
                {
                    var match = PanelComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(ci => ci.Tag == _vm.SelectedAutoPanel);
                    PanelComboBox.SelectedItem = match ?? PanelComboBox.Items[0];
                }
            }
            finally
            {
                _settingSelection = false;
            }
        }

        // UI→VM 방향: 사용자가 직접 선택했을 때만 VM에 반영 (프로그래밍 변경 시 _settingSelection으로 차단).
        private void PanelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingSelection || _vm == null) return;
            _vm.SelectedAutoPanel = (PanelComboBox.SelectedItem as ComboBoxItem)?.Tag as PanelViewModel;
        }

        private void PowerTrendChart_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _isPowerTrendUserNavigating = false;
            ResetPowerTrendChartRange();
            e.Handled = true;
        }

        private void ResetPowerTrendChartRange()
        {
            // X axis uses AutoRange="Always" — scrolls automatically.
            // ZoomExtents resets any manual Y-axis pan the user may have applied.
            PowerTrendChart.ZoomExtents();
        }
    }
}
