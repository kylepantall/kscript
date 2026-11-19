using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Newtonsoft.Json.Linq;

namespace KScript.Pages
{
    /// <summary>Methods called with <c>value->name(args)</c> or <c>value |> name(args)</c>.</summary>
    internal static class PageMethods
    {
        public static object Invoke(string name, object target, List<object> args, PageContext ctx, string source)
        {
            string where = $"->{name}() in '{source}'";

            // Methods every value has.
            switch (name)
            {
                case "type": return PageValues.TypeOf(target);
                case "to_string": return PageValues.ToStr(target);
                case "to_json": return PageValues.ToJsonString(target, args.Count > 0 && PageValues.Truthy(args[0]));
                case "is_null": return target == null;
                case "or": return PageValues.Truthy(target) ? target : Arg(args, 0);
                case "call":
                    if (target is PageLambda fn) return PageEvaluator.Call(fn, args, ctx);
                    break;
            }

            switch (target)
            {
                case List<object> list: return ListMethod(name, list, args, ctx, where);
                case Dictionary<string, object> map: return MapMethod(name, map, args, ctx, where);
                case double d: return NumberMethod(name, d, args, where);
                case DateTime dt: return DateMethod(name, dt, args, where);
                case string s:
                    if (StringMethods.Contains(name)) return StringMethod(name, s, args, ctx, where);
                    if (PageValues.TryNumber(s, out double n) && NumberMethods.Contains(name)) return NumberMethod(name, n, args, where);
                    return StringMethod(name, s, args, ctx, where);
                case bool b when name == "not": return !b;
                case null:
                    throw new PageException($"Can't call ->{name}() on null in '{source}'. Use '?.' or '??' if the value may be missing.");
            }
            throw new PageException($"{PageValues.Describe(target)} has no method '{name}' ({where}).");
        }

        private static readonly HashSet<string> NumberMethods = new HashSet<string> { "round", "floor", "ceil", "abs", "format", "to_number", "to_int", "min", "max", "clamp" };
        private static readonly HashSet<string> StringMethods = new HashSet<string>
        {
            "upper", "lower", "trim", "length", "count", "contains", "starts_with", "ends_with", "replace", "split", "slice",
            "index_of", "repeat", "reverse", "is_empty", "lines", "capitalize", "title", "markdown", "escape", "url_encode",
            "pad_left", "pad_right", "truncate", "to_number", "to_int", "to_bool", "to_date", "json", "first", "last",
        };

        private static object Arg(List<object> args, int i) => i < args.Count ? args[i] : null;

        private static void NeedArgs(List<object> args, int count, string where)
        {
            if (args.Count < count) throw new PageException($"{where} needs {count} argument(s) but got {args.Count}.");
        }

        private static PageLambda Fn(List<object> args, int i, string where) =>
            Arg(args, i) as PageLambda ?? throw new PageException($"{where} needs a lambda like fn($x) => ... as argument {i + 1}.");

        private static double NumArg(List<object> args, int i, string where) => PageValues.ToNumber(Arg(args, i), where);

        private static object ListMethod(string name, List<object> list, List<object> args, PageContext ctx, string where)
        {
            object Apply(PageLambda f, object item, int index) =>
                PageEvaluator.Call(f, new List<object> { item, (double)index }, ctx);

