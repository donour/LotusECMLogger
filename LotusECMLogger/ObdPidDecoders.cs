using System.Collections.Frozen;

namespace LotusECMLogger
{
    /// <summary>
    /// The PID tables behind <see cref="LiveDataReading.ParseCanResponse(byte[])"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each entry states a PID's payload width once and how to turn that payload into readings. The
    /// width is what couples the length check to the cursor advance: previously both were retyped per
    /// PID and could disagree, which is how seven Mode 22 decoders came to read a byte their own guard
    /// did not cover, and how an unknown PID came to advance a guessed single byte into its payload.
    /// </para>
    /// <para>
    /// A decoder receives only its own payload, so it cannot read past its declared width and has no
    /// cursor to mis-advance. Adding a PID means adding a row.
    /// </para>
    /// </remarks>
    internal static class ObdPidDecoders
    {
        /// <summary>Turns one PID's payload into zero or more readings.</summary>
        internal delegate void PidDecode(ReadOnlySpan<byte> payload, List<LiveDataReading> results);

        /// <summary>Turns one PID's payload into a single numeric value.</summary>
        internal delegate double PidValue(ReadOnlySpan<byte> payload);

        /// <param name="Width">Payload bytes, excluding the PID byte(s) that select this decoder.</param>
        internal sealed record PidDecoder(byte Width, PidDecode Decode);

        /// <summary>A PID that yields exactly one named reading.</summary>
        private static PidDecoder Scalar(byte width, string name, PidValue value) =>
            new(width, (payload, results) => results.Add(new LiveDataReading
            {
                name = name,
                value_f = value(payload),
            }));

        private static int U16(ReadOnlySpan<byte> p) => (p[0] << 8) | p[1];
        private static short I16(ReadOnlySpan<byte> p) => (short)((p[0] << 8) | p[1]);

        // ── Mode 01: current data ────────────────────────────────────────────────────────

        public static readonly FrozenDictionary<byte, PidDecoder> Mode01 = new Dictionary<byte, PidDecoder>
        {
            [0x05] = Scalar(1, "Coolant Temperature", p => p[0] - 40),
            [0x06] = Scalar(1, "Short Term Fuel Trim Bank 1", p => p[0] / 1.28f - 100.0f),
            [0x07] = Scalar(1, "Long Term Fuel Trim Bank 1", p => p[0] / 1.28f - 100.0f),
            [0x08] = Scalar(1, "Short Term Fuel Trim Bank 2", p => p[0] / 1.28f - 100.0f),
            [0x09] = Scalar(1, "Long Term Fuel Trim Bank 2", p => p[0] / 1.28f - 100.0f),
            [0x0A] = Scalar(1, "FuelPressure(bar)", p => p[0] * 3f / 100f),
            [0x0B] = Scalar(1, "Intake Manifold Pressure", p => p[0]),
            [0x0C] = Scalar(2, "Engine Speed", p => U16(p) / 4.0),
            [0x0D] = Scalar(1, "Vehicle Speed", p => p[0]),
            [0x0E] = Scalar(1, "Timing Advance", p => p[0] / 2.0f - 64.0f),
            [0x0F] = Scalar(1, "Intake Air Temperature", p => p[0] - 40),
            [0x10] = Scalar(2, "maf (g/s)", p => U16(p) / 100.0f),

            // J1979: A * 100 / 255 %. The ECU applies no scaling of its own -- obd_ii_mode01_processing
            // packs a single byte from get_tps(), which is adc_dma_dest[0x30] >> 6 off the 14-bit TPS-A
            // channel. Mode 22 TPSActual (0x0245) reports that same channel as >> 4 over 1024, so the
            // two describe one full scale and must agree.
            [0x11] = Scalar(1, "Throttle Position", p => p[0] * 100.0 / 255.0),

            [0x24] = OxygenSensor(1),
            [0x25] = OxygenSensor(2),
            [0x26] = OxygenSensor(3),
            [0x27] = OxygenSensor(4),
            [0x28] = OxygenSensor(5),
            [0x29] = OxygenSensor(6),

            [0x43] = Scalar(2, "Absolute Load", p => U16(p) * 100 / 255),
            [0x44] = Scalar(2, "Commanded Equivalence Ratio", p => U16(p) / 32768.0),
            [0x46] = Scalar(1, "Ambient Air Temperature", p => p[0] - 40),
        }.ToFrozenDictionary();

        /// <summary>Wide-range oxygen sensor: AB is the equivalence ratio, CD the sensor voltage.</summary>
        private static PidDecoder OxygenSensor(int sensor) => new(4, (p, results) =>
        {
            results.Add(new LiveDataReading
            {
                name = $"O2SensorLambda{sensor}",
                value_f = 2.0 / 65536.0 * U16(p),
            });
            results.Add(new LiveDataReading
            {
                name = $"O2SensorVoltage{sensor}",
                value_f = 8.0 / 65536.0 * U16(p[2..]),
            });
        });

