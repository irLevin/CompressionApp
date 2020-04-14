using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompressionApp
{
    public class RuntimeParameters
    {
        public RunCommand Command;
        public string input;
        public string output;

        public static RuntimeParameters ParseRuntimeParameters(string command, string input, string output)
        {
            RuntimeParameters pr = new RuntimeParameters();
            if(command.Equals("Compress", StringComparison.InvariantCultureIgnoreCase))
            {
                pr.Command = RunCommand.Compress;
                pr.input = input.TrimStart('\\');
                pr.output = output.TrimStart('\\');
                // check if input exist
                var inputFullPath = !Utils.IsFullPath(pr.input) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, pr.input) : pr.input;
                if (!File.Exists(inputFullPath))
                {
                    throw new Exception($"File {inputFullPath} not found");
                }
            }
            else if(command.Equals("Decompress", StringComparison.InvariantCultureIgnoreCase))
            {
                pr.Command = RunCommand.Decompress;
                pr.input = input.TrimStart('\\');
                pr.output = output.TrimStart('\\');

                var outputFullPath = !Utils.IsFullPath(pr.output) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, pr.output) : pr.output;
                if (File.Exists(outputFullPath))
                {
                    throw new Exception($"File {outputFullPath} already exist");
                }
            }
            else
            {
                throw new Exception($"Command {command} not recognized");
            }
            return pr;
        }
    }
    public enum RunCommand
    {
        Compress,
        Decompress
    }
}
