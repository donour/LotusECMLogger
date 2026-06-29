namespace LotusECMLogger.Services
{
    /// <summary>One loggable ECU symbol: a named RAM address with its parsed channel type.</summary>
    public sealed class SymbolEntry
    {
        public required string Name { get; init; }
        public uint Address { get; init; }
        public required string RawType { get; init; }
        public required ParsedChannelType Type { get; init; }
        public string Namespace { get; init; } = "";

        /// <summary>
        /// Human-readable description from the catalog's "EOL Comment" column, if present; otherwise empty.
        /// Shown as channel help in the Add Channels dialog.
        /// </summary>
        public string Comment { get; init; } = "";
    }

    /// <summary>Filter criteria for <see cref="SymbolCatalog.Search"/>.</summary>
    public sealed class SymbolFilter
    {
        /// <summary>Space-separated terms; an entry matches only if its name contains all of them.</summary>
        public string Query { get; init; } = "";

        /// <summary>Hide array/table types (calibration maps, ring buffers).</summary>
        public bool HideArrays { get; init; } = true;

        /// <summary>Hide calibration/learned constants (names starting with CAL_ or LEA_).</summary>
        public bool HideCalibration { get; init; } = true;

        /// <summary>If set, only entries with this exact (display) unit.</summary>
        public string? Unit { get; init; }
    }

    /// <summary>
    /// An in-memory, searchable set of loggable symbols for one ECU, built by
    /// <see cref="SymbolCatalogLoader"/> from a Ghidra symbol CSV.
    /// </summary>
    public sealed class SymbolCatalog
    {
        private readonly Dictionary<string, SymbolEntry> _byName;

        public string Name { get; }
        public IReadOnlyList<SymbolEntry> Entries { get; }

        public SymbolCatalog(string name, IReadOnlyList<SymbolEntry> entries)
        {
            Name = name;
            Entries = entries;
            _byName = new Dictionary<string, SymbolEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
                _byName[e.Name] = e; // last wins on the (rare) duplicate name
        }

        /// <summary>Resolves a symbol by exact name, or null if not present.</summary>
        public SymbolEntry? Resolve(string name) => _byName.GetValueOrDefault(name);

        /// <summary>Returns entries matching <paramref name="filter"/>, in catalog (address) order.</summary>
        public IEnumerable<SymbolEntry> Search(SymbolFilter filter)
        {
            IEnumerable<SymbolEntry> q = Entries;

            if (filter.HideArrays)
                q = q.Where(e => e.Type.IsScalar);

            if (filter.HideCalibration)
                q = q.Where(e => !IsCalibration(e.Name));

            if (!string.IsNullOrEmpty(filter.Unit))
                q = q.Where(e => string.Equals(e.Type.Unit, filter.Unit, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(filter.Query))
            {
                var terms = filter.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                q = q.Where(e => terms.All(t => e.Name.Contains(t, StringComparison.OrdinalIgnoreCase)));
            }

            return q;
        }

        private static bool IsCalibration(string name) =>
            name.StartsWith("CAL_", StringComparison.Ordinal) || name.StartsWith("LEA_", StringComparison.Ordinal);
    }
}
