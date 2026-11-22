using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using KScript.Runtime;

using Microsoft.Data.Sqlite;

namespace KScript.Data
{
    /// <summary>An index of a table: its name, its columns, and whether two rows may share its values.</summary>
    internal sealed record TableIndex(string Name, IReadOnlyList<string> Columns, bool Unique);

    /// <summary>
    /// What differs between databases: how names are quoted, column types, how a connection opens, and which errors
    /// mean a unique or foreign key clash. The query builder only ever talks to a dialect.
    /// </summary>
    internal abstract class Dialect
    {
        public abstract string Name { get; }

        public virtual string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

        public virtual string Parameter(int number) => "@p" + number.ToString(CultureInfo.InvariantCulture);

        public abstract DbConnection Open(DatabaseSettings settings);

        /// <summary>Closes the connections kept in pools, so a database can be dropped or a file deleted.</summary>
        public abstract void ClearPools();

        /// <summary>The SQL type of a column. <paramref name="indexed"/> is true for a column in a key or an index.</summary>
        public abstract string ColumnType(Column column, bool indexed);

        /// <summary>A bool as the database stores it.</summary>
        public abstract object Bool(bool value);

        /// <summary>A date as the database stores it.</summary>
        public virtual object Date(DateTime value) => value.ToString(Rows.DateFormat, CultureInfo.InvariantCulture);

        /// <summary>The type of a column of whole numbers that can hold milliseconds since 1970.</summary>
        public virtual string BigInteger => "BIGINT";

        /// <summary>The type of a text column that is a key or in an index.</summary>
        public virtual string KeyText => "TEXT";

        /// <summary>The type of a column of text of any length.</summary>
        public virtual string LongText => "TEXT";

