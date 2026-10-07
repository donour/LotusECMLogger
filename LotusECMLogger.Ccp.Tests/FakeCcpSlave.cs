using System.Buffers.Binary;
using System.Text;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>
/// Deterministic firmware model, not a generic CCP simulator. No wall-clock scheduler or native I/O:
/// AdvanceMainLoop runs one checksum pass; QueueEvent/DrainDaq represent sampling/transmission.
/// Only well-sized transfers are modelled; real firmware's oversized buffer corruption is not safe to emulate.
/// </summary>
internal sealed class FakeCcpSlave : ICcpTransport
{
    internal readonly record struct Entry(uint Address, byte Size);
    private readonly Dictionary<uint, byte> _memory = new();
    private readonly Queue<CcpReceivedFrame> _received = new();
    private readonly Queue<byte[]> _daqTransmit = new();
    private readonly byte[] _crm = new byte[8];
    private readonly Entry[,,] _entries;
    private readonly byte[] _listState;
    private readonly byte[] _lastOdt;
    private readonly byte[] _events;
    private readonly ushort[] _prescalers;
    private readonly ushort[] _cycles;
    private readonly bool[] _overruns;
    private readonly CcpBuildProfile _profile;
    private bool _connected, _temporary, _daqInitialized, _disposed;
    private uint _mta0, _mta1;
    private int _checksumRemaining;
    private (int list, int odt, int element)? _daqPointer;
    private const uint IdAddress = 0x40001000;
    public CcpTarget Target => _profile.Target;
    public uint Timestamp { get; set; }
    public uint Mta0 => _mta0;
    public int ChecksumRemaining => _checksumRemaining;

    public FakeCcpSlave(CcpBuildProfile? profile = null)
    {
        _profile = profile ?? CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
        int lists = Target.DaqListCount;
        _entries = new Entry[lists, Target.OdtsPerList, 7];
        _listState = new byte[lists];
        _lastOdt = new byte[lists];
        _events = new byte[lists];
        _prescalers = new ushort[lists];
        _cycles = new ushort[lists];
        _overruns = new bool[lists];
        Load(IdAddress, Encoding.ASCII.GetBytes(Target.SlaveId + "\0"));
        if (_profile.EnableByteAddress is uint enable) Load(enable, [1]);
    }

