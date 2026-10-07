using System.Buffers.Binary;

namespace LotusECMLogger.Services.Ccp;

/// <summary>
/// CCP 2.1 wire encoding for the EFI/Vector slave. Addresses and prescalers are big-endian;
/// station addresses are little-endian. Every CRO is zero-padded to DLC 8.
/// These factories validate wire parameters; write authorization belongs to the session layer.
/// </summary>
public static class CcpFrame
{
    public const int Length = 8;

    private static byte[] Command(CcpCommandCode command, byte counter)
    {
        var bytes = new byte[Length];
        bytes[0] = (byte)command;
        bytes[1] = counter;
        return bytes;
    }

    public static byte[] Connect(byte counter) => Command(CcpCommandCode.Connect, counter);
    public static byte[] Test(byte counter) => Command(CcpCommandCode.Test, counter);
    public static byte[] ExchangeId(byte counter) => Command(CcpCommandCode.ExchangeId, counter);
    public static byte[] GetVersion(byte counter)
    {
        var bytes = Command(CcpCommandCode.GetCcpVersion, counter);
        bytes[2] = 2;
        bytes[3] = 1;
        return bytes;
    }

    public static byte[] Disconnect(byte counter, bool endSession = true)
    {
        var bytes = Command(CcpCommandCode.Disconnect, counter);
        bytes[2] = endSession ? (byte)1 : (byte)0;
        return bytes;
    }

    public static byte[] SetMta(byte counter, uint address)
    {
        var bytes = Command(CcpCommandCode.SetMta, counter);
        // MTA0 only; address extension is ignored by this firmware and is always zero.
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), address);
        return bytes;
    }

    public static byte[] Upload(byte counter, int size)
    {
        ValidateTransferSize(size);
        var bytes = Command(CcpCommandCode.Upload, counter);
        bytes[2] = (byte)size;
        return bytes;
    }

    public static byte[] ShortUpload(byte counter, uint address, int size)
    {
        var bytes = Upload(counter, size);
        bytes[0] = (byte)CcpCommandCode.ShortUpload;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), address);
        return bytes;
    }

    public static byte[] Download(byte counter, ReadOnlySpan<byte> data)
    {
        ValidateTransferSize(data.Length);
        var bytes = Command(CcpCommandCode.Download, counter);
        bytes[2] = (byte)data.Length;
        data.CopyTo(bytes.AsSpan(3));
        return bytes;
    }

    public static byte[] Download6(byte counter, ReadOnlySpan<byte> data)
    {
        if (data.Length != 6) throw new ArgumentException("DNLOAD_6 requires exactly six bytes.", nameof(data));
        var bytes = Command(CcpCommandCode.Download6, counter);
        data.CopyTo(bytes.AsSpan(2));
        return bytes;
    }

    public static byte[] BuildChecksum(byte counter, int size)
    {
        // Zero silently produces no reply; >=64 KiB is silently truncated by this slave.
        if (size is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(size));
        var bytes = Command(CcpCommandCode.BuildChecksum, counter);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)size);
        return bytes;
    }

    public static byte[] GetDaqSize(byte counter, CcpTarget target, int list)
    {
        ValidateList(target, list);
        var bytes = Command(CcpCommandCode.GetDaqSize, counter);
        bytes[2] = (byte)list;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), target.DtoId);
        return bytes;
    }

    public static byte[] SetDaqPointer(byte counter, CcpTarget target, int list, int odt, int element)
    {
        ValidateList(target, list);
        if (odt < 0 || odt >= target.OdtsPerList) throw new ArgumentOutOfRangeException(nameof(odt));
        // Firmware accepts 7, which aliases the next ODT (or list metadata at the end).
        if (element is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(element));
        var bytes = Command(CcpCommandCode.SetDaqPointer, counter);
        bytes[2] = (byte)list;
        bytes[3] = (byte)odt;
        bytes[4] = (byte)element;
        return bytes;
    }

    public static byte[] WriteDaq(byte counter, uint address, int size)
    {
        if (size is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(size));
        if (address == 0) throw new ArgumentOutOfRangeException(nameof(address), "Zero ends the ODT.");
        var bytes = Command(CcpCommandCode.WriteDaq, counter);
        bytes[2] = (byte)size;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), address);
        return bytes;
    }

    public static byte[] StartStop(byte counter, CcpTarget target, CcpStartStopMode mode,
        int list, int lastOdt = 0, int eventChannel = 0, int prescaler = 1)
    {
        ValidateList(target, list);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (lastOdt < 0 || lastOdt >= target.OdtsPerList) throw new ArgumentOutOfRangeException(nameof(lastOdt));
        if (!target.EventRatesHz.ContainsKey(eventChannel)) throw new ArgumentOutOfRangeException(nameof(eventChannel));
        if (prescaler is < 1 or > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(prescaler));
        var bytes = Command(CcpCommandCode.StartStop, counter);
        bytes[2] = (byte)mode;
        bytes[3] = (byte)list;
        bytes[4] = (byte)lastOdt;
        bytes[5] = (byte)eventChannel;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)prescaler);
        return bytes;
    }

    public static byte[] StartStopAll(byte counter, bool start)
    {
        var bytes = Command(CcpCommandCode.StartStopAll, counter);
        bytes[2] = start ? (byte)1 : (byte)0;
        return bytes;
    }

    public static CcpPacket ParseDto(ReadOnlySpan<byte> payload, CcpTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (payload.Length != Length) throw new ArgumentException("A CCP DTO must have DLC 8.", nameof(payload));
        var kind = payload[0] switch
        {
            0xFF => CcpPacketKind.CommandReturn,
            0xFE => CcpPacketKind.Event,
            _ => CcpPacketKind.Daq,
        };
        if (kind == CcpPacketKind.Daq && (payload[0] & 0x7F) >= target.DaqListCount * target.OdtsPerList)
            throw new ArgumentException("PID is outside this target's DAQ range.", nameof(payload));
        return new CcpPacket(kind, payload.ToArray());
    }

    public static ushort AdditiveChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        foreach (byte value in data) sum += value;
        return (ushort)(sum & 0xFFFF);
    }

    private static void ValidateTransferSize(int size)
    {
        if (size is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(size));
    }

    private static void ValidateList(CcpTarget target, int list)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (list < 0 || list >= target.DaqListCount) throw new ArgumentOutOfRangeException(nameof(list));
    }
}
