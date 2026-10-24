namespace LotusECMLogger.Services.A2l
{
    /// <summary>
    /// Sorts T6 calibration names into subsystems by their first word (<c>CAL_inj_…</c> is fuel, <c>CAL_ign_…</c>
    /// ignition). The generator writes these as A2L GROUPs, which the owner can rearrange by hand; the editor uses the
    /// same rules for an A2L that has no groups. Words not listed go to "Other".
    /// </summary>
    public static class CalibrationGroups
    {
        public sealed record Subsystem(string Name, string Title, IReadOnlyList<string> Prefixes);

        public static IReadOnlyList<Subsystem> Subsystems { get; } =
        [
            new("Fuel", "Fuel and injection", ["inj", "injtip", "fuel", "closedloop", "lfb", "dfco", "o2"]),
            new("Ignition", "Ignition and knock", ["ign", "knock", "roughness"]),
            new("Airflow", "Airflow and load", ["load", "maf", "tmap", "airbox", "acis", "cac", "air", "sensor"]),
            new("Throttle", "Throttle", ["tps", "tps1", "tpssmooth", "tpstarget", "etb", "throttle"]),
            new("Idle", "Idle", ["idle"]),
            new("Torque", "Torque and limits", ["torque", "revlimit", "overrev", "dpm", "slip", "racetc", "wheel"]),
            new("Vvt", "Variable valve timing", ["vvt"]),
            new("Cooling", "Cooling and temperatures", ["cooling", "coolant", "oil", "temp", "engine"]),
            new("Emissions", "Emissions and evaporative", ["evap", "cat", "emissions", "exhaust", "misfire"]),
            new("Diagnostics", "OBD-II diagnostics", ["obd"]),
            new("Driveline", "Transmission and driveline", ["trans", "ips", "gear", "gsi", "clutch"]),
            new("Climate", "Air conditioning", ["ac"]),
            new("Cruise", "Cruise control", ["cruise"]),
            new("Vehicle", "Vehicle and body", ["vehicle", "cluster", "tpms", "can", "dca", "accessory"]),
            new("Other", "Other", []),
        ];

        private static readonly Dictionary<string, Subsystem> ByPrefix = Subsystems
            .SelectMany(s => s.Prefixes.Select(p => (p, s))).ToDictionary(x => x.p, x => x.s, StringComparer.OrdinalIgnoreCase);

        /// <summary>The first word after <c>CAL_</c> (or of the name, without it), lower-cased.</summary>
        public static string Prefix(string name)
        {
            string rest = name.StartsWith("CAL_", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
            int end = rest.IndexOf('_');
            return (end > 0 ? rest[..end] : rest).ToLowerInvariant();
        }

        public static Subsystem Classify(string name) =>
            ByPrefix.TryGetValue(Prefix(name), out var subsystem) ? subsystem : Subsystems[^1];
    }
}
