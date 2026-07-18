using System.Security.Cryptography;
using System.Text;

namespace LotusECMLogger
{
    /// <summary>
    /// Creates a .CRP flash container from a calibration (calrom) file and/or a
    /// firmware (prog) file. This is the general, multi-chunk counterpart to
    /// <see cref="CptToCrpConverter"/> (which only packs a single T6 calrom) and the
    /// inverse of <see cref="CrpUnpacker"/>. It implements the CRP08 format and
    /// supports the ECU variants defined in the reference crp08.py.
    /// </summary>
    public class CrpCreator
    {
        private const int ENTRY_NAME_SIZE = 128;
        private const uint XTEA_DELTA = 0x9E3779B9;
        private const int XTEA_ROUNDS = 32;

        // ISO-8859-15 is used for CRP text fields; Latin1 is a close, built-in match.
        private static readonly Encoding TextEncoding = Encoding.Latin1;

        // XTEA keys (from crp08.py)
        private static readonly uint[] T4E_KEY = { 0x8FCB06DA, 0xAC193E62, 0x41500C5C, 0x64A7B1DB };
        private static readonly uint[] T6_KEY = { 0x340D2EB9, 0xC41A93EE, 0x73FAFED5, 0x47C80F57 };
        private static readonly uint[] T6_CATERHAM_KEY = { 0x340B2EB9, 0xC51A93EE, 0x73EAFED5, 0x47C80F53 };
        private static readonly uint[] T6_YARIS_KEY = { 0xAFA89C03, 0x520CC120, 0x3E20902A, 0x2338E27C };

        /// <summary>
        /// Selectable ECU type. Mirrors the variant list in crp08.py.
        /// </summary>
        public enum EcuType
        {
            T4e,
            T6,
            T6Caterham,
            CT1Caterham,
            Tcu,
            T6Yaris,
        }

        /// <summary>
        /// Per-ECU packing parameters: encryption key, ECU id string, the calrom and
        /// prog reference addresses, whether TCU CAN addressing is used, and the CRP
        /// TOC description tag.
        /// </summary>
        public sealed class CrpVariant
        {
            public required EcuType Type { get; init; }
            public required string DisplayName { get; init; }
            public required uint[] Key { get; init; }
            public required string EcuId { get; init; }
            public required uint CalAddress { get; init; }
            public required uint ProgAddress { get; init; }
            public required bool IsTcu { get; init; }
            public required string Description { get; init; }
        }

        // Variant table, matching CRP08.variants: [name, key, ecu_id, cal_addr, prog_addr, is_tcu, description]
        private static readonly CrpVariant[] Variants =
        {
            new() { Type = EcuType.T4e,         DisplayName = "T4e",          Key = T4E_KEY,          EcuId = "T4E",             CalAddress = 0x10000, ProgAddress = 0x20000, IsTcu = false, Description = "LOTUS_T4E" },
            new() { Type = EcuType.T6,          DisplayName = "T6",           Key = T6_KEY,           EcuId = "ECU T6",          CalAddress = 0x5,     ProgAddress = 0x4,     IsTcu = false, Description = "LOTUS_T6" },
            new() { Type = EcuType.T6Caterham,  DisplayName = "T6 Caterham",  Key = T6_CATERHAM_KEY,  EcuId = "CATERHAM T6",     CalAddress = 0x5,     ProgAddress = 0x4,     IsTcu = false, Description = "CATERHAM_ECU_DURATEC" },
            new() { Type = EcuType.CT1Caterham, DisplayName = "CT1 Caterham", Key = T6_CATERHAM_KEY,  EcuId = "CATERHAM CT1",    CalAddress = 0x5,     ProgAddress = 0x4,     IsTcu = false, Description = "CATERHAM_ECU_SIGMA" },
            new() { Type = EcuType.Tcu,         DisplayName = "TCU",          Key = T6_KEY,           EcuId = "TCU MMT/AT REVC", CalAddress = 0x5,     ProgAddress = 0x4,     IsTcu = true,  Description = "LOTUS_TCU" },
            new() { Type = EcuType.T6Yaris,     DisplayName = "T6 Yaris",     Key = T6_YARIS_KEY,     EcuId = "ECU T6B",         CalAddress = 0x5,     ProgAddress = 0x4,     IsTcu = false, Description = "LOTUS_YARIS" },
        };

