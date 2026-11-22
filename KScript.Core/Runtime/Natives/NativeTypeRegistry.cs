using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// Every native type, as a <see cref="NativeType"/>: the language's own (listed in <see cref="BuiltIn"/>, one class
    /// per type in this folder) and those packages add. The interpreter asks it what an operator, a comparison, ==,
    /// a for loop or a truth test means for a native value, and the editor reads the same declarations through
    /// <see cref="NativeTypes"/>, so the two can't disagree.
    /// </summary>
    public static class NativeTypeRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, NativeType> Types = new Dictionary<string, NativeType>(StringComparer.Ordinal);

        /// <summary>The language's own native types. A new one is a class in this folder, added here.</summary>
        public static IReadOnlyList<NativeType> BuiltIn { get; } = new NativeType[]
        {
            new DurationType(),
            new UrlType(),
            new DomainType(),
            new IpType(),
            new CidrType(),
            new MacType(),
            new PortType(),
        };

        static NativeTypeRegistry()
        {
            foreach (NativeType type in BuiltIn) Register(type);
            LegacyNativeTypes.Register();
        }

        /// <summary>Adds a native type: its name, what the editor knows and its operators. Its members register with its package.</summary>
        public static void Register(NativeType type)
        {
            NativeTypeInfo info = type.Info;
            lock (Gate)
            {
                foreach (NativeOperatorInfo op in info.Operators)
                {
                    NativeOperatorInfo clash = NativeTypes.All.Where(t => t.Name != type.Name).SelectMany(t => t.Operators)
                        .FirstOrDefault(o => o.Operator == op.Operator && o.Left == op.Left && o.Right == op.Right);
                    if (clash != null)
                        throw new ConventionException($"Native type {type.Name} declares {op.Display}, which {clash.DeclaredBy} already declares: each operator has one meaning.");
                }
                NativeTypes.Register(info);
                Types[type.Name] = type;
            }
        }

        /// <summary>The native type with this name, or null (also null for a type not yet moved onto <see cref="NativeType"/>).</summary>
        public static NativeType Get(string name)
        {
            if (name == null) return null;
            lock (Gate) return Types.TryGetValue(name, out NativeType t) ? t : null;
        }

        public static IReadOnlyList<NativeType> All
        {
            get { lock (Gate) return Types.Values.ToList(); }
        }

        /// <summary>The native type of a value, or null for a value that isn't one.</summary>
        public static NativeType Of(object value) => value is IKObject k ? Get(k.TypeName) : null;

        /// <summary>The type name an operator side is declared with: number, string, date, a native type's name, ...</summary>
        public static string TypeOf(object value) => value switch
        {
            null => "null",
            double => "number",
            string => "string",
            bool => "bool",
            DateTime => "date",
            List<object> => "list",
            Dictionary<string, object> => "map",
            IKObject k => k.TypeName,
            _ => "any",
        };

        /// <summary>The value as a side of type <paramref name="type"/> needs it, or null when it isn't one.</summary>
        private static object As(string type, object value)
        {
            if (TypeOf(value) == type) return value;
            NativeType native = Get(type);
            return native != null && value is string or ISemanticText or double ? native.Coerce(value) : null;
        }

        private static bool Involves(object a, object b) => Of(a) != null || Of(b) != null;

        /// <summary>
        /// Runs <c>a op b</c> when a native type declares it for these values. False when none does; the caller then
        /// does what it does for plain values (text joins with +, numbers add).
        /// </summary>
        public static bool TryBinary(string op, object a, object b, out object result)
        {
            result = null;
            if (!Involves(a, b)) return false;
            List<NativeOperator> candidates = All.SelectMany(t => t.Operators.BinaryList).Where(o => o.Operator == op).ToList();
            string ta = TypeOf(a), tb = TypeOf(b);
            // The exact types first, then text on a native side parsed: $d + '30m'.
            foreach (NativeOperator o in candidates.Where(o => o.Left == ta && o.Right == tb).Concat(candidates))
            {
                object x = As(o.Left, a);
                object y = x == null ? null : As(o.Right, b);
                if (x == null || y == null) continue;
                result = o.Run(x, y, null);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The error for <c>a op b</c> when a native value is on one side and no type declares the pair: it says
        /// what the type does take. Null when the operator isn't arithmetic or text joins with +.
        /// </summary>
        public static string BinaryProblem(string op, object a, object b)
        {
            if (op == "in" || (op == "+" && (a is string || b is string))) return null;
            NativeType native = Of(a) ?? Of(b);
            if (native == null) return null;
            List<string> takes = NativeTypes.OperatorsFor(op, native.Name).Where(o => o.Left != null && o.Operator != "..").Select(o => o.Display).ToList();
            return $"'{op}' doesn't work on {Values.Describe(a)} and {Values.Describe(b)}." +
                (takes.Count > 0 ? $" With {native.Name} it takes {string.Join(", ", takes)}." : $" {Plural(native)} have no '{op}'.");
        }

        private static string Plural(NativeType t) => char.ToUpperInvariant(t.Name[0]) + t.Name[1..] + " values";

        /// <summary><c>-$value</c> for a native value that declares it.</summary>
        public static bool TryNegate(object value, out object result)
        {
            result = null;
            if (Of(value)?.Operators.Negation is not NativeOperator neg) return false;
            result = neg.Run(null, value, null);
            return true;
        }

        /// <summary>
        /// Compares two values when one is a native value: by the type's own order, by text, or an error for a type
        /// with no order. False when neither is native, so numbers, dates and text compare as they always have.
        /// </summary>
        public static bool TryCompare(object a, object b, string op, out int result)
        {
            result = 0;
            NativeType type = Of(a) ?? Of(b);
            if (type == null) return false;
            object x = As(type.Name, a), y = As(type.Name, b);
            if (x == null || y == null)
                throw new KScriptException($"Can't compare {Values.Describe(a)} with {Values.Describe(b)} using '{Symbol(op)}': both sides need to be {type.Info.WithArticle}.", "Type");
            switch (type.Ordering.Kind)
            {
                case "value":
                    result = type.Ordering.Compare(x, y);
                    return true;
                case "text":
                    result = string.CompareOrdinal(Values.ToStr(x), Values.ToStr(y));
                    return true;
                default:
                    throw new KScriptException($"{Plural(type)} have no order, so '{Symbol(op)}' can't compare them. Compare a field, or the text with ->to_string().", "Type");
            }
        }

        private static string Symbol(string op) => op switch { "lt" => "<", "le" => "<=", "gt" => ">", "ge" => ">=", _ => op };

        /// <summary>== for two values when one is a native value with its own rule. False when the usual rule applies.</summary>
        public static bool TryEquals(object a, object b, out bool equal)
        {
            equal = false;
            NativeType type = Of(a) ?? Of(b);
            if (type == null || type.Equality.Equals == null) return false;
            object x = As(type.Name, a), y = As(type.Name, b);
            equal = x != null && y != null && type.Equality.Equals(x, y);
            return true;
        }

        /// <summary>What a for loop over a native value gives. False when the value isn't native; an error when its type can't be walked.</summary>
        public static bool TryItems(object value, out IEnumerable<object> items)
        {
            items = null;
            NativeType type = Of(value);
            if (type == null) return false;
            if (type.Iteration.Items == null)
                throw new KScriptException($"<for> can't loop over {Values.Describe(value)}: {type.Name} values can't be walked.", "Type");
            items = type.Iteration.Items(value);
            return true;
        }

        /// <summary>Whether a native value counts as false. False (not decided) when it isn't native or its type declares no Falsy.</summary>
        public static bool TryFalsy(object value, out bool falsy)
        {
            falsy = false;
            if (Of(value)?.Operators.IsFalsy is not Func<object, bool> test) return false;
            falsy = test(value);
            return true;
        }

        /// <summary>
        /// <c>start..end step s</c> when the ends are native values or dates with a native step. False when no type
        /// declares a range for them, so number ranges work as before.
        /// </summary>
        public static bool TryRange(object start, object end, object step, out List<object> items)
        {
            items = null;
            string type = TypeOf(start) == "string" ? TypeOf(end) : TypeOf(start);
            foreach (NativeOperator range in All.SelectMany(t => t.Operators.RangeList).Where(r => r.Left == type))
            {
                object x = As(type, start), y = As(type, end);
                object s = step == null ? null : As(range.Right, step);
                if (x == null || y == null)
                    throw new KScriptException($"A range of {type} values needs {type} values at both ends, but got {Values.Describe(start)} and {Values.Describe(end)}.", "Type");
                if (step != null && s == null)
                    throw new KScriptException($"A range of {type} values steps by {(Get(range.Right)?.Info.WithArticle ?? "a " + range.Right)}, but got {Values.Describe(step)}.", "Type");
                items = new List<object>();
                foreach (object item in (IEnumerable<object>)range.Run(x, y, s))
                {
                    items.Add(item);
                    if (items.Count > 10_000_000) throw new KScriptException("The range is too large (more than 10 million values).", "Limit");
                }
                return true;
            }
            if (Of(start) is NativeType native)
                throw new KScriptException($"{Plural(native)} don't make ranges, so {Values.Describe(start)}..{Values.Describe(end)} doesn't work.", "Type");
            return false;
        }
    }
}
