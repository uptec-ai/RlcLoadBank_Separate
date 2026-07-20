---
paths:
  - "**/Views/RlcStatusView.xaml"
  - "**/Views/RlcStatusView.xaml.cs"
  - "**/Views/Diagrams/*.xaml"
  - "**/Views/Diagrams/*.xaml.cs"
  - "**/ViewModels/RlcStatusViewModel.cs"
  - "**/ViewModels/PanelViewModel.cs"
  - "**/ViewModels/McViewModel.cs"
---

# View: RlcStatusView (main operational screen, "RLC 현황")

## Purpose
The primary operator screen: top status bar, the RLC diagram region, and four
bottom panels (manual op / auto op / trip-alarm / operation history).
Self-contained `UserControl` — sets its own `DataContext = new RlcStatusViewModel()`,
so it can be hosted directly in the shell's `NaviFrame`.

## Status
Implemented with **mock data + stub services**. Real Modbus/DB wiring is pending
behind `IPlcService` / `IAutoOperationService` (see `.claude/rules/modbus.md`).

## Structure
- `RlcStatusView.xaml` — top bar (PLC comm chips, 자동/수동 mode toggle, E-Stop/
  Alarm/Heartbeat chips, clock, "DB 기록" toggle, "⚙ 연결 설정" button), diagram
  card, 4 bottom cards.
- "DB 기록" toggle → `DbLoggingOn` → `ServiceHub.DbWriter.SaveFullLogging()`:
  ON = store everything, OFF = Critical only (alarms/sessions). Persisted as
  app.config `Db.FullLogging` (**default OFF**); disabled (greyed) when
  `RLC_DB_CONN` env var is unset (`DbAvailable`).
  See `.claude/docs/database-schema.md`.
- Diagram region hosts `Diagrams/RlcDiagramView` (integrated, connection-aware).
- Bottom lists use WPF `DataGrid` (alarms + history). Badges colored via
  `AlarmLevelToBrushConverter`.

## Diagram UserControls (3, per spec)
- `PanelDiagram1View` — **PLC1**, single-phase individual: R = 3 phase groups
  (R-N/S-N/T-N) × 8, L = 3 × 8, C = 2 stages. Bound to `Panel1` (PanelViewModel).
- `PanelDiagram3PhaseView` — **PLC2 & PLC3** (reused), three-phase batch:
  R 8, L 8, C 2. Bound to `Panel2` / `Panel3`.
- `RlcDiagramView` — integrated container: shows each panel block when connected,
  a placeholder when not, plus the static **PNL-M** block. Adapts to connection
  state (1 connected → 1 block + placeholders; 3 → all blocks). DataContext =
  `RlcStatusViewModel` (uses `Panel1/2/3`).

## ViewModels
- `RlcStatusViewModel` : `ViewModelBase` — Panels, Mode (Auto/Manual), top-bar
  flags, R/L/C auto targets, Alarms/History collections, all commands. Subscribes
  to `ServiceHub.Plc.FeedbackReceived` to apply `*_FB` to MC state.
- `PanelViewModel` — builds the MC set per control mode (single vs three-phase),
  exposes `RGroups`/`LGroups` (McGroupViewModel) and `CSteps`. `McToggleRequested`
  callback drives the manual confirm→command→feedback flow.
- `McViewModel` — one MC: `Tag`, `Label`, `State` (McState), `ToggleCommand`.

## Manual-mode MC flow (spec requirement)
Click MC → `McViewModel.ToggleCommand` → `PanelViewModel.McToggleRequested` →
`RlcStatusViewModel.OnMcToggle`: guards (manual mode + connected) → `DXMessageBox`
confirm → `State = CommWait` (orange) → `ServiceHub.Plc.WriteMcCommand` (stub
echoes) → `FeedbackReceived` sets `On`/`Off`. Reflect `*_FB`, not `*_CMD` (spec §5.3).

## Auto operation
R/L/C individual target setpoints (kW/kvar) bound to `RTarget/LTarget/CTarget`;
`StartAutoCommand` calls `ServiceHub.Auto.Start(AutoTargets)` (algorithm stubbed).

## DB logging (Phase 4)
- Every completed operation calls `LogOp()` → `ServiceHub.DbLog.LogOperation`
  (tb_operation_event): MC_ON/MC_OFF (single MC & C-stage), SEQ_ON/SEQ_OFF
  (bulk R/L/C), ALL_OFF (RESET/TERMINATE/protection), AUTO_COMPLETE (detail
  jsonb = targets/plan size), MCCB_*, MODE_CHANGE (LOC_REM_FB transition).
  Sequence results flow through `WrapSequenceAsync(logOp:)`.
- `AddAlarm(..., dbType:, panelNo:, instant:)` also writes tb_alarm_event
  (Critical — always stored); duration episodes are closed on the falling
  edge (protection tags in `OnProtectionFeedback`, C MC1/MC2/SCR in
  `OnFeedback`) via `AlarmCleared`.
- Before every `WriteMcCommand` burst set `ServiceHub.DbLog.McCommandContext`
  (MANUAL/AUTO/SYSTEM) — `McLoggingPlcService` stamps it on tb_mc_event rows.
  Capacity snapshot in LogOp for single-MC ops is taken at command time
  (FB lands ~0.5 s later); sequence ops are accurate.

## Gotchas
- **`Seg` ToggleButtons** (자동/수동, 개별/전력·역률, DB 기록) are
  `Focusable=False` and their commands go through `SetMode`/`SetAutoMode`,
  which re-raise `IsAuto/IsManual/IsIndividual/IsPowerPf` unconditionally:
  a ToggleButton flips its own IsChecked via SetCurrentValue (overriding the
  OneWay binding), and re-selecting the same mode produces no PropertyChanged,
  so without the forced raise the visual stays untoggled (the old
  "Enter after click clears the toggle" bug). Keep both when adding segs.
- The 자동운전 card also has MCCB ON/OFF buttons (same `MccbOn/OffCommand`
  as the manual card) so auto operation can be started without switching
  to manual mode.
- MC visuals come from `McStateToBrushConverter` (legend: ON green / OFF grey /
  COMM WAIT orange / TRIP & ALARM red). Style `McCircle` is in `App.xaml`.
- PLC1 renders **24** R + 24 L MCs (3 phase rows) per the precondition — richer
  than the simplified single-row mockup, so each individual MC is clickable.
- The diagram region is wrapped in a `ScrollViewer` (PLC1 column is tall).

## Related docs
`.claude/docs/panel-config.md`, `.claude/docs/io-point-list.md`, `.claude/docs/system-spec.md` (§8 C-load, §5 interlocks).
