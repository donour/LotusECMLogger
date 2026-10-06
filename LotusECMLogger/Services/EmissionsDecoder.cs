namespace LotusECMLogger.Services
{
    /// <summary>
    /// Pure decode helpers for the OBD-II data an emissions inspection reads: the monitor status
    /// PIDs (0x01 since codes cleared, 0x41 this drive cycle), the supported-PID bitmasks, the
    /// OBD standard (PID 0x1C), the since-cleared counters, and the model year from the VIN.
    /// </summary>
    public static class EmissionsDecoder
    {
        // [00 00 07 E8] 0x41 <pid> — anything shorter cannot carry data.
        private const int PayloadStart = 6;
        private const byte PositiveSid = 0x41;

        // Byte B: bits 0-2 supported, bits 4-6 incomplete, in this order.
        private static readonly ReadinessMonitorId[] ContinuousMonitors =
        [
            ReadinessMonitorId.Misfire,
            ReadinessMonitorId.FuelSystem,
            ReadinessMonitorId.ComprehensiveComponents,
        ];

        // Bytes C (supported) and D (incomplete), bit 0 first. Null marks a reserved bit.
        private static readonly ReadinessMonitorId?[] SparkMonitors =
        [
            ReadinessMonitorId.Catalyst,
            ReadinessMonitorId.HeatedCatalyst,
            ReadinessMonitorId.EvaporativeSystem,
            ReadinessMonitorId.SecondaryAirSystem,
            ReadinessMonitorId.GasolinePartFilter, // long reserved; older tools label it A/C refrigerant
            ReadinessMonitorId.OxygenSensor,
            ReadinessMonitorId.OxygenSensorHeater,
            ReadinessMonitorId.EgrVvt,
        ];

        private static readonly ReadinessMonitorId?[] CompressionMonitors =
        [
            ReadinessMonitorId.NmhcCatalyst,
            ReadinessMonitorId.NoxScrAftertreatment,
            null,
            ReadinessMonitorId.BoostPressure,
            null,
            ReadinessMonitorId.ExhaustGasSensor,
            ReadinessMonitorId.PmFilter,
            ReadinessMonitorId.EgrVvt,
        ];

        /// <summary>The since-cleared counters an inspection reports, in display order.</summary>
        public static readonly IReadOnlyList<byte> CounterPids = [0x30, 0x31, 0x4E, 0x21, 0x4D];

        public static string MonitorName(ReadinessMonitorId id) => id switch
        {
            ReadinessMonitorId.Misfire => "Misfire",
            ReadinessMonitorId.FuelSystem => "Fuel System",
            ReadinessMonitorId.ComprehensiveComponents => "Comprehensive Components",
            ReadinessMonitorId.Catalyst => "Catalyst",
            ReadinessMonitorId.HeatedCatalyst => "Heated Catalyst",
            ReadinessMonitorId.EvaporativeSystem => "Evaporative System (EVAP)",
            ReadinessMonitorId.SecondaryAirSystem => "Secondary Air System",
            ReadinessMonitorId.GasolinePartFilter => "Gasoline Particulate Filter",
            ReadinessMonitorId.OxygenSensor => "Oxygen Sensor",
            ReadinessMonitorId.OxygenSensorHeater => "Oxygen Sensor Heater",
            ReadinessMonitorId.EgrVvt => "EGR / VVT System",
            ReadinessMonitorId.NmhcCatalyst => "NMHC Catalyst",
            ReadinessMonitorId.NoxScrAftertreatment => "NOx / SCR Aftertreatment",
            ReadinessMonitorId.BoostPressure => "Boost Pressure",
            ReadinessMonitorId.ExhaustGasSensor => "Exhaust Gas Sensor",
            ReadinessMonitorId.PmFilter => "PM Filter",
            _ => id.ToString(),
        };

        public static MonitorKind MonitorKindOf(ReadinessMonitorId id) =>
            ContinuousMonitors.Contains(id) ? MonitorKind.Continuous : MonitorKind.NonContinuous;

        /// <summary>
        /// Returns the data bytes of a positive single-PID Mode 01 response, or an empty span
        /// when the buffer is not a response to <paramref name="pid"/>.
        /// </summary>
        public static ReadOnlySpan<byte> Payload(byte[] response, byte pid)
        {
            if (response.Length <= PayloadStart || response[4] != PositiveSid || response[5] != pid)
                return [];
            return response.AsSpan(PayloadStart);
        }

        /// <summary>
        /// Parses a supported-PID bitmask response (PID 0x00, 0x20, 0x40, ...) into absolute PID
        /// numbers. Returns an empty list for a response that does not match
        /// <paramref name="basePid"/>.
        /// </summary>
        public static IReadOnlyList<int> ParseSupportedPids(byte[] response, int basePid)
        {
            var supported = new List<int>();
            var bitmask = Payload(response, (byte)basePid);

            for (int i = 0; i < bitmask.Length && i < 4; i++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((bitmask[i] & (1 << (7 - bit))) != 0)
                        supported.Add(basePid + i * 8 + bit + 1);
                }
            }

            return supported;
        }

        /// <summary>
        /// Decodes PID 0x01: MIL status and DTC count (byte A), then the monitor status since
        /// codes were last cleared. The ignition type comes from bit 3 of byte B.
        /// </summary>
        /// <exception cref="ArgumentException">Fewer than four data bytes.</exception>
        public static MonitorStatus DecodeMonitorStatusSinceClear(ReadOnlySpan<byte> abcd)
        {
            RequireFourBytes(abcd);
            var ignition = (abcd[1] & 0x08) != 0 ? IgnitionType.Compression : IgnitionType.Spark;
            return Decode(abcd, ignition) with
            {
                MilOn = (abcd[0] & 0x80) != 0,
                DtcCount = abcd[0] & 0x7F,
            };
        }

        /// <summary>
        /// Decodes PID 0x41: the monitor status for the current drive cycle. Byte A is reserved;
        /// the supported bits mean "enabled this drive cycle". Takes the ignition type from PID
        /// 0x01, which defines it authoritatively.
        /// </summary>
        /// <exception cref="ArgumentException">Fewer than four data bytes.</exception>
        public static MonitorStatus DecodeMonitorStatusThisCycle(ReadOnlySpan<byte> abcd, IgnitionType ignition)
        {
            RequireFourBytes(abcd);
            return Decode(abcd, ignition);
        }

        private static void RequireFourBytes(ReadOnlySpan<byte> abcd)
        {
            if (abcd.Length < 4)
                throw new ArgumentException($"Monitor status needs 4 data bytes, got {abcd.Length}.", nameof(abcd));
        }

        private static MonitorStatus Decode(ReadOnlySpan<byte> abcd, IgnitionType ignition)
        {
            byte b = abcd[1], c = abcd[2], d = abcd[3];
            var monitors = new List<ReadinessMonitor>();

            for (int bit = 0; bit < ContinuousMonitors.Length; bit++)
            {
                monitors.Add(new ReadinessMonitor
                {
                    Id = ContinuousMonitors[bit],
                    Supported = (b & (1 << bit)) != 0,
                    Complete = (b & (1 << (bit + 4))) == 0,
                });
            }

            var nonContinuous = ignition == IgnitionType.Compression ? CompressionMonitors : SparkMonitors;
            for (int bit = 0; bit < 8; bit++)
            {
                if (nonContinuous[bit] is not ReadinessMonitorId id)
                    continue;
                monitors.Add(new ReadinessMonitor
                {
                    Id = id,
                    Supported = (c & (1 << bit)) != 0,
                    Complete = (d & (1 << bit)) == 0,
                });
            }

            return new MonitorStatus { Ignition = ignition, Monitors = monitors };
        }

        /// <summary>
        /// Decodes one of the <see cref="CounterPids"/>. Returns null for any other PID or a
        /// payload too short for it.
        /// </summary>
        public static EmissionsCounter? DecodeCounter(byte pid, ReadOnlySpan<byte> payload)
        {
            (string name, string unit, int width)? spec = pid switch
            {
                0x21 => ("Distance with MIL on", "km", 2),
                0x30 => ("Warm-ups since codes cleared", "", 1),
                0x31 => ("Distance since codes cleared", "km", 2),
                0x4D => ("Time run with MIL on", "min", 2),
                0x4E => ("Time since codes cleared", "min", 2),
                _ => null,
            };
            if (spec is not { } s || payload.Length < s.width)
                return null;

            int value = s.width == 1 ? payload[0] : (payload[0] << 8) | payload[1];
            return new EmissionsCounter { Pid = pid, Name = s.name, Value = value, Unit = s.unit };
        }

        private const int CalibrationIdLength = 16;
        private const int CvnLength = 4;

        /// <summary>
        /// Splits a Mode 09 PID 0x04 payload (after the PID and count bytes) into its 16-byte
        /// calibration IDs, each trimmed of the NUL/space padding J1979 allows. Trailing bytes too
        /// few for a whole ID are dropped.
        /// </summary>
        public static IReadOnlyList<string> ParseCalibrationIds(byte[] data)
        {
            var ids = new List<string>();
            for (int i = 0; i + CalibrationIdLength <= data.Length; i += CalibrationIdLength)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, i, CalibrationIdLength).Trim('\0', ' ');
                if (id.Length > 0)
                    ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// Splits a Mode 09 PID 0x06 payload (after the PID and count bytes) into its big-endian
        /// 4-byte CVNs. The T6 reports a CRC16 of its calibration as <c>00 00 hi lo</c>.
        /// </summary>
        public static IReadOnlyList<uint> ParseCvns(byte[] data)
        {
            var cvns = new List<uint>();
            for (int i = 0; i + CvnLength <= data.Length; i += CvnLength)
                cvns.Add((uint)((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]));
            return cvns;
        }

        /// <summary>The OBD requirement the vehicle was certified to (PID 0x1C, SAE J1979).</summary>
        public static string ObdStandardName(byte value) => value switch
        {
            1 => "OBD-II as defined by CARB",
            2 => "OBD as defined by the EPA",
            3 => "OBD and OBD-II",
            4 => "OBD-I",
            5 => "Not OBD compliant",
            6 => "EOBD (Europe)",
            7 => "EOBD and OBD-II",
            8 => "EOBD and OBD",
            9 => "EOBD, OBD and OBD-II",
            10 => "JOBD (Japan)",
            11 => "JOBD and OBD-II",
            12 => "JOBD and EOBD",
            13 => "JOBD, EOBD and OBD-II",
            14 => "OBD, EOBD and KOBD",
            15 => "OBD, OBD-II, EOBD and KOBD",
            17 => "Engine Manufacturer Diagnostics (EMD)",
            18 => "Engine Manufacturer Diagnostics Enhanced (EMD+)",
            19 => "Heavy Duty On-Board Diagnostics (Child/Partial)",
            20 => "Heavy Duty On-Board Diagnostics",
            21 => "World Wide Harmonized OBD",
            23 => "Heavy Duty Euro OBD Stage I without NOx control",
            24 => "Heavy Duty Euro OBD Stage I with NOx control",
            25 => "Heavy Duty Euro OBD Stage II without NOx control",
            26 => "Heavy Duty Euro OBD Stage II with NOx control",
            27 => "Heavy Duty ZEV",
            28 => "Brazil OBD Phase 1",
            29 => "Brazil OBD Phase 2",
            30 => "Korean OBD",
            31 => "India OBD I",
            32 => "India OBD II",
            33 => "Heavy Duty Euro OBD Stage VI",
            34 => "OBD, OBD-II and HD OBD",
            35 => "Brazil OBD Phase 3",
            _ => $"Reserved ({value})",
        };

        /// <summary>
        /// Decodes the model year from VIN position 10. The code repeats every 30 years; per
        /// 49 CFR 565.15 a letter in position 7 marks the 2010+ cycle and a digit the 1980-2009
        /// cycle. VINs from outside North America need not follow that rule, so a year before
        /// OBD-II (1996) is moved to the later cycle and one later than next year to the earlier.
        /// Returns null for a malformed VIN.
        /// </summary>
        public static int? ModelYearFromVin(string? vin) => ModelYearFromVin(vin, DateTime.Now.Year);

        internal static int? ModelYearFromVin(string? vin, int currentYear)
        {
            if (vin is null || vin.Length != 17)
                return null;

            const string codes = "ABCDEFGHJKLMNPRSTVWXY123456789";
            int index = codes.IndexOf(char.ToUpperInvariant(vin[9]));
            if (index < 0)
                return null;

            int year = 1980 + index;
            if (char.IsLetter(vin[6]) || year < 1996)
                year += 30;
            if (year > currentYear + 1 && year - 30 >= 1996)
                year -= 30;
            return year;
        }
    }
}
