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

        public Scope(Scope parent = null) { Parent = parent; }

        public Scope Parent { get; }

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

        public bool TryGet(string name, out object value)
        {
            for (Scope s = this; s != null; s = s.Parent)
            {
                lock (s.vars)
                {
                    if (s.vars.TryGetValue(name, out value)) return true;
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
                vars[name] = value;
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
                    if (!s.vars.ContainsKey(name)) continue;
                    if (s.constants != null && s.constants.Contains(name))
                        throw new KScriptException($"${name} is const, so it can't be changed.", "Const");
                    s.vars[name] = value;
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

        /// <summary>The variables declared in this scope itself.</summary>
        public IReadOnlyList<KeyValuePair<string, object>> OwnVariables
        {
            get { lock (vars) return vars.ToList(); }
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
