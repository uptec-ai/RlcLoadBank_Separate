# Database schema (PostgreSQL) — finalized design v1

Canonical DDL: [`db/schema.sql`](../../db/schema.sql) (idempotent, safe to
re-run from an `EnsureSchema` step). Target DB is **`DB_RLC`** (already
created; schema applied 2026-07-15). The Npgsql connection string comes from
the **`RLC_DB_CONN` environment variable** — never hardcode it in code, the
DB, or the repo. If the variable is unset, all DB features stay disabled and
the HMI runs normally.

## Confirmed design decisions (2026-07-15)

1. **Status = transition events**, never periodic snapshots. Current state
   lives in memory (ServiceHub); DB stores history only. State at time T =
   last event row before T.
2. **Metering values = two tiers.**
   - **1-second raw** (`tb_gimac_raw`/`tb_isem_raw`, schema v3): full
     GimacReading/IsemReading snapshot every 1 s. `PARTITION BY RANGE (ts)`,
     monthly partitions, **no PK** (`(unit_id, ts)` index only), ~12-month
     retention by dropping expired partitions (`fn_maintain_raw_partitions`,
     run each startup via EnsureSchema). Written by `MeteringHistoryService`
     (Normal category — toggle-gated).
   - **1-minute aggregates** (avg/min/max) kept alongside for fast wide-range
     trend queries (reads ~1/60 the rows of raw) and >1yr history; `real`/
     `smallint` compact types, BRIN time index, 2-year retention (DELETE).
   Transient/fast events (inrush, sub-second transients) are the PLC's job,
   not captured in raw. Design decisions per user 2026-07-16.
3. **Device identity denormalized** per row: `device_type + unit_id +
   panel_no`. No device master table — records the panel mapping *as of
   write time* (panels can run 1/2/3-connected in any combination).
4. All table names carry the **`tb_` prefix**.
5. All timestamps are `timestamptz` (legacy `operation_history` used
   naive `timestamp` — see migration note at the bottom of schema.sql).

## Table map

| Table | Kind | Written when |
|---|---|---|
| `tb_schema_version` | meta | migration applied |
| `tb_app_session` | session | app start (insert) / clean exit (update `ended_ts`); `panels_used` array appended on connect |
| `tb_connection_event` | event | PLC/GIMAC/ISEM connect & disconnect |
| `tb_mc_event` | event | each confirmed MC state change; `confirmed=false` = CMD/FB mismatch (spec §5.3); `mode='LOCAL'` = field operation detected (FB change without CMD) |
| `tb_operation_event` | event | each completed operation — op_type: `MC_ON/MC_OFF`, `AUTO_STEP` (target vs measured capacity + duration in `detail` jsonb), `AUTO_COMPLETE`, `C_SEQ_STEP` (spec §8 trace), `MODE_CHANGE` (Local/Remote), `ALL_OFF`; carries applied R/L/C capacity snapshot |
| `tb_alarm_event` | event | alarm episode (raise inserts, clear updates `cleared_ts`); types incl. EMG, MCCB_TRIP, OVR/OCR/HT, DOOR, CMD_FB_MISMATCH, COMM_LOST |
| `tb_gimac_agg_1m` | timeseries | 1/min per connected GIMAC, PK `(unit_id, ts)` |
| `tb_isem_agg_1m` | timeseries | 1/min per connected ISEM, PK `(unit_id, ts)` |

Volume: aggregates ≈ 19k rows/day at full 13-device config (~7M rows/yr,
<1 GB with indexes) — no partitioning needed unless raw tables are enabled.

## Logging categories & dashboard toggle (Phase 1, confirmed)

Every write carries a `DbLogCategory`:

- **Critical** — always written while DB is enabled: `tb_alarm_event`
  (risk-relevant) and `tb_app_session` (FK anchor for alarms).
- **Normal** — written **only when the dashboard DB toggle is ON**:
  connection/MC/operation events and the 1-min aggregates.

The toggle lives on the dashboard (`RlcStatusView`), binds to
`ServiceHub.DbWriter.FullLogging`, persists as app.config key
`Db.FullLogging` (**default OFF** since 2026-07-15 — only Critical writes
until the operator turns it on), and drops Normal writes at enqueue time.

## Write-path rules (implemented in `Services/DbWriterService.cs`)

- **Never open connections / insert on the UI thread or inside the 500 ms
  polling handlers.** `DbWriterService` (ServiceHub singleton) owns a
  bounded `Channel<DbWork>` queue → background batch insert (coalesce
  ~300 ms, up to 200 rows/transaction), 3 retries then drop-and-log (NLog)
  — DB down must never affect HMI operation. It runs `EnsureSchema`
  (embedded `db/schema.sql`) before consuming, retrying every 60 s while
  the DB is unreachable. `App.OnExit` calls `Shutdown()` to drain the queue.
