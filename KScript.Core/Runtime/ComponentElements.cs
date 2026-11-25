using KScript.Metadata;
using KScript.Syntax.Markup;
using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>
    /// Somewhere a component can be drawn from a script: a window, a document. A package adds its surface with
    /// <see cref="ComponentElements.AddSurface"/>. When none is being drawn, a component draws in the terminal.
    /// </summary>
    public abstract class RenderSurface
    {
        /// <summary>The surface's name: one of <see cref="ComponentScript.Surfaces"/>.</summary>
        public abstract string Name { get; }

        /// <summary>True while this surface is being drawn on the current thread.</summary>
        public abstract bool Active { get; }

        /// <summary>
        /// The component for one element. A surface that draws again and again keeps it between draws, so its state
        /// lasts: it calls <paramref name="make"/> with the one it drew last time (or null) and keeps the result.
        /// </summary>
        public abstract Record Instance(ElementRun run, object id, Func<Record, Record> make);

        /// <summary>
        /// Takes what a component's render returned, for a surface a render can return content for (a document
        /// takes Markdown and LaTeX). Most renders draw with elements and return nothing.
        /// </summary>
        public virtual void Returned(ElementRun run, object value)
        {
        }
    }

    /// <summary>
    /// A component written as an element in a script: <c>NoteCard(note: $note)</c> inside a window, a document, or
    /// on its own for the terminal. The one place a component is built and drawn outside a page.
    /// </summary>
    public static class ComponentElements
    {
        private static readonly List<RenderSurface> Surfaces = new List<RenderSurface>();

        [ThreadStatic]
        private static Stack<ElementRun> slots;

        /// <summary>The element a component is written as. Its attributes are the component's fields.</summary>
        internal static readonly ElementInfo Info = new ElementInfo("Component", "A component, drawn where it is written.", ContentKind.Statements)
        {
            AllowsExtraAttributes = true,
        };

        internal static readonly ElementInfo SlotInfo = new ElementInfo(ComponentScript.SlotElement,
            "In a layout or component: where the page's (or caller's) content goes. In a component's render for a window, a document or the terminal, it draws what was written inside the component where it is used.",
            ContentKind.Empty,
            new AttributeInfo("name", AttributeKind.Text, "A named slot, in a page's layout or component."))
        {
            Context = ElementContext.Script | ElementContext.Page,
            Example = "<type name=\"Panel\" extends=\"Component\">\n  <field name=\"title\" type=\"string\" />\n  <fn name=\"render\">\n    <echo>{$title}</echo>\n    <slot />\n  </fn>\n</type>\n<Panel title=\"Notes\">\n  <echo>Buy milk</echo>\n</Panel>",
        };

        /// <summary>Adds a surface. A package calls this once, when it is registered.</summary>
        public static void AddSurface(RenderSurface surface)
        {
            lock (Surfaces)
            {
                Surfaces.RemoveAll(s => s.Name == surface.Name);
                Surfaces.Add(surface);
            }
        }

        private static RenderSurface ActiveSurface()
        {
            lock (Surfaces) return Surfaces.FirstOrDefault(s => s.Active);
        }

        /// <summary>
        /// True when an element that no package declares can be a component here: the script declares a component
        /// by that name, or imports a file that may.
        /// </summary>
        internal static bool MayBeComponent(Module module, string name) =>
            name.Length > 0 && char.IsUpper(name[0]) && module.Document.DescendantElements().Any(e =>
                (ComponentScript.IsComponentType(e) && e.GetAttributeValue("name")?.Trim() == name)
                || (e.Name == "import" && e.GetAttribute("path") != null));

        internal static bool? Run(ElementRun run)
        {
            string name = ElementInfo.BareName(run.Element.Name);
            if (!run.Scope.TryGet(name, out object found) || found is not RecordType type || !type.IsComponent)
                throw run.Error(run.Interpreter.UnknownElement(run.Module, name), null, "Syntax");

            RenderSurface host = ActiveSurface();
            string surface = host?.Name ?? ComponentScript.Terminal;
            string key = ComponentScript.RenderKey(surface);
            if (!type.OwnMethods.TryGetValue(key, out MarkupElement render))
                throw run.Error(ComponentScript.NoRenderFor(type.Name, surface), null, "Type");

            var props = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (MarkupAttribute attribute in run.Element.Attributes)
            {
                props[attribute.Name.Replace('-', '_')] = attribute.Value == null ? true
                    : run.Interpreter.InterpolateValue(attribute.Value, run.Scope, run.Module);
            }
            // id tells two of the same component apart where they are drawn in a loop; a field called id gets it too.
            props.TryGetValue("id", out object id);
            if (type.Field("id") == null) props.Remove("id");

            Record Make(Record previous)
            {
                var mapper = new ShapeMapper(run.Interpreter, type.Closure, type.Module);
                // A field nothing gives a value starts empty, as it does in a component on a page.
                Record record = mapper.MapRecord(props, type, "$", 0, field => ShapeMapper.Initializer(field.Type)?.Invoke());
                mapper.ThrowIfAny(type.Name, TypeRef.Of(type));
                var stored = type.Fields.Where(f => !f.Computed).ToList();
                // What the caller passes follows the caller. Everything else is the component's own and is kept.
                if (previous != null)
                    foreach (FieldDefinition field in stored.Where(f => !f.Input && !props.ContainsKey(f.Name)))
                        record[field.Name] = previous[field.Name];
                record.BoundFields = stored.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
                return record;
            }

            Record instance = host != null ? host.Instance(run, id, Make) : Make(null);
            RecordType owner = type.MethodOwner(key, render);
            Scope scope = CatchClauses.BindReceiver(owner.Closure, instance, recordFields: true);
            UserFunction method = run.Interpreter.DeclareFunction(render, scope, owner.Module, type.Catches, instance);
            slots ??= new Stack<ElementRun>();
            slots.Push(run);
            try
            {
                object returned = run.Interpreter.Call(method, Array.Empty<object>(), scope: run.Scope, module: run.Module);
                host?.Returned(run, returned);
            }
            finally
            {
                slots.Pop();
            }
            return null;
        }

        /// <summary>&lt;Slot /&gt;: runs what the caller wrote inside the component, as the caller wrote it.</summary>
        internal static bool? Slot(ElementRun run)
        {
            if (slots == null || slots.Count == 0)
                throw run.Error(Wording.Prose(ComponentScript.SlotOutsideRender), null, "Syntax");
            // What the caller wrote may hold a Slot of its own, which belongs to the component the caller is in.
            ElementRun caller = slots.Pop();
            try
            {
                caller.RunChildren();
            }
            finally
            {
                slots.Push(caller);
            }
            return null;
        }
    }
}
