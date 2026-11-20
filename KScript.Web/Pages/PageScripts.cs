using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Runtime;

namespace KScript.Pages
{
    /// <summary>
    /// Lets a page use functions written in a KScript script: <c>&lt;import src="lib/life.ks.xml" /&gt;</c> runs the
    /// script with the KScript interpreter and puts its exports (top-level &lt;fn&gt; functions and
    /// &lt;let const="yes"&gt; values) in the page's scope, so <c>@step($grid)</c> calls the same code a terminal script
    /// or a test does. One interpreter serves each render; a script imported twice in one render runs once.
    /// </summary>
    internal sealed class PageScripts
    {
        private readonly PageContext ctx;
        private Interpreter interpreter;
        private readonly Dictionary<string, Module> modules = new Dictionary<string, Module>(StringComparer.Ordinal);

        public PageScripts(PageContext ctx) => this.ctx = ctx;

        /// <summary>How long the scripts of one render may run in total.</summary>
        public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

        private Interpreter Interpreter => interpreter ??= new Interpreter(
            new KScriptHost(TextWriter.Null) { WorkingDirectory = ctx.SiteRoot },
            options: new InterpreterOptions { Timeout = Timeout, MaxSteps = 200_000_000 });

        /// <summary>Runs &lt;import src names&gt; in a page.</summary>
        public void Import(string src, string names, string asName, PageScope scope)
        {
            if (asName != null)
                throw new PageException("<import as> isn't supported in pages yet. Import the names directly (<import src=\"...\" />) or pick some with names=\"step, toggle\".");
            if (src.Contains('{'))
                throw new PageException("<import src> must be a plain path such as lib/life.ks.xml, not an expression, so a visitor can never choose which script runs.");

            string full = ctx.ResolveSitePath(src, "<import src>");
            if (!File.Exists(full)) throw new PageException($"There is no script at {src} (paths are relative to the site folder).");

            if (!modules.TryGetValue(full, out Module module))
            {
                try
                {
                    module = Interpreter.Load(full);
                    Interpreter.Run(module);
                }
                catch (KScriptException ex)
                {
                    throw Wrap(ex, src);
                }
                modules[full] = module;
            }

            IEnumerable<string> wanted = names == null
                ? module.Exports.Keys
                : names.Split(',').Select(n => n.Trim().TrimStart('@', '$')).Where(n => n.Length > 0);
            foreach (string name in wanted)
            {
                if (!module.Exports.TryGetValue(name, out object value))
                    throw new PageException($"{src} doesn't export '{name}'. Scripts export their top-level <fn> functions and <let const=\"yes\"> values"
                        + (module.Exports.Count > 0 ? $"; this one exports {string.Join(", ", module.Exports.Keys)}." : "."));
                scope.Declare(name, value);
            }
        }

        /// <summary>Calls a script function from a page expression. Page lambdas passed as arguments become callable from the script.</summary>
        public object Call(ICallable fn, IList<object> args, string source)
        {
            try
            {
                return Interpreter.Call(fn, args.Select(ToScript).ToList());
            }
            catch (KScriptException ex)
            {
                throw Wrap(ex, source);
            }
        }

        private object ToScript(object value) => value switch
        {
            PageLambda lambda => new NativeFunction("fn", c => PageEvaluator.Call(lambda, c.Args.ToList(), ctx)),
            List<object> list => list.Select(ToScript).ToList(),
            Dictionary<string, object> map => map.ToDictionary(kv => kv.Key, kv => ToScript(kv.Value), StringComparer.Ordinal),
            _ => value,
        };

        private PageException Wrap(KScriptException ex, string where)
        {
            string at = ex.Location != null ? ex.Location.Replace(ctx.SiteRoot + Path.DirectorySeparatorChar, string.Empty) : where;
            return new PageException($"{at}: {ex.Type}: {ex.Message}", ex);
        }
    }
}
