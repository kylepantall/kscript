using System.Globalization;
using System.Text.RegularExpressions;

using KScript.Runtime;

namespace KScript.Style
{
    /// <summary>A colour with red, green and blue from 0 to 255 and alpha from 0 to 1.</summary>
    public readonly record struct Rgba(double R, double G, double B, double A)
    {
        /// <summary>#rrggbb, or #rrggbbaa when it is see-through.</summary>
        public string ToHex()
        {
            string hex = $"#{Byte(R):x2}{Byte(G):x2}{Byte(B):x2}";
            return A >= 1 ? hex : hex + $"{Byte(A * 255):x2}";
        }

        private static int Byte(double v) => (int)Math.Round(Math.Clamp(v, 0, 255));

        public (double H, double S, double L) ToHsl()
        {
            double r = R / 255, g = G / 255, b = B / 255;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2;
            if (max == min) return (0, 0, l);
            double d = max - min;
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s, l);
        }

        public static Rgba FromHsl(double h, double s, double l, double a)
        {
            h = ((h % 360) + 360) % 360 / 360;
            s = Math.Clamp(s, 0, 1);
            l = Math.Clamp(l, 0, 1);
            if (s == 0) return new Rgba(l * 255, l * 255, l * 255, a);
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            return new Rgba(Hue(p, q, h + 1.0 / 3) * 255, Hue(p, q, h) * 255, Hue(p, q, h - 1.0 / 3) * 255, a);
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        /// <summary>The WCAG relative luminance, from 0 (black) to 1 (white).</summary>
        public double Luminance()
        {
            static double Channel(double c)
            {
                c /= 255;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    /// <summary>Reads CSS colours: #rgb, #rgba, #rrggbb, #rrggbbaa, rgb()/rgba(), hsl()/hsla() and a few names.</summary>
    public static class Colors
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = "#000000", ["white"] = "#ffffff", ["red"] = "#ff0000", ["green"] = "#008000", ["blue"] = "#0000ff",
            ["yellow"] = "#ffff00", ["orange"] = "#ffa500", ["purple"] = "#800080", ["gray"] = "#808080", ["grey"] = "#808080",
            ["transparent"] = "#00000000",
        };

        private static readonly Regex Function = new Regex(@"^(rgba?|hsla?)\(\s*([^)]*)\)$", RegexOptions.IgnoreCase);

        public static Rgba Parse(object value, string where)
        {
            string text = Values.ToStr(value)?.Trim() ?? string.Empty;
            if (Names.TryGetValue(text, out string named)) text = named;
            if (text.StartsWith('#'))
            {
                string hex = text.Substring(1);
                if (hex.Length == 3 || hex.Length == 4) hex = string.Concat(hex.Select(c => new string(c, 2)));
                if ((hex.Length == 6 || hex.Length == 8) && hex.All(Uri.IsHexDigit))
                {
                    int Part(int i) => int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return new Rgba(Part(0), Part(2), Part(4), hex.Length == 8 ? Part(6) / 255.0 : 1);
                }
            }
            Match m = Function.Match(text);
            if (m.Success && TryFunction(m, out Rgba parsed)) return parsed;
            throw new KScriptException($"{where}: '{text}' isn't a colour. Use #rrggbb, rgb(...), hsl(...) or a name such as white.", "Argument");
        }

        private static bool TryFunction(Match m, out Rgba colour)
        {
            colour = default;
            try
            {
                string[] parts = m.Groups[2].Value.Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 || parts.Length == 4)
                {
                    double alpha = parts.Length == 4 ? Number(parts[3], 1) : 1;
                    colour = m.Groups[1].Value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
                        ? new Rgba(Number(parts[0], 255), Number(parts[1], 255), Number(parts[2], 255), alpha)
                        : Rgba.FromHsl(Number(parts[0].Replace("deg", string.Empty), 1), Number(parts[1], 1), Number(parts[2], 1), alpha);
                    return true;
                }
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>A number, or a percentage of <paramref name="scale"/> (50% of 255 is 127.5).</summary>
        private static double Number(string text, double scale)
        {
            bool percent = text.EndsWith('%');
            double n = double.Parse(percent ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture);
            return percent ? n / 100 * scale : n;
        }

        /// <summary>A colour as CSS: #rrggbb, or rgba(...) when it is see-through.</summary>
        public static string ToCss(Rgba c) => c.A >= 1
            ? c.ToHex()
            : $"rgba({Math.Round(c.R)}, {Math.Round(c.G)}, {Math.Round(c.B)}, {Math.Round(c.A, 3).ToString(CultureInfo.InvariantCulture)})";
    }
}
