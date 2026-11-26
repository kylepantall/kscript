using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// A handler's optional named shorthands, <c>@on-click(toggle: $open)</c> or <c>@on-click(add: $draft, to: $tags)</c>.
    /// One table here is read by the runtime, the checker, completion and hover. A handler is shorthands or statements,
    /// never both, and its actions run left to right.
    /// </summary>
    public static class HandlerShorthands
    {
        /// <summary>One action a handler can name, with the argument that may (or must) go with it.</summary>
        /// <param name="Name">The action: <c>toggle</c>.</param>
        /// <param name="Companion">The argument that goes with it (<c>by</c>, <c>to</c>, <c>from</c>, <c>with</c>), or null.</param>
        /// <param name="CompanionRequired">True when the action can't run without its companion.</param>
        /// <param name="FieldIsCompanion">True when the companion, not the action's own value, is the field that changes (<c>add: $x, to: $list</c>).</param>
        /// <param name="FieldType">The type the changed field must have (bool, number, list), or null for any.</param>
        /// <param name="Statement">The statement it stands for, with {field} and {value} (the action's value or the companion's).</param>
        /// <param name="Description">One line for hover and completion.</param>
        public sealed record Action(string Name, string Companion, bool CompanionRequired, bool FieldIsCompanion, string FieldType,
            string Statement, string Description);

        public const string Toggle = "toggle";
        public const string Call = "call";

        /// <summary>Every action, in the order completion lists them.</summary>
        public static IReadOnlyList<Action> Actions { get; } = new[]
        {
            new Action(Toggle, null, false, false, KType.Bool, "{field} = not {field}", "Flips a bool field: toggle: $open is open = not $open."),
            new Action(Call, "with", false, false, null, null, "Calls a function or one of the component's methods: call: $save, with: [$note.id]."),
            new Action("increment", "by", false, false, KType.Number, "{field} = {field} + {companion}", "Adds 1 (or by:) to a number field: increment: $count, by: 5."),
            new Action("decrement", "by", false, false, KType.Number, "{field} = {field} - {companion}", "Takes 1 (or by:) from a number field: decrement: $stock."),
            new Action("set", "to", true, false, null, "{field} = {companion}", "Sets a field to a value: set: $tab, to: 'notes'."),
            new Action("add", "to", true, true, KType.List, "{field} = {field}->push({value})", "Adds a value to a list field: add: $draft, to: $tags."),
            new Action("remove", "from", true, true, KType.List, "{field} = {field}->remove({value})", "Removes a value from a list field: remove: $tag, from: $tags."),
        };

        /// <summary>The names a handler takes, actions and companions, for completion and messages.</summary>
        public static IReadOnlyList<string> ActionNames { get; } = Actions.Select(a => a.Name).ToList();

        /// <summary>The action of this name, or null.</summary>
        public static Action Get(string name) => Actions.FirstOrDefault(a => a.Name == name);

        /// <summary>True when the handler is written as shorthands: it starts with a word and a colon.</summary>
        public static bool IsShorthand(string handler) => ExpressionParser.StartsWithNamedArgument(handler);

        /// <summary>One action of a handler as written: <c>add: $draft, to: $tags</c>.</summary>
        public sealed record Step(Action Action, ArgumentNode Value, ArgumentNode Companion)
        {
            /// <summary>The field the action changes, or null for call.</summary>
            public ExprNode Field => Action.Statement == null ? null : Action.FieldIsCompanion ? Companion?.Value : Value.Value;

            public TextSpan Span => TextSpan.FromBounds(Value.Span.Start, (Companion ?? Value).Span.End);
        }

        /// <summary>A handler read as shorthands: its steps in order, and what is wrong with it.</summary>
        public sealed class Reading
        {
            public List<Step> Steps { get; } = new List<Step>();

            /// <summary>Problems, with the span (in the handler's text) to report on and an optional replacement for that span.</summary>
            public List<(TextSpan Span, string Message, string Fix)> Problems { get; } = new List<(TextSpan, string, string)>();

            public ParseResult Parse { get; internal set; }
        }

        /// <summary>Reads a handler written as shorthands into its steps, pairing each companion with the action before it.</summary>
        public static Reading Read(string handler)
        {
            var reading = new Reading { Parse = ExpressionParser.ParseNamedArguments(handler) };
            foreach (ArgumentNode argument in reading.Parse.Nodes.OfType<ArgumentNode>().Where(a => a.Name != null))
            {
                if (Get(argument.Name) is Action action)
                {
                    reading.Steps.Add(new Step(action, argument, null));
                    continue;
                }
                Step last = reading.Steps.Count > 0 ? reading.Steps[^1] : null;
                if (last != null && last.Action.Companion == argument.Name && last.Companion == null)
                {
                    reading.Steps[^1] = last with { Companion = argument };
                    continue;
                }
                bool companion = Actions.Any(a => a.Companion == argument.Name);
                string closest = companion ? null : Closest(argument.Name);
                reading.Problems.Add((argument.NameSpan, companion
                    ? $"{argument.Name}: goes straight after {string.Join(" or ", Actions.Where(a => a.Companion == argument.Name).Select(a => a.Name + ":"))}, and only once."
                    : UnknownMessage(argument.Name), closest));
            }
            foreach (Step step in reading.Steps.Where(s => s.Action.CompanionRequired && s.Companion == null))
            {
                reading.Problems.Add((step.Value.NameSpan, $"{step.Action.Name}: needs {step.Action.Companion}: as well. {step.Action.Description}", null));
            }
            return reading;
        }

        /// <summary>
        /// The statement a step stands for, as source text with a map from each character to an offset in the handler:
        /// the field and values keep their own offsets, and the words added point at the action's name.
        /// Null for call, which is not a statement.
        /// </summary>
        public static (string Text, int[] Map)? Statement(Step step, string handler)
        {
            if (step.Action.Statement == null || step.Field == null) return null;
            var text = new System.Text.StringBuilder();
            var map = new List<int>();
            int at = step.Value.NameSpan.Start;
            void Add(string s, int? from)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    text.Append(s[i]);
                    map.Add(from.HasValue ? from.Value + i : at);
                }
            }
            string pattern = step.Action.Statement;
            for (int i = 0; i < pattern.Length;)
            {
                int open = pattern.IndexOf('{', i);
                if (open < 0)
                {
                    Add(pattern[i..], null);
                    break;
                }
                Add(pattern[i..open], null);
                int close = pattern.IndexOf('}', open);
                string slot = pattern[(open + 1)..close];
                ExprNode node = slot switch
                {
                    "field" => step.Field,
                    "value" => step.Value.Value,
                    _ => step.Companion?.Value,
                };
                if (node == null)
                {
                    Add("1", null); // increment: and decrement: without by:
                }
                else
                {
                    string source = handler.Substring(node.Span.Start, node.Span.Length);
                    // The field is assigned without its $ or $. (open = ...); a value keeps it.
                    bool target = slot == "field" && text.Length == 0;
                    int skip = target ? (source.StartsWith("$.", StringComparison.Ordinal) ? 2 : source.StartsWith('$') ? 1 : 0) : 0;
                    Add(source[skip..], node.Span.Start + skip);
                }
                i = close + 1;
            }
            map.Add(map.Count > 0 ? map[^1] + 1 : at);
            return (text.ToString(), map.ToArray());
        }

        /// <summary>What to say about a name no handler takes.</summary>
        public static string UnknownMessage(string name) =>
            $"A handler has no '{name}:' shorthand. It takes {string.Join(", ", ActionNames.Select(n => n + ":"))}, a function to call, or statements.";

        /// <summary>What to say when an action is given a field of the wrong type.</summary>
        public static string FieldTypeMessage(Action action, string field, string type) =>
            $"{action.Name}: works on a {action.FieldType} field, and {field} is {type}. {action.Description}";

        /// <summary>What to say about a page handler that is a bare bool: the positional argument is an action, so flipping is toggle:.</summary>
        public static string BareBoolMessage(string value) =>
            $"A handler's value is an action to run, and {value} is a bool. Write toggle: {value} to flip it.";

        private static string Closest(string name) =>
            ActionNames.Select(n => (Name: n, Distance: Distance(name, n))).Where(c => c.Distance <= 2).OrderBy(c => c.Distance).Select(c => c.Name).FirstOrDefault();

        private static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
