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
        private TypeRef(string name, int listDepth, bool optional, RecordType inline)
        {
            Name = name;
            ListDepth = listDepth;
            Optional = optional;
            Inline = inline;
        }

        /// <summary>The type's name, without <c>[]</c> or <c>?</c>.</summary>
        public string Name { get; }

        /// <summary>How many <c>[]</c> follow the name.</summary>
        public int ListDepth { get; }

        /// <summary>True when the type ends in <c>?</c>: null and missing values are allowed.</summary>
        public bool Optional { get; }

        /// <summary>A type with no name of its own, such as an object inside a <c>sample</c> file.</summary>
        internal RecordType Inline { get; }

        /// <summary>A built-in type such as string or int, rather than an &lt;enum&gt; or a &lt;type&gt;.</summary>
        public bool IsBuiltIn => Inline == null && Values.DeclarableTypes.Contains(Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>Just a name: no <c>[]</c> and no <c>?</c>.</summary>
        public bool IsSimple => ListDepth == 0 && !Optional;

        internal static TypeRef Of(RecordType type, int listDepth = 0, bool optional = false) => new TypeRef(type.Name, listDepth, optional, type);

        internal static TypeRef Any(bool optional = false) => new TypeRef("any", 0, optional, null);

        internal TypeRef WithOptional(bool optional) => optional == Optional ? this : new TypeRef(Name, ListDepth, optional, Inline);

        internal TypeRef Item => new TypeRef(Name, ListDepth - 1, false, Inline);

        /// <summary>Parses a type, or returns null when <paramref name="text"/> isn't one.</summary>
        public static TypeRef Parse(string text)
        {
            string t = (text ?? string.Empty).Trim();
            bool optional = t.EndsWith('?');
            if (optional) t = t.Substring(0, t.Length - 1).TrimEnd();
            int depth = 0;
            while (t.EndsWith("[]", StringComparison.Ordinal))
            {
                depth++;
                t = t.Substring(0, t.Length - 2).TrimEnd();
            }
            if (t.Length == 0 || !Lexer.IsIdentStart(t[0]) || !t.All(Lexer.IsIdentChar)) return null;
            if (Values.DeclarableTypes.Contains(t, StringComparer.OrdinalIgnoreCase)) t = t.ToLowerInvariant();
            return new TypeRef(t, depth, optional, null);
        }

        public override string ToString() => Name + string.Concat(Enumerable.Repeat("[]", ListDepth)) + (Optional ? "?" : string.Empty);
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

        internal bool HasDefault => DefaultNode != null || DefaultText != null;

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
    /// The value of <c>&lt;type name="Repo"&gt;</c>: the shape some data must have. <c>$Repo->from($data)</c> checks the
    /// data and makes a <see cref="Record"/> of it; <c>$Repo->check($data)</c> lists the problems instead; and
    /// <c>$Repo->schema()</c> describes it as JSON Schema.
    /// </summary>
    public sealed class RecordType : IKObject
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

        internal Scope Closure { get; }
        internal Module Module { get; }

        private readonly List<FieldDefinition> fields = new List<FieldDefinition>();

        internal void SetField(FieldDefinition field)
        {
            int at = fields.FindIndex(f => f.Name == field.Name);
            if (at >= 0) fields[at] = field;
            else fields.Add(field);
        }

        public FieldDefinition Field(string name) => fields.FirstOrDefault(f => f.Name == name);

        public string TypeName => "type";

        public string ToText() => Name;

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
                    result = Shapes.MapAs(call.Interpreter, Shapes.ParseIfText(call.Arg(0)), this, $"The data");
                    return true;
                case "check":
                    result = Shapes.Problems(call.Interpreter, Shapes.ParseIfText(call.Arg(0)), this);
                    return true;
                case "schema":
                    result = Shapes.Schema(this);
                    return true;
                case "fields":
                    result = FieldList();
                    return true;
                case "name":
                    result = Name;
                    return true;
                default:
                    result = null;
                    return false;
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
            ("from", "data", "Checks the data (a map, a list of maps, or JSON text) and returns it as this type. Fails with every problem it finds.", "any"),
            ("check", "data", "The problems that stop the data fitting this type, as { path, field, message } maps. Empty when it fits.", "list"),
            ("schema", "", "The type as a JSON Schema map.", "map"),
            ("fields", "", "The type's fields: name, type, optional, from, computed and description.", "list"),
            ("name", "", "The type's name.", "string"),
        };
    }

    /// <summary>
    /// A map made by a &lt;type&gt;. It is still a map, so map methods, loops and JSON work, but reading a field the
    /// type doesn't have is an error, and setting a field converts the value to the field's type.
    /// </summary>
    public sealed class Record : Dictionary<string, object>
    {
        internal Record(RecordType type) : base(StringComparer.Ordinal) => Type = type;

        public RecordType Type { get; }

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
            this[name] = Shapes.Convert(interpreter, value, field.Type.WithOptional(field.Optional), $"{Type.Name}.{name}", Type.Closure, Type.Module);
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

        internal static object Convert(Interpreter interpreter, object value, TypeRef type, string what, Scope scope, Module module)
        {
            if (type.IsSimple && type.IsBuiltIn) return Values.ConvertTo(value, type.Name, what);
            var mapper = new ShapeMapper(interpreter, scope, module);
            object result = mapper.Convert(value, true, type, "$", null, 0);
            mapper.ThrowIfAny(what, type);
            return result;
        }

        /// <summary>
        /// Maps data to a type given as an argument (<c>as: Repo</c>, <c>$Repo->from()</c>): a type value, or a type
        /// written as text ('Repo[]'). A list given to a record type is mapped item by item.
        /// </summary>
        public static object MapAs(Interpreter interpreter, object value, object type, string what, Scope scope = null, Module module = null)
        {
            TypeRef t = TypeArgument(type, scope, module, out Scope s, out Module m);
            if (t.IsSimple && !t.IsBuiltIn && value is List<object> && Resolve(t, s, m) is RecordType) t = TypeRef.Parse(t.Name + "[]");
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
            TypeRef t = TypeRef.Of(type, value is List<object> ? 1 : 0);
            mapper.Convert(value, true, t, "$", null, 0);
            return mapper.Problems.Select(p => (object)p.ToValue()).ToList();
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
                case string text:
                    return TypeRef.Parse(text) ?? throw new KScriptException($"'{text}' isn't a type. Write a name such as string, int or Repo, with [] for a list and ? to allow null.", "Type");
                default:
                    throw new KScriptException($"as: needs a type, such as Repo or 'Repo[]', but got {Values.Describe(type)}.", "Type");
            }
        }

        /// <summary>The &lt;type&gt; or &lt;enum&gt; a name (not a built-in type) refers to. Unknown names fail.</summary>
        internal static object Resolve(TypeRef type, Scope scope, Module module)
        {
            if (type.Inline != null) return type.Inline;
            if (scope.TryGet(type.Name, out object found) && (found is RecordType || found is EnumType)) return found;
            IEnumerable<string> known = Values.DeclarableTypes.Concat(KnownTypes(scope));
            string close = Values.Closest(type.Name, known);
            throw new KScriptException($"Unknown type '{type.Name}'." + (close != null ? $" Did you mean {close}?" : string.Empty)
                + $" Declare it with <type name=\"{type.Name}\" fields=\"...\" /> or <enum name=\"{type.Name}\" values=\"...\" />, or use one of: {string.Join(", ", Values.DeclarableTypes)}.", "Type");
        }

        private static IEnumerable<string> KnownTypes(Scope scope)
        {
            for (Scope s = scope; s != null; s = s.Parent)
            {
                foreach (KeyValuePair<string, object> v in s.OwnVariables)
                {
                    if (v.Value is RecordType || v.Value is EnumType) yield return v.Key;
                }
            }
        }

        /// <summary>JSON text becomes data; anything else is used as it is.</summary>
        internal static object ParseIfText(object value) => value is string s ? Values.ParseJson(s) : value;

        /// <summary>The name a value's type has: a record's type name, or <see cref="Values.TypeOf"/>.</summary>
        public static string TypeName(object value) => value is Record r ? r.Type.Name : value is ErrorValue ? "error" : Values.TypeOf(value);

        #region Declaring

        /// <summary>Builds the type a &lt;type&gt; element declares.</summary>
        internal static RecordType Declare(ElementRun run)
        {
            string name = run.VariableName("name");
            if (Values.DeclarableTypes.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw run.Error($"'{name}' is a built-in type, so a <type> can't use it as its name.", "name", "Syntax");
            string extraText = run.Raw("extra")?.Trim().ToLowerInvariant() ?? "drop";
            // The loader has already checked extra is one of drop, keep and error.
            ExtraKeys extra = extraText == "keep" ? ExtraKeys.Keep : extraText == "error" ? ExtraKeys.Error : ExtraKeys.Drop;
            var type = new RecordType(name, run.Raw("description"), extra, run.Scope, run.Module);

            string sample = run.Raw("sample");
            if (sample != null)
            {
                string path = run.Host.ResolvePath(sample, run.Module.Directory);
                if (!File.Exists(path)) throw run.Error($"The sample file '{sample}' doesn't exist.", "sample", "FileNotFound");
                object data;
                try
                {
                    data = Values.ParseJson(File.ReadAllText(path));
                }
                catch (KScriptException ex)
                {
                    throw run.Error($"The sample file '{sample}' isn't JSON: {ex.Message}", "sample", "Json");
                }
                SampleTypes.Fill(type, data, sample, run);
            }

            AddFields(run, type);

            if (type.Fields.Count == 0) throw run.Error($"<type name=\"{name}\"> needs fields: fields=\"name: string, age?: int\", <field> elements, or a sample file.", null, "Syntax");
            return type;
        }

        /// <summary>Adds the fields of a &lt;type&gt; or &lt;error&gt;: its <c>fields</c> attribute, then its &lt;field&gt; elements.</summary>
        internal static void AddFields(ElementRun run, RecordType type)
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
                    type.SetField(field);
                }
            }

            foreach (MarkupElement e in run.ChildElements)
            {
                if (e.Name != "field") throw run.Error($"<{run.Name}> can only hold <field> elements, not <{e.Name}>.", null, "Syntax");
                FieldDefinition field = ParseFieldElement(e, run);
                if (!seen.Add(field.Name)) throw run.Error($"<{run.Name} name=\"{name}\"> lists the field '{field.Name}' twice.", null, "Syntax");
                type.SetField(field);
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
                    throw run.Error($"The default of '{text.Substring(0, eq).Trim()}' isn't a valid expression: {parsed.Errors[0].Message}", "fields", "Syntax");
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
            foreach (FieldDefinition f in type.Fields)
            {
                Dictionary<string, object> property = SchemaOf(f.Type, type.Closure, type.Module, root, defs, refBase, named: false);
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
            return schema;
        }

        private static Dictionary<string, object> SchemaOf(TypeRef t, Scope scope, Module module, RecordType root, Dictionary<string, object> defs, string refBase, bool named)
        {
            if (t.ListDepth > 0) return new Dictionary<string, object> { ["type"] = "array", ["items"] = SchemaOf(t.Item, scope, module, root, defs, refBase, named) };
            if (t.Inline != null) return ObjectSchema(t.Inline, root, defs, refBase);
            switch (t.Name)
            {
                case "string": return new Dictionary<string, object> { ["type"] = "string" };
                case "int": return new Dictionary<string, object> { ["type"] = "integer" };
                case "float":
                case "number": return new Dictionary<string, object> { ["type"] = "number" };
                case "bool": return new Dictionary<string, object> { ["type"] = "boolean" };
                case "date": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "date-time" };
                case "list": return new Dictionary<string, object> { ["type"] = "array" };
                case "map": return new Dictionary<string, object> { ["type"] = "object" };
                case "any":
                case "fn": return new Dictionary<string, object>();
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
                    "email" => new Dictionary<string, object> { ["type"] = "string", ["format"] = "email" },
                    "uuid" => new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" },
                    "url" or "uri" => new Dictionary<string, object> { ["type"] = "string", ["format"] = "uri" },
                    "password" => new Dictionary<string, object> { ["type"] = "string", ["format"] = "password" },
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
            var record = (RecordType)resolved;
            if (root != null && record == root) return new Dictionary<string, object> { ["$ref"] = "#" };
            if (!defs.ContainsKey(record.Name))
            {
                defs[record.Name] = null;
                defs[record.Name] = ObjectSchema(record, root, defs, refBase);
            }
            return new Dictionary<string, object> { ["$ref"] = refBase + record.Name };
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

        public ShapeMapper(Interpreter interpreter, Scope scope, Module module)
        {
            this.interpreter = interpreter;
            this.scope = scope;
            this.module = module;
        }

        public List<ShapeProblem> Problems { get; } = new List<ShapeProblem>();

        public void ThrowIfAny(string what, TypeRef type)
        {
            if (Problems.Count == 0) return;
            List<ShapeProblem> problems = Problems.GroupBy(p => (p.Path, p.Message)).Select(g => g.First()).ToList();
            Problems.Clear();
            Problems.AddRange(problems);
            var text = new StringBuilder();
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
                if (value is not List<object> list)
                {
                    Problem(path, field, $"needs a list of {type.Item}, but got {Values.Describe(value)}.");
                    return null;
                }
                TypeRef item = type.Item;
                var result = new List<object>(list.Count);
                for (int i = 0; i < list.Count; i++) result.Add(Convert(list[i], true, item, $"{path}[{i}]", field, depth + 1));
                return result;
            }
            if (type.IsBuiltIn)
            {
                // Data that should be text may be a number or a bool, but not a map or a list.
                if (type.Name == "string" && (value is Dictionary<string, object> || value is List<object>))
                {
                    Problem(path, field, $"needs a string, but got {Values.Describe(value)}.");
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

        private object MapRecord(object value, RecordType type, string path, int depth)
        {
            if (value is Record existing && existing.Type == type) return existing;
            if (value is not Dictionary<string, object> source)
            {
                Problem(path, null, $"needs a {type.Name} (a map), but got {Values.Describe(value)}.");
                return null;
            }

            var record = new Record(type);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var mapper = new ShapeMapper(interpreter, type.Closure, type.Module);
            foreach (FieldDefinition f in type.Fields.Where(f => !f.Computed))
            {
                bool found = TryRead(source, f.Path, out object raw, out string at, out string key);
                if (key != null) used.Add(key);
                string fieldPath = path + at;
                if (raw == null && f.HasDefault)
                {
                    Scope s = type.Closure.Child();
                    raw = f.DefaultNode != null ? interpreter.Evaluate(f.DefaultNode, s, new EvalContext(type.Module, null)) : interpreter.EvaluateText(f.DefaultText, s, type.Module);
                }
                record[f.Name] = mapper.Convert(raw, found, f.Type.WithOptional(f.Optional), fieldPath, f.Name, depth + 1);
            }
            Problems.AddRange(mapper.Problems);
            mapper.Problems.Clear();

            foreach (KeyValuePair<string, object> kv in source)
            {
                if (used.Contains(kv.Key)) continue;
                if (type.Extra == ExtraKeys.Keep && !record.ContainsKey(kv.Key)) record[kv.Key] = kv.Value;
                else if (type.Extra == ExtraKeys.Error) Problem(path + Key(kv.Key), null, $"{type.Name} has no field '{kv.Key}'.");
            }

            foreach (FieldDefinition f in type.Fields.Where(f => f.Computed))
            {
                Scope s = type.Closure.Child();
                foreach (KeyValuePair<string, object> kv in record) s.Declare(kv.Key, kv.Value);
                object computed = interpreter.EvaluateText(f.ValueText, s, type.Module);
                record[f.Name] = mapper.Convert(computed, true, f.Type.WithOptional(true), path + Key(f.Name), f.Name, depth + 1);
            }
            Problems.AddRange(mapper.Problems);
            return record;
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

    /// <summary>Fields taken from a sample of the data: <c>&lt;type name="Repo" sample="repo.json" /&gt;</c>.</summary>
    internal static class SampleTypes
    {
        public static void Fill(RecordType type, object data, string file, ElementRun run)
        {
            Dictionary<string, object> map = data switch
            {
                Dictionary<string, object> m => m,
                List<object> list when list.Count > 0 && list.All(i => i is Dictionary<string, object>) => MergeMaps(list.Cast<Dictionary<string, object>>().ToList()),
                _ => throw run.Error($"The sample file '{file}' must hold a JSON object, or a list of objects.", "sample", "Syntax"),
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
