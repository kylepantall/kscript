using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Runtime;

internal static class ConfigEvaluation
{
    private static Interpreter CreateInterpreter(KScriptHost parent, IReadOnlyDictionary<string, string> dotenv, bool script)
    {
        var host = new KScriptHost(parent.Error, parent.Error)
        {
            WorkingDirectory = parent.WorkingDirectory,
            FileRoot = parent.FileRoot,
            FileRootName = parent.FileRootName,
            CanWriteFiles = parent.CanWriteFiles,
            EvaluatingConfig = true,
            ConfigEnvironment = dotenv,
        };
        return new Interpreter(host, options: new InterpreterOptions
        {
            ProjectExtensions = script,
            MaxCallDepth = 100,
            MaxSteps = 100_000,
        });
    }

    internal static Dictionary<string, object> RunScript(KScriptHost host, string path, string environment,
        IReadOnlyDictionary<string, string> dotenv)
    {
        Interpreter interpreter = CreateInterpreter(host, dotenv, script: true);
        Module module = interpreter.Load(path);
        if (interpreter.Run(module) != 0 || !module.Exports.TryGetValue("configure", out object exported)
            || exported is not UserFunction function || function.Parameters.Count != 1)
        {
            throw new KScriptException("A configuration script must define configure(environment) and return a map.", "Config") { File = path };
        }

        try
        {
            var call = new CallContext(interpreter, function.Name, new object[] { environment ?? "development" }, module: module);
            return Values.Unwrap(function.Call(call)) as Dictionary<string, object>
                ?? throw new KScriptException("configure(environment) must return a map of settings.", "Config") { File = path };
        }
        catch (ExitSignal)
        {
            throw new KScriptException("configure(environment) must return a map of settings.", "Config") { File = path };
        }
    }

    internal static object ReadYaml(KScriptHost host, string path, string text, string environment,
        IReadOnlyDictionary<string, string> dotenv, Func<string, string> lookup)
    {
        Interpreter interpreter = null;
        Module module = null;
        return Yaml.ParseConfig(text, Path.GetFileName(path), (value, tagged, line, column) =>
        {
            if (!tagged && !value.Contains('{')) return DotEnv.Expand(value, lookup);
            Template template = TemplateParser.Parse(DotEnv.EscapeTemplateReferences(value), allowBareReferences: false);
            try
            {
                if (template.Errors.FirstOrDefault() is SyntaxError error)
                    throw new KScriptException(error.Message, "Config");
                if (tagged && !template.IsSingleInterpolation)
                    throw new KScriptException("!kscript needs one expression in braces, such as !kscript { 30s }.", "Config");
                if (!template.Expressions.Any())
                    return string.Concat(template.Segments.Select(segment => DotEnv.Expand(segment.Text, lookup)));

                if (interpreter == null)
                {
                    interpreter = CreateInterpreter(host, dotenv, script: false);
                    module = interpreter.CreateModule(path);
                    module.Globals.Declare("environment", environment ?? "development");
                }
                var context = new EvalContext(module, null);
                if (template.IsSingleInterpolation)
                    return interpreter.Evaluate(template.Expressions.First().Node, module.Globals, context);

                return string.Concat(template.Segments.Select(segment => segment.Kind == TemplateSegmentKind.Text
                    ? DotEnv.Expand(segment.Text, lookup)
                    : Values.ToStr(interpreter.Evaluate(segment.Node, module.Globals, context))));
            }
            catch (KScriptException error)
            {
                throw new KScriptException(error.Message, "Config", error) { File = path, Line = line, Column = column };
            }
        });
    }
}
