using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>
    /// An error type: a built-in such as <c>FileNotFound</c> or <c>Validation</c>, or one a script declares with
    /// <c>&lt;error name="NotFound" extends="Validation" status="404" fields="id: int" message="No item {$id}" /&gt;</c>
    /// (<c>error NotFound(status: 404) extends Validation { id: int }</c> in a .kscript file).
    /// <c>catch NotFound</c> catches it and every error type that extends it, <c>NotFound(id: 42)</c> makes one, and a
    /// <c>status</c> (its own or a parent's) is the HTTP status KScript.Http answers with when a route doesn't catch it.
    /// </summary>
    public sealed class ErrorType : IKObject, IDeclaredType, Dependencies.IDeclaredSource
    {
        private readonly string parentName;
        private ErrorType parent;
        private bool linked;
        private RecordType allFields;

        internal ErrorType(string name, string parentName, int? status, string description, DecodedText message = null, RecordType fields = null, Scope closure = null, Module module = null)
        {
            Name = name;
            this.parentName = parentName;
            OwnStatus = status;
            Description = description;
            Message = message;
            OwnFields = fields;
            Closure = closure;
            Module = module;
            IsBuiltIn = closure == null;
        }

        public string Name { get; }

        public string Description { get; }

        /// <summary>True for the error types KScript itself raises, such as Type and FileNotFound.</summary>
        public bool IsBuiltIn { get; }

        /// <summary>The type this one extends, or null for <c>Error</c>.</summary>
        public ErrorType Parent
        {
            get
            {
                Link();
                return parent;
            }
        }

        /// <summary>The HTTP status: this type's own, or the nearest parent's.</summary>
        public int? Status => OwnStatus ?? Parent?.Status;

        internal int? OwnStatus { get; }

        /// <summary>The message template, with the fields as variables: <c>No item {$id}</c>. Inherited when not set.</summary>
        internal DecodedText Message { get; }

        internal RecordType OwnFields { get; }

        internal Scope Closure { get; }

        internal int? DeclarationOffset { get; init; }

        /// <summary>Where it was declared, so reaching it can be recorded (see <see cref="Dependencies.DependencyTrace"/>).</summary>
        Module Dependencies.IDeclaredSource.DeclaredIn => DeclarationOffset == null ? null : Module;
        int Dependencies.IDeclaredSource.DeclaredAt => DeclarationOffset ?? 0;

        internal Module Module { get; }

        /// <summary>The fields an error of this type has: the parent's, then its own.</summary>
        public RecordType Fields
        {
            get
            {
                if (allFields != null) return allFields;
                var record = new RecordType(Name, Description, ExtraKeys.Error, Closure, Module);
                foreach (FieldDefinition f in Parent?.Fields.Fields ?? Array.Empty<FieldDefinition>()) record.SetField(f);
                foreach (FieldDefinition f in OwnFields?.Fields ?? Array.Empty<FieldDefinition>()) record.SetField(f);
                return allFields = record;
            }
        }

        /// <summary>This type and the types it extends, nearest first, ending with Error.</summary>
        public IEnumerable<ErrorType> Chain()
        {
            for (ErrorType t = this; t != null; t = t.Parent) yield return t;
        }

        /// <summary>True if this type is <paramref name="name"/> or extends it.</summary>
        public bool IsA(string name) => Chain().Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Finds the parent the first time it is needed, so error types can extend ones declared further down.</summary>
        private void Link()
        {
            if (linked) return;
            linked = true;
            if (parentName == null) return;
            ErrorType found = ErrorTypes.Find(parentName, Closure);
            if (found == null)
            {
                linked = false;
                string close = Values.Closest(parentName, ErrorTypes.Names(Closure));
                throw new KScriptException(Wording.Of($"The error {Name} extends '{parentName}', which isn't an error type.") + (close != null ? $" Did you mean {close}?" : Wording.Of($" Declare it with <error name=\"{parentName}\">.")), "Name");
            }
            for (ErrorType t = found; t != null; t = t.parent)
            {
                if (t == this)
                {
                    linked = false;
                    throw new KScriptException($"The error {Name} extends itself through {parentName}. Each error type extends one that doesn't lead back to it.", "Syntax");
                }
            }
            parent = found;
        }

        /// <summary>
        /// Makes an error of this type: <c>NotFound(id: 42)</c>. Named arguments are its fields, plus <c>message</c>
        /// (instead of the type's message) and <c>cause</c> (the error that led to this one). One positional argument
        /// is the message.
        /// </summary>
        public KScriptException Create(Interpreter interpreter, IReadOnlyList<object> args, IReadOnlyDictionary<string, object> named)
        {
            args ??= Array.Empty<object>();
            named ??= new Dictionary<string, object>();
            if (args.Count > 1) throw new KScriptException($"{Name}() takes its fields by name, such as {Name}({Example()}), and at most one message.", "Argument");
            string message = args.Count == 1 ? Values.ToStr(args[0]) : null;
            object cause = null;
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in named)
            {
                if (pair.Key == "message") message = pair.Value == null ? null : Values.ToStr(pair.Value);
                else if (pair.Key == "cause") cause = pair.Value;
                else fields[pair.Key] = pair.Value;
            }

            IDictionary<string, object> values = fields;
            RecordType shape = Fields;
            if (shape.Fields.Count > 0 || !IsBuiltIn)
            {
                try
                {
                    values = (Record)Shapes.MapAs(interpreter, fields, shape, Name + "()", Closure, Module);
                }
                catch (KScriptException ex) when (ex.Type == "Shape")
                {
                    var problems = (List<object>)ex.Fields["problems"];
                    string lines = string.Concat(problems.Cast<Dictionary<string, object>>().Select(p =>
                        $"\n  {Values.ToStr(p["path"]).Replace("$.", string.Empty)}: {p["message"]}"));
                    throw new KScriptException($"{Name}() was given fields that don't fit it:{lines}"
                        + (shape.Fields.Count > 0 ? $"\nIts fields are: {string.Join(", ", shape.Fields.Select(f => f.Name + (f.Optional ? "?" : string.Empty) + ": " + f.Type))}." : $"\n{Name} has no fields."), "Argument");
                }
            }

            KScriptException inner = cause switch
            {
                null => null,
                ErrorValue e => e.Exception,
                Dictionary<string, object> map => ErrorTypes.FromMap(map),
                string text => new KScriptException(text),
                _ => throw new KScriptException($"The cause of a {Name} is an error, such as $err in a catch, not {Values.Describe(cause)}.", "Argument"),
            };

            message ??= MessageFor(interpreter, values);
            var error = new KScriptException(message, Name, inner) { Declared = this };
            foreach (KeyValuePair<string, object> pair in values) error.Fields[pair.Key] = pair.Value;
            return error;
        }

        private string MessageFor(Interpreter interpreter, IDictionary<string, object> values)
        {
            foreach (ErrorType t in Chain())
            {
                if (t.Message == null) continue;
                Scope scope = (t.Closure ?? Closure ?? new Scope()).Child();
                foreach (KeyValuePair<string, object> pair in values) scope.Declare(pair.Key, pair.Value);
                return interpreter.Interpolate(t.Message, scope, t.Module ?? Module, bareReferences: true);
            }
            return IsBuiltIn ? Name : Description ?? Name;
        }

        private string Example() => Fields.Fields.Count > 0 ? string.Join(", ", Fields.Fields.Select(f => f.Name + ": ...")) : "message: '...'";

        public string TypeName => "error_type";

        public string ToText() => Name;

        public bool TryGetMember(string name, out object value)
        {
            switch (name)
            {
                case "name":
                    value = Name;
                    return true;
                case "extends":
                    value = Parent?.Name;
                    return true;
                case "status":
                    value = Status is int s ? (double)s : null;
                    return true;
                case "description":
                    value = Description;
                    return true;
                case "fields":
                    value = FieldList();
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "new":
                    throw new KScriptException($"->new() is gone: call the error type to make an error, {Name}(...).", "Retired");
                default:
                    result = null;
                    return false;
            }
        }

        private List<object> FieldList() => Fields.Fields.Select(f => (object)new Dictionary<string, object>
        {
            ["name"] = f.Name,
            ["type"] = f.Type.ToString(),
            ["optional"] = f.Optional,
            ["description"] = f.Description,
        }).ToList();

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
        };

        internal static readonly (string Name, string Type, string Description)[] ValueFields =
        {
            ("name", "string", "The error type's name."),
            ("extends", "string", "The error type it extends, or null."),
            ("status", "number", "The HTTP status errors of this type carry, such as 404, or null."),
            ("description", "string", "What the error type is for, or null."),
            ("fields", "list", "The error's fields (its own and the ones it inherits): name, type, optional and description."),
        };
    }

    /// <summary>
    /// A caught error: what <c>catch NotFound as e</c> and <c>&lt;expect_error as&gt;</c> declare. It is a map
    /// (<c>$e.type</c>, <c>$e.message</c>, <c>$e.status</c>, <c>$e.cause</c>, its fields), so it prints and converts to
    /// JSON like one, and it remembers the error so <c>throw $e</c> rethrows it unchanged.
    /// </summary>
    public sealed class ErrorValue : Dictionary<string, object>
    {
        internal ErrorValue(KScriptException exception) : base(StringComparer.Ordinal) => Exception = exception;

        public KScriptException Exception { get; }

        /// <summary>True if the error is <paramref name="type"/> or a type that extends it.</summary>
        public bool Is(string type) => ErrorTypes.Matches(Exception, type);
    }

    /// <summary>The built-in error types, how they extend each other, and helpers for matching errors by type.</summary>
    public static class ErrorTypes
    {
        /// <summary>The root of every error type: <c>catch Error</c> catches everything.</summary>
        public const string Root = "Error";

        private static readonly Dictionary<string, ErrorType> builtIn = new Dictionary<string, ErrorType>(StringComparer.OrdinalIgnoreCase);

        static ErrorTypes()
        {
            void Add(string name, string parent, string description, int? status = null)
            {
                Conventions.Enforce(Conventions.ErrorTypeName(name));
                builtIn[name] = new ErrorType(name, parent, status, description);
            }

            Add(Root, null, "Any error. Every error type extends Error.");
            Add("Syntax", Root, "A script that can't be read: bad syntax, an unknown element or a misplaced one.");
            Add("Type", Root, "A value of the wrong type, such as text where a number is needed.");
            Add("Shape", "Type", "Data that doesn't fit a <type>. $err.problems lists each problem with its JSON path.");
            Add("Name", Root, "A name that doesn't exist: a variable, function, field or method.");
            Add("Null", Root, "Using null where a value is needed.");
            Add("Argument", Root, "A call with the wrong arguments.");
            Add("Value", Root, "A value that isn't allowed here.");
            Add("Const", Root, "Changing a const.");
            Add("Math", Root, "A calculation with no answer, such as dividing by zero, or maths in a document that can't be read.");
            Add("Index", "Value", "A position outside a list.");
            Add("Format", Root, "Text that isn't in the expected format, such as a date.");
            Add("Json", "Format", "Text that isn't valid JSON.");
            Add("Yaml", "Format", "Text that isn't valid YAML.");
            Add("Import", Root, "An import that can't be found or a package that isn't imported.");
            Add("Config", Root, "A missing or invalid setting.");
            Add("Input", Root, "Input that couldn't be read.");
            Add("Limit", Root, "A limit was reached, such as too much recursion.");
            Add("Context", Root, "Something used where it doesn't work.");
            Add("Platform", Root, "Something this operating system can't do.");
            Add("Cancelled", Root, "The script was stopped.");
            Add("Retired", Root, "A KScript 1 feature that KScript 2 doesn't have.");
            Add("IO", Root, "Reading or writing a file failed.");
            Add("FileNotFound", "IO", "A file or folder that doesn't exist.");
            Add("Permission", "IO", "A file outside the allowed folder, or a change to read-only files.");
            Add("Access", "IO", "The operating system refused access to a file.");
            Add("Network", Root, "A request that failed or got an error status. $err.status is the status, when there is one.");
            Add("Timeout", "Network", "A request that took too long.");
            Add("Http", Root, "A mistake in a KScript.Http app, or a request that failed.");
            Add("AI", Root, "A call to a language model that failed.");
            Add("UI", Root, "A desktop window that can't be opened, such as on a machine without the window library.");
            Add("Reference", Root, "A document that refers to a section, figure or source it doesn't have, or uses an id twice.");
            Add("Internal", Root, "A fault in KScript itself, not in the script.");
            Add("Assertion", Root, "A failed check in a test.");
            Add("Validation", Root, "Input that isn't valid. Answers 422 in a KScript.Http route.", 422);
            Add("BadRequest", Root, "A request that can't be handled as sent. Answers 400.", 400);
            Add("Unauthorized", Root, "A request that needs someone to sign in. Answers 401.", 401);
            Add("Forbidden", Root, "A request the signed-in user isn't allowed to make. Answers 403.", 403);
            Add("NotFound", Root, "Something that doesn't exist. Answers 404.", 404);
            Add("Conflict", Root, "A change that clashes with the current state. Answers 409.", 409);
            Add("JobTimeout", "Timeout", "A queued job that ran longer than its timeout.");
            Add("JobFailed", Root, "A queued job that used up its tries. $err.cause is the last error.");
            Add("TooManyRequests", Root, "Too many requests in a short time, such as failed sign-ins. Answers 429, with Retry-After when the error has a retry_after field.", 429);
            Add("ModelNotFound", "NotFound", "A KScript.Data record that isn't in the database: ->find_or_fail() and ->first_or_fail(). $err.model and $err.id say which. Answers 404.", 404);
            Add("UniqueViolation", "Conflict", "A save that would give two records the same value in a unique field. $err.fields names them. Answers 409.", 409);
            Add("ForeignKeyViolation", "Conflict", "A save or delete that would leave a record pointing at one that isn't there. Answers 409.", 409);
            Add("Query", Root, "A database query that failed, or a condition that can't be turned into SQL.");
            Add("Connection", Root, "A database that can't be reached or opened. Answers 503.", 503);
            Add("Migration", Root, "A migration that can't be read or run, or a kscript db command that can't go ahead.");
        }

        /// <summary>
        /// Built-in error types that had another name, with the name each has now. No error type's name ends in
        /// "Error" (they are all errors), so <c>catch QueryError</c> is told the new name instead of never matching.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["QueryError"] = "Query",
            ["ConnectionError"] = "Connection",
        };

        /// <summary>What to say about a renamed built-in type, or null if <paramref name="name"/> isn't one.</summary>
        public static string RenamedMessage(string name) =>
            name != null && Renamed.TryGetValue(name, out string now) ? $"The error type {name} was renamed to {now}." : null;

        /// <summary>The built-in error types, by name.</summary>
        public static IReadOnlyDictionary<string, ErrorType> BuiltIn => builtIn;

        /// <summary>A declared error type visible in <paramref name="scope"/>, or a built-in one, or null.</summary>
        public static ErrorType Find(string name, Scope scope)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (scope != null && scope.TryGet(name, out object value) && value is ErrorType declared) return declared;
            return builtIn.TryGetValue(name, out ErrorType b) ? b : null;
        }

        /// <summary>
        /// Like <see cref="Find"/>, but a built-in type only matches with its exact case, so a bare <c>Name</c> in an
        /// expression is the error type and <c>name</c> stays a mistake.
        /// </summary>
        public static ErrorType FindExact(string name, Scope scope)
        {
            if (scope != null && scope.TryGet(name, out object value) && value is ErrorType declared) return declared;
            return builtIn.TryGetValue(name, out ErrorType b) && b.Name == name ? b : null;
        }

        /// <summary>The names of the error types visible in <paramref name="scope"/>, built-in ones included.</summary>
        public static IEnumerable<string> Names(Scope scope)
        {
            IEnumerable<string> declared = scope == null ? Enumerable.Empty<string>()
                : scope.Names().Where(n => scope.TryGet(n, out object v) && v is ErrorType);
            return declared.Concat(builtIn.Keys).Distinct();
        }

        /// <summary>The type of an error: the declared one it was made from, or the built-in one its name matches.</summary>
        public static ErrorType TypeOf(KScriptException ex) => ex.Declared ?? (builtIn.TryGetValue(ex.Type, out ErrorType b) ? b : null);

        /// <summary>The names an error answers to, nearest first: its type, the types that type extends, then Error.</summary>
        public static IEnumerable<string> Chain(KScriptException ex)
        {
            ErrorType type = TypeOf(ex);
            if (type == null) return new[] { ex.Type, Root }.Distinct(StringComparer.OrdinalIgnoreCase);
            return type.Chain().Select(t => t.Name);
        }

        /// <summary>True if the error is <paramref name="type"/> or extends it. Names are matched without case.</summary>
        public static bool Matches(KScriptException ex, string type) =>
            Chain(ex).Any(n => string.Equals(n, type?.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>The HTTP status the error's type (or a type it extends) carries, or null.</summary>
        public static int? StatusOf(KScriptException ex) => TypeOf(ex)?.Status;

        /// <summary>An error made again from a map like the ones <see cref="KScriptException.ToValue"/> gives.</summary>
        internal static KScriptException FromMap(Dictionary<string, object> map)
        {
            string type = map.TryGetValue("type", out object t) && t != null ? Values.ToStr(t) : Root;
            string message = map.TryGetValue("message", out object m) && m != null ? Values.ToStr(m) : type;
            var error = new KScriptException(message, type);
            foreach (KeyValuePair<string, object> pair in map)
            {
                if (pair.Key is not ("type" or "message" or "line" or "column" or "file" or "status" or "cause")) error.Fields[pair.Key] = pair.Value;
            }
            return error;
        }

        [ThreadStatic]
        private static Stack<KScriptException> handling;

        /// <summary>The error the innermost running <c>catch</c> is handling, which a bare <c>throw</c> rethrows.</summary>
        internal static KScriptException Handling => handling != null && handling.Count > 0 ? handling.Peek() : null;

        internal static IDisposable Handle(KScriptException ex)
        {
            (handling ??= new Stack<KScriptException>()).Push(ex);
            return new Popper();
        }

        private sealed class Popper : IDisposable
        {
            public void Dispose() => handling.Pop();
        }
    }
}
