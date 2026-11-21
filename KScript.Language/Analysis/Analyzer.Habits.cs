using System.Text.RegularExpressions;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Habits from other languages. Someone new to KScript writes <c>print "hi"</c>, <c>x++</c> or
    /// <c>@len($list)</c> because that is what they know, so instead of "unknown" these say what KScript calls it and
    /// offer the change as a quick fix. Also the last step for a .kscript file: messages written for the XML form are
    /// put in the file's own words.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>Words other languages print with.</summary>
        private static readonly HashSet<string> PrintWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "print", "println", "printf", "puts", "say", "console.log", "console.writeline", "system.out.println", "print_r", "var_dump", "writeln",
        };

        /// <summary>Keywords of other languages, with KScript's word for the same thing and what it looks like.</summary>
        private static readonly Dictionary<string, (string Use, string Example)> ForeignKeywords = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["foreach"] = ("for", "for item in $list { }"),
            ["switch"] = ("match", "match $value { case 1 { } default { } }"),
            ["elseif"] = ("elif", "} elif $n > 1 { }"),
            ["elsif"] = ("elif", "} elif $n > 1 { }"),
            ["require"] = ("import", "import \"lib/tools.kscript\""),
            ["include"] = ("import", "import \"lib/tools.kscript\""),
            ["using"] = ("import", "import KScript.IO"),
            ["raise"] = ("throw", "throw Validation \"Age must be positive\""),
            ["except"] = ("catch", "} catch err { }"),
            ["rescue"] = ("catch", "} catch err { }"),
            ["class"] = ("type", "type Person { name: string, age: int }"),
            ["struct"] = ("type", "type Person { name: string, age: int }"),
            ["function"] = ("fn", "fn greet(who) { }"),
            ["def"] = ("fn", "fn greet(who) { }"),
            ["func"] = ("fn", "fn greet(who) { }"),
        };

        /// <summary>Commands other languages have, and what to write in KScript.</summary>
        private static readonly Dictionary<string, string> ForeignCommands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["len"] = "Use the length method: $list->length().",
            ["length"] = "Use the length method: $list->length().",
            ["size"] = "Use the length method: $list->length().",
            ["count"] = "Use the count method: $list->count().",
            ["strlen"] = "Use the length method: $text->length().",
            ["print"] = "Print with echo: echo \"Hello\".",
            ["println"] = "Print with echo: echo \"Hello\".",
            ["typeof"] = "Use @type($value), or $value->type().",
            ["str"] = "Use @string($value), or $value->to_string().",
            ["tostring"] = "Use @string($value), or $value->to_string().",
            ["int"] = "Use @number($value).",
            ["float"] = "Use @number($value).",
            ["parseint"] = "Use @number($value).",
            ["parsefloat"] = "Use @number($value).",
            ["upper"] = "Use the upper method: $text->upper().",
            ["lower"] = "Use the lower method: $text->lower().",
            ["strtoupper"] = "Use the upper method: $text->upper().",
            ["strtolower"] = "Use the lower method: $text->lower().",
        };

        /// <summary>What to write instead of a command KScript doesn't have but other languages do, or null.</summary>
        private string ForeignCommandHint(string name) =>
            catalog.GetCommand(name) == null && ForeignCommands.TryGetValue(name, out string hint) ? hint : null;

        /// <summary>
        /// A statement KScript doesn't have that reads like another language. Reports it in KScript's terms, drops the
        /// parser's knock-on errors on that line, and returns true.
        /// </summary>
        private bool ReportHabit(MarkupElement e)
        {
            string text = source.Text;
            int lineEnd = text.IndexOf('\n', e.NameSpan.End);
            lineEnd = lineEnd < 0 ? text.Length : lineEnd;
            string rest = text.Substring(e.NameSpan.End, lineEnd - e.NameSpan.End);
            string after = rest.TrimStart();
            int afterStart = e.NameSpan.End + (rest.Length - after.Length);
            string name = e.Name;

            if (after.StartsWith("++", StringComparison.Ordinal) || after.StartsWith("--", StringComparison.Ordinal))
            {
                string op = after[0] == '+' ? "+=" : "-=";
                ReportHabit(e, lineEnd, TextSpan.FromBounds(e.NameSpan.Start, afterStart + 2),
                    $"KScript has no {after.Substring(0, 2)}. Write {name} {op} 1.",
                    new QuickFix($"Change to {name} {op} 1", new TextChange(TextSpan.FromBounds(e.NameSpan.Start, afterStart + 2), $"{name} {op} 1")));
                return true;
            }

            if (after.StartsWith("==", StringComparison.Ordinal) && !PrintWords.Contains(name))
            {
                int length = after.StartsWith("===", StringComparison.Ordinal) ? 3 : 2;
                ReportHabit(e, lineEnd, TextSpan.FromBounds(e.NameSpan.Start, afterStart + length),
                    $"This compares {name} and does nothing with the answer. To give {name} a value, write {name} = ...; to test it, write if ${name} == ... {{ }}.",
                    new QuickFix($"Change {after.Substring(0, length)} to =", new TextChange(new TextSpan(afterStart, length), "=")));
                return true;
            }

            if (PrintWords.Contains(name) || Suggestions.Closest(name, new[] { "print", "println", "printf" }) != null)
            {
                List<TextChange> changes = new List<TextChange> { new TextChange(e.NameSpan, "echo") };
                string trimmed = rest.TrimEnd();
                if (after.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
                {
                    // print("hi") becomes echo "hi".
                    changes.Add(new TextChange(TextSpan.FromBounds(e.NameSpan.End, afterStart + 1), " "));
                    changes.Add(new TextChange(new TextSpan(e.NameSpan.End + trimmed.Length - 1, 1), string.Empty));
                }
                ReportHabit(e, lineEnd, e.NameSpan, $"KScript prints with echo, not {name}: echo \"Hello\".", new QuickFix("Change to echo", changes.ToArray()));
                return true;
            }

            if (ForeignKeywords.TryGetValue(name, out (string Use, string Example) keyword))
            {
                ReportHabit(e, lineEnd, e.NameSpan, $"KScript writes {keyword.Use}, not {name}: {keyword.Example}",
                    new QuickFix($"Change to {keyword.Use}", new TextChange(e.NameSpan, keyword.Use)));
                return true;
            }

            return false;
        }

        private void ReportHabit(MarkupElement e, int lineEnd, TextSpan span, string message, QuickFix fix)
        {
            // What the parser made of the rest of the line ("Put the next statement on a new line") is noise now.
            result.Diagnostics.RemoveAll(d => (d.Code == DiagnosticCodes.MarkupSyntax || d.Code == DiagnosticCodes.InvalidAttribute)
                && d.Span.Start >= e.Span.Start && d.Span.Start <= lineEnd);
            Report(span, DiagnosticSeverity.Error, DiagnosticCodes.UnknownElement, message, new[] { fix });
        }

        /// <summary>Text literals written as the argument of a call.</summary>
        private readonly HashSet<LiteralNode> argumentTexts = new HashSet<LiteralNode>();

        private static readonly Regex TemplatePart = new Regex(@"\$([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)|\{\s*([^{}]+?)\s*\}", RegexOptions.Compiled);

        /// <summary>
        /// <c>greeting = "Hello $name"</c>: text after echo is a template, but text used as a value is kept as written,
        /// so this prints "Hello $name". Warns when the text names a variable that exists, and offers the joined form.
        /// </summary>
        private void CheckTextIsNotATemplate(LiteralNode literal, ExpressionRegion region, Scope scope)
        {
            if (argumentTexts.Contains(literal) || literal.Kind != LiteralKind.String || string.IsNullOrEmpty(literal.Value) || literal.Value.IndexOf('$') < 0)
            {
                return;
            }
            TextSpan span = region.Source.ToSource(literal.Span);
            if (span.Length < 2 || span.End > source.Length || (source.Text[span.Start] != '"' && source.Text[span.Start] != '\''))
            {
                return;
            }

            List<string> names = new List<string>();
            foreach (Match m in TemplatePart.Matches(literal.Value))
            {
                string variable = m.Groups[1].Success ? m.Groups[1].Value : Regex.Match(m.Groups[2].Value, @"^\$([A-Za-z_]\w*)").Groups[1].Value;
                string root = variable.Split('.')[0];
                if (root.Length > 0 && scope.Lookup(root, span.Start) is Symbol s)
                {
                    names.Add(root);
                    result.References.Add(new SymbolReference(s, span, false, false));
                }
            }
            if (names.Count == 0)
            {
                return;
            }

            // Only offer the rewrite when the text is written exactly as it reads (no escapes or entities inside).
            string joined = span.Length == literal.Value.Length + 2 ? JoinedText(literal.Value) : null;
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.TextNotFilledIn,
                $"${names[0]} is not filled in here: only the text after echo is a template, so this stays as written."
                    + (joined != null ? $" Join the parts instead: {joined}." : " Join the parts with + instead."),
                joined != null ? new[] { new QuickFix($"Change to {joined}", new TextChange(span, joined)) } : null);
        }

        /// <summary><c>Hello $name!</c> as <c>'Hello ' + $name + '!'</c>, or null when the text can't be written in single quotes.</summary>
        private static string JoinedText(string value)
        {
            if (value.Contains('\'') || value.Contains('\\'))
            {
                return null;
            }
            List<string> parts = new List<string>();
            int at = 0;
            foreach (Match m in TemplatePart.Matches(value))
            {
                if (m.Index > at)
                {
                    parts.Add("'" + value.Substring(at, m.Index - at) + "'");
                }
                if (m.Groups[1].Success)
                {
                    parts.Add("$" + m.Groups[1].Value);
                }
                else
                {
                    string inner = m.Groups[2].Value;
                    parts.Add(Regex.IsMatch(inner, @"^\$[\w.]+(->\w+\([^()]*\))*$") ? inner : "(" + inner + ")");
                }
                at = m.Index + m.Length;
            }
            if (at < value.Length)
            {
                parts.Add("'" + value.Substring(at) + "'");
            }
            return string.Join(" + ", parts);
        }

        private static readonly Regex ElementInAngles = new Regex(@"(?<![\w<])</?([A-Za-z][\w.-]*)\s*/?>", RegexOptions.Compiled);

        /// <summary>
        /// The expression parser serves XML scripts, pages and .kscript files alike, and words a few messages for XML.
        /// In a .kscript file they are said in its terms.
        /// </summary>
        private void SpeakCode()
        {
            if (!IsCode)
            {
                return;
            }
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                Diagnostic d = result.Diagnostics[i];
                string message = d.Message
                    .Replace(" (a raw & isn't allowed in XML)", string.Empty, StringComparison.Ordinal)
                    .Replace("assign with <let>, <set> or <do>.", "assign on a line of its own: x = 1.", StringComparison.Ordinal)
                    .Replace("Assign with <set> or <do>.", "Assign on a line of its own: x += 1.", StringComparison.Ordinal);
                message = ElementInAngles.Replace(message, m => catalog.GetElement(m.Groups[1].Value) != null ? m.Groups[1].Value : m.Value);
                if (!ReferenceEquals(message, d.Message) && message != d.Message)
                {
                    result.Diagnostics[i] = new Diagnostic(d.Span, d.Severity, d.Code, message, d.Fixes);
                }
            }
        }
    }
}
