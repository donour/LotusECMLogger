using LotusECMLogger.Services.Links;
using System.Text;

namespace LotusECMLogger.Services
{
    public enum OBDIIMode
    {
        ShowCurrentData = 0x01,
        ShowFreezeFrameData = 0x02,
        ShowStoredDiagnosticTroubleCodes = 0x03,
        ClearDiagnosticTroubleCodesAndStoredValues = 0x04,
        TestResultsOxygenSensors = 0x05,
        TestResultsOtherComponents = 0x06,
        ShowPendingDiagnosticTroubleCodes = 0x07,
        ControlOperationOfOnBoardSystems = 0x08,
        RequestVehicleInformation = 0x09,
        PermanentDiagnosticTroubleCodes = 0x0A,
        ResetLearnedValues = 0x11,

        /// <summary>
        /// Lotus / EFI Technology proprietary service: returns the current, confirmed and TPMS
        /// DTC sets as one flat list. Not part of SAE J1979 — see T6-mode13-programming.md.
        /// </summary>
        ReadAllDiagnosticTroubleCodes = 0x13
    }

    /// <summary>
    /// OBD-II and Lotus services on an <see cref="IDiagnosticLink"/> to the ECM. Requests are built
    /// without the CAN ID header (the link adds it); replies arrive with it, as the J2534 ISO 15765
    /// channel delivers them, so the decoders index them from byte 4.
    /// </summary>
    internal class Iso15765Service
    {
        private readonly IDiagnosticLink _link;

        public Iso15765Service(IDiagnosticLink link)
        {
            _link = link ?? throw new ArgumentNullException(nameof(link));
        }

        // Mode 0x3B (Lotus VIN write). Each sub-function writes a chunk of the VIN
        // into a staging buffer; the firmware commits the new VIN to EEPROM only
        // after all four chunks have been received. Positions 0-2 (the WMI) are
        // not writable — only positions 3-16 are sent here.
        // Layout per sub-function:  [sub_func, vin[start], vin[start+1], ...]
        private static readonly (byte subFunc, int start, int count)[] VinChunks =
        [
            (0x01, 3, 4),
            (0x02, 7, 4),
            (0x03, 11, 4),
            (0x04, 15, 2),
        ];

        public (bool success, string errorMessage) SetVin(string vin)
        {
            if (string.IsNullOrEmpty(vin) || vin.Length != 17)
                return (false, "VIN must be exactly 17 characters.");

            var vinBytes = Encoding.ASCII.GetBytes(vin);

            foreach (var (subFunc, start, count) in VinChunks)
            {
                var (ok, err) = SendVinChunk(subFunc, vinBytes, start, count);
                if (!ok)
                    return (false, err);
            }

            // Mode 0x3B sends a positive 0x7B response unconditionally, even when the
            // engine is running and the firmware silently discards every byte. Read the
            // VIN back via Mode 09 PID 02 and compare the writable range (positions 3-16)
            // to detect that case. Positions 0-2 (WMI) are firmware-locked and ignored
            // by the comparison.
            var readback = GetPID(OBDIIMode.RequestVehicleInformation, 0x02);
            if (readback.Length != 17)
                return (false, "VIN write succeeded on the wire, but read-back returned an unexpected length. Verify with Load Vehicle Data.");

            for (int i = 3; i < 17; i++)
            {
                if (readback[i] != vinBytes[i])
                {
                    var actual = Encoding.ASCII.GetString(readback);
                    return (false,
                        $"ECU acknowledged the write but the VIN did not change. " +
                        $"Read back: '{actual}'. The engine must be off — Mode 0x3B silently ignores writes while running.");
                }
            }

            return (true, "");
        }

        private (bool ok, string error) SendVinChunk(byte subFunc, byte[] vinBytes, int start, int count)
        {
            // Request: 0x3B + sub_func + <count> VIN bytes. The positive reply (0x7B) echoes the sub-function.
            byte[] request = [0x3B, subFunc, .. vinBytes.AsSpan(start, count)];

            var reply = _link.Exchange(request, echoLength: 1);
            if (reply.IsPositive)
                return (true, "");
            if (reply.Nrc is byte nrc)
                return (false, $"ECU rejected sub-function 0x{subFunc:X2} (NRC 0x{nrc:X2}). Is the engine off?");
            return (false, $"No response from ECU for VIN sub-function 0x{subFunc:X2}.");
        }

