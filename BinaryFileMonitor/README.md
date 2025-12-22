# BinaryFileMonitor

A lightweight, high-performance C# library for monitoring binary files and detecting 32-bit word-level changes in real-time.

## Features

- **Real-time Change Detection**: Continuously monitors binary files and detects 32-bit word-level modifications
- **Event-Driven Architecture**: Emits events with precise offset and value information for each changed word
- **32-bit Word Alignment**: Monitors 4-byte aligned words for efficient change detection
- **Thread-Safe**: Safe for concurrent access from multiple threads
- **Lightweight**: Optimized for small to medium files (< 100MB)
- **Easy to Use**: Simple API with minimal configuration
- **Error Handling**: Robust error handling with dedicated error events
- **Well Documented**: Comprehensive XML documentation for IntelliSense support

## Installation

Add a project reference to `BinaryFileMonitor.csproj` in your solution:

```bash
dotnet add reference path/to/BinaryFileMonitor/BinaryFileMonitor.csproj
```

## Quick Start

```csharp
using BinaryFileMonitor;

// Create a monitor for your binary file
using var monitor = new BinaryFileMonitor("data.bin", scanIntervalMs: 100);

// Subscribe to word change events
monitor.WordChanged += (sender, e) =>
{
    Console.WriteLine($"Word[{e.WordIndex}] changed at offset 0x{e.ByteOffset:X}: " +
                      $"0x{e.OldValue:X8} -> 0x{e.NewValue:X8}");
};

// Optional: Subscribe to reload events
monitor.FileReloaded += (sender, e) =>
{
    Console.WriteLine($"File reloaded. {e.ChangeCount} words changed.");
};

// Optional: Handle errors
monitor.MonitorError += (sender, e) =>
{
    Console.WriteLine($"Monitor error: {e.Exception.Message}");
};

// Start monitoring
monitor.Start();

// Your application continues running...
Console.WriteLine("Monitoring started. Press any key to stop...");
Console.ReadKey();

// Stop monitoring (or let Dispose handle it)
monitor.Stop();
```

## How It Works

1. **Load**: The monitor loads the specified binary file into memory
2. **Monitor**: A background thread periodically reloads the file from disk
3. **Compare**: Each reload triggers a word-by-word comparison (32-bit aligned)
4. **Notify**: For each changed word, a `WordChanged` event is emitted with:
   - `ByteOffset`: The byte position where the word starts (0, 4, 8, 12, etc.)
   - `WordIndex`: The word index (ByteOffset / 4)
   - `OldValue`: The previous 32-bit word value
   - `NewValue`: The current 32-bit word value
5. **Update**: The internal buffer is updated with new values

## API Reference

### BinaryFileMonitor Class

#### Constructor

```csharp
public BinaryFileMonitor(string filePath, int scanIntervalMs = 100)
```

**Parameters:**
- `filePath`: Path to the binary file to monitor
- `scanIntervalMs`: Interval between scans in milliseconds (default: 100ms)

#### Methods

```csharp
void Start()                                    // Start monitoring
void Stop()                                     // Stop monitoring
byte[] GetCurrentBuffer()                       // Get copy of entire buffer
byte[] GetBufferRange(int offset, int length)   // Get copy of buffer range
uint GetWord(int byteOffset)                    // Get 32-bit word at offset
void Dispose()                                  // Stop and cleanup
```

#### Properties

```csharp
bool IsRunning { get; }      // Whether monitor is active
string FilePath { get; }     // Path being monitored
int BufferSize { get; }      // Current buffer size in bytes
int WordCount { get; }       // Number of 32-bit words in buffer
```

#### Events

```csharp
event EventHandler<WordChangedEventArgs>? WordChanged;
event EventHandler<FileReloadedEventArgs>? FileReloaded;
event EventHandler<FileMonitorErrorEventArgs>? MonitorError;
```

### Event Arguments

#### WordChangedEventArgs

```csharp
int ByteOffset { get; }   // Byte offset where word starts (4-byte aligned)
int WordIndex { get; }    // Word index (ByteOffset / 4)
uint OldValue { get; }    // Previous 32-bit value
uint NewValue { get; }    // New 32-bit value
string ToString()         // Formatted string: "Word[0] @ 0x0000: 0x00000000 -> 0xFFFFFFFF"
```

#### FileReloadedEventArgs

```csharp
int ChangeCount { get; }   // Number of words that changed
bool SizeChanged { get; }  // Whether file size changed
```

#### FileMonitorErrorEventArgs

```csharp
Exception Exception { get; }  // The exception that occurred
```

## Advanced Usage Examples

### Example 1: Monitoring ECU Data Changes

```csharp
using var monitor = new BinaryFileMonitor("ecu_memory.bin", scanIntervalMs: 50);

// Track specific memory regions
monitor.WordChanged += (sender, e) =>
{
    // ECU coding region (example: 0x200-0x208 is words 128-129)
    if (e.ByteOffset >= 0x200 && e.ByteOffset < 0x208)
    {
        Console.WriteLine($"ECU Coding Word[{e.WordIndex}] changed: " +
                          $"0x{e.OldValue:X8} -> 0x{e.NewValue:X8}");
    }
};

monitor.Start();
```