        /// <summary>LIMIT and OFFSET, after ORDER BY.</summary>
        public virtual string Limit(int? limit, int? offset)
        {
            var sb = new StringBuilder();
            if (limit != null) sb.Append(" LIMIT ").Append(limit.Value.ToString(CultureInfo.InvariantCulture));
            if (offset != null)
            {
                if (limit == null) sb.Append(NoLimit);
                sb.Append(" OFFSET ").Append(offset.Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>The LIMIT an OFFSET needs before it when there is no limit, or empty.</summary>
        protected virtual string NoLimit => string.Empty;

        /// <summary>ASC or DESC after a column in ORDER BY. Nulls come first going up, as in SQLite and MySQL.</summary>
        public virtual string Direction(bool descending) => descending ? " DESC" : " ASC";

        /// <summary>FOR UPDATE and similar, at the end of a SELECT. Empty where the database locks in another way.</summary>
        public virtual string Lock(string mode, bool skipLocked) =>
            (mode == "share" ? " FOR SHARE" : " FOR UPDATE") + (skipLocked ? " SKIP LOCKED" : string.Empty);

        /// <summary>A function's name in this database: LENGTH counts characters everywhere.</summary>
        public virtual string Function(string name) => name;

        /// <summary><c>a LIKE b</c>, as SQLite has it: % and _ are wildcards, \ escapes them, and case doesn't matter.</summary>
        public virtual void Like(SqlWriter w, SqlExpr left, SqlExpr right)
        {
            w.Expr(left, nested: true).Append(" LIKE ").Expr(right, nested: true).Append(" ESCAPE '\\'");
        }

        /// <summary>Text joined: <c>a || b</c>.</summary>
        public virtual void Concat(SqlWriter w, SqlExpr left, SqlExpr right)
        {
            w.Expr(left, nested: true).Append(" || ").Expr(right, nested: true);
        }

        /// <summary>A column matched against a regex.</summary>
        public abstract void Matches(SqlWriter w, SqlExpr target, RegexValue regex);

        /// <summary>
        /// The regex's flags written into its pattern, (?i) and the like, for databases whose regexes read them the way
        /// .NET does (MySQL's ICU and MariaDB's PCRE).
        /// </summary>
        protected static string InlineFlags(RegexValue regex)
        {
            string flags = new string(regex.Flags.Where(f => f is 'i' or 'm' or 's' or 'x').Distinct().ToArray());
            return (flags.Length > 0 ? "(?" + flags + ")" : string.Empty) + regex.Pattern;
        }

        /// <summary>What an INSERT with no values says.</summary>
        public virtual string DefaultValues => " DEFAULT VALUES";

        /// <summary>True when INSERT ... RETURNING gives the new row's key.</summary>
        public virtual bool Returning => true;

        /// <summary>How rows of a table with no model are put in order, so ->chunk() reads each once.</summary>
        public virtual SqlExpr RowOrder => new SqlRaw("1");

        /// <summary>
        /// An INSERT that, when a row with the same <paramref name="key"/> is already there, sets that row's
        /// <paramref name="update"/> columns to the new values instead.
        /// </summary>
        public virtual string Upsert(string insert, string key, IEnumerable<string> update) =>
            insert + $" ON CONFLICT ({Quote(key)}) DO UPDATE SET " + string.Join(", ", update.Select(c => $"{Quote(c)} = excluded.{Quote(c)}"));

        /// <summary>An INSERT that adds nothing when a row with the same <paramref name="key"/> is already there.</summary>
        public virtual string InsertOrIgnore(string insert, string key) => insert + $" ON CONFLICT ({Quote(key)}) DO NOTHING";

        /// <summary>True when an upsert can have a WHERE: <c>ON CONFLICT ... DO UPDATE ... WHERE</c>.</summary>
        public virtual bool ConditionalUpsert => true;

        /// <summary>
        /// The name of an index: ux_ (unique) or ix_, the table and the columns. A name longer than databases allow
        /// is cut, and ends with a hash of the whole name so it stays one of a kind.
        /// </summary>
        public static string IndexName(string table, IEnumerable<string> columns, bool unique)
        {
            string name = (unique ? "ux_" : "ix_") + table + "_" + string.Join("_", columns);
            if (name.Length <= 60) return name;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).Substring(0, 8).ToLowerInvariant();
            return name.Substring(0, 51) + "_" + hash;
        }

        /// <summary>The CREATE TABLE and CREATE INDEX statements for a model.</summary>
        public IEnumerable<string> CreateTable(ModelInfo model, bool ifMissing)
        {
            List<TableIndex> indexes = Indexes(model);
            var indexed = new HashSet<string>(indexes.SelectMany(i => i.Columns), StringComparer.Ordinal);
            var columns = model.Columns.Select(c => ColumnDefinition(c, c.IsKey || indexed.Contains(c.Name)))
                .Concat(model.Columns.Where(c => c.References != null).Select(c => ForeignKey(model.Table, c.Name, c.References.Target.Table, c.References.Target.Key, c.References.OnDelete)))
                .ToList();
            return Prepare(model).Concat(CreateTable(model.Table, columns, indexes, ifMissing));
        }

        /// <summary>
        /// A model's indexes: its unique and index settings, and one on each belongs-to column that no other index
        /// starts with, so loading and counting relations stays fast.
        /// </summary>
        public static List<TableIndex> Indexes(ModelInfo model)
        {
            var indexes = model.Unique.Select(u => new TableIndex(IndexName(model.Table, u, true), u, true))
                .Concat(model.Index.Select(i => new TableIndex(IndexName(model.Table, i, false), i, false))).ToList();
            foreach (Column c in model.Columns.Where(c => c.References != null))
            {
                if (indexes.Any(i => i.Columns[0] == c.Name)) continue;
                indexes.Add(new TableIndex(IndexName(model.Table, new[] { c.Name }, false), new[] { c.Name }, false));
            }
            return indexes;
        }

        /// <summary>A FOREIGN KEY constraint, as a line of CREATE TABLE: <c>CONSTRAINT fk_posts_author_id FOREIGN KEY (author_id) REFERENCES users (id) ON DELETE CASCADE</c>.</summary>
        public string ForeignKey(string table, string column, string references, string key, string onDelete) =>
            $"CONSTRAINT {Quote(ForeignKeyName(table, column))} FOREIGN KEY ({Quote(column)}) REFERENCES {Quote(references)} ({Quote(key)}) ON DELETE {(onDelete ?? "restrict").ToUpperInvariant()}";

        public static string ForeignKeyName(string table, string column)
        {
            string name = "fk_" + table + "_" + column;
            if (name.Length <= 60) return name;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).Substring(0, 8).ToLowerInvariant();
            return name.Substring(0, 51) + "_" + hash;
        }

        /// <summary>
        /// The pivot table of a many to many relation: a column for each side's key, which together are its key, and
        /// a foreign key to each table, so deleting a record unlinks it.
        /// </summary>
        public IEnumerable<string> CreatePivot(Relation r, bool ifMissing)
        {
            Column owner = r.PivotOwnerColumn;
            Column target = r.PivotTargetColumn;
            var columns = new List<string>
            {
                Quote(owner.Name) + " " + ColumnType(owner, indexed: true) + " NOT NULL",
                Quote(target.Name) + " " + ColumnType(target, indexed: true) + " NOT NULL",
                $"PRIMARY KEY ({Quote(owner.Name)}, {Quote(target.Name)})",
                ForeignKey(r.Pivot, owner.Name, r.Owner.Table, r.Owner.Key, "cascade"),
                ForeignKey(r.Pivot, target.Name, r.Target.Table, r.Target.Key, "cascade"),
            };
            var indexes = new List<TableIndex> { new TableIndex(IndexName(r.Pivot, new[] { target.Name }, false), new[] { target.Name }, false) };
            return CreateTable(r.Pivot, columns, indexes, ifMissing);
        }

        /// <summary>Statements a model's table needs first, such as a collation.</summary>
        protected virtual IEnumerable<string> Prepare(ModelInfo model) => Enumerable.Empty<string>();

        /// <summary>CREATE TABLE with the column definitions given, and its indexes.</summary>
        public virtual IEnumerable<string> CreateTable(string table, IReadOnlyList<string> columns, IReadOnlyList<TableIndex> indexes, bool ifMissing)
        {
            var sb = new StringBuilder("CREATE TABLE ");
            if (ifMissing) sb.Append("IF NOT EXISTS ");
            sb.Append(Quote(table)).Append(" (\n");
            sb.Append(string.Join(",\n", columns.Select(c => "  " + c))).Append("\n)");
            yield return sb.ToString();
            foreach (TableIndex index in indexes)
            {
                yield return $"CREATE {(index.Unique ? "UNIQUE " : string.Empty)}INDEX {(ifMissing ? "IF NOT EXISTS " : string.Empty)}{Quote(index.Name)} ON {Quote(table)} ({string.Join(", ", index.Columns.Select(Quote))})";
            }
        }

        protected virtual string ColumnDefinition(Column c, bool indexed)
        {
            var sb = new StringBuilder(Quote(c.Name)).Append(' ');
            if (c.AutoIncrement) return sb.Append(AutoKey).ToString();
            sb.Append(ColumnType(c, indexed));
            if (c.IsKey) sb.Append(" PRIMARY KEY");
            else if (!c.Nullable) sb.Append(" NOT NULL");
            if (c.Kind == ColumnKind.Email) sb.Append(CaseInsensitive);
            if (c.Kind == ColumnKind.Enum && c.Words != null)
            {
                sb.Append(" CHECK (").Append(Quote(c.Name)).Append(" IN (")
                    .Append(string.Join(", ", c.Words.Select(w => "'" + w.Replace("'", "''") + "'"))).Append("))");
            }
            return sb.ToString();
        }

        /// <summary>
        /// How a column compares text without minding case: emails do, so JOAN@example.com and joan@example.com are the
        /// same person for queries and unique indexes.
        /// </summary>
        protected abstract string CaseInsensitive { get; }

        /// <summary>An integer key the database numbers itself.</summary>
        protected abstract string AutoKey { get; }

        /// <summary>The names of the tables in the database.</summary>
        public abstract string TablesQuery { get; }

        /// <summary>
        /// The KScript error a database error is: a clash with a unique index is UniqueViolation (409 in a route), a broken
        /// reference ForeignKeyViolation, and anything else Query. Null for errors that aren't the database's.
        /// </summary>
        public abstract KScriptException Translate(Exception error, string table);

        /// <summary>True when the error means the database was busy or a deadlock stopped the work, so trying again may work.</summary>
        public abstract bool IsBusy(Exception error);

        /// <summary>The UniqueViolation error, naming the fields that clashed when they are known.</summary>
        protected static KScriptException UniqueViolation(Exception e, string table, List<object> fields)
        {
            var ex = new KScriptException(fields.Count > 0
                ? $"{(table == null ? "A row" : ModelInfo.NameOfTable(table) is string model ? "A " + model : "A row in " + table)} already has that {string.Join(" and ", fields)}."
                : "A row already has that value.", "UniqueViolation", e);
            ex.Fields["fields"] = fields;
            ex.Fields["table"] = table;
            return ex;
        }

        protected static KScriptException ForeignKeyViolation(Exception e, string table)
        {
            var ex = new KScriptException("That change would break a link between tables: a row it points to doesn't exist, or other rows still point to it.", "ForeignKeyViolation", e);
            ex.Fields["table"] = table;
            return ex;
        }

        protected static KScriptException QueryError(Exception e, string message) =>
            new KScriptException("The database refused the query: " + message, "Query", e);

        /// <summary>
        /// The fields of the index or key a database named in an error: a unique index KScript.Data made
        /// (ux_users_email), or the table's primary key.
        /// </summary>
        protected static List<object> FieldsOfIndex(string table, string index)
        {
            if (index == null) return new List<object>();
            ModelInfo model = ModelInfo.OfTable(table);
            if (index == "PRIMARY" || table != null && index == table + "_pkey") return new List<object> { model?.Key ?? "id" };
            if (model != null)
            {
                foreach (List<string> group in model.Unique)
                {
                    if (IndexName(model.Table, group, true) == index) return group.Cast<object>().ToList();
                }
            }
            string prefix = "ux_" + table + "_";
            return table != null && index.StartsWith(prefix, StringComparison.Ordinal) ? new List<object> { index.Substring(prefix.Length) } : new List<object>();
        }
    }

