# OBD Configuration Files

This directory contains JSON configuration files for the Lotus ECM Logger application. Each configuration defines which OBD-II parameters to log and their settings.

## Configuration Files

### lotus-default.json
Balanced set of essential Lotus ECU parameters including:
- Standard OBD-II engine data (RPM, throttle, load, coolant temp, timing, MAP)
- Sport button status
- Throttle and accelerator pedal positions  
- Manifold temperature
- Octane scalers for all 6 cylinders

### lotus-fast.json
Minimal parameter set optimized for maximum J2534 performance:
- Core engine data (RPM, throttle, timing)
- Accelerator pedal position

### lotus-complete.json
Comprehensive configuration with 100+ Lotus parameters including:
- Engine management (fuel, air, timing, VVT)
- Performance data (octane scalers, turbo control, sport mode)
- Environmental sensors (temperatures, pressures)
- Transmission parameters (gears, solenoids, learning systems)
- Diagnostic data (error codes, status flags)

### lotus-diagnostic.json
Diagnostic-focused configuration for troubleshooting:
- Extended OBD-II diagnostics
- Fuel trims and error values
- Knock detection and timing errors
- Emissions system status

## JSON Format

```json
{
  "name": "Configuration Name",
  "description": "Description of what this config includes",
  "ecmHeader": [0, 0, 7, 224],
  "requests": [
    {
      "type": "Mode01",
      "name": "Request Name",
      "pids": [12, 17, 67],
      "description": "Optional description",
      "category": "Optional category"
    },
    {
      "type": "Mode22", 
      "name": "Request Name",
      "pidHigh": 2,
      "pidLow": 93,
      "category": "Optional category",
      "unit": "Optional unit"
    }
  ]
}
```

## Field Descriptions

### Configuration Level
- `name`: Human-readable configuration name
- `description`: Description of the configuration purpose
- `ecmHeader`: 4-byte ECM header (typically [0, 0, 7, 224] for Lotus)
- `requests`: Array of OBD request objects

### Request Level
- `type`: Either "Mode01" (standard OBD-II) or "Mode22" (manufacturer-specific)
- `name`: Human-readable request name
- `description`: Optional description
- `category`: Optional category for grouping
- `unit`: Optional unit of measurement

### Mode01 Requests
- `pids`: Array of PID bytes to request in a single message

### Mode22 Requests  
- `pidHigh`: High byte of the PID
- `pidLow`: Low byte of the PID

## Creating Custom Configurations

1. Copy an existing configuration file
2. Modify the `name` and `description` fields
3. Add/remove/modify requests as needed
4. Save with a `.json` extension
5. Load using `OBDConfiguration.LoadFromConfig("your-config-name")`

## Usage in Code

```csharp
// Load predefined configuration
var config = OBDConfiguration.CreateLotusDefault();

// Load custom configuration
var config = OBDConfiguration.LoadFromConfig("my-custom-config");

// Load from specific file path
var config = OBDConfiguration.LoadFromFile("path/to/config.json");

// Get list of available configurations
var available = OBDConfigurationLoader.GetAvailableConfigurations();
```

## Parameter Sources

The complete parameter list is based on documentation from the LotusECU-T4e repository, specifically the Mode22-Live.csv file which contains comprehensive Lotus ECU parameter definitions. 