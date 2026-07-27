---
paths:
  - "**/Views/MeteringView.xaml"
  - "**/Views/MeteringView.xaml.cs"
  - "**/ViewModels/MeteringViewModel.cs"
---

# View: MeteringView ("계측 현황" — SYSTEM STATUS)

## Purpose
Metering/trend screen: per-panel KPI cards, a detail data grid (GIMAC + ISEM
readings), and a SciChart delta-power trend chart across PNL-1/2/3. Hosted in
`NaviFrame` via `App.MeteringView` (SYSTEM STATUS button in `MainWindow`).
Self-contained `UserControl` — sets its own `DataContext = new MeteringViewModel()`.

## Status
Implemented with live device polling wiring (`ServiceHub.Devices` /
`ServiceHub.Plc`-adjacent metering services). All buffering/aggregation
lives in `MeteringHistoryService` (a `ServiceHub.MeteringHistory`
app-lifetime singleton), so trend data accumulates from device connect
even if this view is never opened. The VM is display-only: it binds the
service-owned series and refreshes KPI/grid. Buffers are in-memory (no
persistence) — see buffer capacities below.

## ViewModel
- `MeteringViewModel : ViewModelBase`
- Panel selector: `Panels` / `SelectedPanel` (drives which panel's KPIs/grid show)
- KPI bound properties: `KpiVoltage`, `KpiCurrent`, `KpiActivePower`,
  `KpiReactivePower`, `KpiPowerFactor`, `KpiFrequency`, `KpiVoltThd`
- Detail grid: `DataRows` (`MeterDataRow`: Category/Name/Value/Unit)
- Trend: `SelectPeriodCommand` (`"1m" | "1h" | "1day"`), `DeltaChartTitle`,
  `DeltaXAxisFormat`, `DeltaXRange`, `Pnl{1,2,3}DeltaSeries`
- Connection dots: `Pnl{1,2,3}GimacConnected` (from `ServiceHub.Devices`,
  `DeviceType.GIMAC`)

## Data & Modbus tags
- Devices: **GIMAC 1000** (V/I/P/PF/Hz/THD) at BUS IN/OUT, **EOCR-iSEM2 + sPDM**
  meter units (count config-driven) — see `.claude/docs/measurement-items.md`.
- GIMAC/ISEM readings arrive on the UI thread roughly every **500 ms** (device
  response time dependent) via `ServiceHub` device-received events.
- No `*_FB`/`*_CMD` control tags here — this is a read-only display screen.

## Buffering / aggregation
Lives in `Services/MeteringHistoryService.cs` (`ServiceHub.MeteringHistory`,
subscribed to `IMeteringService` events from app start — declared **after**
`Metering` in `ServiceHub` for static-init order). Four rolling buffers per
panel, aggregated by wall-clock time (not sample count): raw (300 samples ≈
2.5 min @500ms), 1-min (120 = 2h), 1-hr (48 = 2d), 1-day (14 = 2wk). The
delta trend chart keeps a fixed-size FIFO per period: 1m→60pts(1h),
1h→24pts(1d), 1day→7pts(1wk). Aggregation clocks anchor on the first sample
per panel. Completed 1-min windows are persisted to `tb_gimac_agg_1m` /
`tb_isem_agg_1m` (dashboard "DB 기록" toggle gates this), and on startup the
last 2 h of GIMAC aggregates are backfilled so the 1m delta chart survives
restarts — see `.claude/docs/database-schema.md`. The service also caches last GIMAC/ISEM readings so a late-opened
view fills KPI/grid instantly, exposes `GetLastDeltaTime(periodIdx)` for X
window placement, and raises `DeltaAppended(periodIdx, ts)` (UI thread) which
the VM uses to slide `DeltaXRange`.

## UI / DevExpress / SciChart
- 7 KPI cards (`UniformGrid`), a read-only virtualized `DataGrid`, and a
  `SciChartSurface` (`s:` = SciChart **v9** namespace) line chart with
  `RolloverModifier` + `ZoomPanModifier` + `MouseWheelZoomModifier` +
  double-click `ZoomExtentsModifier` to restore full range.
- X axis: `AutoRange="Never"` with `VisibleRange` two-way bound to
  `DeltaXRange` — the ViewModel slides this window as new deltas append
  (custom auto-scroll, not SciChart's built-in auto-range).
- Y axis: `AutoRange="Always"` — auto-scales to visible data.

## Gotchas / rules
- Period buttons use a **custom `ControlTemplate`** (`PeriodBtn` style) so the
  DevExpress lightweight theme doesn't override the selected/unselected colors.
- Panel legend color coding is fixed: PNL-1 = `#E04F4F` (red), PNL-2 =
  `#27A86A` (green), PNL-3 = `#4B7BF5` (blue) — keep consistent with any other
  multi-panel chart in the app.
- Do not confuse this screen's read-only metering data with the R/L/C load
  control flow in `RlcStatusView` — they are separate ViewModels/services.

## Related docs
`.claude/docs/measurement-items.md`, `.claude/docs/panel-config.md`,
`.claude/rules/scichart.md`.
