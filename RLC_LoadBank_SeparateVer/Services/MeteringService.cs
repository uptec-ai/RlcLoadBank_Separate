using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NModbus;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Manages persistent Modbus TCP connections for ISEM/GIMAC metering devices.
    /// Each connected device runs an independent 500 ms polling loop on a background thread.
    ///
    /// GIMAC 1000  (LS ELECTRIC)      : FC4 ReadInputRegisters — 32-bit IEEE 754 float values
    /// EOCR-iSEM2 + sPDM (Schneider)  : FC3 ReadHoldingRegisters — 16-bit values with scale factors
    ///
    /// Register details: MeteringProtocol.cs
    /// </summary>
    public class MeteringService : IMeteringService
    {
        private class DeviceLink
        {
            public DeviceRecord Record;
            public TcpClient Tcp;
            public IModbusMaster Master;
            public CancellationTokenSource Cts;
            public bool Connected;
            public bool Cancelled;
        }

        // keyed by "ip:port:unitId" — accessed only on UI thread (Connect/Disconnect/IsConnected)
        private readonly Dictionary<string, DeviceLink> _links =
            new Dictionary<string, DeviceLink>(StringComparer.OrdinalIgnoreCase);

        private readonly ModbusFactory _factory = new ModbusFactory();

        private static string Key(DeviceRecord d) => $"{d.Ip}:{d.Port}:{d.UnitId}";

        public bool IsConnected(string ip, int port, int unitId) =>
            _links.TryGetValue($"{ip}:{port}:{unitId}", out var l) && l.Connected;

        public void Connect(DeviceRecord device)
        {
            var key = Key(device);
            if (_links.TryGetValue(key, out var existing) && existing.Connected) return;

            var link = new DeviceLink { Record = device };
            _links[key] = link;
            Task.Run(() => OpenAsync(link));
        }

        public void Disconnect(DeviceRecord device)
        {
            var key = Key(device);
            if (!_links.TryGetValue(key, out var link)) return;
            link.Cancelled = true;
            CloseLink(link);
            _links.Remove(key);
            RaiseChanged(device);
        }

        // ── Async connect ─────────────────────────────────────────────────────

        private async Task OpenAsync(DeviceLink link)
        {
            try
            {
                var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(link.Record.Ip, link.Record.Port);
                int timeout = Math.Max(500, link.Record.Timeout);
                var done = await Task.WhenAny(connectTask, Task.Delay(timeout));

                if (link.Cancelled) { tcp.Close(); return; }

                if (done != connectTask || !tcp.Connected)
                {
                    tcp.Close();
                    RaiseChanged(link.Record);
                    return;
                }

                link.Tcp       = tcp;
                link.Master    = _factory.CreateMaster(tcp);
                link.Cts       = new CancellationTokenSource();
                link.Connected = true;
                RaiseChanged(link.Record);

                _ = Task.Run(() => PollLoop(link, link.Cts.Token));
            }
            catch { RaiseChanged(link.Record); }
        }

        // ── Poll loop ─────────────────────────────────────────────────────────

        private async Task PollLoop(DeviceLink link, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (link.Record.Type == DeviceType.GIMAC)
                        PollGimac(link);
                    else
                        PollIsem(link);

                    await Task.Delay(500, ct);
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    link.Connected = false;
                    RaiseChanged(link.Record);
                    return;
                }
            }
        }

        // ── GIMAC 1000 ────────────────────────────────────────────────────────
        // FC4 ReadInputRegisters; all values are 32-bit IEEE 754 float (big-endian word order).
        // Block A: main (avg V/I, per-phase V/I, line V, PF, P/Q/S, Hz) — 32 registers
        // Block B: THD (EX model, voltage + current per phase) — 12 registers

        private void PollGimac(DeviceLink link)
        {
            byte uid = (byte)link.Record.UnitId;

            ushort[] main = link.Master.ReadInputRegisters(uid,
                MeteringProtocol.Gimac_MainAddr, MeteringProtocol.Gimac_MainCount);

            ushort[] thd = link.Master.ReadInputRegisters(uid,
                MeteringProtocol.Gimac_ThdAddr, MeteringProtocol.Gimac_ThdCount);

            var r = new GimacReading
            {
                Device        = link.Record,
                Timestamp     = DateTime.Now,
                AvgVoltage    = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_AvgVoltage),
                AvgCurrent    = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_AvgCurrent),
                CurrA         = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_CurrA),
                CurrB         = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_CurrB),
                CurrC         = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_CurrC),
                VoltA         = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_VoltA),
                VoltB         = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_VoltB),
                VoltC         = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_VoltC),
                VoltAB        = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_VoltAB),
                VoltBC        = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_VoltBC),
                VoltCA        = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_VoltCA),
                PowerFactor   = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_PowerFactor),
                ActivePower   = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_ActivePower),
                ReactivePower = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_ReactivePower),
                ApparentPower = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_ApparentPower),
                Frequency     = MeteringProtocol.ToFloat(main, MeteringProtocol.Gimac_Frequency),
                VoltThdA      = MeteringProtocol.ToFloat(thd,  MeteringProtocol.Gimac_VoltThdA),
                VoltThdB      = MeteringProtocol.ToFloat(thd,  MeteringProtocol.Gimac_VoltThdB),
                VoltThdC      = MeteringProtocol.ToFloat(thd,  MeteringProtocol.Gimac_VoltThdC),
                CurrThdA      = MeteringProtocol.ToFloat(thd,  MeteringProtocol.Gimac_CurrThdA),
                CurrThdB      = MeteringProtocol.ToFloat(thd,  MeteringProtocol.Gimac_CurrThdB),
                CurrThdC      = MeteringProtocol.ToFloat(thd,  MeteringProtocol.Gimac_CurrThdC),
            };

            OnUi(() => GimacDataReceived?.Invoke(this, r));
        }

        // ── EOCR-iSEM2 + sPDM ────────────────────────────────────────────────
        // FC3 ReadHoldingRegisters; values are 16-bit with scale factors.
        // Five read blocks to cover scattered address ranges (see MeteringProtocol.cs).

        private void PollIsem(DeviceLink link)
        {
            byte uid = (byte)link.Record.UnitId;

            // Block 1: voltage, power, PF (6 registers, 166–171)
            ushort[] vp = link.Master.ReadHoldingRegisters(uid,
                MeteringProtocol.Isem_VoltPwrAddr, MeteringProtocol.Isem_VoltPwrCount);

            // Block 2: per-phase current MSB+LSB + ground (8 registers, 302–309)
            ushort[] curr = link.Master.ReadHoldingRegisters(uid,
                MeteringProtocol.Isem_CurrAddr, MeteringProtocol.Isem_CurrCount);

            // Block 3: average voltage, PF, active/reactive power (8 registers, 2010–2017)
            ushort[] avg = link.Master.ReadHoldingRegisters(uid,
                MeteringProtocol.Isem_AvgAddr, MeteringProtocol.Isem_AvgCount);

            // Block 4: current frequency + current THD (7 registers, 2198–2204)
            ushort[] freq = link.Master.ReadHoldingRegisters(uid,
                MeteringProtocol.Isem_FreqThdAddr, MeteringProtocol.Isem_FreqThdCount);

            // Block 5: voltage THD (4 registers, 4200–4203)
            ushort[] vthd = link.Master.ReadHoldingRegisters(uid,
                MeteringProtocol.Isem_VoltThdAddr, MeteringProtocol.Isem_VoltThdCount);

            var r = new IsemReading
            {
                Device    = link.Record,
                Timestamp = DateTime.Now,

                // Block 1 — voltage ×0.1 V, power ×0.1 kW/kVAr, PF ×0.01
                VoltL3L1      = vp[MeteringProtocol.Isem_VoltL3L1]    * 0.1,
                VoltL1L2      = vp[MeteringProtocol.Isem_VoltL1L2]    * 0.1,
                VoltL2L3      = vp[MeteringProtocol.Isem_VoltL2L3]    * 0.1,
                ActivePower   = vp[MeteringProtocol.Isem_ActivePow]   * 0.1,
                ReactivePower = vp[MeteringProtocol.Isem_ReactivePow] * 0.1,
                PowerFactor   = vp[MeteringProtocol.Isem_PowerFactor] * 0.01,

                // Block 2 — 32-bit pairs ×0.01 A (current) / ×1 mA (ground)
                CurrL1        = MeteringProtocol.ToUInt32Scaled(curr, 0, 0.01),
                CurrL2        = MeteringProtocol.ToUInt32Scaled(curr, 2, 0.01),
                CurrL3        = MeteringProtocol.ToUInt32Scaled(curr, 4, 0.01),
                GroundCurrent = MeteringProtocol.ToUInt32Scaled(curr, 6, 1.0),   // mA

                // Block 3 — average voltage ×1 V, avg PF ×0.01, avg P/Q ×0.1 kW/kVAr
                AvgVoltage     = avg[MeteringProtocol.Isem_AvgVoltL3L1]    * 1.0,
                // AvgPf intentionally not stored here; duplicates PowerFactor in block 1

                // Block 4 — frequency ×0.1 Hz, THD ratio ×0.0001 → percent ×100
                CurrentFrequency = freq[MeteringProtocol.Isem_CurrFreq] * 0.1,
                VoltageFrequency = freq[MeteringProtocol.Isem_VoltFreq] * 0.1,
                AvgCurrentThd    = MeteringProtocol.ThdToPercent(freq[MeteringProtocol.Isem_AvgCurrThd]),
                CurrThdL1        = MeteringProtocol.ThdToPercent(freq[MeteringProtocol.Isem_CurrThdL1]),
                CurrThdL2        = MeteringProtocol.ThdToPercent(freq[MeteringProtocol.Isem_CurrThdL2]),
                CurrThdL3        = MeteringProtocol.ThdToPercent(freq[MeteringProtocol.Isem_CurrThdL3]),

                // Block 5 — voltage THD ratio ×0.0001 → percent ×100
                AvgVoltageThd  = MeteringProtocol.ThdToPercent(vthd[MeteringProtocol.Isem_AvgVoltThd]),
                VoltThdL3L1    = MeteringProtocol.ThdToPercent(vthd[MeteringProtocol.Isem_VoltThdL3L1]),
                VoltThdL1L2    = MeteringProtocol.ThdToPercent(vthd[MeteringProtocol.Isem_VoltThdL1L2]),
                VoltThdL2L3    = MeteringProtocol.ThdToPercent(vthd[MeteringProtocol.Isem_VoltThdL2L3]),
            };

            OnUi(() => IsemDataReceived?.Invoke(this, r));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void CloseLink(DeviceLink link)
        {
            try { link.Cts?.Cancel(); } catch { }
            try { link.Tcp?.Close();  } catch { }
            link.Master    = null;
            link.Tcp       = null;
            link.Connected = false;
        }

        private void RaiseChanged(DeviceRecord device) =>
            OnUi(() => ConnectionChanged?.Invoke(this, device));

        private static void OnUi(Action a)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess()) a();
            else disp.BeginInvoke(a);
        }

        public event EventHandler<DeviceRecord> ConnectionChanged;
        public event EventHandler<GimacReading> GimacDataReceived;
        public event EventHandler<IsemReading>  IsemDataReceived;
    }
}
