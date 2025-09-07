namespace LotusECMLogger.Services
{
    /// <summary>
    /// Example usage of the CAN OBD-II interfaces and implementations
    /// This demonstrates how to use the new interfaces for OBD-II communication over CAN
    /// </summary>
    public class CanObdUsageExample
    {
        /// <summary>
        /// Example of basic CAN message construction and parsing
        /// </summary>
        public void BasicCanObdUsage()
        {
            // Create the CAN OBD service
            ICanObdService canObdService = new CanObdService();

            // Construct a Mode 01 request for engine RPM (PID 0x0C)
            byte[] rpmRequest = canObdService.ConstructCanMessage(0x01, 0x0C);
            // Result: [0x00, 0x00, 0x07, 0xE0, 0x01, 0x0C]

            // Construct a Mode 22 request for TPS target (PID 0x023B)
            byte[] tpsRequest = canObdService.ConstructCanMessage(0x22, 0x02, 0x3B);
            // Result: [0x00, 0x00, 0x07, 0xE0, 0x22, 0x02, 0x3B]

            // Example response data (simulated)
            byte[] simulatedResponse = [
                0x00, 0x00, 0x07, 0xE8, // Response header
                0x41, 0x0C,              // Mode 01 response for PID 0x0C
                0x1A, 0xF8               // RPM value: 6904 RPM
            ];

            // Parse the response
            Dictionary<string, object> readings = canObdService.ParseCanMessage(simulatedResponse);
            // Result: Dictionary with "Service"=1, "EngineRPM"=1726.0
        }

        /// <summary>
        /// Example of querying PID availability
        /// </summary>
        public async Task QueryPidAvailabilityExample()
        {
            // Create PID availability service with new J2534 connection
            using PidAvailabilityService pidService = await PidAvailabilityService.CreateWithNewConnection();

            try
            {
                // Query available PIDs for Mode 01 (standard parameters)
                uint mode01Pids = await pidService.QueryAvailablePids(0x01, 0x00);
                Console.WriteLine($"Mode 01 PIDs supported: 0x{mode01Pids:X8}");

                // Check if a specific PID is supported
                bool rpmSupported = await pidService.IsPidSupported(0x01, 0x0C); // Engine RPM
                Console.WriteLine($"Engine RPM (0x0C) supported: {rpmSupported}");

                // Get all supported PIDs for Mode 01
                List<byte> allSupportedPids = await pidService.GetAllSupportedPids(0x01);
                Console.WriteLine($"Total Mode 01 PIDs supported: {allSupportedPids.Count}");
                foreach (byte pid in allSupportedPids)
                {
                    Console.WriteLine($"  - PID 0x{pid:X2}");
                }

                // Query available PIDs for Mode 22 (manufacturer-specific)
                uint mode22Pids = await pidService.QueryAvailablePids(0x22, 0x00);
                Console.WriteLine($"Mode 22 PIDs supported: 0x{mode22Pids:X8}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying PID availability: {ex.Message}");
            }
        }

        /// <summary>
        /// Example of custom header usage
        /// </summary>
        public void CustomHeaderExample()
        {
            ICanObdService canObdService = new CanObdService();

            // Use custom header for different ECU
            byte[] customHeader = [0x00, 0x00, 0x07, 0xDF]; // Different ECU address

            // Construct message with custom header
            byte[] customRequest = canObdService.ConstructCanMessage(customHeader, 0x01, 0x0C);

            // Get headers
            byte[] requestHeader = canObdService.GetRequestHeader();
            byte[] responseHeader = canObdService.GetResponseHeader();
        }

        /// <summary>
        /// Example of working with parsed dictionary results
        /// </summary>
        public void DictionaryResultExample()
        {
            ICanObdService canObdService = new CanObdService();

            // Simulate a Mode 22 response for TPS data
            byte[] tpsResponse = [
                0x00, 0x00, 0x07, 0xE8, // Response header
                0x62, 0x02, 0x3B,        // Mode 22 response for PID 0x023B
                0x01, 0xA0               // TPS Target value: 416 (416 * 100 / 1024 ≈ 40.6%)
            ];

            Dictionary<string, object> result = canObdService.ParseCanMessage(tpsResponse);

            // Access parsed data
            if (result.ContainsKey("Service"))
                Console.WriteLine($"Service: {result["Service"]}");

            if (result.ContainsKey("TPSTarget"))
                Console.WriteLine($"TPS Target: {result["TPSTarget"]}%");

            // Iterate through all results
            foreach (var kvp in result)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }

            // Handle different data types
            if (result.TryGetValue("EngineRPM", out object? rpmValue) && rpmValue is double rpm)
            {
                Console.WriteLine($"Engine RPM: {rpm:F0}");
            }

            if (result.TryGetValue("VehicleSpeed", out object? speedValue) && speedValue is int speed)
            {
                Console.WriteLine($"Vehicle Speed: {speed} km/h");
            }
        }

        /// <summary>
        /// Example of using the convenience methods
        /// </summary>
        public void ConvenienceMethodsExample()
        {
            CanObdService canObdService = new CanObdService();

            // Use convenience methods for common requests
            byte[] mode01Request = canObdService.CreateMode01Request(0x0C, 0x0D, 0x0E); // RPM, Speed, Timing
            byte[] mode22Request = canObdService.CreateMode22Request(0x02, 0x3B); // TPS Target
            byte[] mode09Request = canObdService.CreateMode09Request(0x00); // Supported PIDs for Mode 09
        }

        /// <summary>
        /// Alternative: Simple parsing that just returns raw data
        /// If you want even simpler parsing, you could modify the interface to return raw byte arrays
        /// </summary>
        public void RawDataApproachExample()
        {
            // Alternative approach: If you just want raw byte arrays without any parsing
            // You could modify ICanObdService.ParseCanMessage to return byte[] or (byte[], byte[]) tuples

            ICanObdService canObdService = new CanObdService();

            // Construct a request
            byte[] request = canObdService.ConstructCanMessage(0x01, 0x0C);

            // In your communication layer, you'd get a response:
            // byte[] response = GetResponseFromCAN(request);

            // Then you could parse it however you want:
            // Dictionary<string, object> parsed = ParseResponseManually(response);
        }

        /// <summary>
        /// Manual parsing example (alternative to built-in parsing)
        /// </summary>
        private static Dictionary<string, object> ParseResponseManually(byte[] response)
        {
            var result = new Dictionary<string, object>();

            if (response == null || response.Length < 5)
                return result;

            // Check header
            if (response[0] == 0x00 && response[1] == 0x00 &&
                response[2] == 0x07 && response[3] == 0xE8)
            {
                // Valid ECU response
                byte service = (byte)(response[4] - 0x40);
                result["Service"] = service;

                // Your custom parsing logic here
                if (service == 0x01 && response.Length >= 7)
                {
                    byte pid = response[5];
                    if (pid == 0x0C && response.Length >= 8) // RPM
                    {
                        int rpm = (response[6] << 8) | response[7];
                        result["RPM"] = rpm / 4.0;
                    }
                }
            }

            return result;
        }
    }
}
