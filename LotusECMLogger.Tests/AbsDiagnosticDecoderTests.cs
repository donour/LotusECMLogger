using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Services;
using Xunit;

namespace LotusECMLogger.Tests
{
    public sealed class AbsDiagnosticDecoderTests
    {
        [Fact]
        public void LiveRecordUsesExactOffsetsLittleEndianAndOemScales()
        {
            byte[] response =
            [
                0x61, 0x04,
                0xE8, 0x03, 0xD0, 0x07, 0x00, 0x00, 0xFF, 0x3F,
                0x00, 0x00,
                0x0A, 0x00, 0xFE, 0xFF, 0x01, 0x00,
                150, 175, 0x00, 0x00,
            ];
            AbsLiveRecord decoded = AbsDiagnosticDecoder.DecodeLiveRecord(response);

            Assert.Equal(new[] { "front_left", "front_right", "rear_left", "rear_right" }, decoded.Wheels.Select(w => w.Name));
            Assert.Equal((ushort)1000, decoded.Wheels[0].Raw);
            Assert.Equal(56.25, decoded.Wheels[0].Kph);
            Assert.Equal(new AbsCountInterval(902, 902), decoded.Wheels[0].SourceCounts);
            Assert.Equal(112.5, decoded.Wheels[1].Kph);
            Assert.Equal(new AbsCountInterval(1803, 1803), decoded.Wheels[1].SourceCounts);
            Assert.Equal(0.0, decoded.Wheels[2].Kph);
            Assert.Equal("zero_or_below_report_threshold", decoded.Wheels[2].Status);
            Assert.Equal(new AbsCountInterval(0, 43), decoded.Wheels[2].SourceCounts);
            Assert.Equal("fault_sentinel", decoded.Wheels[3].Status);
            Assert.Null(decoded.Wheels[3].Kph);
            Assert.Null(decoded.Wheels[3].SourceCounts);
            Assert.Equal((short)10, decoded.YawRate.Raw);
            Assert.Equal(2.715, decoded.YawRate.Value, 10);
            Assert.Equal("degrees/s", decoded.YawRate.Unit);
            Assert.Equal(new AbsCountInterval(23, 24), decoded.YawRate.SourceCounts);
            Assert.Equal((short)-2, decoded.Pressure.Raw);
            Assert.Equal(-0.651, decoded.Pressure.Value, 10);
            Assert.Equal("bar", decoded.Pressure.Unit);
            Assert.Equal(new AbsCountInterval(-63, -43), decoded.Pressure.SourceCounts);
            Assert.Equal(0.192, decoded.LongitudinalAcceleration.Value, 10);
            Assert.Equal("m/s^2", decoded.LongitudinalAcceleration.Unit);
            Assert.Equal(new AbsCountInterval(8, 14), decoded.LongitudinalAcceleration.SourceCounts);
            Assert.Equal((byte)150, decoded.BrakeLightSwitch.Raw);
            Assert.Equal(12.0, decoded.BrakeLightSwitch.Volts);
            Assert.Equal(14.0, decoded.Battery.Volts);
            Assert.Empty(decoded.Observations);
            Assert.Equal(BitConverter.ToString(response).Replace('-', ' '), decoded.ResponseHex);
            Assert.Contains(decoded.Rows, row => row.Field == "Raw 61 04 response" && row.Value == decoded.ResponseHex);

            // The returned record is a snapshot and does not retain the caller's mutable buffer.
            Array.Fill(response, (byte)0xFF);
            Assert.Equal((ushort)1000, decoded.Wheels[0].Raw);
            Assert.StartsWith("61 04 E8 03", decoded.ResponseHex);
        }

