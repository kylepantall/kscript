using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>
    /// An error while loading or running a script: bad syntax, an unknown element, a missing variable, a failed
    /// <c>&lt;throw&gt;</c> and so on. <see cref="Type"/> is what <c>&lt;catch when="$err.type eq '...'"&gt;</c> sees.
    /// </summary>
    public class KScriptException : Exception
    {
        private readonly Wording wording;
        private bool? inCode;

        /// <summary>
        /// An error with a message. A message written as an interpolated string (<c>$"&lt;{name}&gt; needs a value."</c>)
        /// is a <see cref="Wording"/>: in a .kscript file it reads in that file's terms. A plain string is final text.
        /// </summary>
        public KScriptException(Wording message, string type = "Error", Exception inner = null) : base(message?.Xml, inner)
        {
            wording = message;
            Type = string.IsNullOrEmpty(type) ? "Error" : type;
        }

        /// <summary>The message, in the terms of the file the error is in: statements and arguments in .kscript, elements and attributes in XML.</summary>
        public override string Message => inCode == true && wording != null ? wording.Code : base.Message;

        /// <summary>The message for either form of script, to give to an error that wraps or repeats this one.</summary>
        public Wording Words => wording ?? Wording.Final(base.Message);

        /// <summary>Says which form of script the error is in, the first time that is known.</summary>
        internal void InFile(Module module)
        {
            if (inCode == null && module != null) inCode = module.IsCode;
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

        /// <summary>Where the statement is, for the error when nothing takes the signal (a break outside a loop).</summary>
        public Module Module { get; init; }

        /// <summary>The statement's offset in <see cref="Module"/>.</summary>
        public int? Offset { get; init; }
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

    /// <summary>Thrown by &lt;exit&gt; to end the script with an exit code.</summary>
    public sealed class ExitSignal : ControlSignal
    {
        public ExitSignal(int code) : base("exit") { Code = code; }

        public int Code { get; }
    }
}
