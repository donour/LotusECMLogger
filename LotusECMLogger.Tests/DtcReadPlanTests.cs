using LotusECMLogger.Services;
using SAE.J2534;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// The engine-required / transmission-optional policy behind Read Codes, exercised with a
    /// scripted transport so no J2534 device is needed.
    /// </summary>
    public sealed class DtcReadPlanTests
    {
        private static DiagnosticTroubleCode Code(string code) => new()
        {
            Code = code,
            Category = DtcCategory.Powertrain,
            Raw = 0x0301,
        };

        private sealed class ScriptedReads
        {
            private readonly Dictionary<(string Ecu, OBDIIMode Mode), Func<IReadOnlyList<DiagnosticTroubleCode>>> responses = [];

            public List<(string Ecu, OBDIIMode Mode, TimeSpan Timeout)> Calls { get; } = [];

            public ScriptedReads Return(string ecu, OBDIIMode mode, params DiagnosticTroubleCode[] codes)
            {
                responses[(ecu, mode)] = () => codes;
                return this;
            }

            public ScriptedReads Throw(string ecu, OBDIIMode mode, Exception exception)
            {
                responses[(ecu, mode)] = () => throw exception;
                return this;
            }

            public IReadOnlyList<DiagnosticTroubleCode> Read(ECUDefinition ecu, OBDIIMode mode, TimeSpan timeout)
            {
                Calls.Add((ecu.Name, mode, timeout));
                return responses.TryGetValue((ecu.Name, mode), out var response) ? response() : [];
            }

            public bool WasCalled(string ecu, OBDIIMode mode) =>
                Calls.Any(call => call.Ecu == ecu && call.Mode == mode);
        }

        [Fact]
        public void ReadCodes_EcmStoredFailurePropagates()
        {
            var reads = new ScriptedReads()
                .Throw("ECM", OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                    new DtcNoAnswerException("ECM did not answer."));

            Assert.Throws<DtcNoAnswerException>(() => DtcReadPlan.ReadCodes(reads.Read, includeTcu: true));
        }

        [Fact]
        public void ReadCodes_EcmOptionalFailuresBecomeNotes()
        {
            var reads = new ScriptedReads()
                .Return("ECM", OBDIIMode.ShowStoredDiagnosticTroubleCodes, Code("P0301"))
                .Throw("ECM", OBDIIMode.ShowPendingDiagnosticTroubleCodes, new IOException("no service 0x07"))
                .Throw("ECM", OBDIIMode.PermanentDiagnosticTroubleCodes,
                    new J2534Exception(ResultCode.FAILED, "adapter failure"));

            var result = DtcReadPlan.ReadCodes(reads.Read, includeTcu: false);

            Assert.Equal(["P0301"], result.Stored.Select(code => code.Code));
            Assert.Contains("no service 0x07", result.PendingError);
            Assert.Contains("adapter failure", result.PermanentError);
            Assert.Null(result.Tcu);
        }

        [Fact]
        public void ReadCodes_TcuUnavailableKeepsEngineCodesAndStopsTheTcuProbes()
        {
            var reads = new ScriptedReads()
                .Return("ECM", OBDIIMode.ShowStoredDiagnosticTroubleCodes, Code("P0301"))
                .Throw("TCM", OBDIIMode.ShowStoredDiagnosticTroubleCodes,
                    new DtcNoAnswerException("TCM did not answer service 0x03 at 0x7E1."));

            var result = DtcReadPlan.ReadCodes(reads.Read, includeTcu: true);

            Assert.Equal(["P0301"], result.Stored.Select(code => code.Code));
            Assert.NotNull(result.Tcu);
            Assert.Contains("0x7E1", result.Tcu!.StoredError);
            Assert.Empty(result.Tcu.Stored);
            Assert.False(reads.WasCalled("TCM", OBDIIMode.ShowPendingDiagnosticTroubleCodes));
            Assert.False(reads.WasCalled("TCM", OBDIIMode.PermanentDiagnosticTroubleCodes));
        }

        [Fact]
        public void ReadCodes_TcuPendingTimeoutSkipsThePermanentProbe()
        {
            var reads = new ScriptedReads()
                .Return("ECM", OBDIIMode.ShowStoredDiagnosticTroubleCodes)
                .Return("TCM", OBDIIMode.ShowStoredDiagnosticTroubleCodes)
                .Throw("TCM", OBDIIMode.ShowPendingDiagnosticTroubleCodes,
                    new DtcNoAnswerException("TCM did not answer service 0x07."));

            var result = DtcReadPlan.ReadCodes(reads.Read, includeTcu: true);

            Assert.NotNull(result.Tcu);
            Assert.Contains("timed out", result.Tcu!.PermanentError);
            Assert.False(reads.WasCalled("TCM", OBDIIMode.PermanentDiagnosticTroubleCodes));
        }

        [Fact]
        public void ReadCodes_TcuFastRejectionStillProbesPermanent()
        {
            var reads = new ScriptedReads()
                .Return("ECM", OBDIIMode.ShowStoredDiagnosticTroubleCodes)
                .Return("TCM", OBDIIMode.ShowStoredDiagnosticTroubleCodes)
                .Throw("TCM", OBDIIMode.ShowPendingDiagnosticTroubleCodes,
                    new IOException("TCM rejected service 0x07 (NRC 0x11)."))
                .Return("TCM", OBDIIMode.PermanentDiagnosticTroubleCodes, Code("P0700"));

            var result = DtcReadPlan.ReadCodes(reads.Read, includeTcu: true);

            Assert.Equal(["P0700"], result.Tcu!.Permanent.Select(code => code.Code));
            Assert.Contains("NRC 0x11", result.Tcu.PendingError);
        }

        [Fact]
        public void ReadCodes_TcuIsNullWhenTheFilterCouldNotBeInstalled()
        {
            var reads = new ScriptedReads()
                .Return("ECM", OBDIIMode.ShowStoredDiagnosticTroubleCodes);

            var result = DtcReadPlan.ReadCodes(reads.Read, includeTcu: false);

            Assert.Null(result.Tcu);
            Assert.False(reads.WasCalled("TCM", OBDIIMode.ShowStoredDiagnosticTroubleCodes));
        }
    }
}
