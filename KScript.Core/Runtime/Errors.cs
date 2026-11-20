namespace KScript.Runtime
{
    /// <summary>
    /// An error while loading or running a script: bad syntax, an unknown element, a missing variable, a failed
    /// <c>&lt;throw&gt;</c> and so on. <see cref="Type"/> is what <c>&lt;catch when="$err.type eq '...'"&gt;</c> sees.
    /// </summary>
    public class KScriptException : Exception
    {
        public KScriptException(string message, string type = "Error", Exception inner = null) : base(message, inner)
        {
            Type = string.IsNullOrEmpty(type) ? "Error" : type;
        }

        /// <summary>The error type, such as Error, Syntax, Type, Name, FileNotFound or a name from &lt;throw type&gt;.</summary>
        public string Type { get; }

        /// <summary>The script file, when known.</summary>
        public string File { get; set; }

        /// <summary>1-based line and column, or 0 when unknown.</summary>
        public int Line { get; set; }

        public int Column { get; set; }

        /// <summary>The document offset the error points at, before it is turned into a line and column.</summary>
        internal int? Offset { get; set; }

        /// <summary>The script <see cref="Offset"/> belongs to, when it isn't the one that reports the error.</summary>
        internal Module OffsetModule { get; set; }

        /// <summary>The declared error type this error was made from (<c>NotFound(id: 42)</c>), or null for a built-in or ad hoc one.</summary>
        public ErrorType Declared { get; set; }

        /// <summary>The error that led to this one: <c>NotFound(id: 1, cause: $err)</c>.</summary>
        public KScriptException Cause => InnerException as KScriptException;

        /// <summary>Extra fields from &lt;throw&gt;, available on the caught error value.</summary>
        public Dictionary<string, object> Fields { get; } = new Dictionary<string, object>();

        public bool HasLocation => Line > 0;

        public string Location => File == null && Line == 0 ? null
            : $"{(File != null ? System.IO.Path.GetFileName(File) : "script")}{(Line > 0 ? $":{Line}" : string.Empty)}{(Column > 0 ? $":{Column}" : string.Empty)}";

        /// <summary>
        /// The error as a map, which is what <c>&lt;catch as="err"&gt;</c> declares: type, message, line, column, file,
        /// status (from its type, for KScript.Http), cause, and its fields.
        /// </summary>
        public ErrorValue ToValue()
        {
            var map = new ErrorValue(this)
            {
                ["type"] = Type,
                ["message"] = Message,
                ["line"] = Line > 0 ? (double)Line : null,
                ["column"] = Column > 0 ? (double)Column : null,
                ["file"] = File,
                ["status"] = ErrorTypes.StatusOf(this) is int status ? (double)status : null,
                ["cause"] = Cause?.ToValue(),
            };
            foreach (var kv in Fields) map[kv.Key] = kv.Value;
            return map;
        }

        /// <summary>The message with its location in front, as printed by the CLI.</summary>
        public override string ToString() => Location == null ? $"{Type}: {Message}" : $"{Location}: {Type}: {Message}";
    }

    /// <summary>The base of break, continue, return and exit, which unwind the interpreter but are not errors.</summary>
    public abstract class ControlSignal : Exception
    {
        protected ControlSignal(string what) : base(what) { }
    }

    public sealed class BreakSignal : ControlSignal
    {
        public BreakSignal() : base("<break/> outside a loop.") { }
    }

    public sealed class ContinueSignal : ControlSignal
    {
        public ContinueSignal() : base("<continue/> outside a loop.") { }
    }

    public sealed class ReturnSignal : ControlSignal
    {
        public ReturnSignal(object value) : base("<return> outside a function.") { Value = value; }

        public object Value { get; }
    }

    /// <summary>Thrown by &lt;exit&gt; and @exit() to end the script with an exit code.</summary>
    public sealed class ExitSignal : ControlSignal
    {
        public ExitSignal(int code) : base("exit") { Code = code; }

        public int Code { get; }
    }
}
