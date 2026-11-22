using System.Text;
using System.Text.RegularExpressions;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    // KS6013: the names a script declares follow the language's own. Variables, functions, parameters and fields are
    // snake_case; types, models, enums, error types and jobs are PascalCase. Names from elsewhere (an import, a
    // built-in, front matter) are not this file's to choose, so they are left alone.
    public sealed partial class Analyzer
    {
        private static readonly Regex SnakeCase = new Regex("^_*[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly Regex PascalCase = new Regex("^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

        /// <summary>Something that reads as a name at all. A template's placeholder (<c>type {{ pascal }}</c>) isn't one, and has no style to hold to.</summary>
        private static readonly Regex Nameable = new Regex("^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.Compiled);

        private void CheckNamingStyle()
        {
            var seen = new HashSet<(int, string)>();
            foreach (Symbol symbol in result.Symbols.ToList())
            {
                if (symbol.SourcePath != null || symbol.Kind is SymbolKind.Import or SymbolKind.BuiltIn or SymbolKind.FrontMatter
                    || string.IsNullOrEmpty(symbol.Name) || !Nameable.IsMatch(symbol.Name) || !seen.Add((symbol.DeclarationSpan.Start, symbol.Name)))
                {
                    continue;
                }
                TextSpan? written = WrittenName(symbol.DeclarationSpan, symbol.Name);
                if (written == null)
                {
                    continue;
                }
                bool type = TypeInference.IsTypeSymbol(symbol) && symbol.DeclaringElement?.Name is "type" or "model" or "enum" or "error" or "job";
                if (type)
                {
                    if (!PascalCase.IsMatch(symbol.Name))
                    {
                        string kind = symbol.DeclaringElement.Name == "error" ? "error type" : symbol.DeclaringElement.Name;
                        ReportName(written.Value, $"Name the {kind} {ToPascalCase(symbol.Name)}: types, models, enums, error types and jobs are PascalCase.", null);
                    }
                    foreach (FieldInfo field in symbol.Shape?.Instance?.Fields.Values.ToList() ?? new List<FieldInfo>())
                    {
                        if (field.Declaration is TextSpan at && WrittenName(at, field.Name) is TextSpan fieldSpan && Nameable.IsMatch(field.Name) && !SnakeCase.IsMatch(field.Name) && seen.Add((fieldSpan.Start, field.Name)))
                        {
                            ReportName(fieldSpan, $"Name the field {ToSnakeCase(field.Name)}: fields are snake_case.", null);
                        }
                    }
                    continue;
                }
                if (SnakeCase.IsMatch(symbol.Name) || symbol.Name == "_")
                {
                    continue;
                }
                string what = symbol.Kind switch
                {
                    SymbolKind.Function => "function",
                    SymbolKind.Parameter => "parameter",
                    SymbolKind.Constant => "constant",
                    _ => "variable",
                };
                string snake = ToSnakeCase(symbol.Name);
                ReportName(written.Value, $"Name the {what} {snake}: variables, functions, parameters and fields are snake_case.", RenameInFile(symbol, snake));
            }
        }

        private void ReportName(TextSpan span, string message, IReadOnlyList<QuickFix> fixes) =>
            Report(span, DiagnosticSeverity.Warning, DiagnosticCodes.NamingStyle, message, fixes);

        /// <summary>Where the name itself is written at a declaration (without a $ before it), or null when the declaration isn't that name as text.</summary>
        private TextSpan? WrittenName(TextSpan span, string name)
        {
            if (span.Length == 0 || span.End > source.Length)
            {
                return null;
            }
            string text = source.Text.Substring(span.Start, span.Length);
            int at = text.IndexOf(name, StringComparison.Ordinal);
            if (at < 0 || text.Substring(0, at).Trim().TrimStart('$').Length > 0)
            {
                return null;
            }
            int end = at + name.Length;
            if (end < text.Length && (Lexer.IsIdentChar(text[end]) || text[end] == '-'))
            {
                return null;
            }
            return new TextSpan(span.Start + at, name.Length);
        }

        /// <summary>
        /// The fix that renames a variable everywhere this file uses it. Only for a variable of this file alone: a
        /// function, a constant or a type may be used by a file that imports this one, and a parameter is named by
        /// its callers (<c>greet(first_name: 'Ada')</c>), so those are renamed with the editor's rename.
        /// </summary>
        private IReadOnlyList<QuickFix> RenameInFile(Symbol symbol, string name)
        {
            if (symbol.Kind is not (SymbolKind.Variable or SymbolKind.LoopVariable) || !SnakeCase.IsMatch(name)
                || result.Symbols.Any(s => s.Name == name) || name is "true" or "false" or "null" or "and" or "or" or "not" or "in" or "is")
            {
                return null;
            }
            var changes = new List<TextChange>();
            foreach (Symbol same in result.Symbols.Where(s => s.Name == symbol.Name && s.Kind == symbol.Kind && s.SourcePath == null))
            {
                foreach (SymbolReference reference in result.ReferencesTo(same))
                {
                    if (WrittenName(reference.Span, symbol.Name) is not TextSpan at)
                    {
                        return null;
                    }
                    if (!changes.Any(c => c.Span.Start == at.Start))
                    {
                        changes.Add(new TextChange(at, name));
                    }
                }
            }
            return changes.Count == 0 ? null : new[] { new QuickFix($"Rename to {name}", changes.OrderBy(c => c.Span.Start).ToArray()) };
        }

        /// <summary>userName, user-name and UserName as user_name.</summary>
        internal static string ToSnakeCase(string name)
        {
            var text = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '-' || c == ' ' || c == '.')
                {
                    text.Append('_');
                    continue;
                }
                bool boundary = char.IsUpper(c) && i > 0 && name[i - 1] != '_' && name[i - 1] != '-'
                    && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (boundary)
                {
                    text.Append('_');
                }
                text.Append(char.ToLowerInvariant(c));
            }
            return Regex.Replace(text.ToString(), "(?<=[a-z0-9])_+", "_");
        }

        /// <summary>user_name and userName as UserName.</summary>
        internal static string ToPascalCase(string name) =>
            string.Concat(name.Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
    }
}
