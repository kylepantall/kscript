using System.Globalization;

using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Data.Migrations
{
    /// <summary>
    /// A column as migrations know it: what its type is, whether it may be null, its default and what it references.
    /// It is the same for every database; <see cref="Ddl"/> writes it as each one's SQL.
    /// </summary>
    internal sealed class ColumnSpec
    {
        public string Name { get; init; }

        public ColumnKind Kind { get; init; }

        public bool Nullable { get; init; }

        /// <summary>varchar(n): <c>string(max: n)</c>, and emails.</summary>
        public int? Length { get; init; }

        /// <summary>The words an enum column may hold.</summary>
        public IReadOnlyList<string> Words { get; init; }

        /// <summary>The table's key, on its own. A key of two columns is <see cref="TableSpec.Key"/> instead.</summary>
        public bool IsKey { get; init; }

        /// <summary>An integer key the database numbers itself.</summary>
        public bool AutoIncrement { get; init; }

        /// <summary>The database's default: text, a number or a bool. Null for none.</summary>
        public object Default { get; init; }

        /// <summary>The table a foreign key column points to, or null.</summary>
        public string References { get; init; }

        /// <summary>The column it points to there: the other table's key.</summary>
        public string ReferencesColumn { get; init; }

        /// <summary>cascade, set null or restrict.</summary>
        public string OnDelete { get; init; }

        /// <summary>The foreign key constraint's name, kept when the table or column is renamed.</summary>
        public string ForeignKeyName { get; init; }

        public ColumnSpec With(string name = null, string foreignKeyName = null) => new ColumnSpec
        {
            Name = name ?? Name, Kind = Kind, Nullable = Nullable, Length = Length, Words = Words, IsKey = IsKey, AutoIncrement = AutoIncrement,
            Default = Default, References = References, ReferencesColumn = ReferencesColumn, OnDelete = OnDelete, ForeignKeyName = foreignKeyName ?? ForeignKeyName,
        };

        /// <summary>The column for a dialect to write.</summary>
        public Column ToColumn() => new Column
        {
            Name = Name, Kind = Kind, Nullable = Nullable, Length = Length, Words = Words, IsKey = IsKey, AutoIncrement = AutoIncrement, Default = Default,
        };

        /// <summary>True when the two are the same column but for their names.</summary>
        public bool SameAs(ColumnSpec other) =>
            Kind == other.Kind && Nullable == other.Nullable && Length == other.Length && IsKey == other.IsKey && AutoIncrement == other.AutoIncrement
            && (Words ?? Array.Empty<string>()).SequenceEqual(other.Words ?? Array.Empty<string>(), StringComparer.Ordinal)
            && DefaultText(Default) == DefaultText(other.Default)
            && References == other.References && (References == null || ReferencesColumn == other.ReferencesColumn && OnDelete == other.OnDelete);

        public bool HasForeignKey => References != null;

        private static string DefaultText(object value) => value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => "'" + Convert.ToString(value, CultureInfo.InvariantCulture),
        };

        /// <summary>The type word a migration file writes: string, int, number, bool, date, email, handle, uuid, password, json or enum.</summary>
        public string TypeWord => TypeWords.First(t => t.Kind == Kind).Word;

        public static readonly (string Word, ColumnKind Kind)[] TypeWords =
        {
            ("string", ColumnKind.Text), ("int", ColumnKind.Integer), ("number", ColumnKind.Real), ("bool", ColumnKind.Bool), ("date", ColumnKind.Date),
            ("email", ColumnKind.Email), ("handle", ColumnKind.Handle), ("uuid", ColumnKind.Uuid), ("password", ColumnKind.Password),
            ("json", ColumnKind.Json), ("enum", ColumnKind.Enum),
        };

        /// <summary>The kind a migration's type word names, or null.</summary>
        public static ColumnKind? KindOf(string word)
        {
            foreach ((string w, ColumnKind k) in TypeWords)
            {
                if (w == word) return k;
            }
            return word switch { "text" => ColumnKind.Text, "integer" => ColumnKind.Integer, "float" => ColumnKind.Real, _ => null };
        }
    }

    /// <summary>A table as migrations know it: its columns in order, its indexes, and a key of more than one column.</summary>
    internal sealed class TableSpec
    {
        public string Name { get; set; }

        public List<ColumnSpec> Columns { get; } = new List<ColumnSpec>();

        public List<TableIndex> Indexes { get; } = new List<TableIndex>();

        /// <summary>A key of two or more columns (a pivot table's), or null.</summary>
        public List<string> Key { get; set; }

        public ColumnSpec Column(string name) => Columns.FirstOrDefault(c => c.Name == name);

        /// <summary>True for a column in the key or in an index: MySQL gives text there a length.</summary>
        public bool Indexed(string column) =>
            Column(column) is ColumnSpec c && (c.IsKey || c.HasForeignKey) || Key?.Contains(column) == true || Indexes.Any(i => i.Columns.Contains(column));

        public TableIndex IndexOf(IReadOnlyList<string> columns, bool unique) =>
            Indexes.FirstOrDefault(i => i.Unique == unique && i.Columns.SequenceEqual(columns, StringComparer.Ordinal));

        public TableSpec Clone(string name = null)
        {
            var t = new TableSpec { Name = name ?? Name, Key = Key?.ToList() };
            t.Columns.AddRange(Columns);
            t.Indexes.AddRange(Indexes);
            return t;
        }

        /// <summary>The tables this one's foreign keys point to.</summary>
        public IEnumerable<string> Parents => Columns.Where(c => c.References != null && c.References != Name).Select(c => c.References).Distinct();
    }

    /// <summary>
    /// The tables a database has, as migrations know them. Replaying the migration files gives the schema they make;
    /// <see cref="FromModels"/> gives the one the models want; <c>kscript db diff</c> writes the steps between the two.
    /// </summary>
    internal sealed class Schema
    {
        private readonly List<TableSpec> tables = new List<TableSpec>();

        public IReadOnlyList<TableSpec> Tables => tables;

        public TableSpec Table(string name) => tables.FirstOrDefault(t => t.Name == name);

        public TableSpec Need(string name, string doing)
        {
            TableSpec t = Table(name);
            if (t != null) return t;
            string close = Values.Closest(name, tables.Select(x => x.Name));
            throw new KScriptException($"Can't {doing}: the migrations before this one don't make a table called {name}." + (close != null ? $" Did you mean {close}?" : string.Empty), "Migration");
        }

        public void Add(TableSpec table)
        {
            if (Table(table.Name) != null) throw new KScriptException($"Can't create the table {table.Name}: an earlier migration made it already.", "Migration");
            tables.Add(table);
        }

        public void Remove(string name) => tables.Remove(Need(name, $"drop the table {name}"));

        /// <summary>Replaces a table with a changed copy, keeping its place.</summary>
        public void Replace(string name, TableSpec changed)
        {
            int i = tables.IndexOf(Need(name, $"change the table {name}"));
            tables[i] = changed;
        }

        public Schema Clone()
        {
            var s = new Schema();
            foreach (TableSpec t in tables) s.tables.Add(t.Clone());
            return s;
        }

        /// <summary>
        /// The schema the models want: a table for each, with its columns, foreign keys and indexes as
        /// <c>$db->create_table()</c> makes them, and the pivot table of each many to many relation between them.
        /// </summary>
        public static Schema FromModels(IEnumerable<ModelInfo> models)
        {
            var schema = new Schema();
            List<ModelInfo> list = models.ToList();
            var tableNames = new HashSet<string>(list.Select(m => m.Table), StringComparer.Ordinal);
            foreach (ModelInfo model in list)
            {
                var table = new TableSpec { Name = model.Table };
                foreach (Column c in model.Columns)
                {
                    Relation r = c.References;
                    object fallback = r == null && !c.IsKey ? DefaultOf(c) : null;
                    table.Columns.Add(new ColumnSpec
                    {
                        Name = c.Name, Kind = c.Kind, Length = c.Length, Words = c.Words, IsKey = c.IsKey, AutoIncrement = c.AutoIncrement,
                        // A field with a default is never null (KScript fills it in), so with the default in the database too it can be NOT NULL.
                        Nullable = c.Nullable && (fallback == null || c.Field.Type.Optional),
                        Default = fallback,
                        References = r?.Target.Table, ReferencesColumn = r?.Target.Key, OnDelete = r == null ? null : r.OnDelete ?? "restrict",
                        ForeignKeyName = r == null ? null : Dialect.ForeignKeyName(model.Table, c.Name),
                    });
                }
                table.Indexes.AddRange(Dialect.Indexes(model));
                schema.Add(table);
            }
            foreach (Relation r in list.SelectMany(m => m.Relations.Values).Where(r => r.Kind == RelationKind.ManyToMany))
            {
                if (schema.Table(r.Pivot) != null || !tableNames.Contains(r.Owner.Table) || !tableNames.Contains(r.Target.Table)) continue;
                schema.Add(Pivot(r));
            }
            return schema;
        }

        /// <summary>A many to many relation's pivot table, as <see cref="Dialect.CreatePivot"/> makes it.</summary>
        public static TableSpec Pivot(Relation r)
        {
            Column owner = r.PivotOwnerColumn;
            Column target = r.PivotTargetColumn;
            var table = new TableSpec { Name = r.Pivot, Key = new List<string> { owner.Name, target.Name } };
            table.Columns.Add(new ColumnSpec
            {
                Name = owner.Name, Kind = owner.Kind, Length = owner.Length, Words = owner.Words,
                References = r.Owner.Table, ReferencesColumn = r.Owner.Key, OnDelete = "cascade", ForeignKeyName = Dialect.ForeignKeyName(r.Pivot, owner.Name),
            });
            table.Columns.Add(new ColumnSpec
            {
                Name = target.Name, Kind = target.Kind, Length = target.Length, Words = target.Words,
                References = r.Target.Table, ReferencesColumn = r.Target.Key, OnDelete = "cascade", ForeignKeyName = Dialect.ForeignKeyName(r.Pivot, target.Name),
            });
            table.Indexes.Add(new TableIndex(Dialect.IndexName(r.Pivot, new[] { target.Name }, false), new[] { target.Name }, false));
            return table;
        }

        /// <summary>
        /// A field's default the database can hold: a literal (<c>= 0</c>, <c>= 'member'</c>, <c>= false</c>) on a column
        /// that isn't a date, JSON or a password. Other defaults are only filled in by KScript when records are made.
        /// </summary>
        private static object DefaultOf(Column c)
        {
            ExprNode node = c.Field?.DefaultNode ?? (c.Field?.DefaultText is DecodedText text ? ExpressionParser.ParseExpression(text.Value) is { Success: true } parsed ? parsed.Root : null : null);
            if (node is not LiteralNode { Template: null } literal) return null;
            if (c.Kind is ColumnKind.Date or ColumnKind.Json or ColumnKind.Password or ColumnKind.Uuid) return null;
            return literal.Kind switch
            {
                LiteralKind.Number when c.Kind is ColumnKind.Integer or ColumnKind.Real
                    && double.TryParse(literal.Value.Replace("_", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) => n,
                LiteralKind.Bool when c.Kind == ColumnKind.Bool => literal.Value == "true",
                LiteralKind.String when c.Kind is ColumnKind.Text or ColumnKind.Enum or ColumnKind.Handle or ColumnKind.Email => literal.Value,
                _ => null,
            };
        }
    }
}