### Example 2: Change Logging to File

```csharp
using var monitor = new BinaryFileMonitor("data.bin");
using var logWriter = new StreamWriter("changes.log", append: true);

monitor.WordChanged += (sender, e) =>
{
    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | " +
                      $"Word[{e.WordIndex}] @ 0x{e.ByteOffset:X4} | " +
                      $"Old: 0x{e.OldValue:X8} | " +
                      $"New: 0x{e.NewValue:X8}";
    logWriter.WriteLine(logEntry);
    logWriter.Flush();
};

monitor.Start();
```

### Example 3: Batching Changes

```csharp
using var monitor = new BinaryFileMonitor("data.bin");
var changesBatch = new List<WordChangedEventArgs>();

monitor.WordChanged += (sender, e) =>
{
    changesBatch.Add(e);
};

monitor.FileReloaded += (sender, e) =>
{
    if (changesBatch.Count > 0)
    {
        Console.WriteLine($"Batch: {changesBatch.Count} words changed");
        foreach (var change in changesBatch)
        {
            Console.WriteLine($"  {change}");
        }
        changesBatch.Clear();
    }
};

monitor.Start();
```

### Example 4: Reading Current Buffer State

```csharp
using var monitor = new BinaryFileMonitor("data.bin");
monitor.Start();

// Later in your code...
byte[] currentState = monitor.GetCurrentBuffer();
uint wordAtOffset = monitor.GetWord(0x100);  // Must be 4-byte aligned
byte[] range = monitor.GetBufferRange(0x200, 8);

Console.WriteLine($"Buffer size: {monitor.BufferSize} bytes");
Console.WriteLine($"Word count: {monitor.WordCount} words");
Console.WriteLine($"Word at 0x100: 0x{wordAtOffset:X8}");
```

### Example 5: Error Handling

```csharp
using var monitor = new BinaryFileMonitor("volatile_file.bin");

monitor.MonitorError += (sender, e) =>
{
    if (e.Exception is FileNotFoundException)
    {
        Console.WriteLine("File was deleted!");
        monitor.Stop();
    }
    else if (e.Exception is UnauthorizedAccessException)
    {
        Console.WriteLine("Access denied!");
    }
    else
    {
        Console.WriteLine($"Unexpected error: {e.Exception.Message}");
    }
};

monitor.Start();
```

## Performance Considerations

### Scan Interval

The `scanIntervalMs` parameter controls how frequently the file is checked:

- **Faster scanning (50-100ms)**: Better responsiveness, higher CPU usage
- **Slower scanning (500-1000ms)**: Lower CPU usage, delayed detection
- **Recommended**: 100ms for most use cases

For a 50KB file, a 100ms scan interval results in ~500KB/s disk read throughput, which is negligible on modern systems.

### File Size Guidelines

- **< 1 MB**: Optimal performance with default settings
- **1-10 MB**: Consider increasing scan interval to 200-500ms
- **10-100 MB**: Use 500-1000ms scan interval
- **> 100 MB**: This library may not be suitable; consider memory-mapped files or specialized solutions

### Thread Safety

All public methods are thread-safe and can be called from multiple threads:

```csharp
// Safe to call from different threads
Task.Run(() => monitor.GetWord(0x100));
Task.Run(() => monitor.GetBufferRange(0x200, 8));
```

## Design Rationale

### Why Disk-Based Monitoring?

This library uses a disk-based approach (periodically re-reading the file) rather than in-memory modification tracking because:

1. **External Process Detection**: Can detect changes made by other processes
2. **Simplicity**: No complex memory management or modification APIs needed
3. **Reliability**: File system is the single source of truth
4. **Performance**: For small files (< 50KB), disk I/O overhead is negligible

### Why Not FileSystemWatcher?

`FileSystemWatcher` only detects *that* a file changed, not *what* changed. This library provides word-level granularity by comparing file contents.

## Use Cases

- **ECU Memory Monitoring**: Track changes to ECU calibration data in real-time
- **Binary Configuration Files**: Monitor config files for unauthorized modifications
- **Debugging**: Watch how external tools modify binary data
- **Data Forensics**: Track word-level changes during experiments
- **Testing**: Verify that binary outputs match expectations

## Requirements

- .NET 8.0 or higher
- Read access to the monitored file
- Sufficient memory to load the file (file is kept in memory)

## License

MIT License - See LICENSE file for details

## Contributing

Contributions are welcome! Please ensure:
- XML documentation for all public APIs
- Thread safety for all operations
- Unit tests for new features

## Future Enhancements

Potential improvements for future versions:

- Memory-mapped file support for large files
- Configurable change batching strategies
- CRC/checksum validation options
- Delta compression for change history
- Async/await API variants
- Change history buffer with rollback capability
