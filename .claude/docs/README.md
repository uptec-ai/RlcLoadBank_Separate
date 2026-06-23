# Domain reference (English) — RLC Load Bank

These are English distillations of the Korean source `.docx` specs in
`Document/`. Read **on demand** — they are not auto-loaded into the session.
The Korean `.docx` files remain the contractual source of truth; if anything
conflicts, the `.docx` wins.

| Doc | Source `.docx` | What it covers |
| --- | --- | --- |
| [system-spec.md](system-spec.md) | `RLC부하장치_시스템사양서_Rev5.docx` (SPEC-RLC-2026-001 Rev.5) | System overview, enclosure/power, interlocks, C-load sequence, per-panel I/O totals |
| [io-point-list.md](io-point-list.md) | `RLC_PLC_IO_Point_List.docx` | DI/DO point list, tag naming, address format, module counts |
| [measurement-items.md](measurement-items.md) | `RPT-LS-MTR-2026-001_...설명서.docx` | GIMAC 1000 & EOCR-iSEM2 + sPDM measurement items and their meaning |
| [panel-config.md](panel-config.md) | `CFM-RLC-PNL-2026-001_...최종확정서.docx` | Final confirmed panel build (STEP layout, MC/SCR counts per panel) |

Quick orientation: **3 load panels** (PNL-1 single-phase individual; PNL-2/3
three-phase batch) + **PNL-M** integrated output panel (no PLC). Per panel:
**R 105 kW, L 105 kVAr, C 100 kVAr**. Control via **Modbus TCP** to 3 PLCs.
