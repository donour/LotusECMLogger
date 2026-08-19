namespace LotusECMLogger.Services
{
    /// <summary>
    /// One parameter from a freeze frame: decoded when the Mode 01 parser knows the PID,
    /// otherwise a raw-bytes fallback so no captured data is hidden.
    /// </summary>
    public sealed record FreezeFrameEntry
    {
        /// <summary>Parameter name, or "PID 0xNN" when the PID has no decoder.</summary>
        public required string Name { get; init; }

        /// <summary>Formatted value; null when the PID could not be decoded.</summary>
        public string? Value { get; init; }

        /// <summary>The data bytes as hex, always populated.</summary>
        public required string RawHex { get; init; }

        public bool IsDecoded { get; init; }
    }

    /// <summary>
    /// The result of one freeze frame read (OBD-II service 0x02): the sensor snapshot the
    /// ECU captured at the moment a diagnostic trouble code set.
    /// </summary>
    public sealed record FreezeFrameResult
    {
        /// <summary>False when the ECU has no freeze frame stored.</summary>
        public bool FrameStored { get; init; }

        /// <summary>The code that caused the frame to be captured (PID 0x02). Non-null when
        /// <see cref="FrameStored"/> is true.</summary>
        public DiagnosticTroubleCode? TriggeringDtc { get; init; }

        public IReadOnlyList<FreezeFrameEntry> Entries { get; init; } = [];

        /// <summary>Non-fatal per-PID failures (NRC or no response); the read still succeeded.</summary>
        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    public interface IFreezeFrameService
    {
        /// <summary>
        /// Reads the freeze frame via OBD-II service 0x02: the triggering code first, then
        /// every PID the ECU reports as present in the frame.
        /// </summary>
        /// <param name="frame">Frame number; J1979 ECUs effectively only store frame 0.</param>
        /// <returns>
        /// Success flag, an error message when unsuccessful, and the frame contents (with
        /// <see cref="FreezeFrameResult.FrameStored"/> false when the ECU has none).
        /// </returns>
        (bool success, string errorMessage, FreezeFrameResult result) ReadFreezeFrame(byte frame = 0x00);
    }
}
