using System.Globalization;

namespace KScript.Data.Migrations
{
    /// <summary>
    /// The SQL each database needs for each kind of step. SQLite can't change or drop most columns in place, so it
    /// makes the table again and copies the rows; Postgres and MySQL alter it.
    /// </summary>
    internal abstract class Ddl
    {
        protected Ddl(Dialect dialect) => D = dialect;

        protected Dialect D { get; }

        public static Ddl For(Dialect dialect) => dialect switch
        {
            SqliteDialect => new SqliteDdl(dialect),
            PostgresDialect => new PostgresDdl(dialect),
            _ => new MySqlDdl(dialect),
        };

        /// <summary>True when a migration's steps can run in one transaction, so a failed migration changes nothing. MySQL commits each step itself.</summary>
        public virtual bool Transactional => true;

        protected string Q(string name) => D.Quote(name);

        /// <summary>The lines inside CREATE TABLE: the columns, a key of two columns, and the foreign keys.</summary>
        protected List<string> Lines(TableSpec t)
        {
            var lines = t.Columns.Select(c => D.ColumnDefinition(c.ToColumn(), t.Indexed(c.Name))).ToList();
            if (t.Key != null) lines.Add($"PRIMARY KEY ({string.Join(", ", t.Key.Select(Q))})");
            lines.AddRange(t.Columns.Where(c => c.HasForeignKey).Select(ForeignKey));
            return lines;
        }

        protected string ForeignKey(ColumnSpec c) =>
            $"CONSTRAINT {Q(c.ForeignKeyName)} FOREIGN KEY ({Q(c.Name)}) REFERENCES {Q(c.References)} ({Q(c.ReferencesColumn)}) ON DELETE {c.OnDelete.ToUpperInvariant()}";

        public virtual IEnumerable<string> CreateTable(TableSpec t) => Prepare(t.Columns).Concat(D.CreateTable(t.Name, Lines(t), t.Indexes, ifMissing: false));

        /// <summary>Statements a column needs first, such as Postgres's collation for emails.</summary>
        protected virtual IEnumerable<string> Prepare(IEnumerable<ColumnSpec> columns) => Enumerable.Empty<string>();

        public virtual IEnumerable<string> DropTable(TableSpec t) => new[] { "DROP TABLE " + Q(t.Name) };

        public virtual IEnumerable<string> RenameTable(string from, string to) => new[] { $"ALTER TABLE {Q(from)} RENAME TO {Q(to)}" };

        public abstract IEnumerable<string> AddColumn(TableSpec before, TableSpec after, ColumnSpec c);

        public abstract IEnumerable<string> DropColumn(TableSpec before, TableSpec after, ColumnSpec c);

        public virtual IEnumerable<string> RenameColumn(TableSpec before, string from, string to) =>
            new[] { $"ALTER TABLE {Q(before.Name)} RENAME COLUMN {Q(from)} TO {Q(to)}" };

        public abstract IEnumerable<string> ChangeColumn(TableSpec before, TableSpec after, ColumnSpec old, ColumnSpec now, Lookup lookup);

        public virtual IEnumerable<string> AddIndex(TableSpec t, TableIndex i) =>
            new[] { $"CREATE {(i.Unique ? "UNIQUE " : string.Empty)}INDEX {Q(i.Name)} ON {Q(t.Name)} ({string.Join(", ", i.Columns.Select(Q))})" };

        public virtual IEnumerable<string> DropIndex(TableSpec t, TableIndex i) => new[] { "DROP INDEX " + Q(i.Name) };

        protected string Definition(TableSpec t, ColumnSpec c) => D.ColumnDefinition(c.ToColumn(), t.Indexed(c.Name));
    }

    internal sealed class SqliteDdl : Ddl
    {
        public SqliteDdl(Dialect dialect) : base(dialect)
        {
        }

        public override IEnumerable<string> AddColumn(TableSpec before, TableSpec after, ColumnSpec c)
        {
            // ALTER TABLE ADD COLUMN can't add a foreign key with its name, or a required column without a default.
            if ((c.Nullable || c.Default != null) && !c.HasForeignKey) return new[] { $"ALTER TABLE {Q(after.Name)} ADD COLUMN {Definition(after, c)}" };
            return Rebuild(before, after);
        }

        public override IEnumerable<string> DropColumn(TableSpec before, TableSpec after, ColumnSpec c) => Rebuild(before, after);

        public override IEnumerable<string> ChangeColumn(TableSpec before, TableSpec after, ColumnSpec old, ColumnSpec now, Lookup lookup) => Rebuild(before, after);

