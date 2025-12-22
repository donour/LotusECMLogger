using BinaryFileMonitor;
using System.Diagnostics;
using SAE.J2534;

namespace LotusECMLogger.Services
{
    /// <summary>
    /// Service for live tuning ECU memory by synchronizing a binary file with ECU RAM.
    ///
    /// This service enables real-time calibration editing by:
    /// 1. Reading ECU memory to a binary file on disk
    /// 2. Monitoring the file for changes (using BinaryFileMonitor)
    /// 3. Writing detected changes back to ECU memory via T6 RMA protocol
    ///
    /// The service monitors 32-bit word-level changes for efficient synchronization.
    /// </summary>
    public class T6LiveTuningService : IDisposable
	{
        private BinaryFileMonitor.BinaryFileMonitor? _fileMonitor;
        private string? _monitoredFilePath;
		private uint _baseMemoryAddress;
		private uint _memoryLength;
		private bool _isMonitoring;
		private readonly object _lock = new();

		// J2534 device and channel for persistent connection during monitoring
		private Device? _device;
		private Channel? _channel;

		// Memory address validation constants
		private const uint RAM_START = 0x40000000;
		private const uint RAM_END = 0x4000FFFF;

		/// <summary>
		/// Event fired when a word is written to ECU memory
		/// </summary>
		public event EventHandler<LiveTuningWordWrittenEventArgs>? WordWritten;

		/// <summary>
		/// Event fired when an error occurs during live tuning
		/// </summary>
		public event EventHandler<string>? ErrorOccurred;

		/// <summary>
		/// Gets whether the service is currently monitoring a file
		/// </summary>
		public bool IsMonitoring
		{
			get
			{
				lock (_lock)
				{
					return _isMonitoring;
				}
			}
		}

		/// <summary>
		/// Gets the path of the currently monitored file, or null if not monitoring
		/// </summary>
		public string? MonitoredFilePath
		{
			get
			{
				lock (_lock)
				{
					return _monitoredFilePath;
				}
			}
		}

		/// <summary>
		/// Gets the base ECU memory address being synchronized
		/// </summary>
		public uint BaseMemoryAddress
		{
			get
			{
				lock (_lock)
				{
					return _baseMemoryAddress;
				}
			}
		}

        /// <summary>
        /// Reads ECU memory and saves it to a binary file on disk.
        /// This creates the initial binary image that can be monitored for changes.
        /// </summary>
        /// <param name="startAddress">Starting ECU memory address (must be in RAM: 0x40000000-0x4000FFFF)</param>
        /// <param name="length">Number of bytes to read (should be multiple of 4 for word alignment)</param>
        /// <param name="filePath">Path where the binary file will be saved</param>
        /// <returns>True if successful, false otherwise</returns>
        /// <remarks>
        /// STUB: Implementation pending
        /// TODO:
        /// - Validate address range (RAM only)
        /// - Read memory in chunks using T6RMAService
        /// - Handle multi-frame reads for large blocks
        /// - Write binary data to file
        /// - Verify file write success
        /// </remarks>
        public static async Task<bool> ReadEcuImageToFileAsync(uint startAddress, uint length, string filePath)
		{
			Debug.WriteLine($"[STUB] ReadEcuImageToFileAsync: Address=0x{startAddress:X8}, Length={length}, File={filePath}");

			// TODO: Implement ECU memory read
			// Algorithm:
			// 1. Validate startAddress is in RAM range (0x40000000-0x4000FFFF)
			// 2. Validate length is reasonable (multiple of 4 preferred)
			// 3. Read memory in chunks (max 255 bytes per read via RMA)
			// 4. Assemble chunks into complete binary image
			// 5. Write to file using File.WriteAllBytes()
			// 6. Return success/failure

			await Task.CompletedTask; // Placeholder for async operation
			return false;
		}

