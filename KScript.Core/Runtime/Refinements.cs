using System.Globalization;
using System.Text.RegularExpressions;

using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>
    /// One <c>validate</c> rule: an expression that must be true, and what to say when it isn't. In a refinement
    /// type the value is <c>$value</c>; in a struct, each field is its own variable.
    /// </summary>
    public sealed class Validator
    {
        internal Validator(DecodedText test, string message)
        {
            Test = test;
            Message = message;
        }

        internal DecodedText Test { get; }

        public string Message { get; }

        public string Source => Test.Value.Trim();

        /// <summary>The problem to report: the message, or the rule itself.</summary>
        internal string Failure => Message ?? $"doesn't pass the rule {Source}";
    }

    /// <summary>
    /// The rules of a refinement type, a field, or a type given arguments where it is used (<c>uuid(v: 4)</c>):
    /// <c>min</c>, <c>max</c>, <c>pattern</c>, <c>one_of</c> and <c>not</c>, options of the base type (such as a
    /// uuid's <c>v</c>), and <c>validate</c> and <c>normalize</c>. All but the last two are plain values.
    /// </summary>
    public sealed class TypeRules
    {
        /// <summary>The rules every type can have, as attributes of &lt;type&gt; and &lt;field&gt; and as type arguments.</summary>
        public static readonly string[] Attributes = { "min", "max", "pattern", "one_of", "not" };

        /// <summary>The plain rules and options by name: numbers, text, bools and lists.</summary>
        internal Dictionary<string, object> Options { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        internal DecodedText Normalize { get; set; }
        internal List<Validator> Validators { get; } = new List<Validator>();

        internal RegexValue Pattern { get; private set; }

        internal string MinText => Text("min");
        internal string MaxText => Text("max");
        internal string PatternText => Options.TryGetValue("pattern", out object p) && p is RegexValue r ? r.Text : Text("pattern");
        internal List<object> OneOf => Options.TryGetValue("one_of", out object v) ? v as List<object> : null;
        internal List<object> Not => Options.TryGetValue("not", out object v) ? v as List<object> : null;

        private string Text(string key) => Options.TryGetValue(key, out object v) && v != null ? Values.ToStr(v).Trim() : null;

        public bool IsEmpty => Options.Count == 0 && Normalize == null && Validators.Count == 0;

        /// <summary>
        /// Rules from plain values: type arguments (<c>uuid(v: 4)</c>) or a type's defaults in kscript.json. Returns
        /// null with <paramref name="error"/> when one isn't valid.
        /// </summary>
        public static TypeRules FromOptions(IEnumerable<KeyValuePair<string, object>> options, out string error)
        {
            var rules = new TypeRules();
            foreach (KeyValuePair<string, object> kv in options)
            {
                object value = kv.Value;
                // one_of: 'a, b' is the same as one_of: ['a', 'b'].
                if (kv.Key is "one_of" or "not" or "domains" && value is string list)
                    value = list.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => (object)p).ToList();
                rules.Options[kv.Key] = value;
            }
            error = rules.Compile();
            return error == null ? rules : null;
        }

        /// <summary>Reads the rule attributes of a &lt;type&gt; or &lt;field&gt;.</summary>
        internal static TypeRules Read(MarkupElement e, ElementRun run)
        {
            var options = new List<KeyValuePair<string, object>>();
            foreach (string name in Attributes)
            {
                MarkupAttribute attr = e.GetAttribute(name);
                if (attr?.Value == null) continue;
                object value = attr.ValueText;
                if (name is "one_of" or "not")
                {
                    value = PlainItems(attr.ValueText)
                        ?? throw run.Error($"{name}=\"{attr.ValueText.Trim()}\" must list plain values, such as {name}=\"a, b\" or {name}=\"['a', 'b']\".", name, "Syntax");
                }
                options.Add(new KeyValuePair<string, object>(name, value));
            }
            return FromOptions(options, out string error) ?? throw run.Error(error, "pattern", "Syntax");
        }

        private string Compile()
        {
            if (PatternText == null) return null;
            // A regex value or literal (/^[a-z]+$/i) keeps its flags; plain text is the pattern itself.
            if (Options["pattern"] is RegexValue value)
            {
                Pattern = value;
                return null;
            }
            if (!RegexValue.TryParse(PatternText, out RegexValue parsed, out string reason)) return $"pattern {PatternText} isn't a regular expression: {reason}.";
            Pattern = parsed;
            return null;
        }

        /// <summary>These rules on top of <paramref name="defaults"/>: a rule given here replaces the default one.</summary>
        internal TypeRules Over(TypeRules defaults)
        {
            if (defaults == null) return this;
            var merged = new TypeRules { Normalize = Normalize ?? defaults.Normalize };
            foreach (KeyValuePair<string, object> kv in defaults.Options) merged.Options[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, object> kv in Options) merged.Options[kv.Key] = kv.Value;
            merged.Validators.AddRange(defaults.Validators);
            merged.Validators.AddRange(Validators);
            merged.Compile();
            return merged;
        }

        /// <summary>A <c>&lt;validate test message&gt;</c> or <c>&lt;normalize value&gt;</c> inside a type.</summary>
        internal bool TryAddChild(MarkupElement e, ElementRun run, bool allowNormalize)
        {
            switch (e.Name)
            {
                case "validate":
                    DecodedText test = e.GetAttribute("test")?.Value ?? throw run.Error($"<validate> needs a test: test=\"$value > 0\".", null, "Syntax");
                    Validators.Add(new Validator(test, e.GetAttributeValue("message")));
                    return true;
                case "normalize":
                    if (!allowNormalize) throw run.Error($"<normalize> only goes in a type made with with=\"...\" from one other type, such as <type name=\"Slug\" with=\"string\">.", null, "Syntax");
                    if (Normalize != null) throw run.Error($"A type can only have one <normalize>.", null, "Syntax");
                    Normalize = e.GetAttribute("value")?.Value ?? throw run.Error($"<normalize> needs a value: value=\"$value->lower()\".", null, "Syntax");
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The problems with <paramref name="value"/> (already converted to the base type). <paramref name="convert"/>
        /// converts the items of one_of and not to the base type, so they compare the same way.
        /// </summary>
        internal IEnumerable<string> Check(Interpreter interpreter, object value, Scope closure, Module module, Func<object, object> convert)
        {
            foreach (string problem in CheckPlain(value, convert, interpreter)) yield return problem;
            foreach (Validator v in Validators)
            {
                Scope s = closure.Child();
                s.Declare("value", value);
                if (!Values.Truthy(interpreter.EvaluateText(v.Test, s, module))) yield return v.Failure;
            }
        }

        /// <summary>Every rule but validate: these need no interpreter, so the editor checks literals with them too.</summary>
        public IEnumerable<string> CheckPlain(object value, Func<object, object> convert) => CheckPlain(value, convert, null);

        /// <summary>Checks the remaining static rules after an editor has tested one_of and not as sets.</summary>
        internal bool AcceptsKnownChoice(object value) => !CheckPlain(value, null, null, checkMembership: false).Any();

        private IEnumerable<string> CheckPlain(object value, Func<object, object> convert, Interpreter interpreter, bool checkMembership = true)
        {
            if (MinText != null && Below(value, MinText, out string minMessage)) yield return minMessage;
            if (MaxText != null && Above(value, MaxText, out string maxMessage)) yield return maxMessage;
            if (Pattern != null)
            {
                bool matches;
                try
                {
                    matches = interpreter != null && value is string text
                        ? interpreter.MatchesRefinement(Pattern, text)
                        : Pattern.Regex.IsMatch(TextOf(value));
                }
                catch (RegexMatchTimeoutException)
                {
                    matches = false;
                }
                if (!matches) yield return $"must match {PatternText}";
            }
            if (checkMembership && OneOf is List<object> oneOf)
            {
                List<object> items = oneOf.Select(convert).ToList();
                if (!items.Any(o => Values.StrictEquals(o, value))) yield return $"must be one of {string.Join(", ", items.Select(o => o == null ? "null" : Values.ToStr(o)))}";
            }
            if (checkMembership && Not is List<object> reserved && reserved.Select(convert).Any(o => Values.StrictEquals(o, value))) yield return $"is reserved ('{Values.ToStr(value)}')";
            foreach (string problem in OptionProblems(value)) yield return problem;
        }

        /// <summary>The options of semantic types: a uuid's version, an email's domains, a password's strength rules.</summary>
        private IEnumerable<string> OptionProblems(object value)
        {
            switch (value)
            {
                case UuidValue uuid when Text("v") is string v && v != "any" && v != uuid.Version.ToString(CultureInfo.InvariantCulture):
                    yield return $"must be a version {v} UUID, but it is version {uuid.Version}";
                    break;
                case EmailAddress email when Options.TryGetValue("domains", out object d) && d is List<object> domains:
                {
                    List<string> allowed = domains.Select(x => Values.ToStr(x).Trim().TrimStart('@').ToLowerInvariant()).ToList();
                    if (!allowed.Any(a => email.Domain == a || email.Domain.EndsWith("." + a, StringComparison.Ordinal)))
                        yield return $"must be at {(allowed.Count == 1 ? allowed[0] : "one of " + string.Join(", ", allowed))}";
                    break;
                }
                case NativeText native:
                    foreach (string problem in SemanticTypes.OptionProblems(native.TypeName, native, Options)) yield return problem;
                    break;
                case PasswordValue password:
                    foreach (string problem in password.RuleProblems(0, null, Flag("upper"), Flag("lower"), Flag("digit"), Flag("symbol"),
                        Flag("common"), null, Options.TryGetValue("score", out object score) ? Values.ToInt(score, "score") : 0))
                    {
                        yield return problem;
                    }
                    break;
            }
        }

        private bool Flag(string key) => Options.TryGetValue(key, out object v) && Values.Truthy(v);

        /// <summary>
        /// The items of one_of or not when they are written as literals: <c>a, b</c>, or a list such as
        /// <c>['a', 'b']</c> or <c>[@admin, @root]</c> (bare handles). Null when they need working out.
        /// </summary>
        internal static List<object> PlainItems(string text)
        {
            string raw = (text ?? string.Empty).Trim();
            if (!raw.StartsWith('[')) return raw.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => (object)p).ToList();
            ParseResult parsed = ExpressionParser.ParseExpression(raw);
            return parsed.Success ? PlainValue(parsed.Root) as List<object> : null;
        }

        /// <summary>A literal as a value: numbers, text, bools, lists of them, bare words, and bare <c>@name</c> (as the text '@name').</summary>
        internal static object PlainValue(ExprNode node)
        {
            switch (node)
            {
                case LiteralNode l:
                    return l.Kind switch
                    {
                        LiteralKind.Number => double.Parse(l.Value, CultureInfo.InvariantCulture),
                        LiteralKind.Bool => l.Value == "true",
                        LiteralKind.Null => null,
                        LiteralKind.Regex => RegexValue.TryParse(l.Value, out RegexValue regex, out _) ? regex : Unplain.Value,
                        _ => l.Value,
                    };
                case CommandCallNode { ArgumentList: null } r:
                    return "@" + r.Name;
                case IdentifierNode word:
                    // A bare word is its text, as for an enum: one_of: [small, large], algorithm: bcrypt.
                    return word.Name;
                case UnaryNode { Operator: "-", Operand: LiteralNode { Kind: LiteralKind.Number } n }:
                    return -double.Parse(n.Value, CultureInfo.InvariantCulture);
                case ListNode list:
                {
                    var items = new List<object>();
                    foreach (ExprNode item in list.Items)
                    {
                        object v = PlainValue(item);
                        if (v is Unplain) return Unplain.Value;
                        items.Add(v);
                    }
                    return items;
                }
                default:
                    return Unplain.Value;
            }
        }

        /// <summary>Marks a value that isn't plain.</summary>
        internal sealed class Unplain
        {
            public static readonly Unplain Value = new Unplain();
        }

        /// <summary>The text pattern matches: a handle with its @, a password's own text.</summary>
        private static string TextOf(object value) => value switch
        {
            PasswordValue p => p.Secret,
            ISemanticText t => t.Text,
            _ => Values.ToStr(value),
        };

        /// <summary>What min and max measure: numbers by value, text by length (a handle without its @), lists and maps by count.</summary>
        private static double? Size(object value, out string unit)
        {
            unit = "character";
            switch (value)
            {
                case double d: unit = null; return d;
                case List<object> list: unit = "item"; return list.Count;
                case Dictionary<string, object> map: unit = "key"; return map.Count;
                case HandleName h: return h.Name.Length;
                case PasswordValue p: return p.Secret.Length;
                case ISemanticText t: return t.Text.Length;
                case string s: return s.Length;
                default: return null;
            }
        }

        private static bool Below(object value, string limit, out string message) => OutOfRange(value, limit, true, out message);

        private static bool Above(object value, string limit, out string message) => OutOfRange(value, limit, false, out message);

        private static bool OutOfRange(object value, string limit, bool isMin, out string message)
        {
            message = null;
            if (value is DateTime date)
            {
                if (!DateTime.TryParse(limit, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime bound)) return false;
                message = $"must be on or {(isMin ? "after" : "before")} {limit}";
                return isMin ? date < bound : date > bound;
            }
            if (Size(value, out string unit) is not double size || !double.TryParse(limit, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return false;
            string least = isMin ? "at least" : "at most";
            message = unit == null ? $"must be {least} {limit}" : unit == "character" ? $"must be {least} {limit} character{(n == 1 ? string.Empty : "s")}" : $"must have {least} {limit} {unit}{(n == 1 ? string.Empty : "s")}";
            return isMin ? size < n : size > n;
        }

        /// <summary>
        /// Why these rules don't suit a type whose built-in root is <paramref name="root"/> (a min that isn't a
        /// number, an option the type doesn't have), or null. <paramref name="owner"/> names the type in the message.
        /// </summary>
        public string ProblemFor(string root, string owner)
        {
            bool numeric = root is "int" or "float" or "number";
            IReadOnlyList<string> options = SemanticTypes.OptionsOf(root);
            foreach (string key in Options.Keys)
            {
                if (!Attributes.Contains(key) && !options.Contains(key))
                    return $"{owner} has no rule or option '{key}'. Use {string.Join(", ", Attributes.Concat(options))}.";
            }
            foreach (string key in new[] { "min", "max" })
            {
                string text = Text(key);
                if (text == null) continue;
                if (root == "date")
                {
                    if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _)) return $"{key} on {owner} must be a date, such as 2020-01-01.";
                    continue;
                }
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || (!numeric && (n < 0 || n != Math.Floor(n))))
                    return $"{key} on {owner} must be {(numeric ? "a number" : "a whole number (a length or a count)")}.";
            }
            if (root == "password" && (OneOf != null || Not != null)) return $"{owner} is a password, so it can't list values with one_of or not: a password is never compared with other values.";
            if (Text("v") is string v && v is not ("4" or "8" or "any") && !(int.TryParse(v, out int version) && version is >= 1 and <= 8))
                return $"v on {owner} must be a UUID version from 1 to 8, or any.";
            if (Text("algorithm") is string a && a.ToLowerInvariant() is not ("argon2id" or "bcrypt")) return $"algorithm on {owner} must be argon2id or bcrypt.";
            if (Options.TryGetValue("domains", out object d) && d is not List<object>) return $"domains on {owner} must be a list, such as domains: ['example.com'].";
            return null;
        }
    }

    /// <summary>
    /// A type made from one other type plus rules: <c>type Age with [int] { min: 0; max: 150 }</c>. Its values are
    /// values of the base type (an Age is a number), so it works anywhere the base type does.
    /// </summary>
    public sealed class RefinedType : IKObject, IDeclaredType
    {
        internal RefinedType(string name, TypeRef baseType, TypeRules rules, string description, Scope closure, Module module)
        {
            Name = name;
            Base = baseType;
            Rules = rules;
            Description = description;
            Closure = closure;
            Module = module;
            if (rules.Normalize == null && rules.Validators.Count == 0 && baseType.Args == null && baseType.ListDepth == 0 && !baseType.Optional)
                StaticStringRoot = baseType.IsBuiltIn && baseType.Name == "string" ? this : (baseType.Inline as RefinedType)?.StaticStringRoot;
        }

        public string Name { get; }
        public TypeRef Base { get; }
        public TypeRules Rules { get; }
        public string Description { get; }

        internal Scope Closure { get; }
        internal Module Module { get; }
        /// <summary>The terminal string declaration when every rule in the chain is independent of script state.</summary>
        internal RefinedType StaticStringRoot { get; }

        public string TypeName => "type";

        public string ToText() => Name;

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "name" => Name,
                "base" => Base.ToString(),
                "description" => Description,
                _ => null,
            };
            return value != null || name == "description";
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "from":
                    throw new KScriptException($"->from() is gone: call the type to make a value of it, {Name}($value).", "Retired");
                case "check":
                    result = Shapes.Problems(call.Interpreter, call.Arg(0), TypeRef.Of(this), Closure, Module);
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        /// <summary>The built-in type at the bottom of the <c>with</c> chain, such as int or handle. Lists give "list".</summary>
        internal string Root() => Base.Root(Closure);
    }
}
