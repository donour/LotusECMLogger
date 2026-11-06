# LotusECMLogger - Project Documentation

## Project Overview

**LotusECMLogger** is a free, open-source diagnostic and logging tool specifically designed for Lotus sports cars. It provides real-time monitoring, logging, and diagnostic capabilities through OBD-II communication.

### Key Capabilities
- Real-time monitoring of engine and vehicle parameters via OBD-II
- Support for both standard OBD-II Mode 01 and Lotus-specific Mode 22 manufacturer extensions
- ECU coding configuration reading and modification
- Vehicle information retrieval (VIN, ECU details via OBD Mode 09)
- Learned data reset capabilities (OBD Mode 0x11)
- CSV export of logged data for analysis
- ECU firmware flashing utilities (experimental)

### Technology Stack
- **.NET 8.0** (Windows Forms application)
- **C# 11+** with nullable reference types enabled
- **J2534-Sharp** (v1.0.0-CI00036) for hardware communication
- **System.Text.Json** for configuration management
- **ISO 15765 (ISO-TP)** protocol over CAN-bus

## Project Structure

## Key Components

### Core Communication Components

#### J2534OBDLogger (250+ lines)
Main logging engine that:
- Manages J2534 hardware connection
- Runs background logging thread
- Parses CAN responses
- Coordinates CSV writing
- Exposes data via event callbacks

#### Iso15765Service (250+ lines)
ISO-TP protocol implementation:
- Multi-frame message assembly/disassembly
- OBD request sending
- Response aggregation with timeout handling
- Standard ECM header: [0x00, 0x00, 0x07, 0xE0]

#### LiveDataReading
Data model class:
- Represents parsed OBD parameter
- Contains name, float value, long value
- Static `ParseCanResponse()` for OBD Mode 01/22 decoding

### Configuration System

#### OBDConfiguration
Configuration container:
- ECM header (4 bytes)
- List of IOBDRequest objects (Mode01Request, Mode22Request)
- Message building methods

#### OBDConfigurationLoader (250+ lines)
JSON configuration management:
- Deserializes JSON configuration files
- Custom `IntArrayByteConverter` for JSON byte arrays
- File discovery and caching


### ECU Coding System

#### T6eCodingDecoder (489 lines)
Sophisticated bit-level decoder:
- Decodes 64-bit ECU coding field (two 4-byte arrays)
- Supports 40+ vehicle configuration options
- Boolean flags, multi-bit fields, numeric ranges
- Both property-based and dictionary-based access patterns

#### J2534EcuCodingService
ECU coding service:
- Mode 22 requests for coding read (PIDs 0x2263, 0x2264)
- Raw CAN writing for coding updates
- Creates T6eCodingDecoder from responses


### HC08 TPS Patching (Hc08Tools library)

