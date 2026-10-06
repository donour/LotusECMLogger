using System.Text;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    public sealed class J2534EmissionsService : IEmissionsService
    {
        private const byte MonitorStatusSinceClearPid = 0x01;
        private const byte ObdStandardPid = 0x1C;
        private const byte MonitorStatusThisCyclePid = 0x41;

        public (bool success, string errorMessage, EmissionsCheckResult result) RunCheck()
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);
                var warnings = new List<string>();

                var (supported, supportError) = QuerySupportedPids(iso);
                if (supportError != null)
                    return (false, supportError, new EmissionsCheckResult());

                // PID 0x01 is the inspection: without it there is nothing to judge.
                if (!supported.Contains(MonitorStatusSinceClearPid))
                    return (false, "The ECU does not support PID 0x01 (monitor status).", new EmissionsCheckResult());

                var (statusPayload, statusError) = ReadPid(iso, MonitorStatusSinceClearPid);
                if (statusPayload == null || statusPayload.Length < 4)
                    return (false, $"Monitor status read failed: {statusError ?? "short response"}.", new EmissionsCheckResult());
                var sinceClear = EmissionsDecoder.DecodeMonitorStatusSinceClear(statusPayload);

                MonitorStatus? thisCycle = null;
                if (supported.Contains(MonitorStatusThisCyclePid))
                {
                    var (payload, error) = ReadPid(iso, MonitorStatusThisCyclePid);
                    if (payload is { Length: >= 4 })
                        thisCycle = EmissionsDecoder.DecodeMonitorStatusThisCycle(payload, sinceClear.Ignition);
                    else
                        warnings.Add($"Drive-cycle monitor status (PID 0x41): {error ?? "short response"}");
                }

                byte? obdStandard = null;
                if (supported.Contains(ObdStandardPid))
                {
                    var (payload, error) = ReadPid(iso, ObdStandardPid);
                    if (payload is { Length: >= 1 })
                        obdStandard = payload[0];
                    else
                        warnings.Add($"OBD standard (PID 0x1C): {error ?? "short response"}");
                }

                var counters = new List<EmissionsCounter>();
                foreach (byte pid in EmissionsDecoder.CounterPids.Where(p => supported.Contains(p)))
                {
                    var (payload, error) = ReadPid(iso, pid);
                    if (payload != null && EmissionsDecoder.DecodeCounter(pid, payload) is { } counter)
                        counters.Add(counter);
                    else
                        warnings.Add($"PID 0x{pid:X2}: {error ?? "short response"}");
                }

                var (dtcs, storedError) = ReadDtcs(iso);

                var vinBytes = iso.GetPID(OBDIIMode.RequestVehicleInformation, 0x02);
                string? vin = vinBytes.Length == 17 ? Encoding.ASCII.GetString(vinBytes) : null;
                if (vin == null)
                    warnings.Add("VIN (Mode 09 PID 0x02) unavailable");

                // The software identity BAR's "Modified Software" check compares.
                var calIds = EmissionsDecoder.ParseCalibrationIds(iso.GetPID(OBDIIMode.RequestVehicleInformation, 0x04));
                if (calIds.Count == 0)
                    warnings.Add("Calibration ID (Mode 09 PID 0x04) unavailable");
                var cvns = EmissionsDecoder.ParseCvns(iso.GetPID(OBDIIMode.RequestVehicleInformation, 0x06));
                if (cvns.Count == 0)
                    warnings.Add("CVN (Mode 09 PID 0x06) unavailable");

                return (true, "", new EmissionsCheckResult
                {
                    SinceClear = sinceClear,
                    ThisDriveCycle = thisCycle,
                    ObdStandard = obdStandard,
                    Vin = vin,
                    CalibrationIds = calIds,
                    Cvns = cvns,
                    Counters = counters,
                    Dtcs = dtcs,
                    StoredDtcError = storedError,
                    Warnings = warnings,
                });
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new EmissionsCheckResult());
            }
        }

        /// <summary>
        /// Reads one Mode 01 PID and returns its data bytes, or null with a reason when the ECU
        /// rejected the request or did not answer.
        /// </summary>
        private static (byte[]? payload, string? error) ReadPid(Iso15765Service iso, byte pid)
        {
            var (response, nrc) = iso.ReadCurrentDataRaw(pid);
            if (response == null)
                return (null, nrc is byte code ? $"NRC 0x{code:X2}" : "no response");
            return (EmissionsDecoder.Payload(response, pid).ToArray(), null);
        }

        /// <summary>
        /// Walks the supported-PID bitmask pages (PID 0x00, then 0x20/0x40/... while each page
        /// flags the next). Only the first page is required: without it the ECU is not talking.
        /// </summary>
        private static (HashSet<int> supported, string? error) QuerySupportedPids(Iso15765Service iso)
        {
            var supported = new HashSet<int>();

            for (int basePid = 0x00; basePid <= 0xE0; basePid += 0x20)
            {
                var (page, nrc) = iso.ReadCurrentDataRaw((byte)basePid);
                if (page == null)
                {
                    if (basePid == 0x00)
                    {
                        return (supported, nrc is byte code
                            ? $"ECU rejected the supported-PID request (NRC 0x{code:X2})."
                            : "No response from ECU. Check the ignition is on.");
                    }
                    break;
                }

                var pagePids = EmissionsDecoder.ParseSupportedPids(page, basePid);
                supported.UnionWith(pagePids);
                if (!pagePids.Contains(basePid + 0x20))
                    break;
            }

            return (supported, null);
        }

        // Each DTC service degrades to a note: the monitor status already carries the MIL state
        // and confirmed-code count, so a missing list does not invalidate the check.
        private static (DtcReadResult dtcs, string? storedError) ReadDtcs(Iso15765Service iso)
        {
            (IReadOnlyList<DiagnosticTroubleCode> codes, string? error) Read(OBDIIMode mode)
            {
                try
                {
                    return (iso.ReadDtcs(mode), null);
                }
                catch (IOException ex)
                {
                    return ([], ex.Message);
                }
            }

            var (stored, storedError) = Read(OBDIIMode.ShowStoredDiagnosticTroubleCodes);
            var (pending, pendingError) = Read(OBDIIMode.ShowPendingDiagnosticTroubleCodes);
            var (permanent, permanentError) = Read(OBDIIMode.PermanentDiagnosticTroubleCodes);

            return (new DtcReadResult
            {
                Stored = stored,
                Pending = pending,
                Permanent = permanent,
                PendingError = pendingError,
                PermanentError = permanentError,
            }, storedError);
        }
    }
}
