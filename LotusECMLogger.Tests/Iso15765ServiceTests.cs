using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the pure request builders and response parsers inside <see cref="Iso15765Service"/>
    /// against canned buffers, so they can be checked without a J2534 device. Every buffer carries
    /// the 4-byte CAN ID header the J2534 ISO15765 channel prepends: 0x7E0 on requests to the ECM,
    /// 0x7E8 on its responses.
    /// </summary>
    public sealed class Iso15765ServiceTests
    {
        private static readonly byte[] RequestHeader = [0x00, 0x00, 0x07, 0xE0];

        /// <summary>Prepends the ECM response header the J2534 channel delivers.</summary>
        private static byte[] Response(params byte[] tail) => [0x00, 0x00, 0x07, 0xE8, .. tail];

        private static IEnumerable<string> Codes(byte[] response) =>
            Iso15765Service.ParseDtcResponse(response).Select(c => c.Code);

        // ── ParseDtcResponse ────────────────────────────────────────────────────────────────

        [Fact]
        public void ParseDtcResponse_WithCountByte_SkipsCountAndReadsPairs()
        {
            // ISO 15765-4: 43 <count> <pairs...> — the odd payload length marks the count byte.
            Assert.Equal(["P0301", "P0420"], Codes(Response(0x43, 0x02, 0x03, 0x01, 0x04, 0x20)));
        }

        [Fact]
        public void ParseDtcResponse_WithoutCountByte_ReadsPairsFromFirstByte()
        {
            // An even payload has no count byte, so the codes start immediately after the SID.
            Assert.Equal(["P0301", "P0420"], Codes(Response(0x43, 0x03, 0x01, 0x04, 0x20)));
        }

        [Fact]
        public void ParseDtcResponse_SkipsZeroPadding()
        {
            Assert.Equal(["P0301"], Codes(Response(0x43, 0x02, 0x03, 0x01, 0x00, 0x00)));
        }

        [Fact]
        public void ParseDtcResponse_ZeroCount_ReturnsEmpty()
        {
            Assert.Empty(Codes(Response(0x43, 0x00)));
        }

        [Fact]
        public void ParseDtcResponse_SidOnly_ReturnsEmpty()
        {
            Assert.Empty(Codes(Response(0x43)));
        }

        [Fact]
        public void ParseDtcResponse_PendingAndPermanentServices_ParseTheSameWay()
        {
            // The parser keys off the layout, not the SID, so 0x47 and 0x4A decode identically.
            Assert.Equal(["P0171"], Codes(Response(0x47, 0x01, 0x01, 0x71)));
            Assert.Equal(["U0100"], Codes(Response(0x4A, 0x01, 0xC1, 0x00)));
        }

        // ── DiagnosticTroubleCode.FromBytes ─────────────────────────────────────────────────

        [Theory]
        [InlineData(0x03, 0x01, "P0301", DtcCategory.Powertrain)]
        [InlineData(0x41, 0x23, "C0123", DtcCategory.Chassis)]
        [InlineData(0x80, 0x01, "B0001", DtcCategory.Body)]
        [InlineData(0xC1, 0x00, "U0100", DtcCategory.Network)]
        [InlineData(0x31, 0x00, "P3100", DtcCategory.Powertrain)]   // first digit uses bits 5-4
        [InlineData(0x0A, 0xBC, "P0ABC", DtcCategory.Powertrain)]   // remaining digits are hex
        [InlineData(0xFF, 0xFF, "U3FFF", DtcCategory.Network)]
        public void FromBytes_DecodesCategoryAndDigits(byte high, byte low, string code, DtcCategory category)
        {
            var dtc = DiagnosticTroubleCode.FromBytes(high, low);

            Assert.Equal(code, dtc.Code);
            Assert.Equal(category, dtc.Category);
            Assert.Equal((ushort)((high << 8) | low), dtc.Raw);
        }

        // ── ParseSupportedPIDsResponse ──────────────────────────────────────────────────────

        [Fact]
        public void ParseSupportedPids_DecodesBitmaskMsbFirst()
        {
            // 0x55 = 0101_0101 → PIDs 02, 04, 06, 08; 0x40 in the second byte → PID 0A.
            // These are the Mode 09 PIDs GetPID knows how to fetch.
            var pids = Iso15765Service.ParseSupportedPIDsResponse(
                Response(0x49, 0x00, 0x55, 0x40, 0x00, 0x00), OBDIIMode.RequestVehicleInformation);

            Assert.Equal([0x02, 0x04, 0x06, 0x08, 0x0A], pids);
        }

        [Fact]
        public void ParseSupportedPids_LastBitOfFourthByteIsPid20()
        {
            var pids = Iso15765Service.ParseSupportedPIDsResponse(
                Response(0x49, 0x00, 0x80, 0x00, 0x00, 0x01), OBDIIMode.RequestVehicleInformation);

            Assert.Equal([0x01, 0x20], pids);
        }

        [Fact]
        public void ParseSupportedPids_WrongResponseId_ReturnsEmpty()
        {
            // A TX echo carries the request ID 0x7E0, not the ECM's 0x7E8.
            byte[] echo = [0x00, 0x00, 0x07, 0xE0, 0x49, 0x00, 0xFF, 0xFF, 0xFF, 0xFF];

            Assert.Empty(Iso15765Service.ParseSupportedPIDsResponse(echo, OBDIIMode.RequestVehicleInformation));
        }

        [Fact]
        public void ParseSupportedPids_WrongMode_ReturnsEmpty()
        {
            var pids = Iso15765Service.ParseSupportedPIDsResponse(
                Response(0x41, 0x00, 0xFF, 0xFF, 0xFF, 0xFF), OBDIIMode.RequestVehicleInformation);

            Assert.Empty(pids);
        }

        [Fact]
        public void ParseSupportedPids_ResponseToOtherPid_ReturnsEmpty()
        {
            var pids = Iso15765Service.ParseSupportedPIDsResponse(
                Response(0x49, 0x20, 0xFF, 0xFF, 0xFF, 0xFF), OBDIIMode.RequestVehicleInformation);

            Assert.Empty(pids);
        }

        [Fact]
        public void ParseSupportedPids_TruncatedResponse_ReturnsEmpty()
        {
            Assert.Empty(Iso15765Service.ParseSupportedPIDsResponse(
                Response(0x49), OBDIIMode.RequestVehicleInformation));
        }

        // ── Request builders ────────────────────────────────────────────────────────────────

        [Fact]
        public void BuildModeMessage_FreezeFrame_AppendsFrameNumber()
        {
            byte[] message = Iso15765Service.BuildModeMessage(OBDIIMode.ShowFreezeFrameData, 0x0C, 0x01);

            Assert.Equal([.. RequestHeader, 0x02, 0x0C, 0x01], message);
        }

        [Fact]
        public void BuildMultiPidMessage_PacksModeAndPids()
        {
            byte[] message = Iso15765Service.BuildMultiPIDMessage(
                OBDIIMode.RequestVehicleInformation, [0x02, 0x04]);

            Assert.Equal([.. RequestHeader, 0x09, 0x02, 0x04], message);
        }

        [Fact]
        public void BuildMultiPidMessage_AcceptsSixPids()
        {
            byte[] message = Iso15765Service.BuildMultiPIDMessage(
                OBDIIMode.ShowCurrentData, [0x04, 0x05, 0x0B, 0x0C, 0x0D, 0x0F]);

            Assert.Equal(RequestHeader.Length + 1 + 6, message.Length);
        }

        [Fact]
        public void BuildMultiPidMessage_RejectsEmptyAndOversizedLists()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Iso15765Service.BuildMultiPIDMessage(OBDIIMode.ShowCurrentData, []));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Iso15765Service.BuildMultiPIDMessage(OBDIIMode.ShowCurrentData, [1, 2, 3, 4, 5, 6, 7]));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0x100)]
        public void BuildMultiPidMessage_RejectsPidOutsideByteRange(int pid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Iso15765Service.BuildMultiPIDMessage(OBDIIMode.ShowCurrentData, [pid]));
        }

        [Fact]
        public void BuildMode13Request_ReportAll_AddsSubFunction()
        {
            Assert.Equal([.. RequestHeader, 0x13, 0xFF, 0x00],
                Iso15765Service.BuildMode13Request(Mode13RequestForm.ReportAll));
        }

        [Fact]
        public void BuildMode13Request_BareService_IsServiceByteOnly()
        {
            Assert.Equal([.. RequestHeader, 0x13],
                Iso15765Service.BuildMode13Request(Mode13RequestForm.BareService));
        }

        [Fact]
        public void BuildMode13Request_BothFormsFitInOneFrame()
        {
            // The firmware reads the single receive frame directly and reassembles no multi-frame
            // request, so the ISO-TP payload must stay within a single frame's 7 bytes.
            foreach (var form in Enum.GetValues<Mode13RequestForm>())
                Assert.True(Iso15765Service.BuildMode13Request(form).Length - RequestHeader.Length <= 7);
        }

        // ── ECU-parameterized DTC reads ─────────────────────────────────────────────────────

        /// <summary>Prepends the TCU response header the J2534 channel delivers.</summary>
        private static byte[] TcuResponse(params byte[] tail) => [0x00, 0x00, 0x07, 0xE9, .. tail];

        [Fact]
        public void BuildDtcRequest_UsesTheTargetEcuHeader()
        {
            Assert.Equal([0x00, 0x00, 0x07, 0xE0, 0x03],
                Iso15765Service.BuildDtcRequest(ECUDefinition.ECM, OBDIIMode.ShowStoredDiagnosticTroubleCodes));
            Assert.Equal([0x00, 0x00, 0x07, 0xE1, 0x03],
                Iso15765Service.BuildDtcRequest(ECUDefinition.TCM, OBDIIMode.ShowStoredDiagnosticTroubleCodes));
            // The TCU clear uses the same address with service 0x04.
            Assert.Equal([0x00, 0x00, 0x07, 0xE1, 0x04],
                Iso15765Service.BuildDtcRequest(
                    ECUDefinition.TCM, OBDIIMode.ClearDiagnosticTroubleCodesAndStoredValues));
        }

        [Fact]
        public void ClassifyDtcReply_RecognisesTheTcuClearPositiveResponse()
        {
            Assert.Equal(
                Iso15765Service.DtcReplyKind.Positive,
                Iso15765Service.ClassifyDtcReply(
                    ECUDefinition.TCM,
                    OBDIIMode.ClearDiagnosticTroubleCodesAndStoredValues,
                    TcuResponse(0x44),
                    out _));
        }

        [Fact]
        public void ReadDtcs_SendsTheTargetModulesRequestAndAcceptsItsReply()
        {
            byte[]? sent = null;
            var replies = new Queue<byte[]>(
            [
                TcuResponse(0x43, 0x02, 0x07, 0x00, 0x07, 0x21),
            ]);

            var codes = Iso15765Service.ReadDtcsCore(
                ECUDefinition.TCM,
                OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                TimeSpan.FromSeconds(1),
                request => sent = request,
                () => replies.Count > 0 ? replies.Dequeue() : null);

            Assert.Equal([0x00, 0x00, 0x07, 0xE1, 0x03], sent);
            Assert.Equal(["P0700", "P0721"], codes.Select(c => c.Code));
        }

        [Fact]
        public void ReadDtcs_IgnoresEchoesAndTheOtherModulesCodes()
        {
            // The payloads differ on purpose: if routing were lost, the ECM code would show up.
            var replies = new Queue<byte[]>(
            [
                [0x00, 0x00, 0x07, 0xE1, 0x03, 0x00, 0x00, 0x00], // echo of the request
                Response(0x43, 0x01, 0x03, 0x01),                 // ECM says P0301
                TcuResponse(0x43, 0x01, 0x07, 0x00),              // TCU says P0700
            ]);

            var codes = Iso15765Service.ReadDtcsCore(
                ECUDefinition.TCM,
                OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                TimeSpan.FromSeconds(1),
                _ => { },
                () => replies.Count > 0 ? replies.Dequeue() : null);

            Assert.Equal(["P0700"], codes.Select(c => c.Code));
        }

        [Fact]
        public void ReadDtcs_IgnoresSameAddressTrafficForAnotherService()
        {
            // The AEM wideband shares 0x7E1/0x7E9 and answers Mode 01 only.
            var replies = new Queue<byte[]>(
            [
                TcuResponse(0x41, 0x24, 0x00, 0x00),
                TcuResponse(0x47, 0x01, 0x01, 0x71),
                TcuResponse(0x43, 0x01, 0x07, 0x00),
            ]);

            var codes = Iso15765Service.ReadDtcsCore(
                ECUDefinition.TCM,
                OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                TimeSpan.FromSeconds(1),
                _ => { },
                () => replies.Count > 0 ? replies.Dequeue() : null);

            Assert.Equal(["P0700"], codes.Select(c => c.Code));
        }

        [Fact]
        public void ReadDtcs_NegativeResponseThrowsIOExceptionWithNrc()
        {
            var ex = Assert.Throws<IOException>(() => Iso15765Service.ReadDtcsCore(
                ECUDefinition.TCM,
                OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                TimeSpan.FromSeconds(1),
                _ => { },
                () => TcuResponse(0x7F, 0x03, 0x22)));

            Assert.Contains("NRC 0x22", ex.Message);
            Assert.IsNotType<DtcNoAnswerException>(ex);
        }

        [Fact]
        public void ReadDtcs_ResponsePendingExtendsTheDeadline()
        {
            int calls = 0;

            // The 0x78 reply arrives after the original 50 ms budget has already run out; only
            // the response-pending extension lets the loop consume the final answer.
            var codes = Iso15765Service.ReadDtcsCore(
                ECUDefinition.TCM,
                OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                TimeSpan.FromMilliseconds(50),
                _ => { },
                () =>
                {
                    if (++calls > 1)
                        return TcuResponse(0x43, 0x01, 0x07, 0x00);

                    Thread.Sleep(60);
                    return TcuResponse(0x7F, 0x03, 0x78);
                });

            Assert.Equal(["P0700"], codes.Select(c => c.Code));
        }

        [Fact]
        public void ReadDtcs_SilenceThrowsDtcNoAnswerExceptionNamingTheAddress()
        {
            var ex = Assert.Throws<DtcNoAnswerException>(() => Iso15765Service.ReadDtcsCore(
                ECUDefinition.TCM,
                OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                TimeSpan.FromMilliseconds(1),
                _ => { },
                () => null));

            Assert.Contains("0x7E1", ex.Message);
        }
    }
}