    /// <summary>SQLite, through Microsoft.Data.Sqlite: the default database, kept in one file.</summary>
    internal sealed class SqliteDialect : Dialect
    {
        public static readonly SqliteDialect Instance = new SqliteDialect();

        public override string Name => "sqlite";

        public override DbConnection Open(DatabaseSettings settings)
        {
            var connection = new SqliteConnection(settings.ConnectionString);
            connection.Open();
            // REGEXP is a function SQLite leaves to the program: $u.name->matches(/^[a-z]+$/i).
            connection.CreateFunction("regexp", (string pattern, string input) =>
                input != null && pattern != null && RegexValue.TryParse(pattern, out RegexValue regex, out _) && RegexValue.Run(regex, r => r.IsMatch(input)));
            using DbCommand pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            pragma.ExecuteNonQuery();
            return connection;
        }

        public override void ClearPools() => SqliteConnection.ClearAllPools();

        public override string ColumnType(Column column, bool indexed) => column.Kind switch
        {
            ColumnKind.Integer => "INTEGER",
            ColumnKind.Real => "REAL",
            ColumnKind.Bool => "INTEGER",
            _ => "TEXT",
        };

        public override object Bool(bool value) => value ? 1L : 0L;

        public override string BigInteger => "INTEGER";

        protected override string NoLimit => " LIMIT -1";

