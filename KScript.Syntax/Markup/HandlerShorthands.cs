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
        public const string Reset = "reset";

        /// <summary>The hidden variable a page declares for reset: with each field's starting value (a FieldDefault).</summary>
        public const string DefaultsVariable = "\0defaults";

        /// <summary>The hidden variable a value is assigned from when a shorthand sets a nested field.</summary>
        public const string ValueVariable = "shorthand_value";
        public const string Cycle = "cycle";
        public const string Decrement = "decrement";

        /// <summary>Every action, in the order completion lists them.</summary>
        public static IReadOnlyList<Action> Actions { get; } = new[]
        {
            new Action(Toggle, null, false, false, KType.Bool, "{field} = not {field}", "Flips a bool field: toggle: $open is open = not $open."),
            new Action(Call, "with", false, false, null, null, "Calls a function or one of the component's methods: call: $save, with: [$note.id]."),
            new Action("increment", "by", false, false, KType.Number, "{field} = {field} + {companion}", "Adds 1 (or by:) to a number field: increment: $count, by: 5."),
            new Action(Decrement, "by", false, false, KType.Number, "{field} = {field} - {companion}", "Takes 1 (or by:) from a number field, never going below its min: (stock: int(min: 0)): decrement: $stock."),
            new Action("set", "to", true, false, null, "{field} = {companion}", "Sets a field to a value: set: $tab, to: 'notes'."),
            new Action("add", "to", true, true, KType.List, "{field} = {field}->push({value})", "Adds a value to a list field: add: $draft, to: $tags."),
            new Action("remove", "from", true, true, KType.List, "{field} = {field}->remove({value})", "Removes a value from a list field: remove: $tag, from: $tags."),
            new Action(Reset, null, false, false, null, null, "Puts a field (or a list of fields) back to its declared default: reset: $draft, or reset: [$title, $done]."),
            new Action(Cycle, "through", false, false, null, null, "Moves a field to the next value of its enum, or of a list given with through:, wrapping round: cycle: $view, through: ['list', 'grid']."),
            new Action("save", null, false, false, null, "{value}->save()", "Saves a KScript.Data record: save: $note is $note->save()."),
            new Action("delete", null, false, false, null, "{value}->delete()", "Deletes a KScript.Data record: delete: $note is $note->delete()."),
            new Action("emit", null, false, false, null, "@emit_event({value})", "Sends an event to the page's live blocks: emit: 'notes-changed' is @emit_event('notes-changed')."),
            new Action("go", null, false, false, null, "@live_redirect({value})", "Sends the browser to a path on this site: go: '/notes' is @live_redirect('/notes')."),
        };

        /// <summary>How a modifier's value is written.</summary>
        public enum ModifierValue
        {
            /// <summary>Text, such as confirm: 'Delete this note?'.</summary>
            Text,

            /// <summary>A key, or a list of keys and modifier keys: key: enter, key: [ctrl, s].</summary>
            Keys,

            /// <summary>A duration: debounce: 300ms.</summary>
            Duration,

            /// <summary>true or false: prevent: true.</summary>
            Flag,
        }

        /// <summary>A modifier changes when or how a handler runs, and goes with any actions: confirm:, key:, debounce:.</summary>
        /// <param name="Name">The modifier: <c>confirm</c>.</param>
        /// <param name="Value">How its value is written.</param>
        /// <param name="Description">One line for hover and completion.</param>
        public sealed record Modifier(string Name, ModifierValue Value, string Description);

        /// <summary>Every modifier, in the order completion lists them. They work on pages; a window has no browser to run them in.</summary>
        public static IReadOnlyList<Modifier> Modifiers { get; } = new[]
        {
            new Modifier("confirm", ModifierValue.Text, "Asks first, and runs only if the person says yes: confirm: 'Delete this note?'."),
            new Modifier("key", ModifierValue.Keys, "Runs only for this key, with any modifier keys: key: enter, key: [ctrl, s]."),
            new Modifier("prevent", ModifierValue.Flag, "Stops the browser's own action for the event, such as saving the page on ctrl+s: prevent: true."),
            new Modifier("debounce", ModifierValue.Duration, "Waits until the events stop for this long, then runs once: debounce: 300ms."),
            new Modifier("throttle", ModifierValue.Duration, "Runs at most once in this long, however often the event fires: throttle: 1s."),
            new Modifier("once", ModifierValue.Flag, "Runs the first time only: once: true."),
            new Modifier("self", ModifierValue.Flag, "Runs only when the event starts on this element itself, not on something inside it: self: true."),
            new Modifier("disable", ModifierValue.Flag, "Disables the element while its action runs: disable: true."),
        };

        /// <summary>The modifier of this name, or null.</summary>
        public static Modifier GetModifier(string name) => Modifiers.FirstOrDefault(m => m.Name == name);

        /// <summary>The keys key: names, with the browser's name for each (KeyboardEvent.key, lower case).</summary>
        public static IReadOnlyDictionary<string, string> KeyNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enter"] = "enter", ["escape"] = "escape", ["esc"] = "escape", ["space"] = " ", ["tab"] = "tab",
            ["backspace"] = "backspace", ["delete"] = "delete", ["up"] = "arrowup", ["down"] = "arrowdown",
            ["left"] = "arrowleft", ["right"] = "arrowright", ["home"] = "home", ["end"] = "end",
        };

        /// <summary>The modifier keys key: can hold beside a key.</summary>
        public static IReadOnlyList<string> ModifierKeys { get; } = new[] { "ctrl", "alt", "shift", "meta" };

        /// <summary>The actions a handler takes, for completion and messages.</summary>
        public static IReadOnlyList<string> ActionNames { get; } = Actions.Select(a => a.Name).ToList();

        /// <summary>
        /// The actions and companions a window element with one obvious event takes straight on it:
        /// <c>Button(toggle: $open) "Details"</c> is <c>Button(on_click: "toggle: $open")</c>. emit: and go: are for pages.
        /// </summary>
        public static IReadOnlyList<string> WindowNames { get; } = Actions.Where(a => a.Name is not ("emit" or "go"))
            .SelectMany(a => a.Companion == null ? new[] { a.Name } : new[] { a.Name, a.Companion }).Distinct().ToList();

        /// <summary>Every name a handler takes on its own: actions, then modifiers.</summary>
        public static IReadOnlyList<string> Names { get; } = ActionNames.Concat(Modifiers.Select(m => m.Name)).ToList();

        /// <summary>The action of this name, or null.</summary>
        public static Action Get(string name) => Actions.FirstOrDefault(a => a.Name == name);

        /// <summary>True when the handler is written as shorthands: it starts with a word and a colon.</summary>
        public static bool IsShorthand(string handler) => ExpressionParser.StartsWithNamedArgument(handler);

        /// <summary>One action of a handler as written: <c>add: $draft, to: $tags</c>.</summary>
        public sealed record Step(Action Action, ArgumentNode Value, ArgumentNode Companion)
        {
            /// <summary>The field the action changes, or null when it changes none (call:, save:, emit:).</summary>
            public ExprNode Field => Action.Name is Reset or Cycle ? Value.Value
                : Action.Statement?.Contains("{field}", StringComparison.Ordinal) != true ? null
                : Action.FieldIsCompanion ? Companion?.Value : Value.Value;

            public TextSpan Span => TextSpan.FromBounds(Value.Span.Start, (Companion ?? Value).Span.End);
        }

        /// <summary>A handler read as shorthands: its steps in order, and what is wrong with it.</summary>
        public sealed class Reading
        {
            public List<Step> Steps { get; } = new List<Step>();

            /// <summary>The modifiers, each with its argument as written.</summary>
            public List<(Modifier Modifier, ArgumentNode Argument)> Modifiers { get; } = new List<(Modifier, ArgumentNode)>();

            /// <summary>The modifier of this name, or null.</summary>
            public ArgumentNode ModifierArgument(string name) => Modifiers.FirstOrDefault(m => m.Modifier.Name == name).Argument;

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
                if (GetModifier(argument.Name) is Modifier modifier)
                {
                    if (reading.Modifiers.Any(m => m.Modifier == modifier))
                    {
                        reading.Problems.Add((argument.NameSpan, $"{modifier.Name}: is given twice. A handler takes each modifier once.", null));
                        continue;
                    }
                    if (ModifierProblem(modifier, argument.Value) is string problem)
                    {
                        reading.Problems.Add((argument.Value.Span, problem, null));
                        continue;
                    }
                    reading.Modifiers.Add((modifier, argument));
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
            if (reading.Steps.Count == 0 && reading.Modifiers.Count > 0)
            {
                ArgumentNode first = reading.Modifiers[0].Argument;
                reading.Problems.Add((first.NameSpan, $"{first.Name}: changes when an action runs, and this handler has no action. Add one, such as call: $save or toggle: $open.", null));
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
            if (step.Action.Statement == null) return null;
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

        /// <summary>What is wrong with a modifier's value as written, or null. Text and durations are checked when they run.</summary>
        public static string ModifierProblem(Modifier modifier, ExprNode value)
        {
            switch (modifier.Value)
            {
                case ModifierValue.Keys:
                    IEnumerable<ExprNode> keys = value is ListNode list ? list.Items : (IEnumerable<ExprNode>)new[] { value };
                    var names = new List<string>();
                    foreach (ExprNode key in keys)
                    {
                        if (KeyName(key) is not string name) return KeyMessage;
                        names.Add(name);
                    }
                    int plain = names.Count(n => !ModifierKeys.Contains(n));
                    return plain == 1 ? null : plain == 0 ? "key: needs a key as well as ctrl, alt, shift or meta: key: [ctrl, s]." : "key: takes one key, with any of ctrl, alt, shift and meta: key: [ctrl, s].";
                case ModifierValue.Flag:
                    return value is LiteralNode { Kind: LiteralKind.Bool } ? null : $"{modifier.Name}: takes true or false: {modifier.Name}: true.";
                default:
                    return null;
            }
        }

        private const string KeyMessage = "key: takes a key name (enter, escape, space, tab, up, down, left, right, a letter or a digit), or a list with ctrl, alt, shift or meta: key: [ctrl, s].";

        /// <summary>A key as key: writes it (enter, s, ctrl), or null when it isn't one.</summary>
        public static string KeyName(ExprNode node)
        {
            string name = node switch
            {
                IdentifierNode id => id.Name,
                LiteralNode { Kind: LiteralKind.String, Template: null } text => text.Value?.ToString(),
                LiteralNode { Kind: LiteralKind.Number } number => number.Value is { Length: 1 } digit && char.IsDigit(digit[0]) ? digit : null,
                _ => null,
            };
            if (name == null) return null;
            name = name.ToLowerInvariant();
            return KeyNames.ContainsKey(name) || ModifierKeys.Contains(name) || name.Length == 1 && char.IsLetterOrDigit(name[0]) ? name : null;
        }

        /// <summary>The keys of a key: modifier as the browser names them: a key ("enter", "s") and the modifier keys it needs.</summary>
        public static (string Key, IReadOnlyList<string> With) Keys(ExprNode value)
        {
            IEnumerable<string> names = (value is ListNode list ? list.Items : (IEnumerable<ExprNode>)new[] { value }).Select(KeyName).ToList();
            string key = names.Single(n => !ModifierKeys.Contains(n));
            return (KeyNames.TryGetValue(key, out string browser) ? browser : key, names.Where(ModifierKeys.Contains).ToList());
        }

        /// <summary>True when a browser event (its key, and ctrl, alt, shift and meta) is the one a key: modifier names.</summary>
        public static bool KeyMatches(ExprNode value, IReadOnlyDictionary<string, object> ev)
        {
            (string key, IReadOnlyList<string> with) = Keys(value);
            string pressed = ev != null && ev.TryGetValue("key", out object k) && k != null ? k.ToString().ToLowerInvariant() : null;
            if (pressed != key) return false;
            return ModifierKeys.All(m => with.Contains(m) == (ev.TryGetValue(m, out object on) && on is true));
        }

        /// <summary>What to say about a modifier in a window, which has no browser to run it.</summary>
        public static string WindowModifierMessage(string name) =>
            $"{name}: works on pages, where the browser runs it. A window handler takes actions only, such as toggle: or call:.";

        /// <summary>What to say about a name no handler takes.</summary>
        public static string UnknownMessage(string name) =>
            $"A handler has no '{name}:' shorthand. It takes {string.Join(", ", ActionNames.Select(n => n + ":"))} (with {string.Join(", ", Modifiers.Select(m => m.Name + ":"))}), a function to call, or statements.";

        /// <summary>What to say when an action is given a field of the wrong type.</summary>
        public static string FieldTypeMessage(Action action, string field, string type) =>
            $"{action.Name}: works on a {action.FieldType} field, and {field} is {type}. {action.Description}";

        /// <summary>What to say about a page handler that is a bare bool: the positional argument is an action, so flipping is toggle:.</summary>
        public static string BareBoolMessage(string value) =>
            $"A handler's value is an action to run, and {value} is a bool. Write toggle: {value} to flip it.";

        private static string Closest(string name) =>
            Names.Select(n => (Name: n, Distance: Distance(name, n))).Where(c => c.Distance <= 2).OrderBy(c => c.Distance).Select(c => c.Name).FirstOrDefault();

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
