namespace LotusECMLogger.Services
{
    /// <summary>One row of the DTC tab: a code plus the module and category it came from.</summary>
    internal sealed record DtcRow(DiagnosticTroubleCode Code, string Module, string Type);

    /// <summary>
    /// Presentation logic for the DTC tab, kept pure so the row set and the status line can be
    /// tested without constructing the WinForms control.
    /// </summary>
    internal static class DtcReadSummary
    {
        internal const string EngineModule = "ECM";
        internal const string TcuModule = "TCU";
        internal const string StoredType = "Stored";
        internal const string PendingType = "Pending";
        internal const string PermanentType = "Permanent";

        internal static IReadOnlyList<DtcRow> Rows(DtcReadResult result)
        {
            var rows = new List<DtcRow>();
            AddRows(rows, EngineModule, result.Stored, result.Pending, result.Permanent);

            // An unavailable module's empty lists are "unknown", not "no codes": it gets a
            // status note instead of rows.
            if (result.Tcu is { StoredError: null } tcu)
                AddRows(rows, TcuModule, tcu.Stored, tcu.Pending, tcu.Permanent);

            return rows;
        }

        private static void AddRows(
            List<DtcRow> rows,
            string module,
            IReadOnlyList<DiagnosticTroubleCode> stored,
            IReadOnlyList<DiagnosticTroubleCode> pending,
            IReadOnlyList<DiagnosticTroubleCode> permanent)
        {
            foreach (var code in stored)
                rows.Add(new DtcRow(code, module, StoredType));
            foreach (var code in pending)
                rows.Add(new DtcRow(code, module, PendingType));
            foreach (var code in permanent)
                rows.Add(new DtcRow(code, module, PermanentType));
        }

        /// <summary>
        /// True for rows an engine Mode 04 clear genuinely removes: engine stored and pending
        /// codes. Engine permanent codes survive the clear, and transmission rows were never
        /// addressed.
        /// </summary>
        internal static bool IsClearedByMode04(object? tag) =>
            tag is DtcRow { Module: EngineModule, Type: not PermanentType };

        /// <summary>True for rows a transmission Mode 04 clear removes: TCU stored and pending
        /// codes. TCU permanent codes survive the clear, and engine rows are untouched.</summary>
        internal static bool IsClearedByTcuMode04(object? tag) =>
            tag is DtcRow { Module: TcuModule, Type: not PermanentType };

        /// <summary>
        /// Builds the one-line status. A module whose list was never read is never described
        /// as having no codes; a transmission that did not answer is named with its address.
        /// </summary>
        internal static string Describe(DtcReadResult result, bool descriptionsAvailable)
        {
            bool engineClean = IsClean(
                result.Stored, result.Pending, result.Permanent,
                result.PendingError, result.PermanentError);

            if (result.Tcu is null && engineClean)
                return AppendNotes("No engine trouble codes", result, descriptionsAvailable);

            if (result.Tcu is { StoredError: null } tcu &&
                engineClean &&
                IsClean(tcu.Stored, tcu.Pending, tcu.Permanent, tcu.PendingError, tcu.PermanentError))
            {
                return AppendNotes(
                    "No engine or transmission trouble codes", result, descriptionsAvailable);
            }

            var parts = new List<string>
            {
                DescribeModule(
                    EngineModule, result.Stored, result.Pending, result.Permanent,
                    result.PendingError, result.PermanentError),
            };

            if (result.Tcu is { } tcuResult)
            {
                parts.Add(tcuResult.StoredError != null
                    ? $"TCU: not available (0x{ECUDefinition.TCM.RequestId:X3})"
                    : DescribeModule(
                        TcuModule, tcuResult.Stored, tcuResult.Pending, tcuResult.Permanent,
                        tcuResult.PendingError, tcuResult.PermanentError));
            }

            return AppendNotes(string.Join(" · ", parts), result, descriptionsAvailable);
        }

        /// <summary>The failure reasons behind the status line, for its tooltip.</summary>
        internal static string DescribeDetail(DtcReadResult result)
        {
            var lines = new List<string>();
            if (result.PendingError != null)
                lines.Add($"Engine pending codes: {result.PendingError}");
            if (result.PermanentError != null)
                lines.Add($"Engine permanent codes: {result.PermanentError}");

            if (result.Tcu is { } tcu)
            {
                if (tcu.StoredError != null)
                {
                    lines.Add($"Transmission codes: {tcu.StoredError}");
                }
                else
                {
                    if (tcu.PendingError != null)
                        lines.Add($"Transmission pending codes: {tcu.PendingError}");
                    if (tcu.PermanentError != null)
                        lines.Add($"Transmission permanent codes: {tcu.PermanentError}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string AppendNotes(
            string text, DtcReadResult result, bool descriptionsAvailable)
        {
            var notes = new List<string>();
            if (result.PendingError != null)
                notes.Add("engine pending codes unavailable");
            if (result.PermanentError != null)
                notes.Add("engine permanent codes unavailable");

            if (result.Tcu is { StoredError: null } tcu)
            {
                if (tcu.PendingError != null)
                    notes.Add("TCU pending codes unavailable");
                if (tcu.PermanentError != null)
                    notes.Add("TCU permanent codes unavailable");
            }

            // Without this note a missing catalog just looks like a table full of unknown codes;
            // it says nothing useful when there are no codes to describe.
            if (!descriptionsAvailable && HasAnyCode(result))
                notes.Add("code descriptions unavailable");

            return notes.Count == 0 ? text : $"{text} — {string.Join(", ", notes)}";
        }

        private static bool HasAnyCode(DtcReadResult result)
        {
            if (result.Stored.Count + result.Pending.Count + result.Permanent.Count > 0)
                return true;

            return result.Tcu is { StoredError: null } tcu &&
                tcu.Stored.Count + tcu.Pending.Count + tcu.Permanent.Count > 0;
        }

        private static bool IsClean(
            IReadOnlyList<DiagnosticTroubleCode> stored,
            IReadOnlyList<DiagnosticTroubleCode> pending,
            IReadOnlyList<DiagnosticTroubleCode> permanent,
            string? pendingError,
            string? permanentError) =>
            stored.Count == 0 && pending.Count == 0 && permanent.Count == 0 &&
            pendingError == null && permanentError == null;

        private static string DescribeModule(
            string module,
            IReadOnlyList<DiagnosticTroubleCode> stored,
            IReadOnlyList<DiagnosticTroubleCode> pending,
            IReadOnlyList<DiagnosticTroubleCode> permanent,
            string? pendingError,
            string? permanentError)
        {
            var counts = new List<string>();
            if (stored.Count > 0)
                counts.Add($"{stored.Count} stored");
            if (pending.Count > 0)
                counts.Add($"{pending.Count} pending");
            if (permanent.Count > 0)
                counts.Add($"{permanent.Count} permanent");

            if (counts.Count > 0)
                return $"{module}: {string.Join(", ", counts)}";

            // Empty lists only justify "no codes" for the categories that were actually read.
            return pendingError == null && permanentError == null
                ? $"{module}: no codes"
                : $"{module}: no stored codes";
        }
    }
}
