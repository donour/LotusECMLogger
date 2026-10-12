using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    // Kept apart from the service so its tests can link the service without J2534.
    public sealed partial class HighSpeedLogService
    {
        /// <summary>
        /// A logger with its own CCP session on the first J2534 adapter. Prefer sharing the application's
        /// <see cref="CcpSessionHost"/>; this exists for standalone use and the Windows Forms designer path.
        /// </summary>
        public HighSpeedLogService() : this(CreateJ2534Host(), ownsHost: true) { }

        /// <summary>A session host on the first J2534 adapter (raw CAN, 500 kbit/s) using the T6 CCP IDs.</summary>
        public static CcpSessionHost CreateJ2534Host() => new(() => J2534CcpTransport.Open(CcpTarget.T6));
    }
}
