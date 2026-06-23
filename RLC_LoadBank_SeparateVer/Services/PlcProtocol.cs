using System.Collections.Generic;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Modbus I/O point definitions — SPEC-RLC-2026-001 기준.
    /// Address = (module - 1) × 16 + channel  (0-based).
    /// DI (*_FB) → ReadInputs (FC2) / production, ReadCoils (FC1) / test server.
    /// DO (*_CMD) → WriteSingleCoil (FC5).
    /// MC/C부하 포인트는 DI·DO 주소가 동일하게 배치되어 있음.
    /// </summary>
    public enum IoKind
    {
        McLoad,   // R/L 부하 MC — DI(FB) + DO(CMD) 주소 동일
        CLoad,    // C부하 서브 디바이스 (R_MC / DIR_MC / SCR) — 주소 동일
        StatusFb, // 보호·상태 DI 전용 (OVR, OCR, HT, EMG, MCCB_*_FB, LOC_REM 등)
        CmdDo,    // 명령 DO 전용 (MCCB_*_CMD, FAN_*_CMD, RESET_CMD)
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

            // C 부하 서브 (DI04.00–05 = addr 48–53 / DO04.00–05 = addr 48–53)
            pts.Add(CSub(p, 1, "R_MC",   48, 48, "C부하 STEP1 저항경유 MC"));
            pts.Add(CSub(p, 1, "DIR_MC", 49, 49, "C부하 STEP1 직결 MC"));
            pts.Add(CSub(p, 2, "R_MC",   50, 50, "C부하 STEP2 저항경유 MC"));
            pts.Add(CSub(p, 2, "DIR_MC", 51, 51, "C부하 STEP2 직결 MC"));
            pts.Add(CSub(p, 1, "SCR",    52, 52, "C부하 STEP1 SCR 게이팅"));
            pts.Add(CSub(p, 2, "SCR",    53, 53, "C부하 STEP2 SCR 게이팅"));

            // 보호·상태 DI only (DI04.06–DI05.06 = addr 54–70)
            pts.Add(SFb(p, "OVR_FB",       54, "과전압 계전기(OVR)"));
            pts.Add(SFb(p, "OCR_FB",       55, "과전류 계전기(OCR)"));
            pts.Add(SFb(p, "HT_FB",        56, "과열(HT) 검출"));
            pts.Add(SFb(p, "FAN_FB",       57, "FAN 종합 운전 상태"));
            pts.Add(SFb(p, "MCCB_ON_FB",   58, "MCCB ON 보조접점"));
            pts.Add(SFb(p, "MCCB_OFF_FB",  59, "MCCB OFF 보조접점"));
            pts.Add(SFb(p, "MCCB_TRIP_FB", 60, "MCCB TRIP 접점"));
            pts.Add(SFb(p, "EMG_FB",       61, "비상정지(EMG STOP)"));
            pts.Add(SFb(p, "DOOR_FB",      62, "Door interlock"));
            pts.Add(SFb(p, "FAN_R_FB",     63, "R부하 냉각팬"));
            pts.Add(SFb(p, "FAN_L_FB",     64, "L부하 냉각팬"));
            pts.Add(SFb(p, "FAN_C_FB",     65, "C부하 냉각팬"));
            pts.Add(SFb(p, "PWR_380_FB",   66, "380V 주전원 투입"));
            pts.Add(SFb(p, "PWR_220_FB",   67, "220V 제어전원 투입"));
            pts.Add(SFb(p, "CTRL_380_FB",  68, "제어전원 380V 선택"));
            pts.Add(SFb(p, "CTRL_220_FB",  69, "제어전원 220V 선택"));
            pts.Add(SFb(p, "LOC_REM_FB",   70, "Local/Remote 선택 (0=Local, 1=Remote)"));

            // 명령 DO only (DO04.06–12 = addr 54–60)
            pts.Add(SDo(p, "FAN_R_CMD",    54, "R부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_L_CMD",    55, "L부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_C_CMD",    56, "C부하 냉각팬 기동"));
            pts.Add(SDo(p, "MCCB_ON_CMD",  57, "MCCB ON 명령"));
            pts.Add(SDo(p, "MCCB_OFF_CMD", 58, "MCCB OFF 명령"));
            pts.Add(SDo(p, "MCCB_TRIP_CMD",59, "MCCB TRIP 명령"));
            pts.Add(SDo(p, "RESET_CMD",    60, "Reset(고장 리셋) 명령"));

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

            // C 부하 서브 (DI02.00–05 = addr 16–21 / DO02.00–05 = addr 16–21)
            pts.Add(CSub(p, 1, "R_MC",   16, 16, "C부하 STEP1 저항경유 MC"));
            pts.Add(CSub(p, 1, "DIR_MC", 17, 17, "C부하 STEP1 직결 MC"));
            pts.Add(CSub(p, 2, "R_MC",   18, 18, "C부하 STEP2 저항경유 MC"));
            pts.Add(CSub(p, 2, "DIR_MC", 19, 19, "C부하 STEP2 직결 MC"));
            pts.Add(CSub(p, 1, "SCR",    20, 20, "C부하 STEP1 SCR 게이팅"));
            pts.Add(CSub(p, 2, "SCR",    21, 21, "C부하 STEP2 SCR 게이팅"));

            // 보호·상태 DI only (DI02.06–DI03.06 = addr 22–38)
            pts.Add(SFb(p, "OVR_FB",       22, "과전압 계전기(OVR)"));
            pts.Add(SFb(p, "OCR_FB",       23, "과전류 계전기(OCR)"));
            pts.Add(SFb(p, "HT_FB",        24, "과열(HT) 검출"));
            pts.Add(SFb(p, "FAN_FB",       25, "FAN 종합 운전 상태"));
            pts.Add(SFb(p, "MCCB_ON_FB",   26, "MCCB ON 보조접점"));
            pts.Add(SFb(p, "MCCB_OFF_FB",  27, "MCCB OFF 보조접점"));
            pts.Add(SFb(p, "MCCB_TRIP_FB", 28, "MCCB TRIP 접점"));
            pts.Add(SFb(p, "EMG_FB",       29, "비상정지(EMG STOP)"));
            pts.Add(SFb(p, "DOOR_FB",      30, "Door interlock"));
            pts.Add(SFb(p, "FAN_R_FB",     31, "R부하 냉각팬"));
            pts.Add(SFb(p, "FAN_L_FB",     32, "L부하 냉각팬"));
            pts.Add(SFb(p, "FAN_C_FB",     33, "C부하 냉각팬"));
            pts.Add(SFb(p, "PWR_380_FB",   34, "380V 주전원 투입"));
            pts.Add(SFb(p, "PWR_220_FB",   35, "220V 제어전원 투입"));
            pts.Add(SFb(p, "CTRL_380_FB",  36, "제어전원 380V 선택"));
            pts.Add(SFb(p, "CTRL_220_FB",  37, "제어전원 220V 선택"));
            pts.Add(SFb(p, "LOC_REM_FB",   38, "Local/Remote 선택 (0=Local, 1=Remote)"));

            // 명령 DO only (DO02.06–12 = addr 22–28)
            pts.Add(SDo(p, "FAN_R_CMD",    22, "R부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_L_CMD",    23, "L부하 냉각팬 기동"));
            pts.Add(SDo(p, "FAN_C_CMD",    24, "C부하 냉각팬 기동"));
            pts.Add(SDo(p, "MCCB_ON_CMD",  25, "MCCB ON 명령"));
            pts.Add(SDo(p, "MCCB_OFF_CMD", 26, "MCCB OFF 명령"));
            pts.Add(SDo(p, "MCCB_TRIP_CMD",27, "MCCB TRIP 명령"));
            pts.Add(SDo(p, "RESET_CMD",    28, "Reset(고장 리셋) 명령"));

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

        private static PlcIoPoint CSub(int p, int stage, string sub,
                                       ushort diAddr, ushort doAddr, string desc) =>
            new PlcIoPoint
            {
                Tag    = $"P{p}_C{stage}_{sub}",
                DiAddr = diAddr,
                DoAddr = doAddr,
                Kind   = IoKind.CLoad,
                Desc   = desc
            };

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