        [Fact]
        public void ZeroSignedQuotientsIncludeBothSignsOfSmallSourceValues()
        {
            AbsLiveRecord decoded = AbsDiagnosticDecoder.DecodeLiveRecord(EmptyLiveResponse());
            Assert.Equal(new AbsCountInterval(-2, 2), decoded.YawRate.SourceCounts);
            Assert.Equal(new AbsCountInterval(-21, 21), decoded.Pressure.SourceCounts);
            Assert.Equal(new AbsCountInterval(-7, 7), decoded.LongitudinalAcceleration.SourceCounts);
        }

        [Fact]
        public void UnexpectedNumericAndReservedValuesRemainVisible()
        {
            byte[] response = EmptyLiveResponse();
            WriteDataWord(response, 0, 47);
            WriteDataWord(response, 2, 6391);
            WriteDataWord(response, 4, 6390);
            WriteDataWord(response, 6, 0x3FFF);
            WriteDataWord(response, 12, (ushort)short.MaxValue);
            response[10] = 0xA5; // data byte 8
            response[21] = 0x5A; // data byte 19
            AbsLiveRecord decoded = AbsDiagnosticDecoder.DecodeLiveRecord(response);

            Assert.Equal("numeric_reply", decoded.Wheels[0].Status);
            Assert.Equal(47 * 9.0 / 160, decoded.Wheels[0].Kph);
            Assert.Null(decoded.Wheels[0].SourceCounts);
            Assert.Equal((ushort)6391, decoded.Wheels[1].Raw);
            Assert.Null(decoded.Wheels[1].SourceCounts);
            Assert.Equal(new AbsCountInterval(5760, 5760), decoded.Wheels[2].SourceCounts);
            Assert.Null(decoded.Pressure.SourceCounts);
            Assert.Equal((short)32767, decoded.Pressure.Raw);
            Assert.Equal(32767 * 3255.0 / 10000, decoded.Pressure.Value);
            Assert.Equal(5, decoded.Observations.Count);
            Assert.Contains(decoded.Rows, row => row.Field == "Reserved data bytes 8..9" && row.Value == "A5 00");
            Assert.Contains(decoded.Rows, row => row.Field == "Reserved data bytes 18..19" && row.Value == "00 5A");
        }

        [Fact]
        public void EveryWireCountMatchesIndependentFiniteForwardConversionPreimages()
        {
            // This oracle enumerates actual signed16 source inputs using C#'s truncating
            // division. It does not reuse the decoder's inverse/ceiling-division algorithm.
            Dictionary<int, AbsCountInterval> yaw = SignedPreimages(1220, 2715);
            Dictionary<int, AbsCountInterval> pressure = SignedPreimages(153, 3255);
            Dictionary<int, AbsCountInterval> acceleration = SignedPreimages(271, 1920);
            var wheel = new Dictionary<int, AbsCountInterval>();
            for (int source = 0; source <= 5760; source++)
            {
                int raw = source * 71 / 64;
                AddPreimage(wheel, raw < 48 ? 0 : raw, source);
            }

            byte[] response = EmptyLiveResponse();
            for (int wire = 0; wire <= ushort.MaxValue; wire++)
            {
                short signed = unchecked((short)wire);
                WriteDataWord(response, 0, (ushort)wire);
                WriteDataWord(response, 10, (ushort)wire);
                WriteDataWord(response, 12, (ushort)wire);
                WriteDataWord(response, 14, (ushort)wire);
                AbsLiveRecord decoded = AbsDiagnosticDecoder.DecodeLiveRecord(response);
                Assert.Equal(wheel.GetValueOrDefault(wire), decoded.Wheels[0].SourceCounts);
                Assert.Equal(yaw.GetValueOrDefault(signed), decoded.YawRate.SourceCounts);
                Assert.Equal(pressure.GetValueOrDefault(signed), decoded.Pressure.SourceCounts);
                Assert.Equal(acceleration.GetValueOrDefault(signed), decoded.LongitudinalAcceleration.SourceCounts);
            }
        }

