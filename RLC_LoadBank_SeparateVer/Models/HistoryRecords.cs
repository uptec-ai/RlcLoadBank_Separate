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

    /// <summary>tb_gimac_agg_1m 1 row (조회용 — DbLogService.QueryGimacAggs).
    /// GIMAC 고유: 무효/피상전력 포함, 전류는 3상 평균 단일값.</summary>
    public class GimacAggRecord
    {
        public DateTime Ts      { get; set; }   // 로컬 시간 (분 경계)
        public int      UnitId  { get; set; }
        public int?     PanelNo { get; set; }
        public double   VoltAvg { get; set; }
        public double   CurrAvg { get; set; }
        public double   KwAvg   { get; set; }
        public double   KwMin   { get; set; }
        public double   KwMax   { get; set; }
        public double   KvarAvg { get; set; }
        public double   KvaAvg  { get; set; }   // 피상전력 (GIMAC 고유)
        public double   PfAvg   { get; set; }
        public double   HzAvg   { get; set; }
    }

    /// <summary>tb_isem_agg_1m 1 row (조회용 — DbLogService.QueryIsemAggs).
    /// ISEM 고유: 상별 전류(L1/L2/L3), 접지전류(보호), 무효전력.</summary>
    public class IsemAggRecord
    {
        public DateTime Ts        { get; set; }   // 로컬 시간 (분 경계)
        public int      UnitId    { get; set; }
        public int?     PanelNo   { get; set; }
        public double   VoltAvg   { get; set; }
        public double   CurrL1    { get; set; }
        public double   CurrL2    { get; set; }
        public double   CurrL3    { get; set; }
        public double   GroundAvg { get; set; }   // 접지전류 평균 (ISEM 고유, 보호)
        public double   GroundMax { get; set; }   // 접지전류 최대
        public double   KwAvg     { get; set; }
        public double   KwMin     { get; set; }
        public double   KwMax     { get; set; }
        public double   KvarAvg   { get; set; }
        public double   PfAvg     { get; set; }
        public double   HzAvg     { get; set; }
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
