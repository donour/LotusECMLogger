using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the pure emissions-check decoders against canned Mode 01 buffers: the PID 0x01 /
    /// 0x41 monitor bit layout for both ignition types, the supported-PID bitmask pages, the
    /// since-cleared counters, and the VIN model-year decode.
    /// </summary>
    public sealed class EmissionsDecoderTests
    {
        /// <summary>Prepends the ECM response header the J2534 channel delivers.</summary>
        private static byte[] Response(params byte[] tail) => [0x00, 0x00, 0x07, 0xE8, .. tail];

        private static ReadinessMonitor Monitor(MonitorStatus status, ReadinessMonitorId id) =>
            status.Monitors.Single(m => m.Id == id);

        // ── PID 0x01 ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void SinceClear_DecodesMilAndDtcCount()
        {
            var status = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x83, 0x07, 0x00, 0x00]);

            Assert.True(status.MilOn);
            Assert.Equal(3, status.DtcCount);
        }

        [Fact]
        public void SinceClear_MilOffWithZeroCodes()
        {
            var status = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x07, 0x00, 0x00]);

            Assert.False(status.MilOn);
            Assert.Equal(0, status.DtcCount);
        }

        [Fact]
        public void SinceClear_SparkMonitors_SupportAndCompletion()
        {
            // C = 0x65: catalyst, EVAP, O2 sensor, O2 heater supported. D = 0x04: EVAP incomplete.
            var status = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x07, 0x65, 0x04]);

            Assert.Equal(IgnitionType.Spark, status.Ignition);
            Assert.Equal(MonitorReadiness.Complete, Monitor(status, ReadinessMonitorId.Catalyst).Readiness);
            Assert.Equal(MonitorReadiness.Incomplete, Monitor(status, ReadinessMonitorId.EvaporativeSystem).Readiness);
            Assert.Equal(MonitorReadiness.Complete, Monitor(status, ReadinessMonitorId.OxygenSensor).Readiness);
            Assert.Equal(MonitorReadiness.Complete, Monitor(status, ReadinessMonitorId.OxygenSensorHeater).Readiness);
            Assert.Equal(MonitorReadiness.NotSupported, Monitor(status, ReadinessMonitorId.SecondaryAirSystem).Readiness);
            Assert.Equal(MonitorReadiness.NotSupported, Monitor(status, ReadinessMonitorId.EgrVvt).Readiness);
        }

        [Fact]
        public void SinceClear_SparkListsThreeContinuousAndEightNonContinuous()
        {
            var status = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x00, 0x00, 0x00]);

            Assert.Equal(3, status.Monitors.Count(m => m.Kind == MonitorKind.Continuous));
            Assert.Equal(8, status.Monitors.Count(m => m.Kind == MonitorKind.NonContinuous));
            Assert.Equal(ReadinessMonitorId.Misfire, status.Monitors[0].Id);
        }

        [Fact]
        public void SinceClear_ContinuousIncompleteBits()
        {
            // B = 0x17: all three supported, misfire (bit 4) incomplete.
            var status = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x17, 0x00, 0x00]);

            Assert.Equal(MonitorReadiness.Incomplete, Monitor(status, ReadinessMonitorId.Misfire).Readiness);
            Assert.Equal(MonitorReadiness.Complete, Monitor(status, ReadinessMonitorId.FuelSystem).Readiness);
            Assert.Equal(MonitorReadiness.Complete, Monitor(status, ReadinessMonitorId.ComprehensiveComponents).Readiness);
        }

        [Fact]
        public void SinceClear_CompressionIgnition_UsesDieselMonitorsAndSkipsReservedBits()
        {
            // B bit 3 set; C = 0xFF supports every bit, D = 0x40 marks the PM filter incomplete.
            var status = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x0F, 0xFF, 0x40]);

            Assert.Equal(IgnitionType.Compression, status.Ignition);
            Assert.Equal(6, status.Monitors.Count(m => m.Kind == MonitorKind.NonContinuous));
            Assert.DoesNotContain(status.Monitors, m => m.Id == ReadinessMonitorId.EvaporativeSystem);
            Assert.Equal(MonitorReadiness.Incomplete, Monitor(status, ReadinessMonitorId.PmFilter).Readiness);
            Assert.Equal(MonitorReadiness.Complete, Monitor(status, ReadinessMonitorId.NmhcCatalyst).Readiness);
        }

        [Fact]
        public void SinceClear_ThrowsOnShortPayload()
        {
            Assert.Throws<ArgumentException>(() => EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x07, 0x65]));
        }

        // ── PID 0x41 ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void ThisCycle_IgnoresReservedByteA_AndUsesGivenIgnition()
        {
            var status = EmissionsDecoder.DecodeMonitorStatusThisCycle([0xFF, 0x00, 0x01, 0x01], IgnitionType.Spark);

            Assert.False(status.MilOn);
            Assert.Equal(0, status.DtcCount);
            Assert.Equal(MonitorReadiness.Incomplete, Monitor(status, ReadinessMonitorId.Catalyst).Readiness);
        }

        // ── Supported-PID pages ───────────────────────────────────────────────────────────

        [Fact]
        public void SupportedPids_FirstPage()
        {
            var pids = EmissionsDecoder.ParseSupportedPids(Response(0x41, 0x00, 0xBE, 0x1F, 0xA8, 0x13), 0x00);

            Assert.Equal([0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x13, 0x15, 0x1C, 0x1F, 0x20], pids);
        }

        [Fact]
        public void SupportedPids_LaterPageIsOffsetByBase()
        {
            var pids = EmissionsDecoder.ParseSupportedPids(Response(0x41, 0x40, 0x80, 0x00, 0x00, 0x00), 0x40);

            Assert.Equal([0x41], pids);
        }

        [Fact]
        public void SupportedPids_MismatchedBaseIsEmpty()
        {
            Assert.Empty(EmissionsDecoder.ParseSupportedPids(Response(0x41, 0x00, 0xFF, 0xFF, 0xFF, 0xFF), 0x20));
        }

        // ── Counters and OBD standard ─────────────────────────────────────────────────────

        [Fact]
        public void Counter_DistanceSinceClear_IsBigEndianKm()
        {
            var counter = EmissionsDecoder.DecodeCounter(0x31, [0x01, 0x2C]);

            Assert.NotNull(counter);
            Assert.Equal(300, counter.Value);
            Assert.Equal("km", counter.Unit);
        }

        [Fact]
        public void Counter_WarmUps_IsOneByte()
        {
            Assert.Equal(5, EmissionsDecoder.DecodeCounter(0x30, [0x05])?.Value);
        }

        [Fact]
        public void Counter_ShortPayloadOrUnknownPid_IsNull()
        {
            Assert.Null(EmissionsDecoder.DecodeCounter(0x4E, [0x01]));
            Assert.Null(EmissionsDecoder.DecodeCounter(0x05, [0x7B]));
        }

        [Fact]
        public void ObdStandard_NamesCarbAndReserved()
        {
            Assert.Contains("CARB", EmissionsDecoder.ObdStandardName(1));
            Assert.Equal("EOBD (Europe)", EmissionsDecoder.ObdStandardName(6));
            Assert.Equal("OBD, EOBD and KOBD", EmissionsDecoder.ObdStandardName(14));
            Assert.Equal("Reserved (16)", EmissionsDecoder.ObdStandardName(16));
        }

        // ── Calibration ID / CVN ──────────────────────────────────────────────────────────

        [Fact]
        public void CalibrationIds_SplitAndTrimPadding()
        {
            byte[] data = [.. "C132E0278\0\0\0\0\0\0\0"u8, .. "SECOND ID       "u8];

            Assert.Equal(["C132E0278", "SECOND ID"], EmissionsDecoder.ParseCalibrationIds(data));
        }

        [Fact]
        public void Cvns_AreBigEndianFourByteWords()
        {
            // The T6 reports its CRC16 as 00 00 hi lo.
            Assert.Equal([0x0000BEEFu], EmissionsDecoder.ParseCvns([0x00, 0x00, 0xBE, 0xEF]));
            Assert.Empty(EmissionsDecoder.ParseCvns([0x00, 0x00, 0xBE]));
        }

        [Fact]
        public void StockCatalog_ParsesHexWithOrWithoutPrefix_CaseInsensitiveIds()
        {
            var catalog = StockSoftwareCatalog.Parse("""{ "c132e0278": ["0x0000BEEF", "1234", "nothex"] }""");

            var cvns = catalog["C132E0278"];
            Assert.Equal(2, cvns.Count);
            Assert.Contains(0x0000BEEFu, cvns);
            Assert.Contains(0x00001234u, cvns);
        }

        // ── Model year ────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("SCCLMDTU9AHA10234", 2010)] // letter in position 7: 2010+ cycle
        [InlineData("SCCPC11165HA30123", 2005)] // digit in position 7, digit year code
        [InlineData("SCCPC1116THA30123", 1996)] // digit in position 7: 1980-2009 cycle
        [InlineData("SCCLMDTU9THA10234", 2026)] // same code, letter in position 7
        [InlineData("SCCPCAAA65HA30123", 2005)] // non-NA VIN: 2035 is in the future, so 2005
        [InlineData("SCCPC1116BHA30123", 2011)] // pre-OBD-II 1981 moves to the later cycle
        public void ModelYear_FromVin(string vin, int expected)
        {
            Assert.Equal(expected, EmissionsDecoder.ModelYearFromVin(vin, currentYear: 2026));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("SHORTVIN")]
        [InlineData("SCCPC1116IHA30123")] // 'I' is never a year code
        public void ModelYear_MalformedVinIsNull(string? vin)
        {
            Assert.Null(EmissionsDecoder.ModelYearFromVin(vin, currentYear: 2026));
        }
    }
}
