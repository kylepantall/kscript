using System.Globalization;

using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Style
{
    /// <summary>
    /// KScript.Style: stylesheets written in KScript, and helpers for colours, sizes and screen widths.
    /// Stylesheets (.ks.css, .ks.scss, .ks.sass) import it on their own.
    /// </summary>
    public sealed class StylePackage : KScriptPackage
    {
        internal const string VarsKey = "KScript.Style.Vars";

        /// <summary>The screen widths @up('md') and friends use when the stylesheet doesn't set $breakpoints.</summary>
        public static readonly Dictionary<string, object> DefaultBreakpoints = new Dictionary<string, object>
        {
            ["sm"] = 640.0,
            ["md"] = 768.0,
            ["lg"] = 1024.0,
            ["xl"] = 1280.0,
            ["xxl"] = 1536.0,
        };

        public override string Name => "KScript.Style";

        public override string Description => "Stylesheets written in KScript (.ks.css, .ks.scss, .ks.sass), with colour, size and responsive helpers.";

        protected override void Register(PackageBuilder p)
        {
            p.Element(new ElementInfo("css-text", "CSS text in a stylesheet, with {$name} and { @fn() } filled in. kscript writes these for you around the KScript elements in a .ks.css file.", ContentKind.Template), run =>
            {
                run.Host.Write(Stylesheets.Interpolate(run.Content, run.Interpreter, run.Scope, run.Module));
                return null;
            }, ownsContent: true);

            p.Variable("vars", KType.Map, "Values given to the stylesheet: <stylesheet vars=\"{ ... }\">, @stylesheet(path, vars) or kscript css --var name=value.", (i, m) =>
                i.Host.Items.TryGetValue(VarsKey, out object v) ? v : new Dictionary<string, object>());

            p.Element(new ElementInfo("stylesheet", "Compiles a stylesheet written in KScript (.ks.css, .ks.scss or .ks.sass) and writes the CSS (or Sass) to a file.", ContentKind.Empty,
                new A("src", K.Template, "The stylesheet, such as theme.ks.css.", true),
                new A("to", K.Template, "The file to write. Default: next to src without .ks (theme.css)."),
                new A("vars", K.Expression, "A map of values the stylesheet reads as $vars."))
            {
                Context = ElementContext.Script,
                Example = "<stylesheet src=\"theme.ks.css\" to=\"public/theme.css\" vars=\"{ brand: '#3355ff' }\" />",
            }, run =>
            {
                string src = run.Host.ResolvePath(run.Template("src"), run.Module.Directory);
                string to = run.Has("to") ? run.Host.ResolvePath(run.Template("to"), run.Module.Directory) : Stylesheets.OutputPath(src);
                run.Host.CheckWrite("<stylesheet>");
                string css = Stylesheets.Compile(src, Vars(run.Has("vars") ? run.Eval("vars") : null, "<stylesheet vars>"), Sandbox(run.Host));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllText(to, css);
                return null;
            });

            p.Command("stylesheet", "path, vars?", "Compiles a stylesheet written in KScript and returns the CSS (or Sass).", KType.String, c =>
            {
                string src = c.Host.ResolvePath(c.Str(0), c.Module?.Directory ?? c.Host.WorkingDirectory);
                return Stylesheets.Compile(src, Vars(c.Arg(1, "vars"), "@stylesheet() vars"), Sandbox(c.Host));
            }, "<let name=\"css\" value=\"@stylesheet('theme.ks.css', { dark: true })\" />");

            // Screen widths.
            p.Command("up", "width", "A media query for screens at least this wide: @up(768) or @up('md') is (min-width: 768px).", KType.String,
                c => $"(min-width: {Px(Width(c, 0))})", "@media {@up('md')} { .menu { display: flex; } }");
            p.Command("down", "width", "A media query for screens narrower than this: @down('md') is (max-width: 767.98px).", KType.String,
                c => $"(max-width: {Px(Width(c, 0) - 0.02)})");
            p.Command("between", "from, to", "A media query for screens from one width up to (not including) another.", KType.String,
                c => $"(min-width: {Px(Width(c, 0))}) and (max-width: {Px(Width(c, 1) - 0.02)})");

            // Sizes.
            p.Command("rem", "px, base?", "Pixels as rem: @rem(24) is 1.5rem (base 16).", KType.String, c => Num(c.Num(0) / Base(c)) + "rem");
            p.Command("fluid", "min, max, from?, to?",
                "A size that grows with the screen: min (px) on screens up to from (default 640px), max on screens from to (default 1280px), and in between it scales. Gives clamp(...) in rem.",
                KType.String, Fluid, "h1 { font-size: {@fluid(28, 48)}; }");

            // Colours.
            p.Command("lighten", "colour, amount", "A lighter colour: amount is 0 to 1 (or a percentage such as '10%') of lightness added.", KType.String,
                c => Adjust(c, +1));
            p.Command("darken", "colour, amount", "A darker colour: amount is 0 to 1 (or '10%') of lightness taken away.", KType.String,
                c => Adjust(c, -1));
            p.Command("mix", "a, b, weight?", "A mix of two colours. weight is how much of a (0 to 1, default 0.5).", KType.String, c =>
            {
                Rgba a = Colors.Parse(c.Arg(0), "@mix()"), b = Colors.Parse(c.Arg(1), "@mix()");
                double w = c.Has(2) ? Amount(c.Arg(2), "@mix() weight") : 0.5;
                return Colors.ToCss(new Rgba(a.R * w + b.R * (1 - w), a.G * w + b.G * (1 - w), a.B * w + b.B * (1 - w), a.A * w + b.A * (1 - w)));
            });
            p.Command("alpha", "colour, opacity", "The colour see-through: opacity from 0 to 1 (or '50%').", KType.String, c =>
            {
                Rgba a = Colors.Parse(c.Arg(0), "@alpha()");
                return Colors.ToCss(a with { A = Amount(c.Arg(1), "@alpha() opacity") });
            });
            p.Command("contrast", "colour, dark?, light?",
                "The text colour that reads best on a background: dark (default #000000) on light colours, light (default #ffffff) on dark ones.",
                KType.String, c =>
                {
                    Rgba bg = Colors.Parse(c.Arg(0), "@contrast()");
                    string dark = c.Str(1, "#000000"), light = c.Str(2, "#ffffff");
                    double Ratio(string fg)
                    {
                        double l1 = bg.Luminance(), l2 = Colors.Parse(fg, "@contrast()").Luminance();
                        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
                    }
                    return Ratio(dark) >= Ratio(light) ? dark : light;
                });

            // Custom properties.
            p.Command("css_var", "name, fallback?", "var(--name), with a fallback when given.", KType.String,
                c => c.Has(1) ? $"var(--{c.Str(0)}, {Stylesheets.Format(c.Arg(1))})" : $"var(--{c.Str(0)})");
            p.Command("css_vars", "map, prefix?",
                "Custom properties from a map, one per line: { primary: '#36f', space: { sm: '4px' } } with prefix 'theme' gives --theme-primary: #36f; --theme-space-sm: 4px;",
                KType.String, c =>
                {
                    var lines = new List<string>();
                    Flatten(c.Map(0), c.Has(1) ? c.Str(1) : null, lines);
                    return string.Join("\n", lines);
                }, ":root {\n  { @css_vars($theme.colors, 'color') }\n}");
        }

        private static Action<KScriptHost> Sandbox(KScriptHost parent) => h =>
        {
            if (parent.FileRoot != null)
            {
                h.FileRoot = parent.FileRoot;
                h.FileRootName = parent.FileRootName;
            }
            h.CanWriteFiles = parent.CanWriteFiles;
            h.Config = parent.Config;
        };

        private static Dictionary<string, object> Vars(object value, string where) => value switch
        {
            null => null,
            Dictionary<string, object> map => map,
            _ => throw new KScriptException($"{where} should be a map, such as {{ brand: '#3355ff' }}, but got {Values.Describe(value)}.", "Argument"),
        };

        private static void Flatten(Dictionary<string, object> map, string prefix, List<string> lines)
        {
            foreach (var kv in map)
            {
                string name = prefix == null ? kv.Key : prefix + "-" + kv.Key;
                if (kv.Value is Dictionary<string, object> nested) Flatten(nested, name, lines);
                else lines.Add($"--{name}: {Stylesheets.Format(kv.Value)};");
            }
        }

        /// <summary>A width in px: a number, '48em', or a breakpoint name from $breakpoints (or the defaults).</summary>
        private static double Width(CallContext c, int i)
        {
            object value = c.Need(i);
            if (value is string name && !double.TryParse(name.Replace("px", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                if (name.EndsWith("em", StringComparison.Ordinal) && double.TryParse(name.TrimEnd('m', 'e', 'r'), NumberStyles.Float, CultureInfo.InvariantCulture, out double em)) return em * 16;
                Dictionary<string, object> points = c.Scope != null && c.Scope.TryGet("breakpoints", out object b) && b is Dictionary<string, object> mine ? mine : DefaultBreakpoints;
                if (!points.TryGetValue(name, out object px))
                    throw new KScriptException($"There is no breakpoint '{name}'. Breakpoints: {string.Join(", ", points.Keys)}. Set your own with <let name=\"breakpoints\" value=\"{{ md: 800 }}\" />.", "Argument");
                return Values.ToNumber(px, $"$breakpoints.{name}");
            }
            return Values.ToNumber(value is string s ? s.Replace("px", string.Empty) : value, $"@{c.Name}()");
        }

        private static double Base(CallContext c) => c.Has(1) ? c.Num(1) : 16;

        private static string Px(double px) => Num(px) + "px";

        private static string Num(double n) => Math.Round(n, 4).ToString("0.####", CultureInfo.InvariantCulture);

        private static object Fluid(CallContext c)
        {
            double min = c.Num(0), max = c.Num(1);
            double from = c.Has(2) ? Width(c, 2) : 640, to = c.Has(3) ? Width(c, 3) : 1280;
            if (to <= from) throw new KScriptException("@fluid() needs the second screen width to be larger than the first.", "Argument");
            double slope = (max - min) / (to - from);
            double intercept = min - slope * from;
            string lo = Num(Math.Min(min, max) / 16) + "rem", hi = Num(Math.Max(min, max) / 16) + "rem";
            string vw = slope < 0 ? $"- {Num(-slope * 100)}vw" : $"+ {Num(slope * 100)}vw";
            return $"clamp({lo}, {Num(intercept / 16)}rem {vw}, {hi})";
        }

        private static double Amount(object value, string where)
        {
            if (value is string s && s.Trim().EndsWith('%'))
                return Values.ToNumber(s.Trim().TrimEnd('%'), where) / 100;
            return Values.ToNumber(value, where);
        }

        private static string Adjust(CallContext c, int sign)
        {
            Rgba colour = Colors.Parse(c.Arg(0), $"@{c.Name}()");
            var (h, s, l) = colour.ToHsl();
            return Colors.ToCss(Rgba.FromHsl(h, s, l + sign * Amount(c.Arg(1), $"@{c.Name}() amount"), colour.A));
        }
    }
}
