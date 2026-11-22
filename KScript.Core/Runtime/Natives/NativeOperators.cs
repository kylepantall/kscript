using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// The operators a native type declares, in its <see cref="NativeType.DeclareOperators"/>. Each one names the
    /// types on both sides and the result's type, so the runtime knows what to run and the editor knows what it
    /// gives, from the same line. One side is always the declaring type: duration declares <c>date + duration</c>.
    /// </summary>
    public sealed class NativeOperators
    {
        /// <summary>The operators a native type can declare with <see cref="Binary"/>.</summary>
        public static readonly IReadOnlySet<string> BinaryOperators = new HashSet<string>(StringComparer.Ordinal) { "+", "-", "*", "/", "%", "**", "in" };

        /// <summary>The type names a side can have besides native types.</summary>
        public static readonly IReadOnlySet<string> BasicTypes = new HashSet<string>(StringComparer.Ordinal) { "number", "string", "bool", "date", "list", "map" };

        private readonly NativeType owner;
        private readonly List<NativeOperator> binary = new List<NativeOperator>();
        private readonly List<NativeOperator> ranges = new List<NativeOperator>();
        private readonly List<NativeOperatorInfo> infos = new List<NativeOperatorInfo>();

        internal NativeOperators(NativeType owner) => this.owner = owner;

        /// <summary>The binary operators, ranges and unary -, as the editor sees them.</summary>
        public IReadOnlyList<NativeOperatorInfo> Infos => infos;

        internal IReadOnlyList<NativeOperator> BinaryList => binary;

        internal IReadOnlyList<NativeOperator> RangeList => ranges;

        /// <summary>Unary -, or null.</summary>
        internal NativeOperator Negation { get; private set; }

        /// <summary>When a value counts as false, or null when it never does.</summary>
        internal Func<object, bool> IsFalsy { get; private set; }

        public string FalsySummary { get; private set; }

        /// <summary>
        /// <c>left op right</c> gives a <paramref name="result"/>. <paramref name="run"/> gets the two values already as
        /// the declared types: text on a native side has been parsed (<c>$d + '30m'</c>), so it can cast them.
        /// </summary>
        public NativeOperators Binary(string op, string left, string right, string result, string summary, Func<object, object, object> run)
        {
            if (!BinaryOperators.Contains(op))
                throw new ConventionException($"Native type {owner.Name} declares operator '{op}': a native type declares {string.Join(", ", BinaryOperators)}, unary - (Negate), ranges (Range) and Falsy. == comes from Equality and < > from Ordering.");
            if (left != owner.Name && right != owner.Name)
                throw new ConventionException($"Native type {owner.Name} declares {left} {op} {right}: one side of an operator a type declares is that type.");
            CheckType(left, $"{left} {op} {right}");
            CheckType(right, $"{left} {op} {right}");
            CheckType(result, $"the result of {left} {op} {right}");
            if (binary.Any(b => b.Operator == op && b.Left == left && b.Right == right))
                throw new ConventionException($"Native type {owner.Name} declares {left} {op} {right} twice.");
            Require(summary, $"{left} {op} {right}");
            binary.Add(new NativeOperator(op, left, right, result, (a, b, _) => run(a, b)));
            infos.Add(new NativeOperatorInfo(op, left, right, result, summary, owner.Name));
            return this;
        }

        /// <summary><c>-$value</c> gives a <paramref name="result"/>.</summary>
        public NativeOperators Negate(string result, string summary, Func<object, object> run)
        {
            if (Negation != null) throw new ConventionException($"Native type {owner.Name} declares unary - twice.");
            CheckType(result, "the result of unary -");
            Require(summary, "unary -");
            Negation = new NativeOperator("-", null, owner.Name, result, (_, v, _) => run(v));
            infos.Add(new NativeOperatorInfo("-", null, owner.Name, result, summary, owner.Name));
            return this;
        }

        /// <summary>
        /// <c>start..end step s</c> walks values of <paramref name="itemType"/> (this type, or date when this type is
        /// the step), <paramref name="stepType"/> apart. <paramref name="run"/> gets the start, the end and the step
        /// (null when none was written) and gives the values, end excluded, as for numbers.
        /// </summary>
        public NativeOperators Range(string itemType, string stepType, string summary, Func<object, object, object, IEnumerable<object>> run)
        {
            if (itemType != owner.Name && stepType != owner.Name)
                throw new ConventionException($"Native type {owner.Name} declares a range of {itemType} by {stepType}: either the values or the step is that type.");
            CheckType(itemType, "a range's values");
            CheckType(stepType, "a range's step");
            if (ranges.Any(r => r.Left == itemType))
                throw new ConventionException($"Native type {owner.Name} declares ranges of {itemType} twice.");
            Require(summary, "a range");
            ranges.Add(new NativeOperator("..", itemType, stepType, "list", run));
            infos.Add(new NativeOperatorInfo("..", itemType, stepType, itemType + "[]", summary, owner.Name));
            return this;
        }

        /// <summary>A value counts as false in if, and, or, not and ?: when <paramref name="isFalsy"/> says so (0s for a duration). Without this, every value is true.</summary>
        public NativeOperators Falsy(string summary, Func<object, bool> isFalsy)
        {
            if (IsFalsy != null) throw new ConventionException($"Native type {owner.Name} declares Falsy twice.");
            Require(summary, "Falsy");
            IsFalsy = isFalsy;
            FalsySummary = summary;
            return this;
        }

        private void CheckType(string type, string where)
        {
            if (string.IsNullOrEmpty(type))
                throw new ConventionException($"Native type {owner.Name} declares {where} without a type.");
            string bare = type.EndsWith("[]", StringComparison.Ordinal) ? type[..^2] : type;
            if (!BasicTypes.Contains(bare) && !bare.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
                throw new ConventionException($"Native type {owner.Name} declares {where} with type '{type}': a side is {string.Join(", ", BasicTypes)} or a native type's name.");
        }

        private void Require(string summary, string what)
        {
            if (string.IsNullOrWhiteSpace(summary))
                throw new ConventionException($"Native type {owner.Name} declares {what} without a summary: the editor shows it on hover.");
        }
    }

    /// <summary>One declared operator, with the code that runs it.</summary>
    internal sealed class NativeOperator
    {
        public NativeOperator(string op, string left, string right, string result, Func<object, object, object, object> run)
        {
            Operator = op;
            Left = left;
            Right = right;
            Result = result;
            Run = run;
        }

        public string Operator { get; }
        public string Left { get; }
        public string Right { get; }
        public string Result { get; }

        /// <summary>(left, right, extra): a range's extra is its step.</summary>
        public Func<object, object, object, object> Run { get; }
    }
}
