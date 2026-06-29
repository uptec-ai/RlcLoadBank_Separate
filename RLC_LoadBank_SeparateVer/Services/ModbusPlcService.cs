using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NModbus;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Real Modbus TCP PLC link (NModbus 3.0.83).
    /// Address maps are built from <see cref="PlcProtocol"/> (SPEC-RLC-2026-001).
    ///
    /// Feedback polling:
    ///   UseDiscreteInputsForFeedback = false (default) → ReadCoils FC1 (test server)
    ///   UseDiscreteInputsForFeedback = true            → ReadInputs FC2 (real PLC)
    /// </summary>
    public class ModbusPlcService : IPlcService
    {
        /// <summary>
        /// false = ReadCoils (FC1) — test Modbus server that echoes coil writes.
        /// true  = ReadInputs (FC2) — production PLC (DI / DO are separate address spaces).
        /// </summary>
        public static bool UseDiscreteInputsForFeedback = true;

        private class PanelState
        {
            public string Host;
            public int    Port             = 502;
            public byte   UnitId           = 1;
            public int    PollMs           = 500;
            public int    ConnectTimeoutMs = 1500;
            public TcpClient     Tcp;
            public IModbusMaster Master;
            public CancellationTokenSource Cts;
            public bool   Connected;

            // DI reverse map (production mode: ReadInputs)
            public string[] DiAddrToTag;   // index = DI addr → base tag (null = spare/unused)
            public ushort   DiCount;

            // DO map (command writing)
            public Dictionary<string, ushort> DoTagToAddr;  // base tag → coil addr

            // Coil reverse map (test mode: ReadCoils — mirrors DO addr space)
            public string[] DoAddrToTag;   // index = coil addr → base tag (MC/C-sub only)
            public ushort   DoCount;

            // Last polled state (shared between DI and coil polling)
            public bool[] LastState;
        }

        private readonly PanelState[] _panels;
        private readonly ModbusFactory _factory = new ModbusFactory();

        public ModbusPlcService()
        {
            var cfgPlcs = DeviceConfigService.Load()
                          .Where(d => d.Type == DeviceType.PLC)
                          .ToList();

            _panels = new PanelState[3];
            for (int i = 0; i < 3; i++)
            {
                if (i < cfgPlcs.Count)
                {
                    var d = cfgPlcs[i];
                    _panels[i] = MakePanel(i, d.Ip, d.Port, (byte)d.UnitId, d.PollInterval, d.Timeout);
                }
                else
                {
                    _panels[i] = MakePanel(i, $"192.168.10.{11 + i}", 502, 1, 500, 1500);
                }
            }
        }

        private static PanelState MakePanel(int index, string host, int port, byte unitId,
                                            int pollMs, int connectTimeoutMs)
        {
            var pts = PlcProtocol.ForPanel(index);

            // DI map (production feedback)
            var diPts    = pts.Where(p => p.DiAddr.HasValue).ToList();
            int diMax    = diPts.Count > 0 ? diPts.Max(p => (int)p.DiAddr.Value) : 0;
            var diToTag  = new string[diMax + 1];
            foreach (var pt in diPts) diToTag[pt.DiAddr.Value] = pt.Tag;

            // DO map
            var doPts      = pts.Where(p => p.DoAddr.HasValue).ToList();
            var doTagToAddr = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            foreach (var pt in doPts) doTagToAddr[pt.Tag] = pt.DoAddr.Value;

            // Coil reverse map (test server: R/L MC only — DO addr == DI addr for these)
            // C부하 CMD(CCmdDo)는 포함 안 함 — C_RESULT가 별도 DI로 오기 때문
            int doMax    = doPts.Count > 0 ? doPts.Max(p => (int)p.DoAddr.Value) : 0;
            var doToTag  = new string[doMax + 1];
            foreach (var pt in doPts.Where(p => p.Kind == IoKind.McLoad))
                doToTag[pt.DoAddr.Value] = pt.Tag;

            // Allocate LastState for the larger of the two ranges
            int stateLen = Math.Max(diMax + 1, doMax + 1);

            return new PanelState
            {
                Host             = host,
                Port             = port,
                UnitId           = unitId,
                PollMs           = pollMs,
                ConnectTimeoutMs = connectTimeoutMs,
                DiAddrToTag      = diToTag,
                DiCount          = (ushort)(diMax + 1),
                DoTagToAddr      = doTagToAddr,
                DoAddrToTag      = doToTag,
                DoCount          = (ushort)(doMax + 1),
                LastState        = new bool[stateLen],
            };
        }

        public bool IsConnected(int panelIndex) =>
            panelIndex >= 0 && panelIndex < _panels.Length && _panels[panelIndex].Connected;

        public void Connect(int panelIndex)
        {
            if (panelIndex < 0 || panelIndex >= _panels.Length) return;
            var pn = _panels[panelIndex];
            if (pn.Connected) return;
            Task.Run(() => OpenAsync(panelIndex, pn));
        }

        private async Task OpenAsync(int index, PanelState pn)
        {
            try
            {
                var tcp     = new TcpClient();
                var connect = tcp.ConnectAsync(pn.Host, pn.Port);
                if (await Task.WhenAny(connect, Task.Delay(pn.ConnectTimeoutMs)) != connect
                    || !tcp.Connected)
                {
                    tcp.Close();
                    RaiseConn(index);
                    return;
                }
                pn.Tcp       = tcp;
                pn.Master    = _factory.CreateMaster(tcp);
                pn.Cts       = new CancellationTokenSource();
                pn.Connected = true;
                RaiseConn(index);
                _ = Task.Run(() => PollLoop(index, pn, pn.Cts.Token));
            }
            catch { RaiseConn(index); }
        }

        private async Task PollLoop(int index, PanelState pn, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool[]   vals;
                    string[] addrToTag;

                    if (UseDiscreteInputsForFeedback)
                    {
                        // Production: ReadInputs (FC2) covers all DI including status tags
                        vals      = pn.Master.ReadInputs(pn.UnitId, 0, pn.DiCount);
                        addrToTag = pn.DiAddrToTag;
                    }
                    else
                    {
                        // Test server: ReadCoils (FC1) — only MC/C-sub range
                        vals      = pn.Master.ReadCoils(pn.UnitId, 0, pn.DoCount);
                        addrToTag = pn.DoAddrToTag;
                    }

                    for (int i = 0; i < vals.Length && i < pn.LastState.Length; i++)
                    {
                        if (vals[i] == pn.LastState[i]) continue;
                        pn.LastState[i] = vals[i];
                        var tag = i < addrToTag.Length ? addrToTag[i] : null;
                        if (tag != null) RaiseFeedback(index, tag, vals[i]);
                    }
                }
                catch
                {
                    pn.Connected = false;
                    RaiseConn(index);
                    return;
                }

                await Task.Delay(pn.PollMs, ct).ContinueWith(_ => { });
            }
        }

        public void Disconnect(int panelIndex)
        {
            if (panelIndex < 0 || panelIndex >= _panels.Length) return;
            var pn = _panels[panelIndex];
            try { pn.Cts?.Cancel(); } catch { }
            try { pn.Tcp?.Close();  } catch { }
            pn.Master = null; pn.Tcp = null; pn.Connected = false;
            RaiseConn(panelIndex);
        }

        public void WriteMcCommand(int panelIndex, string mcTag, bool on)
        {
            if (panelIndex < 0 || panelIndex >= _panels.Length) return;
            var pn = _panels[panelIndex];
            if (!pn.Connected || pn.Master == null) return;
            if (!pn.DoTagToAddr.TryGetValue(mcTag, out var addr)) return;
            try { pn.Master.WriteSingleCoil(pn.UnitId, addr, on); } catch { }

            // PollLoop은 변화가 있을 때만 FeedbackReceived를 발생시킨다.
            // 이미 원하는 값이면 코일이 바뀌지 않으므로 폴링에서 감지되지 않음.
            // 이 경우 즉시 피드백을 발생시켜 UI 상태 동기화를 보장한다.
            if (addr < pn.LastState.Length && pn.LastState[addr] == on)
                RaiseFeedback(panelIndex, mcTag, on);
        }

        public event EventHandler<int>        ConnectionChanged;
        public event EventHandler<McFeedback> FeedbackReceived;

        private void RaiseConn(int index) =>
            OnUi(() => ConnectionChanged?.Invoke(this, index));

        private void RaiseFeedback(int index, string tag, bool on) =>
            OnUi(() => FeedbackReceived?.Invoke(this,
                new McFeedback { PanelIndex = index, McTag = tag, On = on }));

        private static void OnUi(Action a)
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess()) a();
            else disp.BeginInvoke(a);
        }
    }
}
