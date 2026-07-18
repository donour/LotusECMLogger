using System.Text;

namespace LotusECMLogger
{
    /// <summary>
    /// Unpacks a T6 .CRP flash container into its constituent chunks so its
    /// contents can be inspected. This is the inverse of <see cref="CptToCrpConverter"/>
    /// and implements the CRP08 format (chunk table, TOC, CAN config header and
    /// XTEA-CBC encrypted ECU data) for Lotus T6 ECUs.
    /// </summary>
    public class CrpUnpacker
    {
        // T6 XTEA decryption key (matches CptToCrpConverter)
        private static readonly uint[] T6_KEY = { 0x340D2EB9, 0xC41A93EE, 0x73FAFED5, 0x47C80F57 };

        private const int ENTRY_NAME_SIZE = 128;
        private const uint XTEA_DELTA = 0x9E3779B9;
        private const int XTEA_ROUNDS = 32;

        // ISO-8859-15 is used for CRP text fields; Latin1 is a close, built-in match.
        private static readonly Encoding TextEncoding = Encoding.Latin1;

        // Maps T6 reference addresses to their real flash addresses (informational).
        private static readonly Dictionary<uint, uint> T6RealAddress = new()
        {
            { 0x4, 0x040000 }, // prog
            { 0x5, 0x020000 }, // calrom
            { 0x6, 0x030000 }, // optional calrom
        };

        /// <summary>
        /// A single ECU data chunk extracted from a CRP file.
        /// </summary>
        public class CrpChunk
        {
            /// <summary>File name from the CRP table of contents.</summary>
            public string Name { get; set; } = string.Empty;
            /// <summary>Description from the CRP table of contents (e.g. LOTUS_T6).</summary>
            public string Description { get; set; } = string.Empty;

            // CAN configuration header
            public byte EfiLocalId { get; set; }
            public byte EfiRemoteId { get; set; }
            public uint CanBitrate { get; set; }
            public uint CanRemoteId1 { get; set; }
            public uint CanLocalId1 { get; set; }
            public uint CanRemoteId2 { get; set; }
            public uint CanLocalId2 { get; set; }

            // Decrypted ECU data header
            public string EcuId { get; set; } = string.Empty;
            /// <summary>Reference address (T6 uses reference numbers, not real addresses).</summary>
            public uint EcuAddress { get; set; }
            public uint MaxVersion { get; set; }
            public uint MinVersion { get; set; }
            public byte[] XteaSalt { get; set; } = Array.Empty<byte>();

            /// <summary>The decrypted, plain firmware/calibration payload.</summary>
            public byte[] Data { get; set; } = Array.Empty<byte>();

            /// <summary>Real flash address for known T6 reference addresses, if known.</summary>
            public uint? RealAddress =>
                T6RealAddress.TryGetValue(EcuAddress, out uint addr) ? addr : null;
        }

        /// <summary>
        /// Parsed contents of a CRP file.
        /// </summary>
        public class CrpContents
        {
            public int ChunkCount { get; set; }
            public List<CrpChunk> Chunks { get; set; } = new();
        }

        /// <summary>
        /// Reads and decrypts a T6 CRP file into its parsed contents.
        /// </summary>
        /// <param name="crpFilePath">Path to the source .CRP file.</param>
        /// <returns>The parsed CRP contents.</returns>
        public static CrpContents Unpack(string crpFilePath)
        {
            if (!File.Exists(crpFilePath))
            {
                throw new FileNotFoundException($"CRP file not found: {crpFilePath}");
            }

            byte[] data = File.ReadAllBytes(crpFilePath);
            return Parse(data);
        }

        /// <summary>
        /// Unpacks a CRP file and writes each chunk's payload to a .bin file in
        /// <paramref name="outputDir"/>, using the chunk name from the TOC.
        /// </summary>
        /// <param name="crpFilePath">Path to the source .CRP file.</param>
        /// <param name="outputDir">Directory to write extracted .bin files to.</param>
        /// <returns>The parsed CRP contents.</returns>
        public static CrpContents Extract(string crpFilePath, string outputDir)
        {
            CrpContents contents = Unpack(crpFilePath);
            Directory.CreateDirectory(outputDir);

            foreach (CrpChunk chunk in contents.Chunks)
            {
                string name = string.IsNullOrWhiteSpace(chunk.Name) ? "chunk.bin" : chunk.Name;
                // Guard against path traversal from untrusted TOC names.
                name = Path.GetFileName(name);
                string outPath = Path.Combine(outputDir, name);
                File.WriteAllBytes(outPath, chunk.Data);
            }

            return contents;
        }

