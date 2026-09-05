namespace LotusECMLogger.Services
{
    /// <summary>
    /// Merged provisional readings from passive CAN broadcasts 0xA2/0xA4/0xA8.
    /// Their raw bytes are preserved; packing, physical scaling and simultaneous sample timing
    /// have not been validated. No requests are transmitted.
    /// </summary>
    public sealed record AbsTelemetrySample
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;

        /// <summary>Latest full broadcast data bytes; channel timestamps/packing are not inferred.</summary>
        public string? RawA2 { get; init; }
        public string? RawA4 { get; init; }
        public string? RawA8 { get; init; }

        // ── CAN 0xA2 — front wheels + vehicle speed ──
        public int? WheelLf { get; init; }
        public int? WheelRf { get; init; }
        public int? VehicleSpeedRaw { get; init; }
        public int? CounterA2 { get; init; }
        public bool? ChecksumA2Ok { get; init; }

        // ── CAN 0xA4 — rear wheels + brake switch ──
        public int? WheelLr { get; init; }
        public int? WheelRr { get; init; }
        public int? BrakeSwitch { get; init; }
        public int? CounterA4 { get; init; }
        public bool? ChecksumA4Ok { get; init; }

        // ── CAN 0xA8 — ESP/ABS status ──
        public bool? EspActive { get; init; }
        public bool? AbsActive { get; init; }
        public bool? TorqueRequest { get; init; }
        public bool? NoIntervention { get; init; }
        public bool? EspWarning { get; init; }

        /// <summary>True once any of the three broadcast frames has been seen.</summary>
        public bool HasData => VehicleSpeedRaw.HasValue || WheelLr.HasValue || EspActive.HasValue;

        public static string BrakeSwitchName(int value) => value switch
        {
            0 => "released",
            1 => "pressed",
            2 => "fault/invalid",
            _ => "reserved",
        };
    }

    /// <summary>
    /// Provisional legacy broadcast decoders. Their packing/status meanings are not validated
    /// by the primary diagnostic 04 trace. Complete raw bytes are retained for comparison.
    /// They cannot establish physical speed or authorize hydraulic actuation.
    /// </summary>
    internal static class AbsTelemetryDecoder
    {
        public const uint FrontWheelsCanId = 0xA2;
        public const uint RearWheelsCanId = 0xA4;
        public const uint EspStatusCanId = 0xA8;

        /// <summary>A 14-bit wheel field of all ones means the sensor reading is unavailable.</summary>
        private const int InvalidWheelSentinel = 0x3FFF;

        /// <summary>
        /// Applies a broadcast frame to <paramref name="sample"/>, returning the updated record.
        /// Unknown CAN ids and short frames are returned unchanged.
        /// </summary>
        public static AbsTelemetrySample Apply(AbsTelemetrySample sample, uint canId, byte[] data) => canId switch
        {
            FrontWheelsCanId when data.Length >= 7 => ApplyFrontWheels(sample, data),
            RearWheelsCanId when data.Length >= 6 => ApplyRearWheels(sample, data),
            EspStatusCanId when data.Length >= 4 => ApplyEspStatus(sample, data),
            _ => sample,
        };

        // The three wheel-speed extractions are transcribed verbatim from the guide. Note that b[1]
        // appears twice in the RF/RR formula (masked high bits, then shifted whole) — that is how the
        // reverse-engineered layout is published, and the guide flags the packing as 12-bit internal
        // vs 14-bit on-wire, so treat absolute values as provisional until checked against a
        // reference tester. The masks below keep every field to its documented 14 bits.
        private static AbsTelemetrySample ApplyFrontWheels(AbsTelemetrySample sample, byte[] d)
        {
            int rf = (((d[1] & 0xC0) >> 6) | ((d[0] & 0x0F) << 10) | (d[1] << 2)) & 0x3FFF;
            int lf = (((d[1] & 0x3F) << 8) | d[3]) & 0x3FFF;
            int car = (((d[0] & 0xF0) >> 4) | (d[4] << 4) | ((d[6] & 0x03) << 12)) & 0x3FFF;

            return sample with
            {
                Timestamp = DateTime.Now,
                RawA2 = Convert.ToHexString(d),
                WheelRf = Valid(rf),
                WheelLf = Valid(lf),
                VehicleSpeedRaw = Valid(car),
                CounterA2 = (d[6] & 0x3C) >> 2,
                ChecksumA2Ok = Xor(d, 0, 6) == d[6],
            };
        }

        private static AbsTelemetrySample ApplyRearWheels(AbsTelemetrySample sample, byte[] d)
        {
            int rr = (((d[1] & 0xC0) >> 6) | ((d[0] & 0x0F) << 10) | (d[1] << 2)) & 0x3FFF;
            int lr = (((d[1] & 0x3F) << 8) | d[3]) & 0x3FFF;

            return sample with
            {
                Timestamp = DateTime.Now,
                RawA4 = Convert.ToHexString(d),
                WheelRr = Valid(rr),
                WheelLr = Valid(lr),
                BrakeSwitch = d[4] & 0x03,
                CounterA4 = (d[0] & 0xF0) >> 4,
                ChecksumA4Ok = Xor(d, 0, 5) == d[5],
            };
        }

        private static AbsTelemetrySample ApplyEspStatus(AbsTelemetrySample sample, byte[] d) => sample with
        {
            Timestamp = DateTime.Now,
            RawA8 = Convert.ToHexString(d),
            EspActive = (d[1] & 0x08) != 0,
            AbsActive = (d[1] & 0x20) != 0,
            TorqueRequest = (d[1] & 0x40) != 0,
            NoIntervention = (d[1] & 0x80) != 0,
            EspWarning = (d[3] & 0x40) != 0,
        };

        /// <summary>Maps the all-ones sentinel to null so an unavailable sensor is not shown as a speed.</summary>
        private static int? Valid(int raw) => raw == InvalidWheelSentinel ? null : raw;

        /// <summary>XOR of <paramref name="count"/> bytes from <paramref name="start"/> — the frame checksum.</summary>
        private static byte Xor(byte[] data, int start, int count)
        {
            byte sum = 0;
            for (int i = start; i < start + count; i++)
                sum ^= data[i];
            return sum;
        }
    }
}
