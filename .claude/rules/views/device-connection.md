---
paths:
  - "**/Views/DeviceConnectionView.xaml"
  - "**/Views/DeviceConnectionView.xaml.cs"
  - "**/Windows/DeviceConnectionWindow.xaml"
  - "**/Windows/DeviceConnectionWindow.xaml.cs"
  - "**/ViewModels/DeviceConnectionViewModel.cs"
  - "**/ViewModels/DeviceItemViewModel.cs"
  - "**/Services/IDeviceConnectionService.cs"
---

# View: DeviceConnectionView ("장비 연결 설정" popup)

## Purpose
Modbus device connection settings: top summary (PLC comm / metering / monitoring
server), per-type tabs (PLC / ISEM / GIMAC / Server), the device grid, and the
selected-device detail with a connection test. Opened as a modal dialog.

## Status
Implemented with **mock data + stub service** (`MockDeviceConnectionService`).
Real Modbus connect/test + persistence pending behind `IDeviceConnectionService`.

## Hosting (important)
`DeviceConnectionWindow` (DevExpress `ThemedWindow`) **owns the VM**: it news
`DeviceConnectionViewModel`, wires `vm.RequestClose += Close`, and sets
`DataContext`. `DeviceConnectionView` has **no** self-DataContext — it inherits
from the window. Opened from the dashboard via `RlcStatusViewModel.OpenConnection`
("⚙ 연결 설정" button) and also from `MainViewModel.OpenConnection`.

## Structure
- Top summary: PLC list (Name/IP/Port/UnitId/status + Disconnect), ISEM & GIMAC
  counts (total / connected / disconnected), Monitoring Server (IP/Port/Apply/
  Running/Clients — **placeholder, functionality off**).
- Tabs: 4 DevExpress `GridControl`s bound to `Plcs` / `Isems` / `Gimacs` /
  `Servers`, `SelectedItem` → `SelectedDevice`. Columns: Use(bool→auto checkbox),
  Type, 이름, IP, Port, Unit ID, Slave ID, Current Reg, Scale, Input Reg, Current,
  상태, Last Seen.
- Detail panel: `DataContext = SelectedDevice`; editable fields + 연결 테스트
  (binds `DataContext.TestConnectionCommand` via `RelativeSource UserControl`).
- Bottom: 선택 장비 연결 / 선택 장비 해제 / 전체 적용 ... 저장 / 닫기.

## ViewModels
- `DeviceConnectionViewModel` : `ViewModelBase` — 4 device collections, summary
  computed props, server placeholder props, all commands, `RequestClose` event.
- `DeviceItemViewModel` — one device row (Use/Type/Name/Ip/Port/UnitId/SlaveId/
  CurrentReg/Scale/InputReg/State/LastSeen…) + `ToRecord()`.

## Gotchas
- Don't use `dxe:CheckBoxEditSettings` for the Use column — that settings type is
  not in the referenced editors assembly; a plain bool `GridColumn` auto-renders a
  checkbox. (This caused the only build error during initial implementation.)
- Server tab is intentionally inert (추후 사용).

## Related docs
`.claude/rules/modbus.md`, `.claude/docs/io-point-list.md`, `.claude/docs/measurement-items.md`.
