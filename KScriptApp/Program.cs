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
        internal static void ReportUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) ErrorReport.Write(Console.Error, ex);
        }

        static int Main(string[] args)
        {
            WorkingFolder.Remember();
            // An error on a thread of its own (a timer, a background task) ends the process: say why the KScript way.
            AppDomain.CurrentDomain.UnhandledException += ReportUnhandled;
            // A program built by kscript compile runs its own script and nothing else. This check comes first, and
            // Run is never reached, because a compiled program leaves out the assemblies Run uses (the command line,
            // the language server and the packages the script doesn't import).
            var commandLine = new CommandLine();
            if (CompiledApp.IsCompiled) return commandLine.RunCompiledSafely(CompiledApp.FindManifest(), args);
            return commandLine.Run(args);
        }
    }

    /// <summary>The kscript command: runs scripts and dispatches check, fmt, lsp, test, css, serve, docs, app, update, ide and make.</summary>
    internal sealed class CommandLine
    {
        private readonly TextWriter output, error;

        // Read Console.Out and Console.Error when they're used, not when this is made: setting Console.OutputEncoding
        // (Run does) replaces both writers, and colour is only on for the real console writer (CliStyle.Supports).
        public TextWriter Out { get => output ?? Console.Out; init => output = value; }
        public TextWriter Error { get => error ?? Console.Error; init => error = value; }

        /// <summary>Where kscript lsp reads and writes the protocol.</summary>
        public Func<Stream> StandardInput { get; init; } = Console.OpenStandardInput;
        public Func<Stream> StandardOutput { get; init; } = Console.OpenStandardOutput;

        /// <summary>Stops kscript serve and running scripts, as Ctrl+C does.</summary>
        public System.Threading.CancellationToken Cancellation { get; init; }

        /// <summary>Shows why a packaged app's script failed, in a window.</summary>
        /// A lambda, not a method group, so KScript.UI only loads when an error is shown (compiled terminal programs leave it out).
        public Action<string, string> ShowError { get; init; } = (title, message) => KScript.UI.DesktopHost.ShowError(title, message);

        /// <summary>The first Ctrl+C stops the work cleanly (finally blocks run); a second one ends the process.</summary>
        internal static bool StopOnFirstCtrlC(System.Threading.CancellationTokenSource cancel)
        {
            if (cancel.IsCancellationRequested) return false;
            cancel.Cancel();
            return true;
        }

        void PrintUsage(TextWriter output) => CliReference.PrintUsage(output);

        /// <summary>
        /// While kscript serve runs, keeps what the project makes from its code (the agent docs and the other
        /// <see cref="KScript.Language.Outputs.ProjectOutputs"/>) up to date as files change. Does nothing when the
        /// project has none turned on. Its own method, so compiled sites (which leave KScript.Language out) never load it.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal void WatchOutputs(string root, System.Threading.CancellationToken stop)
        {
            if (!Directory.Exists(root) || KScript.Language.Outputs.ProjectOutputs.Enabled(root).Count == 0) return;
            var watcher = new KScript.Language.Outputs.ProjectWatcher(root, null, line => Out.WriteLine($"Watch: {line}"));
            _ = watcher.RunAsync(stop);
        }

        /// <summary>
        /// kscript serve [folder] [--port N] [--host H] [--with-docs]: serves KScript pages from a folder. In a project, the
        /// nearest kscript.json gives the defaults (serve.root, serve.port, serve.host, serve.docs); options on the
        /// command line win. --with-docs also serves the docs, with a page about the project, on localhost; compiled
        /// sites (<paramref name="allowDocs"/> false) and the production environment never do.
        /// </summary>
        int Serve(string[] args, bool allowDocs = true, bool watch = true)
        {
            string folder = null;
            int? port = null;
            string host = null;
            bool? withDocs = null;
            int? docsPort = null;
            bool withWorker = false;

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
                    case "--with-docs":
                        withDocs = true;
                        break;
                    case "--no-docs":
                        withDocs = false;
                        break;
                    case "--with-worker":
                        withWorker = true;
                        break;
                    case "--docs-port":
                        if (i + 1 >= args.Length || !int.TryParse(args[++i], out int dp) || dp < 1 || dp > 65535)
                        {
                            Error.WriteLine("--docs-port needs a number, e.g. --docs-port 4180");
                            return 1;
                        }
                        docsPort = dp;
                        withDocs ??= true;
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
                ErrorReport.Write(Error, ex);
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
                docsPort ??= project.Serve.DocsPort;
                withDocs ??= project.Serve.Docs;
            }

            // The docs (which run examples) are for working on the site: never for a compiled program or in production.
            System.Threading.Tasks.Task docs = System.Threading.Tasks.Task.CompletedTask;
            using var stopDocs = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            if (withDocs == true)
            {
                string environment = project?.ConfigOptions(EnvironmentName).Environment ?? EnvironmentName ?? Environment.GetEnvironmentVariable("KSCRIPT_ENV");
                if (!allowDocs)
                    withDocs = false;
                else if (string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase))
                    Error.WriteLine("Docs: not served in the production environment.");
                else if (Directory.Exists(folder ?? "."))
                    docs = ServeDocs(folder ?? ".", project, docsPort, port ?? 8080, stopDocs.Token);
            }

            // A worker in the server's process runs the jobs routes dispatch, even on the memory connection.
            System.Threading.Tasks.Task worker = System.Threading.Tasks.Task.CompletedTask;
            using var stopWorker = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            if (withWorker && allowDocs)
                worker = StartWorker(project?.Directory ?? Path.GetFullPath(folder ?? "."), stopWorker.Token);

            using var watching = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            try
            {
                if (watch) WatchOutputs(project?.Directory ?? Path.GetFullPath(folder ?? "."), watching.Token);
                return KScript.Http.HttpServer.RunAsync(folder ?? ".", project, port ?? 8080, host ?? "localhost", Out, Cancellation).GetAwaiter().GetResult();
            }
            catch (DirectoryNotFoundException ex)
            {
                ErrorReport.Write(Error, ex);
                return 1;
            }
            catch (FileNotFoundException ex)
            {
                ErrorReport.Write(Error, ex);
                return 1;
            }
            catch (IOException ex)
            {
                ErrorReport.Write(Error, "Could not start the server.", ex);
                return 1;
            }
            finally
            {
                stopDocs.Cancel();
                docs.GetAwaiter().GetResult();
                stopWorker.Cancel();
                worker.GetAwaiter().GetResult();
                watching.Cancel();
            }
        }

        /// <summary>The worker for kscript serve --with-worker, on its own thread until the server stops.</summary>
        System.Threading.Tasks.Task StartWorker(string directory, System.Threading.CancellationToken stop) =>
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                int code = KScript.Queueing.QueueCommand.RunWorker(Array.Empty<string>(), Out, Error, stop, directory, EnvironmentName);
                if (code != 0) Error.WriteLine("Worker: stopped.");
            }, System.Threading.Tasks.TaskCreationOptions.LongRunning);

        /// <summary>
        /// Starts the docs for kscript serve --with-docs. A method of its own, so serving a compiled site (whose program
        /// leaves KScript.Docs out) never loads that assembly.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        System.Threading.Tasks.Task ServeDocs(string folder, KScript.Projects.ProjectConfig project, int? docsPort, int sitePort, System.Threading.CancellationToken stop) =>
            KScript.Docs.DocsCommand.ServeProjectAsync(folder, project, docsPort, sitePort, Out, Error, stop);

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

        /// <summary>
        /// --ansi and --no-ansi force colours on or off for the whole run (as artisan's do): before a script, or
        /// anywhere after one of kscript's own commands.
        /// </summary>
        static string[] TakeAnsi(string[] args)
        {
            bool command = args.Length > 0 && (CliReference.Find(args[0]) != null || args[0].StartsWith("make:", StringComparison.Ordinal) || args[0] == "help");
            var rest = new List<string>();
            bool before = true;
            foreach (string a in args)
            {
                if ((before || command) && (a == "--ansi" || a == "--no-ansi"))
                {
                    KScript.Output.CliStyle.Override = a == "--ansi";
                    continue;
                }
                if (!a.StartsWith("-", StringComparison.Ordinal)) before = false;
                rest.Add(a);
            }
            return rest.ToArray();
        }

        /// <summary>kscript openapi [folder] [--yaml] [--out file]: the OpenAPI document made from a site's routes.</summary>
        int OpenApi(string[] args)
        {
            string output = null, folder = null;
            bool yaml = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--yaml": yaml = true; break;
                    case "--json": yaml = false; break;
                    case "--out":
                    case "-o":
                        if (i + 1 >= args.Length)
                        {
                            Error.WriteLine("--out needs a file name.");
                            return 1;
                        }
                        output = args[++i];
                        break;
                    default:
                        if (args[i].StartsWith("-", StringComparison.Ordinal))
                        {
                            Error.WriteLine($"Unknown option '{args[i]}' for kscript openapi. Use --yaml or --out file.");
                            return 1;
                        }
                        folder = args[i];
                        break;
                }
            }
            if (output != null && (output.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || output.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))) yaml = true;
            if (!TryFindSite(ref folder, out KScript.Projects.ProjectConfig project)) return 1;
            return KScript.Http.HttpServer.WriteOpenApi(folder ?? ".", project, yaml, output, Out, Error);
        }

        /// <summary>The site folder for a command given an optional folder: the project's site root when it names the project.</summary>
        bool TryFindSite(ref string folder, out KScript.Projects.ProjectConfig project)
        {
            project = null;
            try
            {
                project = KScript.Projects.ProjectConfig.FindAndLoad(folder ?? ".");
            }
            catch (KScript.Projects.ProjectConfigException ex)
            {
                ErrorReport.Write(Error, ex);
                return false;
            }
            string given = folder == null ? null : Path.GetFullPath(folder);
            if (project != null && (given == null || string.Equals(given.TrimEnd(Path.DirectorySeparatorChar), project.Directory, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)))
                folder = project.SiteRoot;
            if (!Directory.Exists(folder ?? "."))
            {
                Error.WriteLine($"Site folder not found: {folder}");
                return false;
            }
            return true;
        }

        /// <summary>kscript routes [folder]: the route table of a site (the project's site root by default).</summary>
        int Routes(string[] args)
        {
            string folder = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            bool verbose = args.Contains("-v") || args.Contains("--verbose");
            KScript.Projects.ProjectConfig project;
            try
            {
                project = KScript.Projects.ProjectConfig.FindAndLoad(folder ?? ".");
            }
            catch (KScript.Projects.ProjectConfigException ex)
            {
                ErrorReport.Write(Error, ex);
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
            return KScript.Http.HttpServer.ListRoutes(folder ?? ".", project, Out, Error, verbose);
        }

        /// <summary>
        /// Adds the outputs that live outside KScript.Language (the project's docs) to ProjectOutputs. Its own method,
        /// so compiled programs, which never call <see cref="Run"/> and leave KScript.Docs out, never load it.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void AddProjectOutputs() => KScript.Language.Outputs.ProjectOutputs.Add(new KScript.Docs.ProjectDocsOutput());

        /// <summary>
        /// Runs a kscript command. Whatever goes wrong is shown as a KScript error, never as a .NET exception:
        /// --debug (anywhere before the script, or KSCRIPT_DEBUG=1) adds the .NET details.
        /// </summary>
        public int Run(string[] args)
        {
            args = TakeDebug(args);
            try
            {
                return RunCommand(args);
            }
            catch (Exception ex)
            {
                Out.Flush();
                ErrorReport.Write(Error, ex);
                return ErrorReport.ToKScript(ex).Type == ErrorReport.InternalType ? 70 : 1;
            }
        }

        /// <summary>
        /// Takes --debug out of the arguments and turns on <see cref="ErrorReport.Debug"/>. Like --env, it is read before
        /// the script path (later arguments are the script's own), or anywhere for commands such as serve.
        /// </summary>
        internal static string[] TakeDebug(string[] args)
        {
            var rest = new List<string>(args.Length);
            bool before = true;
            string first = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            bool script = first != null && (first.Contains('.') || first.Contains('/') || first.Contains('\\'));
            foreach (string a in args)
            {
                if (a == "--debug" && (before || !script))
                {
                    ErrorReport.Debug = true;
                    continue;
                }
                if (!a.StartsWith("-", StringComparison.Ordinal)) before = false;
                rest.Add(a);
            }
            return rest.ToArray();
        }

        int RunCommand(string[] args)
        {
            AddProjectOutputs();
            if (args.Length > 0 && args[0] == "lsp")
            {
                // stdout carries the protocol, so nothing else may write to it.
                return KScript.LanguageServer.KScriptLanguageServer.RunStdioAsync(StandardInput(), StandardOutput()).GetAwaiter().GetResult();
            }

            if (args.Length > 0 && args[0] == "mcp")
            {
                // Like lsp: on stdio, stdout carries the protocol.
                return RunMcp(args.Skip(1).ToArray());
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            KScript.Updates.SelfUpdater.CleanupPreviousBinary();

            // --env staging (before the script, or anywhere after serve and test) picks the environment for $config.
            try
            {
                KScript.Output.CliStyle.Override = null;
                args = TakeAnsi(args);
                args = TakeEnvironment(args);
            }
            catch (ArgumentException ex)
            {
                ErrorReport.Write(Error, ex);
                return 1;
            }

            if (args.Length > 0 && (args[0] == "--version" || args[0] == "version"))
            {
                Out.WriteLine(KScript.Updates.SelfUpdater.CurrentVersion);
                return 0;
            }

            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help" || args[0] == "-?" || args[0] == "/?"))
            {
                // kscript help serve shows one command, as artisan help does.
                if (args.Length > 1 && !args[1].StartsWith("-", StringComparison.Ordinal))
                {
                    if (CliReference.Find(args[1] == "make:" ? "make" : args[1]) is CliCommand command)
                    {
                        CliReference.PrintCommand(Out, command);
                        return 0;
                    }
                    new KScript.Output.CliOutput(Error).Error($"There is no command '{args[1]}'. kscript --help lists them.");
                    return 1;
                }
                PrintUsage(Out);
                return 0;
            }

            // kscript serve --help: the command's help from CliReference. make and ide show their own, which list
            // their file types and editors.
            if (args.Length == 2 && (args[1] == "--help" || args[1] == "-h") && args[0] != "make" && args[0] != "ide"
                && CliReference.Find(args[0]) is CliCommand asked)
            {
                CliReference.PrintCommand(Out, asked);
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

            if (args.Length > 0 && args[0] == "compile")
            {
                return KScript.Compile.CompileCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args.Length > 0 && args[0] == "check")
            {
                if (args.Contains("--print-config"))
                    return KScript.LanguageServer.CheckCommand.PrintConfig(args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)), Out);
                int formatAt = Array.IndexOf(args, "--format");
                string format = formatAt > 0 && formatAt + 1 < args.Length ? args[formatAt + 1] : args.FirstOrDefault(a => a.StartsWith("--format=", StringComparison.Ordinal))?.Substring(9) ?? "text";
                if (format != "text" && format != "json")
                {
                    Error.WriteLine($"Unknown --format '{format}'. Use text or json.");
                    return 2;
                }
                IEnumerable<string> paths = args.Skip(1).Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && !(formatAt > 0 && i + 1 == formatAt + 1));
                return KScript.LanguageServer.CheckCommand.Run(paths, Out, warningsAsErrors: args.Contains("--warnaserror"), json: format == "json");
            }

            if (args.Length > 0 && args[0] == "watch")
            {
                return RunWatch(args.Skip(1).ToArray());
            }

            if (args.Length > 0 && args[0] == "agents")
            {
                return KScript.LanguageServer.AgentsCommand.Run(args.Skip(1), Out);
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

            if (args[0] == "docs")
            {
                return RunDocs(args.Skip(1).ToArray());
            }

            if (args[0] == "css")
            {
                return KScript.Style.CssCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args[0] == "routes")
            {
                return Routes(args.Skip(1).ToArray());
            }

            if (args[0] == "openapi")
            {
                return OpenApi(args.Skip(1).ToArray());
            }

            if (args[0] == "convert")
            {
                return KScript.Conversion.ConvertCommand.Run(args.Skip(1).ToArray(), Out, Error);
            }

            if (args[0] == "test")
            {
                return RunTests(args.Skip(1).ToArray());
            }

            if (args[0] == "worker")
            {
                return RunWorker(args.Skip(1).ToArray());
            }

            if (args[0] == "queue")
            {
                return KScript.Queueing.QueueCommand.RunQueue(args.Skip(1).ToArray(), Out, Error, environment: EnvironmentName);
            }

            // kscript make component Hero, or Artisan-style kscript make:component Hero
            if (args[0] == "make" || args[0].StartsWith("make:", StringComparison.Ordinal))
            {
                var rest = args[0] == "make" ? args.Skip(1) : new[] { args[0].Substring(5) }.Concat(args.Skip(1));
                return KScript.Scaffolding.MakeCommand.Run(rest.ToArray(), Out, Error);
            }

            if (args[0].StartsWith("-", StringComparison.Ordinal))
            {
                new KScript.Output.CliOutput(Error).Error($"Unknown option '{args[0]}'.\nRun kscript --help for usage.");
                return 1;
            }

            string path = ResolveScriptPath(args[0]);
            if (path == null)
            {
                new KScript.Output.CliOutput(Error).Error($"There is no script at '{args[0]}'." + (CliReference.Find(args[0]) == null && !args[0].Contains('.') ? " kscript --help lists the commands." : string.Empty));
                return 2;
            }

            // kscript greet.test.ks.xml runs the file's tests.
            if (KScript.Projects.TestSettings.IsTestFileName(path))
            {
                return RunTests(new[] { path }.Concat(args.Skip(1)).ToArray());
            }

            return RunScript(path, args.Skip(1).ToArray());
        }

        /// <summary>
        /// Runs a program built by kscript compile. Its manifest (extracted by the .NET host next to the program's
        /// files) says what it is: a script, a desktop window, or a site to serve. The arguments all go to the program.
        /// </summary>
        internal int RunCompiledSafely(string manifest, string[] args)
        {
            try
            {
                return RunCompiled(manifest, args);
            }
            catch (Exception ex)
            {
                Out.Flush();
                ErrorReport.Write(Error, ex);
                return 70;
            }
        }

        internal int RunCompiled(string manifest, string[] args)
        {
            if (manifest == null)
            {
                Error.WriteLine("This program's files are missing. Delete its folder in the .net folder in your home folder (%TEMP%\\.net on Windows) and start it again.");
                return 1;
            }
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            CompiledApp.Manifest app = CompiledApp.Read(manifest);
            return app.Kind switch
            {
                "site" => Serve(new[] { app.Main }.Concat(args).ToArray(), allowDocs: false, watch: false),
                "window" => RunWindow(app.Main, args),
                _ => RunScript(app.Main, args),
            };
        }

        /// <summary>Runs a compiled desktop app. Opened from Finder or the Start menu there is no terminal, so a script that
        /// fails before its window opens shows why in a window instead.</summary>
        int RunWindow(string main, string[] args)
        {
            var host = KScriptHost.Console();
            host.Arguments = CompiledApp.ScriptArguments(args);
            var options = new InterpreterOptions();
            ProjectConfig project = null;
            try { project = ProjectConfig.FindAndLoad(Path.GetDirectoryName(main)); } catch (ProjectConfigException) { }
            if (project != null) options.DefaultPackages.AddRange(project.Packages);
            host.Config = project?.ConfigOptions(null);
            try
            {
                return new Interpreter(host, options: options).RunFile(main);
            }
            catch (Exception ex) when (ex is not ControlSignal)
            {
                ErrorReport.Write(Error, ex);
                ShowError(Path.GetFileName(main), ErrorReport.Format(ex));
                return 1;
            }
        }

        /// <summary>Opens a URL for kscript docs (tests replace it).</summary>
        public Action<string> OpenBrowser { get; init; }

        /// <summary>kscript watch: rebuilds the project's outputs as files change, until Ctrl+C.</summary>
        int RunWatch(string[] args)
        {
            using var cancel = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            ConsoleCancelEventHandler stop = (_, e) => e.Cancel = StopOnFirstCtrlC(cancel);
            Console.CancelKeyPress += stop;
            try
            {
                return KScript.LanguageServer.WatchCommand.Run(args, Out, cancel.Token);
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }
        }

        /// <summary>kscript docs: Ctrl+C stops the docs server, which then removes its files.</summary>
        int RunDocs(string[] args)
        {
            using var cancel = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            ConsoleCancelEventHandler stop = (_, e) => e.Cancel = StopOnFirstCtrlC(cancel);
            Console.CancelKeyPress += stop;
            try
            {
                return KScript.Docs.DocsCommand.Run(args, Out, Error, cancel.Token, OpenBrowser);
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }
        }

        /// <summary>kscript mcp: the docs for AI coding assistants. Ctrl+C stops the HTTP server.</summary>
        int RunMcp(string[] args)
        {
            using var cancel = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            ConsoleCancelEventHandler stop = (_, e) => e.Cancel = StopOnFirstCtrlC(cancel);
            Console.CancelKeyPress += stop;
            try
            {
                return KScript.Docs.Mcp.McpCommand.Run(args, StandardInput(), StandardOutput(), Error, cancel.Token);
            }
            finally
            {
                Console.CancelKeyPress -= stop;
            }
        }

        /// <summary>kscript worker: runs queued jobs until Ctrl+C, which lets the running jobs finish.</summary>
        int RunWorker(string[] args)
        {
            using var cancel = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            ConsoleCancelEventHandler stop = (_, e) => e.Cancel = StopOnFirstCtrlC(cancel);
            Console.CancelKeyPress += stop;
            try
            {
                return KScript.Queueing.QueueCommand.RunWorker(args, Out, Error, cancel.Token, environment: EnvironmentName);
            }
            finally
            {
                Console.CancelKeyPress -= stop;
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
                ErrorReport.Write(Error, ex);
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
            catch (Exception ex) when (ex is not ControlSignal)
            {
                // A script's error, or anything else that went wrong while it ran: a KScript error either way.
                Out.Flush();
                ErrorReport.Write(Error, ex);
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

            string match = Directory.GetFiles(KScript.Runtime.WorkingFolder.Current)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), arg, StringComparison.OrdinalIgnoreCase));

            return match;
        }
    }
}
