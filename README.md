# LotusECMLogger

**LotusECMLogger** is a free, open-source logging tool designed specifically for Lotus sports cars. It supports both standard OBD-II Mode 01 and manufacturer-specific OBD-II Mode 22, enabling you to capture a wide range of engine and vehicle data.

With LotusECMLogger, you can log not only generic OBD-II parameters, but also Lotus-specific data such as variable cam control, knock control, and other advanced diagnostics. This makes it an invaluable tool for enthusiasts, tuners, and anyone interested in monitoring or troubleshooting their Lotus vehicle.

- **Supports OBD-II Mode 01**: Standard parameters like RPM, speed, coolant temperature, etc.
- **Supports OBD-II Mode 22**: Manufacturer-specific channels, including advanced Lotus data.
- **Capture Lotus-specific data**: Log unique parameters such as variable cam control, knock control, and more.
- **ECU configuration & diagnostics**: Read and modify ECU coding, read trouble codes, program the VIN, and reset learned/adaptive data.
- **Advanced T6e tools**: RAM (RMA) logging, live calibration tuning, calibration flashing, and model-info erase for firmware migration.
- **Free and open source**: No cost, no restrictions, and community-driven development.

![GUI](screenshots/Screenshot-20260504.png)

## Requirements

- **Lotus Vehicle with CAN**: This should be any 2008+ model.
- **x86 Windows Computer**: Tested with Windows 11, but Windows 7+ is supported. Note that the software is 32bit.
- **J2534-compliant Pass-Thru Device**: This is a widely supported industry standard. Beware cheap devices that are not standards compliant.

## Supported Adapters

LotusECMLogger should work with an J2534-compliant pass-thru device connected via USB. Popular options include:

- **Tactrix OpenPort 2.0**: (discontinued) A widely used J2534 device known for its reliability and performance.
- **TopDon RLink X3**: Requires J2534 driver download from TopDon

## Known Incompatible Adapters

- **GO-DIAG GD101**: Low-cost device. Known to have driver issues and is not recommended.

## User Interface Features

LotusECMLogger provides a tabbed interface with specialized tools for different diagnostic and logging tasks:

### Vehicle Information
The Vehicle Information tab retrieves static and learned data from the ECU. It queries OBD-II Mode 09 for identification data — VIN, ECU name, calibration ID, and calibration verification number (displayed as hex) — and Mode 22 for per-cylinder octane scaler values, which indicate how much knock-based fuel correction has been accumulated for each cylinder. After a load, the tab probes the ECU over raw CAN and shows an **unlock indicator** — UNLOCKED, LOCKED, or UNKNOWN — since an unlocked ECU is required for advanced operations such as Erase Model Info, T6 RMA Logging, and T6 Live Tuning.

The tab also hosts two write operations:

- **Set VIN** — Opens a dialog that programs a new VIN using OBD-II Mode 0x3B. The Lotus firmware only allows positions 4–17 to be rewritten (the `SCC` WMI is fixed), validates the entry as you type, and requires the engine to be off; the result is verified by reading the VIN back after programming.
- **Learned Data Reset** — Performs an OBD-II Mode 0x11 reset to clear adaptive learning values (octane scalers, knock retard, alpha-N load trim, torque-to-throttle scaling, per-bank fuel trim, and idle learning), which may be necessary after certain repairs or modifications.

### Live Data
The Live Data tab contains two sub-tabs:

**Logger** — Displays real-time OBD-II parameters from your Lotus vehicle in an easy-to-read list. You can start and stop logging sessions, which automatically saves data to CSV files in your Documents folder for later analysis. The active logging configuration is selected from a dropdown; configurations determine which ECUs and PIDs are polled each session. Wideband sensors are fully supported: live lambda and air-fuel ratio (Mode 01 PIDs 0x24/0x25) plus the per-bank calibration parameters (slope and offset, Mode 22 PIDs 0x0403/0x0404).

**Logging Config** — A full configuration editor for creating and managing logging configuration files. You can add and remove ECUs, set each ECU's CAN request and response IDs, and build a list of OBD requests (Mode 01 or Mode 22) with names, descriptions, categories, units, and PID values. Configurations are saved as JSON files and are immediately available in the Logger sub-tab without restarting the application.

### ECU Coding
The ECU Coding tab allows you to read and modify ECU configuration settings for Lotus T6e ECUs. You can view current coding values, make changes to vehicle configuration options, and write the updated settings back to the ECU with automatic backup creation for safety.

### Diagnostic Trouble Codes
The Diagnostic Trouble Codes (DTC) tab reads stored trouble codes from the ECU using OBD-II Mode 03 and lists each code alongside its category, helping you diagnose issues and monitor faults stored in your vehicle's engine management system. Clearing trouble codes from this tab is not yet implemented.

### T6 RMA Logging
The T6 RMA (Remote Memory Access) Logging tab enables direct reading of ECU memory addresses for advanced diagnostics and development. You can specify any valid RAM address (0x40000000-0x4000FFFF), configure the number of bytes to read and polling interval, then log the data as a time series to CSV. This feature requires a debug-enabled ECU with developer calibration and provides real-time hex dump, ASCII, and numeric interpretations of the memory contents.

