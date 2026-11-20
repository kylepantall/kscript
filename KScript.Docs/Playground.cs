using System.Text;
using System.Text.RegularExpressions;

using KScript.Runtime;
using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Docs
{
    /// <summary>What running an example printed, and how it ended.</summary>
    public sealed class PlaygroundResult
    {
        public string Output { get; set; } = string.Empty;

        /// <summary>The error the script stopped with (<c>example.ks.xml:3:5: Type: message</c>), or null.</summary>
        public string Error { get; set; }

        public int ExitCode { get; set; }
        public long Milliseconds { get; set; }

        /// <summary>True if the script printed more than <see cref="Playground.MaxOutput"/> characters and the rest was dropped.</summary>
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// Runs the "try it" examples of the docs site. Each run gets a fresh interpreter and its own empty folder, which is
    /// the only place files can be read or written and is deleted afterwards. Only the packages that can't reach
    /// outside that folder are available (no network, servers, windows or language models), input comes from the page,
    /// and a run stops after a few seconds or a few million steps.
    /// </summary>
    public static class Playground
    {
        /// <summary>The packages an example may import. KScript.Core is always there.</summary>
        public static readonly IReadOnlyList<string> Packages = new[]
        {
            "KScript.Terminal", "KScript.Math", "KScript.IO", "KScript.Markdown", "KScript.Style",
        };

        /// <summary>Packages that only work in a real script, with why.</summary>
        public static readonly IReadOnlyDictionary<string, string> Unavailable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KScript.Net"] = "makes network requests",
            ["KScript.Web"] = "serves web pages",
            ["KScript.Http"] = "serves web routes",
            ["KScript.UI"] = "opens desktop windows",
            ["KScript.AI"] = "calls a language model",
            ["KScript.Test"] = "runs with kscript test",
        };

        public const int MaxOutput = 64 * 1024;
        public const int MaxCode = 64 * 1024;
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
        public const long MaxSteps = 5_000_000;

        private static readonly Regex Ansi = new Regex(@"\u001b\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

        /// <summary>The registry examples run with: KScript.Core and <see cref="Packages"/>.</summary>
        public static PackageRegistry CreateRegistry()
        {
            var registry = new PackageRegistry();
            foreach (var (name, type, description) in PackageRegistry.BuiltIn)
            {
                if (Packages.Contains(name)) registry.Register(name, type, description);
            }
            return registry;
        }

        /// <summary>True for code that is a whole XML script, rather than statements to put in one.</summary>
        public static bool IsWholeScript(string code)
        {
            string t = code.TrimStart();
            while (t.StartsWith("<!--", StringComparison.Ordinal))
            {
                int end = t.IndexOf("-->", StringComparison.Ordinal);
                if (end < 0) return false;
                t = t.Substring(end + 3).TrimStart();
            }
            return t.StartsWith("<?xml", StringComparison.Ordinal) || t.StartsWith("<kscript", StringComparison.Ordinal) || t.StartsWith("<KScript.", StringComparison.Ordinal);
        }

        /// <summary>
        /// XML statements without a &lt;kscript&gt; root get one. It goes on the first line, so line numbers in errors
        /// still match what the reader sees.
        /// </summary>
        public static string AsScript(string code, string format)
        {
            code ??= string.Empty;
            if (format == "kscript" || IsWholeScript(code)) return code;
            return "<kscript>" + code + "\n</kscript>";
        }

        /// <summary>Cancels a run once its time is up; tests replace it with one that is already cancelled.</summary>
        internal static Func<TimeSpan, CancellationTokenSource> StartTimer = limit => new CancellationTokenSource(limit);

        public static PlaygroundResult Run(string code, string format = "xml", string input = null, TimeSpan? timeout = null)
        {
            format = format == "kscript" ? "kscript" : "xml";
            var result = new PlaygroundResult();
            if ((code ?? string.Empty).Length > MaxCode)
            {
                result.Error = $"The example is too long to run here (over {MaxCode / 1024} KB).";
                result.ExitCode = 1;
                return result;
            }

            string folder = Path.Combine(Path.GetTempPath(), "kscript-try-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = format == "kscript" ? "example.kscript" : "example.ks.xml";
            string path = Path.Combine(folder, file);
            var output = new LimitedWriter(MaxOutput);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var limit = timeout ?? DefaultTimeout;
            using var cancel = StartTimer(limit);
            try
            {
                var host = new KScriptHost(output, output, new StringReader(input ?? string.Empty))
                {
                    WorkingDirectory = folder,
                    FileRoot = folder,
                    FileRootName = "the example's folder",
                };
                var options = new InterpreterOptions { MaxSteps = MaxSteps, MaxCallDepth = 300, Timeout = limit, Cancellation = cancel.Token };
                var interpreter = new Interpreter(host, CreateRegistry(), options);
                result.ExitCode = interpreter.RunText(AsScript(code, format), path);
            }
            catch (KScriptException ex)
            {
                result.ExitCode = 3;
                result.Error = Explain(ex.ToString().Replace(path, file).Replace(folder + Path.DirectorySeparatorChar, string.Empty).Replace(folder, "."));
            }
            catch (OperationCanceledException)
            {
                result.ExitCode = 3;
                result.Error = $"Stopped: the example ran for longer than {limit.TotalSeconds:0} seconds.";
            }
            finally
            {
                watch.Stop();
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            result.Milliseconds = watch.ElapsedMilliseconds;
            result.Output = Ansi.Replace(output.ToString(), string.Empty);
            result.Truncated = output.Truncated;
            return result;
        }

        /// <summary>Says why a package can't be imported here, instead of "isn't installed".</summary>
        private static string Explain(string error)
        {
            foreach (var kv in Unavailable)
            {
                if (error.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) && (error.Contains("isn't installed") || error.Contains("no package") || error.Contains("Unknown package") || error.Contains("Import")))
                    return error + $"\n\n{kv.Key} {kv.Value}, so it can't run in the docs. Save the example as a .ks.xml file and run it with kscript.";
            }
            return error;
        }

        /// <summary>Converts example code between XML and the .kscript format, or returns null if it can't.</summary>
        public static string Convert(string code, string to)
        {
            if (string.IsNullOrWhiteSpace(code) || (code.Length > MaxCode)) return null;
            // The parsers report problems as diagnostics rather than exceptions, so code with errors gives null here.
            if (to == "kscript")
            {
                var xml = MarkupParser.Parse(AsScript(code, "xml"), DocumentKind.Script);
                if (xml.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return null;
                return ScriptConverter.ToCode(xml, nameList: LanguageCatalog.Default.IsNameList).Text?.TrimEnd() + "\n";
            }
            var parsed = CodeParser.Parse(code);
            if (parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return null;
            string text = ScriptConverter.ToXml(parsed, nameList: LanguageCatalog.Default.IsNameList).Text;
            return text == null ? null : Unwrap(text);
        }

        /// <summary>Takes the statements out of a &lt;kscript&gt; root, so examples stay short.</summary>
        internal static string Unwrap(string xml)
        {
            string t = xml.Trim();
            if (!t.StartsWith("<kscript>", StringComparison.Ordinal) || !t.EndsWith("</kscript>", StringComparison.Ordinal)) return t + "\n";
            string inner = t.Substring(9, t.Length - 9 - 10).Trim('\r', '\n');
            string[] lines = inner.Split('\n');
            int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart(' ').Length).DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart())).TrimEnd() + "\n";
        }

        /// <summary>A writer that keeps the first <c>max</c> characters and drops the rest.</summary>
        internal sealed class LimitedWriter : TextWriter
        {
            private readonly StringBuilder text = new StringBuilder();
            private readonly int max;

            // The docs page shows output the same on every OS: line ends are written as \n, and the limit counts
            // what the reader sees.
            public LimitedWriter(int max)
            {
                this.max = max;
                NewLine = "\n";
            }

            public bool Truncated { get; private set; }

            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (text.Length < max) text.Append(value);
                else Truncated = true;
            }

            public override void Write(string value)
            {
                if (value == null) return;
                value = value.Replace("\r\n", "\n");
                int room = max - text.Length;
                if (value.Length <= room) text.Append(value);
                else
                {
                    if (room > 0) text.Append(value, 0, room);
                    Truncated = true;
                }
            }

            public override string ToString() => text.ToString();
        }
    }
}
