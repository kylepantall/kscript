using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>A function value: a lambda, a &lt;fn&gt; function, or a command or method passed as a value.</summary>
    public interface ICallable
    {
        string Name { get; }

        object Call(CallContext call);
    }

    /// <summary>One parameter of a &lt;fn&gt;: <c>who</c>, <c>count: int = 1</c>.</summary>
    public sealed class Parameter
    {
        public Parameter(string name, string type, ExprNode defaultValue, string defaultText = null)
        {
            Name = name;
            Type = type;
            Default = defaultValue;
            DefaultText = defaultText;
        }

        public string Name { get; }
        public string Type { get; }
        public ExprNode Default { get; }

        /// <summary>The default as written. A typed parameter's default can be raw text for its type (size: Size = 1920x1080), which needn't parse.</summary>
        public string DefaultText { get; }

        public bool HasDefault => Default != null || DefaultText != null;

        public override string ToString() => Name + (Type != null ? ": " + Type : string.Empty) + (Default != null ? " = …" : string.Empty);

        /// <summary>Parses <c>who, greeting = 'Hello'</c> or <c>count: int = 1</c>.</summary>
        public static List<Parameter> ParseList(string text, out string error)
        {
            error = null;
            var result = new List<Parameter>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (string part in SplitTopLevel(text, ','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                ExprNode defaultValue = null;
                string defaultText = null;
                int eq = IndexOfTopLevel(p, '=');
                if (eq >= 0)
                {
                    defaultText = p.Substring(eq + 1).Trim();
                    ParseResult parsed = ExpressionParser.ParseExpression(p.Substring(eq + 1));
                    // A typed parameter's default that isn't an expression may be raw text for its type, checked when it is used.
                    if (!parsed.Success && !p.Substring(0, eq).Contains(':'))
                    {
                        error = $"The default value of '{p.Substring(0, eq).Trim()}' isn't a valid expression: {parsed.Errors[0].Message}";
                        return result;
                    }
                    defaultValue = parsed.Success ? parsed.Root : null;
                    p = p.Substring(0, eq).Trim();
                }

                string type = null;
                int colon = p.IndexOf(':');
                if (colon >= 0)
                {
                    type = p.Substring(colon + 1).Trim();
                    p = p.Substring(0, colon).Trim();
                    // Any other name is an <enum> or a <type>, looked up when the function is called.
                    if (TypeRef.Parse(type) == null)
                    {
                        error = $"Unknown type '{type}' for parameter '{p}'. Use one of: {string.Join(", ", Values.DeclarableTypes)}, or the name of an <enum> or <type> (with [] for a list and ? to allow null).";
                        return result;
                    }
                }

                string name = p.TrimStart('$');
                if (name.Length == 0 || !Lexer.IsIdentStart(name[0]) || !name.All(Lexer.IsIdentChar))
                {
                    error = $"'{part.Trim()}' isn't a parameter name. Write names like who, count: int, or greeting = 'Hello'.";
                    return result;
                }
                result.Add(new Parameter(name, type, defaultValue, defaultText));
            }
            return result;
        }

        internal static IEnumerable<string> SplitTopLevel(string text, char separator)
        {
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == separator && depth == 0)
                {
                    yield return text.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return text.Substring(start);
        }

        private static int IndexOfTopLevel(string text, char target)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == target && depth == 0) return i;
            }
            return -1;
        }
    }

    /// <summary><c>fn($x) => expression</c>, closed over the scope it was written in.</summary>
    public sealed class Lambda : ICallable
    {
        internal Lambda(LambdaNode node, Scope closure, EvalContext context, string source)
        {
            Node = node;
            Closure = closure;
            Context = context;
            Source = source;
        }

        public string Name => "fn";
        internal LambdaNode Node { get; }
        internal Scope Closure { get; }
        internal EvalContext Context { get; }
        public string Source { get; }

        public IReadOnlyList<string> Parameters => Node.Parameters.Select(p => p.Name).ToList();

        public object Call(CallContext call)
        {
            Scope scope = Closure.Child();
            IReadOnlyList<object> args = call.Named.Count == 0 && !call.Args.Any(a => a is BareWord) ? call.Args : ArgumentBinder.BindByName(call, Parameters, out _, lenient: true);
            for (int i = 0; i < Node.Parameters.Count; i++)
            {
                scope.Declare(Node.Parameters[i].Name, i < args.Count ? args[i] : null);
            }
            return call.Interpreter.Evaluate(Node.Body, scope, Context);
        }

        public override string ToString() => Source ?? "fn(" + string.Join(", ", Parameters.Select(p => "$" + p)) + ") => …";
    }

    /// <summary>A function declared with &lt;fn name params&gt;, called as <c>@name(args)</c>.</summary>
    public sealed class UserFunction : ICallable
    {
        internal UserFunction(string name, List<Parameter> parameters, MarkupElement element, ExprNode value, EvalContext valueContext, Scope closure, Module module, string description)
        {
            Name = name;
            Parameters = parameters;
            Element = element;
            ValueExpression = value;
            ValueContext = valueContext;
            Closure = closure;
            Module = module;
            Description = description;
        }

        public string Name { get; }
        public IReadOnlyList<Parameter> Parameters { get; }
        public string Description { get; }
        internal MarkupElement Element { get; }
        internal ExprNode ValueExpression { get; }
        internal EvalContext ValueContext { get; }
        internal Scope Closure { get; }
        public Module Module { get; }

        /// <summary>A default written as raw text for the parameter's type: who: handle = @guest, size: Size = medium.</summary>
        private string RawDefault(Parameter p)
        {
            string kind = Shapes.RawTextKind(TypeRef.Parse(p.Type), Closure, Module);
            if (kind == null || p.DefaultText == null) return null;
            return p.Default == null || Interpreter.IsRawWord(p.Default, kind, Closure, Module) || Interpreter.LooksRaw(p.DefaultText, kind) ? p.DefaultText : null;
        }

        public object Call(CallContext call)
        {
            Interpreter interpreter = call.Interpreter;
            Scope scope = Closure.Child();
            var enums = new Dictionary<string, EnumType>();
            var enumLists = new Dictionary<string, EnumType>();
            foreach (Parameter p in Parameters.Where(p => p.Type != null))
            {
                TypeRef t = TypeRef.Parse(p.Type);
                if (t.ListDepth == 1 && !t.Optional && !t.IsBuiltIn && t.Inline == null)
                {
                    // sizes: Size[] takes [large, small] with bare values.
                    try
                    {
                        if (Shapes.Resolve(TypeRef.Parse(t.Name), Closure, Module) is EnumType item) enumLists[p.Name] = item;
                    }
                    catch (KScriptException)
                    {
                        // Reported when the argument is converted below.
                    }
                    continue;
                }
                if (!t.IsSimple || t.IsBuiltIn || t.Args != null) continue;
                object declared;
                try
                {
                    declared = Shapes.Resolve(t, Closure, Module);
                }
                catch (KScriptException ex)
                {
                    throw new KScriptException(ex.Message.Replace($"Unknown type '{t.Name}'.", $"Unknown type '{t.Name}' for a parameter of @{Name}()."), ex.Type);
                }
                if (declared is EnumType e) enums[p.Name] = e;
            }
            IReadOnlyList<object> args = ArgumentBinder.BindByName(call,
                Parameters.Select(p => new ArgumentBinder.Slot(p.Name, false, Values: enums.TryGetValue(p.Name, out EnumType e) ? e.Names : null,
                    TypeName: e?.Name ?? enumLists.GetValueOrDefault(p.Name)?.Name, ItemValues: enumLists.GetValueOrDefault(p.Name)?.Names,
                    RawText: p.Type == null || e != null ? null : Shapes.RawTextKind(TypeRef.Parse(p.Type), Closure, Module))).ToList(), out HashSet<int> given);
            for (int i = 0; i < Parameters.Count; i++)
            {
                Parameter p = Parameters[i];
                object value;
                if (given.Contains(i)) value = args[i];
                else if (p.HasDefault && p.Type != null && RawDefault(p) is string raw) value = raw;
                else if (p.Default != null) value = interpreter.Evaluate(p.Default, scope, new EvalContext(Module, null));
                else if (p.DefaultText != null) throw new KScriptException($"The default of '{p.Name}' in @{Name}() isn't a value or a {p.Type}: {p.DefaultText}.", "Syntax");
                else throw new KScriptException($"@{Name}() needs a value for '{p.Name}'.", "Argument");
                if (enums.TryGetValue(p.Name, out EnumType type)) value = type.Check(value, $"The '{p.Name}' argument of @{Name}()");
                else if (p.Type != null) value = Shapes.Convert(interpreter, value, p.Type, $"The '{p.Name}' argument of @{Name}()", Closure, Module);
                scope.Declare(p.Name, value);
            }

            if (ValueExpression != null)
            {
                return interpreter.Evaluate(ValueExpression, scope, ValueContext);
            }

            try
            {
                interpreter.ExecuteChildren(Element, scope, Module);
            }
            catch (ReturnSignal r)
            {
                return r.Value;
            }
            return null;
        }

        public override string ToString() => $"fn {Name}({string.Join(", ", Parameters)})";
    }

    /// <summary>A command or method used as a value (<c>$people->map(@greet)</c>), or a function written in C#.</summary>
    public sealed class NativeFunction : ICallable
    {
        private readonly Func<CallContext, object> body;

        public NativeFunction(string name, Func<CallContext, object> body)
        {
            Name = name;
            this.body = body;
        }

        public string Name { get; }

        public object Call(CallContext call) => body(call);

        public override string ToString() => "@" + Name;
    }

    /// <summary>
    /// Everything a command, method or function call sees: the arguments, the interpreter, and helpers that convert
    /// arguments and report errors with the call's name.
    /// </summary>
    public sealed class CallContext
    {
        private static readonly IReadOnlyDictionary<string, object> NoNamed = new Dictionary<string, object>();

        public CallContext(Interpreter interpreter, string name, IReadOnlyList<object> args, IReadOnlyDictionary<string, object> named = null,
            Scope scope = null, Module module = null, bool isMethod = false, IReadOnlyCollection<int> skipped = null)
        {
            Skipped = skipped ?? Array.Empty<int>();
            Interpreter = interpreter;
            Name = name;
            Args = args ?? Array.Empty<object>();
            Named = named ?? NoNamed;
            Scope = scope;
            Module = module;
            IsMethod = isMethod;
        }

        public Interpreter Interpreter { get; }
        public string Name { get; }
        public IReadOnlyList<object> Args { get; }
        public IReadOnlyDictionary<string, object> Named { get; }

        /// <summary>The scope of the caller, when known.</summary>
        public Scope Scope { get; }

        /// <summary>The script the call is in, when known.</summary>
        public Module Module { get; }

        public bool IsMethod { get; }

        /// <summary>
        /// Optional positions left empty because a later parameter was given by name (<c>@cmd('a', third: 3)</c>
        /// skips the second). They read as null, and <see cref="Has(int)"/> is false for them, so fallbacks apply.
        /// </summary>
        public IReadOnlyCollection<int> Skipped { get; }

        /// <summary>The parameter names of the signature the arguments were bound to, when known (for error messages).</summary>
        public IReadOnlyList<string> ParameterNames { get; init; }

        /// <summary>Where a command was called, as written in the script, or null when that isn't known (a command passed as a value).</summary>
        public CallSite Site { get; internal set; }

        public int Count => Args.Count;
        public KScriptHost Host => Interpreter.Host;

        /// <summary>How the call is shown in errors: @name() or ->name().</summary>
        public string Display => IsMethod ? $"->{Name}()" : $"@{Name}()";

        public object Arg(int i) => i < Args.Count ? Args[i] : null;

        /// <summary>A positional argument, or the named argument of that name.</summary>
        public object Arg(int i, string name) => Has(i) ? Args[i] : Named.TryGetValue(name, out object v) ? v : null;

        public bool Has(int i) => i < Args.Count && !Skipped.Contains(i);

        public bool Has(int i, string name) => Has(i) || Named.ContainsKey(name);

        public string Str(int i) => Values.ToStr(Arg(i));

        public string Str(int i, string fallback) => Has(i) ? Values.ToStr(Arg(i)) : fallback;

        public double Num(int i) => Values.ToNumber(Need(i), $"Argument {i + 1} of {Display}");

        public double Num(int i, double fallback) => Has(i) && Arg(i) != null ? Values.ToNumber(Arg(i), $"Argument {i + 1} of {Display}") : fallback;

        public int Int(int i) => (int)Math.Truncate(Num(i));

        public int Int(int i, int fallback) => Has(i) && Arg(i) != null ? (int)Math.Truncate(Num(i)) : fallback;

        public bool Bool(int i, bool fallback = false) => Has(i) ? Values.Truthy(Arg(i)) : fallback;

        public ICallable Fn(int i) => Need(i) as ICallable ?? throw Error($"{Display} needs a function like fn($x) => ... as argument {i + 1}, but got {Values.Describe(Arg(i))}.");

        public List<object> List(int i) => Need(i) as List<object> ?? throw Error($"{Display} needs a list as argument {i + 1}, but got {Values.Describe(Arg(i))}.");

        public Dictionary<string, object> Map(int i) => Need(i) as Dictionary<string, object> ?? throw Error($"{Display} needs a map as argument {i + 1}, but got {Values.Describe(Arg(i))}.");

        public object Need(int i)
        {
            if (i >= Args.Count) throw Error($"{Display} needs at least {i + 1} argument{(i == 0 ? string.Empty : "s")}.", "Argument");
            if (Skipped.Contains(i))
                throw Error($"{Display} needs a value for {(ParameterNames != null && i < ParameterNames.Count ? $"'{ParameterNames[i]}'" : $"argument {i + 1}")}.", "Argument");
            return Args[i];
        }

        /// <summary>Calls a function value with these arguments.</summary>
        public object Call(ICallable fn, params object[] args) => Interpreter.Call(fn, args, null, Scope, Module);

        public KScriptException Error(string message, string type = "Error") => new KScriptException(message, type);
    }

    /// <summary>
    /// What <c>as="name"</c> gives an import: a package or script whose commands are called as <c>@name.cmd()</c>
    /// (or <c>name.cmd()</c>, as in <c>$title |&gt; str.slugify()</c>).
    /// </summary>
    public sealed class ImportNamespace
    {
        public ImportNamespace(string alias, KScriptPackage package)
        {
            Alias = alias;
            Package = package;
        }

        public ImportNamespace(string alias, Module module)
        {
            Alias = alias;
            Module = module;
        }

        public string Alias { get; }
        public KScriptPackage Package { get; }
        public Module Module { get; }

        public string Display => Package?.Name ?? Module?.DisplayName;

        public IEnumerable<string> Names => Package != null ? Package.Commands.Keys : Module.Exports.Keys;

        public bool TryGet(string name, out object value)
        {
            if (Package != null)
            {
                if (Package.Commands.TryGetValue(name, out CommandDefinition cmd))
                {
                    value = cmd;
                    return true;
                }
                if (Package.Variables.TryGetValue(name, out VariableDefinition v))
                {
                    value = v;
                    return true;
                }
            }
            else if (Module.Exports.TryGetValue(name, out value))
            {
                return true;
            }
            value = null;
            return false;
        }
    }
}