            switch (name)
            {
                case "count":
                case "length":
                    if (args.Count > 0) { var f = Fn(args, 0, where); return (double)list.Where((x, i) => PageValues.Truthy(Apply(f, x, i))).Count(); }
                    return (double)list.Count;
                case "is_empty": return list.Count == 0;
                case "first": return list.Count > 0 ? list[0] : null;
                case "last": return list.Count > 0 ? list[^1] : null;
                case "at": { int i = (int)NumArg(args, 0, where); if (i < 0) i += list.Count; return i >= 0 && i < list.Count ? list[i] : null; }
                case "filter":
                case "where": { var f = Fn(args, 0, where); return list.Where((x, i) => PageValues.Truthy(Apply(f, x, i))).ToList(); }
                case "reject": { var f = Fn(args, 0, where); return list.Where((x, i) => !PageValues.Truthy(Apply(f, x, i))).ToList(); }
                case "map": { var f = Fn(args, 0, where); return list.Select((x, i) => Apply(f, x, i)).ToList(); }
                case "find": { var f = Fn(args, 0, where); return list.Where((x, i) => PageValues.Truthy(Apply(f, x, i))).FirstOrDefault(); }
                case "find_index": { var f = Fn(args, 0, where); for (int i = 0; i < list.Count; i++) if (PageValues.Truthy(Apply(f, list[i], i))) return (double)i; return -1d; }
                case "any": { if (args.Count == 0) return list.Count > 0; var f = Fn(args, 0, where); return list.Where((x, i) => PageValues.Truthy(Apply(f, x, i))).Any(); }
                case "all": { var f = Fn(args, 0, where); return list.Select((x, i) => PageValues.Truthy(Apply(f, x, i))).All(b => b); }
                case "each": { var f = Fn(args, 0, where); for (int i = 0; i < list.Count; i++) Apply(f, list[i], i); return list; }
                case "reduce":
                {
                    var f = Fn(args, 0, where);
                    object acc = Arg(args, 1);
                    foreach (var item in list) acc = PageEvaluator.Call(f, new List<object> { acc, item }, ctx);
                    return acc;
                }
                case "sort":
                {
                    var copy = list.ToList();
                    if (args.Count > 0 && args[0] is PageLambda cmp)
                        copy.Sort((a, b) => Math.Sign(PageValues.ToNumber(PageEvaluator.Call(cmp, new List<object> { a, b }, ctx), where)));
                    else
                        copy = copy.OrderBy(x => x, Comparer<object>.Create((a, b) => PageValues.Compare(a, b, "sort"))).ToList();
                    return copy;
                }
                case "sort_by":
                {
                    var f = Fn(args, 0, where);
                    var keyed = list.Select(x => (x, k: PageEvaluator.Call(f, new List<object> { x }, ctx))).ToList();
                    var cmp = Comparer<object>.Create((a, b) => PageValues.Compare(a, b, "sort_by"));
                    bool desc = args.Count > 1 && PageValues.ToStr(args[1]).Equals("desc", StringComparison.OrdinalIgnoreCase);
                    return (desc ? keyed.OrderByDescending(p => p.k, cmp) : keyed.OrderBy(p => p.k, cmp)).Select(p => p.x).ToList();
                }
                case "max_by":
                case "min_by":
                {
                    if (list.Count == 0) return null;
                    var f = Fn(args, 0, where);
                    object best = list[0];
                    object bestKey = PageEvaluator.Call(f, new List<object> { best }, ctx);
                    foreach (var item in list.Skip(1))
                    {
                        var k = PageEvaluator.Call(f, new List<object> { item }, ctx);
                        int c = PageValues.Compare(k, bestKey, name);
                        if (name == "max_by" ? c > 0 : c < 0) { best = item; bestKey = k; }
                    }
                    return best;
                }
                case "max": return list.Count == 0 ? null : list.Aggregate((a, b) => PageValues.Compare(a, b, "max") >= 0 ? a : b);
                case "min": return list.Count == 0 ? null : list.Aggregate((a, b) => PageValues.Compare(a, b, "min") <= 0 ? a : b);
                case "sum":
                {
                    var f = args.Count > 0 ? Fn(args, 0, where) : null;
                    return list.Sum(x => PageValues.ToNumber(f == null ? x : PageEvaluator.Call(f, new List<object> { x }, ctx), where));
                }
                case "average":
                {
                    if (list.Count == 0) return null;
                    var f = args.Count > 0 ? Fn(args, 0, where) : null;
                    return list.Average(x => PageValues.ToNumber(f == null ? x : PageEvaluator.Call(f, new List<object> { x }, ctx), where));
                }
                case "group_by":
                {
                    var f = Fn(args, 0, where);
                    var groups = new Dictionary<string, object>();
                    foreach (var item in list)
                    {
                        string key = PageValues.ToStr(PageEvaluator.Call(f, new List<object> { item }, ctx));
                        if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<object>();
                        ((List<object>)g).Add(item);
                    }
                    return groups;
                }
                case "reverse": { var copy = list.ToList(); copy.Reverse(); return copy; }
                case "join": return string.Join(args.Count > 0 ? PageValues.ToStr(args[0]) : ", ", list.Select(PageValues.ToStr));
                case "contains": return list.Any(x => PageValues.LooseEquals(x, Arg(args, 0)));
                case "index_of": return (double)list.FindIndex(x => PageValues.LooseEquals(x, Arg(args, 0)));
                case "slice":
                {
                    int start = (int)NumArg(args, 0, where);
                    int end = args.Count > 1 ? (int)NumArg(args, 1, where) : list.Count;
                    if (start < 0) start += list.Count;
                    if (end < 0) end += list.Count;
                    start = Math.Clamp(start, 0, list.Count);
                    end = Math.Clamp(end, start, list.Count);
                    return list.GetRange(start, end - start);
                }
                case "take": return list.Take((int)NumArg(args, 0, where)).ToList();
                case "skip": return list.Skip((int)NumArg(args, 0, where)).ToList();
                case "unique": return list.Aggregate(new List<object>(), (acc, x) => { if (!acc.Any(y => PageValues.StrictEquals(x, y))) acc.Add(x); return acc; });
                case "push":
                case "append": return list.Concat(args).ToList();
                case "flatten": return list.SelectMany(x => x is List<object> l ? l : new List<object> { x }).ToList();
                case "chunk":
                {
                    int size = Math.Max(1, (int)NumArg(args, 0, where));
                    return list.Select((x, i) => (x, i)).GroupBy(p => p.i / size).Select(g => (object)g.Select(p => p.x).ToList()).ToList();
                }
            }
            throw new PageException($"Lists have no method '{name}' ({where}).");
        }

