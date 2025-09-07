namespace LotusECMLogger.Services
{
    /// <summary>
    /// Interface for handling OBD-II requests over CAN using ISO 15765
    /// Provides functionality to parse CAN messages and construct new messages as byte arrays
    /// </summary>
    public interface ICanObdService
    {
        /// <summary>
        /// Parse a received CAN message byte array into OBD-II data
        /// </summary>
        /// <param name="messageData">Raw CAN message data as byte array</param>
        /// <returns>Dictionary of parameter names to their values</returns>
        Dictionary<string, object> ParseCanMessage(byte[] messageData);

        /// <summary>
        /// Construct a CAN message byte array for an OBD-II request
        /// </summary>
        /// <param name="service">OBD-II service/mode (e.g., 0x01, 0x22)</param>
        /// <param name="pid">Parameter ID (can be null for service-level requests)</param>
        /// <param name="additionalData">Additional data bytes for the request</param>
        /// <returns>Complete CAN message byte array ready for transmission</returns>
        byte[] ConstructCanMessage(byte service, byte? pid = null, params byte[] additionalData);

        /// <summary>
        /// Construct a CAN message byte array for an OBD-II request with custom header
        /// </summary>
        /// <param name="header">CAN header bytes</param>
        /// <param name="service">OBD-II service/mode (e.g., 0x01, 0x22)</param>
        /// <param name="pid">Parameter ID (can be null for service-level requests)</param>
        /// <param name="additionalData">Additional data bytes for the request</param>
        /// <returns>Complete CAN message byte array ready for transmission</returns>
        byte[] ConstructCanMessage(byte[] header, byte service, byte? pid = null, params byte[] additionalData);

        /// <summary>
        /// Get the expected CAN response header for this service
        /// </summary>
        /// <returns>Response header byte array</returns>
        byte[] GetResponseHeader();

        /// <summary>
        /// Get the CAN request header for this service
        /// </summary>
        /// <returns>Request header byte array</returns>
        byte[] GetRequestHeader();
    }
}
