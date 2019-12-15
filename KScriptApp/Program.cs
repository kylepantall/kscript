using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

using KScript.KScriptExceptions;

namespace KScript
{
    class Program
    {
        public static string[] Commands() => new string[] { "-s", "-cai", "-cmds", "-admin", "-colourful", "-root" };

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
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length > 0)
            {
                bool hasColor = args.Any(i => i.ToLower() == "-colourful");

                if (hasColor)
                {
                    SetAppearances();
                }

                string culture = "auto";

                bool quiet = args.Any(i => i.ToLower() == "-s");
                bool clear_after_input = args.Any(i => i.ToLower() == "-cai");
                bool admin_priv = args.Any(i => i.ToLower() == "-admin");
                bool generate_guid = args.Any(i => i.ToLower() == "-guid");
                bool root = args.Any(i => i.ToLower() == "-root");

                bool has_lang = args.Any(i => i.ToLower().StartsWith("-lang") && i.ToLower().Contains("="));

                string path = args[0];

                if (admin_priv)
                {
                    RestartWithAdminPriviledges(args);
                }


                if (root)
                {
                    string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                    UriBuilder uri = new UriBuilder(codeBase);
                    Process.Start(Path.GetDirectoryName(Uri.UnescapeDataString(uri.Path)));
                }


                if (has_lang)
                {
                    string lang = args.FirstOrDefault(i => i.ToLower().StartsWith("-lang") && i.ToLower().Contains("="));

                    if (lang != null)
                    {
                        culture = lang.Split('=')[1];
                    }
                }



                if (!generate_guid && !root)
                {
                    if (!args[0].ToString().StartsWith("-"))
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
                }

                if (generate_guid)
                {
                    Console.WriteLine("GUID: " + Guid.NewGuid().ToString());
                    return;
                }


                if (!root && !generate_guid)
                {
                    KScriptParser parser = new KScriptParser(path);


                    parser.Properties.Quiet = quiet;
                    parser.Properties.ClearAfterInput = clear_after_input;

                    parser.CustomArguments = args.Skip(1).ToArray();
                    parser.Properties.Language = culture;

                    parser.Parse();

                    if (parser.Properties.WaitOnFinish)
                    {
                        if (HasAnyChildProcesses())
                        {
                            if (!parser.Properties.Quiet)
                                Console.WriteLine("Awaiting for child processes to finish...");
                            Console.ReadKey();
                            return;
                        }

                        if (!parser.Properties.Quiet)
                            Console.WriteLine("Press any key to close...");
                        Console.ReadKey();
                    }
                }
            }
        }

        public static bool HasAnyChildProcesses()
        {
            try
            {
                return Process.GetCurrentProcess().Threads.Cast<ProcessThread>().Where(i => i.ThreadState == ThreadState.Running).Any();
            }
            catch (Exception)
            {
                return false;
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
                ProcessStartInfo proc = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = System.Reflection.Assembly.GetExecutingAssembly().Location,
                    Verb = "runas",
                    Arguments = string.Join(" ", args)
                };

                Console.WriteLine(proc.Arguments);

                try { Process.Start(proc); }
                catch { Console.WriteLine("Could not run with administrator priviledges"); }

                Environment.Exit(0);
            }
        }
    }
}
