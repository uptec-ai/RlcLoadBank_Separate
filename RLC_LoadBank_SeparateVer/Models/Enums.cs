namespace RLC_LoadBank_SeparateVer.Models
{
    /// <summary>Modbus device category.</summary>
    public enum DeviceType
    {
        PLC,
        ISEM,   // EOCR-iSEM2 + sPDM line meters
        GIMAC,  // GIMAC 1000 bus power meters
        Server  // Monitoring server (placeholder — not used yet)
    }

    /// <summary>Connection state of a device or panel.</summary>
    public enum ConnState
    {
        Disconnected,
        Connecting,
        Connected,
        Disabled,
        Error
    }

    /// <summary>Visual/operational state of a single MC (matches the diagram legend).</summary>
    public enum McState
    {
        Off,        // grey
        On,         // green
        CommWait,   // orange — command sent, awaiting feedback
        Trip,       // red
        Alarm       // red (blinking) — FB/CMD mismatch or fault
    }

    /// <summary>System operation mode.</summary>
    public enum OperationMode
    {
        Auto,
        Manual
    }

    /// <summary>How auto-operation targets are specified.</summary>
    public enum AutoMode
    {
        Individual,   // R/L/C individual setpoints
        PowerPf       // target real power + power factor
    }

    /// <summary>Load category.</summary>
    public enum LoadType
    {
        R,  // resistor (kW)
        L,  // reactor (kVar)
        C   // capacitor (kVar)
    }

    /// <summary>Alarm / trip severity.</summary>
    public enum AlarmLevel
    {
        Info,
        Alarm,
        Trip
    }
}
