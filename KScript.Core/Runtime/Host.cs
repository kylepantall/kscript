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

        /// <summary>Command line arguments after the script path: <c>$args</c>.</summary>
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        /// <summary>The folder relative paths are resolved against when a script has no file (scripts use their own folder).</summary>
        public string WorkingDirectory { get; set; } = Directory.GetCurrentDirectory();

        /// <summary>State that packages keep for the length of a run, by key.</summary>
        public Dictionary<string, object> Items { get; } = new Dictionary<string, object>();

        /// <summary>Writes a line of output, as &lt;echo&gt; does.</summary>
        public virtual void WriteLine(string text) => Out.WriteLine(text);

        public virtual void Write(string text) => Out.Write(text);

        /// <summary>Reads a line of input, or null at the end of input.</summary>
        public virtual string ReadLine() => In.ReadLine();
    }
}
