using System.Globalization;
using System.Text;


namespace KScript.Tests;

/// <summary>
/// Runs a KScript document in-process and captures what it writes to the console.
/// The interpreter writes to <see cref="Console.Out"/>, so tests must not run in parallel.
/// </summary>
public static class ScriptRunner
{
    public static string Run(string xml, string input = "", string[] args = null)
    {
        TextWriter originalOut = Console.Out;
        TextReader originalIn = Console.In;
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };

        try
        {
            Console.SetOut(output);
            Console.SetIn(new StringReader(input));

            var parser = new KScriptParser();
            parser.SetXML(xml);
            parser.CustomArguments = args ?? Array.Empty<string>();
            parser.Properties.ForceQuiet = true;
            parser.Parse();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetIn(originalIn);
        }

        return output.ToString().Replace("\r\n", "\n");
    }

    /// <summary>Wraps the body in a quiet kscript root element with exceptions thrown to the caller.</summary>
    public static string RunBody(string body, string input = "", string extraAttributes = "")
        => Run($"<kscript dynamic_defs=\"yes\" throw_exceptions=\"yes\" {extraAttributes}>{body}</kscript>", input);
}
