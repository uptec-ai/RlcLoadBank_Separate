using System.Collections.Generic;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// HMI ↔ PLC Modbus 인터페이스 정의 (UI 기준 — PLC 내부 배선과 별개).
    /// Zero-based offset = Modbus 주소 (DI: FC02 ReadInputs, DO: FC05/FC15 Coils).
    ///
    /// C부하 인터페이스 (stage당):
    ///   DO 1점 — Cn_CMD (HMI 투입/개방 명령. PLC가 내부 시퀀스 실행)
    ///   DI 4점 — Cn_RESULT, Cn_MC1_FB(저항경유MC), Cn_MC2_FB(직결MC), Cn_SCR_FB
    /// </summary>
    public enum IoKind
    {
        McLoad,   // R/L 부하 MC — DI(FB)·DO(CMD) 주소 동일
        StatusFb, // 보호·상태 DI (OVR, OCR, HT, EMG, MCCB_*_FB, LOC_REM 등)
        CmdDo,    // 명령 DO (MCCB_*_CMD, FAN_*_CMD, RESET_CMD)
        CResult,  // C부하 동작 결과 DI (T=동작, F=멈춤)
        CAlarm,   // C부하 개별 상태 DI (MC1_FB / MC2_FB / SCR_FB)
        CCmdDo,   // C부하 투입/개방 명령 DO (stage당 단일 CMD)
    }

    public class PlcIoPoint
    {
        public string  Tag;
        public ushort? DiAddr;   // DI 주소 (CmdDo·CCmdDo는 null)
        public ushort? DoAddr;   // DO 주소 (StatusFb·CResult·CAlarm은 null)
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
        // DI: R/L 48점 + C부하 8점(48-55) + 보호 17점(56-72) = 73점
        // DO: R/L 48점 + C-CMD 2점(48,51) + 명령 7점(56-62) = 57점

        private static List<PlcIoPoint> BuildPnl1()
        {
            const int p = 1;
            var pts = new List<PlcIoPoint>();

            // R 부하 (각 상 × 8 STEP) — DI/DO addr 0–47
            AddSinglePhase(pts, p, "R", "RN",  0);
            AddSinglePhase(pts, p, "R", "SN",  8);
            AddSinglePhase(pts, p, "R", "TN", 16);
            AddSinglePhase(pts, p, "L", "RN", 24);
            AddSinglePhase(pts, p, "L", "SN", 32);
            AddSinglePhase(pts, p, "L", "TN", 40);

            // C 부하 — DI addr 48–55 / DO addr 48, 51
            // DI: stage당 RESULT + MC1_FB + MC2_FB; 공통 SCR_FB는 stage별로 마지막에
            pts.Add(CRes(p, 1, 48, "C부하 STEP1 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 1, "MC1_FB", 49, "C부하 STEP1 저항경유 MC 보조접점"));
            pts.Add(CAlm(p, 1, "MC2_FB", 50, "C부하 STEP1 직결 MC 보조접점"));
            pts.Add(CRes(p, 2, 51, "C부하 STEP2 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 2, "MC1_FB", 52, "C부하 STEP2 저항경유 MC 보조접점"));
            pts.Add(CAlm(p, 2, "MC2_FB", 53, "C부하 STEP2 직결 MC 보조접점"));
            pts.Add(CAlm(p, 1, "SCR_FB", 54, "C부하 STEP1 SCR 게이팅 상태"));
            pts.Add(CAlm(p, 2, "SCR_FB", 55, "C부하 STEP2 SCR 게이팅 상태"));
            // DO: stage당 단일 CMD (PLC 내부에서 시퀀스 실행)
            pts.Add(CCm(p, 1, 48, "C부하 STEP1 투입/개방 명령"));
            pts.Add(CCm(p, 2, 51, "C부하 STEP2 투입/개방 명령"));

            // 보호·상태 DI — addr 56–72
            pts.Add(SFb(p, "OVR_FB",       56, "과전압 계전기(OVR) 동작 접점"));
            pts.Add(SFb(p, "OCR_FB",       57, "과전류 계전기(OCR) 동작 접점"));
            pts.Add(SFb(p, "HT_FB",        58, "과열(HT) 검출 접점"));
            pts.Add(SFb(p, "FAN_FB",       59, "FAN 종합 운전 상태 피드백"));
            pts.Add(SFb(p, "MCCB_ON_FB",   60, "MCCB ON 보조접점"));
            pts.Add(SFb(p, "MCCB_OFF_FB",  61, "MCCB OFF 보조접점"));
            pts.Add(SFb(p, "MCCB_TRIP_FB", 62, "MCCB TRIP 접점"));
            pts.Add(SFb(p, "EMG_FB",       63, "비상정지(EMG STOP) 스위치 접점"));
            pts.Add(SFb(p, "DOOR_FB",      64, "Door interlock 접점"));
            pts.Add(SFb(p, "FAN_R_FB",     65, "R부하 냉각팬 운전 피드백"));
            pts.Add(SFb(p, "FAN_L_FB",     66, "L부하 냉각팬 운전 피드백"));
            pts.Add(SFb(p, "FAN_C_FB",     67, "C부하 냉각팬 운전 피드백"));
            pts.Add(SFb(p, "PWR_380_FB",   68, "380V 주전원 투입 상태"));
            pts.Add(SFb(p, "PWR_220_FB",   69, "220V 제어전원 투입 상태"));
            pts.Add(SFb(p, "CTRL_380_FB",  70, "제어전원 380V(내부 변압기) 선택 상태"));
            pts.Add(SFb(p, "CTRL_220_FB",  71, "제어전원 220V(외부전원) 선택 상태"));
            pts.Add(SFb(p, "LOC_REM_FB",   72, "제어방식 선택 (0=Local, 1=Remote)"));

            // 명령 DO — addr 56–62
            pts.Add(SDo(p, "FAN_R_CMD",    56, "R부하 냉각팬 기동 명령"));
            pts.Add(SDo(p, "FAN_L_CMD",    57, "L부하 냉각팬 기동 명령"));
            pts.Add(SDo(p, "FAN_C_CMD",    58, "C부하 냉각팬 기동 명령"));
            pts.Add(SDo(p, "MCCB_ON_CMD",  59, "MCCB ON 명령"));
            pts.Add(SDo(p, "MCCB_OFF_CMD", 60, "MCCB OFF 명령"));
            pts.Add(SDo(p, "MCCB_TRIP_CMD",61, "MCCB TRIP 명령"));
            pts.Add(SDo(p, "RESET_CMD",    62, "Reset(고장 리셋) 명령"));

            return pts;
        }

        // ── PNL-2 / PNL-3 (3상 일괄, 동일 구성) ──────────────────────────────
        // DI: R/L 16점 + C부하 8점(16-23) + 보호 17점(24-40) = 41점
        // DO: R/L 16점 + C-CMD 2점(16,17) + 명령 7점(24-30) = 25점

        private static List<PlcIoPoint> BuildPnl23(int p)
        {
            var pts = new List<PlcIoPoint>();

            // R 부하 STEP1–8 — addr 0–7
            for (int i = 1; i <= 8; i++)
                pts.Add(MC3(p, "R", i, (ushort)(i - 1)));
            // L 부하 STEP1–8 — addr 8–15
            for (int i = 1; i <= 8; i++)
                pts.Add(MC3(p, "L", i, (ushort)(8 + i - 1)));

            // C 부하 — DI addr 16–23 / DO addr 16, 17
            pts.Add(CRes(p, 1, 16, "C부하 STEP1 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 1, "MC1_FB", 17, "C부하 STEP1 저항경유 MC 보조접점"));
            pts.Add(CAlm(p, 1, "MC2_FB", 18, "C부하 STEP1 직결 MC 보조접점"));
            pts.Add(CRes(p, 2, 19, "C부하 STEP2 동작 결과 (T=동작, F=멈춤)"));
            pts.Add(CAlm(p, 2, "MC1_FB", 20, "C부하 STEP2 저항경유 MC 보조접점"));
            pts.Add(CAlm(p, 2, "MC2_FB", 21, "C부하 STEP2 직결 MC 보조접점"));
            pts.Add(CAlm(p, 1, "SCR_FB", 22, "C부하 STEP1 SCR 게이팅 상태"));
            pts.Add(CAlm(p, 2, "SCR_FB", 23, "C부하 STEP2 SCR 게이팅 상태"));
            pts.Add(CCm(p, 1, 16, "C부하 STEP1 투입/개방 명령"));
            pts.Add(CCm(p, 2, 17, "C부하 STEP2 투입/개방 명령"));

            // 보호·상태 DI — addr 24–40
            pts.Add(SFb(p, "OVR_FB",       24, "과전압 계전기(OVR) 동작 접점"));
            pts.Add(SFb(p, "OCR_FB",       25, "과전류 계전기(OCR) 동작 접점"));
            pts.Add(SFb(p, "HT_FB",        26, "과열(HT) 검출 접점"));
            pts.Add(SFb(p, "FAN_FB",       27, "FAN 종합 운전 상태 피드백"));
            pts.Add(SFb(p, "MCCB_ON_FB",   28, "MCCB ON 보조접점"));
            pts.Add(SFb(p, "MCCB_OFF_FB",  29, "MCCB OFF 보조접점"));
            pts.Add(SFb(p, "MCCB_TRIP_FB", 30, "MCCB TRIP 접점"));
            pts.Add(SFb(p, "EMG_FB",       31, "비상정지(EMG STOP) 스위치 접점"));
            pts.Add(SFb(p, "DOOR_FB",      32, "Door interlock 접점"));
            pts.Add(SFb(p, "FAN_R_FB",     33, "R부하 냉각팬 운전 피드백"));
            pts.Add(SFb(p, "FAN_L_FB",     34, "L부하 냉각팬 운전 피드백"));
            pts.Add(SFb(p, "FAN_C_FB",     35, "C부하 냉각팬 운전 피드백"));
            pts.Add(SFb(p, "PWR_380_FB",   36, "380V 주전원 투입 상태"));
            pts.Add(SFb(p, "PWR_220_FB",   37, "220V 제어전원 투입 상태"));
            pts.Add(SFb(p, "CTRL_380_FB",  38, "제어전원 380V(내부 변압기) 선택 상태"));
            pts.Add(SFb(p, "CTRL_220_FB",  39, "제어전원 220V(외부전원) 선택 상태"));
            pts.Add(SFb(p, "LOC_REM_FB",   40, "제어방식 선택 (0=Local, 1=Remote)"));

            // 명령 DO — addr 24–30
            pts.Add(SDo(p, "FAN_R_CMD",    24, "R부하 냉각팬 기동 명령"));
            pts.Add(SDo(p, "FAN_L_CMD",    25, "L부하 냉각팬 기동 명령"));
            pts.Add(SDo(p, "FAN_C_CMD",    26, "C부하 냉각팬 기동 명령"));
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
            new PlcIoPoint { Tag = $"P{p}_{suffix}", DiAddr = diAddr, DoAddr = null, Kind = IoKind.StatusFb, Desc = desc };

        private static PlcIoPoint SDo(int p, string suffix, ushort doAddr, string desc) =>
            new PlcIoPoint { Tag = $"P{p}_{suffix}", DiAddr = null, DoAddr = doAddr, Kind = IoKind.CmdDo, Desc = desc };
    }
}
