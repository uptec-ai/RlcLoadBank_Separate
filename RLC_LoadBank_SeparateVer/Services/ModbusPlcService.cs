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
        public static bool UseDiscreteInputsForFeedback = false;

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

            // NModbus 마스터는 스레드 안전이 아님 — 폴링 읽기와 명령 쓰기가 같은
            // TCP 스트림을 동시에 쓰면 응답이 섞여 호출 스레드가 무한 대기한다.
            // IoLock으로 마스터 접근을 직렬화한다.
            public readonly object IoLock = new object();

            // 명령은 UI 스레드가 아닌 백그라운드에서, 판넬별 발행 순서를 보존하며
            // 실행한다 (C부하/EMG 시퀀스의 순서 보장). ChainLock은 체인 교체 원자화용.
            public readonly object ChainLock = new object();
            public Task WriteChain = Task.CompletedTask;
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
                // 유한 타임아웃 필수: 미설정(무한)이면 응답 유실 시 호출 스레드가
                // 영원히 블록된다 (과거 UI 멈춤의 원인 중 하나).
                tcp.ReceiveTimeout = 2000;
                tcp.SendTimeout    = 2000;
                pn.Tcp       = tcp;
                pn.Master    = _factory.CreateMaster(tcp);
                pn.Master.Transport.ReadTimeout  = 2000;
                pn.Master.Transport.WriteTimeout = 2000;
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
                        addrToTag = pn.DiAddrToTag;
                        lock (pn.IoLock) vals = pn.Master.ReadInputs(pn.UnitId, 0, pn.DiCount);
                    }
                    else
                    {
                        // Test server: ReadCoils (FC1) — only MC/C-sub range
                        addrToTag = pn.DoAddrToTag;
                        lock (pn.IoLock) vals = pn.Master.ReadCoils(pn.UnitId, 0, pn.DoCount);
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

            // UI 스레드에서 동기 소켓 I/O 금지 (modbus.md) — 판넬별 체인으로
            // 발행 순서를 보존하며 백그라운드에서 쓴다. IoLock이 폴링 읽기와의
            // 동시 접근(응답 섞임 → 무한 대기 → UI 멈춤)을 차단한다.
            lock (pn.ChainLock)
            {
                pn.WriteChain = pn.WriteChain.ContinueWith(_ =>
                {
                    try
                    {
                        var master = pn.Master;
                        if (master == null || !pn.Connected) return;
                        lock (pn.IoLock) master.WriteSingleCoil(pn.UnitId, addr, on);
                    }
                    catch { }

                    // PollLoop은 변화가 있을 때만 FeedbackReceived를 발생시킨다.
                    // 이미 원하는 값이면 코일이 바뀌지 않으므로 폴링에서 감지되지 않음.
                    // 이 경우 즉시 피드백을 발생시켜 UI 상태 동기화를 보장한다.
                    if (addr < pn.LastState.Length && pn.LastState[addr] == on)
                        RaiseFeedback(panelIndex, mcTag, on);
                }, TaskScheduler.Default);
            }
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
