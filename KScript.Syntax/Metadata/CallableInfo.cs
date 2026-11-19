namespace KScript.Metadata
{
    public sealed class ParameterInfo
    {
        public ParameterInfo(string name, bool optional = false, bool variadic = false, string description = null)
        {
            Name = name;
            Optional = optional;
            Variadic = variadic;
            Description = description;
        }

        public string Name { get; }
        public bool Optional { get; }
        public bool Variadic { get; }
        public string Description { get; }

        public string Label => Variadic ? Name + "..." : Optional ? Name + "?" : Name;
    }

    /// <summary>One way to call a command or method.</summary>
    public sealed class Signature
    {
        public Signature(params ParameterInfo[] parameters) => Parameters = parameters;

        public IReadOnlyList<ParameterInfo> Parameters { get; }

        public int MinArgs => Parameters.Count(p => !p.Optional && !p.Variadic);

        public int MaxArgs => Parameters.Any(p => p.Variadic) ? int.MaxValue : Parameters.Count;

        /// <summary>Parses "a, b?, rest..." into a signature.</summary>
        public static Signature Parse(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters))
            {
                return new Signature();
            }
            return new Signature(parameters.Split(',').Select(p => p.Trim()).Select(p =>
                p.EndsWith("...") ? new ParameterInfo(p.Substring(0, p.Length - 3), variadic: true)
                : p.EndsWith("?") ? new ParameterInfo(p.Substring(0, p.Length - 1), optional: true)
                : new ParameterInfo(p)).ToArray());
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
        public string Example { get; init; }

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
