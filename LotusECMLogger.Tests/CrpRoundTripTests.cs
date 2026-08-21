namespace LotusECMLogger.Tests
{
    /// <summary>
    /// Round-trips real CRP containers through <see cref="CrpCreator"/> and
    /// <see cref="CrpUnpacker"/>. The CRP payload is XTEA-encrypted and carries a random
    /// per-run salt, so two packs of the same input never produce the same file bytes —
    /// these tests therefore assert on what the unpacker recovers, which is what an ECU
    /// would actually consume.
    /// </summary>
    public sealed class CrpRoundTripTests : IDisposable
    {
        private readonly string workDir;

        public CrpRoundTripTests()
        {
            workDir = Path.Combine(Path.GetTempPath(), "CrpRoundTripTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a passing test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Deterministic pseudo-random payload. The last byte is forced to a non-0xFF value
        /// because both packers strip trailing 0xFF padding (crp08.py's import_bin).
        /// </summary>
        private static byte[] SamplePayload(int length = 4096, int seed = 1234)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            data[^1] = 0x5A;
            return data;
        }

        private string WriteInput(string name, byte[] contents)
        {
            string path = Path.Combine(workDir, name);
            File.WriteAllBytes(path, contents);
            return path;
        }

        private string OutputPath(string name) => Path.Combine(workDir, name);

        /// <summary>Asserts the CAN configuration header a non-TCU T6 chunk must carry.</summary>
        private static void AssertT6CanHeader(CrpUnpacker.CrpChunk chunk)
        {
            Assert.Equal((byte)10, chunk.EfiLocalId);
            Assert.Equal((byte)1, chunk.EfiRemoteId);
            Assert.Equal(500u, chunk.CanBitrate);
            Assert.Equal(0x50u, chunk.CanRemoteId1);
            Assert.Equal(0x7A0u, chunk.CanLocalId1);
            Assert.Equal(0x51u, chunk.CanRemoteId2);
            Assert.Equal(0x7A1u, chunk.CanLocalId2);
        }

        // -- CrpCreator -> CrpUnpacker --------------------------------------------------

        [Fact]
        public void Create_T6Calibration_RoundTripsThroughUnpacker()
        {
            byte[] payload = SamplePayload();
            string calPath = WriteInput("calibration.cpt", payload);
            string crpPath = OutputPath("calibration.crp");

            Assert.True(CrpCreator.Create(crpPath, CrpCreator.EcuType.T6, calPath));

            CrpUnpacker.CrpContents contents = CrpUnpacker.Unpack(crpPath);

            // ChunkCount counts the TOC chunk; Chunks holds only the ECU data chunks.
            Assert.Equal(2, contents.ChunkCount);
            CrpUnpacker.CrpChunk chunk = Assert.Single(contents.Chunks);

            Assert.Equal("calibration.cpt", chunk.Name);
            Assert.Equal("LOTUS_T6", chunk.Description);
            Assert.Equal("ECU T6", chunk.EcuId);
            Assert.Equal(0x5u, chunk.EcuAddress);          // T6 calrom reference address
            Assert.Equal(0x020000u, chunk.RealAddress);    // its real flash address
            Assert.Equal(0u, chunk.MaxVersion);
            Assert.Equal(0u, chunk.MinVersion);
            AssertT6CanHeader(chunk);
            Assert.Equal(payload, chunk.Data);
        }

        [Fact]
        public void Create_CalibrationAndProgram_ProducesBothChunksInOrder()
        {
            byte[] cal = SamplePayload(2048, seed: 1);
            byte[] prog = SamplePayload(8192, seed: 2);
            string calPath = WriteInput("tune.cpt", cal);
            string progPath = WriteInput("firmware.bin", prog);
            string crpPath = OutputPath("both.crp");

            Assert.True(CrpCreator.Create(crpPath, CrpCreator.EcuType.T6, calPath, progPath));

            CrpUnpacker.CrpContents contents = CrpUnpacker.Unpack(crpPath);

            Assert.Equal(3, contents.ChunkCount); // TOC + calibration + firmware
            Assert.Equal(2, contents.Chunks.Count);

            // Calibration first, then firmware, matching crp08.py's "both" ordering.
            Assert.Equal("tune.cpt", contents.Chunks[0].Name);
            Assert.Equal(0x5u, contents.Chunks[0].EcuAddress);
            Assert.Equal(cal, contents.Chunks[0].Data);

            Assert.Equal("firmware.bin", contents.Chunks[1].Name);
            Assert.Equal(0x4u, contents.Chunks[1].EcuAddress); // prog reference address
            Assert.Equal(prog, contents.Chunks[1].Data);
        }

        [Fact]
        public void Create_StripsTrailingFfPadding()
        {
            byte[] meaningful = [0x01, 0x02, 0x03, 0x04];
            string calPath = WriteInput("padded.cpt", [.. meaningful, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
            string crpPath = OutputPath("padded.crp");

            Assert.True(CrpCreator.Create(crpPath, CrpCreator.EcuType.T6, calPath));

            CrpUnpacker.CrpChunk chunk = Assert.Single(CrpUnpacker.Unpack(crpPath).Chunks);
            Assert.Equal(meaningful, chunk.Data);
        }

        [Fact]
        public void Create_PreservesNonAsciiFileNameInToc()
        {
            // CRP text fields are ISO-8859-15, so an accented file name must survive the
            // round trip rather than being flattened to '?' by an ASCII encoder.
            // Written as an escape so the literal survives any source-file encoding.
            const string name = "r\u00E9glage.cpt";
            string calPath = WriteInput(name, SamplePayload(512));
            string crpPath = OutputPath("accented.crp");

            Assert.True(CrpCreator.Create(crpPath, CrpCreator.EcuType.T6, calPath));

            CrpUnpacker.CrpChunk chunk = Assert.Single(CrpUnpacker.Unpack(crpPath).Chunks);
            Assert.Equal(name, chunk.Name);
        }

        [Fact]
        public void Build_WithoutCalibrationOrProgram_Throws()
        {
            Assert.Throws<ArgumentException>(() => CrpCreator.Build(CrpCreator.EcuType.T6, null, null));
        }

        // -- Guard ----------------------------------------------------------------------

        [Fact]
        public void Unpack_RejectsCorruptedContainer()
        {
            // Keeps the round trips above honest: proves the unpacker would notice if the
            // bytes between pack and unpack changed, rather than passing vacuously.
            string calPath = WriteInput("guard.cpt", SamplePayload(512));
            string crpPath = OutputPath("guard.crp");
            Assert.True(CrpCreator.Create(crpPath, CrpCreator.EcuType.T6, calPath));

            byte[] crp = File.ReadAllBytes(crpPath);
            crp[crp.Length / 2] ^= 0xFF;
            File.WriteAllBytes(crpPath, crp);

            Assert.Throws<InvalidDataException>(() => CrpUnpacker.Unpack(crpPath));
        }
    }
}
