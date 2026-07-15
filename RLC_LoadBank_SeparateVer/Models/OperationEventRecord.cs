using System;

namespace RLC_LoadBank_SeparateVer.Models
{
    /// <summary>tb_operation_event 1 row (조회용 — DbLogService.QueryOperations).</summary>
    public class OperationEventRecord
    {
        public DateTime Ts       { get; set; }   // 로컬 시간으로 변환된 값
        public int?     PanelNo  { get; set; }   // NULL = 전체/공통
        public string   Mode     { get; set; }   // MANUAL | AUTO | SYSTEM
        public string   OpType   { get; set; }   // MC_ON | SEQ_ON | ALL_OFF | AUTO_COMPLETE | ...
        public string   LoadType { get; set; }   // R | L | C | MIXED | NULL
        public string   Phase    { get; set; }   // RN | SN | TN | 3P | NULL
        public string   Target   { get; set; }   // mc_tag 또는 대상 설명
        public decimal? RkW      { get; set; }   // 동작 직후 투입 용량 스냅샷
        public decimal? LkVar    { get; set; }
        public decimal? CkVar    { get; set; }
        public string   Result   { get; set; }   // 성공 | 실패 | 중단 | ...
        public string   Detail   { get; set; }   // jsonb 원문
    }
}
