---
paths:
  - "**/Views/OperationHistoryView.xaml"
  - "**/Views/OperationHistoryView.xaml.cs"
  - "**/ViewModels/OperationHistoryViewModel.cs"
---

# View: OperationHistoryView ("운전 이력" — HISTORY)

## Purpose
Read-only operation history: structured grid over **`tb_operation_event`**
(time / panel / mode / operation / load / target / applied R-L-C capacity /
color-coded result badge), with manual refresh and a source label. Hosted in
`NaviFrame` via `App.HistoryView` (HISTORY button). Self-contained
`UserControl` — sets its own `DataContext = new OperationHistoryViewModel()`.

## Status
Implemented (Phase 5, 2026-07-15). Reads the DB; falls back to the
session-scoped in-memory history when `RLC_DB_CONN` is unset.

## ViewModel
- `OperationHistoryViewModel : ViewModelBase`
- `Rows` (`ObservableCollection<OperationRow>`) — display rows mapped from
  `OperationEventRecord` (DB) or `HistoryEntry` (fallback)
- `LoadAsync()`: `Task.Run(() => ServiceHub.DbLog.QueryOperations(500, from,
  to, panel))` → map on the UI thread. `IsLoading` gates re-entry;
  `RefreshCommand`/`SearchCommand` rerun with the current filters.
- **Search filters**: `PanelFilters` ComboBox (전체/PNL-1..3 → panelNo
  null/1/2/3 — panel filter also returns `panel_no IS NULL` common events),
  `FromDate`/`ToDate` DatePickers + optional `FromTime`/`ToTime` "HH:mm"
  TextBoxes (empty → 00:00 / 23:59:59; invalid text silently ignored),
  `QuickRangeCommand` ("1"/"7"/"30" = Today/Week/Month → sets last N days,
  clears times, searches). Fallback source filters client-side.
- `SourceText`: "PostgreSQL · tb_operation_event" or "In-Memory (세션 한정…)"
- Korean label maps live in `Map()`: op_type (MC_ON→"MC 투입" …) and mode
  (MANUAL→수동 / AUTO→자동 / SYSTEM→시스템). Unknown values pass through raw —
  new op_types added by producers appear untranslated until mapped here.

## Data
- Source of truth: `tb_operation_event` written by `RlcStatusViewModel.LogOp`
  (see `.claude/docs/database-schema.md` → Phase 4/5). No Modbus tags here.
- Timestamps arrive UTC from timestamptz and are converted to local in
  `DbLogService.QueryOperations`.

## UI
- Plain WPF `DataGrid`, 8 columns; result badge = `DataTemplate.Triggers`
  (성공 green / 실패 red / 중단 orange / else grey).
- `BooleanToVisibilityConverter` is declared locally in UserControl.Resources
  (not in App.xaml).

## Gotchas
- Full clear+reload per refresh, capped at 500 rows — no paging.
- Legacy `operation_history` table/repository removed in Phase 5; the
  in-memory `ServiceHub.History` remains only as the dashboard grid source
  and this view's no-DB fallback.

## Related docs
`.claude/docs/database-schema.md`, `.claude/rules/views/rlc-status.md`.
