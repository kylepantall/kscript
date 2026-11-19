using System.Globalization;

using KScript.Runtime;

namespace KScript.Tests;

/// <summary>Runs a KScript 2 script in-process with its output and input as text.</summary>
public static class ScriptRunner
{
    /// <summary>Runs a whole script and returns what it wrote (newlines normalised to \n).</summary>
    public static string Run(string xml, string input = "", string[] args = null, string path = null, InterpreterOptions options = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output, input);
        host.Arguments = args ?? Array.Empty<string>();
        new Interpreter(host, options: options ?? new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) }).RunText(xml, path);
        return output.ToString().Replace("\r\n", "\n");
    }

    /// <summary>Runs the body inside &lt;kscript&gt;, with optional imports.</summary>
    public static string RunBody(string body, string input = "", params string[] packages) =>
        Run("<kscript>" + string.Concat(packages.Select(p => $"<import package=\"{p}\" />")) + body + "</kscript>", input);

    /// <summary>Runs the body and returns the error it stops with.</summary>
    public static KScriptException Fails(string body, params string[] packages) =>
        Assert.Throws<KScriptException>(() => RunBody(body, "", packages));

    /// <summary>The value of one expression, as echo prints it.</summary>
    public static string Eval(string expression, params string[] packages) =>
        RunBody($"<echo value=\"{System.Security.SecurityElement.Escape(expression).Replace("\n", "&#10;")}\" />", "", packages).TrimEnd('\n');

    public static string RepoFile(params string[] parts)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "KScript.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(new[] { dir }.Concat(parts).ToArray());
    }
}
