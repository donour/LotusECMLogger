using SAE.J2534;

namespace LotusECMLogger.Services
{
	/// <summary>
	/// Event arguments for T6 RMA (Remote Memory Access) data received
	/// </summary>
	public class T6RMADataEventArgs : EventArgs
	{
		public DateTime Timestamp { get; set; }
		public uint MemoryAddress { get; set; }
		public byte[] Data { get; set; } = [];
		public int DataLength { get; set; }
	}

	/// <summary>
	/// Service for reading ECU memory addresses using the T6 RMA (Remote Memory Access) protocol
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

		/// <summary>
		/// Read a block of ECU memory and save it to a binary file
		/// </summary>
		/// <param name="startAddress">Starting memory address (RAM: 0x40000000-0x4000FFFF)</param>
		/// <param name="length">Number of bytes to read</param>
		/// <param name="filePath">Path where the binary file will be saved</param>
		/// <param name="progress">Optional progress callback (bytesRead, totalBytes)</param>
		/// <returns>True if successful, false otherwise</returns>
		Task<bool> ReadMemoryToFileAsync(uint startAddress, uint length, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null);

		/// <summary>
		/// Downloads the ECU's flash-resident Learned Data region (persisted adaptive
		/// fuel/idle/knock trims) to a binary file, using the same RMA read protocol as
		/// <see cref="ReadMemoryToFileAsync"/>. This targets flash (or, on T4e, a small RAM/DECRAM
		/// window) rather than the main RAM window, at the address for the given <paramref name="variant"/>,
		/// mirroring the "Learned" zone for that generation in the reference lotusecu-tools dumper.
		/// </summary>
		/// <param name="variant">Which ECU generation's memory map to use.</param>
		/// <param name="filePath">Path where the binary dump will be saved.</param>
		/// <param name="progress">Optional progress callback (bytesRead, totalBytes).</param>
		/// <returns>True if successful, false otherwise.</returns>
		/// <remarks>
		/// Requires the ECU to be unlocked (see <see cref="IsEcuUnlocked"/>); a locked ECU
		/// will not respond to the underlying memory reads.
		/// </remarks>
		Task<bool> DownloadLearnedDataAsync(EcuVariant variant, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null);

		/// <summary>
		/// Downloads the ECU's flash-resident Calibration region (the active tune: fuel/ignition
		/// maps, limiters, etc.) to a binary file, using the same RMA read protocol as
		/// <see cref="ReadMemoryToFileAsync"/>, at the address for the given <paramref name="variant"/>,
		/// mirroring the "Calibration" zone for that generation in the reference lotusecu-tools dumper.
		/// </summary>
		/// <param name="variant">Which ECU generation's memory map to use.</param>
		/// <param name="filePath">Path where the binary dump will be saved.</param>
		/// <param name="progress">Optional progress callback (bytesRead, totalBytes).</param>
		/// <returns>True if successful, false otherwise.</returns>
		/// <remarks>
		/// Requires the ECU to be unlocked (see <see cref="IsEcuUnlocked"/>); a locked ECU
		/// will not respond to the underlying memory reads.
		/// </remarks>
		Task<bool> DownloadCalibrationAsync(EcuVariant variant, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null);

		/// <summary>
		/// Downloads the ECU's flash-resident Program region (the compiled firmware code) to a
		/// binary file, using the same RMA read protocol as <see cref="ReadMemoryToFileAsync"/>, at
		/// the address for the given <paramref name="variant"/>, mirroring the "Program" zone for
		/// that generation in the reference lotusecu-tools dumper. It is the largest of the flash
		/// regions on every variant, so a full download can take a while over CAN.
		/// </summary>
		/// <param name="variant">Which ECU generation's memory map to use.</param>
		/// <param name="filePath">Path where the binary dump will be saved.</param>
		/// <param name="progress">Optional progress callback (bytesRead, totalBytes).</param>
		/// <returns>True if successful, false otherwise.</returns>
		/// <remarks>
		/// Requires the ECU to be unlocked (see <see cref="IsEcuUnlocked"/>); a locked ECU
		/// will not respond to the underlying memory reads.
		/// </remarks>
		Task<bool> DownloadProgramAsync(EcuVariant variant, string filePath, IProgress<(int bytesRead, int totalBytes)>? progress = null);

		/// <summary>
		/// Write a 32-bit word to ECU memory using T6 RMA protocol (CAN ID 0x54)
		/// </summary>
		/// <param name="address">ECU memory address (must be in RAM: 0x40000000-0x4000FFFF)</param>
		/// <param name="value">32-bit value to write (will be sent in big-endian format)</param>
		/// <returns>Task representing the async write operation</returns>
		/// <remarks>
		/// Write operations are fire-and-forget in the T6 RMA protocol (no response expected).
		/// The method validates the address is in the valid RAM range before sending.
		/// </remarks>
		Task WriteWordAsync(uint address, uint value);

		/// <summary>
		/// Write a single byte to ECU memory using T6 RMA protocol (CAN ID 0x56).
		/// </summary>
		/// <param name="address">ECU memory address (must be in RAM: 0x40000000-0x4000FFFF)</param>
		/// <param name="value">Byte value to write</param>
		/// <returns>Task representing the async write operation</returns>
		/// <remarks>
		/// Write operations are fire-and-forget in the T6 RMA protocol (no response expected).
		/// The method validates the address is in the valid RAM range before sending.
		/// </remarks>
		Task WriteByteAsync(uint address, byte value);

		/// <summary>
		/// Probes whether the ECU is unlocked (ecu_unlocked == true) by attempting a single
		/// RMA memory read at 0x40000000. The firmware processes RMA reads only when unlocked,
		/// so a response on CAN ID 0x7A0 indicates an unlocked ECU and silence indicates a
		/// locked ECU (or no ECU present).
		/// </summary>
		/// <returns>True if the ECU replied to the read (unlocked); false if it stayed silent.</returns>
		/// <remarks>
		/// Must not be called while a logging session is active, since it opens its own
		/// temporary CAN channel on the J2534 device.
		/// </remarks>
		bool IsEcuUnlocked();
	}
}
