namespace LotusECMLogger.Services
{
    /// <summary>
    /// Interface for querying available PIDs (Parameter IDs) on a given OBD-II service
    /// Provides functionality to discover what parameters are supported by the ECU
    /// </summary>
    public interface IPidAvailabilityService
    {
        /// <summary>
        /// Query available PIDs for a specific OBD-II service
        /// </summary>
        /// <param name="service">OBD-II service/mode (e.g., 0x01, 0x22)</param>
        /// <param name="pidGroup">PID group to query (0x00 for first 32 PIDs, 0x20 for next 32, etc.)</param>
        /// <returns>Bitmask indicating which PIDs are supported</returns>
        Task<uint> QueryAvailablePids(byte service, byte pidGroup = 0x00);

        /// <summary>
        /// Check if a specific PID is supported for a given service
        /// </summary>
        /// <param name="service">OBD-II service/mode</param>
        /// <param name="pid">Parameter ID to check</param>
        /// <returns>True if PID is supported, false otherwise</returns>
        Task<bool> IsPidSupported(byte service, byte pid);

        /// <summary>
        /// Get all supported PIDs for a specific service by querying all PID groups
        /// </summary>
        /// <param name="service">OBD-II service/mode</param>
        /// <returns>List of all supported PIDs for the service</returns>
        Task<List<byte>> GetAllSupportedPids(byte service);

        /// <summary>
        /// Parse PID availability response from CAN message data
        /// </summary>
        /// <param name="messageData">Raw CAN message data containing PID availability response</param>
        /// <param name="expectedService">Expected OBD-II service for validation</param>
        /// <param name="expectedPidGroup">Expected PID group for validation</param>
        /// <returns>Bitmask of supported PIDs, or null if parsing failed</returns>
        uint? ParsePidAvailabilityResponse(byte[] messageData, byte expectedService, byte expectedPidGroup);
    }
}
