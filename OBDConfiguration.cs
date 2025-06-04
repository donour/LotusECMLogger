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
            return OBDConfigurationLoader.LoadByName(configName);
        }

        /// <summary>
        /// Load configuration from specific file path
        /// </summary>
        /// <param name="filePath">Path to JSON configuration file</param>
        /// <returns>Loaded configuration</returns>
        public static OBDConfiguration LoadFromFile(string filePath)
        {
            return OBDConfigurationLoader.LoadFromFile(filePath);
        }

        /// <summary>
        /// Create default Lotus ECM logging configuration (now loads from config file)
        /// </summary>
        /// <returns>Configured OBD request set</returns>
        public static OBDConfiguration CreateLotusDefault()
        {
            try
            {
                return LoadFromConfig("lotus-default");
            }
            catch (FileNotFoundException)
            {
                // Fallback to hardcoded if config file not found
                return CreateLotusDefaultHardcoded();
            }
        }

        /// <summary>
        /// Create fast logging configuration (loads from config file)
        /// </summary>
        /// <returns>Fast logging configuration</returns>
        public static OBDConfiguration CreateFastLogging()
        {
            try
            {
                return LoadFromConfig("lotus-fast");
            }
            catch (FileNotFoundException)
            {
                // Fallback to hardcoded if config file not found
                return CreateFastLoggingHardcoded();
            }
        }

        /// <summary>
        /// Create complete Lotus configuration (loads from config file)
        /// </summary>
        /// <returns>Complete configuration</returns>
        public static OBDConfiguration CreateCompleteLotusConfiguration()
        {
            try
            {
                return LoadFromConfig("lotus-complete");
            }
            catch (FileNotFoundException)
            {
                // Fallback to hardcoded if config file not found
                return CreateCompleteLotusConfigurationHardcoded();
            }
        }

        /// <summary>
        /// Hardcoded fallback for default configuration
        /// </summary>
        private static OBDConfiguration CreateLotusDefaultHardcoded()
        {
            var config = new OBDConfiguration();

            // Standard OBD-II Mode 0x01 requests
            config.Requests.Add(new Mode01Request("Basic Engine Data",
                0x0C, // Engine speed (RPM)
                0x11, // Throttle position
                0x43 // Absolute load
            ));

            config.Requests.Add(new Mode01Request("Secondary Engine Data",
                0x05, // Coolant temperature
                0x0E, // Timing advance
                0x0B  // Intake manifold absolute pressure
            ));

            // Lotus-specific Mode 0x22 requests
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
        /// Hardcoded fallback for fast logging configuration
        /// </summary>
        private static OBDConfiguration CreateFastLoggingHardcoded()
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
        /// Hardcoded fallback for complete Lotus configuration
        /// Data source: LotusECU-T4e repository Mode22-Live.csv
        /// </summary>
        private static OBDConfiguration CreateCompleteLotusConfigurationHardcoded()
        {
            var config = new OBDConfiguration();

            // Standard OBD-II Mode 0x01 requests (essential parameters)
            config.Requests.Add(new Mode01Request("Essential Engine Data",
                0x0C, // Engine speed (RPM)
                0x11, // Throttle position
                0x43, // Absolute load
                0x05, // Coolant temperature
                0x0E, // Timing advance
                0x0B  // Intake manifold absolute pressure
            ));

            // Engine Management - Fuel & Air (0x01-0x1F)
            config.Requests.Add(new Mode22Request("System Leak Status", 0x02, 0x01));
            config.Requests.Add(new Mode22Request("Purge DC", 0x02, 0x02));
            config.Requests.Add(new Mode22Request("Idle Learn", 0x02, 0x03));
            config.Requests.Add(new Mode22Request("Idle Air Output", 0x02, 0x04));
            config.Requests.Add(new Mode22Request("Injection Pulse Time (B1)", 0x02, 0x05));
            config.Requests.Add(new Mode22Request("Idle Status", 0x02, 0x06));
            config.Requests.Add(new Mode22Request("VVT Target Cam Position (inlet)", 0x02, 0x07));
            config.Requests.Add(new Mode22Request("VVT Actual Cam Position (B1 inlet)", 0x02, 0x08));
            config.Requests.Add(new Mode22Request("VVL Status", 0x02, 0x09));
            config.Requests.Add(new Mode22Request("Cat Diag Pre-Cat Sw B1", 0x02, 0x0A));
            config.Requests.Add(new Mode22Request("Cat Diag Pre-Cat Max Sw", 0x02, 0x0B));
            config.Requests.Add(new Mode22Request("Gear Position", 0x02, 0x0C));
            config.Requests.Add(new Mode22Request("Idle Speed Target", 0x02, 0x0D));
            config.Requests.Add(new Mode22Request("Purge Vacuum Pressure", 0x02, 0x12));
            config.Requests.Add(new Mode22Request("Air Fuel Ratio Target", 0x02, 0x13));
            config.Requests.Add(new Mode22Request("Relay Status", 0x02, 0x14)); // Multiple relays in one response
            config.Requests.Add(new Mode22Request("Tyre Pressures", 0x02, 0x15)); // All 4 tyres
            config.Requests.Add(new Mode22Request("Tyre Pressure Monitor Status", 0x02, 0x16));
            config.Requests.Add(new Mode22Request("Injection Pulse Time (B2)", 0x02, 0x17));

            // Octane Scalers (0x18-0x1B)
            config.Requests.Add(new Mode22Request("Octane Scaler Cylinder 1", 0x02, 0x18));
            config.Requests.Add(new Mode22Request("Octane Scaler Cylinder 3", 0x02, 0x19));
            config.Requests.Add(new Mode22Request("Octane Scaler Cylinder 4", 0x02, 0x1A));
            config.Requests.Add(new Mode22Request("Octane Scaler Cylinder 2", 0x02, 0x1B));

            // Environmental & System (0x25-0x40)
            config.Requests.Add(new Mode22Request("Ambient Air Temperature", 0x02, 0x25));
            config.Requests.Add(new Mode22Request("Mass Air Per Stroke", 0x02, 0x26));
            config.Requests.Add(new Mode22Request("Start-Up Coolant Temperature", 0x02, 0x27));
            config.Requests.Add(new Mode22Request("Accumulated Mass Air", 0x02, 0x28));
            config.Requests.Add(new Mode22Request("ECU Shutdown Timer", 0x02, 0x29));
            config.Requests.Add(new Mode22Request("Engine Load", 0x02, 0x2A));
            config.Requests.Add(new Mode22Request("Cam Angle Error (B1 inlet)", 0x02, 0x2B));
            config.Requests.Add(new Mode22Request("Idle Speed Error", 0x02, 0x2C));
            config.Requests.Add(new Mode22Request("ECU On Timer", 0x02, 0x2D));
            config.Requests.Add(new Mode22Request("Fuel Learn Dead Time (B1)", 0x02, 0x2E));
            config.Requests.Add(new Mode22Request("Evaporative Vapour Concentration", 0x02, 0x2F));
            config.Requests.Add(new Mode22Request("Cam Angle Error", 0x02, 0x30));
            config.Requests.Add(new Mode22Request("Knock Spark Advance Retard", 0x02, 0x31)); // All cylinders
            config.Requests.Add(new Mode22Request("Long Term Fuel Trim (B1)", 0x02, 0x32));
            config.Requests.Add(new Mode22Request("Short Term Fuel Trim (B1)", 0x02, 0x33));
            config.Requests.Add(new Mode22Request("Air Flow Error", 0x02, 0x34));
            config.Requests.Add(new Mode22Request("Fuel Learn Dead Time (B2)", 0x02, 0x35));
            config.Requests.Add(new Mode22Request("Long Term Fuel Trim (B2)", 0x02, 0x36));
            config.Requests.Add(new Mode22Request("Short Term Fuel Trim (B2)", 0x02, 0x37));
            config.Requests.Add(new Mode22Request("Catalytic Converter Temperature", 0x02, 0x38));
            config.Requests.Add(new Mode22Request("Target Catalytic Converter Temperature", 0x02, 0x39));

            // Performance & Advanced (0x40-0x60)
            config.Requests.Add(new Mode22Request("Maximum Catalytic Converter Temperature", 0x02, 0x3A));
            config.Requests.Add(new Mode22Request("VVT Target Cam Position (exhaust)", 0x02, 0x3B));
            config.Requests.Add(new Mode22Request("VVT Actual Cam Position (B1 exhaust)", 0x02, 0x3C));
            config.Requests.Add(new Mode22Request("Cam Angle Error (B1 exhaust)", 0x02, 0x3D));
            config.Requests.Add(new Mode22Request("Target Turbo Wastegate Position", 0x02, 0x3E));
            config.Requests.Add(new Mode22Request("Actual Turbo Wastegate Position", 0x02, 0x3F));
            config.Requests.Add(new Mode22Request("Target Turbo Boost Pressure", 0x02, 0x40));
            config.Requests.Add(new Mode22Request("Actual Turbo Boost Pressure", 0x02, 0x41));
            config.Requests.Add(new Mode22Request("Accelerator Pedal Position", 0x02, 0x46));
            config.Requests.Add(new Mode22Request("Throttle Position Sensor", 0x02, 0x45));
            config.Requests.Add(new Mode22Request("Maximum Engine Speed", 0x02, 0x47));
            config.Requests.Add(new Mode22Request("Engine Speed Limit", 0x02, 0x48));
            config.Requests.Add(new Mode22Request("Target Engine Speed", 0x02, 0x49));
            config.Requests.Add(new Mode22Request("Engine Speed Error", 0x02, 0x4A));
            config.Requests.Add(new Mode22Request("Octane Scaler Cylinder 5", 0x02, 0x4D));
            config.Requests.Add(new Mode22Request("Octane Scaler Cylinder 6", 0x02, 0x4E));

            // Sport Mode & Advanced Features (0x5D-0x75)
            config.Requests.Add(new Mode22Request("Sport Button", 0x02, 0x5D));
            config.Requests.Add(new Mode22Request("Manifold Temperature", 0x02, 0x72));
            config.Requests.Add(new Mode22Request("Launch Control Switch", 0x02, 0x73));
            config.Requests.Add(new Mode22Request("Oil Temperature", 0x02, 0x75));

            // Transmission Parameters (0x76-0xA6)
            config.Requests.Add(new Mode22Request("Auto Gear", 0x02, 0x76));
            config.Requests.Add(new Mode22Request("Engine Speed (Received from ECU)", 0x02, 0x77));
            config.Requests.Add(new Mode22Request("Gear Request", 0x02, 0x78));
            config.Requests.Add(new Mode22Request("Input Shaft Speed", 0x02, 0x79));
            config.Requests.Add(new Mode22Request("Output Shaft Speed", 0x02, 0x7A));
            config.Requests.Add(new Mode22Request("SL1 Pressure Switch Status", 0x02, 0x7B));
            config.Requests.Add(new Mode22Request("SL2 Pressure Switch Status", 0x02, 0x7C));
            config.Requests.Add(new Mode22Request("SLU Pressure Switch Status", 0x02, 0x7D));
            config.Requests.Add(new Mode22Request("Dura Position Sensor 1", 0x02, 0x7E));
            config.Requests.Add(new Mode22Request("Dura Position Sensor 2", 0x02, 0x7F));
            config.Requests.Add(new Mode22Request("Current Dura Position", 0x02, 0x81));
            config.Requests.Add(new Mode22Request("Learnt Dura Position Park", 0x02, 0x82));
            config.Requests.Add(new Mode22Request("Learnt Dura Position Reverse", 0x02, 0x83));
            config.Requests.Add(new Mode22Request("Learnt Dura Position Neutral", 0x02, 0x84));
            config.Requests.Add(new Mode22Request("Learnt Dura Position Drive", 0x02, 0x85));
            config.Requests.Add(new Mode22Request("PRND Feedback Switch - Park", 0x02, 0x86));
            config.Requests.Add(new Mode22Request("PRND Feedback Switch - Reverse", 0x02, 0x87));
            config.Requests.Add(new Mode22Request("PRND Feedback Switch - Neutral", 0x02, 0x88));
            config.Requests.Add(new Mode22Request("PRND Feedback Switch - Drive", 0x02, 0x89));
            config.Requests.Add(new Mode22Request("Park Request Switch", 0x02, 0x8A));
            config.Requests.Add(new Mode22Request("Reverse Request Switch", 0x02, 0x8B));
            config.Requests.Add(new Mode22Request("Neutral Request Switch", 0x02, 0x8C));
            config.Requests.Add(new Mode22Request("Drive Request Switch", 0x02, 0x8D));
            config.Requests.Add(new Mode22Request("Gear Ratio", 0x02, 0x8E));
            config.Requests.Add(new Mode22Request("Commanded Torque Converter Status", 0x02, 0x8F));
            config.Requests.Add(new Mode22Request("Green Learn Status", 0x02, 0x90));
            config.Requests.Add(new Mode22Request("Slip Learn Status Flags", 0x02, 0x91));
            config.Requests.Add(new Mode22Request("Green Learn Target Load Minimum", 0x02, 0x92));
            config.Requests.Add(new Mode22Request("Green Learn Target Load Maximum", 0x02, 0x93));
            config.Requests.Add(new Mode22Request("Input Shaft Load", 0x02, 0x94));
            config.Requests.Add(new Mode22Request("Green Learn No. Complete Current Bin", 0x02, 0x95));
            config.Requests.Add(new Mode22Request("Green Learn Target No. Current Bin", 0x02, 0x96));
            config.Requests.Add(new Mode22Request("Torque Converter Slip", 0x02, 0x97));

            // Solenoid Duty Cycles (0x98-0x9E)
            config.Requests.Add(new Mode22Request("Solenoid SL Duty Cycle", 0x02, 0x98));
            config.Requests.Add(new Mode22Request("Solenoid SL1 Duty Cycle", 0x02, 0x99));
            config.Requests.Add(new Mode22Request("Solenoid SL2 Duty Cycle", 0x02, 0x9A));
            config.Requests.Add(new Mode22Request("Solenoid SL3 Duty Cycle", 0x02, 0x9B));
            config.Requests.Add(new Mode22Request("Solenoid SL4 Duty Cycle", 0x02, 0x9C));
            config.Requests.Add(new Mode22Request("Solenoid SLT Duty Cycle", 0x02, 0x9D));
            config.Requests.Add(new Mode22Request("Solenoid SLU Duty Cycle", 0x02, 0x9E));

            // Solenoid Demands (0x9F-0xA6)
            config.Requests.Add(new Mode22Request("Solenoid SL Demand", 0x02, 0x9F));
            config.Requests.Add(new Mode22Request("Solenoid SL1 Demand", 0x02, 0xA1));
            config.Requests.Add(new Mode22Request("Solenoid SL2 Demand", 0x02, 0xA2));
            config.Requests.Add(new Mode22Request("Solenoid SL3 Demand", 0x02, 0xA3));
            config.Requests.Add(new Mode22Request("Solenoid SL4 Demand", 0x02, 0xA4));
            config.Requests.Add(new Mode22Request("Solenoid SLT Demand", 0x02, 0xA5));
            config.Requests.Add(new Mode22Request("Solenoid SLU Demand", 0x02, 0xA6));

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