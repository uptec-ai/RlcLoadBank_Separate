namespace RLC_LoadBank_SeparateVer.Models
{
    /// <summary>One EOCR-iSEM2 + sPDM line meter row (#1–#10).</summary>
    public class MeterLine
    {
        public int No { get; set; }
        public double Voltage { get; set; }   // V (avg)
        public double Current { get; set; }   // A (avg RMS)
        public double Power { get; set; }      // kW
        public double Pf { get; set; }         // power factor
        public double Thd { get; set; }        // current THD %

        public string Name => $"#{No} 선로";
        public string VoltageText => $"{Voltage:F1} V";
        public string CurrentText => $"{Current:F2} A";
        public string PowerText => $"{Power:F1} kW";
        public string PfText => $"{Pf:F2}";
        public string ThdText => $"{Thd:F1} %";
    }
}