		/// <summary>
		/// Starts monitoring a binary file for changes and writing them to ECU memory.
		/// Any 32-bit word changes detected in the file will be written to the corresponding
		/// ECU memory address.
		/// </summary>
		/// <param name="filePath">Path to the binary file to monitor</param>
		/// <param name="baseMemoryAddress">ECU memory address corresponding to file offset 0</param>
		/// <param name="scanIntervalMs">File scan interval in milliseconds (default: 100ms)</param>
		public void StartMonitoring(string filePath, uint baseMemoryAddress, int scanIntervalMs = 100)
		{
			lock (_lock)
			{
				if (_isMonitoring)
				{
					throw new InvalidOperationException("Already monitoring a file. Stop current monitoring before starting new session.");
				}

				if (string.IsNullOrWhiteSpace(filePath))
				{
					throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
				}

				if (!File.Exists(filePath))
				{
					throw new FileNotFoundException("File not found", filePath);
				}

				if (scanIntervalMs < 10)
				{
					throw new ArgumentException("Scan interval must be at least 10ms", nameof(scanIntervalMs));
				}

				try
				{
					Debug.WriteLine($"T6LiveTuning: Starting monitoring - File={filePath}, BaseAddress=0x{baseMemoryAddress:X8}, Interval={scanIntervalMs}ms");

					// Store configuration
					_monitoredFilePath = filePath;
					_baseMemoryAddress = baseMemoryAddress;

					// Get file size for validation
					var fileInfo = new FileInfo(filePath);
					_memoryLength = (uint)fileInfo.Length;

					// Initialize J2534 device and channel for persistent connection
					InitializeDevice();

					// Create and configure BinaryFileMonitor
					_fileMonitor = new BinaryFileMonitor.BinaryFileMonitor(filePath, scanIntervalMs);

					// Subscribe to events
					_fileMonitor.WordChanged += OnWordChanged;
					_fileMonitor.MonitorError += OnFileMonitorError;

					// Start monitoring
					_fileMonitor.Start();

					_isMonitoring = true;

					Debug.WriteLine($"T6LiveTuning: Monitoring started - File size: {_memoryLength} bytes ({_fileMonitor.WordCount} words)");
				}
				catch (Exception ex)
				{
					// Cleanup on failure
					CleanupResources();

					Debug.WriteLine($"T6LiveTuning: Failed to start monitoring - {ex.Message}");
					throw new InvalidOperationException($"Failed to start monitoring: {ex.Message}", ex);
				}
			}
		}

		/// <summary>
		/// Event handler for file monitor errors
		/// </summary>
		private void OnFileMonitorError(object? sender, BinaryFileMonitor.FileMonitorErrorEventArgs e)
		{
			string errorMsg = $"File monitor error: {e.Exception.Message}";
			Debug.WriteLine($"T6LiveTuning ERROR: {errorMsg}");
			ErrorOccurred?.Invoke(this, errorMsg);
		}

		/// <summary>
		/// Stops monitoring the current file.
		/// </summary>
		public void StopMonitoring()
		{
			lock (_lock)
			{
				if (!_isMonitoring)
				{
					return;
				}

				Debug.WriteLine("T6LiveTuning: Stopping monitoring");

				try
				{
					CleanupResources();
					Debug.WriteLine("T6LiveTuning: Monitoring stopped successfully");
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"T6LiveTuning: Error stopping monitoring - {ex.Message}");

					// Even on error, ensure we're in a clean state
					CleanupResources();

					throw new InvalidOperationException($"Error stopping monitoring: {ex.Message}", ex);
				}
			}
		}

