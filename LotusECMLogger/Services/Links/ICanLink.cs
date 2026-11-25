namespace LotusECMLogger.Services.Links
{
    /// <summary>
    /// Raw CAN access (no ISO-TP). Today only the ECU coding write uses it, so the interface only
    /// sends; the broker's <see cref="Broker.BrokerCanLink"/> also receives by CAN ID, for the ABS
    /// telemetry and CCP (J2534-session-broker-plan.md, Phases 5 and 6). Disposing the link releases
    /// its lease.
    /// </summary>
    public interface ICanLink : IDisposable
    {
        /// <summary>Sends one CAN frame. Throws when the adapter rejects it.</summary>
        /// <param name="canId">11-bit CAN ID.</param>
        /// <param name="data">Up to eight data bytes.</param>
        void Send(uint canId, byte[] data);
    }
}
