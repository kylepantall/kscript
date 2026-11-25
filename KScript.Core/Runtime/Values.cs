using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace KScript.Runtime
{
    /// <summary>
    /// A value a package adds to the language, such as a file or an HTTP response. It has a type name (shown by
    /// <c>->type()</c>), fields read with <c>.name</c>, and methods called with <c>->name()</c>.
    /// </summary>
    public interface IKObject
    {
        string TypeName { get; }

        bool TryGetMember(string name, out object value);

        bool TryCallMethod(CallContext call, string name, out object result);

        /// <summary>The value as text, used by <c>{ }</c> and <c>echo</c>.</summary>
        string ToText();
    }

    /// <summary>
    /// KScript values are plain .NET objects: null, bool, double, string, DateTime, List&lt;object&gt; (lists),
    /// Dictionary&lt;string, object&gt; (maps), <see cref="ICallable"/> (functions) and <see cref="IKObject"/>
    /// (values a package adds). These helpers convert, compare and print them.
    /// </summary>
    public static class Values
    {
        /// <summary>The underlying value for operations on the refinement's base type.</summary>
        public static object Unwrap(object value) => value is RefinedValue refined ? refined.Value : value;

        public static string TypeOf(object value) => Unwrap(value) switch
        {
            null => "null",
            bool => "bool",
            double => "number",
            string => "string",
            DateTime => "date",
            List<object> => "list",
            Dictionary<string, object> => "map",
            ICallable => "fn",
            IKObject o => o.TypeName,
            var other => other.GetType().Name,
        };

        /// <summary>Converts a value from .NET code (ints, arrays, dictionaries, JSON) into a KScript value.</summary>
        public static object From(object value)
        {
            switch (value)
            {
                case null: return null;
                case string or bool or double or DateTime or ICallable or IKObject or RefinedValue: return value;
                case int i: return (double)i;
                case long l: return (double)l;
                case float f: return (double)f;
                case decimal d: return (double)d;
                case short s: return (double)s;
                case byte b: return (double)b;
                case uint ui: return (double)ui;
                case ulong ul: return (double)ul;
                case char c: return c.ToString();
                case DateTimeOffset dto: return dto.LocalDateTime;
                case TimeSpan ts: return ts.TotalSeconds;
                case JsonElement json: return FromJson(json);
                case Dictionary<string, object> map: return map;
                case List<object> list: return list;
                case IDictionary<string, string> smap: return smap.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                case IDictionary<string, object> omap: return omap.ToDictionary(kv => kv.Key, kv => From(kv.Value));
                case IDictionary dict:
                {
                    var map = new Dictionary<string, object>();
                    foreach (DictionaryEntry e in dict) map[Convert.ToString(e.Key, CultureInfo.InvariantCulture)] = From(e.Value);
                    return map;
                }
                case IEnumerable items: return items.Cast<object>().Select(From).ToList();
                default: return value.ToString();
            }
        }

        #region JSON

        public static object ParseJson(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text ?? string.Empty, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                return FromJson(doc.RootElement);
            }
            catch (JsonException ex)
            {
                throw new KScriptException($"Not valid JSON: {ex.Message}", "Json");
            }
        }

        public static object FromJson(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) map[p.Name] = FromJson(p.Value);
                    return map;
                case JsonValueKind.Array:
                    return e.EnumerateArray().Select(FromJson).ToList();
                case JsonValueKind.Number:
                    return e.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.String:
                    return e.GetString();
                default:
                    return null;
            }
        }

        public static string ToJson(object value, bool indented = false)
        {
            using var stream = new MemoryStream();
            WriteJson(stream, value, indented);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Writes KScript's JSON representation, optionally bounding the UTF-8 payload while building it.</summary>
        public static void WriteJson(Stream stream, object value, bool indented = false, int? maxBytes = null)
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            WriteJson(writer, value, 0, maxBytes);
        }

        private static void WriteJson(Utf8JsonWriter w, object value, int depth, int? maxBytes)
        {
            // Each UTF-16 code unit needs at least one UTF-8 byte in the finished JSON. Check
            // individual strings before the writer reserves space, then measure their escaped form.
            void CheckSize(int additional)
            {
                if (maxBytes.HasValue && w.BytesCommitted + w.BytesPending + additional > maxBytes.Value)
                    throw new KScriptException($"The JSON value exceeds {maxBytes.Value} bytes.", "Argument");
            }
            if (depth > 200) throw new KScriptException("The value is nested too deeply (or contains itself) to turn into JSON.");
            value = Unwrap(value);
            switch (value)
            {
                case null: w.WriteNullValue(); break;
                case bool b: w.WriteBooleanValue(b); break;
                case double d when double.IsNaN(d) || double.IsInfinity(d): w.WriteNullValue(); break;
                case double d when d == Math.Floor(d) && Math.Abs(d) < 1e15: w.WriteNumberValue((long)d); break;
                case double d: w.WriteNumberValue(d); break;
                case string s: CheckSize(s.Length); w.WriteStringValue(s); break;
                case DateTime dt: w.WriteStringValue(dt.ToString("o", CultureInfo.InvariantCulture)); break;
                case List<object> list:
                    w.WriteStartArray();
                    foreach (var item in list) WriteJson(w, item, depth + 1, maxBytes);
                    w.WriteEndArray();
                    break;
                case Dictionary<string, object> map:
                    w.WriteStartObject();
                    HashSet<string> hidden = (map as Record)?.Type.Hidden;
                    foreach (var kv in map)
                    {
                        if (hidden != null && hidden.Contains(kv.Key)) continue;
                        CheckSize(kv.Key.Length);
                        w.WritePropertyName(kv.Key);
                        WriteJson(w, kv.Value, depth + 1, maxBytes);
                    }
                    w.WriteEndObject();
                    break;
                default:
                    string text = ToStr(value);
                    CheckSize(text.Length);
                    w.WriteStringValue(text);
                    break;
            }
            CheckSize(0);
        }

        #endregion

        #region Text and numbers

        public static string ToStr(object value) => Unwrap(value) switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            double d => FormatNumber(d),
            DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            List<object> list => string.Join(", ", list.Select(ToStr)),
            Dictionary<string, object> map => ToJson(map),
            IKObject o => o.ToText(),
            ICallable fn => fn.ToString(),
            var other => other.ToString(),
        };

        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        public static bool Truthy(object value) => Unwrap(value) switch
        {
            null => false,
            IKObject native when NativeTypeRegistry.TryFalsy(native, out bool falsy) => !falsy,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            string s => s.Length > 0,
            List<object> list => list.Count > 0,
            Dictionary<string, object> map => map.Count > 0,
            _ => true,
        };

        public static bool TryNumber(object value, out double number)
        {
            switch (Unwrap(value))
            {
                case double d: number = d; return true;
                // 'NaN' and 'Infinity' are words, not numbers.
                case string s when s.Trim().Length > 0 && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed):
                    number = parsed; return true;
                default: number = 0; return false;
            }
        }

        public static double ToNumber(object value, string context)
        {
            if (TryNumber(value, out double d)) return d;
            throw new KScriptException($"{context} needs a number, but got {Describe(value)}.", "Type");
        }

        /// <summary>The whole-number part, kept within what an int holds (a position past either end of any list).</summary>
        public static int ToInt(object value, string context)
        {
            double number = Math.Truncate(ToNumber(value, context));
            return double.IsNaN(number) ? 0 : (int)Math.Clamp(number, int.MinValue, int.MaxValue);
        }

        /// <summary>Text as a date. A plain number such as '1.5' is not one.</summary>
        public static bool TryDate(string text, out DateTime date)
        {
            date = default;
            return !TryNumber(text, out _) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
        }

        /// <summary>Moves a date, or says so when the result is outside the years a date can hold.</summary>
        public static DateTime ShiftDate(Func<DateTime> shift)
        {
            try
            {
                return shift();
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new KScriptException("That is outside the dates KScript can hold (the years 1 to 9999).", "Argument");
            }
        }

        /// <summary>yes/no, true/false, on/off and 1/0, as written in attributes like <c>const="yes"</c>.</summary>
        public static bool? ParseBool(string text) => (text ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "yes" or "y" or "true" or "1" or "on" => true,
            "no" or "n" or "false" or "0" or "off" or "" => false,
            _ => null,
        };

        public static string Describe(object value) => Unwrap(value) switch
        {
            null => "null",
            string s => $"the string '{Shorten(s)}'",
            ICallable => "a function",
            _ => $"a {TypeOf(value)} ({Shorten(ToStr(value))})",
        };

        private static string Shorten(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        #endregion

        #region Comparison

        /// <summary>Strict equality (<c>===</c>): same type and same value. Lists and maps compare deeply.</summary>
        public static bool StrictEquals(object a, object b)
        {
            a = Unwrap(a);
            b = Unwrap(b);
            if (a == null || b == null) return a == null && b == null;
            if (a is PasswordValue || b is PasswordValue) throw PasswordValue.CompareError();
            // A record (a map made by a <type>) equals a plain map with the same keys and values.
            if (a.GetType() != b.GetType() && !(a is Dictionary<string, object> && b is Dictionary<string, object>)) return false;
            switch (a)
            {
                case List<object> la:
                    var lb = (List<object>)b;
                    return la.Count == lb.Count && la.Zip(lb, StrictEquals).All(x => x);
                case Dictionary<string, object> ma:
                    var mb = (Dictionary<string, object>)b;
                    return ma.Count == mb.Count && ma.All(kv => mb.TryGetValue(kv.Key, out var v) && StrictEquals(kv.Value, v));
                case ICallable:
                    return ReferenceEquals(a, b);
                default:
                    return a.Equals(b);
            }
        }

        /// <summary>
        /// Loose equality (<c>==</c>): numbers and numeric strings compare as numbers, bools compare with
        /// 'true'/'false', everything else compares by its text. null only equals null.
        /// </summary>
        public static bool LooseEquals(object a, object b)
        {
            a = Unwrap(a);
            b = Unwrap(b);
            if (a == null || b == null) return a == null && b == null;
            if (a is PasswordValue || b is PasswordValue) throw PasswordValue.CompareError();
            if (NativeTypeRegistry.TryEquals(a, b, out bool same)) return same;
            // An email or a handle equals text that means the same once normalised: 'Kyle@Example.com' == 'Kyle@example.com'.
            if (a is ISemanticText ta && b is string tb) return SemanticTypes.TryConvert(ta.TypeName, tb, out object nb, out _) && ta.Equals(nb);
            if (b is ISemanticText && a is string) return LooseEquals(b, a);
            if (a is List<object> || b is List<object> || a is Dictionary<string, object> || b is Dictionary<string, object> || a is ICallable || b is ICallable)
                return StrictEquals(a, b);
            if ((a is double || b is double) && TryNumber(a, out double x) && TryNumber(b, out double y)) return x == y;
            // Two dates are equal when they are the same moment: their text leaves the seconds out.
            if (a is DateTime da && b is DateTime db) return da == db;
            if (a is string sa && b is string sb && TryNumber(sa, out double nx) && TryNumber(sb, out double ny)) return nx == ny;
            return string.Equals(ToStr(a), ToStr(b), StringComparison.Ordinal);
        }

        public static int Compare(object a, object b, string op) => Compare(a, b, op, allowUnordered: false).Value;

        internal static int? CompareRelation(object a, object b, string op) => Compare(a, b, op, allowUnordered: true);

        private static int? Compare(object a, object b, string op, bool allowUnordered)
        {
            a = Unwrap(a);
            b = Unwrap(b);
            if (a is PasswordValue || b is PasswordValue) throw PasswordValue.CompareError();
            if (NativeTypeRegistry.TryCompare(a, b, op, out int native)) return native;
            if (TryNumber(a, out double x) && TryNumber(b, out double y)) return x.CompareTo(y);
            // true < 3 is what 1 < 2 < 3 comes to: a mistake, not a comparison.
            if (allowUnordered && ((a is bool && b is double) || (a is double && b is bool)))
                throw new KScriptException($"Can't compare {Describe(a)} with {Describe(b)} using '{op}'.", "Type");
            if (allowUnordered && ((a is double && b is string) || (a is string && b is double))) return null;
            if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
            if (a == null || b == null)
                throw new KScriptException($"Can't compare {Describe(a)} with {Describe(b)} using '{op}'.", "Type");
            return string.Compare(ToStr(a), ToStr(b), StringComparison.Ordinal);
        }

        public static IComparer<object> Comparer(string op) => Comparer<object>.Create((a, b) => Compare(a, b, op));

        #endregion

        #region Conversion for type="..."

        /// <summary>The types a <c>type</c> attribute or a parameter can declare.</summary>
        public static IReadOnlyList<string> DeclarableTypes =>
            BasicTypes.Concat(KScript.Metadata.NativeTypes.Names.Where(n => !BasicTypes.Contains(n))).Append("any").ToList();

        private static readonly string[] BasicTypes = { "string", "int", "float", "number", "bool", "list", "map", "fn", "date", "email", "password", "handle", "uuid", "regex" };

        /// <summary>
        /// True for a built-in type name. The basic types and the first native ones (email, handle, ...) ignore case; the
        /// newer native types don't, so a script's own Color or Duration type is never taken for color or duration.
        /// </summary>
        public static bool IsDeclarable(string name) =>
            name != null && (name.Equals("any", StringComparison.OrdinalIgnoreCase) || BasicTypes.Contains(name, StringComparer.OrdinalIgnoreCase) || KScript.Metadata.NativeTypes.IsNative(name));

        /// <summary>Converts a value to a declared type, or fails with a clear message.</summary>
        public static object ConvertTo(object value, string type, string what)
        {
            string name = (type ?? "any").Trim().ToLowerInvariant();
            if (name is "any" or "") return value;
            value = Unwrap(value);
            switch (name)
            {
                case "string":
                    return value is string ? value : ToStr(value);
                case "int":
                    if (TryNumber(value, out double i)) return Math.Truncate(i);
                    break;
                case "float":
                case "number":
                    if (TryNumber(value, out double f)) return f;
                    break;
                case "bool":
                    if (value is bool) return value;
                    if (value is string s && ParseBool(s) is bool parsed) return parsed;
                    if (value is double d) return d != 0;
                    break;
                case "list":
                    if (value is List<object>) return value;
                    break;
                case "map":
                    if (value is Dictionary<string, object>) return value;
                    break;
                case "fn":
                    if (value is ICallable) return value;
                    break;
                case "date":
                    if (value is DateTime) return value;
                    if (value is string ds && TryDate(ds, out var dt)) return dt;
                    break;
                case "email":
                case "password":
                case "handle":
                case "uuid":
                case "regex":
                {
                    string semantic = type.Trim().ToLowerInvariant();
                    if (SemanticTypes.TryConvert(semantic, value, out object converted, out string reason)) return converted;
                    throw new KScriptException($"{what} {SemanticTypes.Mismatch(semantic, value, reason)}.", "Type");
                }
                default:
                    if (SemanticTypes.IsSemantic(type.Trim()))
                    {
                        string native = type.Trim();
                        if (SemanticTypes.TryConvert(native, value, out object converted, out string reason)) return converted;
                        throw new KScriptException($"{what} {SemanticTypes.Mismatch(native, value, reason)}.", "Type");
                    }
                    throw new KScriptException($"Unknown type '{type}' for {what}. Use one of: {string.Join(", ", DeclarableTypes)}.", "Type");
            }
            throw new KScriptException($"{what} must be {Article(type)} {type}, but got {Describe(value)}.", "Type");
        }

        private static string Article(string word) => "aeiou".IndexOf(char.ToLowerInvariant(word[0])) >= 0 ? "an" : "a";

        #endregion

        #region Escaping

        public static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '&': sb.Append("&amp;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        #endregion

        /// <summary>The edit distance between two names, for "did you mean" suggestions.</summary>
        public static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1));
            return d[a.Length, b.Length];
        }

        /// <summary>Words as a list for a sentence: "a", "a or b", "a, b or c".</summary>
        public static string OrList(IReadOnlyList<string> words) =>
            words.Count <= 1 ? string.Concat(words) : string.Join(", ", words.Take(words.Count - 1)) + " or " + words[words.Count - 1];

        /// <summary>The closest of <paramref name="candidates"/> to <paramref name="name"/>, if it is close enough to suggest.</summary>
        public static string Closest(string name, IEnumerable<string> candidates)
        {
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (string c in candidates)
            {
                int dist = Distance(name, c);
                if (dist < bestDistance) { best = c; bestDistance = dist; }
            }
            return best != null && bestDistance <= Math.Max(1, Math.Min(3, name.Length / 3)) ? best : null;
        }
    }
}
