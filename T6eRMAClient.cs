using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using SAE.J2534; // Uses J2534-Sharp Message type

namespace Lotus.T6E.Can
{
    /// <summary>
    /// T6e Remote Memory Access client. Implements the ECU's CAN-based memory R/W protocol
    /// using standard IDs 0x50-0x57 and response aggregation on a configurable response ID.
    /// </summary>
    public sealed class T6eRMAClient
    {
        public const uint IdRead32 = 0x50;
        public const uint IdRead   = 0x53; // block read
        public const uint IdWrite32 = 0x54;
        public const uint IdWrite   = 0x57; // block write

        private readonly Channel _channel;
        private readonly uint _responseId;
        private readonly TimeSpan _defaultTimeout;

        public T6eRMAClient(Channel channel, uint responseId = 0x1E8, TimeSpan? defaultTimeout = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _responseId = responseId;
            _defaultTimeout = defaultTimeout ?? TimeSpan.FromMilliseconds(500);
        }

        public async Task<uint> ReadU32(uint address, CancellationToken ct = default)
        {
            ValidateRange(address, 4);
            Span<byte> req = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(req, address);
            await SendAsync(IdRead32, req.ToArray(), ct);
            var payload = await ReceivePayloadExactAsync(4, ct);
            return BinaryPrimitives.ReadUInt32BigEndian(payload);
        }

        public async Task<byte[]> Read(uint address, int length, CancellationToken ct = default)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            ValidateRange(address, (uint)length);

            if (length <= 0xFF)
            {
                Span<byte> hdr = stackalloc byte[5];
                BinaryPrimitives.WriteUInt32BigEndian(hdr, address);
                hdr[4] = (byte)length;
                await SendAsync(IdRead, hdr.ToArray(), ct);
            }
            else
            {
                Span<byte> hdr = stackalloc byte[6];
                BinaryPrimitives.WriteUInt32BigEndian(hdr, address);
                BinaryPrimitives.WriteUInt16BigEndian(hdr[4..], (ushort)length);
                await SendAsync(IdRead, hdr.ToArray(), ct);
            }

            byte[] result = new byte[length];
            int filled = 0;
            while (filled < length)
            {
                var rx = await ReceiveFromRespAsync(_defaultTimeout, ct);
                var payload = GetPayload(rx);
                int copy = Math.Min(payload.Length, length - filled);
                payload.Span.Slice(0, copy).CopyTo(result.AsSpan(filled, copy));
                filled += copy;
            }
            return result;
        }

        public async Task WriteU32(uint address, uint value, CancellationToken ct = default)
        {
            ValidateRange(address, 4);
            Span<byte> payload = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(payload, address);
            BinaryPrimitives.WriteUInt32BigEndian(payload[4..], value);
            await SendAsync(IdWrite32, payload.ToArray(), ct);
        }

        public async Task Write(uint address, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (data.Length <= 0) throw new ArgumentOutOfRangeException(nameof(data));
            ValidateRange(address, (uint)data.Length);

            // Protocol supports only an 8-bit length header for block write (0x57)
            if (data.Length > 0xFF) throw new ArgumentOutOfRangeException(nameof(data), "Block write supports up to 255 bytes per header.");

            Span<byte> hdr = stackalloc byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, address);
            hdr[4] = (byte)data.Length;
            await SendAsync(IdWrite, hdr.ToArray(), ct);

            int offset = 0;
            while (offset < data.Length)
            {
                int chunk = Math.Min(8, data.Length - offset);
                await SendAsync(IdWrite, data.Slice(offset, chunk), ct);
                offset += chunk;
            }
        }

        private async Task<byte[]> ReceivePayloadExactAsync(int expectedLen, CancellationToken ct)
        {
            var msg = await ReceiveFromRespAsync(_defaultTimeout, ct);
            var payload = GetPayload(msg);
            if (payload.Length != expectedLen)
                throw new InvalidOperationException($"Unexpected DLC: got {payload.Length}, expected {expectedLen}");
            return payload.ToArray();
        }

        private async Task<Message> ReceiveFromRespAsync(TimeSpan timeout, CancellationToken ct)
        {
            var msg = await ReceiveAsync(m => HasId(m, _responseId) && GetPayload(m).Length > 0, timeout, ct);
            if (msg == null) throw new TimeoutException("No ECU response");
            return msg.Value;
        }

        private Task SendAsync(uint arbitrationId, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            // Build message buffer: [4 bytes arbId BE][payload]
            var buf = new byte[4 + data.Length];
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), arbitrationId);
            data.CopyTo(buf.AsMemory(4));
            var msg = new Message
            {
                ProtocolID = Protocol.CAN,
                Data = buf,
                DataSize = (uint)buf.Length
            };
            _channel.SendMessage(msg);
            return Task.CompletedTask;
        }

        private async Task<Message?> ReceiveAsync(Func<Message, bool> match, TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var res = _channel.GetMessage();
                if (res.Result == ResultCode.STATUS_NOERROR && res.Messages != null && res.Messages.Length > 0)
                {
                    foreach (var m in res.Messages)
                    {
                        if (match(m)) return m;
                    }
                }
                await Task.Delay(1, ct);
            }
            return null;
        }

        private static bool HasId(in Message m, uint arbitrationId)
        {
            if (m.Data == null || m.Data.Length < 4) return false;
            return BinaryPrimitives.ReadUInt32BigEndian(m.Data.AsSpan(0, 4)) == arbitrationId;
        }

        private static ReadOnlyMemory<byte> GetPayload(in Message m)
        {
            if (m.Data == null || m.Data.Length <= 4) return ReadOnlyMemory<byte>.Empty;
            return new ReadOnlyMemory<byte>(m.Data, 4, m.Data.Length - 4);
        }

        private static void ValidateRange(uint addr, uint len)
        {
            bool low = addr < 0x0010_0000 && (ulong)addr + len <= 0x0010_0000UL;
            bool high = addr >= 0x4000_0000 && (ulong)addr + len <= 0x4001_0000UL;
            if (!(low || high)) throw new ArgumentOutOfRangeException(nameof(addr), "Address out of allowed ECU windows");
        }
    }
}