        [Fact]
        public void CodingLookupRequiresReferenceIdentityAndPreservesDuplicateProfiles()
        {
            AbsCodingRecord unqualified = AbsDiagnosticDecoder.DecodeCoding([0x61, 0x01, 0x07, 0x41]);
            Assert.Equal((ushort)0x4107, unqualified.Word);
            Assert.True(unqualified.Available);
            Assert.Empty(unqualified.MatchingStoredProfiles);
            Assert.Contains(unqualified.Rows, row => row.Field == "Matching stored profiles" && row.Value == "not evaluated");

            AbsCodingRecord manual = AbsDiagnosticDecoder.DecodeCoding([0x61, 0x01, 0x07, 0x41], true);
            Assert.Equal(new[] { 2 }, manual.MatchingStoredProfiles);
            Assert.Contains(manual.Rows, row => row.Field == "Gearbox" && row.Value == "manual with LSD");
            Assert.Contains(manual.Rows, row => row.Field == "Engine" && row.Value == "3.5 litre supercharged 400 hp");
            Assert.Contains(manual.Rows, row => row.Field == "Matching stored profiles" && row.Detail.Contains("active RAM profile"));
            Assert.Contains(manual.Rows, row => row.Field == "Matching stored profiles" && row.Detail.Contains("does not verify the firmware hash"));

            AbsCodingRecord automatic = AbsDiagnosticDecoder.DecodeCoding([0x61, 0x01, 0x03, 0x41], true);
            Assert.Equal(new[] { 5 }, automatic.MatchingStoredProfiles);
            Assert.Contains(automatic.Rows, row => row.Field == "Gearbox" && row.Value == "automatic");
            AbsCodingRecord duplicate = AbsDiagnosticDecoder.DecodeCoding([0x61, 0x01, 0x12, 0xF1], true);
            Assert.Equal(new[] { 8, 9, 10 }, duplicate.MatchingStoredProfiles);
            Assert.False(duplicate.Available);
        }

        [Fact]
        public void CodingUnknownFieldsAndUncodedStateAreNotCoerced()
        {
            AbsCodingRecord unknown = AbsDiagnosticDecoder.DecodeCoding([0x61, 0x01, 0xF9, 0xFE], true);
            Assert.Equal((ushort)0xFEF9, unknown.Word);
            Assert.True(unknown.Available);
            Assert.Empty(unknown.MatchingStoredProfiles);
            foreach (string name in new[] { "Model", "Energy", "Brake system", "Engine" })
                Assert.Contains(unknown.Rows, row => row.Field == name && row.Value == "unknown" && row.Detail.Contains("0x"));
            Assert.Contains(unknown.Rows, row => row.Field == "Uninterpreted coding bit 7" && row.Value == "set");
            Assert.Contains(unknown.Rows, row => row.Field == "Raw 61 01 response" && row.Value == "61 01 F9 FE");

            AbsCodingRecord uncoded = AbsDiagnosticDecoder.DecodeCoding([0x61, 0x01, 0x00, 0x00], true);
            Assert.False(uncoded.Available);
            Assert.Empty(uncoded.MatchingStoredProfiles);
            Assert.Contains(uncoded.Rows, row => row.Value.Contains("stored FF / uncoded"));
        }

        [Theory]
        [InlineData(0x00, "FILLINGINCOMPANDOK", false)]
        [InlineData(0xAA, "FILLINGINNOTCOMP", false)]
        [InlineData(0xEE, "FILLINGINCOMPANDNOTOK", false)]
        [InlineData(0xFF, "BOSCHDELSTATE", false)]
        [InlineData(0x99, "unknown", true)]
        [InlineData(0x42, "unknown", false)]
        public void ProcessLabelsPreserveUnknownAndPossibleReadFailure(int raw, string label, bool possibleFailure)
        {
            AbsProcessRecord decoded = AbsDiagnosticDecoder.DecodeProcess([0x61, 0xBF, (byte)raw]);
            Assert.Equal((byte)raw, decoded.Raw);
            Assert.Equal(label, decoded.OemLabel);
            Assert.Equal(possibleFailure, decoded.PossibleStorageReadFailure);
            Assert.Contains(decoded.Rows, row => row.Field == "Raw 61 BF response" && row.Value == $"61 BF {raw:X2}");
        }