        /// <summary>
        /// Parses raw CRP file bytes into structured contents.
        /// </summary>
        private static byte[] SliceRange(byte[] data, int offset, int size)
        {
            if (offset < 0 || size < 0 || offset + size > data.Length)
            {
                throw new InvalidDataException(
                    $"Chunk range [{offset}, {offset + size}) is outside the file bounds ({data.Length} bytes).");
            }
            byte[] slice = new byte[size];
            Array.Copy(data, offset, slice, 0, size);
            return slice;
        }

        private static CrpContents Parse(byte[] data)
        {
            if (data.Length < 6)
            {
                throw new InvalidDataException("File is too small to be a valid CRP.");
            }

            // Verify the trailing 16-bit checksum (sum of all bytes except last 2).
            int checksum = 0;
            for (int i = 0; i < data.Length - 2; i++)
            {
                checksum += data[i];
            }
            checksum &= 0xFFFF;
            int storedChecksum = ReadUInt16LE(data, data.Length - 2);
            if (checksum != storedChecksum)
            {
                throw new InvalidDataException(
                    $"Checksum mismatch: computed 0x{checksum:X4}, stored 0x{storedChecksum:X4}.");
            }

            var contents = new CrpContents();
            int numChunks = (int)ReadUInt32LE(data, 0);
            contents.ChunkCount = numChunks;
            if (numChunks < 1)
            {
                throw new InvalidDataException("CRP contains no chunks.");
            }

            // Read the chunk offset/size table.
            var offsets = new int[numChunks];
            var sizes = new int[numChunks];
            for (int i = 0; i < numChunks; i++)
            {
                int x = 4 + (8 * i);
                offsets[i] = (int)ReadUInt32LE(data, x);
                sizes[i] = (int)ReadUInt32LE(data, x + 4);
            }

            // Chunk 0 is the TOC; the rest are CAN/ECU data chunks.
            byte[] tocData = SliceRange(data, offsets[0], sizes[0]);
            (List<string> names, List<string> descriptions) = ParseToc(tocData);

            for (int i = 1; i < numChunks; i++)
            {
                byte[] chunkData = SliceRange(data, offsets[i], sizes[i]);
                CrpChunk chunk = ParseCanChunk(chunkData);

                int tocIndex = i - 1;
                chunk.Name = tocIndex < names.Count ? names[tocIndex] : string.Empty;
                chunk.Description = tocIndex < descriptions.Count ? descriptions[tocIndex] : string.Empty;

                contents.Chunks.Add(chunk);
            }

            return contents;
        }

        /// <summary>
        /// Parses the table-of-contents chunk into name and description lists.
        /// </summary>
        private static (List<string> names, List<string> descriptions) ParseToc(byte[] data)
        {
            int numLists = (int)ReadUInt32LE(data, 0);
            var lists = new List<List<string>>();

            for (int i = 0; i < numLists; i++)
            {
                int x = 4 + (8 * i);
                int offset = (int)ReadUInt32LE(data, x);
                int size = (int)ReadUInt32LE(data, x + 4);

                // Each list begins with a 1-based index tag.
                int indexTag = (int)ReadUInt32LE(data, offset);
                if (indexTag != i + 1)
                {
                    throw new InvalidDataException(
                        $"TOC list {i} has unexpected index tag {indexTag}.");
                }

                int entryCount = (size - 4) / ENTRY_NAME_SIZE;
                var entries = new List<string>(entryCount);
                for (int j = 0; j < entryCount; j++)
                {
                    int entryOffset = offset + 4 + (ENTRY_NAME_SIZE * j);
                    string value = TextEncoding.GetString(data, entryOffset, ENTRY_NAME_SIZE).TrimEnd();
                    entries.Add(value);
                }
                lists.Add(entries);
            }

            List<string> names = lists.Count > 0 ? lists[0] : new List<string>();
            List<string> descriptions = lists.Count > 1 ? lists[1] : new List<string>();
            return (names, descriptions);
        }

