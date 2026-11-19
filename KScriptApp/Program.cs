using System;
using System.IO;
using System.Linq;

using KScript.Projects;
using KScript.Runtime;

namespace KScript
{
    class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Usage: kscript <script.xml> [script arguments]                Run a script ($args holds the arguments).");
            Console.WriteLine("       kscript check [file-or-folder]... [--warnaserror]   Check scripts and pages without running them (no path: the current project).");
            Console.WriteLine("       kscript lsp                                       Start the language server (LSP over stdio).");
            Console.WriteLine("       kscript serve [site folder] [--port 8080] [--host localhost]");
            Console.WriteLine("       kscript update [--check] [--force] [--version <tag>]   Update to the latest release.");
            Console.WriteLine("       kscript ide [install|uninstall|config] [editor]       Set up VS Code, Cursor, VSCodium or another editor.");
            Console.WriteLine("       kscript --version | --help");
            Console.WriteLine("       kscript make project <folder> [--port 8080]       Start a new project with a kscript.json.");
            Console.WriteLine("       kscript make <type> <Name> [--key value]   e.g. kscript make component Hero (run kscript make for the list)");
        }

        /// <summary>
        /// kscript serve [folder] [--port N] [--host H]: serves KScript pages from a folder. In a project, the nearest
        /// kscript.json gives the defaults (serve.root, serve.port, serve.host); options on the command line win.
        /// </summary>
        static int Serve(string[] args)
        {
            string folder = null;
            int? port = null;
            string host = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                    case "-p":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out int p))
                        {
                            Console.Error.WriteLine("--port needs a number, e.g. --port 8080");
                            return 1;
                        }
                        port = p;
                        break;
                    case "--host":
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("--host needs a value, e.g. --host 0.0.0.0");
                            return 1;
                        }
                        host = args[++i];
                        break;
                    default:
                        folder = args[i];
                        break;
                }
            }

            KScript.Projects.ProjectConfig project;
            try
            {
                project = KScript.Projects.ProjectConfig.FindAndLoad(folder ?? ".");
            }
            catch (KScript.Projects.ProjectConfigException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            if (project != null)
            {
                foreach (string warning in project.Warnings) Console.Error.WriteLine($"{project.FilePath}: {warning}");
                // `kscript serve` (or `kscript serve <project folder>`) serves the project's site root. A folder
                // inside the project is served as given, with the project's port and host.
                string given = folder == null ? null : Path.GetFullPath(folder);
                if (given == null || string.Equals(given.TrimEnd(Path.DirectorySeparatorChar), project.Directory, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                    folder = project.SiteRoot;
                port ??= project.Serve.Port;
                host ??= project.Serve.Host;
            }

            try
            {
                return KScript.Pages.PageServer.RunAsync(folder ?? ".", port ?? 8080, host ?? "localhost", Console.Out).GetAwaiter().GetResult();
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Could not start the server: {ex.Message}");
                return 1;
            }
        }

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "lsp")
            {
                // stdout carries the protocol, so nothing else may write to it.
                return KScript.LanguageServer.KScriptLanguageServer.RunStdioAsync().GetAwaiter().GetResult();
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            KScript.Updates.SelfUpdater.CleanupPreviousBinary();

            if (args.Length > 0 && (args[0] == "--version" || args[0] == "version"))
            {
                Console.WriteLine(KScript.Updates.SelfUpdater.CurrentVersion);
                return 0;
            }

            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help" || args[0] == "-?" || args[0] == "/?"))
            {
                PrintUsage();
                return 0;
            }

            if (args.Length > 0 && args[0] == "update")
            {
                return KScript.Updates.SelfUpdater.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
            }

            if (args.Length > 0 && args[0] == "ide")
            {
                return KScript.Ide.IdeCommand.Run(args.Skip(1).ToArray(), Console.Out, Console.Error);
            }

            if (args.Length > 0 && args[0] == "check")
            {
                return KScript.LanguageServer.CheckCommand.Run(args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)), Console.Out,
                    warningsAsErrors: args.Contains("--warnaserror"));
            }

            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            if (args[0] == "serve")
            {
                return Serve(args.Skip(1).ToArray());
            }

            // kscript make component Hero, or Artisan-style kscript make:component Hero
            if (args[0] == "make" || args[0].StartsWith("make:", StringComparison.Ordinal))
            {
                var rest = args[0] == "make" ? args.Skip(1) : new[] { args[0].Substring(5) }.Concat(args.Skip(1));
                return KScript.Scaffolding.MakeCommand.Run(rest.ToArray(), Console.Out, Console.Error);
            }

            if (args[0].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown option '{args[0]}'.");
                Console.Error.WriteLine("Run kscript --help for usage.");
                return 1;
            }

            string path = ResolveScriptPath(args[0]);
            if (path == null)
            {
                Console.Error.WriteLine($"There is no script at '{args[0]}'.");
                return 2;
            }

            return RunScript(path, args.Skip(1).ToArray());
        }

        /// <summary>
        /// Runs a script. Packages listed in the nearest kscript.json are imported for it. Scripts run on a thread
        /// with a large stack, so deep recursion reports KScript's own call depth error instead of crashing.
        /// </summary>
        static int RunScript(string path, string[] scriptArgs)
        {
            var options = new InterpreterOptions();
            try
            {
                ProjectConfig project = ProjectConfig.FindAndLoad(Path.GetDirectoryName(path));
                if (project != null)
                {
                    foreach (string warning in project.Warnings) Console.Error.WriteLine($"{project.FilePath}: {warning}");
                    options.DefaultPackages.AddRange(project.Packages);
                }
            }
            catch (ProjectConfigException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            var host = KScriptHost.Console();
            host.Arguments = scriptArgs;

            using var cancel = new System.Threading.CancellationTokenSource();
            options.Cancellation = cancel.Token;
            Console.CancelKeyPress += (_, e) =>
            {
                // The first Ctrl+C stops the script cleanly (finally blocks run); a second one ends the process.
                if (cancel.IsCancellationRequested) return;
                e.Cancel = true;
                cancel.Cancel();
            };

            int exitCode = 0;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    exitCode = new Interpreter(host, options: options).RunFile(path);
                }
                catch (KScriptException ex)
                {
                    Console.Out.Flush();
                    Console.Error.WriteLine(ex.ToString());
                    exitCode = 3;
                }
                catch (OperationCanceledException)
                {
                    exitCode = 130;
                }
            }, 256 * 1024 * 1024);
            thread.Start();
            thread.Join();
            return exitCode;
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
    }
}
