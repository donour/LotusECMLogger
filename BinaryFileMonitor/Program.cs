namespace BinaryFileMonitor;

/// <summary>
/// Simple test program for BinaryFileMonitor.
/// Monitors a binary file and prints all word change events to console.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        // Check arguments
        if (args.Length == 0)
        {
            Console.WriteLine("BinaryFileMonitor Test Program");
            Console.WriteLine("==============================");
            Console.WriteLine();
            Console.WriteLine("Usage: BinaryFileMonitor <filepath> [scan_interval_ms]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  filepath          - Path to the binary file to monitor");
            Console.WriteLine("  scan_interval_ms  - Scan interval in milliseconds (default: 100)");
            Console.WriteLine();
            Console.WriteLine("Example:");
            Console.WriteLine("  BinaryFileMonitor data.bin");
            Console.WriteLine("  BinaryFileMonitor data.bin 50");
            return;
        }

        string filePath = args[0];
        int scanInterval = 100;

        // Parse optional scan interval
        if (args.Length > 1)
        {
            if (!int.TryParse(args[1], out scanInterval) || scanInterval <= 0)
            {
                Console.WriteLine("Error: Invalid scan interval. Must be a positive integer.");
                return;
            }
        }

        // Check if file exists
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Error: File not found: {filePath}");
            return;
        }

        Console.WriteLine("BinaryFileMonitor Test Program");
        Console.WriteLine("==============================");
        Console.WriteLine($"File:          {filePath}");
        Console.WriteLine($"Scan Interval: {scanInterval}ms");
        Console.WriteLine();

        // Create and configure monitor
        using var monitor = new BinaryFileMonitor(filePath, scanInterval);

        // Subscribe to word change events
        monitor.WordChanged += (sender, e) =>
        {
            Console.WriteLine($"[WORD CHANGE] {e}");
        };

        // Subscribe to file reloaded events
        monitor.FileReloaded += (sender, e) =>
        {
            if (e.ChangeCount > 0)
            {
                Console.WriteLine($"[FILE RELOAD] {e.ChangeCount} word(s) changed, " +
                                  $"size changed: {e.SizeChanged}");
            }
        };

        // Subscribe to error events
        monitor.MonitorError += (sender, e) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {e.Exception.GetType().Name}: {e.Exception.Message}");
            Console.ResetColor();
        };

        // Start monitoring
        try
        {
            monitor.Start();
            Console.WriteLine("Monitoring started...");
            Console.WriteLine($"Initial buffer: {monitor.BufferSize} bytes ({monitor.WordCount} words)");
            Console.WriteLine();
            Console.WriteLine("Press any key to stop monitoring...");
            Console.WriteLine();

            // Wait for user to stop
            Console.ReadKey(true);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error starting monitor: {ex.Message}");
            Console.ResetColor();
            return;
        }

        // Stop monitoring
        monitor.Stop();
        Console.WriteLine();
        Console.WriteLine("Monitoring stopped.");
    }
}
