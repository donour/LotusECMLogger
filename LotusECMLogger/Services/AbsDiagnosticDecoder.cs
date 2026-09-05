using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LotusECMLogger.Services
{
    public sealed record AbsCountInterval(int Minimum, int Maximum);
    public sealed record AbsWheelReading(string Name, ushort Raw, double? Kph, string Status,
        AbsCountInterval? SourceCounts);
    public sealed record AbsSignedReading(string Name, short Raw, double Value, string Unit,
        AbsCountInterval? SourceCounts);
    public sealed record AbsVoltageReading(string Name, byte Raw, double Volts);
    public sealed record AbsLiveRecord(string ResponseHex, IReadOnlyList<AbsWheelReading> Wheels,
        AbsSignedReading YawRate, AbsSignedReading Pressure, AbsSignedReading LongitudinalAcceleration,
        AbsVoltageReading BrakeLightSwitch, AbsVoltageReading Battery,
        IReadOnlyList<string> Observations, IReadOnlyList<AbsReportRow> Rows);
    public sealed record AbsCodingRecord(ushort Word, bool Available,
        IReadOnlyList<int> MatchingStoredProfiles, IReadOnlyList<AbsReportRow> Rows);
    public sealed record AbsProcessRecord(byte Raw, string OemLabel, bool PossibleStorageReadFailure,
        IReadOnlyList<AbsReportRow> Rows);

    /// <summary>
    /// Pure decoders for complete reassembled diagnostic responses, including positive SID/PID.
    /// The live layout and optional stored-profile table are derived from BB68638 V0201.
    /// No image file, hardware connection, request transmission or active-profile inference is used.
    /// </summary>
    public static class AbsDiagnosticDecoder
    {
        private static readonly string[] WheelNames = ["front_left", "front_right", "rear_left", "rear_right"];
        private static readonly string[] WheelLabels = ["Front left wheel", "Front right wheel", "Rear left wheel", "Rear right wheel"];
        private static readonly ushort[] Bb68638CodingWords =
            [0x410E, 0x4107, 0x4108, 0x4100, 0x4103, 0x410A, 0xF110, 0xF112, 0xF112, 0xF112];
        private static readonly byte[] Bb68638BuildResponse =
            [0x5A, 0x85, .. Encoding.ASCII.GetBytes("6863802010000"), .. new byte[13]];
        private static readonly byte[] Bb68638PartResponse =
            [0x5A, 0x87, .. Encoding.ASCII.GetBytes("A132J0314A ")];

        /// <summary>
        /// Matches the complete reported build and part records, including their padding.
        /// A match permits reference-layout interpretation; it does not verify a stock firmware hash.
        /// Malformed, missing and unequal responses return false.
        /// </summary>
        public static bool MatchesBb68638Identity(byte[] buildResponse, byte[] partResponse) =>
            buildResponse is not null && partResponse is not null &&
            buildResponse.AsSpan().SequenceEqual(Bb68638BuildResponse) &&
            partResponse.AsSpan().SequenceEqual(Bb68638PartResponse);

        public static AbsLiveRecord DecodeLiveRecord(byte[] fullResponse)
        {
            RequireResponse(fullResponse, 22, 0x61, 0x04);
            ReadOnlySpan<byte> data = fullResponse.AsSpan(2);
            var observations = new List<string>();
            var rows = new List<AbsReportRow>();
            var wheels = new List<AbsWheelReading>(4);
            for (int index = 0; index < WheelNames.Length; index++)
            {
                int offset = index * 2;
                ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
                AbsCountInterval? interval = WheelSourceInterval(raw);
                string status = raw switch
                {
                    0x3FFF => "fault_sentinel",
                    0 => "zero_or_below_report_threshold",
                    _ => "numeric_reply",
                };
                double? kph = raw == 0x3FFF ? null : raw * 9.0 / 160;
                wheels.Add(new AbsWheelReading(WheelNames[index], raw, kph, status, interval));
                if (raw != 0x3FFF && interval is null)
                    observations.Add($"{WheelNames[index]}: count {raw} has no source in the verified normal wheel-export conversion");
                rows.Add(new AbsReportRow(WheelLabels[index], kph.HasValue ? $"{Number(kph.Value)} km/h" : "unavailable (fault sentinel)",
                    $"Raw {raw} (0x{raw:X4}); wire {Hex(data.Slice(offset, 2))}; {status}; source counts {Interval(interval)}"));
            }

            AbsSignedReading yaw = DecodeSigned(data, 10, "yaw_rate", "degrees/s", 2715, 10000, 1220, 2715, observations);
            AbsSignedReading pressure = DecodeSigned(data, 12, "pressure", "bar", 3255, 10000, 153, 3255, observations);
            AbsSignedReading acceleration = DecodeSigned(data, 14, "longitudinal_acceleration", "m/s^2", 192, 1000, 271, 1920, observations);
            AddSignedRow(rows, "Yaw rate", yaw, data.Slice(10, 2));
            AddSignedRow(rows, "Pressure", pressure, data.Slice(12, 2));
            AddSignedRow(rows, "Longitudinal acceleration", acceleration, data.Slice(14, 2));
            var brakeLight = new AbsVoltageReading("brake_light_switch", data[16], data[16] * 2.0 / 25);
            var battery = new AbsVoltageReading("battery", data[17], data[17] * 2.0 / 25);
            rows.Add(new AbsReportRow("Brake light switch voltage", $"{Number(brakeLight.Volts)} V", $"Raw {brakeLight.Raw} (0x{brakeLight.Raw:X2})"));
            rows.Add(new AbsReportRow("Battery voltage", $"{Number(battery.Volts)} V", $"Raw {battery.Raw} (0x{battery.Raw:X2})"));

            foreach (int offset in new[] { 8, 18 })
            {
                bool isZero = data[offset] == 0 && data[offset + 1] == 0;
                rows.Add(new AbsReportRow($"Reserved data bytes {offset}..{offset + 1}", Hex(data.Slice(offset, 2)),
                    isZero ? "Matches the reference writer's zero bytes" : "Differs from the reference writer; raw data retained"));
                if (!isZero)
                    observations.Add($"data bytes {offset}..{offset + 1} differ from the verified writer's zero bytes; raw data retained");
            }
            foreach (string observation in observations)
                rows.Add(new AbsReportRow("Consistency observation", observation));
            rows.Add(new AbsReportRow("Interpretation", "BB68638 V0201 reference layout",
                "The payload does not establish ECU identity, active calibration or physical accuracy. Wheel values precede learned controller correction; zero can mean below the reporting threshold."));
            rows.Add(new AbsReportRow("Source-count intervals", "Arithmetic consistency ranges",
                "Intervals invert the reference integer conversion, assuming coherent samples. They are not direct RAM reads; snapshot concurrency, sensor orientation, timing and physical calibration remain unverified."));
            string responseHex = Hex(fullResponse);
            rows.Add(new AbsReportRow("Raw 61 04 response", responseHex));
            return new AbsLiveRecord(responseHex, wheels.AsReadOnly(), yaw, pressure, acceleration,
                brakeLight, battery, observations.AsReadOnly(), rows.AsReadOnly());
        }

        public static AbsCodingRecord DecodeCoding(byte[] fullResponse, bool matchesBb68638 = false)
        {
            RequireResponse(fullResponse, 4, 0x61, 0x01);
            byte low = fullResponse[2], high = fullResponse[3];
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(fullResponse.AsSpan(2, 2));
            bool available = (low & 1) != 0;
            var matches = new List<int>();
            if (matchesBb68638)
                for (int index = 0; index < Bb68638CodingWords.Length; index++)
                    if (Bb68638CodingWords[index] == word)
                        matches.Add(index + 1);

            var rows = new List<AbsReportRow>
            {
                new("Coding word", $"0x{word:X4}", "Wire order is coding low byte, then high byte"),
                new("Coding availability", available ? "available" : "unavailable", "OEM VARIANT is bit 0, not a numeric profile index"),
                CodingField("Gearbox", low & 0x06, (low & 0x06) switch
                {
                    0 => "manual without LSD", 2 => "automatic", 4 => "MMT", 6 => "manual with LSD", _ => "unknown",
                }),
                CodingField("Model", low & 0x38, (low & 0x38) switch { 0 => "Evora", 8 => "Elise/Exige", _ => "unknown" }),
                CodingField("Energy", low & 0x40, (low & 0x40) == 0 ? "gasoline" : "unknown"),
                CodingField("Brake system", high & 0x0F, (high & 0x0F) switch { 0 => "TCS", 1 => "ESP", _ => "unknown" }),
                CodingField("Engine", high & 0xF0, (high & 0xF0) switch
                {
                    0 => "3.5 litre", 0x10 => "1.6 litre", 0x20 => "3.5 litre supercharged",
                    0x30 => "1.8 litre supercharged", 0x40 => "3.5 litre supercharged 400 hp", _ => "unknown",
                }),
                new("Uninterpreted coding bit 7", (low & 0x80) != 0 ? "set" : "clear", $"Low-byte mask 0x80; raw 0x{low & 0x80:X2}"),
                new("Matching stored profiles", matchesBb68638 ? (matches.Count == 0 ? "none" : string.Join(", ", matches)) : "not evaluated",
                    matchesBb68638
                        ? "Reference BB68638 coding-table matches only. Duplicate entries remain distinct; stored coding does not establish the active RAM profile. Reported identity does not verify the firmware hash."
                        : "BB68638 identity has not been matched; the version-specific table is not applied. Stored coding does not establish the active RAM profile."),
                new("Coding interpretation", word == 0 ? "May represent stored FF / uncoded state" : "OEM field definitions",
                    "Availability and table matches do not establish session permissions or hardware compatibility. Unknown values are retained."),
                new("Raw 61 01 response", Hex(fullResponse)),
            };
            return new AbsCodingRecord(word, available, matches.AsReadOnly(), rows.AsReadOnly());
        }

        public static AbsProcessRecord DecodeProcess(byte[] fullResponse)
        {
            RequireResponse(fullResponse, 3, 0x61, 0xBF);
            byte raw = fullResponse[2];
            string label = raw switch
            {
                0 => "FILLINGINCOMPANDOK", 0xAA => "FILLINGINNOTCOMP",
                0xEE => "FILLINGINCOMPANDNOTOK", 0xFF => "BOSCHDELSTATE", _ => "unknown",
            };
            bool possibleFailure = raw == 0x99;
            var rows = new List<AbsReportRow>
            {
                new("Process byte", $"0x{raw:X2}", $"OEM label: {label}"),
                new("Process interpretation", possibleFailure ? "Possible storage-read failure sentinel" : label,
                    "The raw process byte is retained. It does not identify the active calibration profile."),
                new("Raw 61 BF response", Hex(fullResponse)),
            };
            return new AbsProcessRecord(raw, label, possibleFailure, rows.AsReadOnly());
        }

        private static void RequireResponse(byte[] response, int length, byte sid, byte pid)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (response.Length != length || response[0] != sid || response[1] != pid)
                throw new ArgumentException($"Expected exactly {length} bytes beginning {sid:X2} {pid:X2}; supply one reassembled diagnostic payload without CAN IDs, ISO-TP headers or padding.", nameof(response));
        }

        private static AbsSignedReading DecodeSigned(ReadOnlySpan<byte> data, int offset, string name,
            string unit, int scaleNumerator, int scaleDenominator, int multiplier, int divisor, List<string> observations)
        {
            short raw = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
            AbsCountInterval? interval = SignedSourceInterval(raw, multiplier, divisor);
            if (interval is null)
                observations.Add($"{name}: count {raw} has no signed16 source in the verified conversion");
            return new AbsSignedReading(name, raw, raw * (double)scaleNumerator / scaleDenominator, unit, interval);
        }

        private static AbsCountInterval? SignedSourceInterval(short quotient, int multiplier, int divisor)
        {
            int low, high;
            if (quotient == 0)
            {
                high = CeilDiv(divisor, multiplier) - 1;
                low = -high;
            }
            else
            {
                int magnitude = Math.Abs((int)quotient);
                low = CeilDiv(magnitude * divisor, multiplier);
                high = CeilDiv((magnitude + 1) * divisor, multiplier) - 1;
                if (quotient < 0)
                    (low, high) = (-high, -low);
            }
            low = Math.Max(short.MinValue, low);
            high = Math.Min(short.MaxValue, high);
            return low <= high ? new AbsCountInterval(low, high) : null;
        }

        private static AbsCountInterval? WheelSourceInterval(ushort raw)
        {
            if (raw == 0)
                return new AbsCountInterval(0, CeilDiv(48 * 64, 71) - 1);
            if (raw < 48 || raw == 0x3FFF)
                return null;
            int low = Math.Max(0, CeilDiv(raw * 64, 71));
            int high = Math.Min(5760, CeilDiv((raw + 1) * 64, 71) - 1);
            return low <= high ? new AbsCountInterval(low, high) : null;
        }

        private static int CeilDiv(int positiveNumerator, int positiveDenominator) =>
            (positiveNumerator + positiveDenominator - 1) / positiveDenominator;

        private static AbsReportRow CodingField(string name, int masked, string meaning) =>
            new(name, meaning, $"Masked raw 0x{masked:X2}");

        private static void AddSignedRow(List<AbsReportRow> rows, string label, AbsSignedReading reading, ReadOnlySpan<byte> wire) =>
            rows.Add(new AbsReportRow(label, $"{Number(reading.Value)} {reading.Unit}",
                $"Raw {reading.Raw} (0x{unchecked((ushort)reading.Raw):X4}); wire {Hex(wire)}; source counts {Interval(reading.SourceCounts)}"));

        private static string Interval(AbsCountInterval? interval) => interval is null
            ? "unavailable for the reference conversion"
            : $"{interval.Minimum}..{interval.Maximum}";

        private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        private static string Hex(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray()).Replace('-', ' ');
    }
}
