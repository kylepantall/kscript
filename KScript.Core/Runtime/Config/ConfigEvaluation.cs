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
}