- **Phase 3 (implemented):** `MeteringHistoryService` accumulates per-minute
  stats (all channels, min/max for kW, max for ISEM ground current) and
  inserts `tb_gimac_agg_1m` / `tb_isem_agg_1m` rows (Normal category,
  `ON CONFLICT DO NOTHING`) at each completed 1-min window. GIMAC units 1–3
  only (unit 4 = BUS is not routed through the panel handlers). `samples`
  is always ≥ 1 on real rows — test tooling may use `samples = 0` as a
  marker for fake rows.
- **Backfill on startup (implemented):** a background task reads the last
  2 h of `tb_gimac_agg_1m` (kw_avg, units 1–3, converted UTC→local for the
  chart axis) and rebuilds the 1m buffer + delta series on the UI thread,
  so the 1m delta chart survives app restarts. Panels with live data
  already flowing are skipped; 1h/1day buffers are not backfilled (they
  refill from live collection). ISEM aggs are stored but not backfilled
  (no ISEM trend chart yet).
- Retention (implemented in `DbLogService.RunRetention`, app start):
  aggregates 2 yr. Raw tables (if ever enabled): 7 days. Events unlimited.
- Master gate = `RLC_DB_CONN` presence (`DbWriterService.Enabled`); per-write
  gate = category + `FullLogging` toggle. Legacy `ServiceHub.UseDatabase` /
  `PostgresHistoryRepository` (`operation_history`) stay untouched until
  Phase 5 unifies them onto `tb_operation_event`.
- **Timestamps: Npgsql 6+ rejects non-UTC values for `timestamptz`**
  (`DateTimeOffset` with offset ≠ 0 / `DateTime` Kind=Local throw
  ArgumentException — this silently dropped the first session batch until
  diagnosed). `DbWriterService.NormalizeArg` converts every timestamp arg
  to UTC at write time, so producers may pass `Now` or `UtcNow` safely.
- Session identity: the session-start INSERT (`EnqueueSessionStart`) runs
  `RETURNING id` and the writer caches it; producers pass
  `DbWriterService.SessionIdRef` as a placeholder arg, resolved to that id
  (or NULL before/without a session) at write time.

## Phase 4 producers (implemented)

- **tb_mc_event** — `Services/McLoggingPlcService.cs`, an `IPlcService`
  decorator that `ServiceHub.Plc` wraps around the real/mock service. It
  tracks every `WriteMcCommand` (pending: cmd_ts + `McCommandContext`
  captured at command time), confirms on the matching FB (`base` for R/L,
  `base_RESULT` for C, `base_FB` for MCCB), writes `confirmed=false` on 8 s
  timeout or supersede, and logs `mode='LOCAL'` for R/L FB changes without
  a pending command (first report after connect = baseline only).
  `_RESET` commands are excluded (no FB exists). Schema v2 added `SYSTEM`
  to the mode CHECK for protection auto-off commands.
- **tb_operation_event / tb_alarm_event** — `RlcStatusViewModel` (see
  `.claude/rules/views/rlc-status.md` → "DB logging"): `LogOp()` on every
  completed operation with applied R/L/C capacity snapshot; `AddAlarm`
  writes episodes (dedup on open ones), falling edges call `AlarmCleared`.
  op_type vocabulary in use: MC_ON, MC_OFF, SEQ_ON, SEQ_OFF, ALL_OFF,
  AUTO_COMPLETE, MCCB_ON/OFF/TRIP, MODE_CHANGE. (AUTO_STEP is reserved —
  current auto operation applies one plan, not discrete steps; C_SEQ_STEP
  is reserved — the C-load sequence moved into the PLC and the HMI only
  sees C{n}_CMD/RESULT.)

## Phase 2 producers (implemented — `Services/DbLogService.cs`)

`ServiceHub.DbLog` writes `tb_app_session` (start on ServiceHub init, end
via `App.OnExit`; abnormal exit leaves `ended_ts` NULL by design) and
`tb_connection_event` from PLC/GIMAC/ISEM `ConnectionChanged`, plus the
`panels_used` array append. Panel mapping mirrors
`MeteringViewModel.IsemBelongsToPanel` (`DbLogService.PanelOf`).
**Gotcha:** `ServiceHub.ResetPlcService()` swaps the `IPlcService` instance
and calls `DbLog.RewirePlc()` — any future producer subscribing to `Plc`
events needs the same rewire treatment.

## Phase 5 (implemented)

`OperationHistoryView` reads `tb_operation_event` via
`DbLogService.QueryOperations(max)` (background task, UTC→local), falling
back to the in-memory session history when `RLC_DB_CONN` is unset. The
legacy `operation_history` path (`PostgresHistoryRepository`,
`ServiceHub.UseDatabase`) was removed — the table never existed in DB_RLC,
so there was no data to migrate. `ServiceHub.History`
(`InMemoryHistoryRepository`) remains as the dashboard grid source and the
HISTORY-view fallback only.

## Related

`.claude/docs/system-spec.md` (§5.1 common-stop, §5.3 FB rule, §8 C-load
sequence), `.claude/rules/views/metering-view.md`,
`.claude/rules/views/operation-history-view.md`,
`.claude/rules/views/rlc-status.md` (Phase 4 producers).
