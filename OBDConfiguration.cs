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
        public object? Decode { get; } // Accepts DecodeRuleJson for now

        public Mode22Request(string name, byte pidHigh, byte pidLow)
        {
            Name = name;
            PIDHigh = pidHigh;
            PIDLow = pidLow;
        }
        public Mode22Request(string name, byte pidHigh, byte pidLow, object? decode)
        {
            Name = name;
            PIDHigh = pidHigh;
            PIDLow = pidLow;
            Decode = decode;
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
        public byte[] ECMHeader { get; private set; } = [0x00, 0x00, 0x07, 0xE0];

        /// <summary>
        /// All OBD requests to be executed in sequence
        /// </summary>
        public List<IOBDRequest> Requests { get; } = new();

        /// <summary>
        /// Set ECM header (used by configuration loader)
        /// </summary>
        internal void SetECMHeader(byte[] header)
        {
            ECMHeader = header;
        }

        /// <summary>
        /// Load configuration from file
        /// </summary>
        /// <param name="configName">Configuration name (e.g., "lotus-default", "lotus-fast")</param>
        /// <returns>Loaded configuration</returns>
        public static OBDConfiguration LoadFromConfig(string configName)
        {
            var config = OBDConfigurationLoader.LoadByName(configName);
            PopulateMode22Decoders(config);
            return config;
        }

        /// <summary>
        /// Load configuration from specific file path
        /// </summary>
        /// <param name="filePath">Path to JSON configuration file</param>
        /// <returns>Loaded configuration</returns>
        public static OBDConfiguration LoadFromFile(string filePath)
        {
            var config = OBDConfigurationLoader.LoadFromFile(filePath);
            PopulateMode22Decoders(config);
            return config;
        }
        private static void PopulateMode22Decoders(OBDConfiguration config)
        {
            LiveDataReading.Mode22Decoders.Clear();
            foreach (var req in config.Requests)
            {
                if (req is Mode22Request m22 && m22.Decode is LotusECMLogger.OBDConfigurationLoader.DecodeRuleJson rule)
                {
                    LiveDataReading.Mode22Decoders[(m22.PIDHigh, m22.PIDLow)] = new LiveDataReading.Mode22DecodeRule
                    {
                        Name = m22.Name,
                        Start = rule.Start,
                        Length = rule.Length,
                        Formula = rule.Formula
                    };
                }
            }
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