using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>
    /// One load panel (PNL-1/2/3). Builds its MC set from the panel control mode:
    ///  - single-phase (PLC1): R = 3 phase groups × 8, L = 3 × 8, C = 2 stages
    ///  - three-phase (PLC2/3): R = 8, L = 8, C = 2 stages
    /// Status properties (MCCB, EMG, OVR, LOC_REM …) are updated by
    /// RlcStatusViewModel when FeedbackReceived fires for _FB tags.
    /// </summary>
    public class PanelViewModel : ViewModelBase
    {
        private readonly Dictionary<string, McViewModel> _byTag = new Dictionary<string, McViewModel>();

        private static readonly double[] PerPhaseSteps   = { 0.83, 0.83, 1.67, 3.33, 5, 5, 8.33, 10 };
        private static readonly double[] ThreePhaseSteps = { 2.5,  2.5,  5,   10,   15, 15, 25,  30 };
        private const double CStageKVar = 50;

        public int    Index       { get; }   // 0,1,2
        public string Title       { get; }   // "PLC1 / PNL-1"
        public string ControlMode { get; }
        public bool   IsSinglePhase { get; }

        public ObservableCollection<McGroupViewModel>  RGroups { get; } = new ObservableCollection<McGroupViewModel>();
        public ObservableCollection<McGroupViewModel>  LGroups { get; } = new ObservableCollection<McGroupViewModel>();
        public ObservableCollection<CStageViewModel>   CSteps  { get; } = new ObservableCollection<CStageViewModel>();

        public Action<PanelViewModel, McViewModel>      McToggleRequested;
        public Action<PanelViewModel, CStageViewModel>  CStageToggleRequested;

        // ── 연결 상태 ────────────────────────────────────────────────────────

        public bool IsConnected
        {
            get => GetValue<bool>();
            set => SetValue(value, () =>
            {
                RaisePropertyChanged(nameof(ConnText));
                RaisePropertyChanged(nameof(LinkState));
            });
        }
        public string    ConnText  => IsConnected ? "연결됨" : "미연결";
        public ConnState LinkState => IsConnected ? ConnState.Connected : ConnState.Disconnected;

        // ── 보호·상태 DI 피드백 프로퍼티 (_FB 태그에서 업데이트) ─────────────

        /// <summary>MCCB ON 보조접점 (P{n}_MCCB_ON_FB)</summary>
        public bool MccbOn   { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>MCCB TRIP 접점 (P{n}_MCCB_TRIP_FB)</summary>
        public bool MccbTrip { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>비상정지 입력 — b접점이므로 true=EMG 동작 (P{n}_EMG_FB)</summary>
        public bool EmgActive { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>과전압 계전기(OVR) 동작 (P{n}_OVR_FB)</summary>
        public bool OvrFault { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>과전류 계전기(OCR) 동작 (P{n}_OCR_FB)</summary>
        public bool OcrFault { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>과열(HT) 검출 (P{n}_HT_FB)</summary>
        public bool HtFault  { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>Local/Remote 선택 (0=Local, 1=Remote) (P{n}_LOC_REM_FB)</summary>
        public bool IsRemote { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>FAN 종합 운전 상태 (P{n}_FAN_FB)</summary>
        public bool FanOk    { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>380V 주전원 투입 상태 (P{n}_PWR_380_FB)</summary>
        public bool Pwr380Ok { get => GetValue<bool>(); set => SetValue(value); }

        /// <summary>어떤 보호 기능이라도 동작 중이면 true (알람 칩용)</summary>
        public bool HasFault => OvrFault || OcrFault || HtFault || MccbTrip;

        // ── GIMAC 계측 데이터 (500ms 폴링, OnGimacDataReceived → ApplyGimacReading) ──

        /// <summary>GIMAC 표시 레이블 (e.g. "GIMAC 1")</summary>
        public string GimacLabel => $"GIMAC {Index + 1}";

        public bool   GimacConnected    { get => GetValue<bool>();   set => SetValue(value); }
        public double GimacVoltage      { get => GetValue<double>(); set => SetValue(value); }  // V
        public double GimacCurrent      { get => GetValue<double>(); set => SetValue(value); }  // A
        public double GimacActivePower  { get => GetValue<double>(); set => SetValue(value); }  // kW
        public double GimacReactivePower{ get => GetValue<double>(); set => SetValue(value); }  // kVAr
        public double GimacFrequency    { get => GetValue<double>(); set => SetValue(value); }  // Hz

        // ── 현재 투입된 R/L/C 용량 (MC 상태 기반, RefreshActiveCapacity() 호출 시 갱신) ──

        /// <summary>현재 ON 상태 R 부하 합산 (kW). PLC1=3상 합, PLC2/3=3상 일괄.</summary>
        public double ActiveRkW   { get => GetValue<double>(); set => SetValue(value); }
        /// <summary>현재 ON 상태 L 리액터 합산 (kVAr).</summary>
        public double ActiveLkVar { get => GetValue<double>(); set => SetValue(value); }
        /// <summary>현재 ON 상태 C 부하 합산 (kVAr).</summary>
        public double ActiveCkVar { get => GetValue<double>(); set => SetValue(value); }

        /// <summary>MC / C-stage 상태 변경 시 RlcStatusViewModel에서 호출.</summary>
        public void RefreshActiveCapacity()
        {
            ActiveRkW   = RGroups.Sum(g => g.Items.Where(m => m.State == McState.On).Sum(m => m.Value));
            ActiveLkVar = LGroups.Sum(g => g.Items.Where(m => m.State == McState.On).Sum(m => m.Value));
            ActiveCkVar = CSteps.Where(cs => cs.IsRunning).Sum(cs => cs.Value);
        }

        /// <summary>GIMAC 폴링 데이터를 패널 프로퍼티에 반영.</summary>
        public void ApplyGimacReading(GimacReading r)
        {
            GimacConnected     = true;
            GimacVoltage       = r.AvgVoltage;
            GimacCurrent       = r.AvgCurrent;
            GimacActivePower   = r.ActivePower   / 1000.0;
            GimacReactivePower = r.ReactivePower / 1000.0;
            GimacFrequency     = r.Frequency;
        }

        // ── 생성자 ───────────────────────────────────────────────────────────

        public PanelViewModel(int index, string title, bool singlePhase, bool connected)
        {
            Index       = index;
            Title       = title;
            IsSinglePhase = singlePhase;
            ControlMode = singlePhase ? "단상 개별 제어" : "3상 일괄 제어";
            MccbOn      = true;   // 초기 기본값; 실제 피드백으로 교체됨
            IsRemote    = true;
            Pwr380Ok    = true;
            FanOk       = true;
            Build();
            IsConnected = connected;
        }

        private void Build()
        {
            if (IsSinglePhase)
            {
                foreach (var ph in new[] { "RN", "SN", "TN" }) RGroups.Add(MakeSteps("R", ph));
                foreach (var ph in new[] { "RN", "SN", "TN" }) LGroups.Add(MakeSteps("L", ph));
            }
            else
            {
                RGroups.Add(MakeSteps("R", null));
                LGroups.Add(MakeSteps("L", null));
            }
            for (int s = 1; s <= 2; s++)
                CSteps.Add(new CStageViewModel($"P{Index + 1}_C{s}", $"C{s}", OnCStageToggle));
        }

        private McGroupViewModel MakeSteps(string load, string phase)
        {
            string label = phase == null ? "" : $"{phase[0]}-N";
            var g        = new McGroupViewModel(label);
            var values   = IsSinglePhase ? PerPhaseSteps : ThreePhaseSteps;
            var loadType = load == "R" ? LoadType.R : LoadType.L;
            for (int i = 1; i <= 8; i++)
            {
                string tag = phase == null
                    ? $"P{Index + 1}_{load}_{i:00}"
                    : $"P{Index + 1}_{load}_{phase}_{i:00}";
                var mc = new McViewModel(tag, $"{load}{i}", OnToggle)
                { Load = loadType, Value = values[i - 1] };
                g.Items.Add(mc);
                _byTag[tag] = mc;
            }
            return g;
        }

        private void OnToggle(McViewModel mc)          => McToggleRequested?.Invoke(this, mc);
        private void OnCStageToggle(CStageViewModel cs) => CStageToggleRequested?.Invoke(this, cs);

        public McViewModel FindMc(string tag) =>
            _byTag.TryGetValue(tag, out var mc) ? mc : null;

        public IEnumerable<McViewModel> AllMcs => _byTag.Values;

        // ── 상태 업데이트 (RlcStatusViewModel에서 호출) ───────────────────────

        /// <summary>_FB 태그로부터 해당 상태 프로퍼티를 업데이트한다.</summary>
        public void ApplyStatusFeedback(string fbTag, bool on)
        {
            int p = Index + 1;
            if (fbTag == $"P{p}_MCCB_ON_FB")   { MccbOn   = on; return; }
            if (fbTag == $"P{p}_MCCB_TRIP_FB") { MccbTrip = on; RaisePropertyChanged(nameof(HasFault)); return; }
            if (fbTag == $"P{p}_EMG_FB")        { EmgActive = on; return; }
            if (fbTag == $"P{p}_OVR_FB")        { OvrFault  = on; RaisePropertyChanged(nameof(HasFault)); return; }
            if (fbTag == $"P{p}_OCR_FB")        { OcrFault  = on; RaisePropertyChanged(nameof(HasFault)); return; }
            if (fbTag == $"P{p}_HT_FB")         { HtFault   = on; RaisePropertyChanged(nameof(HasFault)); return; }
            if (fbTag == $"P{p}_LOC_REM_FB")    { IsRemote  = on; return; }
            if (fbTag == $"P{p}_FAN_FB")        { FanOk     = on; return; }
            if (fbTag == $"P{p}_PWR_380_FB")    { Pwr380Ok  = on; return; }
        }

        /// <summary>C부하 피드백 태그를 CStageViewModel에 라우팅한다. 처리되면 true 반환.</summary>
        public bool TryApplyCFeedback(string tag, bool on)
        {
            foreach (var cs in CSteps)
            {
                if (tag == $"{cs.Tag}_RESULT") { cs.IsRunning = on; RefreshActiveCapacity(); return true; }
                if (tag == $"{cs.Tag}_MC1_FB") { cs.Mc1Alarm = on; return true; }
                if (tag == $"{cs.Tag}_MC2_FB") { cs.Mc2Alarm = on; return true; }
                if (tag == $"{cs.Tag}_SCR_FB") { cs.ScrAlarm = on; return true; }
            }
            return false;
        }
    }
}