C# port of `hc08_patcher/hc08_tps_sync.py`; the spec and test vectors are in `hc08_patcher/HC08_TPS_firmware_update.md`.
- `Hc08TpsPatcher.Analyze(prog, cal)`: pure function; runs all safety checks and builds a re-verified patched PROG in memory
- `Hc08TpsPatcher.Apply(path, analysis, backup)`: timestamped backup + atomic replace; refuses if the file changed since analysis
- `Hc08TpsProfiles.Known`: per-firmware table locations keyed by calibration ID (only C132E0278 so far)
- Deliberate divergence from the Python/spec: an rpm axis mismatch is not an error. The CAL axis is converted to HC08 units (never copied raw) and written over the HC08 axis, then the tables are compared on it. Both axes, and the converted one, must be strictly increasing
- UI: `Hc08TpsPatchDialog` (Tools > HC08 TPS Table Patch…); tests: `Hc08TpsPatcherTests` (synthetic images reproduce the spec's vectors)

### A2L tables (CCP live tuning, Phase 6)

`LotusECMLogger/Services/A2l` reads and drafts ASAP2 files; the design and status are in `CCP-client-plan.md` §4 and §6.
- **`A2lGenerator`** drafts an A2L from a build's symbol catalog:
  - `CAL_x` + `CAL_x_X_*`/`CAL_x_Y_*` become a CURVE/MAP with COM_AXIS, `ROW_DIR` (X fastest, as the firmware's
    `lut[y * size_x + x]`);
  - every axis is `NO_INPUT_QUANTITY`; inputs are added by hand after checking the decompile.
- **`A2lTool generate <build>… --out-dir LotusECMLogger/config/a2l`** writes the files (it keeps existing ones unless
  `--force`). **`A2lTool check <file>`** lists what the app will refuse.
- **`A2lParser`** is a tolerant subset reader built on a generic `/begin … /end` tree. **`A2lLayout.Resolve`** turns
  items into `A2lCalibrationItem`s (`CellAddress(x, y)`, axes, conversion). Anything unsupported is unavailable with
  a reason and never defaulted. Conversions without an inverse are read-only.
- **`A2lFormula`** evaluates FORM formulas, including C bit operators.
- **Tests:** `LotusECMLogger.Ccp.Tests/A2lTests.cs` pins the map layout against two firmware lookups.
- **Axis inputs (`AxisInputs`):** `A2lTool extract-inputs <decompile.c> <build> --out-dir LotusECMLogger/config/a2l`
  drafts `<build>.inputs.csv` from the decompile's lookup calls. These are raw expressions as the lookup sees them,
  mask included, kept only where every non-constant call site agrees. Locals are followed to the one variable they
  hold (through trivial getters, ignoring constant defaults and fallbacks) and `DAT_` globals are named by address;
  the `note` column says when. A local the ECU clamps at its maximum is left unmasked (the cursor clamps at the axis
  end; a mask would wrap). Draft from the `t6-patch` decompiles; when the catalog has renamed symbols since the
  decompile, pass `--aliases <the catalog the decompile was named from>` so its names are read by address
  (`git show <rev>:LotusECMLogger/config/highSpeedLogger/database/<build>.csv`). Review the file, then regenerate;
  the generator turns each row into an InputQuantity (a VIRTUAL measurement for an expression).
- **Catalog updates:** a renamed or moved symbol breaks presets that name it. Remap preset channels by their old
  address, and regenerate the inputs (with `--aliases`) and the A2L; `HighSpeedLogGoldenTests` checks the presets.
- **`A2lTool check <a2l> --image <.bin or .CRP>`** also checks the A2L against a real calibration (EPK, segment) and
  lists axes whose points decrease in it.
- **`Services/Tuning/CalibrationImage`** (Phase 6b) is the calibration file being edited:
  - offset = address − CAL_base; it loads raw, CRP-chunk, 64 KB Snapshots and `.CRP` files and saves the same
    layout back (Save As only, never over the source, backing up an existing target);
  - A2L-based physical/raw cell edits are atomic undo steps, refusing read-only items, out-of-limit values,
    decreasing axis points and the CCP enable byte;
  - `Changed` reports the byte spans each edit, undo and redo touched.
- **`Services/Tuning/LiveTuningSession`** (Phase 6c) keeps ECU calibration RAM in step with an image over the shared
  `CcpMaster`:
  - it reads and compares on start (send the file / take the ECU's values), then writes every image change in Live
    mode, or on Apply in Hold mode, as read-back-verified runs;
  - a checksum before each batch catches changes made outside the session, and any failure stops sync until a
    refresh;
  - the enable byte is never written; sending a whole file needs the engine off.
- **Recording and replay** (`Services/Tuning/Recording`, Phase 6f; `CCP-client-plan.md` §4):
  - `TuningRecorder` logs every A2L axis input (and a preset's channels) with its own `HighSpeedLogService`, records
    live sync's verified writes (`LiveTuningSession.Written`) in `X.edits.csv`, and saves `X.cal-start.bin` and
    `X.cal-end.bin`. While it runs it is the live cursor's value source (`IRawValueSource`), so no per-table watch;
  - every high-speed log's preamble has a `# channel:` line per channel (`LoggedChannel`: address, size, signedness,
    scale, offset) and `# build:`, so values turn back into exact raw values;
  - `TuningRecording` reads a log back as per-channel change series; `ReplayCalibration` is the calibration at time t
    (start snapshot plus the writes so far); `CellActivity` is a table's time per cell, split by the lookup's bilinear
    weights on the axes of each moment;
  - `TableInputs` resolves a table's bound inputs once and samples them from any `IRawValueSource`.

### UI Components

#### MainWindow - Top-Level Tabs
1. **Vehicle Information**: VIN, calibration ID, CVN (hex), octane scalers per cylinder; includes Learned Data Reset button (OBD Mode 0x11)
2.**Performance History**: Variant-aware Evora Mode 22 0x03xx reader with Overview, Usage, and Events subtabs  
3. **Live Data**: Sub-tabs — High-Speed Log and OBD-II Logging (Logger + Logging Config)
4. **ECU Coding**: Vehicle configuration bit field editor
5. **Diagnostic Trouble Codes**: Read stored (Mode 03), pending (Mode 07), and permanent (Mode 0A) codes; read the freeze frame (Mode 02) captured when a code set — triggering DTC plus decoded parameters, raw hex for undecoded PIDs; clear codes and freeze frames (Mode 04) with confirmation
6. **Emissions**: OBD-II inspection pre-check (`IEmissionsService` / `J2534EmissionsService`): readiness monitors with support and completion since clear (PID 0x01) and this drive cycle (PID 0x41), MIL, OBD standard, since-cleared counters, stored/pending/permanent DTCs, VIN. `EmissionsCheckEvaluator` judges the read against California Smog Check or federal I/M rules by model year (decoded from the VIN, user-overridable), citing BAR's OBD Test Reference and 40 CFR 85.2207/85.2222; California's "Modified Software" check compares Cal ID / CVN against `StockSoftwareCatalog` (`config/stock_software.json`, ships empty because BAR's list is unpublished); pure decoding lives in `EmissionsDecoder`
7. **Snapshots**: One-shot `.bin` downloads of T6 (2011+) flash regions (Learned Data, Calibration, Program) over the shared CCP session (`CcpFlashSnapshot`, `T6FlashRegion`); each read is checked against the ECU's own BUILD_CHKSUM of the region before it is saved, and the button cancels a running download
8. **CCP**: CCP 2.1 master UI on the shared `CcpSessionHost`. `CcpControl` holds connect/identify (slave ID, version, resources, build from the calibration ID, CAL RAM range) above five sub-tabs and a shared log: **Memory** (read to hex/`.bin`, checksum or byte-level diff against a file with symbol/table-cell names, guarded calibration-RAM writes via `CcpCalibrationWriter` and Restore Backup), **Tuning** (`CcpTuningPanel`, modelled on the owner's RaiderLite editor: open a calibration file with its `config/a2l` A2L, browse items in a tree of A2L GROUPs (`CalibrationTree`), edit in `CalibrationGrid` with a fine/coarse/value toolbar (`StepField`), multiply, interpolate both/vertical/horizontal, smooth, revert, paste and undo, beside a Direct2D graph (`Controls/Plotting`: lines or a 3D surface, GDI+ fallback), Save As, and live sync to the ECU through `LiveTuningSession`; while Tuning is selected the CCP header shrinks to its button row and the log to a one-line strip, giving the editor most of the window; a live cursor (`LiveCursor`, Phase 6e) streams each table's bound axis inputs over DAQ and marks where the ECU is reading it in the grid and graph; Record logs a replayable session (`TuningRecorder`) that also feeds the cursor, and Open Recording replays one: a timeline (`ReplayTimeline`) to play or seek, the calibration and cursor as they were, and Cells read shading each cell by time spent there), **Variables** (`CcpVariablesPanel`: catalog joined with the ECU's variable table, live DAQ watch, read once), **Traffic** (`CcpTrafficPanel`: session counters and latency, DAQ list owners, decoded command trace), **Self-Test** (`CcpSelfTestPanel`: read-only `CcpSelfTest` checklist with a saved text report). The session is shared with the High-Speed Log tab, Snapshots, Erase Model Info and the Vehicle Info probe. `CcpControlUiTests` drives the tab end to end against the simulator; design and status in `CCP-client-plan.md`

#### EcuCodingControl (250+ lines)
ECU coding editor:
- Read/write button handlers
- Dynamic UI generation from decoder options
- Change tracking and modification
- Backup creation before writes

#### VehicleInfoControl (250+ lines)
Vehicle information display:
- Async vehicle data loading
- ListView presentation
- PID discovery and querying

#### ObdResetControl
Learned data reset UI:
- Safe reset operation with confirmation dialogs
- OBD Mode 0x11 implementation

