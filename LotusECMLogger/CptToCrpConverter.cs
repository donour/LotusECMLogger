using System.Security.Cryptography;
using System.Text;

namespace LotusECMLogger
{
    /// <summary>
    /// Converts .CPT calibration files to .CRP format for use with ECU flashing tools
    /// Implements T6 calrom packing with XTEA encryption based on CRP08 format
    /// </summary>
    public class CptToCrpConverter
    {
        // T6 XTEA encryption key
        private static readonly uint[] T6_KEY = { 0x340D2EB9, 0xC41A93EE, 0x73FAFED5, 0x47C80F57 };

        private const string CHARSET = "ISO-8859-15";
        private const int ENTRY_NAME_SIZE = 128;
        private const uint XTEA_DELTA = 0x9E3779B9;
        private const int XTEA_ROUNDS = 32;

        /// <summary>
        /// Converts a .CPT file to .CRP format
        /// </summary>
        /// <param name="cptFilePath">Path to the source .CPT file</param>
        /// <param name="crpFilePath">Path where the .CRP file will be saved</param>
        /// <returns>True if conversion succeeded, false otherwise</returns>
        public static bool Convert(string cptFilePath, string crpFilePath)
        {
            try
            {
                if (!File.Exists(cptFilePath))
                {
                    throw new FileNotFoundException($"CPT file not found: {cptFilePath}");
                }

                byte[] cptData = File.ReadAllBytes(cptFilePath);
                string fileName = Path.GetFileName(cptFilePath);
                byte[] crpData = ConvertCptToCrp(cptData, fileName);
                File.WriteAllBytes(crpFilePath, crpData);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting CPT to CRP: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Core conversion logic from CPT to CRP format
        /// Implements T6 calrom packing with XTEA encryption
        /// </summary>
        /// <param name="cptData">Raw CPT file data</param>
        /// <param name="fileName">Name to use in CRP TOC</param>
        /// <returns>Converted CRP file data</returns>
        private static byte[] ConvertCptToCrp(byte[] cptData, string fileName)
        {
            // Strip trailing 0xFF bytes (like Python's import_bin)
            int endIndex = cptData.Length - 1;
            while (endIndex >= 0 && cptData[endIndex] == 0xFF)
            {
                endIndex--;
            }
            byte[] binData = new byte[endIndex + 1];
            Array.Copy(cptData, binData, endIndex + 1);

            // Create encrypted ECU data chunk
            byte[] encryptedChunk = CreateEncryptedEcuData(binData);

            // Create CAN chunk (64 byte header + encrypted data)
            byte[] canChunk = CreateCanChunk(encryptedChunk);

            // Create TOC chunk
            byte[] tocChunk = CreateTocChunk(fileName, "LOTUS_T6");

            // Assemble final CRP structure
            return AssembleCrp(tocChunk, canChunk);
        }

        /// <summary>
        /// Creates encrypted ECU data using XTEA CBC encryption
        /// </summary>
        private static byte[] CreateEncryptedEcuData(byte[] binData)
        {
            // Create ECU header (64 bytes)
            byte[] ecuHeader = new byte[64];

            // ECU ID: "ECU T6" padded to 32 bytes
            byte[] ecuId = Encoding.GetEncoding(CHARSET).GetBytes("ECU T6".PadRight(32));
            Array.Copy(ecuId, 0, ecuHeader, 0, 32);

            // ECU Address: 0x5 for T6 calrom (4 bytes big-endian)
            WriteInt32BE(ecuHeader, 32, 0x5);

            // Binary data size (4 bytes big-endian)
            WriteInt32BE(ecuHeader, 36, binData.Length);

            // Max version: 0 (4 bytes big-endian)
            WriteInt32BE(ecuHeader, 40, 0);

            // Min version: 0 (4 bytes big-endian)
            WriteInt32BE(ecuHeader, 44, 0);

            // Remaining 16 bytes are already zero

            // Calculate plain data size (64 byte ECU header + binary data)
            int plainSize = 64 + binData.Length;

            // Calculate padded size (must be multiple of 8 for XTEA)
            int paddedSize = plainSize;
            int align = paddedSize % 8;
            if (align > 0)
            {
                paddedSize += (8 - align);
            }

            // Create plain data buffer (8-byte salt + 4-byte size + plain data + padding)
            byte[] plainBuffer = new byte[12 + paddedSize];

            // XTEA salt (8 random bytes)
            byte[] salt = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            Array.Copy(salt, 0, plainBuffer, 0, 8);

            // Plain data size before padding (4 bytes big-endian)
            WriteInt32BE(plainBuffer, 8, plainSize);

            // ECU header
            Array.Copy(ecuHeader, 0, plainBuffer, 12, 64);

            // Binary data
            Array.Copy(binData, 0, plainBuffer, 12 + 64, binData.Length);

            // Padding is already zeros

            // Encrypt with XTEA CBC
            byte[] encrypted = new byte[12 + paddedSize];
            XteaEncryptCBC(plainBuffer, encrypted, T6_KEY);

            return encrypted;
        }

        /// <summary>
        /// Creates CAN chunk with configuration header and encrypted data
        /// </summary>
        private static byte[] CreateCanChunk(byte[] encryptedData)
        {
            byte[] chunk = new byte[64 + encryptedData.Length];

            // CAN configuration header (64 bytes)
            chunk[0] = 10;  // EFI Local ID
            chunk[1] = 1;   // Protocol type
            chunk[2] = 1;   // EFI Remote ID
            chunk[3] = 0;   // Frame delay

            // CAN Bitrate: 500 kbps (4 bytes little-endian)
            WriteInt32LE(chunk, 4, 500);

            // CAN Remote ID 1: 0x50 (4 bytes little-endian)
            WriteInt32LE(chunk, 8, 0x50);

            // CAN Local ID 1: 0x7A0 (4 bytes little-endian)
            WriteInt32LE(chunk, 12, 0x7A0);

            // CAN Remote ID 2: 0x51 (4 bytes little-endian)
            WriteInt32LE(chunk, 16, 0x51);

            // CAN Local ID 2: 0x7A1 (4 bytes little-endian)
            WriteInt32LE(chunk, 20, 0x7A1);

            // Remaining 40 bytes are already zeros

            // Encrypted data
            Array.Copy(encryptedData, 0, chunk, 64, encryptedData.Length);

            return chunk;
        }

        /// <summary>
        /// Creates Table of Contents chunk with file name and description
        /// </summary>
        private static byte[] CreateTocChunk(string fileName, string description)
        {
            // TOC structure:
            // 4 bytes: number of TOC entries (2)
            // 8 bytes: offset and size for file list
            // 8 bytes: offset and size for description list
            // File list: 4 bytes (index=1) + 128 bytes (filename)
            // Description list: 4 bytes (index=2) + 128 bytes (description)

            int tocSize = 4 + (8 * 2) + (4 + ENTRY_NAME_SIZE) + (4 + ENTRY_NAME_SIZE);
            byte[] toc = new byte[tocSize];

            // Number of TOC entries (always 2: file list and description list)
            WriteInt32LE(toc, 0, 2);

            // File list offset and size
            int fileListOffset = 4 + (8 * 2);
            int fileListSize = 4 + ENTRY_NAME_SIZE;
            WriteInt32LE(toc, 4, fileListOffset);
            WriteInt32LE(toc, 8, fileListSize);

            // Description list offset and size
            int descListOffset = fileListOffset + fileListSize;
            int descListSize = 4 + ENTRY_NAME_SIZE;
            WriteInt32LE(toc, 12, descListOffset);
            WriteInt32LE(toc, 16, descListSize);

            // File list: index (1) + filename
            WriteInt32LE(toc, fileListOffset, 1);
            byte[] fileNameBytes = Encoding.GetEncoding(CHARSET).GetBytes(fileName.PadRight(ENTRY_NAME_SIZE));
            Array.Copy(fileNameBytes, 0, toc, fileListOffset + 4, ENTRY_NAME_SIZE);

            // Description list: index (2) + description
            WriteInt32LE(toc, descListOffset, 2);
            byte[] descBytes = Encoding.GetEncoding(CHARSET).GetBytes(description.PadRight(ENTRY_NAME_SIZE));
            Array.Copy(descBytes, 0, toc, descListOffset + 4, ENTRY_NAME_SIZE);

            return toc;
        }

        /// <summary>
        /// Assembles final CRP file with header, chunks, and checksum
        /// </summary>
        private static byte[] AssembleCrp(byte[] tocChunk, byte[] canChunk)
        {
            // CRP structure:
            // 4 bytes: number of chunks (2)
            // 8 bytes per chunk: offset and size
            // Chunk data
            // 2 bytes: checksum

            int numChunks = 2;
            int headerSize = 4 + (8 * numChunks);
            int totalSize = headerSize + tocChunk.Length + canChunk.Length + 2;

            byte[] crp = new byte[totalSize];

            // Number of chunks
            WriteInt32LE(crp, 0, numChunks);

            // TOC chunk offset and size
            int tocOffset = headerSize;
            WriteInt32LE(crp, 4, tocOffset);
            WriteInt32LE(crp, 8, tocChunk.Length);

            // CAN chunk offset and size
            int canOffset = tocOffset + tocChunk.Length;
            WriteInt32LE(crp, 12, canOffset);
            WriteInt32LE(crp, 16, canChunk.Length);

            // Copy chunks
            Array.Copy(tocChunk, 0, crp, tocOffset, tocChunk.Length);
            Array.Copy(canChunk, 0, crp, canOffset, canChunk.Length);

            // Calculate checksum (sum of all bytes except last 2)
            int checksum = 0;
            for (int i = 0; i < totalSize - 2; i++)
            {
                checksum += crp[i];
            }
            checksum &= 0xFFFF;

            // Write checksum (2 bytes little-endian)
            crp[totalSize - 2] = (byte)(checksum & 0xFF);
            crp[totalSize - 1] = (byte)((checksum >> 8) & 0xFF);

            return crp;
        }

        /// <summary>
        /// XTEA encryption in CBC mode
        /// </summary>
        private static void XteaEncryptCBC(byte[] input, byte[] output, uint[] key)
        {
            // IV is [0, 0]
            uint lastV0 = 0;
            uint lastV1 = 0;

            for (int i = 0; i < input.Length; i += 8)
            {
                // Read 8 bytes as two 32-bit big-endian integers
                uint v0 = ReadUInt32BE(input, i);
                uint v1 = ReadUInt32BE(input, i + 4);

                // XOR with previous ciphertext (CBC mode)
                v0 ^= lastV0;
                v1 ^= lastV1;

                // XTEA encryption
                uint sum = 0;
                for (int r = 0; r < XTEA_ROUNDS; r++)
                {
                    v0 += (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (sum + key[sum & 3]);
                    sum += XTEA_DELTA;
                    v1 += (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (sum + key[(sum >> 11) & 3]);
                }

                // Write encrypted values as big-endian
                WriteUInt32BE(output, i, v0);
                WriteUInt32BE(output, i + 4, v1);

                // Save for next CBC iteration
                lastV0 = v0;
                lastV1 = v1;
            }
        }

        // Helper methods for byte order conversion

        private static void WriteInt32LE(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteInt32BE(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteUInt32BE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        private static uint ReadUInt32BE(byte[] buffer, int offset)
        {
            return ((uint)buffer[offset] << 24) |
                   ((uint)buffer[offset + 1] << 16) |
                   ((uint)buffer[offset + 2] << 8) |
                   buffer[offset + 3];
        }
    }
}
