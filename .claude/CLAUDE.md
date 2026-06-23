# RLC Load Bank (선로모의장치 / RLC Load Bank) — Project Instructions

WPF HMI for an **RLC Load Bank**: it drives 3 load panels (PNL-1/2/3) plus an
integrated output panel (PNL-M), controls R/L/C loads over **Modbus TCP** to
per-panel PLCs, and displays metering/trends. Full domain reference lives in
`.claude/docs/` — read those **on demand**, they are not loaded every session.

## Tech stack (source of truth = the `.csproj`)
- IDE: **Visual Studio 2022** · Framework: **.NET 8.0** (`net8.0-windows`) · App: **WPF**
- UI library: **DevExpress v24.2.14** (`DevExpress.Wpf.Core`, `DevExpress.Wpf.ThemesLW`, `DevExpress.Images`)
- Chart library: **SciChart 9.0.0.29196** — note: the spec text said "8.6", but the restored package is v9, so **v9 is the source of truth**.
- Comms: **Modbus TCP** via **NModbus 3.0.83**
- MVVM helper: PropertyChanged.Fody 4.1.0 · Logging: NLog 6.1.3 · DB: Npgsql 10.0.3
- Shell: **PowerShell**

## Project root — portable, never hardcode paths
This repo may be cloned to **any folder** (it is shared via GitHub). Do **not**
hardcode `C:\Project\1. RLC\...`. Resolve the solution root at runtime by
locating `RLC_LoadBank_SeparateVer.sln`:

```powershell
$root = & ".\scripts\Resolve-ProjectRoot.ps1"        # from repo root
# or from anywhere:  & "<path>\scripts\Resolve-ProjectRoot.ps1" -StartPath (Get-Location)
```

The script walks up the directory tree until it finds the `*.sln`. If the
current working folder does not sit under that solution, fix the root (re-run
the resolver) before building — do not assume the original absolute path.

## Build / run (PowerShell)
```powershell
$root = & ".\scripts\Resolve-ProjectRoot.ps1"
dotnet build (Join-Path $root 'RLC_LoadBank_SeparateVer.sln')
dotnet run --project (Join-Path $root 'RLC_LoadBank_SeparateVer\RLC_LoadBank_SeparateVer.csproj')
```
In VS 2022: open `RLC_LoadBank_SeparateVer.sln` and press F5.

## Layout
- `RLC_LoadBank_SeparateVer/Views/` — XAML `UserControl`s, one View per screen
- `RLC_LoadBank_SeparateVer/ViewModels/` — VMs (DevExpress `ViewModelBase`)
- `RLC_LoadBank_SeparateVer/MainWindow.xaml` — DevExpress `ThemedWindow` shell; currently hosts `MainView`
- `.claude/docs/` — English domain reference (system spec, IO list, metering, panels) — on-demand
- `.claude/rules/` — path-scoped guidance; auto-loads **only** when matching files are opened
- `scripts/` — PowerShell helpers (root resolver, etc.)

## Conventions
- MVVM: every screen is a `UserControl` View + a `ViewModel` deriving from DevExpress `ViewModelBase`; use Fody for `INotifyPropertyChanged`.
- App startup already enables DevExpress lightweight themes (`CompatibilitySettings.UseLightweightThemes = true` in `App.xaml.cs`).
- **UI state reflects the actual MC aux-contact DI feedback (`*_FB`), not just the CMD output** (spec §5.3). Raise an alarm when `CMD` and `FB` disagree.
- Modbus / MVVM-DevExpress / SciChart specifics live in `.claude/rules/*.md` and load only when relevant files are touched.

## Per-View docs (token-minimized)
There is **one `.md` per View** under `.claude/rules/views/`, scoped with a
`paths:` front-matter block so that **only the active View's doc loads** while
you edit that View. When you create a new View `FooView`:
1. Copy `.claude/templates/view-doc-template.md` → `.claude/rules/views/foo-view.md`.
2. Set its `paths:` to that View's files (`**/Views/FooView.xaml*`, `**/ViewModels/FooViewModel.cs`).
3. Fill in purpose, bound data, Modbus tags, measurement items, and gotchas.
When editing an existing View, you only need that View's `.md` — do not read the others.

## Boundaries
- **Stay inside this workspace.** If a change must touch files outside this
  folder (e.g. `~/.claude/`), **ask first**.
- All docs/comments under `.claude/**` are written in **English**.
- Never edit `bin/`, `obj/`, or `.vs/` — they are build artifacts.

## Domain quick-facts (details in `.claude/docs/`)
- PNL-1 = **single-phase individual** control (R-N / S-N / T-N each); PNL-2/3 = **3-phase batch**. Per panel: **R 105 kW, L 105 kVAr, C 100 kVAr**.
- C-load = **SCR + resistor-path MC + direct MC**, 2 stages, with a strict ON/OFF sequence and interlocks (spec §8).
- PLC I/O totals: **DI 149 / DO 119** across 3 panels. Tag format `{Pn}_{load}_{phase?}_{step}_{FB|CMD}`; address `{P}-{module}.{channel}`.
- Common-stop signals (EMG-STOP / protection Fault / MCCB-Trip) act on **both Local and Remote** (spec §5.1).
- Metering: **GIMAC 1000** at BUS IN / BUS OUT 1·2·3, **EOCR-iSEM2 + sPDM** on lines #1–#10 (V / I / P / PF / Hz / THD / harmonics / protection).
