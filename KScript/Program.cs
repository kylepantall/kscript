using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace KScript
{
    class Program
    {
        static void Main(string[] args)
        {
            //try
            //{
            if (args.Length > 0)
            {
                bool quiet = args.Any(i => i.ToLower() == "-s");
                bool clear_after_input = args.Any(i => i.ToLower().Equals("-cai"));

                if (!System.IO.File.Exists(args[0]))
                {
                    Console.WriteLine(string.Format("File does not exist - '{0}'...", args[0]));
                    throw new KScriptException("File does not exist.");
                }

                KScriptParser parser = new KScriptParser(args[0]);

                parser.Properties.Quiet = quiet;
                parser.Properties.ClearAfterInput = clear_after_input;

                parser.Parse();

                if (parser.Properties.WaitOnFinish)
                {
                    Console.WriteLine("\n\nPress any key to close...");
                    Console.ReadKey();
                }
            }
        }
    }
}
