using System;
using System.Collections.Generic;
using System.Net.Sockets;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Real device connection service.
    /// Device list is loaded from / saved to app.config via DeviceConfigService.
    /// Connect / Disconnect / TestConnection perform actual Modbus TCP socket operations.
    /// </summary>
    public class ModbusDeviceConnectionService : IDeviceConnectionService
    {
        public IReadOnlyList<DeviceRecord> LoadDevices() => DeviceConfigService.Load();

        public void SaveDevices(IEnumerable<DeviceRecord> devices) => DeviceConfigService.Save(devices);

        public bool TestConnection(DeviceRecord device, out string error)
        {
            error = null;
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(device.Ip, device.Port);
                int timeout = Math.Max(500, device.Timeout);
                if (!connect.Wait(timeout) || !client.Connected)
                {
                    error = "연결 시간 초과";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        public void Connect(DeviceRecord device) =>
            device.State = TestConnection(device, out _) ? ConnState.Connected : ConnState.Error;

        public void Disconnect(DeviceRecord device) => device.State = ConnState.Disconnected;
    }
}
