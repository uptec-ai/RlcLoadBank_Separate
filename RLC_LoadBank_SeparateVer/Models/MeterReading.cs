using System;
using RLC_LoadBank_SeparateVer.Services;

namespace RLC_LoadBank_SeparateVer.Models
{
    /// <summary>
    /// Parsed snapshot from a GIMAC 1000 (EX) meter.
    /// Covers BUS IN / BUS OUT 1/2/3 metering points.
    /// All register reads use FC4 (ReadInputRegisters); values are 32-bit IEEE 754 float.
    /// </summary>
    public class GimacReading
    {
        public DeviceRecord Device    { get; set; }
        public DateTime     Timestamp { get; set; }

        // 3-phase average
        public float AvgVoltage    { get; set; }   // V
        public float AvgCurrent    { get; set; }   // A
        public float ActivePower   { get; set; }   // W (total)
        public float ReactivePower { get; set; }   // VAr (total)
        public float ApparentPower { get; set; }   // VA  (total)
        public float PowerFactor   { get; set; }   // F/R ±1.000
        public float Frequency     { get; set; }   // Hz

        // Per-phase voltage (phase-to-neutral)
        public float VoltA { get; set; }   // V
        public float VoltB { get; set; }
        public float VoltC { get; set; }

        // Line-to-line voltage
        public float VoltAB { get; set; }  // V
        public float VoltBC { get; set; }
        public float VoltCA { get; set; }

        // Per-phase current
        public float CurrA { get; set; }   // A
        public float CurrB { get; set; }
        public float CurrC { get; set; }

        // THD (EX model, FC4 address 90+)
        public float VoltThdA { get; set; }   // %
        public float VoltThdB { get; set; }
        public float VoltThdC { get; set; }
        public float CurrThdA { get; set; }   // %
        public float CurrThdB { get; set; }
        public float CurrThdC { get; set; }
    }

    /// <summary>
    /// Parsed snapshot from an EOCR-iSEM2 + sPDM line meter.
    /// Covers line #1–#10 metering points.
    /// Register reads use FC3 (ReadHoldingRegisters); values are 16-bit with scale factors.
    /// </summary>
    public class IsemReading
    {
        public DeviceRecord Device    { get; set; }
        public DateTime     Timestamp { get; set; }

        // Line-to-line voltage (Trip n-0 = live value), ×0.1 V
        public double VoltL3L1   { get; set; }   // V
        public double VoltL1L2   { get; set; }
        public double VoltL2L3   { get; set; }
        public double AvgVoltage { get; set; }   // V (average, ×1 V from 2010)

        // Per-phase current (32-bit MSB+LSB pair), ×0.01 A
        public double CurrL1 { get; set; }   // A
        public double CurrL2 { get; set; }
        public double CurrL3 { get; set; }
        public double GroundCurrent { get; set; }  // mA

        // Power (Trip n-0), ×0.1 kW / ×0.1 kVAr
        public double ActivePower   { get; set; }   // kW
        public double ReactivePower { get; set; }   // kVAr
        public double PowerFactor   { get; set; }   // ×0.01

        // Frequency (from 2198/2199), ×0.1 Hz
        public double CurrentFrequency { get; set; }  // Hz
        public double VoltageFrequency { get; set; }  // Hz

        // Current THD (from 2200–2203), ×0.0001 × 100 = %
        public double AvgCurrentThd { get; set; }  // %
        public double CurrThdL1     { get; set; }
        public double CurrThdL2     { get; set; }
        public double CurrThdL3     { get; set; }

        // Voltage THD (from 4200–4203), ×0.0001 × 100 = %
        public double AvgVoltageThd { get; set; }  // %
        public double VoltThdL3L1   { get; set; }
        public double VoltThdL1L2   { get; set; }
        public double VoltThdL2L3   { get; set; }
    }
}
