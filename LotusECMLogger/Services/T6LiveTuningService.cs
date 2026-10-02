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
		private int _scanIntervalMs;
		private readonly object _lock = new();

		// Set once the session can no longer keep the ECU in step with the file; no further writes
		// are sent and Faulted has been raised.
		private bool _faulted;

		// How long the file may stay unreadable before the session gives up. Editors that save by
		// replacing the file make it briefly unreadable, so a short outage is expected and ignored.
		private const int FileUnavailableLimitMs = 5000;

		// J2534 device and channel for persistent connection during monitoring
		private J2534Session? _session;
		private J2534Channel? _channel;

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
		/// Raised once, on a background thread, when the session can no longer keep the ECU in step
		/// with the file: a write to the ECU failed, or the file stayed unreadable. Nothing further
		/// is written after this. The service does not stop itself — stopping waits for the monitor
		/// thread, which may be the one raising this — so the owner should call
		/// <see cref="StopMonitoring"/>.
		/// </summary>
		public event EventHandler<string>? Faulted;

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
					_scanIntervalMs = scanIntervalMs;
					_faulted = false;

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
					// Cleanup on failure. Safe under the lock: the monitor thread is started last,
					// so no thread exists yet that could be waiting on _lock.
					ReleaseResources(DetachResources());

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
			Debug.WriteLine($"T6LiveTuning: File monitor error #{e.ConsecutiveFailures}: {e.Exception.Message}");

			// The monitor retries every scan, so a missing or locked file would otherwise log a line
			// every scan interval. Only the first failure of a run is reported.
			if (e.ConsecutiveFailures == 1)
			{
				ErrorOccurred?.Invoke(this, $"File monitor error: {e.Exception.Message}");
			}

			if ((long)e.ConsecutiveFailures * _scanIntervalMs >= FileUnavailableLimitMs)
			{
				Fault($"The calibration file has been unreadable for {FileUnavailableLimitMs / 1000} seconds " +
					$"({e.Exception.Message}). Changes to it can no longer reach the ECU.");
			}
		}

		/// <summary>
		/// Marks the session as unable to continue and raises <see cref="Faulted"/>, once. Ignored
		/// after the session has been stopped, so writes still in flight at Stop cannot trigger it.
		/// </summary>
		private void Fault(string reason)
		{
			lock (_lock)
			{
				if (!_isMonitoring || _faulted)
				{
					return;
				}
				_faulted = true;
			}

			Debug.WriteLine($"T6LiveTuning: Session faulted - {reason}");
			Faulted?.Invoke(this, reason);
		}

		/// <summary>
		/// Stops monitoring the current file. Safe to call more than once; never throws.
		/// </summary>
		public void StopMonitoring()
		{
			(BinaryFileMonitor.BinaryFileMonitor? monitor, J2534Session? session) resources;

			lock (_lock)
			{
				if (!_isMonitoring)
				{
					return;
				}

				Debug.WriteLine("T6LiveTuning: Stopping monitoring");
				resources = DetachResources();
			}

			// Released outside the lock: stopping the monitor waits for its thread to exit, and that
			// thread may be inside OnWordChanged waiting for _lock. Holding it here would deadlock.
			ReleaseResources(resources);
			Debug.WriteLine("T6LiveTuning: Monitoring stopped");
		}

		/// <summary>
		/// Initializes the J2534 device and CAN channel for ECU communication.
		/// This creates a persistent connection that will be used for all writes during the monitoring session.
		/// </summary>
		private void InitializeDevice()
		{
			try
			{
				_session = J2534Session.Open();

				// Use raw CAN protocol at 500 kbaud (standard for automotive CAN)
				_channel = _session.OpenCan();

				Debug.WriteLine("T6LiveTuning: J2534 device initialized with CAN protocol at 500 kbaud");
			}
			catch (Exception ex)
			{
				// The caller (StartMonitoring) releases whatever was opened.
				Debug.WriteLine($"T6LiveTuning: Failed to initialize J2534 device - {ex.Message}");
				throw new InvalidOperationException($"Failed to initialize J2534 device: {ex.Message}", ex);
			}
		}

		/// <summary>
		/// Resets the service to the not-monitoring state and hands back the resources it held, so
		/// the caller can release them. Must be called under <see cref="_lock"/>.
		/// </summary>
		private (BinaryFileMonitor.BinaryFileMonitor? monitor, J2534Session? session) DetachResources()
		{
			var resources = (_fileMonitor, _session);

			_fileMonitor = null;
			_session = null;
			_channel = null;
			_monitoredFilePath = null;
			_baseMemoryAddress = 0;
			_memoryLength = 0;
			_isMonitoring = false;

			return resources;
		}

		/// <summary>
		/// Stops the file monitor (waiting for its thread) and closes the J2534 session. Never
		/// throws: a failure here must not leave the caller thinking monitoring is still running.
		/// </summary>
		private void ReleaseResources((BinaryFileMonitor.BinaryFileMonitor? monitor, J2534Session? session) resources)
		{
			var (monitor, session) = resources;

			try
			{
				if (monitor != null)
				{
					monitor.WordChanged -= OnWordChanged;
					monitor.MonitorError -= OnFileMonitorError;
					monitor.Dispose(); // stops the monitor thread
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6LiveTuning: Error stopping file monitor - {ex.Message}");
			}

			try
			{
				// Disposes the session's channel and device.
				session?.Dispose();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6LiveTuning: Error closing J2534 session - {ex.Message}");
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

			J2534Channel? channelToUse;
			lock (_lock)
			{
				if (_channel == null || !_isMonitoring)
				{
					throw new InvalidOperationException("Cannot write to ECU: monitoring not active or device not connected");
				}
				channelToUse = _channel;
			}

			Debug.WriteLine($"T6LiveTuning: Writing word - Address=0x{address:X8}, Value=0x{value:X8}");

			byte[] canMessage = BuildWordWriteFrame(address, value);

			// The ECU never acknowledges an RMA write, but the adapter does report whether it took the
			// frame for transmission. A refusal (an unplugged adapter, for one) is the only failure
			// visible from here, so it must not be ignored.
			await Task.Run(() => channelToUse.SendMessage(canMessage).ThrowIfError());
		}

		/// <summary>
		/// Builds the RMA 32-bit write frame (CAN ID 0x54):
		/// [CAN ID (4)][address (4, big-endian)][value (4, big-endian)].
		/// </summary>
		/// <remarks>
		/// The firmware's 0x54 handler loads CAN data bytes 4-7 as one word and stores it at the
		/// address (lwz/stw, no byte swap), so data byte 4 lands at the address and byte 7 at
		/// address + 3. Sending the value most significant byte first therefore puts it in ECU
		/// memory exactly as <see cref="BinaryFileMonitor.BinaryFileMonitor"/> read it from the file.
		/// </remarks>
		internal static byte[] BuildWordWriteFrame(uint address, uint value)
		{
			byte[] frame = new byte[12];
			frame[3] = 0x54;
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), address);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), value);
			return frame;
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
				if (_faulted)
				{
					return; // the session is ending; nothing more is sent
				}
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

					// The monitor has already moved past this change and will not offer it again, so
					// the ECU now differs from the file. Carrying on would look like live tuning while
					// silently not being it.
					Fault($"A write to the ECU failed at 0x{ecuAddress:X8} ({ex.Message}). The ECU no longer " +
						"matches the file — upload the file to bring them back in step.");
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
