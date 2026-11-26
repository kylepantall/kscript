using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.UI
{
    /// <summary>Runs KScript.UI elements: each adds a <see cref="UINode"/> to the window being drawn.</summary>
    internal static class UIElements
    {
        internal static Action CloseRequested;

        internal static void RequestClose() => CloseRequested?.Invoke();

        /// <summary>&lt;KScript.Window&gt;: runs the setup, draws the window, and shows it until it is closed.</summary>
        internal static bool? Window(ElementRun run)
        {
            UISession session = UISession.Start(run);
            Action<UISession> host = UIPackage.Host;
            if (host != null)
            {
                host(session);
            }
            else
            {
                // macOS only allows windows on the process's first thread, which is waiting for the script.
                run.Interpreter.InvokeOnStartingThread(() => DesktopHost.Show(session));
            }
            return null;
        }

        /// <summary>A layout element or control: records it (and its children) in the window being drawn.</summary>
        internal static bool? Node(ElementRun run, string type, bool text)
        {
            UINode parent = UISession.CurrentParent
                ?? throw run.Error($"<{run.Element.Name}> only works inside <KScript.Window>.", null, "Syntax");
            var node = new UINode(type, run.Element);
            ReadProps(run, node, run.Scope);
            parent.Children.Add(node);

            if (text)
            {
                node.Text = run.Text();
            }
            else if (run.Element.ChildElements.Any())
            {
                UISession.Push(node);
                try
                {
                    run.RunChildren();
                }
                finally
                {
                    UISession.Pop();
                }
            }
            return null;
        }

        /// <summary>Evaluates an element's attributes into the node: values, handlers and bind.</summary>
        internal static void ReadProps(ElementRun run, UINode node, Scope scope)
        {
            ElementInfo info = run.Definition.Info;
            var shorthand = new System.Text.StringBuilder();
            var map = new List<int>();
            foreach (MarkupAttribute attr in run.Element.Attributes)
            {
                AttributeInfo a = info.GetAttribute(attr.Name);
                if (a == null || attr.Value == null) continue;
                if (HandlerShorthands.WindowNames.Contains(attr.Name) && info.GetAttribute("on_click") != null)
                {
                    // Button(toggle: $open): the shorthands, in order, are the element's on_click.
                    if (shorthand.Length > 0)
                    {
                        shorthand.Append(", ");
                        map.AddRange(Enumerable.Repeat(attr.Value.ToSource(0), 2));
                    }
                    string head = attr.Name + ": ";
                    shorthand.Append(head).Append(attr.Value.Value);
                    map.AddRange(Enumerable.Repeat(attr.Value.ToSource(0), head.Length));
                    map.AddRange(Enumerable.Range(0, attr.Value.Value.Length).Select(attr.Value.ToSource));
                    continue;
                }
                switch (a.Kind)
                {
                    case AttributeKind.Statements:
                        node.Handlers[attr.Name] = new UIHandler(attr.Value, scope, run.Module);
                        break;
                    case AttributeKind.VariableReference when attr.Name == "bind":
                    {
                        string name = attr.ValueText.Trim().TrimStart('$');
                        if (!scope.TryGet(name, out object value))
                            throw run.Error($"bind=\"{name}\": ${name} isn't declared. Add <let name=\"{name}\" value=\"…\" /> directly inside <KScript.Window>.", "bind", "Name");
                        node.Bind = name;
                        node.BindScope = scope;
                        node.Props["value"] = value;
                        break;
                    }
                    case AttributeKind.Expression:
                        node.Props[attr.Name] = run.Interpreter.EvaluateText(attr.Value, scope, run.Module);
                        break;
                    case AttributeKind.Bool:
                        // yes / no, or { ... } worked out each time the window is drawn, as everywhere else.
                        node.Props[attr.Name] = Values.ParseBool(attr.ValueText) ?? Values.Truthy(run.Interpreter.InterpolateValue(attr.Value, scope, run.Module));
                        break;
                    default:
                        node.Props[attr.Name] = run.Interpreter.Interpolate(attr.Value, scope, run.Module, bareReferences: true);
                        break;
                }
            }
            if (shorthand.Length > 0)
            {
                if (node.Handlers.ContainsKey("on_click"))
                    throw run.Error("Use on_click or shorthands such as toggle: $open here, not both.", "on_click", "Syntax");
                map.Add(map[^1] + 1);
                node.Handlers["on_click"] = new UIHandler(new DecodedText(shorthand.ToString(), map.ToArray()), scope, run.Module);
            }
        }
    }
}