        [Fact]
        public void IdentityRequiresBothCompleteExactRecordsIncludingZeroAndSpacePadding()
        {
            byte[] build = [0x5A, 0x85, .. Encoding.ASCII.GetBytes("6863802010000"), .. new byte[13]];
            byte[] part = [0x5A, 0x87, .. Encoding.ASCII.GetBytes("A132J0314A ")];
            Assert.Equal(28, build.Length);
            Assert.Equal(13, part.Length);
            Assert.True(AbsDiagnosticDecoder.MatchesBb68638Identity(build, part));
            for (int i = 0; i < build.Length; i++)
            {
                byte[] changed = (byte[])build.Clone();
                changed[i] ^= 1;
                Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(changed, part));
            }
            for (int i = 0; i < part.Length; i++)
            {
                byte[] changed = (byte[])part.Clone();
                changed[i] ^= 1;
                Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(build, changed));
            }
            Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(build[..^1], part));
            Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(build, part[..^1]));
            Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity([.. build, 0], part));
            Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(build, [.. part, 0]));
            Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(null!, part));
            Assert.False(AbsDiagnosticDecoder.MatchesBb68638Identity(build, null!));
        }

        [Fact]
        public void DecodersRejectAnyWrongLengthSidOrPid()
        {
            (byte[] Valid, Action<byte[]> Decode)[] decoders =
            [
                (EmptyLiveResponse(), response => AbsDiagnosticDecoder.DecodeLiveRecord(response)),
                ([0x61, 0x01, 0x07, 0x41], response => AbsDiagnosticDecoder.DecodeCoding(response)),
                ([0x61, 0xBF, 0x00], response => AbsDiagnosticDecoder.DecodeProcess(response)),
            ];
            foreach (var decoder in decoders)
            {
                Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!));
                for (int length = 0; length < decoder.Valid.Length; length++)
                    Assert.Throws<ArgumentException>(() => decoder.Decode(decoder.Valid[..length]));
                Assert.Throws<ArgumentException>(() => decoder.Decode([.. decoder.Valid, 0]));
                for (int value = 0; value <= byte.MaxValue; value++)
                {
                    byte[] wrongSid = (byte[])decoder.Valid.Clone();
                    wrongSid[0] = (byte)value;
                    if (wrongSid[0] != decoder.Valid[0])
                        Assert.Throws<ArgumentException>(() => decoder.Decode(wrongSid));
                    byte[] wrongPid = (byte[])decoder.Valid.Clone();
                    wrongPid[1] = (byte)value;
                    if (wrongPid[1] != decoder.Valid[1])
                        Assert.Throws<ArgumentException>(() => decoder.Decode(wrongPid));
                }
            }
        }

        private static byte[] EmptyLiveResponse()
        {
            byte[] response = new byte[22];
            response[0] = 0x61;
            response[1] = 0x04;
            return response;
        }

        private static void WriteDataWord(byte[] response, int offset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(offset + 2, 2), value);

        private static Dictionary<int, AbsCountInterval> SignedPreimages(int multiplier, int divisor)
        {
            var result = new Dictionary<int, AbsCountInterval>();
            for (int source = short.MinValue; source <= short.MaxValue; source++)
                AddPreimage(result, source * multiplier / divisor, source);
            return result;
        }

        private static void AddPreimage(Dictionary<int, AbsCountInterval> ranges, int raw, int source)
        {
            ranges[raw] = ranges.TryGetValue(raw, out AbsCountInterval? previous)
                ? new AbsCountInterval(previous.Minimum, source)
                : new AbsCountInterval(source, source);
        }
    }
}
