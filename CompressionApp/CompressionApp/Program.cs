using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressionApp
{
    class Program
    {
        // tests
        // compress C:\Users\irina\Desktop\CompressionApp\CompressionApp\data\Data.csv data
        // decompress  C:\Users\irina\Desktop\CompressionApp\CompressionApp\data\data_1.zip data.csv.txt

        static void Main(string[] args)
        {
            RuntimeParameters parameters = null;
            int result = 0;
            try
            {
                if (args.Length < 3)
                {
                    throw new Exception("Not enough parameters");
                }
                parameters = RuntimeParameters.ParseRuntimeParameters(args[0], args[1], args[2]);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred:" + ex.Message);
                Console.WriteLine($"Result:{result}");
                PrintProgramUsage();
                PreventFromClosing();
                return;
            }

            try
            {
                Compressor compressor = new Compressor(parameters.input, parameters.output);
                if (parameters.Command == RunCommand.Compress)
                {
                    result = compressor.Compress();
                }
                else
                {
                    result = compressor.Decompress();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error occurred:" + ex.Message);
                Console.WriteLine($"Result:{result}");
                PrintProgramUsage();
                PreventFromClosing();
                return;
            }
            Console.WriteLine($"Result:{result}");

            PreventFromClosing();
        }
        private static void PreventFromClosing()
        {
            Console.ReadKey();
        }
        private static void PrintProgramUsage()
        {
            Console.WriteLine("");
            Console.WriteLine("Valid program usage:");
            Console.WriteLine("CompressionApp.exe [command] [intput] [output]");
            Console.WriteLine("Valid command: compress, decompress");
            Console.WriteLine("Example: compress file.txt zipped_file");
        }
    }
}
