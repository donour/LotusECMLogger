namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Characterization tests for the OBD response decoder. These pin the behaviour of the
    /// PIDs that decode <i>correctly</i> today, so a later refactor of the decode switch into a
    /// table can be shown to have changed nothing it was not meant to change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every decoder in the file is covered here.
    /// </para>
    /// <para>
    /// Values are compared to 4 decimal places because several decoders compute in
    /// <see cref="float"/> before widening to the <see cref="double"/> the reading carries.
    /// </para>
    /// </remarks>
    public sealed class LiveDataReadingTests
    {
        private const uint EcmResponseId = 0x7E8;

        /// <summary>Mode 01 response: [hdr][0x41][pid][data]... Exactly sized; the parser walks it.</summary>
        private static byte[] Mode01(params byte[] payload) => [0x00, 0x00, 0x07, 0xE8, 0x41, .. payload];

        /// <summary>Mode 09 response: [hdr][0x49][pid][data]...</summary>
        private static byte[] Mode09(params byte[] payload) => [0x00, 0x00, 0x07, 0xE8, 0x49, .. payload];

        /// <summary>
        /// Mode 22 response: [hdr][0x62][pidHigh][pidLow][payload...], zero-padded out to the
        /// length of a real ISO-TP single-frame reply. Mode 22 decodes one PID and does not walk
        /// a cursor, so the padding is inert.
        /// </summary>
        private static byte[] Mode22(byte pidHigh, byte pidLow, params byte[] payload)
        {
            byte[] frame = new byte[13];
            byte[] head = [0x00, 0x00, 0x07, 0xE8, 0x62, pidHigh, pidLow];
            head.CopyTo(frame, 0);
            payload.CopyTo(frame, 7);
            return frame;
        }

        /// <summary>
        /// Mode 22 response with no frame padding, for exercising the decoders' length guards
        /// against a reply the ECU truncated.
        /// </summary>
        private static byte[] Mode22Exact(byte pidHigh, byte pidLow, params byte[] payload) =>
            [0x00, 0x00, 0x07, 0xE8, 0x62, pidHigh, pidLow, .. payload];

        private static LiveDataReading Single(byte[] response)
        {
            var readings = LiveDataReading.ParseCanResponse(response);
            return Assert.Single(readings);
        }

        private static void AssertReading(LiveDataReading reading, string name, double value)
        {
            Assert.Equal(name, reading.name);
            Assert.Equal(value, reading.value_f, 4);
        }

        // -- Mode 01: single-byte PIDs --------------------------------------------------

        [Theory]
        [InlineData(0x05, 0x7B, "Coolant Temperature", 83)]          // A - 40 degC
        [InlineData(0x06, 0x00, "Short Term Fuel Trim Bank 1", -100)] // A/1.28 - 100 %
        [InlineData(0x07, 0x00, "Long Term Fuel Trim Bank 1", -100)]
        [InlineData(0x08, 0x00, "Short Term Fuel Trim Bank 2", -100)]
        [InlineData(0x09, 0x00, "Long Term Fuel Trim Bank 2", -100)]
        [InlineData(0x0A, 0x64, "FuelPressure(bar)", 3)]              // A * 3 kPa, shown in bar
        [InlineData(0x11, 0x33, "Throttle Position", 20)]             // A * 100/255 %
        [InlineData(0x11, 0xFF, "Throttle Position", 100)]            // full scale must not exceed 100
        [InlineData(0x0B, 0x64, "Intake Manifold Pressure", 100)]     // A kPa
        [InlineData(0x0D, 0x5A, "Vehicle Speed", 90)]                 // A km/h
        [InlineData(0x0E, 0x80, "Timing Advance", 0)]                 // A/2 - 64 deg BTDC
        [InlineData(0x0F, 0x28, "Intake Air Temperature", 0)]         // A - 40 degC
        [InlineData(0x46, 0x32, "Ambient Air Temperature", 10)]       // A - 40 degC
        public void Mode01_SingleBytePid_Decodes(int pid, int a, string name, double expected)
        {
            AssertReading(Single(Mode01((byte)pid, (byte)a)), name, expected);
        }

        // -- Mode 01: two-byte PIDs -----------------------------------------------------

        [Theory]
        [InlineData(0x0C, 0x1A, 0xF8, "Engine Speed", 1726)]                    // (256A+B)/4 rpm
        [InlineData(0x10, 0x13, 0x88, "maf (g/s)", 50)]                         // (256A+B)/100 g/s
        [InlineData(0x43, 0x00, 0xFF, "Absolute Load", 100)]                    // (256A+B)*100/255 %
        [InlineData(0x44, 0x80, 0x00, "Commanded Equivalence Ratio", 1.0)]      // (256A+B)/32768
        public void Mode01_TwoBytePid_Decodes(int pid, int a, int b, string name, double expected)
        {
            AssertReading(Single(Mode01((byte)pid, (byte)a, (byte)b)), name, expected);
        }

        // -- Mode 01: oxygen sensors ----------------------------------------------------

        // PIDs 0x24-0x29 map to sensors 1-6.
        [Theory]
        [InlineData(0x24, 1)]
        [InlineData(0x25, 2)]
        [InlineData(0x26, 3)]
        [InlineData(0x27, 4)]
        [InlineData(0x28, 5)]
        [InlineData(0x29, 6)]
        public void Mode01_OxygenSensor_DecodesLambdaAndVoltage(int pid, int sensor)
        {
            // AB = 0x8000 -> lambda 1.0; CD = 0x2000 -> 1.0 V
            var readings = LiveDataReading.ParseCanResponse(
                Mode01((byte)pid, 0x80, 0x00, 0x20, 0x00));

            Assert.Equal(2, readings.Count);
            AssertReading(readings[0], $"O2SensorLambda{sensor}", 1.0);
            AssertReading(readings[1], $"O2SensorVoltage{sensor}", 1.0);
        }

        // -- Mode 01: the multi-PID cursor ----------------------------------------------

        /// <summary>
        /// The property the decode loop exists to provide: several PIDs of differing widths
        /// packed into one response are each decoded, in order, with the cursor landing on every
        /// PID boundary.
        /// </summary>
        /// <remarks>
        /// Every payload byte here is itself a valid PID number. That matters: the decoder's
        /// unknown-PID branch advances a single byte, which lets a cursor that moved by the wrong
        /// width stumble back onto a real boundary and produce the correct readings anyway. With
        /// PID-valued payloads a wrong advance decodes something, so the sequence visibly changes.
        /// </remarks>
        [Fact]
        public void Mode01_SeveralPidsInOneFrame_DecodesAllInOrder()
        {
            var readings = LiveDataReading.ParseCanResponse(Mode01(
                0x0C, 0x05, 0x0C,       // 2-byte: 0x050C/4 = 323 rpm
                0x0D, 0x5A,             // 1-byte: 90 km/h
                0x05, 0x7B));           // 1-byte: coolant 83 degC

            Assert.Equal(
                new[] { "Engine Speed", "Vehicle Speed", "Coolant Temperature" },
                readings.Select(r => r.name));
            Assert.Equal(323, readings[0].value_f, 4);
            Assert.Equal(90, readings[1].value_f, 4);
            Assert.Equal(83, readings[2].value_f, 4);
        }

        /// <summary>
        /// A five-byte PID must move the cursor five bytes. Payload bytes are PID-valued for the
        /// reason given above.
        /// </summary>
        [Fact]
        public void Mode01_WidePidFollowedByAnother_AdvancesItsFullWidth()
        {
            var readings = LiveDataReading.ParseCanResponse(Mode01(
                0x24, 0x80, 0x0C, 0x20, 0x0D,   // 4-byte payload: O2 sensor 1
                0x05, 0x7B));                    // reached only on a correct 5-byte advance

            Assert.Equal(
                new[] { "O2SensorLambda1", "O2SensorVoltage1", "Coolant Temperature" },
                readings.Select(r => r.name));
            Assert.Equal(1.0004, readings[0].value_f, 4);
            Assert.Equal(1.0016, readings[1].value_f, 4);
            Assert.Equal(83, readings[2].value_f, 4);
        }

        /// <summary>
        /// Real ISO-TP replies are zero-padded to a full 8-byte frame. The trailing zeros must
        /// not produce readings of their own.
        /// </summary>
        [Fact]
        public void Mode01_TrailingFramePadding_ProducesNoExtraReadings()
        {
            var readings = LiveDataReading.ParseCanResponse(
                Mode01(0x05, 0x7B, 0x00, 0x00, 0x00, 0x00));

            AssertReading(Assert.Single(readings), "Coolant Temperature", 83);
        }

        [Fact]
        public void Mode01_PidWithTruncatedPayload_IsSkipped()
        {
            Assert.Empty(LiveDataReading.ParseCanResponse(Mode01(0x0C, 0x1A)));
        }

        [Fact]
        public void Response_ShorterThanHeader_ReturnsNothing()
        {
            Assert.Empty(LiveDataReading.ParseCanResponse([0x00, 0x00, 0x07, 0xE8]));
        }

        // -- Mode 09 ---------------------------------------------------------------------

        [Fact]
        public void Mode09_SupportedPidBitmask_DecodesAsLongValue()
        {
            var reading = Single(Mode09(0x00, 0x12, 0x34, 0x56, 0x78));

            Assert.Equal("SupportedPIDs_01_20", reading.name);
            Assert.Equal(0x12345678L, reading.value_l);
        }

        // -- Mode 22: single-value PIDs --------------------------------------------------

        [Theory]
        [InlineData(0x02, 0xFF, "PurgeDutyCycle", 100)]             // raw*100/255
        [InlineData(0x2A, 0x50, "load_pct", 80)]                    // raw %
        [InlineData(0x13, 0x64, "AFR Target", 1.0)]                 // raw * 0.01
        [InlineData(0x72, 0x80, "ManifoldTempC", 40)]               // raw*5/8 - 40
        [InlineData(0xC7, 0x80, "TransFluidTempC", 40)]             // raw*5/8 - 40
        [InlineData(0xC9, 0xFF, "ChargecoolerDutycycle", 100)]      // raw*100/255
        public void Mode22_OneBytePid_Decodes(int pid, int d0, string name, double expected)
        {
            AssertReading(Single(Mode22(0x02, (byte)pid, (byte)d0)), name, expected);
        }

        [Theory]
        [InlineData(0x3B, 0x04, 0x00, "TPSTarget", 100)]                    // raw*100/1024 %
        [InlineData(0x45, 0x04, 0x00, "TPSActual", 100)]
        [InlineData(0x46, 0x04, 0x00, "AcceleratorPedalPosition", 100)]
        [InlineData(0x3A, 0x12, 0x34, "FuelLearnTimer", 4660)]              // u16
        [InlineData(0x2E, 0xFF, 0x9C, "FuelLearnLeanTimeBank1(us)", -100)]  // i16
        [InlineData(0x55, 0xFF, 0x9C, "FuelLearnLeanTimeBank2(us)", -100)]
        [InlineData(0x6A, 0xFF, 0x9C, "TorqueNM", -100)]                    // i16 big-endian
        [InlineData(0x08, 0x01, 0x00, "VVTI B1 intake (deg)", 64)]          // i16/4
        [InlineData(0x4B, 0x01, 0x00, "VVTI B2 intake (deg)", 64)]
        [InlineData(0x50, 0x01, 0x00, "VVTI B1 exhaust (deg)", 64)]
        [InlineData(0x51, 0x01, 0x00, "VVTI B2 exhaust (deg)", 64)]
        public void Mode22_TwoBytePid_Decodes(int pid, int d0, int d1, string name, double expected)
        {
            AssertReading(Single(Mode22(0x02, (byte)pid, (byte)d0, (byte)d1)), name, expected);
        }

        [Theory]
        [InlineData(0x05, "InjectorPulseTimeBank1(us)")]
        [InlineData(0x17, "InjectorPulseTimeBank2(us)")]
        public void Mode22_InjectorPulseWidth_DecodesAsThreeByteValue(int pid, string name)
        {
            AssertReading(Single(Mode22(0x02, (byte)pid, 0x01, 0x02, 0x03)), name, 0x010203);
        }

        // Offset-128 byte: 0x80 is neutral, each count is ~0.391 %.
        [Theory]
        [InlineData(0x48, 0x80, "FuelLearnZone2Bank1", 0)]
        [InlineData(0x49, 0xA0, "FuelLearnZone3Bank1", 12.5)]
        [InlineData(0x5A, 0x40, "FuelLearnZone2Bank2", -25)]
        [InlineData(0x5B, 0x80, "FuelLearnZone3Bank2", 0)]
        public void Mode22_FuelLearnZoneTrim_Decodes(int pid, int d0, string name, double expected)
        {
            AssertReading(Single(Mode22(0x02, (byte)pid, (byte)d0)), name, expected);
        }

        // -- Mode 22: multi-value PIDs ---------------------------------------------------

        [Fact]
        public void Mode22_KnockRetard_DecodesCylindersOneToFour()
        {
            var readings = LiveDataReading.ParseCanResponse(
                Mode22(0x02, 0x31, 0x04, 0x08, 0x0C, 0x10)); // raw/4 degrees

            Assert.Equal(4, readings.Count);
            for (int i = 0; i < 4; i++)
                AssertReading(readings[i], $"KnockSparkRetardCylinder {i + 1}", i + 1);
        }

        [Fact]
        public void Mode22_KnockRetard_DecodesCylindersFiveAndSix()
        {
            var readings = LiveDataReading.ParseCanResponse(Mode22(0x02, 0x56, 0x14, 0x18));

            Assert.Equal(2, readings.Count);
            AssertReading(readings[0], "KnockSparkRetardCylinder 5", 5);
            AssertReading(readings[1], "KnockSparkRetardCylinder 6", 6);
        }

        // Firmware-verified: obd_ii_mode22_processing packs misfires_per_cyl[0], [2], [3], [1] for
        // PIDs 0x0234-0x0237 and [4], [5] for 0x0257/0x0258. The array is cylinder-indexed, so the
        // PIDs run in four-cylinder firing order - 1, 3, 4, 2 - which is genuine, not a decode bug.
        [Theory]
        [InlineData(0x34, 1)]
        [InlineData(0x37, 2)]
        [InlineData(0x35, 3)]
        [InlineData(0x36, 4)]
        [InlineData(0x57, 5)]
        [InlineData(0x58, 6)]
        public void Mode22_Misfire_DecodesPerCylinderCount(int pid, int cylinder)
        {
            AssertReading(Single(Mode22(0x02, (byte)pid, 0x00, 0x2A)), $"MisfireCylinder{cylinder}", 42);
        }

        // Firmware-verified, and NOT permuted the way the misfire PIDs above are: the firmware packs
        // LEA_octane_scaler[0..5] in PID order into a cylinder-indexed array. Reading the misfire
        // permutation across to these PIDs is the bug this pins shut.
        [Theory]
        [InlineData(0x18, 1)]
        [InlineData(0x19, 2)]
        [InlineData(0x1A, 3)]
        [InlineData(0x1B, 4)]
        [InlineData(0x4D, 5)]
        [InlineData(0x4E, 6)]
        public void Mode22_OctaneRating_DecodesPerCylinderPercentage(int pid, int cylinder)
        {
            // 0x8000 of a Q16 full scale -> exactly 50 %
            AssertReading(Single(Mode22(0x02, (byte)pid, 0x80, 0x00)),
                $"OctaneRatingCylinder{cylinder}", 50);
        }

        /// <summary>
        /// The scaler is a Q16 fraction - the firmware applies it as (x * (s >> 8)) >> 8, i.e.
        /// x * s / 65536 - so full scale is 65536. Dividing by 65535 instead, as the vehicle
        /// information reader used to, is wrong by one part in 65536.
        /// </summary>
        [Fact]
        public void OctaneScaler_UsesQ16FullScale()
        {
            Assert.Equal(0, OctaneScaler.ToPercent(0), 6);
            Assert.Equal(50, OctaneScaler.ToPercent(0x8000), 6);
            Assert.Equal(100, OctaneScaler.ToPercent(0x10000), 6);
        }

        /// <summary>
        /// The vehicle information reader does its own Mode 22 request/response handling and decodes
        /// the payload through <see cref="ObdPidDecoders.DecodeMode22Payload"/>, while the logger
        /// parses whole response buffers. Both must land on the same table entry for every PID, or
        /// the two views of the car drift apart the way they did over the octane cylinder order.
        /// </summary>
        [Fact]
        public void Mode22_PayloadDecode_MatchesFullResponseDecodeForEveryPid()
        {
            byte[] sample = [0xA0, 0x3C, 0x12, 0x34];
            Assert.NotEmpty(ObdPidDecoders.Mode22);

            foreach (var (pid, decoder) in ObdPidDecoders.Mode22)
            {
                byte[] payload = sample[..decoder.Width];

                var viaPayload = ObdPidDecoders.DecodeMode22Payload(pid, payload);
                var viaResponse = LiveDataReading.ParseCanResponse(
                    Mode22((byte)(pid >> 8), (byte)pid, payload));

                Assert.Equal(
                    viaResponse.Select(r => (r.name, r.value_f)),
                    viaPayload.Select(r => (r.name, r.value_f)));
                Assert.NotEmpty(viaPayload);
            }
        }

        /// <summary>
        /// Pins the mapping both readers now share, so the two cannot drift apart again.
        /// </summary>
        [Fact]
        public void OctaneScaler_MapsEveryCylinderExactlyOnce()
        {
            Assert.Equal(
                new[] { 1, 2, 3, 4, 5, 6 },
                OctaneScaler.CylinderByPid.Values.OrderBy(c => c));
        }

        [Theory]
        [InlineData(0x03, 1)]
        [InlineData(0x04, 2)]
        public void Mode22Page04_WidebandCalibration_DecodesSlopeAndOffset(int pid, int bank)
        {
            // slope u16 (4096 = 1.0x), offset i16 signed ADC counts
            var readings = LiveDataReading.ParseCanResponse(
                Mode22(0x04, (byte)pid, 0x10, 0x00, 0xFF, 0x9C));

            Assert.Equal(2, readings.Count);
            AssertReading(readings[0], $"WBCalSlope{bank}", 4096);
            AssertReading(readings[1], $"WBCalOffset{bank}", -100);
        }

        // -- ECU routing and naming ------------------------------------------------------

        [Fact]
        public void LegacyMode_IgnoresResponsesFromOtherEcus()
        {
            // A TCM/UEGO reply (0x7E9) must not be decoded as an ECM reply.
            byte[] fromTcm = [0x00, 0x00, 0x07, 0xE9, 0x41, 0x05, 0x7B];

            Assert.Empty(LiveDataReading.ParseCanResponse(fromTcm));
        }

        [Fact]
        public void MultiEcuMode_IgnoresResponsesThatDoNotMatchTheGivenEcu()
        {
            Assert.Empty(LiveDataReading.ParseCanResponse(Mode01(0x05, 0x7B), ECUDefinition.TCM));
        }

        [Fact]
        public void MultiEcuMode_TagsReadingsWithTheirSourceEcu()
        {
            var ecu = ECUDefinition.ECM;
            Assert.Equal(EcmResponseId, ecu.ResponseId);

            var reading = Assert.Single(
                LiveDataReading.ParseCanResponse(Mode01(0x05, 0x7B), ecu, prefixWithEcuName: false));

            Assert.Equal("Coolant Temperature", reading.name);
            Assert.Equal("ECM", reading.ecuSource);
        }

        [Fact]
        public void MultiEcuMode_PrefixesReadingNamesWhenRequested()
        {
            var reading = Assert.Single(
                LiveDataReading.ParseCanResponse(Mode01(0x05, 0x7B), ECUDefinition.ECM, prefixWithEcuName: true));

            Assert.Equal("ECM:Coolant Temperature", reading.name);
            Assert.Equal("ECM", reading.ecuSource);
        }

        // -- AEM X-Series wideband --------------------------------------------------------

        [Fact]
        public void AemUego_Mode01Pid24_DecodesLambdaAfrAndVoltage()
        {
            // The UEGO answers on the TCM address; AB = 0x8000 -> lambda 1.0, CD = 0x2000 -> 1.0 V
            byte[] response = [0x00, 0x00, 0x07, 0xE9, 0x41, 0x24, 0x80, 0x00, 0x20, 0x00];

            var readings = LiveDataReading.ParseCanResponse(
                response, ECUDefinition.AEM_UEGO, prefixWithEcuName: false);

            Assert.Equal(3, readings.Count);
            AssertReading(readings[0], "Lambda", 1.0);
            AssertReading(readings[1], "AFR", 14.7);   // lambda * gasoline stoich
            AssertReading(readings[2], "O2 Voltage", 1.0);
            Assert.All(readings, r => Assert.Equal("AEM UEGO", r.ecuSource));
        }

        [Fact]
        public void AemUego_PrefixesReadingNamesWhenRequested()
        {
            byte[] response = [0x00, 0x00, 0x07, 0xE9, 0x41, 0x24, 0x80, 0x00, 0x20, 0x00];

            var readings = LiveDataReading.ParseCanResponse(
                response, ECUDefinition.AEM_UEGO, prefixWithEcuName: true);

            Assert.Equal("AEM UEGO:Lambda", readings[0].name);
            Assert.Equal("AEM UEGO:AFR", readings[1].name);
            Assert.Equal("AEM UEGO:O2 Voltage", readings[2].name);
        }

        // -- Resolution, cursor safety and truncated replies ------------------------------

        /// <summary>
        /// Mode 01 PID 0x11 and Mode 22 PID 0x0245 are the same TPS-A ADC channel at different
        /// widths - 8-bit via get_tps() (raw >> 6) and 10-bit via TPSActual (raw >> 4). Decoding
        /// them independently is only correct if they agree, so this pins that they do.
        /// </summary>
        /// <remarks>
        /// They agree to within about half a percentage point rather than exactly, and the residue
        /// is not sloppiness: J1979 scales a byte by 100/255 while the Mode 22 decoder scales its
        /// 10-bit word by 100/1024. Those denominators differ by one part in 256, worth 0.12 pp at
        /// the value used here and 0.39 pp at full scale. Sampling a raw value that is not a
        /// multiple of four would add up to 0.29 pp more from the 8-bit channel's coarser steps.
        /// </remarks>
        [Fact]
        public void ThrottlePosition_AgreesBetweenMode01AndMode22()
        {
            const int raw10Bit = 0x140;   // 320 of 1024; a multiple of 4, so no quantisation gap
            byte raw8Bit = (byte)(raw10Bit >> 2);

            var mode01 = Single(Mode01(0x11, raw8Bit));
            var mode22 = Single(Mode22(0x02, 0x45, (byte)(raw10Bit >> 8), (byte)(raw10Bit & 0xFF)));

            Assert.Equal("Throttle Position", mode01.name);
            Assert.Equal("TPSActual", mode22.name);
            Assert.Equal(mode22.value_f, mode01.value_f, tolerance: 0.5);
        }

        /// <summary>
        /// J1979 gives engine speed a quarter-rpm resolution. Dividing the raw count by an integer
        /// 4 discarded it, so every reading landed on a whole rpm.
        /// </summary>
        [Fact]
        public void Mode01_EngineSpeed_KeepsQuarterRpmResolution()
        {
            AssertReading(Single(Mode01(0x0C, 0x1A, 0xF9)), "Engine Speed", 1726.25);
        }

        /// <summary>
        /// A standard PID this decoder does not interpret still has a known payload width, so it is
        /// stepped over and the PIDs behind it survive. lotus-diagnostic.json requests two such PIDs
        /// (0x21 and 0x31), and before the width table they cost every reading that followed.
        /// </summary>
        [Fact]
        public void Mode01_UndecodedButStandardPid_IsSteppedOverByItsKnownWidth()
        {
            // 0x21 (distance with MIL on) is two bytes and is not decoded. Its payload is chosen to
            // look like a PID, so a cursor stepping the wrong distance would decode something.
            var readings = LiveDataReading.ParseCanResponse(Mode01(
                0x05, 0x7B,          // coolant 83 degC
                0x21, 0x0C, 0x05,    // undecoded, but two bytes wide
                0x0D, 0x5A));        // reached only if 0x21 was stepped over correctly

            Assert.Equal(
                new[] { "Coolant Temperature", "Vehicle Speed" },
                readings.Select(r => r.name));
            Assert.Equal(83, readings[0].value_f, 4);
            Assert.Equal(90, readings[1].value_f, 4);
        }

        /// <summary>
        /// A PID in neither table has no knowable width, so the only safe move is to stop. Advancing
        /// a guessed single byte lands the cursor inside that PID's payload, where a data byte that
        /// happens to equal a real PID number decodes into a reading that was never transmitted.
        /// </summary>
        [Fact]
        public void Mode01_UnrecognisedPid_StopsDecodingInsteadOfGuessingItsWidth()
        {
            // 0xFE is neither decoded nor a standard J1979 PID. Its payload would read as an Engine
            // Speed PID followed by a plausible rpm value if the cursor advanced into it.
            var readings = LiveDataReading.ParseCanResponse(Mode01(
                0x05, 0x7B,          // coolant 83 degC, decoded before the unknown PID
                0xFE, 0x0C, 0x05,    // unrecognised PID whose payload looks like a PID
                0x0D, 0x5A));        // unreachable once decoding stops

            AssertReading(Assert.Single(readings), "Coolant Temperature", 83);
        }

        [Fact]
        public void Mode01_UnknownFirstPid_ProducesNoReadings()
        {
            // The unknown PID's payload looks like PID 0x0C followed by a plausible RPM.
            // With no earlier PID, the fail-closed behavior must still return an empty batch.
            var readings = LiveDataReading.ParseCanResponse(Mode01(0x21, 0x0C, 0x1A, 0xF8));

            Assert.Empty(readings);
        }

        /// <summary>
        /// These decoders read a second payload byte, so a reply carrying only one must be skipped.
        /// Their guards used to admit it and then index past the end of the buffer, and the
        /// resulting exception unwinds through the logging thread and ends the session.
        /// </summary>
        [Theory]
        [InlineData(0x6A)]  // engine torque
        [InlineData(0x08)]  // VVTi B1 intake
        [InlineData(0x4B)]  // VVTi B2 intake
        [InlineData(0x50)]  // VVTi B1 exhaust
        [InlineData(0x51)]  // VVTi B2 exhaust
        public void Mode22_TwoBytePid_TruncatedReply_IsSkippedNotThrown(int pid)
        {
            Assert.Empty(LiveDataReading.ParseCanResponse(Mode22Exact(0x02, (byte)pid, 0x01)));
        }

        /// <summary>As above, for the decoders that need a single payload byte.</summary>
        [Theory]
        [InlineData(0x02)]  // purge duty cycle
        [InlineData(0xC7)]  // transmission fluid temperature
        [InlineData(0xC9)]  // chargecooler duty cycle
        public void Mode22_OneBytePid_TruncatedReply_IsSkippedNotThrown(int pid)
        {
            Assert.Empty(LiveDataReading.ParseCanResponse(Mode22Exact(0x02, (byte)pid)));
        }
    }
}
