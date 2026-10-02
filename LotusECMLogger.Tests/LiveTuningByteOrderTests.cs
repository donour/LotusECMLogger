using System.Buffers.Binary;
using LotusECMLogger.Services;

namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Guards the byte order of live tuning writes end to end: a word edited in the calibration
    /// file must land in ECU RAM with the same bytes in the same order. The file is a byte-for-byte
    /// image of RAM (the RMA read copies memory out one byte at a time), so anything else corrupts
    /// the calibration.
    /// </summary>
    public sealed class LiveTuningByteOrderTests
    {
        private const uint RamBase = 0x40000000;

        /// <summary>
        /// Models the firmware's RMA 32-bit write (CAN ID 0x54) as disassembled from B132E0091
        /// (0xAA2E0-0xAA324; C132E0278 and E132E0288 are identical): with DLC 8 and an address in
        /// 0x40000000-0x4000FFFC, it loads CAN data bytes 0-3 as the address and bytes 4-7 as the
        /// value with lwz, then stores the value with stw. A word load and store copy bytes without
        /// reordering, and the address check only passes if data byte 0 is the most significant —
        /// so data byte 4 lands at the address and byte 7 at address + 3.
        /// </summary>
        private static void EcuHandleWordWrite(byte[] frame, byte[] ram)
        {
            Assert.Equal([0x00, 0x00, 0x00, 0x54], frame[..4]); // CAN ID 0x54
            byte[] data = frame[4..];
            Assert.Equal(8, data.Length);                       // the handler ignores any other DLC

            uint address = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
            Assert.InRange(address, 0x40000000u, 0x4000FFFCu);

            data.AsSpan(4, 4).CopyTo(ram.AsSpan((int)(address - RamBase), 4));
        }

        [Fact]
        public void WordWriteFrame_HasCanIdAddressAndValueMostSignificantByteFirst()
        {
            byte[] frame = T6LiveTuningService.BuildWordWriteFrame(0x40008654, 0x12345678);

            Assert.Equal(
                [0x00, 0x00, 0x00, 0x54, 0x40, 0x00, 0x86, 0x54, 0x12, 0x34, 0x56, 0x78],
                frame);
        }

        [Fact]
        public void GetWord_ReadsBigEndian()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, [0x12, 0x34, 0x56, 0x78, 0xA1, 0xB2, 0xC3, 0xD4]);
                using var monitor = new BinaryFileMonitor.BinaryFileMonitor(path, 10);
                monitor.Start();

                Assert.Equal(0x12345678u, monitor.GetWord(0));
                Assert.Equal(0xA1B2C3D4u, monitor.GetWord(4));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task EditedWord_LandsInEcuRamWithTheFilesByteOrder()
        {
            const uint baseAddress = 0x40008654; // a real calibration preset base
            byte[] edit = [0x12, 0x34, 0x56, 0x78];   // no symmetry, so any reordering shows
            const int editOffset = 8;

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[16]);

                var changed = new TaskCompletionSource<BinaryFileMonitor.WordChangedEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using var monitor = new BinaryFileMonitor.BinaryFileMonitor(path, 10);
                monitor.WordChanged += (_, e) => changed.TrySetResult(e);
                monitor.Start();

                // Edit in place: the file keeps its size, as when an editor saves a modified cell.
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Seek(editOffset, SeekOrigin.Begin);
                    stream.Write(edit);
                }

                // Throws TimeoutException if the monitor never reports the edit.
                var change = await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(editOffset, change.ByteOffset);
                Assert.Equal(0x12345678u, change.NewValue);

                // The same mapping T6LiveTuningService.OnWordChanged applies, then the ECU's handling.
                byte[] ram = new byte[0x10000];
                uint address = baseAddress + (uint)change.ByteOffset;
                EcuHandleWordWrite(T6LiveTuningService.BuildWordWriteFrame(address, change.NewValue), ram);

                Assert.Equal(edit, ram.AsSpan((int)(address - RamBase), 4).ToArray());
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
