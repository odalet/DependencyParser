using System;
using System.IO;

namespace DependencyParser
{
    internal static class Extensions
    {
        public static void WriteError(this TextWriter writer, string message, params string[] args)
        {
            ConsoleColor? old = null;
            if (writer == Console.Error)
            {
                old = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
            }

            var m = args == null || args.Length == 0 ? message : string.Format(message, args);
            writer.WriteLine("{0} Error: {1}", Program.AppName, m);

            if (old.HasValue)
                Console.ForegroundColor = old.Value;
        }        
    }
}
