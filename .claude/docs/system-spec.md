# System Specification — RLC Load Bank

English summary of `RLC부하장치_시스템사양서_Rev5.docx`
(SPEC-RLC-2026-001, Rev.5, 2026-06-11). Korean `.docx` is the source of truth.

## 1. Scope
Defines system configuration, electrical ratings, per-panel PLC I/O,
enclosure/installation, and operating-control policy for the RLC Load Bank.
Rev.5 added the **integrated output panel (PNL-M)** that parallels the 3 panel
outputs (via cable) into a single output.

## 2. Enclosure & installation
- Color per KERI (Korea Electrotechnology Research Institute) standard. Indoor use.
- Height incl. casters ≤ 1.8 m. Industrial casters (lockable) on the base frame;
  lifting lugs on the top frame (hoist-movable).
- Every panel has an **MCCB** (rated 250–300 A).
- **Two control-power feeds**, selected by operation:
  - Feed 1: **220 V** (external supply).
  - Feed 2: **380 V** (internal transformer).
  - **Interlock (anti-back-feed):** the two feeds must never be paralleled.
    Use a selector switch or mechanical/electrical interlock; energizing one
    feed must physically isolate the other.

## 3. System overview
- 3 load panels **PNL-1, PNL-2, PNL-3** + 1 integrated output panel **PNL-M**.
- Each load panel independently controls **R, L, C** loads with its own MCCB and
  PLC I/O. **PNL-1 = single-phase individual** control; **PNL-2/3 = three-phase
  batch** control.
- Outputs of PNL-1/2/3 are cable-paralleled and combined at **PNL-M** into one output.

### 3.1 Capacity
| | PNL-1 | PNL-2 | PNL-3 | Total |
| --- | --- | --- | --- | --- |
| R | 105 kW (1φ) | 105 kW (3φ) | 105 kW (3φ) | 315 kW |
| L | 105 kVAr (1φ) | 105 kVAr (3φ) | 105 kVAr (3φ) | 315 kVAr |
| C | 100 kVAr (3φ) | 100 kVAr (3φ) | 100 kVAr (3φ) | 300 kVAr |

### 3.2 C-load switching
C-load is energized by **SCR (thyristor)** as the main switch; the PLC controls
the **resistor-path MC** and the **direct MC** (not the SCR conduction itself).
Per STEP: 1 SCR, 1 resistor-path MC, 1 direct MC; 2 STEPs → SCR×2, MC×4 per panel.
C-load PLC I/O per panel: **DI 6 (MC 4 + SCR 2) / DO 6 (MC 4 + SCR 2)**.

### 3.3 Control-method comparison
| Item | PNL-1 | PNL-2 / PNL-3 |
| --- | --- | --- |
| R·L control | single-phase individual (R-N/S-N/T-N each) | three-phase batch |
| C switching | SCR + MC (3φ batch) | same |
| MCCB control | ON / OFF / TRIP CMD (DO×3) | same |
| LED handling | all H/W wired — no PLC DO | same |
| Control mode | Local / Remote selector | same |

### 3.4 PNL-M (integrated output panel)
Parallels PNL-1/2/3 outputs via cable into a single output. Has an integrated
**MCCB (with Trip)** + **protection relay** + a parallel **busbar** (rated to
the 3-panel parallel sum). Relay trip → MCCB trip → output disconnect.
**PNL-M has no PLC** — its trip/relay contacts are monitored via a panel's PLC DI.

## 4. Power indication & operating control
- Front lamps for 380 V / 220 V energized state and which control-power feed is
  in use. **All lamps are hard-wired (no PLC DO).** PLC reads state via DI only.
- MC ON/OFF LEDs and OVR/OCR LEDs are also hard-wired.
- **Local / Remote** selector: Local = on-site push buttons (separate On/Off per
  target); Remote = PLC/upper system.
- Local **and** Remote are both subject to: EMG-STOP, protection Fault,
  MCCB-Trip, and the common-stop circuit.

## 5. Circuits & interlocks
- **§5.1 Common-stop circuit** (acts regardless of Local/Remote): EMG-STOP →
  immediate full-load cut; protection Fault (OVR/OCR/HT) → cut affected circuit;
  MCCB-Trip → loads OFF; common-stop command → all loads stop.
- **§5.2 Control-power interlock**: 220 V vs 380 V feeds never paralleled
  (anti-back-feed); selected feed isolates the other; PLC DI monitors the
  in-use feed and alarms on anomaly.
- **§5.3 UI display basis**: HMI/upper-system MC status must be based on the
  **actual MC aux-contact feedback (DI)**, not on the CMD signal alone.
  Recommend alarming when DI feedback and CMD output disagree.
- **§5.4 PNL-M trip linkage**: PNL-M relay → PNL-M MCCB trip → output cut.
  Trip/relay contacts monitored via PLC DI (PNL-M has no PLC). Whether a PNL-M
  trip also trips PNL-1/2/3 (common-stop linkage) is TBD by agreement.

## 6–7. PLC I/O totals
| | PNL-1 | PNL-2 | PNL-3 | Total |
| --- | --- | --- | --- | --- |
| DI | 71 | 39 | 39 | 149 |
| DO | 61 | 29 | 29 | 119 |
| DI+DO | 132 | 68 | 68 | 268 |

PNL-1 carries more points because R·L use single-phase individual MCs
(8 STEP × 3 phases × 2 = 48 vs 8 × 2 = 16 for PNL-2/3). DI/DO breakdown per
panel is in [io-point-list.md](io-point-list.md). LEDs use **no** PLC DO.

## 8. C-load sequence
ON sequence (per STEP): ① resistor-path MC ON (inrush limiting) → ② SCR ON
(gating; capacitor energized) → ③ direct MC ON after delay (bypass resistor) →
④ resistor-path MC OFF (after direct-MC confirmed). Detailed timing is TBD.

**Interlocks:** do not close direct MC until resistor-path MC is confirmed; do
not open resistor-path MC until direct-MC feedback is confirmed; alarm on
sequence timeout; monitor for both MCs being abnormal at once.

C-load tags (per panel): `C1_R_MC_*`, `C1_DIR_MC_*`, `C1_SCR_*`,
`C2_R_MC_*`, `C2_DIR_MC_*`, `C2_SCR_*` (each `_CMD` + `_FB`).

## 9. Notable items (open/TBD)
I/O quantities are current design values; C-load circuit, resistor values, SCR
gating, and FAN-control method (PLC vs hardware) are subject to agreement.
PNL-M MCCB/relay ratings and busbar/cable sizing pending. If LEDs ever move to
PLC control, recompute DO counts.
