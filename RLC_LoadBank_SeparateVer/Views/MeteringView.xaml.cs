using System.Windows.Controls;
using System.Windows.Input;
using RLC_LoadBank_SeparateVer.ViewModels;

namespace RLC_LoadBank_SeparateVer.Views
{
    public partial class MeteringView : UserControl
    {
        public MeteringView()
        {
            InitializeComponent();
        }

        // 더블클릭 시 ZoomExtents(전체 FIFO 표시) 대신 슬라이딩 창(_xWindowSize)으로 리셋.
        private void OnDeltaChartDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MeteringViewModel vm)
                vm.ResetDeltaZoomCommand.Execute(null);
            e.Handled = true;
        }
    }
}
