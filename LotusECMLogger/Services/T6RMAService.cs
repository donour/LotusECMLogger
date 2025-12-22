using SAE.J2534;
using System.Diagnostics;
using System.Text;

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
	/// • Valid Address Range: 0x40000000 - 0x4000FFFF (64KB RAM only)
	/// • Multi-frame Read (0x53): ECU sends first 8 bytes immediately, continuation via 0x7A0
	/// • Multi-frame Write (0x57): Host sends continuation frames after initial command
	/// • Fixed-length reads (0x50-0x52): Optimized for atomic register/variable access
	/// • Variable-length (0x53/0x57): Flexible for arbitrary memory dumps/updates
	///
	/// CURRENT IMPLEMENTATION:
	/// ─────────────────────────────────────────────────────────────────────────────
	/// This service currently implements CAN ID 0x53 (variable-length read) only.
	/// Future expansion could add support for:
	/// - Fixed-length reads (0x50-0x52) for faster single-value polling
	/// - Write operations (0x54-0x57) for memory modification and calibration updates
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

		private Device? _device;
		private Channel? _channel;
		private Thread? _loggingThread;
		private bool _isLogging;
		private uint? _currentAddress;
		private byte _currentLength;
		private int _intervalMs;
		private string? _csvFilePath;
		private StreamWriter? _csvWriter;
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
				_csvFilePath = csvFilePath;

				try
				{
					// Initialize J2534 device and CAN channel
					InitializeDevice();

					// Initialize CSV file
					InitializeCsvFile();

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
			string dllFileName = APIFactory.GetAPIinfo().First().Filename;
			API api = APIFactory.GetAPI(dllFileName);
			_device = api.GetDevice();

			// Use raw CAN protocol at 500 kbaud (standard for automotive CAN)
			_channel = _device.GetChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE);

			// Set up CAN filter to receive responses on 0x7A0
			var passFilter = new MessageFilter
			{
				FilterType = Filter.PASS_FILTER,
				Mask = [0x00, 0x00, 0x07, 0xFF],      // Match all 11 bits of CAN ID
				Pattern = [0x00, 0x00, 0x07, 0xA0]     // CAN ID 0x7A0
			};
			_channel.StartMsgFilter(passFilter);

			Debug.WriteLine("T6RMA: J2534 device initialized with CAN protocol at 500 kbaud");
		}

		private void InitializeCsvFile()
		{
			try
			{
				_csvWriter = new StreamWriter(_csvFilePath!, false, Encoding.UTF8);

				// Write CSV header
				_csvWriter.WriteLine($"# T6 RMA Memory Logging Session");
				_csvWriter.WriteLine($"# Memory Address: 0x{_currentAddress:X8}");
				_csvWriter.WriteLine($"# Length: {_currentLength} bytes");
				_csvWriter.WriteLine($"# Interval: {_intervalMs}ms");
				_csvWriter.WriteLine($"# Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

				// Write column headers
				var headers = new StringBuilder();
				headers.Append("Timestamp,RelativeTime_ms,Address");
				for (int i = 0; i < _currentLength; i++)
				{
					headers.Append($",Byte{i}");
				}
				_csvWriter.WriteLine(headers.ToString());
				_csvWriter.Flush();

				Debug.WriteLine($"T6RMA: CSV file initialized: {_csvFilePath}");
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
							WriteCsvEntry(timestamp, relativeTimeMs, _currentAddress.Value, responseData);
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
				_channel.SendMessages([canMessage]);

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
					var response = _channel.GetMessages(messagesToRequest, 200);

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

		private void WriteCsvEntry(DateTime timestamp, long relativeTimeMs, uint address, byte[] data)
		{
			if (_csvWriter == null)
			{
				return;
			}

			try
			{
				var csv = new StringBuilder();
				csv.Append($"{timestamp:yyyy-MM-dd HH:mm:ss.fff},");
				csv.Append($"{relativeTimeMs},");
				csv.Append($"0x{address:X8}");

				foreach (byte b in data)
				{
					csv.Append($",0x{b:X2}");
				}

				_csvWriter.WriteLine(csv.ToString());
				_csvWriter.Flush(); // Ensure data is written immediately
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
				_csvWriter?.Close();
				_csvWriter?.Dispose();
				_csvWriter = null;

				_channel?.Dispose();
				_channel = null;

				_device?.Dispose();
				_device = null;

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
			const byte MAX_CHUNK_SIZE = 255; // Maximum bytes per RMA read request

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

			Device? tempDevice = null;
			Channel? tempChannel = null;

			try
			{
				// Initialize J2534 device and CAN channel
				string dllFileName = APIFactory.GetAPIinfo().First().Filename;
				API api = APIFactory.GetAPI(dllFileName);
				tempDevice = api.GetDevice();

				tempChannel = tempDevice.GetChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE);

				// Set up CAN filter to receive responses on 0x7A0
				var passFilter = new MessageFilter
				{
					FilterType = Filter.PASS_FILTER,
					Mask = [0x00, 0x00, 0x07, 0xFF],
					Pattern = [0x00, 0x00, 0x07, 0xA0]
				};
				tempChannel.StartMsgFilter(passFilter);

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
				// Cleanup temporary channel and device
				tempChannel?.Dispose();
				tempDevice?.Dispose();
			}
		}

		private byte[]? SendMemoryReadRequestWithChannel(Channel channel, uint address, byte length)
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
				channel.SendMessages([canMessage]);

				// For multi-frame responses, we need to collect multiple CAN messages
				// Each CAN frame can carry ~8 bytes of data, so for 255 bytes we need ~32 frames
				// We'll collect messages until we have the requested length or timeout
				List<byte> assembledData = new List<byte>(length);
				var stopwatch = Stopwatch.StartNew();
				const int TOTAL_TIMEOUT_MS = 2000; // Total timeout for collecting all frames

				while (assembledData.Count < length && stopwatch.ElapsedMilliseconds < TOTAL_TIMEOUT_MS)
				{
					// Calculate remaining bytes needed
					int remainingBytes = length - assembledData.Count;

					// Calculate how many messages we might need (assuming ~8 bytes per message)
					// Request a few more than calculated to avoid multiple iterations
					int messagesToRequest = Math.Max(1, (remainingBytes + 7) / 8);

					// Wait for response messages (shorter timeout per batch)
					var response = channel.GetMessages(messagesToRequest, 200);

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

		public async Task WriteWordAsync(uint address, uint value)
		{
			// Validate address is in RAM range
			if (address < RAM_START || address > RAM_END - 3)
			{
				throw new ArgumentOutOfRangeException(
					nameof(address),
					$"Invalid memory address 0x{address:X8}. Valid range: RAM (0x{RAM_START:X8}-0x{RAM_END - 3:X8})");
			}

			Channel? channelToUse = null;
			Device? tempDevice = null;
			bool usingTemporaryDevice = false;

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
					usingTemporaryDevice = true;
					string dllFileName = APIFactory.GetAPIinfo().First().Filename;
					API api = APIFactory.GetAPI(dllFileName);
					tempDevice = api.GetDevice();
					channelToUse = tempDevice.GetChannel(Protocol.CAN, (Baud)500000, ConnectFlag.NONE);
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
				await Task.Run(() => channelToUse.SendMessages([canMessage]));

				Debug.WriteLine($"T6RMA: Write command sent successfully");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"T6RMA: Error writing word to ECU: {ex.Message}");
				throw new InvalidOperationException($"Failed to write word to ECU: {ex.Message}", ex);
			}
			finally
			{
				// Only cleanup if we created a temporary device
				if (usingTemporaryDevice)
				{
					channelToUse?.Dispose();
					tempDevice?.Dispose();
				}
			}
		}
	}
}
