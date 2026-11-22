using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Data;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// Records kept by key, such as sessions by the hash of their id. Values are maps that go through JSON, so what
    /// comes out is a copy of what went in.
    /// </summary>
    public abstract class RecordStore
    {
        private static readonly Regex SafeKey = new Regex("^[a-z0-9][a-z0-9-]{0,127}$", RegexOptions.CultureInvariant);

        public abstract Dictionary<string, object> Get(string key);

        public abstract void Put(string key, Dictionary<string, object> record);

        public abstract bool Delete(string key);

        /// <summary>Every record, by key.</summary>
        public abstract IEnumerable<KeyValuePair<string, Dictionary<string, object>>> All();

        /// <summary>Adds a record under a new key (a random uuid), sets its id to the key, and returns the key.</summary>
        public virtual string Add(Dictionary<string, object> record)
        {
            string key = UuidValue.New(4).Text;
            record["id"] = key;
            Put(key, record);
            return key;
        }

        /// <summary>The first record whose <paramref name="field"/> is this text (case doesn't matter), or null.</summary>
        public virtual Dictionary<string, object> FindBy(string field, string value) =>
            All().Select(kv => kv.Value)
                .FirstOrDefault(r => string.Equals(AuthApp.Text(r.GetValueOrDefault(field)), value, StringComparison.OrdinalIgnoreCase));

        /// <summary>How many records there are.</summary>
        public virtual int Count() => All().Count();

        /// <summary>A record as scripts see it: <paramref name="shown"/>, the record without its private fields, by default.</summary>
        public virtual Dictionary<string, object> Present(Dictionary<string, object> record, Dictionary<string, object> shown) => shown;

        /// <summary>Keys are made by KScript.Auth (ids and hashes), never by a request, but are checked all the same.</summary>
        protected static string Check(string key)
        {
            if (key == null || !SafeKey.IsMatch(key)) throw new KScriptException($"'{key}' isn't a record key.", "Argument");
            return key;
        }
    }

    /// <summary>Records in memory: they end when kscript does.</summary>
    public sealed class MemoryRecordStore : RecordStore
    {
        private readonly ConcurrentDictionary<string, string> records = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public override Dictionary<string, object> Get(string key) =>
            records.TryGetValue(Check(key), out string json) ? (Dictionary<string, object>)Values.ParseJson(json) : null;

        public override void Put(string key, Dictionary<string, object> record) => records[Check(key)] = Values.ToJson(record);

        public override bool Delete(string key) => records.TryRemove(Check(key), out _);

        public override IEnumerable<KeyValuePair<string, Dictionary<string, object>>> All() =>
            records.ToArray().Select(kv => new KeyValuePair<string, Dictionary<string, object>>(kv.Key, (Dictionary<string, object>)Values.ParseJson(kv.Value)));
    }

    /// <summary>Records as JSON files in a folder, one per key, written safely (a temporary file, then a move).</summary>
    public sealed class FileRecordStore : RecordStore
    {
        public FileRecordStore(string folder) => Folder = folder;

        public string Folder { get; }

        private string PathOf(string key) => Path.Combine(Folder, Check(key) + ".json");

        public override Dictionary<string, object> Get(string key)
        {
            string path = PathOf(key);
            return File.Exists(path) ? Read(path) : null;
        }

        private static Dictionary<string, object> Read(string path)
        {
            try
            {
                return Values.ParseJson(File.ReadAllText(path)) as Dictionary<string, object>;
            }
            catch (Exception ex) when (ex is IOException || ex is KScriptException)
            {
                // A file being replaced, or a broken one, counts as missing.
                return null;
            }
        }

        public override void Put(string key, Dictionary<string, object> record)
        {
            string path = PathOf(key);
            Directory.CreateDirectory(Folder);
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(temp, Values.ToJson(record, indented: true), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }

        public override bool Delete(string key)
        {
            string path = PathOf(key);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public override IEnumerable<KeyValuePair<string, Dictionary<string, object>>> All()
        {
            if (!Directory.Exists(Folder)) yield break;
            foreach (string file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                Dictionary<string, object> record = Read(file);
                if (record != null) yield return new KeyValuePair<string, Dictionary<string, object>>(Path.GetFileNameWithoutExtension(file), record);
            }
        }
    }

    /// <summary>
    /// One app's sign-in state: its settings, users, sessions, API tokens, password reset tokens and the count of
    /// failed sign-ins. There is one per site folder, shared by every request.
    /// </summary>
    public sealed class AuthApp
    {
        private static readonly ConcurrentDictionary<string, AuthApp> Apps = new ConcurrentDictionary<string, AuthApp>(StringComparer.Ordinal);

        /// <summary>The time now. Tests replace it.</summary>
        internal static Func<DateTime> Clock = () => DateTime.UtcNow;

        private readonly Dictionary<string, (DateTime Start, int Count)> failures = new Dictionary<string, (DateTime, int)>(StringComparer.Ordinal);

        private DateTime lastAttemptCleanup;

        private AuthApp(string root, string name, AuthSettings settings)
        {
            Root = root;
            Name = name;
            Settings = settings;
            var (data, state) = StorageFolders(root, name, settings);
            Users = settings.Users != null
                ? new ModelRecordStore(ModelStore.Load(KScriptHost.ForText(new StringWriter()), root, settings.Users, "http.auth.users"))
                : new FileRecordStore(Path.Combine(data, "users"));
            Sessions = settings.Sessions == "memory" ? new MemoryRecordStore() : new FileRecordStore(Path.Combine(state, "sessions"));
            Tokens = new FileRecordStore(Path.Combine(state, "tokens"));
            Resets = new FileRecordStore(Path.Combine(state, "resets"));
            PasskeyStore = new PasskeyStore(Path.Combine(data, "_passkeys", "passkeys.sqlite"));
        }

        /// <summary>The actual locations of the application's auth data and session state.</summary>
        public static (string Data, string State) StorageFolders(string root, string name, AuthSettings settings)
        {
            string own = settings.Folder == null ? null : AppFolders.ProjectDataPath(root, settings.Folder);
            return (own ?? Path.Combine(AppFolders.Folder(name, "data"), "auth"),
                own ?? Path.Combine(AppFolders.Folder(name, "state"), "auth"));
        }

        /// <summary>The site (or script) folder.</summary>
        public string Root { get; }

        /// <summary>The app's name, which its folders are named after.</summary>
        public string Name { get; }

        public AuthSettings Settings { get; }

        public RecordStore Users { get; }
        public RecordStore Sessions { get; }
        public RecordStore Tokens { get; }
        public RecordStore Resets { get; }
        internal PasskeyStore PasskeyStore { get; }

        /// <summary>The app for a site folder, with the settings the server read from kscript.json.</summary>
        public static AuthApp For(string root, AuthSettings settings)
        {
            string full = Path.GetFullPath(root);
            string key = full + "\n" + Values.ToJson(Describe(settings));
            return Apps.GetOrAdd(key, _ => new AuthApp(full, AppFolders.CleanName(AppFolders.AppNameFor(full, null)), settings));
        }

        /// <summary>The app for a folder, with the settings in the nearest kscript.json (or the defaults).</summary>
        public static AuthApp ForFolder(string folder)
        {
            ProjectConfig project;
            try
            {
                project = ProjectConfig.FindAndLoad(folder);
            }
            catch (ProjectConfigException)
            {
                project = null;
            }
            string root = project?.FilePath != null ? Path.GetDirectoryName(project.FilePath) : folder;
            return For(root, project?.Http.Auth ?? new AuthSettings());
        }

        private static Dictionary<string, object> Describe(AuthSettings s) => new Dictionary<string, object>
        {
            ["folder"] = s.Folder,
            ["users"] = s.Users,
            ["sessions"] = s.Sessions,
            ["login"] = s.Login,
            ["home"] = s.Home,
            ["lifetime"] = s.Lifetime,
            ["remember"] = s.Remember,
            ["cookie"] = s.Cookie,
            ["secure"] = s.Secure,
            ["same_site"] = s.SameSite,
            ["csrf"] = s.Csrf,
            ["attempts"] = (double)s.Attempts,
            ["lockout"] = s.Lockout,
            ["reset_lifetime"] = s.ResetLifetime,
            ["passkey_origin"] = s.PasskeyOrigin,
            ["passkey_signup"] = s.PasskeySignup,
            ["passkey_rp_id"] = s.PasskeyRpId,
        };

        #region Secrets

        /// <summary>A new random secret, as URL-safe text: 32 bytes, 43 characters.</summary>
        public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

        /// <summary>The key a secret is kept under: its SHA-256, so a copy of the files can't be used to sign in.</summary>
        public static string KeyOf(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? string.Empty))).ToLowerInvariant();

        internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>True when the two texts are the same, taking the same time whatever they hold.</summary>
        public static bool SameSecret(string a, string b) =>
            a != null && b != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        /// <summary>A value as text, keeping null as null (Values.ToStr makes it empty), so a missing id is never "".</summary>
        internal static string Text(object value) => value == null ? null : Values.ToStr(value);

        internal static double Now() => (Clock() - DateTime.UnixEpoch).TotalSeconds;

        internal static string Stamp() => Clock().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        #endregion

        #region Failed sign-ins

        /// <summary>
        /// Stops a sign-in when there have been too many failures for this email and address lately: throws a
        /// TooManyRequests error that says how long to wait.
        /// </summary>
        internal void CheckAttempts(string key)
        {
            lock (failures)
            {
                if (!failures.TryGetValue(key, out var entry)) return;
                double age = (Clock() - entry.Start).TotalSeconds;
                if (age >= Settings.Lockout)
                {
                    failures.Remove(key);
                    return;
                }
                if (entry.Count < Settings.Attempts) return;
                int wait = (int)Math.Ceiling(Settings.Lockout - age);
                var error = new KScriptException($"Too many sign-in attempts. Try again in {wait} second{(wait == 1 ? string.Empty : "s")}.", "TooManyRequests");
                error.Fields["retry_after"] = (double)wait;
                throw error;
            }
        }

        /// <summary>Reserves one attempt before an anonymous request can create persistent session state.</summary>
        internal void ReserveAttempt(string key)
        {
            lock (failures)
            {
                CheckAttempts(key);
                Failed(key);
            }
        }

        internal void Failed(string key)
        {
            lock (failures)
            {
                DateTime now = Clock();
                if ((now - lastAttemptCleanup).TotalSeconds >= Settings.Lockout)
                {
                    foreach (string expired in failures.Where(f => (now - f.Value.Start).TotalSeconds >= Settings.Lockout).Select(f => f.Key).ToArray())
                        failures.Remove(expired);
                    lastAttemptCleanup = now;
                }
                if (!failures.TryGetValue(key, out var entry) || (now - entry.Start).TotalSeconds >= Settings.Lockout) entry = (now, 0);
                failures[key] = (entry.Start, entry.Count + 1);
            }
        }

        internal void Succeeded(string key)
        {
            lock (failures) failures.Remove(key);
        }

        #endregion
    }
}