        private static object MapMethod(string name, Dictionary<string, object> map, List<object> args, PageContext ctx, string where)
        {
            switch (name)
            {
                case "keys": return map.Keys.Cast<object>().ToList();
                case "values": return map.Values.ToList();
                case "entries": return map.Select(kv => (object)new Dictionary<string, object> { ["key"] = kv.Key, ["value"] = kv.Value }).ToList();
                case "has": return map.ContainsKey(PageValues.ToStr(Arg(args, 0)));
                case "get": return map.TryGetValue(PageValues.ToStr(Arg(args, 0)), out var v) ? v : Arg(args, 1);
                case "set":
                {
                    NeedArgs(args, 2, where);
                    var copy = new Dictionary<string, object>(map) { [PageValues.ToStr(args[0])] = args[1] };
                    return copy;
                }
                case "remove": { var copy = new Dictionary<string, object>(map); copy.Remove(PageValues.ToStr(Arg(args, 0))); return copy; }
                case "merge":
                {
                    var copy = new Dictionary<string, object>(map);
                    if (Arg(args, 0) is Dictionary<string, object> other) foreach (var kv in other) copy[kv.Key] = kv.Value;
                    return copy;
                }
                case "count":
                case "length": return (double)map.Count;
                case "is_empty": return map.Count == 0;
            }
            throw new PageException($"Maps have no method '{name}' ({where}). To read a key, use .{name} or ['{name}'].");
        }

        private static object NumberMethod(string name, double d, List<object> args, string where)
        {
            switch (name)
            {
                case "round": return Math.Round(d, args.Count > 0 ? (int)NumArg(args, 0, where) : 0, MidpointRounding.AwayFromZero);
                case "floor": return Math.Floor(d);
                case "ceil": return Math.Ceiling(d);
                case "abs": return Math.Abs(d);
                case "min": return Math.Min(d, NumArg(args, 0, where));
                case "max": return Math.Max(d, NumArg(args, 0, where));
                case "clamp": return Math.Clamp(d, NumArg(args, 0, where), NumArg(args, 1, where));
                case "to_number": return d;
                case "to_int": return Math.Truncate(d);
                case "format": return d.ToString(args.Count > 0 ? PageValues.ToStr(args[0]) : "N0", CultureInfo.InvariantCulture);
                case "to_string": return PageValues.FormatNumber(d);
                case "increment": return d + 1;
                case "decrement": return d - 1;
                case "is_even": return d % 2 == 0;
                case "is_odd": return Math.Abs(d % 2) == 1;
            }
            throw new PageException($"Numbers have no method '{name}' ({where}).");
        }

