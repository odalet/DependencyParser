using System;
using System.IO;
using System.Reflection;

using NDesk.Options;

namespace DependencyParser
{
    /// <summary>
    /// In charge of command-line processing.
    /// </summary>
    internal class CommandLineProcessor
    {
        private readonly string[] arguments;
        private readonly TextWriter outw;
        private readonly TextWriter errw;
        private readonly OptionSet options;

        private bool showHelp = false;
        private bool showVersion = false;
        private string assemblyFileName = string.Empty;
        private string outputFileName = "output.xml"; // this is the default.        

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandLineProcessor" /> class.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <param name="outWriter">The text writer into which help and messages shouyld be written.</param>
        public CommandLineProcessor(string[] args, TextWriter outWriter = null, TextWriter errWriter = null)
        {
            arguments = args ?? new string[0];
            outw = outWriter ?? Console.Out;
            errw = errWriter ?? Console.Error;
            options = new OptionSet() 
            {
                { "a|assembly=", "the name of the assembly to scan.", v => assemblyFileName = v },
                { "o|output=", string.Format("the path to the output xml report file (Default is {0}).", 
                    outputFileName), v => outputFileName = v },
                { "h|help", "print this message and exit.", v => showHelp = v != null },
                { "v|version", string.Format("print {0} version and exit.", Program.AppName), v => showVersion = v != null }
            };
        }

        public string AssemblyFileName
        {
            get { return assemblyFileName; }
        }

        public string OutputFileName
        {
            get { return outputFileName; }
        }

        public bool Execute()
        {
            try
            {
                options.Parse(arguments);
            }
            catch (OptionException oex)
            {
                errw.WriteError(oex.Message);
                errw.WriteLine("Try '{0} --help' for more information.", Program.AppName);
                return false;
            }

            if (showHelp && showVersion) showVersion = false; // Showing help is more important than showing the version
            if (showVersion)
            {
                ShowVersion();
                return false; // Stop here;
            }

            if (showHelp || string.IsNullOrEmpty(assemblyFileName))
            {
                ShowHelp();
                return false; // Stop there
            }

            return true; // Go on with analysis
        }

        private void ShowVersion()
        {
            outw.WriteLine("{0} Version {1}", Program.AppName, 
                Assembly.GetExecutingAssembly().GetName().Version);
        }

        private void ShowHelp()
        {
            outw.WriteLine("Usage: {0} -a=assembly [options]", Program.AppName);
            options.WriteOptionDescriptions(outw);
        }
    }
}
