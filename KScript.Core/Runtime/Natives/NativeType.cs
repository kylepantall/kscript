using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// The contract every native type keeps: email, url, duration, semver, a package's isbn. A native type is a
    /// subclass of this, and the abstract members make it say, in one place, everything the language does with its
    /// values: how text becomes one (<see cref="Parse"/>), how it is written without quotes (<see cref="RawText"/>),
    /// what == means (<see cref="Equality"/>), whether &lt; and sorting work (<see cref="Ordering"/>), whether a for
    /// loop can walk it (<see cref="Iteration"/>), which operators it takes (<see cref="DeclareOperators"/>), its
    /// fields and methods (<see cref="DeclareMembers"/>), the string methods it keeps and the values the editor
    /// offers. A type that leaves one out doesn't compile, so a new type can't quietly miss an operator or fall back
    /// to comparing its text.
    /// <para>
    /// The runtime (operators, comparisons, loops, truthiness) and the editor (inferred types, hovers, checks) both
    /// read the same declarations, through <see cref="NativeTypeRegistry"/>, so they can't drift apart. Register a
    /// type with <c>p.NativeType(new IsbnType())</c> in a package, or add the language's own to
    /// <see cref="NativeTypeRegistry"/>'s list.
    /// </para>
    /// </summary>
    public abstract class NativeType
    {
        private NativeTypeInfo info;
        private NativeOperators operators;

        // ---- What it is ---------------------------------------------------------------------------------------

        /// <summary>The type's name, as written in <c>x: url</c>: lower case letters, digits and _.</summary>
        public abstract string Name { get; }

        /// <summary>One sentence for hovers and the docs.</summary>
        public abstract string Description { get; }

        /// <summary>A value of the type, as written in code, for docs and hovers: <c>1h30m</c>, <c>'4242 4242 4242 4242'</c>.</summary>
        public abstract string Example { get; }

        // ---- Making values --------------------------------------------------------------------------------------

        /// <summary>
        /// The value <paramref name="text"/> means, tidied (a <see cref="NativeText"/> or the type's own class), or
        /// null and why it doesn't fit, in words that follow "isn't a url: " (such as "it has no host").
        /// </summary>
        public abstract (object Value, string Reason) Parse(string text);

        /// <summary>How a value is written without quotes where the type is expected: a word (1h30m, GB) or an @handle.</summary>
        public abstract RawText RawText { get; }

        /// <summary>True when a number is read as its text too (a port takes 8080 as well as '8080').</summary>
        public virtual bool TakesNumbers => false;

        /// <summary>Arguments it takes where it is used, besides min, max, pattern, one_of and not: uuid(v: 4).</summary>
        public virtual IReadOnlyList<string> Options => Array.Empty<string>();

        /// <summary>Why a value breaks the options given where the type is used, such as ip(version: 4).</summary>
        public virtual IEnumerable<string> OptionProblems(object value, IReadOnlyDictionary<string, object> options) => Enumerable.Empty<string>();

        // ---- What its values do ---------------------------------------------------------------------------------

        /// <summary>What == and != mean for its values.</summary>
        public abstract Equality Equality { get; }

        /// <summary>Whether &lt;, &lt;=, &gt;, &gt;= and sorting work, and in which order.</summary>
        public abstract Ordering Ordering { get; }

        /// <summary>Whether a for loop can walk a value, and what it gives.</summary>
        public abstract Iteration Iteration { get; }

        /// <summary>
        /// The operators its values take: +, -, *, /, %, **, in, unary -, ranges (a..b) and when a value counts
        /// as false. Declare none with an empty body: then only == (and the comparisons, if ordered) work.
        /// </summary>
        protected abstract void DeclareOperators(NativeOperators o);

        /// <summary>Its fields ($url.host) and methods ($version->bump('minor')).</summary>
        protected abstract void DeclareMembers(NativeMembers m);

        /// <summary>The string methods its values keep, such as contains and starts_with. Others need ->to_string() first.</summary>
        public abstract IReadOnlyList<string> StringMethods { get; }

        /// <summary>Values the editor offers where the type is expected (UTC, Europe/London for a timezone). Empty for none.</summary>
        public abstract IReadOnlyList<string> Completions { get; }

        // ---- Optional details -----------------------------------------------------------------------------------

        /// <summary>"a" or "an", when the usual rule gets it wrong.</summary>
        public virtual string Article => null;

        /// <summary>The OpenAPI / JSON Schema string format, such as uri or ipv4, when there is one.</summary>
        public virtual string JsonFormat => null;

        /// <summary>Its values print masked (a card number, a password), so they never show up in logs or JSON.</summary>
        public virtual bool Secret => false;

        /// <summary>The package that adds it, or null for the language's own types. Set when it is registered.</summary>
        public string Package { get; internal set; }

        // ---- What the runtime and the editor read --------------------------------------------------------------

        /// <summary>The declared operators. Declared once, the first time they are asked for.</summary>
        public NativeOperators Operators
        {
            get
            {
                if (operators != null) return operators;
                var o = new NativeOperators(this);
                DeclareOperators(o);
                return operators = o;
            }
        }

        internal void Members(NativeMembers m) => DeclareMembers(m);

        /// <summary>What the parser, analyzer and docs know about it (KScript.Syntax can't see this class).</summary>
        public NativeTypeInfo Info => info ??= new NativeTypeInfo(Name, Description, Example, StringMethods)
        {
            Package = Package,
            RawKind = RawText == RawText.Handle ? "handle" : "word",
            Secret = Secret,
            Options = Options,
            JsonFormat = JsonFormat,
            Article = Article,
            Operators = Operators.Infos,
            Equality = Equality.Kind,
            Ordering = Ordering.Kind,
            Iterates = Iteration.ItemType,
            Falsy = Operators.FalsySummary,
            Completions = Completions,
        };

        /// <summary>The value of this type <paramref name="value"/> means: itself, or text (or a number, if it takes them) parsed. Null when it doesn't fit.</summary>
        public object Coerce(object value)
        {
            if (value is IKObject k && k.TypeName == Name) return value;
            if (value is double && TakesNumbers) value = Values.ToStr(value);
            string text = value switch { string s => s, ISemanticText other => other.Text, _ => null };
            return text == null ? null : Parse(text).Value;
        }

        public override string ToString() => Name;
    }

    /// <summary>How a native value is written without quotes.</summary>
    public enum RawText
    {
        /// <summary>A word: 1h30m, GB, 10.0.0.0/8, example.com.</summary>
        Word,

        /// <summary>An @name: @joan.</summary>
        Handle,
    }

    /// <summary>What == means for a native type's values.</summary>
    public sealed class Equality
    {
        private Equality(string kind, Func<object, object, bool> equals)
        {
            Kind = kind;
            Equals = equals;
        }

        /// <summary>"text", "value" or "never".</summary>
        public string Kind { get; }

        /// <summary>Its own rule, or null for the tidied text.</summary>
        public new Func<object, object, bool> Equals { get; }

        /// <summary>Equal when the tidied text is the same: 'Kyle@Example.COM' == 'kyle@example.com'.</summary>
        public static readonly Equality ByText = new Equality("text", null);

        /// <summary>Can't be compared with == at all (a password: use ->verify()).</summary>
        public static readonly Equality Never = new Equality("never", null);

        /// <summary>Its own rule, given two values of the type: 1.4.2+build.1 == 1.4.2.</summary>
        public static Equality By(Func<object, object, bool> equals) => new Equality("value", equals);
    }

    /// <summary>Whether a native type's values have an order, and which.</summary>
    public sealed class Ordering
    {
        private Ordering(string kind, Func<object, object, int> compare)
        {
            Kind = kind;
            Compare = compare;
        }

        /// <summary>"none", "text" or "value".</summary>
        public string Kind { get; }

        /// <summary>Its own order, given two values of the type, or null.</summary>
        public Func<object, object, int> Compare { get; }

        /// <summary>No order: &lt; and friends are an error that says so. Sorting needs a key, such as ->sort(fn($x) => $x->to_string()).</summary>
        public static readonly Ordering None = new Ordering("none", null);

        /// <summary>Alphabetical by the tidied text (country codes, slugs).</summary>
        public static readonly Ordering ByText = new Ordering("text", null);

        /// <summary>Its own order, given two values of the type: semver 1.4.2 &lt; 1.10.0, duration 90s &lt; 2m.</summary>
        public static Ordering By(Func<object, object, int> compare) => new Ordering("value", compare);
    }

    /// <summary>Whether a for loop can walk a native value.</summary>
    public sealed class Iteration
    {
        private Iteration(string itemType, Func<object, IEnumerable<object>> items)
        {
            ItemType = itemType;
            Items = items;
        }

        /// <summary>The type of what it gives, or null when it can't be walked.</summary>
        public string ItemType { get; }

        public Func<object, IEnumerable<object>> Items { get; }

        /// <summary>A for loop over it is an error that says so.</summary>
        public static readonly Iteration None = new Iteration(null, null);

        /// <summary>A for loop gives these, of type <paramref name="itemType"/>: a cidr block gives its ip addresses.</summary>
        public static Iteration Of(string itemType, Func<object, IEnumerable<object>> items) => new Iteration(itemType, items);
    }
}
