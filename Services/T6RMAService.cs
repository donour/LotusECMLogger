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

				// Wait for response with timeout
				var response = _channel.GetMessages(1, 500); // 500ms timeout

				if (response.Messages.Length > 0)
				{
					return ParseMemoryReadResponse(response.Messages[0]);
				}
				else
				{
					Debug.WriteLine("T6RMA: No response received within timeout");
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
	}
}
