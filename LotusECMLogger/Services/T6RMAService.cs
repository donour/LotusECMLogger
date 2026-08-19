using LotusECMLogger.Services.Logging;
using SAE.J2534;
using System.Diagnostics;

namespace LotusECMLogger.Services
{
	/// <summary>
	/// Implementation of T6 RMA (Remote Memory Access) protocol
	/// Based on reverse-engineered ECU firmware function flexcan_a_rx_50_51_52_53()
	///
	/// ═══════════════════════════════════════════════════════════════════════════════
	/// COMPLETE RMA PROTOCOL SPECIFICATION
	/// ═══════════════════════════════════════════════════════════════════════════════
	///
	/// READ OPERATIONS (All respond on CAN ID 0x7A0):
	/// ─────────────────────────────────────────────────────────────────────────────
	/// CAN ID  | DLC | Format                      | Function
	/// ─────────────────────────────────────────────────────────────────────────────
	/// 0x50    | 4   | [Address(4)]                | Read 4 bytes (dword/uint32)
	/// 0x51    | 4   | [Address(4)]                | Read 2 bytes (word/uint16)
	/// 0x52    | 4   | [Address(4)]                | Read 1 byte (byte/uint8)
	/// 0x53    | 5   | [Address(4)][Length(1)]     | Read variable length (1-255 bytes)
	///                                               | Multi-frame support for >8 bytes
	///
	/// WRITE OPERATIONS (No response, writes are fire-and-forget):
	/// ─────────────────────────────────────────────────────────────────────────────
	/// CAN ID  | DLC | Format                      | Function
	/// ─────────────────────────────────────────────────────────────────────────────
	/// 0x54    | 8   | [Address(4)][Data(4)]       | Write 4 bytes (dword/uint32)
	/// 0x55    | 6   | [Address(4)][Data(2)]       | Write 2 bytes (word/uint16)
	/// 0x56    | 5   | [Address(4)][Data(1)]       | Write 1 byte (byte/uint8)
	/// 0x57    | 5+  | [Address(4)][Length(1)]     | Write variable length (multi-frame)
	///                | + continuation frames       | First frame: address + length
	///                                               | Subsequent frames: data payload
	///
	/// KEY PROTOCOL DETAILS:
	/// ─────────────────────────────────────────────────────────────────────────────
	/// • Byte Order: BIG-ENDIAN (network byte order) for all addresses and multi-byte data
	/// • Response CAN ID: 0x7A0 (all read operations)
	/// • Security: Requires ecu_unlocked == true (calibration must contain "WTF?" magic)
	/// • Valid Address Range: 0x40000000 - 0x4000FFFF (64KB RAM) for polling/logging/writes.
	///   The read path (0x53) also reaches the flash-mapped address space (0x00000000+), used
	///   by the Download*Async snapshot methods (per-<see cref="EcuVariant"/> zone addresses).
	/// • Multi-frame Read (0x53): ECU sends first 8 bytes immediately, continuation via 0x7A0
	/// • Multi-frame Write (0x57): Host sends continuation frames after initial command
	/// • Fixed-length reads (0x50-0x52): Optimized for atomic register/variable access
	/// • Variable-length (0x53/0x57): Flexible for arbitrary memory dumps/updates
	///
	/// CURRENT IMPLEMENTATION:
	/// ─────────────────────────────────────────────────────────────────────────────
	/// Reads use CAN ID 0x53 (variable-length); writes use 0x54 (word) and 0x56 (byte), which
	/// together cover both single-value edits and whole-image uploads
	/// (<see cref="WriteFileToMemoryAsync"/>). Future expansion could add:
	/// - Fixed-length reads (0x50-0x52) for faster single-value polling
	/// - Block write (0x57) to cut an upload's frame count, once the ECU's continuation-frame
	///   format and its tolerance for back-to-back frames have been confirmed on a car
	/// ═══════════════════════════════════════════════════════════════════════════════
	/// </summary>
	public sealed class T6RMAService : IT6RMAService
	{
		// CAN IDs from firmware analysis
		private const uint REQUEST_CAN_ID = 0x53;        // CAN ID for memory read requests
		private const uint RESPONSE_CAN_ID = 0x7A0;      // CAN ID for memory read responses

		// Memory address ranges from firmware
		private const uint RAM_START = 0x40000000;
		private const uint RAM_END = 0x4000FFFF;         // 64KB RAM

		// An upload sends one CAN frame per 4-byte word, so a 27KB calibration is nearly 7000 frames.
		// Reporting every frame would swamp the UI thread with progress marshalling; a report per
		// kilobyte still gives a smooth progress bar.
		private const int UploadProgressStrideBytes = 1024;

		// Bytes compared before an upload to confirm the file belongs to the calibration already in
		// memory. A calibration's opening bytes identify it, so a file for a different tune, ECU, or
		// region shows up here rather than after the whole region has been overwritten.
		private const int HeaderPreflightBytes = 32;

