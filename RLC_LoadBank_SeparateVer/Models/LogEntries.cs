using System;
using System.ComponentModel;

namespace RLC_LoadBank_SeparateVer.Models
{
    /// <summary>A trip/alarm row shown in the dashboard "트립 / 알람" list.</summary>
    public class AlarmEntry
    {
        public DateTime Time { get; set; }
        public string Panel { get; set; }          // PNL-1 / PLC2 / PNL-M ...
        public string Message { get; set; }        // 알람 내용
        public AlarmLevel Level { get; set; }       // INFO / ALARM / TRIP
        public bool Acknowledged { get; set; }

        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>An operation-history row shown in the dashboard "운전 이력" list.
    /// DB-backed later; for now this is an in-memory list only.</summary>
    public class HistoryEntry : INotifyPropertyChanged
    {
        private string _result;

        public DateTime Time { get; set; }
        public string Panel { get; set; }          // 자동 / 수동 / PLC1 ...
        public string Event { get; set; }          // 이벤트 내용
        public string Result
        {
            get => _result;
            set
            {
                if (_result == value) return;
                _result = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Result)));
            }
        }

        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
