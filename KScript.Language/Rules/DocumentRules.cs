using System.Globalization;

using KScript.Language.Analysis;
using KScript.Language.Formatting;
using KScript.Projects;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Rules
{
    /// <summary>Project rules written as declarative KScript statements. Reading a rule file never runs it.</summary>
    public sealed class DocumentRules
    {
        public const string FileName = "kscript.rules.kscript";
        public static DocumentRules Default { get; } = LoadBase();

        private readonly List<LintRule> lint = new List<LintRule>();
        private readonly Dictionary<string, bool> tags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> models = new HashSet<string>(StringComparer.Ordinal);
        private readonly FormatSettings style = new FormatSettings { EndOfLine = null };
        private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();

        private DocumentRules() { }

        public Diagnostic Error => diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);

        public static DocumentRules ForProject(string root, IFileSystem files = null)
        {
            if (root == null) return Default;
            files ??= DiskFileSystem.Instance;
            string path = Path.Combine(root, FileName);
            if (!files.FileExists(path)) return Default;
            string text = files.ReadText(path);
            if (text != null) return Parse(CodeParser.Parse(text));
            DocumentRules rules = new DocumentRules();
            rules.Invalid(default, $"Could not read {FileName}.");
            return rules;
        }

        public static bool IsRuleFile(string path) =>
            string.Equals(Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase);

        internal static DocumentAnalysis Analyze(string text, string path)
        {
            var source = new SourceText(text);
            MarkupDocument markup = CodeParser.Parse(source);
            var result = new DocumentAnalysis(source, path, DocumentKind.Script, markup)
            {
                RootScope = new Scope(null, new TextSpan(0, source.Length)),
            };
            result.Diagnostics.AddRange(Parse(markup).diagnostics);
            return result;
        }

        internal void Check(DocumentAnalysis document)
        {
            if (Error is Diagnostic error)
            {
                document.Diagnostics.Add(new Diagnostic(default, DiagnosticSeverity.Error, error.Code, $"{FileName}: {error.Message}"));
                return;
            }
            if (lint.Count == 0) return;
            foreach (MarkupElement element in document.Markup.DescendantElements())
            {
                foreach (LintRule rule in lint)
                {
                    var comparison = document.Kind == DocumentKind.Script ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    if (!element.Name.Equals(rule.Element, comparison)) continue;
                    bool present = element.Attributes.Any(a => a.Name.Equals(rule.Attribute, comparison));
                    if (present == rule.Forbid)
                    {
                        document.Diagnostics.Add(new Diagnostic(element.NameSpan, rule.Severity, rule.Code, rule.Message));
                    }
                }
            }
        }

        internal bool? WrapTag(string name)
        {
            if (tags.TryGetValue(name, out bool wrap) || tags.TryGetValue("*", out wrap)) return wrap;
            return ReferenceEquals(this, Default) ? null : Default.WrapTag(name);
        }

        internal bool WrapModel(string name) =>
            models.Contains(name) || models.Contains("*") || (!ReferenceEquals(this, Default) && Default.WrapModel(name));

        internal bool HasModelRules => models.Count > 0 || Default.models.Count > 0;

        internal void ApplyStyle(FormatOptions options) => options.Apply(style);

        private static DocumentRules LoadBase()
        {
            using Stream stream = typeof(DocumentRules).Assembly.GetManifestResourceStream("KScript.Formatting.Base")
                ?? throw new InvalidOperationException("The built-in KScript formatting style is missing.");
            using var reader = new StreamReader(stream);
            DocumentRules rules = Parse(CodeParser.Parse(reader.ReadToEnd()));
            if (rules.Error is Diagnostic error)
                throw new InvalidOperationException($"The built-in KScript formatting style is invalid: {error.Message}");
            return rules;
        }

        private static DocumentRules Parse(MarkupDocument document)
        {
            DocumentRules rules = new DocumentRules();
            rules.diagnostics.AddRange(document.Diagnostics);
            if (rules.Error != null) return rules;

            foreach (MarkupElement statement in document.RootElements.SelectMany(e => e.ChildElements))
            {
                if (statement.Children.Count > 0 || statement.Name is not ("Lint" or "Format" or "Style"))
                {
                    rules.Invalid(statement.NameSpan, "Write a Lint(...), Format(...) or Style(...) declaration. Rule files contain declarations only.");
                    continue;
                }
                if (statement.Name == "Style")
                {
                    rules.ReadStyle(document.Source, statement);
                    continue;
                }
                var args = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (MarkupAttribute attribute in statement.Attributes)
                {
                    Token value = ReadLiteral(document.Source, attribute);
                    if (value.Kind == TokenKind.String)
                    {
                        args[attribute.Name] = value.Text;
                        continue;
                    }
                    rules.Invalid(attribute.Span, $"Write a quoted literal for '{attribute.Name}'.");
                }

                if (statement.Name == "Lint") rules.AddLint(statement, args);
                else rules.AddFormat(statement, args);
            }
            return rules;
        }

        private static Token ReadLiteral(SourceText source, MarkupAttribute attribute)
        {
            // Lex the source spelling so an expression is never mistaken for its evaluated value.
            string raw = source[attribute.Span];
            int colon = raw.IndexOf(':');
            if (colon < 0) return default;
            var lexer = new Lexer(raw, colon + 1);
            Token token = lexer.Next();
            return token.Error == null && lexer.Next().Kind == TokenKind.EndOfInput ? token : default;
        }

        private void ReadStyle(SourceText source, MarkupElement statement)
        {
            foreach (MarkupAttribute attribute in statement.Attributes)
            {
                Token value = ReadLiteral(source, attribute);
                string name = attribute.Name;
                if (value.Kind == TokenKind.Number && int.TryParse(value.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    switch (name)
                    {
                        case "indent_size" when number is >= 1 and <= 16: style.IndentSize = number; continue;
                        case "print_width" when number >= 20: style.PrintWidth = number; continue;
                        case "max_blank_lines" when number >= 0: style.MaxBlankLines = number; continue;
                    }
                }
                if (value.Kind == TokenKind.String && name == "end_of_line" && value.Text is "auto" or "lf" or "crlf")
                {
                    style.EndOfLine = value.Text;
                    continue;
                }
                if (value.IsWord("true") || value.IsWord("false"))
                {
                    bool enabled = value.IsWord("true");
                    if (name == "use_tabs")
                    {
                        style.UseTabs = enabled;
                        continue;
                    }
                    if (FormatSettings.RuleNames.Contains(name, StringComparer.Ordinal))
                    {
                        style.Rules[name] = enabled;
                        continue;
                    }
                }
                Invalid(attribute.Span, $"Invalid Style setting '{name}'. Use literal values and a recognized style name.");
            }
        }

        private void AddLint(MarkupElement statement, Dictionary<string, string> args)
        {
            string code = args.GetValueOrDefault("code");
            string element = args.GetValueOrDefault("element");
            string missing = args.GetValueOrDefault("missing_attribute");
            string forbidden = args.GetValueOrDefault("forbidden_attribute");
            string message = args.GetValueOrDefault("message");
            string severity = args.GetValueOrDefault("severity") ?? "warning";
            if (code == null || code.Length != 6 || !code.StartsWith("KS9", StringComparison.Ordinal)
                || !code.Skip(3).All(char.IsAsciiDigit) || lint.Any(rule => rule.Code == code)
                || string.IsNullOrWhiteSpace(element) || string.IsNullOrWhiteSpace(message)
                || (missing == null) == (forbidden == null) || string.IsNullOrWhiteSpace(missing ?? forbidden)
                || severity is not ("error" or "warning" or "info" or "hint")
                || args.Keys.Any(key => key is not ("code" or "element" or "message" or "severity" or "missing_attribute" or "forbidden_attribute")))
            {
                Invalid(statement.NameSpan, "Lint needs a unique KS9xxx code, element, message and one of missing_attribute or forbidden_attribute. Severity can be error, warning, info or hint.");
                return;
            }
            lint.Add(new LintRule(code, element, missing ?? forbidden, forbidden != null, message, LintRules.ToSeverity(severity)));
        }

        private void AddFormat(MarkupElement statement, Dictionary<string, string> args)
        {
            if (args.Count == 2 && args.TryGetValue("element", out string element) && !string.IsNullOrWhiteSpace(element)
                && args.GetValueOrDefault("attributes") is "one_per_line" or "inline")
            {
                tags[element] = args["attributes"] == "one_per_line";
                return;
            }
            if (args.Count == 2 && args.TryGetValue("model", out string model) && !string.IsNullOrWhiteSpace(model)
                && args.GetValueOrDefault("options") == "one_per_line")
            {
                models.Add(model);
                return;
            }
            Invalid(statement.NameSpan, "Format needs element and attributes ('one_per_line' or 'inline'), or model and options ('one_per_line').");
        }

        private void Invalid(TextSpan span, string message) =>
            diagnostics.Add(new Diagnostic(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidToolingRule, message));

        private sealed record LintRule(string Code, string Element, string Attribute, bool Forbid, string Message, DiagnosticSeverity Severity);
    }
}
