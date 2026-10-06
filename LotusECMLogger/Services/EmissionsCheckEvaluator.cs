namespace LotusECMLogger.Services
{
    /// <summary>The inspection program whose OBD pass/fail rules a check is judged against.</summary>
    public enum EmissionsStandard
    {
        /// <summary>California Smog Check (BAR OBD Inspection System).</summary>
        CaliforniaSmogCheck,

        /// <summary>Federal OBD-II I/M inspection (EPA, 40 CFR 85.2207 / 85.2222).</summary>
        FederalIm,
    }

    public enum EmissionsOutcome
    {
        Pass,
        Fail,

        /// <summary>
        /// Federal I/M: too many incomplete monitors. 40 CFR 85.2222(c)(1) sends the customer away
        /// to drive the car and return; only a second not-ready result fails it.
        /// </summary>
        NotReady,

        /// <summary>A criterion could not be judged (unknown model year, a read failed, ...).</summary>
        Indeterminate,
    }

    /// <summary>One inspection rule and how the vehicle fared against it.</summary>
    /// <param name="Passed">Null when the rule could not be judged.</param>
    public sealed record EmissionsCriterion(string Name, bool? Passed, string Detail);

    public sealed record EmissionsVerdict
    {
        public required EmissionsOutcome Outcome { get; init; }
        public required IReadOnlyList<EmissionsCriterion> Criteria { get; init; }
    }

    /// <summary>
    /// Judges an <see cref="EmissionsCheckResult"/> against an inspection program's OBD rules.
    /// A pre-check only: it cannot see the MIL bulb, the data link connector, or anything a
    /// station's equipment checks beyond the data the ECU reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// California follows 16 CCR 3340.42.2 and BAR's On-Board Diagnostic Test Reference (web
    /// revision 8.4, August 2025), whose tables set the allowances the OIS enforces. The October
    /// 2025 amendment to 3340.42.2 replaces the per-model-year allowances in the regulation with
    /// "all monitors complete", but BAR has kept the reference's allowances while it phases that
    /// in — revisit these when the reference changes. California also fails "Modified Software"
    /// (a Cal ID / CVN pair BAR does not recognise); that is judged against the local
    /// <see cref="StockSoftwareCatalog"/>, since BAR's list is not published.
    /// </para>
    /// <para>
    /// Federal follows 40 CFR 85.2207(c) (fail on a MIL commanded on for DTCs) and 85.2222(c)
    /// (readiness, with the 1996-2000 / 2001+ unset-monitor exceptions). Neither excludes
    /// continuous monitors in so many words; they are excluded here as in California.
    /// </para>
    /// <para>
    /// Not modelled: BAR's per-vehicle exceptions (OBD Test Reference tables 4-6), which the OIS
    /// applies by make, model and year. No Lotus appears in them.
    /// </para>
    /// </remarks>
    public static class EmissionsCheckEvaluator
    {
        private const int FirstObdIiModelYear = 1996;

        // BAR OBD Test Reference table 3.5: the permanent-code check applies to 2010 and newer.
        private const int CaliforniaPermanentDtcModelYear = 2010;

        // Table 3.5 footnote 3: permanent codes are ignored once the car has done at least this
        // much since its OBD data was last cleared (Mode 01 PIDs 0x30 and 0x31).
        private const int PermanentDtcWarmUpWaiver = 15;
        private const double PermanentDtcMilesWaiver = 200;
        private const double KmPerMile = 1.609344;

        public static string DisplayName(EmissionsStandard standard) => standard switch
        {
            EmissionsStandard.CaliforniaSmogCheck => "California Smog Check (CARB / BAR)",
            EmissionsStandard.FederalIm => "Federal OBD-II I/M (EPA)",
            _ => standard.ToString(),
        };

        public static EmissionsVerdict Evaluate(EmissionsCheckResult result, EmissionsStandard standard, int? modelYear) =>
            Evaluate(result, standard, modelYear, StockSoftwareCatalog.Known);

        /// <param name="stockSoftware">Calibration ID → stock CVNs for California's software check.</param>
        public static EmissionsVerdict Evaluate(
            EmissionsCheckResult result, EmissionsStandard standard, int? modelYear,
            IReadOnlyDictionary<string, IReadOnlySet<uint>> stockSoftware)
        {
            bool california = standard == EmissionsStandard.CaliforniaSmogCheck;
            var readiness = california
                ? CaliforniaReadiness(result.SinceClear, modelYear)
                : FederalReadiness(result.SinceClear, modelYear);

            var criteria = new List<EmissionsCriterion> { MilCriterion(result.SinceClear), readiness };
            if (california)
            {
                criteria.Add(CaliforniaStoredDtcs(result));
                criteria.Add(CaliforniaPermanentDtcs(result, modelYear));
                criteria.Add(CaliforniaSoftware(result, modelYear, stockSoftware));
            }

            // A federal readiness shortfall sends the car away to drive and return, not a failure
            // — but a lit MIL fails it however many monitors have run. California fails both.
            bool readinessFailed = readiness.Passed == false;
            bool otherFailed = criteria.Any(c => !ReferenceEquals(c, readiness) && c.Passed == false);

            EmissionsOutcome outcome =
                otherFailed || (readinessFailed && california) ? EmissionsOutcome.Fail
                : readinessFailed ? EmissionsOutcome.NotReady
                : criteria.Any(c => c.Passed is null) ? EmissionsOutcome.Indeterminate
                : EmissionsOutcome.Pass;

            return new EmissionsVerdict { Outcome = outcome, Criteria = criteria };
        }

        // 16 CCR 3340.42.2: "reports the MIL as commanded on"; 40 CFR 85.2207(c).
        private static EmissionsCriterion MilCriterion(MonitorStatus status) =>
            status.MilOn
                ? new("MIL commanded off", false, $"MIL commanded ON with {status.DtcCount} confirmed DTC(s)")
                : new("MIL commanded off", true, status.DtcCount == 0
                    ? "MIL off, no confirmed DTCs"
                    : $"MIL off ({status.DtcCount} confirmed DTC(s) not commanding the MIL)");

        /// <summary>Supported non-continuous monitors that have not completed. Continuous
        /// monitors are excluded by every inspection program.</summary>
        public static IReadOnlyList<ReadinessMonitor> IncompleteMonitors(MonitorStatus status) =>
            status.Monitors
                .Where(m => m.Kind == MonitorKind.NonContinuous && m.Readiness == MonitorReadiness.Incomplete)
                .ToList();

        private static EmissionsCriterion? ModelYearProblem(string name, int? modelYear) =>
            modelYear switch
            {
                null => new(name, null, "Model year unknown; set it to judge readiness"),
                < FirstObdIiModelYear => new(name, null, $"Model year {modelYear} predates OBD-II; OBD inspection does not apply"),
                _ => null,
            };

        private static string ListIncomplete(IReadOnlyList<ReadinessMonitor> incomplete) =>
            incomplete.Count == 0
                ? "All supported monitors complete"
                : $"{incomplete.Count} incomplete: {string.Join(", ", incomplete.Select(m => m.Name))}";

        // 40 CFR 85.2222(c)(2): 1996-2000 vehicles may pass with two or fewer unset monitors,
        // 2001 and newer with no more than one.
        private static EmissionsCriterion FederalReadiness(MonitorStatus status, int? modelYear)
        {
            const string name = "Readiness monitors";
            if (ModelYearProblem(name, modelYear) is { } problem)
                return problem;

            var incomplete = IncompleteMonitors(status);
            int allowed = modelYear <= 2000 ? 2 : 1;
            return new(name, incomplete.Count <= allowed,
                $"{ListIncomplete(incomplete)} (MY {modelYear}: up to {allowed} allowed)");
        }

        // BAR OBD Test Reference table 1. Gasoline: 1996-1999 any one incomplete, 2000+ only
        // EVAP. Diesel: 1998-2006 none, 2007+ only the particulate filter and NMHC catalyst.
        private static EmissionsCriterion CaliforniaReadiness(MonitorStatus status, int? modelYear)
        {
            const string name = "Readiness monitors";
            if (ModelYearProblem(name, modelYear) is { } problem)
                return problem;

            var incomplete = IncompleteMonitors(status);
            string list = ListIncomplete(incomplete);

            if (status.Ignition == IgnitionType.Compression)
            {
                if (modelYear < 1998)
                    return new(name, null, $"{list} (diesels before MY 1998 are not OBD-tested)");
                if (modelYear <= 2006)
                    return new(name, incomplete.Count == 0, $"{list} (diesel MY {modelYear}: none allowed)");
                bool dieselPassed = incomplete.All(m =>
                    m.Id is ReadinessMonitorId.PmFilter or ReadinessMonitorId.NmhcCatalyst);
                return new(name, dieselPassed,
                    $"{list} (diesel MY {modelYear}: only PM filter and NMHC catalyst may be incomplete)");
            }

            if (modelYear < 2000)
                return new(name, incomplete.Count <= 1, $"{list} (MY {modelYear}: any one allowed)");

            bool passed = incomplete.All(m => m.Id == ReadinessMonitorId.EvaporativeSystem);
            return new(name, passed, $"{list} (MY {modelYear}: only EVAP may be incomplete)");
        }

        // 16 CCR 3340.42.2 fails a car whose OBD system "reports a Diagnostic Trouble Code",
        // separately from the MIL criterion, so a confirmed code fails even after the MIL has gone
        // out. Pending codes are not confirmed DTCs and are not counted.
        private static EmissionsCriterion CaliforniaStoredDtcs(EmissionsCheckResult result)
        {
            const string name = "No confirmed DTCs";
            var stored = result.Dtcs.Stored;
            int count = Math.Max(stored.Count, result.SinceClear.DtcCount);

            if (count == 0)
            {
                return result.StoredDtcError == null
                    ? new(name, true, "No confirmed DTCs stored")
                    : new(name, true, "PID 0x01 reports no confirmed DTCs (stored-code list unavailable)");
            }

            string codes = stored.Count > 0 ? $": {string.Join(", ", stored.Select(d => d.Code))}" : "";
            return new(name, false, $"{count} confirmed DTC(s){codes}");
        }

        // BAR OBD Test Reference table 3.5 and its footnotes.
        private static EmissionsCriterion CaliforniaPermanentDtcs(EmissionsCheckResult result, int? modelYear)
        {
            const string name = "No permanent DTCs";
            var dtcs = result.Dtcs;
            if (modelYear is null)
                return new(name, null, "Model year unknown; permanent codes fail 2010 and newer vehicles");
            if (modelYear < CaliforniaPermanentDtcModelYear)
                return new(name, true, $"Not checked for MY {modelYear} (applies to 2010 and newer)");
            if (dtcs.PermanentError != null)
            {
                // Footnote 1: some 2010 vehicles do not support permanent codes and are exempt.
                return modelYear == CaliforniaPermanentDtcModelYear
                    ? new(name, true, $"Permanent codes unsupported; 2010 vehicles without them are exempt ({dtcs.PermanentError})")
                    : new(name, null, $"Permanent codes unavailable: {dtcs.PermanentError}");
            }
            if (dtcs.Permanent.Count == 0)
                return new(name, true, "No permanent DTCs stored");

            string codes = $"{dtcs.Permanent.Count} permanent: {string.Join(", ", dtcs.Permanent.Select(d => d.Code))}";
            string waiver = $"{PermanentDtcWarmUpWaiver} warm-ups and {PermanentDtcMilesWaiver:0} miles since codes cleared";
            return PermanentDtcWaiverMet(result.Counters) switch
            {
                true => new(name, true, $"{codes} — ignored: at least {waiver}"),
                false => new(name, false, $"{codes} (ignored only after {waiver})"),
                null => new(name, false, $"{codes} (warm-up/distance counters unavailable to check the waiver of {waiver})"),
            };
        }

        // 16 CCR 3340.42.2: fail when OBD data "does not match the original equipment manufacturer
        // (OEM) or an Air Resources Board (ARB) exempted OBD software configuration". BAR enforces
        // this from July 19, 2021 by comparing Cal ID / CVN with approved configurations on every
        // OIS-tested (2000 and newer) car, reported as "Modified Software". BAR's list is not
        // public, so only calibration IDs in the local stock catalog can be judged.
        private static EmissionsCriterion CaliforniaSoftware(
            EmissionsCheckResult result, int? modelYear, IReadOnlyDictionary<string, IReadOnlySet<uint>> stockSoftware)
        {
            const string name = "Software (Cal ID / CVN)";
            if (modelYear is null)
                return new(name, null, "Model year unknown; the software check applies to 2000 and newer");
            if (modelYear < 2000)
                return new(name, true, $"Not checked for MY {modelYear} (applies to 2000 and newer)");
            if (result.CalibrationIds.Count == 0 || result.Cvns.Count == 0)
                return new(name, null, "Calibration ID or CVN unavailable");

            var modified = new List<string>();
            var unknown = new List<string>();
            for (int i = 0; i < result.CalibrationIds.Count; i++)
            {
                string calId = result.CalibrationIds[i];
                if (i >= result.Cvns.Count)
                    return new(name, null, $"No CVN reported for calibration {calId}");

                uint cvn = result.Cvns[i];
                if (!stockSoftware.TryGetValue(calId, out var stock))
                    unknown.Add($"{calId} / CVN {cvn:X8}");
                else if (!stock.Contains(cvn))
                    modified.Add($"{calId} reports CVN {cvn:X8}, stock is {string.Join(" or ", stock.Select(c => c.ToString("X8")))}");
            }

            if (modified.Count > 0)
                return new(name, false, $"Modified software: {string.Join("; ", modified)}");
            if (unknown.Count > 0)
                return new(name, null, $"{string.Join("; ", unknown)} not in config\\stock_software.json; BAR checks its own (unpublished) list");
            return new(name, true, "Calibration ID and CVN match stock software");
        }

        /// <summary>
        /// Footnote 3 of table 3.5: permanent codes are ignored once the car has completed 15
        /// warm-up cycles and 200 miles since its OBD data was cleared. Null when either counter
        /// (PID 0x30 or 0x31) was not read.
        /// </summary>
        private static bool? PermanentDtcWaiverMet(IReadOnlyList<EmissionsCounter> counters)
        {
            var warmUps = counters.FirstOrDefault(c => c.Pid == 0x30);
            var distance = counters.FirstOrDefault(c => c.Pid == 0x31);
            if (warmUps is null || distance is null)
                return null;
            return warmUps.Value >= PermanentDtcWarmUpWaiver && distance.Value / KmPerMile >= PermanentDtcMilesWaiver;
        }
    }
}
