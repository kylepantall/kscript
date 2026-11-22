using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Metadata;
using KScript.Runtime;
using KScript.Storage;

namespace KScript.Caching
{
    /// <summary>
    /// A cache: values by key, each kept until its time limit (ttl) runs out. The memory store lives as long as the
    /// process (so it lasts between requests in kscript serve); the file store keeps values in the app's cache folder,
    /// so they last between runs.
    /// </summary>
    public abstract class CacheStore : IKObject
    {
        /// <summary>The clock time limits are measured with. Tests replace it.</summary>
        internal static Func<DateTime> Clock = () => DateTime.UtcNow;

        protected CacheStore(string name) => Name = name;

        /// <summary>The store's name, which keeps its keys apart from other stores'.</summary>
        public string Name { get; }

        /// <summary>memory or file.</summary>
        public abstract string Kind { get; }

        public string TypeName => KType.Cache;

        public string ToText() => $"{Kind} cache '{Name}'";

        public override string ToString() => ToText();

        /// <summary>The value and when it runs out (null: never), if the key is there and hasn't run out.</summary>
        public abstract bool TryGet(string key, out object value, out DateTime? expires);

        public abstract void Put(string key, object value, DateTime? expires);

        public abstract bool Forget(string key);

        public abstract int Flush();

        public abstract IEnumerable<string> Keys();

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "name" => Name,
                "kind" => Kind,
                _ => null,
            };
            return value != null;
        }

        /// <summary>Methods go through the package, so their arguments are bound by name first (default:, ttl:, recursive).</summary>
        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        /// <summary>Runs a method with its arguments already bound to their places.</summary>
        internal bool Invoke(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "get":
                    result = TryGet(Key(call), out object found, out _) ? found : call.Arg(1);
                    return true;
                case "has":
                    result = TryGet(Key(call), out _, out _);
                    return true;
                case "set":
                    Put(Key(call), call.Arg(1), Expires(call.Arg(2)));
                    result = call.Arg(1);
                    return true;
                case "remember":
                {
                    string key = Key(call);
                    if (TryGet(key, out object kept, out _))
                    {
                        result = kept;
                        return true;
                    }
                    ICallable compute = call.Fn(1);
                    DateTime? expires = Expires(call.Arg(2));
                    result = call.Call(compute);
                    Put(key, result, expires);
                    return true;
                }
                case "remove":
                    result = Forget(Key(call));
                    return true;
                case "increment":
                {
                    string key = Key(call);
                    double by = call.Num(1, 1);
                    double now = TryGet(key, out object current, out DateTime? expires) && current != null ? Values.ToNumber(current, $"The value of '{key}'") : 0;
                    // The count keeps the time limit it started with, so throttling-style counters still run out.
                    DateTime? until = call.Has(2) && call.Arg(2) != null ? Expires(call.Arg(2)) : expires;
                    Put(key, now + by, until);
                    result = now + by;
                    return true;
                }
                case "ttl":
                    result = TryGet(Key(call), out _, out DateTime? ends) && ends != null ? Duration.Of(Math.Max(0, Math.Ceiling((ends.Value - Clock()).TotalSeconds))) : null;
                    return true;
                case "keys":
                    result = Keys().OrderBy(k => k, StringComparer.Ordinal).Cast<object>().ToList();
                    return true;
                case "clear":
                    result = (double)Flush();
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        private static string Key(CallContext call)
        {
            string key = call.Str(0);
            if (string.IsNullOrEmpty(key)) throw call.Error("A cache key can't be empty.", "Argument");
            return key;
        }

        private static DateTime? Expires(object ttl)
        {
            double? seconds = CachePackage.Seconds(ttl);
            return seconds == null ? null : Clock().AddSeconds(seconds.Value);
        }

        /// <summary>A copy of maps and lists, so changing a value after putting it (or after getting it) doesn't change the cache.</summary>
        internal static object Copy(object value) => value switch
        {
            Dictionary<string, object> map => map.ToDictionary(kv => kv.Key, kv => Copy(kv.Value), map.Comparer),
            List<object> list => list.Select(Copy).ToList(),
            _ => value,
        };

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("name", KType.String, "The store's name, which keeps its keys apart from other stores'."),
            ("kind", KType.String, "Where the values are kept: memory or file."),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("get", "key, default?", "The value kept under a key, or default (null) when it isn't there or has run out.", KType.Any),
            ("has", "key", "True when a value is kept under the key and hasn't run out.", KType.Bool),
            ("set", "key, value, ttl?: duration", "Keeps a value under a key. ttl is how long, such as 30s, 10m, 2h or 1d (a plain number is seconds). Without it the value is kept until it is removed.", KType.Any),
            ("remember", "key, compute: fn, ttl?: duration", "The value under a key; when there isn't one, calls compute, keeps what it returns and returns that.", KType.Any),
            ("remove", "key", "Removes a key. True when it was there.", KType.Bool),
            ("increment", "key, by?: number, ttl?: duration", "Adds to the number under a key (1 by default; 0 when it isn't there) and returns the new number.", KType.Number),
            ("ttl", "key", "How long until a key runs out, as a duration, or null when it never does or isn't there.", "duration"),
            ("keys", "", "The keys that are kept and haven't run out.", KType.List),
            ("clear", "", "Removes every key. Returns how many there were.", KType.Number),
        };
    }

    /// <summary>A cache in memory, shared by every run in this process.</summary>
    public sealed class MemoryCacheStore : CacheStore
    {
        private static readonly ConcurrentDictionary<string, MemoryCacheStore> Stores = new ConcurrentDictionary<string, MemoryCacheStore>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, (object Value, DateTime? Expires)> entries = new ConcurrentDictionary<string, (object, DateTime?)>(StringComparer.Ordinal);

        private MemoryCacheStore(string name) : base(name)
        {
        }

        /// <summary>The memory store with this name (one per name for the whole process).</summary>
        public static MemoryCacheStore Named(string name) => Stores.GetOrAdd(name, n => new MemoryCacheStore(n));

        /// <summary>Most entries one store keeps; past it, entries that ran out go first, then the ones that end soonest.</summary>
        internal const int MaxEntries = 10_000;

        public override string Kind => "memory";

        public override bool TryGet(string key, out object value, out DateTime? expires)
        {
            if (entries.TryGetValue(key, out var entry))
            {
                if (entry.Expires == null || entry.Expires > Clock())
                {
                    value = Copy(entry.Value);
                    expires = entry.Expires;
                    return true;
                }
                entries.TryRemove(key, out _);
            }
            value = null;
            expires = null;
            return false;
        }

        public override void Put(string key, object value, DateTime? expires)
        {
            entries[key] = (Copy(value), expires);
            if (entries.Count <= MaxEntries) return;
            DateTime now = Clock();
            foreach (var old in entries.Where(e => e.Value.Expires <= now).ToList()) entries.TryRemove(old.Key, out _);
            foreach (var old in entries.OrderBy(e => e.Value.Expires ?? DateTime.MaxValue).Take(entries.Count - MaxEntries).ToList()) entries.TryRemove(old.Key, out _);
        }

        public override bool Forget(string key) => entries.TryRemove(key, out _);

        public override int Flush()
        {
            int count = entries.Count;
            entries.Clear();
            return count;
        }

        public override IEnumerable<string> Keys()
        {
            DateTime now = Clock();
            return entries.Where(e => e.Value.Expires == null || e.Value.Expires > now).Select(e => e.Key).ToList();
        }
    }

    /// <summary>A cache in files, one per key, in the app's cache folder: values last between runs.</summary>
    public sealed class FileCacheStore : CacheStore
    {
        public FileCacheStore(string name, string folder) : base(name) => Folder = folder;

        /// <summary>Where the files are.</summary>
        public string Folder { get; }

        public override string Kind => "file";

        private string FileFor(string key) =>
            Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 32).ToLowerInvariant() + ".json");

        public override bool TryGet(string key, out object value, out DateTime? expires)
        {
            value = null;
            expires = null;
            string file = FileFor(key);
            if (!Read(file, out var entry) || entry.Key != key) return false;
            if (entry.Expires != null && entry.Expires <= Clock())
            {
                File.Delete(file);
                return false;
            }
            value = entry.Value;
            expires = entry.Expires;
            return true;
        }

        /// <summary>A cache file's key, value and end. A file that can't be read (half written, or not a cache file) counts as missing.</summary>
        private static bool Read(string file, out (string Key, object Value, DateTime? Expires) entry)
        {
            entry = default;
            if (!File.Exists(file)) return false;
            try
            {
                if (Values.ParseJson(File.ReadAllText(file)) is not Dictionary<string, object> map || map.GetValueOrDefault("key") is not string key) return false;
                DateTime? expires = map.GetValueOrDefault("expires") is double seconds ? DateTime.UnixEpoch.AddSeconds(seconds) : null;
                entry = (key, map.GetValueOrDefault("value"), expires);
                return true;
            }
            catch (KScriptException)
            {
                return false;
            }
        }

        public override void Put(string key, object value, DateTime? expires)
        {
            Directory.CreateDirectory(Folder);
            var map = new Dictionary<string, object>
            {
                ["key"] = key,
                ["expires"] = expires == null ? null : (object)(expires.Value - DateTime.UnixEpoch).TotalSeconds,
                ["value"] = value,
            };
            string file = FileFor(key);
            string temp = file + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(temp, Values.ToJson(map));
            File.Move(temp, file, overwrite: true);
        }

        public override bool Forget(string key)
        {
            string file = FileFor(key);
            if (!Read(file, out var entry) || entry.Key != key) return false;
            File.Delete(file);
            return true;
        }

        public override int Flush()
        {
            if (!Directory.Exists(Folder)) return 0;
            string[] files = Directory.GetFiles(Folder, "*.json");
            foreach (string file in files) File.Delete(file);
            return files.Length;
        }

        public override IEnumerable<string> Keys()
        {
            if (!Directory.Exists(Folder)) return Array.Empty<string>();
            DateTime now = Clock();
            var keys = new List<string>();
            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                if (Read(file, out var entry) && (entry.Expires == null || entry.Expires > now)) keys.Add(entry.Key);
            }
            return keys;
        }
    }

    /// <summary>KScript.Cache: <c>$cache</c> (in memory) and <c>@cache(file)</c> (in the app's cache folder).</summary>
    public sealed class CachePackage : KScriptPackage
    {
        public override string Name => "KScript.Cache";

        public override string Description => "A cache of values by key, with time limits: in memory, or in files in the app's cache folder.";

        /// <summary>
        /// A time limit in seconds: a duration (30s, 10m, 2h, 1d), text that reads as one, or a plain number of
        /// seconds. Null (no limit) for null. Throws for anything else, and for a limit that isn't more than 0.
        /// </summary>
        public static double? Seconds(object ttl)
        {
            if (ttl == null) return null;
            if (ttl is double n && (double.IsNaN(n) || double.IsInfinity(n))) throw new KScriptException($"A time limit must be more than 0 seconds, not {Values.ToStr(ttl)}.", "Argument");
            double seconds = Duration.Seconds(ttl, "A time limit (ttl)");
            if (seconds <= 0) throw new KScriptException($"A time limit must be more than 0 seconds, not {Values.ToStr(ttl)}.", "Argument");
            return seconds;
        }

        /// <summary>The store a script asks for: memory (the default) or file, with an optional name.</summary>
        internal static CacheStore Store(CallContext c, int kindArgument, int nameArgument)
        {
            string kind = c.Has(kindArgument) && c.Arg(kindArgument) != null ? c.Str(kindArgument).Trim().ToLowerInvariant() : "memory";
            string app = StoragePackage.AppName(c, 99);
            string name = c.Has(nameArgument) && c.Arg(nameArgument) != null ? AppFolders.CleanName(c.Str(nameArgument)) : "default";
            // The store's type (memory|file) means the interpreter has already refused any other name.
            return kind == "file"
                ? new FileCacheStore(name, Path.Combine(AppFolders.Folder(app, "cache"), "kscript-cache", name))
                : MemoryCacheStore.Named(app + "/" + name);
        }

        protected override void Register(PackageBuilder p)
        {
            foreach (var f in CacheStore.Fields)
                p.Field(KType.Cache, f.Name, f.Type, f.Description);
            foreach (var m in CacheStore.Methods)
                p.Method(KType.Cache, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((CacheStore)t).Invoke(c, m.Name, out object r) ? r : null);

            p.Variable("cache", KType.Cache,
                "The app's cache in memory: $cache->remember('repos', fn() => @get_json($url), ttl: 10m). It lasts while kscript runs, so in kscript serve it is shared by every request. @cache(file) keeps values between runs.",
                (i, m) => MemoryCacheStore.Named(AppFolders.CleanName(AppFolders.AppNameFor(m?.Directory ?? i.Host.WorkingDirectory, m?.Path)) + "/default"));

            p.Command("cache", "store?: memory|file, name?",
                "A cache store: memory (the default, like $cache) or file, which keeps values in files in the app's cache folder so they last between runs. A name keeps a store's keys apart from other stores'.",
                KType.Cache, c => Store(c, 0, 1),
                "<let name=\"weather\" value=\"@cache(file, name: 'weather')\" />\n<echo>{ $weather->remember('today', fn() => 'sunny', ttl: 1h) }</echo>");

            p.Examples(
                ("cache->get", "<do>$cache->set('greeting', 'hello')</do>\n<echo>{ $cache->get('greeting') } { $cache->get('missing', default: 'none') }</echo>"),
                ("cache->has", "<do>$cache->set('token', 'abc', ttl: 5m)</do>\n<echo>{ $cache->has('token') }</echo>"),
                ("cache->set", "<do>$cache->set('user:1', { name: 'Joan' }, ttl: 600)</do>"),
                ("cache->remember", "<let name=\"squares\" value=\"$cache->remember('squares', fn() => [1, 2, 3]->map(fn($n) => $n * $n), ttl: 10m)\" />"),
                ("cache->remove", "<do>$cache->set('draft', 'hi'); $cache->remove('draft')</do>\n<echo>{ $cache->has('draft') }</echo>"),
                ("cache->increment", "<do>$cache->increment('visits'); $cache->increment('visits', by: 2)</do>\n<echo>{ $cache->get('visits') }</echo>"),
                ("cache->ttl", "<do>$cache->set('code', 1234, ttl: 1m)</do>\n<echo>{ $cache->ttl('code') } left</echo>"),
                ("cache->keys", "<do>$cache->set('a', 1); $cache->set('b', 2)</do>\n<echo>{ $cache->keys() }</echo>"),
                ("cache->clear", "<echo>{ $cache->clear() } keys removed</echo>"));
        }
    }
}
