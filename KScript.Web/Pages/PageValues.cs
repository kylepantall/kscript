using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KScript.Pages
{
    /// <summary>
    /// A lambda value: <c>fn($x) => body</c>, closed over the scope it was created in.
    /// </summary>
    public sealed class PageLambda
    {
        internal PageLambda(IReadOnlyList<string> parameters, PageExpr body, PageScope closure, string source)
        {
            Parameters = parameters;
            Body = body;
            Closure = closure;
            Source = source;
        }

        public IReadOnlyList<string> Parameters { get; }
        internal PageExpr Body { get; }
        internal PageScope Closure { get; }
        public string Source { get; }

        public override string ToString() => Source;
    }

    /// <summary>
    /// Values in page expressions are plain .NET objects: null, bool, double, string, DateTime,
    /// List&lt;object&gt; (lists), Dictionary&lt;string, object&gt; (maps) and <see cref="PageLambda"/>.
    /// </summary>
    public static class PageValues
    {
        public static string TypeOf(object value) => value switch
        {
            null => "null",
            bool => "bool",
            double => "number",
            string => "string",
            DateTime => "date",
            List<object> => "list",
            Dictionary<string, object> => "map",
            PageLambda => "fn",
            KScript.Runtime.ICallable => "fn",
            _ => value.GetType().Name,
        };

        /// <summary>Normalises values coming from .NET code (ints, arrays, JSON) into page values.</summary>
        public static object From(object value)
        {
            switch (value)
            {
                case null: return null;
                case string or bool or double or DateTime or PageLambda or KScript.Runtime.ICallable: return value;
                case int i: return (double)i;
                case long l: return (double)l;
                case float f: return (double)f;
                case decimal d: return (double)d;
                case JToken token: return FromJson(token);
                case Dictionary<string, object> map: return map;
                case List<object> list: return list;
                case IDictionary<string, string> smap: return smap.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                case IDictionary<string, object> omap: return omap.ToDictionary(kv => kv.Key, kv => From(kv.Value));
                case System.Collections.IEnumerable items: return items.Cast<object>().Select(From).ToList();
                default: return value.ToString();
            }
        }

        public static object FromJson(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var map = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties()) map[prop.Name] = FromJson(prop.Value);
                    return map;
                case JTokenType.Array:
                    return token.Select(FromJson).ToList();
                case JTokenType.Integer:
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Date:
                    return token.Value<DateTime>();
                default:
                    return token.ToString();
            }
        }

        public static JToken ToJson(object value)
        {
            switch (value)
            {
                case null: return JValue.CreateNull();
                case List<object> list: return new JArray(list.Select(ToJson));
                case Dictionary<string, object> map:
                    var obj = new JObject();
                    foreach (var kv in map) obj[kv.Key] = ToJson(kv.Value);
                    return obj;
                case double d when d == Math.Floor(d) && Math.Abs(d) < 1e15: return new JValue((long)d);
                case PageLambda lambda: return new JValue(lambda.Source);
                case KScript.Runtime.ICallable callable: return new JValue("@" + callable.Name);
                default: return new JValue(value);
            }
        }

        public static string ToJsonString(object value, bool indented = false) =>
            ToJson(value).ToString(indented ? Formatting.Indented : Formatting.None);

        public static string ToStr(object value) => value switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            double d => FormatNumber(d),
            DateTime dt => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            List<object> list => string.Join(", ", list.Select(ToStr)),
            Dictionary<string, object> => ToJsonString(value),
            _ => value.ToString(),
        };

        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        public static bool Truthy(object value) => value switch
        {
            null => false,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            string s => s.Length > 0,
            List<object> list => list.Count > 0,
            Dictionary<string, object> map => map.Count > 0,
            _ => true,
        };

        public static bool TryNumber(object value, out double number)
        {
            switch (value)
            {
                case double d: number = d; return true;
                case bool b: number = b ? 1 : 0; return false;
                case string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && s.Trim().Length > 0:
                    number = parsed; return true;
                default: number = 0; return false;
            }
        }

        public static double ToNumber(object value, string context)
        {
            if (TryNumber(value, out double d)) return d;
            throw new PageException($"{context} needs a number, but got {Describe(value)}.");
        }

        public static string Describe(object value) => value switch
        {
            null => "null",
            string s => $"the string '{(s.Length > 40 ? s.Substring(0, 40) + "…" : s)}'",
            _ => $"a {TypeOf(value)} ({Shorten(ToStr(value))})",
        };

        private static string Shorten(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        /// <summary>Strict equality (<c>===</c>): same type and same value. Lists and maps compare deeply.</summary>
        public static bool StrictEquals(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.GetType() != b.GetType()) return false;
            switch (a)
            {
                case List<object> la:
                    var lb = (List<object>)b;
                    return la.Count == lb.Count && la.Zip(lb, StrictEquals).All(x => x);
                case Dictionary<string, object> ma:
                    var mb = (Dictionary<string, object>)b;
                    return ma.Count == mb.Count && ma.All(kv => mb.TryGetValue(kv.Key, out var v) && StrictEquals(kv.Value, v));
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
            if (a == null || b == null) return a == null && b == null;
            if (a is List<object> || b is List<object> || a is Dictionary<string, object> || b is Dictionary<string, object>)
                return StrictEquals(a, b);
            if ((a is double || b is double) && TryNumber(a, out double x) && TryNumber(b, out double y)) return x == y;
            if (a is string sa && b is string sb && TryNumber(sa, out double nx) && TryNumber(sb, out double ny)) return nx == ny;
            return string.Equals(ToStr(a), ToStr(b), StringComparison.Ordinal);
        }

        public static int Compare(object a, object b, string op)
        {
            if (TryNumber(a, out double x) && TryNumber(b, out double y)) return x.CompareTo(y);
            if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
            if (a == null || b == null)
                throw new PageException($"Can't compare {Describe(a)} with {Describe(b)} using '{op}'.");
            return string.Compare(ToStr(a), ToStr(b), StringComparison.Ordinal);
        }

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

        /// <summary>Escapes a value placed in Markdown so it is shown literally, not read as Markdown or HTML.</summary>
        public static string MarkdownEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if ("\\`*_[]#|~".IndexOf(c) >= 0) sb.Append('\\').Append(c);
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '&') sb.Append("&amp;");
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
