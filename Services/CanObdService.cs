
namespace LotusECMLogger.Services
{
    /// <summary>
    /// Concrete implementation of ICanObdService for handling OBD-II requests over CAN using ISO 15765
    /// </summary>
    public class CanObdService : ICanObdService
    {
        /// <summary>
        /// Default ECM header for Lotus vehicles (request header)
        /// </summary>
        private readonly byte[] _defaultRequestHeader = [0x00, 0x00, 0x07, 0xE0];

        /// <summary>
        /// Default ECM response header for Lotus vehicles
        /// </summary>
        private readonly byte[] _defaultResponseHeader = [0x00, 0x00, 0x07, 0xE8];

        /// <summary>
        /// Parse a received CAN message byte array into OBD-II data
        /// Returns a dictionary with parameter names as keys and values as objects
        /// </summary>
        /// <param name="messageData">Raw CAN message data as byte array</param>
        /// <returns>Dictionary of parameter names to their values</returns>
        public Dictionary<string, object> ParseCanMessage(byte[] messageData)
        {
            var result = new Dictionary<string, object>();

            if (messageData == null || messageData.Length == 0)
            {
                return result;
            }

            // Check if this is an ECU response (header validation)
            if (messageData.Length < 4 ||
                messageData[0] != 0x00 || messageData[1] != 0x00 ||
                messageData[2] != 0x07 || messageData[3] != 0xE8)
            {
                result["Error"] = "Invalid ECU response header";
                return result;
            }

            // Extract service and determine response type
            if (messageData.Length >= 5)
            {
                byte service = (byte)(messageData[4] - 0x40); // Response service = request + 0x40
                result["Service"] = service;

                switch (service)
                {
                    case 0x09: // Mode 09 response
                        ParseMode09Response(messageData, result);
                        break;
                    default:
                        result["RawData"] = messageData.Skip(4).ToArray();
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Parse Mode 09 (vehicle info) response data
        /// </summary>
        private void ParseMode09Response(byte[] data, Dictionary<string, object> result)
        {
            if (data.Length >= 6)
            {
                byte infoType = data[5];
                result["InfoType"] = infoType;

                if (infoType == 0x00 && data.Length >= 10) // Supported PIDs
                {
                    uint pidMask = (uint)((data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9]);
                    result["SupportedPIDs"] = pidMask;
                }
            }
        }

        /// <summary>
        /// Construct a CAN message byte array for an OBD-II request using default header
        /// </summary>
        /// <param name="service">OBD-II service/mode (e.g., 0x01, 0x22)</param>
        /// <param name="pid">Parameter ID (can be null for service-level requests)</param>
        /// <param name="additionalData">Additional data bytes for the request</param>
        /// <returns>Complete CAN message byte array ready for transmission</returns>
        public byte[] ConstructCanMessage(byte service, byte? pid = null, params byte[] additionalData)
        {
            return ConstructCanMessage(_defaultRequestHeader, service, pid, additionalData);
        }

        /// <summary>
        /// Construct a CAN message byte array for an OBD-II request with custom header
        /// </summary>
        /// <param name="header">CAN header bytes</param>
        /// <param name="service">OBD-II service/mode (e.g., 0x01, 0x22)</param>
        /// <param name="pid">Parameter ID (can be null for service-level requests)</param>
        /// <param name="additionalData">Additional data bytes for the request</param>
        /// <returns>Complete CAN message byte array ready for transmission</returns>
        public byte[] ConstructCanMessage(byte[] header, byte service, byte? pid = null, params byte[] additionalData)
        {
            if (header == null || header.Length == 0)
            {
                throw new ArgumentException("Header cannot be null or empty", nameof(header));
            }

            var messageList = new List<byte>();
            messageList.AddRange(header);
            messageList.Add(service);

            if (pid.HasValue)
            {
                messageList.Add(pid.Value);
            }

            if (additionalData != null && additionalData.Length > 0)
            {
                messageList.AddRange(additionalData);
            }

            return messageList.ToArray();
        }

        /// <summary>
        /// Get the expected CAN response header for this service
        /// </summary>
        /// <returns>Response header byte array</returns>
        public byte[] GetResponseHeader()
        {
            return (byte[])_defaultResponseHeader.Clone();
        }

        /// <summary>
        /// Get the CAN request header for this service
        /// </summary>
        /// <returns>Request header byte array</returns>
        public byte[] GetRequestHeader()
        {
            return (byte[])_defaultRequestHeader.Clone();
        }

        /// <summary>
        /// Create a Mode 01 request message (standard OBD-II parameters)
        /// </summary>
        /// <param name="pids">Parameter IDs to request</param>
        /// <returns>Complete CAN message for Mode 01 request</returns>
        public byte[] CreateMode01Request(params byte[] pids)
        {
            if (pids == null || pids.Length == 0)
            {
                throw new ArgumentException("At least one PID must be specified", nameof(pids));
            }

            return ConstructCanMessage(0x01, null, pids);
        }

        /// <summary>
        /// Create a Mode 22 request message (manufacturer-specific parameters)
        /// </summary>
        /// <param name="pidHigh">High byte of PID</param>
        /// <param name="pidLow">Low byte of PID</param>
        /// <returns>Complete CAN message for Mode 22 request</returns>
        public byte[] CreateMode22Request(byte pidHigh, byte pidLow)
        {
            return ConstructCanMessage(0x22, pidHigh, pidLow);
        }

        /// <summary>
        /// Create a Mode 09 request message (vehicle information)
        /// </summary>
        /// <param name="infoType">Information type (e.g., 0x00 for supported PIDs)</param>
        /// <returns>Complete CAN message for Mode 09 request</returns>
        public byte[] CreateMode09Request(byte infoType)
        {
            return ConstructCanMessage(0x09, infoType);
        }
    }
}
