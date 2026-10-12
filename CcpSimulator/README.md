# Firmware-backed T6 fake ECU

The default profile targets Evora GT430 MY19 ROW manual, C132E0278. The executable decrypts
its local CRP using `CrpTools`, verifies the PROG hash and CAL ID/size, maps PROG/CAL flash,
performs the verified boot RAM copies and serves the firmware's CCP command behavior through
`ICcpTransport`. The registry and byte widths come from the actual program.

Run from the repository root (.NET 10, macOS/Linux/Windows):

```sh
dotnet run --project CcpSimulator --configuration Release -- \
  /Users/friss/Desktop/Dev/abs/analysis/stock/program-files/EMS/C132E0278.CRP
```

This performs an end-to-end verification: identification, all 753 symbol reads via CCP,
full CAL checksum, a RAM write, ten 100 Hz DAQ samples, and reboot restoring the original RAM.
It then repeats the checks through the threaded `CcpMaster`
(build identification from the CAL ID, registry and symbol reads, CAL checksum, a verified write) while a DAQ list
streams, and fails on any stray, malformed, unrouted or dropped frame. Failures return exit code 1. The stock CAL selects FlexCAN C (enable byte 2); the default
uses that bus. `--bus a` tests the expected silent timeout on the other bus. Neither command
changes the source CRP. Decrypted images are kept in memory, and no firmware is shipped in Git.

## Use in tests

Reference `CcpSimulator/CcpSimulator.csproj`; the public simulator types are in
`LotusECMLogger.Services.Ccp.Simulation`:

```csharp
var image = T6FirmwareImage.FromCrp(firmwarePath);
using var ecu = new FakeCcpSlave(image);
var driver = new CcpSimulationClient(ecu);
driver.Connect();
var registry = driver.ReadRegistry(image.Profile);
var bytes = driver.ReadMemory(registry.Entries[55].Address, registry.Entries[55].Size);
```

`FakeCcpSlave` directly implements `ICcpTransport` for future master tests. `Send` dispatches
commands, `Read` drains received frames, and `AdvanceMilliseconds` advances the explicit
virtual clock, deferred checksum worker and DAQ sampling/transmission. `Load` injects scenario
bytes into RAM. `Reboot` restores RAM from the immutable image and clears session/DAQ state.
All access is single-threaded; the harness owns the receive queue. `CcpSimulationClient`
is a deterministic single-threaded test driver. To drive the fake from the real `CcpMaster`, wrap it in
`SimulatedCcpTransport`: it serialises access under a lock, advances virtual time from `Read` (capped at
`speedFactor` × wall time, or only via `Advance` when `Paused`), and `With(...)` runs scenario code safely:

```csharp
using var ecu = new FakeCcpSlave(image);
using var master = new CcpMaster(new SimulatedCcpTransport(ecu));
await master.ConnectAsync();
var identification = await master.IdentifyAsync();
```
It retains DAQ packets received during commands and makes a timed-out driver unusable until
a new driver is created after reboot. `Sample` returns accumulated DAQ packets.

The fake models the decompiled CCP handler; it does **not** execute PowerPC instructions,
run engine-control physics, expose a virtual J2534 DLL, or reproduce subsequent boot functions.
Uninitialized RAM is zero until a scenario supplies values. Unknown flash ranges raise errors,
flash writes are rejected, and oversized firmware buffer-corruption paths remain unsupported.
The parameterless fake retains its small synthetic memory mode for protocol regression tests.

## Plug in another program

Export a profile template, then supply a reviewed profile alongside its CRP:

```sh
dotnet run --project CcpSimulator -- --export-profile /tmp/gt430-profile.json
dotnet run --project CcpSimulator -- another.CRP --profile /tmp/another-profile.json
```

JSON addresses are decimal integers. Each profile defines the exact PROG SHA256, CAL ID and
size, CAL RAM destination/enable byte, initialized-data flash source/length, CCP ID RAM address,
and registry pointer/count. `BuildProfile` is derived from these fields. Verify these locations
against that program's decompile and image before enabling it. An NA CRP with the GT430 profile
fails closed instead of reusing wrong addresses. Swapped boot-source, CAL destination and
registry addresses are covered by synthetic profile tests; an actual NA profile is not yet
verified. The common model currently covers the 2011+ T6 CCP variant.

GT430 evidence: PROG starts at flash 0x40000; CAL at 0x20000. Initial data is copied from
0xD38C8 for 0x1648 bytes to 0x40000000; CAL copies to 0x40008654 for 0x69AC bytes. Registry
entries start at 0xCE984 and terminate at 0xCFB2A after 753 records. Entry 31 is a one-byte
object at 0x40001769, not RPM. The decompile-labeled OBD RPM is entry 55 at 0x40001764,
two bytes. Names and scaling still require separate verified bindings.

```sh
dotnet test LotusECMLogger.Ccp.Tests --configuration Release
```

Tests use invented miniature images, including an encrypted CRP round trip, so CI needs no
production firmware. Run the first command separately to repeat validation against the real GT430.
