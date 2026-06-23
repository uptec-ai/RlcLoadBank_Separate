---
paths:
  - "**/Views/**/*.xaml"
  - "**/Views/**/*.xaml.cs"
  - "**/ViewModels/**/*.cs"
---

# MVVM + DevExpress (v24.2.14) conventions

Loads only while editing Views or ViewModels.

## ViewModel
- Derive from `DevExpress.Mvvm.ViewModelBase`.
- `PropertyChanged.Fody` is referenced — declare auto-properties and let Fody
  weave `INotifyPropertyChanged`. Do **not** hand-write `RaisePropertyChanged`
  unless a property needs custom logic.
- Commands: `DelegateCommand` / `AsyncCommand` from `DevExpress.Mvvm`
  (`using DevExpress.Mvvm;`). Pair a command with a `CanExecute` predicate when
  the action is gated by interlocks (EMG-STOP, Fault, MCCB-Trip, Local/Remote).
- Keep PLC/Modbus access behind a service injected into the VM — never open
  sockets from a View code-behind.

## View
- Each screen is a `UserControl` whose `DataContext` is its ViewModel
  (see `MainView.xaml` for the existing pattern: `<ViewModels:MainViewModel/>`).
- The shell window is `MainWindow` — a DevExpress `dx:ThemedWindow`. Host new
  screens inside it (navigation/region) rather than spawning extra windows.
- Lightweight themes are enabled in `App.xaml.cs`
  (`CompatibilitySettings.UseLightweightThemes = true`); use `ThemesLW` controls.
- Bind status indicators to the **`*_FB` feedback** properties, not the `*_CMD`
  command properties (spec §5.3).

## Indicators & alarms
- A control's ON/OFF lamp = the MC aux-contact DI feedback. When `CMD` and `FB`
  disagree, surface a mismatch alarm.
- Honor Local/Remote mode: in Local the HMI is read-only for that target.

## When adding a screen
Create the matching `.claude/rules/views/<name>.md` from
`.claude/templates/view-doc-template.md` (see root `CLAUDE.md` → Per-View docs).
