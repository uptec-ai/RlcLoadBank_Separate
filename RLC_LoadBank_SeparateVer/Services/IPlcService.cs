using System;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Abstraction over the per-panel PLC link (Modbus TCP).
    /// Tag convention:
    ///   MC/C-sub → base tag without suffix  (e.g. P1_R_RN_01, P1_C1_R_MC)
    ///   StatusFb → tag with _FB suffix      (e.g. P1_MCCB_ON_FB, P1_EMG_FB)
    ///   CmdDo   → tag with _CMD suffix      (e.g. P1_MCCB_ON_CMD, P1_RESET_CMD)
    /// FeedbackReceived fires for MC/C-sub tags and for StatusFb tags.
    /// </summary>
    public interface IPlcService
    {
        /// <summary>panelIndex: 0=PLC1/PNL-1, 1=PLC2/PNL-2, 2=PLC3/PNL-3.</summary>
        bool IsConnected(int panelIndex);

        void Connect(int panelIndex);
        void Disconnect(int panelIndex);

        /// <summary>
        /// Write a DO command.  mcTag is the base tag for MC/C-sub points, or the
        /// full _CMD tag for control-only points (MCCB_ON_CMD, RESET_CMD, etc.).
        /// </summary>
        void WriteMcCommand(int panelIndex, string mcTag, bool on);

        /// <summary>Raised when connection state changes for a panel.</summary>
        event EventHandler<int> ConnectionChanged;

        /// <summary>
        /// Raised when any DI point changes.
        /// McTag carries the base tag (MC/C-sub) or the full _FB tag (status points).
        /// </summary>
        event EventHandler<McFeedback> FeedbackReceived;
    }

    public class McFeedback
    {
        public int    PanelIndex { get; set; }
        public string McTag      { get; set; }
        public bool   On         { get; set; }
    }
}
