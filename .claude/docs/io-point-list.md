# PLC I/O Point List — RLC Load Bank

English summary of `RLC_PLC_IO_Point_List.docx` (linked to SPEC-RLC-2026-001).
DI/DO modules are **16 channels/module**. Korean `.docx` is the source of truth;
use it for the exhaustive point-by-point table.

## Module summary
| Panel / control | DI pts | DI mods | DO pts | DO mods | Note |
| --- | --- | --- | --- | --- | --- |
| PNL-1 (single-phase individual) | 71 | 5 | 61 | 4 | independent PLC |
| PNL-2 (three-phase batch) | 39 | 3 | 29 | 2 | independent PLC |
| PNL-3 (three-phase batch) | 39 | 3 | 29 | 2 | same as PNL-2 |
| **Total** | **149** | **11** | **119** | **8** | 19 modules |

Unused channels are marked **Spare**. FAN-control DO can be removed if hardware
FAN control is adopted (spec note ⑨). PNL-M has no PLC (excluded here).

## Tag naming
| Token | Meaning |
| --- | --- |
| `P1 / P2 / P3` | Panel (PNL-1/2/3) — each has an independent PLC |
| `R / L / C` | Load type (R / L / C) |
| `RN / SN / TN` | Phase — **PNL-1 single-phase only** (R-N / S-N / T-N) |
| `01 … 08` | STEP number (R·L loads, 8 STEPs) |
| `C1 / C2` | C-load STEP (Stage 1 / Stage 2) |
| `R_MC / DIR_MC` | resistor-path MC / direct MC |
| `SCR` | thyristor gating |
| `_FB / _CMD` | feedback (input, DI) / command (output, DO) |

**I/O address format:** `{panel}-{module}.{channel}` — e.g. `P1-DI01.00` =
PNL-1, DI module 1, channel 00. DO example `P2-DO02.04` = PNL-2, DO module 2, ch 04.

## PNL-1 — single-phase individual (DI 71 / DO 61)
- **DI:** R MC feedback 24 (8 STEP × 3 phases), L MC feedback 24, C MC feedback 4
  (2× resistor-path + 2× direct), C SCR feedback 2, protection/status 4
  (OVR + OCR + HT + FAN-FB), MCCB status 3 (ON/OFF/TRIP), EMG-STOP 1, Door 1,
  panel FAN feedback 3 (R/L/C), power state 2 (380 V/220 V), in-use control-power
  2 (380 V/220 V), Local/Remote 1.
- **DO:** R MC 24, L MC 24, C MC 4, C SCR 2, panel FAN 3, MCCB 3 (ON/OFF/TRIP),
  Reset 1. (No DO for LEDs — all hard-wired.)
- Example tags: `P1_R_RN_01_FB` (R load R-N STEP1 MC aux feedback),
  `P1_R_SN_01_FB`, … `P1_R_TN_08_FB`; commands mirror as `_CMD`.

## PNL-2 / PNL-3 — three-phase batch (DI 39 / DO 29 each)
R·L are batch (3φ together), so MC count drops: R MC 8, L MC 8 (vs 24 each).
Rest mirrors PNL-1 (C MC 4, C SCR 2, protection 4, MCCB 3, EMG 1, Door 1,
panel FAN 3, power/control-power 4, Local/Remote 1; DO: R 8, L 8, C MC 4,
C SCR 2, FAN 3, MCCB 3, Reset 1).
- Example DI: `P2_R_01_FB … P2_R_08_FB`, `P2_L_01_FB …`, `P2_C1_R_MC_FB`,
  `P2_C1_DIR_MC_FB`, `P2_C1_SCR_FB`, `P2_OVR_FB`, `P2_OCR_FB`, `P2_HT_FB`,
  `P2_FAN_FB`, `P2_MCCB_ON_FB/OFF_FB/TRIP_FB`, `P2_EMG_FB`, `P2_PWR_380_FB`,
  `P2_PWR_220_FB`, `P2_CTRL_380_FB`, `P2_CTRL_220_FB`, `P2_LOC_REM_FB`.
- Example DO: `P2_R_01_CMD … 08`, `P2_L_01_CMD … 08`, `P2_C1_R_MC_CMD`,
  `P2_C1_DIR_MC_CMD`, `P2_C2_R_MC_CMD`, `P2_C2_DIR_MC_CMD`, `P2_C1_SCR_CMD`,
  `P2_C2_SCR_CMD`, `P2_FAN_R/L/C_CMD`, `P2_MCCB_ON/OFF/TRIP_CMD`, `P2_RESET_CMD`.
- `P2_LOC_REM_FB` selector: **0 = Local, 1 = Remote**.

## Implementation note
Keep the full `{address, tag, type}` map in **one** config/table in code,
generated from the `.docx`, and resolve Modbus coil/register numbers from it.
Do not scatter raw addresses across ViewModels. See `.claude/rules/modbus.md`.
