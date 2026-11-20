using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Pages
{
    /// <summary>An error in a page. <see cref="Location"/> says where (file and line).</summary>
    public class PageException : Exception
    {
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

        /// <summary>
        /// The packages every page has without importing them. KScript.IO reads files (<c>@read_json</c>,
        /// <c>@files</c>); in pages its paths start in the site folder, can't leave it, and files are read-only.
        /// </summary>
        internal static readonly string[] PagePackages = { "KScript.IO", "KScript.Markdown", "KScript.Web" };

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
            host.Items[ContextKey] = ctx;
            Interpreter = new Interpreter(host, options: new InterpreterOptions { MaxSteps = ctx.MaxSteps, MaxCallDepth = 200 });
        }

        public Interpreter Interpreter { get; }

        /// <summary>The module of a page, layout or component file: the packages and scripts it imports.</summary>
        public Module ModuleFor(string file, string fullPath)
        {
            if (!modules.TryGetValue(file, out Module module))
            {
                module = Interpreter.CreateModule(fullPath, PagePackages);
                modules[file] = module;
            }
            return module;
        }

        /// <summary>Runs an <c>&lt;import&gt;</c>: a package for the whole file, or a script's exports into <paramref name="scope"/>.</summary>
        public void Import(Module module, PageElement import, Scope scope, PageContext ctx, string file)
        {
            string package = import.Attr("package");
            string src = import.Attr("src");
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
                    Interpreter.ImportScript(module, ctx.ResolveSitePath(path, "<import src>"), scope, alias, import.Attr("names"));
                }
                else
                {
                    throw new PageException("<import> needs a package or src, e.g. <import package=\"KScript.Math\" as=\"math\" /> or <import src=\"lib/text.ks.xml\" />.");
                }
                return null;
            });
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
