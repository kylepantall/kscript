using System.Data.Common;
using System.Text;

using KScript.Runtime;

using MySqlConnector;

namespace KScript.Data
{
    /// <summary>
    /// MySQL and MariaDB, through MySqlConnector. Tables compare text exactly (utf8mb4_bin), as SQLite and Postgres do,
    /// and emails without minding case (utf8mb4_unicode_ci).
    /// </summary>
    internal sealed class MySqlDialect : Dialect
    {
        public static readonly MySqlDialect MySql = new MySqlDialect("mysql");

        public static readonly MySqlDialect MariaDb = new MySqlDialect("mariadb");

        private MySqlDialect(string name) => Name = name;

        public override string Name { get; }

        public override string Quote(string name) => "`" + name.Replace("`", "``") + "`";

        public override DbConnection Open(DatabaseSettings settings)
        {
            var connection = new MySqlConnection(settings.ConnectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public override void ClearPools() => MySqlConnection.ClearAllPools();

        public override string ColumnType(Column column, bool indexed) => column.Kind switch
        {
            ColumnKind.Integer => "BIGINT",
            ColumnKind.Real => "DOUBLE",
            ColumnKind.Bool => "BOOLEAN",
            ColumnKind.Date => "DATETIME(6)",
            ColumnKind.Json => "LONGTEXT",
            ColumnKind.Uuid => "VARCHAR(36)",
            ColumnKind.Email => $"VARCHAR({column.Length ?? 254}) COLLATE utf8mb4_unicode_ci",
            ColumnKind.Handle or ColumnKind.Enum or ColumnKind.Password => "VARCHAR(255)",
            // Text in a key or an index needs a length; text that may be long doesn't get one.
            _ when column.Length is int n && n <= 16000 => $"VARCHAR({n})",
            _ when indexed => "VARCHAR(255)",
            _ => "LONGTEXT",
        };

        public override object Bool(bool value) => value;

        public override object Date(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

        public override string KeyText => "VARCHAR(191)";

        public override string LongText => "LONGTEXT";

        protected override string NoLimit => " LIMIT 18446744073709551615";

        /// <summary>LOCK IN SHARE MODE works in MySQL and MariaDB alike; FOR SHARE is MySQL's only.</summary>
        public override string Lock(string mode, bool skipLocked) =>
            (mode == "share" ? " LOCK IN SHARE MODE" : " FOR UPDATE") + (skipLocked ? " SKIP LOCKED" : string.Empty);

        /// <summary>LENGTH counts bytes in MySQL; CHAR_LENGTH counts characters, as LENGTH does elsewhere.</summary>
        public override string Function(string name) => name == "LENGTH" ? "CHAR_LENGTH" : name;

        /// <summary>
        /// Both sides lower-cased, so case doesn't matter whatever the column's collation. \ is a string escape in MySQL,
        /// so the escape character is written twice.
        /// </summary>
        public override void Like(SqlWriter w, SqlExpr left, SqlExpr right)
        {
            w.Append("LOWER(").Expr(left).Append(") LIKE LOWER(").Expr(right).Append(") ESCAPE '\\\\'");
        }

        /// <summary>|| is OR in MySQL: text is joined with CONCAT.</summary>
        public override void Concat(SqlWriter w, SqlExpr left, SqlExpr right)
        {
            w.Append("CONCAT(").Expr(left).Append(", ").Expr(right).Append(")");
        }

        public override void Matches(SqlWriter w, SqlExpr target, RegexValue regex)
        {
            w.Expr(target, nested: true).Append(" REGEXP ").Value(InlineFlags(regex));
        }

        public override string DefaultValues => " () VALUES ()";

        public override bool Returning => false;

        public override string Upsert(string insert, string key, IEnumerable<string> update) =>
            insert + " ON DUPLICATE KEY UPDATE " + string.Join(", ", update.Select(c => $"{Quote(c)} = VALUES({Quote(c)})"));

        public override string InsertOrIgnore(string insert, string key) => "INSERT IGNORE" + insert.Substring("INSERT".Length);

        public override bool ConditionalUpsert => false;

        /// <summary>MySQL has no CREATE INDEX IF NOT EXISTS, so the indexes are part of CREATE TABLE.</summary>
        public override IEnumerable<string> CreateTable(string table, IReadOnlyList<string> columns, IReadOnlyList<TableIndex> indexes, bool ifMissing)
        {
            var sb = new StringBuilder("CREATE TABLE ");
            if (ifMissing) sb.Append("IF NOT EXISTS ");
            sb.Append(Quote(table)).Append(" (\n");
            IEnumerable<string> lines = columns.Concat(indexes.Select(i =>
                $"{(i.Unique ? "UNIQUE " : string.Empty)}KEY {Quote(i.Name)} ({string.Join(", ", i.Columns.Select(Quote))})"));
            sb.Append(string.Join(",\n", lines.Select(l => "  " + l))).Append("\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin");
            yield return sb.ToString();
        }

        protected override string AutoKey => "BIGINT AUTO_INCREMENT PRIMARY KEY";

        // The collation is part of the email column's type above.
        protected override string CaseInsensitive => string.Empty;

        public override string TablesQuery =>
            "SELECT table_name AS name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' ORDER BY table_name";

        // 1213: deadlock; 1205: a lock wait timed out.
        public override bool IsBusy(Exception error) => error is MySqlException m && m.Number is 1213 or 1205;

        public override KScriptException Translate(Exception error, string table)
        {
            if (error is not MySqlException e) return null;
            switch (e.Number)
            {
                case 1062:
                {
                    // Duplicate entry 'ada@example.com' for key 'users.ux_users_email' (MariaDB leaves out "users.").
                    string message = e.Message;
                    int at = message.LastIndexOf("for key '", StringComparison.Ordinal);
                    string index = at < 0 ? null : message.Substring(at + 9).TrimEnd('\'');
                    if (index != null && index.Contains('.')) index = index.Substring(index.LastIndexOf('.') + 1);
                    return UniqueViolation(e, table, FieldsOfIndex(table, index));
                }
                case 1451:
                case 1452:
                    return ForeignKeyViolation(e, table);
                default:
                    return QueryError(e, e.Message);
            }
        }
    }
}
