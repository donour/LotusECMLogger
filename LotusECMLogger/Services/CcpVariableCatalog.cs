using System.Globalization;
using System.Text;
using LotusECMLogger.Models;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// One ECU variable as the CCP tab shows it: a named address with its scaling, joined from the build's symbol
    /// catalog and the ECU's own variable table (the T6 registry). Either side may be missing: unnamed registry
    /// entries appear as <c>var#index</c>, and catalog symbols outside the registry have no index.
    /// </summary>
    public sealed record CcpVariable(string Name, uint Address, int Size, bool Signed, double Scale, double Offset,
        string Unit, int? RegistryIndex, bool IsCalibration, string Comment)
    {
        /// <summary>DAQ samples 1, 2 or 4 bytes; larger exported objects are read, not watched.</summary>
        public bool Watchable => Size is 1 or 2 or 4;

        public HighSpeedChannel ToChannel() => new()
        {
            Name = Name, Address = Address, Size = (byte)Size, Signed = Signed, Scale = Scale, Offset = Offset, Unit = Unit,
        };
    }

    public static class CcpVariableCatalog
    {
        private const uint RamStart = 0x4000_0000, RamEnd = 0x4001_0000;
        /// <summary>RAM arrays up to this length are offered element by element (e.g. per-cylinder values).</summary>
        public const int MaxExpandedArray = 16;

        /// <summary>RAM variables in address order. Calibration symbols are included and flagged.</summary>
        public static IReadOnlyList<CcpVariable> Build(SymbolCatalog? catalog, T6SymbolRegistry? registry, CcpBuildProfile? build)
        {
            var variables = new List<CcpVariable>();
            var registryByAddress = registry?.Entries.Where(e => e.IsRam).GroupBy(e => e.Address).ToDictionary(g => g.Key, g => g.First())
                ?? [];
            var named = new HashSet<uint>();
            foreach (var entry in catalog?.Entries.DistinctBy(e => (e.Name, e.Address)) ?? [])
            {
                var type = entry.Type;
                bool calibration = IsCalibration(entry.Name, entry.Address, build);
                registryByAddress.TryGetValue(entry.Address, out var exported);
                int? index = exported == default ? null : exported.Index;
                named.Add(entry.Address);
                if (type.ArrayLength > 1)
                {
                    if (calibration || type.ArrayLength > MaxExpandedArray || type.Size is not (1 or 2 or 4)) continue;
                    for (int i = 0; i < type.ArrayLength; i++)
                    {
                        uint address = entry.Address + (uint)(i * type.Size);
                        variables.Add(new CcpVariable($"{entry.Name}[{i}]", address, type.Size, type.Signed, type.Scale,
                            type.Offset, type.Unit, i == 0 ? index : null, false, entry.Comment));
                    }
                    continue;
                }
                int size = type.Size != 0 ? type.Size : exported.Size is 1 or 2 or 4 ? exported.Size : 1;
                variables.Add(new CcpVariable(entry.Name, entry.Address, size, type.Signed, type.Scale, type.Offset, type.Unit,
                    index, calibration, entry.Comment));
            }
            foreach (var exported in registryByAddress.Values.Where(e => !named.Contains(e.Address)))
                variables.Add(new CcpVariable($"var#{exported.Index}", exported.Address, exported.Size, false, 1, 0, "",
                    exported.Index, IsCalibration("", exported.Address, build), "Exported by the ECU; no catalog name."));
            return variables.Where(v => v.Address is >= RamStart and < RamEnd).OrderBy(v => v.Address).ThenBy(v => v.Name).ToList();
        }

        private static bool IsCalibration(string name, uint address, CcpBuildProfile? build) =>
            name.StartsWith("CAL_", StringComparison.Ordinal) || name.StartsWith("LEA_", StringComparison.Ordinal) ||
            build is not null && build.ContainsCalibrationRange(address, 1);
    }

    /// <summary>
    /// Finds which catalog symbol an address falls in, e.g. <c>CAL_inj_warmup_factor_ips[y=3][x=7]</c>. Tables are
    /// stored X fastest; when a <c>CAL_name_X_*</c> axis exists its length gives the row width.
    /// </summary>
    public sealed class SymbolAddressLookup
    {
        private readonly (uint Start, uint End, string Name, int ElementSize, int RowWidth)[] _spans;

        public SymbolAddressLookup(SymbolCatalog catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            var entries = catalog.Entries.DistinctBy(e => (e.Name, e.Address)).ToList();
            var axisLengths = entries
                .Where(e => e.Name.Contains("_X_", StringComparison.Ordinal) && e.Type.ArrayLength > 0)
                .GroupBy(e => e.Name[..e.Name.IndexOf("_X_", StringComparison.Ordinal)])
                .ToDictionary(g => g.Key, g => g.First().Type.ArrayLength);
            _spans = entries
                .Select(e =>
                {
                    int elementSize = Math.Max(1, e.Type.Size);
                    int count = Math.Max(1, e.Type.ArrayLength);
                    int rowWidth = e.Type.ArrayLength > 0 && axisLengths.TryGetValue(e.Name, out int x) && x > 0 && count % x == 0 && count > x
                        ? x : 0;
                    return (e.Address, e.Address + (uint)(elementSize * count), e.Name, elementSize, e.Type.ArrayLength > 0 ? rowWidth : -1);
                })
                .OrderBy(s => s.Item1).ThenByDescending(s => s.Item2)
                .ToArray();
        }

        /// <summary>The symbol (and element) containing <paramref name="address"/>, or null.</summary>
        public string? At(uint address)
        {
            int lo = 0, hi = _spans.Length - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_spans[mid].Start <= address) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            // Walk back over spans that start earlier but still cover the address (overlapping labels).
            for (int i = found; i >= 0 && i > found - 8; i--)
            {
                var span = _spans[i];
                if (address >= span.End) continue;
                if (span.RowWidth < 0) return address == span.Start ? span.Name : $"{span.Name}+{address - span.Start}";
                int element = (int)((address - span.Start) / (uint)span.ElementSize);
                return span.RowWidth > 0
                    ? $"{span.Name}[y={element / span.RowWidth}][x={element % span.RowWidth}]"
                    : $"{span.Name}[{element}]";
            }
            return null;
        }
    }

    /// <summary>A run of bytes that differ between the ECU and a file.</summary>
    public sealed record CcpDiffRun(uint Address, byte[] Ecu, byte[] File, string? Symbol);

    public static class CcpMemoryDiff
    {
        /// <summary>Contiguous differing runs, each attributed to the symbol its first byte falls in.</summary>
        public static IReadOnlyList<CcpDiffRun> Compare(uint address, ReadOnlySpan<byte> ecu, ReadOnlySpan<byte> file,
            Func<uint, string?>? symbolAt = null)
        {
            if (ecu.Length != file.Length) throw new ArgumentException("The ECU and file regions differ in length.");
            var runs = new List<CcpDiffRun>();
            for (int i = 0; i < ecu.Length;)
            {
                if (ecu[i] == file[i]) { i++; continue; }
                int start = i;
                while (i < ecu.Length && ecu[i] != file[i]) i++;
                uint at = address + (uint)start;
                runs.Add(new CcpDiffRun(at, ecu[start..i].ToArray(), file[start..i].ToArray(), symbolAt?.Invoke(at)));
            }
            return runs;
        }

        /// <summary>A fixed-width report: address, length, symbol, then ECU and file bytes (long runs abbreviated).</summary>
        public static string Format(IReadOnlyList<CcpDiffRun> runs, int maxRuns = 1000)
        {
            if (runs.Count == 0) return "No differences.\r\n";
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture,
                $"{runs.Count:N0} differing run(s), {runs.Sum(r => r.Ecu.Length):N0} byte(s).\r\n\r\n");
            foreach (var run in runs.Take(maxRuns))
            {
                text.Append(CultureInfo.InvariantCulture, $"0x{run.Address:X8}  {run.Ecu.Length,5} B  {run.Symbol ?? "(no symbol)"}\r\n");
                text.Append("    ECU:  ").Append(Bytes(run.Ecu)).Append("\r\n");
                text.Append("    File: ").Append(Bytes(run.File)).Append("\r\n");
            }
            if (runs.Count > maxRuns) text.Append(CultureInfo.InvariantCulture, $"… {runs.Count - maxRuns:N0} more run(s) not shown.\r\n");
            return text.ToString();

            static string Bytes(byte[] bytes) => bytes.Length <= 16
                ? Convert.ToHexString(bytes)
                : $"{Convert.ToHexString(bytes, 0, 16)}… (+{bytes.Length - 16})";
        }
    }
}
