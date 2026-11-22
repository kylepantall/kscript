using System.Globalization;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Types built on other types: refinement types (<c>type Age with [int] { min: 0 }</c>), joined types
    /// (<c>type Admin with [User, Auditable]</c>), and values written for a declared type: a bare <c>@kyle</c> for a
    /// handle, <c>red</c> for an enum, <c>H4cmW12in</c> for a type with a pattern. Literals are checked against the
    /// type's rules where they are written.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>The built-in types, the semantic ones included: a &lt;type&gt; can't take these names.</summary>
        private static IEnumerable<string> SemanticTypeNames => KScript.Metadata.NativeTypes.Names;

        /// <summary>The first native types, whose names ignore case (Handle is handle), as they always have.</summary>
        private static readonly string[] FirstNativeTypes = { "email", "password", "handle", "uuid", "regex" };

        /// <summary>Text types: a refinement of one reads raw words. port is a number, so it isn't one.</summary>
        private static bool IsTextRoot(string root) => root == "string" || (KScript.Metadata.NativeTypes.IsNative(root) && root != "port");

        /// <summary>The types named in with="..." (<c>with [User, Auditable]</c>), without brackets.</summary>
        private static List<string> WithNames(MarkupElement type)
        {
            string with = type.GetAttributeValue("with")?.Trim();
            if (string.IsNullOrEmpty(with))
            {
                return new List<string>();
            }
            if (with.StartsWith('[') && with.EndsWith(']'))
            {
                with = with.Substring(1, with.Length - 2);
            }
            return with.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        }

        private MarkupElement TypeElementNamed(string name) =>
            name == null ? null : TypeElementIn(result.Markup, name) ?? ImportedTypeElement(name);

        private MarkupElement TypeElementIn(MarkupDocument doc, string name) =>
            doc.DescendantElements().FirstOrDefault(e => IsTypeElement(e) && e.GetAttributeValue("name")?.Trim().TrimStart('$') == name);

        /// <summary>The parsed files this one imports, by path, for the types declared in them.</summary>
        private Dictionary<string, MarkupDocument> importedMarkup;

        /// <summary>
        /// A &lt;type&gt; declared in an imported file, so a type made with kscript make native (<c>type Sku with [string]</c>
        /// in types/sku.kscript) reads raw values and checks literals in the files that import it, as it does in its own file.
        /// The bases it is built on are found in the imported files too.
        /// </summary>
        private MarkupElement ImportedTypeElement(string name)
        {
            importedMarkup ??= new Dictionary<string, MarkupDocument>(StringComparer.Ordinal);
            foreach (string path in result.Symbols.Where(s => s.SourcePath != null).Select(s => s.SourcePath).Distinct())
            {
                if (!importedMarkup.TryGetValue(path, out MarkupDocument doc))
                {
                    string text = files.ReadText(path);
                    doc = text == null ? null : MarkupParser.Parse(new SourceText(text), MarkupParser.KindFromPath(path), path);
                    importedMarkup[path] = doc;
                }
                MarkupElement found = doc == null ? null : TypeElementIn(doc, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        /// <summary>
        /// A type named where a value is declared (<c>sku: Sku</c>, <c>fn f(p: Percent)</c>) uses the import that brought it
        /// in, so the import isn't reported as unused and go to definition works on the name.
        /// </summary>
        private void ReferenceImportedType(string text, TextSpan span)
        {
            string name = TypeInference.WithoutTypeArguments(text ?? string.Empty).Trim().TrimEnd('?').TrimEnd();
            while (name.EndsWith("[]", StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - 2).TrimEnd();
            }
            Symbol symbol = result.Symbols.FirstOrDefault(s => s.SourcePath != null && s.Kind == SymbolKind.Constant && s.Name == name);
            if (symbol != null)
            {
                result.References.Add(new SymbolReference(symbol, span, false, false));
            }
        }

        /// <summary>A &lt;type&gt;, or a KScript.Data &lt;model&gt; (a type whose records are kept in a table).</summary>
        private bool IsTypeElement(MarkupElement e) =>
            (IsKScriptElementName(e, "type") && catalog.GetElement("type")?.Package == "KScript.Core")
            || IsModelElement(e);

        private bool IsModelElement(MarkupElement e) =>
            IsKScriptElementName(e, "model") && catalog.GetElement("model")?.Package == "KScript.Data";

        private static bool IsBuiltInTypeName(string name) =>
            BuiltInTypes.Contains(name, StringComparer.OrdinalIgnoreCase) || FirstNativeTypes.Contains(name, StringComparer.OrdinalIgnoreCase) || KScript.Metadata.NativeTypes.IsNative(name);

        /// <summary>The built-in type a name means, in its own case: Handle is handle, but Color is a script's own type.</summary>
        private static string BuiltInTypeName(string name) => KScript.Metadata.NativeTypes.IsNative(name) ? name : name.ToLowerInvariant();

        /// <summary>
        /// The built-in type a refinement type comes down to (<c>Username with [Handle]</c>, <c>Handle with [handle]</c>: handle),
        /// or null for a record type. A refinement type has one base, which is a built-in type or another refinement type.
        /// </summary>
        private string RefinedRoot(MarkupElement type, int depth = 0)
        {
            List<string> bases = WithNames(type);
            if (bases.Count != 1 || depth > 32)
            {
                return null;
            }
            string name = TypeInference.WithoutTypeArguments(bases[0]);
            if (name.EndsWith("[]", StringComparison.Ordinal))
            {
                return "list";
            }
            if (IsBuiltInTypeName(name))
            {
                return BuiltInTypeName(name);
            }
            MarkupElement inner = TypeElementNamed(name);
            return inner != null && inner != type ? RefinedRoot(inner, depth + 1) : null;
        }

        /// <summary>The refinement types from <paramref name="type"/> down to its root, outermost first.</summary>
        private List<MarkupElement> RefinementChain(MarkupElement type)
        {
            var chain = new List<MarkupElement>();
            MarkupElement t = type;
            while (t != null && chain.Count < 32 && !chain.Contains(t))
            {
                chain.Add(t);
                List<string> bases = WithNames(t);
                t = bases.Count == 1 ? TypeElementNamed(TypeInference.WithoutTypeArguments(bases[0])) : null;
            }
            return chain;
        }

        /// <summary>
        /// How text written raw (unquoted) for <paramref name="type"/> is read: "handle" (<c>@kyle</c>), "word" (<c>red</c>,
        /// <c>H4cmW12in</c>, <c>my-post</c>) or null, where a bare word keeps its usual meaning. Matches the runtime.
        /// </summary>
        private string RawTextKindOf(string type, Scope scope, int offset)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }
            string t = type.Trim();
            if (t.EndsWith("?", StringComparison.Ordinal))
            {
                t = t.Substring(0, t.Length - 1).TrimEnd();
            }
            bool hasArgs = t.Contains('(');
            t = TypeInference.WithoutTypeArguments(t);
            if (t.EndsWith("[]", StringComparison.Ordinal))
            {
                return null;
            }
            if (IsBuiltInTypeName(t))
            {
                string lower = BuiltInTypeName(t);
                return KScript.Metadata.NativeTypes.Get(lower) is { } native ? (lower == "port" ? null : native.RawKind) : hasArgs && lower == "string" ? "word" : null;
            }
            Symbol symbol = scope?.Lookup(t, offset) ?? result.RootScope.Lookup(t, int.MaxValue);
            if (symbol?.EnumValues != null || result.Markup.DescendantElements().Any(e => IsKScriptElementName(e, "enum") && e.GetAttributeValue("name")?.Trim() == t))
            {
                return "word";
            }
            MarkupElement element = TypeElementNamed(t);
            if (element == null)
            {
                return null;
            }
            string root = RefinedRoot(element);
            if (root != null)
            {
                return root == "handle" ? "handle" : IsTextRoot(root) ? "word" : null;
            }
            return RefinementChain(element).Any(e => e.GetAttribute("pattern") != null || e.GetAttribute("parse") != null) ? "word" : null;
        }

        /// <summary>
        /// A value written for a declared type (a let, a field default, an argument). Returns true when it is raw text for the
        /// type (so it isn't analyzed as an expression); either way a literal is checked against the type's rules.
        /// </summary>
        private bool CheckTypedValue(DecodedText value, string type, Scope scope, int offset)
        {
            if (value == null || type == null)
            {
                return false;
            }
            string kind = RawTextKindOf(type, scope, offset);
            ParseResult parsed = ExpressionParser.ParseExpression(value.Value);
            TextSpan span = TrimmedSpan(value);
            if (kind != null && (!parsed.Success || Interpreter.IsRawWord(parsed.Root, kind, null, null)))
            {
                CheckLiteralFits(value.Value.Trim(), type, span, scope, offset);
                return true;
            }
            if (parsed.Success && parsed.Root is LiteralNode { Kind: LiteralKind.String or LiteralKind.Email or LiteralKind.Uuid or LiteralKind.Url or LiteralKind.SlashPath or LiteralKind.Duration or LiteralKind.Regex or LiteralKind.Number } literal)
            {
                object text = literal.Kind == LiteralKind.Number ? double.Parse(literal.Value, CultureInfo.InvariantCulture) : literal.Value;
                CheckLiteralFits(text, type, value.ToSource(literal.Span), scope, offset);
            }
            return false;
        }

        /// <summary>Reports, at the literal, a value that can't be <paramref name="type"/>: a bad email, an out of range Age, a word not in an enum.</summary>
        private void CheckLiteralFits(object literal, string type, TextSpan span, Scope scope, int offset)
        {
            string problem = LiteralProblem(literal, type, scope, offset);
            if (problem != null)
            {
                Report(span, DiagnosticSeverity.Error, DiagnosticCodes.InvalidTypedValue, problem);
            }
        }

        private string LiteralProblem(object literal, string type, Scope scope, int offset)
        {
            string t = type.Trim().TrimEnd('?').TrimEnd();
            string name = TypeInference.WithoutTypeArguments(t);
            if (name.EndsWith("[]", StringComparison.Ordinal))
            {
                return null;
            }
            string shown = literal is string s ? s : Convert.ToString(literal, CultureInfo.InvariantCulture);
            if (IsBuiltInTypeName(name))
            {
                string lower = BuiltInTypeName(name);
                if (SemanticTypeNames.Contains(lower))
                {
                    if (!(literal is string) && lower != "password" && !(literal is double && NativeParsers.Get(lower)?.TakesNumbers == true))
                    {
                        return null;
                    }
                    if (!SemanticTypes.TryConvert(lower, literal, out object converted, out string reason))
                    {
                        return lower == "password" ? $"This can't be a password: {(string.IsNullOrEmpty(reason) ? "a password is text, in quotes" : reason)}." : $"'{shown}' isn't {Article(lower)}: {reason}.";
                    }
                    return TypeArgumentProblem(t, lower, converted);
                }
                bool fits = lower is "int" or "float" or "number" ? literal is double : lower == "string" && literal is string;
                return fits ? TypeArgumentProblem(t, lower, literal) : null;
            }

            Symbol symbol = scope?.Lookup(name, offset) ?? result.RootScope.Lookup(name, int.MaxValue);
            if (symbol?.EnumValues is List<string> values && literal is string word)
            {
                if (values.Any(v => string.Equals(v, word, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }
                string close = Suggestions.Closest(word, values);
                return $"'{word}' isn't a {name}." + (close != null ? $" Did you mean {close}?" : string.Empty) + $" Use one of: {string.Join(", ", values)}.";
            }

            MarkupElement element = TypeElementNamed(name);
            if (element == null)
            {
                return null;
            }
            string root = RefinedRoot(element);
            if (root == null)
            {
                // A record read from text: its pattern must match.
                MarkupElement withPattern = RefinementChain(element).FirstOrDefault(e => e.GetAttribute("pattern") != null);
                if (withPattern != null && literal is string text && withPattern.GetAttribute("parse") == null
                    && RegexValue.TryParse(withPattern.GetAttributeValue("pattern"), out RegexValue regex, out _) && !SafeMatch(regex, text))
                {
                    return $"'{text}' doesn't fit {name}: it must match {withPattern.GetAttributeValue("pattern").Trim()}.";
                }
                return null;
            }

            object value = literal;
            if (SemanticTypeNames.Contains(root))
            {
                if (!(literal is string))
                {
                    return null;
                }
                if (!SemanticTypes.TryConvert(root, literal, out value, out string reason))
                {
                    return $"'{shown}' doesn't fit {name}: {reason}.";
                }
            }
            else if (root is "int" or "float" or "number")
            {
                if (!(literal is double))
                {
                    return null;
                }
            }
            else if (root == "string" && !(literal is string))
            {
                return null;
            }

            // Each refinement, innermost first, as the runtime checks them. A type that normalizes changes the value first,
            // so the check stops there; validate: rules need the running script.
            List<MarkupElement> chain = RefinementChain(element);
            chain.Reverse();
            foreach (MarkupElement level in chain)
            {
                if (level.ChildElements.Any(c => IsKScriptElementName(c, "normalize")))
                {
                    return null;
                }
                TypeRules rules = RulesOf(level);
                if (rules == null)
                {
                    return null;
                }
                string problem = rules.CheckPlain(value, item => PlainAs(root, item)).FirstOrDefault();
                if (problem != null)
                {
                    return $"'{shown}' doesn't fit {name}: it {problem}.";
                }
            }
            return null;
        }

        /// <summary>A uuid(v: 4), email(domains: [...]) or password(min: 12) literal that breaks the arguments given where it is used.</summary>
        private static string TypeArgumentProblem(string type, string root, object value)
        {
            int open = type.IndexOf('(');
            if (open < 0 || !type.EndsWith(")", StringComparison.Ordinal))
            {
                return null;
            }
            ParseResult parsed = ExpressionParser.ParseExpression("{" + type.Substring(open + 1, type.Length - open - 2) + "}");
            if (!parsed.Success || !(parsed.Root is MapNode map))
            {
                return null;
            }
            var options = new List<KeyValuePair<string, object>>();
            foreach (MapEntryNode entry in map.Entries)
            {
                object plain = TypeRules.PlainValue(entry.Value);
                if (plain == TypeRules.Unplain.Value)
                {
                    return null;
                }
                options.Add(new KeyValuePair<string, object>(entry.Key, plain));
            }
            TypeRules rules = TypeRules.FromOptions(options, out _);
            string problem = rules?.CheckPlain(value, item => PlainAs(root, item)).FirstOrDefault();
            return problem != null ? $"This doesn't fit {type}: it {problem}." : null;
        }

        /// <summary>The plain rules (min, max, pattern, one_of, not) written on a &lt;type&gt;, or null if they can't be read.</summary>
        private static TypeRules RulesOf(MarkupElement type)
        {
            var options = new List<KeyValuePair<string, object>>();
            foreach (string name in TypeRules.Attributes)
            {
                MarkupAttribute attr = type.GetAttribute(name);
                if (attr?.Value == null)
                {
                    continue;
                }
                object value = attr.ValueText;
                if (name is "one_of" or "not")
                {
                    value = TypeRules.PlainItems(attr.ValueText);
                    if (value == null)
                    {
                        return null;
                    }
                }
                options.Add(new KeyValuePair<string, object>(name, value));
            }
            return TypeRules.FromOptions(options, out _);
        }

        private static object PlainAs(string root, object item)
        {
            if (item is string text && SemanticTypeNames.Contains(root) && SemanticTypes.TryConvert(root, text, out object converted, out _))
            {
                return converted;
            }
            return item;
        }

        private static bool SafeMatch(RegexValue regex, string text)
        {
            try
            {
                return regex.Regex.IsMatch(text);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return true;
            }
        }
    }
}
