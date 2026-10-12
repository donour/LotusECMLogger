using System.Text;
using System.Text.Json;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Ccp.Simulation;

try
{
    if (args.Length == 0)
    {
        Console.WriteLine("Usage: CcpSimulator <firmware.CRP> [--profile profile.json] [--bus a|c]\n       CcpSimulator --export-profile <profile.json>");
        return 0;
    }
    if (args[0] == "--export-profile" && args.Length == 2)
    {
        File.WriteAllText(args[1], JsonSerializer.Serialize(T6FirmwareProfile.Gt430, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    var profile = T6FirmwareProfile.Gt430;
    byte? bus = null;
    for (int i = 1; i < args.Length; i += 2)
    {
        if (i + 1 == args.Length) throw new ArgumentException("Missing option value.");
        switch (args[i])
        {
            case "--profile": profile = T6FirmwareProfile.FromJson(args[i + 1]); break;
            case "--bus": bus = args[i + 1] switch { "a" => 1, "c" => 2, _ => throw new ArgumentException("Bus must be a or c.") }; break;
            default: throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }
    var image = T6FirmwareImage.FromCrp(args[0], profile);
    using var ecu = new FakeCcpSlave(image, bus);
    var client = new CcpSimulationClient(ecu);
    client.Connect();
    var identification = CcpIdentification.Parse(client.Exchange(CcpFrame.ExchangeId));
    var id = Encoding.ASCII.GetString(client.ReadMemory(profile.CcpIdAddress, identification.IdLength));
    var registry = client.ReadRegistry(profile);
    Console.WriteLine($"{profile.Name}: {id}, FlexCAN {(ecu.Bus == 1 ? "A" : "C")}");
    Console.WriteLine($"PROG SHA256 {image.ProgramSha256}\nCAL  SHA256 {image.CalibrationSha256}");
    Console.WriteLine($"Registry: {registry.BuildId}, {registry.Version}, {registry.Entries.Count} entries");
    foreach (var symbol in registry.Entries)
    {
        var actual = client.ReadMemory(symbol.Address, symbol.Size);
        if (!actual.SequenceEqual(image.Read(symbol.Address, symbol.Size))) throw new InvalidDataException($"Symbol {symbol.Index} read mismatch.");
    }
    var cal = image.Read(profile.CalibrationRamAddress, profile.CalibrationSize);
    if (client.Checksum(profile.CalibrationRamAddress, cal.Length) != CcpFrame.AdditiveChecksum(cal)) throw new InvalidDataException("CAL checksum mismatch.");
    var sample = registry.Entries.First(e => e.IsRam && e.Size == 2 && e.Address < profile.CalibrationRamAddress);
    var initial = image.Read(sample.Address, 2);
    client.WriteRam(sample.Address, [0x12, 0x34]);
    byte pid = client.StartDaq(sample.Address, 2);
    var frames = client.Sample(100);
    if (frames.Count != 10 || frames.Any(f => f.Pid != pid || !f.Data.Span[..2].SequenceEqual(new byte[] { 0x12, 0x34 })))
        throw new InvalidDataException("100 Hz DAQ payload/count mismatch.");
    ecu.Reboot(); client = new CcpSimulationClient(ecu); client.Connect();
    if (!client.ReadMemory(sample.Address, 2).SequenceEqual(initial)) throw new InvalidDataException("Reboot did not restore initialized RAM.");
    Console.WriteLine($"Verified: all {registry.Entries.Count} symbol reads, CAL checksum, RAM write, 10 DAQ samples, reboot restoration.");
    await VerifyMasterAsync(image, bus, sample);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Simulator failed: {exception.Message}");
    return 1;
}

// The same checks through the threaded CcpMaster, with DAQ streaming while memory is read.
static async Task VerifyMasterAsync(T6FirmwareImage image, byte? bus, T6SymbolEntry sample)
{
    var profile = image.Profile;
    using var ecu = new FakeCcpSlave(image, bus);
    using var master = new CcpMaster(new SimulatedCcpTransport(ecu, speedFactor: 20));
    await master.ConnectAsync();
    var identification = await master.IdentifyAsync(CcpBuildProfile.Known.Append(profile.BuildProfile));
    if (identification.Build?.Build != profile.CalibrationId)
        throw new InvalidDataException($"CcpMaster identified {identification.Build?.Build ?? "no build"}.");
    Console.WriteLine($"CcpMaster: {identification.Slave.SlaveId} CCP {identification.Slave.VersionMajor}.{identification.Slave.VersionMinor}, " +
        $"{identification.CalibrationId} \"{identification.CalibrationDescription}\", registry {identification.RegistryBuildId}");

    var daq = await master.StartDaqAsync([new CcpDaqListRequest(0, 1, [[new CcpDaqElement(sample.Address, 2)]])]);
    var written = new TaskCompletionSource();
    int samples = 0, writtenSamples = 0;
    var consumer = Task.Run(async () =>
    {
        await foreach (var frame in daq.Stream.Reader.ReadAllAsync())
        {
            samples++;
            if (frame.Data.Span[..2].SequenceEqual(new byte[] { 0x56, 0x78 }) && ++writtenSamples == 10) written.TrySetResult();
        }
    });
    var registry = await master.ReadSymbolRegistryAsync(identification.Build);
    if (!registry.Entries.SequenceEqual(image.Registry.Entries)) throw new InvalidDataException("CcpMaster registry mismatch.");
    foreach (var symbol in registry.Entries)
        if (!(await master.ReadAsync(symbol.Address, symbol.Size)).SequenceEqual(image.Read(symbol.Address, symbol.Size)))
            throw new InvalidDataException($"CcpMaster symbol {symbol.Index} read mismatch.");
    var cal = image.Read(profile.CalibrationRamAddress, profile.CalibrationSize);
    if (await master.ChecksumAsync(profile.CalibrationRamAddress, cal.Length) != CcpFrame.AdditiveChecksum(cal))
        throw new InvalidDataException("CcpMaster CAL checksum mismatch.");
    await master.WriteVerifiedAsync(sample.Address, new byte[] { 0x56, 0x78 }, new CcpMemoryRange(sample.Address, 2));
    await written.Task.WaitAsync(TimeSpan.FromSeconds(10));
    await daq.StopAsync();
    await consumer;
    await master.DisconnectAsync();
    if (master.StrayReplies + master.MalformedFrames + master.UnroutedDaqFrames + daq.Stream.Dropped != 0)
        throw new InvalidDataException($"CcpMaster saw {master.StrayReplies} stray, {master.MalformedFrames} malformed, " +
            $"{master.UnroutedDaqFrames} unrouted and {daq.Stream.Dropped} dropped frame(s).");
    Console.WriteLine($"Verified via CcpMaster: identification, {registry.Entries.Count} symbol reads and CAL checksum " +
        $"with DAQ streaming ({samples} samples), verified RAM write observed over DAQ.");
}
