using System.Buffers.Binary;

namespace LotusECMLogger.Services
{
    internal enum PerformanceHistoryVariant
    {
        Unknown,
        EvoraS1,
        Evora400,
        EvoraGt430,
        EvoraGt,
    }

    internal sealed record PerformanceHistoryProfile(
        PerformanceHistoryVariant Kind,
        string DisplayName,
        ushort? DistancePid,
        int LateralAccelerationBands)
    {
        public static PerformanceHistoryProfile Resolve(string calibrationId)
        {
            string id = calibrationId.ToUpperInvariant();

            if (id.Contains("B13200091", StringComparison.Ordinal) ||
                id.Contains("B132E0091", StringComparison.Ordinal))
                return new(PerformanceHistoryVariant.EvoraS1, "Evora S1 (B13200091)", null, 6);
            if (id.Contains("C132E0271", StringComparison.Ordinal))
                return new(PerformanceHistoryVariant.Evora400, "Evora 400 (C132E0271)", 0x033A, 6);
            if (id.Contains("C132E0278", StringComparison.Ordinal))
                return new(PerformanceHistoryVariant.EvoraGt430, "Evora GT430 (C132E0278)", 0x033A, 6);
            if (id.Contains("E132E0288", StringComparison.Ordinal))
                return new(PerformanceHistoryVariant.EvoraGt, "Evora GT (E132E0288)", 0x0341, 5);

            return new(PerformanceHistoryVariant.Unknown, "Unknown Evora calibration", null, 5);
        }
    }

    /// <summary>
    /// Pure decoder for the persistent performance statistics published by Evora engine firmware.
    /// The firmware updates all time counters from its 10 Hz performance task, so one count is
    /// 100 ms. Histogram thresholds are calibration data rather than part of the 0x03xx responses;
    /// the range labels below come from the constants in each analysed Evora firmware image.
    /// </summary>
    internal static class PerformanceHistoryDecoder
    {
        private const double TickSeconds = 0.1;

        // These boundaries are the calibration constants used by perf_stats_update in all four
        // analysed Evora programs. The inequalities mirror the firmware comparisons exactly.
        private static readonly string[] ThrottleRanges = BuildRanges(
            [0, 4, 38, 64, 89, 127, 166, 204, 255],
            raw => $"{raw * 100.0 / 255.0:F1} %", lowerInclusive: false);
        private static readonly string[] EngineSpeedRanges = BuildRanges(
            [500, 1500, 2500, 3500, 4500, 5500, 6500, 7000, 12000],
            raw => $"{raw:N0} rpm", lowerInclusive: false);
        private static readonly string[] VehicleSpeedRanges = BuildRanges(
            [0, 30, 60, 90, 120, 150, 180, 210, 255],
            raw => $"{raw} km/h", lowerInclusive: false);
        private static readonly string[] CoolantTemperatureRanges = BuildRanges(
            [232, 240, 248, 254, 255],
            raw => $"{raw * 5.0 / 8.0 - 40.0:F1} °C", lowerInclusive: false);
        private static readonly string[] LateralAccelerationRanges = BuildRanges(
            [0, 60, 80, 100, 120, 140, 200],
            raw => $"{raw / 100.0:F2} g", lowerInclusive: true);

        internal static IReadOnlySet<ushort> DecodeSupportedPids(ushort pagePid, ReadOnlySpan<byte> bitmap)
        {
            var result = new HashSet<ushort>();
            if (bitmap.Length < 4)
                return result;

            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                for (int bitIndex = 0; bitIndex < 8; bitIndex++)
                {
                    if ((bitmap[byteIndex] & (0x80 >> bitIndex)) != 0)
                        result.Add((ushort)(pagePid + byteIndex * 8 + bitIndex + 1));
                }
            }
            return result;
        }

