# T6e ECU Coding Decoder

A C# library for decoding T6e ECU coding data from Lotus vehicles. This library provides structured access to vehicle configuration options stored in two 4-byte arrays (total 8 bytes, 64 bits).

## Overview

The T6e ECU stores vehicle configuration data in two 4-byte arrays (8 bytes total, 64 bits) where each bit or group of bits represents different vehicle features and options. This decoder provides a clean, object-oriented interface to access these settings.

## Features

- **Complete Coverage**: Decodes all 32+ vehicle configuration options
- **Type Safety**: Strongly typed properties for each option
- **Flexible Access**: Multiple ways to access decoded data
- **Error Handling**: Proper validation and exception handling
- **Documentation**: Comprehensive XML documentation

## Usage

### Basic Usage

```csharp
// Create decoder with two 4-byte arrays of coding data
byte[] codingDataLow = { 0x12, 0x34, 0x56, 0x78 };  // Lower 4 bytes
byte[] codingDataHigh = { 0x9A, 0xBC, 0xDE, 0xF0 }; // Higher 4 bytes
var decoder = new T6eCodingDecoder(codingDataLow, codingDataHigh);

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
byte[] lowBytes = decoder.CodingDataLow;
byte[] highBytes = decoder.CodingDataHigh;
byte[] completeData = decoder.CodingData; // Complete 8-byte array
ulong bitField = decoder.BitField;

// String representations
string formatted = decoder.ToString(); // Human-readable format
string hexString = decoder.ToHexString(); // "12 34 56 78 9A BC DE F0" format
```

## Coding Scheme

The T6e ECU uses a 64-bit coding field where each bit or group of bits represents specific vehicle configuration options. The coding data is split into two 4-byte arrays:

| Bit Range | Bit Position | Mask | Option Name | Options |
|-----------|-------------|------|-------------|---------|
| 63 | 63 | 1 | Oil Cooling System | Standard, Additional |
| 60-62 | 60 | 3 | Heating Ventilation Air Conditioning | None, Heater Only, Air Conditioning, Climate Control |
| 57-59 | 57 | 7 | Cruise System | None, Basic, Adaptive |
| 52 | 52 | 1 | Wheel Profile | 18/19 inch, 19/20 inch |
| 49-51 | 49 | 7 | Number of Gears | Numeric (0-7) |
| 48 | 48 | 1 | Close Ratio Gearset | False, True |
| 45-47 | 45 | 7 | Transmission Type | Manual, Auto, MMT |
| 43 | 43 | 1 | Speed Units | MPH, KPH |
| 36-42 | 36 | 127 | Fuel Tank Capacity | Numeric (0-127) |
| 35 | 35 | 1 | Rear Fog Fitted | False, True |
| 34 | 34 | 1 | Japan Seatbelt Warning | False, True |
| 33 | 33 | 1 | Symbol Display | ECE(ROW), SAE(FED) |
| 32 | 32 | 1 | Driver Position | LHD, RHD |
| 30 | 30 | 1 | Exhaust Bypass Valve Override | False, True |
| 29 | 29 | 1 | DPM Switch | False, True |
| 28 | 28 | 1 | Seat Heaters | False, True |
| 27 | 27 | 1 | Exhaust Silencer Bypass Valve | False, True |
| 26 | 26 | 1 | Auxiliary Cooling Fan | False, True |
| 25 | 25 | 1 | Speed Alert Buzzer | False, True |
| 24 | 24 | 1 | TC/ESP Button | False, True |
| 23 | 23 | 1 | Sport Button | False, True |
| 21-22 | 21 | 3 | Clutch Input | None, Switch, Potentiometer |
| 15 | 15 | 1 | Body Control Module | False, True |
| 14 | 14 | 1 | Transmission Control Unit | False, True |
| 13 | 13 | 1 | Tyre Pressure Monitoring System | False, True |
| 12 | 12 | 1 | Steering Angle Sensor | False, True |
| 11 | 11 | 1 | Yaw Rate Sensor | False, True |
| 10 | 10 | 1 | Instrument Cluster | MY08, MY11/12 |
| 9 | 9 | 1 | Anti-Lock Braking System | False, True |
| 8 | 8 | 1 | Launch Mode | False, True |
| 7 | 7 | 1 | Race Mode | False, True |
| 6 | 6 | 1 | Speed Limiter | False, True |
| 5 | 5 | 1 | Reverse Camera | False, True |
| 4 | 4 | 1 | Powerfold Mirrors | False, True |
| 1 | 1 | 1 | Central Door Locking | False, True |
| 0 | 0 | 1 | Oil Sump System | Standard, Upgrade |

### Reserved Bits
- **Bit 62**: Not used
- **Bits 53-56**: Not used  
- **Bit 44**: Not used
- **Bit 31**: Not used
- **Bits 16-20**: Not used
- **Bits 2-3**: Not used

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

- **ArgumentException**: Thrown if either coding data array is not exactly 4 bytes
- **ArgumentException**: Thrown if an unknown option name is provided

## Example

```csharp
using LotusECMLogger;

// Example coding data from a Lotus vehicle
byte[] codingDataLow = { 0x7F, 0x42, 0x00, 0x00 };
byte[] codingDataHigh = { 0x00, 0x00, 0x00, 0x00 };

var decoder = new T6eCodingDecoder(codingDataLow, codingDataHigh);

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

The coding data uses a 64-bit field where each option is positioned at specific bit locations:

- **Bits 0-31**: Lower 4 bytes (codingDataLow) - Various boolean and small options
- **Bits 32-63**: Higher 4 bytes (codingDataHigh) - Larger multi-bit options and reserved space

The decoder handles the bit manipulation internally, so users don't need to understand the bit layout to use the library effectively. 