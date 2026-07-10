using System;
using System.Linq;
using System.Windows.Threading;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Backs the MainWindow RibbonStatusBar bindings
    /// (Application.Current.StatusManager.*). Lightweight summary + live clock.
    /// </summary>
    public class StatusManager : ViewModelBase
    {
        private readonly DispatcherTimer _timer;
        private readonly System.Collections.Generic.IReadOnlyList<DeviceRecord> _isems;
        private readonly System.Collections.Generic.IReadOnlyList<DeviceRecord> _gimacs;

        public StatusManager()
        {
            var all = ServiceHub.Devices.LoadDevices();
            _isems  = all.Where(d => d.Type == DeviceType.ISEM).ToList();
            _gimacs = all.Where(d => d.Type == DeviceType.GIMAC).ToList();

            UpdateNetwork();
            UpdateMeteringCounts();
            CommState = "시스템 정상";
            CurrentTime = Now();

            ServiceHub.Plc.ConnectionChanged     += (s, i) => UpdateNetwork();
            ServiceHub.Metering.ConnectionChanged += (s, d) => UpdateMeteringCounts();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => CurrentTime = Now();
            _timer.Start();
        }

        public string Network     { get => GetValue<string>(); set => SetValue(value); }
        public string Isem        { get => GetValue<string>(); set => SetValue(value); }
        public string Gimac       { get => GetValue<string>(); set => SetValue(value); }
        public string CommState   { get => GetValue<string>(); set => SetValue(value); }
        public string CurrentTime { get => GetValue<string>(); set => SetValue(value); }

        private static string Now() => DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");

        private void UpdateNetwork()
        {
            int n = 0;
            for (int i = 0; i < 3; i++) if (ServiceHub.Plc.IsConnected(i)) n++;
            Network = $"PLC {n}/3 연결";
        }

        private void UpdateMeteringCounts()
        {
            int isemConn  = _isems.Count(d => ServiceHub.Metering.IsConnected(d.Ip, d.Port, d.UnitId));
            int gimacConn = _gimacs.Count(d => ServiceHub.Metering.IsConnected(d.Ip, d.Port, d.UnitId));
            Isem  = $"ISEM {isemConn}/{_isems.Count} 연결";
            Gimac = $"GIMAC {gimacConn}/{_gimacs.Count} 연결";
        }
    }
}
