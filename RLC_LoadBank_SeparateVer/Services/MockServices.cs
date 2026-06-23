using System;
using System.Collections.Generic;
using System.Linq;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    #region 모의 PLC/장치 연결 서비스
    /// <summary>
    /// PLC 연결 모의(Mock) 구현체.
    /// MC/C-sub WriteMcCommand 호출 시 즉시 FeedbackReceived 이벤트로 에코(Echo) 응답.
    /// 상태/명령 태그(MCCB_*_CMD, RESET_CMD)는 UpdateStatus로 전달되어
    /// 모의 패널 상태가 실제처럼 동작하도록 처리됨
    /// (예: MCCB_ON_CMD → MCCB_ON_FB = true 발생).
    /// Connect 호출 시 기본 초기 상태를 전송함
    /// (MCCB ON, 원격(Remote) 모드, 알람 없음).
    /// </summary>
    public class MockPlcService : IPlcService
    {
        private readonly bool[] _connected = { true, true, true };

        public bool IsConnected(int panelIndex) =>
            panelIndex >= 0 && panelIndex < _connected.Length && _connected[panelIndex];

        public void Connect(int panelIndex)
        {
            if (panelIndex < 0 || panelIndex >= _connected.Length) return;
            _connected[panelIndex] = true;
            ConnectionChanged?.Invoke(this, panelIndex);
            FireInitialStatus(panelIndex);
        }

        public void Disconnect(int panelIndex)
        {
            if (panelIndex < 0 || panelIndex >= _connected.Length) return;
            _connected[panelIndex] = false;
            ConnectionChanged?.Invoke(this, panelIndex);
        }

        public void WriteMcCommand(int panelIndex, string mcTag, bool on)
        {
            if (mcTag.EndsWith("_CMD", StringComparison.OrdinalIgnoreCase))
            {
                // Control command → simulate status feedback
                SimulateStatusFeedback(panelIndex, mcTag, on);
            }
            else
            {
                // 실제 PLC는 폴링 주기(~500ms) 후 피드백이 오므로 CommWait(주황색)이
                // UI에 렌더링될 시간이 있다. Mock도 짧은 지연 후 피드백을 보낸다.
                var fb = new McFeedback { PanelIndex = panelIndex, McTag = mcTag, On = on };
                System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
                {
                    var disp = System.Windows.Application.Current?.Dispatcher;
                    if (disp != null)
                        disp.BeginInvoke(new Action(() => FeedbackReceived?.Invoke(this, fb)));
                    else
                        FeedbackReceived?.Invoke(this, fb);
                });
            }
        }

        public event EventHandler<int>        ConnectionChanged;
        public event EventHandler<McFeedback> FeedbackReceived;

        // ── Helpers ──────────────────────────────────────────────────────────

        private void Raise(int panel, string tag, bool on) =>
            FeedbackReceived?.Invoke(this, new McFeedback { PanelIndex = panel, McTag = tag, On = on });

        private void FireInitialStatus(int panelIndex)
        {
            int p = panelIndex + 1;
            Raise(panelIndex, $"P{p}_MCCB_ON_FB",   true);   // MCCB는 ON 상태
            Raise(panelIndex, $"P{p}_MCCB_OFF_FB",  false);
            Raise(panelIndex, $"P{p}_MCCB_TRIP_FB", false);
            Raise(panelIndex, $"P{p}_EMG_FB",        false);  // E-Stop 정상
            Raise(panelIndex, $"P{p}_OVR_FB",        false);
            Raise(panelIndex, $"P{p}_OCR_FB",        false);
            Raise(panelIndex, $"P{p}_HT_FB",         false);
            Raise(panelIndex, $"P{p}_DOOR_FB",       false);
            Raise(panelIndex, $"P{p}_LOC_REM_FB",    true);   // Remote 모드
            Raise(panelIndex, $"P{p}_PWR_380_FB",    true);
            Raise(panelIndex, $"P{p}_FAN_FB",        true);
        }

        private void SimulateStatusFeedback(int panelIndex, string cmdTag, bool on)
        {
            // MCCB_ON_CMD → MCCB_ON_FB = on, MCCB_OFF_FB = !on
            if (cmdTag.EndsWith("MCCB_ON_CMD",   StringComparison.OrdinalIgnoreCase))
            {
                int p = panelIndex + 1;
                Raise(panelIndex, $"P{p}_MCCB_ON_FB",  on);
                Raise(panelIndex, $"P{p}_MCCB_OFF_FB", !on);
            }
            else if (cmdTag.EndsWith("MCCB_OFF_CMD",  StringComparison.OrdinalIgnoreCase))
            {
                int p = panelIndex + 1;
                Raise(panelIndex, $"P{p}_MCCB_ON_FB",  !on);
                Raise(panelIndex, $"P{p}_MCCB_OFF_FB", on);
            }
            else if (cmdTag.EndsWith("MCCB_TRIP_CMD", StringComparison.OrdinalIgnoreCase))
            {
                int p = panelIndex + 1;
                Raise(panelIndex, $"P{p}_MCCB_TRIP_FB", on);
                if (on) Raise(panelIndex, $"P{p}_MCCB_ON_FB", false);
            }
            // RESET_CMD, FAN_*_CMD 등은 별도 피드백 없이 무시
        }
    }

    /// <summary>
    /// 장치 레지스트리 모의(Mock) 구현체.
    /// 영속성(Persistence)은 DeviceConfigService에 위임함.
    /// </summary>
    public class MockDeviceConnectionService : IDeviceConnectionService
    {
        public IReadOnlyList<DeviceRecord> LoadDevices() => DeviceConfigService.Load();
        public void SaveDevices(IEnumerable<DeviceRecord> devices) => DeviceConfigService.Save(devices);

        public bool TestConnection(DeviceRecord device, out string error)
        {
            error = null;
            if (device.Type == DeviceType.PLC) return true;
            error = "미연결 (스텁)";
            return false;
        }

        public void Connect(DeviceRecord device) =>
            device.State = device.Type == DeviceType.PLC ? ConnState.Connected : ConnState.Disabled;

        public void Disconnect(DeviceRecord device) => device.State = ConnState.Disconnected;
    }
    #endregion

    /// <summary>
    /// 정적 서비스 로케이터.
    /// PLC에 실제 연결 가능한 경우 UseRealHardware = true 로 설정.
    /// </summary>
    public static class ServiceHub
    {
        public static bool UseRealHardware = true;

        private static IPlcService _plc;
        // ServiceHub.Plc        => PLC 연결/명령/피드백 (ModbusPlcService)
        public static IPlcService Plc =>
            _plc ??= UseRealHardware ? (IPlcService)new ModbusPlcService() : new MockPlcService(); 

        public static void ResetPlcService() { _plc = null; }

        private static IDeviceConnectionService _devices;
        // ServiceHub.Devices    => 장비 목록 로드/저장 (ModbusDeviceConnectionService)
        public static IDeviceConnectionService Devices =>
            _devices ??= UseRealHardware
                ? (IDeviceConnectionService)new ModbusDeviceConnectionService()
                : new MockDeviceConnectionService();

        // ServiceHub.Auto       => 자동운전 계획 (AutoOperationService)
        public static IAutoOperationService Auto  { get; } = new AutoOperationService();
        // ServiceHub.CLoad      => C부하 시퀀스 (CLoadSequencer)
        public static ICLoadSequencer       CLoad { get; } = new CLoadSequencer();

        public static bool   UseDatabase      = false;
        public static string ConnectionString =
            "Host=localhost;Port=5432;Database=rlc;Username=postgres;Password=postgres";

        private static IHistoryRepository _history;
        // ServiceHub.History    => 운전 이력 (InMemoryHistoryRepository)
        public static IHistoryRepository History => _history ??=
            UseDatabase
                ? new PostgresHistoryRepository(ConnectionString)
                : (IHistoryRepository)new InMemoryHistoryRepository();
    }
}
