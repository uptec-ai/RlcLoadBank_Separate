using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using SciChart.Charting.Model.DataSeries;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;
using System.Reflection;
using SciChart.Data.Model;

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

        // DB 기록 토글: ON = 모든 로그 저장 / OFF = Critical(알람·세션)만 저장.
        // 변경 즉시 app.config(Db.FullLogging)에 영속.
        public bool DbLoggingOn
        { get => GetValue<bool>(); set => SetValue(value, () => ServiceHub.DbWriter.SaveFullLogging(DbLoggingOn)); }
        // RLC_DB_CONN 환경변수 미설정이면 DB 기능 자체가 꺼져 토글 비활성.
        public bool DbAvailable => ServiceHub.DbWriter.Enabled;

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

        // Y-axis visible range — expands when data exceeds current max, never shrinks below -1..10
        public DoubleRange PowerYAxisRange
        {
            get => GetProperty(() => PowerYAxisRange);
            set => SetProperty(() => PowerYAxisRange, value);
        }

        // ── RLC operational state (computed from MC feedback states) ──────────
        // True when any connected panel has at least one MC confirmed ON via FB.
        public bool IsRlcOn    => Panels.Any(p => p.IsConnected && p.AllMcs.Any(m => m.State == McState.On));
        // True while any Modbus write is pending PLC confirmation (COMM WAIT).
        public bool IsCommWait => Panels.Any(p => p.AllMcs.Any(m => m.State == McState.CommWait));
        // True only after an explicit manual/stop OFF action — cleared on next ON or disconnect.
        private bool _rlcOffSignaled;
        public bool IsRlcOff   => PlcCommOk && _rlcOffSignaled && !IsRlcOn;
        // Sequence direction chips: green while the sequence runs, ghost after it ends.
        public bool IsOnSequenceActive  { get => GetValue<bool>(); set => SetValue(value); }
        public bool IsOffSequenceActive { get => GetValue<bool>(); set => SetValue(value); }
        // Tracks which protection tags have already fired to prevent repeated triggers per signal.
        private readonly HashSet<string> _activeProtections = new HashSet<string>();
        private CancellationTokenSource _seqCts;
        public bool IsSequenceRunning
        {
            get => GetValue<bool>();
            set => SetValue(value, RefreshOperationCommands);
        }
        // True while any unacknowledged Trip alarm exists.
        public bool HasTrip    => Alarms.Any(a => a.Level == AlarmLevel.Trip  && !a.Acknowledged);
        // True while any unacknowledged Alarm (non-Trip) exists.
        public bool HasAlarm   => Alarms.Any(a => a.Level == AlarmLevel.Alarm && !a.Acknowledged);
        // Operation gate: PLC connected AND no unacknowledged alarms.
        public bool CanOperate => PlcCommOk && UnackCount == 0;

        public DelegateCommand SetAutoCommand { get; }
        public DelegateCommand SetManualCommand { get; }
        public DelegateCommand<string> LoadOnCommand { get; }
        public DelegateCommand<string> LoadOffCommand { get; }
        public DelegateCommand MccbOnCommand { get; }
        public DelegateCommand MccbOffCommand { get; }
        public DelegateCommand MccbTripCommand { get; }
        public DelegateCommand ResetCommand { get; }
        public DelegateCommand StartAutoCommand { get; }
        public DelegateCommand TerminateAutoCommand { get; }
        public DelegateCommand AckAlarmsCommand { get; }
        public DelegateCommand ClearAlarmsCommand { get; }
        public DelegateCommand ClearHistoryCommand { get; }
        public DelegateCommand AbortSequenceCommand { get; }
        public DelegateCommand OpenConnectionCommand { get; }
        public DelegateCommand SetAutoIndividualCommand { get; }
        public DelegateCommand SetAutoPowerPfCommand { get; }

        public RlcStatusViewModel()
        {
            Panels.Add(new PanelViewModel(0, "PLC1 / PNL-1", true, ServiceHub.Plc.IsConnected(0)));
            Panels.Add(new PanelViewModel(1, "PLC2 / PNL-2", false, ServiceHub.Plc.IsConnected(1)));
            Panels.Add(new PanelViewModel(2, "PLC3 / PNL-3", false, ServiceHub.Plc.IsConnected(2)));
            foreach (var p in Panels)
            {
                p.McToggleRequested     = OnMcToggle;
                p.CStageToggleRequested = OnCStageToggle;
            }

            Pnl1PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-1 (kW)", FifoCapacity = 300 };
            Pnl2PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-2 (kW)", FifoCapacity = 300 };
            Pnl3PowerSeries = new XyDataSeries<DateTime, double> { SeriesName = "PNL-3 (kW)", FifoCapacity = 300 };
            PowerYAxisRange = new DoubleRange(-1, 10);

            EStopOk = true; AlarmWait = false; HeartbeatOk = true;
            DbLoggingOn = ServiceHub.DbWriter.FullLogging;   // app.config에서 복원된 값
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
            SetAutoCommand   = new DelegateCommand(() => SetMode(OperationMode.Auto));
            SetManualCommand = new DelegateCommand(() => SetMode(OperationMode.Manual));
            LoadOnCommand  = new DelegateCommand<string>(t => ManualLoad(t, true),  _ => CanOperate);
            LoadOffCommand = new DelegateCommand<string>(t => ManualLoad(t, false), _ => CanOperate);
            MccbOnCommand  = new DelegateCommand(() => Mccb("ON"),   () => CanOperate);
            MccbOffCommand = new DelegateCommand(() => Mccb("OFF"),  () => CanOperate);
            MccbTripCommand= new DelegateCommand(() => Mccb("TRIP"), () => CanOperate);
            ResetCommand   = new DelegateCommand(ResetAll, () => CanOperate);
            StartAutoCommand    = new DelegateCommand(StartAuto,       () => CanOperate && IsAuto);
            TerminateAutoCommand= new DelegateCommand(TerminateAuto,   () => CanOperate);
            AckAlarmsCommand = new DelegateCommand(() =>
            {
                foreach (var a in Alarms) a.Acknowledged = true;
                RefreshFaultState();
            });
            ClearAlarmsCommand  = new DelegateCommand(() => { Alarms.Clear(); RefreshFaultState(); });
            ClearHistoryCommand = new DelegateCommand(() => History.Clear());
            AbortSequenceCommand = new DelegateCommand(() => _seqCts?.Cancel(), () => IsSequenceRunning);
            OpenConnectionCommand  = new DelegateCommand(OpenConnection);
            SetAutoIndividualCommand = new DelegateCommand(() => SetAutoMode(AutoMode.Individual));
            SetAutoPowerPfCommand    = new DelegateCommand(() => SetAutoMode(AutoMode.PowerPf));

            Mode = OperationMode.Auto;

            ServiceHub.Metering.GimacDataReceived += OnGimacDataReceived;

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

            // R/L MC 피드백
            var mc = panel.FindMc(fb.McTag);
            if (mc != null)
            {
                mc.State = fb.On ? McState.On : McState.Off;
                panel.RefreshActiveCapacity();
                RefreshRlcState();
                return;
            }

            // C부하 RESULT / MC1·MC2·SCR 상태 DI 피드백
            if (panel.TryApplyCFeedback(fb.McTag, fb.On))
            {
                string alarmKind = null;
                if      (fb.McTag.EndsWith("_MC1_FB")) alarmKind = "MC1";
                else if (fb.McTag.EndsWith("_MC2_FB")) alarmKind = "MC2";
                else if (fb.McTag.EndsWith("_SCR_FB")) alarmKind = "SCR";

                if (alarmKind != null)
                {
                    if (fb.On)
                    {
                        // MC1/MC2/SCR 알람 ON 전환 시 트립·알람 패널에도 표출
                        var cs = panel.CSteps.FirstOrDefault(c => fb.McTag.StartsWith(c.Tag + "_"));
                        string label = cs?.Label ?? fb.McTag;
                        AddAlarm(panel.Title, $"{label} {alarmKind} 알람 발생", AlarmLevel.Alarm,
                                 dbType: "C_" + alarmKind, panelNo: panel.Index + 1);
                    }
                    else
                    {
                        // 하강 에지 → 알람 에피소드 종료
                        ServiceHub.DbLog.AlarmCleared(panel.Index + 1, "C_" + alarmKind);
                    }
                }
                RefreshRlcState();
                return;
            }

            // 보호·상태 DI 피드백
            if (fb.McTag.EndsWith("_FB", StringComparison.OrdinalIgnoreCase))
            {
                bool wasRemote = panel.IsRemote;
                panel.ApplyStatusFeedback(fb.McTag, fb.On);
                // Local/Remote 전환 이력 (spec: MODE_CHANGE)
                if (fb.McTag == $"P{panel.Index + 1}_LOC_REM_FB" && panel.IsRemote != wasRemote)
                    ServiceHub.DbLog.LogOperation(panel.Index + 1, "SYSTEM", "MODE_CHANGE", "성공",
                        detailJson: $"{{\"from\":\"{(wasRemote ? "REMOTE" : "LOCAL")}\",\"to\":\"{(panel.IsRemote ? "REMOTE" : "LOCAL")}\"}}");
                RefreshStatusFlags();
                OnProtectionFeedback(panel, fb.McTag, fb.On);
            }
        }

        // ── 보호동작 보조 시퀀스 (spec §5.1) ─────────────────────────────────

        private void OnProtectionFeedback(PanelViewModel panel, string fbTag, bool isOn)
        {
            int p = panel.Index + 1;
            bool isProtTag = fbTag == $"P{p}_EMG_FB"       ||
                             fbTag == $"P{p}_MCCB_TRIP_FB" ||
                             fbTag == $"P{p}_OVR_FB"       ||
                             fbTag == $"P{p}_OCR_FB"       ||
                             fbTag == $"P{p}_HT_FB";
            if (!isProtTag) return;

            if (isOn)
            {
                if (!_activeProtections.Add(fbTag)) return;  // Rising edge only
                TriggerProtectionAction(panel, fbTag);
            }
            else
            {
                // Falling edge: reset + DB 알람 에피소드 종료
                if (_activeProtections.Remove(fbTag))
                    ServiceHub.DbLog.AlarmCleared(p, ProtectionTypeOf(fbTag, p));
            }
        }

        // P{p}_EMG_FB → "EMG" 등 tb_alarm_event.alarm_type 토큰
        private static string ProtectionTypeOf(string fbTag, int p) =>
            fbTag == $"P{p}_EMG_FB"       ? "EMG"       :
            fbTag == $"P{p}_MCCB_TRIP_FB" ? "MCCB_TRIP" :
            fbTag == $"P{p}_OVR_FB"       ? "OVR"       :
            fbTag == $"P{p}_OCR_FB"       ? "OCR"       :
            fbTag == $"P{p}_HT_FB"        ? "HT"        : fbTag;

        private void TriggerProtectionAction(PanelViewModel panel, string fbTag)
        {
            _seqCts?.Cancel();
            int p = panel.Index + 1;

            if (fbTag == $"P{p}_EMG_FB")
            {
                AddAlarm("ALL", "비상정지(EMG) 발생 → 전체 부하 자동 차단", AlarmLevel.Trip,
                         dbType: "EMG", panelNo: p);
                ServiceHub.Auto.Stop();
                _ = EmergencyOffAsync();
                return;
            }

            string reason, msg;
            AlarmLevel level;
            if      (fbTag == $"P{p}_MCCB_TRIP_FB") { reason = "MCCB TRIP"; msg = "MCCB 트립 발생 → 부하 자동 차단";      level = AlarmLevel.Trip;  }
            else if (fbTag == $"P{p}_OVR_FB")        { reason = "OVR";       msg = "과전압(OVR) 보호 동작 → 부하 자동 차단"; level = AlarmLevel.Trip;  }
            else if (fbTag == $"P{p}_OCR_FB")        { reason = "OCR";       msg = "과전류(OCR) 보호 동작 → 부하 자동 차단"; level = AlarmLevel.Trip;  }
            else if (fbTag == $"P{p}_HT_FB")         { reason = "HT";        msg = "과열(HT) 보호 동작 → 부하 차단";        level = AlarmLevel.Alarm; }
            else return;

            AddAlarm(panel.Title, msg, level, dbType: ProtectionTypeOf(fbTag, p), panelNo: p);
            _ = ProtectionOffAsync(panel, reason);
        }

        // EMG: 지연 없이 모든 판넬 즉시 차단
        private System.Threading.Tasks.Task EmergencyOffAsync()
        {
            ServiceHub.DbLog.McCommandContext = "SYSTEM";
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var cs in CStagesOrdered(p, false))
                    if (cs.IsRunning) ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", false);
                foreach (var typeName in new[] { "L", "R" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: false))
                    {
                        if (mc.State == McState.Off) continue;
                        mc.State = McState.CommWait;
                        p.RefreshActiveCapacity();
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                    }
                }
            }
            _rlcOffSignaled = true;
            RefreshRlcState();
            ClearPreview();
            AddHistory("ALL", "EMG-STOP 보호동작 자동 차단 (즉시)", "성공");
            LogOp("ALL_OFF", "성공", mode: "SYSTEM", detailJson: "{\"trigger\":\"EMG\"}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        // 개별 판넬 보호동작: C→L→R 역순, 500ms 간격
        private async System.Threading.Tasks.Task ProtectionOffAsync(PanelViewModel panel, string reason)
        {
            ServiceHub.DbLog.McCommandContext = "SYSTEM";
            foreach (var cs in CStagesOrdered(panel, false))
            {
                if (cs.IsRunning)
                {
                    ServiceHub.Plc.WriteMcCommand(panel.Index, $"{cs.Tag}_CMD", false);
                    await System.Threading.Tasks.Task.Delay(500);
                }
            }
            foreach (var typeName in new[] { "L", "R" })
            {
                foreach (var mc in TypeMcsOrdered(panel, typeName, forward: false))
                {
                    if (mc.State == McState.Off) continue;
                    mc.State = McState.CommWait;
                    panel.RefreshActiveCapacity();
                    ServiceHub.Plc.WriteMcCommand(panel.Index, mc.Tag, false);
                    await System.Threading.Tasks.Task.Delay(500);
                }
            }
            _rlcOffSignaled = true;
            RefreshRlcState();
            ClearPreview();
            AddHistory(panel.Title, $"{reason} 보호동작 자동 차단 (C→L→R 역순)", "성공");
            LogOp("ALL_OFF", "성공", panel, "SYSTEM",
                  detailJson: $"{{\"trigger\":\"{reason.Replace(' ', '_')}\"}}");
        }

        private void RefreshStatusFlags()
        {
            EStopOk   = Panels.All(p => !p.EmgActive);
            AlarmWait = Panels.Any(p => p.HasFault);
            RefreshRlcState();
        }

        private void OnClockTick(object sender, EventArgs e)
        {
            Now = DateTime.Now;
            RefreshRlcState();
        }

        private void OnGimacDataReceived(object sender, GimacReading r)
        {
            double kw = r.ActivePower / 1000.0;
            switch (r.Device.UnitId)
            {
                case 1: Pnl1PowerSeries.Append(r.Timestamp, kw); break;
                case 2: Pnl2PowerSeries.Append(r.Timestamp, kw); break;
                case 3: Pnl3PowerSeries.Append(r.Timestamp, kw); break;
            }
            // 패널 다이어그램 계측 데이터 반영 (UnitId 1→Panel 0, 2→Panel 1, 3→Panel 2)
            var panel = Panels.FirstOrDefault(p => p.Index == r.Device.UnitId - 1);
            panel?.ApplyGimacReading(r);
            UpdatePowerYAxisRange(kw);
        }

        private void UpdatePowerYAxisRange(double kw)
        {
            const double minTop  = 10.0;
            const double padding = 0.1;
            double needed = kw * (1.0 + padding);
            if (needed > PowerYAxisRange.Max)
                PowerYAxisRange = new DoubleRange(-1, Math.Max(minTop, needed));
        }

        private void RefreshRlcState()
        {
            RaisePropertyChanged(nameof(IsRlcOn));
            RaisePropertyChanged(nameof(IsRlcOff));
            RaisePropertyChanged(nameof(IsCommWait));
        }

        private void RefreshFaultState()
        {
            RaisePropertyChanged(nameof(HasTrip));
            RaisePropertyChanged(nameof(HasAlarm));
            RaisePropertyChanged(nameof(UnackCount));
            RaisePropertyChanged(nameof(CanOperate));
            RefreshOperationCommands();
        }

        private void RefreshOperationCommands()
        {
            LoadOnCommand?.RaiseCanExecuteChanged();
            LoadOffCommand?.RaiseCanExecuteChanged();
            MccbOnCommand?.RaiseCanExecuteChanged();
            MccbOffCommand?.RaiseCanExecuteChanged();
            MccbTripCommand?.RaiseCanExecuteChanged();
            ResetCommand?.RaiseCanExecuteChanged();
            StartAutoCommand?.RaiseCanExecuteChanged();
            TerminateAutoCommand?.RaiseCanExecuteChanged();
            AbortSequenceCommand?.RaiseCanExecuteChanged();
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
            if (!CanOperate) return;   // 미확인 알람 존재 시 개별 MC 조작 차단
            if (mc.State == McState.CommWait) return;

            if (IsManual)
            {
                // 수동: ON/OFF 자유 토글
                bool turnOn = mc.State != McState.On;
                if (turnOn && !panel.MccbOn)
                {
                    DXMessageBox.Show($"{panel.Title} MCCB가 OFF 상태입니다.\nMCCB를 먼저 투입하세요.", "MC 투입 불가",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                string act = turnOn ? "투입(ON)" : "개방(OFF)";
                if (DXMessageBox.Show($"{panel.Title}  {mc.Label} 을(를) {act} 하시겠습니까?", "MC 조작 확인",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                    return;
                mc.State = McState.CommWait;
                ServiceHub.DbLog.McCommandContext = "MANUAL";
                ServiceHub.Plc.WriteMcCommand(panel.Index, mc.Tag, turnOn);
                AddHistory(panel.Title, $"{mc.Label} {(turnOn ? "ON" : "OFF")}", "성공");
                LogOp(turnOn ? "MC_ON" : "MC_OFF", "성공", panel, "MANUAL", LoadTypeOf(mc.Tag), mc.Tag);
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
                mc.State = McState.CommWait;
                ServiceHub.DbLog.McCommandContext = "MANUAL";   // 사용자 수동 개입
                ServiceHub.Plc.WriteMcCommand(panel.Index, mc.Tag, false);
                AddHistory(panel.Title, $"[자동] {mc.Label} 강제 개방", "성공");
                LogOp("MC_OFF", "성공", panel, "MANUAL", LoadTypeOf(mc.Tag), mc.Tag,
                      detailJson: "{\"forced\":true,\"during\":\"AUTO\"}");
            }
            else
            {
                DXMessageBox.Show("운전 모드를 먼저 설정하세요.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── C-stage toggle ────────────────────────────────────────────────────
        // PLC가 내부 시퀀스(MC1→SCR→MC2) 담당. HMI는 C{n}_CMD 단일 신호만 전송.
        // 결과는 RESULT DI로 수신 → 확인되면 "성공", 타임아웃이면 "진행중" 유지 + 알람.

        private async void OnCStageToggle(PanelViewModel panel, CStageViewModel cs)
        {
            if (!panel.IsConnected)
            {
                DXMessageBox.Show($"{panel.Title} 미연결 상태입니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!CanOperate) return;   // 미확인 알람 존재 시 C-stage 조작 차단
            if (!IsManual)
            {
                DXMessageBox.Show("C부하는 수동 모드에서만 UI에서 조작할 수 있습니다.", "조작 불가",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool turnOn = !cs.IsRunning;
            if (turnOn && !panel.MccbOn)
            {
                DXMessageBox.Show($"{panel.Title} MCCB가 OFF 상태입니다.\nMCCB를 먼저 투입하세요.", "C부하 투입 불가",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string act = turnOn ? "투입(ON)" : "개방(OFF)";
            if (DXMessageBox.Show($"{panel.Title}  {cs.Label} 을(를) {act} 하시겠습니까?", "C부하 조작 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            // 이력 "진행중" 등록 → C_RESULT 피드백 확인 시 "성공"으로 갱신
            var entry      = AddHistory(panel.Title, $"{cs.Label} {act}", "진행중");
            string resTtag = $"{cs.Tag}_RESULT";
            ServiceHub.DbLog.McCommandContext = "MANUAL";
            ServiceHub.Plc.WriteMcCommand(panel.Index, $"{cs.Tag}_CMD", turnOn);

            // C_RESULT DI 피드백 대기 (T=동작, F=멈춤)
            const int timeoutMs = 5000;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<McFeedback> handler = (s, fb) =>
            {
                if (fb.PanelIndex == panel.Index && fb.McTag == resTtag && fb.On == turnOn)
                    tcs.TrySetResult(true);
            };
            _subscribedPlc.FeedbackReceived += handler;
            try
            {
                // 이미 원하는 상태면 즉시 성공 (동일 상태 쓰기 → 코일 변화 없음)
                if (cs.IsRunning == turnOn) tcs.TrySetResult(true);

                if (await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)) == tcs.Task)
                {
                    entry.Result = "성공";
                }
                else
                {
                    entry.Result = "실패";
                    AddAlarm(panel.Title, $"{cs.Label} {act} 피드백 타임아웃", AlarmLevel.Alarm,
                             dbType: "CMD_FB_MISMATCH", panelNo: panel.Index + 1, instant: true);
                }
                LogOp(turnOn ? "MC_ON" : "MC_OFF", entry.Result, panel, "MANUAL", "C", cs.Tag);
            }
            finally
            {
                _subscribedPlc.FeedbackReceived -= handler;
            }
        }

        // ── Manual load control ───────────────────────────────────────────────

        private CancellationToken BeginSequence()
        {
            _seqCts = new CancellationTokenSource();
            IsSequenceRunning = true;
            return _seqCts.Token;
        }

        private void EndSequence()
        {
            IsSequenceRunning = false;
            IsOnSequenceActive  = false;
            IsOffSequenceActive = false;
            _seqCts?.Dispose();
            _seqCts = null;
        }

        private static void ShowSequenceRunningMessage() =>
            DXMessageBox.Show(
                "MC 투입/차단 시퀀스가 진행 중입니다.\n[■ 시퀀스 중지] 버튼으로 먼저 멈춰주세요.",
                "동작 불가", MessageBoxButton.OK, MessageBoxImage.Warning);

        private async Task WrapSequenceAsync(Func<CancellationToken, Task> body,
            string historyPanel, string cancelMsg, CancellationToken ct,
            Action onCancel = null, HistoryEntry inProgress = null, Action<string> logOp = null)
        {
            try
            {
                await body(ct);
                if (inProgress != null) inProgress.Result = "성공";
                logOp?.Invoke("성공");
            }
            catch (OperationCanceledException)
            {
                onCancel?.Invoke();
                if (inProgress != null) inProgress.Result = "중단";
                else AddHistory(historyPanel, cancelMsg, "중단");
                logOp?.Invoke("중단");
            }
            finally { EndSequence(); }
        }

        // 선택된 판넬만 반환. null(전체 판넬) → 모든 연결 판넬.
        private IEnumerable<PanelViewModel> GetTargetPanels() =>
            SelectedAutoPanel != null
                ? Panels.Where(p => p.IsConnected && p == SelectedAutoPanel)
                : Panels.Where(p => p.IsConnected);

        private void ManualLoad(string type, bool on)
        {
            if (IsSequenceRunning) { ShowSequenceRunningMessage(); return; }
            if (!IsManual)
            {
                DXMessageBox.Show("수동 모드에서만 조작할 수 있습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string act  = on ? "ON" : "OFF";
            string target = SelectedAutoPanel?.Title ?? "전체 판넬";

            if (on)
            {
                var mccbOff = GetTargetPanels().Where(p => !p.MccbOn).ToList();
                if (mccbOff.Any())
                {
                    string names = string.Join(", ", mccbOff.Select(p => p.Title));
                    DXMessageBox.Show($"다음 판넬의 MCCB가 OFF 상태입니다:\n{names}\n\nMCCB를 먼저 투입하세요.", "MC 투입 불가",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (DXMessageBox.Show($"[{target}] {type} 부하를 {act} 하시겠습니까?", "부하 제어 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            string evtText = on ? $"[{target}] {type} 부하 ON 순차" : $"[{target}] {type} 부하 OFF 순차 (역순)";
            var entry = AddHistory("수동", evtText, "진행중");
            IsOnSequenceActive  = on;
            IsOffSequenceActive = !on;
            ServiceHub.DbLog.McCommandContext = "MANUAL";
            var ct = BeginSequence();
            _ = WrapSequenceAsync(
                t => on ? ManualLoadOnAsync(type, t) : ManualLoadOffAsync(type, t),
                "수동", $"[{target}] {type} 부하 {act} 시퀀스 중단", ct, inProgress: entry,
                logOp: res => LogOp(on ? "SEQ_ON" : "SEQ_OFF", res, SelectedAutoPanel, "MANUAL", type, target));
        }

        // 선택 판넬(또는 전체) 순차 ON: PNL-1→2→3 순서
        private async Task ManualLoadOnAsync(string type, CancellationToken ct)
        {
            _rlcOffSignaled = false;
            foreach (var p in GetTargetPanels().OrderBy(p => p.Index))
            {
                if (type == "C")
                {
                    foreach (var cs in CStagesOrdered(p, true))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (cs.IsRunning) continue;
                        ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", true);
                        await Task.Delay(1000, ct);
                    }
                }
                else
                {
                    foreach (var mc in TypeMcsOrdered(p, type, forward: true))
                    {
                        ct.ThrowIfCancellationRequested();
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, true);
                        await Task.Delay(1000, ct);
                    }
                }
            }
        }

        // 선택 판넬(또는 전체) 순차 OFF: 역순
        private async Task ManualLoadOffAsync(string type, CancellationToken ct)
        {
            foreach (var p in GetTargetPanels().OrderBy(p => p.Index))
            {
                if (type == "C")
                {
                    foreach (var cs in CStagesOrdered(p, false))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!cs.IsRunning) continue;
                        ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", false);
                        await Task.Delay(1000, ct);
                    }
                }
                else
                {
                    foreach (var mc in TypeMcsOrdered(p, type, forward: false))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (mc.State == McState.Off) continue;
                        mc.State = McState.CommWait;
                        p.RefreshActiveCapacity();
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                        await Task.Delay(1000, ct);
                    }
                }
            }
            _rlcOffSignaled = true;
            RefreshRlcState();
        }

        // R/L MC를 정방향/역방향으로 반환 (C부하는 별도 CStagesOrdered 사용)
        // PNL-1 R/L: forward=RN→SN→TN 순, backward=TN→SN→RN 순, 각 그룹 내 스텝도 동일 방향
        // PNL-2/3: 단일 그룹, 스텝 방향만 적용
        private static IEnumerable<McViewModel> TypeMcsOrdered(PanelViewModel p, string type, bool forward)
        {
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

        private static IEnumerable<CStageViewModel> CStagesOrdered(PanelViewModel p, bool forward)
        {
            var list = p.CSteps.ToList();
            if (!forward) list.Reverse();
            return list;
        }

        private void Mccb(string kind)
        {
            if (IsSequenceRunning) { ShowSequenceRunningMessage(); return; }
            if (DXMessageBox.Show($"MCCB {kind} 명령을 실행하시겠습니까?", "MCCB 제어 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            ServiceHub.DbLog.McCommandContext = "MANUAL";
            foreach (var p in GetTargetPanels())
            {
                ServiceHub.Plc.WriteMcCommand(p.Index, $"P{p.Index + 1}_MCCB_{kind}_CMD", true);
                // Optimistic UI update — real DI-FB will confirm/correct within poll cycle
                if (kind == "ON")        p.ApplyStatusFeedback($"P{p.Index + 1}_MCCB_ON_FB",   true);
                else if (kind == "OFF")  p.ApplyStatusFeedback($"P{p.Index + 1}_MCCB_ON_FB",   false);
                else if (kind == "TRIP") p.ApplyStatusFeedback($"P{p.Index + 1}_MCCB_TRIP_FB", true);
                AddHistory(p.Title, $"MCCB {kind}", "성공");
                LogOp("MCCB_" + kind, "성공", p, "MANUAL");
            }
            if (kind == "TRIP") AddAlarm(SelectedAutoPanel?.Title ?? "ALL", "MCCB TRIP 명령", AlarmLevel.Trip,
                                         dbType: "MCCB_TRIP_CMD", panelNo: SelectedAutoPanel?.Index + 1, instant: true);
            RefreshStatusFlags();
        }

        private void ResetAll()
        {
            if (IsSequenceRunning) { ShowSequenceRunningMessage(); return; }
            if (DXMessageBox.Show("전체 부하를 OFF(Reset) 하시겠습니까?", "Reset 확인",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            var entry = AddHistory("수동", "RESET 전체 OFF (C→L→R 역순)", "진행중");
            IsOnSequenceActive  = false;
            IsOffSequenceActive = true;
            ServiceHub.DbLog.McCommandContext = "MANUAL";
            var ct = BeginSequence();
            _ = WrapSequenceAsync(t => SequentialOffAsync(t), "수동", "RESET 시퀀스 중단", ct, inProgress: entry,
                logOp: res => LogOp("ALL_OFF", res, mode: "MANUAL", detailJson: "{\"trigger\":\"RESET\"}"));
        }

        // ── Auto operation ────────────────────────────────────────────────────

        private void StartAuto()
        {
            if (IsSequenceRunning) { ShowSequenceRunningMessage(); return; }
            if (!Panels.Any(p => p.IsConnected))
            {
                DXMessageBox.Show("연결된 PLC가 없습니다.", "자동운전", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var mccbOff = GetTargetPanels().Where(p => !p.MccbOn).ToList();
            if (mccbOff.Any())
            {
                string names = string.Join(", ", mccbOff.Select(p => p.Title));
                DXMessageBox.Show($"다음 판넬의 MCCB가 OFF 상태입니다:\n{names}\n\nMCCB를 먼저 투입하세요.", "자동운전 불가",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (DXMessageBox.Show("자동운전을 시작하시겠습니까?\n\nR/L/C 부하가 순차적으로 투입됩니다.",
                    "자동운전 시작 확인", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;
            _rlcOffSignaled = false;
            var targets = ComputeTargets();
            var steps = BuildAutoSteps();
            var plan = ServiceHub.Auto.Start(targets, steps);

            HistoryEntry entry;
            if (AutoMode == AutoMode.PowerPf)
            {
                RTarget = targets.RkW; LTarget = targets.LkVar; CTarget = targets.CkVar;
                entry = AddHistory("자동", $"자동운전 시작 (P {TargetPower}kW · PF {TargetPf}{(LeadingPf ? " 진상" : " 지상")} → R {targets.RkW:F1}/L {targets.LkVar:F1}/C {targets.CkVar:F1})", "진행중");
            }
            else
            {
                entry = AddHistory("자동", $"자동운전 시작 (R {RTarget} / L {LTarget} / C {CTarget})", "진행중");
            }

            IsOnSequenceActive  = true;
            IsOffSequenceActive = false;
            ServiceHub.DbLog.McCommandContext = "AUTO";
            string autoDetail = System.Text.Json.JsonSerializer.Serialize(new
            {
                autoMode = AutoMode.ToString(),
                target_r_kw = targets.RkW, target_l_kvar = targets.LkVar, target_c_kvar = targets.CkVar,
                planned_tags = plan.OnTags.Count,
            });
            var ct = BeginSequence();
            _ = WrapSequenceAsync(t => ApplyPlanAsync(plan, t), "자동", "자동운전 시퀀스 중단", ct, ClearPreview, entry,
                logOp: res => LogOp("AUTO_COMPLETE", res, SelectedAutoPanel, "AUTO", detailJson: autoDetail));
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
                foreach (var cs in p.CSteps)
                    steps.Add(new AutoStep { PanelIndex = p.Index, Load = LoadType.C, Value = cs.Value, Tags = new[] { cs.Tag } });
            }
            return steps;
        }

        // 계획된 MC만 순차 투입 (R→L→C), 이미 ON인 MC는 건너뜀.
        // OFF해야 할 MC(현재 ON이지만 계획에 없는 것)는 C→L→R 역순으로 개방.
        // 두 루프 모두 GetTargetPanels()로 제한 — 선택되지 않은 판넬의 MC는 건드리지 않는다.
        private async Task ApplyPlanAsync(AutoPlan plan, CancellationToken ct)
        {
            var on = new HashSet<string>(plan.OnTags);

            // 투입: 계획에 포함된 MC만, R→L→C 순서 (MCCB OFF 판넬은 건너뜀)
            foreach (var p in GetTargetPanels().OrderBy(p => p.Index))
            {
                if (!p.MccbOn) continue;
                foreach (var typeName in new[] { "R", "L" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: true))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!on.Contains(mc.Tag)) continue;
                        if (mc.State == McState.On) continue;
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, true);
                        await Task.Delay(1000, ct);
                    }
                }
                foreach (var cs in CStagesOrdered(p, true))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!on.Contains(cs.Tag)) continue;
                    if (cs.IsRunning) continue;
                    ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", true);
                    await Task.Delay(1000, ct);
                }
            }

            // 개방: 현재 ON이지만 계획에 없는 MC, C→L→R 역순 (선택된 판넬만)
            foreach (var p in GetTargetPanels().OrderBy(p => p.Index))
            {
                foreach (var cs in CStagesOrdered(p, false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (on.Contains(cs.Tag) || !cs.IsRunning) continue;
                    ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", false);
                    await Task.Delay(1000, ct);
                }
                foreach (var typeName in new[] { "L", "R" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: false))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (on.Contains(mc.Tag) || mc.State != McState.On) continue;
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                        await Task.Delay(1000, ct);
                    }
                }
            }

            // 투입/개방 완료 후 미리보기 하이라이트 해제
            ClearPreview();
        }

        // PNL-1→2→3, 각 패널: C(2→1) → L(TN→SN→RN, 8→1) → R(TN→SN→RN, 8→1)
        private async Task SequentialOffAsync(CancellationToken ct)
        {
            foreach (var p in Panels.Where(p => p.IsConnected).OrderBy(p => p.Index))
            {
                foreach (var cs in CStagesOrdered(p, false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!cs.IsRunning) continue;
                    ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", false);
                    await Task.Delay(1000, ct);
                }
                foreach (var typeName in new[] { "L", "R" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: false))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (mc.State == McState.Off) continue;
                        mc.State = McState.CommWait;
                        p.RefreshActiveCapacity();
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                        await Task.Delay(1000, ct);
                    }
                }
            }
            _rlcOffSignaled = true;
            RefreshRlcState();
        }

        // 선택 판넬(또는 전체)의 현재 ON MC를 C→L→R 역순으로 1초 간격 OFF
        private void TerminateAuto()
        {
            if (IsSequenceRunning) { ShowSequenceRunningMessage(); return; }
            var targets = GetTargetPanels().ToList();
            bool anyOn = targets.Any(p => p.AllMcs.Any(m => m.State == McState.On) || p.CSteps.Any(cs => cs.IsRunning));
            if (!anyOn)
            {
                string panelName = SelectedAutoPanel?.Title ?? "전체 판넬";
                DXMessageBox.Show($"{panelName}에 켜진 MC가 없습니다.", "운전 종료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string confirmPanel = SelectedAutoPanel?.Title ?? "전체 판넬";
            if (DXMessageBox.Show($"운전을 종료하시겠습니까?\n\n[{confirmPanel}] C→L→R 순으로 부하가 차단됩니다.",
                    "운전 종료 확인", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
            ServiceHub.Auto.Stop();
            string panelLabel = SelectedAutoPanel?.Title ?? "전체";
            var entry = AddHistory(panelLabel, "운전 종료 (C→L→R 역순)", "진행중");
            IsOnSequenceActive  = false;
            IsOffSequenceActive = true;
            ServiceHub.DbLog.McCommandContext = "MANUAL";   // 사용자 종료 조작
            var ct = BeginSequence();
            _ = WrapSequenceAsync(t => TerminateAsync(targets, t), panelLabel, "운전 종료 시퀀스 중단", ct, ClearPreview, entry,
                logOp: res => LogOp("ALL_OFF", res, SelectedAutoPanel, "MANUAL", detailJson: "{\"trigger\":\"TERMINATE\"}"));
        }

        private async Task TerminateAsync(IEnumerable<PanelViewModel> panels, CancellationToken ct)
        {
            foreach (var p in panels.OrderBy(p => p.Index))
            {
                foreach (var cs in CStagesOrdered(p, false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!cs.IsRunning) continue;
                    ServiceHub.Plc.WriteMcCommand(p.Index, $"{cs.Tag}_CMD", false);
                    await Task.Delay(1000, ct);
                }
                foreach (var typeName in new[] { "L", "R" })
                {
                    foreach (var mc in TypeMcsOrdered(p, typeName, forward: false))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (mc.State == McState.Off) continue;
                        mc.State = McState.CommWait;
                        ServiceHub.Plc.WriteMcCommand(p.Index, mc.Tag, false);
                        await Task.Delay(1000, ct);
                    }
                }
            }
            _rlcOffSignaled = true;
            RefreshRlcState();
            ClearPreview();
        }

        // ── State refresh ─────────────────────────────────────────────────────

        private void RefreshConn()
        {
            for (int i = 0; i < Panels.Count; i++)
                Panels[i].IsConnected = ServiceHub.Plc.IsConnected(i);
            RaisePropertyChanged(nameof(PlcCommOk));
            RaisePropertyChanged(nameof(CanOperate));
            RefreshRlcState();
            RefreshOperationCommands();

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
            {
                foreach (var mc in p.AllMcs)
                    mc.IsPlanned = onSet.Contains(mc.Tag);
                foreach (var cs in p.CSteps)
                    cs.IsPlanned = onSet.Contains(cs.Tag);
            }
        }

        private void ClearPreview()
        {
            foreach (var p in Panels)
            {
                foreach (var mc in p.AllMcs)
                    mc.IsPlanned = false;
                foreach (var cs in p.CSteps)
                    cs.IsPlanned = false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Seg 토글버튼은 클릭/엔터 시 자기 IsChecked를 스스로 뒤집는다(SetCurrentValue가
        // OneWay 바인딩 표시값을 덮어씀). 같은 모드를 재선택하면 SetValue가 변경 없음으로
        // 판단해 PropertyChanged를 올리지 않아 표시가 꺼진 채 남으므로, 값과 무관하게
        // 항상 강제 raise하여 표시를 VM 상태로 복원한다.
        private void SetMode(OperationMode m)
        {
            Mode = m;
            RaisePropertyChanged(nameof(IsAuto));
            RaisePropertyChanged(nameof(IsManual));
        }

        private void SetAutoMode(AutoMode m)
        {
            AutoMode = m;
            RaisePropertyChanged(nameof(IsIndividual));
            RaisePropertyChanged(nameof(IsPowerPf));
        }

        private HistoryEntry AddHistory(string panel, string ev, string result)
        {
            var entry = new HistoryEntry { Time = DateTime.Now, Panel = panel, Event = ev, Result = result };
            History.Insert(0, entry);
            ServiceHub.History.Add(entry);
            return entry;
        }

        /// <summary>UI 알람 + (dbType 지정 시) tb_alarm_event 기록.
        /// instant=true는 지속시간 없는 단발 이벤트(발생=해제); 지속형은 해당
        /// FB 하강 에지에서 ServiceHub.DbLog.AlarmCleared로 닫는다.</summary>
        private void AddAlarm(string panel, string msg, AlarmLevel level,
            string dbType = null, int? panelNo = null, bool instant = false)
        {
            Alarms.Insert(0, new AlarmEntry { Time = DateTime.Now, Panel = panel, Message = msg, Level = level });
            if (dbType != null) ServiceHub.DbLog.AlarmRaised(panelNo, dbType, msg, instant);
            RefreshFaultState();
        }

        // ── DB 운전 이벤트 (tb_operation_event) ──────────────────────────────

        /// <summary>동작 완료 1건을 DB에 기록. panel=null이면 전체(연결 판넬 합산 용량).</summary>
        private void LogOp(string opType, string result, PanelViewModel panel = null,
            string mode = null, string loadType = null, string target = null, string detailJson = null)
        {
            var ps = panel != null ? new[] { panel } : Panels.Where(x => x.IsConnected).ToArray();
            ServiceHub.DbLog.LogOperation(
                panel != null ? panel.Index + 1 : (int?)null,
                mode ?? (IsAuto ? "AUTO" : "MANUAL"),
                opType, result, loadType,
                phase: PhaseOf(target),
                target: target,
                appliedRkW:   Math.Round(ps.Sum(x => x.ActiveRkW),   2),
                appliedLkVar: Math.Round(ps.Sum(x => x.ActiveLkVar), 2),
                appliedCkVar: Math.Round(ps.Sum(x => x.ActiveCkVar), 2),
                detailJson: detailJson);
        }

        // P1_R_RN_01 → "RN" (PNL-1 개별상). 그 외 형식은 null.
        private static string PhaseOf(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var parts = tag.Split('_');
            return parts.Length >= 4 && (parts[2] == "RN" || parts[2] == "SN" || parts[2] == "TN")
                ? parts[2] : null;
        }

        private static string LoadTypeOf(string tag) =>
            tag == null ? null :
            tag.Contains("_R_") ? "R" :
            tag.Contains("_L_") ? "L" :
            tag.Contains("_C")  ? "C" : null;

        private void SeedLogs() { }
    }
}
