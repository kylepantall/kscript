using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

using KScript.KScriptExceptions;

namespace KScript
{
    class Program
    {
        public static string[] Commands() => new string[] { "-s", "-cai", "-cmds", "-admin", "-colourful", "-root", "-all", "-guid", "-nowait", "-lang=<culture>" };

        /// <summary>
        /// Method to adjust console appearances.
        /// </summary>
        static void SetAppearances()
        {
            try
            {
                Console.BackgroundColor = ConsoleColor.DarkMagenta;
                Console.ForegroundColor = ConsoleColor.White;

                if (OperatingSystem.IsWindows())
                {
                    Console.Title = "KScript";
                }
            }
            catch (Exception)
            {
                // Console appearance is cosmetic - ignore terminals that do not support it.
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: kscript <script.xml> [options] [script arguments]");
            Console.WriteLine("Options: " + string.Join(" ", Commands()));
        }

        static bool HasFlag(string[] args, string flag) => args.Any(i => string.Equals(i, flag, StringComparison.OrdinalIgnoreCase));

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            if (HasFlag(args, "-colourful"))
            {
                SetAppearances();
            }

            bool quiet = HasFlag(args, "-s");
            bool clearAfterInput = HasFlag(args, "-cai");
            bool adminPriv = HasFlag(args, "-admin");
            bool generateGuid = HasFlag(args, "-guid");
            bool root = HasFlag(args, "-root");
            bool allCommands = HasFlag(args, "-all");
            bool noWait = HasFlag(args, "-nowait");

            string culture = "auto";
            string lang = args.FirstOrDefault(i => i.StartsWith("-lang", StringComparison.OrdinalIgnoreCase) && i.Contains('='));
            if (lang != null)
            {
                culture = lang.Split('=')[1];
            }

            if (generateGuid)
            {
                Console.WriteLine("GUID: " + Guid.NewGuid().ToString());
                return 0;
            }

            if (root)
            {
                OpenInShell(AppContext.BaseDirectory);
                return 0;
            }

            if (adminPriv)
            {
                RestartWithAdminPriviledges(args);
            }

            string path = ResolveScriptPath(args[0]);
            if (path == null)
            {
                Console.Error.WriteLine(string.Format("File does not exist - '{0}'...", args[0]));
                return 2;
            }

            KScriptParser parser = new KScriptParser(path);

            parser.Properties.Quiet = quiet;
            parser.Properties.ForceQuiet = quiet;
            parser.Properties.ClearAfterInput = clearAfterInput;
            parser.CustomArguments = args.Skip(1).ToArray();
            parser.Properties.Language = culture;
            parser.Properties.PrintInfo = allCommands;

            try
            {
                parser.Parse();
            }
            catch (KScriptException ex)
            {
                Console.Error.WriteLine($"[error ~{ex.GetExceptionType()}] {ex.Message}");
                return 3;
            }

            // Only wait for a key press when attached to an interactive terminal,
            // otherwise piping / CI usage would hang or throw.
            if (parser.Properties.WaitOnFinish && !noWait && !Console.IsInputRedirected)
            {
                if (!parser.Properties.Quiet)
                {
                    Console.WriteLine("Press any key to close...");
                }

                try { Console.ReadKey(true); } catch (InvalidOperationException) { }
            }

            return 0;
        }

        /// <summary>
        /// Resolves the script path given on the command line. Falls back to a case-insensitive
        /// lookup in the working directory, since Linux and macOS file systems are case-sensitive.
        /// </summary>
        static string ResolveScriptPath(string arg)
        {
            if (arg.StartsWith("-"))
            {
                return null;
            }

            if (File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }

            string match = Directory.GetFiles(Directory.GetCurrentDirectory())
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), arg, StringComparison.OrdinalIgnoreCase));

            return match;
        }

        /// <summary>
        /// Opens a file or directory using the platform's default handler.
        /// </summary>
        static void OpenInShell(string target)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", $"\"{target}\"");
                }
                else
                {
                    Process.Start("xdg-open", $"\"{target}\"");
                }
            }
            catch (Exception)
            {
                Console.WriteLine(target);
            }
        }

        public static void RestartWithAdminPriviledges(string[] args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("-admin is only supported on Windows. Re-run the command with sudo instead.");
                return;
            }

            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);

            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                ProcessStartInfo proc = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Environment.ProcessPath,
                    Verb = "runas",
                    Arguments = string.Join(" ", args.Where(a => !string.Equals(a, "-admin", StringComparison.OrdinalIgnoreCase)).Select(a => $"\"{a}\""))
                };

                try { Process.Start(proc); }
                catch { Console.WriteLine("Could not run with administrator priviledges"); }

                Environment.Exit(0);
            }
        }
    }
}
