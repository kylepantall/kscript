using KScript.Metadata;
using KScript.Pages;
using KScript.Syntax.Markup;

namespace KScript.Language.Analysis
{
    public sealed partial class Analyzer
    {
        /// <summary>
        /// $event in a handler, typed from what that handler really gets (<see cref="EventFields"/>): a click has
        /// key, x, y..., an on-sort has item, from, index and order. In an on-event listener, $event.data takes the
        /// shape of what the matching @emit_event calls send, found across the project.
        /// </summary>
        /// <param name="e">The element the handler is on (or the &lt;on&gt; block).</param>
        /// <param name="attribute">The handler attribute (on-click, on_enter...), or null for an &lt;on&gt; block.</param>
        private Symbol EventSymbol(MarkupElement e, string attribute)
        {
            bool window = attribute != null && attribute.StartsWith("on_", StringComparison.Ordinal);
            IReadOnlyList<EventFields.Field> fields = window ? EventFields.ForWindow(attribute) : EventFields.ForPage(attribute);
            TypeShape shape = new TypeShape(KType.Map) { Name = EventFields.TypeName, Closed = true };
            foreach (EventFields.Field field in fields)
            {
                TypeShape fieldShape = field.Type == KType.Any ? TypeShape.Unknown : field.Type.Contains('<') ? ShapeText.Parse(field.Type) : new TypeShape(field.Type);
                shape.Fields[field.Name] = new FieldInfo(field.Name, fieldShape, null, field.Description);
            }
            if (string.Equals(attribute, "on-event", StringComparison.OrdinalIgnoreCase) && shape.Fields.ContainsKey(EventFields.Data))
            {
                shape.Fields[EventFields.Data] = EmittedData(e);
            }
            Symbol symbol = new Symbol { Name = "event", Kind = SymbolKind.BuiltIn, Shape = shape, Description = EventFields.Describe(attribute) };
            result.Symbols.Add(symbol);
            return symbol;
        }

        /// <summary>$event.data in an on-event listener: the shape every matching @emit_event call sends, and where they are.</summary>
        private FieldInfo EmittedData(MarkupElement e)
        {
            string name = e.GetAttributeValue("event-name")?.Trim();
            if (string.IsNullOrEmpty(name) || name.Contains('{'))
            {
                name = null;
            }
            List<EmittedEvent> emits = EmittedEvents.Find(result.Path, source.Text, name, files, catalog);
            string events = name != null ? $"'{name}'" : "any event";
            if (emits.Count == 0)
            {
                return new FieldInfo(EventFields.Data, TypeShape.Unknown, null,
                    $"What @emit_event sent with {events}, or null. No @emit_event call for {events} was found, so its type isn't known.");
            }
            TypeShape data = Unify(emits.Select(c => c.Data ?? new TypeShape(KType.Null)));
            const int listed = 3;
            IEnumerable<string> where = emits.Take(listed).Select(c => $"{EmitLocation(c)} (line {c.At.Line + 1})");
            string sources = string.Join(", ", where) + (emits.Count > listed ? $" and {emits.Count - listed} more" : string.Empty);
            return new FieldInfo(EventFields.Data, data, null, $"What @emit_event sends with {events}: from {sources}.");
        }

        /// <summary>Where an @emit_event call is: "this file", or its path from the project's folder.</summary>
        private string EmitLocation(EmittedEvent call)
        {
            string here = result.Path != null ? System.IO.Path.GetFullPath(result.Path) : null;
            if (string.Equals(here, call.Path, StringComparison.Ordinal))
            {
                return "this file";
            }
            string from = ProjectUsage.ProjectRoot(System.IO.Path.GetDirectoryName(call.Path), files) ?? System.IO.Path.GetDirectoryName(call.Path);
            return System.IO.Path.GetRelativePath(from, call.Path).Replace('\\', '/');
        }
    }
}
