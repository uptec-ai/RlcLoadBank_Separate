using System;
using System.Collections.ObjectModel;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>One magnetic contactor (MC) shown as a circle in the diagram.</summary>
    public class McViewModel : ViewModelBase
    {
        private readonly Action<McViewModel> _toggle;

        public string Tag { get; }     // e.g. P1_R_RN_01
        public string Label { get; }   // e.g. R1

        /// <summary>Step magnitude (kW for R, kvar for L/C). Per-phase for PLC1
        /// single-phase MCs, 3-phase total for PLC2/3. Used by auto-operation.</summary>
        public double Value { get; set; }
        public LoadType Load { get; set; }

        /// <summary>Formatted capacity label shown below the MC ellipse (e.g. "5kW", "2.5kvar").</summary>
        public string ValueText
        {
            get
            {
                if (Value == 0) return "";
                string unit = Load == LoadType.R ? "kW" : "kvar";
                return $"{Value:0.#}{unit}";
            }
        }

        public McState State
        {
            get => GetValue<McState>();
            set => SetValue(value, () => RaisePropertyChanged(nameof(IsOn)));
        }
        public bool IsOn => State == McState.On;

        /// <summary>자동운전 계획에 포함된 MC (목표 설정 시 주황 테두리로 미리 표시).</summary>
        public bool IsPlanned { get => GetValue<bool>(); set => SetValue(value); }

        public DelegateCommand ToggleCommand { get; }

        public McViewModel(string tag, string label, Action<McViewModel> toggle, McState state = McState.Off)
        {
            Tag = tag;
            Label = label;
            _toggle = toggle;
            State = state;
            ToggleCommand = new DelegateCommand(() => _toggle?.Invoke(this));
        }
    }

    /// <summary>
    /// One C-load stage (C1 or C2). PLC handles the internal sequence (MC1→SCR→MC2).
    /// HMI sends a single CMD and monitors RESULT + three alarm DIs.
    /// </summary>
    public class CStageViewModel : ViewModelBase
    {
        public string Tag   { get; }   // e.g. "P1_C1"
        public string Label { get; }   // e.g. "C1"
        public double Value { get; }   // 50 kVAr per stage

        // C_RESULT DI: T=running, F=stopped
        public bool IsRunning { get => GetValue<bool>(); set => SetValue(value); }

        // Alarm DIs: T=alarm, F=normal
        public bool Mc1Alarm
        {
            get => GetValue<bool>();
            set => SetValue(value, () => RaisePropertyChanged(nameof(HasAlarm)));
        }
        public bool Mc2Alarm
        {
            get => GetValue<bool>();
            set => SetValue(value, () => RaisePropertyChanged(nameof(HasAlarm)));
        }
        public bool ScrAlarm
        {
            get => GetValue<bool>();
            set => SetValue(value, () => RaisePropertyChanged(nameof(HasAlarm)));
        }
        public bool HasAlarm => Mc1Alarm || Mc2Alarm || ScrAlarm;

        public DelegateCommand ToggleCommand { get; }

        public CStageViewModel(string tag, string label, Action<CStageViewModel> onToggle)
        {
            Tag   = tag;
            Label = label;
            Value = 50.0;
            ToggleCommand = new DelegateCommand(() => onToggle?.Invoke(this));
        }
    }

    /// <summary>A row of MCs (a phase group for PLC1, or a single batch row for PLC2/3).</summary>
    public class McGroupViewModel : ViewModelBase
    {
        public string Label { get; }                 // "R-N" / "S-N" / "T-N" / ""
        public bool HasLabel => !string.IsNullOrEmpty(Label);
        public ObservableCollection<McViewModel> Items { get; } = new ObservableCollection<McViewModel>();

        public McGroupViewModel(string label) { Label = label; }
    }
}
