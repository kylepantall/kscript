using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace KScript.Runtime
{
    /// <summary>YAML read into KScript values (maps, lists, text, numbers, true/false and null), and written back.</summary>
    public static class Yaml
    {
        /// <summary>
        /// Parses YAML. Plain scalars become numbers, true/false or null where they look like one; quoted scalars stay
        /// text. An empty document is null. Several documents (separated by ---) give a list.
        /// </summary>
        public static object Parse(string text, string source = null)
        {
            var stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(text ?? string.Empty));
            }
            catch (YamlException ex)
            {
                string where = source == null ? string.Empty : $"{source}, ";
                throw new KScriptException($"Not valid YAML ({where}line {ex.Start.Line}): {FirstLine(ex.InnerException?.Message ?? ex.Message)}", "Yaml");
            }
            if (stream.Documents.Count == 0) return null;
            if (stream.Documents.Count == 1) return FromNode(stream.Documents[0].RootNode, 0);
            return stream.Documents.Select(d => FromNode(d.RootNode, 0)).ToList();
        }

        private static string FirstLine(string message) => message.Split('\n')[0].Trim();

        private static object FromNode(YamlNode node, int depth)
        {
            if (depth > 200) throw new KScriptException("The YAML is nested too deeply (or refers to itself).", "Yaml");
            switch (node)
            {
                case YamlMappingNode map:
                    var result = new Dictionary<string, object>();
                    foreach (var pair in map.Children)
                    {
                        string key = pair.Key is YamlScalarNode k ? k.Value ?? string.Empty : throw new KScriptException("YAML keys must be text, numbers or true/false.", "Yaml");
                        if (key == "<<")
                        {
                            // Merge keys: <<: *defaults copies the fields that this map doesn't set itself.
                            foreach (YamlNode merged in pair.Value is YamlSequenceNode many ? many.Children : new List<YamlNode> { pair.Value })
                            {
                                if (FromNode(merged, depth + 1) is Dictionary<string, object> m)
                                    foreach (var kv in m) result.TryAdd(kv.Key, kv.Value);
                            }
                            continue;
                        }
                        result[key] = FromNode(pair.Value, depth + 1);
                    }
                    return result;
                case YamlSequenceNode list:
                    return list.Children.Select(c => FromNode(c, depth + 1)).ToList();
                default:
                    // Aliases are resolved when the stream loads, so every other node is a scalar.
                    var scalar = (YamlScalarNode)node;
                    return scalar.Style == ScalarStyle.Plain ? PlainScalar(scalar.Value) : scalar.Value;
            }
        }

        private static readonly Regex Float = new Regex(@"^[-+]?(\.[0-9]+|[0-9][0-9_]*(\.[0-9]*)?)([eE][-+]?[0-9]+)?$");

        /// <summary>A plain (unquoted) YAML scalar: null, true/false, a number, or text.</summary>
        internal static object PlainScalar(string value)
        {
            switch (value)
            {
                case null: case "": case "~": case "null": case "Null": case "NULL":
                    return null;
                case "true": case "True": case "TRUE":
                    return true;
                case "false": case "False": case "FALSE":
                    return false;
                case ".inf": case "+.inf": case ".Inf":
                    return double.PositiveInfinity;
                case "-.inf": case "-.Inf":
                    return double.NegativeInfinity;
                case ".nan": case ".NaN":
                    return double.NaN;
            }
            if (value.StartsWith("0x", StringComparison.Ordinal) && long.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex)) return (double)hex;
            if (Float.IsMatch(value) && double.TryParse(value.Replace("_", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            return value;
        }

        /// <summary>A value as YAML: maps and lists in block style, text quoted when it would otherwise read as something else.</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, int indent, int depth)
        {
            if (depth > 200) throw new KScriptException("The value is nested too deeply (or contains itself) to turn into YAML.");
            value = Values.Unwrap(value);
            switch (value)
            {
                case Dictionary<string, object> map when map.Count > 0:
                    foreach (var kv in map)
                    {
                        sb.Append(' ', indent).Append(Key(kv.Key)).Append(':');
                        WriteNested(sb, kv.Value, indent + 2, depth);
                    }
                    break;
                case List<object> list when list.Count > 0:
                    foreach (object item in list)
                    {
                        sb.Append(' ', indent).Append('-');
                        if (Values.Unwrap(item) is Dictionary<string, object> m && m.Count > 0)
                        {
                            // The first field goes on the dash's line: "- name: Ann".
                            var inner = new StringBuilder();
                            WriteValue(inner, m, indent + 2, depth + 1);
                            sb.Append(' ').Append(inner.ToString().Substring(indent + 2));
                        }
                        else
                        {
                            WriteNested(sb, item, indent + 2, depth);
                        }
                    }
                    break;
                default:
                    sb.Append(Scalar(value)).Append('\n');
                    break;
            }
        }

        private static void WriteNested(StringBuilder sb, object value, int indent, int depth)
        {
            value = Values.Unwrap(value);
            if (value is Dictionary<string, object> { Count: > 0 } || value is List<object> { Count: > 0 })
            {
                sb.Append('\n');
                WriteValue(sb, value, indent, depth + 1);
            }
            else
            {
                sb.Append(' ').Append(Scalar(value)).Append('\n');
            }
        }

        private static string Key(string key) => key.Length > 0 && Regex.IsMatch(key, @"^[A-Za-z_][A-Za-z0-9_.\-]*$") && PlainScalar(key) is string ? key : Quote(key);

        private static string Scalar(object value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            double d when double.IsPositiveInfinity(d) => ".inf",
            double d when double.IsNegativeInfinity(d) => "-.inf",
            double d when double.IsNaN(d) => ".nan",
            double or int or long => Values.ToStr(value),
            Dictionary<string, object> => "{}",
            List<object> => "[]",
            string s => NeedsQuotes(s) ? Quote(s) : s,
            _ => Scalar(Values.ToStr(value)),
        };

        private static bool NeedsQuotes(string s) =>
            s.Length == 0 || PlainScalar(s) is not string || s != s.Trim() || s.IndexOfAny(new[] { ':', '#', '\n', '"', '\'', '\t' }) >= 0
            || "-?[]{},&*!|>%@`".IndexOf(s[0]) >= 0;

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>
    /// .env files: <c>NAME=value</c> lines, with <c>#</c> comments, an optional <c>export</c> in front, 'single quotes'
    /// (taken as written), "double quotes" (\n escapes, and may span lines) and <c>${NAME}</c> or
    /// <c>${NAME:-default}</c> to use another variable.
    /// </summary>
    public static class DotEnv
    {
        private static readonly Regex Line = new Regex(@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_.\-]*)\s*=\s*(.*)$");

        /// <summary>
        /// Reads .env text into name/value pairs, in order. <paramref name="lookup"/> finds variables for <c>${NAME}</c>
        /// that the file doesn't set itself (usually the environment).
        /// </summary>
        public static List<KeyValuePair<string, string>> Parse(string text, Func<string, string> lookup = null, string source = null)
        {
            var result = new List<KeyValuePair<string, string>>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            string Find(string name) => seen.TryGetValue(name, out string v) ? v : lookup?.Invoke(name);

            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                Match m = Line.Match(line);
                if (!m.Success)
                    throw new KScriptException($"{source ?? ".env"} line {i + 1}: expected NAME=value, but got: {trimmed}", "Format");

                string name = m.Groups[1].Value;
                string raw = m.Groups[2].Value;
                string value;
                if (raw.StartsWith('\''))
                {
                    int end = raw.IndexOf('\'', 1);
                    if (end < 0) throw new KScriptException($"{source ?? ".env"} line {i + 1}: the ' quote around {name}'s value isn't closed.", "Format");
                    value = raw.Substring(1, end - 1);
                }
                else if (raw.StartsWith('"'))
                {
                    // Double-quoted values may run over several lines.
                    var sb = new StringBuilder(raw.Substring(1));
                    int start = i;
                    int close;
                    while ((close = ClosingQuote(sb.ToString())) < 0)
                    {
                        if (++i >= lines.Length) throw new KScriptException($"{source ?? ".env"} line {start + 1}: the \" quote around {name}'s value isn't closed.", "Format");
                        sb.Append('\n').Append(lines[i]);
                    }
                    value = Expand(Unescape(sb.ToString().Substring(0, close)), Find);
                }
                else
                {
                    int comment = raw.IndexOf(" #", StringComparison.Ordinal);
                    value = Expand((comment >= 0 ? raw.Substring(0, comment) : raw).Trim(), Find);
                }
                seen[name] = value;
                result.Add(new KeyValuePair<string, string>(name, value));
            }
            return result;
        }

        private static int ClosingQuote(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\') i++;
                else if (s[i] == '"') return i;
            }
            return -1;
        }

        private static string Unescape(string s) => Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch
        {
            "n" => "\n",
            "r" => "\r",
            "t" => "\t",
            var other => other,
        });

        private static readonly Regex Reference = new Regex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::?-([^}]*))?\}");

        /// <summary>Replaces <c>${NAME}</c> and <c>${NAME:-default}</c> with the variable's value.</summary>
        public static string Expand(string text, Func<string, string> lookup) =>
            text == null || !text.Contains("${", StringComparison.Ordinal) ? text : Reference.Replace(text, m =>
            {
                string value = lookup?.Invoke(m.Groups[1].Value);
                return string.IsNullOrEmpty(value) && m.Groups[2].Success ? m.Groups[2].Value : value ?? string.Empty;
            });
    }
}