		// Per-variant flash zone tables, transcribed from the zone list in the reference
		// lotusecu-tools dumper (lib/ltacc.py). The RMA read protocol (CAN ID 0x53) addresses
		// the ECU's full memory map, so the same read path used for RAM above also reaches these
		// flash regions (and, on T4e, the small RAM1/DECRAM window used for its Learned zone).
		//
		// Learned Data: persisted adaptive fuel/idle/knock trims. On T6 this sits in flash,
		// distinct from the Coding zone (0x1C000-0x20000) already read via Mode 22 PIDs
		// 0x2263/0x2264 in J2534EcuCodingService; T4e keeps it in a small battery-backed RAM/DECRAM
		// window instead.
		private static readonly Dictionary<EcuVariant, (uint Address, uint Length)> LearnedDataZones = new()
		{
			[EcuVariant.T4e] = (0x002F8000, 0x00000800), // RAM1/DECRAM, 2KB
			[EcuVariant.K4] = (0x00006000, 0x00002000),  // S2, 8KB
			[EcuVariant.T4] = (0x00006000, 0x00002000),  // S2, 8KB
			[EcuVariant.T6] = (0x00010000, 0x0000C000),  // L2, 48KB
		};

		// Calibration: the active tune (fuel/ignition maps, limiters, etc.) as loaded into the ECU.
		private static readonly Dictionary<EcuVariant, (uint Address, uint Length)> CalibrationZones = new()
		{
			[EcuVariant.T4e] = (0x00010000, 0x00010000), // S1, 64KB
			[EcuVariant.K4] = (0x00030000, 0x00010000),  // S6, 64KB
			[EcuVariant.T4] = (0x00070000, 0x00010000),  // S10, 64KB
			[EcuVariant.T6] = (0x00020000, 0x00010000),  // L4, 64KB
		};

		// Program: the ECU's compiled firmware code. Largest of the flash regions on every
		// variant, so a full download runs to many RMA read chunks (255 bytes each) and can
		// take a while over CAN.
		private static readonly Dictionary<EcuVariant, (uint Address, uint Length)> ProgramZones = new()
		{
			[EcuVariant.T4e] = (0x00020000, 0x00060000), // S2-S7, 384KB
			[EcuVariant.K4] = (0x00010000, 0x00020000),  // S4-S5, 128KB
			[EcuVariant.T4] = (0x00010000, 0x00060000),  // S4-S9, 384KB
			[EcuVariant.T6] = (0x00040000, 0x000C0000),  // M0-H3, 768KB
		};

		private J2534Session? _session;
		private J2534Channel? _channel;
		private Thread? _loggingThread;
		private bool _isLogging;
		private uint? _currentAddress;
		private byte _currentLength;
		private int _intervalMs;
		private ISampleSink? _sink;

		/// <summary>
		/// Column name per byte of the read, built once when a session starts. Naming them on every
		/// row would allocate a string per byte at the polling rate.
		/// </summary>
		private string[] _byteColumns = [];

		private readonly object _lock = new();

		public event EventHandler<T6RMADataEventArgs>? DataReceived;
		public event EventHandler<string>? ErrorOccurred;

		public bool IsLogging
		{
			get
			{
				lock (_lock)
				{
					return _isLogging;
				}
			}
		}

		public uint? CurrentAddress
		{
			get
			{
				lock (_lock)
				{
					return _currentAddress;
				}
			}
		}

		public void StartLogging(uint memoryAddress, byte length, int intervalMs, string csvFilePath)
		{
			lock (_lock)
			{
				if (_isLogging)
				{
					throw new InvalidOperationException("Logging is already active. Stop current session before starting a new one.");
				}

				// Validate memory address range
				ValidateMemoryAddress(memoryAddress, length);

				if (intervalMs < 10)
				{
					throw new ArgumentException("Interval must be at least 10ms", nameof(intervalMs));
				}

				if (string.IsNullOrWhiteSpace(csvFilePath))
				{
					throw new ArgumentException("CSV file path cannot be empty", nameof(csvFilePath));
				}

				_currentAddress = memoryAddress;
				_currentLength = length;
				_intervalMs = intervalMs;

				try
				{
					// Initialize J2534 device and CAN channel
					InitializeDevice();

					// Initialize CSV file
					InitializeCsvFile(csvFilePath);

					// Start logging thread
					_isLogging = true;
					_loggingThread = new Thread(LoggingThreadProc)
					{
						Name = "T6RMA Logging Thread",
						IsBackground = true
					};
					_loggingThread.Start();

					Debug.WriteLine($"T6RMA logging started: Address=0x{memoryAddress:X8}, Length={length}, Interval={intervalMs}ms");
				}
				catch (Exception ex)
				{
					CleanupResources();
					throw new InvalidOperationException($"Failed to start logging: {ex.Message}", ex);
				}
			}
		}

		public void StopLogging()
		{
			lock (_lock)
			{
				if (!_isLogging)
				{
					return;
				}

				_isLogging = false;
			}

			// Wait for logging thread to finish (outside lock to avoid deadlock)
			_loggingThread?.Join(TimeSpan.FromSeconds(5));

			lock (_lock)
			{
				CleanupResources();
				Debug.WriteLine("T6RMA logging stopped");
			}
		}

		public void Dispose()
		{
			StopLogging();
		}

		private void InitializeDevice()
		{
			_session = J2534Session.Open();

			// Use raw CAN protocol at 500 kbaud (standard for automotive CAN)
			_channel = _session.OpenCan();

			// Set up CAN filter to receive responses on 0x7A0
			var passFilter = new MessageFilter
			{
				FilterType = Filter.PASS_FILTER,
				Mask = [0x00, 0x00, 0x07, 0xFF],      // Match all 11 bits of CAN ID
				Pattern = [0x00, 0x00, 0x07, 0xA0]     // CAN ID 0x7A0
			};
			_channel.StartMessageFilter(passFilter).ThrowIfError();

			Debug.WriteLine("T6RMA: J2534 device initialized with CAN protocol at 500 kbaud");
		}

