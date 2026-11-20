using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Projects;
using KScript.Runtime;

namespace KScript
{
    class Program
    {
        static int Main(string[] args) => new CommandLine().Run(args);
    }

    /// <summary>The kscript command: runs scripts and dispatches check, fmt, lsp, test, css, serve, app, update, ide and make.</summary>
    internal sealed class CommandLine
    {
        public TextWriter Out { get; init; } = Console.Out;
        public TextWriter Error { get; init; } = Console.Error;

        /// <summary>Where kscript lsp reads and writes the protocol.</summary>
        public Func<Stream> StandardInput { get; init; } = Console.OpenStandardInput;
        public Func<Stream> StandardOutput { get; init; } = Console.OpenStandardOutput;

        /// <summary>The folder kscript runs from, where a packaged app keeps its manifest.</summary>
        public string AppFolder { get; init; } = AppContext.BaseDirectory;

        /// <summary>Stops kscript serve and running scripts, as Ctrl+C does.</summary>
        public System.Threading.CancellationToken Cancellation { get; init; }

        /// <summary>Shows why a packaged app's script failed, in a window.</summary>
        public Action<string, string> ShowError { get; init; } = KScript.UI.DesktopHost.ShowError;

        /// <summary>The first Ctrl+C stops the work cleanly (finally blocks run); a second one ends the process.</summary>
        internal static bool StopOnFirstCtrlC(System.Threading.CancellationTokenSource cancel)
        {
            if (cancel.IsCancellationRequested) return false;
            cancel.Cancel();
            return true;
        }

        void PrintUsage(TextWriter output)
        {
            output.WriteLine("Usage: kscript [--env name] <script.xml> [script arguments]   Run a script ($args holds the arguments; --env picks staging.yaml, .env.staging...).");
            output.WriteLine("       kscript check [file-or-folder]... [--warnaserror]   Check scripts and pages without running them (no path: the current project).");
            output.WriteLine("       kscript check --print-config [file-or-folder]      Show the lint and format settings that apply, and which standard set each.");
            output.WriteLine("       kscript fmt [file-or-folder]... [--check]          Tidy scripts and pages (--check: only report, for CI).");
            output.WriteLine("       kscript lsp                                       Start the language server (LSP over stdio).");
            output.WriteLine("       kscript test [file-or-folder]... [--filter text] [--timeout s] [--junit file]   Run *.test.kscript and *.test.ks.xml tests.");
            output.WriteLine("       kscript css [file-or-folder]... [--out folder] [--var name=value]   Compile .ks.css, .ks.scss and .ks.sass stylesheets.");
            output.WriteLine("       kscript convert <file-or-folder>... [--to xml|kscript] [--stdout]   Write the .kscript version of an XML script, or the reverse.");
            output.WriteLine("       kscript serve [site folder] [--port 8080] [--host localhost] [--env name]   Serve pages, and routes.ks.xml / api.ks.xml routes.");
            output.WriteLine("       kscript routes [site folder]                      List the routes of a site.");
            output.WriteLine("       kscript app build [script] [--name \"My App\"] [--out folder]   Package a script as a desktop app.");
            output.WriteLine("       kscript update [--check] [--force] [--version <tag>]   Update to the latest release.");
            output.WriteLine("       kscript ide [install|uninstall|config] [editor]       Set up VS Code, Cursor, VSCodium or another editor.");
            output.WriteLine("       kscript --version | --help");
            output.WriteLine("       kscript make project <folder> [--port 8080]       Start a new project with a kscript.json.");
            output.WriteLine("       kscript make <type> <Name> [--key value]   e.g. kscript make component Hero (run kscript make for the list)");
        }

        /// <summary>
        /// kscript serve [folder] [--port N] [--host H]: serves KScript pages from a folder. In a project, the nearest
        /// kscript.json gives the defaults (serve.root, serve.port, serve.host); options on the command line win.
        /// </summary>
        int Serve(string[] args)
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
                            Error.WriteLine("--port needs a number, e.g. --port 8080");
                            return 1;
                        }
                        port = p;
                        break;
                    case "--host":
                        if (i + 1 >= args.Length)
                        {
                            Error.WriteLine("--host needs a value, e.g. --host 0.0.0.0");
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
                Error.WriteLine(ex.Message);
                return 1;
            }

