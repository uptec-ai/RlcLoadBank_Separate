using System.Collections.Generic;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Modbus I/O point definitions — SPEC-RLC-2026-001 기준.
    /// Address = (module - 1) × 16 + channel  (0-based).
    /// DI (*_FB) → ReadInputs (FC2) / production, ReadCoils (FC1) / test server.
    /// DO (*_CMD) → WriteSingleCoil (FC5).
    /// C부하 시퀀스는 PLC 내부 담당. HMI는 C_CMD 전송 + 피드백(RESULT/알람) 모니터링만.
    /// </summary>
    public enum IoKind
    {
        McLoad,   // R/L 부하 MC — DI(FB) + DO(CMD) 주소 동일
        StatusFb, // 보호·상태 DI 전용 (OVR, OCR, HT, EMG, MCCB_*_FB, LOC_REM 등)
        CmdDo,    // 명령 DO 전용 (MCCB_*_CMD, FAN_*_CMD, RESET_CMD)
        CResult,  // C부하 동작 결과 DI — T=동작, F=멈춤
        CAlarm,   // C부하 알람 DI — T=알람, F=정상 (MC1_FB, MC2_FB, SCR_FB)
        CCmdDo,   // C부하 투입/개방 명령 DO (C1_CMD, C2_CMD)
    }

    public class PlcIoPoint
    {
        public string  Tag;      // 기본 태그명 (MC/C부하: _FB/_CMD 없음; StatusFb: _FB 포함; CmdDo: _CMD 포함)
        public ushort? DiAddr;   // DI 주소 (CmdDo 포인트는 null)
        public ushort? DoAddr;   // DO 주소 (StatusFb 포인트는 null)
        public IoKind  Kind;
        public string  Desc;
    }

    public static class PlcProtocol
    {
        /// <summary>panelIndex 0=PNL-1, 1=PNL-2, 2=PNL-3</summary>
        public static IReadOnlyList<PlcIoPoint> ForPanel(int panelIndex)
            => panelIndex == 0 ? _pnl1 : BuildPnl23(panelIndex + 1);

        private static readonly IReadOnlyList<PlcIoPoint> _pnl1 = BuildPnl1();

        // ── PNL-1 (단상 개별) ─────────────────────────────────────────────────
        // DI 71점: addr  0–70  (DI01.00–DI05.06)
        // DO 61점: addr  0–60  (DO01.00–DO04.12)

        private static List<PlcIoPoint> BuildPnl1()
        {
            const int p = 1;
            var pts = new List<PlcIoPoint>();

            // R 부하 (각 상 × 8 STEP) ─ DI/DO 0–47
            AddSinglePhase(pts, p, "R", "RN",  0);   //  0– 7
            AddSinglePhase(pts, p, "R", "SN",  8);   //  8–15
            AddSinglePhase(pts, p, "R", "TN", 16);   // 16–23
            AddSinglePhase(pts, p, "L", "RN", 24);   // 24–31
            AddSinglePhase(pts, p, "L", "SN", 32);   // 32–39
            AddSinglePhase(pts, p, "L", "TN", 40);   // 40–47

            // C 부하 (DI04.00–07 = addr 48–55 / DO04.00, DO04.03 = addr 48, 51)
            // PLC 내부 시퀀스 담당 — HMI는 CMD 전송 + 피드백 모니터링만
            pts.Add(CRes(p, 1, 48, "C1 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 1, "MC1_FB", 49, "C1 MC1 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 1, "MC2_FB", 50, "C1 MC2 알람 (T=알람, F=정상)"));
            pts.Add(CRes(p, 2, 51, "C2 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 2, "MC1_FB", 52, "C2 MC1 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 2, "MC2_FB", 53, "C2 MC2 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 1, "SCR_FB", 54, "C1 SCR 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 2, "SCR_FB", 55, "C2 SCR 알람 (T=알람, F=정상)"));
            pts.Add(CCm (p, 1, 48, "C1 투입/개방 명령"));
            pts.Add(CCm (p, 2, 51, "C2 투입/개방 명령"));

            // 보호·상태 DI only (DI04.08–DI05.08 = addr 56–72, C부하 확장으로 +2 이동)
            pts.Add(SFb(p, "OVR_FB",       56, "과전압 계전기(OVR)"));
            pts.Add(SFb(p, "OCR_FB",       57, "과전류 계전기(OCR)"));
            pts.Add(SFb(p, "HT_FB",        58, "과열(HT) 검출"));
            pts.Add(SFb(p, "FAN_FB",       59, "FAN 종합 운전 상태"));
            pts.Add(SFb(p, "MCCB_ON_FB",   60, "MCCB ON 보조접점"));
            pts.Add(SFb(p, "MCCB_OFF_FB",  61, "MCCB OFF 보조접점"));
            pts.Add(SFb(p, "MCCB_TRIP_FB", 62, "MCCB TRIP 접점"));
            pts.Add(SFb(p, "EMG_FB",       63, "비상정지(EMG STOP)"));
            pts.Add(SFb(p, "DOOR_FB",      64, "Door interlock"));
            pts.Add(SFb(p, "FAN_R_FB",     65, "R부하 냉각팬"));
            pts.Add(SFb(p, "FAN_L_FB",     66, "L부하 냉각팬"));
            pts.Add(SFb(p, "FAN_C_FB",     67, "C부하 냉각팬"));
            pts.Add(SFb(p, "PWR_380_FB",   68, "380V 주전원 투입"));
            pts.Add(SFb(p, "PWR_220_FB",   69, "220V 제어전원 투입"));
            pts.Add(SFb(p, "CTRL_380_FB",  70, "제어전원 380V 선택"));
            pts.Add(SFb(p, "CTRL_220_FB",  71, "제어전원 220V 선택"));
            pts.Add(SFb(p, "LOC_REM_FB",   72, "Local/Remote 선택 (0=Local, 1=Remote)"));

            // 명령 DO only (DO04.08–14 = addr 56–62, C부하 확장으로 +2 이동)
            pts.Add(SDo(p, "FAN_R_CMD",    56, "R부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_L_CMD",    57, "L부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_C_CMD",    58, "C부하 냉각팬 기동"));
            pts.Add(SDo(p, "MCCB_ON_CMD",  59, "MCCB ON 명령"));
            pts.Add(SDo(p, "MCCB_OFF_CMD", 60, "MCCB OFF 명령"));
            pts.Add(SDo(p, "MCCB_TRIP_CMD",61, "MCCB TRIP 명령"));
            pts.Add(SDo(p, "RESET_CMD",    62, "Reset(고장 리셋) 명령"));

            return pts;
        }

        // ── PNL-2 / PNL-3 (3상 일괄, 동일 구성) ──────────────────────────────
        // DI 39점: addr  0–38  (DI01.00–DI03.06)
        // DO 29점: addr  0–28  (DO01.00–DO02.12)

        private static List<PlcIoPoint> BuildPnl23(int p)
        {
            var pts = new List<PlcIoPoint>();

            // R 부하 STEP1–8: addr 0–7
            for (int i = 1; i <= 8; i++)
                pts.Add(MC3(p, "R", i, (ushort)(i - 1)));
            // L 부하 STEP1–8: addr 8–15
            for (int i = 1; i <= 8; i++)
                pts.Add(MC3(p, "L", i, (ushort)(8 + i - 1)));

            // C 부하 (DI02.00–07 = addr 16–23 / DO02.00–01 = addr 16–17)
            // PLC 내부 시퀀스 담당 — HMI는 CMD 전송 + 피드백 모니터링만
            pts.Add(CRes(p, 1, 16, "C1 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 1, "MC1_FB", 17, "C1 MC1 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 1, "MC2_FB", 18, "C1 MC2 알람 (T=알람, F=정상)"));
            pts.Add(CRes(p, 2, 19, "C2 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 2, "MC1_FB", 20, "C2 MC1 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 2, "MC2_FB", 21, "C2 MC2 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 1, "SCR_FB", 22, "C1 SCR 알람 (T=알람, F=정상)"));
            pts.Add(CAlm(p, 2, "SCR_FB", 23, "C2 SCR 알람 (T=알람, F=정상)"));
            pts.Add(CCm (p, 1, 16, "C1 투입/개방 명령"));
            pts.Add(CCm (p, 2, 17, "C2 투입/개방 명령"));

            // 보호·상태 DI only (DI02.08–DI03.08 = addr 24–40, C부하 확장으로 +2 이동)
            pts.Add(SFb(p, "OVR_FB",       24, "과전압 계전기(OVR)"));
            pts.Add(SFb(p, "OCR_FB",       25, "과전류 계전기(OCR)"));
            pts.Add(SFb(p, "HT_FB",        26, "과열(HT) 검출"));
            pts.Add(SFb(p, "FAN_FB",       27, "FAN 종합 운전 상태"));
            pts.Add(SFb(p, "MCCB_ON_FB",   28, "MCCB ON 보조접점"));
            pts.Add(SFb(p, "MCCB_OFF_FB",  29, "MCCB OFF 보조접점"));
            pts.Add(SFb(p, "MCCB_TRIP_FB", 30, "MCCB TRIP 접점"));
            pts.Add(SFb(p, "EMG_FB",       31, "비상정지(EMG STOP)"));
            pts.Add(SFb(p, "DOOR_FB",      32, "Door interlock"));
            pts.Add(SFb(p, "FAN_R_FB",     33, "R부하 냉각팬"));
            pts.Add(SFb(p, "FAN_L_FB",     34, "L부하 냉각팬"));
            pts.Add(SFb(p, "FAN_C_FB",     35, "C부하 냉각팬"));
            pts.Add(SFb(p, "PWR_380_FB",   36, "380V 주전원 투입"));
            pts.Add(SFb(p, "PWR_220_FB",   37, "220V 제어전원 투입"));
            pts.Add(SFb(p, "CTRL_380_FB",  38, "제어전원 380V 선택"));
            pts.Add(SFb(p, "CTRL_220_FB",  39, "제어전원 220V 선택"));
            pts.Add(SFb(p, "LOC_REM_FB",   40, "Local/Remote 선택 (0=Local, 1=Remote)"));

            // 명령 DO only (DO02.08–14 = addr 24–30, C부하 확장으로 +2 이동)
            pts.Add(SDo(p, "FAN_R_CMD",    24, "R부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_L_CMD",    25, "L부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_C_CMD",    26, "C부하 냉각팬 기동"));
            pts.Add(SDo(p, "MCCB_ON_CMD",  27, "MCCB ON 명령"));
            pts.Add(SDo(p, "MCCB_OFF_CMD", 28, "MCCB OFF 명령"));
            pts.Add(SDo(p, "MCCB_TRIP_CMD",29, "MCCB TRIP 명령"));
            pts.Add(SDo(p, "RESET_CMD",    30, "Reset(고장 리셋) 명령"));

            return pts;
        }

        // ── 팩토리 헬퍼 ──────────────────────────────────────────────────────

        private static void AddSinglePhase(List<PlcIoPoint> pts, int p,
            string load, string phase, int baseAddr)
        {
            for (int i = 1; i <= 8; i++)
                pts.Add(new PlcIoPoint
                {
                    Tag    = $"P{p}_{load}_{phase}_{i:00}",
                    DiAddr = (ushort)(baseAddr + i - 1),
                    DoAddr = (ushort)(baseAddr + i - 1),
                    Kind   = IoKind.McLoad,
                    Desc   = $"{load}부하 {phase[0]}-N STEP{i} MC"
                });
        }

        private static PlcIoPoint MC3(int p, string load, int step, ushort addr) =>
            new PlcIoPoint
            {
                Tag    = $"P{p}_{load}_{step:00}",
                DiAddr = addr,
                DoAddr = addr,
                Kind   = IoKind.McLoad,
                Desc   = $"{load}부하 3상 STEP{step} MC"
            };

        private static PlcIoPoint CRes(int p, int stage, ushort diAddr, string desc) =>
            new PlcIoPoint { Tag = $"P{p}_C{stage}_RESULT", DiAddr = diAddr, DoAddr = null, Kind = IoKind.CResult, Desc = desc };

        private static PlcIoPoint CAlm(int p, int stage, string sub, ushort diAddr, string desc) =>
            new PlcIoPoint { Tag = $"P{p}_C{stage}_{sub}", DiAddr = diAddr, DoAddr = null, Kind = IoKind.CAlarm, Desc = desc };

        private static PlcIoPoint CCm(int p, int stage, ushort doAddr, string desc) =>
            new PlcIoPoint { Tag = $"P{p}_C{stage}_CMD", DiAddr = null, DoAddr = doAddr, Kind = IoKind.CCmdDo, Desc = desc };

        private static PlcIoPoint SFb(int p, string suffix, ushort diAddr, string desc) =>
            new PlcIoPoint
            {
                Tag    = $"P{p}_{suffix}",
                DiAddr = diAddr,
                DoAddr = null,
                Kind   = IoKind.StatusFb,
                Desc   = desc
            };

        private static PlcIoPoint SDo(int p, string suffix, ushort doAddr, string desc) =>
            new PlcIoPoint
            {
                Tag    = $"P{p}_{suffix}",
                DiAddr = null,
                DoAddr = doAddr,
                Kind   = IoKind.CmdDo,
                Desc   = desc
            };
    }
}