        internal static PerformanceHistorySnapshot Decode(
            string calibrationId,
            IReadOnlyDictionary<ushort, byte[]> payloads)
        {
            PerformanceHistoryProfile profile = PerformanceHistoryProfile.Resolve(calibrationId);
            var usage = new List<PerformanceUsageBucket>();

            AddUsage(usage, payloads, "Throttle position", 0x0301, ThrottleRanges);
            AddUsage(usage, payloads, "Engine speed", 0x0309, EngineSpeedRanges);
            AddUsage(usage, payloads, "Vehicle speed", 0x0311, VehicleSpeedRanges);
            AddUsage(usage, payloads, "Coolant temperature", 0x031A, CoolantTemperatureRanges);
            AddUsage(usage, payloads, "Lateral acceleration", 0x033B,
                LateralAccelerationRanges[..Math.Min(5, profile.LateralAccelerationBands)]);
            if (profile.LateralAccelerationBands == 6)
                AddUsageBucket(usage, payloads, "Lateral acceleration", 6,
                    LateralAccelerationRanges[5], 0x0341);

            var events = new List<PerformanceHistoryEvent>();
            AddTopRpmEvents(events, payloads);
            AddTopSpeedEvents(events, payloads);
            AddDetailedEvents(events, payloads, "Low oil pressure", 0x0342);
            AddDetailedEvents(events, payloads, "High lateral-G", 0x0351);

            double? distance = profile.DistancePid switch
            {
                0x033A => ReadU32Tail(payloads, 0x033A),
                0x0341 => ReadU32(payloads, 0x0341),
                _ => null,
            };

            var notes = new List<string>
            {
                "Usage ranges are ECU calibration constants verified in the analysed Evora firmwares; the boundaries are not transmitted by Mode 22.",
                "Times and event timestamps are engine-runtime values recorded in 100 ms increments, not wall-clock dates.",
            };
            if (profile.Kind == PerformanceHistoryVariant.EvoraS1)
                notes.Add("This S1 firmware does not publish a distance value in its 0x03xx history.");
            else if (profile.Kind == PerformanceHistoryVariant.Unknown)
                notes.Add("The calibration was not recognised, so only fields with common meanings across the analysed Evora variants are shown.");

            return new PerformanceHistorySnapshot
            {
                CalibrationId = string.IsNullOrWhiteSpace(calibrationId) ? "Unavailable" : calibrationId,
                Variant = profile.DisplayName,
                EngineRuntime = CounterTime(ReadU32(payloads, 0x0338) ?? 0),
                DistanceKm = distance,
                StandingStartCount = ReadU16(payloads, 0x0339) ?? 0,
                FastestZeroTo100Seconds = ReadStandingStart(payloads, 0x0334),
                FastestZeroTo160Seconds = ReadStandingStart(payloads, 0x0335),
                LastZeroTo100Seconds = ReadStandingStart(payloads, 0x0336),
                LastZeroTo160Seconds = ReadStandingStart(payloads, 0x0337),
                LowOilPressureEventCount = ReadByte(payloads, 0x0361) ?? 0,
                Usage = usage,
                Events = events,
                Notes = notes,
            };
        }

        private static void AddUsage(
            List<PerformanceUsageBucket> target,
            IReadOnlyDictionary<ushort, byte[]> payloads,
            string category,
            ushort firstPid,
            IReadOnlyList<string> ranges)
        {
            for (int i = 0; i < ranges.Count; i++)
                AddUsageBucket(target, payloads, category, i + 1, ranges[i], (ushort)(firstPid + i));
        }

        private static void AddUsageBucket(
            List<PerformanceUsageBucket> target,
            IReadOnlyDictionary<ushort, byte[]> payloads,
            string category,
            int band,
            string range,
            ushort pid)
        {
            if (ReadU32(payloads, pid) is not uint samples)
                return;

            target.Add(new PerformanceUsageBucket
            {
                Category = category,
                Band = $"ECU band {band}",
                Range = range,
                Pid = pid,
                Samples = samples,
            });
        }

        private static void AddTopRpmEvents(
            List<PerformanceHistoryEvent> target,
            IReadOnlyDictionary<ushort, byte[]> payloads)
        {
            ushort[] rpmPids = [0x031E, 0x031F, 0x0321, 0x0322, 0x0323];
            ushort[] temperaturePids = [0x0324, 0x0326, 0x0328, 0x032A, 0x032C];
            ushort[] runtimePids = [0x0325, 0x0327, 0x0329, 0x032B, 0x032E];

            for (int i = 0; i < rpmPids.Length; i++)
            {
                if (ReadU32(payloads, rpmPids[i]) is not uint rpm)
                    continue;

                byte? temperatureRaw = ReadByteTail(payloads, temperaturePids[i]);
                target.Add(new PerformanceHistoryEvent
                {
                    Category = "Highest engine speed",
                    Rank = 5 - i,
                    Value = rpm,
                    Unit = "rpm",
                    ContextValue = temperatureRaw is byte raw ? raw * 5.0 / 8.0 - 40.0 : null,
                    ContextUnit = temperatureRaw.HasValue ? "°C coolant" : null,
                    EngineRuntime = ReadCounterTime(payloads, runtimePids[i]),
                });
            }
        }

