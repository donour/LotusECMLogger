namespace LotusECMLogger.Services.Links
{
    /// <summary>
    /// Where services get their links from: a <see cref="Broker.BrokerLease"/>, or the broker's
    /// <see cref="Broker.J2534Broker.Links"/> (a lease per link). Tests pass a scripted fake.
    /// </summary>
    public interface ILinkProvider
    {
        /// <summary>Opens an ISO 15765 link to one ECU.</summary>
        IDiagnosticLink OpenDiagnostic(ECUDefinition ecu);

        /// <summary>Opens a raw CAN link.</summary>
        ICanLink OpenCan();
    }
}
