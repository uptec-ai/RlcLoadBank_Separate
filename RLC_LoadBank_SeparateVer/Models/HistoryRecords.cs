using System;

namespace RLC_LoadBank_SeparateVer.Models
{
    /// <summary>tb_alarm_event 1 row (조회용 — DbLogService.QueryAlarms).</summary>
    public class AlarmEventRecord
    {
        public DateTime  RaisedTs  { get; set; }   // 로컬 시간
        public DateTime? ClearedTs { get; set; }   // NULL = 활성
        public int?      PanelNo   { get; set; }
        public string    AlarmType { get; set; }   // EMG | MCCB_TRIP | OVR | ...
        public string    Detail    { get; set; }
    }

    /// <summary>tb_gimac_agg_1m ∪ tb_isem_agg_1m 1 row (조회용 — DbLogService.QueryMeterAggs).</summary>
    public class MeterAggRecord
    {
        public DateTime Ts         { get; set; }   // 로컬 시간 (분 경계)
        public string   DeviceType { get; set; }   // GIMAC | ISEM
        public int      UnitId     { get; set; }
        public int?     PanelNo    { get; set; }
        public double   VoltAvg    { get; set; }
        public double   CurrAvg    { get; set; }   // ISEM은 L1~L3 평균
        public double   KwAvg      { get; set; }
        public double   KwMin      { get; set; }
        public double   KwMax      { get; set; }
        public double   PfAvg      { get; set; }
        public double   HzAvg      { get; set; }
    }

    /// <summary>tb_connection_event 1 row (조회용 — DbLogService.QueryConnections).</summary>
    public class ConnectionEventRecord
    {
        public DateTime Ts         { get; set; }   // 로컬 시간
        public string   DeviceType { get; set; }   // PLC | GIMAC | ISEM
        public int      UnitId     { get; set; }
        public int?     PanelNo    { get; set; }
        public bool     Connected  { get; set; }
        public string   Detail     { get; set; }
    }
}
