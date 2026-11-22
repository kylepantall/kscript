using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Language.Catalog
{
    /// <summary>
    /// Everything the tooling knows about KScript 2: elements and their attributes, commands, methods, retired v1
    /// syntax, built-in variables, and the package each one belongs to.
    ///
    /// The interpreter is the source of truth: every package registers its elements, commands, methods and variables
    /// with their descriptions (<see cref="KScriptPackage"/>), and the retired v1 syntax is in <see cref="Retired"/>.
    /// This class reads all of that. The only things described here directly are the parts of the KScript 2 proposal
    /// that no package implements yet (<c>Planned</c>); once a package implements one, the package wins.
    /// </summary>
    public sealed class LanguageCatalog
    {
        private static readonly Lazy<LanguageCatalog> DefaultInstance = new Lazy<LanguageCatalog>(() => new LanguageCatalog());

        private readonly Dictionary<string, ElementInfo> elements = new Dictionary<string, ElementInfo>(StringComparer.OrdinalIgnoreCase);

        // Names as written, so KScript.Http's <Delete> and a script's own <delete> are both known: an exact match wins.
        private readonly Dictionary<string, ElementInfo> exactElements = new Dictionary<string, ElementInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, RetiredInfo> retiredElements = new Dictionary<string, RetiredInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RetiredInfo> retiredAttributes = new Dictionary<string, RetiredInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CallableInfo> commands = new Dictionary<string, CallableInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RetiredInfo> retiredCommands = new Dictionary<string, RetiredInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CallableInfo> methods = new List<CallableInfo>();
        private readonly Dictionary<string, (string Type, string Description)> builtInVariables = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> variablePackages = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> typeFields = new Dictionary<string, Dictionary<string, string>>();
        private readonly Dictionary<string, string> packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> packageRequires = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public static LanguageCatalog Default => DefaultInstance.Value;

        public LanguageCatalog() : this(PackageRegistry.Default)
        {
        }

        public LanguageCatalog(PackageRegistry registry)
        {
            foreach (string name in registry.Names)
            {
                packages[name] = registry.DescriptionOf(name);
            }

            // Core first, so a name another package reuses keeps its core meaning.
            foreach (KScriptPackage package in registry.LoadAll().OrderBy(p => p.Name == "KScript.Core" ? 0 : 1).ThenBy(p => p.Name, StringComparer.Ordinal))
            {
                AddPackage(package);
            }

            AddRetired();
            AddPlanned();
            AddVariables();
        }

        #region Lookups

        public IEnumerable<ElementInfo> Elements => exactElements.Values;
        public IEnumerable<CallableInfo> Commands => commands.Values;
        public IEnumerable<CallableInfo> Methods => methods;
        public IEnumerable<string> RetiredElementNames => retiredElements.Keys;

        /// <summary>Every package a script can import, with its description (installed or not).</summary>
        public IReadOnlyDictionary<string, string> Packages => packages;

        public ElementInfo GetElement(string name) =>
            name == null ? null : exactElements.TryGetValue(name, out ElementInfo exact) ? exact : elements.TryGetValue(name, out ElementInfo e) ? e : null;

        /// <summary>True when an element's attribute holds a list of names, such as &lt;Middleware use&gt; or &lt;Resource only&gt;.</summary>
        /// <c>KScript.Document</c> is <c>Document</c> from the package that has it, as in a script.
        public bool IsNameList(string element, string attribute)
        {
            ElementInfo info = GetElement(element);
            if (info == null && element != null && element.StartsWith("KScript.", StringComparison.Ordinal)) info = GetElement(element.Substring(8));
            return info?.GetAttribute(attribute)?.IsNameList == true;
        }

        public RetiredInfo GetRetiredElement(string name) => name != null && retiredElements.TryGetValue(name, out RetiredInfo r) ? r : null;

        public RetiredInfo GetRetiredAttribute(string name) => name != null && retiredAttributes.TryGetValue(name, out RetiredInfo r) ? r : null;

        public CallableInfo GetCommand(string name) => name != null && commands.TryGetValue(name, out CallableInfo c) ? c : null;

        public RetiredInfo GetRetiredCommand(string name) => name != null && retiredCommands.TryGetValue(name, out RetiredInfo r) ? r : null;

        /// <summary>Methods with this name, best match for the receiver type first.</summary>
        public IEnumerable<CallableInfo> GetMethods(string name, string receiverType = KType.Any) =>
            methods.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                   .OrderBy(m => m.ReceiverType == receiverType ? 0 : KType.AcceptsMethod(m.ReceiverType, m.Name, receiverType) ? 1 : 2);

        /// <summary>Every type some method is for, such as date, list or response.</summary>
        public IEnumerable<string> MethodReceivers => methods.Select(m => m.ReceiverType).Distinct(StringComparer.Ordinal);

        public IEnumerable<CallableInfo> MethodsFor(string receiverType) =>
            methods.Where(m => KType.AcceptsMethod(m.ReceiverType, m.Name, receiverType))
                   .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                   .Select(g => g.OrderBy(m => m.ReceiverType == receiverType ? 0 : 1).First());

        /// <summary>Variables that exist without being declared, such as <c>$args</c> or <c>$request</c> in pages.</summary>
        public IReadOnlyDictionary<string, (string Type, string Description)> BuiltInVariables => builtInVariables;

        /// <summary>The package that provides a built-in variable (null for ones every script and page has).</summary>
        public string PackageOfVariable(string name) => name != null && variablePackages.TryGetValue(name, out string p) ? p : null;

        /// <summary>The package's name as written in &lt;import&gt;, or null if there is no such package.</summary>
        public string FindPackage(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string full = PackageRegistry.Normalise(name);
            return packages.Keys.FirstOrDefault(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The package and the packages it brings with it (KScript.Terminal brings KScript.Markdown).</summary>
        public IEnumerable<string> WithRequired(string package)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(package);
            while (stack.Count > 0)
            {
                string p = stack.Pop();
                if (p == null || !seen.Add(p)) continue;
                yield return p;
                if (packageRequires.TryGetValue(p, out List<string> required))
                {
                    foreach (string r in required) stack.Push(r);
                }
            }
        }

        /// <summary>Known fields of a value type (<c>$request.query</c>, <c>$err.message</c>).</summary>
        public IReadOnlyDictionary<string, string> FieldsOf(string type) =>
            type != null && typeFields.TryGetValue(type, out Dictionary<string, string> f) ? f : new Dictionary<string, string>();

        #endregion

        #region Packages

        private void AddPackage(KScriptPackage package)
        {
            packages[package.Name] = package.Description;
            packageRequires[package.Name] = package.Requires.Select(PackageRegistry.Normalise).ToList();

            foreach (ElementDefinition e in package.Elements.Values)
            {
                AddElement(e.Info);
            }
            foreach (CommandDefinition c in package.Commands.Values)
            {
                commands.TryAdd(c.Name, c.Info);
            }
            foreach (MethodDefinition m in package.Methods)
            {
                if (!methods.Any(x => x.Name == m.Name && x.ReceiverType == m.Info.ReceiverType)) methods.Add(m.Info);
            }
            foreach (VariableDefinition v in package.Variables.Values)
            {
                if (builtInVariables.ContainsKey(v.Name)) continue;
                builtInVariables[v.Name] = (v.Type, v.Description);
                if (package.Name != "KScript.Core") variablePackages[v.Name] = package.Name;
            }
        }

        #endregion

        #region Retired v1 syntax

        private void AddRetired()
        {
            foreach (var kv in Retired.Elements)
            {
                if (!elements.ContainsKey(kv.Key)) retiredElements[kv.Key] = kv.Value;
            }
            foreach (var kv in Retired.Attributes) retiredAttributes[kv.Key] = kv.Value;
            foreach (var kv in Retired.Commands)
            {
                if (!commands.ContainsKey(kv.Key)) retiredCommands[kv.Key] = kv.Value;
            }
        }

        #endregion

        #region Planned

        private void AddPlanned()
        {
            AddPlannedElement(new ElementInfo("render", "Replaces the content of a page element. Non-KScript elements inside are output as HTML, with `{ }` values escaped.", ContentKind.Markup,
                new A("into", K.Selector, "The element to fill, as a CSS selector.", true),
                new A("raw", K.Bool, "yes outputs values without HTML escaping.")));
            AddPlannedElement(new ElementInfo("on", "Runs its body when a DOM event fires (browser). `$event` holds the event.", ContentKind.Statements,
                new A("event", K.Text, "The event name, such as click or input.", true),
                new A("target", K.Selector, "The element to listen on.", true)));

            AddPlannedCommand("dom", "selector", "A page element (browser): ->value(), ->text(), ->add_class()...", KType.Element);

            AddPlannedMethod(KType.Element, "value", "", "The value of an input.", KType.String);
            AddPlannedMethod(KType.Element, "text", "", "The text content.", KType.String);
            AddPlannedMethod(KType.Element, "attr", "name, value?", "Reads or sets an attribute.", KType.String);
            AddPlannedMethod(KType.Element, "add_class", "name", "Adds a CSS class.", KType.Element);
            AddPlannedMethod(KType.Element, "remove_class", "name", "Removes a CSS class.", KType.Element);
            AddPlannedMethod(KType.Element, "show", "", "Shows the element.", KType.Element);
            AddPlannedMethod(KType.Element, "hide", "", "Hides the element.", KType.Element);
        }

        private void AddPlannedElement(ElementInfo e) => AddElement(e);

        private void AddElement(ElementInfo e)
        {
            elements.TryAdd(e.Name, e);
            exactElements.TryAdd(e.Name, e);
        }

        private void AddPlannedCommand(string name, string parameters, string description, string returns)
        {
            if (commands.ContainsKey(name)) return;
            commands[name] = new CallableInfo(CallableKind.Command, name, description, new[] { Signature.Parse(parameters) }) { ReturnType = returns };
            retiredCommands.Remove(name);
        }

        private void AddPlannedMethod(string receiver, string name, string parameters, string description, string returns)
        {
            if (methods.Any(m => m.Name == name && m.ReceiverType == receiver)) return;
            methods.Add(new CallableInfo(CallableKind.Method, name, description, new[] { Signature.Parse(parameters) }) { ReceiverType = receiver, ReturnType = returns });
        }

        #endregion

        #region Variables

        private void AddVariables()
        {
            builtInVariables.TryAdd("event", (KType.Map, "The DOM event inside <on>: value, key, target."));

            typeFields[KType.Request] = new Dictionary<string, string>
            {
                ["method"] = "GET, POST...",
                ["path"] = "The path, such as /people.",
                ["query"] = "Query string values: $request.query.page.",
                ["form"] = "Posted form fields: $request.form.email.",
                ["headers"] = "Request headers.",
                ["cookies"] = "Cookies.",
                ["params"] = "Route parameters from [name].html files: $request.params.id.",
                ["body"] = "The raw body: $request.body->json().",
            };
            typeFields[KType.Error] = new Dictionary<string, string>
            {
                ["type"] = "The error type, such as FileNotFound.",
                ["message"] = "What went wrong.",
                ["file"] = "The script the error came from.",
                ["line"] = "The line the error came from.",
                ["column"] = "The column the error came from.",
                ["status"] = "The HTTP status its error type carries, such as 404, or null.",
                ["cause"] = "The error that led to this one, or null.",
            };
            typeFields[KType.File] = new Dictionary<string, string>
            {
                ["path"] = "The full path.",
                ["name"] = "The file name with its extension.",
                ["extension"] = "The extension without the dot.",
            };
            typeFields[KType.Response] = new Dictionary<string, string>
            {
                ["status"] = "The HTTP status code.",
                ["ok"] = "True for a 2xx status.",
                ["url"] = "The address that was fetched.",
                ["headers"] = "The response headers.",
                ["text"] = "The body as text.",
            };
        }

        #endregion
    }
}
