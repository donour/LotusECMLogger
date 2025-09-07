using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Implementation of IPidAvailabilityService for querying available PIDs on OBD-II services
    /// </summary>
    public class PidAvailabilityService : IPidAvailabilityService, IDisposable
    {
        private readonly ICanObdService _canObdService;
        private readonly Device? _device;
        private readonly Channel? _channel;
        private bool _disposed;

        /// <summary>
        /// Constructor that takes an existing CAN OBD service and J2534 channel
        /// </summary>
        /// <param name="canObdService">CAN OBD service implementation</param>
        /// <param name="device">J2534 device (optional, will be created if null)</param>
        /// <param name="channel">J2534 channel (optional, will be created if null)</param>
        public PidAvailabilityService(ICanObdService canObdService, Device? device = null, Channel? channel = null)
        {
            _canObdService = canObdService ?? throw new ArgumentNullException(nameof(canObdService));
            _device = device;
            _channel = channel;
        }

        /// <summary>
        /// Query available PIDs for a specific OBD-II service
        /// </summary>
        /// <param name="service">OBD-II service/mode (e.g., 0x01, 0x22)</param>
        /// <param name="pidGroup">PID group to query (0x00 for first 32 PIDs, 0x20 for next 32, etc.)</param>
        /// <returns>Bitmask indicating which PIDs are supported</returns>
        public async Task<uint> QueryAvailablePids(byte service, byte pidGroup = 0x00)
        {
            // For synchronous J2534 operations, we'll use Task.FromResult
            // In a real async implementation, this would involve proper async channel operations
            return await Task.FromResult(QueryAvailablePidsSync(service, pidGroup));
        }

        /// <summary>
        /// Synchronous implementation of PID availability query
        /// </summary>
        private uint QueryAvailablePidsSync(byte service, byte pidGroup)
        {
            if (_channel == null)
            {
                throw new InvalidOperationException("No J2534 channel available for communication");
            }

            try
            {
                // Construct the PID availability query message
                byte[] message = _canObdService.ConstructCanMessage(service, pidGroup);

                // Send the message
                _channel.SendMessages([message]);

                // Read response with timeout
                var response = _channel.GetMessages(1, 1000); // 1 second timeout

                if (response.Messages.Length > 0)
                {
                    var result = ParsePidAvailabilityResponse(response.Messages[0].Data, service, pidGroup);
                    return result ?? 0;
                }

                return 0; // No response received
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying PID availability: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Check if a specific PID is supported for a given service
        /// </summary>
        /// <param name="service">OBD-II service/mode</param>
        /// <param name="pid">Parameter ID to check</param>
        /// <returns>True if PID is supported, false otherwise</returns>
        public async Task<bool> IsPidSupported(byte service, byte pid)
        {
            // Calculate which PID group this PID belongs to
            byte pidGroup = (byte)(pid / 32 * 32); // Groups are in multiples of 32

            // Query the PID group
            uint bitmask = await QueryAvailablePids(service, pidGroup);

            // Check if the specific bit is set
            int bitPosition = pid % 32;
            return (bitmask & 1U << 31 - bitPosition) != 0;
        }

        /// <summary>
        /// Get all supported PIDs for a specific service by querying all PID groups
        /// </summary>
        /// <param name="service">OBD-II service/mode</param>
        /// <returns>List of all supported PIDs for the service</returns>
        public async Task<List<byte>> GetAllSupportedPids(byte service)
        {
            var supportedPids = new List<byte>();
            byte pidGroup = 0x00;

            // Query PID groups until we find no more supported PIDs
            while (true)
            {
                uint bitmask = await QueryAvailablePids(service, pidGroup);

                if (bitmask == 0)
                {
                    // No PIDs supported in this group, check if we should continue
                    // Some services might have gaps, so we check a few more groups
                    if (pidGroup >= 0x40) // Stop after checking first few groups
                        break;

                    pidGroup += 0x20;
                    continue;
                }

                // Extract supported PIDs from bitmask
                for (int i = 0; i < 32; i++)
                {
                    if ((bitmask & 1U << 31 - i) != 0)
                    {
                        byte pid = (byte)(pidGroup + i);
                        supportedPids.Add(pid);
                    }
                }

                pidGroup += 0x20;

                // Safety check to prevent infinite loops
                if (pidGroup > 0xFF)
                    break;
            }

            return supportedPids;
        }

        /// <summary>
        /// Parse PID availability response from CAN message data
        /// </summary>
        /// <param name="messageData">Raw CAN message data containing PID availability response</param>
        /// <param name="expectedService">Expected OBD-II service for validation</param>
        /// <param name="expectedPidGroup">Expected PID group for validation</param>
        /// <returns>Bitmask of supported PIDs, or null if parsing failed</returns>
        public uint? ParsePidAvailabilityResponse(byte[] messageData, byte expectedService, byte expectedPidGroup)
        {
            if (messageData == null || messageData.Length < 8)
            {
                return null;
            }

            // Check if this is a response from the ECU (header should match response header)
            byte[] responseHeader = _canObdService.GetResponseHeader();
            if (messageData.Length < responseHeader.Length ||
                !messageData.Take(responseHeader.Length).SequenceEqual(responseHeader))
            {
                return null; // Not an ECU response
            }

            // Extract service and PID group from response
            int serviceIndex = responseHeader.Length;
            if (messageData.Length <= serviceIndex)
                return null;

            byte responseService = (byte)(messageData[serviceIndex] - 0x40); // Response service = request service + 0x40
            byte responsePidGroup = messageData[serviceIndex + 1];

            // Validate that this is the expected response
            if (responseService != expectedService || responsePidGroup != expectedPidGroup)
            {
                return null;
            }

            // Extract the 4-byte bitmask (big-endian)
            if (messageData.Length < serviceIndex + 6)
                return null;

            uint bitmask = (uint)(
                messageData[serviceIndex + 2] << 24 |
                messageData[serviceIndex + 3] << 16 |
                messageData[serviceIndex + 4] << 8 |
                messageData[serviceIndex + 5]);

            return bitmask;
        }

        /// <summary>
        /// Create a PID availability service with new J2534 connection
        /// </summary>
        /// <returns>Configured PidAvailabilityService instance</returns>
        public static async Task<PidAvailabilityService> CreateWithNewConnection()
        {
            try
            {
                string dllFileName = APIFactory.GetAPIinfo().First().Filename;
                API api = APIFactory.GetAPI(dllFileName);
                Device device = api.GetDevice();

                // Use ISO15765 protocol
                Channel channel = device.GetChannel(Protocol.ISO15765, Baud.ISO15765, ConnectFlag.NONE);

                // Set up flow control filter for Lotus ECM
                var flowControlFilter = new MessageFilter
                {
                    FilterType = Filter.FLOW_CONTROL_FILTER,
                    Mask = [0xFF, 0xFF, 0xFF, 0xFF],
                    Pattern = [0x00, 0x00, 0x07, 0xE8],
                    FlowControl = [0x00, 0x00, 0x07, 0xE0]
                };
                channel.StartMsgFilter(flowControlFilter);

                var canObdService = new CanObdService();
                return new PidAvailabilityService(canObdService, device, channel);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create J2534 connection: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Dispose method to clean up J2534 resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method for proper resource cleanup
        /// </summary>
        /// <param name="disposing">Whether disposing managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Dispose managed resources
                _channel?.Dispose();
                _device?.Dispose();
            }

            _disposed = true;
        }
    }
}
