using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    // Kept apart from the service so its tests can link the service without J2534.
    public sealed partial class HighSpeedLogService
    {
        /// <summary>Logs through the first J2534 adapter (raw CAN, 500 kbit/s) on the T6 CCP IDs.</summary>
        public HighSpeedLogService() : this(() => J2534CcpTransport.Open(CcpTarget.T6)) { }
    }
}
