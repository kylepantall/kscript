namespace KScript.Runtime
{
    /// <summary>
    /// Where a script's input and output go. The CLI uses the console; tests, the web server and desktop windows use
    /// their own. Packages keep per-run state in <see cref="Items"/>.
    /// </summary>
    public class KScriptHost
    {
        public KScriptHost(TextWriter output, TextWriter error = null, TextReader input = null, bool interactive = false)
        {
            Out = TextWriter.Synchronized(output ?? TextWriter.Null);
            Error = TextWriter.Synchronized(error ?? output ?? TextWriter.Null);
            In = input ?? TextReader.Null;
            IsInteractive = interactive;
        }

        /// <summary>A host on the console. Input is interactive when it isn't redirected.</summary>
        public static KScriptHost Console() =>
            new KScriptHost(System.Console.Out, System.Console.Error, System.Console.In, interactive: !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected);

        /// <summary>A host that reads <paramref name="input"/> and collects output in <see cref="Out"/> (use a StringWriter).</summary>
        public static KScriptHost ForText(StringWriter output, string input = null) =>
            new KScriptHost(output, output, new StringReader(input ?? string.Empty));

        public TextWriter Out { get; }
        public TextWriter Error { get; }
        public TextReader In { get; }

        /// <summary>True when a person is at a terminal (so prompts can use colours, cursor keys and so on).</summary>
        public bool IsInteractive { get; }

        /// <summary>
        /// True when output may use ANSI colours: a person is at a terminal and NO_COLOR isn't set, or FORCE_COLOR is
        /// set; kscript --ansi and --no-ansi win over both. Colours are never written to files, pipes or tests unless asked for.
        /// </summary>
        public bool UseColour => KScript.Output.CliStyle.Allowed(IsInteractive);

        /// <summary>The test file kscript test is running in this host, or null outside a test run.</summary>
        public string TestFile { get; set; }

        /// <summary>True while one test of <see cref="TestFile"/> runs (not its top-level code).</summary>
        public bool TestRunning { get; set; }

        /// <summary>
        /// Work to do when the running test ends, such as undoing its database changes. Packages add to it while a
        /// test runs; the test runner runs it, newest first, and empties it.
        /// </summary>
        public List<Action> AfterTest { get; } = new List<Action>();

        /// <summary>
        /// True for a sandbox, such as the docs playground: KScript.Data's main database is an empty one in memory that
        /// ends with the run, whatever the config says.
        /// </summary>
        public bool MemoryDatabase { get; set; }

        /// <summary>
        /// Work to do when <see cref="TestFile"/>'s tests have all run (or a sandbox run ends), such as closing its
        /// database.
        /// </summary>
        public List<Action> AfterFile { get; } = new List<Action>();

        /// <summary>Command line arguments after the script path: <c>$args</c>.</summary>
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        /// <summary>The folder relative paths are resolved against when a script has no file (scripts use their own folder).</summary>
        public string WorkingDirectory
        {
            // Read when first needed, not when the host is made: a server sets it to the site folder, and its process
            // folder may since have been deleted or replaced.
            get => workingDirectory ??= WorkingFolder.Current;
            set => workingDirectory = value;
        }

        private string workingDirectory;

        /// <summary>
        /// When set, relative file paths start in this folder and no file outside it can be read or written.
        /// kscript serve sets it to the site folder, so a page can't reach the rest of the computer.
        /// </summary>
        public string FileRoot
        {
            get => fileRoot;
            set => fileRoot = value == null ? null : Path.GetFullPath(value);
        }

        /// <summary>False when files may only be read, not written, moved or deleted (pages served by kscript serve).</summary>
        public bool CanWriteFiles { get; set; } = true;

        /// <summary>What <see cref="FileRoot"/> is called in errors, such as "the site folder".</summary>
        public string FileRootName { get; set; } = "the allowed folder";

        private string fileRoot;

        /// <summary>
        /// Turns a path from a script into a full path: <c>~</c> is the home folder and relative paths start in
        /// <paramref name="baseDirectory"/> (or <see cref="FileRoot"/>). Paths outside <see cref="FileRoot"/> are refused.
        /// </summary>
        public string ResolvePath(string path, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new KScriptException("The path is empty.", "Argument");
            if (FileRoot == null && (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path.Substring(2) : string.Empty);
            if (FileRoot != null) path = path.TrimStart('/', '\\');
            string basePath = EvaluatingConfig ? baseDirectory ?? FileRoot : FileRoot ?? baseDirectory;
            string full = Path.GetFullPath(Path.Combine(basePath ?? WorkingDirectory, path));
            CheckPath(full, path);
            return full;
        }

        /// <summary>Throws if a full path is outside <see cref="FileRoot"/>.</summary>
        public void CheckPath(string fullPath, string written = null)
        {
            if (FileRoot == null) return;
            string root = FileRoot.EndsWith(Path.DirectorySeparatorChar) ? FileRoot : FileRoot + Path.DirectorySeparatorChar;
            if (fullPath != FileRoot && !fullPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new KScriptException($"'{written ?? fullPath}' is outside {FileRootName}.", "Permission");
        }

        /// <summary>Throws if files can't be changed here.</summary>
        public void CheckWrite(string what)
        {
            if (!CanWriteFiles) throw new KScriptException($"{what} can't change files here: files are read-only.", "Permission");
        }

        /// <summary>Where <c>$config</c> reads settings from (kscript --env, kscript.json's config section). Null for the defaults.</summary>
        public ConfigOptions Config { get; set; }

        internal bool EvaluatingConfig { get; init; }
        internal IReadOnlyDictionary<string, string> ConfigEnvironment { get; set; }

        /// <summary>The run's logging: providers and levels, set up from $config.logging on first use.</summary>
        public LogHub Logs
        {
            get
            {
                lock (Items) return logs ??= new LogHub(this);
            }
        }

        private LogHub logs;

        /// <summary>Writes out log entries that providers hold back. The interpreter calls it when a run ends.</summary>
        public void FlushLogs() => logs?.Flush();

        /// <summary>State that packages keep for the length of a run, by key.</summary>
        public Dictionary<string, object> Items { get; } = new Dictionary<string, object>();

        /// <summary>Writes a line of output, as &lt;echo&gt; does.</summary>
        public virtual void WriteLine(string text)
        {
            if (CurrentCapture is TextWriter capture) capture.WriteLine(text);
            else Out.WriteLine(text);
        }

        public virtual void Write(string text)
        {
            if (CurrentCapture is TextWriter capture) capture.Write(text);
            else Out.Write(text);
        }

        /// <summary>Reads a line of input, or null at the end of input.</summary>
        public virtual string ReadLine()
        {
            lock (captures)
            {
                if (inputs.Count > 0) return inputs[inputs.Count - 1].ReadLine();
            }
            return In.ReadLine();
        }

        /// <summary>Waits for a key press without showing it (the terminal's, unless a host stands in for it).</summary>
        public virtual ConsoleKeyInfo ReadKey() => System.Console.ReadKey(true);

        /// <summary>Clears the terminal.</summary>
        public virtual void ClearScreen() => System.Console.Clear();

        /// <summary>The width of the terminal in characters.</summary>
        public virtual int TerminalWidth => System.Console.WindowWidth;

        private readonly List<TextWriter> captures = new List<TextWriter>();
        private readonly List<TextReader> inputs = new List<TextReader>();

        private TextWriter CurrentCapture
        {
            get { lock (captures) return captures.Count > 0 ? captures[captures.Count - 1] : null; }
        }

        /// <summary>
        /// Sends what <see cref="Write"/> and <see cref="WriteLine"/> print to <paramref name="output"/> until the
        /// result is disposed, for example while a test runs or inside &lt;capture&gt;. Captures nest.
        /// </summary>
        public IDisposable CaptureOutput(TextWriter output)
        {
            lock (captures) captures.Add(output);
            return new Undo(() => { lock (captures) captures.Remove(output); });
        }

        /// <summary>Makes <see cref="ReadLine"/> read from <paramref name="input"/> until the result is disposed.</summary>
        public IDisposable ProvideInput(TextReader input)
        {
            lock (captures) inputs.Add(input);
            return new Undo(() => { lock (captures) inputs.Remove(input); });
        }

        private sealed class Undo : IDisposable
        {
            private Action action;

            public Undo(Action action) => this.action = action;

            public void Dispose()
            {
                action?.Invoke();
                action = null;
            }
        }
    }
}
