using SAE.J2534;

namespace LotusECMLogger.Services
{
	/// <summary>
	/// Event arguments for T6 RMA (Read Memory Address) data received
	/// </summary>
	public class T6RMADataEventArgs : EventArgs
	{
		public DateTime Timestamp { get; set; }
		public uint MemoryAddress { get; set; }
		public byte[] Data { get; set; } = [];
		public int DataLength { get; set; }
	}

	/// <summary>
	/// Service for reading ECU memory addresses using the T6 RMA protocol
	/// Protocol reverse-engineered from firmware function flexcan_a_rx_50_51_52_53()
	/// </summary>
	public interface IT6RMAService : IDisposable
	{
		/// <summary>
		/// Event fired when new memory data is received from the ECU
		/// </summary>
		event EventHandler<T6RMADataEventArgs>? DataReceived;

		/// <summary>
		/// Event fired when an error occurs during logging
		/// </summary>
		event EventHandler<string>? ErrorOccurred;

		/// <summary>
		/// Start logging a specific memory address at regular intervals
		/// </summary>
		/// <param name="memoryAddress">32-bit memory address to read (RAM: 0x40000000-0x4000FFFF)</param>
		/// <param name="length">Number of bytes to read (1-255)</param>
		/// <param name="intervalMs">Polling interval in milliseconds</param>
		/// <param name="csvFilePath">Path to save CSV log file</param>
		void StartLogging(uint memoryAddress, byte length, int intervalMs, string csvFilePath);

		/// <summary>
		/// Stop the current logging session
		/// </summary>
		void StopLogging();

		/// <summary>
		/// Check if logging is currently active
		/// </summary>
		bool IsLogging { get; }

		/// <summary>
		/// Get the current memory address being logged
		/// </summary>
		uint? CurrentAddress { get; }
	}
}
