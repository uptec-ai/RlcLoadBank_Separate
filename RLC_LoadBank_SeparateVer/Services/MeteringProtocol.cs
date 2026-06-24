using System;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Modbus register map constants and parse helpers for GIMAC 1000 and EOCR-iSEM2.
    /// Sources: [GIMAC1000] Modbus Map_KO_V44_202402-1.pdf  / EOCR-iSEM2_RegisterMap_V1.4.0 (CS Version).xlsx
    /// </summary>
    internal static class MeteringProtocol
    {
        // ── GIMAC 1000 (LS ELECTRIC) ─────────────────────────────────────────
        // Function code: FC4 ReadInputRegisters
        // All measurement values are 32-bit IEEE 754 float (big-endian word order)
        // NModbus wire address = Modbus 5-digit reference − 30001
        //   e.g. Modbus 30001 → NModbus address 0
        //        Modbus 30091 → NModbus address 90

        // Block A: main measurements — FC4, start 0, count 32 (covers Modbus 30001–30032)
        public const ushort Gimac_MainAddr  = 0;
        public const ushort Gimac_MainCount = 32;

        // Offsets within main block (each float value occupies 2 consecutive registers)
        public const int Gimac_AvgVoltage    = 0;    // 30001  V
        public const int Gimac_AvgCurrent    = 2;    // 30003  A
        public const int Gimac_CurrA         = 4;    // 30005  A
        public const int Gimac_CurrB         = 6;    // 30007  A
        public const int Gimac_CurrC         = 8;    // 30009  A
        public const int Gimac_VoltA         = 10;   // 30011  V
        public const int Gimac_VoltB         = 12;   // 30013  V
        public const int Gimac_VoltC         = 14;   // 30015  V
        public const int Gimac_VoltAB        = 16;   // 30017  V
        public const int Gimac_VoltBC        = 18;   // 30019  V
        public const int Gimac_VoltCA        = 20;   // 30021  V
        public const int Gimac_PowerFactor   = 22;   // 30023  F/R
        public const int Gimac_ActivePower   = 24;   // 30025  W
        public const int Gimac_ReactivePower = 26;   // 30027  VAr
        public const int Gimac_ApparentPower = 28;   // 30029  VA
        public const int Gimac_Frequency     = 30;   // 30031  Hz

        // Block B: THD (EX model) — FC4, start 90, count 12 (Modbus 30091–30102)
        public const ushort Gimac_ThdAddr  = 90;
        public const ushort Gimac_ThdCount = 12;

        // Offsets within THD block
        public const int Gimac_VoltThdA = 0;   // 30091  %
        public const int Gimac_VoltThdB = 2;   // 30093  %
        public const int Gimac_VoltThdC = 4;   // 30095  %
        public const int Gimac_CurrThdA = 6;   // 30097  %
        public const int Gimac_CurrThdB = 8;   // 30099  %
        public const int Gimac_CurrThdC = 10;  // 30101  %

        // ── EOCR-iSEM2 + sPDM (Schneider Electric) ──────────────────────────
        // Function code: FC3 ReadHoldingRegisters
        // NModbus wire address = register address as shown in register map (no offset)
        // "Trip n-0" in the register map = the current (live) measurement snapshot.

        // Block 1: Voltage, active power, reactive power, power factor
        // FC3, start 166, count 6 (covers 166–171)
        public const ushort Isem_VoltPwrAddr  = 166;
        public const ushort Isem_VoltPwrCount = 6;
        // Offsets within block 1:
        public const int Isem_VoltL3L1    = 0;   // 166  V ×0.1
        public const int Isem_VoltL1L2    = 1;   // 167  V ×0.1
        public const int Isem_VoltL2L3    = 2;   // 168  V ×0.1
        public const int Isem_ActivePow   = 3;   // 169  kW ×0.1
        public const int Isem_ReactivePow = 4;   // 170  kVAr ×0.1
        public const int Isem_PowerFactor = 5;   // 171  ×0.01

        // Block 2: Per-phase current (32-bit MSB+LSB pairs) + ground current
        // FC3, start 302, count 8 (covers 302–309)
        // Combined 32-bit value × 0.01 = A (current) or mA (ground)
        public const ushort Isem_CurrAddr  = 302;
        public const ushort Isem_CurrCount = 8;
        // Offsets: [0,1]=L1 MSB/LSB, [2,3]=L2 MSB/LSB, [4,5]=L3 MSB/LSB, [6,7]=Gnd MSB/LSB

        // Block 3: Average values
        // FC3, start 2010, count 8 (covers 2010–2017)
        public const ushort Isem_AvgAddr  = 2010;
        public const ushort Isem_AvgCount = 8;
        // Offsets within block 3:
        public const int Isem_AvgVoltL3L1   = 0;  // 2010  V ×1
        public const int Isem_AvgVoltL1L2   = 1;  // 2011  V ×1
        public const int Isem_AvgVoltL2L3   = 2;  // 2012  V ×1
        // 2013, 2014 are duplicates of 2010, 2012 (different averaging window)
        public const int Isem_AvgPf         = 5;  // 2015  ×0.01
        public const int Isem_AvgActivePow  = 6;  // 2016  kW ×0.1
        public const int Isem_AvgReactivePow= 7;  // 2017  kVAr ×0.1

        // Block 4: Frequency + current THD
        // FC3, start 2198, count 7 (covers 2198–2204)
        public const ushort Isem_FreqThdAddr  = 2198;
        public const ushort Isem_FreqThdCount = 7;
        // Offsets within block 4:
        public const int Isem_CurrFreq     = 0;  // 2198  Hz ×0.1
        public const int Isem_VoltFreq     = 1;  // 2199  Hz ×0.1
        public const int Isem_AvgCurrThd   = 2;  // 2200  ratio ×0.0001 → % ×100
        public const int Isem_CurrThdL1    = 3;  // 2201
        public const int Isem_CurrThdL2    = 4;  // 2202
        public const int Isem_CurrThdL3    = 5;  // 2203
        public const int Isem_MaxCurrThd   = 6;  // 2204

        // Block 5: Voltage THD
        // FC3, start 4200, count 4 (covers 4200–4203)
        public const ushort Isem_VoltThdAddr  = 4200;
        public const ushort Isem_VoltThdCount = 4;
        // Offsets within block 5:
        public const int Isem_AvgVoltThd   = 0;  // 4200  ratio ×0.0001 → % ×100
        public const int Isem_VoltThdL3L1  = 1;  // 4201
        public const int Isem_VoltThdL1L2  = 2;  // 4202
        public const int Isem_VoltThdL2L3  = 3;  // 4203

        // ── Parse helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Converts two consecutive ushort registers to IEEE 754 float.
        /// GIMAC uses Modbus big-endian word order: high word first, low word second.
        /// </summary>
        public static float ToFloat(ushort[] regs, int offset)
        {
            uint raw = ((uint)regs[offset] << 16) | regs[offset + 1];
            return BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        }

        /// <summary>
        /// Combines a 32-bit MSB+LSB register pair to an unsigned value, then applies scale.
        /// Used for EOCR-iSEM2 current registers (e.g. L1 current = (MSB<<16|LSB) × 0.01 A).
        /// </summary>
        public static double ToUInt32Scaled(ushort[] regs, int offset, double scale)
        {
            uint raw = ((uint)regs[offset] << 16) | regs[offset + 1];
            return raw * scale;
        }

        /// <summary>
        /// Converts an EOCR THD ratio register to percent.
        /// Scale in register map is ×0.0001 (ratio); multiply by 100 to get %.
        /// </summary>
        public static double ThdToPercent(ushort raw) => raw * 0.0001 * 100.0;
    }
}
