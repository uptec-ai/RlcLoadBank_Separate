using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using SciChart.Charting.Model.DataSeries;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;
using System.Reflection;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>
    /// Main operational screen ("RLC 현황"): top status bar, RLC diagram region,
    /// and the four bottom panels (manual op / auto op / trip-alarm / history).
    /// </summary>
    public class RlcStatusViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _clock;

        // Track which IPlcService instance we subscribed to so we can cleanly re-subscribe
        // after SaveConnection resets ServiceHub.Plc to a new instance.
        private IPlcService _subscribedPlc;

        public ObservableCollection<PanelViewModel> Panels { get; } = new ObservableCollection<PanelViewModel>();
        public PanelViewModel Panel1 => Panels[0];
        public PanelViewModel Panel2 => Panels[1];
        public PanelViewModel Panel3 => Panels[2];

        public OperationMode Mode
        {
            get => GetValue<OperationMode>();
            set => SetValue(value, () =>
            {
                RaisePropertyChanged(nameof(IsAuto));
                RaisePropertyChanged(nameof(IsManual));
                StartAutoCommand?.RaiseCanExecuteChanged();
                if (value == OperationMode.Manual) ClearPreview();
                else RefreshPreview();
            });
        }
        public bool IsAuto => Mode == OperationMode.Auto;
        public bool IsManual => Mode == OperationMode.Manual;

        public bool PlcCommOk => Panels.Any(p => p.IsConnected);
        public bool EStopOk { get => GetValue<bool>(); set => SetValue(value); }
        public bool AlarmWait { get => GetValue<bool>(); set => SetValue(value); }
        public bool HeartbeatOk { get => GetValue<bool>(); set => SetValue(value); }
        public DateTime Now { get => GetValue<DateTime>(); set => SetValue(value); }

        // Auto-operation targets (R/L/C individual setpoints)
        public double RTarget { get => GetValue<double>(); set => SetValue(value, RefreshPreview); }
        public double LTarget { get => GetValue<double>(); set => SetValue(value, RefreshPreview); }
        public double CTarget { get => GetValue<double>(); set => SetValue(value, RefreshPreview); }

        // Auto mode: individual R/L/C, or target power + power factor
        public AutoMode AutoMode
        {
            get => GetValue<AutoMode>();
            set => SetValue(value, () => { RaisePropertyChanged(nameof(IsIndividual)); RaisePropertyChanged(nameof(IsPowerPf)); RefreshPreview(); });
        }
        public bool IsIndividual => AutoMode == AutoMode.Individual;
        public bool IsPowerPf => AutoMode == AutoMode.PowerPf;
        public double TargetPower { get => GetValue<double>(); set => SetValue(value, RefreshPreview); }   // kW
        public double TargetPf { get => GetValue<double>(); set => SetValue(value, RefreshPreview); }       // 0..1
        public bool LeadingPf { get => GetValue<bool>(); set => SetValue(value, RefreshPreview); }          // true=진상(C), false=지상(L)

        // 판넬 선택 (null = 연결된 모든 판넬)
        // ObservableCollection 대신 배열 프로퍼티: Clear()→CollectionChanged(Reset) 경로를 없애
        // WPF ComboBox가 ItemContainer DataContext를 binding source로 잘못 해석하는 TargetException을 방지.
        private PanelViewModel[] _connectedPanels = Array.Empty<PanelViewModel>();
        public PanelViewModel[] ConnectedPanels
        {
            get => _connectedPanels;
            private set
            {
                if (value == _connectedPanels) return;
                _connectedPanels = value;
                RaisePropertyChanged(nameof(ConnectedPanels));
            }
        }
        public PanelViewModel SelectedAutoPanel
        {
            get => GetValue<PanelViewModel>();
            set => SetValue(value, RefreshPreview);
        }

        public ObservableCollection<AlarmEntry> Alarms { get; } = new ObservableCollection<AlarmEntry>();
        public ObservableCollection<HistoryEntry> History { get; } = new ObservableCollection<HistoryEntry>();
        public int UnackCount => Alarms.Count(a => !a.Acknowledged);

        // Power trend series (one per panel, FIFO 300s at 1s poll)
        public XyDataSeries<DateTime, double> Pnl1PowerSeries { get; }
        public XyDataSeries<DateTime, double> Pnl2PowerSeries { get; }
        public XyDataSeries<DateTime, double> Pnl3PowerSeries { get; }

        public DelegateCommand SetAutoCommand { get; }
        public DelegateCommand SetManualCommand { get; }
        public DelegateCommand<string> LoadOnCommand { get; }
        public DelegateCommand<string> LoadOffCommand { get; }
        public DelegateCommand MccbOnCommand { get; }
        public DelegateCommand MccbOffCommand { get; }
        public DelegateCommand MccbTripCommand { get; }
        public DelegateCommand ResetCommand { get; }
        public DelegateCommand StartAutoCommand { get; }
        public DelegateCommand StopAutoCommand { get; }
        public DelegateCommand AckAlarmsCommand { get; }
        public DelegateCommand OpenConnectionCommand { get; }
        public DelegateCommand SetAutoIndividualCommand { get; }
        public DelegateCommand SetAutoPowerPfCommand { get; }

        public RlcStatusViewModel()
        {
            Panels.Add(new PanelViewModel(0, "PLC1 / PNL-1", true, ServiceHub.Plc.IsConnected(0)));
            Panels.Add(new PanelViewModel(1, "PLC2 / PNL-2", false, ServiceHub.Plc.IsConnected(1)));
            Panels.Add(new PanelViewModel(2, "PLC3 / PNL-3", false, ServiceHub.Plc.IsConnected(2)));
            foreach (var p in Panels) p.McToggleRequested = OnMcToggle;

            Pnl1PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-1 (kW)", FifoCapacity = 300 };
            Pnl2PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-2 (kW)", FifoCapacity = 300 };
            Pnl3PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-3 (kW)", FifoCapacity = 300 };

            EStopOk = true; AlarmWait = false; HeartbeatOk = true;
            // 초기값 세팅: RefreshPreview는 Mode = Auto 할당 시 호출되므로 여기서는 raw SetValue 없음.
            // SetValue 콜백이 RefreshPreview를 부르지 않도록 먼저 Panels를 구성한 후 target 값을 지정.
            RTarget = 200.0; LTarget = 200.0; CTarget = 130.0;
            AutoMode = AutoMode.Individual; TargetPower = 200.0; TargetPf = 0.95; LeadingPf = false;
            Now = DateTime.Now;
            SeedLogs();

            // ConnectedPanels 초기화 (배열 교체)
            _connectedPanels = Panels.Where(p => p.IsConnected).ToArray();

            _subscribedPlc = ServiceHub.Plc;
            _subscribedPlc.FeedbackReceived += OnFeedback;
            _subscribedPlc.ConnectionChanged += OnPlcConnectionChanged;

            bool anyConnected = PlcCommOk;
            SetAutoCommand   = new DelegateCommand(() => Mode = OperationMode.Auto);
            SetManualCommand = new DelegateCommand(() => Mode = OperationMode.Manual);
            LoadOnCommand  = new DelegateCommand<string>(t => ManualLoad(t, true),  _ => PlcCommOk);
            LoadOffCommand = new DelegateCommand<string>(t => ManualLoad(t, false), _ => PlcCommOk);
            MccbOnCommand  = new DelegateCommand(() => Mccb("ON"),   () => PlcCommOk);
            MccbOffCommand = new DelegateCommand(() => Mccb("OFF"),  () => PlcCommOk);
            MccbTripCommand= new DelegateCommand(() => Mccb("TRIP"), () => PlcCommOk);
            ResetCommand   = new DelegateCommand(ResetAll, () => PlcCommOk);
            StartAutoCommand = new DelegateCommand(StartAuto,  () => PlcCommOk && IsAuto);
            StopAutoCommand  = new DelegateCommand(StopAuto, () => PlcCommOk);
            AckAlarmsCommand = new DelegateCommand(() =>
            {
                foreach (var a in Alarms) a.Acknowledged = true;
                RaisePropertyChanged(nameof(UnackCount));
            });
            OpenConnectionCommand  = new DelegateCommand(OpenConnection);
            SetAutoIndividualCommand = new DelegateCommand(() => AutoMode = AutoMode.Individual);
            SetAutoPowerPfCommand    = new DelegateCommand(() => AutoMode = AutoMode.PowerPf);

            Mode = OperationMode.Auto;

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += OnClockTick;
            _clock.Start();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnPlcConnectionChanged(object sender, int panelIndex)
        {
            RefreshConn();
        }

        private void OnFeedback(object sender, McFeedback fb)
        {
            var panel = Panels.FirstOrDefault(p => p.Index == fb.PanelIndex);
            if (panel == null) return;

            // MC / C-sub base tag
            var mc = panel.FindMc(fb.McTag);
            if (mc != null) { mc.State = fb.On ? McState.On : McState.Off; return; }

            // Status _FB tag → panel status properties + top-bar flags
            if (fb.McTag.EndsWith("_FB", StringComparison.OrdinalIgnoreCase))
            {
                panel.ApplyStatusFeedback(fb.McTag, fb.On);
                RefreshStatusFlags();
            }
        }

        private void RefreshStatusFlags()
        {
            // E-Stop OK = 어느 판넬도 EMG 동작 중이 아닐 때
            EStopOk = Panels.All(p => !p.EmgActive);
            // AlarmWait = 어떤 보호 기능이 동작 중일 때 (OVR/OCR/HT/MCCB_TRIP)
            AlarmWait = Panels.Any(p => p.HasFault);
        }

        private void OnClockTick(object sender, EventArgs e)
        {
            Now = DateTime.Now;
            var t = DateTime.Now;
            // Push estimated active-power kW for each connected panel
            var series = new[] { Pnl1PowerSeries, Pnl2PowerSeries, Pnl3PowerSeries };
            for (int i = 0; i < Panels.Count; i++)
            {
                if (!Panels[i].IsConnected) continue;
                series[i].Append(t, EstimateKw(Panels[i]));
            }
        }

        private static double EstimateKw(PanelViewModel p)
        {
            var rMcs = p.RGroups.SelectMany(g => g.Items).ToList();
            if (rMcs.Count == 0) return 0;
            int onCount = rMcs.Count(m => m.State == McState.On);
            return onCount * (105.0 / rMcs.Count);
        }

        // ── Connection popup ──────────────────────────────────────────────────

        private void OpenConnection()
        {
            var win = new Windows.DeviceConnectionWindow();
            if (Application.Current != null && Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                win.Owner = Application.Current.MainWindow;
            win.ShowDialog();

            // Re-subscribe in case Save() in the popup reset ServiceHub.Plc to a new instance.
            // Unsubscribe from the previously tracked instance, then subscribe to the current one.
            if (_subscribedPlc != null)
            {
                _subscribedPlc.FeedbackReceived -= OnFeedback;
                _subscribedPlc.ConnectionChanged -= OnPlcConnectionChanged;
            }
            _subscribedPlc = ServiceHub.Plc;
            _subscribedPlc.FeedbackReceived += OnFeedback;
            _subscribedPlc.ConnectionChanged += OnPlcConnectionChanged;
            RefreshConn();
        }

        // ── MC toggle ─────────────────────────────────────────────────────────

        private void OnMcToggle(PanelViewModel panel, McViewModel mc)
        {
            if (!panel.IsConnected)
            {
                DXMessageBox.Show($"{panel.Title} 미연결 상태입니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (mc.State == McState.CommWait) return;

            if (IsManual)
            {
                // 수동: ON/OFF 자유 토글
                bool turnOn = mc.State != McState.On;
                string act = turnOn ? "투입(ON)" : "개방(OFF)";
                if (DXMessageBox.Show($"{panel.Title}  {mc.Label} 을(를) {act} 하시겠습니까?", "MC 조작 확인",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                    return;
                if (mc.Load == LoadType.C) { RunCStageAsync(panel, mc, turnOn); return; }
                mc.State = McState.CommWait;
                ServiceHub.Plc.WriteMcCommand(panel.Index, mc.Tag, turnOn);
                AddHistory(panel.Title, $"{mc.Label} {(turnOn ? "ON" : "OFF")}", "성공");
            }
            else if (IsAuto)
            {
                // 자동: 투입 중인 MC만 강제 개방 허용
                if (mc.State != McState.On)
                {
                    DXMessageBox.Show("자동 모드에서는 투입 중인 MC만 개방할 수 있습니다.", "안내",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (DXMessageBox.Show($"[자동운전 중] {panel.Title}  {mc.Label} 을(를) 강제 개방합니다.\n계속하시겠습니까?",
                        "MC 강제 개방 확인", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                    return;
                if (mc.Load == LoadType.C) { RunCStageAsync(panel, mc, false); return; }
                mc.State = McState.CommWait;
                ServiceHub.Plc.WriteMcCommand(panel.Index, mc.Tag, false);
                AddHistory(panel.Title, $"[자동] {mc.Label} 강제 개방", "성공");
            }
            else
            {
                DXMessageBox.Show("운전 모드를 먼저 설정하세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void RunCStageAsync(PanelViewModel panel, McViewModel mc, bool on)
        {
            int stage = mc.Label.EndsWith("2") ? 2 : 1;
            mc.State = McState.CommWait;
            bool ok = await ServiceHub.CLoad.RunAsync(panel.Index, stage, on);
            mc.State = ok ? (on ? McState.On : McState.Off) : McState.Alarm;
            AddHistory(panel.Title, $"C{stage} {(on ? "투입 시퀀스" : "개방 시퀀스")} {(ok ? "완료" : "실패")}", ok ? "성공" : "실패");
            if (!ok) AddAlarm(panel.Title, $"C{stage} 시퀀스 타임아웃 / 인터락 실패", AlarmLevel.Alarm);
        }

        // ── Manual load control ───────────────────────────────────────────────

        private void ManualLoad(string type, bool on)
        {
            if (!IsManual)
            {
                DXMessageBox.Show("수동 모드에서만 조작할 수 있습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string act = on ? "ON" : "OFF";
            if (DXMessageBox.Show($"{type} 부하 전체를 {act} 하시겠습니까?", "부하 제어 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            _ = on ? ManualLoadOnAsync(type) : ManualLoadOffAsync(type);
        }

        // PNL-1→2→3 순서, PNL-1 R/L은 RN→SN→TN 그룹 순, 각 그룹 1→N 스텝
        private async System.Threading.Tasks.Task ManualLoadOnAsync(string type)
        {
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var mc in TypeMcsOrdered(p, type, forward: true))
                {
                    mc.State = McState.CommWait;
                    ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, true);
                    await System.Threading.Tasks.Task.Delay(1000);
                }
            }
            AddHistory("수동", $"{type} 부하 ON 순차 (PNL1→2→3)", "성공");
        }

        // PNL-1→2→3 순서, PNL-1 R/L은 TN→SN→RN 그룹 역순, 각 그룹 N→1 스텝
        private async System.Threading.Tasks.Task ManualLoadOffAsync(string type)
        {
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var mc in TypeMcsOrdered(p, type, forward: false))
                {
                    if (mc.State == McState.Off) continue;
                    mc.State = McState.CommWait;
                    ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                    await System.Threading.Tasks.Task.Delay(1000);
                }
            }
            AddHistory("수동", $"{type} 부하 OFF 순차 (역순, PNL1→2→3)", "성공");
        }

        // 타입별 MC를 정방향/역방향으로 반환
        // PNL-1 R/L: forward=RN→SN→TN 순, backward=TN→SN→RN 순, 각 그룹 내 스텝도 동일 방향
        // PNL-2/3 및 C: 단일 그룹 또는 CSteps, 스텝 방향만 적용
        private static IEnumerable<McViewModel> TypeMcsOrdered(PanelViewModel p, string type, bool forward)
        {
            if (type == "C")
            {
                var cList = p.CSteps.ToList();
                if (!forward) cList.Reverse();
                return cList;
            }
            var groups = (type == "R" ? p.RGroups : p.LGroups).ToList();
            if (!forward) groups.Reverse();
            var result = new List<McViewModel>();
            foreach (var g in groups)
            {
                var items = g.Items.ToList();
                if (!forward) items.Reverse();
                result.AddRange(items);
            }
            return result;
        }

        private void Mccb(string kind)
        {
            if (DXMessageBox.Show($"MCCB {kind} 명령을 실행하시겠습니까?", "MCCB 제어 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            // 연결된 판넬에 Modbus DO 명령 전송 — 피드백(MCCB_*_FB)이 돌아오면 MccbOn/MccbTrip 자동 갱신
            foreach (var p in Panels.Where(x => x.IsConnected))
            {
                ServiceHub.Plc.WriteMcCommand(p.Index, $"P{p.Index + 1}_MCCB_{kind}_CMD", true);
                AddHistory(p.Title, $"MCCB {kind}", "성공");
            }
            if (kind == "TRIP") AddAlarm("ALL", "MCCB TRIP 명령", AlarmLevel.Trip);
        }

        private void ResetAll()
        {
            if (DXMessageBox.Show("전체 부하를 OFF(Reset) 하시겠습니까?", "Reset 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            _ = ResetAllAsync();
        }

        private async System.Threading.Tasks.Task ResetAllAsync()
        {
            // C(2→1) → L(8→1) → R(8→1) 역순 순차 OFF
            await SequentialOffAsync("수동", "RESET 전체 OFF");
        }

        // ── Auto operation ────────────────────────────────────────────────────

        private void StartAuto()
        {
            if (!Panels.Any(p => p.IsConnected))
            {
                DXMessageBox.Show("연결된 PLC가 없습니다.", "자동운전", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var targets = ComputeTargets();
            var steps = BuildAutoSteps();
            var plan = ServiceHub.Auto.Start(targets, steps);
            ApplyPlan(plan);
            if (AutoMode == AutoMode.PowerPf)
            {
                RTarget = targets.RkW; LTarget = targets.LkVar; CTarget = targets.CkVar;
                AddHistory("자동", $"자동운전 시작 (P {TargetPower}kW · PF {TargetPf}{(LeadingPf ? " 진상" : " 지상")} → R {targets.RkW:F1}/L {targets.LkVar:F1}/C {targets.CkVar:F1})", "성공");
            }
            else
            {
                AddHistory("자동", $"자동운전 시작 (R {RTarget} / L {LTarget} / C {CTarget})", "성공");
            }
        }

        private AutoTargets ComputeTargets()
        {
            if (AutoMode == AutoMode.PowerPf)
            {
                double pf = Math.Min(1.0, Math.Max(0.05, TargetPf));
                double q = TargetPower * Math.Tan(Math.Acos(pf));
                return new AutoTargets { RkW = TargetPower, LkVar = LeadingPf ? 0 : q, CkVar = LeadingPf ? q : 0 };
            }
            return new AutoTargets { RkW = RTarget, LkVar = LTarget, CkVar = CTarget };
        }

        private List<AutoStep> BuildAutoSteps()
        {
            var steps = new List<AutoStep>();
            var panels = SelectedAutoPanel != null
                ? Panels.Where(p => p.IsConnected && p == SelectedAutoPanel)
                : Panels.Where(p => p.IsConnected);
            foreach (var p in panels)
            {
                foreach (var (groups, load) in new[] { (p.RGroups, LoadType.R), (p.LGroups, LoadType.L) })
                {
                    if (p.IsSinglePhase)
                    {
                        for (int i = 0; i < groups[0].Items.Count; i++)
                            steps.Add(new AutoStep
                            {
                                PanelIndex = p.Index,
                                Load = load,
                                Value = groups[0].Items[i].Value * 3.0,
                                Tags = groups.Select(g => g.Items[i].Tag).ToArray()
                            });
                    }
                    else
                    {
                        foreach (var mc in groups[0].Items)
                            steps.Add(new AutoStep { PanelIndex = p.Index, Load = load, Value = mc.Value, Tags = new[] { mc.Tag } });
                    }
                }
                foreach (var c in p.CSteps)
                    steps.Add(new AutoStep { PanelIndex = p.Index, Load = LoadType.C, Value = c.Value, Tags = new[] { c.Tag } });
            }
            return steps;
        }

        private void ApplyPlan(AutoPlan plan)
        {
            _ = ApplyPlanAsync(plan);
        }

        // 계획된 MC만 순차 투입 (R→L→C), 이미 ON인 MC는 건너뜀.
        // OFF해야 할 MC(현재 ON이지만 계획에 없는 것)는 C→L→R 역순으로 개방.
        private async System.Threading.Tasks.Task ApplyPlanAsync(AutoPlan plan)
        {
            var on = new HashSet<string>(plan.OnTags);

            // 투입: 계획에 포함된 MC만, R→L→C 순서
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var typeName in new[] { "R", "L", "C" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: true))
                    {
                        if (!on.Contains(mc.Tag)) continue;    // 계획 외 MC 건너뜀
                        if (mc.State == McState.On) continue;  // 이미 ON
                        if (mc.Load == LoadType.C) { RunCStageAsync(p, mc, true); continue; }
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, true);
                        await System.Threading.Tasks.Task.Delay(1000);
                    }
                }
            }

            // 개방: 현재 ON이지만 계획에 없는 MC, C→L→R 역순
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var typeName in new[] { "C", "L", "R" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: false))
                    {
                        if (on.Contains(mc.Tag) || mc.State != McState.On) continue;
                        if (mc.Load == LoadType.C) { RunCStageAsync(p, mc, false); continue; }
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                        await System.Threading.Tasks.Task.Delay(1000);
                    }
                }
            }

            // 투입/개방 완료 후 미리보기 하이라이트 해제
            ClearPreview();
        }

        private void StopAuto()
        {
            ServiceHub.Auto.Stop();
            _ = SequentialOffAsync("자동", "운전 정지");
        }

        // PNL-1→2→3, 각 패널: C(2→1) → L(TN→SN→RN, 8→1) → R(TN→SN→RN, 8→1)
        private async System.Threading.Tasks.Task SequentialOffAsync(string panel, string eventLabel)
        {
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var typeName in new[] { "C", "L", "R" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: false))
                    {
                        if (mc.State == McState.Off) continue;
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                        await System.Threading.Tasks.Task.Delay(1000);
                    }
                }
            }
            AddHistory(panel, $"{eventLabel} (C→L→R 역순, PNL1→2→3)", "성공");
        }

        // ── State refresh ─────────────────────────────────────────────────────

        private void RefreshConn()
        {
            for (int i = 0; i < Panels.Count; i++)
                Panels[i].IsConnected = ServiceHub.Plc.IsConnected(i);
            RaisePropertyChanged(nameof(PlcCommOk));
            LoadOnCommand.RaiseCanExecuteChanged();
            LoadOffCommand.RaiseCanExecuteChanged();
            MccbOnCommand.RaiseCanExecuteChanged();
            MccbOffCommand.RaiseCanExecuteChanged();
            MccbTripCommand.RaiseCanExecuteChanged();
            ResetCommand.RaiseCanExecuteChanged();
            StartAutoCommand.RaiseCanExecuteChanged();
            StopAutoCommand.RaiseCanExecuteChanged();

            var newPanels = Panels.Where(p => p.IsConnected).ToArray();
            var saved = SelectedAutoPanel;

            // ConnectedPanels 변경 전에 선택 해제: 선택된 판넬이 새 목록에 없으면 먼저 null로 만든다.
            // 순서가 중요하다 — ConnectedPanels(ItemsSource) 변경 후에 null로 만들면
            // ItemsSource 교체 처리 스택 안에서 SelectionChanged→PropertyChanged 연쇄가 발생해
            // WPF reflection 엔진이 TargetException을 던진다.
            if (saved != null && !newPanels.Contains(saved))
                SelectedAutoPanel = null;

            ConnectedPanels = newPanels;   // SelectedItem이 이미 null → SelectionChanged 미발생 → 안전

            RefreshPreview();
        }

        // ── 자동운전 미리보기 ────────────────────────────────────────────────

        private void RefreshPreview()
        {
            if (!IsAuto || !Panels.Any(p => p.IsConnected)) { ClearPreview(); return; }
            var plan = ServiceHub.Auto.Preview(ComputeTargets(), BuildAutoSteps());
            var onSet = new HashSet<string>(plan.OnTags);
            foreach (var p in Panels)
                foreach (var mc in p.AllMcs)
                    mc.IsPlanned = onSet.Contains(mc.Tag);
        }

        private void ClearPreview()
        {
            foreach (var p in Panels)
                foreach (var mc in p.AllMcs)
                    mc.IsPlanned = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AddHistory(string panel, string ev, string result)
        {
            var entry = new HistoryEntry { Time = DateTime.Now, Panel = panel, Event = ev, Result = result };
            History.Insert(0, entry);
            ServiceHub.History.Add(entry);
        }

        private void AddAlarm(string panel, string msg, AlarmLevel level)
        {
            Alarms.Insert(0, new AlarmEntry { Time = DateTime.Now, Panel = panel, Message = msg, Level = level });
            RaisePropertyChanged(nameof(UnackCount));
        }

        private void SeedLogs()
        {
            var t = DateTime.Now;
            Alarms.Add(new AlarmEntry { Time = t.AddSeconds(-24), Panel = "PNL-M", Message = "MCCB-M Trip 발생", Level = AlarmLevel.Trip });
            Alarms.Add(new AlarmEntry { Time = t.AddSeconds(-70), Panel = "PLC2", Message = "PLC 통신 지연 (500ms 초과)", Level = AlarmLevel.Alarm });
            Alarms.Add(new AlarmEntry { Time = t.AddSeconds(-163), Panel = "PLC1", Message = "C 부하 시퀀스 타임아웃", Level = AlarmLevel.Alarm });
            Alarms.Add(new AlarmEntry { Time = t.AddSeconds(-207), Panel = "PNL-3", Message = "원격모드 해제 (로컬 전환)", Level = AlarmLevel.Info });
            Alarms.Add(new AlarmEntry { Time = t.AddSeconds(-263), Panel = "PLC3", Message = "E-Stop 입력 감지", Level = AlarmLevel.Trip });
            Alarms.Add(new AlarmEntry { Time = t.AddSeconds(-297), Panel = "PNL-2", Message = "보호계전기 알람", Level = AlarmLevel.Alarm });

            History.Add(new HistoryEntry { Time = t.AddSeconds(-95),  Panel = "자동", Event = "자동운전 시작",   Result = "성공" });
            History.Add(new HistoryEntry { Time = t.AddSeconds(-194), Panel = "PLC2", Event = "R 부하 4단 ON",   Result = "성공" });
            History.Add(new HistoryEntry { Time = t.AddSeconds(-195), Panel = "PLC2", Event = "L 부하 2단 ON",   Result = "성공" });
            History.Add(new HistoryEntry { Time = t.AddSeconds(-209), Panel = "PLC1", Event = "C 부하 1단 ON",   Result = "성공" });
            History.Add(new HistoryEntry { Time = t.AddSeconds(-284), Panel = "수동", Event = "MCCB ON",         Result = "성공" });
            History.Add(new HistoryEntry { Time = t.AddSeconds(-297), Panel = "PLC3", Event = "원격모드 설정",   Result = "성공" });
            foreach (var h in History) ServiceHub.History.Add(h);
            RaisePropertyChanged(nameof(UnackCount));
        }
    }
}
