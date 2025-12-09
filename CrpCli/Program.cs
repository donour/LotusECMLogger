using LotusECMLogger;

namespace CrpCli
{
    internal class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("CPT to CRP Converter");
            Console.WriteLine("====================\n");

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: CrpCli <input.cpt> [output.crp]");
                Console.WriteLine();
                Console.WriteLine("Arguments:");
                Console.WriteLine("  input.cpt   - Path to the input CPT file to convert");
                Console.WriteLine("  output.crp  - (Optional) Path for the output CRP file");
                Console.WriteLine("                If not specified, uses input filename with .crp extension");
                return 1;
            }

            string cptFilePath = args[0];
            string crpFilePath;

            if (args.Length >= 2)
            {
                crpFilePath = args[1];
            }
            else
            {
                // Auto-generate output filename by replacing extension
                crpFilePath = Path.ChangeExtension(cptFilePath, ".crp");
            }

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
                else
                {
                    Console.WriteLine("Conversion failed.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}
