namespace LotusECMLogger.Services
{
    /// <summary>A single ECU-defined usage histogram bucket.</summary>
    public sealed record PerformanceUsageBucket
    {
        public required string Category { get; init; }
        public required string Band { get; init; }
        public required string Range { get; init; }
        public required ushort Pid { get; init; }
        public required uint Samples { get; init; }
        public TimeSpan Duration => TimeSpan.FromMilliseconds(Samples * 100.0);
    }

    /// <summary>A ranked event retained by the ECU's performance-history recorder.</summary>
    public sealed record PerformanceHistoryEvent
    {
        public required string Category { get; init; }
        public required int Rank { get; init; }
        public required double Value { get; init; }
        public required string Unit { get; init; }
        public int? VehicleSpeedKph { get; init; }
        public int? EngineSpeedRpm { get; init; }
        public double? ContextValue { get; init; }
        public string? ContextUnit { get; init; }
        public TimeSpan? EngineRuntime { get; init; }
    }

    /// <summary>Decoded contents of the engine ECU's Mode 22 0x03xx history area.</summary>
    public sealed record PerformanceHistorySnapshot
    {
        public required string CalibrationId { get; init; }
        public required string Variant { get; init; }
        public required TimeSpan EngineRuntime { get; init; }
        public double? DistanceKm { get; init; }
        public int StandingStartCount { get; init; }
        public double? FastestZeroTo100Seconds { get; init; }
        public double? FastestZeroTo160Seconds { get; init; }
        public double? LastZeroTo100Seconds { get; init; }
        public double? LastZeroTo160Seconds { get; init; }
        public int LowOilPressureEventCount { get; init; }
        public required IReadOnlyList<PerformanceUsageBucket> Usage { get; init; }
        public required IReadOnlyList<PerformanceHistoryEvent> Events { get; init; }
        public required IReadOnlyList<string> Notes { get; init; }
    }

    public interface IPerformanceHistoryService
    {
        /// <summary>Reads and decodes the persistent Mode 22 0x03xx history from the engine ECU.</summary>
        PerformanceHistorySnapshot LoadPerformanceHistory();
    }
}
