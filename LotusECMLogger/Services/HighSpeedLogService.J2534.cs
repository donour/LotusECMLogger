using LotusECMLogger.Services.Broker;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    // Kept apart from the service so its tests can link the service without J2534.
    public sealed partial class HighSpeedLogService
    {
        /// <summary>
        /// A logger with its own CCP session host on the application's adapter. Prefer sharing the application's
        /// <see cref="CcpSessionHost"/>; this exists for standalone use and the Windows Forms designer path.
        /// </summary>
        public HighSpeedLogService() : this(CreateJ2534Host(), ownsHost: true) { }

        /// <summary>
        /// A session host on the application's J2534 broker (its shared raw CAN channel, 500 kbit/s) using the T6
        /// CCP IDs. Each session holds a Shared broker lease while it is open.
        /// </summary>
        public static CcpSessionHost CreateJ2534Host() => new(() => new BrokerCcpTransport(J2534Broker.Shared, CcpTarget.T6));
    }
}
