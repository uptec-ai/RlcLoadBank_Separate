# Panel Configuration (Final) — RLC Load Bank

English summary of `CFM-RLC-PNL-2026-001_..._판넬구성_최종확정서_260605.docx`
(Rev.0 final, confirmed 2026-06-05). Records the final, confirmed panel build.
Korean `.docx` is the source of truth.

## Background
Built from existing stock resistors and reactors to realize single- and
three-phase loads, minimizing cost/rework while meeting required test loads.

## Confirmed build
### PNL-1 (single-phase individual control ★)
- **R load 105 kW** — R-N / S-N / T-N (single-phase). STEP: `0.83×2 + 1.67 +
  3.33 + 5×2 + 8.33 + 10 kW` per phase. Per-phase individual MC ON/OFF.
  MCs = 8 STEP × 3 phases = **24 EA**.
- **L load 105 kVAr** — same structure as R (single-phase individual). STEP:
  `0.83×2 + 1.67 + 3.33 + 5×2 + 8.33 + 10 kVAr` per phase. MCs = **24 EA**.
- **C load 100 kVAr** — 3-phase batch. STEP: `50 + 50 kVAr`. SCR-module ON/OFF
  + MC ON/OFF (direct) + MC ON/OFF (resistor-series). SCR = **2 EA**, MC = **4 EA**.

### PNL-2 (three-phase batch control ★)
- **R load 105 kW** — 3-phase batch. STEP: `2.5×2 + 5 + 10 + 15×2 + 25 + 30`
  (3 phases simultaneously). MCs = 8 STEP = **8 EA**.
- **L load 105 kVAr** — 3-phase batch, same STEP layout. MCs = **8 EA**.
- **C load 100 kVAr** — same as PNL-1 C load (SCR 2, MC 4).

### PNL-3 — identical to PNL-2.

## PNL-1 vs PNL-2/3 — key differences
| Item | PNL-1 | PNL-2, PNL-3 |
| --- | --- | --- |
| R control | single-phase individual (R-N/S-N/T-N each) | 3-phase batch |
| L control | single-phase individual | 3-phase batch |
| C control | 3-phase batch (same as 2/3) | 3-phase batch |
| R+L MC count | 8 STEP × 3φ × 2(R+L) = 48 EA | 8 STEP × 2(R+L) × 2 panels = 32 EA |
| C MC count | 2 STEP × 2 (resistor + bypass MC) = 4 EA | × 2 panels = 8 EA |
| C SCR count | 2 STEP = 2 EA | × 2 panels = 4 EA |

## Capacity summary
| | PNL-1 | PNL-2 | PNL-3 | 3-panel total |
| --- | --- | --- | --- | --- |
| R | 105 kW (1φ) | 105 kW (3φ) | 105 kW (3φ) | 315 kW |
| L | 105 kVAr (1φ) | 105 kVAr (3φ) | 105 kVAr (3φ) | 315 kVAr |
| C | 100 kVAr (3φ) | 100 kVAr (3φ) | 100 kVAr (3φ) | 300 kVAr |

## Notes
- C-load circuit detail is provided by UPTEC after circuit review.
- This confirmation must be reflected in ECR-RLC-2026-001 and all subsequent drawings.
- Implication for the HMI: PNL-1 needs **per-phase** R/L step controls
  (R-N/S-N/T-N), while PNL-2/3 need a single **3-phase** step control per step.
