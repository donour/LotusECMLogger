namespace LotusECMLogger.Services.Ccp;

public enum CcpCommandCode : byte
{
    Connect = 0x01, SetMta = 0x02, Download = 0x03, Upload = 0x04, Test = 0x05,
    StartStop = 0x06, Disconnect = 0x07, StartStopAll = 0x08, GetActiveCalPage = 0x09,
    BuildChecksum = 0x0E, ShortUpload = 0x0F, SelectCalPage = 0x11,
    GetDaqSize = 0x14, SetDaqPointer = 0x15, WriteDaq = 0x16, ExchangeId = 0x17,
    GetCcpVersion = 0x1B, Download6 = 0x23,
}

public enum CcpError : byte
{
    Success = 0x00, DaqInitializationRequired = 0x22, UnknownCommand = 0x30,
    CommandSyntax = 0x31, ParameterOutOfRange = 0x32, AccessDenied = 0x33,
}

public sealed class CcpException(CcpCommandCode command, byte counter, byte error)
    : Exception($"CCP {command} (counter {counter}) failed with error 0x{error:X2}.")
{
    public CcpCommandCode Command { get; } = command;
    public byte Counter { get; } = counter;
    // Keep the raw code: an unfamiliar firmware response must not become 'success'.
    public byte ErrorCode { get; } = error;
}

public enum CcpStartStopMode : byte { Stop = 0, Start = 1, Prepare = 2 }
public enum CcpPacketKind { CommandReturn, Daq, Event }

/// <summary>Owned copy of a DTO, without the J2534 CAN header.</summary>
public sealed class CcpPacket
{
    internal CcpPacket(CcpPacketKind kind, byte[] bytes)
    {
        Kind = kind;
        Bytes = bytes;
    }

    public CcpPacketKind Kind { get; }
    public ReadOnlyMemory<byte> Bytes { get; }
    public byte Counter => Kind == CcpPacketKind.CommandReturn
        ? Bytes.Span[2] : throw new InvalidOperationException("Only a CRM carries a counter.");
    public byte ErrorCode => Kind == CcpPacketKind.CommandReturn
        ? Bytes.Span[1] : throw new InvalidOperationException("Only a CRM carries an error code.");
    public byte Pid => Kind == CcpPacketKind.Daq
        ? (byte)(Bytes.Span[0] & 0x7F) : throw new InvalidOperationException("Only DAQ carries an ODT PID.");
    public bool Overrun => Kind == CcpPacketKind.Daq && (Bytes.Span[0] & 0x80) != 0;
    public ReadOnlyMemory<byte> Data => Bytes[(Kind == CcpPacketKind.CommandReturn ? 3 : 1)..];

    public void EnsureSuccess(CcpCommandCode command, byte expectedCounter)
    {
        if (Kind != CcpPacketKind.CommandReturn || Counter != expectedCounter)
            throw new InvalidOperationException("The CRM does not match the waiting command.");
        if (ErrorCode != 0) throw new CcpException(command, Counter, ErrorCode);
    }
}

public readonly record struct CcpIdentification(byte IdLength, byte DataType, byte AvailableResources,
    byte ProtectedResources, byte DriverVersion)
{
    public static CcpIdentification Parse(CcpPacket reply)
    {
        if (reply.Kind != CcpPacketKind.CommandReturn || reply.ErrorCode != 0)
            throw new ArgumentException("EXCHANGE_ID requires a successful CRM.", nameof(reply));
        var d = reply.Data.Span;
        return new(d[0], d[1], d[2], d[3], d[4]);
    }
}
