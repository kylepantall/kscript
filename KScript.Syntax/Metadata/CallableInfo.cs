namespace KScript.Metadata
{
    /// <summary>A capability an explicitly supplied type must provide.</summary>
    public enum TypeArgumentConstraint { None, DeclaredRecord, Array, Iterable, Mappable }

    public sealed class ParameterInfo
    {
        // A type alternative, distinct from an enum such as "home | desktop" whose values are literal words.
        public const string ModelOrString = "model or string";

        public ParameterInfo(string name, bool optional = false, bool variadic = false, string description = null, string type = null, IReadOnlyList<string> values = null)
        {
            Name = name;
            Optional = optional;
            Variadic = variadic;
            Description = description;
            Type = type;
            Values = values ?? Array.Empty<string>();
        }

        public string Name { get; }
        public bool Optional { get; }
        public bool Variadic { get; }
        public string Description { get; }

        /// <summary>The parameter's type when it is declared: bool for a flag (<c>recursive</c> / <c>not recursive</c>), or null.</summary>
        public string Type { get; }

        /// <summary>
        /// The values the parameter allows, when it is an enum (<c>name: home|desktop|downloads</c>). A call may write
        /// them bare: <c>@known_folder(downloads)</c>.
        /// </summary>
        public IReadOnlyList<string> Values { get; }

        /// <summary>Named fields accepted when this parameter is a map. Each field can describe a nested map.</summary>
        public IReadOnlyList<ParameterInfo> Fields { get; init; } = Array.Empty<ParameterInfo>();

        public TypeArgumentConstraint TypeConstraint { get; init; }
        public Signature FunctionSignature { get; init; }

        /// <summary>A field of the result (or its list element) populated by the type argument.</summary>
        public string ResultField { get; init; }

        /// <summary>The default value and an example value, written as KScript expressions for the reference.</summary>
        public string DefaultValue { get; init; }
        public string Example { get; init; }

        /// <summary>Set when the parameter is deprecated; it still works, with a warning.</summary>
        public Deprecation Deprecated { get; internal set; }

        public bool IsFlag => Type == KType.Bool;

        public bool AcceptsModel => Type is KType.Model or ModelOrString;

        public string Label => (Variadic ? Name + "..." : Optional ? Name + "?" : Name)
            + (Values.Count > 0 ? ": " + string.Join(" | ", Values) : Type != null ? ": " + (FunctionSignature?.FunctionLabel ?? Type) : string.Empty);
    }

    /// <summary>One way to call a command or method.</summary>
    public sealed class Signature
    {
        public Signature(params ParameterInfo[] parameters) => Parameters = parameters;

        public IReadOnlyList<ParameterInfo> Parameters { get; }

        public int MinArgs => Parameters.Count(p => !p.Optional && !p.Variadic);

        public int MaxArgs => Parameters.Any(p => p.Variadic) ? int.MaxValue : Parameters.Count;

        /// <summary>Whether this signature can take all arguments supplied by a fixed positional call.</summary>
        public bool AcceptsPositionalCall(Signature call)
        {
            int count = call.Parameters.Count;
            if (count > MaxArgs || Parameters.Skip(count).Any(p => !p.Optional && !p.Variadic)) return false;
            for (int i = 0; i < count; i++)
            {
                ParameterInfo supplied = call.Parameters[i];
                ParameterInfo accepted = Parameters[Math.Min(i, Parameters.Count - 1)];
                string type = accepted.Type?.TrimEnd('?');
                if (accepted.Values.Count != 0 || (type != null && type != "any" && type != supplied.Type)) return false;
            }
            return true;
        }

        public string FunctionLabel => $"fn({string.Join(", ", Parameters.Select(p => p.Label))})";

        /// <summary>
        /// Parses "a, b?, rest..." into a signature. A parameter may give a type, "recursive?: bool", or the values it
        /// allows, "name: home|desktop|downloads".
        /// </summary>
        public static Signature Parse(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters))
            {
                return new Signature();
            }
            return new Signature(parameters.Split(',').Select(p => p.Trim()).Select(p =>
            {
                string type = null;
                string[] values = null;
                int colon = p.IndexOf(':');
                if (colon >= 0)
                {
                    string spec = p.Substring(colon + 1).Trim();
                    p = p.Substring(0, colon).Trim();
                    if (spec.Contains('|'))
                    {
                        values = spec.Split('|').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
                        type = KType.String;
                    }
                    else
                    {
                        type = spec;
                    }
                }
                return p.EndsWith("...") ? new ParameterInfo(p.Substring(0, p.Length - 3), variadic: true, type: type, values: values)
                    : p.EndsWith("?") ? new ParameterInfo(p.Substring(0, p.Length - 1), optional: true, type: type, values: values)
                    : new ParameterInfo(p, type: type, values: values);
            }).ToArray());
        }
    }

    public enum CallableKind
    {
        Command,
        Method,
        Function,
    }

    /// <summary>A command (<c>@name()</c>), a method (<c>->name()</c>) or a user function.</summary>
    public sealed class CallableInfo
    {
        public CallableInfo(CallableKind kind, string name, string description, IReadOnlyList<Signature> signatures)
        {
            Kind = kind;
            Name = name;
            Description = description;
            Signatures = signatures;
        }

        public CallableKind Kind { get; }
        public string Name { get; }
        public string Description { get; init; }
        public IReadOnlyList<Signature> Signatures { get; }

        /// <summary>A short usage example, shown in hover.</summary>
        public string Example { get; set; }

        /// <summary>Set when the command or method is deprecated; it still works, with a warning.</summary>
        public Deprecation Deprecated { get; internal set; }

        /// <summary>Notes for coding agents (<c>kscript agents</c>), or null.</summary>
        public AgentNotes Agent { get; set; }

        /// <summary>For methods: the type the method is for.</summary>
        public string ReceiverType { get; init; } = KType.Any;

        /// <summary>The type of the result, when known.</summary>
        public string ReturnType { get; init; } = KType.Any;

        /// <summary>
        /// The result's shape, written like a type, when its fields are known: <c>{ id: string, email: email }</c> or
        /// <c>list&lt;{ id: string, name: string }&gt;</c>. The editor uses it for completion and hover after the call
        /// (<c>@user().</c>, a loop over <c>@api_tokens()</c>). Null when only <see cref="ReturnType"/> is known.
        /// </summary>
        public string ReturnShape { get; set; }

        /// <summary>
        /// True when names the signature doesn't list, and words without quotes, mean something to the call itself, as
        /// columns and values do in <c>User->where(role: admin)</c>: they reach it as they are instead of being an error.
        /// </summary>
        public bool KeepsBareWords { get; set; }

        /// <summary>True if the callable is in the KScript 2 design but no package implements it yet.</summary>
        public bool Planned => Package == null && Kind != CallableKind.Function;

        /// <summary>The package that provides the command or method (KScript.Core, KScript.Math...), or null while planned.</summary>
        public string Package { get; internal set; }

        public int MinArgs => Signatures.Count == 0 ? 0 : Signatures.Min(s => s.MinArgs);
        public int MaxArgs => Signatures.Count == 0 ? int.MaxValue : Signatures.Max(s => s.MaxArgs);

        public string Prefix => Kind == CallableKind.Method ? "->" : "@";

        public IEnumerable<string> Labels => Signatures.Select(s => $"{Prefix}{Name}({string.Join(", ", s.Parameters.Select(p => p.Label))})");
    }
}
