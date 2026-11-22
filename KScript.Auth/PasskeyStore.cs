using KScript.Runtime;
using Microsoft.Data.Sqlite;

namespace KScript.Auth
{
    /// <summary>Durable passkeys and one-use ceremonies. A ceremony belongs to the server instance that issued it.</summary>
    internal sealed class PasskeyStore(string path)
    {
        internal sealed record Credential(string User, byte[] Handle, byte[] Key, uint Counter, bool BackupEligible);

        private const string UserPrefix = "ks-passkey:";

        internal static bool IsUserId(string id) => id?.StartsWith(UserPrefix, StringComparison.Ordinal) == true;
        internal static string NewUserId() => UserPrefix + Guid.NewGuid().ToString("N");

        private readonly string instance = AuthApp.NewSecret();
        private readonly object schemaLock = new();
        private bool initialized;

        private SqliteConnection Open()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var db = new SqliteConnection("Data Source=\"" + path.Replace("\"", "\"\"") + "\";Foreign Keys=True;Pooling=False");
            try
            {
                db.Open();
                lock (schemaLock)
                {
                    if (!initialized)
                    {
                        using var schema = Command(db, """
                            CREATE TABLE IF NOT EXISTS accounts (
                                rp TEXT NOT NULL, user TEXT NOT NULL, handle BLOB NOT NULL UNIQUE,
                                PRIMARY KEY (rp, user)
                            );
                            CREATE TABLE IF NOT EXISTS passkey_users (
                                id TEXT PRIMARY KEY, rp TEXT NOT NULL, profile TEXT NOT NULL,
                                FOREIGN KEY (rp, id) REFERENCES accounts(rp, user) ON DELETE CASCADE
                            );
                            CREATE TABLE IF NOT EXISTS credentials (
                                rp TEXT NOT NULL, id TEXT NOT NULL, user TEXT NOT NULL, public_key BLOB NOT NULL,
                                counter INTEGER NOT NULL, backup_eligible INTEGER NOT NULL, created_at TEXT NOT NULL,
                                PRIMARY KEY (rp, id), FOREIGN KEY (rp, user) REFERENCES accounts ON DELETE CASCADE
                            );
                            CREATE INDEX IF NOT EXISTS credentials_owner ON credentials(rp, user);
                            CREATE TABLE IF NOT EXISTS ceremonies (binding TEXT PRIMARY KEY, options TEXT NOT NULL, expires REAL NOT NULL);
                            CREATE INDEX IF NOT EXISTS ceremonies_expiry ON ceremonies(expires);
                            """);
                        schema.ExecuteNonQuery();
                        initialized = true;
                    }
                }
                return db;
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }

