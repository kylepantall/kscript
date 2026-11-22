using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

using KScript.Metadata;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// Where one named connection goes, from <c>database.connections.&lt;name&gt;</c> in the project's config. With no
    /// config, <c>main</c> is SQLite in data/app.sqlite in the project folder.
    /// </summary>
    internal sealed class DatabaseSettings
    {
        public string Name { get; init; }

        public Dialect Dialect { get; init; }

        public string ConnectionString { get; init; }

        /// <summary>Where the database is, for messages: a file path, or "memory".</summary>
        public string Where { get; init; }

        /// <summary>The file of a SQLite database, or null.</summary>
        public string File { get; init; }

        /// <summary>True for a SQLite database in memory, which lasts while one connection to it is open.</summary>
        public bool Memory { get; init; }

        public const string DefaultName = "main";

        public const string DefaultFile = "data/app.sqlite";

        /// <summary>The folder relative paths start from: the one with kscript.json, or the script's.</summary>
        public static string ProjectFolder(string scriptFolder)
        {
            string config = ProjectConfig.Find(scriptFolder);
            return config != null ? Path.GetDirectoryName(config) : scriptFolder;
        }

        /// <summary>The settings of a connection, read from the config of the script in <paramref name="folder"/>.</summary>
        public static DatabaseSettings Resolve(KScriptHost host, string folder, string name)
        {
            string root = ProjectFolder(folder);
            Dictionary<string, object> config = ConfigValue.For(host, folder).Values.GetValueOrDefault("database") as Dictionary<string, object>;
            string wanted = name ?? (config?.GetValueOrDefault("default") is object d && d != null ? Values.ToStr(d) : DefaultName);
            var connections = config?.GetValueOrDefault("connections") as Dictionary<string, object>;
            Dictionary<string, object> options = connections?.GetValueOrDefault(wanted) as Dictionary<string, object>;
            if (options == null)
            {
                if (connections != null && connections.Count > 0 || wanted != DefaultName)
                {
                    IEnumerable<string> known = connections?.Keys ?? Enumerable.Empty<string>();
                    string close = Values.Closest(wanted, known);
                    throw new KScriptException($"There is no database connection called '{wanted}'." + (close != null ? $" Did you mean {close}?" : string.Empty)
                        + $" Add it to your config under database.connections.{wanted}, with a url such as sqlite:data/{wanted}.sqlite.", "Config");
                }
                string env = Environment.GetEnvironmentVariable("DATABASE_URL");
                options = new Dictionary<string, object> { ["url"] = string.IsNullOrWhiteSpace(env) ? "sqlite:" + DefaultFile : env };
            }
            return FromOptions(wanted, options, root);
        }

        internal static DatabaseSettings FromOptions(string name, Dictionary<string, object> options, string root)
        {
            string url = Text(options, "url");
            string driver = Text(options, "driver")?.ToLowerInvariant();
            string file = Text(options, "file") ?? Text(options, "database");
            if (url != null)
            {
                int colon = url.IndexOf(':');
                if (colon <= 0) throw new KScriptException($"The url of the '{name}' database connection should start with the database, such as sqlite:data/app.sqlite or postgres://user:password@localhost/app, not '{url}'.", "Config");
                driver = url.Substring(0, colon).ToLowerInvariant();
                file = url.Substring(colon + 1);
                if (file.StartsWith("//", StringComparison.Ordinal)) file = file.Substring(2);
            }
            driver ??= "sqlite";
            switch (driver)
            {
                case "sqlite":
                case "sqlite3":
                    return Sqlite(name, string.IsNullOrWhiteSpace(file) ? DefaultFile : file.Trim(), root);
                case "postgres":
                case "postgresql":
                case "pgsql":
                    return Server(name, PostgresDialect.Instance, url != null ? ServerAddress.Parse(name, file, 5432) : ServerAddress.From(name, options, 5432));
                case "mysql":
                    return Server(name, MySqlDialect.MySql, url != null ? ServerAddress.Parse(name, file, 3306) : ServerAddress.From(name, options, 3306));
                case "mariadb":
                    return Server(name, MySqlDialect.MariaDb, url != null ? ServerAddress.Parse(name, file, 3306) : ServerAddress.From(name, options, 3306));
                default:
                    string close = Values.Closest(driver, new[] { "sqlite", "postgres", "mysql", "mariadb" });
                    throw new KScriptException($"'{driver}' isn't a database KScript.Data knows." + (close != null ? $" Did you mean {close}?" : string.Empty)
                        + " Use sqlite, postgres, mysql or mariadb, with a url such as sqlite:data/app.sqlite or postgres://user:password@localhost/app.", "Config");
            }
        }

        /// <summary>A database on a server: Postgres, MySQL or MariaDB.</summary>
        private static DatabaseSettings Server(string name, Dialect dialect, ServerAddress address)
        {
            var parts = new List<(string, string)>();
            bool mysql = dialect is MySqlDialect;
            parts.Add((mysql ? "Server" : "Host", address.Host));
            parts.Add(("Port", address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (address.Database != null) parts.Add(("Database", address.Database));
            if (address.User != null) parts.Add((mysql ? "User ID" : "Username", address.User));
            if (address.Password != null) parts.Add(("Password", address.Password));
            foreach (KeyValuePair<string, string> option in address.Options)
            {
                parts.RemoveAll(p => string.Equals(p.Item1, option.Key, StringComparison.OrdinalIgnoreCase));
                parts.Add((option.Key, option.Value));
            }
            return new DatabaseSettings
            {
                Name = name,
                Dialect = dialect,
                ConnectionString = string.Join(";", parts.Select(p => p.Item1 + "=" + ConnectionValue(p.Item2))),
                Where = (address.User != null ? address.User + "@" : string.Empty) + address.Host + ":" + address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + (address.Database != null ? "/" + address.Database : string.Empty),
            };
        }

        /// <summary>A value in a connection string: quoted when it has a ; or a quote in it, or spaces at its ends.</summary>
        internal static string ConnectionValue(string value) =>
            value.IndexOfAny(new[] { ';', '"', '\'', '=' }) >= 0 || value.Trim() != value ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

        private static DatabaseSettings Sqlite(string name, string file, string root)
        {
            if (file == ":memory:")
            {
                // A shared in-memory database, kept while kscript runs: every connection to it sees the same tables.
                string memory = "kscript-" + name + "-" + Math.Abs(StringComparer.Ordinal.GetHashCode(root)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new DatabaseSettings
                {
                    Name = name,
                    Dialect = SqliteDialect.Instance,
                    ConnectionString = "Data Source=" + memory + ";Mode=Memory;Cache=Shared",
                    Where = "memory",
                    Memory = true,
                };
            }
            string path = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(root, file));
            return new DatabaseSettings
            {
                Name = name,
                Dialect = SqliteDialect.Instance,
                ConnectionString = "Data Source=\"" + path.Replace("\"", "\"\"") + "\";Mode=ReadWriteCreate",
                Where = path,
                File = path,
            };
        }

        // Connection strings are written by hand: SqliteConnectionStringBuilder brings in reflection that Native AOT warns about.

        private static string Text(Dictionary<string, object> options, string key) =>
            options.GetValueOrDefault(key) is object v && v != null && Values.ToStr(v).Trim().Length > 0 ? Values.ToStr(v).Trim() : null;
    }

    /// <summary>
    /// A database connection: <c>$db</c>, or <c>@db(connection: analytics)</c>. It opens connections as queries need
    /// them, and runs a script's statements inside its transaction while one is open.
    /// </summary>
    /// <summary>
    /// <c>$db</c>: the default connection of the script's project, worked out when it is first used, so a script that
    /// imports KScript.Data with no database set up only fails if it uses one.
    /// </summary>
    internal sealed class DefaultDatabase : IKObject
    {
        private readonly KScriptHost host;
        private readonly string folder;

        public DefaultDatabase(KScriptHost host, string folder)
        {
            this.host = host;
            this.folder = folder;
        }

        public Database Resolve() => Database.For(host, folder, null);

        public string TypeName => KType.Database;

        public string ToText() => Resolve().ToText();

        public override string ToString() => ToText();

        public override bool Equals(object obj) => Resolve().Equals(obj is DefaultDatabase d ? d.Resolve() : obj);

        public override int GetHashCode() => Resolve().GetHashCode();

        public bool TryGetMember(string name, out object value) => Resolve().TryGetMember(name, out value);

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }
    }

    internal sealed class Database : IKObject
    {
        private static readonly ConcurrentDictionary<string, Database> Open = new ConcurrentDictionary<string, Database>(StringComparer.Ordinal);

        /// <summary>For in-memory databases: one connection kept open, so the data lasts while kscript runs.</summary>
        private DbConnection keeper;

        /// <summary>The transaction each running script has open on this database, if any.</summary>
        private readonly ConditionalWeakTable<Interpreter, TransactionState> transactions = new ConditionalWeakTable<Interpreter, TransactionState>();

        private Database(DatabaseSettings settings) => Settings = settings;

        public DatabaseSettings Settings { get; }

        public Dialect Dialect => Settings.Dialect;

        public string Name => Settings.Name;

        /// <summary>The database a connection name means for a script in <paramref name="folder"/>: the same object for the same settings.</summary>
        public static Database For(KScriptHost host, string folder, string name)
        {
            if (host != null && (name == null || name == DatabaseSettings.DefaultName))
            {
                if (host.MemoryDatabase) return ForSandbox(host);
                if (host.TestFile != null && ForTest(host, folder) is Database test) return test;
            }
            DatabaseSettings settings = DatabaseSettings.Resolve(host, folder, name);
            return Open.GetOrAdd(settings.Dialect.Name + "|" + settings.ConnectionString, _ => new Database(settings));
        }

        /// <summary>The database of the script making a call (its module's folder), with an optional connection name.</summary>
        public static Database For(CallContext call, string name = null) =>
            For(call.Host, call.Module?.Directory ?? call.Host.WorkingDirectory, name);

        /// <summary>The database a model's records live in.</summary>
        public static Database For(CallContext call, ModelInfo model) => For(call, model.Connection);

        /// <summary>Every statement run in this process, newest last, when logging is on (tests turn it on).</summary>
        internal static List<SqlStatement> Log { get; set; }

        public string TypeName => KType.Database;

        public string ToText() => $"{Dialect.Name} database '{Name}' ({Settings.Where})";

        public override string ToString() => ToText();

        public override bool Equals(object obj) => obj is DefaultDatabase d ? ReferenceEquals(d.Resolve(), this) : ReferenceEquals(obj, this);

        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "name" => Name,
                "driver" => Dialect.Name,
                "file" => Settings.File,
                _ => null,
            };
            return value != null || name == "file";
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        #region Running statements

        /// <summary>A new connection, for work that manages its own transactions, such as migrations.</summary>
        internal DbConnection OpenConnection() => Connect();

        private DbConnection Connect()
        {
            try
            {
                if (Settings.File != null) Directory.CreateDirectory(Path.GetDirectoryName(Settings.File));
                DbConnection connection = Dialect.Open(Settings);
                if (Settings.Memory && keeper == null)
                {
                    lock (Open)
                    {
                        keeper ??= Dialect.Open(Settings);
                    }
                }
                return connection;
            }
            catch (Exception ex) when (ex is DbException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is TimeoutException)
            {
                string message = ex is Npgsql.PostgresException pg ? pg.MessageText : ex.Message;
                throw new KScriptException($"Can't open the {Dialect.Name} database '{Name}' ({Settings.Where}): {message.TrimEnd('.')}.", "ConnectionError", ex);
            }
        }

        /// <summary>
        /// Runs work with a connection: the script's open transaction's, or a new one that is closed afterwards.
        /// Database errors become KScript errors (UniqueViolation, ForeignKeyViolation, QueryError).
        /// </summary>
        public T With<T>(Interpreter interpreter, string table, Func<DbConnection, DbTransaction, T> work)
        {
            TransactionState open = Current(interpreter) ?? BeginTest(interpreter);
            DbConnection connection = open?.Connection ?? Connect();
            // Postgres ends a transaction at its first error, so in a test's transaction each statement is a
            // savepoint: a test can expect a UniqueViolation and go on.
            bool guard = open?.Test == true && Dialect is PostgresDialect;
            try
            {
                if (guard) Raw(connection, open.Transaction, "SAVEPOINT kscript_test_statement");
                T result = work(connection, open?.Transaction);
                if (guard) Raw(connection, open.Transaction, "RELEASE SAVEPOINT kscript_test_statement");
                return result;
            }
            catch (DbException ex)
            {
                if (guard) Raw(connection, open.Transaction, "ROLLBACK TO SAVEPOINT kscript_test_statement");
                throw Dialect.Translate(ex, table) ?? new KScriptException(ex.Message, "QueryError", ex);
            }
            finally
            {
                if (open == null) connection.Dispose();
            }
        }

        private DbCommand Command(DbConnection connection, DbTransaction transaction, SqlStatement statement, Interpreter interpreter)
        {
            QueryLog.Note(interpreter, this, statement);
            DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = statement.Text;
            for (int i = 0; i < statement.Parameters.Count; i++)
            {
                DbParameter p = command.CreateParameter();
                p.ParameterName = "@p" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                p.Value = statement.Parameters[i] ?? DBNull.Value;
                command.Parameters.Add(p);
            }
            Log?.Add(statement);
            return command;
        }

        private static void Raw(DbConnection connection, DbTransaction transaction, string sql)
        {
            using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        /// <summary>Runs a statement that returns rows, as maps of column name to database value.</summary>
        public List<Dictionary<string, object>> Rows(Interpreter interpreter, SqlStatement statement, string table = null) =>
            With(interpreter, table, (connection, transaction) =>
            {
                using DbCommand command = Command(connection, transaction, statement, interpreter);
                using DbDataReader reader = command.ExecuteReader();
                return ReadAll(reader);
            });

        internal static List<Dictionary<string, object>> ReadAll(DbDataReader reader)
        {
            var rows = new List<Dictionary<string, object>>();
            while (reader.Read())
            {
                var row = new Dictionary<string, object>(StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Runs a statement and returns how many rows it changed.</summary>
        public int Execute(Interpreter interpreter, SqlStatement statement, string table = null) =>
            With(interpreter, table, (connection, transaction) =>
            {
                using DbCommand command = Command(connection, transaction, statement, interpreter);
                return command.ExecuteNonQuery();
            });

        /// <summary>
        /// Runs any statement: one that returns rows (SELECT, or anything with RETURNING) gives them, and one that doesn't
        /// gives how many rows it changed. For <c>@sql</c>.
        /// </summary>
        public object Run(Interpreter interpreter, SqlStatement statement) =>
            With(interpreter, null, (connection, transaction) =>
            {
                using DbCommand command = Command(connection, transaction, statement, interpreter);
                using DbDataReader reader = command.ExecuteReader();
                if (reader.FieldCount == 0) return (object)(double)reader.RecordsAffected;
                return ReadAll(reader);
            });

        /// <summary>The tables in the database.</summary>
        public List<string> Tables(Interpreter interpreter) =>
            Rows(interpreter, new SqlStatement(Dialect.TablesQuery, Array.Empty<object>())).Select(r => Values.ToStr(r.Values.First())).ToList();

        #endregion

        #region Transactions

        private sealed class TransactionState
        {
            public DbConnection Connection;
            public DbTransaction Transaction;
            public int Depth;

            /// <summary>True once a commit has been tried: the transaction is over, whether it worked or not.</summary>
            public bool Finished;

            /// <summary>True for the transaction a test runs in, which is undone when the test ends.</summary>
            public bool Test;
        }

        private TransactionState Current(Interpreter interpreter) =>
            interpreter != null && transactions.TryGetValue(interpreter, out TransactionState s) && s.Connection != null ? s : null;

        /// <summary>True while the script has a transaction open on this database.</summary>
        public bool InTransaction(Interpreter interpreter) => Current(interpreter) != null;

        /// <summary>
        /// Runs work in a transaction: committed when it finishes (return, break and continue finish it too), rolled
        /// back when it fails. Inside another transaction it is a savepoint, so only its own changes roll back.
        /// A deadlock or busy database is tried again, up to <paramref name="retries"/> more times.
        /// </summary>
        public T InTransaction<T>(Interpreter interpreter, Func<T> work, int retries = 0, IsolationLevel isolation = IsolationLevel.Unspecified)
        {
            // Only a whole transaction is tried again, not a savepoint in one. (Worked out first: an exception filter
            // runs before the finally blocks below it, while the failed transaction is still open.)
            bool outermost = Current(interpreter) == null;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return Once(interpreter, work, isolation);
                }
                catch (KScriptException ex) when (outermost && attempt < retries && IsBusy(ex))
                {
                    Thread.Sleep(20 * (attempt + 1));
                }
            }
        }

        private bool IsBusy(KScriptException ex) => ex.InnerException != null && Dialect.IsBusy(ex.InnerException);

        private T Once<T>(Interpreter interpreter, Func<T> work, IsolationLevel isolation)
        {
            TransactionState open = Current(interpreter) ?? BeginTest(interpreter);
            if (open != null)
            {
                string savepoint = "kscript_" + (++open.Depth).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Execute(interpreter, new SqlStatement("SAVEPOINT " + savepoint, Array.Empty<object>()));
                try
                {
                    T result = work();
                    Execute(interpreter, new SqlStatement("RELEASE SAVEPOINT " + savepoint, Array.Empty<object>()));
                    return result;
                }
                catch (Exception ex) when (Commits(ex))
                {
                    Execute(interpreter, new SqlStatement("RELEASE SAVEPOINT " + savepoint, Array.Empty<object>()));
                    throw;
                }
                catch
                {
                    Execute(interpreter, new SqlStatement("ROLLBACK TO SAVEPOINT " + savepoint, Array.Empty<object>()));
                    Execute(interpreter, new SqlStatement("RELEASE SAVEPOINT " + savepoint, Array.Empty<object>()));
                    throw;
                }
                finally
                {
                    open.Depth--;
                }
            }

            DbConnection connection = Connect();
            var state = new TransactionState { Connection = connection };
            try
            {
                state.Transaction = connection.BeginTransaction(isolation);
                transactions.AddOrUpdate(interpreter, state);
                try
                {
                    T result = work();
                    Commit(state);
                    return result;
                }
                catch (Exception ex) when (Commits(ex))
                {
                    Commit(state);
                    throw;
                }
                catch
                {
                    // A commit that failed has ended the transaction already (Postgres rolls it back itself).
                    if (!state.Finished) state.Transaction.Rollback();
                    throw;
                }
            }
            finally
            {
                transactions.Remove(interpreter);
                state.Transaction?.Dispose();
                connection.Dispose();
            }
        }

        private void Commit(TransactionState state)
        {
            state.Finished = true;
            try
            {
                state.Transaction.Commit();
            }
            catch (DbException ex)
            {
                throw Dialect.Translate(ex, null);
            }
        }

        /// <summary>
        /// While a test runs, the transaction it runs in: begun at its first statement and rolled back when it ends,
        /// so no test sees another's rows. Null outside a test.
        /// </summary>
        private TransactionState BeginTest(Interpreter interpreter)
        {
            KScriptHost host = interpreter?.Host;
            if (host == null || !host.TestRunning) return null;
            DbConnection connection = Connect();
            var state = new TransactionState { Connection = connection, Test = true };
            state.Transaction = connection.BeginTransaction();
            transactions.AddOrUpdate(interpreter, state);
            host.AfterTest.Add(() =>
            {
                transactions.Remove(interpreter);
                try
                {
                    state.Transaction.Rollback();
                }
                finally
                {
                    state.Transaction.Dispose();
                    connection.Dispose();
                }
            });
            return state;
        }

        #region Test databases

        private const string TestKey = "KScript.Data.TestDatabase";

        /// <summary>
        /// The main database of a test file under kscript test: with test.database memory (the default), a new SQLite
        /// database in memory, made from the project's migrations (or from its models when it has none), that ends
        /// with the file. Null for test.database config.
        /// </summary>
        private static Database ForTest(KScriptHost host, string folder)
        {
            lock (host.Items)
            {
                if (host.Items.TryGetValue(TestKey, out object made)) return made as Database;
                ProjectConfig project = ProjectConfig.FindAndLoad(folder);
                if (project?.Test.Database == "config")
                {
                    host.Items[TestKey] = "config";
                    return null;
                }
                Database db = InMemory(host, "memory, for " + Path.GetFileName(host.TestFile));
                host.Items[TestKey] = db;
                try
                {
                    new Migrations.Migrator(project?.Directory ?? folder, host, project).Build();
                }
                catch
                {
                    host.Items.Remove(TestKey);
                    throw;
                }
                return db;
            }
        }

        /// <summary>A sandbox's main database (see <see cref="KScriptHost.MemoryDatabase"/>): empty, in memory, one per host.</summary>
        private static Database ForSandbox(KScriptHost host)
        {
            lock (host.Items)
            {
                if (host.Items.TryGetValue(TestKey, out object made) && made is Database db) return db;
                db = InMemory(host, "memory");
                host.Items[TestKey] = db;
                return db;
            }
        }

        /// <summary>A new SQLite database in memory, closed (and so gone) when the host's file or run ends.</summary>
        private static Database InMemory(KScriptHost host, string where)
        {
            var db = new Database(new DatabaseSettings
            {
                Name = DatabaseSettings.DefaultName,
                Dialect = SqliteDialect.Instance,
                ConnectionString = "Data Source=kscript-test-" + Guid.NewGuid().ToString("N") + ";Mode=Memory;Cache=Shared",
                Where = where,
                Memory = true,
            });
            host.AfterFile.Add(() =>
            {
                db.keeper?.Dispose();
                db.keeper = null;
            });
            return db;
        }

        #endregion

        /// <summary>Leaving the block with return, break or continue finishes the work, so it is kept.</summary>
        private static bool Commits(Exception ex) => ex is ReturnSignal || ex is BreakSignal || ex is ContinueSignal;

        #endregion

        /// <summary>Closes every connection (tests use it to delete database files).</summary>
        internal static void CloseAll()
        {
            foreach (Database db in Open.Values)
            {
                db.keeper?.Dispose();
                db.keeper = null;
            }
            Open.Clear();
            SqliteDialect.Instance.ClearPools();
            PostgresDialect.Instance.ClearPools();
            MySqlDialect.MySql.ClearPools();
        }

        /// <summary>
        /// Inserts a row and gives the value of its <paramref name="key"/> column: from RETURNING where the database has
        /// it, or else the number the database gave it (or the value the row was given).
        /// </summary>
        public object Insert(Interpreter interpreter, string table, List<(string Name, SqlExpr Value)> values, Column key)
        {
            if (Dialect.Returning) return Rows(interpreter, Query.InsertStatement(Dialect, table, values, key.Name), table)[0].Values.First();
            return With(interpreter, table, (connection, transaction) =>
            {
                using (DbCommand insert = Command(connection, transaction, Query.InsertStatement(Dialect, table, values, null), interpreter))
                {
                    insert.ExecuteNonQuery();
                }
                if (!key.AutoIncrement) return KeyValue(values.First(v => v.Name == key.Name).Value);
                using DbCommand last = Command(connection, transaction, new SqlStatement("SELECT LAST_INSERT_ID()", Array.Empty<object>()), null);
                return last.ExecuteScalar();
            });
        }

        private object KeyValue(SqlExpr value) => value is SqlValue v ? KScript.Data.Rows.ForColumn(v.Value, v.Column, Dialect) : null;
    }
}
