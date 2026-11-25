using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// The DTC tab's rows and status text. The control itself has no service seam, so this is
    /// where the user-visible claims ("no codes", "not available", what a clear removes) are
    /// pinned.
    /// </summary>
    public sealed class DtcReadSummaryTests
    {
        private static DiagnosticTroubleCode Code(string code) => new()
        {
            Code = code,
            Category = DtcCategory.Powertrain,
            Raw = 0x0301,
        };

        [Fact]
        public void Rows_AddTheModuleColumnAndSkipAnUnavailableTcu()
        {
            var result = new DtcReadResult
            {
                Stored = [Code("P0301")],
                Pending = [Code("P0171")],
                Tcu = new DtcModuleResult { StoredError = "no answer" },
            };

            var rows = DtcReadSummary.Rows(result);

            Assert.Collection(rows,
                row =>
                {
                    Assert.Equal("P0301", row.Code.Code);
                    Assert.Equal("ECM", row.Module);
                    Assert.Equal("Stored", row.Type);
                },
                row =>
                {
                    Assert.Equal("P0171", row.Code.Code);
                    Assert.Equal("ECM", row.Module);
                    Assert.Equal("Pending", row.Type);
                });
        }

        [Fact]
        public void Rows_IncludeTcuCodesWhenTheModuleAnswered()
        {
            var result = new DtcReadResult
            {
                Tcu = new DtcModuleResult
                {
                    Stored = [Code("P0700")],
                    Permanent = [Code("P0721")],
                },
            };

            var rows = DtcReadSummary.Rows(result);

            Assert.Equal(["TCU P0700 Stored", "TCU P0721 Permanent"],
                rows.Select(row => $"{row.Module} {row.Code.Code} {row.Type}"));
        }

        [Fact]
        public void Describe_EngineOnlyCleanSaysEngine()
        {
            Assert.Equal("No engine trouble codes",
                DtcReadSummary.Describe(new DtcReadResult(), descriptionsAvailable: true));
        }

        [Fact]
        public void Describe_BothModulesCleanSaysEngineOrTransmission()
        {
            var result = new DtcReadResult { Tcu = new DtcModuleResult() };

            Assert.Equal("No engine or transmission trouble codes",
                DtcReadSummary.Describe(result, descriptionsAvailable: true));
        }

        [Fact]
        public void Describe_TcuUnavailableNamesTheAddress()
        {
            var result = new DtcReadResult
            {
                Stored = [Code("P0301")],
                Tcu = new DtcModuleResult { StoredError = "No answer at 0x7E1." },
            };

            string text = DtcReadSummary.Describe(result, descriptionsAvailable: true);

            Assert.Contains("ECM: 1 stored", text);
            Assert.Contains("TCU: not available (0x7E1)", text);
        }

        [Fact]
        public void Describe_UnreadListNeverRendersAsNoCodes()
        {
            var result = new DtcReadResult
            {
                PendingError = "No answer to service 0x07.",
                Tcu = new DtcModuleResult { PendingError = "No answer to service 0x07." },
            };

            string text = DtcReadSummary.Describe(result, descriptionsAvailable: true);

            Assert.Contains("ECM: no stored codes", text);
            Assert.Contains("TCU: no stored codes", text);
            Assert.DoesNotContain("ECM: no codes", text);
            Assert.DoesNotContain("TCU: no codes", text);
        }

        [Fact]
        public void Describe_CountsAndNotesAreCombined()
        {
            var result = new DtcReadResult
            {
                Stored = [Code("P0301")],
                Pending = [Code("P0171")],
                PermanentError = "no service 0x0A",
                Tcu = new DtcModuleResult { Stored = [Code("P0700"), Code("P0721")] },
            };

            string text = DtcReadSummary.Describe(result, descriptionsAvailable: false);

            Assert.Contains("ECM: 1 stored, 1 pending", text);
            Assert.Contains("TCU: 2 stored", text);
            Assert.Contains("engine permanent codes unavailable", text);
            Assert.Contains("code descriptions unavailable", text);
        }

        [Fact]
        public void DescribeDetail_IncludesTheFailureReasons()
        {
            var result = new DtcReadResult
            {
                PendingError = "engine pending failure",
                Tcu = new DtcModuleResult { StoredError = "tcu failure" },
            };

            string detail = DtcReadSummary.DescribeDetail(result);

            Assert.Contains("engine pending failure", detail);
            Assert.Contains("tcu failure", detail);
        }

        [Fact]
        public void ClearPredicatesSeparateModulesAndPermanentCodes()
        {
            var engineStored = new DtcRow(Code("P0301"), "ECM", "Stored");
            var enginePermanent = new DtcRow(Code("P0301"), "ECM", "Permanent");
            var tcuStored = new DtcRow(Code("P0700"), "TCU", "Stored");
            var tcuPermanent = new DtcRow(Code("P0700"), "TCU", "Permanent");

            Assert.True(DtcReadSummary.IsClearedByMode04(engineStored));
            Assert.False(DtcReadSummary.IsClearedByMode04(enginePermanent));
            Assert.False(DtcReadSummary.IsClearedByMode04(tcuStored));
            Assert.False(DtcReadSummary.IsClearedByMode04(null));

            Assert.True(DtcReadSummary.IsClearedByTcuMode04(tcuStored));
            Assert.False(DtcReadSummary.IsClearedByTcuMode04(tcuPermanent));
            Assert.False(DtcReadSummary.IsClearedByTcuMode04(engineStored));
            Assert.False(DtcReadSummary.IsClearedByTcuMode04(null));
        }
    }
}
