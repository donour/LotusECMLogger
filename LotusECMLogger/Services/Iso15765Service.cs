using SAE.J2534;
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
        ResetLearnedValues = 0x11
    }


    internal class Iso15765Service
    {
        private readonly static byte[] ECM_HEADER = [0x00, 0x00, 0x07, 0xE0];

        private J2534Channel _channel;

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
        /// Service 0x04: clears emissions-related diagnostic information — stored and pending
        /// DTCs, freeze frame data, readiness monitor results and related stored values.
        /// Waits for the ECU's positive response (0x44) so callers know the clear was accepted.
        /// </summary>
        public (bool success, string errorMessage) ClearDiagnosticInformation()
        {
            byte[] request = new byte[ECM_HEADER.Length + 1];
            Array.Copy(ECM_HEADER, request, ECM_HEADER.Length);
            request[ECM_HEADER.Length] = (byte)OBDIIMode.ClearDiagnosticTroubleCodesAndStoredValues;
            _channel.SendMessage(request);

            for (int i = 0; i < 10; i++)
            {
                var response = _channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                var data = response.Messages[0].Data;
                // Skip echoes of our own transmit and TX confirmation frames.
                if (data.Length < 5 || data[2] != 0x07 || data[3] != 0xE8)
                    continue;

                // Positive response: 0x44 (= 0x04 | 0x40).
                if (data[4] == 0x44)
                    return (true, "");

                // Negative response: 0x7F 0x04 <NRC>
                if (data.Length >= 7 && data[4] == 0x7F &&
                    data[5] == (byte)OBDIIMode.ClearDiagnosticTroubleCodesAndStoredValues)
                    return (false, $"ECU rejected the clear request (NRC 0x{data[6]:X2}). Try with the ignition on and the engine off.");
            }

            return (false, "No response from ECU for the clear request.");
        }

        /// <summary>
        /// Reads diagnostic trouble codes for a DTC service: 0x03 (stored/confirmed),
        /// 0x07 (pending), or 0x0A (permanent). Sends the single-byte service request and
        /// parses the positive response into individual codes. Returns an empty list when the
        /// ECU responds with no codes.
        /// </summary>
        /// <exception cref="IOException">Thrown when the ECU does not respond.</exception>
        public List<DiagnosticTroubleCode> ReadDtcs(OBDIIMode mode)
        {
            byte[] request = new byte[ECM_HEADER.Length + 1];
            Array.Copy(ECM_HEADER, request, ECM_HEADER.Length);
            request[ECM_HEADER.Length] = (byte)mode;
            _channel.SendMessage(request);

            byte expectedResponse = (byte)(0x40 | (byte)mode);

            for (int retry = 0; retry < 10; retry++)
            {
                var response = _channel.ReadMessages(1, 250);
                if (response.Messages.Length == 0)
                    continue;

                // Response from ECM: [0x00, 0x00, 0x07, 0xE8] <SID|0x40> <payload...>
                var data = response.Messages[0].Data;
                if (data.Length < 5 || data[2] != 0x07 || data[3] != 0xE8 || data[4] != expectedResponse)
                    continue;

                return ParseDtcResponse(data);
            }

            throw new IOException("No response from ECU for DTC request.");
        }

        // Parses the DTC payload that follows the 4-byte header and the service response byte.
        // ISO 15765-4 (CAN) prefixes the codes with a one-byte DTC count; the payload after the
        // SID is therefore odd-length when that count is present. Each code is two bytes, and a
        // 0x0000 pair is padding/"no code" and skipped.
        private static List<DiagnosticTroubleCode> ParseDtcResponse(byte[] data)
        {
            var codes = new List<DiagnosticTroubleCode>();

            int payloadStart = 5; // after the 4-byte header and the SID at data[4]
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

                for(int i=0; i<100; i++) // only wait for 100 messages
                {
                    // Get response with timeout
                    var response = _channel.ReadMessages(1, 1000);

                    var messages = response.Messages;
                    // TODO check for specific response message
                    if (messages.Length > 0 && messages[0].Data.Length > 4)
                    {
                        return ParseSupportedPIDsResponse(response.Messages[0].Data, mode);
                    }
                }
                return [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying supported PIDs: {ex.Message}");
                return new List<int>();
            }
        }

        private static byte[] BuildModeMessage(OBDIIMode mode, byte pid)
        {
            // Use Lotus ECM header
            var message = new byte[ECM_HEADER.Length + 3];
            Array.Copy(ECM_HEADER, message, ECM_HEADER.Length);
            message[ECM_HEADER.Length] = (byte)mode;
            message[ECM_HEADER.Length + 1] = pid;
            message[ECM_HEADER.Length + 2] = 0x00;
            return message;
        }

        private static byte[] BuildMultiPIDMessage(OBDIIMode mode, List<int> pids)
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

        private static List<int> ParseSupportedPIDsResponse(byte[] data, OBDIIMode mode)
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
}
