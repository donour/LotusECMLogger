namespace LotusECMLogger.Services.Links
{
    /// <summary>
    /// Where services get their links from. <see cref="J2534LinkProvider"/> opens a J2534 session
    /// per link, as every service did before; the planned session broker will implement this on
    /// one shared device. Tests pass a scripted fake.
    /// </summary>
    public interface ILinkProvider
    {
        /// <summary>Opens an ISO 15765 link to one ECU.</summary>
        IDiagnosticLink OpenDiagnostic(ECUDefinition ecu);

        /// <summary>Opens a raw CAN link.</summary>
        ICanLink OpenCan();
    }
}