        private static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object Value)[] fields)
        {
            var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in fields) cmd.Parameters.AddWithValue("$" + name, value);
            return cmd;
        }

        public byte[] Handle(string rp, string user)
        {
            using var db = Open();
            using var cmd = Command(db, """
                INSERT INTO accounts(rp, user, handle) VALUES ($rp, $user, randomblob(32)) ON CONFLICT(rp, user) DO NOTHING;
                SELECT handle FROM accounts WHERE rp = $rp AND user = $user;
                """, ("rp", rp), ("user", user));
            return (byte[])cmd.ExecuteScalar();
        }

        public List<(string Id, string Created)> ForUser(string rp, string user)
        {
            using var db = Open();
            using var cmd = Command(db, "SELECT id, created_at FROM credentials WHERE rp = $rp AND user = $user ORDER BY created_at, id", ("rp", rp), ("user", user));
            using var rows = cmd.ExecuteReader();
            var keys = new List<(string, string)>();
            while (rows.Read()) keys.Add((rows.GetString(0), rows.GetString(1)));
            return keys;
        }

        public Credential Find(string rp, string id)
        {
            using var db = Open();
            using var cmd = Command(db, """
                SELECT user, handle, public_key, counter, backup_eligible FROM credentials JOIN accounts USING (rp, user)
                WHERE rp = $rp AND id = $id
                """, ("rp", rp), ("id", id));
            using var row = cmd.ExecuteReader();
            return row.Read() ? new Credential(row.GetString(0), (byte[])row[1], (byte[])row[2], checked((uint)row.GetInt64(3)), row.GetBoolean(4)) : null;
        }

        public bool Add(string rp, string id, string user, byte[] handle, byte[] key, uint counter, bool eligible)
        {
            using var db = Open();
            return Add(db, null, rp, id, user, handle, key, counter, eligible);
        }

        private static bool Add(SqliteConnection db, SqliteTransaction transaction, string rp, string id, string user, byte[] handle, byte[] key, uint counter, bool eligible)
        {
            using var cmd = Command(db, """
                INSERT INTO credentials(rp, id, user, public_key, counter, backup_eligible, created_at)
                SELECT rp, $id, user, $key, $counter, $eligible, $created FROM accounts
                WHERE rp = $rp AND user = $user AND handle = $handle ON CONFLICT(rp, id) DO NOTHING
                """, ("rp", rp), ("id", id), ("user", user), ("handle", handle), ("key", key),
                ("counter", (long)counter), ("eligible", eligible), ("created", AuthApp.Stamp()));
            cmd.Transaction = transaction;
            return cmd.ExecuteNonQuery() == 1;
        }

        // Store the new identity, its WebAuthn user handle and its first credential together. If the
        // credential was concurrently registered or any write fails, disposing the transaction rolls back all three.
        public bool CreateUser(string rp, Dictionary<string, object> profile, string id, byte[] handle, byte[] key, uint counter, bool eligible)
        {
            string user = (string)profile["id"];
            using var db = Open();
            using var transaction = db.BeginTransaction();
            using var account = Command(db, """
                INSERT INTO accounts(rp, user, handle) VALUES ($rp, $user, $handle);
                INSERT INTO passkey_users(id, rp, profile) VALUES ($user, $rp, $profile)
                """, ("rp", rp), ("user", user), ("handle", handle), ("profile", Values.ToJson(profile)));
            account.Transaction = transaction;
            account.ExecuteNonQuery();
            if (!Add(db, transaction, rp, id, user, handle, key, counter, eligible)) return false;
            transaction.Commit();
            return true;
        }

        // Other stores may use UUIDs, integers or app-defined keys. Only the identifier pattern generated
        // by signup warrants a lookup here, and existing sites need no passkey database to read their users.
        public Dictionary<string, object> User(string id)
        {
            if (!IsUserId(id) || !File.Exists(path)) return null;
            using var db = Open();
            using var cmd = Command(db, "SELECT profile FROM passkey_users WHERE id = $user", ("user", id));
            return cmd.ExecuteScalar() is string profile ? (Dictionary<string, object>)Values.ParseJson(profile) : null;
        }

        public bool UpdateUser(string id, Dictionary<string, object> profile)
        {
            using var db = Open();
            using var cmd = Command(db, "UPDATE passkey_users SET profile = $profile WHERE id = $user", ("profile", Values.ToJson(profile)), ("user", id));
            return cmd.ExecuteNonQuery() == 1;
        }

        public IEnumerable<Dictionary<string, object>> Users()
        {
            if (!File.Exists(path)) yield break;
            using var db = Open();
            using var cmd = Command(db, "SELECT profile FROM passkey_users");
            using var rows = cmd.ExecuteReader();
            while (rows.Read()) yield return (Dictionary<string, object>)Values.ParseJson(rows.GetString(0));
        }

        public int UserCount()
        {
            if (!File.Exists(path)) return 0;
            using var db = Open();
            using var cmd = Command(db, "SELECT count(*) FROM passkey_users");
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public bool Advance(string rp, string id, Credential credential, uint counter)
        {
            using var db = Open();
            using var cmd = Command(db, """
                UPDATE credentials SET counter = $next WHERE rp = $rp AND id = $id AND counter = $previous AND public_key = $key AND user = $user
                """, ("rp", rp), ("id", id), ("previous", (long)credential.Counter), ("next", (long)counter), ("key", credential.Key), ("user", credential.User));
            return cmd.ExecuteNonQuery() == 1;
        }

        public bool Revoke(string rp, string user, string id)
        {
            using var db = Open();
            // An account created without a password must retain a usable credential. Check and delete
            // in the same statement so concurrent revocations cannot remove its final two keys.
            using var cmd = Command(db, """
                DELETE FROM credentials WHERE rp = $rp AND user = $user AND id = $id
                AND (NOT EXISTS (SELECT 1 FROM passkey_users WHERE id = $user)
                  OR EXISTS (SELECT 1 FROM credentials WHERE rp = $rp AND user = $user AND id <> $id))
                """, ("rp", rp), ("user", user), ("id", id));
            return cmd.ExecuteNonQuery() == 1;
        }

        public void ForgetUser(string user)
        {
            if (!File.Exists(path)) return;
            using var db = Open();
            using var cmd = Command(db, "DELETE FROM accounts WHERE user = $user", ("user", user));
            cmd.ExecuteNonQuery();
        }

        public void Challenge(string binding, string options, int seconds)
        {
            using var db = Open();
            double now = AuthApp.Now();
            using var cmd = Command(db, """
                DELETE FROM ceremonies WHERE expires <= $now;
                INSERT OR REPLACE INTO ceremonies(binding, options, expires) VALUES ($binding, $options, $expires);
                """, ("binding", AuthApp.KeyOf(instance + binding)), ("options", options), ("expires", now + seconds), ("now", now));
            cmd.ExecuteNonQuery();
        }

        public string Consume(string binding)
        {
            using var db = Open();
            using var cmd = Command(db, "DELETE FROM ceremonies WHERE binding = $binding AND expires > $now RETURNING options", ("binding", AuthApp.KeyOf(instance + binding)), ("now", AuthApp.Now()));
            return cmd.ExecuteScalar() as string;
        }
    }
}
