using LotusECMLogger;

namespace CrpCli
{
    internal class Program
    {
        static int Main(string[] args)
        {
            // Subcommand dispatch. For backward compatibility, if the first
            // argument is a file (not a known command), default to "convert".
            if (args.Length >= 1 &&
                args[0].Equals("unpack", StringComparison.OrdinalIgnoreCase))
            {
                return RunUnpack(args.Skip(1).ToArray());
            }

            if (args.Length >= 1 &&
                args[0].Equals("convert", StringComparison.OrdinalIgnoreCase))
            {
                return RunConvert(args.Skip(1).ToArray());
            }

            if (args.Length >= 1 &&
                args[0].Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                return RunCreate(args.Skip(1).ToArray());
            }

            if (args.Length >= 1 && !IsCommand(args[0]))
            {
                // Legacy form: CrpCli <input.cpt> [output.crp]
                return RunConvert(args);
            }

            PrintUsage();
            return 1;
        }

        private static bool IsCommand(string arg)
        {
            return arg.Equals("unpack", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("convert", StringComparison.OrdinalIgnoreCase) ||
                   arg.Equals("create", StringComparison.OrdinalIgnoreCase) ||
                   arg is "-h" or "--help" or "/?";
        }

        private static void PrintUsage()
        {
            Console.WriteLine("CRP Tools");
            Console.WriteLine("=========\n");
            Console.WriteLine("Usage:");
            Console.WriteLine("  CrpCli convert <input.cpt> [output.crp]");
            Console.WriteLine("  CrpCli unpack  <input.crp> [output_dir]");
            Console.WriteLine("  CrpCli create  <output.crp> [--ecu <type>] [--cal <file>] [--prog <file>]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  convert   Pack a .CPT calibration into a T6 .CRP file");
            Console.WriteLine("  unpack    Decrypt a T6 .CRP file and show/extract its contents");
            Console.WriteLine("  create    Build a .CRP from a calibration and/or firmware file");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  For convert, if output.crp is omitted the input name with a");
            Console.WriteLine("  .crp extension is used.");
            Console.WriteLine("  For unpack, if output_dir is omitted contents are only printed;");
            Console.WriteLine("  provide a directory to also extract each chunk as a .bin file.");
            Console.WriteLine("  For create, at least one of --cal or --prog is required.");
            Console.WriteLine($"  ECU types: {string.Join(", ", EcuTypeNames())}  (default: T6)");
        }

        private static IEnumerable<string> EcuTypeNames() =>
            CrpCreator.AllVariants.Select(v => v.Type.ToString());

        private static int RunConvert(string[] args)
        {
            Console.WriteLine("CPT to CRP Converter");
            Console.WriteLine("====================\n");

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: CrpCli convert <input.cpt> [output.crp]");
                return 1;
            }

            string cptFilePath = args[0];
            string crpFilePath = args.Length >= 2
                ? args[1]
                : Path.ChangeExtension(cptFilePath, ".crp");

            Console.WriteLine($"Input:  {cptFilePath}");
            Console.WriteLine($"Output: {crpFilePath}");
            Console.WriteLine();

            if (!File.Exists(cptFilePath))
            {
                Console.WriteLine($"Error: Input file not found: {cptFilePath}");
                return 1;
            }

            try
            {
                Console.WriteLine("Converting...");
                bool success = CptToCrpConverter.Convert(cptFilePath, crpFilePath);

                if (success)
                {
                    Console.WriteLine("Conversion successful!");
                    Console.WriteLine($"Output file: {Path.GetFullPath(crpFilePath)}");
                    return 0;
                }

                Console.WriteLine("Conversion failed.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private static int RunUnpack(string[] args)
        {
            Console.WriteLine("CRP Unpacker (T6)");
            Console.WriteLine("=================\n");

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: CrpCli unpack <input.crp> [output_dir]");
                return 1;
            }

            string crpFilePath = args[0];
            string? outputDir = args.Length >= 2 ? args[1] : null;

            Console.WriteLine($"Input:  {crpFilePath}");
            if (outputDir != null)
            {
                Console.WriteLine($"Output: {outputDir}");
            }
            Console.WriteLine();

            if (!File.Exists(crpFilePath))
            {
                Console.WriteLine($"Error: Input file not found: {crpFilePath}");
                return 1;
            }

            try
            {
                CrpUnpacker.CrpContents contents = outputDir != null
                    ? CrpUnpacker.Extract(crpFilePath, outputDir)
                    : CrpUnpacker.Unpack(crpFilePath);

                PrintContents(contents, outputDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        private static void PrintContents(CrpUnpacker.CrpContents contents, string? outputDir)
        {
            Console.WriteLine($"Chunks: {contents.ChunkCount} (1 TOC + {contents.Chunks.Count} data)");

            int index = 1;
            foreach (CrpUnpacker.CrpChunk chunk in contents.Chunks)
            {
                Console.WriteLine();
                Console.WriteLine($"[Chunk {index}] {chunk.Name}");
                Console.WriteLine($"  Description : {chunk.Description}");
                Console.WriteLine($"  ECU Id      : {chunk.EcuId}");

                string addr = $"0x{chunk.EcuAddress:X}";
                if (chunk.RealAddress is uint real)
                {
                    addr += $" (real 0x{real:X6})";
                }
                Console.WriteLine($"  Address     : {addr}");
                Console.WriteLine($"  Data size   : {chunk.Data.Length} bytes (0x{chunk.Data.Length:X})");
                Console.WriteLine($"  Version     : min {chunk.MinVersion}, max {chunk.MaxVersion}");
                Console.WriteLine($"  CAN bitrate : {chunk.CanBitrate} kbit/s");
                Console.WriteLine(
                    $"  CAN IDs     : remote 0x{chunk.CanRemoteId1:X}/0x{chunk.CanRemoteId2:X}, " +
                    $"local 0x{chunk.CanLocalId1:X}/0x{chunk.CanLocalId2:X}");
                Console.WriteLine($"  XTEA salt   : {BitConverter.ToString(chunk.XteaSalt).Replace('-', ' ')}");

                if (outputDir != null)
                {
                    string name = string.IsNullOrWhiteSpace(chunk.Name)
                        ? "chunk.bin"
                        : Path.GetFileName(chunk.Name);
                    Console.WriteLine($"  Extracted   : {Path.Combine(outputDir, name)}");
                }

                index++;
            }
        }

        private static int RunCreate(string[] args)
        {
            Console.WriteLine("CRP Creator");
            Console.WriteLine("===========\n");

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: CrpCli create <output.crp> [--ecu <type>] [--cal <file>] [--prog <file>]");
                Console.WriteLine($"ECU types: {string.Join(", ", EcuTypeNames())}  (default: T6)");
                return 1;
            }

            string crpFilePath = args[0];
            var ecuType = CrpCreator.EcuType.T6;
            string? calFilePath = null;
            string? progFilePath = null;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--ecu" when i + 1 < args.Length:
                        string ecuName = args[++i];
                        if (!Enum.TryParse(ecuName, ignoreCase: true, out ecuType))
                        {
                            Console.WriteLine($"Error: Unknown ECU type '{ecuName}'.");
                            Console.WriteLine($"Valid types: {string.Join(", ", EcuTypeNames())}");
                            return 1;
                        }
                        break;
                    case "--cal" when i + 1 < args.Length:
                        calFilePath = args[++i];
                        break;
                    case "--prog" when i + 1 < args.Length:
                        progFilePath = args[++i];
                        break;
                    default:
                        Console.WriteLine($"Error: Unexpected argument '{args[i]}'.");
                        return 1;
                }
            }

            if (calFilePath == null && progFilePath == null)
            {
                Console.WriteLine("Error: At least one of --cal or --prog is required.");
                return 1;
            }

            CrpCreator.CrpVariant variant = CrpCreator.GetVariant(ecuType);
            Console.WriteLine($"ECU type : {variant.DisplayName} ({variant.Description})");
            if (calFilePath != null)
            {
                Console.WriteLine($"Cal      : {calFilePath} -> address 0x{variant.CalAddress:X}");
            }
            if (progFilePath != null)
            {
                Console.WriteLine($"Prog     : {progFilePath} -> address 0x{variant.ProgAddress:X}");
            }
            Console.WriteLine($"Output   : {crpFilePath}");
            Console.WriteLine();

            try
            {
                bool success = CrpCreator.Create(crpFilePath, ecuType, calFilePath, progFilePath);
                if (success)
                {
                    Console.WriteLine("CRP created successfully!");
                    Console.WriteLine($"Output file: {Path.GetFullPath(crpFilePath)}");
                    return 0;
                }

                Console.WriteLine("CRP creation failed.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}
