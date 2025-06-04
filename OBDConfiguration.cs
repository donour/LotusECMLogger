namespace LotusECMLogger
{
    /// <summary>
    /// Base interface for all OBD-II requests
    /// </summary>
    public interface IOBDRequest
    {
        /// <summary>
        /// OBD-II mode (0x01 for standard, 0x22 for manufacturer-specific)
        /// </summary>
        byte Mode { get; }
        
        /// <summary>
        /// Human-readable name for this request
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Build the complete message bytes including ECM header
        /// </summary>
        /// <param name="ecmHeader">ECM header bytes</param>
        /// <returns>Complete message ready to send</returns>
        byte[] BuildMessage(byte[] ecmHeader);
    }

    /// <summary>
    /// Standard OBD-II Mode 0x01 request (supports multiple PIDs in one request)
    /// </summary>
    public class Mode01Request : IOBDRequest
    {
        public byte Mode => 0x01;
        public string Name { get; }
        public byte[] PIDs { get; }

        public Mode01Request(string name, params byte[] pids)
        {
            Name = name;
            PIDs = pids;
        }

        public byte[] BuildMessage(byte[] ecmHeader)
        {
            var message = new byte[ecmHeader.Length + 1 + PIDs.Length];
            Array.Copy(ecmHeader, 0, message, 0, ecmHeader.Length);
            message[ecmHeader.Length] = Mode;
            Array.Copy(PIDs, 0, message, ecmHeader.Length + 1, PIDs.Length);
            return message;
        }
    }

    /// <summary>
    /// Manufacturer-specific Mode 0x22 request (single PID per request)
    /// </summary>
    public class Mode22Request : IOBDRequest
    {
        public byte Mode => 0x22;
        public string Name { get; }
        public byte PIDHigh { get; }
        public byte PIDLow { get; }

        public Mode22Request(string name, byte pidHigh, byte pidLow)
        {
            Name = name;
            PIDHigh = pidHigh;
            PIDLow = pidLow;
        }

        public byte[] BuildMessage(byte[] ecmHeader)
        {
            var message = new byte[ecmHeader.Length + 3]; // mode + 2 PID bytes
            Array.Copy(ecmHeader, 0, message, 0, ecmHeader.Length);
            message[ecmHeader.Length] = Mode;
            message[ecmHeader.Length + 1] = PIDHigh;
            message[ecmHeader.Length + 2] = PIDLow;
            return message;
        }
    }

    /// <summary>
    /// Configuration for all OBD-II requests used in logging
    /// </summary>
    public class OBDConfiguration
    {
        /// <summary>
        /// ECM header for Lotus vehicles
        /// </summary>
        public byte[] ECMHeader { get; } = [0x00, 0x00, 0x07, 0xE0];

        /// <summary>
        /// All OBD requests to be executed in sequence
        /// </summary>
        public List<IOBDRequest> Requests { get; } = new();

        /// <summary>
        /// Create default Lotus ECM logging configuration
        /// </summary>
        /// <returns>Configured OBD request set</returns>
        public static OBDConfiguration CreateLotusDefault()
        {
            var config = new OBDConfiguration();

            // Standard OBD-II Mode 0x01 requests
            config.Requests.Add(new Mode01Request("Basic Engine Data",
                0x0C, // Engine speed (RPM)
                0x11, // Throttle position
                0x43  // Absolute load
            ));

            config.Requests.Add(new Mode01Request("Secondary Engine Data", 
                0x05, // Coolant temperature
                0x0E, // Timing advance
                0x0B  // Intake manifold absolute pressure
            ));

            // Lotus-specific Mode 0x22 requests
            config.Requests.Add(new Mode22Request("Sport Button", 0x02, 0x5D));
            config.Requests.Add(new Mode22Request("TPS", 0x02, 0x45));
            config.Requests.Add(new Mode22Request("Accelerator Pedal Position", 0x02, 0x46));
            config.Requests.Add(new Mode22Request("Manifold Temperature", 0x02, 0x72));
            
            // Octane rating requests
            config.Requests.Add(new Mode22Request("Octane Rating 1", 0x02, 0x18));
            config.Requests.Add(new Mode22Request("Octane Rating 2", 0x02, 0x1B));
            config.Requests.Add(new Mode22Request("Octane Rating 3", 0x02, 0x19));
            config.Requests.Add(new Mode22Request("Octane Rating 4", 0x02, 0x1A));
            config.Requests.Add(new Mode22Request("Octane Rating 5", 0x02, 0x4D));
            config.Requests.Add(new Mode22Request("Octane Rating 6", 0x02, 0x4E));

            return config;
        }

        /// <summary>
        /// Create a minimal configuration for maximum performance (fewer requests)
        /// </summary>
        /// <returns>Fast logging configuration with essential parameters only</returns>
        public static OBDConfiguration CreateFastLogging()
        {
            var config = new OBDConfiguration();

            // Only essential engine parameters for high-speed logging
            config.Requests.Add(new Mode01Request("Essential Engine Data",
                0x0C, // Engine speed (RPM)
                0x11, // Throttle position
                0x0E  // Timing advance
            ));

            config.Requests.Add(new Mode22Request("Accelerator Pedal Position", 0x02, 0x46));

            return config;
        }

        /// <summary>
        /// Create a diagnostic-focused configuration with all available parameters
        /// </summary>
        /// <returns>Comprehensive logging configuration</returns>
        public static OBDConfiguration CreateDiagnosticMode()
        {
            var config = CreateLotusDefault();
            
            // Add additional diagnostic parameters
            config.Requests.Add(new Mode01Request("Additional Diagnostics",
                0x0D, // Vehicle speed
                0x0F, // Intake air temperature
                0x21, // Distance with MIL on
                0x31  // Distance since DTCs cleared
            ));

            return config;
        }

        /// <summary>
        /// Build all messages ready for transmission
        /// </summary>
        /// <returns>Array of complete message bytes</returns>
        public byte[][] BuildAllMessages()
        {
            return Requests.Select(request => request.BuildMessage(ECMHeader)).ToArray();
        }

        /// <summary>
        /// Get requests of a specific type for batch sending
        /// </summary>
        /// <typeparam name="T">Request type to filter by</typeparam>
        /// <returns>Filtered requests</returns>
        public IEnumerable<T> GetRequests<T>() where T : class, IOBDRequest
        {
            return Requests.OfType<T>();
        }
    }
} 