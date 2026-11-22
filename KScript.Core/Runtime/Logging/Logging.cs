using System.Globalization;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>How serious a log entry is. A provider writes entries at its level and above; Off writes nothing.</summary>
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warn,
        Error,
        Fatal,
        Off,
    }

    public static class LogLevels
    {
        public static readonly string[] Names = { "trace", "debug", "info", "warn", "error", "fatal", "off" };

        public static string Name(LogLevel level) => Names[(int)level];

        /// <summary>A level by name: trace, debug, info, warn (or warning), error, fatal or off.</summary>
        public static LogLevel Parse(object value, string where)
        {
            string name = Values.ToStr(value)?.Trim().ToLowerInvariant();
            if (name == "warning") name = "warn";
            int index = Array.IndexOf(Names, name);
            if (index < 0) throw new KScriptException($"{where}: '{Values.ToStr(value)}' isn't a log level. Use trace, debug, info, warn, error, fatal or off.", "Argument");
            return (LogLevel)index;
        }
    }

    /// <summary>One thing a script logged.</summary>
    public sealed class LogEntry
    {
        public DateTimeOffset Time { get; init; }
        public LogLevel Level { get; init; }

        /// <summary>Where it came from, such as db or http.client; null for the script itself.</summary>
        public string Category { get; init; }

        public string Message { get; init; }

        /// <summary>Extra values, such as { user: 42 }.</summary>
        public Dictionary<string, object> Fields { get; init; } = new Dictionary<string, object>();

        /// <summary>The script's file name, and the line of the &lt;log&gt; element when there is one.</summary>
        public string Script { get; init; }

        public int? Line { get; init; }

        /// <summary>The entry as a map: time, level, category, message, fields, script, line.</summary>
        public Dictionary<string, object> ToMap()
        {
            var map = new Dictionary<string, object>
            {
                ["time"] = Time.ToString("o", CultureInfo.InvariantCulture),
                ["level"] = LogLevels.Name(Level),
                ["category"] = Category,
                ["message"] = Message,
                ["fields"] = Fields,
                ["script"] = Script,
                ["line"] = Line.HasValue ? (object)(double)Line.Value : null,
            };
            return map;
        }

        /// <summary>One line of JSON: time, level, message, then category, script, line and the fields.</summary>
        public string ToJson()
        {
            var map = new Dictionary<string, object>
            {
                ["time"] = Time.ToString("o", CultureInfo.InvariantCulture),
                ["level"] = LogLevels.Name(Level),
                ["message"] = Message,
            };
            if (Category != null) map["category"] = Category;
            if (Script != null) map["script"] = Script;
            if (Line.HasValue) map["line"] = (double)Line.Value;
            foreach (var kv in Fields) map.TryAdd(kv.Key, kv.Value);
            return Values.ToJson(map);
        }

        /// <summary>One line of text: <c>2026-11-20T09:52:02.000+00:00 INFO  [db] Connected host=db.internal</c>.</summary>
        public string ToText(string timeFormat = "o")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(timeFormat)) sb.Append(Time.ToString(timeFormat, CultureInfo.InvariantCulture)).Append(' ');
            sb.Append(LogLevels.Name(Level).ToUpperInvariant().PadRight(5)).Append(' ');
            if (Category != null) sb.Append('[').Append(Category).Append("] ");
            sb.Append(Message);
            foreach (var kv in Fields)
            {
                string value = kv.Value is string s ? s : Values.ToJson(kv.Value);
                sb.Append(' ').Append(kv.Key).Append('=').Append(value.Length == 0 || value.Any(char.IsWhiteSpace) ? Values.ToJson(value) : value);
            }
            return sb.ToString();
        }
    }

    /// <summary>Somewhere log entries go: the console, a file, a script function, a web service...</summary>
    public interface ILogProvider
    {
        void Write(LogEntry entry);

        /// <summary>Writes anything held back. Called at the end of a run and by $log->flush().</summary>
        void Flush();
    }

    /// <summary>What a provider is made from: its options (from config or &lt;log-provider&gt;) and the script.</summary>
    public sealed class LogProviderContext
    {
        public Dictionary<string, object> Options { get; init; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public Interpreter Interpreter { get; init; }
        public Module Module { get; init; }
        public KScriptHost Host => Interpreter.Host;

        /// <summary>The folder relative paths start in.</summary>
        public string Directory => Module?.Directory ?? Host.WorkingDirectory;

        /// <summary>How errors name this provider, such as "logging.providers[1]".</summary>
        public string Where { get; init; } = "the log provider";

        public object Get(string key) => Options.TryGetValue(key, out object v) ? v : null;

        public string Str(string key, string fallback = null) => Get(key) is object v ? Values.ToStr(v) : fallback;

        public string Need(string key) => Str(key) ?? throw new KScriptException($"{Where} needs a {key}.", "Config");
    }

    /// <summary>A kind of log provider a package adds, such as http in KScript.Net.</summary>
    public sealed class LogProviderType
    {
        public LogProviderType(string type, string description, Func<LogProviderContext, ILogProvider> create, string package)
        {
            Type = type;
            Description = description;
            Create = create;
            Package = package;
        }

        public string Type { get; }
        public string Description { get; }
        public Func<LogProviderContext, ILogProvider> Create { get; }
        public string Package { get; }
    }

    /// <summary>
    /// A run's logging: its providers and levels. It sets itself up from <c>$config.logging</c> the first time
    /// something is logged; with no providers configured, entries at info and above go to the console (stderr).
    /// </summary>
    public sealed class LogHub
    {
        private readonly KScriptHost host;
        private readonly object gate = new object();
        private readonly List<Registered> providers = new List<Registered>();
        private readonly Dictionary<string, LogLevel> categoryLevels = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);
        private bool configured;

        [ThreadStatic]
        private static int writing;

        private sealed class Registered
        {
            public string Name;
            public ILogProvider Provider;
            public LogLevel Level;
            public bool Failed;
        }

        internal LogHub(KScriptHost host) => this.host = host;

        /// <summary>The lowest level written anywhere (logging.level). Default: info.</summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>The clock entries are stamped with. Tests set it.</summary>
        public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

        /// <summary>The built-in kinds of provider: console, file and memory.</summary>
        public static readonly IReadOnlyDictionary<string, LogProviderType> BuiltIn = new Dictionary<string, LogProviderType>(StringComparer.OrdinalIgnoreCase)
        {
            ["console"] = new LogProviderType("console", "Writes to the terminal (stderr unless stream: stdout), as text or json.", c => new ConsoleLogProvider(c), "KScript.Core"),
            ["file"] = new LogProviderType("file", "Appends to a file (path), as text or json lines. roll: daily starts a new file each day.", c => new FileLogProvider(c), "KScript.Core"),
            ["memory"] = new LogProviderType("memory", "Keeps entries for $log->entries(), for tests.", _ => new MemoryLogProvider(), "KScript.Core"),
        };

        /// <summary>Adds a provider that receives entries at <paramref name="level"/> and above.</summary>
        public void AddProvider(ILogProvider provider, LogLevel level = LogLevel.Trace, string name = null)
        {
            lock (gate)
            {
                configured = true;
                providers.Add(new Registered { Name = name ?? provider.GetType().Name, Provider = provider, Level = level });
            }
        }

        /// <summary>Removes every provider (including the default console one).</summary>
        public void ClearProviders()
        {
            lock (gate)
            {
                configured = true;
                providers.Clear();
            }
        }

        public IReadOnlyList<ILogProvider> Providers
        {
            get { lock (gate) return providers.Select(p => p.Provider).ToList(); }
        }

        /// <summary>Sets a category's lowest level (logging.levels): db: debug also covers db.queries.</summary>
        public void SetCategoryLevel(string category, LogLevel level)
        {
            lock (gate) categoryLevels[category] = level;
        }

        /// <summary>The lowest level written for a category: the longest matching logging.levels entry, else logging.level.</summary>
        public LogLevel LevelFor(string category)
        {
            lock (gate)
            {
                if (category != null)
                {
                    for (string c = category; c.Length > 0; c = c.Contains('.') ? c.Substring(0, c.LastIndexOf('.')) : string.Empty)
                    {
                        if (categoryLevels.TryGetValue(c, out LogLevel level)) return level;
                    }
                }
                return Level;
            }
        }

        public bool IsEnabled(LogLevel level, string category) => level != LogLevel.Off && level >= LevelFor(category);

        /// <summary>Sets logging up from $config.logging, once, before the first entry.</summary>
        internal void EnsureConfigured(Interpreter interpreter, Module module)
        {
            lock (gate)
            {
                if (configured) return;
                configured = true;
            }
            ConfigValue config = ConfigValue.For(host, module?.Directory ?? host.WorkingDirectory);
            config.Values.TryGetValue("logging", out object logging);
            Configure(logging, interpreter, module);
        }

        /// <summary>
        /// Applies a logging section: <c>{ level, levels: { category: level }, providers: [{ type, level, ... }] }</c>.
        /// Without providers, entries go to the console.
        /// </summary>
        public void Configure(object section, Interpreter interpreter, Module module)
        {
            var map = section as Dictionary<string, object>;
            if (section != null && map == null)
                throw new KScriptException($"logging should be a map, such as {{ level: 'debug' }}, but is {Values.Describe(section)}.", "Config");
            lock (gate) configured = true;
            if (map != null && map.TryGetValue("level", out object level) && level != null) Level = LogLevels.Parse(level, "logging.level");
            if (map != null && map.TryGetValue("levels", out object levels) && levels != null)
            {
                if (levels is not Dictionary<string, object> byCategory)
                    throw new KScriptException("logging.levels should be a map of category to level, such as { db: 'debug' }.", "Config");
                foreach (var kv in byCategory) SetCategoryLevel(kv.Key, LogLevels.Parse(kv.Value, $"logging.levels.{kv.Key}"));
            }

            List<object> list = null;
            if (map != null && map.TryGetValue("providers", out object configuredProviders) && configuredProviders != null)
            {
                list = configuredProviders as List<object>
                    ?? throw new KScriptException("logging.providers should be a list, such as [{ type: 'console' }, { type: 'file', path: 'app.log' }].", "Config");
            }
            if (list == null)
            {
                Add(new Dictionary<string, object> { ["type"] = "console" }, interpreter, module, "the console log");
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not Dictionary<string, object> options)
                    throw new KScriptException($"logging.providers[{i}] should be a map, such as {{ type: 'file', path: 'app.log' }}.", "Config");
                Add(options, interpreter, module, $"logging.providers[{i}]");
            }
        }

        /// <summary>Creates and adds a provider from options: type (or fn), level, and the type's own options.</summary>
        public ILogProvider Add(Dictionary<string, object> options, Interpreter interpreter, Module module, string where)
        {
            var copy = new Dictionary<string, object>(options, StringComparer.OrdinalIgnoreCase);
            var context = new LogProviderContext { Options = copy, Interpreter = interpreter, Module = module, Where = where };
            string type = context.Str("type") ?? (context.Get("fn") != null ? "fn" : throw new KScriptException($"{where} needs a type, such as console, file or memory (or fn, a function).", "Config"));
            LogLevel level = context.Get("level") is object l ? LogLevels.Parse(l, where + " level") : LogLevel.Trace;

            ILogProvider provider;
            if (type.Equals("fn", StringComparison.OrdinalIgnoreCase))
            {
                provider = new FunctionLogProvider(context);
            }
            else if (BuiltIn.TryGetValue(type, out LogProviderType builtIn))
            {
                provider = builtIn.Create(context);
            }
            else
            {
                LogProviderType fromPackage = module?.Packages.SelectMany(p => p.LogProviders.Values).FirstOrDefault(t => t.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
                if (fromPackage == null)
                {
                    IEnumerable<string> known = BuiltIn.Keys.Concat(module?.Packages.SelectMany(p => p.LogProviders.Keys) ?? Enumerable.Empty<string>()).Append("fn");
                    throw new KScriptException($"{where}: there is no log provider type '{type}'. Types: {string.Join(", ", known)}. Packages add others when imported (KScript.Net adds http).", "Config");
                }
                provider = fromPackage.Create(context);
            }
            AddProvider(provider, level, context.Str("name") ?? type);
            return provider;
        }

        /// <summary>Sends an entry to every provider whose level it reaches. A failing provider is reported once and skipped.</summary>
        public void Write(LogEntry entry)
        {
            if (!IsEnabled(entry.Level, entry.Category)) return;
            // A provider that logs (a script function calling $log) doesn't loop forever.
            if (writing > 0) return;
            List<Registered> targets;
            lock (gate) targets = providers.Where(p => entry.Level >= p.Level).ToList();
            writing++;
            try
            {
                foreach (Registered p in targets)
                {
                    try
                    {
                        p.Provider.Write(entry);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not ExitSignal)
                    {
                        Report(p, ex);
                    }
                }
            }
            finally
            {
                writing--;
            }
        }

        public void Flush()
        {
            List<Registered> targets;
            lock (gate) targets = providers.ToList();
            foreach (Registered p in targets)
            {
                try
                {
                    p.Provider.Flush();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Report(p, ex);
                }
            }
        }

        private void Report(Registered provider, Exception ex)
        {
            if (provider.Failed) return;
            provider.Failed = true;
            ErrorReport.Write(host.Error, $"Log provider '{provider.Name}' failed.", ex);
        }

        /// <summary>The entries every memory provider kept.</summary>
        public List<LogEntry> Entries()
        {
            lock (gate) return providers.Select(p => p.Provider).OfType<MemoryLogProvider>().SelectMany(m => m.Entries).OrderBy(e => e.Time).ToList();
        }
    }

    /// <summary>Writes to the terminal: stderr by default, as text (with colours at a terminal) or json.</summary>
    public sealed class ConsoleLogProvider : ILogProvider
    {
        private readonly TextWriter writer;
        private readonly bool json;
        private readonly bool colour;
        private readonly string timeFormat;

        public ConsoleLogProvider(LogProviderContext c)
        {
            string stream = c.Str("stream", "stderr").ToLowerInvariant();
            writer = stream switch
            {
                "stdout" => c.Host.Out,
                "stderr" => c.Host.Error,
                _ => throw new KScriptException($"{c.Where}: stream should be stdout or stderr.", "Config"),
            };
            json = Format(c) == "json";
            colour = c.Get("colour") is object v ? Flag(v) : c.Host.IsInteractive;
            timeFormat = c.Str("time", "HH:mm:ss");
        }

        /// <summary>yes/no, true/false or on/off, from config or an attribute.</summary>
        internal static bool Flag(object value) => value is string s ? !(s.Trim().ToLowerInvariant() is "no" or "false" or "off" or "0" or "") : Values.Truthy(value);

        internal static string Format(LogProviderContext c)
        {
            string format = c.Str("format", "text").ToLowerInvariant();
            if (format != "text" && format != "json") throw new KScriptException($"{c.Where}: format should be text or json.", "Config");
            return format;
        }

        public void Write(LogEntry entry)
        {
            string line = json ? entry.ToJson() : entry.ToText(timeFormat);
            if (colour && !json)
            {
                string code = entry.Level switch
                {
                    LogLevel.Trace or LogLevel.Debug => "90",
                    LogLevel.Info => "36",
                    LogLevel.Warn => "33",
                    _ => "31",
                };
                line = $"\u001b[{code}m{line}\u001b[0m";
            }
            writer.WriteLine(line);
        }

        public void Flush() => writer.Flush();
    }

    /// <summary>Appends entries to a file, as text or json lines. roll: daily writes app-2026-11-20.log and so on.</summary>
    public sealed class FileLogProvider : ILogProvider
    {
        private readonly string path;
        private readonly bool json;
        private readonly bool daily;
        private readonly object gate = new object();

        public FileLogProvider(LogProviderContext c)
        {
            c.Host.CheckWrite("Logging to a file");
            path = c.Host.ResolvePath(c.Need("path"), c.Directory);
            json = ConsoleLogProvider.Format(c) == "json";
            string roll = c.Str("roll", "never").ToLowerInvariant();
            if (roll != "never" && roll != "daily") throw new KScriptException($"{c.Where}: roll should be never or daily.", "Config");
            daily = roll == "daily";
        }

        /// <summary>The file an entry goes to: the path, or with roll: daily the path with the entry's date.</summary>
        public string PathFor(DateTimeOffset time) => daily
            ? Path.Combine(Path.GetDirectoryName(path), $"{Path.GetFileNameWithoutExtension(path)}-{time:yyyy-MM-dd}{Path.GetExtension(path)}")
            : path;

        public void Write(LogEntry entry)
        {
            string file = PathFor(entry.Time);
            string line = (json ? entry.ToJson() : entry.ToText()) + "\n";
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.AppendAllText(file, line);
            }
        }

        public void Flush()
        {
        }
    }

    /// <summary>Keeps entries in memory, for $log->entries() (in tests, for example).</summary>
    public sealed class MemoryLogProvider : ILogProvider
    {
        private readonly List<LogEntry> entries = new List<LogEntry>();

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (entries) return entries.ToList(); }
        }

        public void Write(LogEntry entry)
        {
            lock (entries) entries.Add(entry);
        }

        public void Flush()
        {
        }
    }

    /// <summary>Calls a script function with each entry (a map of time, level, category, message, fields, script and line).</summary>
    public sealed class FunctionLogProvider : ILogProvider
    {
        private readonly ICallable function;
        private readonly LogProviderContext context;

        public FunctionLogProvider(LogProviderContext c)
        {
            context = c;
            object fn = c.Get("fn");
            if (fn is string name)
            {
                string clean = name.Trim().TrimStart('@', '$');
                if (c.Module?.Globals == null || !c.Module.Globals.TryGet(clean, out fn))
                    throw new KScriptException($"{c.Where}: there is no function called {clean}. Declare it with <fn name=\"{clean}\" params=\"entry\" ...>.", "Config");
            }
            function = fn as ICallable ?? throw new KScriptException($"{c.Where}: fn should be a function, such as fn($entry) => ..., but got {Values.Describe(fn)}.", "Config");
        }

        public void Write(LogEntry entry) =>
            context.Interpreter.Call(function, new object[] { entry.ToMap() }, null, context.Module.Globals, context.Module);

        public void Flush()
        {
        }
    }

    /// <summary>
    /// <c>$log</c>: <c>$log->info('Saved', { id: $id })</c>, <c>$log->named('db')->debug(...)</c>,
    /// <c>$log->with({ request: $id })</c>. Each logger adds its category and fields to what it writes.
    /// </summary>
    public sealed class LoggerValue : IKObject
    {
        public LoggerValue(Interpreter interpreter, Module module, string category = null, Dictionary<string, object> fields = null)
        {
            Interpreter = interpreter;
            Module = module;
            Category = category;
            Fields = fields ?? new Dictionary<string, object>();
        }

        public Interpreter Interpreter { get; }
        public Module Module { get; }
        public string Category { get; }
        public Dictionary<string, object> Fields { get; }

        public string TypeName => KType.Logger;

        public string ToText() => Category == null ? "logger" : $"logger ({Category})";

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "category" => Category,
                "fields" => Fields,
                _ => null,
            };
            return name == "category" || name == "fields";
        }

        public LogHub Hub
        {
            get
            {
                LogHub hub = Interpreter.Host.Logs;
                hub.EnsureConfigured(Interpreter, Module);
                return hub;
            }
        }

        /// <summary>Logs a message with this logger's category and fields, plus <paramref name="extra"/>.</summary>
        public void Log(LogLevel level, string message, object extra, int? line = null)
        {
            LogHub hub = Hub;
            if (!hub.IsEnabled(level, Category)) return;
            var fields = new Dictionary<string, object>(Fields);
            switch (extra)
            {
                case null:
                    break;
                case Dictionary<string, object> err when IsError(err):
                    // An error from <catch as="err">: its message, type and line, without clashing with the entry's own message.
                    fields["error"] = err["message"];
                    fields["error_type"] = err["type"];
                    if (err["line"] != null) fields["error_line"] = err["line"];
                    break;
                case Dictionary<string, object> map:
                    foreach (var kv in map) fields[kv.Key] = kv.Value;
                    break;
                default:
                    fields["value"] = extra is IKObject o ? o.ToText() : extra;
                    break;
            }
            hub.Write(new LogEntry
            {
                Time = hub.Clock(),
                Level = level,
                Category = Category,
                Message = message,
                Fields = fields,
                Script = Module?.DisplayName,
                Line = line,
            });
        }

        private static bool IsError(Dictionary<string, object> map) =>
            map.ContainsKey("type") && map.ContainsKey("message") && map.ContainsKey("line") && map.ContainsKey("column") && map.ContainsKey("file");

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            switch (name)
            {
                case "trace": case "debug": case "info": case "warn": case "error": case "fatal":
                    Log(LogLevels.Parse(name, "$log"), Values.ToStr(call.Need(0)), call.Arg(1, "fields"));
                    return true;
                case "log":
                    Log(LogLevels.Parse(call.Need(0), "$log->log() level"), Values.ToStr(call.Need(1)), call.Arg(2, "fields"));
                    return true;
                case "with":
                {
                    var fields = new Dictionary<string, object>(Fields);
                    foreach (var kv in call.Map(0)) fields[kv.Key] = kv.Value;
                    result = new LoggerValue(Interpreter, Module, Category, fields);
                    return true;
                }
                case "named":
                {
                    string category = call.Str(0);
                    result = new LoggerValue(Interpreter, Module, Category == null ? category : Category + "." + category, Fields);
                    return true;
                }
                case "is_enabled":
                    result = Hub.IsEnabled(LogLevels.Parse(call.Need(0), "$log->is_enabled()"), Category);
                    return true;
                case "entries":
                    result = Hub.Entries().Select(e => (object)e.ToMap()).ToList();
                    return true;
                case "flush":
                    Hub.Flush();
                    return true;
                default:
                    return false;
            }
        }
    }
}
