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
	/// Which stage of an upload a progress report describes.
	/// </summary>
	public enum T6RMAUploadPhase
	{
		/// <summary>Sending the image to ECU memory.</summary>
		Writing,

		/// <summary>Reading the region back to confirm it matches the image.</summary>
		Verifying
	}

	/// <summary>
	/// Progress report for an upload. Unlike a read, an upload has two stages that each cover the
	/// whole region, so the phase is carried alongside the byte counts.
	/// </summary>
	public readonly record struct T6RMAUploadProgress(T6RMAUploadPhase Phase, int BytesDone, int TotalBytes);

	/// <summary>
	/// Outcome of an upload, including what the verification pass found.
	/// </summary>
	public sealed class T6RMAUploadResult
	{
		/// <summary>Bytes sent to the ECU.</summary>
		public int BytesWritten { get; init; }

		/// <summary>Whether the region was read back and compared after writing.</summary>
		public bool VerificationRan { get; init; }

		/// <summary>Bytes that read back differently from the uploaded image.</summary>
		public int MismatchCount { get; init; }

		/// <summary>
		/// Addresses of the first few mismatching bytes, for diagnostics. Capped, so this is a
		/// sample rather than the full set when <see cref="MismatchCount"/> is large.
		/// </summary>
		public IReadOnlyList<uint> SampleMismatchAddresses { get; init; } = [];

		/// <summary>
		/// True when nothing mismatched. An unverified upload reports true because nothing
		/// contradicted it — check <see cref="VerificationRan"/> to tell the two apart.
		/// </summary>
		public bool Success => MismatchCount == 0;
	}

	/// <summary>
	/// Thrown when an upload's pre-flight check finds that the head of the file does not match the
	/// head of the region it would overwrite, which means the file almost certainly belongs to a
	/// different calibration, a different ECU, or a different memory region.
	/// </summary>
	/// <remarks>
	/// Nothing has been written to the ECU when this is thrown — the check runs before the first
	/// frame goes out. Callers that genuinely intend to replace the running calibration with an
	/// unrelated one can retry with the check disabled.
	/// </remarks>
	public sealed class T6RMAHeaderMismatchException : Exception
	{
		public T6RMAHeaderMismatchException(uint address, byte[] expectedFromFile, byte[] actualFromEcu)
			: base($"The first {expectedFromFile.Length} bytes at 0x{address:X8} do not match the calibration file. " +
				   "Nothing was written.")
		{
			Address = address;
			ExpectedFromFile = expectedFromFile;
			ActualFromEcu = actualFromEcu;
		}

		/// <summary>Address the comparison started at.</summary>
		public uint Address { get; }

		/// <summary>The bytes the calibration file starts with.</summary>
		public byte[] ExpectedFromFile { get; }

		/// <summary>The bytes ECU memory currently holds at <see cref="Address"/>.</summary>
		public byte[] ActualFromEcu { get; }
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
		/// Uploads a binary calibration image into ECU RAM, the inverse of
		/// <see cref="ReadMemoryToFileAsync"/>: the file's bytes land in memory in the order they
		/// appear in the file, so a region read to a .cpt file and uploaded again is byte-for-byte
		/// what was read. The image is written with the RMA word-write command (CAN ID 0x54), one
		/// 4-byte word per CAN frame, with any trailing 1-3 bytes finished by single-byte writes
		/// (CAN ID 0x56). <paramref name="baseAddress"/> is where file offset 0 lands, and the file's
		/// own length determines how much is written.
		/// </summary>
		/// <param name="baseAddress">ECU address for file offset 0 (RAM: 0x40000000-0x4000FFFF).</param>
		/// <param name="filePath">Binary image to upload; its length sets the region size.</param>
		/// <param name="verify">
		/// When true, reads the region back afterwards and compares it against the image. RMA writes
		/// are fire-and-forget with no ECU acknowledgement, so this read-back is the only confirmation
		/// that every frame was accepted.
		/// </param>
		/// <param name="checkHeader">
		/// When true, compares the head of the region against the head of the file before writing
		/// anything and throws <see cref="T6RMAHeaderMismatchException"/> if they differ. This is the
		/// only check that can catch the wrong file being uploaded; <paramref name="verify"/> confirms
		/// that what was sent arrived, not that it should have been sent. Pass false to overwrite a
		/// running calibration with a deliberately unrelated image.
		/// </param>
		/// <param name="progress">Optional progress callback, reported for both phases.</param>
		/// <param name="cancellationToken">Cancels the upload between frames.</param>
		/// <returns>The outcome, including any bytes that failed verification.</returns>
		/// <exception cref="T6RMAHeaderMismatchException">
		/// The pre-flight check found the file does not belong to the calibration in memory. Nothing
		/// was written.
		/// </exception>
		/// <remarks>
		/// This writes into the memory a running engine is calibrated from, and the transfer is not
		/// atomic: until it completes, the ECU is running a mix of the old and new calibrations.
		/// Cancelling mid-upload leaves that mix in place — an ignition cycle reloads the calibration
		/// from flash. Requires an unlocked ECU (see <see cref="IsEcuUnlocked"/>); a locked ECU
		/// silently discards the writes, which surfaces as wholesale verification mismatches.
		/// </remarks>
		Task<T6RMAUploadResult> WriteFileToMemoryAsync(
			uint baseAddress,
			string filePath,
			bool verify = true,
			bool checkHeader = true,
			IProgress<T6RMAUploadProgress>? progress = null,
			CancellationToken cancellationToken = default);

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
