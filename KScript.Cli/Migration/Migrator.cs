using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Runtime;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Migration
{
    /// <summary>Something <c>kscript migrate</c> couldn't convert, or converted with a caveat.</summary>
    public sealed class MigrationNote
    {
        public MigrationNote(int line, string message)
        {
            Line = line;
            Message = message;
        }

        /// <summary>The line in the original file, from 1.</summary>
        public int Line { get; }
        public string Message { get; }

        public override string ToString() => $"{Line}: {Message}";
    }

    public sealed class MigrationResult
    {
        /// <summary>The KScript 2 script (the original text when nothing was converted).</summary>
        public string Text { get; init; }

        /// <summary>False when the file didn't look like a KScript 1 script.</summary>
        public bool WasV1 { get; init; }

        public IReadOnlyList<MigrationNote> Notes { get; init; }
    }

    /// <summary>
    /// Rewrites a KScript 1 script as KScript 2: <c>&lt;def&gt;</c> becomes <c>&lt;let&gt;</c>,
    /// <c>@more_less($i,'lt',3)</c> becomes <c>$i lt 3</c>, <c>~people[$i]['age']</c> becomes
    /// <c>$people[$i].age</c>, and so on. The rewrite edits the original text, so comments and layout are kept.
    /// What can't be converted is left as it was, with a comment above it, and listed in the notes.
    /// </summary>
    public static class Migrator
    {
        public const string CommentPrefix = "kscript migrate:";

        public static MigrationResult Migrate(string text, bool force = false)
        {
            MarkupDocument doc = MarkupParser.Parse(text ?? string.Empty, DocumentKind.Script);
            MarkupElement root = doc.RootElements.FirstOrDefault();
            if (root == null || root.Name != "kscript")
            {
                return new MigrationResult
                {
                    Text = text,
                    WasV1 = false,
                    Notes = new[] { new MigrationNote(1, "This isn't a KScript script: the root element must be <kscript>.") },
                };
            }

            if (!force && !LooksLikeV1(doc))
            {
                return new MigrationResult { Text = text, WasV1 = false, Notes = Array.Empty<MigrationNote>() };
            }

            var run = new MigrationRun(doc, root);
            string migrated = run.Run();
            migrated = AddImports(migrated);
            return new MigrationResult { Text = migrated, WasV1 = true, Notes = run.Notes };
        }

        /// <summary>True if the document uses anything that only KScript 1 has.</summary>
        public static bool LooksLikeV1(MarkupDocument doc)
        {
            MarkupElement root = doc.RootElements.FirstOrDefault();
            if (root == null) return false;
            if (root.Attributes.Count > 0) return true;

            foreach (MarkupElement e in doc.DescendantElements())
            {
                if (Retired.Elements.ContainsKey(e.Name)) return true;
                if (e.GetAttribute("condition") != null || e.GetAttribute("checkfor") != null) return true;
                switch (e.Name)
                {
                    case "for" when e.GetAttribute("to") != null && e.GetAttribute("each") == null:
                    case "call" when e.GetAttribute("method") != null:
                    case "marray" when e.GetAttribute("id") != null:
                    case "echo" when e.GetAttribute("trail_newline") != null || e.GetAttribute("trim") != null || e.GetAttribute("type") != null:
                    case "input" when e.GetAttribute("cai") != null || e.GetAttribute("accepted_values") != null:
                    case "picker" when e.GetAttribute("persist") != null || e.GetAttribute("error_prompt") != null:
                    case "move" when e.GetAttribute("from") is MarkupAttribute from && !LooksLikeExpression(from.ValueText):
                        return true;
                }

                foreach (MarkupAttribute a in e.Attributes)
                {
                    if (UsesV1Syntax(a.ValueText)) return true;
                }
                foreach (MarkupText t in e.Children.OfType<MarkupText>())
                {
                    if (UsesV1Syntax(t.Value)) return true;
                }
            }
            return false;
        }

        private static readonly Regex V1Command = new Regex(@"@([A-Za-z_][A-Za-z0-9_]*)\(", RegexOptions.Compiled);
        private static readonly Regex V1ArrayRef = new Regex(@"~[A-Za-z_][A-Za-z0-9_]*\[", RegexOptions.Compiled);

        private static bool UsesV1Syntax(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (V1ArrayRef.IsMatch(value)) return true;
            foreach (Match m in V1Command.Matches(value))
            {
                if (Retired.Commands.ContainsKey(m.Groups[1].Value)) return true;
            }
            return false;
        }

        private static bool LooksLikeExpression(string value) =>
            value != null && (value.Contains('\'') || value.Contains('@') || value.Contains('(') || value.StartsWith("$", StringComparison.Ordinal));

        #region Imports

        /// <summary>Adds an &lt;import&gt; for each package the migrated script uses.</summary>
        internal static string AddImports(string text)
        {
            MarkupDocument doc = MarkupParser.Parse(text, DocumentKind.Script);
            MarkupElement root = doc.RootElements.FirstOrDefault();
            if (root == null || !root.StartTagClosed || root.SelfClosing) return text;

            PackageRegistry registry = PackageRegistry.Default;
            KScriptPackage core = registry.Core;
            var packages = new List<KScriptPackage>();
            foreach (string name in new[] { "KScript.Terminal", "KScript.Math", "KScript.IO", "KScript.Net" })
            {
                KScriptPackage p = registry.Get(name);
                if (p != null) packages.Add(p);
            }

            var userFunctions = new HashSet<string>(doc.DescendantElements().Where(e => e.Name == "fn").Select(e => e.GetAttributeValue("name")).Where(n => n != null), StringComparer.Ordinal);
            var already = new HashSet<string>(doc.DescendantElements().Where(e => e.Name == "import").Select(e => PackageRegistry.Normalise(e.GetAttributeValue("package") ?? "")), StringComparer.Ordinal);
            var needed = new List<string>();

            void Need(KScriptPackage p)
            {
                if (!needed.Contains(p.Name) && !already.Contains(p.Name)) needed.Add(p.Name);
            }

            foreach (MarkupElement e in doc.DescendantElements())
            {
                if (!core.Elements.ContainsKey(e.Name))
                {
                    KScriptPackage p = packages.FirstOrDefault(x => x.Elements.ContainsKey(e.Name));
                    if (p != null) Need(p);
                }

                IEnumerable<string> values = e.Attributes.Select(a => a.ValueText).Concat(e.Children.OfType<MarkupText>().Select(t => t.Value));
                foreach (string value in values.Where(v => !string.IsNullOrEmpty(v)))
                {
                    foreach (Match m in V1Command.Matches(value))
                    {
                        string name = m.Groups[1].Value;
                        if (core.Commands.ContainsKey(name) || userFunctions.Contains(name)) continue;
                        KScriptPackage p = packages.FirstOrDefault(x => x.Commands.ContainsKey(name));
                        if (p != null) Need(p);
                    }
                    foreach (Match m in Regex.Matches(value, @"\$([A-Za-z_][A-Za-z0-9_]*)\b(?!\s*=[^=])"))
                    {
                        string name = m.Groups[1].Value;
                        if (core.Variables.ContainsKey(name)) continue;
                        KScriptPackage p = packages.FirstOrDefault(x => x.Variables.ContainsKey(name));
                        if (p != null && !IsDeclared(doc, name)) Need(p);
                    }
                }
            }

            if (needed.Count == 0) return text;

            string indent = MigrationRun.ChildIndent(doc.Source, root);
            var sb = new StringBuilder();
            foreach (string name in needed.OrderBy(n => packages.FindIndex(p => p.Name == n)))
            {
                sb.Append('\n').Append(indent).Append("<import package=\"").Append(name).Append("\" />");
            }
            int at = root.StartTagSpan.End;
            return text.Substring(0, at) + sb + text.Substring(at);
        }

        private static bool IsDeclared(MarkupDocument doc, string name) =>
            doc.DescendantElements().Any(e => (e.Name == "let" || e.Name == "marray" || e.Name == "fn") && e.GetAttributeValue("name") == name
                || e.Name == "for" && (e.GetAttributeValue("each") ?? "").Split(',').Select(x => x.Trim()).Contains(name));

        #endregion
    }

    /// <summary>One migration: walks the v1 document and collects text edits.</summary>
    internal sealed class MigrationRun
    {
        private readonly MarkupDocument doc;
        private readonly MarkupElement root;
        private readonly SourceText source;
        private readonly List<(int Start, int End, string Text, int Order)> edits = new List<(int, int, string, int)>();
        private readonly V1Converter converter = new V1Converter();
        private readonly string unit;

        /// <summary>Every v1 variable, by its lower-case name, with its KScript 2 name.</summary>
        private readonly Dictionary<string, string> known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The element that declares each variable with &lt;let&gt;; null for a variable declared at the top.</summary>
        private readonly Dictionary<string, MarkupElement> letAt = new Dictionary<string, MarkupElement>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> hoisted = new List<string>();
        private readonly Dictionary<string, int> loopUses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private MarkupElement current;
        private string method;
        private HashSet<string> methodParams;

        public MigrationRun(MarkupDocument doc, MarkupElement root)
        {
            this.doc = doc;
            this.root = root;
            source = doc.Source;
            unit = IndentUnit();
            converter.ResolveVariable = Resolve;
            converter.Problem = message => Note(current ?? root, message);
        }

        public List<MigrationNote> Notes { get; } = new List<MigrationNote>();

        public string Run()
        {
            CollectVariables();
            current = root;
            RootStartTag();
            ConvertChildren(root);
            HoistDeclarations();
            return Apply();
        }

        #region Variables

        private static readonly HashSet<string> Declaring = new HashSet<string>(StringComparer.Ordinal)
        {
            "def", "temp", "defm", "update", "array", "marray", "math", "file_info", "input", "picker", "func",
        };

        private void CollectVariables()
        {
            var first = new Dictionary<string, MarkupElement>(StringComparer.OrdinalIgnoreCase);
            var loopOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (MarkupElement e in doc.DescendantElements())
            {
                foreach (string name in DeclaredBy(e))
                {
                    Know(name);
                    if (!first.ContainsKey(name)) first[name] = e;
                }

                if (e.Name == "afor" && e.GetAttributeValue("to") is string item) Know(item);
                if (e.Name == "for" && e.GetAttribute("each") == null && e.GetAttributeValue("to") is string loop)
                {
                    Know(loop);
                    loopUses[loop] = loopUses.TryGetValue(loop, out int n) ? n + 1 : 1;
                }
                if ((e.Name == "method" || e.Name == "thread") && e.GetAttributeValue("params") is string ps)
                {
                    foreach (string p in ps.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)) Know(p);
                }
            }

            foreach (KeyValuePair<string, MarkupElement> pair in first)
            {
                MarkupElement e = pair.Value;
                bool topLevel = e.Parent == root;
                bool declaresHere = e.Name != "input" && e.Name != "picker" || topLevel;
                if (topLevel && declaresHere)
                {
                    letAt[pair.Key] = e;
                }
                else if (e.Name != "input" && e.Name != "picker")
                {
                    hoisted.Add(known[pair.Key] + (loopUses.ContainsKey(pair.Key) ? "=0" : ""));
                    letAt[pair.Key] = null;
                }
            }

            // A loop counter that is used by more than one loop, or changed elsewhere, is declared once at the top.
            foreach (KeyValuePair<string, int> pair in loopUses)
            {
                if (!letAt.ContainsKey(pair.Key) && pair.Value > 1)
                {
                    hoisted.Add(known[pair.Key] + "=0");
                    letAt[pair.Key] = null;
                }
            }
        }

        private static IEnumerable<string> DeclaredBy(MarkupElement e)
        {
            switch (e.Name)
            {
                case "def":
                case "temp":
                case "array":
                case "marray":
                    return Single(e.GetAttributeValue("id"));
                case "defm":
                    return (e.GetAttributeValue("ids") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
                case "update":
                case "math":
                case "file_info":
                case "func":
                case "input":
                case "picker":
                    return Single(e.GetAttributeValue("to"));
                default:
                    return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> Single(string name) => string.IsNullOrWhiteSpace(name) ? Array.Empty<string>() : new[] { name.Trim().TrimStart('$', '~') };

        private void Know(string name)
        {
            name = name.Trim().TrimStart('$', '~');
            if (name.Length > 0 && !known.ContainsKey(name)) known[name] = Sanitise(name);
        }

        private static string Sanitise(string name)
        {
            string s = Regex.Replace(name, @"[^A-Za-z0-9_]", "_");
            return s.Length == 0 || char.IsDigit(s[0]) ? "_" + s : s;
        }

        private string Name(string v1) => known.TryGetValue(v1.Trim().TrimStart('$', '~'), out string n) ? n : Sanitise(v1.Trim().TrimStart('$', '~'));

        private (string, string) Resolve(string raw)
        {
            if (method != null && raw.StartsWith(method + ".", StringComparison.OrdinalIgnoreCase))
            {
                string rest = raw.Substring(method.Length + 1);
                string param = methodParams.FirstOrDefault(p => rest.Equals(p, StringComparison.OrdinalIgnoreCase) || rest.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase) || rest.StartsWith(p + "-", StringComparison.OrdinalIgnoreCase));
                if (param != null) return ("$" + Name(param), rest.Substring(param.Length));
            }

            for (int end = raw.Length; end > 0; end--)
            {
                if (end < raw.Length && raw[end] != '.' && raw[end] != '-') continue;
                string head = raw.Substring(0, end);
                if (known.TryGetValue(head, out string name)) return ("$" + name, raw.Substring(end));
            }
            return ("$" + Sanitise(raw), "");
        }

        /// <summary>&lt;let&gt; for the element that first declares the variable, &lt;set&gt; everywhere else.</summary>
        private string LetOrSet(MarkupElement e, string v1Name) =>
            letAt.TryGetValue(v1Name.Trim(), out MarkupElement at) && at == e ? "let" : known.ContainsKey(v1Name.Trim()) && letAt.ContainsKey(v1Name.Trim()) ? "set" : "let";

        private void HoistDeclarations()
        {
            if (hoisted.Count == 0) return;
            string indent = ChildIndent(source, root);
            var sb = new StringBuilder();
            foreach (string h in hoisted.Distinct())
            {
                string[] parts = h.Split('=');
                sb.Append('\n').Append(indent).Append(Tag("let", ("name", parts[0]), ("value", parts.Length > 1 ? parts[1] : "null")));
            }
            Insert(root.StartTagSpan.End, sb.ToString(), order: 1);
        }

        #endregion

        #region Walk

        private void ConvertChildren(MarkupElement parent)
        {
            foreach (MarkupElement child in parent.ChildElements.ToList())
            {
                Convert(child);
            }
        }

        private void Convert(MarkupElement e)
        {
            MarkupElement saved = current;
            current = e;
            try
            {
                ConvertElement(e);
            }
            finally
            {
                current = saved;
            }
        }

        private void ConvertElement(MarkupElement e)
        {
            switch (e.Name)
            {
                case "def":
                case "temp":
                    Assign(e, e.GetAttributeValue("id"), Value(e));
                    return;

                case "defm":
                    {
                        string value = Value(e);
                        string indent = IndentOf(e.Span.Start);
                        var lines = (e.GetAttributeValue("ids") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
                            .Select(id => Tag(LetOrSet(e, id), ("name", Name(id)), ("value", value)));
                        Replace(e.Span, string.Join("\n" + indent, lines));
                        return;
                    }

                case "update":
                    {
                        string to = e.GetAttributeValue("to");
                        string type = (e.GetAttributeValue("type") ?? "replace").Trim().ToLowerInvariant();
                        if (type == "append" || type == "add")
                        {
                            string value = type == "append" ? converter.Expression(TextOf(e), ValueHint.Text) : Value(e);
                            Replace(e.Span, Tag("set", ("name", Name(to ?? "")), ("op", "+="), ("value", value)));
                            return;
                        }
                        Assign(e, to, Value(e));
                        return;
                    }

                case "guard":
                    StartTag(e, "if", ("test", $"({Name0(e.GetAttributeValue("from"))} ?? '') == ''"));
                    ConvertChildren(e);
                    return;

                case "array":
                    ArrayElement(e);
                    return;

                case "marray":
                    MArray(e);
                    return;

                case "aecho":
                    AEcho(e);
                    return;

                case "ainsert":
                    {
                        Code list = converter.ArrayVariable(e.GetAttributeValue("to") ?? "");
                        Replace(e.Span, Tag("set", ("name", list.Text.TrimStart('$')), ("value", $"{list.Text}->push({Value(e)})")));
                        return;
                    }

                case "aremove":
                    {
                        Code list = converter.ArrayVariable(e.GetAttributeValue("from") ?? "");
                        Replace(e.Span, Tag("set", ("name", list.Text.TrimStart('$')), ("value", $"{list.Text}->remove_at({converter.Expression(e.GetAttributeValue("index") ?? "0")})")));
                        return;
                    }

                case "afor":
                    {
                        string item = Name(e.GetAttributeValue("to") ?? "item");
                        Code list = converter.ArrayVariable(e.GetAttributeValue("from") ?? "");
                        StartTag(e, "for", ("each", item), ("in", list.Text));
                        ConvertChildren(e);
                        return;
                    }

                case "for" when e.GetAttribute("each") == null && e.GetAttribute("to") != null:
                    ForLoop(e);
                    return;

                case "dowhile":
                    StartTag(e, "while", ("test", Condition(e.GetAttributeValue("condition"))));
                    ConvertChildren(e);
                    return;

                case "forever":
                    StartTag(e, "while", ("test", "true"));
                    ConvertChildren(e);
                    return;

                case "if" when e.GetAttribute("condition") != null || e.GetAttribute("checkfor") != null:
                    {
                        string test = Condition(e.GetAttributeValue("condition"));
                        string checkfor = e.GetAttributeValue("checkfor");
                        if (checkfor != null && converter.Expression(checkfor, ValueHint.Bool) == "false")
                        {
                            test = "not (" + test + ")";
                        }
                        StartTag(e, "if", ("test", test));
                        ConvertChildren(e);
                        return;
                    }

                case "method":
                case "thread":
                    FunctionElement(e);
                    return;

                case "call" when e.GetAttribute("method") != null:
                    CallElement(e);
                    return;

                case "math":
                    {
                        string to = e.GetAttributeValue("to");
                        if (string.IsNullOrWhiteSpace(to))
                        {
                            Note(e, "<math> without to= was used with @math_previous(); store the sum with <let name=\"...\" value=\"...\" /> instead.");
                            return;
                        }
                        Assign(e, to, converter.Arithmetic(TextOf(e)).Text);
                        return;
                    }

                case "echo":
                    Echo(e);
                    return;

                case "input":
                    Input(e);
                    return;

                case "picker":
                    Picker(e);
                    return;

                case "anykey":
                    {
                        string prompt = e.GetAttributeValue("prompt");
                        Replace(e.Span, string.IsNullOrEmpty(prompt) ? "<pause />" : "<pause>" + EscapeText(converter.Template(prompt)) + "</pause>");
                        return;
                    }

                case "end":
                    Replace(e.Span, "<exit />");
                    return;

                case "wait":
                    Wait(e);
                    return;

                case "new_dir":
                    Replace(e.Span, Tag("mkdir", ("path", converter.Expression(e.GetAttributeValue("path") ?? TextOf(e), ValueHint.Text))));
                    return;

                case "move" when e.GetAttribute("from") != null:
                    Replace(e.Span, Tag("move",
                        ("from", converter.Expression(e.GetAttributeValue("from"), ValueHint.Text)),
                        ("to", converter.Expression(e.GetAttributeValue("to") ?? "", ValueHint.Text))));
                    return;

                case "write":
                    if (e.GetAttribute("value") != null) break;
                    Replace(e.Span, OpenTag("write", ("to", converter.Expression(e.GetAttributeValue("to") ?? "", ValueHint.Text))) + EscapeText(converter.Template(TextOf(e))) + "</write>");
                    return;

                case "file_info":
                    Assign(e, e.GetAttributeValue("to"), $"@file({converter.Expression(e.GetAttributeValue("path") ?? "", ValueHint.Text)})");
                    Note(e, "<file_info> is now @file(path), a file value with ->size(), ->modified(), ->name() and more.");
                    return;

                case "download" when e.GetAttribute("destination") != null || e.GetAttribute("share_progress") != null:
                    Replace(e.Span, Tag("download",
                        ("url", converter.Expression(e.GetAttributeValue("url") ?? "", ValueHint.Text)),
                        ("to", converter.Expression(e.GetAttributeValue("destination") ?? "", ValueHint.Text))));
                    return;

                case "throw" when e.GetAttribute("message") == null:
                    {
                        string type = e.GetAttributeValue("type");
                        string message = converter.Template(TextOf(e).Trim());
                        Replace(e.Span, Tag("throw", ("type", string.IsNullOrWhiteSpace(type) ? null : type), ("message", message)));
                        return;
                    }

                case "try":
                    if (e.GetAttribute("else") != null) Note(e, "<try else=\"...\"> isn't part of KScript 2; put that in a <catch>.");
                    if (e.Attributes.Count > 0) StartTag(e, "try");
                    ConvertChildren(e);
                    return;

                case "catch" when e.GetAttribute("as") == null:
                    StartTag(e, "catch", ("as", "err"));
                    ConvertChildren(e);
                    return;

                case "kscript":
                    ConvertChildren(e);
                    return;
            }

            if (Retired.Elements.TryGetValue(e.Name, out var retired))
            {
                Note(e, $"<{e.Name}> can't be converted automatically. {retired.Replacement}");
                return;
            }

            ConvertChildren(e);
        }

        #endregion

        #region Elements

        private void RootStartTag()
        {
            MarkupAttribute throwing = root.GetAttribute("throw_exceptions");
            if (throwing != null && converter.Expression(throwing.ValueText, ValueHint.Bool) == "false")
            {
                Note(root, "throw_exceptions=\"no\" is gone: errors stop the script unless a <try> catches them.");
            }
            if (root.Attributes.Count > 0)
            {
                Replace(root.StartTagSpan, root.SelfClosing ? "<kscript />" : "<kscript>");
            }
        }

        private void Assign(MarkupElement e, string v1Name, string value)
        {
            if (string.IsNullOrWhiteSpace(v1Name))
            {
                Note(e, $"<{e.Name}> has no variable name.");
                return;
            }
            Replace(e.Span, Tag(LetOrSet(e, v1Name), ("name", Name(v1Name)), ("value", value)));
        }

        private string Name0(string v1Name) => "$" + Name(v1Name ?? "");

        /// <summary>The element's text as an expression, the way v1 &lt;def&gt; read it.</summary>
        private string Value(MarkupElement e)
        {
            if (e.ChildElements.Any())
            {
                Note(e, $"<{e.Name}> has elements inside it; only its text was converted.");
            }
            string text = TextOf(e);
            return string.IsNullOrWhiteSpace(text) ? "null" : converter.Expression(text);
        }

        private string Condition(string v1) => string.IsNullOrWhiteSpace(v1) ? "false" : converter.Expression(v1, ValueHint.Bool);

        private void ArrayElement(MarkupElement e)
        {
            string from = e.GetAttributeValue("from");
            string id = e.GetAttributeValue("id");
            string value;
            if (!string.IsNullOrWhiteSpace(from))
            {
                value = converter.ArrayVariable(from).Text;
            }
            else
            {
                string text = TextOf(e).Trim();
                string delimiter = e.GetAttributeValue("delimiter");
                if (string.IsNullOrEmpty(delimiter)) delimiter = ",";
                List<V1Node> nodes;
                try { nodes = V1Parser.ParseTemplate(text); }
                catch (V1SyntaxException) { nodes = null; }

                if (text.Length == 0)
                {
                    value = "[]";
                }
                else if (nodes != null && nodes.All(n => n is V1Text))
                {
                    value = "[" + string.Join(", ", text.Split(delimiter).Select(x => converter.Expression(x.Trim()))) + "]";
                }
                else if (nodes != null && nodes.Count == 1 && nodes[0] is V1Command { Name: "getfiles" })
                {
                    value = converter.Expression(text);
                }
                else
                {
                    value = converter.ExpressionCode(text, ValueHint.Text).Operand + "->split(" + V1Converter.Quote(delimiter) + ")";
                }
            }
            Assign(e, id, value);
        }

        private void MArray(MarkupElement e)
        {
            var attrs = new List<(string, string)> { ("name", Name(e.GetAttributeValue("id") ?? e.GetAttributeValue("name") ?? "")) };
            if (e.GetAttribute("_insertion") != null || e.GetAttribute("_removal") != null)
            {
                Note(e, "_insertion and _removal were removed; transform the values with ->map(fn($x) => ...) instead.");
            }
            if (!letAt.TryGetValue(e.GetAttributeValue("id") ?? "", out MarkupElement at) || at != e)
            {
                Note(e, "This <marray> declares the variable again; KScript 2 variables belong to the block they are declared in.");
            }
            StartTag(e, "marray", attrs.ToArray());
            foreach (MarkupElement item in Descendants(e).Where(x => x.Name == "item"))
            {
                MarkupText text = item.SingleText;
                if (text == null || item.GetAttribute("value") != null) continue;
                string raw = text.Value;
                if (raw.IndexOf('$') < 0 && raw.IndexOf('@') < 0 && raw.IndexOf('~') < 0) continue;
                MarkupElement saved = current;
                current = item;
                var itemAttrs = item.Attributes.Select(a => (a.Name, a.ValueText)).ToList();
                itemAttrs.Add(("value", converter.Expression(raw)));
                current = saved;
                Replace(item.Span, Tag("item", itemAttrs.ToArray()));
            }
        }

        private void AEcho(MarkupElement e)
        {
            Code list = converter.ArrayVariable(e.GetAttributeValue("from") ?? "");
            string format = e.GetAttributeValue("format") ?? "[$index] - $value\\n";
            bool newline = format.EndsWith("\\n", StringComparison.Ordinal);
            if (newline) format = format.Substring(0, format.Length - 2);

            Know("index");
            Know("value");
            string template = converter.Template(format);
            string echo = newline ? $"<echo>{EscapeText(template)}</echo>" : $"<echo newline=\"no\">{EscapeText(template)}</echo>";
            Replace(e.Span, $"<for each=\"value\" index=\"index\" in=\"{EscapeAttribute(list.Text)}\">{echo}</for>");
        }

        private static readonly Regex StepPattern = new Regex(@"^\$(?<v>[A-Za-z_][A-Za-z0-9_]*)\s*\+\s*(?<n>\d+)$|^\$(?<v>[A-Za-z_][A-Za-z0-9_]*)->increment\(\)$", RegexOptions.Compiled);
        private static readonly Regex LimitPattern = new Regex(@"^\$(?<v>[A-Za-z_][A-Za-z0-9_]*) (?<op>lt|le) (?<limit>.+)$", RegexOptions.Compiled);

        private void ForLoop(MarkupElement e)
        {
            string v1Name = e.GetAttributeValue("to").Trim();
            string name = Name(v1Name);
            string step = converter.Arithmetic(e.GetAttributeValue("math") ?? "").Text;
            if (e.GetAttributeValue("math") is string m && !m.Contains("@math", StringComparison.Ordinal) && !m.Contains("->", StringComparison.Ordinal))
            {
                step = converter.Expression(m);
            }
            string test = Condition(e.GetAttributeValue("while"));

            Match s = StepPattern.Match(step);
            Match l = LimitPattern.Match(test);
            bool changedInside = Descendants(e).Any(d => DeclaredBy(d).Any(x => x.Equals(v1Name, StringComparison.OrdinalIgnoreCase)));
            bool declaredElsewhere = letAt.ContainsKey(v1Name);
            if (s.Success && l.Success && s.Groups["v"].Value == name && l.Groups["v"].Value == name && !changedInside)
            {
                // v1 started the counter at its current value and put it back afterwards, which is what a
                // KScript 2 for loop does with its own variable.
                string start = declaredElsewhere ? "$" + name : "0";
                string limit = l.Groups["limit"].Value;
                if (l.Groups["op"].Value == "le")
                {
                    limit = Regex.IsMatch(limit, @"^\d+$") ? (long.Parse(limit) + 1).ToString() : "(" + limit + ") + 1";
                }
                else if (!Regex.IsMatch(limit, @"^[\w$.]+(\(\))?(->\w+\(\))?$"))
                {
                    limit = "(" + limit + ")";
                }
                string by = s.Groups["n"].Success && s.Groups["n"].Value != "1" ? " step " + s.Groups["n"].Value : "";
                StartTag(e, "for", ("each", name), ("in", start + ".." + limit + by));
                ConvertChildren(e);
                return;
            }

            // The general case: a while loop that changes the counter at the end of each pass.
            StartTag(e, "while", ("test", test));
            string indent = IndentOf(e.Span.Start);
            if (!declaredElsewhere)
            {
                Insert(e.Span.Start, Tag("let", ("name", name), ("value", "0")) + "\n" + indent, order: 0);
            }
            if (e.GetAttribute("math") != null)
            {
                InsertAtEnd(e, Tag("set", ("name", name), ("value", step)));
            }
            if (Descendants(e).Any(d => d.Name == "continue"))
            {
                Note(e, $"<continue /> in this loop now skips the step that changes ${name}; move that <set> above the <continue />.");
            }
            ConvertChildren(e);
        }

        private void FunctionElement(MarkupElement e)
        {
            string name = e.GetAttributeValue("name") ?? "";
            string ps = e.GetAttributeValue("params");
            var parameters = (ps ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var attrs = new List<(string, string)> { ("name", name) };
            if (parameters.Count > 0) attrs.Add(("params", string.Join(", ", parameters.Select(Name))));
            StartTag(e, "fn", attrs.ToArray());
            if (e.Name == "thread")
            {
                Note(e, $"<thread> is now a <fn>; start it in the background with <spawn fn=\"{name}\" />.");
            }

            string savedMethod = method;
            HashSet<string> savedParams = methodParams;
            method = name;
            methodParams = new HashSet<string>(parameters, StringComparer.OrdinalIgnoreCase);
            try
            {
                ConvertChildren(e);
            }
            finally
            {
                method = savedMethod;
                methodParams = savedParams;
            }
        }

        private void CallElement(MarkupElement e)
        {
            string fn = (e.GetAttributeValue("method") ?? "").Trim();
            if (!Regex.IsMatch(fn, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                Note(e, $"The method name '{fn}' is worked out while the script runs; KScript 2 calls a function by its name.");
            }
            string args = e.GetAttributeValue("args");
            var attrs = new List<(string, string)> { ("fn", fn) };
            if (!string.IsNullOrWhiteSpace(args)) attrs.Add(("args", string.Join(", ", converter.ArgumentList(args))));
            bool thread = string.Equals(e.GetAttributeValue("type"), "thread", StringComparison.OrdinalIgnoreCase);
            Replace(e.Span, Tag(thread ? "spawn" : "call", attrs.ToArray()));
        }

        private void Echo(MarkupElement e)
        {
            var attrs = new List<(string, string)>();
            if (e.GetAttributeValue("trail_newline") is string trail && converter.Expression(trail, ValueHint.Bool) == "false") attrs.Add(("newline", "no"));
            if (string.Equals(e.GetAttributeValue("type"), "paragraph", StringComparison.OrdinalIgnoreCase))
            {
                Note(e, "type=\"paragraph\" isn't part of KScript 2; the text is printed as it is.");
            }
            foreach (MarkupAttribute a in e.Attributes.Where(a => a.Name is "value" or "format" or "newline")) attrs.Add((a.Name, a.ValueText));

            bool trim = e.GetAttributeValue("trim") is string t && converter.Expression(t, ValueHint.Bool) == "true";

            // KScript 2 tidies an echo's text, so a line whose spaces matter is printed as a value instead.
            string text = TextOf(e);
            bool noNewline = attrs.Any(a => a.Item1 == "newline");
            if (!trim && e.GetAttribute("value") == null && !e.ChildElements.Any() && !text.Contains('\n') && text.Trim().Length > 0
                && (text.Length - text.TrimStart().Length > 0 || noNewline && text.Length - text.TrimEnd().Length > 0))
            {
                attrs.Insert(0, ("value", converter.Expression(text, ValueHint.Text)));
                Replace(e.Span, Tag("echo", attrs.ToArray()));
                return;
            }
            if (e.Attributes.Any(a => a.Name is "trail_newline" or "trim" or "type"))
            {
                StartTag(e, "echo", attrs.ToArray());
            }
            TemplateContent(e, trim);
        }

        private void Input(MarkupElement e)
        {
            var attrs = new List<(string, string)> { ("to", Name(e.GetAttributeValue("to") ?? "")) };
            string type = (e.GetAttributeValue("type") ?? "").Trim().ToLowerInvariant();
            if (type == "number") attrs.Add(("type", "number"));
            if (type == "multiline") Note(e, "type=\"multiline\" reads one line in KScript 2.");
            if (e.GetAttribute("accepted_values") != null)
            {
                Note(e, "accepted_values isn't part of KScript 2; use <menu options=\"[...]\"> to offer a fixed set of answers.");
            }
            StartTag(e, "input", attrs.ToArray());
            TemplateContent(e, trim: false);
        }

        private void Picker(MarkupElement e)
        {
            var attrs = new List<(string, string)> { ("to", Name(e.GetAttributeValue("to") ?? "")) };
            if (string.Equals(e.GetAttributeValue("type")?.Trim(), "file", StringComparison.OrdinalIgnoreCase)) attrs.Add(("type", "file"));
            if (e.GetAttributeValue("error_prompt") is string error && error.Length > 0) attrs.Add(("error", converter.Template(error)));
            StartTag(e, "picker", attrs.ToArray());
            TemplateContent(e, trim: false);
        }

        private void Wait(MarkupElement e)
        {
            if (e.GetAttribute("ms") != null || e.GetAttribute("seconds") != null) return;
            string interval = (e.GetAttributeValue("interval") ?? "5s").Trim().ToLowerInvariant();
            Match m = Regex.Match(interval, @"^(\d+)\s*(mins?|ms|s)?$");
            if (!m.Success)
            {
                Note(e, $"Couldn't read the interval '{interval}'; use ms=\"...\" or seconds=\"...\".");
                return;
            }
            string unit = m.Groups[2].Value;
            string tag = unit == "ms" ? Tag("wait", ("ms", m.Groups[1].Value))
                // v1 counted minutes as seconds; keep what the script did.
                : Tag("wait", ("seconds", m.Groups[1].Value));
            if (unit.StartsWith("min", StringComparison.Ordinal))
            {
                Note(e, "KScript 1 waited this many seconds for \"min\"; the converted wait keeps that. Multiply by 60 if you meant minutes.");
            }
            Replace(e.Span, tag);
        }

        /// <summary>Converts the element's text as a template, in place.</summary>
        private void TemplateContent(MarkupElement e, bool trim)
        {
            if (e.SelfClosing || e.ContentSpan.Length == 0) return;
            if (e.ChildElements.Any())
            {
                Note(e, $"<{e.Name}> has elements inside it; they were kept as they are.");
                return;
            }
            string text = TextOf(e);
            if (trim) text = text.Trim();
            string template = converter.Template(text);

            string escaped = EscapeText(template);
            if (escaped != source[e.ContentSpan])
            {
                Replace(e.ContentSpan, escaped);
            }
        }

        #endregion

        #region Edits

        private static IEnumerable<MarkupElement> Descendants(MarkupElement e)
        {
            foreach (MarkupElement child in e.ChildElements)
            {
                yield return child;
                foreach (MarkupElement d in Descendants(child)) yield return d;
            }
        }

        private string TextOf(MarkupElement e) => string.Concat(e.Children.OfType<MarkupText>().Select(t => t.Value));

        private void Note(MarkupElement e, string message)
        {
            int line = source.GetPosition(e.Span.Start).Line + 1;
            if (Notes.Any(n => n.Line == line && n.Message == message)) return;
            Notes.Add(new MigrationNote(line, message));
            string indent = IndentOf(e.Span.Start);
            Insert(e.Span.Start, "<!-- " + Migrator.CommentPrefix + " " + message.Replace("--", "- -") + " -->\n" + indent, order: -1);
        }

        private void StartTag(MarkupElement e, string name, params (string Name, string Value)[] attrs)
        {
            string tag = e.SelfClosing ? Tag(name, attrs) : OpenTag(name, attrs);
            Replace(e.StartTagSpan, tag);
            if (!e.SelfClosing && e.EndNameSpan is TextSpan end && name != e.Name)
            {
                Replace(end, name);
            }
        }

        /// <summary>Inserts a line as the last statement inside the element, before its end tag.</summary>
        private void InsertAtEnd(MarkupElement e, string line)
        {
            if (e.SelfClosing || e.EndNameSpan == null) return;
            int endTag = e.EndNameSpan.Value.Start - 2;
            string indent = IndentOf(e.Span.Start);
            string before = source.Text.Substring(e.ContentSpan.Start, endTag - e.ContentSpan.Start);
            if (before.Contains('\n'))
            {
                Insert(endTag, unit + line + "\n" + indent, order: 2);
            }
            else
            {
                Insert(endTag, line, order: 2);
            }
        }

        private void Replace(TextSpan span, string text) => edits.Add((span.Start, span.End, text, edits.Count + 100));

        private void Insert(int at, string text, int order) => edits.Add((at, at, text, order * 1000000 + edits.Count));

        private string Apply()
        {
            var sb = new StringBuilder();
            int pos = 0;
            foreach (var edit in edits.OrderBy(x => x.Start).ThenBy(x => x.End > x.Start ? 1 : 0).ThenBy(x => x.Order))
            {
                if (edit.Start < pos) continue;
                sb.Append(source.Text, pos, edit.Start - pos);
                sb.Append(edit.Text);
                pos = edit.End;
            }
            sb.Append(source.Text, pos, source.Length - pos);
            return sb.ToString();
        }

        internal static string Tag(string name, params (string Name, string Value)[] attrs)
        {
            var sb = new StringBuilder("<").Append(name);
            foreach ((string n, string v) in attrs)
            {
                if (v == null) continue;
                sb.Append(' ').Append(n).Append("=\"").Append(EscapeAttribute(v)).Append('"');
            }
            return sb.Append(" />").ToString();
        }

        internal static string OpenTag(string name, params (string Name, string Value)[] attrs)
        {
            string tag = Tag(name, attrs);
            return tag.Substring(0, tag.Length - 3) + ">";
        }

        internal static string EscapeAttribute(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;").Replace("\n", "&#10;").Replace("\r", "");

        internal static string EscapeText(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;");

        private string IndentOf(int offset)
        {
            int i = offset;
            while (i > 0 && source.Text[i - 1] != '\n')
            {
                if (!char.IsWhiteSpace(source.Text[i - 1])) return "";
                i--;
            }
            return source.Text.Substring(i, offset - i);
        }

        internal static string ChildIndent(SourceText source, MarkupElement parent)
        {
            MarkupElement first = parent.ChildElements.FirstOrDefault();
            if (first != null)
            {
                int i = first.Span.Start;
                while (i > 0 && source.Text[i - 1] != '\n' && char.IsWhiteSpace(source.Text[i - 1])) i--;
                if (i == 0 || source.Text[i - 1] == '\n') return source.Text.Substring(i, first.Span.Start - i);
            }
            return "  ";
        }

        private string IndentUnit()
        {
            string child = ChildIndent(source, root);
            return child.Length > 0 ? child : "  ";
        }

        #endregion
    }
}
