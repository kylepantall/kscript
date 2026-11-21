using System.Data.Common;
using System.Globalization;
using System.Text;

using KScript.Runtime;

using Microsoft.Data.Sqlite;

namespace KScript.Data
{
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

        /// <summary>The SQL type of a column.</summary>
        public abstract string ColumnType(Column column);

        /// <summary>A bool as the database stores it.</summary>
        public abstract object Bool(bool value);

        /// <summary>LIMIT and OFFSET, after ORDER BY.</summary>
        public virtual string Limit(int? limit, int? offset)
        {
            var sb = new StringBuilder();
            if (limit != null) sb.Append(" LIMIT ").Append(limit.Value.ToString(CultureInfo.InvariantCulture));
            if (offset != null)
            {
                if (limit == null) sb.Append(" LIMIT -1");
                sb.Append(" OFFSET ").Append(offset.Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>FOR UPDATE and similar, at the end of a SELECT. Empty where the database locks in another way.</summary>
        public virtual string Lock(string mode, bool skipLocked) => string.Empty;

        /// <summary>The CREATE TABLE and CREATE INDEX statements for a model.</summary>
        public virtual IEnumerable<string> CreateTable(ModelInfo model, bool ifMissing)
        {
            var sb = new StringBuilder("CREATE TABLE ");
            if (ifMissing) sb.Append("IF NOT EXISTS ");
            sb.Append(Quote(model.Table)).Append(" (\n");
            var lines = new List<string>();
            foreach (Column c in model.Columns)
            {
                lines.Add("  " + ColumnDefinition(c));
            }
            sb.Append(string.Join(",\n", lines)).Append("\n)");
            yield return sb.ToString();

            foreach ((List<string> columns, bool unique) in model.Unique.Select(u => (u, true)).Concat(model.Index.Select(i => (i, false))))
            {
                string index = (unique ? "ux_" : "ix_") + model.Table + "_" + string.Join("_", columns);
                yield return $"CREATE {(unique ? "UNIQUE " : string.Empty)}INDEX {(ifMissing ? "IF NOT EXISTS " : string.Empty)}{Quote(index)} ON {Quote(model.Table)} ({string.Join(", ", columns.Select(Quote))})";
            }
        }

        protected virtual string ColumnDefinition(Column c)
        {
            var sb = new StringBuilder(Quote(c.Name)).Append(' ');
            if (c.AutoIncrement) return sb.Append(AutoKey).ToString();
            sb.Append(ColumnType(c));
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
        /// reference ForeignKeyViolation, and anything else QueryError. Null for errors that aren't the database's.
        /// </summary>
        public abstract KScriptException Translate(Exception error, string table);
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

        public override string ColumnType(Column column) => column.Kind switch
        {
            ColumnKind.Integer => "INTEGER",
            ColumnKind.Real => "REAL",
            ColumnKind.Bool => "INTEGER",
            ColumnKind.Json => "TEXT",
            _ => "TEXT",
        };

        public override object Bool(bool value) => value ? 1L : 0L;

        protected override string AutoKey => "INTEGER PRIMARY KEY AUTOINCREMENT";

        protected override string CaseInsensitive => " COLLATE NOCASE";

        public override string TablesQuery => "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

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
                    var ex = new KScriptException(fields.Count > 0
                        ? $"{(table == null ? "A row" : ModelInfo.NameOfTable(table) is string model ? "A " + model : "A row in " + table)} already has that {string.Join(" and ", fields)}."
                        : "A row already has that value.", "UniqueViolation", e);
                    ex.Fields["fields"] = fields;
                    ex.Fields["table"] = table;
                    return ex;
                }
                case 787:
                {
                    var ex = new KScriptException("That change would break a link between tables: a row it points to doesn't exist, or other rows still point to it.", "ForeignKeyViolation", e);
                    ex.Fields["table"] = table;
                    return ex;
                }
                default:
                    return new KScriptException("The database refused the query: " + e.Message.Replace("SQLite Error " + e.SqliteErrorCode + ": ", string.Empty), "QueryError", e);
            }
        }
    }
}
