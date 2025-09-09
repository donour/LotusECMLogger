using SAE.J2534;

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
        PermanentDiagnosticTroubleCodes = 0x0A
    }


    internal class Iso15765Service
    {
        private readonly static byte[] ECM_HEADER = [0x00, 0x00, 0x07, 0xE0];

        private Channel _channel;

        public Iso15765Service(Channel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
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
                _channel.SendMessages([request]);

                for(int i=0; i<100; i++) // only wait for 100 messages
                {
                    // Get response with timeout
                    var response = _channel.GetMessages(1, 1000);

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

        private static byte[] readMultiFrameResponse(Channel channel, OBDIIMode mode)
        {
            int first_message_retries = 10;
            do
            {
                var first_response = channel.GetMessages(1, 250);
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
        public static byte[] GetMultiMessageRequest(Channel channel, OBDIIMode mode, List<int> pids)
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
