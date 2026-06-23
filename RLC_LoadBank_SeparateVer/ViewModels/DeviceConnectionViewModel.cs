using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>
    /// "장비 연결 설정" popup.
    ///
    /// PLC 연결/해제는 ServiceHub.Plc (ModbusPlcService) 에 위임하여
    /// 대시보드와 같은 소켓을 공유한다.
    /// ISEM / GIMAC 은 TCP probe (IDeviceConnectionService) 로 처리한다.
    /// Server tab은 placeholder (기능 off).
    /// </summary>
    public class DeviceConnectionViewModel : ViewModelBase
    {
        public ObservableCollection<DeviceItemViewModel> Plcs   { get; } = new();
        public ObservableCollection<DeviceItemViewModel> Isems  { get; } = new();
        public ObservableCollection<DeviceItemViewModel> Gimacs { get; } = new();
        public ObservableCollection<DeviceItemViewModel> Servers{ get; } = new();

        public DeviceItemViewModel SelectedDevice
        {
            get => GetValue<DeviceItemViewModel>();
            set => SetValue(value, OnSelectionChanged);
        }
        public bool HasSelection => SelectedDevice != null;

        // Monitoring server (placeholder)
        public string ServerIp      { get => GetValue<string>(); set => SetValue(value); }
        public int    ServerPort    { get => GetValue<int>();    set => SetValue(value); }
        public bool   ServerRunning { get => GetValue<bool>();   set => SetValue(value); }
        public string ServerStateText => ServerRunning ? "Running" : "Stopped";
        public string ServerClients   => "0/16";

        // Top summary
        public string PlcSummary       => $"{Plcs.Count(d => d.IsConnected)}/{Plcs.Count} 연결됨";
        public int    IsemConnected    => Isems.Count(d => d.IsConnected);
        public int    IsemDisconnected => Isems.Count - IsemConnected;
        public int    IsemTotal        => Isems.Count;
        public int    GimacConnected   => Gimacs.Count(d => d.IsConnected);
        public int    GimacDisconnected=> Gimacs.Count - GimacConnected;
        public int    GimacTotal       => Gimacs.Count;

        public event EventHandler RequestClose;

        public DelegateCommand ConnectSelectedCommand    { get; }
        public DelegateCommand DisconnectSelectedCommand { get; }
        public DelegateCommand ApplyAllCommand           { get; }
        public DelegateCommand TestConnectionCommand     { get; }
        public DelegateCommand SaveCommand               { get; }
        public DelegateCommand CloseCommand              { get; }
        public DelegateCommand ApplyServerCommand        { get; }
        public DelegateCommand<DeviceItemViewModel> ConnectDeviceCommand    { get; }
        public DelegateCommand<DeviceItemViewModel> DisconnectDeviceCommand { get; }

        public DeviceConnectionViewModel()
        {
            // Load device list from app.config
            foreach (var r in ServiceHub.Devices.LoadDevices())
            {
                var vm = new DeviceItemViewModel(r);
                switch (r.Type)
                {
                    case DeviceType.PLC:   Plcs.Add(vm);    break;
                    case DeviceType.ISEM:  Isems.Add(vm);   break;
                    case DeviceType.GIMAC: Gimacs.Add(vm);  break;
                    default:               Servers.Add(vm); break;
                }
            }

            // Sync PLC row states with the actual Plc service connection state
            SyncPlcStates();
            ServiceHub.Plc.ConnectionChanged += OnPlcConnectionChanged;

            ServerIp = "127.0.0.1"; ServerPort = 7000; ServerRunning = true;

            ConnectSelectedCommand    = new DelegateCommand(ConnectChecked,    HasChecked);
            DisconnectSelectedCommand = new DelegateCommand(DisconnectChecked, HasChecked);
            TestConnectionCommand     = new DelegateCommand(TestSelected,      () => HasSelection);
            ApplyAllCommand           = new DelegateCommand(ApplyAll);
            SaveCommand               = new DelegateCommand(Save);
            CloseCommand              = new DelegateCommand(Close);
            ApplyServerCommand        = new DelegateCommand(() =>
                DXMessageBox.Show("Monitoring Server 기능은 추후 제공 예정입니다.", "안내",
                    MessageBoxButton.OK, MessageBoxImage.Information));
            ConnectDeviceCommand    = new DelegateCommand<DeviceItemViewModel>(ConnectOne);
            DisconnectDeviceCommand = new DelegateCommand<DeviceItemViewModel>(DisconnectOne);

            SelectedDevice = Plcs.FirstOrDefault();
        }

        // ── PLC connection sync ───────────────────────────────────────────────

        private void SyncPlcStates()
        {
            for (int i = 0; i < Plcs.Count; i++)
            {
                bool connected = ServiceHub.Plc.IsConnected(i);
                Plcs[i].State = connected ? ConnState.Connected : ConnState.Disconnected;
                if (connected) Plcs[i].LastSeen = DateTime.Now;
            }
            RefreshSummary();
        }

        // Called on UI thread by ModbusPlcService.RaiseConn()
        private void OnPlcConnectionChanged(object sender, int panelIndex)
        {
            if (panelIndex < 0 || panelIndex >= Plcs.Count) return;
            bool connected = ServiceHub.Plc.IsConnected(panelIndex);
            Plcs[panelIndex].State = connected ? ConnState.Connected : ConnState.Disconnected;
            if (connected) Plcs[panelIndex].LastSeen = DateTime.Now;
            RefreshSummary();
        }

        // ── Selection ─────────────────────────────────────────────────────────

        private void OnSelectionChanged()
        {
            ConnectSelectedCommand?.RaiseCanExecuteChanged();
            DisconnectSelectedCommand?.RaiseCanExecuteChanged();
            TestConnectionCommand?.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(HasSelection));
        }

        // ── Batch operations (Use=true) ───────────────────────────────────────

        private IEnumerable<DeviceItemViewModel> AllDevices => Plcs.Concat(Isems).Concat(Gimacs);

        private bool HasChecked() => AllDevices.Any(d => d.Use);

        private void ConnectChecked()
        {
            foreach (var d in AllDevices.Where(d => d.Use).ToList())
                ConnectOne(d);
        }

        private void DisconnectChecked()
        {
            foreach (var d in AllDevices.Where(d => d.Use).ToList())
                DisconnectOne(d);
        }

        // ── Per-device connect / disconnect ───────────────────────────────────

        private void ConnectOne(DeviceItemViewModel d)
        {
            if (d == null) return;

            if (d.Type == DeviceType.PLC)
            {
                int idx = Plcs.IndexOf(d);
                if (idx >= 0)
                {
                    // Bridge to the shared PlcService — same socket as the dashboard
                    d.State = ConnState.Connecting;
                    ServiceHub.Plc.Connect(idx);   // async; result arrives via ConnectionChanged
                }
                return;
            }

            // ISEM / GIMAC: TCP probe via DeviceConnectionService
            var rec = d.ToRecord();
            ServiceHub.Devices.Connect(rec);
            d.State = rec.State;
            if (d.IsConnected) d.LastSeen = DateTime.Now;
            RefreshSummary();
        }

        private void DisconnectOne(DeviceItemViewModel d)
        {
            if (d == null) return;

            if (d.Type == DeviceType.PLC)
            {
                int idx = Plcs.IndexOf(d);
                if (idx >= 0) ServiceHub.Plc.Disconnect(idx);  // triggers ConnectionChanged
                return;
            }

            ServiceHub.Devices.Disconnect(d.ToRecord());
            d.State = ConnState.Disconnected;
            RefreshSummary();
        }

        private void TestSelected()
        {
            var d = SelectedDevice;
            if (d == null) return;
            bool ok = ServiceHub.Devices.TestConnection(d.ToRecord(), out var err);
            d.LastError = ok ? "-" : err;
            DXMessageBox.Show(
                ok ? $"{d.Name} 연결 성공" : $"{d.Name} 연결 실패\n{err}",
                "연결 테스트", MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void ApplyAll()
        {
            foreach (var d in AllDevices.ToList())
                if (d.Use) ConnectOne(d); else DisconnectOne(d);
            DXMessageBox.Show("선택(Use)된 장비에 설정을 일괄 적용했습니다.",
                "전체 적용", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Save()
        {
            // 1. Persist to app.config
            var all = Plcs.Concat(Isems).Concat(Gimacs).Concat(Servers).Select(d => d.ToRecord());
            ServiceHub.Devices.SaveDevices(all);

            // 2. Rebuild ModbusPlcService with the new config so the next Connect()
            //    uses the updated IP/Port. Disconnect active panels first to avoid leaks.
            ServiceHub.Plc.ConnectionChanged -= OnPlcConnectionChanged;
            for (int i = 0; i < Plcs.Count; i++)
                ServiceHub.Plc.Disconnect(i);
            ServiceHub.ResetPlcService();
            ServiceHub.Plc.ConnectionChanged += OnPlcConnectionChanged;
            SyncPlcStates();

            DXMessageBox.Show("장비 설정을 저장했습니다.\nPLC 서비스가 새 주소로 재설정되었습니다.",
                "저장", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close()
        {
            ServiceHub.Plc.ConnectionChanged -= OnPlcConnectionChanged;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshSummary()
        {
            RaisePropertyChanged(nameof(PlcSummary));
            RaisePropertyChanged(nameof(IsemConnected));
            RaisePropertyChanged(nameof(IsemDisconnected));
            RaisePropertyChanged(nameof(GimacConnected));
            RaisePropertyChanged(nameof(GimacDisconnected));
        }
    }
}
