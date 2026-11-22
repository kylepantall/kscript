using System;
using System.Collections.Generic;
using System.Linq;

namespace KScript.Metadata
{
    /// <summary>
    /// What the editor and the runtime know about a native type: text with a meaning of its own, such as <c>email</c>,
    /// <c>url</c> or <c>country</c>. It is checked and tidied when it is made, prints as its text and keeps the string
    /// methods that make sense for it. The parser and methods live in the runtime (KScript.Core); this is the part the
    /// parser, analyzer and docs share.
    /// </summary>
    public sealed class NativeTypeInfo
    {
        /// <summary>
        /// Made only by the runtime, from a <c>NativeType</c> (KScript.Core): a native type is declared by subclassing
        /// it, which makes it say what it supports, so there is no other way to make one.
        /// </summary>
        internal NativeTypeInfo(string name, string description, string example, IEnumerable<string> stringMethods = null)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') || !char.IsAsciiLetterLower(name[0]))
                throw new ArgumentException($"A native type's name is lower case letters, digits and _, such as ip or semver: '{name}' isn't.", nameof(name));
            Name = name;
            Description = description;
            Example = example;
            StringMethods = new HashSet<string>(stringMethods ?? NativeTypes.Searchable, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The type's name, as written in <c>x: url</c>.</summary>
        public string Name { get; }

        /// <summary>One sentence for hovers and the docs.</summary>
        public string Description { get; }

        /// <summary>A value of the type, for docs and hovers: <c>https://example.com/docs</c>.</summary>
        public string Example { get; }

        /// <summary>The string methods its values keep, such as contains and starts_with. Others need ->to_string() first.</summary>
        public HashSet<string> StringMethods { get; }

        /// <summary>The package that adds it: null for the language's own types.</summary>
        public string Package { get; set; }

        /// <summary>
        /// How a raw (unquoted) value is read where the type is expected: "word" (most types), "handle" (@name) or
        /// "slash" (slash_path: anything starting with /, spaces and a closing / included, is the text as written).
        /// </summary>
        public string RawKind { get; init; } = "word";

        /// <summary>Its values print masked (a card number, a password), so they never show up in logs or JSON.</summary>
        public bool Secret { get; init; }

        /// <summary>Arguments it takes where it is used, besides min, max, pattern, one_of and not: uuid(v: 4).</summary>
        public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

        /// <summary>The OpenAPI / JSON Schema string format, such as uri or ipv4, when there is one.</summary>
        public string JsonFormat { get; init; }

        /// <summary>"a" or "an", for messages: an email, a url.</summary>
        public string Article { get; init; }

        /// <summary>
        /// The operators its values work with, besides == and the comparisons its <see cref="Ordering"/> gives. A range
        /// is operator <c>...</c> with the values' type on the left and the step's on the right; unary - has no left.
        /// </summary>
        public IReadOnlyList<NativeOperatorInfo> Operators { get; init; } = Array.Empty<NativeOperatorInfo>();

        /// <summary>How == treats it: "text" (the same once tidied), "value" (its own rule) or "never" (a password).</summary>
        public string Equality { get; init; } = "text";

        /// <summary>How &lt; &gt; &lt;= &gt;= and sorting treat it: "none" (an error), "text" (alphabetical) or "value" (its own order, such as 1.4.2 &lt; 1.10.0).</summary>
        public string Ordering { get; init; } = "none";

        /// <summary>What a for loop over a value gives (the items' type name), or null when it can't be looped over.</summary>
        public string Iterates { get; init; }

        /// <summary>When a value counts as false in if, and, or and ?: (one sentence), or null when it is always true.</summary>
        public string Falsy { get; init; }

        /// <summary>Values the editor offers where the type is expected, such as UTC and Europe/London for a timezone.</summary>
        public IReadOnlyList<string> Completions { get; init; } = Array.Empty<string>();

        /// <summary>
        /// The named filters a list of the type takes in ->only(name) and ->without(name), such as releases and
        /// prereleases for semver, each with one sentence on what it keeps. Set once the type's members register.
        /// </summary>
        public IReadOnlyList<(string Name, string Summary)> ListFilters { get; internal set; } = Array.Empty<(string, string)>();

        public string WithArticle => (Article ?? ("aeiou".Contains(Name[0]) && Name != "uuid" && Name != "url" ? "an" : "a")) + " " + Name;
    }