		private void InitializeCsvFile(string csvFilePath)
		{
			try
			{
				// The address is fixed for the session, so it identifies the log in the preamble
				// rather than repeating unchanged down a column of every row.
				var header = new SampleLogHeader("T6 RMA Memory Logging Session",
				[
					$"Memory Address: 0x{_currentAddress:X8}",
					$"Length: {_currentLength} bytes",
					$"Interval: {_intervalMs}ms",
				]);

				// Hex columns: these are raw memory bytes, so the bit pattern is the point — a
				// decimal rendering would have to be converted back by hand to mean anything.
				_byteColumns = new string[_currentLength];
				var columns = new SampleColumn[_currentLength];
				for (int i = 0; i < _byteColumns.Length; i++)
				{
					_byteColumns[i] = $"Byte{i}";
					columns[i] = SampleColumn.Hex(_byteColumns[i], digits: 2);
				}

				_sink = new CsvSampleSink(csvFilePath, header, columns);

				Debug.WriteLine($"T6RMA: CSV file initialized: {csvFilePath}");
			}
			catch (Exception ex)
			{
				throw new IOException($"Failed to create CSV file: {ex.Message}", ex);
			}
		}

		private void LoggingThreadProc()
		{
			var startTime = DateTime.Now;
			var stopwatch = Stopwatch.StartNew();

			try
			{
				while (IsLogging)
				{
					try
					{
						var timestamp = DateTime.Now;
						var relativeTimeMs = stopwatch.ElapsedMilliseconds;

						// Send memory read request
						byte[]? responseData = SendMemoryReadRequest(_currentAddress!.Value, _currentLength);

						if (responseData != null && responseData.Length > 0)
						{
							// Fire event for UI update
							DataReceived?.Invoke(this, new T6RMADataEventArgs
							{
								Timestamp = timestamp,
								MemoryAddress = _currentAddress.Value,
								Data = responseData,
								DataLength = responseData.Length
							});

							// Write to CSV
							WriteCsvEntry(timestamp, relativeTimeMs, responseData);
						}
						else
						{
							Debug.WriteLine("T6RMA: No response data received");
						}

						// Wait for next interval
						Thread.Sleep(_intervalMs);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"T6RMA logging error: {ex.Message}");
						ErrorOccurred?.Invoke(this, $"Logging error: {ex.Message}");

						// Continue logging even after errors (ECU might be temporarily busy)
						Thread.Sleep(_intervalMs);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA logging thread fatal error: {ex.Message}");
				ErrorOccurred?.Invoke(this, $"Fatal error: {ex.Message}");
			}
			finally
			{
				stopwatch.Stop();
			}
		}

		private byte[]? SendMemoryReadRequest(uint address, byte length)
		{
			if (_channel == null)
			{
				throw new InvalidOperationException("Channel not initialized");
			}

			// Build CAN message for memory read request (CAN ID 0x53)
			// Format: [CAN ID (4 bytes)] [Data (5 bytes: 4-byte address + 1-byte length)]
			// Total: 9 bytes
			// CAN ID encoding format (from J2534EcuCodingService reference):
			// For 11-bit CAN ID, split into upper 3 bits and lower 8 bits
			// Example: 0x502 = [0x00, 0x00, 0x05, 0x02]
			//          0x053 = [0x00, 0x00, 0x00, 0x53]

			byte[] canMessage = new byte[9]; // 4 bytes CAN ID + 5 bytes data

			// CAN ID 0x53: 11-bit ID = 0b000 0101 0011
			// Upper 3 bits (10-8): 0b000 = 0x00
			// Lower 8 bits (7-0):  0b0101 0011 = 0x53
			canMessage[0] = 0x00;
			canMessage[1] = 0x00;
			canMessage[2] = 0x00; // Upper 3 bits of 0x53
			canMessage[3] = 0x53; // Lower 8 bits of 0x53

			// Data payload: 5 bytes (4-byte address + 1-byte length)
			// Address is BIG-ENDIAN (network byte order, standard for CAN bus and PowerPC ECU)
			canMessage[4] = (byte)((address >> 24) & 0xFF);
			canMessage[5] = (byte)((address >> 16) & 0xFF);
			canMessage[6] = (byte)((address >> 8) & 0xFF);
			canMessage[7] = (byte)(address & 0xFF);
			canMessage[8] = length;

			try
			{
				// Send the request
				_channel.SendMessage(canMessage);

				// For multi-frame responses, we need to collect multiple CAN messages
				// Each CAN frame can carry ~8 bytes of data
				List<byte> assembledData = new List<byte>(length);
				var stopwatch = Stopwatch.StartNew();
				const int TOTAL_TIMEOUT_MS = 2000; // Total timeout for collecting all frames

				while (assembledData.Count < length && stopwatch.ElapsedMilliseconds < TOTAL_TIMEOUT_MS)
				{
					// Calculate remaining bytes needed
					int remainingBytes = length - assembledData.Count;

					// Calculate how many messages we might need (assuming ~8 bytes per message)
					int messagesToRequest = Math.Max(1, (remainingBytes + 7) / 8);

					// Wait for response messages
					var response = _channel.ReadMessages(messagesToRequest, 200);

					if (response.Messages.Length > 0)
					{
						foreach (var message in response.Messages)
						{
							byte[]? frameData = ParseMemoryReadResponse(message);
							if (frameData != null && frameData.Length > 0)
							{
								// Add the data from this frame
								int bytesToTake = Math.Min(frameData.Length, remainingBytes);
								assembledData.AddRange(frameData.Take(bytesToTake));

								// Break if we have all the data we need
								if (assembledData.Count >= length)
								{
									break;
								}
							}
						}
					}
					else
					{
						// No more messages available
						if (assembledData.Count > 0)
						{
							Debug.WriteLine($"T6RMA: Collected {assembledData.Count} of {length} requested bytes before timeout");
							break;
						}
						else
						{
							Debug.WriteLine("T6RMA: No response received within timeout");
							return null;
						}
					}
				}

				if (assembledData.Count > 0)
				{
					return [.. assembledData];
				}
				else
				{
					Debug.WriteLine("T6RMA: No data received");
					return null;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error sending memory read request: {ex.Message}");
				throw;
			}
		}

		private byte[]? ParseMemoryReadResponse(SAE.J2534.Message message)
		{
			// Response should be on CAN ID 0x7A0
			// CAN ID format: [0x00, 0x00, upper 3 bits, lower 8 bits]
			// For 0x7A0 = 0b111 1010 0000
			// Upper 3 bits: 0b111 = 0x07
			// Lower 8 bits: 0b1010 0000 = 0xA0
			// Expected: [0x00, 0x00, 0x07, 0xA0]

			uint canId = ((uint)message.Data[2] << 8) | message.Data[3];

			if (canId != RESPONSE_CAN_ID)
			{
				Debug.WriteLine($"T6RMA: Unexpected CAN ID in response: 0x{canId:X3}, expected 0x{RESPONSE_CAN_ID:X3}");
				return null;
			}

			// Data starts after the 4-byte CAN header
			// The actual data length is determined by the DLC field
			int dataLength = message.Data.Length - 4;
			if (dataLength <= 0)
			{
				Debug.WriteLine("T6RMA: Empty response data");
				return null;
			}

			byte[] responseData = new byte[dataLength];
			Array.Copy(message.Data, 4, responseData, 0, dataLength);

			Debug.WriteLine($"T6RMA: Received {dataLength} bytes from ECU");
			return responseData;
		}

		private void WriteCsvEntry(DateTime timestamp, long relativeTimeMs, byte[] data)
		{
			if (_sink is not ISampleSink sink)
			{
				return;
			}

			try
			{
				// A short reply leaves the remaining columns holding their previous value, which is
				// what every other logger does when the ECU does not answer for a channel.
				int count = Math.Min(data.Length, _byteColumns.Length);
				for (int i = 0; i < count; i++)
				{
					sink.Set(_byteColumns[i], data[i]);
				}

				sink.WriteRow(timestamp, relativeTimeMs);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error writing CSV entry: {ex.Message}");
			}
		}

		private void CleanupResources()
		{
			try
			{
				_sink?.Dispose();
				_sink = null;
				_byteColumns = [];

				// Disposing the session releases its channel, device, and API handle.
				_session?.Dispose();
				_session = null;
				_channel = null;

				_currentAddress = null;
				_currentLength = 0;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error during cleanup: {ex.Message}");
			}
		}

		private static void ValidateMemoryAddress(uint address, byte length)
		{
			if (length < 1 || length > 255)
			{
				throw new ArgumentOutOfRangeException(nameof(length), "Length must be between 1 and 255 bytes");
			}

			bool isValidRam = (address >= RAM_START && address <= RAM_END - length + 1);

			if (!isValidRam)
			{
				throw new ArgumentOutOfRangeException(
					nameof(address),
					$"Invalid memory address 0x{address:X8}. Valid range: " +
					$"RAM (0x{RAM_START:X8}-0x{RAM_END:X8})");
			}
		}

		public async Task<bool> ReadMemoryToFileAsync(uint startAddress, uint length, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null)
		{
			// Validate parameters
			if (length == 0)
			{
				throw new ArgumentException("Length must be greater than 0", nameof(length));
			}

			if (string.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException("File path cannot be empty", nameof(filePath));
			}

			// Validate address range
			if (startAddress < RAM_START || startAddress > RAM_END)
			{
				throw new ArgumentOutOfRangeException(
					nameof(startAddress),
					$"Invalid memory address 0x{startAddress:X8}. Valid range: RAM (0x{RAM_START:X8}-0x{RAM_END:X8})");
			}

			if (startAddress + length - 1 > RAM_END)
			{
				throw new ArgumentOutOfRangeException(
					nameof(length),
					$"Memory range exceeds RAM bounds. Start: 0x{startAddress:X8}, Length: {length}, End: 0x{startAddress + length - 1:X8}, Max: 0x{RAM_END:X8}");
			}

			return await ReadMemoryToFileCoreAsync(startAddress, length, filePath, progress);
		}

		public async Task<bool> DownloadLearnedDataAsync(EcuVariant variant, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null)
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException("File path cannot be empty", nameof(filePath));
			}

			var (address, length) = LearnedDataZones[variant];
			return await ReadMemoryToFileCoreAsync(address, length, filePath, progress);
		}