        public void SendLearningDataClear()
        {
            _link.Send([(byte)OBDIIMode.ResetLearnedValues]);
        }

        /// <summary>
        /// Service 0x04: asks the link's ECU to clear emissions-related diagnostic information —
        /// stored and pending DTCs, freeze frame data, readiness monitor results and related
        /// stored values. Waits for its positive response (0x44) so callers know the clear was
        /// accepted. The engine (ECM) and transmission (TCM) are separate links.
        /// </summary>
        public (bool success, string errorMessage) ClearDiagnosticInformation()
        {
            var ecu = _link.Ecu;
            var reply = _link.Exchange([(byte)OBDIIMode.ClearDiagnosticTroubleCodesAndStoredValues]);
            if (reply.IsPositive)
                return (true, "");
            if (reply.Nrc is byte nrc)
                return (false, $"{ecu.Name} rejected the clear request (NRC 0x{nrc:X2}). Try with the ignition on and the engine off.");
            return (false, $"No response from {ecu.Name} for the clear request at 0x{ecu.RequestId:X3}.");
        }

        /// <summary>
        /// Reads diagnostic trouble codes for a DTC service: 0x03 (stored/confirmed),
        /// 0x07 (pending), or 0x0A (permanent), from the link's ECU. Sends the single-byte
        /// service request and parses the positive response into individual codes. Returns an
        /// empty list when the ECU responds with no codes. The link only accepts replies from its
        /// own response ID with the matching service, which is what rejects the other module's
        /// codes and the AEM wideband's Mode 01 traffic on the shared 0x7E9.
        /// </summary>
        /// <param name="timeoutMs">
        /// Wait for the answer; a response-pending (NRC 0x78) extends it inside the link.
        /// </param>
        /// <exception cref="DtcNoAnswerException">Thrown when the ECU stays silent.</exception>
        /// <exception cref="IOException">Thrown when the ECU rejects the request.</exception>
        public List<DiagnosticTroubleCode> ReadDtcs(OBDIIMode mode,
            int timeoutMs = DiagnosticTiming.DefaultReplyTimeoutMs)
        {
            var ecu = _link.Ecu;
            var reply = _link.Exchange([(byte)mode], timeoutMs: timeoutMs);
            if (reply.Message is byte[] data)
                return ParseDtcResponse(data);
            if (reply.Nrc is byte nrc)
                throw new IOException($"{ecu.Name} rejected service 0x{(byte)mode:X2} (NRC 0x{nrc:X2}).");
            throw new DtcNoAnswerException(
                $"No response from {ecu.Name} for service 0x{(byte)mode:X2} at 0x{ecu.RequestId:X3}.");
        }

        // Parses the DTC payload that follows the 4-byte header and the service response byte.
        // ISO 15765-4 (CAN) prefixes the codes with a one-byte DTC count; the payload after the
        // SID is therefore odd-length when that count is present. Each code is two bytes, and a
        // 0x0000 pair is padding/"no code" and skipped.
        internal static List<DiagnosticTroubleCode> ParseDtcResponse(byte[] data)
        {
            var codes = new List<DiagnosticTroubleCode>();

            const int payloadStart = 5; // after the 4-byte header and the SID at data[4]
            int payloadLength = data.Length - payloadStart;
            if (payloadLength <= 0)
                return codes;

            // Odd payload => leading DTC-count byte present; skip it so we land on a code boundary.
            int pairStart = (payloadLength % 2 == 1) ? payloadStart + 1 : payloadStart;

            for (int i = pairStart; i + 1 < data.Length; i += 2)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00)
                    continue;
                codes.Add(DiagnosticTroubleCode.FromBytes(data[i], data[i + 1]));
            }