            if (project != null)
            {
                foreach (string warning in project.Warnings) Error.WriteLine($"{project.FilePath}: {warning}");
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
                return KScript.Http.HttpServer.RunAsync(folder ?? ".", project, port ?? 8080, host ?? "localhost", Out, Cancellation).GetAwaiter().GetResult();
            }
            catch (DirectoryNotFoundException ex)
            {
                Error.WriteLine(ex.Message);
                return 1;
            }
            catch (FileNotFoundException ex)
            {
                Error.WriteLine(ex.Message);
                return 1;
            }
            catch (IOException ex)
            {
                Error.WriteLine($"Could not start the server: {ex.Message}");
                return 1;
            }
        }

        /// <summary>The environment from --env, also set as KSCRIPT_ENV so pages and child scripts see it.</summary>
        string EnvironmentName;

        string[] TakeEnvironment(string[] args)
        {
            var rest = new List<string>();
            bool anywhere = args.Length > 0 && (args[0] == "serve" || args[0] == "test" || args[0] == "css");
            bool before = true;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if ((before || anywhere) && (a == "--env" || a.StartsWith("--env=", StringComparison.Ordinal)))
                {
                    string value = a == "--env" ? (i + 1 < args.Length ? args[++i] : null) : a.Substring(6);
                    if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("--env needs a name, e.g. --env staging");
                    EnvironmentName = value;
                    Environment.SetEnvironmentVariable("KSCRIPT_ENV", value);
                    continue;
                }
                if (!a.StartsWith("-", StringComparison.Ordinal)) before = false;
                rest.Add(a);
            }
            return rest.ToArray();
        }

        /// <summary>kscript routes [folder]: the route table of a site (the project's site root by default).</summary>
        int Routes(string[] args)
        {
            string folder = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            KScript.Projects.ProjectConfig project;
            try
            {
                project = KScript.Projects.ProjectConfig.FindAndLoad(folder ?? ".");
            }
            catch (KScript.Projects.ProjectConfigException ex)
            {
                Error.WriteLine(ex.Message);
                return 1;
            }
            string given = folder == null ? null : Path.GetFullPath(folder);
            if (project != null && (given == null || string.Equals(given.TrimEnd(Path.DirectorySeparatorChar), project.Directory, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)))
                folder = project.SiteRoot;
            if (!Directory.Exists(folder ?? "."))
            {
                Error.WriteLine($"Site folder not found: {folder}");
                return 1;
            }
            return KScript.Http.HttpServer.ListRoutes(folder ?? ".", project, Out, Error);
        }

        public int Run(string[] args)
        {
            // Inside an app built by `kscript app build`: run the app's script and nothing else.
            string appManifest = KScript.Apps.AppLauncher.FindManifest(AppFolder);
            if (appManifest != null)
            {
                return RunApp(appManifest, args);
            }

            if (args.Length > 0 && args[0] == "lsp")
            {
                // stdout carries the protocol, so nothing else may write to it.
                return KScript.LanguageServer.KScriptLanguageServer.RunStdioAsync(StandardInput(), StandardOutput()).GetAwaiter().GetResult();
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            KScript.Updates.SelfUpdater.CleanupPreviousBinary();

            // --env staging (before the script, or anywhere after serve and test) picks the environment for $config.
            try
            {
                args = TakeEnvironment(args);
            }
            catch (ArgumentException ex)
            {
                Error.WriteLine(ex.Message);
                return 1;
            }

            if (args.Length > 0 && (args[0] == "--version" || args[0] == "version"))
            {
                Out.WriteLine(KScript.Updates.SelfUpdater.CurrentVersion);
                return 0;
            }

            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help" || args[0] == "-?" || args[0] == "/?"))
            {
                PrintUsage(Out);
                return 0;
            }

            if (args.Length > 0 && args[0] == "update")
            {
                return KScript.Updates.SelfUpdater.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args.Length > 0 && args[0] == "ide")
            {
                return KScript.Ide.IdeCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args.Length > 0 && args[0] == "app")
            {
                return KScript.Apps.AppCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args.Length > 0 && args[0] == "check")
            {
                if (args.Contains("--print-config"))
                    return KScript.LanguageServer.CheckCommand.PrintConfig(args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)), Out);
                return KScript.LanguageServer.CheckCommand.Run(args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)), Out,
                    warningsAsErrors: args.Contains("--warnaserror"));
            }

            if (args.Length > 0 && args[0] == "fmt")
            {
                return KScript.LanguageServer.FormatCommand.Run(args.Skip(1), Out);
            }

            if (args.Length == 0)
            {
                PrintUsage(Out);
                return 1;
            }

            if (args[0] == "serve")
            {
                return Serve(args.Skip(1).ToArray());
            }

            if (args[0] == "css")
            {
                return KScript.Style.CssCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args[0] == "routes")
            {
                return Routes(args.Skip(1).ToArray());
            }

            if (args[0] == "convert")
            {
                return KScript.Conversion.ConvertCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args[0] == "test")
            {
                return RunTests(args.Skip(1).ToArray());
            }

            // kscript make component Hero, or Artisan-style kscript make:component Hero
            if (args[0] == "make" || args[0].StartsWith("make:", StringComparison.Ordinal))
            {
                var rest = args[0] == "make" ? args.Skip(1) : new[] { args[0].Substring(5) }.Concat(args.Skip(1));
                return KScript.Scaffolding.MakeCommand.Run(rest.ToArray(), Out, Error);
            }

            if (args[0].StartsWith("-", StringComparison.Ordinal))
            {
                Error.WriteLine($"Unknown option '{args[0]}'.");
                Error.WriteLine("Run kscript --help for usage.");
                return 1;
            }

            string path = ResolveScriptPath(args[0]);
            if (path == null)
            {
                Error.WriteLine($"There is no script at '{args[0]}'.");
                return 2;
            }

            // kscript greet.test.ks.xml runs the file's tests.
            if (KScript.Projects.TestSettings.IsTestFileName(path))
            {
                return RunTests(new[] { path }.Concat(args.Skip(1)).ToArray());
            }

            return RunScript(path, args.Skip(1).ToArray());
        }

        /// <summary>Runs the script of a packaged app. Opened from Finder or a launcher there is no terminal, so a script that
        /// fails before its window opens shows why in a window instead.</summary>
        int RunApp(string manifest, string[] args)
        {
            string main = KScript.Apps.AppLauncher.MainScript(manifest);
            var host = KScriptHost.Console();
            host.Arguments = KScript.Apps.AppLauncher.ScriptArguments(args);
            var options = new InterpreterOptions();
            ProjectConfig project = null;
            try { project = ProjectConfig.FindAndLoad(Path.GetDirectoryName(main)); } catch (ProjectConfigException) { }
            if (project != null) options.DefaultPackages.AddRange(project.Packages);
            try
            {
                return new Interpreter(host, options: options).RunFile(main);
            }
            catch (KScriptException ex)
            {
                Error.WriteLine(ex.ToString());
                ShowError(Path.GetFileName(main), ex.ToString());
                return 1;
            }
        }

        /// <summary>kscript test: Ctrl+C stops the run after the current test.</summary>
        int RunTests(string[] args)
        {
            using var cancel = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            ConsoleCancelEventHandler stop = (_, e) => e.Cancel = StopOnFirstCtrlC(cancel);
            Console.CancelKeyPress += stop;
            try
            {
                return KScript.Testing.TestCommand.Run(args, Out, Error, cancel.Token);
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }
        }

        /// <summary>
        /// Runs a script. Packages listed in the nearest kscript.json are imported for it.
        /// </summary>
        int RunScript(string path, string[] scriptArgs)
        {
            var options = new InterpreterOptions();
            ConfigOptions config = null;
            try
            {
                ProjectConfig project = ProjectConfig.FindAndLoad(Path.GetDirectoryName(path));
                if (project != null)
                {
                    foreach (string warning in project.Warnings) Error.WriteLine($"{project.FilePath}: {warning}");
                    options.DefaultPackages.AddRange(project.Packages);
                }
                config = project?.ConfigOptions(EnvironmentName);
            }
            catch (ProjectConfigException ex)
            {
                Error.WriteLine(ex.Message);
                return 1;
            }

            var host = KScriptHost.Console();
            host.Arguments = scriptArgs;
            host.Config = config;

            using var cancel = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            options.Cancellation = cancel.Token;
            ConsoleCancelEventHandler stop = (_, e) => e.Cancel = StopOnFirstCtrlC(cancel);
            Console.CancelKeyPress += stop;

            try
            {
                return new Interpreter(host, options: options).RunFile(path);
            }
            catch (Exception ex) when (cancel.IsCancellationRequested && (ex is OperationCanceledException || ex is KScriptException { Type: "Cancelled" }))
            {
                // Stopped with Ctrl+C: the usual exit code for an interrupted command, without an error report.
                return 130;
            }
            catch (KScriptException ex)
            {
                Out.Flush();
                Error.WriteLine(ex.ToString());
                return 3;
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }
        }

        /// <summary>
        /// Resolves the script path given on the command line. Falls back to a case-insensitive
        /// lookup in the working directory, since Linux and macOS file systems are case-sensitive.
        /// </summary>
        static string ResolveScriptPath(string arg)
        {
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
