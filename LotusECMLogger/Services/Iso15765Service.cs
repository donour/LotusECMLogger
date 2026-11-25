using SAE.J2534;
using System.Diagnostics;
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


    // Every method here addresses the engine ECU (0x7E0/0x7E8) except ReadDtcs and
    // ClearDiagnosticInformation, which take the target ECU as an argument. Do not turn the
    // ECU into a constructor parameter: the static builders and hard-coded 0x7E8 checks in
    // the rest of the class would silently stay on the engine, so a TCM instance would send
    // an engine-addressed clear.
    internal class Iso15765Service
    {
        private readonly static byte[] ECM_HEADER = [0x00, 0x00, 0x07, 0xE0];

        /// <summary>
        /// The budget the old ten-attempt ECM read loop used (10 × 250 ms). Kept as the default
        /// so the engine read and the emissions pre-check take no longer than they did.
        /// </summary>
        internal static readonly TimeSpan DefaultDtcTimeout = TimeSpan.FromMilliseconds(2500);

        // responsePending (NRC 0x78) means "still working": ISO 14229 allows the tester to wait
        // P2* (5 s) for the final reply instead of re-sending. The cap stops a module that
        // answers 0x78 forever from recreating the hang the timeout exists to prevent.
        private const byte NrcResponsePending = 0x78;
        internal static readonly TimeSpan ResponsePendingExtension = TimeSpan.FromSeconds(5);

        private readonly J2534Channel _channel;

        public Iso15765Service(J2534Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
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
            // Request: ECM_HEADER + 0x3B + sub_func + <count> VIN bytes
            var request = new byte[ECM_HEADER.Length + 2 + count];
            Array.Copy(ECM_HEADER, request, ECM_HEADER.Length);
            request[ECM_HEADER.Length] = 0x3B;
            request[ECM_HEADER.Length + 1] = subFunc;
            Array.Copy(vinBytes, start, request, ECM_HEADER.Length + 2, count);

            _channel.SendMessage(request);

            for (int i = 0; i < 10; i++)
            {
                var response = _channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                var data = response.Messages[0].Data;
                // Skip echoes of our own transmit and TX confirmation frames.
                if (data.Length < 6 || data[2] != 0x07 || data[3] != 0xE8)
                    continue;

                // Positive response: 0x7B (= 0x3B | 0x40) followed by sub-function echo.
                if (data[4] == 0x7B && data[5] == subFunc)
                    return (true, "");

                // Negative response: 0x7F 0x3B <NRC>
                if (data.Length >= 7 && data[4] == 0x7F && data[5] == 0x3B)
                    return (false, $"ECU rejected sub-function 0x{subFunc:X2} (NRC 0x{data[6]:X2}). Is the engine off?");
            }

            return (false, $"No response from ECU for VIN sub-function 0x{subFunc:X2}.");
        }

        public void SendLearningDataClear()
        {
            byte[] request = new byte[ECM_HEADER.Length + 1];
            Array.Copy(ECM_HEADER, request, ECM_HEADER.Length);
            request[ECM_HEADER.Length] = (byte)OBDIIMode.ResetLearnedValues;
            _channel.SendMessage(request);
        }

        /// <summary>
        /// Service 0x04: asks one ECU to clear emissions-related diagnostic information — stored
        /// and pending DTCs, freeze frame data, readiness monitor results and related stored
        /// values. Waits for that ECU's positive response (0x44) so callers know the clear was
        /// accepted. The engine (ECM) and transmission (TCM) are separate targets.
        /// </summary>
        public (bool success, string errorMessage) ClearDiagnosticInformation(
            ECUDefinition ecu, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            TimeSpan effectiveTimeout = timeout ?? DefaultDtcTimeout;

            try
            {
                var mode = OBDIIMode.ClearDiagnosticTroubleCodesAndStoredValues;
                _channel.SendMessage(BuildDtcRequest(ecu, mode)).ThrowIfError();

                var clock = Stopwatch.StartNew();
                long deadlineMs = (long)effectiveTimeout.TotalMilliseconds;
                long capMs = deadlineMs + (long)ResponsePendingExtension.TotalMilliseconds;

                while (clock.ElapsedMilliseconds < deadlineMs)
                {
                    byte[]? data = ReadOneFrame();
                    if (data == null)
                        continue;

                    switch (ClassifyDtcReply(ecu, mode, data, out byte nrc))
                    {
                        case DtcReplyKind.Positive:
                            // Positive response: 0x44 (= 0x04 | 0x40).
                            return (true, "");

                        case DtcReplyKind.Negative:
                            return (false,
                                $"{ecu.Name} rejected the clear request (NRC 0x{nrc:X2}). Try with the ignition on and the engine off.");

                        case DtcReplyKind.Pending:
                            deadlineMs = Math.Min(
                                clock.ElapsedMilliseconds + (long)ResponsePendingExtension.TotalMilliseconds,
                                capMs);
                            break;
                    }
                }

                return (false, $"{ecu.Name} did not answer the clear request at 0x{ecu.RequestId:X3}.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Reads diagnostic trouble codes for a DTC service: 0x03 (stored/confirmed),
        /// 0x07 (pending), or 0x0A (permanent), from <paramref name="ecu"/>. Sends the
        /// single-byte service request and parses the positive response into individual
        /// codes. Returns an empty list when the ECU responds with no codes.
        /// </summary>
        /// <param name="ecu">
        /// Target module. <see cref="ECUDefinition.ECM"/> is the engine (0x7E0/0x7E8);
        /// <see cref="ECUDefinition.TCM"/> is the standard second OBD ECU slot (0x7E1/0x7E9),
        /// which is where many manufacturers put the transmission control module.
        /// </param>
        /// <param name="timeout">
        /// Wall-clock budget for the answer. Defaults to <see cref="DefaultDtcTimeout"/>; a
        /// response-pending (NRC 0x78) may extend it by up to <see cref="ResponsePendingExtension"/>.
        /// </param>
        /// <exception cref="DtcNoAnswerException">Thrown when the ECU stays silent.</exception>
        /// <exception cref="IOException">Thrown when the ECU rejects the request.</exception>
        public List<DiagnosticTroubleCode> ReadDtcs(OBDIIMode mode, ECUDefinition ecu, TimeSpan? timeout = null)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            return ReadDtcsCore(
                ecu,
                mode,
                timeout ?? DefaultDtcTimeout,
                request => _channel.SendMessage(request).ThrowIfError(),
                ReadOneFrame);
        }

        /// <summary>
        /// Reads one message, treating "nothing arrived" as silence and any other adapter
        /// failure as a real error. J2534 reports TIMEOUT whenever fewer messages than
        /// requested arrived, so the message list is consumed before the status is judged.
        /// </summary>
        private byte[]? ReadOneFrame()
        {
            GetMessagesResult response = _channel.ReadMessages(1, 250);
            if (!response.IsSuccess && !response.IsTimeout && !response.IsBufferEmpty &&
                response.Status != ResultCode.BUFFER_OVERFLOW)
            {
                throw new J2534Exception(response.Status,
                    $"J2534 read failed while reading trouble codes: {response.Status}");
            }
            return response.Messages.Length == 0 ? null : response.Messages[0].Data;
        }

        internal static byte[] BuildDtcRequest(ECUDefinition ecu, OBDIIMode mode)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            return [.. ecu.GetRequestHeader(), (byte)mode];
        }

        internal enum DtcReplyKind
        {
            /// <summary>An echo, a reply from another module, or an unrelated service.</summary>
            Unrelated,
            Positive,
            Negative,
            /// <summary>NRC 0x78: the module is still working and will answer later.</summary>
            Pending,
        }

        /// <summary>
        /// Classifies one frame for a DTC request: only the targeted ECU's response ID and the
        /// echoed service count. The SID check is what rejects the AEM wideband's Mode 01
        /// traffic on the shared 0x7E9, and <see cref="ECUDefinition.MatchesResponse"/> rejects
        /// the other module's codes.
        /// </summary>
        internal static DtcReplyKind ClassifyDtcReply(
            ECUDefinition ecu, OBDIIMode mode, byte[] data, out byte nrc)
        {
            nrc = 0;
            if (data.Length < 5 || !ecu.MatchesResponse(data))
                return DtcReplyKind.Unrelated;

            if (data[4] == (byte)(0x40 | (byte)mode))
                return DtcReplyKind.Positive;

            if (data.Length >= 7 && data[4] == 0x7F && data[5] == (byte)mode)
            {
                nrc = data[6];
                return nrc == NrcResponsePending ? DtcReplyKind.Pending : DtcReplyKind.Negative;
            }

            return DtcReplyKind.Unrelated;
        }

        /// <summary>
        /// The read loop behind <see cref="ReadDtcs"/>, with the transport injected so request
        /// framing, response classification, NRC handling and the 0x78 deadline can be tested
        /// without J2534 hardware. A poll that returns null means nothing arrived.
        /// </summary>
        internal static List<DiagnosticTroubleCode> ReadDtcsCore(
            ECUDefinition ecu,
            OBDIIMode mode,
            TimeSpan timeout,
            Action<byte[]> send,
            Func<byte[]?> read)
        {
            ArgumentNullException.ThrowIfNull(ecu);
            send(BuildDtcRequest(ecu, mode));

            var clock = Stopwatch.StartNew();
            long deadlineMs = (long)timeout.TotalMilliseconds;
            long capMs = deadlineMs + (long)ResponsePendingExtension.TotalMilliseconds;

            while (clock.ElapsedMilliseconds < deadlineMs)
            {
                byte[]? data = read();
                if (data == null)
                    continue;

                switch (ClassifyDtcReply(ecu, mode, data, out byte nrc))
                {
                    case DtcReplyKind.Positive:
                        return ParseDtcResponse(data);

                    case DtcReplyKind.Negative:
                        throw new IOException(
                            $"{ecu.Name} rejected service 0x{(byte)mode:X2} (NRC 0x{nrc:X2}).");

                    case DtcReplyKind.Pending:
                        // Keep listening in the same window rather than re-sending to a busy
                        // module. The cap is measured from the start of the original budget.
                        deadlineMs = Math.Min(
                            clock.ElapsedMilliseconds + (long)ResponsePendingExtension.TotalMilliseconds,
                            capMs);
                        break;
                }
            }

            throw new DtcNoAnswerException(
                $"{ecu.Name} did not answer service 0x{(byte)mode:X2} at 0x{ecu.RequestId:X3}.");
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
            _channel.SendMessage(BuildMode13Request(form));

            // On a TPMS-equipped car the ECU interrogates the TPMS module before answering,
            // which can push the first response frame out to ~40 ms — past the 50 ms OBD-II
            // default, hence the deliberately generous read window here.
            for (int retry = 0; retry < 10; retry++)
            {
                var response = _channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                // Skip echoes of our own transmit, TX confirmations and first-frame indications.
                var data = response.Messages[0].Data;
                if (data.Length < 5 || data[2] != 0x07 || data[3] != 0xE8)
                    continue;

                if (data[4] == Mode13Decoder.PositiveSid)
                    return data;

                // Negative response: 0x7F 0x13 <NRC> — firmware without the service.
                if (data.Length >= 7 && data[4] == 0x7F &&
                    data[5] == (byte)OBDIIMode.ReadAllDiagnosticTroubleCodes)
                    throw new IOException($"ECU rejected the Mode 0x13 request (NRC 0x{data[6]:X2}).");
            }

            throw new IOException("No response from ECU for the Mode 0x13 request.");
        }

        // Form A is the bare service byte; Form B adds the 0xFF00 "report all" sub-function.
        // Both fit in one frame, which matters: the firmware reads the eight receive-buffer
        // bytes directly and reassembles no multi-frame request.
        internal static byte[] BuildMode13Request(Mode13RequestForm form)
        {
            byte[] payload = form == Mode13RequestForm.BareService
                ? [(byte)OBDIIMode.ReadAllDiagnosticTroubleCodes]
                : [(byte)OBDIIMode.ReadAllDiagnosticTroubleCodes, 0xFF, 0x00];

            var request = new byte[ECM_HEADER.Length + payload.Length];
            Array.Copy(ECM_HEADER, request, ECM_HEADER.Length);
            Array.Copy(payload, 0, request, ECM_HEADER.Length, payload.Length);
            return request;
        }

        /// <summary>
        /// Service 0x02: requests one freeze frame PID. Returns the full raw response buffer
        /// on success, or the negative response code when the ECU rejects the request; both
        /// are null when the ECU does not answer.
        /// </summary>
        public (byte[]? response, byte? nrc) ReadFreezeFrameRaw(byte pid, byte frame = 0x00)
        {
            _channel.SendMessage(BuildModeMessage(OBDIIMode.ShowFreezeFrameData, pid, frame));

            for (int retry = 0; retry < 10; retry++)
            {
                var response = _channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                // Skip echoes of our own transmit and TX confirmation frames.
                var data = response.Messages[0].Data;
                if (data.Length < 7 || data[2] != 0x07 || data[3] != 0xE8)
                    continue;

                // Positive response: 0x42 <pid> <frame> <data...>
                if (data[4] == 0x42 && data[5] == pid && data[6] == frame)
                    return (data, null);

                // Negative response: 0x7F 0x02 <NRC>
                if (data[4] == 0x7F && data[5] == (byte)OBDIIMode.ShowFreezeFrameData)
                    return (null, data[6]);
            }

            return (null, null);
        }

        /// <summary>
        /// Service 0x01: requests one current-data PID. Returns the full raw response buffer
        /// ([hdr4] 0x41 &lt;pid&gt; &lt;data...&gt;) on success, or the negative response code when
        /// the ECU rejects the request; both are null when the ECU does not answer.
        /// </summary>
        public (byte[]? response, byte? nrc) ReadCurrentDataRaw(byte pid)
        {
            // A single-PID request: BuildModeMessage's trailing byte would ask for PID 0x00 too.
            _channel.SendMessage(BuildMultiPIDMessage(OBDIIMode.ShowCurrentData, [pid]));

            for (int retry = 0; retry < 10; retry++)
            {
                var response = _channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                // Skip echoes of our own transmit and TX confirmation frames.
                var data = response.Messages[0].Data;
                if (data.Length < 7 || data[2] != 0x07 || data[3] != 0xE8)
                    continue;

                // Positive response: 0x41 <pid> <data...>
                if (data[4] == 0x41 && data[5] == pid)
                    return (data, null);

                // Negative response: 0x7F 0x01 <NRC>
                if (data[4] == 0x7F && data[5] == (byte)OBDIIMode.ShowCurrentData)
                    return (null, data[6]);
            }

            return (null, null);
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
                            var resp = GetMultiMessageRequest(_channel, mode, [pid]);
                            if (resp.Length >= 2)
                            {
                                return resp[2..];                            }
                            else
                            {
                                throw new IOException("Invalid response length");
                            }
                        default:
                            return [];
                    }
                } else
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
                // Send request for supported PIDs (PID 0x00)
                byte[] request = BuildModeMessage(mode, 0x00);
                _channel.SendMessage(request);

                byte expectedResponse = (byte)(0x40 | (byte)mode);

                for (int i = 0; i < 100; i++) // only wait for 100 messages
                {
                    // Get response with timeout
                    var response = _channel.ReadMessages(1, 1000);
                    if (response.Messages.Length == 0)
                        continue;

                    // Skip echoes of our own transmit and TX-confirmation frames; only accept the
                    // ECM's supported-PIDs response ([..0x07,0xE8] <SID|0x40> 0x00 <bitmask...>).
                    // Accepting the first non-empty frame would return an empty list on an echo
                    // and give up before the real response arrives.
                    var data = response.Messages[0].Data;
                    if (data.Length < 6 || data[2] != 0x07 || data[3] != 0xE8 ||
                        data[4] != expectedResponse || data[5] != 0x00)
                        continue;

                    return ParseSupportedPIDsResponse(data, mode);
                }
                return [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying supported PIDs: {ex.Message}");
                return new List<int>();
            }
        }

        internal static byte[] BuildModeMessage(OBDIIMode mode, byte pid, byte trailing = 0x00)
        {
            // Use Lotus ECM header. The trailing byte is the frame number for Mode 02.
            var message = new byte[ECM_HEADER.Length + 3];
            Array.Copy(ECM_HEADER, message, ECM_HEADER.Length);
            message[ECM_HEADER.Length] = (byte)mode;
            message[ECM_HEADER.Length + 1] = pid;
            message[ECM_HEADER.Length + 2] = trailing;
            return message;
        }

        internal static byte[] BuildMultiPIDMessage(OBDIIMode mode, List<int> pids)
        {
            if (pids.Count < 1 || pids.Count > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(pids), "PID list must contain between 1 and 6 PIDs.");
            }

            var message = new byte[ECM_HEADER.Length + 1 + pids.Count];
            Array.Copy(ECM_HEADER, message, ECM_HEADER.Length);
            message[ECM_HEADER.Length] = (byte)mode;
            for (int i = 0; i < pids.Count; i++)
            {
                int pid = pids[i];
                if (pid < 0 || pid > 0xFF)
                {
                    throw new ArgumentOutOfRangeException(nameof(pids), "Each PID must be between 0x00 and 0xFF.");
                }
                message[ECM_HEADER.Length + 1 + i] = (byte)pid;
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

        private static byte[] readMultiFrameResponse(J2534Channel channel, OBDIIMode mode)
        {
            int first_message_retries = 10;
            do
            {
                var first_response = channel.ReadMessages(1, 250);
                if (first_response.Messages.Length > 0)
                {
                    var first_msg = first_response.Messages[0].Data; 
                    // check for header and mode match
                    if (
                        first_msg.Length > 4 &&
                        first_msg[2] == 0x07 &&
                        first_msg[3] == 0xE8 && 
                        first_msg[4] == ((int)mode | 0x40)                       
                        )
                    {
                        return first_msg[5..];
                    }
                }


            } while (first_message_retries-- > 0);
            return [];
        }
        public static byte[] GetMultiMessageRequest(J2534Channel channel, OBDIIMode mode, List<int> pids)
        {
            int retry_count = 3;
            do {
                var request = BuildMultiPIDMessage(mode, pids);
                channel.SendMessage(request);

                var result = readMultiFrameResponse(channel, mode);
                if (result.Length > 0)
                {
                    return result;
                }
            } while (retry_count-- > 0);
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
