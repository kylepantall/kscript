using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace KScript
{
    class Program
    {
        public static String[] Commands() => new string[] { "-s", "-cai", "-cmds" };

        public static List<string> GetUsedCommands(string[] cmds) => Commands().Where(i => cmds.Contains(i)).Select(i => i.ToLower()).ToList();

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                bool quiet = args.Any(i => i.ToLower() == "-s");
                bool clear_after_input = args.Any(i => i.ToLower().Equals("-cai"));

                String path = args[0];

                if (!File.Exists(args[0]))
                {
                    string[] files = Directory.GetFiles(Directory.GetCurrentDirectory());
                    string filePath = files.Select(u => Path.GetFileName(u)).FirstOrDefault().ToLower();

                    if (filePath != null && filePath == args[0].ToLower())
                        path = filePath;
                    else
                    {
                        if (GetUsedCommands(args).Contains("-cmds"))
                        {

                        }
                        else
                        {
                            Console.WriteLine(string.Format("File does not exist - '{0}'...", args[0]));
                            throw new KScriptException("File does not exist.");
                        }
                    }
                }
                else
                {
                    path = args[0];
                }

                KScriptParser parser = new KScriptParser(path);


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

        public static bool IsAnyOf(string value, params string[] vals)
        {
            bool isAny = false;
            vals.ToList().ForEach(i => isAny = (i.ToLower() == value));
            return isAny;
        }
    }
}
