using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Covers the inspection-program rules: MIL, the per-model-year readiness allowances of the
    /// California Smog Check and federal I/M programs, California's confirmed- and permanent-code
    /// checks, its Cal ID / CVN software check, and how the criteria combine into one outcome.
    /// Expected values come from BAR's On-Board Diagnostic Test Reference (tables 1 and 3.5), its
    /// July 2021 modified-software advisory, and 40 CFR 85.2207 / 85.2222.
    /// </summary>
    public sealed class EmissionsCheckEvaluatorTests
    {
        // Spark-ignition C byte: catalyst, EVAP, O2 sensor, O2 heater supported.
        private const byte Supported = 0x65;
        private const byte CatalystBit = 0x01;
        private const byte EvapBit = 0x04;
        private const byte O2SensorBit = 0x20;
        private const byte O2HeaterBit = 0x40;

        // A made-up stock pair: tests other than the software ones run on "stock" software.
        private const string StockCalId = "C132E0278";
        private const uint StockCvn = 0x0000BEEF;

        private static readonly IReadOnlyDictionary<string, IReadOnlySet<uint>> StockSoftware =
            new Dictionary<string, IReadOnlySet<uint>> { [StockCalId] = new HashSet<uint> { StockCvn } };

        private static EmissionsCheckResult Result(
            byte incomplete = 0x00, bool milOn = false, byte continuous = 0x07, byte dtcCount = 0,
            IReadOnlyList<DiagnosticTroubleCode>? permanent = null, string? permanentError = null,
            IReadOnlyList<EmissionsCounter>? counters = null)
        {
            byte a = (byte)((milOn ? 0x80 : 0x00) | (milOn && dtcCount == 0 ? 1 : dtcCount));
            return new EmissionsCheckResult
            {
                SinceClear = EmissionsDecoder.DecodeMonitorStatusSinceClear([a, continuous, Supported, incomplete]),
                Dtcs = new DtcReadResult { Permanent = permanent ?? [], PermanentError = permanentError },
                Counters = counters ?? [],
                CalibrationIds = [StockCalId],
                Cvns = [StockCvn],
            };
        }

        private static EmissionsCheckResult Diesel(byte supported, byte incomplete) => new()
        {
            SinceClear = EmissionsDecoder.DecodeMonitorStatusSinceClear([0x00, 0x0F, supported, incomplete]),
            CalibrationIds = [StockCalId],
            Cvns = [StockCvn],
        };

        private static IReadOnlyList<EmissionsCounter> SinceCleared(int warmUps, int km) =>
        [
            new EmissionsCounter { Pid = 0x30, Name = "Warm-ups", Value = warmUps, Unit = "" },
            new EmissionsCounter { Pid = 0x31, Name = "Distance", Value = km, Unit = "km" },
        ];

        private static readonly DiagnosticTroubleCode P0420 = DiagnosticTroubleCode.FromBytes(0x04, 0x20);

        private static EmissionsOutcome California(EmissionsCheckResult r, int? year) =>
            EmissionsCheckEvaluator.Evaluate(r, EmissionsStandard.CaliforniaSmogCheck, year, StockSoftware).Outcome;

        private static EmissionsOutcome Federal(EmissionsCheckResult r, int? year) =>
            EmissionsCheckEvaluator.Evaluate(r, EmissionsStandard.FederalIm, year, StockSoftware).Outcome;

        [Fact]
        public void AllComplete_PassesBothPrograms()
        {
            Assert.Equal(EmissionsOutcome.Pass, California(Result(), 2010));
            Assert.Equal(EmissionsOutcome.Pass, Federal(Result(), 2010));
        }

        [Fact]
        public void MilOn_Fails()
        {
            Assert.Equal(EmissionsOutcome.Fail, California(Result(milOn: true), 2010));
            Assert.Equal(EmissionsOutcome.Fail, Federal(Result(milOn: true), 2010));
        }

        // ── California readiness ──────────────────────────────────────────────────────────

        [Fact]
        public void California2000Plus_OnlyEvapIncomplete_Passes()
        {
            Assert.Equal(EmissionsOutcome.Pass, California(Result(EvapBit), 2010));
        }

        [Fact]
        public void California2000Plus_CatalystIncomplete_Fails()
        {
            Assert.Equal(EmissionsOutcome.Fail, California(Result(CatalystBit), 2010));
        }

        [Fact]
        public void CaliforniaPre2000_AllowsAnyOne()
        {
            Assert.Equal(EmissionsOutcome.Pass, California(Result(CatalystBit), 1998));
            Assert.Equal(EmissionsOutcome.Fail, California(Result(CatalystBit | O2SensorBit), 1998));
        }

        // Diesel C/D bits: 0 NMHC catalyst, 1 NOx/SCR, 6 PM filter.
        [Fact]
        public void CaliforniaDiesel2007Plus_AllowsOnlyPmFilterAndNmhc()
        {
            Assert.Equal(EmissionsOutcome.Pass, California(Diesel(0x43, 0x41), 2015));
            Assert.Equal(EmissionsOutcome.Fail, California(Diesel(0x43, 0x02), 2015));
        }

        [Fact]
        public void CaliforniaDiesel1998To2006_AllowsNone()
        {
            Assert.Equal(EmissionsOutcome.Pass, California(Diesel(0x43, 0x00), 2005));
            Assert.Equal(EmissionsOutcome.Fail, California(Diesel(0x43, 0x40), 2005));
        }

        // ── California confirmed codes ────────────────────────────────────────────────────

        [Fact]
        public void California_ConfirmedDtcWithMilOff_Fails_FederalPasses()
        {
            var r = Result(dtcCount: 1);

            Assert.Equal(EmissionsOutcome.Fail, California(r, 2012));
            Assert.Equal(EmissionsOutcome.Pass, Federal(r, 2012));
        }

        // ── California permanent codes ────────────────────────────────────────────────────

        [Fact]
        public void California_PermanentDtc_Fails2010Plus()
        {
            var r = Result(permanent: [P0420]);

            Assert.Equal(EmissionsOutcome.Fail, California(r, 2012));
            Assert.Equal(EmissionsOutcome.Pass, California(r, 2008));
            Assert.Equal(EmissionsOutcome.Pass, Federal(r, 2012));
        }

        [Fact]
        public void California_PermanentDtc_IgnoredAfter15WarmUpsAnd200Miles()
        {
            // 322 km is just over 200 miles.
            Assert.Equal(EmissionsOutcome.Pass, California(Result(permanent: [P0420], counters: SinceCleared(15, 322)), 2012));
            Assert.Equal(EmissionsOutcome.Fail, California(Result(permanent: [P0420], counters: SinceCleared(14, 322)), 2012));
            Assert.Equal(EmissionsOutcome.Fail, California(Result(permanent: [P0420], counters: SinceCleared(15, 320)), 2012));
        }

        [Fact]
        public void California_PermanentReadFailure_IsIndeterminate_Except2010()
        {
            Assert.Equal(EmissionsOutcome.Indeterminate, California(Result(permanentError: "no response"), 2012));
            Assert.Equal(EmissionsOutcome.Pass, California(Result(permanentError: "no response"), 2010));
        }

        // ── California software (Cal ID / CVN) ────────────────────────────────────────────

        [Fact]
        public void California_ModifiedCvn_Fails_FederalIgnores()
        {
            var tuned = Result() with { Cvns = [0x00001234] };

            Assert.Equal(EmissionsOutcome.Fail, California(tuned, 2012));
            Assert.Equal(EmissionsOutcome.Pass, Federal(tuned, 2012));
        }

        [Fact]
        public void California_UnlistedCalId_IsIndeterminate()
        {
            var unknown = Result() with { CalibrationIds = ["E132E0288"] };

            Assert.Equal(EmissionsOutcome.Indeterminate, California(unknown, 2012));
        }

        [Fact]
        public void California_SoftwareUnread_IsIndeterminate()
        {
            Assert.Equal(EmissionsOutcome.Indeterminate, California(Result() with { Cvns = [] }, 2012));
        }

        [Fact]
        public void California_SoftwareNotCheckedBefore2000()
        {
            var tuned = Result() with { Cvns = [0x00001234] };

            Assert.Equal(EmissionsOutcome.Pass, California(tuned, 1999));
        }

        // ── Federal readiness ─────────────────────────────────────────────────────────────

        [Fact]
        public void Federal2001Plus_AllowsOne_RejectsTwo()
        {
            Assert.Equal(EmissionsOutcome.Pass, Federal(Result(CatalystBit), 2001));
            Assert.Equal(EmissionsOutcome.NotReady, Federal(Result(CatalystBit | EvapBit), 2001));
        }

        [Fact]
        public void Federal1996To2000_AllowsTwo_RejectsThree()
        {
            Assert.Equal(EmissionsOutcome.Pass, Federal(Result(CatalystBit | EvapBit), 2000));
            Assert.Equal(EmissionsOutcome.NotReady, Federal(Result(CatalystBit | EvapBit | O2SensorBit), 2000));
        }

        [Fact]
        public void Federal_MilOnOutranksNotReady()
        {
            Assert.Equal(EmissionsOutcome.Fail, Federal(Result(CatalystBit | EvapBit, milOn: true), 2010));
        }

        // ── Shared rules ──────────────────────────────────────────────────────────────────

        [Fact]
        public void IncompleteContinuousMonitor_IsIgnored()
        {
            // B = 0x17: misfire supported but incomplete.
            Assert.Equal(EmissionsOutcome.Pass, California(Result(continuous: 0x17), 2010));
        }

        [Fact]
        public void UnknownModelYear_IsIndeterminate_ButMilStillFails()
        {
            Assert.Equal(EmissionsOutcome.Indeterminate, Federal(Result(), null));
            Assert.Equal(EmissionsOutcome.Fail, Federal(Result(milOn: true), null));
        }

        [Fact]
        public void PreObdIiModelYear_IsIndeterminate()
        {
            Assert.Equal(EmissionsOutcome.Indeterminate, Federal(Result(), 1995));
        }

        [Fact]
        public void IncompleteMonitors_ListsOnlySupportedNonContinuous()
        {
            var status = Result(CatalystBit | 0x08 /* secondary air: unsupported */, continuous: 0x17).SinceClear;

            var incomplete = EmissionsCheckEvaluator.IncompleteMonitors(status);

            Assert.Equal([ReadinessMonitorId.Catalyst], incomplete.Select(m => m.Id));
        }
    }
}