		/// <summary>
		/// Initializes the J2534 device and CAN channel for ECU communication.
		/// This creates a persistent connection that will be used for all writes during the monitoring session.
		/// </summary>
		private void InitializeDevice()
		{
			try
			{
				string dllFileName = APIFactory.GetAPIinfo().First().Filename;
				API api = APIFactory.GetAPI(dllFileName);
				_device = api.GetDevice();

				// Use raw CAN protocol at 500 kbaud (standard for automotive CAN)
				_channel = _device.GetChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE);

				Debug.WriteLine("T6LiveTuning: J2534 device initialized with CAN protocol at 500 kbaud");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6LiveTuning: Failed to initialize J2534 device - {ex.Message}");
				CleanupResources();
				throw new InvalidOperationException($"Failed to initialize J2534 device: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Cleans up all resources including file monitor and J2534 device/channel
		/// </summary>
		private void CleanupResources()
		{
			try
			{
				// Stop and cleanup file monitor
				if (_fileMonitor != null)
				{
					_fileMonitor.WordChanged -= OnWordChanged;
					_fileMonitor.MonitorError -= OnFileMonitorError;
					_fileMonitor.Stop();
					_fileMonitor.Dispose();
					_fileMonitor = null;
				}

				// Cleanup J2534 device and channel
				if (_channel != null)
				{
					_channel.Dispose();
					_channel = null;
				}

				if (_device != null)
				{
					_device.Dispose();
					_device = null;
				}

				// Clear state variables
				_monitoredFilePath = null;
				_baseMemoryAddress = 0;
				_memoryLength = 0;
				_isMonitoring = false;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6LiveTuning: Error during cleanup - {ex.Message}");

				// Force null even on error
				_fileMonitor = null;
				_channel = null;
				_device = null;
				_monitoredFilePath = null;
				_baseMemoryAddress = 0;
				_memoryLength = 0;
				_isMonitoring = false;
			}
		}

		/// <summary>
		/// Writes a 32-bit word to ECU memory using T6 RMA protocol.
		/// Uses CAN ID 0x54 (Write 4 bytes / dword).
		/// </summary>
		/// <param name="address">ECU memory address (must be in RAM range)</param>
		/// <param name="value">32-bit value to write</param>
		/// <returns>Task representing the async write operation</returns>
		private async Task WriteWordToEcuAsync(uint address, uint value)
		{
			// Validate address is in RAM range
			if (address < RAM_START || address > RAM_END - 3)
			{
				throw new ArgumentOutOfRangeException(
					nameof(address),
					$"Invalid memory address 0x{address:X8}. Valid range: RAM (0x{RAM_START:X8}-0x{RAM_END - 3:X8})");
			}

			Channel? channelToUse;
			lock (_lock)
			{
				if (_channel == null || !_isMonitoring)
				{
					throw new InvalidOperationException("Cannot write to ECU: monitoring not active or device not connected");
				}
				channelToUse = _channel;
			}

			try
			{
				Debug.WriteLine($"T6LiveTuning: Writing word - Address=0x{address:X8}, Value=0x{value:X8}");

				// Build CAN message for memory write (CAN ID 0x54)
				// Format: [CAN ID (4 bytes)][Address (4 bytes, big-endian)][Data (4 bytes, big-endian)]
				byte[] canMessage = new byte[12];

				// CAN ID 0x54
				canMessage[0] = 0x00;
				canMessage[1] = 0x00;
				canMessage[2] = 0x00;
				canMessage[3] = 0x54;

				// Address in BIG-ENDIAN format
				canMessage[4] = (byte)((address >> 24) & 0xFF);
				canMessage[5] = (byte)((address >> 16) & 0xFF);
				canMessage[6] = (byte)((address >> 8) & 0xFF);
				canMessage[7] = (byte)(address & 0xFF);

				// Value in BIG-ENDIAN format
				canMessage[8] = (byte)((value >> 24) & 0xFF);
				canMessage[9] = (byte)((value >> 16) & 0xFF);
				canMessage[10] = (byte)((value >> 8) & 0xFF);
				canMessage[11] = (byte)(value & 0xFF);

				// Send the write command (fire-and-forget, no response expected)
				await Task.Run(() => channelToUse.SendMessages([canMessage]));

				Debug.WriteLine($"T6LiveTuning: Write successful");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6LiveTuning: Write failed - {ex.Message}");
				ErrorOccurred?.Invoke(this, $"Failed to write to ECU at 0x{address:X8}: {ex.Message}");
				throw;
			}
		}

		/// <summary>
		/// Event handler for word changes detected by BinaryFileMonitor.
		/// Translates file offset to ECU memory address and writes the change.
		/// </summary>
		/// <param name="sender">The BinaryFileMonitor instance</param>
		/// <param name="e">Event arguments containing offset and new value</param>
		private void OnWordChanged(object? sender, WordChangedEventArgs e)
		{
			// Calculate ECU memory address from file offset
			uint ecuAddress;
			lock (_lock)
			{
				ecuAddress = _baseMemoryAddress + (uint)e.ByteOffset;
			}

			Debug.WriteLine($"T6LiveTuning: File word changed - Offset=0x{e.ByteOffset:X}, ECU Addr=0x{ecuAddress:X8}, Old=0x{e.OldValue:X8}, New=0x{e.NewValue:X8}");

			// Use Task.Run to handle async operation in event handler
			Task.Run(async () =>
			{
				try
				{
					// Write the new value to ECU memory
					await WriteWordToEcuAsync(ecuAddress, e.NewValue);

					// Fire WordWritten event on success
					WordWritten?.Invoke(this, new LiveTuningWordWrittenEventArgs
					{
						MemoryAddress = ecuAddress,
						FileOffset = e.ByteOffset,
						OldValue = e.OldValue,
						NewValue = e.NewValue,
						Timestamp = DateTime.Now
					});

					Debug.WriteLine($"T6LiveTuning: Successfully wrote change to ECU at 0x{ecuAddress:X8}");
				}
				catch (Exception ex)
				{
					string errorMsg = $"Failed to write word change to ECU at 0x{ecuAddress:X8}: {ex.Message}";
					Debug.WriteLine($"T6LiveTuning ERROR: {errorMsg}");
					ErrorOccurred?.Invoke(this, errorMsg);
				}
			});
		}

		/// <summary>
		/// Disposes resources used by the service
		/// </summary>
		public void Dispose()
		{
			StopMonitoring();
			GC.SuppressFinalize(this);
		}
	}

	/// <summary>
	/// Event arguments for live tuning word write events
	/// </summary>
	public class LiveTuningWordWrittenEventArgs : EventArgs
	{
		/// <summary>
		/// ECU memory address that was written
		/// </summary>
		public uint MemoryAddress { get; set; }

		/// <summary>
		/// File offset that triggered the write
		/// </summary>
		public int FileOffset { get; set; }

		/// <summary>
		/// Previous value in the file
		/// </summary>
		public uint OldValue { get; set; }

		/// <summary>
		/// New value written to ECU
		/// </summary>
		public uint NewValue { get; set; }

		/// <summary>
		/// Timestamp when the write occurred
		/// </summary>
		public DateTime Timestamp { get; set; }
	}
}
