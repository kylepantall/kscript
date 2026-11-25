using KScript.Metadata;
using System.Globalization;
using System.Text;

using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>
    /// A type as written where a type goes: <c>string</c>, <c>int</c>, <c>Repo</c>, <c>Repo[]</c>, <c>string?</c> or
    /// <c>Repo[][]?</c>. <c>[]</c> is a list of the type before it, and a trailing <c>?</c> allows null.
    /// </summary>
    public sealed class TypeRef
    {
        private TypeRef(string name, int listDepth, bool optional, IKObject inline, TypeRef argBase = null, IReadOnlyDictionary<string, object> args = null, string argsText = null)
        {
            Name = name;
            ListDepth = listDepth;
            Optional = optional;
            Inline = inline;
            ArgBase = argBase;
            Args = args;
            ArgsText = argsText;
        }

        /// <summary>The type's name, without <c>[]</c> or <c>?</c>.</summary>
        public string Name { get; }

        /// <summary>How many <c>[]</c> follow the name.</summary>
        public int ListDepth { get; }

        /// <summary>True when the type ends in <c>?</c>: null and missing values are allowed.</summary>
        public bool Optional { get; }

        /// <summary>
        /// The type itself, when it is already known: an object inside a <c>sample</c> file, a field's own rules
        /// (<c>&lt;field min="1"&gt;</c>), or a type given as a value.
        /// </summary>
        internal IKObject Inline { get; }

        /// <summary>
        /// For a type given arguments where it is used, such as <c>uuid(v: 4)</c> or <c>string[](max: 5)</c>: the
        /// type before the brackets. Its items (at <see cref="ListDepth"/> 0) are that type with <see cref="Args"/> as rules.
        /// </summary>
        public TypeRef ArgBase { get; }

        /// <summary>The arguments: plain values by name, such as v = 4.</summary>
        public IReadOnlyDictionary<string, object> Args { get; }

        private string ArgsText { get; }

        /// <summary>A built-in type such as string or int, rather than an &lt;enum&gt; or a &lt;type&gt;.</summary>
        public bool IsBuiltIn => Inline == null && Args == null && Values.IsDeclarable(Name);

        /// <summary>Just a name: no <c>[]</c> and no <c>?</c>.</summary>
        public bool IsSimple => ListDepth == 0 && !Optional;

        internal static TypeRef Of(RecordType type, int listDepth = 0, bool optional = false) => new TypeRef(type.Name, listDepth, optional, type);

        internal static TypeRef Of(RefinedType type, int listDepth = 0, bool optional = false) => new TypeRef(type.Name, listDepth, optional, type);

        internal static TypeRef Any(bool optional = false) => new TypeRef("any", 0, optional, null);

        internal TypeRef WithOptional(bool optional) => optional == Optional ? this : new TypeRef(Name, ListDepth, optional, Inline, ArgBase, Args, ArgsText);

        internal TypeRef Item => new TypeRef(Name, ListDepth - 1, false, Inline, ArgBase, Args, ArgsText);

        /// <summary>The underlying type after following refinements and rules. Lists give "list".</summary>
        internal string Root(Scope scope)
        {
            TypeRef type = this;
            while (type.ListDepth == 0)
            {
                if (type.ArgBase != null) { type = type.ArgBase; continue; }
                if (type.IsBuiltIn) return type.Name;
                object definition = type.Inline ?? (scope != null && scope.TryGet(type.Name, out object value) ? value : null);
                if (definition is not RefinedType refined) return type.Name;
                type = refined.Base;
                scope = refined.Closure;
            }
            return "list";
        }

        internal string RuntimeName(Scope scope)
        {
            var element = new TypeRef(Name, 0, false, Inline, ArgBase, Args, ArgsText);
            string name = element.Root(scope) switch
            {
                "int" or "float" or "port" => "number",
                "text" => "string",
                "list" => "any[]",
                string root => root,
            };
            return name + string.Concat(Enumerable.Repeat("[]", ListDepth));
        }

        private TypeRef ListOf() => new TypeRef(Name, ListDepth + 1, false, Inline, ArgBase, Args, ArgsText);

        private TypeRef WithArgs(IReadOnlyDictionary<string, object> args, string text) => new TypeRef(Name, 0, false, null, this, args, text.Trim());

        private RefinedType refined;
        private Scope refinedScope;

        /// <summary>The type with its arguments as rules, on top of the project's defaults for a built-in type.</summary>
        internal RefinedType Refined(Scope scope, Module module)
        {
            if (refined != null && refinedScope == scope) return refined;
            TypeRules rules = TypeRules.FromOptions(Args, out string error) ?? throw new KScriptException($"{this}: {error}", "Type");
            TypeRef baseType = ArgBase;
            if (baseType.IsBuiltIn)
            {
                // The arguments replace the project's defaults for the type rather than adding to them.
                rules = rules.Over(TypeDefaults.For(module, baseType.Name));
                baseType = baseType.WithoutDefaults();
            }
            var type = new RefinedType(ToItemString(), baseType, rules, null, scope, module);
            if (rules.ProblemFor(type.Root(), ToItemString()) is string problem) throw new KScriptException(problem, "Type");
            refinedScope = scope;
            return refined = type;
        }

        /// <summary>True for a built-in type that ignores the project's defaults (they were merged into its arguments).</summary>
        internal bool NoDefaults { get; private init; }

        internal TypeRef WithoutDefaults() => new TypeRef(Name, ListDepth, Optional, Inline, ArgBase, Args, ArgsText) { NoDefaults = true };

        /// <summary>Parses a type, or returns null when <paramref name="text"/> isn't one.</summary>
        public static TypeRef Parse(string text)
        {
            string t = (text ?? string.Empty).Trim();
            bool optional = t.EndsWith('?');
            if (optional) t = t.Substring(0, t.Length - 1).TrimEnd();
            // Peel [] and (args) off the end: string(min: 1)[] is a list of such strings, string[](max: 5) a short list.
            var suffixes = new List<string>();
            while (true)
            {
                if (t.EndsWith("[]", StringComparison.Ordinal))
                {
                    suffixes.Add("[]");
                    t = t.Substring(0, t.Length - 2).TrimEnd();
                    continue;
                }
                if (t.EndsWith(')'))
                {
                    int open = MatchingParen(t);
                    if (open <= 0) return null;
                    suffixes.Add(t.Substring(open));
                    t = t.Substring(0, open).TrimEnd();
                    continue;
                }
                break;
            }
            if (t.Length == 0 || !Lexer.IsIdentStart(t[0]) || !t.All(Lexer.IsIdentChar)) return null;
            if (Values.IsDeclarable(t) && !KScript.Metadata.NativeTypes.IsNative(t)) t = t.ToLowerInvariant();
            var type = new TypeRef(t, 0, false, null);
            for (int i = suffixes.Count - 1; i >= 0; i--)
            {
                if (suffixes[i] == "[]")
                {
                    type = type.ListOf();
                    continue;
                }
                string inner = suffixes[i].Substring(1, suffixes[i].Length - 2);
                IReadOnlyDictionary<string, object> args = ParseArgs(inner);
                if (args == null) return null;
                type = type.WithArgs(args, inner);
            }
            return optional ? type.WithOptional(true) : type;
        }

        /// <summary>The ( that the ) ending <paramref name="t"/> closes, skipping brackets and quotes inside.</summary>
        private static int MatchingParen(string t)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = t.Length - 1; i >= 0; i--)
            {
                char c = t[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c is '\'' or '"') quote = c;
                else if (c is ')' or ']' or '}') depth++;
                else if (c is '(' or '[' or '{')
                {
                    depth--;
                    if (depth == 0) return c == '(' ? i : -1;
                }
            }
            return -1;
        }

        /// <summary><c>v: 4, domains: ['example.com']</c> as plain values by name, or null if they aren't plain.</summary>
        internal static IReadOnlyDictionary<string, object> ParseArgs(string text)
        {
            ParseResult parsed = ExpressionParser.ParseExpression("{" + text + "}");
            if (!parsed.Success || parsed.Root is not MapNode map) return null;
            var options = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (MapEntryNode entry in map.Entries)
            {
                if (entry.Key == "rates" && entry.Value is MapNode rates)
                {
                    var table = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (MapEntryNode currency in rates.Entries)
                    {
                        object rate = TypeRules.PlainValue(currency.Value);
                        if (rate is TypeRules.Unplain || !table.TryAdd(currency.Key, rate)) return null;
                    }
                    options[entry.Key] = table;
                    continue;
                }
                object value = TypeRules.PlainValue(entry.Value);
                if (value is TypeRules.Unplain) return null;
                options[entry.Key] = value;
            }
            return options;
        }

        private string ToItemString() => Args != null ? ArgBase + "(" + ArgsText + ")" : Name;

        public override string ToString() => ToItemString() + string.Concat(Enumerable.Repeat("[]", ListDepth)) + (Optional ? "?" : string.Empty);
    }

    /// <summary>One field of a &lt;type&gt;.</summary>
    public sealed class FieldDefinition
    {
        internal FieldDefinition(string name, TypeRef type, string from, IReadOnlyList<object> path, ExprNode defaultValue, DecodedText defaultText,
            DecodedText value, string description, bool optional)
        {
            Name = name;
            Type = type;
            From = from;
            Path = path;
            DefaultNode = defaultValue;
            DefaultText = defaultText;
            ValueText = value;
            Description = description;
            Optional = optional || type.Optional || defaultValue != null || defaultText != null;
        }

        public string Name { get; }
        public TypeRef Type { get; }

        /// <summary>Where the value is read from, when it isn't the field's own name: <c>owner.login</c>.</summary>
        public string From { get; }

        public string Description { get; }

        /// <summary>Missing and null values are allowed (they become the default, or null).</summary>
        public bool Optional { get; }

        /// <summary>Worked out from the other fields (<c>value="..."</c>) rather than read from the data.</summary>
        public bool Computed => ValueText != null;

        internal IReadOnlyList<object> Path { get; }
        internal ExprNode DefaultNode { get; }
        internal DecodedText DefaultText { get; }
        internal DecodedText ValueText { get; }

        internal RecordType Owner { get; set; }

        /// <summary>Listed in the type's <c>construct({ ... })</c>: a caller may pass it.</summary>
        public bool Input { get; internal set; }

        internal bool HasDefault => DefaultNode != null || DefaultText != null;

        internal FieldDefinition WithType(TypeRef type, bool optional) =>
            new FieldDefinition(Name, type, From, Path, DefaultNode, DefaultText, ValueText, Description, optional) { Owner = Owner, Input = Input };

        /// <summary>Parses a path such as <c>owner.login</c> or <c>items[0].name</c> into keys and indexes, or null.</summary>
        internal static List<object> ParsePath(string text)
        {
            var parts = new List<object>();
            if (string.IsNullOrWhiteSpace(text)) return null;
            int i = 0;
            string s = text.Trim();
            while (i < s.Length)
            {
                if (s[i] == '[')
                {
                    int close = s.IndexOf(']', i);
                    if (close < 0 || !int.TryParse(s.AsSpan(i + 1, close - i - 1), out int index) || index < 0) return null;
                    parts.Add(index);
                    i = close + 1;
                    if (i < s.Length && s[i] == '.') i++;
                    else if (i < s.Length && s[i] != '[') return null;
                    continue;
                }
                int end = i;
                while (end < s.Length && s[end] != '.' && s[end] != '[') end++;
                string key = s.Substring(i, end - i).Trim();
                if (key.Length == 0) return null;
                parts.Add(key);
                i = end;
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    if (i == s.Length) return null;
                }
            }
            return parts[0] is string ? parts : null;
        }
    }

    /// <summary>What a &lt;type&gt; does with keys in the data that it has no field for.</summary>
    public enum ExtraKeys
    {
        Drop,
        Keep,
        Error,
    }

    /// <summary>
    /// A value that is a declared type: a type, model, enum, error type or job. Scripts name it bare (<c>Repo</c>,
    /// never <c>Repo</c>), and calling it makes a value of it: <c>Repo($data)</c>, <c>OutOfStock(item: 'tea')</c>.
    /// </summary>
    public interface IDeclaredType
    {
        string Name { get; }
    }

    /// <summary>
    /// The value of <c>&lt;type name="Repo"&gt;</c>: the shape some data must have. <c>Repo($data)</c> checks the
    /// data and makes a <see cref="Record"/> of it; <c>Repo->check($data)</c> lists the problems instead; and
    /// <c>Repo->schema()</c> describes it as JSON Schema.
    /// </summary>
    public sealed class RecordType : IKObject, IDeclaredType
    {
        internal RecordType(string name, string description, ExtraKeys extra, Scope closure, Module module)
        {
            Name = name;
            Description = description;
            Extra = extra;
            Closure = closure;
            Module = module;
        }

        public string Name { get; }
        public string Description { get; }
        public ExtraKeys Extra { get; }
        public IReadOnlyList<FieldDefinition> Fields => fields;
        internal int? DeclarationOffset { get; init; }
        internal IReadOnlyList<TypeCatch> Catches { get; set; } = Array.Empty<TypeCatch>();

        internal Scope Closure { get; }
        internal Module Module { get; }

        private readonly List<FieldDefinition> fields = new List<FieldDefinition>();

        internal void SetField(FieldDefinition field)
        {
            field.Owner ??= this;
            int at = fields.FindIndex(f => f.Name == field.Name);
            if (at >= 0) fields[at] = field;
            else fields.Add(field);
        }

        public FieldDefinition Field(string name) => fields.FirstOrDefault(f => f.Name == name);

        /// <summary>Adds a field at a place in the list, such as a model's key first.</summary>
        internal void InsertField(int index, FieldDefinition field)
        {
            field.Owner ??= this;
            fields.Insert(index, field);
        }

        /// <summary>The types this one joins: <c>type Admin with [User, Auditable]</c>.</summary>
        public IReadOnlyList<RecordType> Bases => bases;

        private readonly List<RecordType> bases = new List<RecordType>();

        /// <summary>Rules on the whole record (<c>validate: $end > $start, '...'</c>), its bases' first.</summary>
        internal List<Validator> Validators { get; } = new List<Validator>();

        internal void AddBase(RecordType type) => bases.Add(type);

        /// <summary>
        /// How text becomes a record: <c>pattern</c>'s named groups give the fields
        /// (<c>/^(?&lt;w&gt;\d+)x(?&lt;h&gt;\d+)$/</c> turns 1920x1080 into { w: 1920, h: 1080 }), and <c>parse</c>, when
        /// there is one, works the fields out instead (from $value, and $match when there is a pattern).
        /// </summary>
        internal RegexValue Pattern { get; set; }

        internal DecodedText Parse { get; set; }

        /// <summary>The type's own methods (<c>fn</c> inside the type), by name, with its bases' after its own.</summary>
        internal Dictionary<string, MethodSignature> Signatures { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, MarkupElement> OwnMethods { get; } = new Dictionary<string, MarkupElement>(StringComparer.Ordinal);

        /// <summary>
        /// The type's <c>construct({ ... }) { ... }</c> blocks, its bases' first. With one, a caller may pass only the
        /// fields it lists, the other fields are the value's own state, and each block runs once when a value is made.
        /// </summary>
        internal List<(RecordType Owner, MarkupElement Element)> Constructors { get; } = new();

        public bool HasConstructor => Constructors.Count > 0;

        /// <summary>Inherited record methods evaluate their bodies, defaults and catches in the defining module.</summary>
        internal RecordType MethodOwner(string name, MarkupElement declaration)
        {
            foreach (RecordType parent in Bases)
                if (parent.OwnMethods.TryGetValue(name, out var inherited) && inherited == declaration)
                    return parent.MethodOwner(name, declaration);
            return this;
        }

        internal RecordType ValidatorOwner(Validator validator)
        {
            foreach (RecordType parent in Bases)
                if (parent.Validators.Contains(validator)) return parent.ValidatorOwner(validator);
            return this;
        }

        /// <summary>True when text (a literal such as H4cmW12in) can become a record of this type.</summary>
        internal bool ReadsText => Pattern != null || Parse != null;

        /// <summary>True if a record of this type can be used where <paramref name="other"/> is expected: it is that type, or joins it.</summary>
        /// <summary>Recognizes the same declaration in separately loaded instances of a script.</summary>
        public bool SameDeclaration(RecordType other) =>
            ReferenceEquals(this, other) || (other != null && Name == other.Name && Module.Path != null && Module.Path == other.Module.Path
                && DeclarationOffset != null && DeclarationOffset == other.DeclarationOffset);

        public bool IsA(RecordType other) => this == other || bases.Any(b => b.IsA(other));

        /// <summary>"type", or the kind of type a package declares with it, such as "model": package methods for that kind work on it.</summary>
        public string TypeName => Kind ?? "type";

        /// <summary>The kind of type a package made this for (<c>model</c> for KScript.Data), or null for a plain &lt;type&gt;.</summary>
        internal string Kind { get; set; }

        /// <summary>The receiver that package methods for this type's records are registered for (<c>record</c>), or null for none.</summary>
        internal string RecordKind { get; set; }

        /// <summary>What the package that made the type keeps with it, such as a model's table.</summary>
        internal object Tag { get; set; }

        /// <summary>Fields left out when a record is turned into JSON, such as a password hash.</summary>
        internal HashSet<string> Hidden { get; set; }

        public string ToText() => Name;

        /// <summary>
        /// <c>Repo($data)</c> or <c>Repo(name: 'x', stars: 2)</c>: checks the data (a map, a list of maps, or JSON text)
        /// or the fields given by name, and returns it as this type. Fails with every problem it finds.
        /// </summary>
        internal object Construct(Interpreter interpreter, IReadOnlyList<object> args, IReadOnlyDictionary<string, object> named)
        {
            if (args.Count > 1 || (args.Count == 1 && named.Count > 0))
            {
                throw new KScriptException($"{Name}() takes the data to check ({Name}($data)) or its fields by name ({Name}({Example()})).", "Argument");
            }
            object data = args.Count == 1 ? (ReadsText ? args[0] : Shapes.ParseIfText(args[0])) : new Dictionary<string, object>(named, StringComparer.Ordinal);
            return Shapes.MapAs(interpreter, data, this, "The data");
        }

        private string Example() => fields.Count == 0 ? "..." : string.Join(", ", fields.Take(2).Select(f => f.Name + ": ..."));

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "name" => Name,
                "description" => Description,
                "fields" => FieldList(),
                _ => null,
            };
            return value != null || name == "description";
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "from":
                    throw new KScriptException($"->from() is gone: call the type to make a value of it, {Name}($data) or {Name}(field: value).", "Retired");
                case "check":
                    result = Shapes.Problems(call.Interpreter, ReadsText ? call.Arg(0) : Shapes.ParseIfText(call.Arg(0)), this);
                    return true;
                case "is":
                    result = call.Arg(0) is Record r ? r.Type.IsA(this) : call.Arg(0) is RecordType t && t.IsA(this);
                    return true;
                case "schema":
                    result = Shapes.Schema(this);
                    return true;
                default:
                    result = null;
                    return Tag is IRecordKind kind && kind.TryCallTypeMethod(call, this, name, out result);
            }
        }

        private List<object> FieldList() => fields.Select(f => (object)new Dictionary<string, object>
        {
            ["name"] = f.Name,
            ["type"] = f.Type.ToString(),
            ["optional"] = f.Optional,
            ["from"] = f.From,
            ["computed"] = f.Computed,
            ["description"] = f.Description,
        }).ToList();

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("check", "data", "The problems that stop the data fitting this type, as { path, field, message } maps. Empty when it fits.", "list"),
            ("is", "value", "True when the value is a record of this type, or of a type built on it with with [...]. Given a type, true when that type is this one or is built on it.", "bool"),
            ("schema", "", "The type as a JSON Schema map.", "map"),
        };

        internal static readonly (string Name, string Type, string Description)[] ValueFields =
        {
            ("name", "string", "The type's name."),
            ("description", "string", "What the type is for, or null."),
            ("fields", "list", "The type's fields: name, type, optional, from, computed and description."),
        };
    }

    /// <summary>
    /// What a package does with the records of a kind of type it declares, beyond what a plain record does: KScript.Data
    /// loads a model's relations when they are first read, gives records a method for each relation, and gives the
    /// model its scopes. Kept as the type's <see cref="RecordType.Tag"/>.
    /// </summary>
    internal interface IRecordKind
    {
        /// <summary>A member the record doesn't hold, such as a relation that hasn't been loaded yet. False to read the field as usual.</summary>
        bool TryGetMember(Interpreter interpreter, Record record, string name, out object value);

        /// <summary>True when the kind adds a method by this name to its records, such as <c>->posts()</c>.</summary>
        bool HasMethod(Record record, string name);

        /// <summary>A method of the record that the kind adds, such as <c>$user->posts()</c>.</summary>
        bool TryCallMethod(CallContext call, Record record, string name, out object result);

        /// <summary>A method of the type that the kind adds, such as a scope: <c>User->admins()</c>.</summary>
        bool TryCallTypeMethod(CallContext call, RecordType type, string name, out object result);

        /// <summary>Called after a field of a record is set, with its converted value.</summary>
        void FieldSet(Record record, string name, object value);

        /// <summary>
        /// The record a key names, for a route's path parameter typed with the type (<c>fn show(post: Post)</c> on
        /// <c>/posts/{post}</c>). False when the kind has no such lookup; otherwise it returns the record or throws
        /// the kind's not found error.
        /// </summary>
        bool TryFind(CallContext call, RecordType type, object key, out object record);

        /// <summary>
        /// Adjusts the OpenAPI schema of the type's records (hidden fields are already left out): a model marks the
        /// fields the database fills in as read-only, and its relations as there only when loaded.
        /// </summary>
        void DescribeSchema(RecordType type, Dictionary<string, object> schema);

        /// <summary>
        /// The fields an OpenAPI schema of the type lists, each with the record type it links to when that type can't be
        /// found by name where the field is declared (a model in the models folder), or null.
        /// </summary>
        IEnumerable<(FieldDefinition Field, RecordType Links)> SchemaFields(RecordType type);

        /// <summary>The OpenAPI schema of a key that names one of the type's records in a path, or null when it has none.</summary>
        Dictionary<string, object> KeySchema(RecordType type);
    }

    /// <summary>
    /// A map made by a &lt;type&gt;. It is still a map, so map methods, loops and JSON work, but reading a field the
    /// type doesn't have is an error, and setting a field converts the value to the field's type.
    /// </summary>
    public sealed class Record : Dictionary<string, object>
    {
        internal Record(RecordType type) : base(StringComparer.Ordinal) => Type = type;

        /// <summary>What a package keeps with one record, such as the values a model record was loaded with.</summary>
        internal object Tag { get; set; }

        public RecordType Type { get; }

        /// <summary>Fields exposed by reference in method scopes for a rendered component instance.</summary>
        internal IReadOnlySet<string> BoundFields { get; set; }

        /// <summary>A copy of the stored data. Maps, lists, and records retain their shapes without sharing mutable containers.</summary>
        public Record Copy()
        {
            var copies = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            return (Record)Clone(this);

            object Clone(object value)
            {
                if (value == null) return null;
                if (copies.TryGetValue(value, out object existing)) return existing;
                if (value is Dictionary<string, object> map)
                {
                    Dictionary<string, object> result = value is Record record
                        ? new Record(record.Type) { BoundFields = record.BoundFields } : new Dictionary<string, object>(map.Comparer);
                    copies[value] = result;
                    foreach (var entry in map) result[entry.Key] = Clone(entry.Value);
                    return result;
                }
                if (value is List<object> list)
                {
                    var result = new List<object>(list.Count);
                    copies[value] = result;
                    foreach (object member in list) result.Add(Clone(member));
                    if (result.Count == 0) ListFilters.RememberEmptyType(result, ListFilters.DeclaredElementType(list));
                    return result;
                }
                return value;
            }
        }

        /// <summary>Reads a field, or fails with the type's fields as suggestions.</summary>
        internal object Get(string name)
        {
            if (TryGetValue(name, out object v)) return v;
            throw new KScriptException($"{Type.Name} has no field '{name}'." + Suggest(name), "Name");
        }

        /// <summary>Sets a field, converting the value to the field's type.</summary>
        internal void Set(Interpreter interpreter, string name, object value)
        {
            FieldDefinition field = Type.Field(name);
            if (field == null)
            {
                if (Type.Extra != ExtraKeys.Keep)
                    throw new KScriptException($"{Type.Name} has no field '{name}'." + Suggest(name), "Name");
                this[name] = value;
                return;
            }
            RecordType owner = field.Owner ?? Type;
            object converted = Shapes.Convert(interpreter, value, field.Type.WithOptional(field.Optional), $"{Type.Name}.{name}", owner.Closure, owner.Module);
            // Every field is set when a record is made, so there is always an old value to put back.
            TryGetValue(name, out object old);
            this[name] = converted;
            (Type.Tag as IRecordKind)?.FieldSet(this, name, converted);
            if (Type.Validators.Count == 0) return;
            List<string> failures = Shapes.Failures(interpreter, this);
            if (failures.Count == 0) return;
            this[name] = old;
            var error = new KScriptException($"{Type.Name}.{name} can't be set to that: the {Type.Name} {failures[0]}.", "Shape");
            error.Fields["problems"] = failures.Select(f => (object)new Dictionary<string, object> { ["path"] = "$", ["field"] = null, ["message"] = f + "." }).ToList();
            throw error;
        }

        private string Suggest(string name)
        {
            string close = KScript.Runtime.Values.Closest(name, Keys);
            return close != null ? $" Did you mean {close}?" : $" Its fields are: {string.Join(", ", Keys)}.";
        }
    }

    /// <summary>Checks values against types and turns data into <see cref="Record"/>s.</summary>
    public static class Shapes
    {
        /// <summary>The most problems one error lists.</summary>
        public const int MaxProblems = 20;

        /// <summary>
        /// Converts a value to a declared type (<c>&lt;let type&gt;</c>, a <c>&lt;fn&gt;</c> parameter, a field), or fails.
        /// Built-in types fail with one message; records list every problem.
        /// </summary>
        public static object Convert(Interpreter interpreter, object value, string type, string what, Scope scope, Module module)
        {
            TypeRef parsed = TypeRef.Parse(type) ?? throw new KScriptException($"'{type}' isn't a type. Write a name such as string, int or Repo, with [] for a list and ? to allow null.", "Type");
            return Convert(interpreter, value, parsed, what, scope, module);
        }

        internal static object Convert(Interpreter interpreter, object value, TypeRef type, string what, Scope scope, Module module, bool normalize = true)
        {
            if (type.IsSimple && type.IsBuiltIn && TypeDefaults.For(module, type.Name) == null) return Values.ConvertTo(value, type.Name, what);
            var mapper = new ShapeMapper(interpreter, scope, module, normalize);
            object result = mapper.Convert(value, true, type, "$", null, 0);
            mapper.ThrowIfAny(what, type);
            return result;
        }

        /// <summary>
        /// Maps data to a type given as an argument (<c>as: Repo</c>, <c>Repo()</c>): a type value, or a type
        /// written as text ('Repo[]'). A list given to a record type is mapped item by item.
        /// </summary>
        public static object MapAs(Interpreter interpreter, object value, object type, string what, Scope scope = null, Module module = null)
        {
            TypeRef t = TypeArgument(type, scope, module, out Scope s, out Module m);
            if (t.IsSimple && !t.IsBuiltIn && Values.Unwrap(value) is List<object> && Resolve(t, s, m) is RecordType) t = TypeRef.Parse(t.Name + "[]");
            return Convert(interpreter, value, t, what, s, m);
        }

        /// <summary>
        /// For commands and methods that read JSON (<c>@json</c>, <c>->json()</c>, <c>@read_json</c>): maps the data
        /// when the call has an <c>as</c> argument at <paramref name="index"/>, otherwise returns it as it is.
        /// </summary>
        public static object MapJson(CallContext call, object data, int index)
        {
            object type = call.Arg(index, "as");
            return type == null ? data : MapAs(call.Interpreter, data, type, "The data", call.Scope, call.Module);
        }

        /// <summary>The problems that stop <paramref name="value"/> fitting the type, as maps.</summary>
        public static List<object> Problems(Interpreter interpreter, object value, RecordType type)
        {
            var mapper = new ShapeMapper(interpreter, type.Closure, type.Module);
            TypeRef t = TypeRef.Of(type, Values.Unwrap(value) is List<object> ? 1 : 0);
            mapper.Convert(value, true, t, "$", null, 0);
            return mapper.Problems.Select(p => (object)p.ToValue()).ToList();
        }

        /// <summary>The problems that stop <paramref name="value"/> fitting <paramref name="type"/>, as maps.</summary>
        internal static List<object> Problems(Interpreter interpreter, object value, TypeRef type, Scope scope, Module module)
        {
            var mapper = new ShapeMapper(interpreter, scope, module);
            mapper.Convert(value, true, type, "$", null, 0);
            return mapper.Problems.Select(p => (object)p.ToValue()).ToList();
        }

        internal static void CheckTypeArgument(CallContext call, ParameterInfo parameter, object value)
        {
            bool accepts;
            if (parameter.TypeConstraint == TypeArgumentConstraint.DeclaredRecord)
            {
                accepts = value is RecordType { Kind: null };
            }
            else
            {
                TypeRef type = TypeArgument(value, call.Scope, call.Module, out Scope scope, out Module module);
                if (!type.IsBuiltIn) _ = Resolve(type, scope, module);
                string root = type.ListDepth > 0 ? KType.List : type.Name;
                accepts = parameter.TypeConstraint switch
                {
                    TypeArgumentConstraint.Array => root == KType.List,
                    TypeArgumentConstraint.Mappable => root is KType.List or KType.Map || value is RecordType,
                    TypeArgumentConstraint.Iterable => root is (KType.List or KType.Map or KType.Number or KType.String or "int" or "float") || value is RecordType
                        || NativeTypes.Get(root)?.Iterates != null,
                    _ => true,
                };
            }
            if (!accepts)
                throw call.Error($"'{parameter.Name}' requires a type with the {parameter.TypeConstraint} constraint.", "Type");
        }

        /// <summary>An <c>as:</c> argument: a &lt;type&gt; or &lt;enum&gt; value, or a type written as text.</summary>
        internal static TypeRef TypeArgument(object type, Scope scope, Module module, out Scope resolveIn, out Module resolveModule)
        {
            resolveIn = scope;
            resolveModule = module;
            switch (type)
            {
                case RecordType record:
                    resolveIn = record.Closure;
                    resolveModule = record.Module;
                    return TypeRef.Of(record);
                case EnumType e:
                    return TypeRef.Parse(e.Name);
                case RefinedType refined:
                    resolveIn = refined.Closure;
                    resolveModule = refined.Module;
                    return TypeRef.Of(refined);
                case string text:
                    return TypeRef.Parse(text) ?? throw new KScriptException($"'{text}' isn't a type. Write a name such as string, int or Repo, with [] for a list and ? to allow null.", "Type");
                default:
                    throw new KScriptException($"as: needs a type, such as Repo or 'Repo[]', but got {Values.Describe(type)}.", "Type");
            }
        }

        /// <summary>
        /// What text written raw (unquoted) for <paramref name="type"/> means: "handle" for handle types (so <c>@kyle</c> is a handle),
        /// "word" for types read from text (semantic and refined text types, enums, types with a pattern or parse), or null
        /// when raw words keep their usual meaning (plain string, numbers, lists and records).
        /// </summary>
        internal static string RawTextKind(TypeRef type, Scope scope, Module module)
        {
            if (type == null || type.ListDepth > 0) return null;
            if (type.ArgBase != null) return RawTextKind(type.ArgBase, scope, module) ?? (TextRoots.Contains(type.ArgBase.Name) || KScript.Metadata.NativeTypes.IsNative(type.ArgBase.Name) ? "word" : null);
            if (type.Inline == null && Values.IsDeclarable(type.Name))
            {
                string name = KScript.Metadata.NativeTypes.IsNative(type.Name) ? type.Name : type.Name.ToLowerInvariant();
                return KScript.Metadata.NativeTypes.Get(name)?.RawKind;
            }
            object resolved;
            try { resolved = Resolve(type, scope, module); }
            catch (KScriptException) { return null; }
            switch (resolved)
            {
                case EnumType:
                    return "word";
                case RecordType r:
                    return r.ReadsText ? "word" : null;
                default:
                    string root = ((RefinedType)resolved).Root();
                    return KScript.Metadata.NativeTypes.Get(root)?.RawKind ?? (TextRoots.Contains(root) ? "word" : null);
            }
        }

        private static readonly HashSet<string> TextRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "string", SemanticTypes.Email, SemanticTypes.Handle, SemanticTypes.Uuid, SemanticTypes.Regex, SemanticTypes.Password,
        };

        /// <summary>The &lt;type&gt; or &lt;enum&gt; a name (not a built-in type) refers to. Unknown names fail.</summary>
        internal static object Resolve(TypeRef type, Scope scope, Module module)
        {
            if (type.Inline != null) return type.Inline;
            if (scope.TryGet(type.Name, out object found) && (found is RecordType || found is EnumType || found is RefinedType)) return found;
            IEnumerable<string> known = Values.DeclarableTypes.Concat(KnownTypes(scope));
            string close = Values.Closest(type.Name, known);
            throw new KScriptException(Wording.Of($"Unknown type '{type.Name}'.") + (close != null ? $" Did you mean {close}?" : string.Empty)
                + Wording.Of($" Declare it with <type name=\"{type.Name}\" fields=\"...\" /> or <enum name=\"{type.Name}\" values=\"...\" />, or use one of: {string.Join(", ", Values.DeclarableTypes)}."), "Type");
        }

        private static IEnumerable<string> KnownTypes(Scope scope)
        {
            for (Scope s = scope; s != null; s = s.Parent)
            {
                foreach (KeyValuePair<string, object> v in s.OwnVariables)
                {
                    if (v.Value is RecordType || v.Value is EnumType || v.Value is RefinedType) yield return v.Key;
                }
            }
        }

        /// <summary>The record-wide rules (<c>validate</c>) that <paramref name="record"/> breaks. Each field is a variable.</summary>
        internal static List<string> Failures(Interpreter interpreter, Record record)
        {
            var failures = new List<string>();
            foreach (Validator v in record.Type.Validators)
            {
                RecordType owner = record.Type.ValidatorOwner(v);
                Scope s = owner.Closure.Child();
                foreach (KeyValuePair<string, object> kv in record) s.Declare(kv.Key, kv.Value);
                if (!Values.Truthy(interpreter.EvaluateText(v.Test, s, owner.Module))) failures.Add(v.Failure);
            }
            return failures;
        }

        /// <summary>JSON text becomes data; anything else is used as it is.</summary>
        internal static object ParseIfText(object value) => value is string s ? Values.ParseJson(s) : value;

        /// <summary>The kind of value, using the record's name where applicable.</summary>
        public static string TypeName(object value) => value is Record r ? r.Type.Name : value is ErrorValue ? "error" : Values.TypeOf(value);

        internal static string RuntimeTypeName(object value) => Values.Unwrap(value) is List<object> list
            ? ListElementType(list) + "[]" : TypeName(value);

        internal static string ListElementType(List<object> list)
        {
            var elements = new Dictionary<List<object>, string>();
            return Inspect(list, 0);

            string Inspect(List<object> items, int depth)
            {
                if (items.Count == 0) return ListFilters.DeclaredElementType(items) ?? "any";
                if (depth == 64) return "any";
                if (elements.TryGetValue(items, out string known)) return known;
                // A list reached again before inspection finishes has no finite element type.
                elements.Add(items, null);
                string element = ItemType(items[0]);
                for (int i = 1; element != null && i < items.Count; i++)
                    if (ItemType(items[i]) != element) element = null;
                return elements[items] = element ?? "any";

                string ItemType(object item) => Values.Unwrap(item) is List<object> nested
                    ? Inspect(nested, depth + 1) is string type ? type + "[]" : null
                    : TypeName(item);
            }
        }

        #region Declaring

        /// <summary>
        /// Builds the type a &lt;type&gt; element declares: a record type (fields, perhaps joining other record
        /// types with <c>with</c>), or a refinement of one other type (<c>with="int" min="0"</c>).
        /// </summary>
        internal static IKObject Declare(ElementRun run, List<string> bases = null, Func<RecordType, FieldDefinition, bool> inheritField = null, bool allowEmpty = false)
        {
            string name = run.VariableName("name");
            if (Values.IsDeclarable(name))
                throw run.Error($"'{name}' is a built-in type, so a <type> can't use it as its name.", "name", "Syntax");
            List<string> with = bases ?? TypeNames(run.Raw("with"));
            if (with.Count == 1 && IsRefinable(with[0], run.Scope))
            {
                if (run.Has("extends") || run.ChildElements.Any(e => e.GetAttribute("signature") != null))
                    throw run.Error("Method signatures and extends require a record type.", "extends", "Type");
                return DeclareRefined(run, name, with[0]);
            }
            // A component's own types, enums, errors and models are declared first, beside it: its fields and
            // methods name them, and the rest of the file can too. Only top-level declarations are exported.
            List<MarkupNode> nested = run.ChildElements.Where(ComponentScript.IsNestedDeclaration).Cast<MarkupNode>().ToList();
            if (nested.Count > 0) run.Interpreter.ExecuteNodes(nested, run.Scope, run.Module);
            foreach (string b in with)
            {
                string baseName = TypeRef.Parse(b).Name;
                if (!Values.IsDeclarable(baseName) && !run.Scope.TryGet(baseName, out _))
                {
                    string close = Values.Closest(baseName, Values.DeclarableTypes.Concat(KnownTypes(run.Scope)));
                    throw run.Error(Wording.Of($"Unknown type '{baseName}' in with.") + (close != null ? $" Did you mean {close}?" : string.Empty)
                        + Wording.Of($" Build on a <type> declared before this one, or on a built-in type such as string, int or email."), "with", "Type");
                }
            }

            string rule = TypeRules.Attributes.FirstOrDefault(a => a != "pattern" && run.Has(a));
            if (rule != null)
                throw run.Error($"{rule} only goes on a type made from one other type, such as <type name=\"Slug\" with=\"string\" {rule}=\"...\">, or on a field.", rule, "Syntax");

            string extraText = run.Raw("extra")?.Trim().ToLowerInvariant() ?? "drop";
            // The loader has already checked extra is one of drop, keep and error.
            ExtraKeys extra = extraText == "keep" ? ExtraKeys.Keep : extraText == "error" ? ExtraKeys.Error : ExtraKeys.Drop;
            var type = new RecordType(name, run.Raw("description"), extra, run.Scope, run.Module) { DeclarationOffset = run.Element.NameSpan.Start };
            Dictionary<string, string> conflicts = JoinBases(type, with, run, inheritField);
            type.Catches = TypeCatch.Collect(run, type.Bases.SelectMany(b => b.Catches), recordFields: true);
            if (run.Has("pattern"))
            {
                if (!RegexValue.TryParse(PatternOf(run.Element), out RegexValue pattern, out string reason))
                    throw run.Error($"pattern on <type name=\"{name}\"> isn't a regular expression: {reason}.", "pattern", "Syntax");
                type.Pattern = pattern;
            }
            if (run.Element.GetAttribute("parse")?.Value is DecodedText parse) type.Parse = parse;

            string sample = run.Raw("path");
            if (sample != null)
            {
                string path = run.Host.ResolvePath(sample, run.Module.Directory);
                if (!File.Exists(path)) throw run.Error($"The sample file '{sample}' doesn't exist.", "path", "FileNotFound");
                object data;
                try
                {
                    data = Values.ParseJson(File.ReadAllText(path));
                }
                catch (KScriptException ex)
                {
                    throw run.Error($"The sample file '{sample}' isn't JSON: {ex.Message}", "path", "Json");
                }
                SampleTypes.Fill(type, data, sample, run);
            }

            HashSet<string> seen = AddFields(run, type, rulesAndMethods: true);

            // A field two bases disagree on is fine once this type declares it itself.
            string conflict = conflicts.Keys.FirstOrDefault(f => !seen.Contains(f));
            if (conflict != null) throw run.Error(conflicts[conflict], "with", "Syntax");
            if (run.Raw("extends")?.Trim() is string contract)
            {
                IEnumerable<MethodSignature> signatures = contract == ComponentScript.Contract
                    ? MethodSignature.Component
                    : run.Scope.TryGet(contract, out object target) && target is RecordType parent && parent.Signatures.Count > 0
                        ? parent.Signatures.Values
                        : throw run.Error($"'{contract}' does not declare method signatures. Declare it before {name} or import its file.", "extends", "Type");
                foreach (var signature in signatures)
                {
                    if (signature.Problem(type.OwnMethods.GetValueOrDefault(signature.Name)) is string problem)
                        throw run.Error($"{name} must {problem} to extend {contract}.", "extends", "Type");
                }
            }
            if (!allowEmpty && type.Fields.Count == 0 && type.OwnMethods.Count == 0 && type.Signatures.Count == 0 && !run.Has("extends"))
                throw run.Error(KScript.Syntax.Markup.CodeArguments.TypeNeedsFields(name), null, "Syntax");
            return type;
        }

        /// <summary>
        /// Adds the fields of a &lt;type&gt; or &lt;error&gt;: its <c>fields</c> attribute, then its &lt;field&gt; elements.
        /// A &lt;type&gt; (<paramref name="rulesAndMethods"/>) can also hold &lt;validate&gt; rules and &lt;fn&gt; methods.
        /// Returns the names it declared, methods as "fn name".
        /// </summary>
        internal static HashSet<string> AddFields(ElementRun run, RecordType type, bool rulesAndMethods = false)
        {
            string name = type.Name;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string fieldsText = run.Raw("fields");
            if (!string.IsNullOrWhiteSpace(fieldsText))
            {
                foreach (string part in Parameter.SplitTopLevel(fieldsText, ','))
                {
                    if (part.Trim().Length == 0) continue;
                    FieldDefinition field = ParseInlineField(part.Trim(), run);
                    if (!seen.Add(field.Name)) throw run.Error($"<{run.Name} name=\"{name}\"> lists the field '{field.Name}' twice.", "fields", "Syntax");
                    SetOwnField(type, field, run);
                }
            }

            var own = new TypeRules();
            foreach (MarkupElement e in run.ChildElements)
            {
                if (rulesAndMethods && (e.Name == "catch" || ComponentScript.IsNestedDeclaration(e) || own.TryAddChild(e, run, allowNormalize: false))) continue;
                if (ComponentScript.NestedDeclarations.Contains(e.Name)) throw run.Error(ComponentScript.NestedOnlyInComponent(e), null, "Syntax");
                if (rulesAndMethods && e.Name == CodeParser.ConstructWord)
                {
                    if (type.Constructors.Any(c => c.Owner == type)) throw run.Error($"{name} has two construct blocks. Keep one.", null, "Syntax");
                    foreach (MarkupElement input in e.ChildElements.Where(c => c.Name == "field"))
                    {
                        FieldDefinition listed = ParseFieldElement(input, run);
                        if (listed.Computed) throw run.Error($"construct lists what a caller may pass, so '{listed.Name}' can't be worked out with =>. Declare it below construct.", null, "Syntax");
                        if (!seen.Add(listed.Name)) throw run.Error($"<{run.Name} name=\"{name}\"> lists the field '{listed.Name}' twice.", null, "Syntax");
                        listed.Input = true;
                        SetOwnField(type, listed, run);
                    }
                    type.Constructors.Add((type, e));
                    continue;
                }
                if (rulesAndMethods && e.Name == "fn")
                {
                    string method = e.GetAttributeValue("name")?.Trim().TrimStart('@');
                    if (string.IsNullOrEmpty(method)) throw run.Error($"A method needs a name: <fn name=\"area\" value=\"$width * $height\" />.", null, "Syntax");
                    if (type.Field(method) != null && !type.Bases.Any(b => b.Field(method) != null)) throw run.Error($"{name} has a field and a method both called '{method}'.", null, "Syntax");
                    if (!seen.Add("fn " + method))
                        throw run.Error($"{name} declares the method '{method}' twice.", null, "Syntax");
                    if (e.GetAttribute("signature") != null)
                        type.Signatures.Add(method, MethodSignature.From(e));
                    else
                    {
                        if (e.GetAttribute("optional") != null)
                            throw run.Error($"Only a method signature can be optional ({method}).", null, "Syntax");
                        type.OwnMethods[method] = e;
                    }
                    continue;
                }
                if (e.Name != "field")
                {
                    throw run.Error(rulesAndMethods ? Wording.Of($"<type> can only hold <field>, <construct>, <validate>, <normalize> and <fn> elements, not <{e.Name}>.") : Wording.Of($"<{run.Name}> can only hold <field> elements, not <{e.Name}>."), null, "Syntax");
                }
                FieldDefinition field = ParseFieldElement(e, run);
                if (!seen.Add(field.Name)) throw run.Error($"<{run.Name} name=\"{name}\"> lists the field '{field.Name}' twice.", null, "Syntax");
                SetOwnField(type, field, run);
            }
            type.Validators.AddRange(own.Validators);
            return seen;
        }

        /// <summary>The pattern attribute's text: a regex literal (/.../i) or a plain pattern.</summary>
        private static string PatternOf(MarkupElement e) => e.GetAttributeValue("pattern");

        /// <summary><c>User, Auditable</c> or <c>[User, Auditable]</c>.</summary>
        internal static List<string> TypeNames(string text)
        {
            string t = (text ?? string.Empty).Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) t = t.Substring(1, t.Length - 2);
            return Parameter.SplitTopLevel(t, ',').Select(n => n.Trim().Trim('\'', '"')).Where(n => n.Length > 0).ToList();
        }

        /// <summary>A built-in type, a list type, an &lt;enum&gt; or another refinement: a type that can have rules, rather than fields.</summary>
        private static bool IsRefinable(string name, Scope scope)
        {
            TypeRef t = TypeRef.Parse(name);
            if (t == null) return false;
            if (t.Args != null) t = t.ArgBase;
            if (t.IsBuiltIn || t.ListDepth > 0) return true;
            return scope.TryGet(t.Name, out object found) && (found is RefinedType || found is EnumType);
        }

        private static RefinedType DeclareRefined(ElementRun run, string name, string baseName)
        {
            TypeRef baseType = TypeRef.Parse(baseName);
            if (baseType.Optional) throw run.Error($"with=\"{baseName}\": a type's base can't allow null. Write {name}? where the type is used instead.", "with", "Syntax");
            if (run.Has("fields") || run.Has("path"))
                throw run.Error($"<type name=\"{name}\"> is made from {baseName}, so it has rules (min, max, pattern, validate...) rather than fields.", run.Has("fields") ? "fields" : "path", "Syntax");
            if (!baseType.IsBuiltIn && baseType.Args == null && baseType.ListDepth == 0 && run.Scope.TryGet(baseType.Name, out object found) && found is RefinedType inner) baseType = TypeRef.Of(inner);
            TypeRules rules = TypeRules.Read(run.Element, run);
            Dictionary<string, MarkupElement> methods = null;
            foreach (MarkupElement e in run.ChildElements)
            {
                if (e.Name == "catch") continue;
                if (e.Name == "fn")
                {
                    string method = e.GetAttributeValue("name")?.Trim().TrimStart('@');
                    if (string.IsNullOrEmpty(method)) throw run.Error("A method needs a name.", null, "Syntax");
                    methods ??= new Dictionary<string, MarkupElement>(StringComparer.Ordinal);
                    methods[method] = e;
                    continue;
                }
                if (!rules.TryAddChild(e, run, allowNormalize: true))
                    throw run.Error($"<type name=\"{name}\"> is made from {baseName}, so it can only hold <validate> and <normalize> rules or <fn> methods, not <{e.Name}>.", null, "Syntax");
            }
            var type = new RefinedType(name, baseType, rules, run.Raw("description"), run.Scope, run.Module, methods);
            type.Catches = TypeCatch.Collect(run, type.BaseRefinement?.Catches ?? Array.Empty<TypeCatch>(), recordFields: false);
            if (rules.ProblemFor(type.Root(), $"<type name=\"{name}\">") is string problem) throw run.Error(problem, null, "Syntax");
            return type;
        }

        /// <summary>
        /// Copies the fields and rules of the types named in <c>with</c>, in order. Returns the fields two bases
        /// give different types, with the message to fail with unless the new type declares them itself.
        /// </summary>
        private static Dictionary<string, string> JoinBases(RecordType type, List<string> with, ElementRun run, Func<RecordType, FieldDefinition, bool> inheritField)
        {
            var conflicts = new Dictionary<string, string>(StringComparer.Ordinal);
            var origin = new Dictionary<string, RecordType>(StringComparer.Ordinal);
            var methodOrigin = new Dictionary<string, RecordType>(StringComparer.Ordinal);
            RecordType patternOrigin = null;
            foreach (string baseName in with)
            {
                if (!run.Scope.TryGet(baseName, out object found) || found is not RecordType b)
                {
                    string why = found is RefinedType || IsRefinable(baseName, run.Scope)
                        ? $"{baseName} isn't a type with fields, and a type made from one ({baseName}) can't join others. Use with=\"{baseName}\" on its own for rules on a {baseName}."
                        : $"{baseName} isn't a type declared before this one.";
                    throw run.Error($"<type name=\"{type.Name}\" with=\"...\">: {why}", "with", "Type");
                }
                if (b.IsA(type) || type.Bases.Contains(b)) throw run.Error($"<type name=\"{type.Name}\"> joins {b.Name} twice.", "with", "Syntax");
                type.AddBase(b);
                foreach (var constructor in b.Constructors)
                    if (!type.Constructors.Contains(constructor)) type.Constructors.Add(constructor);
                foreach (Validator validator in b.Validators)
                    if (!type.Validators.Contains(validator)) type.Validators.Add(validator);
                foreach (KeyValuePair<string, MarkupElement> m in b.OwnMethods)
                {
                    if (type.OwnMethods.TryAdd(m.Key, m.Value)) methodOrigin[m.Key] = b;
                    else if (type.OwnMethods[m.Key] != m.Value)
                        conflicts["fn " + m.Key] = $"{type.Name} gets the method '{m.Key}' from {methodOrigin[m.Key].Name} and {b.Name}. Declare fn {m.Key} in {type.Name} to choose.";
                }
                if (b.Pattern != null && type.Pattern != null && type.Pattern != b.Pattern)
                    throw run.Error($"{type.Name} joins two types read from text ({patternOrigin.Name} and {b.Name}), so it can't tell which pattern to use.", "with", "Syntax");
                if (b.Pattern != null && type.Pattern == null) patternOrigin = b;
                type.Pattern ??= b.Pattern;
                type.Parse ??= b.Parse;
                foreach (FieldDefinition f in b.Fields)
                {
                    if (inheritField != null && !inheritField(b, f)) continue;
                    FieldDefinition existing = type.Field(f.Name);
                    if (existing == null)
                    {
                        type.SetField(f);
                        origin[f.Name] = b;
                    }
                    else if (BaseName(existing.Type) != BaseName(f.Type) || existing.Type.ListDepth != f.Type.ListDepth)
                    {
                        conflicts[f.Name] = $"{type.Name} gets '{f.Name}' from {origin[f.Name].Name} ({existing.Type}) and {b.Name} ({f.Type}). Declare {f.Name} in {type.Name} to choose.";
                    }
                    else if (f.Type.Inline is RefinedType || f.Type.Args != null && f.Type.ListDepth == 0)
                    {
                        RefinedType rules = f.Type.Inline as RefinedType ?? f.Type.Refined(b.Closure, b.Module);
                        // Both bases have the field: the second one's rules apply on top of the first's.
                        var both = new RefinedType(rules.Name, existing.Type.WithOptional(false), rules.Rules, null, rules.Closure, rules.Module);
                        type.SetField(existing.WithType(TypeRef.Of(both, 0, existing.Type.Optional && f.Type.Optional), existing.Optional && f.Optional));
                    }
                }
            }
            return conflicts;
        }

        /// <summary>The name of the type under any field rules: string for <c>string(min: 1)</c>.</summary>
        private static string BaseName(TypeRef t)
        {
            if (t.Args != null) t = t.ArgBase.WithOptional(t.Optional);
            while (t.Inline is RefinedType r && r.Name == r.Base.WithOptional(false).ToString()) t = r.Base;
            return t.WithOptional(false).ToString();
        }

        /// <summary>
        /// Adds a field the type declares itself. One that replaces a base's field must keep that field's type or
        /// narrow it (a refinement of it, or a type that joins it), so the record still fits where the base does.
        /// </summary>
        private static void SetOwnField(RecordType type, FieldDefinition field, ElementRun run)
        {
            FieldDefinition inherited = type.Field(field.Name);
            if (inherited != null && type.Bases.Count > 0 && !Narrows(field.Type, inherited.Type, run.Scope))
                throw run.Error($"{type.Name} declares '{field.Name}' as {field.Type}, but it joins a type where it is {inherited.Type}. Use {inherited.Type}, or a type made from it.", null, "Type");
            type.SetField(field);
        }

        private static bool Narrows(TypeRef own, TypeRef inherited, Scope scope)
        {
            string target = BaseName(inherited);
            if (own.ListDepth != inherited.ListDepth) return false;
            // Each step goes down to a base declared before, so this ends.
            while (true)
            {
                if (BaseName(own) == target || own.WithOptional(false).ToString() == target) return true;
                object resolved = own.Inline ?? (own.IsBuiltIn ? null : scope.TryGet(own.Name, out object f) ? f : null);
                switch (resolved)
                {
                    case RefinedType r:
                        own = r.Base;
                        continue;
                    case RecordType record:
                        // A type declared later can't be checked yet; a known one must join the inherited type.
                        return !(scope.TryGet(target, out object t) && t is RecordType wanted) || record.IsA(wanted);
                    case null when !own.IsBuiltIn:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary><c>login: string</c>, <c>avatar_url?: string</c>, <c>tags: string[] = []</c> or just <c>name</c>.</summary>
        private static FieldDefinition ParseInlineField(string text, ElementRun run)
        {
            ExprNode defaultValue = null;
            // Only a name and a type come before the first '=', so it starts the default.
            int eq = text.IndexOf('=');
            if (eq >= 0)
            {
                ParseResult parsed = ExpressionParser.ParseExpression(text.Substring(eq + 1));
                if (!parsed.Success)
                    throw run.Error($"The default of '{text.Substring(0, eq).Trim()}' isn't a valid expression: {parsed.Errors[0].Words}", "fields", "Syntax");
                defaultValue = parsed.Root;
                text = text.Substring(0, eq).Trim();
            }
            string typeText = null;
            int colon = text.IndexOf(':');
            if (colon >= 0)
            {
                typeText = text.Substring(colon + 1).Trim();
                text = text.Substring(0, colon).Trim();
            }
            if (run.Module.IsCode && KScript.Syntax.Markup.OldSpellings.OptionalIsOnName(text, typeText, codeFile: true) is string onName)
                throw run.Error(onName, "fields", "Syntax");
            bool optional = text.EndsWith('?');
            string name = optional ? text.Substring(0, text.Length - 1).TrimEnd() : text;
            CheckFieldName(name, run, "fields");
            TypeRef type = ParseFieldType(typeText, name, run, "fields");
            return new FieldDefinition(name, type, null, new object[] { name }, defaultValue, null, null, null, optional);
        }

        private static FieldDefinition ParseFieldElement(MarkupElement e, ElementRun run)
        {
            // The loader has already checked the name is there.
            string name = e.GetAttributeValue("name").Trim();
            if (run.Module.IsCode && KScript.Syntax.Markup.OldSpellings.OptionalIsOnName(name, e.GetAttributeValue("type")?.Trim(), codeFile: true) is string onName)
                throw run.Error(onName, null, "Syntax");
            bool optional = name.EndsWith('?');
            if (optional) name = name.Substring(0, name.Length - 1).TrimEnd();
            CheckFieldName(name, run, null);
            TypeRef type = ParseFieldType(e.GetAttributeValue("type"), name, run, null);
            string from = e.GetAttributeValue("from")?.Trim();
            List<object> path = from != null ? FieldDefinition.ParsePath(from) : new List<object> { name };
            if (path == null) throw run.Error($"from=\"{from}\" on the field '{name}' isn't a path. Write keys joined by dots, with [0] for a list item: owner.login, items[0].name.", null, "Syntax");
            // The loader has already checked optional is yes or no.
            bool optionalAttribute = Values.ParseBool(e.GetAttributeValue("optional")) == true;
            DecodedText value = e.GetAttribute("value")?.Value;
            DecodedText defaultText = e.GetAttribute("default")?.Value;
            if (value != null && (from != null || defaultText != null))
                throw run.Error($"The field '{name}' has a value, so it is worked out rather than read: it can't also have from or default.", null, "Syntax");
            TypeRules rules = TypeRules.Read(e, run);
            if (!rules.IsEmpty)
            {
                // title: string(min: 1) is a string with rules of its own: a refinement with no name.
                var refined = new RefinedType(type.WithOptional(false).ToString(), type.WithOptional(false), rules, null, run.Scope, run.Module);
                if (rules.ProblemFor(refined.Root(), $"the field '{name}'") is string problem) throw run.Error(problem, null, "Syntax");
                type = TypeRef.Of(refined, 0, type.Optional);
            }
            return new FieldDefinition(name, type, from, path, null, defaultText, value, e.GetAttributeValue("description"), optional || optionalAttribute);
        }

        private static void CheckFieldName(string name, ElementRun run, string attribute)
        {
            if (name.Length == 0) throw run.Error("A field needs a name, such as login or avatar_url?.", attribute, "Syntax");
            if (!Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar))
                throw run.Error($"'{name}' can't be a field name: use letters, digits and _, starting with a letter. To read a key like '{name}', use <field name=\"...\" from=\"{name}\" />.", attribute, "Syntax");
        }

        private static TypeRef ParseFieldType(string text, string field, ElementRun run, string attribute)
        {
            if (string.IsNullOrWhiteSpace(text)) return TypeRef.Any();
            return TypeRef.Parse(text) ?? throw run.Error($"'{text}' isn't a type for the field '{field}'. Write a name such as string, int or User, with [] for a list and ? to allow null.", attribute, "Syntax");
        }

        #endregion

        #region JSON Schema

        /// <summary>The type as a JSON Schema (draft 2020-12) map. Other named types go in $defs.</summary>
        public static Dictionary<string, object> Schema(RecordType root)
        {
            var defs = new Dictionary<string, object>(StringComparer.Ordinal);
            var schema = new Dictionary<string, object> { ["$schema"] = "https://json-schema.org/draft/2020-12/schema" };
            foreach (var kv in ObjectSchema(root, root, defs, "#/$defs/")) schema[kv.Key] = kv.Value;
            if (defs.Count > 0) schema["$defs"] = defs;
            return schema;
        }

        /// <summary>
        /// A type as written where a type goes (<c>Person</c>, <c>Person[]</c>, <c>int</c>), as JSON Schema for an
        /// OpenAPI document: named &lt;type&gt;s and &lt;enum&gt;s go in <paramref name="defs"/> and are referred to as
        /// <c>{refBase}Name</c> (<c>#/components/schemas/Person</c>). Null when the type isn't one KScript knows.
        /// </summary>
        public static Dictionary<string, object> SchemaFor(string type, Scope scope, Module module, Dictionary<string, object> defs, string refBase)
        {
            TypeRef t = TypeRef.Parse(type);
            if (t == null) return null;
            return SchemaOf(t, scope, module, null, defs, refBase, named: true);
        }

        /// <summary>The named type (a &lt;type&gt; or &lt;enum&gt;) a type refers to, or null: <c>Person</c> for <c>Person[]?</c>.</summary>
        public static object ResolveNamed(string type, Scope scope, Module module)
        {
            TypeRef t = TypeRef.Parse(type);
            if (t == null || t.IsBuiltIn) return null;
            try
            {
                return Resolve(t, scope, module);
            }
            catch (KScriptException)
            {
                return null;
            }
        }

        private static Dictionary<string, object> ObjectSchema(RecordType type, RecordType root, Dictionary<string, object> defs, string refBase)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            var required = new List<object>();
            IEnumerable<(FieldDefinition Field, RecordType Links)> fields = (type.Tag as IRecordKind)?.SchemaFields(type) ?? type.Fields.Select(f => (f, (RecordType)null));
            foreach ((FieldDefinition f, RecordType links) in fields)
            {
                // Hidden fields are never in a record's JSON.
                if (type.Hidden?.Contains(f.Name) == true) continue;
                RecordType owner = f.Owner ?? type;
                Dictionary<string, object> property = links != null
                    ? WithListDepth(RecordSchema(links, root, defs, refBase), f.Type.ListDepth)
                    : SchemaOf(f.Type, owner.Closure, owner.Module, root, defs, refBase, named: false);
                if (f.Description != null) property["description"] = f.Description;
                if (f.Computed) property["readOnly"] = true;
                properties[f.Name] = property;
                if (!f.Optional && !f.Computed) required.Add(f.Name);
            }
            var schema = new Dictionary<string, object> { ["type"] = "object", ["title"] = type.Name };
            if (type.Description != null) schema["description"] = type.Description;
            schema["properties"] = properties;
            if (required.Count > 0) schema["required"] = required;
            schema["additionalProperties"] = type.Extra == ExtraKeys.Keep;
            (type.Tag as IRecordKind)?.DescribeSchema(type, schema);
            return schema;
        }

        private static Dictionary<string, object> SchemaOf(TypeRef t, Scope scope, Module module, RecordType root, Dictionary<string, object> defs, string refBase, bool named)
        {
            if (t.ListDepth > 0) return new Dictionary<string, object> { ["type"] = "array", ["items"] = SchemaOf(t.Item, scope, module, root, defs, refBase, named) };
            if (t.Args != null && t.Inline == null) return RefinedSchema(t.Refined(scope, module), root, defs, refBase);
            switch (t.Inline)
            {
                case RecordType inline: return ObjectSchema(inline, root, defs, refBase);
                case RefinedType refined: return RefinedSchema(refined, root, defs, refBase);
            }
            switch (t.Name)
            {
                case "string": return new Dictionary<string, object> { ["type"] = "string" };
                case "int": return new Dictionary<string, object> { ["type"] = "integer" };
                case "float":
                case "number": return new Dictionary<string, object> { ["type"] = "number" };
                case "bool": return new Dictionary<string, object> { ["type"] = "boolean" };
                case "date": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "date-time" };
                case "email": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "email" };
                case "handle": return new Dictionary<string, object> { ["type"] = "string", ["pattern"] = "^@[A-Za-z0-9_][A-Za-z0-9_.-]{0,29}$" };
                case "password": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "password", ["writeOnly"] = true };
                case "uuid": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" };
                case "regex": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "regex" };
                case "list": return new Dictionary<string, object> { ["type"] = "array" };
                case "map": return new Dictionary<string, object> { ["type"] = "object" };
                case "any":
                case "fn": return new Dictionary<string, object>();
                case "port": return new Dictionary<string, object> { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 65535 };
            }
            if (KScript.Metadata.NativeTypes.Get(t.Name) is KScript.Metadata.NativeTypeInfo native)
            {
                var schema = new Dictionary<string, object> { ["type"] = "string" };
                if (native.JsonFormat != null) schema["format"] = native.JsonFormat;
                if (native.Secret) schema["writeOnly"] = true;
                schema["example"] = native.Example.Trim('\'');
                return schema;
            }
            object resolved;
            try
            {
                resolved = Resolve(t, scope, module);
            }
            catch (KScriptException)
            {
                // Names other packages give meaning to, when they are there.
                return t.Name.ToLowerInvariant() switch
                {
                    "url" or "uri" => new Dictionary<string, object> { ["type"] = "string", ["format"] = "uri" },
                    _ => named ? null : new Dictionary<string, object>(),
                };
            }
            if (resolved is EnumType e)
            {
                var values = new Dictionary<string, object> { ["type"] = "string", ["enum"] = e.Names.Cast<object>().ToList() };
                if (refBase == "#/$defs/") return values;
                defs.TryAdd(e.Name, values);
                return new Dictionary<string, object> { ["$ref"] = refBase + e.Name };
            }
            if (resolved is RefinedType r) return RefinedSchema(r, root, defs, refBase);
            return RecordSchema((RecordType)resolved, root, defs, refBase);
        }

        /// <summary>A reference to a record type's schema, which is added to <paramref name="defs"/> the first time.</summary>
        private static Dictionary<string, object> RecordSchema(RecordType record, RecordType root, Dictionary<string, object> defs, string refBase)
        {
            if (root != null && record == root) return new Dictionary<string, object> { ["$ref"] = "#" };
            if (!defs.ContainsKey(record.Name))
            {
                defs[record.Name] = null;
                defs[record.Name] = ObjectSchema(record, root, defs, refBase);
            }
            return new Dictionary<string, object> { ["$ref"] = refBase + record.Name };
        }

        private static Dictionary<string, object> WithListDepth(Dictionary<string, object> schema, int depth) =>
            depth == 0 ? schema : new Dictionary<string, object> { ["type"] = "array", ["items"] = WithListDepth(schema, depth - 1) };

        /// <summary>The base type's schema, with the rules JSON Schema can say: min and max, pattern, and the plain lists of one_of and not.</summary>
        private static Dictionary<string, object> RefinedSchema(RefinedType type, RecordType root, Dictionary<string, object> defs, string refBase)
        {
            Dictionary<string, object> schema = SchemaOf(type.Base, type.Closure, type.Module, root, defs, refBase, named: false);
            TypeRules rules = type.Rules;
            string kind = schema.TryGetValue("type", out object k) ? k as string : null;
            (string min, string max) = kind switch
            {
                "integer" or "number" => ("minimum", "maximum"),
                "string" when schema.ContainsKey("format") && (string)schema["format"] == "date-time" => (null, null),
                "string" => ("minLength", "maxLength"),
                "array" => ("minItems", "maxItems"),
                _ => (null, null),
            };
            if (min != null && rules.MinText != null && double.TryParse(rules.MinText, NumberStyles.Float, CultureInfo.InvariantCulture, out double lo)) schema[min] = lo;
            if (max != null && rules.MaxText != null && double.TryParse(rules.MaxText, NumberStyles.Float, CultureInfo.InvariantCulture, out double hi)) schema[max] = hi;
            if (rules.PatternText != null) schema["pattern"] = rules.PatternText;
            if (rules.OneOf != null) schema["enum"] = rules.OneOf;
            if (rules.Not != null) schema["not"] = new Dictionary<string, object> { ["enum"] = rules.Not };
            if (type.Description != null) schema["description"] = type.Description;
            return schema;
        }

        #endregion
    }

    /// <summary>One way some data doesn't fit a type.</summary>
    internal sealed record ShapeProblem(string Path, string Field, string Message)
    {
        public Dictionary<string, object> ToValue() => new Dictionary<string, object> { ["path"] = Path, ["field"] = Field, ["message"] = Message };
    }

    /// <summary>Walks some data and a type together, building records and collecting every problem.</summary>
    internal sealed class ShapeMapper
    {
        private readonly Interpreter interpreter;
        private readonly Scope scope;
        private readonly Module module;
        private readonly bool normalize;

        public ShapeMapper(Interpreter interpreter, Scope scope, Module module, bool normalize = true)
        {
            this.interpreter = interpreter;
            this.scope = scope;
            this.module = module;
            this.normalize = normalize;
        }

        public List<ShapeProblem> Problems { get; } = new List<ShapeProblem>();

        public void ThrowIfAny(string what, TypeRef type)
        {
            if (Problems.Count == 0) return;
            List<ShapeProblem> problems = Problems.GroupBy(p => (p.Path, p.Message)).Select(g => g.First()).ToList();
            Problems.Clear();
            Problems.AddRange(problems);
            var text = new StringBuilder();
            if (Problems.Count == 1 && Problems[0].Path == "$")
            {
                // A value with one problem of its own (not in a field): say it on one line.
                text.Append($"{what} doesn't fit {type}: {Problems[0].Message}");
                var single = new KScriptException(text.ToString(), "Shape");
                single.Fields["problems"] = Problems.Select(p => (object)p.ToValue()).ToList();
                throw single;
            }
            text.Append($"{what} doesn't fit {type} ({Problems.Count} problem{(Problems.Count == 1 ? string.Empty : "s")}):");
            foreach (ShapeProblem p in Problems.Take(Shapes.MaxProblems)) text.Append($"\n  {p.Path}: {p.Message}");
            if (Problems.Count > Shapes.MaxProblems) text.Append($"\n  ...and {Problems.Count - Shapes.MaxProblems} more.");
            var error = new KScriptException(text.ToString(), "Shape");
            error.Fields["problems"] = Problems.Select(p => (object)p.ToValue()).ToList();
            throw error;
        }

        private void Problem(string path, string field, string message) => Problems.Add(new ShapeProblem(path, field, message));

        /// <summary>The value as <paramref name="type"/>, or null with a problem recorded.</summary>
        public object Convert(object value, bool present, TypeRef type, string path, string field, int depth)
        {
            if (value == null)
            {
                if (!type.Optional) Problem(path, field, $"needs {Article(type)}, but it is {(present ? "null" : "missing")}.");
                return null;
            }
            if (depth > 64)
            {
                Problem(path, field, "is nested too deeply.");
                return null;
            }
            if (type.ListDepth > 0)
            {
                if (Values.Unwrap(value) is not List<object> list)
                {
                    Problem(path, field, $"needs a list of {type.Item}, but got {Values.Describe(value)}.");
                    return null;
                }
                TypeRef item = type.Item;
                var result = new List<object>(list.Count);
                for (int i = 0; i < list.Count; i++) result.Add(Convert(list[i], true, item, $"{path}[{i}]", field, depth + 1));
                if (result.Count == 0) ListFilters.RememberEmptyType(result, item.RuntimeName(scope));
                return result;
            }
            if (type.Args != null) return MapRefined(value, type.Refined(scope, module), path, field, depth, replaceRates: type.Args.ContainsKey("rates"));
            if (type.IsBuiltIn && !type.NoDefaults && TypeDefaults.For(module, type.Name) is TypeRules defaults)
            {
                // The project's defaults for the type (kscript.json "types") are rules on top of it.
                TypeRef plain = TypeRef.Parse(type.Name).WithoutDefaults();
                return MapRefined(value, new RefinedType(type.Name, plain, defaults, null, scope, module), path, field, depth, replaceRates: false);
            }
            if (type.IsBuiltIn)
            {
                if (type.Name != "any") value = Values.Unwrap(value);
                // Data that should be text may be a number or a bool, but not a map or a list.
                if (type.Name == "string" && (value is Dictionary<string, object> || value is List<object>))
                {
                    Problem(path, field, $"needs a string, but got {Values.Describe(value)}.");
                    return null;
                }
                if (SemanticTypes.IsSemantic(type.Name))
                {
                    if (SemanticTypes.TryConvert(type.Name, value, out object converted, out string reason)) return converted;
                    Problem(path, field, SemanticTypes.Mismatch(type.Name, value, reason) + ".");
                    return null;
                }
                try
                {
                    return Values.ConvertTo(value, type.Name, path);
                }
                catch (KScriptException)
                {
                    Problem(path, field, $"needs {Article(type)}, but got {Values.Describe(value)}.");
                    return null;
                }
            }

            object resolved = type.Inline ?? Shapes.Resolve(type, scope, module);
            if (resolved is RefinedType refined) return MapRefined(value, refined, path, field, depth);
            value = Values.Unwrap(value);
            if (resolved is EnumType e)
            {
                string match = value is string s ? e.Names.FirstOrDefault(n => string.Equals(n, s.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
                if (match != null) return match;
                string close = value is string text ? Values.Closest(text, e.Names) : null;
                Problem(path, field, $"must be a {e.Name} ({string.Join(", ", e.Names)}), but got {Values.Describe(value)}." + (close != null ? $" Did you mean {close}?" : string.Empty));
                return null;
            }
            return MapRecord(value, (RecordType)resolved, path, depth);
        }

        /// <summary>
        /// Converts to the base type (which checks the base's own rules), then normalises, then checks this type's
        /// rules. A value that doesn't fit the base isn't checked any further, so it gets one problem, not several.
        /// </summary>
        private object MapRefined(object value, RefinedType type, string path, string field, int depth, bool replaceRates = true)
        {
            bool normalizeValue = normalize;
            if (value is RefinedValue typed)
            {
                if (typed.Type.IsA(type))
                {
                    // The existing value is canonical. Recheck its rules and retain its method overrides.
                    type = typed.Type;
                    value = typed.Value;
                    normalizeValue = false;
                }
                else if (!type.IsA(typed.Type)) value = typed.Value;
            }
            var inner = new ShapeMapper(interpreter, type.Closure, type.Module, normalizeValue);
            object mapped = inner.Convert(value, true, type.Base, path, field, depth + 1);
            if (normalizeValue && inner.Problems.Count == 0 && type.Rules.Normalize != null)
            {
                object input = Values.Unwrap(mapped);
                var s = new Scope(type.Closure) { Normalizing = type.HasMethods ? (type, input) : null };
                s.Declare("value", input);
                object normalized = interpreter.EvaluateText(type.Rules.Normalize, s, type.Module);
                // A rule that returns its input unchanged also works when the input itself is a function.
                if (!ReferenceEquals(normalized, input) && Values.Unwrap(normalized) is ICallable callback)
                    normalized = interpreter.Call(callback, new[] { input }, scope: s, module: type.Module);
                mapped = inner.Convert(normalized, true, type.Base, path, field, depth + 1);
            }
            if (inner.Problems.Count > 0)
            {
                Problems.AddRange(inner.Problems);
                return null;
            }
            object result = Values.Unwrap(mapped);
            int before = Problems.Count;
            foreach (string problem in type.Rules.Check(interpreter, result, type.Closure, type.Module, item => BaseItem(item, type, path, field, depth)))
                Problem(path, field, problem + ".");
            if (result is PasswordValue password && type.Rules.Options.TryGetValue("algorithm", out object algorithm)) result = password.WithAlgorithm(Values.ToStr(algorithm));
            if (Problems.Count != before) return null;
            if (result is NativeText { TypeName: "money" } money && type.Rules.Options.TryGetValue("rates", out object rates)
                && (replaceRates || !MoneyType.HasRates(money)))
                result = MoneyType.WithRates(money, rates);
            if (type.HasMethods) return new RefinedValue(type, result);
            // A field may reference a type declared later in the same module.
            return mapped is RefinedValue inherited ? new RefinedValue(inherited.Type, result) : result;
        }

        /// <summary>An item of one_of or not, converted to the base type so it compares like the value.</summary>
        private object BaseItem(object item, RefinedType type, string path, string field, int depth)
        {
            var mapper = new ShapeMapper(interpreter, type.Closure, type.Module);
            object converted = mapper.Convert(item, true, type.Base, path, field, depth + 1);
            return mapper.Problems.Count == 0 ? converted : item;
        }

        /// <summary>The value a field of a constructed type starts with when nothing gives it one.</summary>
        private static object Initial(TypeRef type)
        {
            if (type.ListDepth > 0) return new List<object>();
            return (type.IsBuiltIn ? type.Name : null) switch
            {
                "int" or "float" or "number" => 0d,
                "string" or "text" => string.Empty,
                "bool" => false,
                "list" => new List<object>(),
                "map" => new Dictionary<string, object>(StringComparer.Ordinal),
                _ => null,
            };
        }

        /// <summary>Runs the type's construct blocks, its bases' first, with the fields as variables that can be set.</summary>
        private void Construct(Record record)
        {
            foreach ((RecordType owner, MarkupElement element) in record.Type.Constructors)
            {
                Scope scope = owner.Closure.Child();
                foreach (string field in record.Keys.ToList()) scope.BindField(record, field, interpreter);
                scope.Declare("this", record);
                interpreter.ExecuteNodes(element.Children.Where(c => c is not MarkupElement { Name: "field" }).ToList(), scope.Child(), owner.Module);
            }
        }

        internal Record MapRecord(object value, RecordType type, string path, int depth,
            Func<FieldDefinition, object> missingField = null)
        {
            if (value is Record existing && existing.Type.IsA(type)) return existing;
            if (type.ReadsText && (value is string || value is ISemanticText))
            {
                value = ReadText(Values.ToStr(value), type, path);
                if (value == null) return null;
            }
            if (value is not Dictionary<string, object> source)
            {
                Problem(path, null, $"needs a {type.Name} (a map), but got {Values.Describe(value)}.");
                return null;
            }

            var record = new Record(type);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var mapper = new ShapeMapper(interpreter, type.Closure, type.Module, normalize);
            object ConvertField(FieldDefinition field, object raw, bool found, TypeRef target, string fieldPath)
            {
                RecordType owner = field.Owner ?? type;
                if (owner == type) return mapper.Convert(raw, found, target, fieldPath, field.Name, depth + 1);
                var inherited = new ShapeMapper(interpreter, owner.Closure, owner.Module, normalize);
                object result = inherited.Convert(raw, found, target, fieldPath, field.Name, depth + 1);
                mapper.Problems.AddRange(inherited.Problems);
                return result;
            }

            foreach (FieldDefinition f in type.Fields.Where(f => !f.Computed))
            {
                bool found = TryRead(source, f.Path, out object raw, out string at, out string key);
                if (key != null) used.Add(key);
                string fieldPath = path + at;
                bool own = type.HasConstructor && !f.Input;
                if (own && found)
                {
                    // construct lists what may be passed; every other field is the value's own state.
                    Problem(fieldPath, f.Name, $"is {type.Name}'s own state and can't be passed in. construct takes: {string.Join(", ", type.Fields.Where(i => i.Input).Select(i => i.Name))}.");
                    continue;
                }
                if (own && !found && !f.HasDefault)
                {
                    // Until construct sets it, a field of the value's own starts empty: 0, '', false, [] or null.
                    record[f.Name] = f.Optional ? null : Initial(f.Type);
                    continue;
                }
                if (raw == null && f.HasDefault)
                {
                    RecordType owner = f.Owner ?? type;
                    Scope s = owner.Closure.Child();
                    raw = f.DefaultNode != null ? interpreter.Evaluate(f.DefaultNode, s, new EvalContext(owner.Module, null)) : interpreter.EvaluateText(f.DefaultText, s, owner.Module);
                }
                else if (!found && !f.Optional && missingField != null)
                    raw = missingField(f);
                record[f.Name] = ConvertField(f, raw, found, f.Type.WithOptional(f.Optional), fieldPath);
            }
            Problems.AddRange(mapper.Problems);
            mapper.Problems.Clear();
            if (type.HasConstructor && !Problems.Any(p => p.Path.StartsWith(path, StringComparison.Ordinal)))
                Construct(record);

            foreach (KeyValuePair<string, object> kv in source)
            {
                if (used.Contains(kv.Key)) continue;
                if (type.Extra == ExtraKeys.Keep && !record.ContainsKey(kv.Key)) record[kv.Key] = kv.Value;
                else if (type.Extra == ExtraKeys.Error) Problem(path + Key(kv.Key), null, $"{type.Name} has no field '{kv.Key}'.");
            }

            foreach (FieldDefinition f in type.Fields.Where(f => f.Computed))
            {
                RecordType owner = f.Owner ?? type;
                Scope s = owner.Closure.Child();
                foreach (KeyValuePair<string, object> kv in record) s.Declare(kv.Key, kv.Value);
                object computed = interpreter.EvaluateText(f.ValueText, s, owner.Module);
                record[f.Name] = ConvertField(f, computed, true, f.Type.WithOptional(true), path + Key(f.Name));
            }
            // Rules on the whole record only run once its fields fit, so they can rely on them.
            if (mapper.Problems.Count == 0 && !Problems.Any(p => p.Path.StartsWith(path, StringComparison.Ordinal)))
            {
                foreach (string failure in Shapes.Failures(interpreter, record)) Problem(path, null, failure + ".");
            }
            Problems.AddRange(mapper.Problems);
            return record;
        }

        /// <summary>
        /// Text such as H4cmW12in as a map of fields: the pattern's named groups, or what parse makes of it. Null, with
        /// a problem, when the text doesn't match.
        /// </summary>
        private Dictionary<string, object> ReadText(string text, RecordType type, string path)
        {
            Dictionary<string, object> match = null;
            if (type.Pattern != null)
            {
                System.Text.RegularExpressions.Match m;
                try
                {
                    m = type.Pattern.Regex.Match(text);
                }
                catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
                {
                    m = System.Text.RegularExpressions.Match.Empty;
                }
                if (!m.Success)
                {
                    Problem(path, null, $"needs text matching {type.Pattern.Text} to make a {type.Name}, but got {Values.Describe(text)}.");
                    return null;
                }
                match = RegexValue.MatchMap(type.Pattern.Regex, m);
                if (type.Parse == null)
                {
                    return ((Dictionary<string, object>)match["groups"]).Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
                }
            }
            Scope s = type.Closure.Child();
            s.Declare("value", text);
            if (match != null) s.Declare("match", match);
            object parsed = interpreter.EvaluateText(type.Parse, s, type.Module);
            if (parsed is Dictionary<string, object> map) return map;
            Problem(path, null, $"parse on {type.Name} must make a map of its fields, but made {Values.Describe(parsed)}.");
            return null;
        }

        /// <summary>Follows a field's path into the data. <paramref name="at"/> is the path as written in problems.</summary>
        private static bool TryRead(Dictionary<string, object> source, IReadOnlyList<object> path, out object value, out string at, out string firstKey)
        {
            object current = source;
            bool found = true;
            var written = new StringBuilder();
            firstKey = null;
            foreach (object part in path)
            {
                if (part is string key)
                {
                    var map = found ? current as Dictionary<string, object> : null;
                    string actual = map == null || map.ContainsKey(key) ? key : map.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
                    written.Append(Key(actual));
                    if (map == null || !map.TryGetValue(actual, out current))
                    {
                        found = false;
                        continue;
                    }
                    firstKey ??= actual;
                }
                else
                {
                    int index = (int)part;
                    written.Append($"[{index}]");
                    if (!found || current is not List<object> list || index >= list.Count)
                    {
                        found = false;
                        continue;
                    }
                    current = list[index];
                }
            }
            value = found ? current : null;
            at = written.ToString();
            return found;
        }

        private static string Key(string key) => key.Length > 0 && Lexer.IsIdentStart(key[0]) && key.All(Lexer.IsIdentChar) ? "." + key : $"['{key.Replace("'", "\\'")}']";

        private static string Article(TypeRef type)
        {
            string name = type.WithOptional(false).ToString();
            if (type.ListDepth > 0) return "a list of " + type.Item;
            if (name == "any") return "a value";
            return ("aeiou".IndexOf(char.ToLowerInvariant(name[0])) >= 0 ? "an " : "a ") + name;
        }
    }

    /// <summary>Fields taken from a sample of the data: <c>&lt;type name="Repo" path="repo.json" /&gt;</c>.</summary>
    internal static class SampleTypes
    {
        public static void Fill(RecordType type, object data, string file, ElementRun run)
        {
            Dictionary<string, object> map = data switch
            {
                Dictionary<string, object> m => m,
                List<object> list when list.Count > 0 && list.All(i => i is Dictionary<string, object>) => MergeMaps(list.Cast<Dictionary<string, object>>().ToList()),
                _ => throw run.Error($"The sample file '{file}' must hold a JSON object, or a list of objects.", "path", "Syntax"),
            };
            HashSet<string> partial = data is List<object> items ? KeysNotInAll(items.Cast<Dictionary<string, object>>().ToList()) : new HashSet<string>();
            foreach (FieldDefinition f in FieldsOf(map, partial, type.Name, type, 0)) type.SetField(f);
        }

        private static IEnumerable<FieldDefinition> FieldsOf(Dictionary<string, object> map, HashSet<string> partial, string owner, RecordType parent, int depth)
        {
            foreach (KeyValuePair<string, object> kv in map)
            {
                string name = FieldName(kv.Key);
                if (name == null) continue;
                TypeRef type = TypeOf(kv.Value, owner + "." + name, parent, depth);
                bool optional = partial.Contains(kv.Key) || kv.Value == null;
                yield return new FieldDefinition(name, type, name == kv.Key ? null : kv.Key, new object[] { kv.Key }, null, null, null, "From the sample file.", optional);
            }
        }

        /// <summary>A key as a field name: 'avatar-url' becomes avatar_url, and a key with no letters is skipped.</summary>
        private static string FieldName(string key)
        {
            var sb = new StringBuilder();
            foreach (char c in key) sb.Append(Lexer.IsIdentChar(c) ? c : '_');
            string name = sb.ToString();
            if (!name.Any(char.IsLetter)) return null;
            return Lexer.IsIdentStart(name[0]) ? name : "_" + name;
        }

        private static TypeRef TypeOf(object value, string name, RecordType parent, int depth)
        {
            switch (value)
            {
                case string: return TypeRef.Parse("string");
                case double: return TypeRef.Parse("number");
                case bool: return TypeRef.Parse("bool");
                case Dictionary<string, object> m when depth < 8: return TypeRef.Of(Nested(name, m, new HashSet<string>(), parent, depth));
                case Dictionary<string, object>: return TypeRef.Parse("map");
                case List<object> list:
                {
                    List<object> items = list.Where(i => i != null).ToList();
                    if (items.Count == 0) return TypeRef.Parse("any[]");
                    if (items.All(i => i is Dictionary<string, object>) && depth < 8)
                    {
                        List<Dictionary<string, object>> maps = items.Cast<Dictionary<string, object>>().ToList();
                        return TypeRef.Of(Nested(name, MergeMaps(maps), KeysNotInAll(maps), parent, depth), 1);
                    }
                    string first = Values.TypeOf(items[0]);
                    if (items.All(i => Values.TypeOf(i) == first) && first is "string" or "number" or "bool") return TypeRef.Parse(first + "[]");
                    return TypeRef.Parse("any[]");
                }
                default: return TypeRef.Any(optional: true);
            }
        }

        private static RecordType Nested(string name, Dictionary<string, object> map, HashSet<string> partial, RecordType parent, int depth)
        {
            var type = new RecordType(name, null, parent.Extra, parent.Closure, parent.Module);
            foreach (FieldDefinition f in FieldsOf(map, partial, name, parent, depth + 1)) type.SetField(f);
            return type;
        }

        /// <summary>One map with every key any of the maps has (the first non-null value wins).</summary>
        private static Dictionary<string, object> MergeMaps(List<Dictionary<string, object>> maps)
        {
            var merged = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (Dictionary<string, object> m in maps)
            {
                foreach (KeyValuePair<string, object> kv in m)
                {
                    if (!merged.TryGetValue(kv.Key, out object current) || current == null) merged[kv.Key] = kv.Value;
                }
            }
            return merged;
        }

        private static HashSet<string> KeysNotInAll(List<Dictionary<string, object>> maps) =>
            new HashSet<string>(maps.SelectMany(m => m.Keys).Distinct().Where(k => maps.Any(m => !m.ContainsKey(k) || m[k] == null)));
    }
}
