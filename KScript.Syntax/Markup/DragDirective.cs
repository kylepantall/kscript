using KScript.Metadata;
using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// <c>@drag(...)</c> on an HTML tag: the browser moves the element with the pointer, or with <c>copy: true</c>
    /// drags a ghost copy onto an <c>@on-drop</c> target. Every option is named and optional. One table here is read
    /// by the renderer, the checker and completion.
    /// </summary>
    public static class DragDirective
    {
        /// <summary>The attribute a <c>@drag(...)</c> directive becomes.</summary>
        public const string Attribute = "drag";

        /// <summary>One option: its name, the type its value must have, and one line for hover and completion.</summary>
        public sealed record Option(string Name, string Type, string Description);

        public static IReadOnlyList<Option> Options { get; } = new[]
        {
            new Option("front", KType.Bool, "Raise the element above the other @drag elements in its positioned parent. $event.z is the z-index it got."),
            new Option("scroll", KType.String, "The scrolling container (a CSS selector) that scrolls when the pointer nears its edges. Defaults to the nearest one."),
            new Option("skip", KType.String, "Presses on these (a CSS selector) belong to them, not to dragging. Fields, buttons, links and labels are always skipped."),
            new Option("threshold", KType.Number, "How far, in pixels, the pointer moves before a press becomes a drag. Defaults to 4, so a click stays a click."),
            new Option("copy", KType.Bool, "Drag a ghost copy instead of the element, to drop on an @on-drop target."),
            new Option("value", KType.Any, "With copy: the value an @on-drop handler reads as $event.drag_value."),
            new Option("ghost", KType.String, "With copy: the classes of the ghost that follows the pointer. Defaults to the element's own."),
        };

        /// <summary>The option of this name, or null.</summary>
        public static Option Get(string name) => Options.FirstOrDefault(o => o.Name == name);

        /// <summary>A directive read into its options, and what is wrong with it.</summary>
        public sealed class Reading
        {
            public List<(Option Option, ArgumentNode Argument)> Values { get; } = new List<(Option, ArgumentNode)>();
            public List<(TextSpan Span, string Message)> Problems { get; } = new List<(TextSpan, string)>();
        }

        /// <summary>Reads a <c>@drag(...)</c> directive's text. Empty text is a drag with every default.</summary>
        public static Reading Read(string text)
        {
            var reading = new Reading();
            if (string.IsNullOrWhiteSpace(text)) return reading;
            ParseResult parse = ExpressionParser.ParseNamedArguments(text);
            foreach (SyntaxError error in parse.Errors)
                reading.Problems.Add((error.Span, error.Message.StartsWith("Expected a shorthand", StringComparison.Ordinal)
                    ? "@drag takes named options, such as @drag(front: true) or @drag(copy: true, value: $color)." : error.Message));
            if (parse.Errors.Count > 0) return reading;
            foreach (ArgumentNode argument in parse.Nodes.OfType<ArgumentNode>())
            {
                if (Get(argument.Name) is not Option option)
                {
                    reading.Problems.Add((argument.NameSpan, $"@drag has no {argument.Name}: option. It takes {string.Join(", ", Options.Select(o => o.Name + ":"))}."));
                    continue;
                }
                if (reading.Values.Any(v => v.Option == option))
                {
                    reading.Problems.Add((argument.NameSpan, $"{option.Name}: is given twice."));
                    continue;
                }
                reading.Values.Add((option, argument));
            }
            bool copy = reading.Values.Any(v => v.Option.Name == "copy");
            foreach ((Option option, ArgumentNode argument) in reading.Values)
                if (option.Name is "value" or "ghost" && !copy)
                    reading.Problems.Add((argument.NameSpan, $"{option.Name}: goes with copy: true, which drags a ghost copy onto an @on-drop target."));
            return reading;
        }
    }
}
