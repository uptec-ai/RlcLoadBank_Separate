# Measurement Items — Metering Instruments

English summary of `RPT-LS-MTR-2026-001_..._측정항목_설명서.docx` (Rev.0,
2026-06-05). Defines what the metering instruments measure and the data scope.
Korean `.docx` + instrument vendor manuals are the source of truth for exact
ranges and Modbus/comm registers.

## Instruments
| Instrument | Model / vendor | Location | Role |
| --- | --- | --- | --- |
| Power meter | **GIMAC 1000 (EX)** / LS ELECTRIC | panel front; measures at **BUS IN, BUS OUT 1/2/3** | full power-quality metering |
| Overcurrent relay + meter | **EOCR-iSEM2** (relay) + **sPDM** (meter) / Schneider | relay inside panel, meter on front; measures **lines #1–#10** | overcurrent protection + per-line power quality / protection state |

## GIMAC 1000 — group ① (instantaneous)
Avg voltage, avg current, phase currents A/B/C, phase voltages A/B/C, line
voltages AB/BC/CA, power factor (−1…1), TOTAL active/reactive/apparent power
(W/Var/VA), frequency (0, 40–70.5 Hz), forward active/reactive energy (Wh/Varh),
apparent energy (VAh), phase load ratios A/B/C (%), reverse active/reactive
energy, per-phase active/reactive/apparent power (A/B/C), per-phase voltage &
current phase angles (0–359.9°), per-phase PF, per-phase fundamental PF,
per-phase voltage THD & current THD (%).

## GIMAC 1000 — group ② (demand / harmonics / TDD)
Per-phase + avg current demand, power demand, per-phase MAX current, MAX avg
current, MAX apparent/active/reactive power, per-phase MAX voltage THD & current
THD, MAX-demand per-phase/avg current, MAX-demand active power, per-phase
voltage & current **1st–31st harmonics**, per-phase current **TDD** (%).

## EOCR-iSEM2 + sPDM — group ① (per line #1–#10)
Active/reactive energy (kWh/kVArh), thermal-capacity level (% Trip Level),
max-current ratio (% OC), L1/L2/L3 current ratio (% Inom), max current
imbalance (% imb), voltage frequency, avg voltage, line voltages L3-L1/L1-L2/
L2-L3, max voltage imbalance, power factor + PF state + phase angle, last outage
duration (s), active/reactive power (W/Var), current frequency, avg/L1/L2/L3 RMS
current, ground current (mA), max RMS current, residual current (mA), fast
avg/L1/L2/L3/max RMS current, min voltage ratio (% Vnom), fast avg + line
voltages, operating time (h), CPU junction temp (°C), internal reference voltage,
12 VDC main supply, avg L1/L2/L3 current.

## EOCR-iSEM2 + sPDM — group ② (per line)
Avg ground current, avg max RMS current, avg line voltages, avg PF, avg
active/reactive power, per-phase + max current crest factor, current/voltage
fundamental frequency, avg + per-phase + max current THD, total/per-phase/max
current degradation, per-phase fundamental & total current, avg + per-line
voltage THD, per-line fundamental & total voltage.

## Meaning / use (§4)
- **Voltage** (phase/line, avg, min/max, imbalance, fundamental, THD): source
  health, phase imbalance, sag, distortion.
- **Current** (L1/L2/L3, avg, max RMS, ground, residual, imbalance, fundamental,
  total, THD): load distribution, per-phase deviation, distorted-load detection.
- **Power** (kW/kVar/kVA, kWh/kVarh, avg P/Q, PF, phase angle): real consumption,
  reactive burden, efficiency, load characterization.
- **Frequency** (voltage & current fundamental): power quality / source anomaly.
- **Quality/waveform** (V/I THD, crest factor, degradation): nonlinear-load and
  waveform-stress assessment.
- **Protection/fault** (OC ratio, phase-current ratio, thermal level, ground
  current, max-imbalance phase, relay Trip/Alarm, last outage duration): tracing
  protective-action causes.
- **Operation/diagnostics** (operating time, CPU temp, internal ref voltage):
  separate instrument faults from grid faults.

## Note for the HMI / trend view
These items feed the SciChart trend View(s). Group by source (GIMAC BUS IN/OUT,
EOCR lines #1–#10) and by unit (V / A / W / Var / Hz / %); use separate Y axes
per unit. See `.claude/rules/scichart.md`.
