using System.Collections.ObjectModel;

namespace LotusECMLogger.Services.Ccp;

/// <summary>Wire limits for the documented EFI CCP variants; event rates require per-build verification.</summary>
public sealed class CcpTarget
{
    private CcpTarget(string name, string slaveId, uint croId, uint dtoId, int lists, int odts,
        bool hasEnableByte, params int[] rates)
    {
        Name = name;
        SlaveId = slaveId;
        CroId = croId;
        DtoId = dtoId;
        DaqListCount = lists;
        OdtsPerList = odts;
        HasEnableByte = hasEnableByte;
        EventRatesHz = new ReadOnlyDictionary<int, int>(rates.Select((rate, index) => (rate, index))
            .ToDictionary(x => x.index, x => x.rate));
    }

    public string Name { get; }
    public string SlaveId { get; }
    public uint CroId { get; }
    public uint DtoId { get; }
    public int DaqListCount { get; }
    public int OdtsPerList { get; }
    public bool HasEnableByte { get; }
    public IReadOnlyDictionary<int, int> EventRatesHz { get; }

    // Expected fallback only: the master must use GET_DAQ_SIZE's returned first PID.
    public byte ExpectedFirstPid(int list)
    {
        if (list < 0 || list >= DaqListCount) throw new ArgumentOutOfRangeException(nameof(list));
        return (byte)(list * OdtsPerList);
    }

    public static CcpTarget T6 { get; } = new("T6 2011+", "Ccp_T6", 0x350, 0x351, 10, 12, true,
        100, 100, 50, 50, 20, 20, 10, 10, 2, 1);
    public static CcpTarget Evora2010 { get; } = new("2010 Evora C132E0044", "Ccp_GD8", 0x200, 0x201, 1, 10, false, 100);
    public static CcpTarget IpsTcu { get; } = new("IPS TCU C132F0395", "Ccp_TCU", 0x360, 0x361, 10, 12, false,
        100, 100, 50, 50, 20, 20, 10, 10, 2, 1);
    public static IReadOnlyList<CcpTarget> Known { get; } = Array.AsReadOnly(new[] { T6, Evora2010, IpsTcu });
}

/// <summary>Static-analysis profiles, not evidence that the connected ECU matches a selected build.</summary>
public sealed record CcpBuildProfile(string Build, CcpTarget Target, uint CalibrationBase,
    uint CalibrationSize, uint? EnableByteAddress = null, uint? ProgramVersionOffset = null,
    uint? SymbolEntriesAddress = null)
{
    public bool ContainsCalibrationRange(uint address, int length) => length > 0 &&
        address >= CalibrationBase && (ulong)address + (uint)length <= (ulong)CalibrationBase + CalibrationSize;

    public static IReadOnlyList<CcpBuildProfile> Known { get; } = Array.AsReadOnly(new[]
    {
        new CcpBuildProfile("C132E0044", CcpTarget.Evora2010, 0x40002768, 0x4874),
        new CcpBuildProfile("B13200091", CcpTarget.T6, 0x40008E24, 0x61D8, 0x40008E4A,
            SymbolEntriesAddress: 0x000C02E4),
        new CcpBuildProfile("C132E0077", CcpTarget.T6, 0x40008E24, 0x61D8, 0x40008E4A),
        new CcpBuildProfile("C132E0271", CcpTarget.T6, 0x40008654, 0x69AC, 0x400088EC),
        new CcpBuildProfile("A132E0263", CcpTarget.T6, 0x40008654, 0x69AC, 0x400088EC),
        new CcpBuildProfile("C132E0278", CcpTarget.T6, 0x40008654, 0x69AC, 0x400088EC, 0x59D4, 0x000CE984),
        new CcpBuildProfile("E132E0288", CcpTarget.T6, 0x40008654, 0x69AC, 0x400088EC,
            SymbolEntriesAddress: 0x000D2EF4),
        new CcpBuildProfile("A138E0112", CcpTarget.T6, 0x40008654, 0x69AC, 0x400088EC, 0x59D4),
        new CcpBuildProfile("C132F0395", CcpTarget.IpsTcu, 0x40008E58, 0x61A8),
    });
}
