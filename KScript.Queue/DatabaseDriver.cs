using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;

using KScript.Data;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>
    /// Jobs in a database table, through a KScript.Data connection (database.connections in the project's config).
    /// Each job is a row of kscript_jobs with its queue, when it may run, its lease, and the job itself as JSON. A worker
    /// claims a job in one write transaction that reads and takes the next due row, so only one worker ever gets it. Failed jobs go to
    /// kscript_failed_jobs, and unique-job locks to kscript_job_locks. The tables are made when first needed.
    /// </summary>
    public sealed class DatabaseQueueDriver : IQueueDriver
    {
        internal const string JobsTable = "kscript_jobs";
        internal const string FailedTable = "kscript_failed_jobs";
        internal const string LocksTable = "kscript_job_locks";

        private static readonly ConcurrentDictionary<Database, bool> Ready = new ConcurrentDictionary<Database, bool>();

        private readonly Database db;

        internal DatabaseQueueDriver(string name, Database db)
        {
            Name = name;
            this.db = db;
        }

        /// <summary>The driver for a queue connection: its database connection, or the project's default database.</summary>
        internal static DatabaseQueueDriver For(QueueContext context, QueueSettings.Connection connection)
        {
            string folder = context.Directory ?? Directory.GetCurrentDirectory();
            KScriptHost host = context.Host ?? new KScriptHost(null)
            {
                WorkingDirectory = folder,
                Config = context.Project?.ConfigOptions(context.Environment),
            };
            return new DatabaseQueueDriver(connection.Name, Database.For(host, folder, connection.Database));
        }

        public string Kind => "database";

        public string Name { get; }

        /// <summary>Where the jobs are, for kscript queue status: the database connection.</summary>
        public string Where => db.ToText();

        private static long Ms(DateTime time) =>
            new DateTimeOffset(time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime()).ToUnixTimeMilliseconds();

        private string Q(string name) => db.Dialect.Quote(name);

        private int Execute(string sql, params object[] parameters)
        {
            EnsureTables();
            return db.Execute(null, new SqlStatement(sql, parameters));
        }

        private List<Dictionary<string, object>> Rows(string sql, params object[] parameters)
        {
            EnsureTables();
            return db.Rows(null, new SqlStatement(sql, parameters));
        }

        /// <summary>Runs work in a transaction of its own, on one connection, committed when the work returns.</summary>
        private T Transaction<T>(Func<Tx, T> work)
        {
            EnsureTables();
            return db.With(null, null, (connection, _) =>
            {
                using DbTransaction transaction = connection.BeginTransaction();
                T result = work(new Tx(connection, transaction));
                transaction.Commit();
                return result;
            });
        }

        /// <summary>A connection and its open transaction.</summary>
        private readonly struct Tx
        {
            private readonly DbConnection connection;
            private readonly DbTransaction transaction;

            public Tx(DbConnection connection, DbTransaction transaction)
            {
                this.connection = connection;
                this.transaction = transaction;
            }

            private DbCommand Command(string sql, object[] parameters)
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
                return command;
            }

            public int Execute(string sql, params object[] parameters)
            {
                using DbCommand command = Command(sql, parameters);
                return command.ExecuteNonQuery();
            }

            public List<Dictionary<string, object>> Rows(string sql, params object[] parameters)
            {
                using DbCommand command = Command(sql, parameters);
                using DbDataReader reader = command.ExecuteReader();
                return Database.ReadAll(reader);
            }
        }

        /// <summary>Makes the three tables the first time this process uses the database.</summary>
        private void EnsureTables()
        {
            if (Ready.ContainsKey(db)) return;
            Dialect d = db.Dialect;
            string key = d.KeyText, text = d.LongText, number = d.BigInteger;
            foreach (string sql in d.CreateTable(JobsTable,
                    new[] { $"{Q("id")} {key} PRIMARY KEY", $"{Q("queue")} {key} NOT NULL", $"{Q("available_at")} {number} NOT NULL", $"{Q("reserved_until")} {number}", $"{Q("payload")} {text} NOT NULL" },
                    new[] { new TableIndex("ix_" + JobsTable + "_next", new[] { "queue", "available_at" }, false) }, ifMissing: true)
                .Concat(d.CreateTable(FailedTable,
                    new[] { $"{Q("id")} {key} PRIMARY KEY", $"{Q("queue")} {key} NOT NULL", $"{Q("failed_at")} {number} NOT NULL", $"{Q("payload")} {text} NOT NULL" },
                    Array.Empty<TableIndex>(), ifMissing: true))
                .Concat(d.CreateTable(LocksTable,
                    new[] { $"{Q("key")} {key} PRIMARY KEY", $"{Q("until")} {number} NOT NULL" },
                    Array.Empty<TableIndex>(), ifMissing: true)))
            {
                db.Execute(null, new SqlStatement(sql, Array.Empty<object>()));
            }
            Ready[db] = true;
        }

        /// <summary>Forgets which databases have their tables, for tests that delete database files.</summary>
        internal static void ForgetTables() => Ready.Clear();

        private static QueuedJob Parse(object payload)
        {
            try
            {
                return QueuedJob.FromJson(Values.ToStr(payload));
            }
            catch (KScriptException)
            {
                return null;
            }
        }

        private List<QueuedJob> Jobs(string sql, params object[] parameters) =>
            Rows(sql, parameters).Select(r => Parse(r["payload"])).Where(j => j != null).ToList();

        private string PushSql =>
            db.Dialect.Upsert($"INSERT INTO {Q(JobsTable)} ({Q("id")}, {Q("queue")}, {Q("available_at")}, {Q("reserved_until")}, {Q("payload")}) VALUES (@p1, @p2, @p3, NULL, @p4)",
                "id", new[] { "queue", "available_at", "reserved_until", "payload" });

        /// <summary>Saves a job as waiting: a new row, or the row it already has (a released or retried job).</summary>
        public void Push(QueuedJob job) => Execute(PushSql, job.Id, job.Queue, Ms(job.AvailableAt), job.ToJson());

        public QueuedJob Reserve(IReadOnlyList<string> queues, DateTime now)
        {
            long at = Ms(now);
            foreach (string queue in queues)
            {
                // The next due row (waiting, or one whose worker's lease ran out) is read and taken in one write
                // transaction, so two workers can never both get it.
                QueuedJob job = Transaction(tx =>
                {
                    while (true)
                    {
                        Dictionary<string, object> row = tx.Rows($"SELECT {Q("id")}, {Q("payload")} FROM {Q(JobsTable)} WHERE {Q("queue")} = @p1 AND {Q("available_at")} <= @p2"
                            + $" AND ({Q("reserved_until")} IS NULL OR {Q("reserved_until")} <= @p2) ORDER BY {Q("available_at")}, {Q("id")}{db.Dialect.Limit(1, null)}{db.Dialect.Lock("update", true)}",
                            queue, at).FirstOrDefault();
                        if (row == null) return null;
                        string id = Values.ToStr(row["id"]);
                        QueuedJob next = Parse(row["payload"]);
                        if (next == null)
                        {
                            // Not a job: move it to the failed jobs so it doesn't block the queue.
                            tx.Execute(db.Dialect.InsertOrIgnore($"INSERT INTO {Q(FailedTable)} ({Q("id")}, {Q("queue")}, {Q("failed_at")}, {Q("payload")}) SELECT {Q("id")}, {Q("queue")}, @p2, {Q("payload")} FROM {Q(JobsTable)} WHERE {Q("id")} = @p1", "id"), id, at);
                            tx.Execute($"DELETE FROM {Q(JobsTable)} WHERE {Q("id")} = @p1", id);
                            continue;
                        }
                        next.Attempts++;
                        next.ReservedUntil = now.AddSeconds(next.Timeout + QueueRuntime.LeaseMargin);
                        tx.Execute($"UPDATE {Q(JobsTable)} SET {Q("reserved_until")} = @p1, {Q("payload")} = @p2 WHERE {Q("id")} = @p3",
                            Ms(next.ReservedUntil.Value), next.ToJson(), id);
                        return next;
                    }
                });
                if (job != null) return job;
            }
            return null;
        }

        public void Complete(QueuedJob job) => Execute($"DELETE FROM {Q(JobsTable)} WHERE {Q("id")} = @p1", job.Id);

        public void Release(QueuedJob job)
        {
            QueuedJob copy = job.Copy();
            copy.ReservedUntil = null;
            Push(copy);
        }

        public void Fail(QueuedJob job)
        {
            QueuedJob copy = job.Copy();
            copy.ReservedUntil = null;
            Transaction(tx =>
            {
                tx.Execute(db.Dialect.Upsert($"INSERT INTO {Q(FailedTable)} ({Q("id")}, {Q("queue")}, {Q("failed_at")}, {Q("payload")}) VALUES (@p1, @p2, @p3, @p4)",
                    "id", new[] { "queue", "failed_at", "payload" }),
                    copy.Id, copy.Queue, Ms(copy.FailedAt ?? QueueRuntime.Clock()), copy.ToJson());
                return tx.Execute($"DELETE FROM {Q(JobsTable)} WHERE {Q("id")} = @p1", job.Id);
            });
        }

        public List<QueuedJob> Waiting(string queue) =>
            queue == null
                ? Jobs($"SELECT {Q("payload")} FROM {Q(JobsTable)} WHERE {Q("reserved_until")} IS NULL ORDER BY {Q("available_at")}, {Q("id")}")
                : Jobs($"SELECT {Q("payload")} FROM {Q(JobsTable)} WHERE {Q("reserved_until")} IS NULL AND {Q("queue")} = @p1 ORDER BY {Q("available_at")}, {Q("id")}", queue);

        public List<QueuedJob> Running() =>
            Jobs($"SELECT {Q("payload")} FROM {Q(JobsTable)} WHERE {Q("reserved_until")} IS NOT NULL ORDER BY {Q("id")}");

        public List<QueuedJob> Failed() =>
            Jobs($"SELECT {Q("payload")} FROM {Q(FailedTable)} ORDER BY {Q("failed_at")}, {Q("id")}");

        public List<string> QueueNames() =>
            Rows($"SELECT DISTINCT {Q("queue")} FROM {Q(JobsTable)} WHERE {Q("reserved_until")} IS NULL ORDER BY {Q("queue")}").Select(r => Values.ToStr(r["queue"])).ToList();

        public int Clear(string queue) =>
            Transaction(tx =>
            {
                // A cleared unique job no longer holds its key, so it can be dispatched again.
                string where = $"{Q("reserved_until")} IS NULL" + (queue == null ? string.Empty : $" AND {Q("queue")} = @p1");
                object[] parameters = queue == null ? Array.Empty<object>() : new object[] { queue };
                foreach (Dictionary<string, object> row in tx.Rows($"SELECT {Q("payload")} FROM {Q(JobsTable)} WHERE {where}", parameters))
                {
                    if (Parse(row["payload"])?.UniqueKey is string key) tx.Execute($"DELETE FROM {Q(LocksTable)} WHERE {Q("key")} = @p1", key);
                }
                return tx.Execute($"DELETE FROM {Q(JobsTable)} WHERE {where}", parameters);
            });

        public bool Retry(string id) =>
            Transaction(tx =>
            {
                Dictionary<string, object> row = tx.Rows($"SELECT {Q("payload")} FROM {Q(FailedTable)} WHERE {Q("id")} = @p1", id).FirstOrDefault();
                QueuedJob job = row == null ? null : Parse(row["payload"]);
                if (job == null) return false;
                QueueRuntime.ResetForRetry(job);
                tx.Execute(PushSql, job.Id, job.Queue, Ms(job.AvailableAt), job.ToJson());
                tx.Execute($"DELETE FROM {Q(FailedTable)} WHERE {Q("id")} = @p1", id);
                return true;
            });

        public bool Forget(string id) => Execute($"DELETE FROM {Q(FailedTable)} WHERE {Q("id")} = @p1", id) > 0;

        public bool Lock(string key, DateTime until, DateTime now)
        {
            // A new key, or one whose lock has run out, is taken; a key held until later changes nothing.
            if (db.Dialect.ConditionalUpsert)
            {
                return Execute(db.Dialect.Upsert($"INSERT INTO {Q(LocksTable)} ({Q("key")}, {Q("until")}) VALUES (@p1, @p2)", "key", new[] { "until" })
                    + $" WHERE {Q(LocksTable)}.{Q("until")} <= @p3", key, Ms(until), Ms(now)) > 0;
            }
            // MySQL's upsert can't have a WHERE: a lock that has run out is deleted, then the key is taken if it is free.
            return Transaction(tx =>
            {
                tx.Execute($"DELETE FROM {Q(LocksTable)} WHERE {Q("key")} = @p1 AND {Q("until")} <= @p2", key, Ms(now));
                return tx.Execute(db.Dialect.InsertOrIgnore($"INSERT INTO {Q(LocksTable)} ({Q("key")}, {Q("until")}) VALUES (@p1, @p2)", "key"), key, Ms(until)) > 0;
            });
        }

        public void Unlock(string key) => Execute($"DELETE FROM {Q(LocksTable)} WHERE {Q("key")} = @p1", key);

        public int Prune(DateTime before) => Execute($"DELETE FROM {Q(FailedTable)} WHERE {Q("failed_at")} < @p1", Ms(before));
    }
}
