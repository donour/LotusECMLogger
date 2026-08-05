namespace LotusECMLogger.Services
{
    /// <summary>
    /// One decoded snapshot of the ABS module's passive CAN broadcasts (guide §4). The module sends
    /// 0xA2/0xA4/0xA8 at 100 Hz with no request and no session, so this needs neither addressing nor
    /// a security unlock and is safe to read while the vehicle is moving.
    /// </summary>
    public sealed record AbsTelemetrySample
    {
        public DateTime Timestamp { get; init; } = DateTime.Now;

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

        /// <summary>
        /// Raw 14-bit wheel/vehicle counts converted to km/h. The multiplier is an ECU-side
        /// calibration (not stored in the ABS), so absolute km/h is only correct at the stock 1.0.
        /// </summary>
        public static double ToKph(int raw, double wheelMultiplier = 1.0) =>
            raw * 6.25 * wheelMultiplier / 1000.0;

        public static string BrakeSwitchName(int value) => value switch
        {
            0 => "released",
            1 => "pressed",
            2 => "fault/invalid",
            _ => "reserved",
        };
    }

    /// <summary>
    /// Decoders for the ABS module's three broadcast frames. Bit layouts are transcribed from
    /// <c>DIAGNOSTICS_PROGRAMMING_GUIDE.md</c> §4 / <c>CAN_MESSAGES.md</c>, which derive them from
    /// the firmware's CAN builders. They are independent of the diagnostic client — no session, no
    /// unlock, nothing transmitted.
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
