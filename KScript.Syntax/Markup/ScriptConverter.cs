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
        public static ConversionResult Convert(string text, string path)
        {
            if (MarkupParser.IsCodePath(path))
            {
                return ToXml(CodeParser.Parse(text));
            }
            return ToCode(MarkupParser.Parse(text, DocumentKind.Script));
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

        public static ConversionResult ToCode(MarkupDocument document)
        {
            var writer = new CodeWriter();
            writer.WriteDocument(document);
            return writer.Result;
        }

        public static ConversionResult ToXml(MarkupDocument document)
        {
            var writer = new XmlWriter();
            writer.WriteDocument(document);
            return writer.Result;
        }

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
                    if (c == '\\')
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
                Result.Text = sb.ToString().TrimEnd('\n') + "\n";
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
                string prefix = c.IsDoc ? "///" : "//";
                foreach (string line in c.IsDoc ? c.DocText.Split('\n').ToList() : DedentAll(c.Text))
                {
                    Line(indent, line.Length == 0 ? prefix : prefix + " " + line);
                }
            }

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
                    case "let" when Only("name", "value", "const", "type", "description") && !e.ChildElements.Any():
                        {
                            string name = Attr("name")?.Trim();
                            string value = Attr("value") ?? TrimLayout(TextOf(e))?.Trim();
                            if (name == null)
                            {
                                WriteStatements(value, indent, declare: true);
                                return;
                            }
                            bool isConst = IsYes(Attr("const"));
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
                            string options = string.Join(", ", e.Attributes.Where(a => a.Name != "value").Select(FormatAttribute));
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
                    case "for" when Only("each", "in", "index") && Attr("each") != null && Attr("in") != null:
                        {
                            scopes.Push();
                            foreach (string v in Attr("each").Split(','))
                            {
                                scopes.Declare(v);
                            }
                            scopes.Declare(Attr("index"));
                            string header = "for " + string.Join(", ", Attr("each").Split(',').Select(x => x.Trim().TrimStart('$')))
                                + (Attr("index") != null ? " at " + Attr("index").Trim().TrimStart('$') : string.Empty) + " in " + Attr("in").Trim();
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
                                    string head = branch.Name == "default" ? "default"
                                        : branch.GetAttribute("when") != null ? "when " + branch.GetAttributeValue("when").Trim()
                                        : "case " + branch.GetAttributeValue("is")?.Trim();
                                    WriteBlock(head, branch, indent + 1, chained: false);
                                }
                            }
                            Line(indent, "}");
                            return;
                        }
                    case "fn" when Only("name", "params", "value", "description") && Attr("name") != null:
                        {
                            if (!chained)
                            {
                                WriteDoc(e, indent);
                            }
                            string header = "fn " + Attr("name").Trim() + "(" + (Attr("params")?.Trim() ?? string.Empty) + ")";
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
                    case "call" when Only("fn", "args", "to") && Attr("fn") != null && !e.Children.Any():
                        {
                            // <call fn="greet" args="'x'" to="r" /> is @greet('x'), or r = @greet('x').
                            string call = "@" + Attr("fn").Trim().TrimStart('@') + "(" + (Attr("args")?.Trim() ?? string.Empty) + ")";
                            if (Attr("to") is string to)
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
                    case "throw" when Only("type", "message") && !e.ChildElements.Any() && (Attr("type") == null || IsName(Attr("type").Trim())):
                        {
                            string message = Attr("message") ?? TrimLayout(TextOf(e));
                            Line(indent, "throw" + (Attr("type") != null ? " " + Attr("type").Trim() : string.Empty) + (message != null ? " " + Quote(message) : string.Empty));
                            return;
                        }
                    case "try" when e.Attributes.Count == 0:
                        WriteTry(e, indent, chained);
                        return;
                    case "import" when Only("package", "src", "as", "names") && !e.Children.Any():
                        {
                            string line = Attr("package") != null ? "import " + Attr("package").Trim()
                                : Attr("names") != null ? "import " + Attr("names").Trim() + " from " + Quote(ConvertedImport(Attr("src")))
                                : "import " + Quote(ConvertedImport(Attr("src") ?? string.Empty));
                            Line(indent, line + (Attr("as") != null ? " as " + Attr("as").Trim() : string.Empty));
                            return;
                        }
                }

                WriteGeneric(e, indent, chained);
            }

            private static bool IsName(string s) => s.Length > 0 && Lexer.IsIdentStart(s[0]) && s.All(c => Lexer.IsIdentChar(c) || c == '.');

            /// <summary>Imports keep pointing at the file as it is; converting a library is a separate step.</summary>
            private static string ConvertedImport(string src) => src.Trim();

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
                        if (clause.GetAttributeValue("type") is string type)
                        {
                            sb.Append("(type: ").Append(FormatValue(type)).Append(')');
                        }
                        if (clause.GetAttributeValue("as") is string name)
                        {
                            sb.Append(' ').Append(name.Trim().TrimStart('$'));
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
                    sb.Append('(').Append(string.Join(", ", e.Attributes.Select(FormatAttribute))).Append(')');
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

            private static string FormatAttribute(MarkupAttribute a)
            {
                if (a.Value == null || a.ValueText.Trim() == "yes")
                {
                    return a.Name;
                }
                return a.Name + ": " + FormatValue(a.ValueText);
            }

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
            private readonly StringBuilder sb = new StringBuilder();
            private readonly Scopes scopes = new Scopes();

            public ConversionResult Result { get; } = new ConversionResult();

            public void WriteDocument(MarkupDocument document)
            {
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
                if (e.Name is "let" or "set" && e.GetAttributeValue("value") is string v && v.Contains('\n') && !e.Children.Any())
                {
                    attributes.RemoveAll(a => a.Name == "value");
                    textValue = Reindent(v, Pad(indent + 1));
                }

                sb.Append(Pad(indent)).Append('<').Append(e.Name);
                foreach (MarkupAttribute a in attributes)
                {
                    sb.Append(' ').Append(a.Name);
                    if (a.Value != null)
                    {
                        sb.Append("=\"").Append(EscapeAttribute(Reindent(a.ValueText, Pad(indent + 1)))).Append('"');
                    }
                }

                List<MarkupNode> content = e.Children.Where(c => !(c is MarkupText t && t.IsWhitespace)).ToList();
                if (textValue != null)
                {
                    sb.Append('>').Append(EscapeText(textValue)).Append("</").Append(e.Name).Append(">\n");
                    return;
                }
                if (content.Count == 0)
                {
                    sb.Append(" />\n");
                    return;
                }
                if (content.All(c => c is MarkupText))
                {
                    sb.Append('>').Append(EscapeText(string.Concat(content.Cast<MarkupText>().Select(t => t.Value)))).Append("</").Append(e.Name).Append(">\n");
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
                        scopes.Declare(e.GetAttributeValue("index"));
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
                sb.Append(Pad(indent)).Append("</").Append(e.Name).Append(">\n");
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
