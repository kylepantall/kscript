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

        public static IEnumerable<AttributeInfo> All => Attributes.Values;
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
            { Name: "focus" } => LiveDirectiveArgument.OptionalExpression,
            { Kind: AttributeKind.Statements or AttributeKind.Expression } => LiveDirectiveArgument.Statements,
            { Kind: AttributeKind.Bool } => LiveDirectiveArgument.Flag,
            _ => LiveDirectiveArgument.Text,
        };

        private static IEnumerable<AttributeInfo> Describe()
        {
            foreach (string name in "click dblclick input change submit keydown keyup enter escape focusin focusout cancel close pointerdown pointerup pointercancel drop dragend".Split(' '))
                yield return new("on-" + name, AttributeKind.Statements, $"Run server-side statements on {name}.");
            yield return new("on-init", AttributeKind.Statements, "Run when the component is initialized.");
            yield return new("on-visible", AttributeKind.Statements, "Run when the element becomes visible.");
            yield return new("on-tick", AttributeKind.Statements, "Run periodically. Set the interval with every.");
            yield return new("on-event", AttributeKind.Statements, "Handle an event emitted by another component.");
            yield return new("on-sort", AttributeKind.Statements, "Handle reordering of sort-item children.");
            yield return new("on-action", AttributeKind.Statements, "Handle a named server action. Read the caller's payload from $event.data.");
            yield return new("live-action", AttributeKind.Text, "Name the signed on-action handler for KScriptLive.call().");
            yield return new("bind", AttributeKind.VariableReference, "Bind this control to writable live state.");
            yield return new("bind-on", AttributeKind.Enum, "Choose when the control sends its value.", false, "input", "change", "blur", "action");
            yield return new("live-id", AttributeKind.Template, "Give this component instance a stable identity.");
            yield return new("live-upload", AttributeKind.Bool, "Send the selected files with the action.");
            yield return new("live-loading", AttributeKind.Bool, "Show this element while an action is pending.");
            yield return new("live-disable", AttributeKind.Bool, "Disable this control while an action is pending, or while a condition holds: @live-disable(while: $saving).");
            yield return new("show", AttributeKind.Expression, "Shows the tag while the value is true and hides it (keeping it in the page) while false: @show($open).");
            yield return new("live-offline", AttributeKind.Bool, "Show this element when the browser is offline.");
            yield return new("live-error", AttributeKind.Bool, "Show this element when an action fails.");
            yield return new("live-cloak", AttributeKind.Bool, "Hide this element until the component starts.");
            yield return new("live-dirty", AttributeKind.VariableReference, "Show this element while a binding has unsent edits. Optionally name a field.");
            yield return new("live-ignore", AttributeKind.Bool, "Preserve this browser-managed subtree during updates.");
            yield return new("live-replace", AttributeKind.Bool, "Replace this element on each redraw.");
            yield return new("live-key", AttributeKind.Template, "Give a repeated element a stable identity across renders.");
            yield return new("live-confirm", AttributeKind.Template, "Show a confirmation message before posting an action.");
            yield return new("live-stream", AttributeKind.Template, "Receive streamed updates for this target name.");
            yield return new("live-portal", AttributeKind.Selector, "Mount this template's content at the matching CSS selector.");
            yield return new("live-client", AttributeKind.Text, "Invoke a registered client action on click.");
            yield return new("live-use-", AttributeKind.Text, "Attach a registered client directive by name.");
            yield return new("live-navigate", AttributeKind.Bool, "Follow this link using in-page navigation.");
            yield return new("live-navigate-once", AttributeKind.Bool, "Run this script only once across in-page visits.");
            yield return new("sort-item", AttributeKind.Template, "Identify a sortable item within an on-sort container.");
            yield return new("sort-axis", AttributeKind.Enum, "Set the direction of the sortable items.", false, "vertical", "horizontal");
            yield return new("drop-files", AttributeKind.Text, "Send files dropped on this element through the named live-upload input inside it: @drop-files('upload').");
            yield return new("drop-class", AttributeKind.Text, "Add classes while something this element accepts is dragged over it: @drop-class('file-over').");
            yield return new("drag-value", AttributeKind.Template, "Send this draggable element's value to the drop handler.");
            yield return new("every", AttributeKind.Template, "Set the on-tick interval, for example 1s.");
            yield return new("event-name", AttributeKind.Text, "Filter on-event by the emitted event name.");
            yield return new(Syntax.Markup.ClassDirective.Attribute, AttributeKind.Expression, "Switch classes on by conditions: @class(done: $todo.done) or @class({ $todo.done: ['done', 'struck'] }).");
            yield return new("focus", AttributeKind.Expression, "Give this element focus when it appears, or when its condition turns true: @focus() or @focus($editing).");
            yield return new("autogrow", AttributeKind.Bool, "Grow this textarea with its text.");
            yield return new(Syntax.Markup.PendingDirective.Attribute, AttributeKind.Expression, "Add classes while an action runs: @pending('saving'), or @pending('crumple', for: 'delete') for one method's actions.");
            yield return new("prevent-default", AttributeKind.Text, "Cancel the default browser action for the named events.");
        }
    }
}
