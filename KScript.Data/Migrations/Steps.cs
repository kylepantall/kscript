using System.Globalization;
using System.Text;

using KScript.Runtime;

namespace KScript.Data.Migrations
{
    /// <summary>Reads rows during a migration, for steps that look at the database first (MySQL's check constraints). Null when only printing the SQL.</summary>
    internal delegate List<Dictionary<string, object>> Lookup(string sql, params object[] parameters);

    /// <summary>
    /// One change a migration makes: create a table, add a column... Each knows how it changes the schema, its SQL on
    /// each database, how to undo it, and how it is written in a migration file.
    /// </summary>
    internal abstract class Step
    {
        /// <summary>What it does, for messages: "add column users.bio".</summary>
        public abstract string Describe();

        /// <summary>Changes the schema as the step does, or throws when it can't be done to it.</summary>
        public abstract void Apply(Schema schema);

        /// <summary>The SQL, given the schema before and after the step.</summary>
        public abstract IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup);

        /// <summary>The steps that undo this one, given the schema before it; null when it can't be undone.</summary>
        public abstract IEnumerable<Step> Reverse(Schema before);

        /// <summary>The step as a line (or block) of a .kscript migration.</summary>
        public abstract void Write(MigrationWriter w);
    }

    internal sealed class CreateTableStep : Step
    {
        public CreateTableStep(TableSpec table) => Table = table;

        public TableSpec Table { get; }

        public override string Describe() => "create table " + Table.Name;

        public override void Apply(Schema schema)
        {
            TableSpec t = Table.Clone();
            if (t.Columns.Count == 0) throw new KScriptException($"The table {t.Name} needs at least one Field.", "Migration");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < t.Columns.Count; i++)
            {
                ColumnSpec c = t.Columns[i];
                if (!seen.Add(c.Name)) throw new KScriptException($"The table {t.Name} has two columns called {c.Name}.", "Migration");
                t.Columns[i] = References.Resolve(schema, t, c);
            }
            if (t.Columns.Count(c => c.IsKey) + (t.Key != null ? 1 : 0) > 1) throw new KScriptException($"The table {t.Name} has more than one key. A key of two columns is Key(columns: [a, b]).", "Migration");
            foreach (string k in t.Key ?? new List<string>())
            {
                if (t.Column(k) == null) throw new KScriptException($"The key of {t.Name} names {k}, which isn't one of its columns.", "Migration");
            }
            for (int i = 0; i < t.Indexes.Count; i++)
            {
                TableIndex index = t.Indexes[i];
                Indexes.Check(t, index.Columns);
                t.Indexes[i] = index with { Name = index.Name ?? Dialect.IndexName(t.Name, index.Columns, index.Unique) };
            }
            schema.Add(t);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) => ddl.CreateTable(after.Table(Table.Name));

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new DropTableStep(Table.Name) };

        public override void Write(MigrationWriter w)
        {
            w.Open("CreateTable", ("name", MigrationWriter.Name(Table.Name)));
            foreach (ColumnSpec c in Table.Columns) w.Line(MigrationWriter.Call("Field", MigrationWriter.Column(c).ToArray()));
            foreach (TableIndex i in Table.Indexes) w.Line(MigrationWriter.Call("Index", MigrationWriter.IndexArgs(null, i)));
            if (Table.Key != null) w.Line(MigrationWriter.Call("Key", ("columns", MigrationWriter.List(Table.Key))));
            w.Close();
        }
    }

    internal sealed class DropTableStep : Step
    {
        public DropTableStep(string name) => Name = name;

        public string Name { get; }

        public override string Describe() => "drop table " + Name;

        public override void Apply(Schema schema)
        {
            schema.Need(Name, $"drop the table {Name}");
            foreach (TableSpec other in schema.Tables.Where(t => t.Name != Name))
            {
                ColumnSpec link = other.Columns.FirstOrDefault(c => c.References == Name);
                if (link != null) throw new KScriptException($"Can't drop the table {Name}: {other.Name}.{link.Name} points to it. Drop that column or table first.", "Migration");
            }
            schema.Remove(Name);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) => ddl.DropTable(before.Table(Name));

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new CreateTableStep(before.Need(Name, $"undo dropping {Name}").Clone()) };

        public override void Write(MigrationWriter w) => w.Line(MigrationWriter.Call("DropTable", ("name", MigrationWriter.Name(Name))));
    }

    internal sealed class RenameTableStep : Step
    {
        public RenameTableStep(string from, string to)
        {
            From = from;
            To = to;
        }

        public string From { get; }

        public string To { get; }

        public override string Describe() => $"rename table {From} to {To}";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(From, $"rename the table {From}");
            if (schema.Table(To) != null) throw new KScriptException($"Can't rename {From} to {To}: there is a table called {To} already.", "Migration");
            schema.Replace(From, t.Clone(To));
            // The databases change the foreign keys that point to a renamed table too.
            foreach (TableSpec other in schema.Tables)
            {
                for (int i = 0; i < other.Columns.Count; i++)
                {
                    if (other.Columns[i].References == From) other.Columns[i] = Retarget(other.Columns[i], To, other.Columns[i].ReferencesColumn);
                }
            }
        }

        internal static ColumnSpec Retarget(ColumnSpec c, string table, string column) => new ColumnSpec
        {
            Name = c.Name, Kind = c.Kind, Nullable = c.Nullable, Length = c.Length, Words = c.Words, IsKey = c.IsKey, AutoIncrement = c.AutoIncrement,
            Default = c.Default, References = table, ReferencesColumn = column, OnDelete = c.OnDelete, ForeignKeyName = c.ForeignKeyName,
        };

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) => ddl.RenameTable(From, To);

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new RenameTableStep(To, From) };

        public override void Write(MigrationWriter w) => w.Line(MigrationWriter.Call("RenameTable", ("from", MigrationWriter.Name(From)), ("to", MigrationWriter.Name(To))));
    }

    internal sealed class AddColumnStep : Step
    {
        public AddColumnStep(string table, ColumnSpec column)
        {
            Table = table;
            Column = column;
        }

        public string Table { get; }

        public ColumnSpec Column { get; }

        public override string Describe() => $"add column {Table}.{Column.Name}";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(Table, $"add the column {Column.Name}").Clone();
            if (t.Column(Column.Name) != null) throw new KScriptException($"Can't add {Column.Name} to {Table}: it has that column already.", "Migration");
            if (Column.IsKey || Column.AutoIncrement) throw new KScriptException($"Can't add the key {Column.Name} to {Table}: a table's key is made with it.", "Migration");
            t.Columns.Add(References.Resolve(schema, t, Column));
            schema.Replace(Table, t);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) =>
            ddl.AddColumn(before.Table(Table), after.Table(Table), after.Table(Table).Column(Column.Name));

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new DropColumnStep(Table, Column.Name) };

        public override void Write(MigrationWriter w) =>
            w.Line(MigrationWriter.Call("AddColumn", new[] { ("table", MigrationWriter.Name(Table)) }.Concat(MigrationWriter.Column(Column)).ToArray()));
    }

    internal sealed class DropColumnStep : Step
    {
        public DropColumnStep(string table, string name)
        {
            Table = table;
            Name = name;
        }

        public string Table { get; }

        public string Name { get; }

        public override string Describe() => $"drop column {Table}.{Name}";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(Table, $"drop the column {Name}").Clone();
            ColumnSpec c = t.Column(Name) ?? throw References.NoColumn(t, Name, $"drop the column {Name}");
            if (c.IsKey || t.Key?.Contains(Name) == true) throw new KScriptException($"Can't drop {Table}.{Name}: it is the table's key.", "Migration");
            foreach (TableSpec other in schema.Tables)
            {
                ColumnSpec link = other.Columns.FirstOrDefault(o => o.References == Table && o.ReferencesColumn == Name);
                if (link != null) throw new KScriptException($"Can't drop {Table}.{Name}: {other.Name}.{link.Name} points to it.", "Migration");
            }
            t.Columns.Remove(c);
            t.Indexes.RemoveAll(i => i.Columns.Contains(Name));
            schema.Replace(Table, t);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) =>
            ddl.DropColumn(before.Table(Table), after.Table(Table), before.Table(Table).Column(Name));

        public override IEnumerable<Step> Reverse(Schema before)
        {
            TableSpec t = before.Need(Table, $"undo dropping {Table}.{Name}");
            ColumnSpec c = t.Column(Name) ?? throw References.NoColumn(t, Name, $"undo dropping {Name}");
            yield return new AddColumnStep(Table, c);
            // The indexes that went with the column come back with it.
            foreach (TableIndex i in t.Indexes.Where(i => i.Columns.Contains(Name))) yield return new AddIndexStep(Table, i.Columns, i.Unique);
        }

        public override void Write(MigrationWriter w) => w.Line(MigrationWriter.Call("DropColumn", ("table", MigrationWriter.Name(Table)), ("name", MigrationWriter.Name(Name))));
    }

    internal sealed class RenameColumnStep : Step
    {
        public RenameColumnStep(string table, string from, string to)
        {
            Table = table;
            From = from;
            To = to;
        }

        public string Table { get; }

        public string From { get; }

        public string To { get; }

        public override string Describe() => $"rename column {Table}.{From} to {To}";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(Table, $"rename the column {From}").Clone();
            ColumnSpec c = t.Column(From) ?? throw References.NoColumn(t, From, $"rename the column {From}");
            if (t.Column(To) != null) throw new KScriptException($"Can't rename {Table}.{From} to {To}: there is a column called {To} already.", "Migration");
            t.Columns[t.Columns.IndexOf(c)] = c.With(name: To);
            for (int i = 0; i < t.Indexes.Count; i++)
            {
                TableIndex index = t.Indexes[i];
                if (index.Columns.Contains(From)) t.Indexes[i] = index with { Columns = index.Columns.Select(n => n == From ? To : n).ToList() };
            }
            if (t.Key != null) t.Key = t.Key.Select(n => n == From ? To : n).ToList();
            schema.Replace(Table, t);
            foreach (TableSpec other in schema.Tables)
            {
                for (int i = 0; i < other.Columns.Count; i++)
                {
                    ColumnSpec o = other.Columns[i];
                    if (o.References == Table && o.ReferencesColumn == From) other.Columns[i] = RenameTableStep.Retarget(o, Table, To);
                }
            }
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) => ddl.RenameColumn(before.Table(Table), From, To);

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new RenameColumnStep(Table, To, From) };

        public override void Write(MigrationWriter w) =>
            w.Line(MigrationWriter.Call("RenameColumn", ("table", MigrationWriter.Name(Table)), ("from", MigrationWriter.Name(From)), ("to", MigrationWriter.Name(To))));
    }

    internal sealed class ChangeColumnStep : Step
    {
        public ChangeColumnStep(string table, ColumnSpec column)
        {
            Table = table;
            Column = column;
        }

        public string Table { get; }

        public ColumnSpec Column { get; }

        public override string Describe() => $"change column {Table}.{Column.Name}";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(Table, $"change the column {Column.Name}").Clone();
            ColumnSpec old = t.Column(Column.Name) ?? throw References.NoColumn(t, Column.Name, $"change the column {Column.Name}");
            if (Column.IsKey != old.IsKey || Column.AutoIncrement != old.AutoIncrement)
                throw new KScriptException($"Can't change whether {Table}.{Column.Name} is the key. Make a new table instead.", "Migration");
            ColumnSpec changed = References.Resolve(schema, t, Column);
            // A foreign key keeps its name, so a renamed table's constraint can still be found.
            if (changed.References != null && old.ForeignKeyName != null) changed = changed.With(foreignKeyName: old.ForeignKeyName);
            t.Columns[t.Columns.IndexOf(old)] = changed;
            schema.Replace(Table, t);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) =>
            ddl.ChangeColumn(before.Table(Table), after.Table(Table), before.Table(Table).Column(Column.Name), after.Table(Table).Column(Column.Name), lookup);

        public override IEnumerable<Step> Reverse(Schema before)
        {
            TableSpec t = before.Need(Table, $"undo changing {Table}.{Column.Name}");
            return new[] { new ChangeColumnStep(Table, t.Column(Column.Name) ?? throw References.NoColumn(t, Column.Name, "undo the change")) };
        }

        public override void Write(MigrationWriter w) =>
            w.Line(MigrationWriter.Call("ChangeColumn", new[] { ("table", MigrationWriter.Name(Table)) }.Concat(MigrationWriter.Column(Column)).ToArray()));
    }

    internal sealed class AddIndexStep : Step
    {
        public AddIndexStep(string table, IReadOnlyList<string> columns, bool unique)
        {
            Table = table;
            Columns = columns;
            Unique = unique;
        }

        public string Table { get; }

        public IReadOnlyList<string> Columns { get; }

        public bool Unique { get; }

        public override string Describe() => $"add {(Unique ? "unique " : string.Empty)}index on {Table} ({string.Join(", ", Columns)})";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(Table, "add the index").Clone();
            Indexes.Check(t, Columns);
            if (t.IndexOf(Columns, Unique) != null) throw new KScriptException($"{Table} has that index already: ({string.Join(", ", Columns)}).", "Migration");
            t.Indexes.Add(new TableIndex(Dialect.IndexName(Table, Columns, Unique), Columns.ToList(), Unique));
            schema.Replace(Table, t);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) =>
            ddl.AddIndex(after.Table(Table), after.Table(Table).IndexOf(Columns, Unique));

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new DropIndexStep(Table, Columns, Unique) };

        public override void Write(MigrationWriter w) => w.Line(MigrationWriter.Call("AddIndex", MigrationWriter.IndexArgs(Table, new TableIndex(null, Columns, Unique))));
    }

    internal sealed class DropIndexStep : Step
    {
        public DropIndexStep(string table, IReadOnlyList<string> columns, bool unique)
        {
            Table = table;
            Columns = columns;
            Unique = unique;
        }

        public string Table { get; }

        public IReadOnlyList<string> Columns { get; }

        public bool Unique { get; }

        public override string Describe() => $"drop {(Unique ? "unique " : string.Empty)}index on {Table} ({string.Join(", ", Columns)})";

        public override void Apply(Schema schema)
        {
            TableSpec t = schema.Need(Table, "drop the index").Clone();
            TableIndex index = t.IndexOf(Columns, Unique)
                ?? throw new KScriptException($"{Table} has no {(Unique ? "unique " : string.Empty)}index on ({string.Join(", ", Columns)}) to drop.", "Migration");
            t.Indexes.Remove(index);
            schema.Replace(Table, t);
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) =>
            ddl.DropIndex(before.Table(Table), before.Table(Table).IndexOf(Columns, Unique));

        public override IEnumerable<Step> Reverse(Schema before) => new[] { new AddIndexStep(Table, Columns, Unique) };

        public override void Write(MigrationWriter w) => w.Line(MigrationWriter.Call("DropIndex", MigrationWriter.IndexArgs(Table, new TableIndex(null, Columns, Unique))));
    }

    /// <summary>SQL written by hand. db diff can't see what it does to the tables, and only a down: lets rollback undo it.</summary>
    internal sealed class SqlStep : Step
    {
        public SqlStep(string up, string down)
        {
            Up = up;
            Down = down;
        }

        public string Up { get; }

        public string Down { get; }

        public override string Describe() => "SQL: " + (Up.Length > 40 ? Up.Substring(0, 40) + "…" : Up);

        public override void Apply(Schema schema)
        {
        }

        public override IEnumerable<string> Sql(Ddl ddl, Schema before, Schema after, Lookup lookup) => new[] { Up };

        public override IEnumerable<Step> Reverse(Schema before) => Down == null ? null : new[] { new SqlStep(Down, Up) };

        public override void Write(MigrationWriter w) =>
            w.Line(MigrationWriter.Call("Sql", new[] { ("up", MigrationWriter.Text(Up)) }.Concat(Down == null ? Array.Empty<(string, string)>() : new[] { ("down", MigrationWriter.Text(Down)) }).ToArray()));
    }

    /// <summary>Checks on foreign keys: the table a column points to must be there, and the column takes its key's type.</summary>
    internal static class References
    {
        public static ColumnSpec Resolve(Schema schema, TableSpec table, ColumnSpec c)
        {
            if (c.References == null) return c;
            TableSpec target = c.References == table.Name ? table : schema.Table(c.References)
                ?? throw new KScriptException($"{table.Name}.{c.Name} points to {c.References}, but there is no table called {c.References} by then. Create it first.", "Migration");
            string key = c.ReferencesColumn ?? target.Columns.FirstOrDefault(x => x.IsKey)?.Name
                ?? throw new KScriptException($"{table.Name}.{c.Name} points to {c.References}, which has no key of one column. Say which column with references_column.", "Migration");
            if (target.Column(key) == null) throw NoColumn(target, key, $"point {table.Name}.{c.Name} to it");
            if (!target.Column(key).IsKey && target.IndexOf(new[] { key }, unique: true) == null)
                throw new KScriptException($"{table.Name}.{c.Name} points to {target.Name}.{key}, which isn't the table's key or unique, so a row could match more than one. Give it a unique index first: AddIndex(table: {target.Name}, columns: [{key}], unique: yes).", "Migration");
            return new ColumnSpec
            {
                Name = c.Name, Kind = c.Kind, Nullable = c.Nullable, Length = c.Length, Words = c.Words, IsKey = c.IsKey, AutoIncrement = c.AutoIncrement, Default = c.Default,
                References = target.Name, ReferencesColumn = key, OnDelete = c.OnDelete ?? (c.Nullable ? "set null" : "cascade"),
                ForeignKeyName = c.ForeignKeyName ?? Dialect.ForeignKeyName(table.Name, c.Name),
            };
        }

        public static KScriptException NoColumn(TableSpec t, string name, string doing)
        {
            string close = Values.Closest(name, t.Columns.Select(c => c.Name));
            return new KScriptException($"Can't {doing}: {t.Name} has no column called {name}." + (close != null ? $" Did you mean {close}?" : string.Empty), "Migration");
        }
    }

    internal static class Indexes
    {
        public static void Check(TableSpec t, IReadOnlyList<string> columns)
        {
            if (columns.Count == 0) throw new KScriptException($"An index of {t.Name} needs its columns: columns: [a, b].", "Migration");
            foreach (string c in columns)
            {
                if (t.Column(c) == null) throw References.NoColumn(t, c, "index it");
            }
        }
    }

    /// <summary>Writes a migration file's steps as .kscript.</summary>
    internal sealed class MigrationWriter
    {
        private readonly StringBuilder sb = new StringBuilder();
        private int depth;

        public void Line(string text) => sb.Append(' ', depth * 2).Append(text).Append('\n');

        public void Open(string element, params (string Name, string Value)[] args)
        {
            Line(Call(element, args) + " {");
            depth++;
        }

        public void Close()
        {
            depth--;
            Line("}");
        }

        public override string ToString() => sb.ToString();

        /// <summary>A whole migration file, as .kscript.</summary>
        public static string File(IEnumerable<Step> steps, string comment)
        {
            var w = new MigrationWriter();
            if (comment != null) w.Line("// " + comment);
            w.Open("KScript.Migration");
            foreach (Step step in steps) step.Write(w);
            w.Close();
            return w.ToString();
        }

        public static string Call(string element, params (string Name, string Value)[] args) =>
            args.Length == 0 ? element + (element.StartsWith("KScript.", StringComparison.Ordinal) ? string.Empty : "()") : element + "(" + string.Join(", ", args.Select(a => a.Name + ": " + a.Value)) + ")";

        /// <summary>A table or column name: bare when it is a word, else in quotes.</summary>
        public static string Name(string name) =>
            name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_') ? name : Text(name);

        /// <summary>Text as a migration writes it: 'single quotes', which are taken as written, or "double quotes" for text with a ' in it.</summary>
        public static string Text(string text) => text.Contains('\'') ? "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : "'" + text + "'";

        public static string List(IEnumerable<string> names) => "[" + string.Join(", ", names.Select(Name)) + "]";

        public static (string, string)[] IndexArgs(string table, TableIndex index)
        {
            var args = new List<(string, string)>();
            if (table != null) args.Add(("table", Name(table)));
            args.Add(("columns", List(index.Columns)));
            if (index.Unique) args.Add(("unique", "yes"));
            return args.ToArray();
        }

        /// <summary>A column's arguments: name, type, then only what isn't the default.</summary>
        public static IEnumerable<(string, string)> Column(ColumnSpec c)
        {
            yield return ("name", Name(c.Name));
            yield return ("type", c.TypeWord);
            if (c.Kind == ColumnKind.Enum && c.Words != null) yield return ("values", List(c.Words));
            if (c.Length != null && !(c.Kind == ColumnKind.Email && c.Length == 254)) yield return ("max", c.Length.Value.ToString(CultureInfo.InvariantCulture));
            if (c.Nullable) yield return ("null", "yes");
            if (c.IsKey) yield return ("key", "yes");
            if (c.AutoIncrement) yield return ("auto", "yes");
            if (c.Default != null) yield return ("default", Literal(c.Default));
            if (c.References != null)
            {
                yield return ("references", Name(c.References));
                if (c.ReferencesColumn != null && c.ReferencesColumn != "id") yield return ("references_column", Name(c.ReferencesColumn));
                string usual = c.Nullable ? "set null" : "cascade";
                if (c.OnDelete != null && c.OnDelete != usual) yield return ("on_delete", c.OnDelete.Replace(' ', '_'));
            }
        }

        public static string Literal(object value) => value switch
        {
            bool b => b ? "true" : "false",
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };
    }
}
