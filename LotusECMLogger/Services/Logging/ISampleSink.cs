namespace LotusECMLogger.Services.Logging
{
    /// <summary>
    /// Preamble written at the top of a sample log: a title plus any logger-specific notes
    /// (channel lists, memory addresses, poll intervals — whatever identifies the session).
    /// </summary>
    /// <param name="Title">Short name of the logging session, e.g. "Lotus High-Speed CAN Channel Log".</param>
    /// <param name="Notes">Extra lines describing the session. May be empty.</param>
    public sealed record SampleLogHeader(string Title, IReadOnlyList<string> Notes)
    {
        public SampleLogHeader(string title) : this(title, []) { }
    }

    /// <summary>
    /// A time-series destination for logged samples. Producers set values and emit rows; the sink
    /// owns everything about storage — file handling, number formatting, escaping, buffering and
    /// flushing. This keeps the protocol services free of persistence code and lets a session be
    /// pointed at something other than a CSV file without touching them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column set is fixed when a sink is constructed, so there is no ordering contract to get
    /// wrong and no partially-initialised state to guard against.
    /// </para>
    /// <para>
    /// Values are <b>carried forward</b>: a column set once keeps that value on every subsequent row
    /// until it is set or cleared again. Producers that learn one channel at a time (the OBD and
    /// high-speed loggers) therefore need not restate a whole row, and producers that build a
    /// complete row each time (RMA, ABS) are simply the degenerate case.
    /// </para>
    /// <para>
    /// <b>Not thread-safe.</b> A sink must be driven by a single thread. Every logger already
    /// confines its writing to one: the RMA logging thread, the high-speed writer thread,
    /// <c>J2534LoggingService.RunCSVWriter</c>, and <c>J2534AbsService.TelemetryLoop</c>.
    /// </para>
    /// </remarks>
    public interface ISampleSink : IDisposable
    {
        /// <summary>
        /// Sets one numeric cell of the row under construction. The value is carried into later rows
        /// until set or cleared again. A column the sink does not know is ignored, so a producer
        /// whose channel set drifts mid-session cannot shift the columns of a header already on disk.
        /// </summary>
        /// <remarks>
        /// <para>A non-finite <paramref name="value"/> is recorded as "no value" — see <see cref="Clear"/>.</para>
        /// <para>
        /// This is the setter for both <see cref="SampleFormat.Number"/> and
        /// <see cref="SampleFormat.Hex"/> columns: a hex column holds the same value and differs
        /// only in how it is written, so a raw byte is set here and rendered as <c>0x1F</c>.
        /// </para>
        /// </remarks>
        void Set(string column, double value);

        /// <summary>
        /// Sets one <see cref="SampleFormat.Text"/> cell, for values that are not quantities at all
        /// — a state name, a fault code, an identifier. Carried forward like a numeric value.
        /// </summary>
        void SetText(string column, string value);

        /// <summary>
        /// Marks a column as having no value, from this row on. It is written as an empty cell,
        /// which is how an unavailable sensor reading is distinguished from a genuine zero.
        /// </summary>
        void Clear(string column);

        /// <summary>
        /// Emits the row under construction, timing it relative to the start of the session.
        /// </summary>
        void WriteRow(DateTime timestamp);

        /// <summary>
        /// Emits the row under construction with an explicit elapsed time. Used where a better
        /// source than wall-clock exists — the high-speed logger derives it from the J2534 adapter's
        /// hardware timestamp, which times frames within a read batch far more accurately.
        /// </summary>
        /// <param name="relativeTimeMs">Milliseconds since the session started.</param>
        void WriteRow(DateTime timestamp, double relativeTimeMs);

        /// <summary>Pushes buffered rows to storage.</summary>
        void Flush();
    }
}
