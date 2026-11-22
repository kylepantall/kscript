using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>color</c>: a colour written as #rgb, #rrggbb, #rrggbbaa, rgb(...) or a CSS name, kept as lowercase
    /// #rrggbb (#rrggbbaa with alpha), so orange == '#ffa500'. Colours take no operators: adding two could mean
    /// adding the light or mixing the paint, so ->mix(), ->lighten() and ->darken() say which. They have no order
    /// either (by lightness, hue or hex?): sort by a field, such as ->sort_by(fn($c) => $c.luminance).
    /// </summary>
    public sealed class ColorType : NativeType
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["black"] = "#000000", ["white"] = "#ffffff", ["red"] = "#ff0000", ["green"] = "#008000", ["blue"] = "#0000ff",
            ["yellow"] = "#ffff00", ["orange"] = "#ffa500", ["purple"] = "#800080", ["pink"] = "#ffc0cb", ["brown"] = "#a52a2a",
            ["gray"] = "#808080", ["grey"] = "#808080", ["silver"] = "#c0c0c0", ["gold"] = "#ffd700", ["navy"] = "#000080",
            ["teal"] = "#008080", ["lime"] = "#00ff00", ["aqua"] = "#00ffff", ["cyan"] = "#00ffff", ["magenta"] = "#ff00ff",
            ["fuchsia"] = "#ff00ff", ["maroon"] = "#800000", ["olive"] = "#808000", ["indigo"] = "#4b0082", ["violet"] = "#ee82ee",
            ["coral"] = "#ff7f50", ["salmon"] = "#fa8072", ["crimson"] = "#dc143c", ["tomato"] = "#ff6347", ["turquoise"] = "#40e0d0",
            ["beige"] = "#f5f5dc", ["ivory"] = "#fffff0", ["khaki"] = "#f0e68c", ["lavender"] = "#e6e6fa", ["plum"] = "#dda0dd",
            ["orchid"] = "#da70d6", ["tan"] = "#d2b48c", ["chocolate"] = "#d2691e", ["skyblue"] = "#87ceeb", ["steelblue"] = "#4682b4",
            ["slategray"] = "#708090", ["darkgray"] = "#a9a9a9", ["lightgray"] = "#d3d3d3", ["transparent"] = "#00000000",
        };

        public override string Name => "color";

        public override string Description => "A colour: #rgb, #rrggbb, #rrggbbaa, rgb(...) or a CSS name. Kept as lowercase #rrggbb (#rrggbbaa with alpha).";

        public override string Example => "#ff8800";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Append("upper").ToArray();

        public override IReadOnlyList<string> Completions => new[] { "black", "white", "red", "green", "blue", "orange", "gray", "transparent" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.None;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number;
            const string Let = "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n";
            m.Method("rgb", "", "Its red, green, blue (0-255) and alpha (0-1): { r, g, b, a }.", KType.Map, (t, c) =>
                new Dictionary<string, object> { ["r"] = Ch(t, "r"), ["g"] = Ch(t, "g"), ["b"] = Ch(t, "b"), ["a"] = Ch(t, "a") },
                Let + "<echo>{ $x->rgb() }</echo>");
            m.Method("hex", "", "Its #rrggbb form, without the alpha.", S, (t, c) => ((NativeText)t).Text.Substring(0, 7),
                Let + "<echo>{ $x->hex() }</echo>");
            m.Method("css", "", "Its CSS form: #rrggbb, or rgb(r g b / a) with transparency.", S, (t, c) =>
                Ch(t, "a") < 1 ? FormattableString.Invariant($"rgb({Ch(t, "r")} {Ch(t, "g")} {Ch(t, "b")} / {Math.Round(Ch(t, "a"), 3)})") : ((NativeText)t).Text,
                Let + "<echo>{ $x->css() }</echo>");
            m.Method("mix", "other: color, amount?: number", "The colour part way to the other: amount 0.5 (the default) is halfway.", "color", (t, c) =>
            {
                NativeText o = Other(c.Arg(0));
                double w = c.Arg(1, "amount") is double a ? Math.Clamp(a, 0, 1) : 0.5;
                double Mix(string ch) => Ch(t, ch) + (Ch(o, ch) - Ch(t, ch)) * w;
                return Of(Mix("r"), Mix("g"), Mix("b"), Mix("a"));
            }, Let + "<echo>{ $x->mix('blue') }</echo>");
            m.Method("lighten", "amount?: number", "Mixed with white: amount 0.2 (the default) is a fifth of the way.", "color", (t, c) =>
            {
                double w = c.Arg(0, "amount") is double a ? Math.Clamp(a, 0, 1) : 0.2;
                return Of(Ch(t, "r") + (255 - Ch(t, "r")) * w, Ch(t, "g") + (255 - Ch(t, "g")) * w, Ch(t, "b") + (255 - Ch(t, "b")) * w, Ch(t, "a"));
            }, Let + "<echo>{ $x->lighten(0.5) }</echo>");
            m.Method("darken", "amount?: number", "Mixed with black: amount 0.2 (the default) is a fifth of the way.", "color", (t, c) =>
            {
                double w = 1 - (c.Arg(0, "amount") is double a ? Math.Clamp(a, 0, 1) : 0.2);
                return Of(Ch(t, "r") * w, Ch(t, "g") * w, Ch(t, "b") * w, Ch(t, "a"));
            }, Let + "<echo>{ $x->darken() }</echo>");
            m.Field("luminance", N, "How light it looks, from 0 (black) to 1 (white), as WCAG measures it.", (t, _) => Luminance((NativeText)t),
                Let + "<echo>{ $x.luminance }</echo>");
            m.Method("contrast_ratio", "other: color", "The WCAG contrast ratio with the other colour, from 1 to 21. Text needs 4.5 or more.", N, (t, c) =>
            {
                double a = Luminance((NativeText)t), b = Luminance(Other(c.Arg(0)));
                return Math.Round((Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05), 2);
            }, Let + "<echo>{ $x->contrast_ratio('white') }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = s.Trim().ToLowerInvariant();
            if (t.Length == 0) return (null, "it is empty");
            if (Names.TryGetValue(t, out string named)) t = named;
            double r, g, b, a = 1;
            if (t.StartsWith('#'))
            {
                string h = t.Substring(1);
                if (!h.All(Uri.IsHexDigit) || h.Length is not (3 or 4 or 6 or 8)) return (null, "a hex colour has 3, 4, 6 or 8 hex digits after the #, as in #ff8800");
                if (h.Length <= 4) h = string.Concat(h.Select(c => new string(c, 2)));
                r = Convert.ToInt32(h.Substring(0, 2), 16);
                g = Convert.ToInt32(h.Substring(2, 2), 16);
                b = Convert.ToInt32(h.Substring(4, 2), 16);
                if (h.Length == 8) a = Math.Round(Convert.ToInt32(h.Substring(6, 2), 16) / 255.0, 3);
            }
            else if ((t.StartsWith("rgb(") || t.StartsWith("rgba(")) && t.EndsWith(')'))
            {
                string[] parts = t.Substring(t.IndexOf('(') + 1).TrimEnd(')').Replace("/", ",").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length is not (3 or 4)) return (null, "rgb() takes three numbers and an optional alpha, as in rgb(255, 136, 0)");
                var values = new double[4];
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i];
                    bool percent = p.EndsWith('%');
                    if (!double.TryParse(p.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return (null, $"'{p}' isn't a number");
                    values[i] = i < 3 ? (percent ? v * 2.55 : v) : (percent ? v / 100 : v);
                    if (i < 3 && (values[i] < 0 || values[i] > 255)) return (null, $"'{p}' must be 0 to 255");
                    if (i == 3 && (values[i] < 0 || values[i] > 1)) return (null, $"the alpha '{p}' must be 0 to 1");
                }
                (r, g, b) = (Math.Round(values[0]), Math.Round(values[1]), Math.Round(values[2]));
                if (parts.Length == 4) a = values[3];
            }
            else
            {
                return (null, "use #rrggbb, rgb(r, g, b) or a CSS colour name such as orange");
            }
            return (Of(r, g, b, a), null);
        }

        /// <summary>A colour from its red, green and blue (0-255) and alpha (0-1).</summary>
        public static NativeText Of(double r, double g, double b, double a)
        {
            string hex = "#" + string.Concat(new[] { r, g, b }.Select(v => ((int)Math.Clamp(Math.Round(v), 0, 255)).ToString("x2", CultureInfo.InvariantCulture)))
                + (a < 1 ? ((int)Math.Round(Math.Clamp(a, 0, 1) * 255)).ToString("x2", CultureInfo.InvariantCulture) : string.Empty);
            return new NativeText("color", hex, new Dictionary<string, object> { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = a });
        }

        private static double Ch(object color, string channel) => (double)((NativeText)color).Part(channel);

        /// <summary>A method's colour argument: a colour, or text that reads as one. An error says why it doesn't.</summary>
        private NativeText Other(object arg)
        {
            if (Coerce(arg) is NativeText color) return color;
            string text = Values.ToStr(arg);
            throw new KScriptException($"'{text}' isn't {Info.WithArticle}: {Parse(text).Reason}.", "Value");
        }

        private static double Luminance(NativeText color)
        {
            double Channel(string n)
            {
                double v = (double)color.Part(n) / 255;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return Math.Round(0.2126 * Channel("r") + 0.7152 * Channel("g") + 0.0722 * Channel("b"), 4);
        }
    }
}