        /// <summary>
        /// The table made again as <paramref name="after"/>, with the rows copied, the way sqlite.org/lang_altertable.html
        /// says to. The migration runs with foreign keys off, so dropping the old table deletes nothing that points to it.
        /// A column that is now required takes its default where it was null.
        /// </summary>
        private IEnumerable<string> Rebuild(TableSpec before, TableSpec after)
        {
            string temp = "_ks_new_" + after.Name;
            foreach (string s in D.CreateTable(temp, Lines(after), Array.Empty<TableIndex>(), ifMissing: false)) yield return s;
            List<ColumnSpec> copied = after.Columns.Where(c => before.Column(c.Name) != null).ToList();
            if (copied.Count > 0)
            {
                IEnumerable<string> values = copied.Select(c =>
                    before.Column(c.Name).Nullable && !c.Nullable && c.Default != null ? $"COALESCE({Q(c.Name)}, {D.DefaultValue(c.ToColumn())})" : Q(c.Name));
                yield return $"INSERT INTO {Q(temp)} ({string.Join(", ", copied.Select(c => Q(c.Name)))}) SELECT {string.Join(", ", values)} FROM {Q(before.Name)}";
            }
            yield return "DROP TABLE " + Q(before.Name);
            yield return $"ALTER TABLE {Q(temp)} RENAME TO {Q(after.Name)}";
            foreach (TableIndex i in after.Indexes)
            {
                foreach (string s in AddIndex(after, i)) yield return s;
            }
        }
    }

    internal sealed class PostgresDdl : Ddl
    {
        public PostgresDdl(Dialect dialect) : base(dialect)
        {
        }

        protected override IEnumerable<string> Prepare(IEnumerable<ColumnSpec> columns)
        {
            if (columns.Any(c => c.Kind == ColumnKind.Email))
                yield return $"CREATE COLLATION IF NOT EXISTS {Q(PostgresDialect.Collation)} (provider = icu, locale = 'und-u-ks-level2', deterministic = false)";
        }

        public override IEnumerable<string> AddColumn(TableSpec before, TableSpec after, ColumnSpec c)
        {
            foreach (string s in Prepare(new[] { c })) yield return s;
            yield return $"ALTER TABLE {Q(after.Name)} ADD COLUMN {Definition(after, c)}";
            if (c.HasForeignKey) yield return $"ALTER TABLE {Q(after.Name)} ADD {ForeignKey(c)}";
        }

        public override IEnumerable<string> DropColumn(TableSpec before, TableSpec after, ColumnSpec c)
        {
            // Postgres drops the column's indexes and foreign key with it.
            yield return $"ALTER TABLE {Q(before.Name)} DROP COLUMN {Q(c.Name)}";
        }

        public override IEnumerable<string> ChangeColumn(TableSpec before, TableSpec after, ColumnSpec old, ColumnSpec now, Lookup lookup)
        {
            string table = Q(after.Name);
            string column = Q(now.Name);
            string alter = $"ALTER TABLE {table} ALTER COLUMN {column}";
            if (old.HasForeignKey) yield return $"ALTER TABLE {table} DROP CONSTRAINT IF EXISTS {Q(old.ForeignKeyName)}";
            if (old.Words != null)
            {
                // A check made with the table has the name Postgres gave it, so it is looked up by its column.
                if (lookup == null)
                {
                    yield return $"-- drops the check constraints on {after.Name}.{now.Name}";
                }
                else
                {
                    foreach (Dictionary<string, object> row in lookup(
                        "SELECT c.conname AS name FROM pg_constraint c JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey) " +
                        "WHERE c.contype = 'c' AND c.conrelid = to_regclass(@p1) AND a.attname = @p2", Q(after.Name), now.Name))
                    {
                        yield return $"ALTER TABLE {table} DROP CONSTRAINT {Q(Convert.ToString(row["name"], CultureInfo.InvariantCulture))}";
                    }
                }
            }
            string type = D.ColumnType(now.ToColumn(), after.Indexed(now.Name));
            if (type != D.ColumnType(old.ToColumn(), before.Indexed(old.Name)) || (old.Kind == ColumnKind.Email) != (now.Kind == ColumnKind.Email))
            {
                foreach (string s in Prepare(new[] { now })) yield return s;
                string collate = now.Kind == ColumnKind.Email ? " COLLATE " + Q(PostgresDialect.Collation) : " COLLATE \"default\"";
                yield return $"{alter} DROP DEFAULT";
                yield return $"{alter} TYPE {type}{(type == "TEXT" ? collate : string.Empty)} USING {column}::{type}";
            }
            if (old.Nullable != now.Nullable) yield return $"{alter} {(now.Nullable ? "DROP" : "SET")} NOT NULL";
            if (now.Default != null) yield return $"{alter} SET DEFAULT {D.DefaultValue(now.ToColumn())}";
            else if (old.Default != null) yield return $"{alter} DROP DEFAULT";
            if (now.Words != null)
                yield return $"ALTER TABLE {table} ADD CONSTRAINT {Q(after.Name + "_" + now.Name + "_check")} CHECK ({column} IN ({string.Join(", ", now.Words.Select(w => "'" + w.Replace("'", "''") + "'"))}))";
            if (now.HasForeignKey) yield return $"ALTER TABLE {table} ADD {ForeignKey(now)}";
        }

