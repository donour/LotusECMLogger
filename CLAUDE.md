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


### UI Components

#### MainWindow - 6 Tabs
1. **Vehicle Information**: VIN, calibration ID, CVN (hex), octane scalers per cylinder; includes Learned Data Reset button (OBD Mode 0x11)
2. **Live Data**: Sub-tabs — High-Speed Log, OBD-II Logging (Logger + Logging Config), and T6 RMA Logging (direct ECU RAM read/log via RMA protocol)
3. **ECU Coding**: Vehicle configuration bit field editor
4. **Diagnostic Trouble Codes**: Read stored (Mode 03) and permanent (Mode 0A) codes; clear codes and freeze frames (Mode 04) with confirmation
5. **T6 Live Tuning**: Real-time calibration editing via .CPT file monitoring
6. **Snapshots**: One-shot binary downloads of ECU flash regions (Learned Data, Calibration, Program) via the T6 RMA read protocol; an ECU Version selector (T4e, K4, T4, T6/T6e) picks which generation's memory map to use, since each lays out flash differently; shares its `IT6RMAService` instance with the T6 RMA Logging tab

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

