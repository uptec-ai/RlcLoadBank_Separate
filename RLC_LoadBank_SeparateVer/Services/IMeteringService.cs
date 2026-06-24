using System;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Persistent Modbus TCP connection manager for ISEM/GIMAC metering devices.
    /// Survives DeviceConnectionWindow close/reopen — lives as a ServiceHub singleton.
    ///
    /// Polling sequence per connected device (500 ms interval):
    ///   GIMAC 1000  : FC4 ReadInputRegisters — Block A (main) + Block B (THD) → GimacDataReceived
    ///   EOCR-iSEM2  : FC3 ReadHoldingRegisters — Block 1–5 → IsemDataReceived
    /// </summary>
    public interface IMeteringService
    {
        bool IsConnected(string ip, int port, int unitId);
        void Connect(DeviceRecord device);
        void Disconnect(DeviceRecord device);

        /// <summary>Fired on the UI thread when a device connects or disconnects.</summary>
        event EventHandler<DeviceRecord> ConnectionChanged;

        /// <summary>
        /// Fired on the UI thread after each successful 500 ms GIMAC poll cycle.
        /// Subscriber (e.g. MeteringViewModel) can map DeviceRecord → bus position.
        /// </summary>
        event EventHandler<GimacReading> GimacDataReceived;

        /// <summary>
        /// Fired on the UI thread after each successful 500 ms ISEM poll cycle.
        /// Subscriber can map DeviceRecord → line number (#1–#10).
        /// </summary>
        event EventHandler<IsemReading> IsemDataReceived;
    }
}
