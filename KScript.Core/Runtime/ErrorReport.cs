using System.Reflection;
using System.Text;

namespace KScript.Runtime
{
    /// <summary>
    /// How kscript shows an error to a person: always as a KScript error (<c>file:line: Type: message</c>), never as a
    /// .NET exception with a stack trace. Exceptions that didn't come from a script are turned into the KScript error
    /// type that fits (FileNotFound, IO, Network, ...), or Internal when nothing does. The .NET details are shown only
    /// with <c>kscript --debug</c> or <c>KSCRIPT_DEBUG=1</c>.
    /// </summary>
    /// <summary>
    /// An exception from a part of kscript that isn't a script (kscript.json, a template, a page) that knows which
    /// KScript error it is, so <see cref="ErrorReport"/> shows it as that error.
    /// </summary>
    public interface IKScriptError
    {
        KScriptException ToKScriptError();
    }

    public static class ErrorReport
    {
        /// <summary>A KScript error from a message and a location such as <c>pages/index.html:12</c> (file, line, column).</summary>
        public static KScriptException At(string message, string type, string location, Exception inner = null)
        {
            var error = new KScriptException(message, type, inner);
            if (string.IsNullOrEmpty(location)) return error;
            var m = System.Text.RegularExpressions.Regex.Match(location, @"^(.*?):(\d+)(?::(\d+))?$");
            error.File = m.Success ? m.Groups[1].Value : location;
            if (m.Success) error.Line = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (m.Success && m.Groups[3].Success) error.Column = int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            return error;
        }

        /// <summary>The error type for an exception kscript didn't expect: a bug in kscript, not in the script.</summary>
        public const string InternalType = "Internal";

        /// <summary>The environment variable that turns on <see cref="Debug"/>.</summary>
        public const string DebugVariable = "KSCRIPT_DEBUG";

        /// <summary>True when errors also show the .NET exception and stack trace: --debug, or KSCRIPT_DEBUG set (and not 0 or false).</summary>
        public static bool Debug { get; set; } = IsOn(Environment.GetEnvironmentVariable(DebugVariable));

        internal static bool IsOn(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.Trim() != "0" && !value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);

        /// <summary>The exception as a KScript error. A KScriptException is returned as it is.</summary>
        public static KScriptException ToKScript(Exception ex)
        {
            ex = Unwrap(ex);
            if (ex is KScriptException k) return k;
            if (ex is IKScriptError own) return own.ToKScriptError();
            // getcwd fails with a bare "Unable to find the specified file" when kscript's folder was deleted or replaced.
            if (ex is FileNotFoundException && ex.StackTrace?.Contains("GetCwd", StringComparison.Ordinal) == true)
                return WorkingFolder.Missing(WorkingFolder.Remembered, ex);
            return Interpreter.Wrap(ex) ?? ex switch
            {
                ControlSignal => new KScriptException(ex.Message, "Context", ex),
                OperationCanceledException => new KScriptException("The work was stopped.", "Cancelled", ex),
                InsufficientExecutionStackException => new KScriptException("The script went too deep (too much recursion).", "Limit", ex),
                OutOfMemoryException => new KScriptException("kscript ran out of memory.", "Limit", ex),
                _ => new KScriptException(ex.Message, InternalType, ex),
            };
        }

        /// <summary>The exception a wrapper (a task, a reflection call, a static constructor) is carrying.</summary>
        internal static Exception Unwrap(Exception ex)
        {
            while (true)
            {
                switch (ex)
                {
                    case AggregateException a when a.InnerExceptions.Count == 1:
                        ex = a.InnerExceptions[0];
                        break;
                    case TargetInvocationException t when t.InnerException != null:
                        ex = t.InnerException;
                        break;
                    case TypeInitializationException ti when ti.InnerException != null:
                        ex = ti.InnerException;
                        break;
                    default:
                        return ex;
                }
            }
        }

        /// <summary>
        /// The error as plain text: <c>file:line: Type: message</c>, each cause on its own line, and with
        /// <see cref="Debug"/> the .NET details. This is what the CLI prints after its ERROR label and what error pages show.
        /// </summary>
        public static string Format(Exception ex, bool? debug = null)
        {
            KScriptException error = ToKScript(ex);
            var text = new StringBuilder(error.ToString());
            for (KScriptException cause = error.Cause; cause != null; cause = cause.Cause)
                text.Append("\nCaused by ").Append(cause.ToString());

            if (debug ?? Debug)
            {
                text.Append("\n\n.NET details (shown because of --debug or ").Append(DebugVariable).Append("):\n").Append(DotNetDetails(ex));
            }
            else if (error.Type == InternalType)
            {
                text.Append("\nThis is a problem in kscript, not in your script. Run the command again with --debug to see the details,")
                    .Append(" and report it at https://github.com/kylepantall/kscript/issues.");
            }
            return text.ToString();
        }

        /// <summary>Every exception in the chain, with its .NET type and stack trace.</summary>
        internal static string DotNetDetails(Exception ex)
        {
            var text = new StringBuilder();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (text.Length > 0) text.Append("--- inner exception ---\n");
                text.Append(e.GetType().FullName).Append(": ").Append(e.Message).Append('\n');
                if (e.StackTrace != null) text.Append(e.StackTrace.TrimEnd()).Append('\n');
            }
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Prints the error in the CLI's style: <c>  ERROR  message</c>, the label bold white on red when
        /// <paramref name="colour"/> is on, later lines lined up under the message. <paramref name="colour"/> defaults to
        /// on when <paramref name="output"/> is the console's error stream at a terminal (NO_COLOR and FORCE_COLOR are honoured).
        /// </summary>
        public static void Write(TextWriter output, Exception ex, bool? colour = null) => WriteLabelled(output, Format(ex), colour ?? UseColour(output));

        /// <summary>
        /// Prints what kscript was doing (<c>Could not start the server.</c>) with the error on the lines under it.
        /// </summary>
        public static void Write(TextWriter output, string doing, Exception ex, bool? colour = null) =>
            WriteLabelled(output, doing + "\n" + Format(ex), colour ?? UseColour(output));

        /// <summary>Prints a message after the ERROR label (for errors that aren't exceptions, such as a bad option).</summary>
        public static void WriteMessage(TextWriter output, string message, bool? colour = null) => WriteLabelled(output, message, colour ?? UseColour(output));

        private static void WriteLabelled(TextWriter output, string message, bool colour)
        {
            const string indent = "          ";
            string[] lines = message.Replace("\r\n", "\n").Split('\n');
            output.Write("  ");
            output.Write(colour ? "\u001b[1;97;41m ERROR \u001b[0m" : " ERROR ");
            output.Write(' ');
            output.WriteLine(lines[0]);
            for (int i = 1; i < lines.Length; i++) output.WriteLine(lines[i].Length == 0 ? string.Empty : indent + lines[i]);
            output.Flush();
        }

        internal static bool UseColour(TextWriter output)
        {
            if (Environment.GetEnvironmentVariable("NO_COLOR") != null) return false;
            if (Environment.GetEnvironmentVariable("FORCE_COLOR") != null) return true;
            return ReferenceEquals(output, Console.Error) && !Console.IsErrorRedirected;
        }
    }
}
