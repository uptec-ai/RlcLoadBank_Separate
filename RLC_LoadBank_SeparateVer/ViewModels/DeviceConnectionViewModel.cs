using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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
    /// PLC  : ServiceHub.Plc (ModbusPlcService) — 대시보드와 동일 소켓 공유.
    /// ISEM / GIMAC : ServiceHub.Metering (MeteringService) — 창 닫아도 연결 유지,
    ///                500ms 폴링 스레드 실행 중.
    /// Server tab   : placeholder (기능 off).
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

        // 0=PLC, 1=ISEM, 2=GIMAC, 3=Server — bound to TabControl.SelectedIndex
        public int ActiveTab
        {
            get => GetValue<int>();
            set => SetValue(value, () =>
            {
                ConnectSelectedCommand?.RaiseCanExecuteChanged();
                DisconnectSelectedCommand?.RaiseCanExecuteChanged();
            });
        }

        // Monitoring server (placeholder)
        public string ServerIp      { get => GetValue<string>(); set => SetValue(value); }
        public int    ServerPort    { get => GetValue<int>();    set => SetValue(value); }
        public bool   ServerRunning { get => GetValue<bool>();   set => SetValue(value); }
        public string ServerStateText => ServerRunning ? "Running" : "Stopped";
        public string ServerClients   => "0/16";

        // Top summary
        public string PlcSummary        => $"{Plcs.Count(d => d.IsConnected)}/{Plcs.Count} 연결됨";
        public int    IsemConnected     => Isems.Count(d => d.IsConnected);
        public int    IsemDisconnected  => Isems.Count - IsemConnected;
        public int    IsemTotal         => Isems.Count;
        public int    GimacConnected    => Gimacs.Count(d => d.IsConnected);
        public int    GimacDisconnected => Gimacs.Count - GimacConnected;
        public int    GimacTotal        => Gimacs.Count;

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

            // Sync UI states from the persistent services (survives window close/reopen)
            SyncPlcStates();
            SyncMeteringStates();
            ServiceHub.Plc.ConnectionChanged      += OnPlcConnectionChanged;
            ServiceHub.Metering.ConnectionChanged += OnMeteringConnectionChanged;
            _ = ProbeUnconnectedAsync();

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

            // Issue fix: Use 체크박스 변경 → CanExecuteChanged 즉시 발생
            WireUseChangedEvents();
        }

        // ── State sync from persistent services ──────────────────────────────

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

        // ISEM/GIMAC: read connection state from MeteringService singleton
        private void SyncMeteringStates()
        {
            foreach (var d in Isems.Concat(Gimacs))
            {
                bool connected = ServiceHub.Metering.IsConnected(d.Ip, d.Port, d.UnitId);
                d.State = connected ? ConnState.Connected : ConnState.Disconnected;
                if (connected) d.LastSeen = DateTime.Now;
            }
            RefreshSummary();
        }

        // Background TCP probe for non-connected devices → set Idle if port is open
        private async Task ProbeUnconnectedAsync()
        {
            var targets = Plcs.Cast<DeviceItemViewModel>()
                              .Concat(Isems)
                              .Concat(Gimacs)
                              .Where(d => d.State != ConnState.Connected)
                              .ToList();

            await Task.WhenAll(targets.Select(d => Task.Run(() =>
            {
                bool ok = ServiceHub.Devices.TestConnection(d.ToRecord(), out _);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (d.State != ConnState.Connected)
                        d.State = ok ? ConnState.Idle : ConnState.Disconnected;
                });
            })));
        }

        // ── Connection event handlers ─────────────────────────────────────────

        private void OnPlcConnectionChanged(object sender, int panelIndex)
        {
            if (panelIndex < 0 || panelIndex >= Plcs.Count) return;
            bool connected = ServiceHub.Plc.IsConnected(panelIndex);
            Plcs[panelIndex].State = connected ? ConnState.Connected : ConnState.Disconnected;
            if (connected) Plcs[panelIndex].LastSeen = DateTime.Now;
            RefreshSummary();
        }

        private void OnMeteringConnectionChanged(object sender, DeviceRecord record)
        {
            var target = Isems.Concat(Gimacs).FirstOrDefault(
                d => d.Ip == record.Ip && d.Port == record.Port && d.UnitId == record.UnitId);
            if (target == null) return;
            bool connected = ServiceHub.Metering.IsConnected(record.Ip, record.Port, record.UnitId);
            target.State = connected ? ConnState.Connected : ConnState.Disconnected;
            if (connected) target.LastSeen = DateTime.Now;
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

        // Use 체크박스가 바뀔 때마다 선택 장비 연결/해제 버튼 CanExecute 재평가
        private void WireUseChangedEvents()
        {
            foreach (var d in Plcs.Concat(Isems).Concat(Gimacs))
                d.PropertyChanged += OnDeviceUseChanged;
        }

        private void OnDeviceUseChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DeviceItemViewModel.Use)) return;
            ConnectSelectedCommand?.RaiseCanExecuteChanged();
            DisconnectSelectedCommand?.RaiseCanExecuteChanged();
        }

        // ── Batch operations (active tab only) ────────────────────────────────

        private IEnumerable<DeviceItemViewModel> AllDevices => Plcs.Concat(Isems).Concat(Gimacs);

        private IEnumerable<DeviceItemViewModel> ActiveTabDevices => ActiveTab switch
        {
            0 => Plcs,
            1 => Isems,
            2 => Gimacs,
            _ => Enumerable.Empty<DeviceItemViewModel>()
        };

        private bool HasChecked() => ActiveTabDevices.Any(d => d.Use);

        private void ConnectChecked()
        {
            foreach (var d in ActiveTabDevices.Where(d => d.Use).ToList())
                ConnectOne(d);
        }

        private void DisconnectChecked()
        {
            foreach (var d in ActiveTabDevices.Where(d => d.Use).ToList())
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
                    d.State = ConnState.Connecting;
                    ServiceHub.Plc.Connect(idx);   // async; result via OnPlcConnectionChanged
                }
                return;
            }

            // ISEM / GIMAC: persistent TCP via MeteringService
            // async connect → 500ms poll loop → ConnectionChanged fires on success/fail
            d.State = ConnState.Connecting;
            ServiceHub.Metering.Connect(d.ToRecord());
        }

        private void DisconnectOne(DeviceItemViewModel d)
        {
            if (d == null) return;

            if (d.Type == DeviceType.PLC)
            {
                int idx = Plcs.IndexOf(d);
                if (idx >= 0) ServiceHub.Plc.Disconnect(idx);  // triggers OnPlcConnectionChanged
                return;
            }

            // ISEM / GIMAC: stop polling + close TCP
            ServiceHub.Metering.Disconnect(d.ToRecord());  // triggers OnMeteringConnectionChanged
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
            var all = Plcs.Concat(Isems).Concat(Gimacs).Concat(Servers).Select(d => d.ToRecord());
            ServiceHub.Devices.SaveDevices(all);

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
            ServiceHub.Plc.ConnectionChanged      -= OnPlcConnectionChanged;
            ServiceHub.Metering.ConnectionChanged -= OnMeteringConnectionChanged;
            // MeteringService 자체는 닫지 않음 — 연결은 ServiceHub 싱글턴으로 유지
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
