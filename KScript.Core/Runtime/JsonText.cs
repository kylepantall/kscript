using System.Text;

namespace KScript.Runtime
{
    /// <summary>JSON for people: which values print as JSON, and colours for a terminal.</summary>
    public static class JsonText
    {
        // ANSI styles, close to jq's: keys bold blue, strings green, numbers cyan, true/false yellow, null grey.
        private const string KeyStyle = "1;34";
        private const string StringStyle = "32";
        private const string NumberStyle = "36";
        private const string BoolStyle = "33";
        private const string NullStyle = "90";

        /// <summary>
        /// True for values that read best as indented JSON: maps, and lists that hold maps or lists. A list of plain
        /// values still prints as <c>a, b, c</c>.
        /// </summary>
        public static bool IsStructured(object value) => value switch
        {
            Dictionary<string, object> => true,
            List<object> list => list.Any(item => item is Dictionary<string, object> || item is List<object>),
            _ => false,
        };

        /// <summary>True if the text is a JSON object or array.</summary>
        public static bool IsValid(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || (text[0] != '{' && text[0] != '[')) return false;
            try
            {
                using System.Text.Json.JsonDocument _ = System.Text.Json.JsonDocument.Parse(text);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Indented JSON that stays compact where it can: one key per line, but short lists of plain values on one
        /// line (<c>"tags": ["a", "b"]</c>) and empty lists and maps as <c>[]</c> and <c>{}</c>.
        /// </summary>
        public static string Pretty(object value)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, value, string.Empty, 0);
            return sb.ToString();
        }

        private const int InlineWidth = 72;

        private static void Write(StringBuilder sb, object value, string indent, int depth)
        {
            if (depth > 200) throw new KScriptException("The value is nested too deeply (or contains itself) to turn into JSON.");
            switch (value)
            {
                case Dictionary<string, object> map when map.Count == 0:
                    sb.Append("{}");
                    break;
                case Dictionary<string, object> map:
                {
                    string inner = indent + "  ";
                    sb.Append("{\n");
                    int n = 0;
                    foreach (KeyValuePair<string, object> kv in map)
                    {
                        sb.Append(inner).Append(Values.ToJson(kv.Key)).Append(": ");
                        Write(sb, kv.Value, inner, depth + 1);
                        sb.Append(++n < map.Count ? ",\n" : "\n");
                    }
                    sb.Append(indent).Append('}');
                    break;
                }
                case List<object> list when list.Count == 0:
                    sb.Append("[]");
                    break;
                case List<object> list when !IsStructured(list):
                {
                    string line = "[" + string.Join(", ", list.Select(item => Values.ToJson(item))) + "]";
                    if (line.Length + indent.Length <= InlineWidth)
                    {
                        sb.Append(line);
                        break;
                    }
                    goto default;
                }
                case List<object> list:
                {
                    string inner = indent + "  ";
                    sb.Append("[\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append(inner);
                        Write(sb, list[i], inner, depth + 1);
                        sb.Append(i + 1 < list.Count ? ",\n" : "\n");
                    }
                    sb.Append(indent).Append(']');
                    break;
                }
                default:
                    if (value is List<object> longList)
                    {
                        // A long list of plain values: one per line.
                        string inner = indent + "  ";
                        sb.Append("[\n");
                        for (int i = 0; i < longList.Count; i++)
                        {
                            sb.Append(inner).Append(Values.ToJson(longList[i]));
                            sb.Append(i + 1 < longList.Count ? ",\n" : "\n");
                        }
                        sb.Append(indent).Append(']');
                        break;
                    }
                    sb.Append(Values.ToJson(value));
                    break;
            }
        }

        /// <summary>
        /// Adds ANSI colours to JSON text without changing anything else, so it still reads as the same JSON once the
        /// colours are stripped. Text that isn't JSON is coloured as far as it looks like JSON.
        /// </summary>
        public static string Colourize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }
            StringBuilder sb = new StringBuilder(json.Length * 2);
            int i = 0;
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '"')
                {
                    int end = StringEnd(json, i);
                    string token = json.Substring(i, end - i);
                    Append(sb, token, IsKey(json, end) ? KeyStyle : StringStyle);
                    i = end;
                }
                else if (c == '-' || char.IsDigit(c))
                {
                    int end = i + 1;
                    while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == 'e' || json[end] == 'E' || json[end] == '+' || json[end] == '-'))
                    {
                        end++;
                    }
                    Append(sb, json.Substring(i, end - i), NumberStyle);
                    i = end;
                }
                else if (Word(json, i, "true") || Word(json, i, "false"))
                {
                    int length = json[i] == 't' ? 4 : 5;
                    Append(sb, json.Substring(i, length), BoolStyle);
                    i += length;
                }
                else if (Word(json, i, "null"))
                {
                    Append(sb, "null", NullStyle);
                    i += 4;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string text, string style) => sb.Append("\u001b[").Append(style).Append('m').Append(text).Append("\u001b[0m");

        /// <summary>The index just after the closing quote of the string that starts at <paramref name="start"/>.</summary>
        private static int StringEnd(string json, int start)
        {
            int i = start + 1;
            while (i < json.Length)
            {
                if (json[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (json[i] == '"')
                {
                    return i + 1;
                }
                i++;
            }
            return json.Length;
        }

        /// <summary>A string is an object key when a colon follows it.</summary>
        private static bool IsKey(string json, int after)
        {
            while (after < json.Length && char.IsWhiteSpace(json[after]))
            {
                after++;
            }
            return after < json.Length && json[after] == ':';
        }

        private static bool Word(string json, int i, string word) =>
            string.CompareOrdinal(json, i, word, 0, word.Length) == 0
            && (i + word.Length == json.Length || !char.IsLetterOrDigit(json[i + word.Length]))
            && (i == 0 || !char.IsLetterOrDigit(json[i - 1]));
    }
}