        /// <summary>SQLite locks the whole database for a transaction's writes, so there is nothing to add.</summary>
        public override string Lock(string mode, bool skipLocked) => string.Empty;

        public override void Matches(SqlWriter w, SqlExpr target, RegexValue regex)
        {
            w.Expr(target, nested: true).Append(" REGEXP ").Value(regex.Text);
        }

        public override SqlExpr RowOrder => new SqlColumn("rowid");

        protected override string AutoKey => "INTEGER PRIMARY KEY AUTOINCREMENT";

        protected override string CaseInsensitive => " COLLATE NOCASE";

        public override string TablesQuery => "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        public override bool IsBusy(Exception error) => error is SqliteException s && (s.SqliteErrorCode == 5 || s.SqliteErrorCode == 6);

        public override KScriptException Translate(Exception error, string table)
        {
            if (error is not SqliteException e) return null;
            // https://www.sqlite.org/rescode.html: 2067 SQLITE_CONSTRAINT_UNIQUE, 1555 _PRIMARYKEY, 787 _FOREIGNKEY.
            switch (e.SqliteExtendedErrorCode)
            {
                case 2067:
                case 1555:
                {
                    // UNIQUE constraint failed: users.email, users.team
                    string message = e.Message;
                    int colon = message.LastIndexOf("failed:", StringComparison.Ordinal);
                    List<object> fields = colon < 0 ? new List<object>()
                        : message.Substring(colon + 7).Trim().TrimEnd('.', '\'').Split(',').Select(f => (object)f.Trim().Split('.').Last()).ToList();
                    return UniqueViolation(e, table, fields);
                }
                case 787:
                    return ForeignKeyViolation(e, table);
                default:
                    return QueryError(e, e.Message.Replace("SQLite Error " + e.SqliteErrorCode + ": ", string.Empty));
            }
        }
    }
}
