using System.Data.Common;
using System.Globalization;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Data.Migrations
{
    /// <summary>A migration file: its name (the file's, without the ending) and its steps.</summary>
    internal sealed class MigrationFile
    {
        public string Name { get; init; }

        public string Path { get; init; }

        public MigrationPlan Plan { get; init; }
    }

    /// <summary>A row of kscript_migrations: a migration that has run, and the batch it ran in.</summary>
    internal sealed record AppliedMigration(long Id, string Name, int Batch, string RanAt);

    /// <summary>
    /// Runs a project's migrations on its database: which have run is kept in the kscript_migrations table. The
    /// schema the migrations make is worked out by replaying the files, so db diff, rollback and status need no
    /// copy of it in the database.
    /// </summary>
    internal sealed class Migrator
    {
        public const string Table = "kscript_migrations";

        private readonly KScriptHost host;
        private List<MigrationFile> files;
        private Database database;

        public Migrator(string folder, KScriptHost host, ProjectConfig project = null)
        {
            this.host = host;
            Project = project ?? ProjectConfig.FindAndLoad(folder);
            Directory = Project?.Directory ?? System.IO.Path.GetFullPath(folder);
        }

        public ProjectConfig Project { get; }

        /// <summary>The project's folder.</summary>
        public string Directory { get; }

        public string MigrationsFolder => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, Project?.PathFor("migrations") ?? ProjectConfig.DefaultPaths["migrations"]));

        public string SeedersFolder => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, Project?.PathFor("seeders") ?? ProjectConfig.DefaultPaths["seeders"]));

        public string ModelsFolder => System.IO.Path.GetFullPath(System.IO.Path.Combine(Directory, Project?.PathFor("models") ?? ProjectConfig.DefaultPaths["models"]));

        public Database Database => database ??= Database.For(host, Directory, null);

        private Ddl Ddl => Ddl.For(Database.Dialect);

        /// <summary>Called as each migration finishes, with the time it took (for the command's output).</summary>
        public Action<MigrationFile, TimeSpan> Done { get; set; }

        /// <summary>Called with each statement when only printing the SQL (--pretend).</summary>
        public Action<MigrationFile, string> Pretended { get; set; }

        /// <summary>An interpreter for the project's scripts, with the packages kscript.json gives every script.</summary>
        internal Interpreter NewInterpreter()
        {
            var options = new InterpreterOptions();
            if (Project != null) options.DefaultPackages.AddRange(Project.Packages);
            return new Interpreter(host, options: options);
        }

        #region Files

        /// <summary>The scripts in a folder and the folders inside it, in order of their file names.</summary>
        internal static List<string> Scripts(string folder) =>
            !System.IO.Directory.Exists(folder) ? new List<string>()
            : System.IO.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(ProjectConfig.IsExtensionFileName)
                .OrderBy(f => ProjectConfig.ScriptName(f), StringComparer.Ordinal).ThenBy(f => f, StringComparer.Ordinal)
                .ToList();

        /// <summary>The migration files, read once: each is run to collect its steps.</summary>
        public List<MigrationFile> Files()
        {
            if (files != null) return files;
            var list = new List<MigrationFile>();
            var interpreter = NewInterpreter();
            foreach (string path in Scripts(MigrationsFolder))
            {
                string name = ProjectConfig.ScriptName(path);
                if (list.Any(f => f.Name == name)) throw new KScriptException($"Two migrations are called {name}. Rename one of them.", "Migration");
                list.Add(new MigrationFile { Name = name, Path = path, Plan = Read(interpreter, path) });
            }
            return files = list;
        }

        private MigrationPlan Read(Interpreter interpreter, string path)
        {
            var plan = new MigrationPlan();
            Module module = interpreter.Load(path);
            if (module.MainElementName != "Migration")
                throw new KScriptException($"{System.IO.Path.GetFileName(path)} is in the migrations folder, so it holds Migration {{ }} after import KScript.Data (or <KScript.Migration> in an XML file), not {(module.MainElement ?? module.Root).Name}.", "Migration")
                { File = path, Line = 1 };
            lock (host.Items)
            {
                host.Items[MigrationPlan.HostKey] = plan;
            }
            try
            {
                interpreter.RunModule(module);
            }
            catch (KScriptException ex)
            {
                throw Interpreter.WithLocation(ex, module);
            }
            finally
            {
                lock (host.Items)
                {
                    host.Items.Remove(MigrationPlan.HostKey);
                }
            }
            return plan;
        }

        /// <summary>
        /// The schema the migrations make, run in the order they ran (then the pending ones in order), stopping
        /// before <paramref name="until"/> when it is given.
        /// </summary>
        public Schema Replay(IEnumerable<MigrationFile> order, MigrationFile until = null)
        {
            var schema = new Schema();
            foreach (MigrationFile file in order)
            {
                if (file == until) break;
                Apply(schema, file);
            }
            return schema;
        }

        private static void Apply(Schema schema, MigrationFile file)
        {
            foreach (Step step in file.Plan.Up)
            {
                try
                {
                    step.Apply(schema);
                }
                catch (KScriptException ex) when (ex.File == null)
                {
                    throw new KScriptException($"{file.Name}: {ex.Message}", ex.Type, ex) { File = file.Path };
                }
            }
        }

        /// <summary>Every migration, in the order they make the schema: those that ran, in the order they ran, then the rest by name.</summary>
        public List<MigrationFile> Ordered(IReadOnlyList<AppliedMigration> applied)
        {
            var byName = Files().ToDictionary(f => f.Name, StringComparer.Ordinal);
            var ran = applied.OrderBy(a => a.Id).Select(a => byName.GetValueOrDefault(a.Name)).Where(f => f != null).ToList();
            return ran.Concat(Files().Where(f => !ran.Contains(f))).ToList();
        }

        #endregion

        #region The migrations table

        /// <summary>The migrations that have run. None when the table isn't there yet.</summary>
        public List<AppliedMigration> Applied() => Connected((connection, _) => Applied(connection, null));

        private List<AppliedMigration> Applied(DbConnection connection, DbTransaction transaction)
        {
            if (!HasTable(connection, transaction)) return new List<AppliedMigration>();
            return Query(connection, transaction, $"SELECT id, migration, batch, ran_at FROM {Q(Table)} ORDER BY id")
                .Select(r => new AppliedMigration(
                    Convert.ToInt64(r["id"], CultureInfo.InvariantCulture),
                    Convert.ToString(r["migration"], CultureInfo.InvariantCulture),
                    Convert.ToInt32(r["batch"], CultureInfo.InvariantCulture),
                    r["ran_at"] is DateTime d ? d.ToString(Rows.DateFormat, CultureInfo.InvariantCulture) : Convert.ToString(r["ran_at"], CultureInfo.InvariantCulture)))
                .ToList();
        }

        private bool HasTable(DbConnection connection, DbTransaction transaction) =>
            Query(connection, transaction, Database.Dialect.TablesQuery).Any(r => Values.ToStr(r.Values.First()) == Table);

        private void EnsureTable(DbConnection connection)
        {
            if (HasTable(connection, null)) return;
            var t = new TableSpec { Name = Table };
            t.Columns.Add(new ColumnSpec { Name = "id", Kind = ColumnKind.Integer, IsKey = true, AutoIncrement = true });
            t.Columns.Add(new ColumnSpec { Name = "migration", Kind = ColumnKind.Text, Length = 255 });
            t.Columns.Add(new ColumnSpec { Name = "batch", Kind = ColumnKind.Integer });
            t.Columns.Add(new ColumnSpec { Name = "ran_at", Kind = ColumnKind.Date });
            t.Indexes.Add(new TableIndex(Dialect.IndexName(Table, new[] { "migration" }, true), new[] { "migration" }, true));
            foreach (string sql in Ddl.CreateTable(t)) Execute(connection, null, sql);
        }

        #endregion

        #region Running

        /// <summary>The migrations that haven't run, in order.</summary>
        public List<MigrationFile> Pending(IReadOnlyList<AppliedMigration> applied)
        {
            var ran = new HashSet<string>(applied.Select(a => a.Name), StringComparer.Ordinal);
            return Files().Where(f => !ran.Contains(f.Name)).ToList();
        }

        /// <summary>Runs the pending migrations as one batch, and gives the ones it ran. With <paramref name="pretend"/>, only gives their SQL to <see cref="Pretended"/>.</summary>
        public List<MigrationFile> Migrate(bool pretend = false) => Connected((connection, _) =>
        {
            if (!pretend) EnsureTable(connection);
            List<AppliedMigration> applied = Applied(connection, null);
            List<MigrationFile> pending = Pending(applied);
            if (pending.Count == 0) return pending;
            Schema schema = Replay(Ordered(applied).Where(f => !pending.Contains(f)));
            int batch = applied.Count == 0 ? 1 : applied.Max(a => a.Batch) + 1;
            foreach (MigrationFile file in pending)
            {
                var steps = file.Plan.Up.ToList();
                Run(connection, file, steps, schema, pretend, record: transaction =>
                    Execute(connection, transaction, $"INSERT INTO {Q(Table)} (migration, batch, ran_at) VALUES ({P(1)}, {P(2)}, {P(3)})", file.Name, (long)batch, Database.Dialect.Date(Rows.Clock())));
            }
            return pending;
        });

        /// <summary>
        /// Brings a new, empty database (a test file's) to the project's schema: its migrations, or the tables of its
        /// models when it has no migrations.
        /// </summary>
        public void Build()
        {
            if (Files().Count > 0)
            {
                Migrate();
                return;
            }
            List<ModelInfo> models = Models();
            if (models.Count == 0) return;
            List<Step> steps = Differ.Diff(new Schema(), Schema.FromModels(models));
            Connected((connection, _) =>
            {
                Run(connection, new MigrationFile { Name = "the models", Plan = new MigrationPlan() }, steps, new Schema(), false, record: _ => { });
                return steps.Count;
            });
        }

        /// <summary>
        /// Undoes the last batch, or the last <paramref name="steps"/> migrations: each with its Down block, or by
        /// undoing its steps from last to first. Gives the ones it undid.
        /// </summary>
        public List<MigrationFile> Rollback(int? steps = null, bool pretend = false, bool all = false) => Connected((connection, _) =>
        {
            List<AppliedMigration> applied = Applied(connection, null);
            if (applied.Count == 0) return new List<MigrationFile>();
            int lastBatch = applied.Max(a => a.Batch);
            List<AppliedMigration> undo = (all ? applied : steps is int n ? applied.OrderByDescending(a => a.Id).Take(n) : applied.Where(a => a.Batch == lastBatch))
                .OrderByDescending(a => a.Id).ToList();
            var byName = Files().ToDictionary(f => f.Name, StringComparer.Ordinal);
            List<MigrationFile> order = Ordered(applied).Where(f => applied.Any(a => a.Name == f.Name)).ToList();
            var done = new List<MigrationFile>();
            foreach (AppliedMigration a in undo)
            {
                MigrationFile file = byName.GetValueOrDefault(a.Name)
                    ?? throw new KScriptException($"The migration {a.Name} has run, but its file isn't in {Relative(MigrationsFolder)} any more, so it can't be undone. Put it back.", "Migration");
                Schema before = Replay(order, until: file);
                Schema after = before.Clone();
                Apply(after, file);
                List<Step> reverse = Reverse(file, before);
                Run(connection, file, reverse, after, pretend, record: transaction =>
                    Execute(connection, transaction, $"DELETE FROM {Q(Table)} WHERE id = {P(1)}", a.Id));
                order.Remove(file);
                done.Add(file);
            }
            return done;
        });

        /// <summary>The steps that undo a migration: its Down block, or each step undone, last first.</summary>
        internal static List<Step> Reverse(MigrationFile file, Schema before)
        {
            if (file.Plan.Down != null) return file.Plan.Down;
            var schema = before.Clone();
            var undo = new List<List<Step>>();
            var stuck = new List<string>();
            foreach (Step step in file.Plan.Up)
            {
                IEnumerable<Step> reverse = step.Reverse(schema);
                if (reverse == null) stuck.Add(step.Describe());
                else undo.Add(reverse.ToList());
                step.Apply(schema);
            }
            if (stuck.Count > 0)
                throw new KScriptException($"{file.Name} can't be undone: nothing says how to undo {string.Join(", ", stuck)}. Add a down: to its Sql, or a Down {{ }} block with the steps that undo the migration.", "Migration")
                { File = file.Path };
            undo.Reverse();
            return undo.SelectMany(s => s).ToList();
        }

        /// <summary>
        /// Runs a migration's steps, in a transaction where the database has them for changing tables (MySQL
        /// commits each one), then records it. SQLite runs it with foreign keys off, so a table can be made again,
        /// and checks them before it commits.
        /// </summary>
        private void Run(DbConnection connection, MigrationFile file, List<Step> steps, Schema schema, bool pretend, Action<DbTransaction> record)
        {
            var started = DateTime.UtcNow;
            Ddl ddl = Ddl;
            bool sqlite = Database.Dialect is SqliteDialect;
            if (pretend)
            {
                foreach (Step step in steps)
                {
                    Schema before = schema.Clone();
                    Apply(schema, file, step);
                    foreach (string sql in step.Sql(ddl, before, schema, null)) Pretended?.Invoke(file, sql);
                }
                return;
            }
            if (sqlite) Execute(connection, null, "PRAGMA foreign_keys = OFF");
            DbTransaction transaction = ddl.Transactional ? connection.BeginTransaction() : null;
            int done = 0;
            Step current = null;
            try
            {
                Lookup lookup = (sql, parameters) => Query(connection, transaction, sql, parameters);
                foreach (Step step in steps)
                {
                    current = step;
                    Schema before = schema.Clone();
                    Apply(schema, file, step);
                    foreach (string sql in step.Sql(ddl, before, schema, lookup)) Execute(connection, transaction, sql);
                    done++;
                }
                current = null;
                if (sqlite)
                {
                    List<Dictionary<string, object>> broken = Query(connection, transaction, "PRAGMA foreign_key_check");
                    if (broken.Count > 0)
                    {
                        Dictionary<string, object> first = broken[0];
                        throw new KScriptException($"rows of {first["table"]} point to rows of {first["parent"]} that aren't there", "ForeignKeyViolation");
                    }
                }
                record(transaction);
                transaction?.Commit();
            }
            catch (Exception ex) when (ex is KScriptException or DbException)
            {
                transaction?.Rollback();
                string message = ex is DbException db ? (Database.Dialect.Translate(db, null)?.Message ?? db.Message) : ex.Message;
                string where = current != null ? $" at '{current.Describe()}'" : string.Empty;
                string kept = ddl.Transactional || done == 0 ? " Nothing it did was kept." : $" {done} of its {steps.Count} steps ran before that, and stay: {Database.Dialect.Name} can't undo changes to tables.";
                throw new KScriptException($"The migration {file.Name} failed{where}: {message.TrimEnd('.')}.{kept}", "Migration", ex) { File = file.Path };
            }
            finally
            {
                transaction?.Dispose();
                if (sqlite) Execute(connection, null, "PRAGMA foreign_keys = ON");
            }
            Done?.Invoke(file, DateTime.UtcNow - started);
        }

        private static void Apply(Schema schema, MigrationFile file, Step step)
        {
            try
            {
                step.Apply(schema);
            }
            catch (KScriptException ex) when (ex.File == null)
            {
                throw new KScriptException($"{file.Name}: {ex.Message}", ex.Type, ex) { File = file.Path };
            }
        }

        /// <summary>Drops every table in the database, for db fresh.</summary>
        public List<string> DropAll() => Connected((connection, _) =>
        {
            List<string> tables = Query(connection, null, Database.Dialect.TablesQuery).Select(r => Values.ToStr(r.Values.First())).ToList();
            switch (Database.Dialect)
            {
                case SqliteDialect:
                    Execute(connection, null, "PRAGMA foreign_keys = OFF");
                    foreach (string t in tables) Execute(connection, null, "DROP TABLE " + Q(t));
                    Execute(connection, null, "PRAGMA foreign_keys = ON");
                    break;
                case PostgresDialect:
                    foreach (string t in tables) Execute(connection, null, $"DROP TABLE IF EXISTS {Q(t)} CASCADE");
                    break;
                default:
                    Execute(connection, null, "SET FOREIGN_KEY_CHECKS = 0");
                    foreach (string t in tables) Execute(connection, null, "DROP TABLE " + Q(t));
                    Execute(connection, null, "SET FOREIGN_KEY_CHECKS = 1");
                    break;
            }
            return tables;
        });

        #endregion

        #region Models and seeders

        /// <summary>The models in the project's models folder, declared by running their files.</summary>
        public List<ModelInfo> Models()
        {
            var interpreter = NewInterpreter();
            foreach (string path in Scripts(ModelsFolder))
            {
                Module module = interpreter.Load(path);
                try
                {
                    interpreter.RunModule(module);
                }
                catch (KScriptException ex)
                {
                    throw Interpreter.WithLocation(ex, module);
                }
            }
            // Models on another connection have their own database; these migrations are for the default one.
            return ModelInfo.DeclaredBy(interpreter).Where(m => m.Connection == null).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        }

        /// <summary>The seeders, or the one called <paramref name="name"/>.</summary>
        public List<string> Seeders(string name = null)
        {
            List<string> all = Scripts(SeedersFolder);
            if (name == null) return all;
            string found = ProjectConfig.ModelFileFor(all, name);
            if (found != null) return new List<string> { found };
            string close = Values.Closest(name, all.Select(ProjectConfig.ScriptName));
            throw new KScriptException($"There is no seeder called {name} in {Relative(SeedersFolder)}." + (close != null ? $" Did you mean {close}?" : all.Count == 0 ? " Make one with kscript make seeder " + name + "." : string.Empty), "Migration");
        }

        #endregion

        #region SQL

        private string Q(string name) => Database.Dialect.Quote(name);

        private string P(int n) => Database.Dialect.Parameter(n);

        internal string Relative(string path) => System.IO.Path.GetRelativePath(Directory, path).Replace('\\', '/');

        private T Connected<T>(Func<DbConnection, DbTransaction, T> work)
        {
            using DbConnection connection = Database.OpenConnection();
            try
            {
                return work(connection, null);
            }
            catch (DbException ex)
            {
                throw Database.Dialect.Translate(ex, null) ?? new KScriptException(ex.Message, "Query", ex);
            }
        }

        private static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql, object[] parameters)
        {
            DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            for (int i = 0; i < parameters.Length; i++)
            {
                DbParameter p = command.CreateParameter();
                p.ParameterName = "@p" + (i + 1).ToString(CultureInfo.InvariantCulture);
                p.Value = parameters[i] ?? DBNull.Value;
                command.Parameters.Add(p);
            }
            Database.Log?.Add(new SqlStatement(sql, parameters));
            return command;
        }

        private static void Execute(DbConnection connection, DbTransaction transaction, string sql, params object[] parameters)
        {
            using DbCommand command = Command(connection, transaction, sql, parameters);
            command.ExecuteNonQuery();
        }

        private static List<Dictionary<string, object>> Query(DbConnection connection, DbTransaction transaction, string sql, params object[] parameters)
        {
            using DbCommand command = Command(connection, transaction, sql, parameters);
            using DbDataReader reader = command.ExecuteReader();
            return Database.ReadAll(reader);
        }

        #endregion
    }
}
