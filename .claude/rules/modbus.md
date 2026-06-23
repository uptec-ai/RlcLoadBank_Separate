---
paths:
  - "**/*Modbus*.cs"
  - "**/*Plc*.cs"
  - "**/Communication/**/*.cs"
  - "**/Services/**/*.cs"
  - "**/Models/**/*.cs"
---

# Modbus TCP communication (NModbus 3.0.83)

Loads only while editing communication / PLC / service / model code.

## Topology
- **3 independent PLCs** — one per load panel (PNL-1, PNL-2, PNL-3). Each needs
  its own TCP connection (host:port, default Modbus port 502). PNL-M has **no
  PLC** — its Trip/relay state is wired into a panel's DI when available.
- PNL-1 = single-phase individual (more points); PNL-2/3 = 3-phase batch.
  Totals: **DI 149 / DO 119**. Full map: `.claude/docs/io-point-list.md`.

## NModbus usage pattern
```csharp
using NModbus;
var tcp = new System.Net.Sockets.TcpClient(host, port);
var factory = new ModbusFactory();
IModbusMaster master = factory.CreateMaster(tcp);

// DI feedback (*_FB)  -> discrete inputs / input registers
bool[] fb = master.ReadInputs(slaveId, startAddress, numberOfPoints);
// DO command (*_CMD)  -> coils
master.WriteSingleCoil(slaveId, coilAddress, value);
master.WriteMultipleCoils(slaveId, startAddress, values);
```
- Map each PLC **I/O address `{P}-{module}.{channel}`** and **Tag** to a Modbus
  register/coil. Keep that mapping in one place (a config/table), driven from
  `.claude/docs/io-point-list.md`; do not scatter magic addresses.

## Runtime rules
- Poll feedback on a **background loop**; marshal updates to the UI thread
  (`Dispatcher` / VM property set). Never block the UI thread on socket I/O.
- Treat read/write timeouts and disconnects as recoverable: log (NLog),
  reconnect with backoff, and flag the panel as "comms lost" in the UI.
- **Interlocks live in the PLC**, but the HMI must still gate commands: respect
  Local/Remote, EMG-STOP, protection Fault, MCCB-Trip before issuing `*_CMD`.
- C-load is sequenced: resistor-path MC → SCR gating → direct MC (delay) →
  open resistor-path MC. Enforce the interlocks in `.claude/docs/system-spec.md` §8
  and never bypass the `*_FB` confirmation between steps.

## Tag naming (from the IO list)
`{P1|P2|P3}_{R|L|C}_{RN|SN|TN?}_{01..08 | C1|C2}_{FB|CMD}` plus protection/status
tags (`*_OVR_FB`, `*_OCR_FB`, `*_HT_FB`, `*_EMG_FB`, `*_MCCB_*`, `*_LOC_REM_FB`).
