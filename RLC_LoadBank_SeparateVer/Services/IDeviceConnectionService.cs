using System.Collections.Generic;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>Persisted Modbus device record (one grid row in the connect popup).</summary>
    public class DeviceRecord
    {
        public bool Use { get; set; }
        public DeviceType Type { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; } = 502;
        public int UnitId { get; set; } = 1;
        public int SlaveId { get; set; } = 1;
        public int CurrentReg { get; set; }
        public double Scale { get; set; } = 1;
        public int InputReg { get; set; }
        public int PollInterval { get; set; } = 500;
        public int Timeout { get; set; } = 1000;
        public ConnState State { get; set; } = ConnState.Disabled;
    }

    /// <summary>
    /// Loads/saves the device registry and performs connect/disconnect/test.
    /// Stubbed now (mock data); real Modbus + persistence wired later.
    /// </summary>
    public interface IDeviceConnectionService
    {
        IReadOnlyList<DeviceRecord> LoadDevices();
        void SaveDevices(IEnumerable<DeviceRecord> devices);

        /// <summary>Try to open a Modbus TCP socket to the device. Returns success.</summary>
        bool TestConnection(DeviceRecord device, out string error);

        void Connect(DeviceRecord device);
        void Disconnect(DeviceRecord device);
    }
}
