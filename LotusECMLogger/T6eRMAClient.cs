using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using LotusECMLogger.Services;
using SAE.J2534;

namespace LotusECMLogger
{
    /// <summary>
    /// T6e Remote Memory Access client. Implements the ECU's CAN-based memory R/W protocol.
    /// The eight operations are laid out contiguously from a base ID (Evora 0x50, Emira 0x40):
    /// +0 read32, +1 read16, +2 read8, +3 block read, +4 write32, +5 write16, +6 write8,
    /// +7 block write. Every read is answered on the profile's response ID (0x7A0 on both ECUs).
    ///
    /// This family is gated by the firmware's <c>ecu_unlocked</c> boot predicate: reads are only
    /// serviced when the ECU is unlocked, which is exactly what <see cref="ProbeEngineeringAccessAsync"/>
    /// exploits — a reply means the family is live, silence means locked (or ECU absent).
    ///
    /// SAFETY: writes are refused unless <see cref="AllowWrites"/> is set true.
    /// </summary>
    public sealed class T6eRMAClient
    {
        private readonly J2534Channel _channel;
        private readonly EcuMemoryProfile _profile;
        private readonly uint _responseId;
        private readonly TimeSpan _defaultTimeout;

        private uint IdRead32 => _profile.RmaBaseId + 0;
        private uint IdRead   => _profile.RmaBaseId + 3; // block read
        private uint IdWrite32 => _profile.RmaBaseId + 4;
        private uint IdWrite   => _profile.RmaBaseId + 7; // block write

        /// <summary>When false (default), every write helper throws before touching the bus.</summary>
        public bool AllowWrites { get; set; }

        public T6eRMAClient(J2534Channel channel, EcuMemoryProfile profile, TimeSpan? defaultTimeout = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _responseId = profile.RmaResponseId;
            _defaultTimeout = defaultTimeout ?? TimeSpan.FromMilliseconds(500);
        }

        /// <summary>
        /// Probes whether the raw engineering memory family is currently live (i.e. the ECU's
        /// <c>ecu_unlocked</c> predicate passed at boot) by issuing a single read32 at the base
        /// of the RAM window and waiting briefly for a reply on the response ID. A reply => the
        /// family is enabled; silence => locked or no ECU. Read-only and side-effect free.
        /// </summary>
        public async Task<bool> ProbeEngineeringAccessAsync(CancellationToken ct = default)
        {
            try
            {
                byte[] req = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(req, _profile.RamStart);
                Send(IdRead32, req);
                var msg = await ReceiveAsync(m => HasId(m, _responseId) && GetPayload(m).Length > 0,
                    TimeSpan.FromMilliseconds(150), ct);
                return msg != null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        public async Task<uint> ReadU32(uint address, CancellationToken ct = default)
        {
            ValidateRange(address, 4);
            byte[] req = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(req, address);
            Send(IdRead32, req);
            var payload = await ReceivePayloadExactAsync(4, ct);
            return BinaryPrimitives.ReadUInt32BigEndian(payload);
        }

        public async Task<byte[]> Read(uint address, int length, CancellationToken ct = default)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            ValidateRange(address, (uint)length);

            if (length <= 0xFF)
            {
                byte[] hdr = new byte[5];
                BinaryPrimitives.WriteUInt32BigEndian(hdr, address);
                hdr[4] = (byte)length;
                Send(IdRead, hdr);
            }
            else
            {
                byte[] hdr = new byte[6];
                BinaryPrimitives.WriteUInt32BigEndian(hdr, address);
                BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(4), (ushort)length);
                Send(IdRead, hdr);
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

        public Task WriteU32(uint address, uint value, CancellationToken ct = default)
        {
            RequireWrites();
            ValidateRange(address, 4);
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(payload, address);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), value);
            Send(IdWrite32, payload);
            return Task.CompletedTask;
        }

        public Task Write(uint address, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            RequireWrites();
            if (data.Length <= 0) throw new ArgumentOutOfRangeException(nameof(data));
            ValidateRange(address, (uint)data.Length);

            // Protocol supports only an 8-bit length header for block write.
            if (data.Length > 0xFF) throw new ArgumentOutOfRangeException(nameof(data), "Block write supports up to 255 bytes per header.");

            byte[] hdr = new byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(hdr, address);
            hdr[4] = (byte)data.Length;
            Send(IdWrite, hdr);

            int offset = 0;
            while (offset < data.Length)
            {
                int chunk = Math.Min(8, data.Length - offset);
                Send(IdWrite, data.Slice(offset, chunk).Span);
                offset += chunk;
            }
            return Task.CompletedTask;
        }

        private void RequireWrites()
        {
            if (!AllowWrites)
                throw new InvalidOperationException("RMA writes are disabled. Set AllowWrites = true to permit them.");
        }

        private async Task<byte[]> ReceivePayloadExactAsync(int expectedLen, CancellationToken ct)
        {
            var msg = await ReceiveFromRespAsync(_defaultTimeout, ct);
            var payload = GetPayload(msg);
            if (payload.Length != expectedLen)
                throw new InvalidOperationException($"Unexpected DLC: got {payload.Length}, expected {expectedLen}");
            return payload.ToArray();
        }

        private async Task<SAE.J2534.Message> ReceiveFromRespAsync(TimeSpan timeout, CancellationToken ct)
        {
            var msg = await ReceiveAsync(m => HasId(m, _responseId) && GetPayload(m).Length > 0, timeout, ct);
            if (msg == null) throw new TimeoutException("No ECU response");
            return msg.Value;
        }

        private void Send(uint arbitrationId, ReadOnlySpan<byte> data)
        {
            var buf = new byte[4 + data.Length];
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), arbitrationId);
            data.CopyTo(buf.AsSpan(4));
            _channel.SendMessage(new SAE.J2534.Message(buf, TxFlag.NONE));
        }

        private async Task<SAE.J2534.Message?> ReceiveAsync(Func<SAE.J2534.Message, bool> match, TimeSpan timeout, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var res = _channel.ReadMessage();
                if (res.Status == ResultCode.STATUS_NOERROR && res.Messages != null && res.Messages.Length > 0)
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

        private static bool HasId(in SAE.J2534.Message m, uint arbitrationId)
        {
            if (m.Data == null || m.Data.Length < 4) return false;
            return BinaryPrimitives.ReadUInt32BigEndian(m.Data.AsSpan(0, 4)) == arbitrationId;
        }

        private static ReadOnlyMemory<byte> GetPayload(in SAE.J2534.Message m)
        {
            if (m.Data == null || m.Data.Length <= 4) return ReadOnlyMemory<byte>.Empty;
            return new ReadOnlyMemory<byte>(m.Data, 4, m.Data.Length - 4);
        }

        private void ValidateRange(uint addr, uint len)
        {
            bool ok = addr >= _profile.RamStart && (ulong)addr + len <= (ulong)_profile.RamEnd + 1;
            if (!ok) throw new ArgumentOutOfRangeException(nameof(addr),
                $"Address 0x{addr:X8} (+{len}) is outside the {_profile.Name} window 0x{_profile.RamStart:X8}-0x{_profile.RamEnd:X8}.");
        }
    }
}