    /// <summary>
    /// Every native type: the language's own (email, url, ip, country, ...) and those a package adds with
    /// <c>p.NativeType(...)</c>. The runtime registers a parser for each one under the same name.
    /// </summary>
    public static class NativeTypes
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, NativeTypeInfo> Types = new Dictionary<string, NativeTypeInfo>(StringComparer.Ordinal);

        /// <summary>The string methods most native types keep: searching, measuring, cutting up and showing their text.</summary>
        public static readonly string[] Searchable =
        {
            "contains", "starts_with", "ends_with", "index_of", "count", "slice", "matches", "truncate",
            "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json",
        };

        /// <summary>
        /// The language's own types register from KScript.Core (each is a NativeType subclass there). Asking for the
        /// types loads them, so the parser and editor see them even before a script runs.
        /// </summary>
        static NativeTypes()
        {
            Type catalog = Type.GetType("KScript.Runtime.NativeTypeRegistry, KScript.Core");
            if (catalog != null) System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(catalog.TypeHandle);
        }

        /// <summary>Adds a native type, as a package does when it loads. A name that is taken can't be reused.</summary>
        public static void Register(NativeTypeInfo info)
        {
            lock (Gate)
            {
                if (Types.TryGetValue(info.Name, out NativeTypeInfo existing) && existing.Package != info.Package)
                    throw new InvalidOperationException($"There is already a native type called {info.Name}" + (existing.Package != null ? $" (from {existing.Package})." : "."));
                Types[info.Name] = info;
            }
        }

        /// <summary>The native type with this name (case matters: Color is a script's own type, color is the native one).</summary>
        public static NativeTypeInfo Get(string name)
        {
            if (name == null) return null;
            lock (Gate) return Types.TryGetValue(name, out NativeTypeInfo info) ? info : null;
        }

        public static bool IsNative(string name) => Get(name) != null;

        /// <summary>Every native type, the language's own first.</summary>
        public static IReadOnlyList<NativeTypeInfo> All
        {
            get { lock (Gate) return Types.Values.ToList(); }
        }

        public static IEnumerable<string> Names => All.Select(t => t.Name);

        /// <summary>
        /// What <c>left op right</c> gives when a native type declares it (left and right are type names: duration,
        /// number, date, ...), or null. The editor infers results and checks operators with this.
        /// </summary>
        public static NativeOperatorInfo Operator(string op, string left, string right)
        {
            foreach (NativeTypeInfo t in All)
                foreach (NativeOperatorInfo o in t.Operators)
                    if (o.Operator == op && o.Left == left && o.Right == right) return o;
            return null;
        }

        /// <summary>Every operator a native type declares with this operator and one side of this type.</summary>
        public static IEnumerable<NativeOperatorInfo> OperatorsFor(string op, string type) =>
            All.SelectMany(t => t.Operators).Where(o => o.Operator == op && (o.Left == type || o.Right == type));
    }

    /// <summary>
    /// One operator a native type declares: <c>duration + duration</c> is a duration, <c>date - duration</c> is a date.
    /// A unary operator has no <see cref="Left"/>.
    /// </summary>
    public sealed class NativeOperatorInfo
    {
        public NativeOperatorInfo(string op, string left, string right, string result, string summary, string declaredBy)
        {
            Operator = op;
            Left = left;
            Right = right;
            Result = result;
            Summary = summary;
            DeclaredBy = declaredBy;
        }

        /// <summary>+, -, *, /, %, **, in, .. (a range) or - with no left (negation).</summary>
        public string Operator { get; }

        /// <summary>The left side's type, or null for a unary operator (-$d).</summary>
        public string Left { get; }

        public string Right { get; }

        /// <summary>The result's type name.</summary>
        public string Result { get; }

        /// <summary>What it does, for hovers and the docs: "The two lengths added: 1h + 30m is 1h30m."</summary>
        public string Summary { get; }

        /// <summary>The native type that declares it.</summary>
        public string DeclaredBy { get; }

        public string Display => Left == null ? $"{Operator}{Right}" : $"{Left} {Operator} {Right}";
    }
}
