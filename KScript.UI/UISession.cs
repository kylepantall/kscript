using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.UI
{
    /// <summary>
    /// A running window, independent of any GUI toolkit. The direct &lt;let&gt;, &lt;fn&gt; and &lt;import&gt;
    /// children of &lt;KScript.Window&gt; run once and hold the window's state; everything else is the layout,
    /// drawn again (as a tree of <see cref="UINode"/>s) after every event, like a &lt;live&gt; block on a page.
    /// </summary>
    public sealed class UISession
    {
        /// <summary>Window children that run once, before the first draw.</summary>
        internal static readonly HashSet<string> SetupElements = new HashSet<string>(StringComparer.Ordinal) { "let", "fn", "import", "do", "set", "call" };

        [ThreadStatic]
        private static Stack<UINode> drawing;

        private readonly ElementRun run;
        private readonly List<MarkupNode> layout;

        private UISession(ElementRun run)
        {
            this.run = run;
            State = run.Scope.Child();
            layout = run.Element.Children.Where(n => !(n is MarkupElement e && SetupElements.Contains(e.Name))).ToList();
        }

        public Interpreter Interpreter => run.Interpreter;
        public Module Module => run.Module;

        /// <summary>The window's variables: its top-level &lt;let&gt; values and &lt;fn&gt; functions.</summary>
        public Scope State { get; }

        /// <summary>The window as last drawn.</summary>
        public UINode Tree { get; private set; }

        /// <summary>The last error from drawing or a handler, shown in the window until the next successful event. Null when all is well.</summary>
        public string Error { get; private set; }

        /// <summary>Called after every redraw, so the host can update its controls.</summary>
        public event Action Redrawn;

        /// <summary>The node new elements are added to while the window is being drawn, or null outside a draw.</summary>
        internal static UINode CurrentParent => drawing != null && drawing.Count > 0 ? drawing.Peek() : null;

        internal static void Push(UINode node) => drawing.Push(node);

        internal static void Pop() => drawing.Pop();

        /// <summary>Runs the window's setup and draws it for the first time.</summary>
        internal static UISession Start(ElementRun run)
        {
            if (CurrentParent != null) throw run.Error($"A <Window> can't be inside another window.", null, "Syntax");
            var session = new UISession(run);
            List<MarkupNode> setup = run.Element.Children.Where(n => n is MarkupElement e && SetupElements.Contains(e.Name)).ToList();
            run.Run(setup, session.State);
            session.Draw(throwErrors: true, notify: false);
            return session;
        }

        /// <summary>Draws the layout again from the current state.</summary>
        public void Redraw() => Draw(throwErrors: false, notify: true);

        private void Draw(bool throwErrors, bool notify)
        {
            var root = new UINode("Window", run.Element);
            Stack<UINode> previous = drawing;
            drawing = new Stack<UINode>();
            drawing.Push(root);
            try
            {
                UIElements.ReadProps(run, root, State);
                Interpreter.ExecuteNodes(layout, State.Child(), Module);
                Tree = root;
                Error = null;
            }
            catch (KScriptException ex) when (!throwErrors)
            {
                Error = Interpreter.WithLocation(ex, Module).ToString();
            }
            finally
            {
                drawing = previous;
            }
            if (notify) Redrawn?.Invoke();
        }

        /// <summary>Runs a node's handler (on-click, on-tick…) and redraws. Returns false if it has no such handler.</summary>
        public bool Fire(UINode node, string handler, Dictionary<string, object> evt = null)
        {
            if (!node.Handlers.TryGetValue(handler, out UIHandler h)) return false;
            try
            {
                Scope scope = h.Scope.Child();
                scope.Declare("event", evt ?? new Dictionary<string, object>());
                Interpreter.ExecuteStatements(h.Statements, scope, h.Module, declare: false);
            }
            catch (KScriptException ex)
            {
                // Draw what the handler changed before it stopped, then show why it stopped.
                string error = Interpreter.WithLocation(ex, h.Module).ToString();
                Draw(throwErrors: false, notify: false);
                Error = error;
                Redrawn?.Invoke();
                return true;
            }
            Redraw();
            return true;
        }

        /// <summary>The person changed a bound input: stores the value, runs on-change, and redraws.</summary>
        public void SetBound(UINode node, object value)
        {
            if (node.Bind == null) return;
            try
            {
                node.BindScope.Assign(node.Bind, value);
            }
            catch (KScriptException ex)
            {
                Error = Interpreter.WithLocation(ex, Module).ToString();
                Redrawn?.Invoke();
                return;
            }
            var evt = new Dictionary<string, object> { ["value"] = value };
            if (!Fire(node, "on-change", evt)) Redraw();
        }

        #region For tests and tools

        /// <summary>Every node of a type, in the order they are drawn.</summary>
        public List<UINode> FindAll(string type) => Tree.Descendants().Where(n => n.Type == type).ToList();

        /// <summary>The first node of a type whose text is <paramref name="text"/>.</summary>
        public UINode Find(string type, string text) =>
            Tree.Descendants().FirstOrDefault(n => n.Type == type && string.Equals(n.Text?.Trim(), text, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No <{type}> says '{text}'. The window has: {string.Join(", ", Tree.Descendants().Where(n => n.Text != null).Select(n => n.ToString()))}");

        /// <summary>Clicks the button (or box) with this text.</summary>
        public void Click(string text) => Fire(Tree.Descendants().First(n => (n.Type == "Button" || n.Type == "Box") && n.Text?.Trim() == text), "on-click");

        #endregion
    }
}
