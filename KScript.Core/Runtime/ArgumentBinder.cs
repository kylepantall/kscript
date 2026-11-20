using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// Matches named arguments (<c>@known_folder(name: 'downloads')</c>) to parameters. Positional arguments come
    /// first and fill parameters in order; each named argument then fills the parameter of that name. Commands,
    /// methods, &lt;fn&gt; functions and lambdas all bind this way, so a handler only ever reads positions.
    /// </summary>
    public static class ArgumentBinder
    {
        /// <summary>
        /// Moves the named arguments of a command or method call into the positions of its signature. Returns the
        /// call unchanged when nothing was named. Names a signature doesn't have are an error, unless the signature
        /// ends in a variadic parameter (<c>args...</c>): those calls keep their extra names in <see cref="CallContext.Named"/>.
        /// </summary>
        public static CallContext Bind(CallableInfo info, CallContext call)
        {
            if (call.Named.Count == 0 || info.Signatures.Count == 0) return call;

            Signature signature = info.Signatures.FirstOrDefault(s => call.Named.Keys.All(k => Index(s, k) >= 0))
                ?? info.Signatures.FirstOrDefault(s => s.Parameters.Any(p => p.Variadic));
            if (signature == null)
            {
                Signature first = info.Signatures[0];
                string unknown = call.Named.Keys.First(k => Index(first, k) < 0);
                throw UnknownName(call, unknown, info.Signatures.SelectMany(s => s.Parameters).Where(p => !p.Variadic).Select(p => p.Name).Distinct());
            }

            var args = call.Args.ToList();
            var skipped = new HashSet<int>();
            var rest = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in call.Named)
            {
                int index = Index(signature, pair.Key);
                if (index < 0)
                {
                    rest[pair.Key] = pair.Value;
                    continue;
                }
                if (index < call.Args.Count) throw Twice(call, pair.Key, index);
                while (args.Count <= index)
                {
                    skipped.Add(args.Count);
                    args.Add(null);
                }
                args[index] = pair.Value;
                skipped.Remove(index);
            }

            for (int i = 0; i < signature.Parameters.Count; i++)
            {
                ParameterInfo p = signature.Parameters[i];
                if (!p.Optional && !p.Variadic && (i >= args.Count || skipped.Contains(i)))
                    throw new KScriptException($"{call.Display} needs a value for '{p.Name}'.", "Argument");
            }

            return new CallContext(call.Interpreter, call.Name, args, rest, call.Scope, call.Module, call.IsMethod, skipped)
            {
                ParameterNames = signature.Parameters.Select(p => p.Name).ToList(),
            };
        }

        /// <summary>
        /// Binds a call to a list of parameter names, as for a &lt;fn&gt; or a lambda. <paramref name="given"/> holds the
        /// positions that received a value. With <paramref name="lenient"/>, extra positional arguments are ignored
        /// (lambdas passed to list methods are often given more arguments than they use).
        /// </summary>
        public static IReadOnlyList<object> BindByName(CallContext call, IReadOnlyList<string> parameters, out HashSet<int> given, bool lenient = false)
        {
            given = new HashSet<int>(Enumerable.Range(0, Math.Min(call.Args.Count, parameters.Count)));
            if (!lenient && call.Args.Count > parameters.Count)
                throw new KScriptException($"{call.Display} takes {parameters.Count} argument{(parameters.Count == 1 ? string.Empty : "s")} but was given {call.Args.Count}.", "Argument");
            if (call.Named.Count == 0) return call.Args;

            var args = new object[Math.Max(parameters.Count, call.Args.Count)];
            for (int i = 0; i < call.Args.Count; i++) args[i] = call.Args[i];
            foreach (KeyValuePair<string, object> pair in call.Named)
            {
                int index = IndexOf(parameters, pair.Key);
                if (index < 0) throw UnknownName(call, pair.Key, parameters);
                if (index < call.Args.Count) throw Twice(call, pair.Key, index);
                args[index] = pair.Value;
                given.Add(index);
            }
            return args;
        }

        private static int Index(Signature signature, string name)
        {
            for (int i = 0; i < signature.Parameters.Count; i++)
            {
                if (!signature.Parameters[i].Variadic && signature.Parameters[i].Name == name) return i;
            }
            return -1;
        }

        private static int IndexOf(IReadOnlyList<string> parameters, string name)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i] == name) return i;
            }
            return -1;
        }

        private static KScriptException UnknownName(CallContext call, string name, IEnumerable<string> parameters)
        {
            List<string> names = parameters.ToList();
            string close = Values.Closest(name, names);
            string list = names.Count == 0 ? $"{call.Display} takes no named arguments." : $"Its parameters are: {string.Join(", ", names)}.";
            return new KScriptException($"{call.Display} has no parameter named '{name}'." + (close != null ? $" Did you mean {close}?" : string.Empty) + " " + list, "Argument");
        }

        private static KScriptException Twice(CallContext call, string name, int index) =>
            new KScriptException($"{call.Display} was given '{name}' twice: as argument {index + 1} and by name.", "Argument");
    }
}
