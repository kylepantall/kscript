using System;
using System.Collections.Generic;
using System.Linq;
using KScript.Metadata;

namespace KScript.Pages
{
    /// <summary>
    /// What <c>$event</c> holds in a handler: the fields the browser sends for a page's <c>on-*</c> handlers (live.js
    /// fills them in), and the few a window's <c>on_*</c> handlers get. The editor types <c>$event</c> from this table,
    /// so <c>$event.</c> offers only what the handler really receives, and a test checks it against live.js.
    /// </summary>
    public static class EventFields
    {
        /// <summary>One field of <c>$event</c>. <see cref="Type"/> is a type as written in KScript (<c>list&lt;string&gt;</c>).</summary>
        public sealed record Field(string Name, string Type, string Description);

        /// <summary>The name <c>$event</c>'s type is shown by.</summary>
        public const string TypeName = "event";

        /// <summary>The field that carries what another component or client code sent: <c>$event.data</c>.</summary>
        public const string Data = "data";

        /// <summary>Every page handler gets these, whatever the event.</summary>
        public static IReadOnlyList<Field> Common { get; } = new[]
        {
            new Field("type", KType.String, "The event's name: click, input, submit, component, action, sort..."),
            new Field("value", KType.String, "The value of the element the event came from (an input, select or textarea), or null."),
            new Field("checked", KType.Bool, "For a checkbox: whether it is ticked. Null for other elements."),
            new Field("target_id", KType.String, "The id of the element the event came from, or null."),
            new Field("key", KType.String, "For a key event: the key pressed, such as Enter, Escape or a. Null otherwise."),
            new Field("code", KType.String, "For a key event: the physical key, such as KeyA or Digit1. Null otherwise."),
            new Field("alt", KType.Bool, "True if Alt (Option) was held."),
            new Field("ctrl", KType.Bool, "True if Ctrl was held."),
            new Field("meta", KType.Bool, "True if Cmd (Windows key) was held."),
            new Field("shift", KType.Bool, "True if Shift was held."),
            new Field("repeat", KType.Bool, "True if a key event is a repeat from a key held down."),
            new Field("client_x", KType.Number, "The pointer's x in the window, in pixels, or null."),
            new Field("client_y", KType.Number, "The pointer's y in the window, in pixels, or null."),
            new Field("page_x", KType.Number, "The pointer's x in the page, counting scroll, or null."),
            new Field("page_y", KType.Number, "The pointer's y in the page, counting scroll, or null."),
            new Field("x", KType.Number, "The pointer's x inside the handler's element, counting its scroll, or null."),
            new Field("y", KType.Number, "The pointer's y inside the handler's element, counting its scroll, or null."),
            new Field("button", KType.Number, "For a pointer event: the button (0 main, 1 middle, 2 secondary), or null."),
            new Field("pointer_id", KType.Number, "For a pointer event: the pointer's id, or null."),
            new Field("pointer_type", KType.String, "For a pointer event: mouse, pen or touch, or null."),
            new Field("files", KType.Map, "Files uploaded with the event, by the name of their live-upload input."),
        };

        /// <summary>The extra fields an event carries, by handler attribute (<c>on-event</c>).</summary>
        private static readonly Dictionary<string, Field[]> Extra = new Dictionary<string, Field[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["on-event"] = new[]
            {
                new Field("name", KType.String, "The event's name, as given to @emit_event."),
                new Field(Data, KType.Any, "What @emit_event sent with the event (its data: argument), or null."),
                new Field("source", KType.String, "The id of the live block that emitted the event."),
            },
            ["on-action"] = new[]
            {
                new Field("name", KType.String, "The action's name."),
                new Field(Data, KType.Any, "What the caller sent with the action, or null."),
            },
            ["on-sort"] = new[]
            {
                new Field("item", KType.String, "The sort-item value of the item that moved."),
                new Field("from", KType.Number, "Where the item was, counting from 0."),
                new Field("index", KType.Number, "Where the item is now, counting from 0."),
                new Field("order", "list<string>", "Every item's sort-item value, in the new order."),
            },
            ["on-drop"] = new[]
            {
                new Field("drag_value", KType.String, "The value of the @drag element dropped here, or null."),
            },
            ["on-dragend"] = new[]
            {
                new Field("vx", KType.Number, "How fast the pointer moved across at the end, in pixels per frame."),
                new Field("vy", KType.Number, "How fast the pointer moved down at the end, in pixels per frame."),
                new Field("moved", KType.Bool, "True if the element was dragged, false for a press without moving."),
                new Field("z", KType.Number, "With @drag(front: true): the z-index the element was given, or null."),
            },
        };

        /// <summary>The handler attributes whose events carry fields beyond <see cref="Common"/>.</summary>
        public static IEnumerable<string> HandlersWithExtraFields => Extra.Keys;

        /// <summary>
        /// The fields <c>$event</c> has in a page handler written as <paramref name="attribute"/> (<c>on-click</c>,
        /// <c>on-event</c>...), or in an <c>&lt;on&gt;</c> block when it is null. The handler's own fields come first,
        /// and replace common ones of the same name.
        /// </summary>
        public static IReadOnlyList<Field> ForPage(string attribute)
        {
            if (attribute == null || !Extra.TryGetValue(attribute, out Field[] extra)) return Common;
            HashSet<string> names = extra.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            return extra.Concat(Common.Where(f => !names.Contains(f.Name))).ToList();
        }

        /// <summary>
        /// The fields <c>$event</c> has in a window's handler (<c>on_click</c>, <c>on_enter</c>...): a text box's
        /// on_enter and a bound control's on_change get the value; other handlers get nothing.
        /// </summary>
        public static IReadOnlyList<Field> ForWindow(string attribute) => attribute is "on_enter" or "on_change"
            ? new[] { new Field("value", KType.Any, attribute == "on_enter" ? "The text in the box when Enter was pressed." : "The control's new value.") }
            : Array.Empty<Field>();

        /// <summary>What <c>$event</c> is, for its hover, in a handler written as <paramref name="attribute"/>.</summary>
        public static string Describe(string attribute) => attribute switch
        {
            null => "The browser event this `<on>` block runs for.",
            "on-event" => "The event another live block sent with `@emit_event`. Read what it sent from `$event.data`.",
            "on-action" => "The named action this handler runs for. Read what the caller sent from `$event.data`.",
            "on-sort" => "The reorder this handler runs for: which item moved, from where to where.",
            _ when attribute.StartsWith("on_", StringComparison.Ordinal) => $"The window event this `{attribute}` handler runs for.",
            _ => $"The browser event this `{attribute}` handler runs for.",
        };
    }
}