		public async Task<bool> DownloadCalibrationAsync(EcuVariant variant, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null)
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException("File path cannot be empty", nameof(filePath));
			}

			var (address, length) = CalibrationZones[variant];
			return await ReadMemoryToFileCoreAsync(address, length, filePath, progress);
		}

		public async Task<bool> DownloadProgramAsync(EcuVariant variant, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null)
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException("File path cannot be empty", nameof(filePath));
			}

			var (address, length) = ProgramZones[variant];
			return await ReadMemoryToFileCoreAsync(address, length, filePath, progress);
		}

		private async Task<bool> ReadMemoryToFileCoreAsync(uint startAddress, uint length, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress)
		{
			const byte MAX_CHUNK_SIZE = 255; // Maximum bytes per RMA read request

			J2534Session? tempSession = null;
			J2534Channel? tempChannel = null;

			try
			{
				// Initialize J2534 device and CAN channel
				tempSession = J2534Session.Open();
				tempChannel = tempSession.OpenCan();

				// Set up CAN filter to receive responses on 0x7A0
				var passFilter = new MessageFilter
				{
					FilterType = Filter.PASS_FILTER,
					Mask = [0x00, 0x00, 0x07, 0xFF],
					Pattern = [0x00, 0x00, 0x07, 0xA0]
				};
				tempChannel.StartMessageFilter(passFilter).ThrowIfError();

				Debug.WriteLine($"T6RMA: Reading {length} bytes from 0x{startAddress:X8} to {filePath}");

				// Create output file
				using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

				uint currentAddress = startAddress;
				uint bytesRemaining = length;
				int totalBytesRead = 0;
				int chunkNumber = 0;

				while (bytesRemaining > 0)
				{
					chunkNumber++;

					// Calculate chunk size for this iteration
					byte chunkSize = (byte)Math.Min(bytesRemaining, MAX_CHUNK_SIZE);

					// Read chunk from ECU
					byte[]? chunkData = await Task.Run(() => SendMemoryReadRequestWithChannel(tempChannel, currentAddress, chunkSize));

					if (chunkData == null || chunkData.Length == 0)
					{
						Debug.WriteLine($"T6RMA: Failed to read chunk {chunkNumber} at address 0x{currentAddress:X8}");
						return false;
					}

					// Only write the exact number of bytes we requested (ECU might return more)
					int bytesToWrite = Math.Min(chunkData.Length, chunkSize);
					await fileStream.WriteAsync(chunkData, 0, bytesToWrite);

					// Update progress based on bytes actually written (which is the requested amount)
					totalBytesRead += bytesToWrite;
					currentAddress += (uint)bytesToWrite;
					bytesRemaining -= (uint)bytesToWrite;

					progress?.Report((totalBytesRead, (int)length));

					int percentComplete = totalBytesRead * 100 / (int)length;
					Debug.WriteLine($"T6RMA: Chunk {chunkNumber}: Read 0x{currentAddress - (uint)bytesToWrite:X8}-0x{currentAddress - 1:X8} ({bytesToWrite} bytes) - Total: {totalBytesRead}/{length} ({percentComplete}%)");
				}

				Debug.WriteLine($"T6RMA: Successfully read {totalBytesRead} bytes to {filePath}");
				return true;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error reading memory to file: {ex.Message}");
				throw new InvalidOperationException($"Failed to read ECU memory: {ex.Message}", ex);
			}
			finally
			{
				// Cleanup temporary session (disposes its channel, device, and API)
				tempSession?.Dispose();
			}
		}

		private byte[]? SendMemoryReadRequestWithChannel(J2534Channel channel, uint address, byte length, int totalTimeoutMs = 2000)
		{
			// Build CAN message for memory read request (CAN ID 0x53)
			byte[] canMessage = new byte[9];

			canMessage[0] = 0x00;
			canMessage[1] = 0x00;
			canMessage[2] = 0x00;
			canMessage[3] = 0x53;

			// Address in BIG-ENDIAN
			canMessage[4] = (byte)((address >> 24) & 0xFF);
			canMessage[5] = (byte)((address >> 16) & 0xFF);
			canMessage[6] = (byte)((address >> 8) & 0xFF);
			canMessage[7] = (byte)(address & 0xFF);
			canMessage[8] = length;

			try
			{
				// Send the request
				channel.SendMessage(canMessage);

				// For multi-frame responses, we need to collect multiple CAN messages
				// Each CAN frame can carry ~8 bytes of data, so for 255 bytes we need ~32 frames
				// We'll collect messages until we have the requested length or timeout
				List<byte> assembledData = new List<byte>(length);
				var stopwatch = Stopwatch.StartNew();
				int TOTAL_TIMEOUT_MS = totalTimeoutMs; // Total timeout for collecting all frames

				while (assembledData.Count < length && stopwatch.ElapsedMilliseconds < TOTAL_TIMEOUT_MS)
				{
					// Calculate remaining bytes needed
					int remainingBytes = length - assembledData.Count;

					// Calculate how many messages we might need (assuming ~8 bytes per message)
					// Request a few more than calculated to avoid multiple iterations
					int messagesToRequest = Math.Max(1, (remainingBytes + 7) / 8);

					// Wait for response messages (shorter timeout per batch)
					var response = channel.ReadMessages(messagesToRequest, 200);

					if (response.Messages.Length > 0)
					{
						foreach (var message in response.Messages)
						{
							byte[]? frameData = ParseMemoryReadResponse(message);
							if (frameData != null && frameData.Length > 0)
							{
								// Add the data from this frame
								int bytesToTake = Math.Min(frameData.Length, remainingBytes);
								assembledData.AddRange(frameData.Take(bytesToTake));

								// Break if we have all the data we need
								if (assembledData.Count >= length)
								{
									break;
								}
							}
						}
					}
					else
					{
						// No more messages available, break out
						if (assembledData.Count > 0)
						{
							// We got some data, so return what we have
							Debug.WriteLine($"T6RMA: Collected {assembledData.Count} of {length} requested bytes before timeout");
							break;
						}
						else
						{
							Debug.WriteLine("T6RMA: No response received within timeout");
							return null;
						}
					}
				}

				if (assembledData.Count > 0)
				{
					Debug.WriteLine($"T6RMA: Assembled {assembledData.Count} bytes from multi-frame response");
					return [.. assembledData];
				}
				else
				{
					Debug.WriteLine("T6RMA: No data received");
					return null;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error sending memory read request: {ex.Message}");
				throw;
			}
		}

		public Task<T6RMAUploadResult> WriteFileToMemoryAsync(
			uint baseAddress,
			string filePath,
			bool verify = true,
			bool checkHeader = true,
			IProgress<T6RMAUploadProgress>? progress = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException("File path cannot be empty", nameof(filePath));
			}

			if (!File.Exists(filePath))
			{
				throw new FileNotFoundException("Calibration file not found", filePath);
			}

			byte[] image = File.ReadAllBytes(filePath);

			if (image.Length == 0)
			{
				throw new ArgumentException($"Calibration file is empty: {filePath}", nameof(filePath));
			}

			// Unlike the read path, which also reaches the flash-mapped regions, the RMA write
			// commands are serviced for RAM only — so the upload target is bounded by RAM.
			if (baseAddress < RAM_START)
			{
				throw new ArgumentOutOfRangeException(
					nameof(baseAddress),
					$"Invalid base address 0x{baseAddress:X8}. Uploads target RAM (0x{RAM_START:X8}-0x{RAM_END:X8}).");
			}

			if ((ulong)baseAddress + (ulong)image.Length - 1 > RAM_END)
			{
				throw new ArgumentOutOfRangeException(
					nameof(filePath),
					$"Image does not fit in RAM. Base: 0x{baseAddress:X8}, Size: {image.Length} bytes, " +
					$"End: 0x{(ulong)baseAddress + (ulong)image.Length - 1:X8}, Max: 0x{RAM_END:X8}");
			}

			return Task.Run(
				() => WriteImageToMemory(baseAddress, image, verify, checkHeader, progress, cancellationToken),
				cancellationToken);
		}

		/// <summary>
		/// Sends the image and, when asked, verifies it. Reuses the logging channel if a session is
		/// already running, since the J2534 device cannot be opened twice.
		/// </summary>
		private T6RMAUploadResult WriteImageToMemory(
			uint baseAddress,
			byte[] image,
			bool verify,
			bool checkHeader,
			IProgress<T6RMAUploadProgress>? progress,
			CancellationToken cancellationToken)
		{
			J2534Channel? activeChannel;
			lock (_lock)
			{
				activeChannel = _isLogging ? _channel : null;
			}

			J2534Session? tempSession = null;

			try
			{
				J2534Channel channel;
				if (activeChannel != null)
				{
					channel = activeChannel;
				}
				else
				{
					tempSession = J2534Session.Open();
					channel = tempSession.OpenCan();

					// Writes draw no reply, but the verification pass reads on 0x7A0.
					var passFilter = new MessageFilter
					{
						FilterType = Filter.PASS_FILTER,
						Mask = [0x00, 0x00, 0x07, 0xFF],
						Pattern = [0x00, 0x00, 0x07, 0xA0]
					};
					channel.StartMessageFilter(passFilter).ThrowIfError();
				}

				Debug.WriteLine($"T6RMA: Uploading {image.Length} bytes to 0x{baseAddress:X8} (verify={verify}, checkHeader={checkHeader})");

				if (checkHeader)
				{
					CheckHeaderMatches(channel, baseAddress, image);
				}

				SendImage(channel, baseAddress, image, progress, cancellationToken);

				if (!verify)
				{
					return new T6RMAUploadResult { BytesWritten = image.Length, VerificationRan = false };
				}

				return VerifyImage(channel, baseAddress, image, progress, cancellationToken);
			}
			finally
			{
				// Only a session we opened is ours to dispose; the logging session (when reused
				// above) is owned by the logging lifecycle.
				tempSession?.Dispose();
			}
		}

		/// <summary>
		/// Reads the head of the target region and compares it against the head of the image, before
		/// any frame goes out.
		/// </summary>
		/// <remarks>
		/// An upload replaces a running calibration wholesale, and the verification pass afterwards
		/// only proves the bytes arrived intact — it reports a clean success whether or not the file
		/// had any business being written. This is the check that can tell the difference, on the
		/// assumption that a calibration's opening bytes identify it and are not rewritten at runtime.
		/// A pre-flight read that comes back short or empty is treated as a failure rather than a
		/// pass: an unanswered read means the match cannot be established, which is not the same as
		/// establishing a match.
		/// </remarks>
		/// <exception cref="T6RMAHeaderMismatchException">The heads differ.</exception>
		private void CheckHeaderMatches(J2534Channel channel, uint baseAddress, byte[] image)
		{
			int length = Math.Min(HeaderPreflightBytes, image.Length);

			byte[]? head = SendMemoryReadRequestWithChannel(channel, baseAddress, (byte)length);

			if (head == null || head.Length < length)
			{
				throw new InvalidOperationException(
					$"Pre-flight read at 0x{baseAddress:X8} returned {head?.Length ?? 0} of {length} bytes, " +
					"so the file could not be matched against the calibration in memory. Nothing was written.");
			}

			if (head.AsSpan(0, length).SequenceEqual(image.AsSpan(0, length)))
			{
				Debug.WriteLine($"T6RMA: Pre-flight header check passed ({length} bytes at 0x{baseAddress:X8})");
				return;
			}

			Debug.WriteLine($"T6RMA: Pre-flight header check FAILED at 0x{baseAddress:X8}");
			throw new T6RMAHeaderMismatchException(baseAddress, image[..length], head[..length]);
		}

		/// <summary>
		/// Writes the image one word per CAN frame.
		/// </summary>
		/// <remarks>
		/// The file's bytes are copied straight into the frame payload rather than being routed
		/// through a uint, so the upload preserves the byte order of the file exactly and a
		/// read-then-upload round trip is an identity.
		///
		/// Frames go out one at a time rather than batched through <c>SendMessages</c>. RMA writes
		/// are fire-and-forget, so nothing throttles the host: the per-call round trip through the
		/// pass-thru device is what paces the burst, which keeps the ECU's CAN receive path from
		/// being overrun by frames it would silently drop. Batching would be the obvious speed-up if
		/// an upload ever proves too slow, but it needs the ECU's actual tolerance measured first.
		/// </remarks>
		private static void SendImage(
			J2534Channel channel,
			uint baseAddress,
			byte[] image,
			IProgress<T6RMAUploadProgress>? progress,
			CancellationToken cancellationToken)
		{
			// Reused across frames: SendMessage marshals the payload before returning, so the
			// buffer is free to be rewritten for the next word.
			// Layout: [CAN ID (4)][Address (4, big-endian)][Data (4, file order)]
			byte[] wordFrame = new byte[12];
			wordFrame[3] = 0x54;

			int wholeWords = image.Length / 4;
			int nextReportAt = 0;

			for (int word = 0; word < wholeWords; word++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				int offset = word * 4;
				WriteAddressBigEndian(wordFrame, baseAddress + (uint)offset);
				image.AsSpan(offset, 4).CopyTo(wordFrame.AsSpan(8, 4));

				channel.SendMessage(wordFrame).ThrowIfError();

				if (offset >= nextReportAt)
				{
					progress?.Report(new T6RMAUploadProgress(T6RMAUploadPhase.Writing, offset + 4, image.Length));
					nextReportAt = offset + UploadProgressStrideBytes;
				}
			}

			// A file whose length is not a multiple of 4 leaves a 1-3 byte tail. Single-byte writes
			// finish it, so an odd-sized region uploads whole instead of being rejected or truncated.
			// Layout: [CAN ID (4)][Address (4, big-endian)][Data (1)]
			byte[] byteFrame = new byte[9];
			byteFrame[3] = 0x56;

			for (int offset = wholeWords * 4; offset < image.Length; offset++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				WriteAddressBigEndian(byteFrame, baseAddress + (uint)offset);
				byteFrame[8] = image[offset];

				channel.SendMessage(byteFrame).ThrowIfError();
			}

			progress?.Report(new T6RMAUploadProgress(T6RMAUploadPhase.Writing, image.Length, image.Length));
		}

		/// <summary>
		/// Reads the region back and compares it against the image that was just written. Writes draw
		/// no acknowledgement from the ECU, so this is the only evidence that they landed.
		/// </summary>
		private T6RMAUploadResult VerifyImage(
			J2534Channel channel,
			uint baseAddress,
			byte[] image,
			IProgress<T6RMAUploadProgress>? progress,
			CancellationToken cancellationToken)
		{
			const byte MAX_CHUNK_SIZE = 255;
			const int MAX_REPORTED_MISMATCHES = 16;

			var sampleMismatches = new List<uint>(MAX_REPORTED_MISMATCHES);
			int mismatchCount = 0;
			int offset = 0;

			progress?.Report(new T6RMAUploadProgress(T6RMAUploadPhase.Verifying, 0, image.Length));

			while (offset < image.Length)
			{
				cancellationToken.ThrowIfCancellationRequested();

				byte chunkSize = (byte)Math.Min(image.Length - offset, MAX_CHUNK_SIZE);
				byte[]? chunk = SendMemoryReadRequestWithChannel(channel, baseAddress + (uint)offset, chunkSize);

				if (chunk == null || chunk.Length < chunkSize)
				{
					throw new InvalidOperationException(
						$"Verification read failed at 0x{baseAddress + (uint)offset:X8}: " +
						$"expected {chunkSize} bytes, got {chunk?.Length ?? 0}. " +
						"The upload itself may or may not have landed.");
				}

				for (int i = 0; i < chunkSize; i++)
				{
					if (chunk[i] == image[offset + i])
					{
						continue;
					}

					mismatchCount++;
					if (sampleMismatches.Count < MAX_REPORTED_MISMATCHES)
					{
						sampleMismatches.Add(baseAddress + (uint)(offset + i));
					}
				}

				offset += chunkSize;
				progress?.Report(new T6RMAUploadProgress(T6RMAUploadPhase.Verifying, offset, image.Length));
			}

			Debug.WriteLine($"T6RMA: Verification finished with {mismatchCount} mismatching byte(s)");

			return new T6RMAUploadResult
			{
				BytesWritten = image.Length,
				VerificationRan = true,
				MismatchCount = mismatchCount,
				SampleMismatchAddresses = sampleMismatches
			};
		}

		/// <summary>Writes an address into a frame's 4-byte address field in big-endian order.</summary>
		private static void WriteAddressBigEndian(byte[] frame, uint address)
		{
			frame[4] = (byte)((address >> 24) & 0xFF);
			frame[5] = (byte)((address >> 16) & 0xFF);
			frame[6] = (byte)((address >> 8) & 0xFF);
			frame[7] = (byte)(address & 0xFF);
		}

		public async Task WriteWordAsync(uint address, uint value)
		{
			// Validate address is in RAM range
			if (address < RAM_START || address > RAM_END - 3)
			{
				throw new ArgumentOutOfRangeException(
					nameof(address),
					$"Invalid memory address 0x{address:X8}. Valid range: RAM (0x{RAM_START:X8}-0x{RAM_END - 3:X8})");
			}

			J2534Channel? channelToUse = null;
			J2534Session? tempSession = null;

			try
			{
				// Check if we have an active channel from logging
				lock (_lock)
				{
					if (_channel != null && _isLogging)
					{
						channelToUse = _channel;
					}
				}

				// If no active channel, create a temporary one
				if (channelToUse == null)
				{
					tempSession = J2534Session.Open();
					channelToUse = tempSession.OpenCan();
				}

				Debug.WriteLine($"T6RMA: Writing word to ECU - Address=0x{address:X8}, Value=0x{value:X8}");

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
				await Task.Run(() => channelToUse.SendMessage(canMessage));

				Debug.WriteLine($"T6RMA: Write command sent successfully");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error writing word to ECU: {ex.Message}");
				throw new InvalidOperationException($"Failed to write word to ECU: {ex.Message}", ex);
			}
			finally
			{
				// Only the temporary session is ours to dispose; the logging session
				// (when reused above) is owned by the logging lifecycle.
				tempSession?.Dispose();
			}
		}

		public async Task WriteByteAsync(uint address, byte value)
		{
			// Validate address is in RAM range
			if (address < RAM_START || address > RAM_END)
			{
				throw new ArgumentOutOfRangeException(
					nameof(address),
					$"Invalid memory address 0x{address:X8}. Valid range: RAM (0x{RAM_START:X8}-0x{RAM_END:X8})");
			}

			J2534Channel? channelToUse = null;
			J2534Session? tempSession = null;

			try
			{
				// Check if we have an active channel from logging
				lock (_lock)
				{
					if (_channel != null && _isLogging)
					{
						channelToUse = _channel;
					}
				}

				// If no active channel, create a temporary one
				if (channelToUse == null)
				{
					tempSession = J2534Session.Open();
					channelToUse = tempSession.OpenCan();
				}

				Debug.WriteLine($"T6RMA: Writing byte to ECU - Address=0x{address:X8}, Value=0x{value:X2}");

				// Build CAN message for single-byte memory write (CAN ID 0x56)
				// Format: [CAN ID (4 bytes)][Address (4 bytes, big-endian)][Data (1 byte)]
				// The 5-byte CAN payload sets DLC=5, which the firmware requires for 0x56.
				byte[] canMessage = new byte[9];

				// CAN ID 0x56
				canMessage[0] = 0x00;
				canMessage[1] = 0x00;
				canMessage[2] = 0x00;
				canMessage[3] = 0x56;

				// Address in BIG-ENDIAN format
				canMessage[4] = (byte)((address >> 24) & 0xFF);
				canMessage[5] = (byte)((address >> 16) & 0xFF);
				canMessage[6] = (byte)((address >> 8) & 0xFF);
				canMessage[7] = (byte)(address & 0xFF);

				canMessage[8] = value;

				// Send the write command (fire-and-forget, no response expected)
				await Task.Run(() => channelToUse.SendMessage(canMessage));

				Debug.WriteLine($"T6RMA: Byte write command sent successfully");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error writing byte to ECU: {ex.Message}");
				throw new InvalidOperationException($"Failed to write byte to ECU: {ex.Message}", ex);
			}
			finally
			{
				// Only the temporary session is ours to dispose; the logging session
				// (when reused above) is owned by the logging lifecycle.
				tempSession?.Dispose();
			}
		}

		public bool IsEcuUnlocked()
		{
			// The firmware only services RMA reads when ecu_unlocked == true, replying on
			// CAN ID 0x7A0. A single read at RAM_START therefore tells us the unlock state:
			// a response => unlocked, silence => locked (or ECU not present).
			const int PROBE_TIMEOUT_MS = 150;

			J2534Session? tempSession = null;

			try
			{
				// Open a temporary CAN connection (same pattern as ReadMemoryToFileAsync).
				tempSession = J2534Session.Open();
				J2534Channel tempChannel = tempSession.OpenCan();

				// Set up CAN filter to receive responses on 0x7A0
				var passFilter = new MessageFilter
				{
					FilterType = Filter.PASS_FILTER,
					Mask = [0x00, 0x00, 0x07, 0xFF],
					Pattern = [0x00, 0x00, 0x07, 0xA0]
				};
				tempChannel.StartMessageFilter(passFilter).ThrowIfError();

				byte[]? responseData = SendMemoryReadRequestWithChannel(tempChannel, RAM_START, 4, PROBE_TIMEOUT_MS);
				return responseData is { Length: > 0 };
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Unlock probe failed: {ex.Message}");
				return false;
			}
			finally
			{
				tempSession?.Dispose();
			}
		}
	}
}
