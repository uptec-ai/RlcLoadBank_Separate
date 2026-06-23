using System;
using System.Windows.Threading;
using DevExpress.Mvvm;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Backs the MainWindow RibbonStatusBar bindings
    /// (Application.Current.StatusManager.*). Lightweight summary + live clock.
    /// </summary>
    public class StatusManager : ViewModelBase
    {
        private readonly DispatcherTimer _timer;

        public StatusManager()
        {
            UpdateNetwork();
            Modbus = "Modbus TCP";
            CommState = "시스템 정상";
            SystemState = "RUN";
            CurrentTime = Now();

            ServiceHub.Plc.ConnectionChanged += (s, i) => UpdateNetwork();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => CurrentTime = Now();
            _timer.Start();
        }

        public string Network { get => GetValue<string>(); set => SetValue(value); }
        public string Modbus { get => GetValue<string>(); set => SetValue(value); }
        public string CommState { get => GetValue<string>(); set => SetValue(value); }
        public string SystemState { get => GetValue<string>(); set => SetValue(value); }
        public string CurrentTime { get => GetValue<string>(); set => SetValue(value); }

        private static string Now() => DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");

        private void UpdateNetwork()
        {
            int n = 0;
            for (int i = 0; i < 3; i++) if (ServiceHub.Plc.IsConnected(i)) n++;
            Network = $"PLC {n}/3 연결";
        }
    }
}
