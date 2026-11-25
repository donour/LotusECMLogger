using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Tests.Fakes;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Regression tests for the one-shot diagnostic services, run against scripted ECUs through
    /// <see cref="ScriptedLinkProvider"/> instead of a J2534 adapter (J2534-session-broker-plan.md,
    /// Phase 0). They pin what each service sends, which replies it accepts, and how it reports
    /// rejections and silence, so moving the services onto the session broker cannot change them
    /// unnoticed. Every test also checks that the service closed the links it opened.
    /// </summary>
    public sealed class DiagnosticServiceTests
    {
        private const string Vin = "SCCLMDTU9BHA10001";

        private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        // ── DTC read and clear ─────────────────────────────────────────────────────────────

        [Fact]
        public void Dtc_ReadCodes_ReadsStoredPendingAndPermanent()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x03], 0x43, 0x01, 0x03, 0x01)
                .Reply([0x07], 0x47, 0x01, 0x04, 0x20)
                .Reply([0x0A], 0x4A, 0x00);

            var (success, error, result) = new J2534DtcService(links).ReadCodes();

            Assert.True(success, error);
            Assert.Equal(["P0301"], result.Stored.Select(c => c.Code));
            Assert.Equal(["P0420"], result.Pending.Select(c => c.Code));
            Assert.Empty(result.Permanent);
            Assert.Null(result.PendingError);
            Assert.Null(result.PermanentError);
            // The engine's three services, then one TCU probe that times out (no TCU scripted).
            Assert.Equal(["03", "07", "0A", "03"], links.ExchangedHex);
            Assert.Equal(ECUDefinition.TCM.ResponseId, links.Exchanges[^1].Ecu.ResponseId);
            Assert.Equal(2, links.LinksOpened);
            Assert.Equal(0, links.LinksOpen);
            Assert.NotNull(result.Tcu);
            Assert.Contains("0x7E1", result.Tcu!.StoredError);
        }

        [Fact]
        public void Dtc_ReadCodes_PendingAndPermanentFailuresBecomeNotes()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x03], 0x43, 0x00)
                .Reject([0x07], 0x11);

            var (success, _, result) = new J2534DtcService(links).ReadCodes();

            Assert.True(success);
            Assert.Empty(result.Stored);
            Assert.Contains("NRC 0x11", result.PendingError);
            Assert.Contains("No response", result.PermanentError);
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Dtc_ReadCodes_SilentEcuFails()
        {
            var links = new ScriptedLinkProvider();

            var (success, error, _) = new J2534DtcService(links).ReadCodes();

            Assert.False(success);
            Assert.Contains("No response", error);
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Dtc_ReadCodes_AdapterErrorIsReported()
        {
            var links = new ScriptedLinkProvider { OpenError = new InvalidOperationException("no adapter") };

            var (success, error, _) = new J2534DtcService(links).ReadCodes();

            Assert.False(success);
            Assert.Equal("no adapter", error);
        }

        [Fact]
        public void Dtc_ClearCodes_AcceptsPositiveReply()
        {
            var links = new ScriptedLinkProvider().Reply([0x04], 0x44);

            Assert.Equal((true, ""), new J2534DtcService(links).ClearCodes());
            Assert.Equal(["04"], links.ExchangedHex);
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Dtc_ClearCodes_ReportsRejectionAndSilence()
        {
            var rejecting = new ScriptedLinkProvider().Reject([0x04], 0x22);
            var (rejected, rejectError) = new J2534DtcService(rejecting).ClearCodes();
            Assert.False(rejected);
            Assert.Contains("NRC 0x22", rejectError);

            var (answered, silenceError) = new J2534DtcService(new ScriptedLinkProvider()).ClearCodes();
            Assert.False(answered);
            Assert.Contains("No response", silenceError);
        }

        [Fact]
        public void Dtc_ReadCodes_ReadsTheTcuSeparatelyFromTheEngine()
        {
            // The payloads differ on purpose: if routing were lost, the engine code would show up
            // under the TCU or the other way round.
            var links = new ScriptedLinkProvider()
                .Reply([0x03], 0x43, 0x01, 0x03, 0x01)
                .Reply([0x07], 0x47, 0x00)
                .Reply([0x0A], 0x4A, 0x00)
                .Reply(ECUDefinition.TCM, [0x03], 0x43, 0x02, 0x07, 0x00, 0x07, 0x21)
                .Reply(ECUDefinition.TCM, [0x07], 0x47, 0x00)
                .Reply(ECUDefinition.TCM, [0x0A], 0x4A, 0x00);

            var (success, error, result) = new J2534DtcService(links).ReadCodes();

            Assert.True(success, error);
            Assert.Equal(["P0301"], result.Stored.Select(c => c.Code));
            Assert.Equal(["P0700", "P0721"], result.Tcu!.Stored.Select(c => c.Code));
            Assert.Null(result.Tcu.StoredError);
            Assert.Equal(
                [.. Enumerable.Repeat(ECUDefinition.ECM.ResponseId, 3), .. Enumerable.Repeat(ECUDefinition.TCM.ResponseId, 3)],
                links.Exchanges.Select(e => e.Ecu.ResponseId));
            // The engine link is closed before the TCU's opens, so only one is ever open.
            Assert.Equal(2, links.LinksOpened);
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Dtc_ReadCodes_TcuRejectionIsANoteAndPermanentIsStillTried()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x03], 0x43, 0x00)
                .Reply([0x07], 0x47, 0x00)
                .Reply([0x0A], 0x4A, 0x00)
                .Reply(ECUDefinition.TCM, [0x03], 0x43, 0x00)
                .Reply(ECUDefinition.TCM, [0x07], 0x7F, 0x07, 0x11);

            var (success, _, result) = new J2534DtcService(links).ReadCodes();

            Assert.True(success);
            Assert.Contains("NRC 0x11", result.Tcu!.PendingError);
            // A fast rejection is service-specific, so 0x0A is still probed (and here times out).
            Assert.Contains("No response", result.Tcu.PermanentError);
            Assert.Equal(["03", "07", "0A", "03", "07", "0A"], links.ExchangedHex);
        }

        [Fact]
        public void Dtc_ReadCodes_TcuSilenceOnPendingSkipsPermanent()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x03], 0x43, 0x00)
                .Reply([0x07], 0x47, 0x00)
                .Reply([0x0A], 0x4A, 0x00)
                .Reply(ECUDefinition.TCM, [0x03], 0x43, 0x00);

            var (success, _, result) = new J2534DtcService(links).ReadCodes();

            Assert.True(success);
            Assert.Contains("0x7E1", result.Tcu!.PendingError);
            Assert.Contains("not requested", result.Tcu.PermanentError);
            Assert.Equal(["03", "07", "0A", "03", "07"], links.ExchangedHex);
        }

        [Fact]
        public void Dtc_ClearTcuCodes_ClearsOnlyTheTcu()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x04], 0x44)
                .Reply(ECUDefinition.TCM, [0x04], 0x44);

            Assert.Equal((true, ""), new J2534DtcService(links).ClearTcuCodes());
            Assert.Equal(["04"], links.ExchangedHex);
            Assert.Equal(ECUDefinition.TCM.ResponseId, links.Exchanges[0].Ecu.ResponseId);
            Assert.Equal(0, links.LinksOpen);

            var (answered, error) = new J2534DtcService(new ScriptedLinkProvider().Reply([0x04], 0x44)).ClearTcuCodes();
            Assert.False(answered);
            Assert.Contains("TCM", error);
        }

        // ── Mode 0x13 ──────────────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(Mode13RequestForm.ReportAll, new byte[] { 0x13, 0xFF, 0x00 })]
        [InlineData(Mode13RequestForm.BareService, new byte[] { 0x13 })]
        public void Mode13_ReadAllCodes_SendsTheChosenFormAndDecodes(Mode13RequestForm form, byte[] request)
        {
            var links = new ScriptedLinkProvider().Reply(request, 0x53, 0x03, 0x01, 0x04, 0x20);

            var (success, error, result) = new J2534Mode13DtcService(links).ReadAllCodes(form);

            Assert.True(success, error);
            Assert.Equal(["P0301", "P0420"], result.Codes.Select(c => c.Code));
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Mode13_ReadAllCodes_ReportsRejection()
        {
            var links = new ScriptedLinkProvider().Reject([0x13, 0xFF, 0x00], 0x11);

            var (success, error, _) = new J2534Mode13DtcService(links).ReadAllCodes();

            Assert.False(success);
            Assert.Contains("NRC 0x11", error);
        }

        // ── Freeze frame ───────────────────────────────────────────────────────────────────

        [Fact]
        public void FreezeFrame_ReadsTriggeringDtcAndSupportedPids()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x02, 0x02, 0x00], 0x42, 0x02, 0x00, 0x03, 0x01)
                // Supported: 0x02, 0x05 (byte A) and 0x0C (byte B).
                .Reply([0x02, 0x00, 0x00], 0x42, 0x00, 0x00, 0x48, 0x10, 0x00, 0x00)
                .Reply([0x02, 0x05, 0x00], 0x42, 0x05, 0x00, 0x7B)
                .Reply([0x02, 0x0C, 0x00], 0x42, 0x0C, 0x00, 0x1A, 0xF8);

            var (success, error, result) = new J2534FreezeFrameService(links).ReadFreezeFrame();

            Assert.True(success, error);
            Assert.True(result.FrameStored);
            Assert.Equal("P0301", result.TriggeringDtc?.Code);
            Assert.Equal(["Coolant Temperature", "Engine Speed"], result.Entries.Select(e => e.Name));
            Assert.Empty(result.Warnings);
            Assert.All(links.Exchanges, e => Assert.Equal(2, e.EchoLength));
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void FreezeFrame_ZeroedDtcMeansNoFrame()
        {
            var links = new ScriptedLinkProvider().Reply([0x02, 0x02, 0x00], 0x42, 0x02, 0x00, 0x00, 0x00);

            var (success, _, result) = new J2534FreezeFrameService(links).ReadFreezeFrame();

            Assert.True(success);
            Assert.False(result.FrameStored);
            Assert.Single(links.Exchanges);
        }

        [Theory]
        [InlineData(0x12, true)]
        [InlineData(0x31, true)]
        [InlineData(0x22, false)]
        public void FreezeFrame_NoFrameNrcsAreNotErrors(byte nrc, bool expectSuccess)
        {
            var links = new ScriptedLinkProvider().Reject([0x02, 0x02, 0x00], nrc);

            var (success, error, result) = new J2534FreezeFrameService(links).ReadFreezeFrame();

            Assert.Equal(expectSuccess, success);
            Assert.False(result.FrameStored);
            if (!expectSuccess)
                Assert.Contains($"NRC 0x{nrc:X2}", error);
        }

        // ── Emissions ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void Emissions_RunCheck_ReadsMonitorsDtcsAndVin()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x01, 0x00], 0x41, 0x00, 0x80, 0x00, 0x00, 0x00)       // only PID 0x01
                .Reply([0x01, 0x01], 0x41, 0x01, 0x81, 0x07, 0x65, 0x00)       // MIL on, one code
                .Reply([0x03], 0x43, 0x01, 0x03, 0x01)
                .Reply([0x07], 0x47, 0x00)
                .Reply([0x0A], 0x4A, 0x00)
                .Reply([0x09, 0x02], [0x49, 0x02, 0x01, .. Ascii(Vin)]);

            var (success, error, result) = new J2534EmissionsService(links).RunCheck();

            Assert.True(success, error);
            Assert.True(result.SinceClear.MilOn);
            Assert.Equal(1, result.SinceClear.DtcCount);
            Assert.Equal(["P0301"], result.Dtcs.Stored.Select(c => c.Code));
            Assert.Equal(Vin, result.Vin);
            Assert.Contains(result.Warnings, w => w.Contains("Calibration ID"));
            Assert.Contains(result.Warnings, w => w.Contains("CVN"));
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Emissions_RunCheck_SilentEcuFails()
        {
            var (success, error, _) = new J2534EmissionsService(new ScriptedLinkProvider()).RunCheck();

            Assert.False(success);
            Assert.Contains("No response", error);
        }

        // ── Vehicle information ────────────────────────────────────────────────────────────

        [Fact]
        public void VehicleInfo_ReadsSupportedMode09PidsAndMode22Identification()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x09, 0x00, 0x00], 0x49, 0x00, 0x40, 0x00, 0x00, 0x00) // only PID 0x02
                .Reply([0x09, 0x02], [0x49, 0x02, 0x01, .. Ascii(Vin)])
                .Reply([0x22, 0x02, 0x0E], 0x62, 0x02, 0x0E, 0x12, 0x34, 0x56, 0x78);

            var readings = new VehicleInfoService(links).LoadVehicleData();

            Assert.Contains(readings, r => r.Name == "Vehicle Identification Number" && r.Value == Vin);
            Assert.Contains(readings, r => r.Name == "ECU Serial Number" && r.Value == "12 34 56 78");
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void VehicleInfo_Mode22ReplyForAnotherPidIsNotUsed()
        {
            // The ECU answers the serial-number request with the hardware number's PID.
            var links = new ScriptedLinkProvider()
                .Reply([0x22, 0x02, 0x0E], 0x62, 0x02, 0x0F, 0x12, 0x34, 0x56, 0x78);

            var readings = new VehicleInfoService(links).LoadVehicleData();

            Assert.DoesNotContain(readings, r => r.Name == "ECU Serial Number");
        }

        // ── Performance history ────────────────────────────────────────────────────────────

        [Fact]
        public void PerformanceHistory_ReadsCalibrationIdAndDataPids()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x09, 0x04], [0x49, 0x04, 0x01, .. Ascii("C132E0278"), 0, 0, 0, 0, 0, 0, 0])
                .Reply([0x22, 0x03, 0x01], 0x62, 0x03, 0x01, 0x00, 0x00, 0x00, 0x0A);

            var snapshot = new J2534PerformanceHistoryService(links).LoadPerformanceHistory();

            Assert.Equal("C132E0278", snapshot.CalibrationId);
            Assert.Contains("220301", links.ExchangedHex);
            Assert.All(links.Exchanges.Where(e => e.Request[0] == 0x22), e => Assert.Equal(2, e.EchoLength));
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void PerformanceHistory_NoDataThrows()
        {
            var links = new ScriptedLinkProvider();

            Assert.Throws<IOException>(() => new J2534PerformanceHistoryService(links).LoadPerformanceHistory());
            Assert.Equal(0, links.LinksOpen);
        }

        // ── ECU coding ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Coding_Read_CombinesLowAndHighWords()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x22, 0x02, 0x63], 0x62, 0x02, 0x63, 0x00, 0x00, 0x00, 0x00)
                .Reply([0x22, 0x02, 0x64], 0x62, 0x02, 0x64, 0x00, 0x00, 0x00, 0x00);

            var coding = new J2534EcuCodingService(links).ReadCoding();

            Assert.Equal(new byte[4], coding.GetLowBytes());
            Assert.Equal(new byte[4], coding.GetHighBytes());
            Assert.Equal(["220263", "220264"], links.ExchangedHex);
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Coding_Read_RejectionThrows()
        {
            var links = new ScriptedLinkProvider().Reject([0x22, 0x02, 0x63], 0x31);

            var ex = Assert.Throws<IOException>(() => new J2534EcuCodingService(links).ReadCoding());
            Assert.Contains("NRC 0x31", ex.Message);
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Coding_Read_SilenceNamesTheMissingPidAfterThreeAttempts()
        {
            var links = new ScriptedLinkProvider()
                .Reply([0x22, 0x02, 0x63], 0x62, 0x02, 0x63, 0x00, 0x00, 0x00, 0x00);

            var ex = Assert.Throws<TimeoutException>(() => new J2534EcuCodingService(links).ReadCoding());

            Assert.Contains("PID 0x0264", ex.Message);
            Assert.DoesNotContain("0x0263", ex.Message);
            Assert.Equal(3, links.ExchangedHex.Count(r => r == "220264"));
        }

        [Fact]
        public void Coding_Write_SendsBothWordsToEveryModule()
        {
            var links = new ScriptedLinkProvider();
            var coding = new T6eCodingDecoder(0x1122334455667788UL);

            var (success, error) = new J2534EcuCodingService(links).WriteCoding(coding);

            Assert.True(success, error);
            Assert.Equal([0x500u, 0x501u, 0x502u, 0x503u, 0x504u, 0x505u], links.CanFrames.Select(f => f.CanId));
            byte[] expected = [.. coding.GetHighBytes(), .. coding.GetLowBytes()];
            Assert.All(links.CanFrames, f => Assert.Equal(expected, f.Data));
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void Coding_Write_RefusedFrameIsReportedAndStopsTheWrite()
        {
            // Before the link, SendMessage's result was ignored and the write always reported success.
            var links = new ScriptedLinkProvider { RefuseCanId = 0x502 };

            var (success, error) = new J2534EcuCodingService(links).WriteCoding(new T6eCodingDecoder(0x1122334455667788UL));

            Assert.False(success);
            Assert.Contains("adapter refused", error);
            Assert.Equal([0x500u, 0x501u], links.CanFrames.Select(f => f.CanId));
            Assert.Equal(0, links.LinksOpen);
        }

        // ── Dyno mode, learned-data reset, VIN write ───────────────────────────────────────

        [Fact]
        public void DynoMode_SendsFiveTimesAndNeedsTheEchoedPid()
        {
            var links = new ScriptedLinkProvider().Reply([0x2F, 0x01, 0x70], 0x6F, 0x01, 0x70);

            Assert.Equal((true, ""), new J2534DynoModeService(links).EnableDynoMode());
            Assert.Equal(5, links.Exchanges.Count);
            Assert.Equal(0, links.LinksOpen);

            var wrongEcho = new ScriptedLinkProvider().Reply([0x2F, 0x01, 0x70], 0x6F, 0x01, 0x71);
            Assert.False(new J2534DynoModeService(wrongEcho).EnableDynoMode().success);
        }

        [Fact]
        public void LearnedDataReset_SendsWithoutWaiting()
        {
            var links = new ScriptedLinkProvider();

            Assert.Equal((true, ""), new J2534ObdResetService(links).PerformLearningReset());
            Assert.Equal([0x11], Assert.Single(links.Sent).Request);
            Assert.Empty(links.Exchanges);
            Assert.Equal(0, links.LinksOpen);
        }

        private static ScriptedLinkProvider VinWriteEcu(string vinAfterWrite)
        {
            byte[] v = Ascii(Vin);
            return new ScriptedLinkProvider()
                .Reply([0x3B, 0x01, .. v[3..7]], 0x7B, 0x01)
                .Reply([0x3B, 0x02, .. v[7..11]], 0x7B, 0x02)
                .Reply([0x3B, 0x03, .. v[11..15]], 0x7B, 0x03)
                .Reply([0x3B, 0x04, .. v[15..17]], 0x7B, 0x04)
                .Reply([0x09, 0x02], [0x49, 0x02, 0x01, .. Ascii(vinAfterWrite)]);
        }

        [Fact]
        public void VinWrite_SendsFourChunksAndVerifiesByReadBack()
        {
            var links = VinWriteEcu(vinAfterWrite: Vin);

            Assert.Equal((true, ""), new J2534VinSetService(links).SetVin(Vin));
            Assert.Equal(["3B01", "3B02", "3B03", "3B04", "0902"], links.ExchangedHex.Select(h => h[..4]));
            Assert.Equal(0, links.LinksOpen);
        }

        [Fact]
        public void VinWrite_UnchangedReadBackFails()
        {
            var links = VinWriteEcu(vinAfterWrite: "SCCLMDTU9BHA99999");

            var (success, error) = new J2534VinSetService(links).SetVin(Vin);

            Assert.False(success);
            Assert.Contains("did not change", error);
        }

        [Fact]
        public void VinWrite_RejectedChunkStopsTheWrite()
        {
            byte[] v = Ascii(Vin);
            var links = new ScriptedLinkProvider().Reject([0x3B, 0x01, .. v[3..7]], 0x22);

            var (success, error) = new J2534VinSetService(links).SetVin(Vin);

            Assert.False(success);
            Assert.Contains("NRC 0x22", error);
            Assert.Single(links.Exchanges);
        }
    }
}
