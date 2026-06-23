using System;
using DevExpress.Mvvm;
using RLC_LoadBank_SeparateVer.Models;
using RLC_LoadBank_SeparateVer.Services;

namespace RLC_LoadBank_SeparateVer.ViewModels
{
    /// <summary>One device row in the connect popup grid + detail panel.</summary>
    public class DeviceItemViewModel : ViewModelBase
    {
        public DeviceItemViewModel(DeviceRecord r)
        {
            Type = r.Type; Name = r.Name; Ip = r.Ip; Port = r.Port;
            UnitId = r.UnitId; SlaveId = r.SlaveId; CurrentReg = r.CurrentReg;
            Scale = r.Scale; InputReg = r.InputReg; PollInterval = r.PollInterval;
            Timeout = r.Timeout; State = r.State; Use = r.Use;
            if (State == ConnState.Connected) LastSeen = DateTime.Now;
            LastError = "-";
        }

        public bool Use { get => GetValue<bool>(); set => SetValue(value); }
        public DeviceType Type { get => GetValue<DeviceType>(); set => SetValue(value); }
        public string Name { get => GetValue<string>(); set => SetValue(value); }
        public string Ip { get => GetValue<string>(); set => SetValue(value); }
        public int Port { get => GetValue<int>(); set => SetValue(value); }
        public int UnitId { get => GetValue<int>(); set => SetValue(value); }
        public int SlaveId { get => GetValue<int>(); set => SetValue(value); }
        public int CurrentReg { get => GetValue<int>(); set => SetValue(value); }
        public double Scale { get => GetValue<double>(); set => SetValue(value); }
        public int InputReg { get => GetValue<int>(); set => SetValue(value); }
        public string Current => "-";
        public int PollInterval { get => GetValue<int>(); set => SetValue(value); }
        public int Timeout { get => GetValue<int>(); set => SetValue(value); }

        public ConnState State
        {
            get => GetValue<ConnState>();
            set => SetValue(value, () =>
            {
                RaisePropertyChanged(nameof(StatusText));
                RaisePropertyChanged(nameof(IsConnected));
            });
        }
        public bool IsConnected => State == ConnState.Connected;
        public string StatusText => State switch
        {
            ConnState.Connected => "연결됨",
            ConnState.Connecting => "연결 중",
            ConnState.Error => "오류",
            ConnState.Disconnected => "해제됨",
            _ => "Disabled",
        };

        public DateTime? LastSeen
        {
            get => GetValue<DateTime?>();
            set => SetValue(value, () => RaisePropertyChanged(nameof(LastSeenText)));
        }
        public string LastSeenText => LastSeen?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        public string LastSeenShort => LastSeen?.ToString("HH:mm:ss") ?? "-";

        public string LastError { get => GetValue<string>(); set => SetValue(value); }

        public DeviceRecord ToRecord() => new DeviceRecord
        {
            Use = Use, Type = Type, Name = Name, Ip = Ip, Port = Port, UnitId = UnitId,
            SlaveId = SlaveId, CurrentReg = CurrentReg, Scale = Scale, InputReg = InputReg,
            PollInterval = PollInterval, Timeout = Timeout, State = State
        };
    }
}
