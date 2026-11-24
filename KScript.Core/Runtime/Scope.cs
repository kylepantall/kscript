using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>
    /// Variables. Each element body, loop iteration and function call gets a child scope, so a &lt;let&gt; lives until
    /// the end of its parent element and nothing leaks out of loops or functions.
    /// </summary>
    public sealed class Scope
    {
        private readonly Dictionary<string, object> vars = new Dictionary<string, object>(StringComparer.Ordinal);
        private HashSet<string> constants;
        private PatternLiteralScope inheritedPatterns;
        private PatternLiteralScope visiblePatterns;

        // A component method and its live template can share a typed record field as a lexical variable.
        private sealed record FieldBinding(Record Record, string Name, Interpreter Interpreter)
        {
            internal object Value => Record.Get(Name);
            internal void Set(object value) => Record.Set(Interpreter, Name, value);
        }

        internal void BindField(Record record, string field, Interpreter interpreter) =>
            Declare(field, new FieldBinding(record, field, interpreter));

        public Scope(Scope parent = null) { Parent = parent; }

        public Scope Parent { get; }

        // A normalize rule can call methods of the value being normalized with @name.
        internal (RefinedType Type, object Input)? Normalizing { get; init; }

        public Scope Child() => new Scope(this);

        public Scope Root
        {
            get
            {
                Scope s = this;
                while (s.Parent != null) s = s.Parent;
                return s;
            }
        }

        public bool TryGet(string name, out object value) => TryGet(name, out value, includeMethods: false);

        internal bool TryGet(string name, out object value, bool includeMethods)
        {
            for (Scope s = this; s != null; s = s.Parent)
            {
                if (includeMethods && s.Normalizing is { } normalizer
                    && normalizer.Type.TryFindMethod(name, out RefinedType owner, out var method))
                {
                    value = owner.Module.Interpreter.BindRefinedMethod(normalizer.Input, owner, method, normalizer.Type);
                    return true;
                }
                lock (s.vars)
                {
                    if (s.vars.TryGetValue(name, out value))
                    {
                        if (value is FieldBinding field) value = field.Value;
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        public bool Has(string name) => TryGet(name, out _);

        /// <summary>True if the variable is declared in this scope itself (not a parent).</summary>
        public bool HasOwn(string name)
        {
            lock (vars) return vars.ContainsKey(name);
        }

        /// <summary>Declares (or redeclares) a variable in this scope.</summary>
        public void Declare(string name, object value, bool isConst = false)
        {
            lock (vars)
            {
                if (constants != null && constants.Contains(name))
                    throw new KScriptException($"${name} is const and can't be declared again.", "Const");
                SetValue(name, value);
                if (isConst) (constants ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
            }
        }

        /// <summary>Updates an existing variable wherever it was declared.</summary>
        public void Assign(string name, object value)
        {
            for (Scope s = this; s != null; s = s.Parent)
            {
                lock (s.vars)
                {
                    if (!s.vars.TryGetValue(name, out object old)) continue;
                    if (s.constants != null && s.constants.Contains(name))
                        throw new KScriptException($"${name} is const, so it can't be changed.", "Const");
                    if (old is FieldBinding field) field.Set(value);
                    else s.SetValue(name, value);
                    return;
                }
            }
            string close = Values.Closest(name, Names());
            throw new KScriptException(Wording.Of($"${name} is not defined, so it can't be set. Declare it first with <let name=\"{name}\" value=\"...\" />.")
                + (close != null ? $" Did you mean ${close}?" : string.Empty), "Name");
        }

        public bool IsConst(string name)
        {
            for (Scope s = this; s != null; s = s.Parent)
            {
                lock (s.vars)
                {
                    if (s.vars.ContainsKey(name)) return s.constants != null && s.constants.Contains(name);
                }
            }
            return false;
        }

        // Caller holds vars' lock. Changing an ordinary value leaves the set of visible types alone.
        private void SetValue(string name, object value)
        {
            if (visiblePatterns != null && (visiblePatterns.Contains(name) || value is RefinedType type && type.Name == name
                || vars.TryGetValue(name, out object old) && old is RefinedType previous && previous.Name == name))
                visiblePatterns = null;
            vars[name] = value;
        }

        internal PatternLiteralScope PatternTypes()
        {
            PatternLiteralScope parent = Parent?.PatternTypes() ?? PatternLiteralScope.Empty;
            lock (vars)
            {
                if (visiblePatterns == null || inheritedPatterns != parent)
                {
                    inheritedPatterns = parent;
                    visiblePatterns = parent.With(vars);
                }
                return visiblePatterns;
            }
        }

        /// <summary>The variables declared in this scope itself.</summary>
        public IReadOnlyList<KeyValuePair<string, object>> OwnVariables
        {
            get
            {
                lock (vars) return vars.Select(entry => entry.Value is FieldBinding field
                    ? new KeyValuePair<string, object>(entry.Key, field.Value) : entry).ToList();
            }
        }

        /// <summary>Every visible variable name, innermost first.</summary>
        public IEnumerable<string> Names()
        {
            var seen = new HashSet<string>();
            for (Scope s = this; s != null; s = s.Parent)
            {
                List<string> keys;
                lock (s.vars) keys = s.vars.Keys.ToList();
                foreach (string k in keys)
                {
                    if (seen.Add(k)) yield return k;
                }
            }
        }
    }
}
