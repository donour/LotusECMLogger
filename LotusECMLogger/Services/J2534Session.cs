using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Owns an opened J2534 device (and the channels opened from it) as a single disposable
    /// unit. Replaces the repeated
    /// <c>DiscoverAPIs().First().FileName / LoadAPI / OpenDevice / OpenChannel</c> prologue.
    /// </summary>
    /// <remarks>
    /// The <see cref="J2534API"/> handle is intentionally NOT disposed here. In J2534-Sharp.Core
    /// v2, <see cref="J2534APIFactory.LoadAPI"/> caches and shares a single API instance per DLL
    /// for the whole process; disposing it poisons that cache, so the next <c>LoadAPI</c> returns
    /// a disposed instance and <c>OpenDevice</c> throws <see cref="ObjectDisposedException"/>.
    /// The cache is released — if ever needed — via <see cref="J2534APIFactory.ClearCache"/> at
    /// shutdown. Disposing the session does close its channels and device (PTClose), which is
    /// required so the device can be opened again on the next call.
    /// </remarks>
    public sealed class J2534Session : IDisposable
    {
        private readonly J2534Device _device;
        private readonly List<J2534Channel> _channels = new();

        private J2534Session(J2534Device device) => _device = device;

        /// <summary>Loads the first registered J2534 API and opens its first device.</summary>
        public static J2534Session Open()
        {
            string dllFileName = J2534APIFactory.DiscoverAPIs().First().FileName;
            // Cached, process-wide instance owned by the factory — must not be disposed here.
            J2534API api = J2534APIFactory.LoadAPI(dllFileName).Unwrap();
            return new J2534Session(api.OpenDevice("").Unwrap());
        }

        /// <summary>Opens a channel owned by this session (disposed when the session is).</summary>
        public J2534Channel OpenChannel(Protocol protocol, Baud baud, ConnectFlag flags = ConnectFlag.NONE)
        {
            J2534Channel channel = _device.OpenChannel(protocol, baud, flags).Unwrap();
            _channels.Add(channel);
            return channel;
        }

        /// <summary>Opens an ISO 15765 (ISO-TP) channel at the standard 500 kbaud.</summary>
        public J2534Channel OpenIso15765() => OpenChannel(Protocol.ISO15765, Baud.ISO15765);

        /// <summary>Opens a raw CAN channel (defaults to 500 kbaud).</summary>
        public J2534Channel OpenCan(Baud baud = Baud.CAN) => OpenChannel(Protocol.CAN, baud);

        public void Dispose()
        {
            foreach (J2534Channel channel in _channels)
                channel.Dispose();
            _channels.Clear();
            _device.Dispose();
        }
    }
}
