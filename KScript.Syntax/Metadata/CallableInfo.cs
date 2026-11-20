namespace KScript.Metadata
{
    public sealed class ParameterInfo
    {
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

        public bool IsFlag => Type == KType.Bool;

        public string Label => (Variadic ? Name + "..." : Optional ? Name + "?" : Name)
            + (Values.Count > 0 ? ": " + string.Join(" | ", Values) : Type != null ? ": " + Type : string.Empty);
    }

    /// <summary>One way to call a command or method.</summary>
    public sealed class Signature
    {
        public Signature(params ParameterInfo[] parameters) => Parameters = parameters;

        public IReadOnlyList<ParameterInfo> Parameters { get; }

        public int MinArgs => Parameters.Count(p => !p.Optional && !p.Variadic);

        public int MaxArgs => Parameters.Any(p => p.Variadic) ? int.MaxValue : Parameters.Count;

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

        /// <summary>For methods: the type the method is for.</summary>
        public string ReceiverType { get; init; } = KType.Any;

        /// <summary>The type of the result, when known.</summary>
        public string ReturnType { get; init; } = KType.Any;

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
