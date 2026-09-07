namespace LotusECMLogger.Services
{
    /// <summary>Decoded values from one Zeitronix Zt-3 CAN broadcast frame.</summary>
    internal readonly record struct Zt3WidebandSample(
        double Lambda,
        double LambdaCoarse,
        double Afr,
        byte OxygenSensorStatus);

    /// <summary>Decodes the 8-byte Zt-3 payload broadcast on standard CAN ID 0x05A.</summary>
    internal static class Zt3WidebandDecoder
    {
        internal const uint CanId = 0x05A;
        internal const int PayloadLength = 8;

        internal static bool TryDecode(ReadOnlySpan<byte> payload, out Zt3WidebandSample sample)
        {
            if (payload.Length < PayloadLength)
            {
                sample = default;
                return false;
            }

            sample = new Zt3WidebandSample(
                Lambda: ((payload[0] << 8) | payload[1]) * 0.001,
                LambdaCoarse: payload[2] * 0.01,
                Afr: payload[3] * 0.1,
                OxygenSensorStatus: payload[7]);
            return true;
        }
    }
}
