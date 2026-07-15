using LotusECMLogger.Models;

namespace LotusECMLogger.Services
{
    /// <summary>One decoded channel value from a high-speed log frame.</summary>
    public sealed class HighSpeedReading
    {
        public string Name { get; init; } = string.Empty;
        public double Value { get; init; }
        public string Unit { get; init; } = string.Empty;
    }

    /// <summary>Event payload for one received 0x351 log frame, with its channels decoded.</summary>
    public sealed class HighSpeedSampleEventArgs : EventArgs
    {
        public DateTime Timestamp { get; init; }

        /// <summary>Frame demux label (group*12 + frameIndex), with the overflow bit stripped.</summary>
        public byte Label { get; init; }

        /// <summary>True if the ECU flagged dropped frames (label bit 7) for this group.</summary>
        public bool Overflow { get; init; }

        public IReadOnlyList<HighSpeedReading> Readings { get; init; } = [];
    }

    /// <summary>
    /// Result of a connection test (open-session + identify handshake) against the ECU.
    /// </summary>
    public sealed class HighSpeedIdentifyResult
    {
        /// <summary>Capability magic the channel logger returns from the identify command (0x17).</summary>
        public const uint ChannelLoggerMagic = 0x0003008E;

        /// <summary>The ECU acknowledged open-session (0x01) → the diagnostic bus is enabled and reachable.</summary>
        public bool SessionOpened { get; init; }

        /// <summary>The ECU acknowledged identify (0x17).</summary>
        public bool Identified { get; init; }

        /// <summary>Length (bytes) of the firmware-ID string the ECU reported.</summary>
        public int FirmwareIdLength { get; init; }

        /// <summary>Capability/protocol magic returned by identify (expected <see cref="ChannelLoggerMagic"/>).</summary>
        public uint Magic { get; init; }

        /// <summary>True when identify succeeded and the magic matches the channel logger.</summary>
        public bool IsChannelLogger => Identified && Magic == ChannelLoggerMagic;
    }

    /// <summary>
    /// Service for the T6e high-speed CAN channel logger. Configures the ECU's developer telemetry
    /// facility over CAN ID 0x350, then streams decoded channel data from 0x351 to a CSV file and to
    /// <see cref="DataReceived"/> subscribers. Protocol per <c>ChannelLogger.md</c>.
    /// </summary>
    public interface IHighSpeedLogService : IDisposable
    {
        /// <summary>Column/reading names used when AEM wideband polling is enabled.</summary>
        const string AemLambdaChannelName = "AEM Lambda";
        const string AemAfrChannelName = "AEM AFR";

        /// <summary>Fired (off the UI thread) for each decoded log frame.</summary>
        event EventHandler<HighSpeedSampleEventArgs>? DataReceived;

        /// <summary>Fired when configuration or streaming fails; logging is stopped first.</summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>True while a logging session is configured and streaming.</summary>
        bool IsLogging { get; }

        /// <summary>
        /// Stream frames dropped because the writer/CSV thread fell behind and the bounded hand-off
        /// queue filled (e.g. a sustained disk stall). 0 in normal operation.
        /// </summary>
        long DroppedFrames { get; }

        /// <summary>
        /// Builds a channel program from the selected (channel, rateHz) pairs, configures and arms the
        /// ECU, and begins streaming to <paramref name="csvFilePath"/>. Throws if a session is already
        /// active or the selection cannot be packed into the ECU's capacity.
        /// When <paramref name="pollAemWideband"/> is true, an AEM X-Series wideband (OBDII variant,
        /// e.g. 30-0334) is also polled on the same bus — Mode 01 PID 0x24 to 0x7E1 — and its
        /// lambda/AFR are merged into the CSV as extra last-value-hold columns. A missing/silent AEM
        /// does not fail the session; the columns simply stay empty.
        /// </summary>
        void StartLogging(IReadOnlyList<(HighSpeedChannel channel, int rateHz)> channels, string csvFilePath,
            bool pollAemWideband = false);

        /// <summary>Stops the ECU stream, ends the session, and closes the CSV file.</summary>
        void StopLogging();

        /// <summary>
        /// Probes the ECU on its own temporary CAN session: opens a session (0x01) then sends identify
        /// (0x17), reporting whether the diagnostic bus is reachable and whether the channel logger is
        /// present. Must not be called while logging is active (it opens its own channel).
        /// </summary>
        HighSpeedIdentifyResult Identify();
    }
}