        public override IEnumerable<string> DropIndex(TableSpec t, TableIndex i) => new[] { "DROP INDEX IF EXISTS " + Q(i.Name) };
    }

    internal sealed class MySqlDdl : Ddl
    {
        public MySqlDdl(Dialect dialect) : base(dialect)
        {
        }

        public override bool Transactional => false;

        public override IEnumerable<string> RenameTable(string from, string to) => new[] { $"RENAME TABLE {Q(from)} TO {Q(to)}" };

        public override IEnumerable<string> AddColumn(TableSpec before, TableSpec after, ColumnSpec c)
        {
            yield return $"ALTER TABLE {Q(after.Name)} ADD COLUMN {Definition(after, c)}";
            if (c.HasForeignKey) yield return $"ALTER TABLE {Q(after.Name)} ADD {ForeignKey(c)}";
        }

        public override IEnumerable<string> DropColumn(TableSpec before, TableSpec after, ColumnSpec c)
        {
            if (c.HasForeignKey) yield return $"ALTER TABLE {Q(before.Name)} DROP FOREIGN KEY {Q(c.ForeignKeyName)}";
            foreach (TableIndex i in before.Indexes.Where(i => i.Columns.Contains(c.Name)))
            {
                foreach (string s in DropIndex(before, i)) yield return s;
            }
            yield return $"ALTER TABLE {Q(before.Name)} DROP COLUMN {Q(c.Name)}";
        }

        public override IEnumerable<string> ChangeColumn(TableSpec before, TableSpec after, ColumnSpec old, ColumnSpec now, Lookup lookup)
        {
            string table = Q(after.Name);
            if (old.HasForeignKey) yield return $"ALTER TABLE {table} DROP FOREIGN KEY {Q(old.ForeignKeyName)}";
            if (old.Words != null)
            {
                // MySQL keeps a column's check as one of the table's, and MODIFY would add another, so the old one goes
                // first. MariaDB keeps it with the column, and MODIFY replaces it.
                if (lookup == null)
                {
                    yield return $"-- drops the check constraints on {after.Name}.{now.Name} (MySQL)";
                }
                else if (!Convert.ToString(lookup("SELECT VERSION() AS v")[0]["v"], CultureInfo.InvariantCulture).Contains("MariaDB", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (Dictionary<string, object> row in lookup(
                        "SELECT tc.CONSTRAINT_NAME AS name, cc.CHECK_CLAUSE AS clause FROM information_schema.TABLE_CONSTRAINTS tc JOIN information_schema.CHECK_CONSTRAINTS cc " +
                        "ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME " +
                        "WHERE tc.TABLE_SCHEMA = DATABASE() AND tc.TABLE_NAME = @p1 AND tc.CONSTRAINT_TYPE = 'CHECK'", after.Name))
                    {
                        if (Convert.ToString(row["clause"], CultureInfo.InvariantCulture).Contains(Q(now.Name), StringComparison.Ordinal))
                            yield return $"ALTER TABLE {table} DROP CHECK {Q(Convert.ToString(row["name"], CultureInfo.InvariantCulture))}";
                    }
                }
            }
            yield return $"ALTER TABLE {table} MODIFY COLUMN {Definition(after, now)}";
            if (now.HasForeignKey) yield return $"ALTER TABLE {table} ADD {ForeignKey(now)}";
        }

        public override IEnumerable<string> AddIndex(TableSpec t, TableIndex i)
        {
            // Text that may be long is LONGTEXT, which MySQL indexes by its first 255 characters.
            IEnumerable<string> columns = i.Columns.Select(n => t.Column(n) is ColumnSpec c && D.ColumnType(c.ToColumn(), indexed: false) == "LONGTEXT" ? Q(n) + "(255)" : Q(n));
            return new[] { $"CREATE {(i.Unique ? "UNIQUE " : string.Empty)}INDEX {Q(i.Name)} ON {Q(t.Name)} ({string.Join(", ", columns)})" };
        }

        public override IEnumerable<string> DropIndex(TableSpec t, TableIndex i) => new[] { $"DROP INDEX {Q(i.Name)} ON {Q(t.Name)}" };
    }
}
