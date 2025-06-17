# T6e ECU Coding Decoder

A C# library for decoding T6e ECU coding data from Lotus vehicles. This library provides structured access to vehicle configuration options stored in a 4-byte coding array.

## Overview

The T6e ECU stores vehicle configuration data in a 4-byte coding array where each bit or group of bits represents different vehicle features and options. This decoder provides a clean, object-oriented interface to access these settings.

## Features

- **Complete Coverage**: Decodes all 32+ vehicle configuration options
- **Type Safety**: Strongly typed properties for each option
- **Flexible Access**: Multiple ways to access decoded data
- **Error Handling**: Proper validation and exception handling
- **Documentation**: Comprehensive XML documentation

## Usage

### Basic Usage

```csharp
// Create decoder with 4-byte coding data
byte[] codingData = { 0x12, 0x34, 0x56, 0x78 }; // Example values
var decoder = new T6eCodingDecoder(codingData);

// Access individual properties
string driverPosition = decoder.DriverPosition; // "LHD" or "RHD"
string speedUnits = decoder.SpeedUnits; // "MPH" or "KPH"
int numberOfGears = decoder.NumberOfGears; // Raw numeric value
string sportButton = decoder.SportButton; // "False" or "True"
```

### Accessing All Options

```csharp
// Get all options as a dictionary
var allOptions = decoder.GetAllOptions();
foreach (var option in allOptions)
{
    Console.WriteLine($"{option.Key}: {option.Value}");
}

// Get all raw numeric values
var rawValues = decoder.GetAllRawValues();
foreach (var value in rawValues)
{
    Console.WriteLine($"{option.Key}: {value.Value}");
}
```

### Using Method-Based Access

```csharp
// Get specific option by name
string cruiseSystem = decoder.GetOptionValue("Cruise System");
int fuelCapacity = decoder.GetOptionRawValue("Fuel Tank Capacity");

// Get available option names
string[] availableOptions = decoder.GetAvailableOptions();
```

### Utility Methods

```csharp
// Get raw data
byte[] rawData = decoder.CodingData;
ulong bitField = decoder.BitField;

// String representations
string formatted = decoder.ToString(); // Human-readable format
string hexString = decoder.ToHexString(); // "12 34 56 78" format
```

## Available Options

The decoder supports the following vehicle configuration options:

### Vehicle Systems
- **Oil Cooling System**: Standard, Additional
- **Oil Sump System**: Standard, Upgrade
- **Fuel Tank Capacity**: Numeric value (0-127)
- **Auxiliary Cooling Fan**: True/False

### Transmission
- **Transmission Type**: Manual, Auto, MMT
- **Number of Gears**: Numeric value (0-7)
- **Close Ratio Gearset**: True/False
- **Clutch Input**: None, Switch, Potentiometer

### Wheels and Tires
- **Wheel Profile**: 18/19 inch, 19/20 inch
- **Tyre Pressure Monitoring System**: True/False

### Driver Interface
- **Driver Position**: LHD, RHD
- **Speed Units**: MPH, KPH
- **Symbol Display**: ECE(ROW), SAE(FED)
- **Instrument Cluster**: MY08, MY11/12

### Climate Control
- **Heating Ventilation Air Conditioning**: None, Heater Only, Air Conditioning, Climate Control
- **Seat Heaters**: True/False

### Safety Systems
- **Anti-Lock Braking System**: True/False
- **Steering Angle Sensor**: True/False
- **Yaw Rate Sensor**: True/False
- **Japan Seatbelt Warning**: True/False

### Performance Features
- **Cruise System**: None, Basic, Adaptive
- **Sport Button**: True/False
- **Launch Mode**: True/False
- **Race Mode**: True/False
- **Speed Limiter**: True/False
- **Speed Alert Buzzer**: True/False

### Exhaust and Engine
- **Exhaust Bypass Valve Override**: True/False
- **Exhaust Silencer Bypass Valve**: True/False
- **DPM Switch**: True/False

### Comfort and Convenience
- **Central Door Locking**: True/False
- **Powerfold Mirrors**: True/False
- **Reverse Camera**: True/False
- **TC/ESP Button**: True/False

### Control Modules
- **Body Control Module**: True/False
- **Transmission Control Unit**: True/False

### Lighting
- **Rear Fog Fitted**: True/False

## Error Handling

The decoder includes comprehensive error handling:

- **ArgumentException**: Thrown if coding data is not exactly 4 bytes
- **ArgumentException**: Thrown if an unknown option name is provided

## Example

```csharp
using LotusECMLogger;

// Example coding data from a Lotus vehicle
byte[] codingData = { 0x7F, 0x42, 0x00, 0x00 };

var decoder = new T6eCodingDecoder(codingData);

Console.WriteLine("Vehicle Configuration:");
Console.WriteLine($"Driver Position: {decoder.DriverPosition}");
Console.WriteLine($"Speed Units: {decoder.SpeedUnits}");
Console.WriteLine($"Transmission: {decoder.TransmissionType}");
Console.WriteLine($"Gears: {decoder.NumberOfGears}");
Console.WriteLine($"Sport Button: {decoder.SportButton}");
Console.WriteLine($"Launch Mode: {decoder.LaunchMode}");
```

## Integration

This library is designed to integrate seamlessly with the existing LotusECMLogger project. It follows the same coding conventions and namespace structure as the rest of the codebase.

## Testing

A test program (`T6eCodingTest.cs`) is included to demonstrate the functionality and verify correct operation with sample data.

## Bit Layout

The coding data uses a 32-bit field where each option is positioned at specific bit locations:

- Bits 0-7: Various boolean and small options
- Bits 8-15: Additional boolean and small options
- Bits 16-31: Reserved for future use
- Bits 32-63: Larger multi-bit options

The decoder handles the bit manipulation internally, so users don't need to understand the bit layout to use the library effectively. 