        /// <summary>
        /// Payload widths for the standard J1979 Mode 01 PIDs this decoder does not interpret. A PID
        /// listed here is stepped over cleanly, so one undecoded parameter in a multi-PID reply no
        /// longer costs every reading behind it -- which is what <c>lotus-diagnostic.json</c> hits
        /// with PIDs 0x21 and 0x31.
        /// </summary>
        public static readonly FrozenDictionary<byte, byte> Mode01StandardWidths = BuildStandardWidths();

        private static FrozenDictionary<byte, byte> BuildStandardWidths()
        {
            var widths = new Dictionary<byte, byte>
            {
                [0x01] = 4, [0x02] = 2, [0x03] = 2, [0x04] = 1, [0x12] = 1, [0x13] = 1,
                [0x1C] = 1, [0x1D] = 1, [0x1E] = 1, [0x1F] = 2, [0x21] = 2, [0x22] = 2,
                [0x23] = 2, [0x2C] = 1, [0x2D] = 1, [0x2E] = 1, [0x2F] = 1, [0x30] = 1,
                [0x31] = 2, [0x32] = 2, [0x33] = 1, [0x3C] = 2, [0x3D] = 2, [0x3E] = 2,
                [0x3F] = 2, [0x42] = 2, [0x45] = 1, [0x47] = 1, [0x48] = 1, [0x49] = 1,
                [0x4A] = 1, [0x4B] = 1, [0x4C] = 1, [0x4D] = 2, [0x4E] = 2, [0x51] = 1,
                [0x52] = 1, [0x53] = 2, [0x54] = 2, [0x55] = 2, [0x56] = 2, [0x57] = 2,
                [0x58] = 2, [0x59] = 2, [0x5A] = 1, [0x5B] = 1, [0x5C] = 1, [0x5D] = 2,
                [0x5E] = 2, [0x5F] = 1,
            };

            // Supported-PID bitmasks and the 4-byte monitor/status PIDs.
            foreach (byte pid in new byte[] { 0x00, 0x20, 0x40, 0x60, 0x41, 0x4F, 0x50 })
                widths[pid] = 4;

            // Oxygen sensors 7 and 8 share the wide-range layout of the ones decoded above.
            widths[0x2A] = 4;
            widths[0x2B] = 4;

            // Sensor-bank PIDs 0x14-0x1B are two bytes each.
            for (byte pid = 0x14; pid <= 0x1B; pid++)
                widths[pid] = 2;

            return widths.ToFrozenDictionary();
        }

        // ── Mode 09: vehicle information ─────────────────────────────────────────────────

        public static readonly FrozenDictionary<byte, PidDecoder> Mode09 = new Dictionary<byte, PidDecoder>
        {
            [0x00] = new(4, (p, results) => results.Add(new LiveDataReading
            {
                name = "SupportedPIDs_01_20",
                value_l = (uint)((p[0] << 24) | (p[1] << 16) | (p[2] << 8) | p[3]),
            })),
        }.ToFrozenDictionary();

        // ── Mode 22: manufacturer extensions ─────────────────────────────────────────────

        /// <summary>
        /// Keyed by the full 16-bit Mode 22 PID, matching how the firmware's
        /// <c>obd_ii_mode22_processing</c> switches on it.
        /// </summary>
        public static readonly FrozenDictionary<ushort, PidDecoder> Mode22 = BuildMode22();

