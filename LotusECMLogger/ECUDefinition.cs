using SAE.J2534;

namespace LotusECMLogger
{
    /// <summary>
    /// Defines an ECU (Electronic Control Unit) that can be queried via OBD-II/CAN
    /// </summary>
    public class ECUDefinition
    {
        /// <summary>
        /// Human-readable name for this ECU (e.g., "ECM", "AEM UEGO", "TCM")
        /// </summary>
        public string Name { get; set; } = "ECU";

        /// <summary>
        /// CAN ID for sending requests to this ECU (e.g., 0x7E0 for ECM, 0x7E1 for TCM/UEGO)
        /// </summary>
        public uint RequestId { get; set; } = 0x7E0;

        /// <summary>
        /// CAN ID for responses from this ECU (e.g., 0x7E8 for ECM, 0x7E9 for TCM/UEGO)
        /// </summary>
        public uint ResponseId { get; set; } = 0x7E8;

        /// <summary>
        /// Standard ECM definition (0x7E0/0x7E8)
        /// </summary>
        public static ECUDefinition ECM => new()
        {
            Name = "ECM",
            RequestId = 0x7E0,
            ResponseId = 0x7E8
        };

        /// <summary>
        /// Standard TCM definition (0x7E1/0x7E9) - also commonly used for aftermarket devices
        /// </summary>
        public static ECUDefinition TCM => new()
        {
            Name = "TCM",
            RequestId = 0x7E1,
            ResponseId = 0x7E9
        };

        /// <summary>
        /// AEM X-Series UEGO on TCM address (0x7E1/0x7E9)
        /// </summary>
        public static ECUDefinition AEM_UEGO => new()
        {
            Name = "AEM UEGO",
            RequestId = 0x7E1,
            ResponseId = 0x7E9
        };

        /// <summary>
        /// Get the 4-byte CAN header for requests to this ECU
        /// </summary>
        public byte[] GetRequestHeader()
        {
            return
            [
                (byte)((RequestId >> 24) & 0xFF),
                (byte)((RequestId >> 16) & 0xFF),
                (byte)((RequestId >> 8) & 0xFF),
                (byte)(RequestId & 0xFF)
            ];
        }

        /// <summary>
        /// Get the 4-byte CAN header for responses from this ECU
        /// </summary>
        public byte[] GetResponseHeader()
        {
            return
            [
                (byte)((ResponseId >> 24) & 0xFF),
                (byte)((ResponseId >> 16) & 0xFF),
                (byte)((ResponseId >> 8) & 0xFF),
                (byte)(ResponseId & 0xFF)
            ];
        }

        /// <summary>
        /// Create a J2534 flow control filter for this ECU
        /// </summary>
        public MessageFilter CreateFlowControlFilter()
        {
            return new MessageFilter
            {
                FilterType = Filter.FLOW_CONTROL_FILTER,
                Mask = [0xFF, 0xFF, 0xFF, 0xFF],
                Pattern = GetResponseHeader(),
                FlowControl = GetRequestHeader()
            };
        }

        /// <summary>
        /// Check if a CAN response belongs to this ECU based on the response ID
        /// </summary>
        /// <param name="data">Raw CAN message data (4-byte header + payload)</param>
        /// <returns>True if the response is from this ECU</returns>
        public bool MatchesResponse(byte[] data)
        {
            if (data.Length < 4)
                return false;

            uint responseId = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
            return responseId == ResponseId;
        }

        public override string ToString()
        {
            return $"{Name} (Request: 0x{RequestId:X3}, Response: 0x{ResponseId:X3})";
        }
    }
}