        /// <summary>All supported ECU variants, in menu order.</summary>
        public static IReadOnlyList<CrpVariant> AllVariants => Variants;

        /// <summary>Looks up the packing parameters for an ECU type.</summary>
        public static CrpVariant GetVariant(EcuType type) =>
            Variants.First(v => v.Type == type);

        /// <summary>
        /// Creates a CRP file for the given ECU type from a calibration file and/or a
        /// firmware file. At least one of the two paths must be provided. When both are
        /// given, the calibration chunk is written first, followed by the firmware chunk.
        /// </summary>
        /// <param name="crpFilePath">Path where the .CRP file will be written.</param>
        /// <param name="ecuType">Target ECU type (default T6).</param>
        /// <param name="calFilePath">Path to the calibration (calrom/.CPT) file, or null.</param>
        /// <param name="progFilePath">Path to the firmware (prog/.BIN) file, or null.</param>
        /// <returns>True if the CRP was created successfully.</returns>
        public static bool Create(
            string crpFilePath,
            EcuType ecuType = EcuType.T6,
            string? calFilePath = null,
            string? progFilePath = null)
        {
            try
            {
                byte[] crpData = Build(ecuType, calFilePath, progFilePath);
                File.WriteAllBytes(crpFilePath, crpData);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating CRP: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Builds the raw CRP file bytes for the given ECU type and inputs.
        /// </summary>
        public static byte[] Build(
            EcuType ecuType,
            string? calFilePath,
            string? progFilePath)
        {
            if (calFilePath == null && progFilePath == null)
            {
                throw new ArgumentException("At least one of a calibration or firmware file must be provided.");
            }

            CrpVariant variant = GetVariant(ecuType);

            // Calibration first, then firmware, matching crp08.py's "both" ordering.
            var names = new List<string>();
            var canChunks = new List<byte[]>();

            if (calFilePath != null)
            {
                byte[] bin = LoadBinStripped(calFilePath);
                byte[] encrypted = CreateEncryptedEcuData(bin, variant, variant.CalAddress);
                canChunks.Add(CreateCanChunk(encrypted, variant));
                names.Add(Path.GetFileName(calFilePath));
            }

            if (progFilePath != null)
            {
                byte[] bin = LoadBinStripped(progFilePath);
                byte[] encrypted = CreateEncryptedEcuData(bin, variant, variant.ProgAddress);
                canChunks.Add(CreateCanChunk(encrypted, variant));
                names.Add(Path.GetFileName(progFilePath));
            }

            // Every chunk shares the variant's description tag.
            var descriptions = names.Select(_ => variant.Description).ToList();
            byte[] tocChunk = CreateTocChunk(names, descriptions);

            return AssembleCrp(tocChunk, canChunks);
        }

        /// <summary>
        /// Reads a binary file and strips trailing 0xFF padding (matching crp08's import_bin).
        /// </summary>
        private static byte[] LoadBinStripped(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Input file not found: {filePath}");
            }

            byte[] data = File.ReadAllBytes(filePath);
            int endIndex = data.Length - 1;
            while (endIndex >= 0 && data[endIndex] == 0xFF)
            {
                endIndex--;
            }
            byte[] stripped = new byte[endIndex + 1];
            Array.Copy(data, stripped, endIndex + 1);
            return stripped;
        }

        /// <summary>
        /// Creates XTEA-CBC encrypted ECU data (64-byte ECU header + payload, wrapped in
        /// the 12-byte encryption header and padded to an 8-byte boundary).
        /// </summary>
        private static byte[] CreateEncryptedEcuData(byte[] binData, CrpVariant variant, uint ecuAddress)
        {
            // ECU header (64 bytes)
            byte[] ecuHeader = new byte[64];

            byte[] ecuId = TextEncoding.GetBytes(variant.EcuId.PadRight(32));
            Array.Copy(ecuId, 0, ecuHeader, 0, 32);

            WriteInt32BE(ecuHeader, 32, (int)ecuAddress);   // reference address
            WriteInt32BE(ecuHeader, 36, binData.Length);    // payload size
            WriteInt32BE(ecuHeader, 40, 0);                 // max version
            WriteInt32BE(ecuHeader, 44, 0);                 // min version
            // Remaining 16 bytes stay zero.

            int plainSize = 64 + binData.Length;

            // Total = 8-byte salt + 4-byte size + plain data, padded to a multiple of 8.
            int totalSize = 12 + plainSize;
            int align = totalSize % 8;
            if (align > 0)
            {
                totalSize += (8 - align);
            }

            byte[] plainBuffer = new byte[totalSize];

            // XTEA salt (8 random bytes)
            byte[] salt = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            Array.Copy(salt, 0, plainBuffer, 0, 8);

            WriteInt32BE(plainBuffer, 8, plainSize);       // plain size before padding
            Array.Copy(ecuHeader, 0, plainBuffer, 12, 64);
            Array.Copy(binData, 0, plainBuffer, 12 + 64, binData.Length);
            // Padding stays zero.

            byte[] encrypted = new byte[totalSize];
            XteaEncryptCBC(plainBuffer, encrypted, variant.Key);
            return encrypted;
        }

        /// <summary>
        /// Creates a CAN chunk: a 64-byte config header followed by the encrypted ECU data.
        /// </summary>
        private static byte[] CreateCanChunk(byte[] encryptedData, CrpVariant variant)
        {
            byte[] chunk = new byte[64 + encryptedData.Length];

            // CAN config header defaults; overridden below for TCU addressing.
            byte efiLocalId = 10;
            byte efiRemoteId = 1;
            uint canRemoteId1 = 0x50;
            uint canLocalId1 = 0x7A0;
            uint canRemoteId2 = 0x51;
            uint canLocalId2 = 0x7A1;

            if (variant.IsTcu)
            {
                efiLocalId = 12;
                efiRemoteId = 2;
                canRemoteId1 = 0x60;
                canLocalId1 = 0x7B0;
                canRemoteId2 = 0x52;
                canLocalId2 = 0x7A2;
            }

            chunk[0] = efiLocalId;
            chunk[1] = 1;            // protocol type
            chunk[2] = efiRemoteId;
            chunk[3] = 0;            // frame delay

            WriteInt32LE(chunk, 4, 500);                 // CAN bitrate (kbit/s)
            WriteInt32LE(chunk, 8, (int)canRemoteId1);
            WriteInt32LE(chunk, 12, (int)canLocalId1);
            WriteInt32LE(chunk, 16, (int)canRemoteId2);
            WriteInt32LE(chunk, 20, (int)canLocalId2);
            // Remaining 40 header bytes stay zero.

            Array.Copy(encryptedData, 0, chunk, 64, encryptedData.Length);
            return chunk;
        }

        /// <summary>
        /// Creates the table-of-contents chunk holding the file-name and description lists.
        /// </summary>
        private static byte[] CreateTocChunk(List<string> names, List<string> descriptions)
        {
            // TOC layout:
            //   4 bytes LE  - number of lists (2)
            //   per list    - 4 bytes offset + 4 bytes size
            //   list 0      - 4 bytes index tag (1) + N*128 name entries
            //   list 1      - 4 bytes index tag (2) + N*128 description entries
            var lists = new List<string>[] { names.ToList(), descriptions.ToList() };

            int header = 4 + (8 * lists.Length);
            int list0Size = 4 + (names.Count * ENTRY_NAME_SIZE);
            int list1Size = 4 + (descriptions.Count * ENTRY_NAME_SIZE);
            int tocSize = header + list0Size + list1Size;

            byte[] toc = new byte[tocSize];

            WriteInt32LE(toc, 0, lists.Length);

            int offset = header;
            for (int i = 0; i < lists.Length; i++)
            {
                int listSize = 4 + (lists[i].Count * ENTRY_NAME_SIZE);
                int x = 4 + (8 * i);
                WriteInt32LE(toc, x, offset);
                WriteInt32LE(toc, x + 4, listSize);

                WriteInt32LE(toc, offset, i + 1); // 1-based index tag
                for (int j = 0; j < lists[i].Count; j++)
                {
                    int entryOffset = offset + 4 + (ENTRY_NAME_SIZE * j);
                    WritePaddedString(toc, entryOffset, lists[i][j], ENTRY_NAME_SIZE);
                }

                offset += listSize;
            }

            return toc;
        }

        /// <summary>
        /// Assembles the final CRP file: chunk table, TOC chunk, CAN chunks, and checksum.
        /// </summary>
        private static byte[] AssembleCrp(byte[] tocChunk, List<byte[]> canChunks)
        {
            var chunks = new List<byte[]> { tocChunk };
            chunks.AddRange(canChunks);

            int numChunks = chunks.Count;
            int headerSize = 4 + (8 * numChunks);
            int totalSize = headerSize + chunks.Sum(c => c.Length) + 2;

            byte[] crp = new byte[totalSize];

            WriteInt32LE(crp, 0, numChunks);

            int offset = headerSize;
            for (int i = 0; i < numChunks; i++)
            {
                int x = 4 + (8 * i);
                WriteInt32LE(crp, x, offset);
                WriteInt32LE(crp, x + 4, chunks[i].Length);
                Array.Copy(chunks[i], 0, crp, offset, chunks[i].Length);
                offset += chunks[i].Length;
            }

            // 16-bit checksum: sum of all bytes except the last 2.
            int checksum = 0;
            for (int i = 0; i < totalSize - 2; i++)
            {
                checksum += crp[i];
            }
            checksum &= 0xFFFF;
            crp[totalSize - 2] = (byte)(checksum & 0xFF);
            crp[totalSize - 1] = (byte)((checksum >> 8) & 0xFF);

            return crp;
        }

        /// <summary>
        /// XTEA encryption in CBC mode (identical to CptToCrpConverter's implementation).
        /// </summary>
        private static void XteaEncryptCBC(byte[] input, byte[] output, uint[] key)
        {
            uint lastV0 = 0;
            uint lastV1 = 0;

            for (int i = 0; i < input.Length; i += 8)
            {
                uint v0 = ReadUInt32BE(input, i);
                uint v1 = ReadUInt32BE(input, i + 4);

                v0 ^= lastV0;
                v1 ^= lastV1;

                uint sum = 0;
                for (int r = 0; r < XTEA_ROUNDS; r++)
                {
                    v0 += (((v1 << 4) ^ (v1 >> 5)) + v1) ^ (sum + key[sum & 3]);
                    sum += XTEA_DELTA;
                    v1 += (((v0 << 4) ^ (v0 >> 5)) + v0) ^ (sum + key[(sum >> 11) & 3]);
                }

                WriteUInt32BE(output, i, v0);
                WriteUInt32BE(output, i + 4, v1);

                lastV0 = v0;
                lastV1 = v1;
            }
        }

        // Helper methods for byte order conversion and text fields

        private static void WritePaddedString(byte[] buffer, int offset, string value, int size)
        {
            string padded = value.Length > size ? value.Substring(0, size) : value.PadRight(size);
            byte[] bytes = TextEncoding.GetBytes(padded);
            Array.Copy(bytes, 0, buffer, offset, Math.Min(bytes.Length, size));
        }

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
