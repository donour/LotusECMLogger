using System.Buffers.Binary;

namespace LotusECMLogger.Services.Ccp.Simulation;

/// <summary>
/// Single-threaded, deterministic test driver. Owns the fake's receive queue and virtual clock;
/// command polling retains asynchronous DTOs. Not a hardware session implementation.
/// </summary>
public sealed class CcpSimulationClient(FakeCcpSlave ecu)
{
    private byte _counter;
    private bool _uncertain;
    private readonly List<CcpPacket> _daq = [];
    public IReadOnlyList<CcpPacket> TakeDaq()
    {
        var packets = _daq.ToArray(); _daq.Clear(); return packets;
    }
    public CcpPacket Exchange(Func<byte, byte[]> command)
    {
        if (_uncertain) throw new InvalidOperationException("Timed-out session: create a fresh driver after reboot.");
        var bytes = command(_counter++);
        ecu.Send(bytes);
        for (int i = 0; i <= 1000; i++)
        {
            CcpPacket? reply = null;
            foreach (var frame in ecu.Read(0))
            {
                var packet = CcpFrame.ParseDto(frame.Payload.Span, ecu.Target);
                if (packet.Kind == CcpPacketKind.CommandReturn)
                {
                    if (packet.Counter != bytes[1]) { _uncertain = true; throw new InvalidDataException("Unexpected CRM counter."); }
                    packet.EnsureSuccess((CcpCommandCode)bytes[0], bytes[1]); reply = packet;
                }
                else if (packet.Kind == CcpPacketKind.Daq) _daq.Add(packet);
            }
            if (reply is not null) return reply;
            ecu.AdvanceMilliseconds(1);
        }
        _uncertain = true;
        throw new TimeoutException("No CRM within 1000 virtual milliseconds (check CCP bus/enable byte).");
    }
    public void Connect() => Exchange(CcpFrame.Connect);
    public byte[] ReadMemory(uint address, int length)
    {
        if (length < 1 || (ulong)address + (uint)length > 0x100000000UL) throw new ArgumentOutOfRangeException(nameof(length));
        Exchange(c => CcpFrame.SetMta(c, address));
        var result = new byte[length];
        for (int offset = 0; offset < length; offset += 5)
        {
            int count = Math.Min(5, length - offset);
            Exchange(c => CcpFrame.Upload(c, count)).Data.Span[..count].CopyTo(result.AsSpan(offset));
        }
        return result;
    }
    public void WriteRam(uint address, ReadOnlySpan<byte> data)
    {
        if (address < T6FirmwareImage.RamBase || (ulong)address + (uint)data.Length > 0x40010000UL || data.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(address));
        var bytes = data.ToArray();
        Exchange(c => CcpFrame.SetMta(c, address));
        for (int offset = 0; offset < bytes.Length; offset += 5)
        {
            var chunk = bytes.AsSpan(offset, Math.Min(5, bytes.Length - offset)).ToArray();
            Exchange(c => CcpFrame.Download(c, chunk));
        }
    }
    public ushort Checksum(uint address, int length)
    {
        if (length is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(length));
        Exchange(c => CcpFrame.SetMta(c, address));
        return BinaryPrimitives.ReadUInt16BigEndian(Exchange(c => CcpFrame.BuildChecksum(c, length)).Data.Span[1..]);
    }
    public T6SymbolRegistry ReadRegistry(T6FirmwareProfile profile) => T6SymbolRegistry.ParseTable(
        ReadMemory(profile.SymbolEntriesAddress - 44, 44 + profile.SymbolCount * 6), profile.SymbolCount);

    public byte StartDaq(uint address, int size, int eventChannel = 0)
    {
        byte firstPid = Exchange(c => CcpFrame.GetDaqSize(c, ecu.Target, 0)).Data.Span[1];
        Exchange(c => CcpFrame.SetDaqPointer(c, ecu.Target, 0, 0, 0));
        Exchange(c => CcpFrame.WriteDaq(c, address, size));
        Exchange(c => CcpFrame.StartStop(c, ecu.Target, CcpStartStopMode.Start, 0, 0, eventChannel, 1));
        return firstPid;
    }
    public IReadOnlyList<CcpPacket> Sample(int milliseconds)
    {
        ecu.AdvanceMilliseconds(milliseconds);
        // Poll with a harmless version command to exercise mixed CRM/DAQ demultiplexing.
        Exchange(CcpFrame.GetVersion);
        return TakeDaq();
    }
}