    public void Load(uint address, ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < data.Length; i++) _memory[unchecked(address + (uint)i)] = data[i];
    }
    public byte Peek(uint address) => _memory.GetValueOrDefault(address);
    public Entry DaqEntry(int list, int odt, int element) => _entries[list, odt, element];

    public void Send(ReadOnlySpan<byte> command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (command.Length != 8) throw new ArgumentException("Expected DLC 8.", nameof(command));
        if (_profile.EnableByteAddress is uint enable && Peek(enable) != 1) return;
        byte cmd = command[0];
        if (cmd is 1 or 5)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(command[2..]) != 0)
            {
                if (_connected) { _connected = false; _temporary = true; }
                return;
            }
            if (cmd == 1)
            {
                if (!_connected && !_temporary) StopAll();
                _connected = true;
                _temporary = false;
            }
            BeginReply(command[1]);
            _crm[3] = 0xFE;
            _crm.AsSpan(4).Clear();
            Reply();
            return;
        }
        if (!_connected) return;
        BeginReply(command[1]);
        switch ((CcpCommandCode)cmd)
        {
            case CcpCommandCode.Disconnect:
                _connected = false;
                if (command[2] == 0) _temporary = true;
                else StopAll();
                break;
            case CcpCommandCode.GetCcpVersion:
                _crm[3] = 2; _crm[4] = 1;
                break;
            case CcpCommandCode.ExchangeId:
                _crm[3] = (byte)Target.SlaveId.Length;
                _crm[4] = 0; _crm[5] = 3; _crm[6] = 0; _crm[7] = 0x8E;
                _mta0 = IdAddress;
                break;
            case CcpCommandCode.SetMta:
                if (command[2] != 0) _crm[1] = 0x32;
                else _mta0 = BinaryPrimitives.ReadUInt32BigEndian(command[4..]);
                break;
            case CcpCommandCode.Upload:
                ReadMemory(ref _mta0, command[2]);
                break;
            case CcpCommandCode.ShortUpload:
                _mta1 = BinaryPrimitives.ReadUInt32BigEndian(command[4..]);
                ReadMemory(ref _mta1, command[2]);
                break;
            case CcpCommandCode.Download:
                CheckSize(command[2]);
                WriteMemory(command.Slice(3, command[2]));
                break;
            case CcpCommandCode.Download6:
                WriteMemory(command[2..]);
                break;
            case CcpCommandCode.BuildChecksum:
                _crm[3] = 2;
                _crm.AsSpan(4).Clear();
                _mta1 = _mta0;
                _checksumRemaining = BinaryPrimitives.ReadUInt16BigEndian(command[4..]);
                return; // completion occurs only in the main loop, including 'no reply' for size zero
            case CcpCommandCode.GetDaqSize:
                int list = command[2];
                _crm[3] = 0;
                _crm[4] = (byte)(list * Target.OdtsPerList);
                if (list < Target.DaqListCount)
                {
                    _listState[list] = 0;
                    _lastOdt[list] = _events[list] = 0;
                    _prescalers[list] = _cycles[list] = 0;
                    _overruns[list] = false;
                    for (int odt = 0; odt < Target.OdtsPerList; odt++)
                        for (int element = 0; element < 7; element++) _entries[list, odt, element] = default;
                    _daqInitialized = true;
                    _daqTransmit.Clear(); // a global queue reset, even when other lists are active
                    _crm[3] = (byte)Target.OdtsPerList;
                }
                break;
            case CcpCommandCode.SetDaqPointer:
                if (command[2] >= Target.DaqListCount || command[3] >= Target.OdtsPerList || command[4] >= 8)
                { _crm[1] = 0x31; _daqPointer = null; }
                else _daqPointer = (command[2], command[3], command[4]);
                break;
            case CcpCommandCode.WriteDaq:
                if (_daqPointer is not { } pointer || command[2] is not (1 or 2 or 4)) _crm[1] = 0x31;
                else
                {
                    var (l, o, e) = pointer;
                    if (e == 7) { o++; e = 0; } // reproduced alias bug
                    if (o >= Target.OdtsPerList)
                        throw new NotSupportedException("Last-ODT metadata corruption is outside this model.");
                    _entries[l, o, e] = new(BinaryPrimitives.ReadUInt32BigEndian(command[4..]), command[2]);
                }
                break;
            case CcpCommandCode.StartStop:
                if (!_daqInitialized) _crm[1] = 0x22;
                else if (command[2] > 2) _crm[1] = 0x31;
                else if (command[3] < Target.DaqListCount)
                {
                    int l = command[3];
                    _listState[l] = command[2] == 2 ? (byte)4 : command[2];
                    if (command[2] != 0)
                    {
                        _lastOdt[l] = command[4]; _events[l] = command[5];
                        _prescalers[l] = Math.Max((ushort)1, BinaryPrimitives.ReadUInt16BigEndian(command[6..]));
                        _cycles[l] = 1;
                    }
                }
                break;
            case CcpCommandCode.StartStopAll:
                if (!_daqInitialized) _crm[1] = 0x22;
                else if (command[2] == 0) StopAll();
                else if (command[2] == 1)
                {
                    for (int l = 0; l < Target.DaqListCount; l++) if (_listState[l] == 4) _listState[l] = 1;
                }
                else _crm[1] = 0x31;
                break;
            default:
                _crm[1] = 0x30;
                break;
        }
        Reply();
    }

    public void AdvanceMainLoop()
    {
        if (_checksumRemaining == 0 || (_profile.EnableByteAddress is uint enable && Peek(enable) != 1)) return;
        int count = Math.Min(256, _checksumRemaining);
        uint sum = BinaryPrimitives.ReadUInt16BigEndian(_crm.AsSpan(4));
        for (int i = 0; i < count; i++) { sum += Peek(_mta1); _mta1 = unchecked(_mta1 + 1); }
        BinaryPrimitives.WriteUInt16BigEndian(_crm.AsSpan(4), (ushort)(sum & 0xFFFF));
        _checksumRemaining -= count;
        if (_checksumRemaining == 0) Reply();
    }

    public void QueueEvent(int eventChannel)
    {
        if (_profile.EnableByteAddress is uint enable && Peek(enable) != 1) return;
        for (int l = 0; l < Target.DaqListCount; l++)
        {
            if (_listState[l] != 1 || _events[l] != eventChannel || --_cycles[l] != 0) continue;
            _cycles[l] = _prescalers[l];
            int count = _lastOdt[l] + 1;
            if (_daqTransmit.Count + count > Target.DaqListCount * Target.OdtsPerList)
            { _overruns[l] = true; continue; }
            for (int o = 0; o < count; o++)
            {
                var bytes = new byte[8];
                bytes[0] = (byte)(Target.ExpectedFirstPid(l) + o | (_overruns[l] ? 0x80 : 0));
                int offset = 1;
                for (int e = 0; e < 7 && offset < 8; e++)
                {
                    Entry entry = _entries[l, o, e];
                    if (entry.Address == 0) break;
                    // The actual builder copies the full element; only seven data bytes go on the wire.
                    for (uint b = 0; b < entry.Size && offset < 8; b++) bytes[offset++] = Peek(entry.Address + b);
                }
                _daqTransmit.Enqueue(bytes);
                _overruns[l] = false; // successful first DTO consumes the pending overrun flag
            }
        }
    }

    public void DrainDaq()
    {
        while (_daqTransmit.TryDequeue(out var bytes)) Receive(bytes);
    }
    public IReadOnlyList<CcpReceivedFrame> Read(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (timeoutMilliseconds is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        cancellationToken.ThrowIfCancellationRequested();
        var frames = _received.ToArray();
        _received.Clear();
        return frames;
    }
    public void Dispose() { _disposed = true; _received.Clear(); _daqTransmit.Clear(); }
    private void BeginReply(byte counter) { _crm[0] = 0xFF; _crm[1] = 0; _crm[2] = counter; }
    private void Reply()
    {
        if (_profile.EnableByteAddress is not uint enable || Peek(enable) == 1) Receive(_crm);
    }
    private void Receive(byte[] bytes) => _received.Enqueue(new(bytes.ToArray(), Timestamp++));
    private void StopAll() => Array.Clear(_listState);
    private static void CheckSize(int size)
    {
        if (size > 5) throw new NotSupportedException("Oversized transfer buffer corruption is outside this model.");
    }
    private void ReadMemory(ref uint mta, int size)
    {
        CheckSize(size);
        for (int i = 0; i < size; i++) { _crm[3 + i] = Peek(mta); mta = unchecked(mta + 1); }
    }
    private void WriteMemory(ReadOnlySpan<byte> data)
    {
        foreach (byte value in data) { Load(_mta0, [value]); _mta0 = unchecked(_mta0 + 1); }
        // Bytes 3–7 deliberately remain stale instead of reporting the post-incremented MTA.
    }
}
