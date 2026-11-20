using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.UI
{
    /// <summary>
    /// KScript.UI: desktop windows. A script whose root is &lt;KScript.Window&gt; opens a native window (macOS,
    /// Windows, Linux) laid out with &lt;Column&gt;, &lt;Row&gt; and &lt;Grid&gt; and filled with &lt;Text&gt;,
    /// &lt;Button&gt;, &lt;TextBox&gt; and friends. on-* attributes run KScript when the person uses a control, and
    /// the window is drawn again from its variables, like a &lt;live&gt; block on a page.
    /// </summary>
    /// <remarks>
    /// This class only describes elements, so tools can load it without starting a GUI. The window itself is
    /// shown by <see cref="DesktopHost"/>, which is only touched when a script opens one.
    /// </remarks>
    public sealed class UIPackage : KScriptPackage
    {
        public override string Name => "KScript.UI";

        public override string Description => "Desktop windows with buttons, text, inputs and timers, on macOS, Windows and Linux.";

        /// <summary>Replaces the desktop for a run: tests drive windows through their <see cref="UISession"/> instead.</summary>
        public static Action<UISession> Host { get; set; }

        private static readonly A[] Box =
        {
            new A("width", K.Expression, "Width in pixels."),
            new A("height", K.Expression, "Height in pixels."),
            new A("padding", K.Expression, "Space inside the edge, in pixels."),
            new A("margin", K.Expression, "Space outside the edge, in pixels."),
            new A("background", K.Template, "A colour: a name such as teal, or #1e293b."),
            new A("radius", K.Expression, "How rounded the corners are, in pixels."),
            new A("tooltip", K.Template, "Text shown when the pointer rests on it."),
        };

        private static A[] With(A[] common, params A[] own) => own.Concat(common).ToArray();

        private static readonly A Gap = new A("gap", K.Expression, "Space between items, in pixels.");
        private static readonly A Align = new A("align", K.Enum, "How items line up across the container: start, center, end or stretch.", false, "start", "center", "end", "stretch");
        private static readonly A Disabled = new A("disabled", K.Expression, "yes greys it out so it can't be used.");
        private static readonly A Bind = new A("bind", K.VariableReference, "The variable it shows and changes. Declare it with <let> directly inside <KScript.Window>.", true);
        private static readonly A OnChange = new A("on-change", K.Statements, "Runs after the person changes the value ($event.value), e.g. on-change=\"saved = false\".");

        protected override void Register(PackageBuilder p)
        {
            p.Element(new ElementInfo("Window", "Opens a desktop window. Its direct <let>, <fn> and <import> children run once and hold its state; the rest is the layout, drawn again after every on-* handler. The script continues when the window is closed.", ContentKind.Statements,
                new A("title", K.Template, "The title bar text."),
                new A("width", K.Expression, "Starting width in pixels (default 800)."),
                new A("height", K.Expression, "Starting height in pixels (default 600)."),
                new A("theme", K.Enum, "dark, light or system (the default).", false, "dark", "light", "system"),
                new A("background", K.Template, "The window's colour."),
                new A("padding", K.Expression, "Space around the layout, in pixels (default 16)."),
                new A("gap", K.Expression, "Space between the window's top-level items (default 12)."),
                new A("resizable", K.Bool, "no fixes the size."))
            {
                Context = ElementContext.Script,
                Example = "<KScript.Window title=\"Counter\" width=\"300\" height=\"200\">\n  <let name=\"count\" value=\"0\" />\n  <Text size=\"32\">{$count}</Text>\n  <Button on-click=\"count += 1\">Add one</Button>\n</KScript.Window>",
            }, UIElements.Window);

            Layout(p, "Column", "Stacks its items top to bottom.", Gap, Align, new A("scroll", K.Bool, "yes adds a scroll bar when the items don't fit."));
            Layout(p, "Row", "Puts its items side by side.", Gap, Align, new A("wrap", K.Bool, "yes moves items to a new line when they don't fit."));
            Layout(p, "Grid", "Lays its items out in equal columns, filling each row in turn: a board, a keypad, a gallery.", Gap,
                new A("columns", K.Expression, "How many columns.", true));

            p.Element(new ElementInfo("Text", "Shows text. `{ }` inserts values.", ContentKind.Template, With(Box,
                new A("size", K.Expression, "Font size in points (default 14)."),
                new A("weight", K.Enum, "normal, bold or light.", false, "normal", "bold", "light"),
                new A("color", K.Template, "Text colour: a name or #hex."),
                new A("wrap", K.Bool, "yes wraps long text onto more lines."),
                new A("text-align", K.Enum, "left, center or right.", false, "left", "center", "right")))
            {
                Context = ElementContext.Script,
                Example = "<Text size=\"24\" weight=\"bold\">Hello, {$name}!</Text>",
            }, run => UIElements.Node(run, "Text", text: true));

            p.Element(new ElementInfo("Button", "A button. Its text is the label; on-click runs when it is pressed.", ContentKind.Template, With(Box,
                new A("on-click", K.Statements, "Runs when the button is pressed, e.g. on-click=\"count += 1\"."),
                new A("variant", K.Enum, "primary makes it stand out.", false, "normal", "primary"),
                Disabled))
            {
                Context = ElementContext.Script,
                Example = "<Button variant=\"primary\" on-click=\"count += 1\">Add one</Button>",
            }, run => UIElements.Node(run, "Button", text: true));

            p.Element(new ElementInfo("TextBox", "A box to type in, tied to a variable.", ContentKind.Empty, With(Box,
                Bind,
                new A("placeholder", K.Template, "Grey text shown while it is empty."),
                new A("on-enter", K.Statements, "Runs when Enter is pressed."),
                OnChange,
                new A("multiline", K.Bool, "yes allows several lines."),
                Disabled))
            {
                Context = ElementContext.Script,
                Example = "<TextBox bind=\"name\" placeholder=\"Your name\" on-enter=\"greeting = 'Hi ' + $name\" />",
            }, run => UIElements.Node(run, "TextBox", text: false));

            p.Element(new ElementInfo("CheckBox", "A tick box tied to a yes/no variable. Its text is the label.", ContentKind.Template, With(Box, Bind, OnChange, Disabled))
            {
                Context = ElementContext.Script,
                Example = "<CheckBox bind=\"wrap\">Wrap around the edges</CheckBox>",
            }, run => UIElements.Node(run, "CheckBox", text: true));

            p.Element(new ElementInfo("Select", "A drop-down list tied to a variable.", ContentKind.Empty, With(Box, Bind,
                new A("options", K.Expression, "The choices: a list of values, a list of { label, value } maps, or a map from label to value.", true),
                OnChange, Disabled))
            {
                Context = ElementContext.Script,
                Example = "<Select bind=\"speed\" options=\"{ Slow: 400, Normal: 150, Fast: 40 }\" />",
            }, run => UIElements.Node(run, "Select", text: false));

            p.Element(new ElementInfo("Slider", "A slider tied to a number variable.", ContentKind.Empty, With(Box, Bind,
                new A("min", K.Expression, "The smallest value (default 0)."),
                new A("max", K.Expression, "The largest value (default 100)."),
                new A("step", K.Expression, "Snap to multiples of this."),
                OnChange, Disabled))
            {
                Context = ElementContext.Script,
                Example = "<Slider bind=\"volume\" min=\"0\" max=\"10\" step=\"1\" />",
            }, run => UIElements.Node(run, "Slider", text: false));

            p.Element(new ElementInfo("Box", "A rectangle: a coloured cell, a card or a panel. It can hold other items, and on-click makes it clickable.", ContentKind.Statements, With(Box,
                new A("on-click", K.Statements, "Runs when it is clicked."),
                new A("border", K.Template, "A border colour.")))
            {
                Context = ElementContext.Script,
                Example = "<Box width=\"20\" height=\"20\" background=\"{ $on ? 'lime' : '#222' }\" on-click=\"on = not $on\" />",
            }, run => UIElements.Node(run, "Box", text: false));

            p.Element(new ElementInfo("Spacer", "Empty space.", ContentKind.Empty,
                new A("size", K.Expression, "How much space, in pixels (default 12)."))
            {
                Context = ElementContext.Script,
            }, run => UIElements.Node(run, "Spacer", text: false));

            p.Element(new ElementInfo("Timer", "Runs on-tick again and again while it is in the layout (and `when` is true). The next tick is counted from when the last one finished.", ContentKind.Empty,
                new A("every", K.Expression, "Milliseconds between ticks.", true),
                new A("when", K.Expression, "Only tick while this is true, e.g. when=\"$playing\"."),
                new A("on-tick", K.Statements, "Runs on every tick.", true))
            {
                Context = ElementContext.Script,
                Example = "<Timer every=\"100\" when=\"$playing\" on-tick=\"frame += 1\" />",
            }, run => UIElements.Node(run, "Timer", text: false));

            p.Command("close_window", "", "Closes the window, so the script carries on after </KScript.Window>.", KType.Null, c =>
            {
                UIElements.RequestClose();
                return null;
            });
        }

        private static void Layout(PackageBuilder p, string name, string description, params A[] own)
        {
            p.Element(new ElementInfo(name, description, ContentKind.Statements, With(Box, own))
            {
                Context = ElementContext.Script,
                Example = $"<{name}{(name == "Grid" ? " columns=\"2\"" : "")} gap=\"8\">\n  <Text>One</Text>\n  <Text>Two</Text>\n</{name}>",
            }, run => UIElements.Node(run, name, text: false));
        }
    }
}
