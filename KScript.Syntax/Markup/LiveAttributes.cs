using System;
using System.Collections.Generic;
using System.Linq;
using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Pages
{
    public enum LiveDirectiveArgument { Text, OptionalText, Flag, Statements, OptionalExpression }

    /// <summary>The live attributes authors can put on HTML elements and component invocations.</summary>
    public static class LiveAttributes
    {
        private static readonly Dictionary<string, AttributeInfo> Attributes = Describe().ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<AttributeInfo> All => Attributes.Values.Where(a => !Retired.ContainsKey(a.Name));

        /// <summary>
        /// Attributes a shorthand replaced, each with what to write instead. They are still read, so the checker and
        /// the renderer can say so, and both refuse them.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Retired { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["live-confirm"] = "live-confirm is now the handler's confirm: modifier, such as @on-click(delete: $note, confirm: 'Delete this note?').",
            ["prevent-default"] = "prevent-default is now the handler's prevent: modifier, such as @on-keydown(save: $note, key: [ctrl, s], prevent: true).",
            ["bind-on"] = "bind-on is now @bind's on: option, such as @bind($draft, on: blur).",
        };
        public static AttributeInfo Get(string name) => Attributes.GetValueOrDefault(
            name.StartsWith("live-use-", StringComparison.OrdinalIgnoreCase) ? "live-use-" : name);
        public static bool IsDirective(string name) => Get(name) != null;
        /// <summary>
        /// @live-disable(while: $saving): the condition's span in the text, or null when there is none (plain
        /// @live-disable()), and what is wrong with it, or null.
        /// </summary>
        public static (TextSpan? Condition, string Problem) DisableCondition(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() is "true" or "") return (null, null);
            const string usage = "@live-disable() takes nothing, or while: and a condition: @live-disable(while: $saving).";
            ParseResult parse = KScript.Syntax.ExpressionParser.ParseNamedArguments(text);
            var arguments = parse.Nodes.OfType<KScript.Syntax.ArgumentNode>().ToList();
            if (parse.Errors.Count > 0 || arguments.Count != 1 || arguments[0].Name != "while") return (null, usage);
            return (arguments[0].Value.Span, null);
        }

        public static LiveDirectiveArgument? ArgumentFor(string name) => Get(name) switch
        {
            null => null,
            { Name: "live-use-" or "live-dirty" or "prevent-default" } => LiveDirectiveArgument.OptionalText,
            { Name: "focus" or "drag" } => LiveDirectiveArgument.OptionalExpression,
            { Kind: AttributeKind.Statements or AttributeKind.Expression } => LiveDirectiveArgument.Statements,
            { Kind: AttributeKind.Bool } => LiveDirectiveArgument.Flag,
            _ => LiveDirectiveArgument.Text,
        };

        private static IEnumerable<AttributeInfo> Describe()
        {
            foreach ((string name, string description) in new[]
            {
                ("click", "Runs when the element is clicked: `@on-click(toggle: $open)`."),
                ("dblclick", "Runs when the element is double-clicked."),
                ("input", "Runs each time a control's value changes as the person types. The new value is in `$event.value`."),
                ("change", "Runs when a control's value is committed, such as a select being picked or a checkbox ticked."),
                ("submit", "Runs when a form is submitted, instead of the browser posting it: `<form @on-submit($save)>`."),
                ("keydown", "Runs when a key is pressed. Pick the key with `key:`, as in `@on-keydown(save: $note, key: [ctrl, s])`."),
                ("keyup", "Runs when a key is let go."),
                ("enter", "Runs when Enter is pressed in the element: `<input @on-enter(add: $draft, to: $tags)>`."),
                ("escape", "Runs when Escape is pressed in the element."),
                ("focusin", "Runs when the element, or something inside it, gets focus."),
                ("focusout", "Runs when focus leaves the element and everything inside it."),
                ("cancel", "Runs when a `<dialog>` is cancelled, such as with Escape."),
                ("close", "Runs when a `<dialog>` closes."),
                ("pointerdown", "Runs when a mouse button, pen or finger goes down on the element."),
                ("pointerup", "Runs when the mouse button, pen or finger comes up."),
                ("pointercancel", "Runs when the browser takes over a pointer, such as to scroll the page."),
                ("drop", "Runs when something is dropped on the element: a `@drag(copy: true)` ghost, whose value is in `$event.drag_value`."),
                ("dragend", "Runs when a drag of this element ends. After `@drag`, `$event.x` and `$event.y` are where it landed."),
            })
                yield return new("on-" + name, AttributeKind.Statements, description);
            yield return new("on-init", AttributeKind.Statements, "Runs once, when the component is first drawn: load what it shows here.");
            yield return new("on-visible", AttributeKind.Statements, "Runs when the element scrolls into view, such as to load the next page of a list.");
            yield return new("on-tick", AttributeKind.Statements, "Runs again and again, as often as `@every` says: `<p @on-tick(increment: $seconds) every=\"1s\">`.");
            yield return new("on-event", AttributeKind.Statements, "Runs when another live block on the page sends an event with `@emit_event` or `emit:`. Pick which with `@event-name`.");
            yield return new("on-sort", AttributeKind.Statements, "Runs when the person drags one of the `@sort-item` children to a new place. `$event.order` is the new order.");
            yield return new("on-action", AttributeKind.Statements, "Runs when client code calls the action named by `@live-action` with `KScriptLive.call()`. What it sent is in `$event.data`.");
            yield return new("live-action", AttributeKind.Text, "Names the `@on-action` handler, so client code can call it with `KScriptLive.call()`.");
            yield return new("bind", AttributeKind.VariableReference, "Keeps a control and a field in step: `@bind($title)`. Options such as `on: blur` and `trim: true` go after the field.");
            yield return new("bind-on", AttributeKind.Enum, "Choose when the control sends its value.", false, "input", "change", "blur", "action");
            yield return new("live-id", AttributeKind.Template, "Gives this component a name that stays the same between updates, so two of the same component keep their own state.");
            yield return new("live-upload", AttributeKind.Bool, "Sends the files picked in this file input with the next action. They are in `$event.files`.");
            yield return new("live-loading", AttributeKind.Bool, "Shows the element only while an action is running, such as a spinner.");
            yield return new("live-disable", AttributeKind.Bool, "Disable this control while an action is pending, or while a condition holds: @live-disable(while: $saving).");
            yield return new(KScript.Syntax.Markup.ElementDirectives.If, AttributeKind.Expression, "Draws the tag only while the condition is true: @if($note). Same as <if $note> around it.");
            yield return new(KScript.Syntax.Markup.ElementDirectives.Each, AttributeKind.Expression, "Draws the tag once for each item: @each($tasks, as: task, at: i). Same as <for each=\"task\" in=\"$tasks\"> around it.");
            yield return new("show", AttributeKind.Expression, "Shows the tag while the value is true and hides it (keeping it in the page) while false: @show($open).");
            yield return new("live-offline", AttributeKind.Bool, "Shows the element only while the browser is offline.");
            yield return new("live-error", AttributeKind.Bool, "Shows the element only after an action fails.");
            yield return new("live-cloak", AttributeKind.Bool, "Hides the element until the page's live code has started, so nothing half-drawn shows.");
            yield return new("live-dirty", AttributeKind.VariableReference, "Shows the element while a binding has edits not yet sent. Name a field to watch only that one: `@live-dirty($title)`.");
            yield return new("live-ignore", AttributeKind.Bool, "Leaves everything inside the element alone during updates, for a widget the browser or a script manages.");
            yield return new("live-replace", AttributeKind.Bool, "Replaces the element whole on each update instead of patching it.");
            yield return new("live-key", AttributeKind.Template, "Gives each repeated element a key that stays the same between updates, so a moved row keeps its focus and state: `live-key=\"{$task.id}\"`.");
            yield return new("live-confirm", AttributeKind.Template, "Show a confirmation message before posting an action.");
            yield return new("live-stream", AttributeKind.Template, "Receives what an action streams to this name, as it arrives.");
            yield return new("live-portal", AttributeKind.Selector, "Draws this `<template>`'s content somewhere else in the page, at the CSS selector given: `live-portal=\"#modals\"`.");
            yield return new("live-client", AttributeKind.Text, "Runs a client action registered with `KScriptLive` when the element is clicked, without a trip to the server.");
            yield return new("live-use-", AttributeKind.Text, "Attaches a client directive registered with `KScriptLive`, by its name: `@live-use-autosize()`.");
            yield return new("live-navigate", AttributeKind.Bool, "Follows the link without a full page load, swapping in the new page.");
            yield return new("live-navigate-once", AttributeKind.Bool, "Runs this `<script>` only once, even when in-page navigation comes back to the page.");
            yield return new("sort-item", AttributeKind.Template, "Marks a child of an `@on-sort` element as one that can be dragged to a new place, with the value it is known by: `sort-item=\"{$task.id}\"`.");
            yield return new("sort-axis", AttributeKind.Enum, "Says which way the `@sort-item` children run: vertical (the default) or horizontal.", false, "vertical", "horizontal");
            yield return new(Syntax.Markup.DragDirective.Attribute, AttributeKind.Expression, "Move this element with the pointer, and run its on-dragend handler where it lands: @drag(front: true). With copy: true, drag a ghost onto an on-drop target.");
            yield return new("drop-files", AttributeKind.Text, "Send files dropped on this element through the named live-upload input inside it: @drop-files('upload').");
            yield return new("drop-class", AttributeKind.Text, "Add classes while something this element accepts is dragged over it: @drop-class('file-over').");
            yield return new("every", AttributeKind.Template, "How often `@on-tick` runs: `every=\"1s\"`.");
            yield return new("event-name", AttributeKind.Text, "Runs `@on-event` only for events with this name: `event-name=\"notes-changed\"`.");
            yield return new(Syntax.Markup.ClassDirective.Attribute, AttributeKind.Expression, "Switch classes on by conditions: @class(done: $todo.done) or @class({ $todo.done: ['done', 'struck'] }).");
            yield return new("focus", AttributeKind.Expression, "Give this element focus when it appears, or when its condition turns true: @focus() or @focus($editing).");
            yield return new("autogrow", AttributeKind.Bool, "Makes a `<textarea>` grow taller as its text grows.");
            yield return new(Syntax.Markup.PendingDirective.Attribute, AttributeKind.Expression, "Add classes while an action runs: @pending('saving'), or @pending('crumple', for: 'delete') for one method's actions.");
            yield return new("prevent-default", AttributeKind.Text, "Cancel the default browser action for the named events.");
        }
    }
}
