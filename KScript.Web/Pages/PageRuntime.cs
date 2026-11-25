using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Pages
{
    /// <summary>An error in a page. <see cref="Location"/> says where (file and line).</summary>
    public class PageException : Exception, KScript.Runtime.IKScriptError
    {
        public KScript.Runtime.KScriptException ToKScriptError() =>
            InnerException is KScript.Runtime.KScriptException k && k.Message == Message ? k : KScript.Runtime.ErrorReport.At(Message, "Page", Location, this);

        public PageException(string message) : base(message) { }
        public PageException(string message, Exception inner) : base(message, inner) { }

        /// <summary>File and line, filled in by the renderer as the error passes through an element.</summary>
        public string Location { get; set; }
    }

    /// <summary>
    /// Runs the KScript in pages on the same interpreter as scripts: expressions, statements, methods, commands and
    /// packages all come from KScript.Core and the packages a page imports. One runtime serves one request.
    /// </summary>
    internal sealed class PageRuntime
    {
        /// <summary>Where packages find the request's <see cref="PageContext"/>.</summary>
        internal const string ContextKey = "KScript.Web.Page";

        private readonly Dictionary<string, Module> modules = new Dictionary<string, Module>(StringComparer.Ordinal);
        private readonly Dictionary<(Module, int, string), DecodedText> texts = new Dictionary<(Module, int, string), DecodedText>();

        public PageRuntime(PageContext ctx)
        {
            // Scripts a page imports run once per request; anything they print goes to the server's error stream.
            var host = new KScriptHost(TextWriter.Null, Console.Error)
            {
                WorkingDirectory = ctx.SiteRoot,
                FileRoot = ctx.SiteRoot,
                FileRootName = "the site folder",
                CanWriteFiles = false,
            };
            foreach (var entry in ctx.HostItems) host.Items[entry.Key] = entry.Value;
            host.Items[ContextKey] = ctx;
            if (ctx.Request != null) host.Items[Translator.RequestKey] = ctx.Request;
            ctx.StartupConfig?.CopyTo(host);
            Interpreter = new Interpreter(host, options: new InterpreterOptions
            {
                MaxSteps = ctx.MaxSteps, MaxCallDepth = 200, Cancellation = LiveActionStream.Cancellation(ctx.Request),
            });
        }

        public Interpreter Interpreter { get; }

        // Model forms need the framework's session helper even in a component with no imports.
        private Module authModule;
        internal string CsrfToken()
        {
            authModule ??= Interpreter.CreateModule(null, new[] { "KScript.Auth" });
            return (string)Evaluate("@csrf_token()", authModule.Globals, authModule);
        }

        internal bool TryGetSessionState(string key, out object value)
        {
            authModule ??= Interpreter.CreateModule(null, new[] { "KScript.Auth" });
            var scope = authModule.Globals.Child();
            scope.Declare("key", "kscript.live." + key);
            if (!Values.Truthy(Evaluate("$session->has($key)", scope, authModule)))
            {
                value = null;
                return false;
            }
            value = Evaluate("$session->get($key)", scope, authModule);
            return true;
        }

        internal void SetSessionState(string key, object value)
        {
            authModule ??= Interpreter.CreateModule(null, new[] { "KScript.Auth" });
            var scope = authModule.Globals.Child();
            scope.Declare("key", "kscript.live." + key);
            scope.Declare("value", value);
            Evaluate("$session->set($key, $value)", scope, authModule);
        }

        /// <summary>The module of a page, layout or component file: the packages and scripts it imports.</summary>
        public Module ModuleFor(string file, string fullPath)
        {
            if (!modules.TryGetValue(file, out Module module))
            {
                module = Interpreter.CreateModule(fullPath, PageElementNames.DefaultPackages);
                modules[file] = module;
            }
            return module;
        }

        /// <summary>Runs an <c>&lt;import&gt;</c>: a package for the whole file, or a script's exports into <paramref name="scope"/>.</summary>
        public void Import(Module module, PageElement import, Scope scope, PageContext ctx, string file)
        {
            string package = import.Attr("package");
            string src = import.Attr("path");
            string alias = import.Attr("as")?.Trim();
            Run(module, () =>
            {
                if (package != null)
                {
                    Interpreter.ImportPackage(module, package.Trim(), alias);
                }
                else if (src != null)
                {
                    string folder = Path.GetDirectoryName(file.Replace('\\', '/')) ?? string.Empty;
                    string path = src.StartsWith("/", StringComparison.Ordinal) ? src : Path.Combine(folder, src).Replace('\\', '/');
                    Interpreter.ImportScript(module, ctx.ResolveSitePath(path, "<import path>"), scope, alias, import.Attr("names"));
                }
                else
                {
                    throw new PageException(import.HasAttr("src") ? "<import src> was renamed: write <import path=\"lib/text.ks.xml\" />." : "<import> needs a package or path, e.g. <import package=\"KScript.Math\" as=\"math\" /> or <import path=\"lib/text.ks.xml\" />.");
                }
                return null;
            });
        }

        public void ImportCompanion(Module module, string fullPath, Scope scope) =>
            Run(module, () =>
            {
                Interpreter.ImportScript(module, fullPath, scope);
                return null;
            });

        public object Call(ICallable function, object argument, Scope scope, Module module) =>
            Run(module, () => Interpreter.Call(function, new[] { argument }, scope: scope, module: module));

        private readonly Dictionary<string, (RecordType Type, IReadOnlySet<string> State)> components = new(StringComparer.Ordinal);

        internal ComponentTemplate RenderComponent(PageContext ctx, string path, string tag, Dictionary<string, object> props,
            out IReadOnlySet<string> liveFields, out string css)
        {
            string full = ctx.ResolveSitePath(path, "component");
            Module page = ModuleFor(path, full);
            string stylesheet = null;
            var rendered = (ComponentTemplate)Run(page, () =>
            {
                if (!components.TryGetValue(full, out var definition))
                {
                    Module script = Interpreter.LoadedModules.FirstOrDefault(m => m.Path == full) ?? Interpreter.Load(full);
                    var type = ComponentScript.FindType(script.Document, tag)
                        ?? throw new PageException($"{path} needs a type matching <{tag}> that extends Component.");
                    string name = type.GetAttributeValue("name");
                    Interpreter.ImportScript(page, full, page.Globals, names: name);
                    foreach (string package in PageElementNames.DefaultPackages)
                        Interpreter.ImportPackage(script, package);
                    if (!page.Globals.TryGet(name, out object exported) || exported is not RecordType recordType)
                        throw new PageException($"{path} must export the component type {name}.");
                    var fields = recordType.Fields.Where(f => !f.Computed).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
                    components[full] = definition = (recordType, fields);
                }
                // The mapper resolves 'from' paths and declared defaults before requesting a type's initial value.
                var mapper = new ShapeMapper(Interpreter, definition.Type.Closure, definition.Type.Module);
                Record record = mapper.MapRecord(props, definition.Type, "$", 0, field => InitialState(definition.Type, field));
                mapper.ThrowIfAny(definition.Type.Name, TypeRef.Of(definition.Type));
                record.BoundFields = definition.State;
                var scope = page.Globals.Child();
                scope.Declare("component", record);
                var template = Interpreter.EvaluateText(Text(page, 0, "$component->render()"), scope, page)
                    as ComponentTemplate ?? throw new PageException($"{definition.Type.Name}->render() must return a Component.");
                if (definition.Type.OwnMethods.ContainsKey("style"))
                {
                    var style = (ComponentStyle)Interpreter.EvaluateText(Text(page, 0, "$component->style()"), scope, page);
                    stylesheet = style.Language == "css" ? style.Text : Style.Stylesheets.CompileText(
                        style.Text, Path.ChangeExtension(style.Module.Path, ".ks.css"), record, host =>
                        {
                            host.FileRoot = ctx.SiteRoot;
                            host.FileRootName = "the site folder";
                            host.CanWriteFiles = false;
                            ctx.StartupConfig?.CopyTo(host);
                        });
                    if (stylesheet.Contains("</style", StringComparison.OrdinalIgnoreCase))
                        throw new PageException($"{definition.Type.Name}->style() contains a closing HTML style tag.");
                }
                return template;
            });
            liveFields = components[full].State;
            css = stylesheet;
            return rendered;
        }

        private static object InitialState(RecordType owner, FieldDefinition field)
        {
            var create = TemplateContract.FieldInitializer(field.Type)
                ?? throw new PageException($"Component field {owner.Name}.{field.Name} needs an initial value for {field.Type}. "
                    + "Supply it as a prop, give it a default or make the field optional.");
            return create();
        }

        public object Evaluate(string expression, Scope scope, Module module) =>
            Run(module, () => Interpreter.EvaluateText(Text(module, 0, expression), scope, module));

        public List<object> EvaluateList(string expressions, Scope scope, Module module) =>
            (List<object>)Run(module, () => Interpreter.EvaluateList(Text(module, 1, expressions), scope, module));

        public void Execute(string statements, Scope scope, Module module, bool declare) =>
            Run(module, () =>
            {
                Interpreter.ExecuteStatements(Text(module, 2, statements), scope, module, declare);
                return null;
            });

        public void RunHandler(string handler, Scope scope, Module module) =>
            Run(module, () =>
            {
                Interpreter.RunHandler(Text(module, 2, handler), scope, module);
                return null;
            });

        public string Interpolate(string text, Scope scope, Module module, Func<string, string> escape) =>
            (string)Run(module, () => Interpreter.Interpolate(Text(module, 3, text), scope, module, bareReferences: false, escape));

        public object Combine(object current, string op, object value, Module module) =>
            Run(module, () => Interpreter.Combine(current, op, value));

        /// <summary>The same text object for the same source, so the interpreter's parse cache works.</summary>
        private DecodedText Text(Module module, int mode, string value)
        {
            if (!texts.TryGetValue((module, mode, value), out DecodedText text))
            {
                text = DecodedText.Plain(value, 0);
                texts[(module, mode, value)] = text;
            }
            return text;
        }

        /// <summary>Turns interpreter errors into page errors. The renderer adds the page's file and line.</summary>
        private static object Run(Module module, Func<object> action)
        {
            try
            {
                return action();
            }
            catch (KScriptException error)
            {
                KScriptException ex = Interpreter.WithLocation(error, module);
                // Errors inside an imported script keep that script's file and line; errors in the page itself get
                // the page's line from the renderer.
                bool inPage = ex.File == null || string.Equals(ex.File, module.Path ?? module.DisplayName, StringComparison.Ordinal);
                throw new PageException(inPage ? ex.Message : $"{ex.Location}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>Evaluates one page expression: for tests and tools.</summary>
    public static class PageEvaluator
    {
        public static object Evaluate(string expression, Scope scope, PageContext ctx)
        {
            PageRuntime runtime = ctx.Runtime;
            return runtime.Evaluate(expression, scope, runtime.ModuleFor("expression", null));
        }
    }
}
