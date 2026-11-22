using KScript.Metadata;
using System.Text;

using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>The converted text, and anything that couldn't be carried over exactly.</summary>
    public sealed class ConversionResult
    {
        public string Text { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Converts scripts between XML (<c>.ks.xml</c>) and the brace-and-newline format (<c>.kscript</c>). Both parse to
    /// the same element tree, so conversion prints that tree in the other syntax. Expressions are copied as written;
    /// comments and doc comments are kept. Where one meaning has two spellings (<c>&lt;let&gt;x = 3&lt;/let&gt;</c> and
    /// <c>&lt;let name="x" value="3"/&gt;</c>) the printer picks one, so converting back gives tidy, stable text.
    /// </summary>
    public static class ScriptConverter
    {
        /// <summary>Converts a file's text to the other format, chosen by its path (.kscript becomes XML, anything else .kscript).</summary>
        public static ConversionResult Convert(string text, string path, Func<string, string> importPath = null, Func<string, string, bool> nameList = null, Func<string, ElementInfo> elements = null)
        {
            if (MarkupParser.IsCodePath(path))
            {
                return ToXml(CodeParser.Parse(text), importPath, nameList, elements);
            }
            return ToCode(MarkupParser.Parse(text, DocumentKind.Script), importPath, nameList, elements);
        }

        /// <summary>The path a converted file gets: x.ks.xml or x.xml becomes x.kscript, and x.kscript becomes x.ks.xml.</summary>
        public static string ConvertedPath(string path)
        {
            if (MarkupParser.IsCodePath(path))
            {
                return path.Substring(0, path.Length - ".kscript".Length) + ".ks.xml";
            }
            foreach (string ext in new[] { ".ks.xml", ".xml", ".ks" })
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(0, path.Length - ext.Length) + ".kscript";
                }
            }
            return path + ".kscript";
        }

        /// <param name="importPath">
        /// Rewrites the <c>path</c> of each &lt;import&gt; (given as written, trimmed); null or a function that returns
        /// its input keeps every import as it is. <c>kscript convert</c> uses it to point imports at converted files.
        /// </param>
        /// <param name="nameList">
        /// True for an element's attribute that holds a list of names (<c>&lt;Middleware use&gt;</c>), such as
        /// LanguageCatalog.IsNameList. Such lists are written <c>[a, b]</c> in
        /// .kscript and <c>"a, b"</c> in XML. Null leaves every value as written.
        /// </param>
        public static ConversionResult ToCode(MarkupDocument document, Func<string, string> importPath = null, Func<string, string, bool> nameList = null, Func<string, ElementInfo> elements = null)
        {
            elements ??= Elements;
            var writer = new CodeWriter { ImportPath = importPath ?? (s => s), NameList = nameList ?? NameListFrom(elements), Elements = elements };
            writer.WriteDocument(document);
            return writer.Result;
        }

        public static ConversionResult ToXml(MarkupDocument document, Func<string, string> importPath = null, Func<string, string, bool> nameList = null, Func<string, ElementInfo> elements = null)
        {
            elements ??= Elements;
            // What each argument means depends on the element, so read them by the one rule before printing.
            if (elements != null) CodeArguments.Normalise(document, elements);
            var writer = new XmlWriter { ImportPath = importPath ?? (s => s), NameList = nameList ?? NameListFrom(elements), Elements = elements };
            writer.WriteDocument(document);
            return writer.Result;
        }

        private static Func<string, ElementInfo> elementsHook;
        private static bool elementsLooked;

        /// <summary>
        /// Describes each element by name (the language catalog), so values are written the way each argument is
        /// read: text in quotes, values and names bare, lists as arrays, handlers in braces. KScript.Language sets it;
        /// when it hasn't been set, the catalog is looked up the first time it is needed.
        /// </summary>
        public static Func<string, ElementInfo> Elements
        {
            get
            {
                if (elementsHook == null && !elementsLooked)
                {
                    elementsLooked = true;
                    try
                    {
                        Type catalog = Type.GetType("KScript.Language.Catalog.LanguageCatalog, KScript.Language");
                        object instance = catalog?.GetProperty("Default")?.GetValue(null);
                        System.Reflection.MethodInfo get = catalog?.GetMethod("GetElement", new[] { typeof(string) });
                        if (instance != null && get != null) elementsHook = name => (ElementInfo)get.Invoke(instance, new object[] { name });
                    }
                    catch (Exception)
                    {
                        // No catalog here (the syntax library on its own): values are copied as written.
                    }
                }
                return elementsHook;
            }
            set => elementsHook = value;
        }

        private static Func<string, string, bool> NameListFrom(Func<string, ElementInfo> elements) =>
            elements == null ? (_, _) => false : (element, attribute) => elements(ElementInfo.BareName(element))?.GetAttribute(attribute)?.IsNameList == true;

        #region Shared

        private static readonly HashSet<string> BlockStatements = new HashSet<string>(StringComparer.Ordinal)
        {
            "kscript", "let", "set", "do", "echo", "if", "elif", "else", "for", "while", "match", "case", "default", "fn", "return",
            "call", "try", "catch", "finally", "throw", "import", "exit", "break", "continue",
        };

        internal static bool IsYes(string value) => value != null && (value.Trim() == "yes" || value.Trim() == "true");

        /// <summary>Text content of an element when it holds only text (comments ignored), else null.</summary>
        internal static string TextOf(MarkupElement e)
        {
            if (e.ChildElements.Any())
            {
                return null;
            }
            List<MarkupText> texts = e.Children.OfType<MarkupText>().ToList();
            if (texts.Count == 0)
            {
                return null;
            }
            return string.Concat(texts.Select(t => t.Value));
        }

        /// <summary>Drops layout whitespace (that holds a new line) from both ends of element text.</summary>
        internal static string TrimLayout(string text)
        {
            if (text == null)
            {
                return null;
            }
            int start = 0;
            int end = text.Length;
            int firstNonSpace = 0;
            while (firstNonSpace < text.Length && char.IsWhiteSpace(text[firstNonSpace]))
            {
                firstNonSpace++;
            }
            if (text.Substring(0, firstNonSpace).Contains('\n'))
            {
                start = firstNonSpace;
            }
            int lastNonSpace = text.Length;
            while (lastNonSpace > start && char.IsWhiteSpace(text[lastNonSpace - 1]))
            {
                lastNonSpace--;
            }
            if (text.Substring(lastNonSpace).Contains('\n'))
            {
                end = lastNonSpace;
            }
            return text.Substring(start, Math.Max(0, end - start));
        }

        /// <summary>Removes the common indentation of a multi-line value's later lines and indents them to <paramref name="indent"/>.</summary>
        internal static string Reindent(string value, string indent)
        {
            string[] lines = value.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 1)
            {
                return value;
            }
            int common = lines.Skip(1).Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            // A continuation (|> map(...)) is indented one more step; the inside of an open bracket already is.
            string first = lines[0].TrimEnd();
            if (first.Length > 0 && !"[{(".Contains(first[first.Length - 1]))
            {
                indent += "  ";
            }
            return string.Join("\n", new[] { lines[0] }.Concat(lines.Skip(1).Select(l =>
                l.Trim().Length == 0 ? string.Empty : indent + (l.Length >= common ? l.Substring(common) : l.TrimStart()))));
        }

        /// <summary>Drops blank first and last lines and the indentation all lines share.</summary>
        internal static List<string> DedentAll(string text)
        {
            List<string> lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            int common = lines.Where(l => l.Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return lines.Select(l => l.Length >= common ? l.Substring(common) : l).ToList();
        }

        /// <summary>The names a params attribute declares: "who, greeting = 'Hi', count: int" gives who, greeting, count.</summary>
        internal static IEnumerable<string> ParameterNames(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters))
            {
                yield break;
            }
            foreach (string part in SplitTopLevel(parameters))
            {
                string name = part.Trim().TrimStart('$');
                int cut = name.IndexOfAny(new[] { ':', '=', ' ' });
                yield return (cut < 0 ? name : name.Substring(0, cut)).Trim().TrimStart('.');
            }
        }

        internal static List<string> SplitTopLevel(string text)
        {
            var parts = new List<string>();
            int depth = 0;
            char quote = '\0';
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (Quotes.IsEscape(text, i, text.Length, quote))
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                switch (c)
                {
                    case '\'':
                    case '"':
                        quote = c;
                        break;
                    case '(':
                    case '[':
                    case '{':
                        depth++;
                        break;
                    case ')':
                    case ']':
                    case '}':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        parts.Add(text.Substring(start, i - start));
                        start = i + 1;
                        break;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        /// <summary>Lexical scopes of variable names, to tell a first assignment (a declaration) from a change.</summary>
        private static readonly System.Text.RegularExpressions.Regex FieldPart =
            new System.Text.RegularExpressions.Regex(@"^[A-Za-z_]\w*\??(\s*:\s*[A-Za-z_][\w.]*(\[\])?\??)?(\s*=\s*\S.*)?$", System.Text.RegularExpressions.RegexOptions.Singleline);

        /// <summary>
        /// The parts of a one-line <c>fields</c> attribute as field lines (<c>login: string</c>, <c>tags: string[] = []</c>),
        /// or null when there is nothing to split or a part isn't a plain field.
        /// </summary>
        internal static List<string> FieldsAsLines(string fields)
        {
            if (string.IsNullOrWhiteSpace(fields))
            {
                return null;
            }
            var parts = new List<string>();
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i < fields.Length; i++)
            {
                char c = fields[i];
                if (quote != '\0')
                {
                    if (Quotes.IsEscape(fields, i, fields.Length, quote)) i++;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c is '\'' or '"') quote = c;
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(fields.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            parts.Add(fields.Substring(start).Trim());
            return quote == '\0' && depth == 0 && parts.All(p => p.Length > 0 && !p.Contains('\n') && FieldPart.IsMatch(p)) ? parts : null;
        }

        private sealed class Scopes
        {
            private readonly List<HashSet<string>> stack = new List<HashSet<string>> { new HashSet<string>(StringComparer.Ordinal) };

            public void Push() => stack.Add(new HashSet<string>(StringComparer.Ordinal));

            public void Pop() => stack.RemoveAt(stack.Count - 1);

            public void Declare(string name)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    stack[stack.Count - 1].Add(name.Trim().TrimStart('$'));
                }
            }

            public bool IsVisible(string name) => stack.Any(s => s.Contains(name.Trim().TrimStart('$')));

            public bool IsInOuterScope(string name) => stack.Take(stack.Count - 1).Any(s => s.Contains(name.Trim().TrimStart('$')));
        }

        #endregion

        #region XML to .kscript

        private sealed class CodeWriter
        {
            public Func<string, string> ImportPath { get; init; }

            /// <summary>True for an element's attribute that holds a list of names.</summary>
            public Func<string, string, bool> NameList { get; init; }

            /// <summary>Describes an element by name, or null when nothing is known about elements.</summary>
            public Func<string, ElementInfo> Elements { get; init; }

            private readonly StringBuilder sb = new StringBuilder();
            private readonly Scopes scopes = new Scopes();

            public ConversionResult Result { get; } = new ConversionResult();

            private SourceText source;

            public void WriteDocument(MarkupDocument document)
            {
                source = document.Source;
                foreach (MarkupNode node in document.Children)
                {
                    switch (node)
                    {
                        case MarkupComment c:
                            WriteComment(c, 0);
                            break;
                        case MarkupElement e when e.Name == "kscript":
                            WriteBody(e.Children, 0);
                            break;
                        case MarkupElement e:
                            WriteElement(e, 0, chained: false);
                            break;
                    }
                }
                // XML's own spellings (lt, gt, eq...) are written the .kscript way.
                Result.Text = CodeSpellings.Normalize(sb.ToString().TrimEnd('\n') + "\n");
            }

            private static string Pad(int indent) => new string(' ', indent * 2);

            private void Line(int indent, string text) => sb.Append(Pad(indent)).Append(text).Append('\n');

            private void WriteBody(IReadOnlyList<MarkupNode> nodes, int indent)
            {
                MarkupElement previous = null;
                int blankRuns = 0;
                for (int i = 0; i < nodes.Count; i++)
                {
                    switch (nodes[i])
                    {
                        case MarkupText t when t.IsWhitespace:
                            // Keep one blank line where the XML had blank lines between statements.
                            if (previous != null && t.Value.Count(ch => ch == '\n') > 1 && i + 1 < nodes.Count)
                            {
                                blankRuns++;
                            }
                            break;
                        case MarkupText t:
                            Result.Warnings.Add($"Text outside an element was kept as a comment: \"{t.Value.Trim()}\".");
                            Line(indent, "// " + t.Value.Trim().Replace("\n", " "));
                            break;
                        case MarkupComment c:
                            if (c.IsDoc && NextElement(nodes, i) is MarkupElement documented && documented.GetAttribute("description") == null)
                            {
                                break;
                            }
                            if (!c.IsDoc && IsTrailing(nodes, i) && sb.Length > 0 && sb[sb.Length - 1] == '\n')
                            {
                                // <let ... />  <!-- note --> stays on its line: count = 0 // note
                                sb.Length--;
                                sb.Append(" // ").Append(XmlExamples(c.Text.Trim())).Append('\n');
                                break;
                            }
                            FlushBlank(ref blankRuns);
                            WriteComment(c, indent);
                            previous = null;
                            break;
                        case MarkupElement e:
                            bool chained = previous != null && sb.Length > 0 && sb[sb.Length - 1] == '\n' && sb.ToString().TrimEnd('\n').EndsWith("}", StringComparison.Ordinal)
                                && ((e.Name == "elif" && previous.Name is "if" or "elif")
                                    || (e.Name == "else" && previous.Name is "if" or "elif" or "for"));
                            if (chained)
                            {
                                sb.Length--;
                                sb.Append(' ');
                                blankRuns = 0;
                            }
                            else
                            {
                                FlushBlank(ref blankRuns);
                            }
                            WriteElement(e, indent, chained);
                            previous = e;
                            break;
                    }
                }
            }

            private void FlushBlank(ref int blankRuns)
            {
                if (blankRuns > 0 && sb.Length > 0 && !sb.ToString().EndsWith("{\n", StringComparison.Ordinal) && !sb.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                {
                    sb.Append('\n');
                }
                blankRuns = 0;
            }

            /// <summary>A one-line comment written after an element on the same line.</summary>
            private static bool IsTrailing(IReadOnlyList<MarkupNode> nodes, int index)
            {
                if (nodes[index] is not MarkupComment c || c.Text.Contains('\n') || c.Text.Trim().Length == 0) return false;
                int before = index - 1;
                if (before >= 0 && nodes[before] is MarkupText space && space.IsWhitespace && !space.Value.Contains('\n')) before--;
                return before >= 0 && before < index && nodes[before] is MarkupElement;
            }

            private static MarkupElement NextElement(IReadOnlyList<MarkupNode> nodes, int index)
            {
                for (int j = index + 1; j < nodes.Count; j++)
                {
                    if (nodes[j] is MarkupText t && t.IsWhitespace)
                    {
                        continue;
                    }
                    return nodes[j] as MarkupElement;
                }
                return null;
            }

            private void WriteComment(MarkupComment c, int indent)
            {
                if (c.IsBlock && !c.Text.Contains("*/", StringComparison.Ordinal))
                {
                    List<string> lines = c.Text.Split('\n').Select(XmlExamples).ToList();
                    if (lines.Count == 1)
                    {
                        Line(indent, "/* " + lines[0] + " */");
                        return;
                    }
                    Line(indent, "/*");
                    foreach (string line in lines)
                    {
                        if (line.Length == 0) sb.Append('\n'); else Line(indent, "  " + line);
                    }
                    Line(indent, "*/");
                    return;
                }
                string prefix = c.IsDoc ? "///" : "//";
                foreach (string line in c.IsDoc ? c.DocText.Split('\n').ToList() : DedentAll(c.Text))
                {
                    string converted = XmlExamples(line);
                    Line(indent, converted.Length == 0 ? prefix : prefix + " " + converted);
                }
            }

            private static readonly System.Text.RegularExpressions.Regex XmlExample =
                new System.Text.RegularExpressions.Regex(@"<[a-z][\w.\-]*(?:\s+[\w\-:]+=""[^""<>]*"")*\s*/>");

            /// <summary>
            /// One-line XML examples in a comment (<c>&lt;import path="lib/x.ks.xml" /&gt;</c>) are written the .kscript
            /// way (<c>import "lib/x.ks.xml"</c>); anything that doesn't convert to one line is kept as written.
            /// </summary>
            private string XmlExamples(string line) => XmlExample.Replace(line, m =>
            {
                MarkupDocument doc = MarkupParser.Parse("<kscript>" + m.Value + "</kscript>", DocumentKind.Script);
                if (doc.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    return m.Value;
                }
                var writer = new CodeWriter { ImportPath = ImportPath, NameList = NameList, Elements = Elements };
                writer.WriteDocument(doc);
                string text = writer.Result.Text.Trim();
                return text.Length == 0 || text.Contains('\n') ? m.Value : text;
            });

            private void WriteDoc(MarkupElement e, int indent)
            {
                string doc = e.Documentation;
                if (doc == null)
                {
                    return;
                }
                foreach (string line in doc.Split('\n'))
                {
                    Line(indent, line.Length == 0 ? "///" : "/// " + line);
                }
            }

            private void WriteElement(MarkupElement e, int indent, bool chained)
            {
                string pad = chained ? string.Empty : Pad(indent);
                string Attr(string name) => e.GetAttributeValue(name);
                bool Only(params string[] names) => e.Attributes.All(a => names.Contains(a.Name));

                switch (e.Name)
                {
                    case "let" or "const" when Only("name", "value", "const", "type", "description") && !e.ChildElements.Any():
                        {
                            string name = Attr("name")?.Trim();
                            string value = Attr("value") ?? TrimLayout(TextOf(e))?.Trim();
                            if (name == null)
                            {
                                WriteStatements(value, indent, declare: true);
                                return;
                            }
                            bool isConst = e.Name == "const" || IsYes(Attr("const"));
                            if (!isConst && scopes.IsInOuterScope(name))
                            {
                                Result.Warnings.Add($"<let name=\"{name}\"> declared a new ${name} inside a block, hiding the outer one. In .kscript, {name} = ... changes the outer ${name} instead.");
                            }
                            WriteDoc(e, indent);
                            string type = Attr("type") != null ? ": " + Attr("type").Trim() : string.Empty;
                            sb.Append(pad).Append(isConst ? "const " : string.Empty).Append(name).Append(type).Append(" = ")
                                .Append(value != null ? Reindent(value.Trim(), Pad(indent)) : "null").Append('\n');
                            scopes.Declare(name);
                            return;
                        }
                    case "set" when Only("name", "value", "op") && !e.ChildElements.Any():
                        {
                            string name = Attr("name")?.Trim().TrimStart('$');
                            string value = Attr("value") ?? TrimLayout(TextOf(e))?.Trim();
                            if (name == null)
                            {
                                WriteStatements(value, indent, declare: false);
                                return;
                            }
                            sb.Append(pad).Append(name).Append(' ').Append(Attr("op")?.Trim() ?? "=").Append(' ')
                                .Append(Reindent(value ?? "null", Pad(indent))).Append('\n');
                            return;
                        }
                    case "do" when e.Attributes.Count == 0 && !e.ChildElements.Any():
                        WriteStatements(TrimLayout(TextOf(e))?.Trim(), indent, declare: false);
                        return;
                    case "echo" when Only("value", "format", "newline") && !e.ChildElements.Any():
                        {
                            string options = string.Join(", ", e.Attributes.Where(a => a.Name != "value").Select(a => FormatAttribute(e, a)));
                            sb.Append(pad).Append("echo").Append(options.Length > 0 ? "(" + options + ")" : string.Empty);
                            string text = TrimLayout(TextOf(e));
                            if (Attr("value") is string v)
                            {
                                sb.Append(' ').Append(Reindent(v.Trim(), Pad(indent)));
                            }
                            else if (!string.IsNullOrEmpty(text))
                            {
                                sb.Append(' ').Append(Quote(text));
                            }
                            sb.Append('\n');
                            return;
                        }
                    case "if" when Only("test"):
                    case "elif" when Only("test"):
                    case "while" when Only("test"):
                        WriteBlock(e.Name + " " + Attr("test")?.Trim(), e, indent, chained);
                        return;
                    case "else" when e.Attributes.Count == 0:
                        WriteBlock("else", e, indent, chained);
                        return;
                    case "for" when Only("each", "in", "at") && Attr("each") != null && Attr("in") != null:
                        {
                            scopes.Push();
                            foreach (string v in Attr("each").Split(','))
                            {
                                scopes.Declare(v);
                            }
                            scopes.Declare(Attr("at"));
                            string header = "for " + string.Join(", ", Attr("each").Split(',').Select(x => x.Trim().TrimStart('$')))
                                + (Attr("at") != null ? " at " + Attr("at").Trim().TrimStart('$') : string.Empty) + " in " + Attr("in").Trim();
                            WriteBlock(header, e, indent, chained, pushScope: false);
                            scopes.Pop();
                            return;
                        }
                    case "match" when Only("value") && e.ChildElements.All(c => c.Name is "case" or "default"):
                        {
                            sb.Append(pad).Append("match ").Append(Attr("value")?.Trim()).Append(" {\n");
                            foreach (MarkupNode node in e.Children)
                            {
                                if (node is MarkupComment c)
                                {
                                    WriteComment(c, indent + 1);
                                }
                                else if (node is MarkupElement branch)
                                {
                                    WriteArm(branch, indent + 1);
                                }
                            }
                            Line(indent, "}");
                            return;
                        }
                    case "extend" when Only("type", "description") && Attr("type") is string extended && IsName(extended.Trim())
                        && e.Children.All(n => n is MarkupComment || (n is MarkupText t && t.IsWhitespace) || n is MarkupElement { Name: "fn" }):
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            WriteBlock("extend " + extended.Trim(), e, indent, chained);
                            return;
                        }
                    case "fn" when Only("name", "params", "value", "description", "override") && Attr("name") != null
                        && (Attr("override") == null || (e.Parent?.Name == "extend" && IsYes(Attr("override")))):
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            string header = (Attr("override") != null ? "override fn " : "fn ") + Attr("name").Trim() + "(" + (Attr("params")?.Trim() ?? string.Empty) + ")";
                            if (Attr("value") is string value && !e.ChildElements.Any())
                            {
                                sb.Append(pad).Append(header).Append(" => ").Append(Reindent(value.Trim(), Pad(indent))).Append('\n');
                                return;
                            }
                            scopes.Push();
                            foreach (string p in ParameterNames(Attr("params")))
                            {
                                scopes.Declare(p);
                            }
                            WriteBlock(header, e, indent, chained, pushScope: false);
                            scopes.Pop();
                            return;
                        }
                    case "enum" when Only("name", "values", "description") && Attr("name") is string enumName && IsName(enumName.Trim()) && !enumName.Contains('.') && !e.ChildElements.Any() && (Attr("values") ?? string.Empty).All(c => c != '}'):
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            string values = string.Join(", ", (Attr("values") ?? string.Empty).Split(',').Select(v => v.Trim()).Where(v => v.Length > 0));
                            Line(indent, "enum " + enumName.Trim() + (values.Length > 0 ? " { " + values + " }" : " { }"));
                            return;
                        }
                    case "type" or "model" when Attr("name") is string typeName && IsName(typeName.Trim()) && !typeName.Contains('.') && e.Children.All(IsTypeLine)
                        && TypeRuleLines(e) is List<string> rules:
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            // fields="login: string, id: int" becomes one line per field, the way .kscript writes types.
                            List<string> fieldLines = FieldsAsLines(Attr("fields"));
                            // A model's settings are lines too: table: people, unique: [email].
                            bool model = e.Name == "model";
                            if (model)
                            {
                                rules.InsertRange(0, ModelSettingLines(e));
                            }
                            string options = string.Join(", ", e.Attributes.Where(a => a.Name is not ("name" or "description" or "with") && !RuleAttributes.Contains(a.Name)
                                && !(model && CodeParser.ModelSettings.Contains(a.Name))
                                && !(a.Name == "fields" && fieldLines != null)).Select(a => FormatAttribute(e, a)));
                            sb.Append(pad).Append(e.Name).Append(' ').Append(typeName.Trim()).Append(options.Length > 0 ? "(" + options + ")" : string.Empty);
                            if (Attr("with") is string with)
                            {
                                // with="User, Auditable" is with [User, Auditable].
                                string bases = with.Trim().TrimStart('[').TrimEnd(']');
                                sb.Append(" with [").Append(string.Join(", ", bases.Split(',').Select(b => b.Trim()).Where(b => b.Length > 0))).Append(']');
                            }
                            if (!e.ChildElements.Any() && rules.Count == 0 && fieldLines == null)
                            {
                                sb.Append('\n');
                                return;
                            }
                            // A refinement type with only short rules fits on one line: type Age with [int] { min: 0; max: 150 }.
                            if (!e.ChildElements.Any() && fieldLines == null && !e.Children.OfType<MarkupComment>().Any() && rules.Count <= 3 && rules.Sum(r => r.Length) <= 60)
                            {
                                sb.Append(" { ").Append(string.Join("; ", rules)).Append(" }\n");
                                return;
                            }
                            sb.Append(" {\n");
                            foreach (string rule in rules)
                            {
                                Line(indent + 1, rule);
                            }
                            foreach (string fieldLine in fieldLines ?? new List<string>())
                            {
                                Line(indent + 1, fieldLine);
                            }
                            scopes.Push();
                            foreach (MarkupNode node in e.Children)
                            {
                                if (node is MarkupComment c)
                                {
                                    WriteComment(c, indent + 1);
                                }
                                else if (node is MarkupElement child && child.Name == "field")
                                {
                                    WriteField(child, indent + 1);
                                }
                                else if (node is MarkupElement check && check.Name == "validate")
                                {
                                    WriteDoc(check, indent + 1);
                                    string message = check.GetAttributeValue("message");
                                    Line(indent + 1, "validate: " + Reindent(check.GetAttributeValue("test").Trim(), Pad(indent + 1)) + (message != null ? ", " + Quote(message) : string.Empty));
                                }
                                else if (node is MarkupElement normalize && normalize.Name == "normalize")
                                {
                                    WriteDoc(normalize, indent + 1);
                                    Line(indent + 1, "normalize: " + Reindent(normalize.GetAttributeValue("value").Trim(), Pad(indent + 1)));
                                }
                                else if (node is MarkupElement method)
                                {
                                    WriteElement(method, indent + 1, false);
                                }
                            }
                            scopes.Pop();
                            Line(indent, "}");
                            return;
                        }
                    case "call" when Only("fn", "args", "into") && Attr("fn") != null && !e.Children.Any():
                        {
                            // <call fn="greet" args="'x'" into="r" /> is @greet('x'), or r = @greet('x').
                            string call = "@" + Attr("fn").Trim().TrimStart('@') + "(" + (Attr("args")?.Trim() ?? string.Empty) + ")";
                            if (Attr("into") is string to)
                            {
                                scopes.Declare(to);
                                call = to.Trim().TrimStart('$') + " = " + call;
                            }
                            Line(indent, call);
                            return;
                        }
                    case "return" when Only("value") && !e.Children.Any():
                        Line(indent, Attr("value") is string rv ? "return " + Reindent(rv.Trim(), Pad(indent)) : "return");
                        return;
                    case "exit" when Only("code") && !e.Children.Any():
                        Line(indent, Attr("code") is string code ? "exit " + code.Trim() : "exit");
                        return;
                    case "break" when e.Attributes.Count == 0 && !e.Children.Any():
                    case "continue" when e.Attributes.Count == 0 && !e.Children.Any():
                        Line(indent, e.Name);
                        return;
                    case "throw" when Only("value") && Attr("value") != null && !e.Children.Any():
                        Line(indent, "throw " + Reindent(Attr("value").Trim(), Pad(indent)));
                        return;
                    case "throw" when e.Attributes.Count == 0 && !e.Children.Any(c => !(c is MarkupText t && t.IsWhitespace)):
                        Line(indent, "throw");
                        return;
                    case "error" when Attr("name") is string errorName && IsName(errorName.Trim()) && !errorName.Contains('.') && e.Children.All(IsFieldLine)
                        && (Attr("extends") == null || IsName(Attr("extends").Trim())):
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            string options = string.Join(", ", e.Attributes.Where(a => a.Name is not ("name" or "description" or "extends")).Select(a => FormatAttribute(e, a)));
                            sb.Append(pad).Append("error ").Append(errorName.Trim()).Append(options.Length > 0 ? "(" + options + ")" : string.Empty);
                            if (Attr("extends") is string extends)
                            {
                                sb.Append(" extends ").Append(extends.Trim());
                            }
                            if (!e.ChildElements.Any())
                            {
                                sb.Append('\n');
                                return;
                            }
                            sb.Append(" {\n");
                            foreach (MarkupNode node in e.Children)
                            {
                                if (node is MarkupComment c)
                                {
                                    WriteComment(c, indent + 1);
                                }
                                else if (node is MarkupElement field)
                                {
                                    WriteField(field, indent + 1);
                                }
                            }
                            Line(indent, "}");
                            return;
                        }
                    case "job" when Attr("name") is string jobName && IsName(jobName.Trim()) && !jobName.Contains('.')
                        && e.Children.All(n => IsFieldLine(n) || n is MarkupElement { Name: "fn" }):
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            List<string> fieldLines = FieldsAsLines(Attr("fields"));
                            string options = string.Join(", ", e.Attributes.Where(a => a.Name is not ("name" or "description") && !(a.Name == "fields" && fieldLines != null)).Select(a => FormatAttribute(e, a)));
                            sb.Append(pad).Append("job ").Append(jobName.Trim()).Append(options.Length > 0 ? "(" + options + ")" : string.Empty);
                            if (!e.ChildElements.Any() && fieldLines == null)
                            {
                                sb.Append('\n');
                                return;
                            }
                            sb.Append(" {\n");
                            foreach (string fieldLine in fieldLines ?? new List<string>())
                            {
                                Line(indent + 1, fieldLine);
                            }
                            scopes.Push();
                            foreach (MarkupNode node in e.Children)
                            {
                                if (node is MarkupComment c)
                                {
                                    WriteComment(c, indent + 1);
                                }
                                else if (node is MarkupElement child && child.Name == "field")
                                {
                                    WriteField(child, indent + 1);
                                }
                                else if (node is MarkupElement method)
                                {
                                    WriteElement(method, indent + 1, false);
                                }
                            }
                            scopes.Pop();
                            Line(indent, "}");
                            return;
                        }
                    case "throw" when Only("type", "message") && !e.ChildElements.Any() && (Attr("type") == null || IsName(Attr("type").Trim())):
                        {
                            string message = Attr("message") ?? TrimLayout(TextOf(e));
                            Line(indent, "throw" + (Attr("type") != null ? " " + Attr("type").Trim() : string.Empty) + (message != null ? " " + Quote(message) : string.Empty));
                            return;
                        }
                    case "try" when e.Attributes.Count == 0:
                        WriteTry(e, indent, chained);
                        return;
                    case "import" when Only("package", "path", "as", "names") && !e.Children.Any():
                        {
                            string line = Attr("package") != null ? "import " + Attr("package").Trim()
                                : Attr("names") != null ? "import " + Attr("names").Trim() + " from " + Quote(ConvertedImport(Attr("path")))
                                : "import " + Quote(ConvertedImport(Attr("path") ?? string.Empty));
                            Line(indent, line + (Attr("as") != null ? " as " + Attr("as").Trim() : string.Empty));
                            return;
                        }
                }

                WriteGeneric(e, indent, chained);
            }

            private static readonly string[] RuleAttributes = { "min", "max", "pattern", "one_of", "not", "parse" };

            /// <summary>A model's settings as .kscript lines: <c>table: people</c>, <c>unique: [email, [team, slug]]</c>.</summary>
            private static IEnumerable<string> ModelSettingLines(MarkupElement e)
            {
                foreach (string name in CodeParser.ModelSettings)
                {
                    string value = e.GetAttributeValue(name)?.Trim();
                    if (value == null)
                    {
                        continue;
                    }
                    bool list = name is "unique" or "index" or "hidden" or "guarded";
                    yield return name + ": " + (list && !value.StartsWith('[') ? "[" + string.Join(", ", value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0)) + "]" : value);
                }
            }

            /// <summary>
            /// A type's rule attributes as .kscript lines (<c>min: 0</c>, <c>pattern: /^[a-z]+$/</c>, <c>one_of: [a, b]</c>),
            /// or null if one can't be written as a line.
            /// </summary>
            private static List<string> TypeRuleLines(MarkupElement e)
            {
                var lines = new List<string>();
                foreach (string name in RuleAttributes)
                {
                    string value = e.GetAttributeValue(name)?.Trim();
                    if (value == null)
                    {
                        continue;
                    }
                    if (value.Contains('\n'))
                    {
                        return null;
                    }
                    string written = name switch
                    {
                        "pattern" => value.StartsWith('/') ? value : Quote(value),
                        "min" or "max" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ? value : Quote(value),
                        "one_of" or "not" => value.StartsWith('[') ? value : "[" + string.Join(", ", value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0)) + "]",
                        _ => value,
                    };
                    lines.Add(name + ": " + written);
                }
                return lines;
            }

            /// <summary>What the body of a .kscript type can hold: fields, validate and normalize rules, fn methods, comments.</summary>
            private static bool IsTypeLine(MarkupNode node)
            {
                if (IsFieldLine(node))
                {
                    return true;
                }
                if (!(node is MarkupElement e) || e.Children.Any(c => !(c is MarkupText t && t.IsWhitespace)))
                {
                    return node is MarkupElement fn && fn.Name == "fn";
                }
                return e.Name switch
                {
                    "validate" => e.Attributes.All(a => a.Name is "test" or "message") && e.GetAttribute("test")?.Value != null
                        && (e.GetAttributeValue("message") is not string m || !m.Contains('\n')),
                    "normalize" => e.Attributes.All(a => a.Name == "value") && e.GetAttribute("value")?.Value != null,
                    "fn" => true,
                    _ => false,
                };
            }

            /// <summary>A &lt;field&gt; the one-line form can hold, a comment, or layout.</summary>
            private static bool IsFieldLine(MarkupNode node)
            {
                if (node is MarkupComment || (node is MarkupText t && t.IsWhitespace))
                {
                    return true;
                }
                if (!(node is MarkupElement f) || f.Name != "field" || f.Children.Any(c => !(c is MarkupText ft && ft.IsWhitespace)))
                {
                    return false;
                }
                if (f.Attributes.Any(a => a.Name is not ("name" or "type" or "from" or "default" or "optional" or "value" or "description")))
                {
                    return false;
                }
                string name = f.GetAttributeValue("name")?.Trim() ?? string.Empty;
                string type = f.GetAttributeValue("type")?.Trim();
                string optional = f.GetAttributeValue("optional")?.Trim();
                return IsName(name.TrimEnd('?')) && !name.Contains('.')
                    && (type == null || (type.Length > 0 && type.All(c => Lexer.IsIdentChar(c) || c is '.' or '[' or ']' or '?' or '(' or ')' or ':' or ',' or ' ' or '\'' or '-')))
                    && (optional == null || optional is "yes" or "no")
                    && !(f.GetAttribute("default") != null && f.GetAttribute("value") != null);
            }

            /// <summary><c>&lt;field name="stars" type="int" from="stargazers_count" /&gt;</c> is <c>stars: int from stargazers_count</c>.</summary>
            private void WriteField(MarkupElement f, int indent)
            {
                string Attr(string name) => f.GetAttributeValue(name)?.Trim();
                WriteDoc(f, indent);
                string name = Attr("name");
                if (Attr("optional") == "yes" && !name.EndsWith('?'))
                {
                    name += "?";
                }
                var line = new StringBuilder(name);
                if (Attr("type") is string type)
                {
                    line.Append(": ").Append(type);
                }
                if (Attr("from") is string from)
                {
                    bool bare = from.Length > 0 && from.All(c => !char.IsWhiteSpace(c) && c is not (';' or '}' or '=' or '"'));
                    line.Append(" from ").Append(bare ? from : Quote(from));
                }
                if (Attr("default") is string defaultValue)
                {
                    line.Append(" = ").Append(Reindent(defaultValue, Pad(indent)));
                }
                else if (Attr("value") is string value)
                {
                    line.Append(" => ").Append(Reindent(value, Pad(indent)));
                }
                Line(indent, line.ToString());
            }

            private static bool IsName(string s) => s.Length > 0 && Lexer.IsIdentStart(s[0]) && s.All(c => Lexer.IsIdentChar(c) || c == '.');

            private string ConvertedImport(string src) => ImportPath(src.Trim());

            private void WriteStatements(string statements, int indent, bool declare)
            {
                if (string.IsNullOrWhiteSpace(statements))
                {
                    return;
                }
                if (declare)
                {
                    foreach (ExprNode node in ExpressionParser.ParseStatements(statements).Nodes)
                    {
                        if (node is AssignmentNode a && a.Operator == "=")
                        {
                            scopes.Declare(a.Name);
                        }
                    }
                }
                // One statement per line (a .kscript line ending in ; starts a new statement anyway).
                ParseResult parsed = ExpressionParser.ParseStatements(statements);
                if (parsed.Errors.Count > 0)
                {
                    Line(indent, Reindent(statements.Trim(), Pad(indent)));
                    return;
                }
                foreach (ExprNode node in parsed.Nodes)
                {
                    string statement = statements.Substring(node.Span.Start, node.Span.End - node.Span.Start).Trim();
                    if (node is AssignmentNode a && a.Target is VariableNode && statement.StartsWith("$", StringComparison.Ordinal))
                    {
                        statement = statement.Substring(1); // $x = 3 is written x = 3
                    }
                    Line(indent, Reindent(statement, Pad(indent)));
                }
            }

            private void WriteBlock(string header, MarkupElement e, int indent, bool chained, bool pushScope = true)
            {
                string pad = chained ? string.Empty : Pad(indent);
                if (!e.Children.Any(c => !(c is MarkupText t && t.IsWhitespace)))
                {
                    sb.Append(pad).Append(header).Append(" { }\n");
                    return;
                }
                sb.Append(pad).Append(header).Append(" {\n");
                if (pushScope)
                {
                    scopes.Push();
                }
                WriteBody(e.Children, indent + 1);
                if (pushScope)
                {
                    scopes.Pop();
                }
                Line(indent, "}");
            }

            /// <summary>
            /// A &lt;case&gt; or &lt;default&gt; as a match arm: <c>1, 2 =&gt; { }</c>, <c>_ when $n &gt; 9 =&gt; { }</c>,
            /// <c>_ =&gt; { }</c>. An arm that holds one short statement is written on its line: <c>1 =&gt; echo "one"</c>.
            /// </summary>
            private void WriteArm(MarkupElement branch, int indent)
            {
                string values = branch.Name == "default" ? null : branch.GetAttributeValue("is")?.Trim();
                string test = branch.Name == "default" ? null : branch.GetAttributeValue("when")?.Trim();
                string head = (string.IsNullOrEmpty(values) ? "_" : values) + (string.IsNullOrEmpty(test) ? string.Empty : " when " + test) + " =>";

                List<MarkupNode> body = branch.Children.Where(c => !(c is MarkupText t && t.IsWhitespace)).ToList();
                if (body.Count == 1 && body[0] is MarkupElement)
                {
                    int mark = sb.Length;
                    scopes.Push();
                    WriteBody(branch.Children, indent);
                    scopes.Pop();
                    string statement = sb.ToString(mark, sb.Length - mark).Trim('\n');
                    sb.Length = mark;
                    if (statement.IndexOf('\n') < 0 && statement.IndexOf(',') < 0 && statement.IndexOf("//", StringComparison.Ordinal) < 0)
                    {
                        Line(indent, head + " " + statement.Trim());
                        return;
                    }
                }
                WriteBlock(head, branch, indent, chained: false);
            }

            /// <summary>The names in a catch's type: <c>NotFound, Conflict</c>, <c>NotFound | Conflict</c> or <c>[IO, Network]</c>.</summary>
            private static List<string> ErrorTypeNames(string text) =>
                text.Trim().TrimStart('[').TrimEnd(']').Split(',', '|').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

            private void WriteTry(MarkupElement e, int indent, bool chained)
            {
                string pad = chained ? string.Empty : Pad(indent);
                List<MarkupNode> body = e.Children.Where(c => !(c is MarkupElement x && x.Name is "catch" or "finally")).ToList();
                sb.Append(pad).Append("try {\n");
                scopes.Push();
                WriteBody(body, indent + 1);
                scopes.Pop();
                sb.Append(Pad(indent)).Append('}');
                foreach (MarkupElement clause in e.ChildElements.Where(c => c.Name is "catch" or "finally"))
                {
                    scopes.Push();
                    if (clause.Name == "finally")
                    {
                        sb.Append(" finally");
                    }
                    else
                    {
                        sb.Append(" catch");
                        string name = clause.GetAttributeValue("as")?.Trim().TrimStart('$');
                        List<string> types = clause.GetAttributeValue("type") is string type ? ErrorTypeNames(type) : null;
                        if (types != null && types.All(t => IsName(t) && char.IsUpper(t[0])) && types.Count > 0)
                        {
                            // catch NotFound | Conflict as e
                            sb.Append(' ').Append(string.Join(" | ", types));
                            if (name != null)
                            {
                                sb.Append(" as ").Append(name);
                            }
                        }
                        else
                        {
                            if (clause.GetAttributeValue("type") is string raw)
                            {
                                sb.Append("(type: ").Append(FormatValue(raw)).Append(')');
                            }
                            if (name != null && name.Length > 0 && char.IsUpper(name[0]))
                            {
                                // A capitalised name would read as an error type.
                                sb.Append(types == null ? " Error" : string.Empty).Append(" as ").Append(name);
                            }
                            else if (name != null)
                            {
                                sb.Append(' ').Append(name);
                            }
                        }
                        if (name != null)
                        {
                            scopes.Declare(name);
                        }
                        if (clause.GetAttributeValue("when") is string when)
                        {
                            sb.Append(" when ").Append(when.Trim());
                        }
                    }
                    if (!clause.Children.Any(c => !(c is MarkupText t && t.IsWhitespace)))
                    {
                        sb.Append(" { }");
                    }
                    else
                    {
                        sb.Append(" {\n");
                        WriteBody(clause.Children, indent + 1);
                        sb.Append(Pad(indent)).Append('}');
                    }
                    scopes.Pop();
                }
                sb.Append('\n');
            }

            private void WriteGeneric(MarkupElement e, int indent, bool chained)
            {
                string pad = chained ? string.Empty : Pad(indent);
                if (BlockStatements.Contains(e.Name) && e.Name != "kscript")
                {
                    Result.Warnings.Add($"<{e.Name}> on line {LineOf(e)} has a shape the short form can't hold, so it was written as {e.Name}(...).");
                }
                sb.Append(pad).Append(e.Name);
                if (e.Attributes.Count > 0)
                {
                    sb.Append('(').Append(string.Join(", ", e.Attributes.Select(a => FormatAttribute(e, a)))).Append(')');
                }
                List<MarkupNode> content = e.Children.Where(c => !(c is MarkupText t && t.IsWhitespace)).ToList();
                bool hasElements = content.Any(c => c is MarkupElement);
                string text = hasElements ? null : TrimLayout(TextOf(e));
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(' ').Append(Quote(text));
                }
                if (hasElements)
                {
                    if (content.OfType<MarkupText>().Any())
                    {
                        Result.Warnings.Add($"<{e.Name}> on line {LineOf(e)} mixes text and elements; the text was kept as comments.");
                    }
                    sb.Append(" {\n");
                    scopes.Push();
                    WriteBody(e.Children, indent + 1);
                    scopes.Pop();
                    sb.Append(Pad(indent)).Append('}');
                }
                sb.Append('\n');
            }

            private int LineOf(MarkupElement e) => source.GetPosition(e.Span.Start).Line + 1;

            private string FormatAttribute(MarkupElement e, MarkupAttribute a)
            {
                if (a.Value == null || a.ValueText.Trim() == "yes")
                {
                    return a.Name;
                }
                ElementInfo element = Elements?.Invoke(ElementInfo.BareName(e.Name));
                AttributeInfo info = element?.GetAttribute(a.Name);
                bool text = info != null && info.Kind is AttributeKind.Template or AttributeKind.Path;
                if (CodeArguments.TakesFunctions(ElementInfo.BareName(e.Name), info))
                {
                    // <spawn fn="send"> becomes spawn(fn: @send): a function is @name wherever one is named.
                    return a.Name + ": " + CodeArguments.FunctionNames(a.ValueText, code: true);
                }
                if (NameList(e.Name, a.Name) && global::KScript.Metadata.NameList.Names(a.ValueText) is { Count: > 1 } names
                    && names.All(n => IsSafeBare(n)))
                {
                    // <Middleware use="log, cors"> becomes Middleware(use: [log, cors]); text in a list is quoted.
                    return a.Name + ": [" + string.Join(", ", names.Select(n => text ? "'" + n + "'" : n)) + "]";
                }
                if (Elements == null)
                {
                    return a.Name + ": " + FormatValue(a.ValueText);
                }
                return a.Name + ": " + FormatByKind(a, info, element);
            }

            /// <summary>
            /// The value as .kscript writes it for what the argument holds: quotes make a string and everything else
            /// is code, so text is quoted, values and names are bare, lists are arrays and handlers are in braces.
            /// </summary>
            private static string FormatByKind(MarkupAttribute a, AttributeInfo info, ElementInfo element)
            {
                string value = a.ValueText;
                string trimmed = value.Trim();
                AttributeKind kind = info?.Kind ?? (a.Name.StartsWith("on_", StringComparison.Ordinal) || a.Name.StartsWith("on-", StringComparison.Ordinal) ? AttributeKind.Statements : AttributeKind.Template);
                switch (kind)
                {
                    case AttributeKind.Statements:
                        return "{ " + trimmed + " }";
                    case AttributeKind.Expression:
                        return IsSafeBare(trimmed) || IsWholeString(trimmed) ? trimmed : "(" + trimmed + ")";
                    case AttributeKind.ExpressionList:
                        return IsSafeBare(trimmed) || IsWholeString(trimmed) ? trimmed : "[" + trimmed + "]";
                    case AttributeKind.Bool:
                        if (CodeArguments.IsBoolWord(trimmed.ToLowerInvariant()))
                        {
                            return trimmed.ToLowerInvariant() is "yes" or "y" or "true" or "1" or "on" ? "true" : "false";
                        }
                        // { not $ready }: a condition worked out when it is read is written as the condition.
                        return trimmed.Length > 2 && trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}' && IsSafeBare(trimmed.Substring(1, trimmed.Length - 2).Trim())
                            ? trimmed.Substring(1, trimmed.Length - 2).Trim()
                            : Quote(value);
                    case AttributeKind.Template:
                    case AttributeKind.Text:
                    case AttributeKind.Path:
                    case AttributeKind.Selector:
                        if (info?.IsName == true && IsSafeBare(trimmed))
                        {
                            return trimmed;
                        }
                        return IsNumber(trimmed) ? trimmed : Quote(value);
                    default:
                        if (CodeArguments.IsList(kind, info) && !IsSafeBare(trimmed))
                        {
                            return "[" + trimmed + "]";
                        }
                        return FormatValue(value);
                }
            }

            private static bool IsNumber(string value) =>
                double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

            /// <summary>True when the value is one "double quoted" string and nothing else.</summary>
            private static bool IsWholeString(string value) =>
                value.Length >= 2 && value[0] == '"' && !value.Contains('\n') && Quotes.Close(value, 0, value.Length) == value.Length - 1;

            /// <summary>Writes a value bare when the parser would read it back unchanged, else in double quotes.</summary>
            private static string FormatValue(string value)
            {
                return IsSafeBare(value) ? value : Quote(value);
            }

            private static bool IsSafeBare(string value)
            {
                if (value.Length == 0 || value != value.Trim() || value.Contains('\n') || value.Contains("//") || value[0] == '"' || value == "yes")
                {
                    return false;
                }
                int depth = 0;
                char quote = '\0';
                foreach (char c in value)
                {
                    if (quote != '\0')
                    {
                        if (c == quote)
                        {
                            quote = '\0';
                        }
                        continue;
                    }
                    switch (c)
                    {
                        case '\'':
                        case '"':
                            quote = c;
                            break;
                        case '(':
                        case '[':
                        case '{':
                            depth++;
                            break;
                        case ')':
                        case ']':
                        case '}':
                            if (--depth < 0)
                            {
                                return false;
                            }
                            break;
                        case ',' when depth == 0:
                        case ';' when depth == 0:
                            return false;
                    }
                }
                return depth == 0 && quote == '\0';
            }
        }

        /// <summary>"text" with the parser's two escapes: \" and \\.</summary>
        internal static string Quote(string text)
        {
            var sb = new StringBuilder("\"");
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\' && (i + 1 == text.Length || text[i + 1] == '"' || text[i + 1] == '\\'))
                {
                    sb.Append("\\\\");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.Append('"').ToString();
        }

        #endregion

        #region .kscript to XML

        private sealed class XmlWriter
        {
            public Func<string, string> ImportPath { get; init; }

            /// <summary>True for an element's attribute that holds a list of names.</summary>
            public Func<string, string, bool> NameList { get; init; }

            /// <summary>Describes an element by name, or null when nothing is known about elements.</summary>
            public Func<string, ElementInfo> Elements { get; init; }

            private readonly StringBuilder sb = new StringBuilder();
            private readonly Scopes scopes = new Scopes();

            public ConversionResult Result { get; } = new ConversionResult();

            private SourceText source;

            public void WriteDocument(MarkupDocument document)
            {
                source = document.Source;
                WriteNodes(document.Children, 0);
                Result.Text = sb.ToString().TrimEnd('\n') + "\n";
            }

            private static string Pad(int indent) => new string(' ', indent * 2);

            private void WriteChildren(MarkupElement e, int indent) => WriteNodes(e.Children, indent);

            private void WriteNodes(IReadOnlyList<MarkupNode> nodes, int indent)
            {
                var comment = new List<string>();
                void FlushComment()
                {
                    if (comment.Count == 0)
                    {
                        return;
                    }
                    WriteCommentLines(comment, indent);
                    comment.Clear();
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    switch (nodes[i])
                    {
                        case MarkupComment c when comment.Count == 0 && IsTrailing(nodes, i) && sb.Length > 0 && sb[sb.Length - 1] == '\n':
                            // count = 0 // note stays on its line: <let ... /> <!-- note -->
                            sb.Length--;
                            sb.Append(" <!-- ").Append(c.Text.Trim().Replace("--", "- -")).Append(" -->\n");
                            break;
                        case MarkupComment c:
                            // Consecutive // lines become one XML comment. (/// lines are descriptions, not comments.)
                            comment.AddRange(c.Text.Replace("\r\n", "\n").Split('\n'));
                            break;
                        case MarkupText t when t.IsWhitespace && t.Value.Count(ch => ch == '\n') > 1:
                            FlushComment();
                            if (i > 0 && i + 1 < nodes.Count && !sb.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                            {
                                sb.Append('\n');
                            }
                            break;
                        case MarkupElement child:
                            FlushComment();
                            WriteElement(child, indent);
                            break;
                    }
                }
                FlushComment();
            }

            /// <summary>A one-line // comment written after a statement on the same line.</summary>
            private bool IsTrailing(IReadOnlyList<MarkupNode> nodes, int index)
            {
                if (source == null || index == 0 || nodes[index] is not MarkupComment c || c.Text.Contains('\n') || nodes[index - 1] is not MarkupElement previous)
                {
                    return false;
                }
                int from = previous.Span.End, to = c.Span.Start;
                return from <= to && to <= source.Length && source.Text.IndexOf('\n', from, to - from) < 0;
            }

            private void WriteCommentLines(List<string> lines, int indent)
            {
                List<string> body = DedentAll(string.Join("\n", lines).Replace("--", "- -"));
                if (body.Count <= 1)
                {
                    sb.Append(Pad(indent)).Append("<!-- ").Append(body.FirstOrDefault() ?? string.Empty).Append(" -->\n");
                    return;
                }
                sb.Append(Pad(indent)).Append("<!--\n");
                foreach (string line in body)
                {
                    sb.Append(line.Length > 0 ? Pad(indent + 1) + line : string.Empty).Append('\n');
                }
                sb.Append(Pad(indent)).Append("-->\n");
            }

            private void WriteElement(MarkupElement e, int indent)
            {
                // Scope bookkeeping mirrors the runtime: a block's body is a new scope.
                switch (e.Name)
                {
                    case "let":
                        if (e.GetAttributeValue("name") is string declared)
                        {
                            scopes.Declare(declared);
                        }
                        break;
                    case "do" when TextOf(e) != null:
                        WriteAssignment(e, indent);
                        return;
                }

                List<MarkupAttribute> attributes = e.Attributes.ToList();
                // let const="yes" is written as <const>, the element made for it.
                string tag = e.Name;
                if (tag == "let" && attributes.Any(a => a.Name == "const" && IsYes(a.ValueText)))
                {
                    tag = "const";
                    attributes.RemoveAll(a => a.Name == "const");
                }
                string doc = e.GetAttributeValue("description");
                if (doc != null && (doc.Contains('\n') || doc.TrimStart().StartsWith("@", StringComparison.Ordinal) || doc.Contains("\n@")))
                {
                    // Long or tagged docs read better as a <!--- doc comment --> than as an attribute.
                    attributes.RemoveAll(a => a.Name == "description");
                    string body = doc.Replace("--", "- -");
                    sb.Append(Pad(indent)).Append("<!---\n");
                    foreach (string line in body.Split('\n'))
                    {
                        sb.Append(line.Length > 0 ? Pad(indent + 1) + line : string.Empty).Append('\n');
                    }
                    sb.Append(Pad(indent)).Append("-->\n");
                }

                // A long value reads better as element text: <let name="people">[ ... ]</let>.
                string textValue = null;
                if (e.Name is "let" or "set" or "const" && e.GetAttributeValue("value") is string v && v.Contains('\n') && !e.Children.Any())
                {
                    attributes.RemoveAll(a => a.Name == "value");
                    textValue = Reindent(v, Pad(indent + 1));
                }

                sb.Append(Pad(indent)).Append('<').Append(tag);
                if (attributes.Count == 1 && attributes[0].Name == ShorthandCondition.AttributeFor(tag)
                    && ShorthandCondition.CanWrite(attributes[0].ValueText))
                {
                    // <if $ready> rather than <if test="$ready">.
                    sb.Append(' ').Append(attributes[0].ValueText.Trim());
                    attributes.Clear();
                }
                foreach (MarkupAttribute a in attributes)
                {
                    sb.Append(' ').Append(a.Name);
                    if (a.Value != null && e.Name == "import" && a.Name == "path")
                    {
                        sb.Append("=\"").Append(EscapeAttribute(ImportPath(a.ValueText.Trim()))).Append('"');
                    }
                    else if (a.Value != null && CodeArguments.TakesFunctions(ElementInfo.BareName(e.Name), Elements?.Invoke(ElementInfo.BareName(e.Name))?.GetAttribute(a.Name)))
                    {
                        // spawn(fn: @send) becomes <spawn fn="send">, and tools: [@a, @b] becomes tools="a, b".
                        sb.Append("=\"").Append(EscapeAttribute(CodeArguments.FunctionNames(a.ValueText, code: false))).Append('"');
                    }
                    else if (a.Value != null && NameList(e.Name, a.Name) && global::KScript.Metadata.NameList.IsArray(a.ValueText))
                    {
                        // Middleware(use: [log, cors]) becomes <Middleware use="log, cors">.
                        sb.Append("=\"").Append(EscapeAttribute(string.Join(", ", global::KScript.Metadata.NameList.Names(a.ValueText)))).Append('"');
                    }
                    else if (a.Value != null)
                    {
                        sb.Append("=\"").Append(EscapeAttribute(Reindent(a.ValueText, Pad(indent + 1)))).Append('"');
                    }
                }

                List<MarkupNode> content = e.Children.Where(c => !(c is MarkupText t && t.IsWhitespace)).ToList();
                if (textValue != null)
                {
                    sb.Append('>').Append(EscapeText(textValue)).Append("</").Append(tag).Append(">\n");
                    return;
                }
                if (content.Count == 0)
                {
                    sb.Append(" />\n");
                    return;
                }
                if (content.All(c => c is MarkupText))
                {
                    sb.Append('>').Append(EscapeText(string.Concat(content.Cast<MarkupText>().Select(t => t.Value)))).Append("</").Append(tag).Append(">\n");
                    return;
                }

                sb.Append(">\n");
                bool block = e.Name != "kscript" && !e.Name.StartsWith("KScript.", StringComparison.Ordinal);
                if (block)
                {
                    scopes.Push();
                }
                switch (e.Name)
                {
                    case "for":
                        foreach (string name in (e.GetAttributeValue("each") ?? string.Empty).Split(','))
                        {
                            scopes.Declare(name);
                        }
                        scopes.Declare(e.GetAttributeValue("at"));
                        break;
                    case "fn":
                        foreach (string p in ParameterNames(e.GetAttributeValue("params")))
                        {
                            scopes.Declare(p);
                        }
                        break;
                    case "catch":
                        scopes.Declare(e.GetAttributeValue("as"));
                        break;
                }
                foreach (MarkupNode node in e.Children)
                {
                    if (node is MarkupText t && !t.IsWhitespace)
                    {
                        sb.Append(Pad(indent + 1)).Append(EscapeText(t.Value.Trim())).Append('\n');
                    }
                }
                WriteChildren(e, indent + 1);
                if (block)
                {
                    scopes.Pop();
                }
                sb.Append(Pad(indent)).Append("</").Append(tag).Append(">\n");
            }

            /// <summary>
            /// x = 3 in a .kscript file declares x the first time. XML says which: &lt;let&gt; for a new name,
            /// &lt;set&gt; for a change, &lt;do&gt; for calls and anything else.
            /// </summary>
            private void WriteAssignment(MarkupElement e, int indent)
            {
                string text = TextOf(e)?.Trim() ?? string.Empty;
                ParseResult parsed = ExpressionParser.ParseStatements(text);
                if (parsed.Errors.Count == 0 && parsed.Nodes.Count == 1 && parsed.Nodes[0] is AssignmentNode a
                    && a.Target is VariableNode or IdentifierNode)
                {
                    string value = Reindent(text.Substring(a.Value.Span.Start, a.Value.Span.End - a.Value.Span.Start).Trim(), Pad(indent + 1));
                    string name = a.Name;
                    if (a.Operator == "=" && !scopes.IsVisible(name))
                    {
                        scopes.Declare(name);
                        WriteNamed("let", name, null, value, indent);
                        return;
                    }
                    WriteNamed("set", name, a.Operator == "=" ? null : a.Operator, value, indent);
                    return;
                }

                bool declares = false;
                foreach (ExprNode node in parsed.Nodes)
                {
                    if (node is AssignmentNode s && s.Operator == "=" && s.Target is VariableNode or IdentifierNode && !scopes.IsVisible(s.Name))
                    {
                        declares = true;
                        scopes.Declare(s.Name);
                    }
                }
                if (declares)
                {
                    Result.Warnings.Add($"\"{text}\" declares new variables and does other things; it was written as <let> text, which declares every name it assigns.");
                }
                sb.Append(Pad(indent)).Append(declares ? "<let>" : "<do>").Append(EscapeText(Reindent(text, Pad(indent + 1)))).Append(declares ? "</let>\n" : "</do>\n");
            }

            private void WriteNamed(string element, string name, string op, string value, int indent)
            {
                sb.Append(Pad(indent)).Append('<').Append(element).Append(" name=\"").Append(name).Append('"');
                if (op != null)
                {
                    sb.Append(" op=\"").Append(EscapeAttribute(op)).Append('"');
                }
                if (value.Contains('\n'))
                {
                    sb.Append('>').Append(EscapeText(value)).Append("</").Append(element).Append(">\n");
                    return;
                }
                sb.Append(" value=\"").Append(EscapeAttribute(value)).Append("\" />\n");
            }

            // '>' is fine in XML (so -> and |> stay readable); only ']]>' needs it escaped.
            private static string EscapeText(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace("]]>", "]]&gt;");

            private static string EscapeAttribute(string s) => EscapeText(s).Replace("\"", "&quot;");
        }

        #endregion
    }
}
