namespace LotusECMLogger.Services
{
    /// <summary>
    /// The two-module policy behind <see cref="J2534DtcService.ReadCodes"/>, kept pure so the
    /// engine-required / transmission-optional behaviour is testable without a J2534 device.
    /// </summary>
    internal static class DtcReadPlan
    {
        /// <summary>The old ten-attempt (10 × 250 ms) engine budget.</summary>
        internal static readonly TimeSpan EcmTimeout = TimeSpan.FromMilliseconds(2500);

        /// <summary>
        /// One short probe. On a manual car nothing answers 0x7E1, and Read Codes must not
        /// spend three full engine budgets discovering that. A live module answers well inside
        /// this window.
        /// </summary>
        internal static readonly TimeSpan TcuTimeout = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// Reads the engine's codes and, when <paramref name="includeTcu"/> is true, the
        /// transmission's. The engine's stored read is mandatory and its exception propagates;
        /// every optional read degrades to a note instead.
        /// </summary>
        /// <param name="read">One DTC service request against one module.</param>
        /// <param name="includeTcu">
        /// False when the adapter could not install the transmission reply filter. The result
        /// then has a null <see cref="DtcReadResult.Tcu"/>; the caller may replace it with an
        /// explanatory error.
        /// </param>
        internal static DtcReadResult ReadCodes(
            Func<ECUDefinition, OBDIIMode, TimeSpan, IReadOnlyList<DiagnosticTroubleCode>> read,
            bool includeTcu)
        {
            // Engine stored codes remain required: Read Codes has always failed when the engine
            // does not answer, and the emissions pre-check relies on that.
            var stored = read(ECUDefinition.ECM, OBDIIMode.ShowStoredDiagnosticTroubleCodes, EcmTimeout);

            var (pending, pendingError) = TryReadEcm(read, OBDIIMode.ShowPendingDiagnosticTroubleCodes);
            var (permanent, permanentError) = TryReadEcm(read, OBDIIMode.PermanentDiagnosticTroubleCodes);

            var result = new DtcReadResult
            {
                Stored = stored,
                Pending = pending,
                Permanent = permanent,
                PendingError = pendingError,
                PermanentError = permanentError,
            };

            if (!includeTcu)
                return result;

            return result with { Tcu = ReadTcu(read) };
        }

        private static (IReadOnlyList<DiagnosticTroubleCode> codes, string? error) TryReadEcm(
            Func<ECUDefinition, OBDIIMode, TimeSpan, IReadOnlyList<DiagnosticTroubleCode>> read,
            OBDIIMode mode)
        {
            try
            {
                return (read(ECUDefinition.ECM, mode, EcmTimeout), null);
            }
            // J2534Exception is not an IOException; without it a transient adapter failure
            // would escape the note and discard the engine codes already read.
            catch (Exception ex) when (ex is IOException or J2534Exception)
            {
                return ([], ex.Message);
            }
        }

        private static DtcModuleResult ReadTcu(
            Func<ECUDefinition, OBDIIMode, TimeSpan, IReadOnlyList<DiagnosticTroubleCode>> read)
        {
            IReadOnlyList<DiagnosticTroubleCode> stored;
            try
            {
                stored = read(ECUDefinition.TCM, OBDIIMode.ShowStoredDiagnosticTroubleCodes, TcuTimeout);
            }
            // A missing transmission (manual car), the AEM wideband on the same address, or an
            // adapter that cannot route 0x7E9 must never invalidate the engine result.
            catch (Exception ex)
            {
                return new DtcModuleResult { StoredError = ex.Message };
            }

            var (pending, pendingError, pendingTimedOut) =
                TryReadTcu(read, OBDIIMode.ShowPendingDiagnosticTroubleCodes);

            // A pending timeout means the module answers some services but goes silent on
            // 0x07 — probing 0x0A too would add another full timeout for no answer. A fast
            // rejection may be service-specific, so 0x0A is still worth one attempt.
            IReadOnlyList<DiagnosticTroubleCode> permanent = [];
            string? permanentError = null;
            if (pendingTimedOut)
            {
                permanentError = "Service 0x0A was not requested because service 0x07 timed out.";
            }
            else
            {
                (permanent, permanentError, _) = TryReadTcu(read, OBDIIMode.PermanentDiagnosticTroubleCodes);
            }

            return new DtcModuleResult
            {
                Stored = stored,
                Pending = pending,
                Permanent = permanent,
                PendingError = pendingError,
                PermanentError = permanentError,
            };
        }

        private static (IReadOnlyList<DiagnosticTroubleCode> codes, string? error, bool noAnswer) TryReadTcu(
            Func<ECUDefinition, OBDIIMode, TimeSpan, IReadOnlyList<DiagnosticTroubleCode>> read,
            OBDIIMode mode)
        {
            try
            {
                return (read(ECUDefinition.TCM, mode, TcuTimeout), null, false);
            }
            catch (DtcNoAnswerException ex)
            {
                return ([], ex.Message, true);
            }
            catch (Exception ex)
            {
                return ([], ex.Message, false);
            }
        }
    }
}
