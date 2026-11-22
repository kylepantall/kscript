using KScript.Metadata;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        /// <summary>The attributes of &lt;log_provider&gt; that are passed to the provider as options.</summary>
        private static readonly string[] ProviderOptions = { "type", "name", "level", "format", "path", "roll", "stream", "colour", "time", "url", "batch" };

        /// <summary>Logging: $log, &lt;log&gt; and &lt;log_provider&gt;.</summary>
        private static void RegisterLogging(PackageBuilder p)
        {
            p.Variable("log", KType.Logger,
                "The script's logger: $log->info('Saved', { id: $id }), $log->named('db')->debug(...). Providers and levels come from $config.logging.",
                (i, m) => new LoggerValue(i, m));

            foreach (string level in LogLevels.Names.Take(6))
            {
                p.Method(KType.Logger, level, "message, fields?", $"Logs a message at {level} level, with an optional map of fields.", KType.Null, LoggerMethod(level));
            }
            p.Field(KType.Logger, "category", KType.String, "The logger's category, such as db, or null.");
            p.Field(KType.Logger, "fields", KType.Map, "The fields the logger adds to everything it logs.");
            p.Method(KType.Logger, "log", "level, message, fields?", "Logs a message at a level given by name.", KType.Null, LoggerMethod("log"));
            p.Method(KType.Logger, "with", "fields", "A logger that adds these fields to everything it logs.", KType.Logger, LoggerMethod("with"));
            p.Method(KType.Logger, "named", "category", "A logger for a category, such as db (inside a named logger, db.queries).", KType.Logger, LoggerMethod("named"));
            p.Method(KType.Logger, "is_enabled", "level", "True when entries at this level would be written, so costly messages can be skipped.", KType.Bool, LoggerMethod("is_enabled"));
            p.Method(KType.Logger, "entries", "", "What memory providers kept: a list of { time, level, category, message, fields, script, line }.", KType.List, LoggerMethod("entries"));
            p.Method(KType.Logger, "flush", "", "Writes out entries that providers hold back (they are also written when the script ends).", KType.Null, LoggerMethod("flush"));

            p.Element(new ElementInfo("log", "Logs its text through $log's providers, at a level. Fields add values such as { user: $id }.", ContentKind.Template,
                new A("level", K.Enum, "How serious it is (default info).", false, LogLevels.Names.Take(6).ToArray()),
                new A("message", K.Template, "The message, instead of the element's text."),
                new A("category", K.Template, "Where it comes from, such as db or payments."),
                new A("fields", K.Expression, "A map of extra values, such as { order: $order.id, total: $total }."))
            {
                Example = "<let name=\"id\" value=\"42\" />\n<let name=\"customer\" value=\"{ name: 'Joan' }\" />\n<log level=\"warn\" category=\"payments\" fields=\"{ order: $id }\">Card declined for {$customer.name}</log>",
            }, run =>
            {
                string message = run.Has("message") ? run.Template("message") : run.Text();
                LogLevel level = run.Has("level") ? LogLevels.Parse(run.Raw("level"), run.Module.ArgumentHint("log", "level")) : LogLevel.Info;
                object fields = run.Has("fields") ? run.Eval("fields") : null;
                int line = run.Module.Source.GetPosition(run.Element.Span.Start).Line + 1;
                new LoggerValue(run.Interpreter, run.Module, run.Has("category") ? run.Template("category") : null).Log(level, message, fields, line);
                return null;
            });

            p.Element(new ElementInfo("log_provider",
                "Adds somewhere for log entries to go: the console, a file, memory (for tests), a function, or a type a package adds (KScript.Net adds http). Usually set in config under logging.providers instead.",
                ContentKind.Empty,
                new A("type", K.Text, "console, file, memory, or a type from an imported package such as http. Leave it out when using fn."),
                new A("fn", K.FunctionReference, "A function called with each entry (a map of time, level, category, message, fields, script and line): `@alert` (in XML, fn=\"alert\")."),
                new A("level", K.Enum, "The lowest level this provider writes (default: all that logging lets through).", false, LogLevels.Names),
                new A("format", K.Enum, "text or json (console and file).", false, "text", "json"),
                new A("path", K.Template, "The file to append to (file)."),
                new A("roll", K.Enum, "daily starts a new file each day: app-2026-11-20.log (file).", false, "never", "daily"),
                new A("stream", K.Enum, "Where console entries go (default stderr).", false, "stderr", "stdout"),
                new A("colour", K.Bool, "Colour console entries by level (default: when at a terminal)."),
                new A("time", K.Text, "The console's time format, such as HH:mm:ss.fff (default HH:mm:ss). Empty leaves the time out."),
                new A("url", K.Template, "Where entries are sent (http, from KScript.Net)."),
                new A("headers", K.Expression, "A map of request headers (http)."),
                new A("batch", K.Expression, "How many entries to send at once (http, default 20)."),
                new A("name", K.Text, "A name for the provider, used in error messages."),
                new A("replace", K.Bool, "true removes the providers already set up, including the default console one."))
            {
                Context = ElementContext.Script,
                Example = "<log_provider type=\"file\" path=\"logs/app.log\" format=\"json\" level=\"debug\" />\n<fn name=\"alert\" params=\"entry\"><echo>ALERT: {$entry.message}</echo></fn>\n<log_provider fn=\"alert\" level=\"error\" />",
            }, run =>
            {
                LogHub hub = run.Host.Logs;
                bool replace = run.Flag("replace");
                if (replace) hub.ClearProviders();
                else hub.EnsureConfigured(run.Interpreter, run.Module);

                var options = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in ProviderOptions)
                {
                    if (run.Has(name)) options[name] = run.Template(name);
                }
                if (run.Has("headers")) options["headers"] = run.Eval("headers");
                if (run.Has("batch")) options["batch"] = run.Eval("batch");
                if (run.Has("fn"))
                {
                    string fn = run.Raw("fn").Trim().TrimStart('@', '$');
                    options["fn"] = run.Scope.TryGet(fn, out object f) ? f : throw run.Error($"fn=\"{run.Raw("fn")}\": there is no function called {fn}. Declare it with <fn name=\"{fn}\" params=\"entry\" ...>.", "fn", "Name");
                }
                try
                {
                    hub.Add(options, run.Interpreter, run.Module, run.Module.ElementHint("log_provider"));
                }
                catch (KScriptException ex) when (ex.Offset == null)
                {
                    throw run.Error(ex.Words, null, ex.Type);
                }
                return null;
            });
        }

        private static MethodHandler LoggerMethod(string name) => (t, c) => ((LoggerValue)t).TryCallMethod(c, name, out object r) ? r : null;
    }
}
