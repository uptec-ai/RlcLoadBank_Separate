using DevExpress.Xpf.Core;
using RLC_LoadBank_SeparateVer.ViewModels;

namespace RLC_LoadBank_SeparateVer.Windows
{
    public partial class DeviceConnectionWindow : ThemedWindow
    {
        public DeviceConnectionWindow()
        {
            InitializeComponent();
            var vm = new DeviceConnectionViewModel();
            vm.RequestClose += (s, e) => Close();
            DataContext = vm;
        }
    }
}
