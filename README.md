# LotusECMLogger

**LotusECMLogger** is a free, open-source logging tool designed specifically for Lotus sports cars. It supports both standard OBD-II Mode 01 and manufacturer-specific OBD-II Mode 22, enabling you to capture a wide range of engine and vehicle data.

With LotusECMLogger, you can log not only generic OBD-II parameters, but also Lotus-specific data such as variable cam control, knock control, and other advanced diagnostics. This makes it an invaluable tool for enthusiasts, tuners, and anyone interested in monitoring or troubleshooting their Lotus vehicle.

- **Supports OBD-II Mode 01**: Standard parameters like RPM, speed, coolant temperature, etc.
- **Supports OBD-II Mode 22**: Manufacturer-specific channels, including advanced Lotus data.
- **Capture Lotus-specific data**: Log unique parameters such as variable cam control, knock control, and more.
- **Free and open source**: No cost, no restrictions, and community-driven development.

## User Interface Features

LotusECMLogger provides a tabbed interface with specialized tools for different diagnostic and logging tasks:

### Live Data
The Live Data tab displays real-time OBD-II parameters from your Lotus vehicle in an easy-to-read list format. You can start and stop logging sessions, which automatically saves data to CSV files in your Documents folder for later analysis.

### ECU Coding
The ECU Coding tab allows you to read and modify ECU configuration settings for Lotus T6e ECUs. You can view current coding values, make changes to vehicle configuration options, and write the updated settings back to the ECU with automatic backup creation for safety.

### Extended Vehicle Information
The Extended Vehicle Information tab retrieves static vehicle data such as VIN, ECU name, calibration ID, and calibration verification numbers. This information is queried using OBD-II Mode 09 and provides essential identification data about your vehicle's ECU and configuration.

### Diagnostic Trouble Codes
The Diagnostic Trouble Codes (DTC) tab provides functionality for reading and clearing diagnostic trouble codes from the ECU. This feature helps you diagnose issues and monitor fault codes stored in your vehicle's engine management system.

### Learned Data Reset
The Learned Data Reset tab allows you to perform an OBD-II Mode 0x11 reset to clear learned parameters from the ECU. This operation resets adaptive learning values, which may be necessary after certain repairs or modifications, though the ECU will need time to relearn optimal settings afterward.