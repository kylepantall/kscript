using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

using KScript.Metadata;
using KScript.Projects;
using KScript.Runtime;

using Microsoft.Data.Sqlite;

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
                if (colon <= 0) throw new KScriptException($"The url of the '{name}' database connection should start with the database, such as sqlite:data/app.sqlite, not '{url}'.", "Config");
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
                case "mysql":
                case "mariadb":
                    throw new KScriptException($"The '{name}' database connection uses {driver}, which this version of KScript.Data can't connect to yet: it supports SQLite. Use a url such as sqlite:data/app.sqlite for now.", "Config");
                default:
                    throw new KScriptException($"'{driver}' isn't a database KScript.Data knows. Use sqlite, with a url such as sqlite:data/app.sqlite.", "Config");
            }
        }

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

        private DbConnection Connect()
        {
            try
            {
                if (Settings.File != null) Directory.CreateDirectory(Path.GetDirectoryName(Settings.File));
                DbConnection connection = Dialect.Open(Settings);
                if (Settings.File == null && keeper == null)
                {
                    lock (Open)
                    {
                        keeper ??= Dialect.Open(Settings);
                    }
                }
                return connection;
            }
            catch (Exception ex) when (ex is DbException || ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new KScriptException($"Can't open the {Dialect.Name} database '{Name}' ({Settings.Where}): {ex.Message}", "ConnectionError", ex);
            }
        }

        /// <summary>
        /// Runs work with a connection: the script's open transaction's, or a new one that is closed afterwards.
        /// Database errors become KScript errors (UniqueViolation, ForeignKeyViolation, QueryError).
        /// </summary>
        public T With<T>(Interpreter interpreter, string table, Func<DbConnection, DbTransaction, T> work)
        {
            TransactionState open = Current(interpreter);
            DbConnection connection = open?.Connection ?? Connect();
            try
            {
                return work(connection, open?.Transaction);
            }
            catch (DbException ex)
            {
                throw Dialect.Translate(ex, table) ?? new KScriptException(ex.Message, "QueryError", ex);
            }
            finally
            {
                if (open == null) connection.Dispose();
            }
        }

        private static DbCommand Command(DbConnection connection, DbTransaction transaction, SqlStatement statement)
        {
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

        /// <summary>Runs a statement that returns rows, as maps of column name to database value.</summary>
        public List<Dictionary<string, object>> Rows(Interpreter interpreter, SqlStatement statement, string table = null) =>
            With(interpreter, table, (connection, transaction) =>
            {
                using DbCommand command = Command(connection, transaction, statement);
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
                using DbCommand command = Command(connection, transaction, statement);
                return command.ExecuteNonQuery();
            });

        /// <summary>
        /// Runs any statement: one that returns rows (SELECT, or anything with RETURNING) gives them, and one that doesn't
        /// gives how many rows it changed. For <c>@sql</c>.
        /// </summary>
        public object Run(Interpreter interpreter, SqlStatement statement) =>
            With(interpreter, null, (connection, transaction) =>
            {
                using DbCommand command = Command(connection, transaction, statement);
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

        private static bool IsBusy(KScriptException ex) =>
            ex.InnerException is SqliteException s && (s.SqliteErrorCode == 5 || s.SqliteErrorCode == 6);

        private T Once<T>(Interpreter interpreter, Func<T> work, IsolationLevel isolation)
        {
            TransactionState open = Current(interpreter);
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
                    state.Transaction.Rollback();
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
            try
            {
                state.Transaction.Commit();
            }
            catch (DbException ex)
            {
                throw Dialect.Translate(ex, null);
            }
        }

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
            SqliteConnection.ClearAllPools();
        }
    }
}
