namespace LotusECMLogger.Services.Tuning
{
    /// <summary>
    /// The files of one tuning recording, named after its log <c>X.csv</c>: the calibration writes <c>X.edits.csv</c>,
    /// and what the ECU's calibration RAM held when recording started and stopped, <c>X.cal-start.bin</c> and
    /// <c>X.cal-end.bin</c> (raw images of the build's CAL range, which the Tuning page opens like any calibration).
    /// </summary>
    public sealed record RecordingFiles
    {
        public RecordingFiles(string log)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(log);
            Log = Path.GetFullPath(log);
        }

        public string Log { get; }
        public string Edits => Sibling(".edits.csv");
        public string CalibrationStart => Sibling(".cal-start.bin");
        public string CalibrationEnd => Sibling(".cal-end.bin");

        private string Sibling(string suffix) => Path.Combine(Path.GetDirectoryName(Log)!, Path.GetFileNameWithoutExtension(Log) + suffix);
    }
}