        private static FrozenDictionary<ushort, PidDecoder> BuildMode22()
        {
            var map = new Dictionary<ushort, PidDecoder>
            {
                [0x0202] = Scalar(1, "PurgeDutyCycle", p => p[0] * 100 / 255),
                [0x0205] = Scalar(3, "InjectorPulseTimeBank1(us)", p => (p[0] << 16) | (p[1] << 8) | p[2]),
                [0x0217] = Scalar(3, "InjectorPulseTimeBank2(us)", p => (p[0] << 16) | (p[1] << 8) | p[2]),
                [0x0208] = Scalar(2, "VVTI B1 intake (deg)", p => I16(p) / 4.0),
                [0x024B] = Scalar(2, "VVTI B2 intake (deg)", p => I16(p) / 4.0),
                [0x0250] = Scalar(2, "VVTI B1 exhaust (deg)", p => I16(p) / 4.0),
                [0x0251] = Scalar(2, "VVTI B2 exhaust (deg)", p => I16(p) / 4.0),
                [0x0213] = Scalar(1, "AFR Target", p => p[0] * 0.01),
                [0x022A] = Scalar(1, "load_pct", p => p[0]),
                [0x023A] = Scalar(2, "FuelLearnTimer", p => U16(p)),
                [0x023B] = Scalar(2, "TPSTarget", p => U16(p) * 100.0 / 1024),
                [0x0245] = Scalar(2, "TPSActual", p => U16(p) * 100.0 / 1024),
                [0x0246] = Scalar(2, "AcceleratorPedalPosition", p => U16(p) * 100.0 / 1024),
                [0x026A] = Scalar(2, "TorqueNM", p => I16(p)),
                [0x0272] = Scalar(1, "ManifoldTempC", p => p[0] * 5 / 8 - 40),
                [0x02C7] = Scalar(1, "TransFluidTempC", p => p[0] * 5 / 8 - 40),
                [0x02C9] = Scalar(1, "ChargecoolerDutycycle", p => p[0] * 100 / 255),
                [0x022E] = Scalar(2, "FuelLearnLeanTimeBank1(us)", p => I16(p)),
                [0x0255] = Scalar(2, "FuelLearnLeanTimeBank2(us)", p => I16(p)),

                [0x0231] = KnockRetard(firstCylinder: 1, cylinders: 4),
                [0x0256] = KnockRetard(firstCylinder: 5, cylinders: 2),

                [0x0403] = WidebandCalibration(bank: 1),
                [0x0404] = WidebandCalibration(bank: 2),
            };

            // Regional fuel-learn zone trims: one offset-128 byte, 0x80 neutral, ~0.391 % per count.
            foreach (var (pid, name) in new (ushort, string)[]
            {
                (0x0248, "FuelLearnZone2Bank1"), (0x0249, "FuelLearnZone3Bank1"),
                (0x025A, "FuelLearnZone2Bank2"), (0x025B, "FuelLearnZone3Bank2"),
            })
            {
                map[pid] = Scalar(1, name, p => (sbyte)(p[0] - 0x80) * 500.0 / 128 / 10);
            }

            // Misfire counters. The firmware packs misfires_per_cyl[0], [2], [3], [1] for PIDs
            // 0x0234-0x0237 and [4], [5] for 0x0257/0x0258 -- a cylinder-indexed array read in
            // four-cylinder firing order, so these PIDs genuinely are permuted.
            foreach (var (pid, cylinder) in new (ushort, int)[]
            {
                (0x0234, 1), (0x0235, 3), (0x0236, 4), (0x0237, 2), (0x0257, 5), (0x0258, 6),
            })
            {
                map[pid] = Scalar(2, $"MisfireCylinder{cylinder}", p => U16(p));
            }

            // Learned octane scaler. Unlike the misfire counters, these are NOT permuted: the firmware
            // packs LEA_octane_scaler[0..5] in PID order into a cylinder-indexed array. See OctaneScaler.
            foreach (var (pid, cylinder) in OctaneScaler.CylinderByPid)
            {
                map[(ushort)(0x0200 | pid)] =
                    Scalar(2, $"OctaneRatingCylinder{cylinder}", p => OctaneScaler.ToPercent(U16(p)));
            }

            return map.ToFrozenDictionary();
        }

        /// <summary>
        /// Decodes a Mode 22 payload that a caller has already read off the wire. Callers doing their
        /// own request/response handling (the vehicle information reader) share the table through this
        /// rather than restating what a PID's bytes mean.
        /// </summary>
        public static IReadOnlyList<LiveDataReading> DecodeMode22Payload(ushort pid, ReadOnlySpan<byte> payload)
        {
            var results = new List<LiveDataReading>();
            if (Mode22.TryGetValue(pid, out PidDecoder? decoder) && payload.Length >= decoder.Width)
                decoder.Decode(payload[..decoder.Width], results);
            return results;
        }

        /// <summary>Per-cylinder knock retard, one byte each at quarter-degree resolution.</summary>
        private static PidDecoder KnockRetard(int firstCylinder, int cylinders) =>
            new((byte)cylinders, (p, results) =>
            {
                for (int i = 0; i < cylinders; i++)
                {
                    results.Add(new LiveDataReading
                    {
                        name = $"KnockSparkRetardCylinder {firstCylinder + i}",
                        value_f = p[i] / 4.0,
                    });
                }
            });

        /// <summary>Wideband calibration: u16 slope (4096 = 1.0x) then i16 offset in ADC counts.</summary>
        private static PidDecoder WidebandCalibration(int bank) => new(4, (p, results) =>
        {
            results.Add(new LiveDataReading { name = $"WBCalSlope{bank}", value_f = (ushort)U16(p) });
            results.Add(new LiveDataReading { name = $"WBCalOffset{bank}", value_f = I16(p[2..]) });
        });
    }
}
