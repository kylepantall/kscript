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
        /// set. Colours are never written to files, pipes or tests unless asked for.
        /// </summary>
        public bool UseColour =>
            Environment.GetEnvironmentVariable("FORCE_COLOR") != null || (IsInteractive && Environment.GetEnvironmentVariable("NO_COLOR") == null);

        /// <summary>Command line arguments after the script path: <c>$args</c>.</summary>
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        /// <summary>The folder relative paths are resolved against when a script has no file (scripts use their own folder).</summary>
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

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