            return codes;
        }

        /// <summary>
        /// Service 0x13 (Lotus proprietary): asks the ECU for its current, confirmed and TPMS
        /// codes as a single flat list, and returns the raw positive response buffer
        /// ([hdr4] 0x53 &lt;code pairs...&gt;). Responses longer than seven payload bytes arrive
        /// reassembled: the J2534 ISO15765 layer sends the flow-control frame the transfer
        /// stalls without.
        /// </summary>
        /// <param name="form">Which of the two accepted request forms to send.</param>
        /// <exception cref="IOException">The ECU rejected the request or did not answer.</exception>
        public byte[] ReadAllDtcsMode13(Mode13RequestForm form = Mode13RequestForm.ReportAll)
        {
            // On a TPMS-equipped car the ECU interrogates the TPMS module before answering,
            // which can push the first response frame out to ~40 ms — past the 50 ms OBD-II
            // default, hence the default (generous) reply window here.
            var reply = _link.Exchange(BuildMode13Request(form));
            if (reply.Message is byte[] data)
                return data;

            // Negative response: 0x7F 0x13 <NRC> — firmware without the service.
            if (reply.Nrc is byte nrc)
                throw new IOException($"ECU rejected the Mode 0x13 request (NRC 0x{nrc:X2}).");

            throw new IOException("No response from ECU for the Mode 0x13 request.");
        }

        // Form A is the bare service byte; Form B adds the 0xFF00 "report all" sub-function.
        // Both fit in one frame, which matters: the firmware reads the eight receive-buffer
        // bytes directly and reassembles no multi-frame request.
        internal static byte[] BuildMode13Request(Mode13RequestForm form) =>
            form == Mode13RequestForm.BareService
                ? [(byte)OBDIIMode.ReadAllDiagnosticTroubleCodes]
                : [(byte)OBDIIMode.ReadAllDiagnosticTroubleCodes, 0xFF, 0x00];

        /// <summary>
        /// Service 0x02: requests one freeze frame PID. Returns the full raw response buffer
        /// on success, or the negative response code when the ECU rejects the request; both
        /// are null when the ECU does not answer.
        /// </summary>
        public (byte[]? response, byte? nrc) ReadFreezeFrameRaw(byte pid, byte frame = 0x00)
        {
            // Positive response: 0x42 <pid> <frame> <data...>
            var reply = _link.Exchange(BuildModeMessage(OBDIIMode.ShowFreezeFrameData, pid, frame), echoLength: 2);
            return (reply.Message, reply.Nrc);
        }

        /// <summary>
        /// Service 0x01: requests one current-data PID. Returns the full raw response buffer
        /// ([hdr4] 0x41 &lt;pid&gt; &lt;data...&gt;) on success, or the negative response code when
        /// the ECU rejects the request; both are null when the ECU does not answer.
        /// </summary>
        public (byte[]? response, byte? nrc) ReadCurrentDataRaw(byte pid)
        {
            // A single-PID request: BuildModeMessage's trailing byte would ask for PID 0x00 too.
            var reply = _link.Exchange(BuildMultiPIDMessage(OBDIIMode.ShowCurrentData, [pid]), echoLength: 1);
            return (reply.Message, reply.Nrc);
        }

        public List<int> GetSupportedPIDs(OBDIIMode mode)
        {
            if (mode == OBDIIMode.RequestVehicleInformation)
            {
                return QuerySupportedPIDs(mode);
            }
            throw new NotImplementedException();
        }

        public byte[] GetPID(OBDIIMode mode, int pid)
        {
            try
            {
                if (mode == OBDIIMode.RequestVehicleInformation)
                {
                    switch (pid)
                    {
                        case 0x02:
                        case 0x04:
                        case 0x06:
                        case 0x0A:
                            var resp = GetMultiMessageRequest(mode, [pid]);
                            if (resp.Length >= 2)
                            {
                                return resp[2..];
                            }
                            else
                            {
                                throw new IOException("Invalid response length");
                            }
                        default:
                            return [];
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying supported PIDs: {ex.Message}");
                return [];
            }

        }

        private List<int> QuerySupportedPIDs(OBDIIMode mode)
        {
            try
            {
                // Request the supported-PIDs bitmask (PID 0x00); the reply echoes the PID.
                var reply = _link.Exchange(BuildModeMessage(mode, 0x00), echoLength: 1);
                return reply.Message is byte[] data ? ParseSupportedPIDsResponse(data, mode) : [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying supported PIDs: {ex.Message}");
                return new List<int>();
            }
        }

        internal static byte[] BuildModeMessage(OBDIIMode mode, byte pid, byte trailing = 0x00) =>
            // The trailing byte is the frame number for Mode 02.
            [(byte)mode, pid, trailing];

        internal static byte[] BuildMultiPIDMessage(OBDIIMode mode, List<int> pids)
        {
            if (pids.Count < 1 || pids.Count > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(pids), "PID list must contain between 1 and 6 PIDs.");
            }

            var message = new byte[1 + pids.Count];
            message[0] = (byte)mode;
            for (int i = 0; i < pids.Count; i++)
            {
                int pid = pids[i];
                if (pid < 0 || pid > 0xFF)
                {
                    throw new ArgumentOutOfRangeException(nameof(pids), "Each PID must be between 0x00 and 0xFF.");
                }
                message[1 + i] = (byte)pid;
            }
            return message;
        }

        internal static List<int> ParseSupportedPIDsResponse(byte[] data, OBDIIMode mode)
        {
            var supportedPIDs = new List<int>();

            // Check minimum length and response header
            if (data.Length < 6 || data[0] != 0x00 || data[1] != 0x00 || data[2] != 0x07 || data[3] != 0xE8)
            {
                return supportedPIDs;
            }

            // Check if this is the correct mode response
            byte expectedResponse = (byte)(0x40 | (byte)mode);
            if (data[4] != expectedResponse)
            {
                return supportedPIDs;
            }

            // Check if this is response to PID 0x00
            if (data[5] != 0x00)
            {
                return supportedPIDs;
            }

            // Parse bitmask starting from data[6]
            // Each byte contains 8 PID support flags (PIDs 1-8, 9-16, etc.)
            for (int i = 6; i < data.Length; i++)
            {
                byte bitmask = data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((bitmask & (1 << (7 - bit))) != 0)
                    {
                        int pid = (i - 6) * 8 + bit + 1;
                        supportedPIDs.Add(pid);
                    }
                }
            }

            return supportedPIDs;
        }

        // Before the link existed, each attempt read up to eleven messages at 250 ms each.
        private const int MultiMessageAttempts = 4;
        private const int MultiMessageReplyTimeoutMs = 2750;

        /// <summary>
        /// Sends a request and returns the positive reply after its SID ([pid, ...data]), retrying
        /// while the ECU is silent. Only the SID is matched, as before the link existed.
        /// </summary>
        /// <exception cref="IOException">The ECU rejected the request or never answered.</exception>
        private byte[] GetMultiMessageRequest(OBDIIMode mode, List<int> pids)
        {
            var request = BuildMultiPIDMessage(mode, pids);
            for (int attempt = 0; attempt < MultiMessageAttempts; attempt++)
            {
                var reply = _link.Exchange(request, echoLength: 0, timeoutMs: MultiMessageReplyTimeoutMs);
                if (reply.Message is byte[] data && data.Length > 5)
                    return data[5..];
                if (reply.Nrc is byte nrc)
                    throw new IOException($"ECU rejected the Mode 0x{(byte)mode:X2} request (NRC 0x{nrc:X2}).");
            }
            throw new IOException("Failed to get multi-message response");
        }
    }

    /// <summary>
    /// Thrown when a module stays silent for a DTC service. Kept distinct from a negative
    /// response so the read plan can skip a second optional probe after a timeout while still
    /// probing after a fast rejection.
    /// </summary>
    internal sealed class DtcNoAnswerException : IOException
    {
        public DtcNoAnswerException(string message) : base(message) { }
    }
}