        private static object DateMethod(string name, DateTime dt, List<object> args, string where)
        {
            switch (name)
            {
                case "format": return dt.ToString(args.Count > 0 ? PageValues.ToStr(args[0]) : "yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "year": return (double)dt.Year;
                case "month": return (double)dt.Month;
                case "day": return (double)dt.Day;
                case "hour": return (double)dt.Hour;
                case "minute": return (double)dt.Minute;
                case "weekday": return dt.DayOfWeek.ToString();
                case "add_days": return dt.AddDays(NumArg(args, 0, where));
                case "add_hours": return dt.AddHours(NumArg(args, 0, where));
                case "iso": return dt.ToString("o", CultureInfo.InvariantCulture);
            }
            throw new PageException($"Dates have no method '{name}' ({where}).");
        }

        private static object StringMethod(string name, string s, List<object> args, PageContext ctx, string where)
        {
            string A(int i) => PageValues.ToStr(Arg(args, i));
            switch (name)
            {
                case "upper": return s.ToUpperInvariant();
                case "lower": return s.ToLowerInvariant();
                case "trim": return args.Count > 0 ? s.Trim(A(0).ToCharArray()) : s.Trim();
                case "length":
                case "count": return (double)s.Length;
                case "is_empty": return s.Trim().Length == 0;
                case "contains": return s.Contains(A(0), StringComparison.Ordinal);
                case "starts_with": return s.StartsWith(A(0), StringComparison.Ordinal);
                case "ends_with": return s.EndsWith(A(0), StringComparison.Ordinal);
                case "replace": NeedArgs(args, 2, where); return s.Replace(A(0), A(1), StringComparison.Ordinal);
                case "split": return (args.Count > 0 ? s.Split(A(0)) : s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).Cast<object>().ToList();
                case "lines": return s.Replace("\r\n", "\n").Split('\n').Cast<object>().ToList();
                case "index_of": return (double)s.IndexOf(A(0), StringComparison.Ordinal);
                case "repeat": return string.Concat(Enumerable.Repeat(s, Math.Max(0, (int)NumArg(args, 0, where))));
                case "reverse": { var arr = s.ToCharArray(); Array.Reverse(arr); return new string(arr); }
                case "first": return s.Length > 0 ? s.Substring(0, 1) : string.Empty;
                case "last": return s.Length > 0 ? s.Substring(s.Length - 1) : string.Empty;
                case "slice":
                {
                    int start = (int)NumArg(args, 0, where);
                    int end = args.Count > 1 ? (int)NumArg(args, 1, where) : s.Length;
                    if (start < 0) start += s.Length;
                    if (end < 0) end += s.Length;
                    start = Math.Clamp(start, 0, s.Length);
                    end = Math.Clamp(end, start, s.Length);
                    return s.Substring(start, end - start);
                }
                case "truncate":
                {
                    int max = (int)NumArg(args, 0, where);
                    string tail = args.Count > 1 ? A(1) : "…";
                    return s.Length <= max ? s : s.Substring(0, Math.Max(0, max)).TrimEnd() + tail;
                }
                case "pad_left": return s.PadLeft((int)NumArg(args, 0, where), args.Count > 1 && A(1).Length > 0 ? A(1)[0] : ' ');
                case "pad_right": return s.PadRight((int)NumArg(args, 0, where), args.Count > 1 && A(1).Length > 0 ? A(1)[0] : ' ');
                case "capitalize": return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
                case "title": return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
                case "escape": return PageValues.HtmlEscape(s);
                case "url_encode": return Uri.EscapeDataString(s);
                case "markdown": return PageMarkdown.ToHtml(s);
                case "json":
                    try { return PageValues.FromJson(JToken.Parse(s)); }
                    catch (Newtonsoft.Json.JsonException ex) { throw new PageException($"Not valid JSON ({where}): {ex.Message}"); }
                case "to_number":
                case "to_int":
                {
                    if (!PageValues.TryNumber(s, out double d))
                    {
                        if (args.Count > 0) return args[0];
                        throw new PageException($"'{s}' is not a number ({where}). Pass a default: ->{name}(0).");
                    }
                    return name == "to_int" ? Math.Truncate(d) : d;
                }
                case "to_bool":
                    return s.Trim().ToLowerInvariant() switch
                    {
                        "true" or "yes" or "y" or "1" or "on" => true,
                        "false" or "no" or "n" or "0" or "off" or "" => false,
                        _ => throw new PageException($"'{s}' is not a yes/no value ({where})."),
                    };
                case "to_date":
                    if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return dt;
                    throw new PageException($"'{s}' is not a date ({where}).");
            }
            throw new PageException($"Strings have no method '{name}' ({where}).");
        }
    }

