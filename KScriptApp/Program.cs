using KScript.KScriptTypes.KScriptExceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace KScript
{
    class Program
    {
        public static String[] Commands() => new string[] { "-s", "-cai", "-cmds", "-admin", "-colourful" };

        public static List<string> GetUsedCommands(string[] cmds) => Commands().Where(i => cmds.Contains(i)).Select(i => i.ToLower()).ToList();

        /// <summary>
        /// Method to adjust console appearances.
        /// </summary>
        static void SetAppearances()
        {
            Console.Title = "KScript";

            Console.BackgroundColor = ConsoleColor.DarkMagenta;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WindowHeight = Console.WindowHeight;
        }

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                bool hasColor = args.Any(i => i.ToLower() == "-colourful");

                if (hasColor)
                {
                    SetAppearances();
                }

                bool quiet = args.Any(i => i.ToLower() == "-s");
                bool clear_after_input = args.Any(i => i.ToLower() == "-cai");
                bool admin_priv = args.Any(i => i.ToLower() == "-admin");

                String path = args[0];

                if (admin_priv)
                {
                    RestartWithAdminPriviledges(args);
                }

                if (!File.Exists(args[0]))
                {
                    string[] files = Directory.GetFiles(Directory.GetCurrentDirectory());
                    string filePath = files.Select(u => Path.GetFileName(u)).FirstOrDefault().ToLower();
                    if (filePath != null && filePath == args[0].ToLower())
                    {
                        path = filePath;
                    }
                    else
                    {
                        Console.WriteLine(string.Format("File does not exist - '{0}'...", args[0]));
                        throw new KScriptException("File does not exist.");
                    }
                }
                else
                {
                    path = args[0];
                }

                KScriptParser parser = new KScriptParser(path);


                parser.Properties.Quiet = quiet;
                parser.Properties.ClearAfterInput = clear_after_input;

                parser.CustomArguments = args.Skip(1).ToArray();

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

        public static void RestartWithAdminPriviledges(string[] args)
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);

            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                ProcessStartInfo proc = new ProcessStartInfo();
                proc.UseShellExecute = true;
                proc.WorkingDirectory = Environment.CurrentDirectory;
                proc.FileName = System.Reflection.Assembly.GetExecutingAssembly().Location;
                proc.Verb = "runas";
                proc.Arguments = String.Join(" ", args);

                Console.WriteLine(proc.Arguments);

                try { Process.Start(proc); }
                catch { Console.WriteLine("Could not run with administrator priviledges"); }

                Environment.Exit(0);
            }
        }
    }
}
