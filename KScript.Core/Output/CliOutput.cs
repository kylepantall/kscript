using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KScript.Output
{
    /// <summary>
    /// ANSI styles for one writer. Colours are only used when the writer is the console and that stream is a terminal,
    /// so files, pipes and tests get plain text. FORCE_COLOR turns them on, NO_COLOR off, and kscript --ansi / --no-ansi
    /// (<see cref="Override"/>) win over both.
    /// </summary>
    public sealed class CliStyle
    {
        private static readonly Regex Escapes = new Regex("\u001b\\[[0-9;]*m", RegexOptions.Compiled);

        public CliStyle(bool enabled) => Enabled = enabled;

        /// <summary>Set by kscript --ansi (true) or --no-ansi (false) for the whole run.</summary>
        public static bool? Override { get; set; }

        public static readonly CliStyle Plain = new CliStyle(false);

        public bool Enabled { get; }

        /// <summary>The style for a writer: coloured only when it is a terminal (see the class summary).</summary>
        public static CliStyle For(TextWriter writer) => new CliStyle(Supports(writer));

        /// <summary>True when ANSI colours should be written to <paramref name="writer"/>.</summary>
        public static bool Supports(TextWriter writer)
        {
            if (Override is bool forced) return forced;
            if (Environment.GetEnvironmentVariable("FORCE_COLOR") is string force) return force != "0" && !force.Equals("false", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
            if (Environment.GetEnvironmentVariable("TERM") == "dumb") return false;
            if (writer == null) return false;
            try
            {
                if (ReferenceEquals(writer, Console.Out)) return !Console.IsOutputRedirected;
                if (ReferenceEquals(writer, Console.Error)) return !Console.IsErrorRedirected;
            }
            catch (IOException) { }
            return false;
        }

        /// <summary>True when colours are allowed at all (for a host that knows it is at a terminal).</summary>
        public static bool Allowed(bool interactive)
        {
            if (Override is bool forced) return forced;
            if (Environment.GetEnvironmentVariable("FORCE_COLOR") is string force) return force != "0" && !force.Equals("false", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
            return interactive && Environment.GetEnvironmentVariable("TERM") != "dumb";
        }

        /// <summary>Wraps text in an SGR code such as "1" (bold) or "1;97;41" (bold white on red), when enabled.</summary>
        public string Apply(string text, string code) => Enabled && !string.IsNullOrEmpty(text) && code != null ? $"\u001b[{code}m{text}\u001b[0m" : text ?? string.Empty;

        public string Bold(string text) => Apply(text, "1");
        public string Dim(string text) => Apply(text, "2");
        public string Italic(string text) => Apply(text, "3");
        public string Underline(string text) => Apply(text, "4");
        public string Red(string text) => Apply(text, "31");
        public string Green(string text) => Apply(text, "32");
        public string Yellow(string text) => Apply(text, "33");
        public string Blue(string text) => Apply(text, "34");
        public string Magenta(string text) => Apply(text, "35");
        public string Cyan(string text) => Apply(text, "36");
        public string Gray(string text) => Apply(text, "90");

        /// <summary>Text without its ANSI codes.</summary>
        public static string Strip(string text) => text == null ? string.Empty : Escapes.Replace(text, string.Empty);

        /// <summary>How many columns text takes in a terminal, leaving out ANSI codes.</summary>
        public static int VisibleLength(string text) => Strip(text).Length;

        /// <summary>Pads text on the right to <paramref name="width"/> visible columns.</summary>
        public static string Pad(string text, int width) => (text ?? string.Empty) + new string(' ', Math.Max(0, width - VisibleLength(text)));

        /// <summary>Pads text on the left to <paramref name="width"/> visible columns.</summary>
        public static string PadLeft(string text, int width) => new string(' ', Math.Max(0, width - VisibleLength(text))) + (text ?? string.Empty);
    }

    /// <summary>How a table column lines up its cells.</summary>
    public enum CliAlign { Left, Right }

    /// <summary>
    /// The kscript command line's output, styled like Laravel's artisan: INFO / DONE / WARN / ERROR labels, headings,
    /// dotted two-column lines, tasks with their time, bullet lists and tables. Every command writes through it so the
    /// whole CLI looks the same, and KScript.Terminal gives scripts the same calls. Plain text (no colours, no wrapping)
    /// when the writer isn't a terminal, so tests, pipes and CI logs are stable.
    /// </summary>
    public sealed class CliOutput
    {
        public CliOutput(TextWriter writer, bool? colour = null, int? width = null)
        {
            Writer = writer ?? TextWriter.Null;
            Style = new CliStyle(colour ?? CliStyle.Supports(Writer));
            Width = Math.Max(40, width ?? DetectWidth(Style.Enabled));
        }

        public TextWriter Writer { get; }
        public CliStyle Style { get; }

        /// <summary>The terminal's width, or COLUMNS, or 80.</summary>
        public int Width { get; }

        /// <summary>True when long text is wrapped to <see cref="Width"/>: only at a terminal, so piped lines stay whole.</summary>
        public bool Wraps => Style.Enabled;

        private static int DetectWidth(bool terminal)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out int columns) && columns > 0) return columns;
            if (terminal)
            {
                try
                {
                    if (Console.WindowWidth > 0) return Console.WindowWidth;
                }
                catch (IOException) { }
                catch (PlatformNotSupportedException) { }
            }
            return 80;
        }

        #region Lines

        public void Line(string text = "") => Writer.WriteLine(text ?? string.Empty);
        public void NewLine() => Writer.WriteLine();

        /// <summary>A section heading: bold yellow, as artisan's.</summary>
        public void Heading(string text) => Line(Style.Apply(text, "1;33"));

        /// <summary>A quieter note, in gray.</summary>
        public void Comment(string text) => Line(Style.Gray(text));

        #endregion

        #region Labels

        public const string InfoCode = "1;97;44";
        public const string DoneCode = "1;97;42";
        public const string WarnCode = "1;30;43";
        public const string ErrorCode = "1;97;41";

        /// <summary>"   INFO  message": what happened, or what to do next.</summary>
        public void Info(string message) => Label("INFO", InfoCode, message);

        /// <summary>"   DONE  message": something finished well.</summary>
        public void Success(string message) => Label("DONE", DoneCode, message);

        /// <summary>"   WARN  message".</summary>
        public void Warn(string message) => Label("WARN", WarnCode, message);

        /// <summary>"   ERROR  message".</summary>
        /// <summary>
        /// The ERROR label. Pass <paramref name="format"/> false for text that must stay as written, such as an error
        /// report with a code frame: it isn't wrapped and [brackets] aren't made bold.
        /// </summary>
        public void Error(string message, bool format = true) => Label("ERROR", ErrorCode, message, format);

        /// <summary>A label of any name and SGR code, then the message; later lines line up under the first.</summary>
        public void Label(string name, string code, string message, bool format = true)
        {
            string label = "  " + Style.Apply($" {name} ", code) + " ";
            string indent = new string(' ', name.Length + 5);
            string[] lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0 && lines[i].Length == 0)
                {
                    sb.Append('\n');
                    continue;
                }
                foreach (string part in format ? Wrap(lines[i], Width - indent.Length) : new[] { lines[i] })
                {
                    sb.Append(sb.Length == 0 ? label : indent).Append(format ? Highlight(part) : part).Append('\n');
                }
            }
            Writer.Write(sb.ToString().Replace("\n", Writer.NewLine));
            Writer.Flush();
        }

        /// <summary>[bracketed] parts of a label's message (paths, names, URLs) are shown bold, as artisan does.</summary>
        private string Highlight(string text)
        {
            if (!Style.Enabled || text.IndexOf('[') < 0) return text;
            return Regex.Replace(text, @"\[([^\[\]\s][^\[\]]*)\]", m => Style.Bold(m.Value));
        }

        #endregion

        #region Two columns, tasks and lists

        /// <summary>"  left ........... right": a name and its value, filling the line with dots.</summary>
        public void TwoColumn(string left, string right = null)
        {
            right ??= string.Empty;
            int dots = Width - 4 - CliStyle.VisibleLength(left) - CliStyle.VisibleLength(right) - (right.Length > 0 ? 1 : 0);
            Line($"  {left} {Style.Gray(new string('.', Math.Max(2, dots)))}{(right.Length > 0 ? " " + right : string.Empty)}");
        }

        /// <summary>
        /// Runs <paramref name="work"/> as "  description ....... 12ms DONE" (or FAIL when it returns false or throws;
        /// an exception is passed on after the line is finished).
        /// </summary>
        public bool Task(string description, Func<bool> work)
        {
            var clock = Stopwatch.StartNew();
            // At a terminal the description shows while the work runs; elsewhere the line is written once.
            if (Style.Enabled) Writer.Write($"  {description} ");
            bool ok = false;
            try
            {
                ok = work();
                return ok;
            }
            finally
            {
                FinishTask(description, ok, clock.Elapsed, started: Style.Enabled);
            }
        }

        /// <summary>A task line for work that has already run.</summary>
        public void TaskLine(string description, bool ok, TimeSpan elapsed) => FinishTask(description, ok, elapsed, started: false);

        private void FinishTask(string description, bool ok, TimeSpan elapsed, bool started)
        {
            string time = elapsed.TotalMilliseconds < 1000 ? $"{Math.Max(0, (int)elapsed.TotalMilliseconds)}ms" : $"{elapsed.TotalSeconds:0.0}s";
            string status = ok ? Style.Apply("DONE", "1;32") : Style.Apply("FAIL", "1;31");
            int dots = Width - 6 - CliStyle.VisibleLength(description) - time.Length - 4;
            string tail = $"{Style.Gray(new string('.', Math.Max(2, dots)))} {Style.Gray(time)} {status}";
            if (started) Writer.WriteLine(tail);
            else Line($"  {description} {tail}");
        }

        /// <summary>"  ⇂ item" lines.</summary>
        public void Bullets(IEnumerable<string> items)
        {
            foreach (string item in items) Line($"  {Style.Gray("⇂")} {item}");
        }

        /// <summary>
        /// A list of names and what they are, like artisan's command list: names in green, padded to the longest, and
        /// descriptions wrapped (at a terminal) with a hanging indent.
        /// </summary>
        public void Definitions(IEnumerable<(string Name, string Description)> items, int indent = 2, int? nameWidth = null)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            int width = nameWidth ?? list.Max(i => CliStyle.VisibleLength(i.Name));
            string pad = new string(' ', indent);
            string hanging = new string(' ', indent + width + 2);
            foreach (var (name, description) in list)
            {
                var parts = Wrap(description ?? string.Empty, Width - hanging.Length).ToList();
                Line((pad + CliStyle.Pad(Style.Green(name), width) + "  " + parts[0]).TrimEnd());
                foreach (string more in parts.Skip(1)) Line(hanging + more);
            }
        }

        /// <summary>Splits text into lines of at most <paramref name="width"/> columns, at spaces; only at a terminal.</summary>
        public IEnumerable<string> Wrap(string text, int width)
        {
            if (!Wraps || width < 20 || CliStyle.VisibleLength(text) <= width)
            {
                yield return text;
                yield break;
            }
            var line = new StringBuilder();
            int length = 0;
            foreach (string word in text.Split(' '))
            {
                int w = CliStyle.VisibleLength(word);
                if (length > 0 && length + 1 + w > width)
                {
                    yield return line.ToString();
                    line.Clear();
                    length = 0;
                }
                if (length > 0) { line.Append(' '); length++; }
                line.Append(word);
                length += w;
            }
            yield return line.ToString();
        }

        #endregion

        #region Tables

        /// <summary>
        /// A table with box borders and bold headers. Cells may hold styled text; widths count visible columns only.
        /// <paramref name="align"/> gives each column's alignment (left by default).
        /// </summary>
        public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, IReadOnlyList<CliAlign> align = null)
        {
            Writer.Write(RenderTable(headers, rows, align).Replace("\n", Writer.NewLine));
        }

        /// <summary>The text of <see cref="Table"/>, with "\n" line ends.</summary>
        public string RenderTable(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, IReadOnlyList<CliAlign> align = null)
        {
            List<IReadOnlyList<string>> all = rows.ToList();
            int columns = Math.Max(headers.Count, all.Count == 0 ? 0 : all.Max(r => r.Count));
            string Cell(IReadOnlyList<string> row, int i) => i < row.Count ? (row[i] ?? string.Empty).Replace("\n", " ") : string.Empty;
            int[] widths = Enumerable.Range(0, columns)
                .Select(i => Math.Max(CliStyle.VisibleLength(Cell(headers, i)), all.Count == 0 ? 0 : all.Max(r => CliStyle.VisibleLength(Cell(r, i)))))
                .ToArray();

            var sb = new StringBuilder();
            string Border(char left, char middle, char right) =>
                "  " + Style.Gray(left + string.Join(middle.ToString(), widths.Select(w => new string('─', w + 2))) + right);
            void Row(IReadOnlyList<string> cells, bool header)
            {
                sb.Append("  ").Append(Style.Gray("│"));
                for (int i = 0; i < columns; i++)
                {
                    string text = Cell(cells, i);
                    if (header) text = Style.Apply(text, "1;32");
                    bool right = !header && align != null && i < align.Count && align[i] == CliAlign.Right;
                    sb.Append(' ').Append(right ? CliStyle.PadLeft(text, widths[i]) : CliStyle.Pad(text, widths[i])).Append(' ').Append(Style.Gray("│"));
                }
                sb.Append('\n');
            }

            sb.Append(Border('┌', '┬', '┐')).Append('\n');
            Row(headers, header: true);
            sb.Append(Border('├', '┼', '┤')).Append('\n');
            foreach (var row in all) Row(row, header: false);
            sb.Append(Border('└', '┴', '┘')).Append('\n');
            return sb.ToString();
        }

        #endregion
    }
}