        private static void AddTopSpeedEvents(
            List<PerformanceHistoryEvent> target,
            IReadOnlyDictionary<ushort, byte[]> payloads)
        {
            // Firmware keeps this list in ascending order, so PID 0x0333 is rank 1.
            for (int i = 0; i < 5; i++)
            {
                if (ReadU16(payloads, (ushort)(0x032F + i)) is not ushort speed)
                    continue;

                target.Add(new PerformanceHistoryEvent
                {
                    Category = "Highest vehicle speed",
                    Rank = 5 - i,
                    Value = speed,
                    Unit = "km/h",
                });
            }
        }

        private static void AddDetailedEvents(
            List<PerformanceHistoryEvent> target,
            IReadOnlyDictionary<ushort, byte[]> payloads,
            string category,
            ushort firstPid)
        {
            for (int i = 0; i < 3; i++)
            {
                ushort pid = (ushort)(firstPid + i * 5);
                byte? duration = ReadByte(payloads, pid);
                byte? speed = ReadByte(payloads, (ushort)(pid + 1));
                ushort? rpm = ReadU16(payloads, (ushort)(pid + 2));
                short? peakG = ReadI16(payloads, (ushort)(pid + 3));
                TimeSpan? runtime = ReadCounterTime(payloads, (ushort)(pid + 4));

                if (!duration.HasValue && !speed.HasValue && !rpm.HasValue && !peakG.HasValue && !runtime.HasValue)
                    continue;

                target.Add(new PerformanceHistoryEvent
                {
                    Category = category,
                    Rank = 3 - i,
                    Value = (duration ?? 0) * TickSeconds,
                    Unit = "s",
                    VehicleSpeedKph = speed,
                    EngineSpeedRpm = rpm,
                    // The recorder starts a high-G event at raw 101, which is 1.01 g. Although
                    // older Ghidra projects named this type "1/10g", the stored CAN/yaw value is
                    // actually hundredths of g.
                    ContextValue = peakG / 100.0,
                    ContextUnit = "g peak lateral",
                    EngineRuntime = runtime,
                });
            }
        }

        private static string[] BuildRanges(
            int[] thresholds,
            Func<int, string> format,
            bool lowerInclusive)
        {
            var ranges = new string[thresholds.Length - 1];
            string lowerOperator = lowerInclusive ? "≤" : "<";
            string upperOperator = lowerInclusive ? "<" : "≤";
            for (int i = 0; i < ranges.Length; i++)
                ranges[i] = $"{format(thresholds[i])} {lowerOperator} value {upperOperator} {format(thresholds[i + 1])}";
            return ranges;
        }

        private static double? ReadStandingStart(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid)
        {
            byte? raw = ReadByte(payloads, pid);
            return raw is null or 0 or 0xFF ? null : raw.Value * TickSeconds;
        }

        private static TimeSpan? ReadCounterTime(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            ReadU32(payloads, pid) is uint value ? CounterTime(value) : null;

        private static TimeSpan CounterTime(uint samples) => TimeSpan.FromMilliseconds(samples * 100.0);

        private static byte? ReadByte(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            payloads.TryGetValue(pid, out byte[]? data) && data.Length >= 1 ? data[0] : null;

        private static byte? ReadByteTail(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            payloads.TryGetValue(pid, out byte[]? data) && data.Length >= 1 ? data[^1] : null;

        private static ushort? ReadU16(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            payloads.TryGetValue(pid, out byte[]? data) && data.Length >= 2
                ? BinaryPrimitives.ReadUInt16BigEndian(data)
                : null;

        private static short? ReadI16(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            payloads.TryGetValue(pid, out byte[]? data) && data.Length >= 2
                ? BinaryPrimitives.ReadInt16BigEndian(data)
                : null;

        private static uint? ReadU32(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            payloads.TryGetValue(pid, out byte[]? data) && data.Length >= 4
                ? BinaryPrimitives.ReadUInt32BigEndian(data)
                : null;

        // 0x033A prepends the request length before its four-byte distance value on the S2 code.
        private static uint? ReadU32Tail(IReadOnlyDictionary<ushort, byte[]> payloads, ushort pid) =>
            payloads.TryGetValue(pid, out byte[]? data) && data.Length >= 4
                ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(data.Length - 4))
                : null;
    }
}
