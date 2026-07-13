using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Diagnostic client for the ECU's proprietary "tooling" CAN channel
    /// (Emira: request 0x202, responses 0x200/0x201). This channel is initialized
    /// independently of the raw memory family (0x40-0x47 / 0x50-0x57), so probing it is a
    /// useful way to check what diagnostic access a given ECU exposes.
    ///
    /// Protocol recovered from disassembly/emira/8896915220A_ROW/emira.c:
    ///   • Receive (flexcan_c_rx_202 → FUN_00a058dc): a request is accepted ONLY when the
    ///     CAN frame carries a full DLC of 8. The eight data bytes are copied into a command
    ///     record and executed by the periodic interpreter (FUN_00a04eec).
    ///   • Command record layout (the 8 data bytes):
    ///         [0] opcode
    ///         [1] echo tag (returned in the reply so requests can be correlated)
    ///         [2] length / sub-command
    ///         [3] first inline payload byte
    ///         [4..7] 32-bit parameter, big-endian (address for set-pointer / read-at)
    ///   • Reply (on 0x200 or 0x201), 8 bytes: [0]=0xFF marker, [1]=status (0 = OK, else an
    ///     NRC-like code such as 0x30-0x33), [2]=echo tag, [3..] up to five result bytes.
    ///
    /// Decoded opcodes used here:
    ///   0x02 set pointer register N (N = byte[2] ∈ {0,1}) to the big-endian address in [4..7]
    ///   0x03 write byte[2] payload bytes (from [3]) through pointer 0, advancing it
    ///   0x04 read byte[2] bytes through pointer 0 into the reply, advancing it
    ///   0x09 version / current calibration-base marker
    ///   0x0F read byte[2] bytes starting at the big-endian address in [4..7] (one-shot read)
    ///
    /// A single frame moves at most five payload bytes, so this channel is for probing and
    /// small reads, not bulk transfer — use the RMA path for dumps.
    ///
    /// SAFETY: the write opcode is included for completeness but, like the existing RMA write
    /// path, is refused unless <see cref="AllowWrites"/> is explicitly set true.
    /// </summary>
    public sealed class T6eToolingClient
    {
        private const byte OpSetPointer = 0x02;
        private const byte OpWriteThroughPointer = 0x03;
        private const byte OpReadThroughPointer = 0x04;
        private const byte OpVersion = 0x09;
        private const byte OpReadAt = 0x0F;

        private const byte ReplyMarker = 0xFF;
        private const int MaxPayloadPerFrame = 5; // bytes [3..7] of the 8-byte record

        private readonly J2534Channel _channel;
        private readonly uint _requestId;
        private readonly uint[] _responseIds;
        private readonly TimeSpan _defaultTimeout;
        private byte _tag;

        /// <summary>When false (default), the write helper throws before touching the bus.</summary>
        public bool AllowWrites { get; set; }

        public T6eToolingClient(J2534Channel channel, EcuMemoryProfile profile, TimeSpan? defaultTimeout = null)
            : this(channel, (profile ?? throw new ArgumentNullException(nameof(profile))).ToolingRequestId,
                   profile.ToolingResponseIds, defaultTimeout)
        {
        }

        public T6eToolingClient(J2534Channel channel, uint requestId, uint[] responseIds, TimeSpan? defaultTimeout = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            if (responseIds is null || responseIds.Length == 0)
                throw new ArgumentException("At least one response ID is required.", nameof(responseIds));
            _requestId = requestId;
            _responseIds = responseIds;
            _defaultTimeout = defaultTimeout ?? TimeSpan.FromMilliseconds(500);
        }

        /// <summary>
        /// Queries the channel's version / calibration-base marker (opcode 0x09). This is the
        /// safest liveness probe: a reply proves the tooling channel is reachable on this ECU
        /// without any memory access. Returns null if the ECU stays silent.
        /// </summary>
        public async Task<ToolingReply?> GetVersionAsync(CancellationToken ct = default)
        {
            byte[] frame = BuildFrame(OpVersion, subOrLen: 0, address: 0, inlinePayload: default);
            return await ExchangeAsync(frame, ct);
        }

        /// <summary>
        /// Reads up to five bytes starting at <paramref name="address"/> using the one-shot
        /// read-at opcode (0x0F).
        /// </summary>
        public async Task<byte[]> ReadAsync(uint address, int length, CancellationToken ct = default)
        {
            if (length < 1 || length > MaxPayloadPerFrame)
                throw new ArgumentOutOfRangeException(nameof(length), $"Length must be 1..{MaxPayloadPerFrame} bytes per frame.");

            byte[] frame = BuildFrame(OpReadAt, subOrLen: (byte)length, address: address, inlinePayload: default);
            ToolingReply reply = await ExchangeAsync(frame, ct)
                ?? throw new TimeoutException($"No tooling-channel reply to read at 0x{address:X8}.");
            reply.ThrowIfError();
            return reply.Data.Length >= length ? reply.Data[..length] : reply.Data;
        }

        /// <summary>Sets pointer register <paramref name="register"/> (0 or 1) to an address (opcode 0x02).</summary>
        public async Task SetPointerAsync(byte register, uint address, CancellationToken ct = default)
        {
            if (register > 1) throw new ArgumentOutOfRangeException(nameof(register), "Only pointer registers 0 and 1 exist.");
            byte[] frame = BuildFrame(OpSetPointer, subOrLen: register, address: address, inlinePayload: default);
            ToolingReply? reply = await ExchangeAsync(frame, ct);
            reply?.ThrowIfError();
        }

        /// <summary>Reads up to five bytes through pointer 0, advancing it (opcode 0x04).</summary>
        public async Task<byte[]> ReadThroughPointerAsync(int length, CancellationToken ct = default)
        {
            if (length < 1 || length > MaxPayloadPerFrame)
                throw new ArgumentOutOfRangeException(nameof(length), $"Length must be 1..{MaxPayloadPerFrame} bytes per frame.");
            byte[] frame = BuildFrame(OpReadThroughPointer, subOrLen: (byte)length, address: 0, inlinePayload: default);
            ToolingReply reply = await ExchangeAsync(frame, ct)
                ?? throw new TimeoutException("No tooling-channel reply to pointer read.");
            reply.ThrowIfError();
            return reply.Data.Length >= length ? reply.Data[..length] : reply.Data;
        }

        /// <summary>
        /// Points pointer register 0 at <paramref name="address"/> (opcode 0x02) then writes the
        /// supplied bytes through it (opcode 0x03). Gated by <see cref="AllowWrites"/>, mirroring
        /// the existing RMA write path.
        /// </summary>
        public async Task WriteAsync(uint address, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            if (!AllowWrites)
                throw new InvalidOperationException(
                    "Tooling-channel writes are disabled. Set AllowWrites = true to permit them (same guard as RMA writes).");
            if (data.Length < 1 || data.Length > MaxPayloadPerFrame)
                throw new ArgumentOutOfRangeException(nameof(data), $"Write payload must be 1..{MaxPayloadPerFrame} bytes per frame.");

            await SetPointerAsync(0, address, ct);
            byte[] frame = BuildFrame(OpWriteThroughPointer, subOrLen: (byte)data.Length, address: 0, inlinePayload: data.Span);
            ToolingReply? reply = await ExchangeAsync(frame, ct);
            reply?.ThrowIfError();
        }

        private byte[] BuildFrame(byte opcode, byte subOrLen, uint address, ReadOnlySpan<byte> inlinePayload)
        {
            // Every request is a full 8-byte-DLC frame; the firmware discards anything shorter.
            byte[] data = new byte[8];
            data[0] = opcode;
            data[1] = _tag = unchecked((byte)(_tag + 1)); // rolling echo tag for correlation
            data[2] = subOrLen;
            if (!inlinePayload.IsEmpty)
                inlinePayload.CopyTo(data.AsSpan(3, Math.Min(inlinePayload.Length, MaxPayloadPerFrame)));
            else
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), address);
            return data;
        }

        private async Task<ToolingReply?> ExchangeAsync(byte[] data8, CancellationToken ct)
        {
            byte expectedTag = data8[1];
            Send(_requestId, data8);

            var deadline = DateTime.UtcNow + _defaultTimeout;
            while (DateTime.UtcNow < deadline)
            {
                var res = _channel.ReadMessage();
                if (res.Status == ResultCode.STATUS_NOERROR && res.Messages is { Length: > 0 })
                {
                    foreach (var m in res.Messages)
                    {
                        if (TryParseReply(m, expectedTag, out ToolingReply reply))
                            return reply;
                    }
                }
                await Task.Delay(1, ct);
            }
            return null;
        }

        private bool TryParseReply(in SAE.J2534.Message m, byte expectedTag, out ToolingReply reply)
        {
            reply = default;
            if (m.Data is null || m.Data.Length <= 4) return false;

            uint id = BinaryPrimitives.ReadUInt32BigEndian(m.Data.AsSpan(0, 4));
            if (Array.IndexOf(_responseIds, id) < 0) return false;

            ReadOnlySpan<byte> payload = m.Data.AsSpan(4);
            if (payload.Length < 3 || payload[0] != ReplyMarker) return false;

            // Ignore stale replies that don't echo our request tag (best-effort correlation).
            if (payload[2] != expectedTag) return false;

            reply = new ToolingReply(
                status: payload[1],
                echoTag: payload[2],
                data: payload.Length > 3 ? payload[3..].ToArray() : []);
            return true;
        }

        private void Send(uint arbitrationId, ReadOnlySpan<byte> data)
        {
            var buf = new byte[4 + data.Length];
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), arbitrationId);
            data.CopyTo(buf.AsSpan(4));
            _channel.SendMessage(new SAE.J2534.Message(buf, TxFlag.NONE));
        }
    }

    /// <summary>A parsed reply from the tooling channel.</summary>
    public readonly record struct ToolingReply(byte status, byte echoTag, byte[] data)
    {
        public byte Status => status;
        public byte EchoTag => echoTag;
        public byte[] Data => data;
        public bool IsOk => status == 0;

        public void ThrowIfError()
        {
            if (!IsOk)
                throw new InvalidOperationException($"Tooling channel returned status 0x{status:X2} (non-zero = command rejected).");
        }
    }
}
