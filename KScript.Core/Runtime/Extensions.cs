using KScript.Metadata;
using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>A method an <c>extend</c> block adds to a type, such as <c>extend date { fn json() => ... }</c>.</summary>
    internal sealed class ExtensionMethod
    {
        public ExtensionMethod(string receiver, string name, MarkupElement element, Scope closure, Module module, bool isOverride)
        {
            Receiver = receiver;
            Name = name;
            Element = element;
            Closure = closure;
            Module = module;
            Override = isOverride;
        }

        public string Receiver { get; }
        public string Name { get; }
        public MarkupElement Element { get; }
        public Scope Closure { get; }
        public Module Module { get; }
        public bool Override { get; }

        /// <summary>Where it was declared, for errors: <c>date.kscript line 3</c>.</summary>
        public string Where => $"{Module.DisplayName} line {Module.PositionOf(Element.Span.Start).Line}";
    }

    /// <summary>A project's extension files, which every script in the project gets the methods of.</summary>
    internal sealed class ProjectExtensionSet
    {
        public List<Module> Modules { get; } = new List<Module>();

        /// <summary>The project's native type files (the natives folder, or kscript.json's "natives").</summary>
        public List<Module> Natives { get; } = new List<Module>();

        /// <summary>
        /// The types and enums the native type files declare, filled in as they run. Every script in the project
        /// sees them through its globals' parent scope, so a script's own type of the same name wins.
        /// </summary>
        public Scope NativeScope { get; } = new Scope();
    }

    /// <summary>
    /// Extension methods: <c>extend date { fn json() => $this->to_json() }</c> adds <c>->json()</c> to every date in
    /// the file, in the files that import it and, for a project's extension files, in the whole project. A method
    /// can't reuse a built-in method's name unless it says <c>override fn</c>.
    /// </summary>
    internal static class Extensions
    {
        /// <summary>The built-in types an extend block can name. Packages' own types (file, response, ...) work too.</summary>
        public static IEnumerable<string> BuiltInTypes =>
            new[] { "string", "number", "bool", "list", "map", "fn", "date" }.Concat(KScript.Metadata.NativeTypes.Names);

        /// <summary>The receiver an extend block's type means: int and float are numbers.</summary>
        public static string Receiver(string type) => type switch
        {
            "int" or "float" => KType.Number,
            _ => type,
        };

        /// <summary>Runs an &lt;extend&gt;: checks it and registers its methods on the module.</summary>
        public static void Declare(ElementRun run)
        {
            Module module = run.Module;
            bool code = module.IsCode;
            if (run.Element.Parent != module.Root && !module.IsPackageRoot)
            {
                throw run.Error(code ? "extend goes at the top level of a file, not inside a block." : "<extend> must be a direct child of <kscript>.", null, "Syntax");
            }

            string type = run.Raw("type")?.Trim();
            if (string.IsNullOrEmpty(type))
            {
                throw run.Error(code ? "Write the type to add methods to: extend date { }." : "<extend> needs a type, such as <extend type=\"date\">.", "type", "Syntax");
            }

            RecordType declared = run.Scope.TryGet(type, out object value) ? value as RecordType : null;
            string receiver = declared?.Name ?? Receiver(type);
            if (declared == null && !IsKnownType(run.Interpreter, receiver))
            {
                string close = Values.Closest(type, KnownTypes(run.Interpreter));
                throw run.Error($"There is no type called {type} to extend." + (close != null ? $" Did you mean {close}?" : string.Empty)
                    + $" Extend a built-in type ({string.Join(", ", BuiltInTypes)}), a package's type such as file, or a type declared in this file or one it imports.", "type", "Name");
            }

            foreach (MarkupNode node in run.Element.Children)
            {
                if (node is MarkupText text && !text.IsWhitespace)
                    throw run.Error(code ? $"extend {type} {{ }} holds fn methods only." : "<extend> holds <fn> elements only.", null, "Syntax");
                if (node is not MarkupElement e) continue;
                if (e.Name != "fn")
                {
                    throw Located(run, e, code ? $"extend {type} {{ }} holds fn methods only, not {e.Name}." : $"<extend> holds <fn> elements only, not <{e.Name}>.");
                }

                string name = e.GetAttributeValue("name")?.Trim().TrimStart('@');
                if (string.IsNullOrEmpty(name)) throw Located(run, e, code ? "Write the method's name: fn json() => ..." : "<fn> needs a name, such as <fn name=\"json\">.");
                bool isOverride = Values.ParseBool(e.GetAttributeValue("override")) == true;
                bool builtIn = HasBuiltIn(module, receiver, declared, name);
                if (builtIn && !isOverride)
                {
                    throw Located(run, e, $"{Article(receiver)} already has ->{name}(). To replace it, write "
                        + (code ? $"override fn {name}(...)" : $"<fn name=\"{name}\" override=\"true\">") + "; to add a new method, give it another name.");
                }
                if (isOverride && !builtIn)
                {
                    throw Located(run, e, $"{Article(receiver)} has no built-in ->{name}() to override. " + (code ? "Drop override" : "Drop override=\"true\"") + " to add it as a new method.");
                }

                string key = receiver + ":" + name;
                if (module.Extensions.TryGetValue(key, out ExtensionMethod existing) && existing.Module == module && existing.Element != e)
                {
                    throw Located(run, e, $"->{name}() is added to {receiver} twice: at {existing.Where} and here. Keep one.");
                }
                ExtensionMethod other = module.ProjectExtensions?.Modules
                    .Where(m => m != module && m.Extensions.TryGetValue(key, out ExtensionMethod x) && x.Module == m)
                    .Select(m => m.Extensions[key]).FirstOrDefault();
                if (other != null && module.ProjectExtensions.Modules.Contains(module))
                {
                    throw Located(run, e, $"Two extension files add ->{name}() to {receiver}: {other.Where} and {module.DisplayName} line {module.PositionOf(e.Span.Start).Line}. Keep one.");
                }
                module.Extensions[key] = new ExtensionMethod(receiver, name, e, run.Scope, module, isOverride);
            }
        }

        private static KScriptException Located(ElementRun run, MarkupElement e, string message) =>
            new KScriptException(message, "Syntax") { Offset = e.NameSpan.Start };

        private static string Article(string receiver) =>
            receiver.Length > 0 && "aeio".Contains(char.ToLowerInvariant(receiver[0])) ? "An " + receiver : "A " + receiver;

        private static bool HasBuiltIn(Module module, string receiver, RecordType declared, string name)
        {
            if (declared != null)
            {
                return declared.OwnMethods.ContainsKey(name) || module.Methods.ContainsKey("map:" + name) || module.Methods.ContainsKey("any:" + name);
            }
            return module.Methods.ContainsKey(receiver + ":" + name) || module.Methods.ContainsKey("any:" + name)
                || (SemanticTypes.IsSemantic(receiver) && KType.AcceptsMethod(KType.String, name, receiver) && module.Methods.ContainsKey("string:" + name));
        }

        private static IEnumerable<string> KnownTypes(Interpreter interpreter) =>
            BuiltInTypes.Concat(interpreter.Packages.LoadAll().SelectMany(p => p.Methods).Select(m => m.Receiver)).Where(r => r != KType.Any).Distinct();

        private static bool IsKnownType(Interpreter interpreter, string receiver) => KnownTypes(interpreter).Contains(receiver);

        /// <summary>Gives <paramref name="into"/> the methods the extend blocks of an imported script add.</summary>
        public static void Import(Module into, Module from)
        {
            foreach (KeyValuePair<string, ExtensionMethod> m in from.Extensions)
            {
                if (into.Extensions.TryGetValue(m.Key, out ExtensionMethod mine) && mine.Module == into) continue; // the script's own wins
                into.Extensions[m.Key] = m.Value;
            }
        }

        /// <summary>The receivers a value's extension methods are found under, most specific first.</summary>
        private static IEnumerable<string> ReceiversOf(object target)
        {
            if (target is Record record)
            {
                yield return record.Type.Name;
                yield return KType.Map;
                yield break;
            }
            yield return Values.TypeOf(target);
            if (target is string s && Values.TryNumber(s, out _)) yield return KType.Number;
        }

        /// <summary>The extension method <c>value->name()</c> calls in this module, if one is in effect.</summary>
        public static bool TryFind(object target, string name, Module module, out ExtensionMethod method)
        {
            method = null;
            if (module == null || target == null || (module.Extensions.Count == 0 && (module.ProjectExtensions == null || module.ProjectExtensions.Modules.Count == 0))) return false;
            foreach (string receiver in ReceiversOf(target))
            {
                string key = receiver + ":" + name;
                if (module.Extensions.TryGetValue(key, out method)) return true;
                if (module.ProjectExtensions == null) continue;
                foreach (Module m in module.ProjectExtensions.Modules)
                {
                    if (m.Extensions.TryGetValue(key, out method) && method.Module == m) return true;
                }
            }
            method = null;
            return false;
        }

        /// <summary>Calls an extension method with the value as <c>$this</c>.</summary>
        public static object Call(Interpreter interpreter, ExtensionMethod method, object target, List<object> args, Dictionary<string, object> named, Scope scope, Module caller)
        {
            Scope methodScope = method.Closure.Child();
            methodScope.Declare("this", target);
            interpreter.DeclareFunction(method.Element, methodScope, method.Module);
            methodScope.TryGet(method.Name, out object fn);
            return interpreter.Call((ICallable)fn, args, named, scope, caller);
        }
    }

    public sealed partial class Interpreter
    {
        private readonly Dictionary<string, ProjectExtensionSet> projectExtensions = new Dictionary<string, ProjectExtensionSet>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> projectFiles = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The extension files of the project a module is in (loaded and run the first time a script in the project
        /// runs), or null outside a project or with <see cref="InterpreterOptions.ProjectExtensions"/> off.
        /// </summary>
        internal ProjectExtensionSet ProjectExtensionsFor(Module module)
        {
            string dir = module.Path != null ? System.IO.Path.GetDirectoryName(module.Path) : module.Directory;
            if (!Options.ProjectExtensions || string.IsNullOrEmpty(dir)) return null;
            if (!projectFiles.TryGetValue(dir, out string file)) projectFiles[dir] = file = ProjectConfig.Find(dir);
            if (file == null) return null;
            if (projectExtensions.TryGetValue(file, out ProjectExtensionSet set)) return set;

            set = projectExtensions[file] = new ProjectExtensionSet();
            List<string> files, natives;
            try
            {
                ProjectConfig config = ProjectConfig.Load(file);
                files = config.ExtensionFiles();
                natives = config.NativeFiles();
            }
            catch (ProjectConfigException)
            {
                return set; // the project's own commands report a broken kscript.json
            }
            foreach (string path in natives)
            {
                set.Natives.Add(modulesByPath.TryGetValue(path, out Module loaded) ? loaded : Load(path));
            }
            foreach (string path in files)
            {
                set.Modules.Add(modulesByPath.TryGetValue(path, out Module loaded) ? loaded : Load(path));
            }

            // Native types first, so extensions (and later natives) can use them.
            foreach (Module m in set.Natives.ToList())
            {
                RunModule(m);
                foreach (KeyValuePair<string, object> export in m.Exports)
                {
                    if (export.Value is RefinedType || export.Value is RecordType || export.Value is EnumType) set.NativeScope.Declare(export.Key, export.Value);
                }
            }
            foreach (Module m in set.Modules.ToList())
            {
                RunModule(m);
            }
            return set;
        }
    }
}
