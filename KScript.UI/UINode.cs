using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.UI
{
    /// <summary>
    /// One element of a window as the script last drew it: its type (Button, Column…), its attribute values, its
    /// text, its children, and the handlers to run when the person uses it. The desktop host turns these into real
    /// controls and updates them in place after every redraw.
    /// </summary>
    public sealed class UINode
    {
        public UINode(string type, MarkupElement element = null)
        {
            Type = type;
            Element = element;
        }

        public string Type { get; }
        public MarkupElement Element { get; }
        public Dictionary<string, object> Props { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public string Text { get; set; }
        public List<UINode> Children { get; } = new List<UINode>();
        public Dictionary<string, UIHandler> Handlers { get; } = new Dictionary<string, UIHandler>(StringComparer.Ordinal);

        /// <summary>The variable a TextBox, CheckBox, Select or Slider shows and changes (bind="name").</summary>
        public string Bind { get; set; }
        internal Scope BindScope { get; set; }

        public object Prop(string name) => Props.TryGetValue(name, out object v) ? v : null;

        public bool Has(string name) => Props.ContainsKey(name) && Props[name] != null;

        public double? Number(string name) => Prop(name) is object v && v != null ? Values.ToNumber(v, $"<{Type} {name}>") : null;

        public string String(string name) => Prop(name) is object v && v != null ? Values.ToStr(v) : null;

        public bool Flag(string name) => Values.Truthy(Prop(name));

        /// <summary>This node and everything inside it, depth first.</summary>
        public IEnumerable<UINode> Descendants()
        {
            yield return this;
            foreach (UINode child in Children)
            {
                foreach (UINode d in child.Descendants()) yield return d;
            }
        }

        public override string ToString() => Text != null ? $"<{Type}>{Text}</{Type}>" : $"<{Type}>";
    }

    /// <summary>Statements from an on-* attribute, with the scope they were written in (so loop variables are there).</summary>
    public sealed class UIHandler
    {
        internal UIHandler(DecodedText statements, Scope scope, Module module)
        {
            Statements = statements;
            Scope = scope;
            Module = module;
        }

        internal DecodedText Statements { get; }
        internal Scope Scope { get; }
        internal Module Module { get; }
    }
}