        /// <summary>
        /// Parses a CAN chunk: a 64-byte CAN config header followed by
        /// XTEA-CBC encrypted ECU data.
        /// </summary>
        private static CrpChunk ParseCanChunk(byte[] data)
        {
            if (data.Length < 64)
            {
                throw new InvalidDataException("CAN chunk is smaller than its 64-byte header.");
            }

            var chunk = new CrpChunk
            {
                EfiLocalId = data[0],
                EfiRemoteId = data[2],
                CanBitrate = ReadUInt32LE(data, 4),
                CanRemoteId1 = ReadUInt32LE(data, 8),
                CanLocalId1 = ReadUInt32LE(data, 12),
                CanRemoteId2 = ReadUInt32LE(data, 16),
                CanLocalId2 = ReadUInt32LE(data, 20),
            };

            if (data[1] != 1)
            {
                throw new InvalidDataException($"Unexpected CAN protocol type {data[1]} (expected 1).");
            }
            if (data[3] != 0)
            {
                throw new InvalidDataException($"Unexpected CAN frame delay {data[3]} (expected 0).");
            }

            // Encrypted ECU data follows the 64-byte config header.
            byte[] encrypted = new byte[data.Length - 64];
            Array.Copy(data, 64, encrypted, 0, encrypted.Length);
            ParseEncryptedEcuData(encrypted, chunk);

            return chunk;
        }

        /// <summary>
        /// Decrypts XTEA-CBC ECU data and parses the plain ECU header + payload.
        /// </summary>
        private static void ParseEncryptedEcuData(byte[] encrypted, CrpChunk chunk)
        {
            if (encrypted.Length % 8 != 0 || encrypted.Length < 16)
            {
                throw new InvalidDataException("Encrypted ECU data is not a valid XTEA block length.");
            }

            byte[] plain = new byte[encrypted.Length];
            XteaDecryptCBC(encrypted, plain, T6_KEY);

            // Encryption header: 8-byte salt + 4-byte plain size (big-endian).
            chunk.XteaSalt = new byte[8];
            Array.Copy(plain, 0, chunk.XteaSalt, 0, 8);
            int plainSize = (int)ReadUInt32BE(plain, 8);

            if (12 + plainSize > plain.Length)
            {
                throw new InvalidDataException("Declared plain size exceeds decrypted buffer.");
            }

            // Plain ECU data begins after the 12-byte encryption header.
            int baseOffset = 12;

            chunk.EcuId = TextEncoding.GetString(plain, baseOffset, 32).TrimEnd();
            chunk.EcuAddress = ReadUInt32BE(plain, baseOffset + 32);
            int binSize = (int)ReadUInt32BE(plain, baseOffset + 36);
            chunk.MaxVersion = ReadUInt32BE(plain, baseOffset + 40);
            chunk.MinVersion = ReadUInt32BE(plain, baseOffset + 44);

            // ECU header is 64 bytes; payload follows.
            if (plainSize != binSize + 64)
            {
                throw new InvalidDataException(
                    $"ECU size mismatch: header declares {binSize} bytes but plain size is {plainSize}.");
            }

            int dataOffset = baseOffset + 64;
            if (dataOffset + binSize > plain.Length)
            {
                throw new InvalidDataException("ECU payload extends beyond decrypted buffer.");
            }

            chunk.Data = new byte[binSize];
            Array.Copy(plain, dataOffset, chunk.Data, 0, binSize);
        }

        /// <summary>
        /// XTEA decryption in CBC mode (inverse of CptToCrpConverter's XteaEncryptCBC).
        /// </summary>
        private static void XteaDecryptCBC(byte[] input, byte[] output, uint[] key)
        {
            // IV is [0, 0]
            uint lastV0 = 0;
            uint lastV1 = 0;

            for (int i = 0; i < input.Length; i += 8)
            {
                uint c0 = ReadUInt32BE(input, i);
                uint c1 = ReadUInt32BE(input, i + 4);

                // XTEA decryption
                uint v0 = c0;
                uint v1 = c1;
                uint sum = unchecked(XTEA_DELTA * XTEA_ROUNDS);
                for (int r = 0; r < XTEA_ROUNDS; r++)
                {
                    v1 -= (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (sum + key[(sum >> 11) & 3]);
                    sum -= XTEA_DELTA;
                    v0 -= (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (sum + key[sum & 3]);
                }

                // XOR with previous ciphertext block (CBC mode)
                v0 ^= lastV0;
                v1 ^= lastV1;

                WriteUInt32BE(output, i, v0);
                WriteUInt32BE(output, i + 4, v1);

                // Save ciphertext for the next CBC iteration
                lastV0 = c0;
                lastV1 = c1;
            }
        }

        // Helper methods for byte order conversion

        private static ushort ReadUInt16LE(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static uint ReadUInt32LE(byte[] buffer, int offset)
        {
            return buffer[offset] |
                   ((uint)buffer[offset + 1] << 8) |
                   ((uint)buffer[offset + 2] << 16) |
                   ((uint)buffer[offset + 3] << 24);
        }

        private static uint ReadUInt32BE(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }

        private static void WriteUInt32BE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }
    }
}
