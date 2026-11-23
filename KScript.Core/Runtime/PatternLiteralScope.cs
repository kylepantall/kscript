namespace KScript.Runtime
{
    /// <summary>Visible pattern declarations. Child scopes share this snapshot until a declaration is added or shadowed.</summary>
    internal sealed class PatternLiteralScope
    {
        internal static readonly PatternLiteralScope Empty = new(new Dictionary<string, RefinedType>(StringComparer.Ordinal));
        private const int MaxCachedWords = 256;
        private readonly Dictionary<string, RefinedType> types;
        private readonly Dictionary<string, PatternLiteral> words = new(StringComparer.Ordinal);

        private PatternLiteralScope(Dictionary<string, RefinedType> types) => this.types = types;

        internal bool Contains(string name) => types.ContainsKey(name);

        /// <summary>True when any pattern type is visible, so words need looking up at all.</summary>
        internal bool Any => types.Count > 0;

        internal PatternLiteralScope With(IEnumerable<KeyValuePair<string, object>> variables)
        {
            Dictionary<string, RefinedType> changed = null;
            foreach ((string name, object value) in variables)
            {
                // A variable holding a type is not a new declaration of that type.
                RefinedType declared = value as RefinedType;
                if (declared?.Name == name && declared.Rules.Pattern != null && PatternLiterals.ReadsWords(declared.Root()))
                    (changed ??= new(types, StringComparer.Ordinal))[name] = declared;
                else if (Contains(name))
                    (changed ??= new(types, StringComparer.Ordinal)).Remove(name);
            }
            return changed == null ? this : changed.Count == 0 ? Empty : new PatternLiteralScope(changed);
        }

        internal PatternLiteral Resolve(string word)
        {
            if (types.Count == 0) return null;
            bool cacheable = word.Length <= Interpreter.MaxCachedTextLength;
            if (cacheable)
            {
                lock (words)
                    if (words.TryGetValue(word, out PatternLiteral cached)) return cached;
            }

            // Match outside the lock, so a slow or timed-out regex cannot hold up unrelated requests.
            RefinedType type = PatternLiterals.Resolve(word, types.Values.Select(t => (t, t.Name, t.Rules.Pattern)));
            PatternLiteral result = type == null ? null : new PatternLiteral(word, type);
            if (!cacheable) return result;
            lock (words)
            {
                if (words.Count >= MaxCachedWords) words.Clear();
                words[word] = result;
            }
            return result;
        }
    }

    /// <summary>One word and its inferred type. A match never evaluates a user-supplied rule.</summary>
    internal sealed class PatternLiteral
    {
        internal string Word { get; }
        private readonly RefinedType type;
        private volatile string checkedValue;

        internal PatternLiteral(string word, RefinedType type)
        {
            Word = word;
            this.type = type;
        }

        internal object Convert(Interpreter interpreter)
        {
            // Project defaults may change while the module is loaded. A configured base must use normal conversion.
            bool constant = type.StaticStringRoot is RefinedType root && TypeDefaults.For(root.Module, "string") == null;
            if (constant && checkedValue is string previous) return previous;
            object result = Shapes.Convert(interpreter, Word, TypeRef.Of(type), "The value", type.Closure, type.Module);
            if (constant && result is string text) checkedValue = text;
            return result;
        }
    }
}