    /// <summary>Built-in <c>@commands</c> available in pages.</summary>
    internal static class PageCommands
    {
        private static readonly Random Rng = new Random();

        public static object Invoke(string name, List<object> args, PageContext ctx, string source)
        {
            object Arg(int i) => i < args.Count ? args[i] : null;
            string S(int i) => PageValues.ToStr(Arg(i));
            string where = $"@{name}() in '{source}'";

            switch (name)
            {
                case "read_text": return ctx.ReadSiteFile(S(0), where);
                case "read_json":
                    try { return PageValues.FromJson(JToken.Parse(ctx.ReadSiteFile(S(0), where))); }
                    catch (Newtonsoft.Json.JsonException ex) { throw new PageException($"{S(0)} is not valid JSON ({where}): {ex.Message}"); }
                case "read_markdown": return PageMarkdown.Document(ctx.ReadSiteFile(S(0), where));
                case "files": return ctx.ListSiteFiles(args.Count > 0 ? S(0) : ".", args.Count > 1 ? S(1) : "*", where);
                case "exists": return ctx.SiteFileExists(S(0));
                case "json":
                    try { return PageValues.FromJson(JToken.Parse(S(0))); }
                    catch (Newtonsoft.Json.JsonException ex) { throw new PageException($"Not valid JSON ({where}): {ex.Message}"); }
                case "to_json": return PageValues.ToJsonString(Arg(0), args.Count > 1 && PageValues.Truthy(Arg(1)));
                case "markdown": return PageMarkdown.Document(S(0));
                case "escape": return PageValues.HtmlEscape(S(0));
                case "now": return DateTime.Now;
                case "today": return DateTime.Today;
                case "date":
                    if (DateTime.TryParse(S(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)) return dt;
                    throw new PageException($"'{S(0)}' is not a date ({where}).");
                case "range":
                {
                    double start = args.Count > 1 ? PageValues.ToNumber(Arg(0), where) : 0;
                    double end = PageValues.ToNumber(args.Count > 1 ? Arg(1) : Arg(0), where);
                    double step = args.Count > 2 ? PageValues.ToNumber(Arg(2), where) : 1;
                    if (step == 0) throw new PageException($"Step can't be 0 ({where}).");
                    var list = new List<object>();
                    for (double x = start; step > 0 ? x < end : x > end; x += step)
                    {
                        list.Add(x);
                        if (list.Count > 100_000) throw new PageException($"Range is too large ({where}).");
                    }
                    return list;
                }
                case "random":
                    if (args.Count == 0) return Rng.NextDouble();
                    if (Arg(0) is List<object> items) return items.Count == 0 ? null : items[Rng.Next(items.Count)];
                    return (double)Rng.Next((int)PageValues.ToNumber(Arg(0), where), (int)PageValues.ToNumber(Arg(1), where) + 1);
                case "guid": return Guid.NewGuid().ToString();
                case "number": return PageValues.ToNumber(Arg(0), where);
                case "string": return S(0);
                case "type": return PageValues.TypeOf(Arg(0));
                case "join_path": return string.Join("/", args.Select(PageValues.ToStr).Select(p => p.Trim('/')).Where(p => p.Length > 0));
                case "min": return args.Count == 1 && Arg(0) is List<object> ml ? ml.Min(x => PageValues.ToNumber(x, where)) : args.Min(x => PageValues.ToNumber(x, where));
                case "max": return args.Count == 1 && Arg(0) is List<object> xl ? xl.Max(x => PageValues.ToNumber(x, where)) : args.Max(x => PageValues.ToNumber(x, where));
            }

            throw new PageException($"Unknown command @{name}() in '{source}'.");
        }
    }
}
