using System.Reflection;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// Runs one element. The return value only matters for elements that start or continue a chain: true when a
    /// branch ran, false when it didn't (so a following &lt;elif&gt; or &lt;else&gt; may run), and null otherwise.
    /// </summary>
    public delegate bool? ElementHandler(ElementRun run);

    public delegate object CommandHandler(CallContext call);

    public delegate object MethodHandler(object target, CallContext call);

    /// <summary>Formats the text of <c>&lt;echo format="name"&gt;</c>, for example Markdown for a terminal.</summary>
    public delegate string EchoFormatter(string text, Interpreter interpreter);

    public sealed class ElementDefinition
    {
        internal ElementDefinition(ElementInfo info, ElementHandler handler, KScriptPackage package)
        {
            Info = info;
            Handler = handler;
            Package = package;
        }

        public string Name => Info.Name;
        public ElementInfo Info { get; }

        /// <summary>Null for elements that only work somewhere else, such as page elements served by KScript.Web.</summary>
        public ElementHandler Handler { get; }

        public KScriptPackage Package { get; }

        /// <summary>
        /// For elements whose content is <see cref="ContentKind.Markup"/> (a window, a page block): true if the
        /// content may hold any element, which the package interprets itself. The interpreter then doesn't check it.
        /// </summary>
        public bool OwnsContent { get; init; }
    }

    public sealed class CommandDefinition
    {
        internal CommandDefinition(CallableInfo info, CommandHandler handler, KScriptPackage package)
        {
            Info = info;
            Handler = handler;
            Package = package;
        }

        public string Name => Info.Name;
        public CallableInfo Info { get; }
        public CommandHandler Handler { get; }
        public KScriptPackage Package { get; }
    }

    public sealed class MethodDefinition
    {
        internal MethodDefinition(CallableInfo info, MethodHandler handler, KScriptPackage package)
        {
            Info = info;
            Handler = handler;
            Package = package;
        }

        public string Name => Info.Name;

        /// <summary>The type the method is for: string, number, list, map, date, fn, any, or a package type such as file.</summary>
        public string Receiver => Info.ReceiverType;

        public CallableInfo Info { get; }
        public MethodHandler Handler { get; }
        public KScriptPackage Package { get; }
    }

    /// <summary>A variable that exists without being declared, such as <c>$args</c>.</summary>
    public sealed class VariableDefinition
    {
        internal VariableDefinition(string name, string type, string description, Func<Interpreter, Module, object> value, KScriptPackage package)
        {
            Name = name;
            Type = type;
            Description = description;
            Value = value;
            Package = package;
        }

        public string Name { get; }
        public string Type { get; }
        public string Description { get; }
        public Func<Interpreter, Module, object> Value { get; }
        public KScriptPackage Package { get; }
    }

    /// <summary>
    /// A package of elements, commands, methods and variables, such as KScript.Terminal or KScript.Math. Scripts
    /// only see the packages they import (KScript.Core is always there), and a package's assembly is only loaded
    /// when a script imports it, so a terminal script never loads the UI or web server code.
    /// </summary>
    public abstract class KScriptPackage
    {
        private readonly object gate = new object();
        private bool registered;
        private readonly Dictionary<string, ElementDefinition> elements = new Dictionary<string, ElementDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, CommandDefinition> commands = new Dictionary<string, CommandDefinition>(StringComparer.Ordinal);
        private readonly List<MethodDefinition> methods = new List<MethodDefinition>();
        private readonly Dictionary<string, VariableDefinition> variables = new Dictionary<string, VariableDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, EchoFormatter> echoFormats = new Dictionary<string, EchoFormatter>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> requires = new List<string>();
        private readonly Dictionary<string, LogProviderType> logProviders = new Dictionary<string, LogProviderType>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The full name, such as KScript.Terminal.</summary>
        public abstract string Name { get; }

        /// <summary>One sentence on what the package is for.</summary>
        public abstract string Description { get; }

        /// <summary>The name without the KScript. prefix, such as Terminal.</summary>
        public string ShortName => Name.StartsWith("KScript.", StringComparison.Ordinal) ? Name.Substring(8) : Name;

        protected abstract void Register(PackageBuilder package);

        public IReadOnlyDictionary<string, ElementDefinition> Elements { get { EnsureRegistered(); return elements; } }
        public IReadOnlyDictionary<string, CommandDefinition> Commands { get { EnsureRegistered(); return commands; } }
        public IReadOnlyList<MethodDefinition> Methods { get { EnsureRegistered(); return methods; } }
        public IReadOnlyDictionary<string, VariableDefinition> Variables { get { EnsureRegistered(); return variables; } }
        public IReadOnlyDictionary<string, EchoFormatter> EchoFormats { get { EnsureRegistered(); return echoFormats; } }

        /// <summary>Kinds of log provider this package adds, by type name: logging.providers[].type and &lt;log-provider type&gt;.</summary>
        public IReadOnlyDictionary<string, LogProviderType> LogProviders { get { EnsureRegistered(); return logProviders; } }

        /// <summary>Packages that are imported along with this one.</summary>
        public IReadOnlyList<string> Requires { get { EnsureRegistered(); return requires; } }

        internal void EnsureRegistered()
        {
            if (registered) return;
            lock (gate)
            {
                if (registered) return;
                Register(new PackageBuilder(this));
                registered = true;
            }
        }

        public override string ToString() => Name;

        /// <summary>Adds elements, commands and methods to a package. Packages call it from <see cref="Register"/>.</summary>
        public sealed class PackageBuilder
        {
            private readonly KScriptPackage package;

            internal PackageBuilder(KScriptPackage package) => this.package = package;

            public string Name => package.Name;

            /// <summary>Adds an element. <paramref name="handler"/> may be null for elements that only describe syntax.</summary>
            public ElementDefinition Element(ElementInfo info, ElementHandler handler, bool ownsContent = false)
            {
                info.Package = package.Name;
                var def = new ElementDefinition(info, handler, package) { OwnsContent = ownsContent };
                package.elements[info.Name] = def;
                return def;
            }

            /// <summary>Adds a command, called as <c>@name(args)</c>. Parameters are written like "path, pattern?, rest...".</summary>
            public CommandDefinition Command(string name, string parameters, string description, string returns, CommandHandler handler, string example = null) =>
                Command(new CallableInfo(CallableKind.Command, name, description, new[] { Signature.Parse(parameters) }) { ReturnType = returns, Example = example }, handler);

            public CommandDefinition Command(CallableInfo info, CommandHandler handler)
            {
                info.Package = package.Name;
                var def = new CommandDefinition(info, handler, package);
                package.commands[info.Name] = def;
                return def;
            }

            /// <summary>Adds a method, called as <c>value->name(args)</c> on values of <paramref name="receiver"/>.</summary>
            public MethodDefinition Method(string receiver, string name, string parameters, string description, string returns, MethodHandler handler)
            {
                var info = new CallableInfo(CallableKind.Method, name, description, new[] { Signature.Parse(parameters) }) { ReceiverType = receiver, ReturnType = returns };
                info.Package = package.Name;
                var def = new MethodDefinition(info, handler, package);
                package.methods.Add(def);
                return def;
            }

            /// <summary>
            /// Adds usage examples to commands and methods registered above: <c>("@now", "...")</c> for a command and
            /// <c>("string->upper", "'hi'->upper()")</c> for a method. An example is an expression (the docs show it
            /// echoed) or statements starting with <c>&lt;</c>. Hover and <c>kscript docs</c> show them, and the docs
            /// tests run them, so a key that names nothing is an error here.
            /// </summary>
            public void Examples(params (string Key, string Example)[] examples)
            {
                foreach (var (key, example) in examples)
                {
                    Find(key, "an example").Example = example;
                }
            }

            /// <summary>
            /// Adds notes for coding agents to commands, methods and elements registered above, keyed like
            /// <see cref="Examples"/> (<c>"@read_json"</c>, <c>"list->map"</c>) or <c>"&lt;each&gt;"</c> for an element.
            /// <c>kscript agents</c> writes them into AGENTS.md and .kscript/agents. Keep each note to one short line:
            /// agents read every one, so they cost tokens. A key that names nothing is an error here.
            /// </summary>
            public void AgentNotes(params (string Key, AgentNotes Notes)[] notes)
            {
                foreach (var (key, value) in notes)
                {
                    if (key.StartsWith("<", StringComparison.Ordinal) && key.EndsWith(">", StringComparison.Ordinal))
                    {
                        ElementInfo element = package.elements.TryGetValue(key.Substring(1, key.Length - 2), out ElementDefinition e) ? e.Info : null;
                        if (element == null) throw new InvalidOperationException($"{package.Name} has no element {key} to give agent notes to.");
                        element.Agent = value;
                        continue;
                    }
                    Find(key, "agent notes").Agent = value;
                }
            }

            private CallableInfo Find(string key, string what)
            {
                CallableInfo info;
                int arrow = key.IndexOf("->", StringComparison.Ordinal);
                if (key.StartsWith("@", StringComparison.Ordinal))
                    info = package.commands.TryGetValue(key.Substring(1), out CommandDefinition c) ? c.Info : null;
                else if (arrow > 0)
                    info = package.methods.FirstOrDefault(m => m.Receiver == key.Substring(0, arrow) && m.Name == key.Substring(arrow + 2))?.Info;
                else
                    info = null;
                return info ?? throw new InvalidOperationException($"{package.Name} has no command or method {key} to give {what} to.");
            }

            /// <summary>
            /// Marks something registered above as deprecated. It keeps working, but <c>kscript check</c> and the editor
            /// warn where it is used, completion shows it struck through, and the reference labels it. <paramref name="key"/>
            /// names a command (<c>@name</c>), a method (<c>string->name</c>), an element (<c>&lt;name&gt;</c>), or one of
            /// their parameters or attributes: <c>@name(param)</c>, <c>string->name(param)</c>, <c>&lt;name attr&gt;</c>.
            /// <paramref name="replacement"/> is what to use instead, as written in a script (<c>@read_text</c>, <c>path</c>).
            /// A key that names nothing is an error here, so a removed parameter can't leave a stale deprecation behind.
            /// </summary>
            public void Deprecate(string key, string message, string replacement = null)
            {
                var deprecation = new Deprecation(message, replacement);
                string member = null;
                if (key.StartsWith("<", StringComparison.Ordinal) && key.EndsWith(">", StringComparison.Ordinal))
                {
                    string inner = key.Substring(1, key.Length - 2).Trim();
                    int space = inner.IndexOf(' ');
                    string elementName = space < 0 ? inner : inner.Substring(0, space);
                    member = space < 0 ? null : inner.Substring(space + 1).Trim();
                    ElementInfo element = package.elements.TryGetValue(elementName, out ElementDefinition e) ? e.Info : null;
                    AttributeInfo attribute = member == null ? null : element?.GetAttribute(member);
                    if (element == null || (member != null && attribute == null))
                        throw new InvalidOperationException($"{package.Name} has no element or attribute {key} to deprecate.");
                    if (attribute != null) attribute.Deprecated = deprecation;
                    else element.Deprecated = deprecation;
                    return;
                }

                string callable = key;
                int open = key.IndexOf('(');
                if (open > 0 && key.EndsWith(")", StringComparison.Ordinal))
                {
                    callable = key.Substring(0, open);
                    member = key.Substring(open + 1, key.Length - open - 2).Trim();
                }
                CallableInfo info;
                int arrow = callable.IndexOf("->", StringComparison.Ordinal);
                if (callable.StartsWith("@", StringComparison.Ordinal))
                    info = package.commands.TryGetValue(callable.Substring(1), out CommandDefinition c) ? c.Info : null;
                else if (arrow > 0)
                    info = package.methods.FirstOrDefault(m => m.Receiver == callable.Substring(0, arrow) && m.Name == callable.Substring(arrow + 2))?.Info;
                else
                    info = null;
                if (info == null) throw new InvalidOperationException($"{package.Name} has no command or method {callable} to deprecate.");
                if (member == null)
                {
                    info.Deprecated = deprecation;
                    return;
                }
                var parameters = info.Signatures.SelectMany(sig => sig.Parameters).Where(p => p.Name == member).ToList();
                if (parameters.Count == 0) throw new InvalidOperationException($"{package.Name}: {callable} has no parameter '{member}' to deprecate.");
                foreach (var parameter in parameters) parameter.Deprecated = deprecation;
            }

            public VariableDefinition Variable(string name, string type, string description, Func<Interpreter, Module, object> value)
            {
                var def = new VariableDefinition(name, type, description, value, package);
                package.variables[name] = def;
                return def;
            }

            public void EchoFormat(string name, EchoFormatter formatter) => package.echoFormats[name] = formatter;

            /// <summary>
            /// Adds a kind of log provider, such as http. Scripts that import the package can name it in
            /// logging.providers (type: http) or &lt;log-provider type="http"&gt;; <paramref name="create"/> gets the options.
            /// </summary>
            public void LogProvider(string type, string description, Func<LogProviderContext, ILogProvider> create) =>
                package.logProviders[type] = new LogProviderType(type, description, create, package.Name);

            /// <summary>Another package that is always imported with this one.</summary>
            public void Requires(string packageName) => package.requires.Add(packageName);
        }
    }

    /// <summary>
    /// The packages a script can import. Packages are registered by name and the assembly-qualified name of their
    /// class, and only loaded on first use. <see cref="Default"/> knows every package that ships with kscript.
    /// </summary>
    public sealed class PackageRegistry
    {
        /// <summary>The packages that ship with KScript, by name and class.</summary>
        public static readonly IReadOnlyList<(string Name, string TypeName, string Description)> BuiltIn = new[]
        {
            ("KScript.Core", typeof(CorePackage).AssemblyQualifiedName, "The language itself; always imported."),
            ("KScript.Terminal", "KScript.Terminal.TerminalPackage, KScript.Terminal", "Prompts, menus, colours and other terminal input and output."),
            ("KScript.Math", "KScript.Mathematics.MathPackage, KScript.Math", "Square roots, trigonometry, rounding, random numbers and statistics."),
            ("KScript.IO", "KScript.IO.IOPackage, KScript.IO", "Files, folders and paths."),
            ("KScript.Net", "KScript.Net.NetPackage, KScript.Net", "HTTP requests and downloads."),
            ("KScript.Markdown", "KScript.Markdown.MarkdownPackage, KScript.Markdown", "Markdown to HTML."),
            ("KScript.Web", "KScript.Web.WebPackage, KScript.Web", "Web pages run by kscript serve: layouts, components and live blocks."),
            ("KScript.Style", "KScript.Style.StylePackage, KScript.Style", "Stylesheets written in KScript (.ks.css, .ks.scss, .ks.sass), with colour, size and responsive helpers."),
            ("KScript.Storage", "KScript.Storage.StoragePackage, KScript.Storage", "App folders for config, data, cache, state and logs, in the right place on each platform."),
            ("KScript.Document", "KScript.Documents.DocumentPackage, KScript.Document", "Scientific and professional documents (papers, reports, letters, invoices) with LaTeX maths, citations and tables, saved as PDF, Word, LaTeX, Markdown or HTML."),
            ("KScript.Cache", "KScript.Caching.CachePackage, KScript.Cache", "A cache of values by key, with time limits: in memory, or in files in the app's cache folder."),
            ("KScript.Http", "KScript.Http.HttpPackage, KScript.Http", "Routes, controllers and middleware: <KScript.Routes> files served by kscript serve."),
            ("KScript.Auth", "KScript.Auth.AuthPackage, KScript.Auth", "Sign-in for kscript serve apps: users, sessions, the auth and guest middleware, CSRF, password resets and API tokens."),
            ("KScript.Test", "KScript.Testing.TestPackage, KScript.Test", "Tests run by kscript test: <test>, <describe>, <expect>, <assert> and <capture>."),
            ("KScript.UI", "KScript.UI.UIPackage, KScript.UI", "Desktop windows with buttons, text and inputs."),
            ("KScript.AI", "KScript.AI.AIPackage, KScript.AI", "Language models: @llm, @chat and <llm>."),
        };

        private static readonly Lazy<PackageRegistry> DefaultInstance = new Lazy<PackageRegistry>(() =>
        {
            var registry = new PackageRegistry();
            foreach (var (name, type, description) in BuiltIn) registry.Register(name, type, description);
            return registry;
        });

        public static PackageRegistry Default => DefaultInstance.Value;

        private readonly object gate = new object();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private sealed class Entry
        {
            public string Name;
            public string TypeName;
            public string Description;
            public KScriptPackage Instance;
            public bool Missing;
        }

        public PackageRegistry()
        {
            Register(new CorePackage());
        }

        public KScriptPackage Core => Get("KScript.Core");

        /// <summary>Every registered package name, whether or not it is installed.</summary>
        public IEnumerable<string> Names
        {
            get { lock (gate) return entries.Values.Select(e => e.Name).Distinct().ToList(); }
        }

        public string DescriptionOf(string name)
        {
            lock (gate) return entries.TryGetValue(Normalise(name), out Entry e) ? e.Description ?? e.Instance?.Description : null;
        }

        public void Register(string name, string typeName, string description = null)
        {
            lock (gate)
            {
                if (entries.TryGetValue(name, out Entry existing) && existing.Instance != null) return;
                entries[name] = new Entry { Name = name, TypeName = typeName, Description = description };
            }
        }

        public void Register(KScriptPackage package)
        {
            lock (gate)
            {
                entries[package.Name] = new Entry { Name = package.Name, Instance = package, Description = package.Description };
            }
        }

        /// <summary>KScript.Math, Math and math all name the same package.</summary>
        public static string Normalise(string name)
        {
            name = (name ?? string.Empty).Trim();
            return name.StartsWith("KScript.", StringComparison.OrdinalIgnoreCase) ? "KScript." + name.Substring(8) : "KScript." + name;
        }

        public bool IsKnown(string name)
        {
            lock (gate) return entries.ContainsKey(Normalise(name));
        }

        /// <summary>The package, loading its assembly if needed; null if it isn't registered or isn't installed.</summary>
        public KScriptPackage Get(string name)
        {
            Entry entry;
            lock (gate)
            {
                if (!entries.TryGetValue(Normalise(name), out entry)) return null;
                if (entry.Instance != null || entry.Missing) return entry.Instance;
            }

            KScriptPackage instance = null;
            try
            {
                Type type = Type.GetType(entry.TypeName, throwOnError: false);
                if (type != null) instance = (KScriptPackage)Activator.CreateInstance(type);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is FileLoadException || ex is BadImageFormatException || ex is TargetInvocationException)
            {
                instance = null;
            }

            lock (gate)
            {
                if (instance == null) entry.Missing = true;
                else entry.Instance ??= instance;
                return entry.Instance;
            }
        }

        /// <summary>Loads every installed package. Used by tools and to suggest which package has a missing name.</summary>
        public IEnumerable<KScriptPackage> LoadAll() => Names.Select(Get).Where(p => p != null).ToList();

        /// <summary>The installed packages (other than <paramref name="except"/>) that define an element with this name.</summary>
        public IEnumerable<KScriptPackage> FindElement(string name, IEnumerable<KScriptPackage> except = null) =>
            LoadAll().Where(p => p.Elements.ContainsKey(name) && (except == null || !except.Contains(p)));

        public IEnumerable<KScriptPackage> FindCommand(string name, IEnumerable<KScriptPackage> except = null) =>
            LoadAll().Where(p => p.Commands.ContainsKey(name) && (except == null || !except.Contains(p)));

        public IEnumerable<KScriptPackage> FindMethod(string name, IEnumerable<KScriptPackage> except = null) =>
            LoadAll().Where(p => p.Methods.Any(m => m.Name == name) && (except == null || !except.Contains(p)));
    }
}
