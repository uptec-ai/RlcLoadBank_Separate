---
paths:
  - "**/Views/MainView.xaml"
  - "**/Views/MainView.xaml.cs"
  - "**/ViewModels/MainViewModel.cs"
---

# View: MainView

## Purpose
Root content view of the application. Hosted directly inside `MainWindow`
(`dx:ThemedWindow`) via `<Views:MainView/>`. Intended to become the shell /
dashboard surface that hosts or navigates to the other screens.

## Status
Scaffold — the `UserControl` has an empty `<Grid>` and `MainViewModel` is empty
(`: ViewModelBase`, no members yet). No bindings, commands, or Modbus wiring.

## ViewModel
- `MainViewModel : DevExpress.Mvvm.ViewModelBase` — currently empty.
- DataContext is set in XAML: `<UserControl.DataContext><ViewModels:MainViewModel/></UserControl.DataContext>`.

## Data & Modbus tags
- None yet. As the dashboard, it will likely surface per-panel summary status
  (R/L/C ON-step counts, total kW/kVAr, comms state, active alarms) read from
  all three PLCs — see `.claude/rules/modbus.md` and `.claude/docs/io-point-list.md`.

## UI / DevExpress
- Lives in a DevExpress `ThemedWindow`; lightweight themes are enabled in
  `App.xaml.cs`. Window is sized 1920×1080.

## Gotchas / rules
- This is the only View today. When you split out screens (panel control,
  C-load sequence, metering, trend, alarm, comm/settings), give each its own
  View + `.claude/rules/views/<name>.md` from the template, and turn MainView
  into the host/navigation shell.
- Reflect status from `*_FB` feedback, not `*_CMD` (spec §5.3).

## Related docs
- `.claude/docs/system-spec.md` (system overview), `.claude/docs/panel-config.md`.