### T6 Live Tuning
The T6 Live Tuning tab enables real-time calibration editing by monitoring .CPT calibration files and automatically writing changes to ECU memory. This feature supports two workflows: reading memory directly from the ECU to create a new calibration file, or loading an existing .CPT file for monitoring. Memory presets are available for common calibration regions. When monitoring is active, any edits made to the .CPT file are detected within 100ms and immediately written to the corresponding ECU memory address, with detailed logging showing the memory address, file offset, and old/new values for each change. This requires a debug-enabled ECU with developer calibration.

### T6E Calibration Flasher
Available from the **Tools** menu, the T6E Calibration Flasher provides a convenient interface for flashing ECU calibrations to Lotus T6e engine control units. The tool supports both .CRP and .CPT file formats, automatically converting .CPT files to XTEA-encrypted .CRP format (CRP08) before flashing to ensure compatibility with the ECU's flash programming protocol.

The flasher does not program the ECU directly. It prepares the calibration file and launches **EFI_PROT.EXE** — the external T6 flash programming utility (part of the T6_ECU_FIX package) — which then carries out the flash from its own console window. When EFI_PROT starts, you select the J2534 pass-thru device to use, so make sure the device is connected first. The vehicle must be powered off for the flashing procedure. Note that EFI_PROT must establish communication with the ECU within a limited time window; if it times out, the ECU locks out flashing. An ignition cycle does **not** clear this lockout — you must remove power from the ECU or wait several minutes before retrying — so be ready to proceed promptly once the tool launches.

### Erase Model Info
Available from the **Tools** menu, Erase Model Info clears the model identification stored in the ECU's variant coding so the currently installed firmware can re-initialize it. This is primarily used when migrating firmware versions: flashing a new calibration does not update the stored model info, so the ECU reports a program-version mismatch and the new firmware is not fully activated. Erasing the field (filling it with 0xFF) causes the firmware to re-seed it from the freshly flashed calibration's program version on its next coding-initialization cycle, committing the change to EEPROM. The operation requires an unlocked ECU and the correct firmware version to be selected (which resolves the target command-register address); it is disabled while logging is active and cannot be undone.

## Developer TODOs

### UI / UX
- **Tab and button icons are not visible in the Visual Studio designer.** Icons are applied at runtime using Segoe MDL2 Assets glyph rendering (`GuiIcons.cs`), but the WinForms designer only executes `InitializeComponent()` and does not run post-constructor code. Fix: pre-render glyphs to PNG and store them as embedded resources in the project `.resx` file, then reference them via `Properties.Resources` in `InitializeComponent()` so the designer can read and re-serialize them.
- **`MainWindow` silently swallows exceptions** in three constructor `try/catch` blocks when creating `OBDLoggerControl`, `EcuCodingControl`, and `T6RMAControl`. If a control fails to initialize the tab just appears empty with no feedback. Add proper error reporting.
- **`MainWindow.OnLoggerStateChanged` also swallows exceptions silently.** Any failure propagating logger state to child controls is hidden.

### Threading / Correctness
- **`VehicleInfoControl.LoadVehicleData` blocks the UI thread.** All J2534 work (connection, filter setup, PID queries, octane scaler reads) runs on the UI thread, freezing the app for the duration. Move to `Task.Run()` and use `Invoke`/`BeginInvoke` for status label updates and the final ListView refresh. (`VehicleInfoControl.cs`)
- **`liveData` dictionary has a data race.** It is written by the background logger thread and read on the UI thread with no synchronization. Fix by using `ConcurrentDictionary` or capturing a snapshot under a lock before dispatching to the UI thread. (`OBDLoggerControl.cs`)

### Code Quality
- **`VehicleInfoService` is instantiated but never used.** All protocol work (Mode 0x09, Mode 0x22, octane scalers) is duplicated inline in `VehicleInfoControl`. Consolidate into `VehicleInfoService` and have the control delegate to it. The service currently contains stubs that don't match the real parsing and should be removed.
- **`T6LiveTuningService.ReadEcuImageToFileAsync` is a stub.** The method validates arguments and logs but does not read ECU memory. Needs implementation: validate RAM address range (0x40000000–0x4000FFFF), read in chunks via `T6RMAService`, handle multi-frame reads, write binary output to file.
- **`T6eCodingDecoder` individual backing fields may be redundant.** `_codingDataHigh` and `_codingDataLow` are stored as raw byte arrays alongside the computed `BitField`. Evaluate whether the raw arrays are still needed or can be derived on demand.
- **`T6eCodingDecoder` constructor logic is duplicated.** Initialization is repeated across two constructors; refactor into a shared private method.
- **`T6eCodingDecoder` validation rules are incomplete.** Coding validation only covers a subset of models. Add validation rules for S2, Exige, and Emira variants.
- **`Iso15765Service` response filtering is loose.** After sending a request, the first non-empty message is accepted without checking whether it is actually the expected response. Add response header validation. (`Iso15765Service.cs:110`)

### Protocol / Data
- **Throttle position scaling constant may not be portable.** `LiveDataReading.cs` uses a hard-coded divisor of 77 as the observed max raw throttle value. This may vary across ECU calibrations. Verify and replace with a documented or configurable value.
- **DTC clearing is not implemented.** `DTCControl.clearCodesButton_Click` only shows a "not yet implemented" message. Add OBD-II Mode 0x04 clear; the firmware already zeroes both stored and permanent DTCs when it receives a clear request. (`DTCControl.cs`)