namespace LotusECMLogger.Services.Links
{
    /// <summary>
    /// Raw CAN access (no ISO-TP). Today only the ECU coding write uses it, so it only sends;
    /// receiving by CAN ID is added when the ABS telemetry moves onto it
    /// (J2534-session-broker-plan.md, Phase 6). Disposing the link releases the adapter.
    /// </summary>
    public interface ICanLink : IDisposable
    {
        /// <summary>Sends one CAN frame. Throws when the adapter rejects it.</summary>
        /// <param name="canId">11-bit CAN ID.</param>
        /// <param name="data">Up to eight data bytes.</param>
        void Send(uint canId, byte[] data);
    }
}
