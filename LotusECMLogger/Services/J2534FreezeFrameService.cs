using SAE.J2534;

namespace LotusECMLogger.Services
{
    public sealed class J2534FreezeFrameService : IFreezeFrameService
    {
        // NRCs some firmwares return for PID 0x02 instead of a zeroed DTC when no frame
        // is stored: 0x12 subFunctionNotSupported, 0x31 requestOutOfRange.
        private static readonly byte[] NoFrameStoredNrcs = [0x12, 0x31];

        public (bool success, string errorMessage, FreezeFrameResult result) ReadFreezeFrame(byte frame = 0x00)
        {
            try
            {
                using var session = J2534Session.Open();
                J2534Channel channel = session.OpenIso15765();
                channel.StartMessageFilter(ECUDefinition.ECM.CreateFlowControlFilter()).ThrowIfError();

                var iso = new Iso15765Service(channel);

                // PID 0x02 first: the DTC that captured the frame. A zeroed code means
                // nothing is stored, so there is no point querying the data PIDs.
                var (dtcResponse, nrc) = iso.ReadFreezeFrameRaw(0x02, frame);
                if (dtcResponse == null)
                {
                    if (nrc is byte code && NoFrameStoredNrcs.Contains(code))
                        return (true, "", new FreezeFrameResult { FrameStored = false });
                    return nrc is byte rejected
                        ? (false, $"ECU rejected the freeze frame request (NRC 0x{rejected:X2}).", new FreezeFrameResult())
                        : (false, "No response from ECU for the freeze frame request.", new FreezeFrameResult());
                }

                var triggeringDtc = FreezeFrameDecoder.ParseTriggeringDtc(dtcResponse);
                if (triggeringDtc == null)
                    return (true, "", new FreezeFrameResult { FrameStored = false });

                var warnings = new List<string>();
                var entries = new List<FreezeFrameEntry>();

                foreach (byte pid in QuerySupportedPids(iso, frame, warnings))
                {
                    var (response, pidNrc) = iso.ReadFreezeFrameRaw(pid, frame);
                    if (response == null)
                    {
                        warnings.Add(pidNrc is byte c
                            ? $"PID 0x{pid:X2}: NRC 0x{c:X2}"
                            : $"PID 0x{pid:X2}: no response");
                        continue;
                    }
                    entries.AddRange(FreezeFrameDecoder.DecodePidResponse(response));
                }

                return (true, "", new FreezeFrameResult
                {
                    FrameStored = true,
                    TriggeringDtc = triggeringDtc,
                    Entries = entries,
                    Warnings = warnings,
                });
            }
            catch (Exception ex)
            {
                return (false, ex.Message, new FreezeFrameResult());
            }
        }

        /// <summary>
        /// Walks the supported-PID bitmask pages (PID 0x00, then 0x20/0x40/... while each
        /// page flags the next) to find the data PIDs present in the frame. Failure here
        /// degrades to a warning: the triggering DTC alone is still worth showing.
        /// </summary>
        private static List<byte> QuerySupportedPids(Iso15765Service iso, byte frame, List<string> warnings)
        {
            var supported = new List<int>();

            for (int basePid = 0x00; basePid <= 0xE0; basePid += 0x20)
            {
                var (page, nrc) = iso.ReadFreezeFrameRaw((byte)basePid, frame);
                if (page == null)
                {
                    if (basePid == 0x00)
                    {
                        warnings.Add(nrc is byte c
                            ? $"supported-PID query failed (NRC 0x{c:X2})"
                            : "supported-PID query failed (no response)");
                    }
                    break;
                }

                var pagePids = FreezeFrameDecoder.ParseSupportedPids(page, basePid);
                supported.AddRange(pagePids);
                if (!pagePids.Contains(basePid + 0x20))
                    break;
            }

            // 0x02 was read separately; multiples of 0x20 only select the next bitmask page.
            return supported
                .Where(pid => pid != 0x02 && pid % 0x20 != 0)
                .Select(pid => (byte)pid)
                .ToList();
        }
    }
}
