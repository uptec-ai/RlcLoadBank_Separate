---
paths:
  - "**/Views/*Trend*.xaml"
  - "**/Views/*Trend*.xaml.cs"
  - "**/Views/*Chart*.xaml"
  - "**/Views/*Chart*.xaml.cs"
  - "**/ViewModels/*Trend*.cs"
  - "**/ViewModels/*Chart*.cs"
---

# SciChart 9.0.0.29196 (charts / trends)

Loads only while editing chart / trend Views or ViewModels. Use the **v9** API
(the spec text said "8.6" but the restored package is v9).

## Setup
- Set the SciChart runtime license **once at startup** (App constructor /
  `OnStartup`) via `SciChartSurface.SetRuntimeLicenseKeyFromLicensingWizard()`
  or `SciChart.Charting.Visuals.SciChartSurface.SetRuntimeLicenseKey(...)`.
  Without a key SciChart renders a watermark.
- Namespace: `SciChart.Charting.Visuals` (surface), `SciChart.Charting.Model.DataSeries`.

## Real-time trends
- Use a FIFO series for streaming live measurements:
  `new XyDataSeries<DateTime, double> { FifoCapacity = N }` and `Append(...)`
  on each poll; bind `RenderableSeries`. Update on the UI thread.
- Suspend redraws while bulk-appending: `using (surface.SuspendUpdates()) { ... }`.
- Plot the metering items defined in `.claude/docs/measurement-items.md`
  (voltage, current, power, frequency, PF, THD, harmonics). Group by source:
  GIMAC 1000 (BUS IN / OUT 1·2·3) and EOCR-iSEM2 + sPDM (lines #1–#10).

## Conventions
- Keep data series in the ViewModel; keep axes/annotations in XAML.
- One `XAxis` shared (time), multiple `YAxis` per unit (V / A / W / Hz / %) when
  mixing quantities — do not plot V and Hz on the same scale.
