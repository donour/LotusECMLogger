using SAE.J2534;

namespace LotusECMLogger
{
    /// <summary>
    /// Groups an ECU definition with its associated OBD requests
    /// </summary>
    public class ECURequestGroup
    {
        /// <summary>
        /// The ECU to send requests to
        /// </summary>
        public ECUDefinition ECU { get; set; } = ECUDefinition.ECM;

        /// <summary>
        /// List of OBD requests for this ECU
        /// </summary>
        public List<IOBDRequest> Requests { get; set; } = [];

        /// <summary>
        /// Build all messages for this ECU with the correct header
        /// </summary>
        public byte[][] BuildMessages()
        {
            var header = ECU.GetRequestHeader();
            return Requests.Select(r => r.BuildMessage(header)).ToArray();
        }
    }

    /// <summary>
    /// Configuration for logging from multiple ECUs simultaneously
    /// </summary>
    public class MultiECUConfiguration
    {
        /// <summary>
        /// Name of this configuration
        /// </summary>
        public string Name { get; set; } = "Multi-ECU Configuration";

        /// <summary>
        /// Description of this configuration
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// All ECU groups (ECU + requests) in this configuration
        /// </summary>
        public List<ECURequestGroup> ECUGroups { get; set; } = [];

        /// <summary>
        /// Get all unique ECU definitions in this configuration
        /// </summary>
        public IEnumerable<ECUDefinition> GetAllECUs()
        {
            return ECUGroups.Select(g => g.ECU);
        }

        /// <summary>
        /// Get all flow control filters needed for this configuration
        /// </summary>
        public IEnumerable<MessageFilter> GetAllFlowControlFilters()
        {
            // Use distinct by ResponseId to avoid duplicate filters
            var seenResponseIds = new HashSet<uint>();
            foreach (var group in ECUGroups)
            {
                if (seenResponseIds.Add(group.ECU.ResponseId))
                {
                    yield return group.ECU.CreateFlowControlFilter();
                }
            }
        }

        /// <summary>
        /// Build all messages for all ECUs, grouped by ECU
        /// </summary>
        /// <returns>List of (ECU, messages) tuples</returns>
        public List<(ECUDefinition ecu, byte[][] messages)> BuildAllMessagesByECU()
        {
            return ECUGroups
                .Select(g => (g.ECU, g.BuildMessages()))
                .ToList();
        }

        /// <summary>
        /// Build all messages for all ECUs as a flat array (for sequential sending)
        /// Each message tagged with its source ECU
        /// </summary>
        public List<(ECUDefinition ecu, byte[] message)> BuildAllMessagesFlat()
        {
            var result = new List<(ECUDefinition, byte[])>();
            foreach (var group in ECUGroups)
            {
                var header = group.ECU.GetRequestHeader();
                foreach (var request in group.Requests)
                {
                    result.Add((group.ECU, request.BuildMessage(header)));
                }
            }
            return result;
        }

        /// <summary>
        /// Find the ECU that matches a response based on response CAN ID
        /// </summary>
        /// <param name="responseData">Raw CAN response data</param>
        /// <returns>Matching ECU definition or null if not found</returns>
        public ECUDefinition? FindECUForResponse(byte[] responseData)
        {
            return ECUGroups
                .Select(g => g.ECU)
                .FirstOrDefault(ecu => ecu.MatchesResponse(responseData));
        }

        /// <summary>
        /// Get total number of requests across all ECUs
        /// </summary>
        public int TotalRequestCount => ECUGroups.Sum(g => g.Requests.Count);

        /// <summary>
        /// Create a multi-ECU configuration from a legacy single-ECU OBDConfiguration
        /// </summary>
        public static MultiECUConfiguration FromLegacyConfig(OBDConfiguration legacyConfig)
        {
            // Determine ECU from header
            var header = legacyConfig.ECMHeader;
            uint requestId = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
            uint responseId = requestId + 8; // Standard OBD-II convention

            var ecu = new ECUDefinition
            {
                Name = "ECM",
                RequestId = requestId,
                ResponseId = responseId
            };

            return new MultiECUConfiguration
            {
                Name = "Converted Legacy Configuration",
                ECUGroups =
                [
                    new ECURequestGroup
                    {
                        ECU = ecu,
                        Requests = legacyConfig.Requests.ToList()
                    }
                ]
            };
        }

        /// <summary>
        /// Load multi-ECU configuration from file (auto-detects format)
        /// </summary>
        public static MultiECUConfiguration LoadFromConfig(string configName)
        {
            return MultiECUConfigurationLoader.LoadByName(configName);
        }
    }
}
