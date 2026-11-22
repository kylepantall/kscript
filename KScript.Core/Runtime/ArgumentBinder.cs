using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// A bare word in an argument list, such as <c>downloads</c> in <c>@known_folder(downloads)</c> or
    /// <c>recursive</c> / <c>not recursive</c> in <c>@files($dir, recursive)</c>. What it means depends on the
    /// parameter it lands on, so the binder resolves it: a value of an enum parameter, or a yes/no flag.
    /// </summary>
    public sealed class BareWord
    {
        public BareWord(string name, bool negated, bool afterNamed)
        {
            Name = name;
            Negated = negated;
            AfterNamed = afterNamed;
        }

        public string Name { get; }

        /// <summary>Written as <c>not name</c>: a flag that is off.</summary>
        public bool Negated { get; }

        /// <summary>Written after a named argument, so it can only be a flag.</summary>
        public bool AfterNamed { get; }

        public string Source => (Negated ? "not " : string.Empty) + Name;

        /// <summary>
        /// Makes the word's value as the declared type whose pattern fits it (<c>@f(passkey_abc)</c>), for a parameter
        /// it isn't a flag, enum value or raw text of; null when no type fits. See <see cref="BareWordTypes"/>.
        /// </summary>
        public Func<object> Typed { get; init; }

        public override string ToString() => Source;
    }

    /// <summary>
    /// An array argument with bare words in it, such as <c>[large, small]</c> in <c>@pack([large, small])</c>. The
    /// words are values of the parameter's enum when it is a list of one (<c>sizes: Size[]</c>), and an error otherwise.
    /// </summary>
    public sealed class BareList
    {
        public BareList(IReadOnlyList<object> items) => Items = items;

        /// <summary>The items: <see cref="BareWord"/>s and the values of the other expressions.</summary>
        public IReadOnlyList<object> Items { get; }

        public BareWord FirstWord => Items.OfType<BareWord>().First();
    }

    /// <summary>
    /// A positional argument written after a named one: <c>@join_path(base: $folder, ['archive', $name])</c>. It
    /// fills the first parameter that is still empty.
    /// </summary>
    public sealed class LatePositional
    {
        public LatePositional(object value) => Value = value;

        public object Value { get; }
    }

    /// <summary>
    /// Matches arguments to parameters. Positional arguments come first and fill parameters in order; each named
    /// argument (<c>name: value</c>) then fills the parameter of that name, and each flag (<c>recursive</c>,
    /// <c>not recursive</c>) sets a parameter to yes or no. A bare word on an enum parameter is one of its values
    /// (<c>@known_folder(downloads)</c>). Commands, methods, &lt;fn&gt; functions and lambdas all bind this way, so a
    /// handler only ever reads positions.
    /// </summary>
    public static class ArgumentBinder
    {
        /// <summary>One parameter, as the binder sees it.</summary>
        /// <remarks><paramref name="RawText"/>: how a bare word for this parameter is read as text: "handle" (<c>@kyle</c> too), "word", or null.</remarks>
        public sealed record Slot(string Name, bool Required, bool Variadic = false, IReadOnlyCollection<string> Values = null, string TypeName = null,
            IReadOnlyCollection<string> ItemValues = null, string RawText = null)
        {
            public bool IsEnum => Values != null && Values.Count > 0;

            /// <summary>A list of an enum (<c>sizes: Size[]</c>), whose items can be written bare.</summary>
            public bool IsEnumList => ItemValues != null && ItemValues.Count > 0;

            public static Slot From(ParameterInfo p) => new Slot(p.Name, !p.Optional && !p.Variadic, p.Variadic, p.Values, RawText: KScript.Metadata.NativeTypes.Get(p.Type)?.RawKind ?? (p.Type == "word" ? "word" : null));
        }

        /// <summary>
        /// Binds a command or method call to its signature. Names a signature doesn't have are an error, unless the
        /// signature ends in a variadic parameter (<c>args...</c>): those calls keep their extra names in
        /// <see cref="CallContext.Named"/>.
        /// </summary>
        public static CallContext Bind(CallableInfo info, CallContext call)
        {
            if (info.Signatures.Count == 0) return Plain(call);
            bool simple = call.Named.Count == 0 && !call.Args.Any(a => a is BareWord || a is LatePositional || a is BareList);
            if (simple && !info.Signatures.Any(s => s.Parameters.Any(p => p.Values.Count > 0))) return call;

            KScriptException first = null;
            foreach (Signature signature in Ordered(info, call))
            {
                List<Slot> slots = signature.Parameters.Select(Slot.From).ToList();
                if (TryResolve(call, slots, lenient: true, out object[] args, out HashSet<int> skipped, out Dictionary<string, object> rest, out KScriptException error, info.KeepsBareWords))
                {
                    return new CallContext(call.Interpreter, call.Name, args, rest, call.Scope, call.Module, call.IsMethod, skipped)
                    {
                        ParameterNames = signature.Parameters.Select(p => p.Name).ToList(),
                    };
                }
                first ??= error;
            }
            throw first;
        }

        /// <summary>
        /// Binds a call to a list of parameters, as for a &lt;fn&gt; or a lambda. <paramref name="given"/> holds the
        /// positions that received a value. With <paramref name="lenient"/>, extra positional arguments are kept
        /// (lambdas passed to list methods are often given more arguments than they use).
        /// </summary>
        public static IReadOnlyList<object> BindByName(CallContext call, IReadOnlyList<Slot> parameters, out HashSet<int> given, bool lenient = false)
        {
            if (!TryResolve(call, parameters.ToList(), lenient, out object[] args, out HashSet<int> skipped, out Dictionary<string, object> rest, out KScriptException error))
                throw error;
            if (rest.Count > 0) throw UnknownName(call, rest.Keys.First(), parameters.Select(p => p.Name));
            given = new HashSet<int>(Enumerable.Range(0, args.Length).Where(i => !skipped.Contains(i)));
            return args;
        }

        /// <summary>Names only (lambdas, user functions without declared types).</summary>
        public static IReadOnlyList<object> BindByName(CallContext call, IReadOnlyList<string> parameters, out HashSet<int> given, bool lenient = false) =>
            BindByName(call, parameters.Select(p => new Slot(p, false)).ToList(), out given, lenient);

        /// <summary>
        /// For calls with no parameter list to bind to (methods of package objects): a bare word there has nothing
        /// to mean, so it is an error.
        /// </summary>
        public static CallContext Plain(CallContext call)
        {
            BareWord bare = call.Args.OfType<BareWord>().FirstOrDefault() ?? call.Named.Values.OfType<BareWord>().FirstOrDefault()
                ?? call.Args.Concat(call.Named.Values).Select(a => a is LatePositional l ? l.Value : a).OfType<BareList>().Select(l => l.FirstWord).FirstOrDefault();
            if (bare != null && bare.Typed == null) throw NotAValue(call, bare, null);
            if (bare != null) call = Typed(call);
            if (!call.Args.Any(a => a is LatePositional)) return call;
            return new CallContext(call.Interpreter, call.Name, call.Args.Select(a => a is LatePositional l ? l.Value : a).ToList(), call.Named,
                call.Scope, call.Module, call.IsMethod, call.Skipped);
        }

        /// <summary>The call with each bare word that fits a declared type made into its value; any other bare word fails.</summary>
        private static CallContext Typed(CallContext call)
        {
            object Value(object a) => a switch
            {
                BareWord { Typed: not null } word => word.Typed(),
                BareWord word => throw NotAValue(call, word, null),
                BareList list => list.Items.Select(Value).ToList(),
                LatePositional late => new LatePositional(Value(late.Value)),
                _ => a,
            };
            return new CallContext(call.Interpreter, call.Name, call.Args.Select(Value).ToList(),
                call.Named.ToDictionary(kv => kv.Key, kv => Value(kv.Value), StringComparer.Ordinal), call.Scope, call.Module, call.IsMethod, call.Skipped);
        }

        private static IEnumerable<Signature> Ordered(CallableInfo info, CallContext call)
        {
            // Signatures that know every name first, then the rest (so the error comes from the closest one).
            IEnumerable<string> names = call.Named.Keys.Concat(call.Args.OfType<BareWord>().Select(b => b.Name));
            return info.Signatures.OrderBy(s => names.All(n => s.Parameters.Any(p => !p.Variadic && p.Name == n) || s.Parameters.Any(p => p.Variadic)) ? 0 : 1);
        }

        private static bool TryResolve(CallContext call, List<Slot> slots, bool lenient, out object[] args, out HashSet<int> skipped,
            out Dictionary<string, object> rest, out KScriptException error, bool keepBare = false)
        {
            args = null;
            skipped = new HashSet<int>();
            rest = new Dictionary<string, object>(StringComparer.Ordinal);
            error = null;

            int Index(string name) => slots.FindIndex(s => !s.Variadic && s.Name == name);
            bool variadic = slots.Any(s => s.Variadic);
            var positional = new List<object>();
            var named = new List<KeyValuePair<string, object>>();
            var flags = new HashSet<string>(StringComparer.Ordinal);

            // Bare words among the positional arguments: an enum value where they land, otherwise a flag.
            var late = new List<object>();
            foreach (object a in call.Args)
            {
                if (a is LatePositional || a is BareWord { AfterNamed: true })
                {
                    late.Add(a);
                    continue;
                }
                if (!(a is BareWord bare))
                {
                    positional.Add(a);
                    continue;
                }
                Slot slot = positional.Count < slots.Count ? slots[positional.Count] : slots.LastOrDefault(s => s.Variadic);
                string value = !bare.Negated && !bare.AfterNamed ? EnumValue(slot, bare.Name) : null;
                if (value != null)
                {
                    positional.Add(value);
                }
                else if (keepBare && !bare.Negated && slot != null && !slot.IsEnum)
                {
                    // A column name or word the method reads itself: ->pluck(email), @db(users).
                    positional.Add(bare);
                }
                else if (Index(bare.Name) >= 0)
                {
                    named.Add(new KeyValuePair<string, object>(bare.Name, !bare.Negated));
                    flags.Add(bare.Name);
                }
                else if (bare.Typed != null && slot?.IsEnum != true)
                {
                    positional.Add(bare.Typed());
                }
                else
                {
                    error = NotAValue(call, bare, slot, slots);
                    return false;
                }
            }

            foreach (KeyValuePair<string, object> pair in call.Named)
            {
                object value = pair.Value;
                if (value is BareWord bare)
                {
                    int index = Index(pair.Key);
                    string resolved = index >= 0 && !bare.Negated ? EnumValue(slots[index], bare.Name) : null;
                    if (resolved == null && keepBare && (index < 0 ? variadic : slots[index].Values == null || slots[index].Values.Count == 0))
                    {
                        named.Add(new KeyValuePair<string, object>(pair.Key, bare));
                        continue;
                    }
                    if (resolved == null && bare.Typed != null && (index < 0 || !slots[index].IsEnum))
                    {
                        named.Add(new KeyValuePair<string, object>(pair.Key, bare.Typed()));
                        continue;
                    }
                    if (resolved == null)
                    {
                        error = NotAValue(call, bare, index >= 0 ? slots[index] : null, slots);
                        return false;
                    }
                    value = resolved;
                }
                named.Add(new KeyValuePair<string, object>(pair.Key, value));
            }

            if (!lenient && !variadic && positional.Count > slots.Count)
            {
                error = new KScriptException($"{call.Display} takes {slots.Count} argument{(slots.Count == 1 ? string.Empty : "s")} but was given {positional.Count}.", "Argument");
                return false;
            }

            var bound = new List<object>(positional);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in named)
            {
                int index = Index(pair.Key);
                if (index < 0)
                {
                    if (variadic)
                    {
                        rest[pair.Key] = pair.Value;
                        continue;
                    }
                    error = UnknownName(call, pair.Key, slots.Where(s => !s.Variadic).Select(s => s.Name));
                    return false;
                }
                if (index < positional.Count || !seen.Add(pair.Key))
                {
                    error = new KScriptException(index < positional.Count
                        ? $"{call.Display} was given '{pair.Key}' twice: as argument {index + 1} and {(flags.Contains(pair.Key) ? "as a flag" : "by name")}."
                        : $"{call.Display} was given '{pair.Key}' twice.", "Argument");
                    return false;
                }
                while (bound.Count <= index)
                {
                    skipped.Add(bound.Count);
                    bound.Add(null);
                }
                bound[index] = pair.Value;
                skipped.Remove(index);
            }

            // Positional arguments after named ones fill the parameters still empty, in order.
            foreach (object a in late)
            {
                if (a is BareWord word && Index(word.Name) is int flag && flag >= 0)
                {
                    if (flag < bound.Count && !skipped.Contains(flag) || !seen.Add(word.Name))
                    {
                        error = new KScriptException($"{call.Display} was given '{word.Name}' twice.", "Argument");
                        return false;
                    }
                    while (bound.Count <= flag) { skipped.Add(bound.Count); bound.Add(null); }
                    bound[flag] = !word.Negated;
                    skipped.Remove(flag);
                    continue;
                }
                int free = skipped.Count > 0 ? skipped.Min() : bound.Count;
                Slot target = free < slots.Count ? slots[free] : slots.LastOrDefault(s => s.Variadic);
                object value = a is LatePositional lp ? lp.Value : null;
                if (a is BareWord bare2)
                {
                    value = !bare2.Negated ? EnumValue(target, bare2.Name) : null;
                    if (value == null && bare2.Typed != null && target?.IsEnum != true) value = bare2.Typed();
                    if (value == null)
                    {
                        error = NotAValue(call, bare2, target, slots);
                        return false;
                    }
                }
                if (target == null && !lenient)
                {
                    error = new KScriptException($"{call.Display} takes {slots.Count} argument{(slots.Count == 1 ? string.Empty : "s")}, and has no parameter left for {Values.Describe(value)}.", "Argument");
                    return false;
                }
                if (free < bound.Count) bound[free] = value;
                else bound.Add(value);
                skipped.Remove(free);
            }

            // [large, small]: bare words are values of a list-of-enum parameter, and nothing anywhere else.
            for (int i = 0; i < bound.Count; i++)
            {
                if (!(bound[i] is BareList list)) continue;
                Slot slot = i < slots.Count ? slots[i] : slots.LastOrDefault(s => s.Variadic);
                if (keepBare && slot?.IsEnumList != true)
                {
                    // The call reads the words itself: [id, name] are columns.
                    bound[i] = list.Items.ToList();
                    continue;
                }
                var items = new List<object>(list.Items.Count);
                foreach (object item in list.Items)
                {
                    if (!(item is BareWord word))
                    {
                        items.Add(item);
                        continue;
                    }
                    string value = slot?.IsEnumList == true && !word.Negated
                        ? slot.ItemValues.FirstOrDefault(v => string.Equals(v, word.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                        : null;
                    if (value == null && slot?.IsEnumList != true && word.Typed != null)
                    {
                        items.Add(word.Typed());
                        continue;
                    }
                    if (value == null)
                    {
                        error = slot?.IsEnumList == true
                            ? new KScriptException($"'{word.Name}' isn't a {slot.TypeName}." + Hint(word.Name, slot.ItemValues) + $" Use {Or(slot.ItemValues)}.", "Argument")
                            : NotAValue(call, word, null);
                        return false;
                    }
                    items.Add(value);
                }
                bound[i] = items;
            }
            foreach (string key in rest.Where(r => r.Value is BareList).Select(r => r.Key).ToList())
            {
                if (keepBare)
                {
                    rest[key] = ((BareList)rest[key]).Items.ToList();
                    continue;
                }
                error = NotAValue(call, ((BareList)rest[key]).FirstWord, null);
                return false;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                bool has = i < bound.Count && !skipped.Contains(i);
                if (slot.Required && !has)
                {
                    error = new KScriptException($"{call.Display} needs a value for '{slot.Name}'.", "Argument");
                    return false;
                }
                if (has && slot.IsEnum && bound[i] is string text && EnumValue(slot, text) is not string)
                {
                    error = new KScriptException($"'{text}' isn't " + (slot.TypeName != null ? $"a {slot.TypeName}." : $"a valid {slot.Name} for {call.Display}.") + Hint(text, slot.Values) + $" Use {Or(slot.Values)}.", "Argument");
                    return false;
                }
                if (has && slot.IsEnum && bound[i] is string text2) bound[i] = EnumValue(slot, text2);
            }

            args = bound.ToArray();
            return true;
        }

        /// <summary>The enum value <paramref name="word"/> names (ignoring case and spaces), or null.</summary>
        private static string EnumValue(Slot slot, string word)
        {
            if (slot?.IsEnum == true) return slot.Values.FirstOrDefault(v => string.Equals(v, word.Trim(), StringComparison.OrdinalIgnoreCase));
            if (slot?.RawText == null) return null;
            return slot.RawText == "handle" || !word.StartsWith('@') ? word : null;
        }

        private static string Or(IEnumerable<string> values)
        {
            List<string> list = values.ToList();
            return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " or " + list[list.Count - 1];
        }

        private static string Hint(string word, IEnumerable<string> names)
        {
            string close = Values.Closest(word, names);
            return close != null ? $" Did you mean {close}?" : string.Empty;
        }

        private static KScriptException NotAValue(CallContext call, BareWord bare, Slot slot, IReadOnlyList<Slot> slots = null)
        {
            string message;
            if (bare.Negated || bare.AfterNamed)
            {
                IEnumerable<string> names = slots?.Where(s => !s.Variadic).Select(s => s.Name) ?? Enumerable.Empty<string>();
                message = $"{call.Display} has no flag '{bare.Name}'." + Hint(bare.Name, names)
                    + (slots != null && slots.Count > 0 ? $" Its parameters are: {string.Join(", ", names)}." : string.Empty);
            }
            else if (slot?.IsEnum == true)
            {
                message = $"'{bare.Name}' isn't " + (slot.TypeName != null ? $"a {slot.TypeName}." : $"a valid {slot.Name} for {call.Display}.") + Hint(bare.Name, slot.Values) + $" Use {Or(slot.Values)}.";
            }
            else
            {
                string close = slots == null ? null : Values.Closest(bare.Name, slots.Where(s => !s.Variadic).Select(s => s.Name));
                if (close != null) return new KScriptException($"{call.Display} has no flag '{bare.Name}'. Did you mean {close}?", "Name");
                message = $"Unknown name '{bare.Name}' in {call.Display}. Variables start with $ (${bare.Name}), text needs quotes ('{bare.Name}')"
                    + (slots != null && slots.Count > 0 ? $", and a bare word must be one of its parameters ({string.Join(", ", slots.Where(s => !s.Variadic).Select(s => s.Name))}) used as a flag." : ".");
            }
            return new KScriptException(message, slot?.IsEnum == true ? "Argument" : "Name");
        }

        private static KScriptException UnknownName(CallContext call, string name, IEnumerable<string> parameters)
        {
            List<string> names = parameters.ToList();
            string close = Values.Closest(name, names);
            string list = names.Count == 0 ? $"{call.Display} takes no named arguments." : $"Its parameters are: {string.Join(", ", names)}.";
            return new KScriptException($"{call.Display} has no parameter named '{name}'." + (close != null ? $" Did you mean {close}?" : string.Empty) + " " + list, "Argument");
        }
    }
}
