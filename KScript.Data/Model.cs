using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Data
{
    /// <summary>How a column is stored and read back: the KScript type it holds, as the database sees it.</summary>
    internal enum ColumnKind
    {
        Text,
        Integer,
        Real,
        Bool,
        Date,
        Email,
        Handle,
        Uuid,
        Password,
        Enum,
        Json,
    }

    /// <summary>One stored field of a model: a column of its table.</summary>
    internal sealed class Column
    {
        public string Name { get; init; }

        public FieldDefinition Field { get; init; }

        public ColumnKind Kind { get; init; }

        public bool Nullable { get; init; }

        /// <summary>varchar(n) for <c>string(max: n)</c>, and the length of emails and handles.</summary>
        public int? Length { get; init; }

        /// <summary>The words an enum column may hold.</summary>
        public IReadOnlyList<string> Words { get; init; }

        public bool IsKey { get; init; }

        /// <summary>An integer key the database numbers itself.</summary>
        public bool AutoIncrement { get; init; }
    }

    /// <summary>
    /// What a <c>model</c> declaration says beyond its fields: its table, key, timestamps, soft deletes, indexes and
    /// the fields that are hidden from JSON or can't be filled from a map. Kept on the model's <see cref="RecordType"/>.
    /// </summary>
    internal sealed class ModelInfo
    {
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
        public const string DeletedAt = "deleted_at";

        public ModelInfo(RecordType type) => Type = type;

        public RecordType Type { get; }

        public string Name => Type.Name;

        public string Table { get; set; }

        /// <summary>The connection's name, or null for the default one.</summary>
        public string Connection { get; set; }

        public string Key { get; set; } = "id";

        public bool Timestamps { get; set; }

        public bool SoftDeletes { get; set; }

        public List<List<string>> Unique { get; } = new List<List<string>>();

        public List<List<string>> Index { get; } = new List<List<string>>();

        public HashSet<string> Hidden { get; } = new HashSet<string>(StringComparer.Ordinal);

        public HashSet<string> Guarded { get; } = new HashSet<string>(StringComparer.Ordinal);

        private List<Column> columns;

        /// <summary>The model a record or type value belongs to, or null.</summary>
        public static ModelInfo Of(object value) => value switch
        {
            RecordType t => t.Tag as ModelInfo,
            Record r => r.Type.Tag as ModelInfo,
            _ => null,
        };

        /// <summary>
        /// The columns, worked out the first time they are needed (by then the enums and types the fields name have
        /// been declared, wherever they are in the script).
        /// </summary>
        public IReadOnlyList<Column> Columns
        {
            get
            {
                if (columns != null) return columns;
                var list = new List<Column>();
                foreach (FieldDefinition f in Type.Fields.Where(f => !f.Computed))
                {
                    list.Add(ColumnFor(f));
                }
                columns = list;
                return columns;
            }
        }

        public Column Column(string name) => Columns.FirstOrDefault(c => c.Name == name);

        public Column KeyColumn => Column(Key);

        /// <summary>A column by name, or an error naming the closest one.</summary>
        public Column Need(string name, string where)
        {
            Column c = Column(name);
            if (c != null) return c;
            string close = Values.Closest(name, Columns.Select(x => x.Name));
            throw new KScriptException($"{Name} has no column '{name}'{where}." + (close != null ? $" Did you mean {close}?" : $" Its columns are: {string.Join(", ", Columns.Select(x => x.Name))}."), "Name");
        }

        private Column ColumnFor(FieldDefinition f)
        {
            TypeRef type = f.Type;
            bool nullable = f.Optional || type.Optional;
            bool key = f.Name == Key;
            TypeRef b = type.Args != null ? type.ArgBase : type;
            int? length = type.Args != null && type.Args.TryGetValue("max", out object max) && max is double d ? (int)d : null;
            ColumnKind kind;
            IReadOnlyList<string> words = null;
            string name = b.Inline == null && b.IsBuiltIn ? b.Name.ToLowerInvariant() : null;
            if (type.ListDepth > 0)
            {
                kind = ColumnKind.Json;
            }
            else if (name == null)
            {
                object resolved = Shapes.Resolve(b, Type.Closure, Type.Module);
                switch (resolved)
                {
                    case EnumType e:
                        kind = ColumnKind.Enum;
                        words = e.Names;
                        break;
                    case RecordType r when r.Tag is ModelInfo other:
                        throw new KScriptException($"{Name}.{f.Name} is a {other.Name}: links between models (relations) aren't in this version of KScript.Data yet. Store the key instead, such as {f.Name}_id: int.", "Type");
                    case RecordType:
                        kind = ColumnKind.Json;
                        break;
                    default:
                        name = ((RefinedType)resolved).Root().ToLowerInvariant();
                        kind = KindOf(name);
                        break;
                }
            }
            else
            {
                kind = KindOf(name);
            }
            if (kind == ColumnKind.Email) length ??= 254;
            bool auto = key && kind == ColumnKind.Integer && !f.HasDefault;
            if (key && kind is not (ColumnKind.Integer or ColumnKind.Text or ColumnKind.Uuid or ColumnKind.Handle or ColumnKind.Email or ColumnKind.Enum))
                throw new KScriptException($"{Name}.{f.Name} is the model's key, so it must be an int, string, uuid, handle, email or enum, not {f.Type}.", "Type");
            return new Column { Name = f.Name, Field = f, Kind = kind, Nullable = nullable && !key, Length = length, Words = words, IsKey = key, AutoIncrement = auto };
        }

        private static ColumnKind KindOf(string builtIn) => builtIn switch
        {
            "int" => ColumnKind.Integer,
            "float" or "number" => ColumnKind.Real,
            "bool" => ColumnKind.Bool,
            "date" => ColumnKind.Date,
            "email" => ColumnKind.Email,
            "handle" => ColumnKind.Handle,
            "uuid" => ColumnKind.Uuid,
            "password" => ColumnKind.Password,
            "string" or "regex" => ColumnKind.Text,
            _ => ColumnKind.Json,
        };

        #region Declaring

        /// <summary>The settings a model has besides fields, as attributes (XML) or lines of its block (.kscript).</summary>
        public static readonly string[] Settings = CodeParser.ModelSettings;

        /// <summary>Builds the model a &lt;model&gt; element declares: a record type, with a table behind it.</summary>
        public static RecordType Declare(ElementRun run)
        {
            // <model> has no with attribute, so what it declares is always a record type.
            var type = (RecordType)Shapes.Declare(run);

            var model = new ModelInfo(type)
            {
                Table = run.Raw("table")?.Trim() ?? TableName(type.Name),
                Connection = run.Raw("connection")?.Trim(),
                Key = run.Raw("key")?.Trim() ?? "id",
                Timestamps = Flag(run, "timestamps"),
                SoftDeletes = Flag(run, "soft_deletes"),
            };
            if (!IsName(model.Table)) throw run.Error($"'{model.Table}' can't be a table name: use letters, digits and _.", "table", "Syntax");

            foreach (List<string> group in Groups(run, "unique")) model.Unique.Add(group);
            foreach (List<string> group in Groups(run, "index")) model.Index.Add(group);
            foreach (string f in Names(run, "hidden")) model.Hidden.Add(f);
            foreach (string f in Names(run, "guarded")) model.Guarded.Add(f);

            // The key comes first. Without one, a model has id: an int the database numbers.
            if (type.Field(model.Key) == null)
            {
                if (run.Has("key")) throw run.Error($"{type.Name} has no field '{model.Key}' to be its key.", "key", "Syntax");
                type.InsertField(0, Field("id", "int", "The record's number, given by the database when it is saved."));
            }
            if (model.Timestamps)
            {
                AddOwn(type, CreatedAt, "When the record was saved first.", run);
                AddOwn(type, UpdatedAt, "When the record was last saved.", run);
            }
            if (model.SoftDeletes) AddOwn(type, DeletedAt, "When the record was deleted, or null. Queries leave deleted records out.", run);

            foreach (string f in model.Unique.Concat(model.Index).SelectMany(g => g).Concat(model.Hidden).Concat(model.Guarded))
            {
                if (type.Field(f) == null)
                {
                    string close = Values.Closest(f, type.Fields.Select(x => x.Name));
                    throw run.Error($"{type.Name} has no field '{f}'." + (close != null ? $" Did you mean {close}?" : string.Empty), null, "Syntax");
                }
            }

            type.Kind = "model";
            type.RecordKind = "record";
            type.Tag = model;
            type.Hidden = model.Hidden.Count > 0 ? model.Hidden : null;
            tables[model.Table] = type.Name;
            return type;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> tables = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The model a table was last declared for, so errors can say "A User" rather than "A row in users".</summary>
        internal static string NameOfTable(string table) => tables.TryGetValue(table, out string name) ? name : null;

        private static void AddOwn(RecordType type, string name, string description, ElementRun run)
        {
            if (type.Field(name) != null) throw run.Error($"{type.Name} declares {name} itself, but the model's settings add it: leave the field out.", null, "Syntax");
            type.SetField(Field(name, "date", description));
        }

        private static FieldDefinition Field(string name, string type, string description) =>
            new FieldDefinition(name, TypeRef.Parse(type).WithOptional(true), null, new List<object> { name }, null, null, null, description, optional: true);

        private static bool Flag(ElementRun run, string name)
        {
            string raw = run.Raw(name)?.Trim();
            if (raw == null) return false;
            return Values.ParseBool(raw) ?? throw run.Error($"{name} is yes or no, not '{raw}'.", name, "Syntax");
        }

        /// <summary>The names of a list setting: <c>[a, b]</c> or <c>a, b</c>.</summary>
        private static IEnumerable<string> Names(ElementRun run, string name)
        {
            foreach (List<string> group in Groups(run, name))
            {
                if (group.Count > 1) throw run.Error($"{name} lists field names: [{string.Join(", ", group)}] can't be one.", name, "Syntax");
                yield return group[0];
            }
        }

        /// <summary>
        /// The groups of a list setting: <c>unique: [email, [team, slug]]</c> is one index on email and one on team and
        /// slug together.
        /// </summary>
        internal static List<List<string>> Groups(ElementRun run, string name)
        {
            string raw = run.Raw(name)?.Trim();
            var groups = new List<List<string>>();
            if (string.IsNullOrEmpty(raw)) return groups;
            if (raw.StartsWith('[') && raw.EndsWith(']')) raw = raw.Substring(1, raw.Length - 2);
            int depth = 0;
            var current = new System.Text.StringBuilder();
            void Flush()
            {
                string part = current.ToString().Trim();
                current.Clear();
                if (part.Length == 0) return;
                var names = part.TrimStart('[').TrimEnd(']').Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
                string bad = names.FirstOrDefault(n => !IsName(n));
                if (bad != null) throw run.Error($"'{bad}' isn't a field name in {name}.", name, "Syntax");
                groups.Add(names);
            }
            foreach (char c in raw)
            {
                if (c == '[') depth++;
                if (c == ']') depth--;
                if (c == ',' && depth == 0) Flush();
                else current.Append(c);
            }
            Flush();
            return groups;
        }

        internal static bool IsName(string s) => s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_');

        /// <summary>A model's table: the plural of its name in snake case. User is users, BlogPost blog_posts, Category categories.</summary>
        internal static string TableName(string model)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < model.Length; i++)
            {
                char c = model[i];
                if (char.IsUpper(c) && i > 0 && (char.IsLower(model[i - 1]) || char.IsDigit(model[i - 1]) || (i + 1 < model.Length && char.IsLower(model[i + 1])))) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return Plural(sb.ToString());
        }

        internal static string Plural(string word)
        {
            if (word.EndsWith("ss") || word.EndsWith("sh") || word.EndsWith("ch") || word.EndsWith('x') || word.EndsWith('z') || word.EndsWith('s')) return word + "es";
            if (word.EndsWith('y') && word.Length > 1 && "aeiou".IndexOf(word[word.Length - 2]) < 0) return word.Substring(0, word.Length - 1) + "ies";
            if (word.EndsWith("person")) return word.Substring(0, word.Length - 6) + "people";
            return word + "s";
        }

        #endregion
    }
}
