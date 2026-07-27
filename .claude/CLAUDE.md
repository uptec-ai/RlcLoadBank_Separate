# RLC Load Bank — Project Instructions

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
- `RLC_LoadBank_SeparateVer/MainWindow.xaml` — DevExpress `ThemedWindow` shell: HamburgerMenu + `NaviFrame` (NavigationFrame). 3 nav buttons swap `NaviFrame.Content` between lazily-created, App-cached views: DASHBOARD → `App.HomeView` (an `RlcStatusView` instance), SYSTEM STATUS → `App.MeteringView`, HISTORY → `App.HistoryView`. There is no separate "MainView" — `RlcStatusView` serves as the dashboard.
- `.claude/docs/` — English domain reference (system spec, IO list, metering, panels) — on-demand
- `.claude/rules/` — path-scoped guidance; auto-loads **only** when matching files are opened
- `.claude/agents/` — subagent personas (protocol/ui/architecture/safety specialists, feature-worker, integration-reviewer); invoked by the `multi-task` workflow via `agentType`. Re-design them with the global `harness-team` skill.
- `.claude/hooks/` — `check-view-doc.ps1`: a `PostToolUse` hook (registered in `.claude/settings.json`) that nudges Claude to create the matching `.claude/rules/views/*.md` when a new View `.xaml` file is written without one.
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

## Skill usage
- Registered skills live in `.claude/skills/{name}/SKILL.md`.
- When the user's prompt matches a registered skill's purpose, **ask whether to
  use the skill before proceeding** — do not silently skip it.
- Comparison is based on each skill's `description` field (already loaded in
  system context); do not read the full SKILL.md on every prompt.
- Example: "PlcProtocol.cs와 Excel이 일치하는지 확인해줘" → propose `sync-protocol`.

## Worktree routing (relaxed 2026-07-15)
Five feature worktrees exist alongside this repo (table below). Route by the
size and parallelism of the change:

- **Edit directly in main** when ALL of these hold: (a) it's a small serial
  fix in one area (≈1–2 files, no new feature), (b) that area's worktree
  branch is fully merged into main — `Ahead 0` in
  `scripts/Get-WorktreeStatus.ps1` — and (c) no `multi-task` run is active.
  Rationale: with `Ahead 0`, the next sync-down is a pure fast-forward, so
  main-direct edits can never conflict with the worktree.
- **Use the worktree** (EnterWorktree → work → ExitWorktree keep) for
  feature-sized work in that area, and **always** during `multi-task`
  parallel runs.
- If the status script shows `Ahead > 0` for the area you want to touch,
  merge that branch up (or finish its work) **before** editing main directly.

| File area (glob) | Worktree folder (absolute path) | Branch |
|---|---|---|
| `**/PlcProtocol.cs`, `**/ModbusPlcService.cs`, `RLC_IO_Protocol_Map.xlsx` | `C:/Project/1. RLC/RLC-protocol` | `feature/protocol` |
| `**/RlcStatusView.xaml*`, `**/RlcStatusViewModel.cs`, `**/PanelDiagram*` | `C:/Project/1. RLC/RLC-rlcstatus` | `feature/rlc-status` |
| `**/MeteringView.xaml*`, `**/MeteringViewModel.cs` | `C:/Project/1. RLC/RLC-metering` | `feature/metering` |
| `**/OperationHistoryView.xaml*`, `**/OperationHistoryViewModel.cs` | `C:/Project/1. RLC/RLC-history` | `feature/history` |
| `**/AutoOperationService.cs` | `C:/Project/1. RLC/RLC-automode` | `feature/auto-mode` |

Before editing a matching file, call `EnterWorktree({ path: "<absolute path from the table above>" })`
to switch the session into that existing worktree (it's already registered in
`git worktree list`, so this attaches rather than creating a new one) — use the
**absolute path exactly as shown above**, not a relative one, since
`git worktree list` registers worktrees by absolute path and `EnterWorktree`
matches against that registry. Do **not** edit the copy under this repo root.
When done, call
`ExitWorktree({ action: "keep" })` — never `"remove"`, since these are
long-lived feature worktrees, not throwaway session worktrees. Files **not**
matching any row above (shared files — `App.xaml.cs`, `MainWindow.xaml.cs`,
`MockServices.cs`(ServiceHub), `*.csproj`, `db/schema.sql` — and harness
config under `.claude/**`) are always edited directly in this main repo
folder, since they have no dedicated worktree.

### Worktree rhythm (follow every time a worktree is used)
1. **Sync down** — automated: a PostToolUse hook on `EnterWorktree`
   (`.claude/hooks/sync-worktree.ps1`) fast-forwards the branch to main when
   it is behind with `Ahead 0` (risk-free). If the hook **warns** instead
   (diverged: `Ahead > 0` and behind), run `git merge main` in the worktree
   manually, resolve/build/verify, then continue.
2. **Work + verify** — build **and run** from that worktree's own bin/sln;
   don't defer functional checks to main.
3. **Commit** on the feature branch.
4. **Merge up** — merge the branch into main (`--no-ff`).
5. **Integration check** — if more than one branch landed together (or
   shared files changed), build + run once from main. Rebuild **both**
   Debug and Release so no stale binary gets executed later.
6. *(optional)* `git push`.

`scripts/Get-WorktreeStatus.ps1` prints every worktree's branch,
Ahead/Behind (feature branches vs main; the main row vs origin/main =
push state) and uncommitted-file count. Run it before a main-direct edit
and before starting a multi-task run.

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
- Metering: **GIMAC 1000** at BUS IN / BUS OUT 1·2·3, plus **EOCR-iSEM2 + sPDM** meter units (V / I / P / PF / Hz / THD / harmonics / protection). Device count/mapping is config-driven (`DeviceConfigService` / app.config), not a fixed line count.
