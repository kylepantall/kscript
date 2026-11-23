using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        /// <summary>Calls f with an item, and its index too when f can take one: @greet(who) gets only the item.</summary>
        private static object Apply(CallContext c, ICallable f, object item, int index) =>
            TakesIndex(f) ? c.Call(f, item, (double)index) : c.Call(f, item);

        private static bool TakesIndex(ICallable f) => f switch
        {
            UserFunction u => u.Parameters.Count >= 2,
            NativeFunction => false,
            _ => true,
        };

        private static List<object> L(object target) => (List<object>)target;
        private static Dictionary<string, object> M(object target) => (Dictionary<string, object>)target;
        private static string S(object target) => (string)target;
        private static double N(object target) => (double)target;
        private static DateTime D(object target) => (DateTime)target;

        /// <summary>The .NET format for a date_format argument: a date_format value, or text that is one (checked, with the reason when it isn't).</summary>
        private static string DateFormatOf(object value) =>
            DateFormatType.DotNet(value is NativeText { TypeName: "date_format" } format ? format : (NativeText)Native("date_format", Values.ToStr(value)));

        private static string ErrorName(CallContext c, object type) => type switch
        {
            ErrorType t => t.Name,
            string s => s,
            _ => throw c.Error($"->is() takes an error type, such as NotFound or 'NotFound', not {Values.Describe(type)}.", "Type"),
        };

        private static int Clamp(int i, int count) => Math.Clamp(i < 0 ? i + count : i, 0, count);

        private static void RegisterMethods(PackageBuilder p)
        {
            // Any value.
            p.Method(KType.Any, "type", "", "The value's type: string, number, bool, list, map, fn, date, error or null, or the name of the <type> that made a record.", KType.String, (t, c) => Shapes.TypeName(t));
            p.Method(KType.Any, "to_string", "", "The value as text.", KType.String, (t, c) => Values.ToStr(t));
            p.Method(KType.Any, "to_json", "indented?: bool", "The value as JSON text.", KType.String, (t, c) => Values.ToJson(t, c.Bool(0)));
            p.Method(KType.Any, "to_yaml", "", "The value as YAML text.", KType.String, (t, c) => Yaml.Write(t));
            p.Method(KType.Any, "is_null", "", "True when the value is null.", KType.Bool, (t, c) => t == null);
            p.Method(KType.Any, "or", "fallback", "The value if it is truthy, otherwise fallback.", KType.Any, (t, c) => Values.Truthy(t) ? t : c.Arg(0));
            p.Method(KType.Fn, "call", "args...", "Calls the function with these arguments.", KType.Any, (t, c) => c.Interpreter.Call((ICallable)t, c.Args.ToList(), c.Named, c.Scope, c.Module));
            p.Method(KType.Bool, "not", "", "The opposite bool.", KType.Bool, (t, c) => !(bool)t);

            // Caught errors and error types.
            p.Method(KType.Error, "is", "type", "True when the error is this type or one that extends it: $err->is(NotFound), $err->is(IO).", KType.Bool, (t, c) => ((ErrorValue)t).Is(ErrorName(c, c.Arg(0))));
            p.Method(KType.Error, "chain", "", "The error's type and the types it extends, nearest first: ['NotFound', 'Validation', 'Error'].", KType.List, (t, c) => ErrorTypes.Chain(((ErrorValue)t).Exception).Cast<object>().ToList());
            foreach (var f in ErrorType.ValueFields)
                p.Field(KType.ErrorType, f.Name, f.Type, f.Description);
            foreach (var m in ErrorType.Methods)
                p.Method(KType.ErrorType, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((ErrorType)t).TryCallMethod(c, m.Name, out object r) ? r : null);

            RegisterListMethods(p);
            RegisterMapMethods(p);
            RegisterNumberMethods(p);
            RegisterDateMethods(p);
            RegisterStringMethods(p);
            RegisterNativeMethods(p);
        }

        private static void RegisterListMethods(PackageBuilder p)
        {
            string l = KType.List;
            p.Method(l, "count", "", "The number of items.", KType.Number, (t, c) =>
                c.Has(0) ? throw c.Error("->count() takes no arguments. To count the items that match, use ->count_of(fn).", "Argument") : (double)L(t).Count);
            p.Method(l, "count_of", "match", "Counts items for which a function is true, or items equal to a value using ==.", KType.Number, (t, c) =>
                c.Arg(0) is ICallable ? (double)L(t).Where((x, i) => Values.Truthy(Apply(c, c.Fn(0), x, i))).Count() : (double)L(t).Count(x => Values.LooseEquals(x, c.Arg(0))));
            p.Method(l, "is_empty", "", "True when the list has no items.", KType.Bool, (t, c) => L(t).Count == 0);
            p.Method(l, "first", "", "The first item, or null.", KType.Any, (t, c) => L(t).Count > 0 ? L(t)[0] : null);
            p.Method(l, "last", "", "The last item, or null.", KType.Any, (t, c) => L(t).Count > 0 ? L(t)[^1] : null);
            p.Method(l, "at", "index: number", "The item at an index; negative counts from the end.", KType.Any, (t, c) =>
            {
                var list = L(t);
                int i = c.Int(0);
                if (i < 0) i += list.Count;
                return i >= 0 && i < list.Count ? list[i] : null;
            });
            p.Method(l, "filter", "fn", "The items for which fn(item, index) is true.", l, (t, c) => ListFilters.Where(L(t), (x, i) => Values.Truthy(Apply(c, c.Fn(0), x, i))));
            p.Method(l, "reject", "fn", "The items for which fn is false.", l, (t, c) => ListFilters.Where(L(t), (x, i) => !Values.Truthy(Apply(c, c.Fn(0), x, i))));
            p.Method(l, "map", "fn", "A new list with fn(item, index) applied to each item.", l, (t, c) => L(t).Select((x, i) => Apply(c, c.Fn(0), x, i)).ToList());
            p.Method(l, "pluck", "key", "One field from each item, or a map of the selected fields for each item when given a list of keys.", l, Pluck).Info.KeepsBareWords = true;
            p.Method(l, "find", "fn", "The first item for which fn is true, or null.", KType.Any, (t, c) => L(t).Where((x, i) => Values.Truthy(Apply(c, c.Fn(0), x, i))).FirstOrDefault());
            p.Method(l, "find_index", "fn", "The index of the first item for which fn is true, or -1.", KType.Number, (t, c) =>
            {
                var list = L(t);
                for (int i = 0; i < list.Count; i++) if (Values.Truthy(Apply(c, c.Fn(0), list[i], i))) return (double)i;
                return -1d;
            });
            p.Method(l, "any", "fn?", "True when fn is true for any item (or, without fn, if the list isn't empty).", KType.Bool, (t, c) =>
                c.Has(0) ? L(t).Where((x, i) => Values.Truthy(Apply(c, c.Fn(0), x, i))).Any() : L(t).Count > 0);
            p.Method(l, "all", "fn", "True when fn is true for every item.", KType.Bool, (t, c) => L(t).Select((x, i) => Values.Truthy(Apply(c, c.Fn(0), x, i))).All(b => b));
            p.Method(l, "each", "fn", "Calls fn for each item and returns the list.", l, (t, c) =>
            {
                var list = L(t);
                for (int i = 0; i < list.Count; i++) Apply(c, c.Fn(0), list[i], i);
                return list;
            });
            p.Method(l, "reduce", "fn, initial?", "Combines the items: fn(total, item), starting from initial.", KType.Any, (t, c) =>
            {
                object acc = c.Arg(1);
                foreach (object item in L(t)) acc = c.Call(c.Fn(0), acc, item);
                return acc;
            });
            p.Method(l, "sort", "fn?", "A sorted copy; fn(a, b) compares (negative, 0 or positive).", l, (t, c) =>
            {
                var copy = L(t).ToList();
                if (c.Has(0))
                {
                    ICallable cmp = c.Fn(0);
                    return copy.OrderBy(x => x, Comparer<object>.Create((a, b) => Math.Sign(Values.ToNumber(c.Call(cmp, a, b), c.Display)))).ToList();
                }
                return copy.OrderBy(x => x, Values.Comparer("sort")).ToList();
            });
            p.Method(l, "sort_by", "fn, order?", "A copy sorted by fn(item); order 'desc' sorts largest first.", l, (t, c) =>
            {
                var keyed = L(t).Select(x => (x, k: c.Call(c.Fn(0), x))).ToList();
                bool desc = c.Str(1, "asc").Equals("desc", StringComparison.OrdinalIgnoreCase);
                return (desc ? keyed.OrderByDescending(e => e.k, Values.Comparer("sort_by")) : keyed.OrderBy(e => e.k, Values.Comparer("sort_by"))).Select(e => e.x).ToList();
            });
            p.Method(l, "max_by", "fn", "The item with the largest fn(item).", KType.Any, (t, c) => ExtremeBy(t, c, max: true));
            p.Method(l, "min_by", "fn", "The item with the smallest fn(item).", KType.Any, (t, c) => ExtremeBy(t, c, max: false));
            p.Method(l, "max", "", "The largest item.", KType.Any, (t, c) => L(t).Count == 0 ? null : L(t).Aggregate((a, b) => Values.Compare(a, b, "max") >= 0 ? a : b));
            p.Method(l, "min", "", "The smallest item.", KType.Any, (t, c) => L(t).Count == 0 ? null : L(t).Aggregate((a, b) => Values.Compare(a, b, "min") <= 0 ? a : b));
            p.Method(l, "sum", "fn?", "The total of the items (or of fn for each item).", KType.Number, (t, c) =>
                L(t).Sum(x => Values.ToNumber(c.Has(0) ? c.Call(c.Fn(0), x) : x, c.Display)));
            p.Method(l, "average", "fn?", "The mean of the items (or of fn for each item), or null when empty.", KType.Number, (t, c) =>
                L(t).Count == 0 ? null : L(t).Average(x => Values.ToNumber(c.Has(0) ? c.Call(c.Fn(0), x) : x, c.Display)));
            p.Method(l, "group_by", "fn", "A map from fn's result to the items that gave it.", KType.Map, (t, c) =>
            {
                var groups = new Dictionary<string, object>();
                foreach (object item in L(t))
                {
                    string key = Values.ToStr(c.Call(c.Fn(0), item));
                    if (!groups.TryGetValue(key, out object g)) groups[key] = g = new List<object>();
                    ((List<object>)g).Add(item);
                }
                return groups;
            });
            p.Method(l, "reverse", "", "A reversed copy.", l, (t, c) => { var copy = L(t).ToList(); copy.Reverse(); return copy; });
            p.Method(l, "join", "separator?", "The items joined into text (', ' by default).", KType.String, (t, c) => string.Join(c.Str(0, ", "), L(t).Select(Values.ToStr)));
            p.Method(l, "contains", "item", "True when the list contains the item.", KType.Bool, (t, c) => L(t).Any(x => Values.LooseEquals(x, c.Arg(0))));
            p.Method(l, "index_of", "item", "The index of an item, or -1.", KType.Number, (t, c) => (double)L(t).FindIndex(x => Values.LooseEquals(x, c.Arg(0))));
            p.Method(l, "slice", "start: number, end?: number", "The items from start up to (not including) end; negative positions count from the end.", l, (t, c) =>
            {
                var list = L(t);
                int start = Clamp(c.Int(0), list.Count);
                int end = Math.Max(start, Clamp(c.Int(1, list.Count), list.Count));
                return list.GetRange(start, end - start);
            });
            p.Method(l, "take", "count: number", "The first count items.", l, (t, c) => L(t).Take(c.Int(0)).ToList());
            p.Method(l, "skip", "count: number", "All but the first count items.", l, (t, c) => L(t).Skip(c.Int(0)).ToList());
            p.Method(l, "unique", "", "The list without duplicates, using strict equality (===).", l, (t, c) => L(t).Aggregate(new List<object>(), (acc, x) => { if (!acc.Any(y => Values.StrictEquals(x, y))) acc.Add(x); return acc; }));
            p.Method(l, "push", "items...", "A new list with items added to the end.", l, (t, c) => L(t).Concat(c.Args).ToList());
            p.Method(l, "prepend", "items...", "A new list with items added to the start.", l, (t, c) => c.Args.Concat(L(t)).ToList());
            p.Method(l, "insert", "index: number, item", "A new list with item inserted at index.", l, (t, c) =>
            {
                var copy = L(t).ToList();
                copy.Insert(Clamp(c.Int(0), copy.Count), c.Arg(1));
                return copy;
            });
            p.Method(l, "pop", "", "A new list without its last item.", l, (t, c) => L(t).Take(Math.Max(0, L(t).Count - 1)).ToList());
            p.Method(l, "remove", "item", "A new list without any items equal to item.", l, (t, c) => L(t).Where(x => !Values.LooseEquals(x, c.Arg(0))).ToList());
            p.Method(l, "remove_at", "index: number", "A new list without the item at index.", l, (t, c) =>
            {
                var copy = L(t).ToList();
                int i = c.Int(0);
                if (i < 0) i += copy.Count;
                if (i >= 0 && i < copy.Count) copy.RemoveAt(i);
                return copy;
            });
            p.Method(l, "flatten", "", "Lists inside the list are spread into it.", l, (t, c) => L(t).SelectMany(x => x is List<object> inner ? inner : new List<object> { x }).ToList());
            p.Method(l, "chunk", "size: number", "The items in lists of size.", l, (t, c) =>
            {
                int size = Math.Max(1, c.Int(0));
                return L(t).Select((x, i) => (x, i)).GroupBy(e => e.i / size).Select(g => (object)g.Select(e => e.x).ToList()).ToList();
            });
            p.Method(l, "zip", "other", "Pairs of items from both lists: [[a1, b1], [a2, b2]...].", l, (t, c) => L(t).Zip(c.List(0), (a, b) => (object)new List<object> { a, b }).ToList());
            p.Method(l, "to_map", "fn", "A map with fn(item) as each item's key.", KType.Map, (t, c) =>
            {
                var map = new Dictionary<string, object>();
                foreach (object item in L(t)) map[Values.ToStr(c.Call(c.Fn(0), item))] = item;
                return map;
            });
        }

        private static object Pluck(object target, CallContext call)
        {
            string Key(object value) => value switch
            {
                string name => name,
                BareWord { Negated: false } word => word.Name,
                _ => throw call.Error($"->pluck() needs a field name or list of field names, but got {Values.Describe(value)}.", "Argument"),
            };

            object selector = call.Need(0);
            if (selector is List<object> list)
            {
                var keys = list.Select(Key).Distinct(StringComparer.Ordinal).ToList();
                return L(target).Select(item => (object)keys.ToDictionary(key => key,
                    key => call.Interpreter.GetMember(item, key, call.Module), StringComparer.Ordinal)).ToList();
            }
            string key = Key(selector);
            return L(target).Select(item => call.Interpreter.GetMember(item, key, call.Module)).ToList();
        }

        private static object ExtremeBy(object t, CallContext c, bool max)
        {
            var list = L(t);
            if (list.Count == 0) return null;
            ICallable f = c.Fn(0);
            object best = list[0];
            object bestKey = c.Call(f, best);
            foreach (object item in list.Skip(1))
            {
                object k = c.Call(f, item);
                int cmp = Values.Compare(k, bestKey, c.Name);
                if (max ? cmp > 0 : cmp < 0) { best = item; bestKey = k; }
            }
            return best;
        }

        private static void RegisterMapMethods(PackageBuilder p)
        {
            string m = KType.Map;
            p.Method(m, "keys", "", "The keys, as a list.", KType.List, (t, c) => M(t).Keys.Cast<object>().ToList());
            p.Method(m, "values", "", "The values, as a list.", KType.List, (t, c) => M(t).Values.ToList());
            p.Method(m, "entries", "", "The entries, as a list of { key, value } maps.", KType.List, (t, c) =>
                M(t).Select(kv => (object)new Dictionary<string, object> { ["key"] = kv.Key, ["value"] = kv.Value }).ToList());
            p.Method(m, "has", "key", "True when the key exists.", KType.Bool, (t, c) => M(t).ContainsKey(c.Str(0)));
            p.Method(m, "get", "key, default?", "The value for a key, or the default.", KType.Any, (t, c) => M(t).TryGetValue(c.Str(0), out object v) ? v : c.Arg(1));
            p.Method(m, "set", "key, value", "A new map with the key set.", m, (t, c) => new Dictionary<string, object>(M(t)) { [c.Str(0)] = c.Arg(1) });
            p.Method(m, "remove", "key", "A new map without the key.", m, (t, c) => { var copy = new Dictionary<string, object>(M(t)); copy.Remove(c.Str(0)); return copy; });
            p.Method(m, "merge", "other", "A new map with other's keys added or replaced.", m, (t, c) =>
            {
                var copy = new Dictionary<string, object>(M(t));
                foreach (var kv in c.Map(0)) copy[kv.Key] = kv.Value;
                return copy;
            });
            p.Method(m, "count", "", "The number of keys.", KType.Number, (t, c) => (double)M(t).Count);
            p.Method(m, "is_empty", "", "True when the map has no keys.", KType.Bool, (t, c) => M(t).Count == 0);
            p.Method(m, "map", "fn", "A new map with fn(value, key) applied to each value.", m, (t, c) => M(t).ToDictionary(kv => kv.Key, kv => c.Call(c.Fn(0), kv.Value, kv.Key)));
            p.Method(m, "filter", "fn", "The entries for which fn(value, key) is true.", m, (t, c) => M(t).Where(kv => Values.Truthy(c.Call(c.Fn(0), kv.Value, kv.Key))).ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        private static void RegisterNumberMethods(PackageBuilder p)
        {
            string n = KType.Number;
            p.Method(n, "round", "digits?: number", "Rounded to a number of decimal places (0 by default).", n, (t, c) => Math.Round(N(t), Math.Clamp(c.Int(0, 0), 0, 15), MidpointRounding.AwayFromZero));
            p.Method(n, "floor", "", "Rounded down.", n, (t, c) => Math.Floor(N(t)));
            p.Method(n, "ceil", "", "Rounded up.", n, (t, c) => Math.Ceiling(N(t)));
            p.Method(n, "abs", "", "Without its sign.", n, (t, c) => Math.Abs(N(t)));
            p.Method(n, "min", "other: number", "The smaller of the two.", n, (t, c) => Math.Min(N(t), c.Num(0)));
            p.Method(n, "max", "other: number", "The larger of the two.", n, (t, c) => Math.Max(N(t), c.Num(0)));
            p.Method(n, "clamp", "min: number, max: number", "The number limited to min..max.", n, (t, c) => Math.Clamp(N(t), c.Num(0), c.Num(1)));
            p.Method(n, "between", "min: number, max: number", "True when min ≤ the number ≤ max.", KType.Bool, (t, c) => N(t) >= c.Num(0) && N(t) <= c.Num(1));
            p.Method(n, "to_number", "default?", "The number itself.", n, (t, c) => N(t));
            p.Method(n, "to_int", "", "The whole-number part.", n, (t, c) => Math.Truncate(N(t)));
            p.Method(n, "format", "pattern?", "Formats the number (.NET format, N0 by default: 1,234).", KType.String, (t, c) => N(t).ToString(c.Str(0, "N0"), CultureInfo.InvariantCulture));
            p.Method(n, "increment", "", "The number plus 1.", n, (t, c) => N(t) + 1);
            p.Method(n, "decrement", "", "The number minus 1.", n, (t, c) => N(t) - 1);
            p.Method(n, "is_even", "", "True when the number is even.", KType.Bool, (t, c) => N(t) % 2 == 0);
            p.Method(n, "is_odd", "", "True when the number is odd.", KType.Bool, (t, c) => Math.Abs(N(t) % 2) == 1);
            p.Method(n, "times", "fn", "Calls fn(i) for i from 0 up to the number, and returns the results.", KType.List, (t, c) =>
                Enumerable.Range(0, Math.Max(0, (int)N(t))).Select(i => c.Call(c.Fn(0), (double)i)).ToList());
        }

        private static void RegisterDateMethods(PackageBuilder p)
        {
            string d = KType.Date;
            p.Method(d, "format", "pattern?: date_format", "Writes the date the way a date_format says: ->format(dd-MM-yyyy), ->format('d MMMM yyyy') or ->format('HH:mm'). yyyy-MM-dd by default.", KType.String, (t, c) =>
                D(t).ToString(c.Has(0) && c.Arg(0) != null ? DateFormatOf(c.Arg(0)) : "yyyy-MM-dd", CultureInfo.InvariantCulture));
            p.Field(d, "year", KType.Number, "The year.", (t, _) => (double)D(t).Year);
            p.Field(d, "month", KType.Number, "The month, 1 to 12.", (t, _) => (double)D(t).Month);
            p.Field(d, "day", KType.Number, "The day of the month.", (t, _) => (double)D(t).Day);
            p.Field(d, "hour", KType.Number, "The hour, 0 to 23.", (t, _) => (double)D(t).Hour);
            p.Field(d, "minute", KType.Number, "The minute.", (t, _) => (double)D(t).Minute);
            p.Field(d, "second", KType.Number, "The second.", (t, _) => (double)D(t).Second);
            p.Field(d, "weekday", KType.String, "The day of the week, such as Monday.", (t, _) => D(t).DayOfWeek.ToString());
            p.Method(d, "add_days", "days: number", "The date plus a number of days (negative to go back).", d, (t, c) => D(t).AddDays(c.Num(0)));
            p.Method(d, "add_hours", "hours: number", "The date plus a number of hours.", d, (t, c) => D(t).AddHours(c.Num(0)));
            p.Method(d, "add_minutes", "minutes: number", "The date plus a number of minutes.", d, (t, c) => D(t).AddMinutes(c.Num(0)));
            p.Method(d, "add_months", "months: number", "The date plus a number of months.", d, (t, c) => D(t).AddMonths(c.Int(0)));
            p.Method(d, "date", "", "The date at midnight.", d, (t, c) => D(t).Date);
            p.Method(d, "iso", "", "The date in ISO 8601 format.", KType.String, (t, c) => D(t).ToString("o", CultureInfo.InvariantCulture));
            p.Method(d, "timestamp", "", "Seconds since 1970-01-01 UTC.", KType.Number, (t, c) => (double)new DateTimeOffset(D(t)).ToUnixTimeSeconds());
        }

        private static void RegisterStringMethods(PackageBuilder p)
        {
            string s = KType.String;
            p.Method(s, "upper", "", "The text in upper case.", s, (t, c) => S(t).ToUpperInvariant());
            p.Method(s, "lower", "", "The text in lower case.", s, (t, c) => S(t).ToLowerInvariant());
            p.Method(s, "trim", "characters?", "The text without spaces (or the given characters) at either end.", s, (t, c) => c.Has(0) ? S(t).Trim(c.Str(0).ToCharArray()) : S(t).Trim());
            p.Method(s, "trim_start", "characters?", "The text without spaces (or the given characters) at the start.", s, (t, c) => c.Has(0) ? S(t).TrimStart(c.Str(0).ToCharArray()) : S(t).TrimStart());
            p.Method(s, "trim_end", "characters?", "The text without spaces (or the given characters) at the end.", s, (t, c) => c.Has(0) ? S(t).TrimEnd(c.Str(0).ToCharArray()) : S(t).TrimEnd());
            p.Method(s, "count", "", "The number of characters.", KType.Number, (t, c) =>
                c.Has(0) ? throw c.Error("->count() takes no arguments. To count how often text occurs, use ->count_of(text).", "Argument") : (double)S(t).Length);
            p.Method(s, "count_of", "match", "How many times a piece of text occurs.", KType.Number, (t, c) =>
                (double)(c.Str(0).Length == 0 ? 0 : (S(t).Length - S(t).Replace(c.Str(0), string.Empty, StringComparison.Ordinal).Length) / c.Str(0).Length));
            p.Method(s, "is_empty", "", "True when the text has zero characters.", KType.Bool, (t, c) => S(t).Length == 0);
            p.Method(s, "is_blank", "", "True when the text is empty or contains only whitespace.", KType.Bool, (t, c) => string.IsNullOrWhiteSpace(S(t)));
            p.Method(s, "contains", "text", "True when the text contains this text.", KType.Bool, (t, c) => S(t).Contains(c.Str(0), StringComparison.Ordinal));
            p.Method(s, "starts_with", "text", "True when the text starts with this text.", KType.Bool, (t, c) => S(t).StartsWith(c.Str(0), StringComparison.Ordinal));
            p.Method(s, "ends_with", "text", "True when the text ends with this text.", KType.Bool, (t, c) => S(t).EndsWith(c.Str(0), StringComparison.Ordinal));
            p.Method(s, "replace", "old, new", "The text with every old replaced by new. old can be a regex: ->replace(/\\s+/, ' ').", s, (t, c) =>
                c.Arg(0) is RegexValue rx ? RegexValue.Run(rx, r => r.Replace(S(t), c.Str(1)))
                : c.Str(0).Length == 0 ? S(t) : S(t).Replace(c.Str(0), c.Str(1), StringComparison.Ordinal));
            p.Method(s, "split", "separator?", "The parts between each separator (or between spaces), as a list. The separator can be a regex: ->split(/\\s*,\\s*/).", KType.List, (t, c) =>
                (c.Arg(0) is RegexValue rx ? RegexValue.Run(rx, r => r.Split(S(t)))
                : c.Has(0) ? S(t).Split(c.Str(0)) : S(t).Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).Cast<object>().ToList());
            p.Method(s, "lines", "", "The lines of the text, as a list.", KType.List, (t, c) => S(t).Replace("\r\n", "\n").Split('\n').Cast<object>().ToList());
            p.Method(s, "chars", "", "The characters, as a list.", KType.List, (t, c) => S(t).Select(ch => (object)ch.ToString()).ToList());
            p.Method(s, "index_of", "text", "Where text first occurs, or -1.", KType.Number, (t, c) => (double)S(t).IndexOf(c.Str(0), StringComparison.Ordinal));
            p.Method(s, "repeat", "count: number", "The text repeated count times.", s, (t, c) => string.Concat(Enumerable.Repeat(S(t), Math.Max(0, c.Int(0)))));
            p.Method(s, "reverse", "", "The text backwards.", s, (t, c) => { char[] a = S(t).ToCharArray(); Array.Reverse(a); return new string(a); });
            p.Method(s, "first", "", "The first character.", s, (t, c) => S(t).Length > 0 ? S(t).Substring(0, 1) : string.Empty);
            p.Method(s, "last", "", "The last character.", s, (t, c) => S(t).Length > 0 ? S(t).Substring(S(t).Length - 1) : string.Empty);
            p.Method(s, "slice", "start: number, end?: number", "Part of the text; negative positions count from the end.", s, (t, c) =>
            {
                int start = Clamp(c.Int(0), S(t).Length);
                int end = Math.Max(start, Clamp(c.Int(1, S(t).Length), S(t).Length));
                return S(t).Substring(start, end - start);
            });
            p.Method(s, "truncate", "length: number, suffix?", "Shortens the text to length, adding suffix (… by default).", s, (t, c) =>
            {
                int max = c.Int(0);
                return S(t).Length <= max ? S(t) : S(t).Substring(0, Math.Max(0, max)).TrimEnd() + c.Str(1, "…");
            });
            p.Method(s, "pad_left", "width: number, char?", "The text padded on the left to width.", s, (t, c) => S(t).PadLeft(c.Int(0), c.Str(1, " ").FirstOrDefault(' ')));
            p.Method(s, "pad_right", "width: number, char?", "The text padded on the right to width.", s, (t, c) => S(t).PadRight(c.Int(0), c.Str(1, " ").FirstOrDefault(' ')));
            p.Method(s, "capitalize", "", "The text with its first letter upper-cased.", s, (t, c) => S(t).Length == 0 ? S(t) : char.ToUpperInvariant(S(t)[0]) + S(t).Substring(1));
            p.Method(s, "title", "", "The text in Title Case.", s, (t, c) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(S(t).ToLowerInvariant()));
            p.Method(s, "escape", "", "The text HTML-escaped.", s, (t, c) => Values.HtmlEscape(S(t)));
            p.Method(s, "url_encode", "", "The text escaped for a URL.", s, (t, c) => Uri.EscapeDataString(S(t)));
            p.Method(s, "url_decode", "", "URL-escaped text decoded.", s, (t, c) => Uri.UnescapeDataString(S(t)));
            p.Method(s, "json", "as?: type", "Parses the text as JSON. With as: a <type> (or 'Repo[]'), the data is checked and turned into records.", KType.Any, (t, c) => Shapes.MapJson(c, Values.ParseJson(S(t)), 0));
            p.Method(s, "yaml", "", "Parses the text as YAML.", KType.Any, (t, c) => Yaml.Parse(S(t)));
            foreach (var f in RecordType.ValueFields)
                p.Field(KType.Type, f.Name, f.Type, f.Description, (t, _) => ((IKObject)t).TryGetMember(f.Name, out object v)
                    ? v : throw new KScriptException($"{((IKObject)t).ToText()} is made from another type, so it has no .{f.Name}. It has ->check() and .name.", "Name"));
            foreach (var m in RecordType.Methods)
                p.Method(KType.Type, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((IKObject)t).TryCallMethod(c, m.Name, out object r)
                    ? r : throw c.Error($"{((IKObject)t).ToText()} is made from another type, so it has no ->{m.Name}(). It has ->check() and .name.", "Name"));
            p.Method(s, "to_number", "default?", "The text as a number, or default when it isn't one.", KType.Number, (t, c) =>
                Values.TryNumber(S(t), out double d) ? d : c.Has(0) ? c.Arg(0) : throw c.Error($"'{S(t)}' is not a number. Pass a default: ->to_number(0).", "Type"));
            p.Method(s, "to_int", "default?", "The text as a whole number, or default when it isn't one.", KType.Number, (t, c) =>
                Values.TryNumber(S(t), out double d) ? Math.Truncate(d) : c.Has(0) ? c.Arg(0) : throw c.Error($"'{S(t)}' is not a number. Pass a default: ->to_int(0).", "Type"));
            p.Method(s, "to_bool", "", "Text such as 'true', 'false', 'yes', 'no', '1' or '0' as a bool.", KType.Bool, (t, c) =>
                Values.ParseBool(S(t)) ?? throw c.Error($"'{S(t)}' is not a true/false value.", "Type"));
            p.Method(s, "to_date", "format?: date_format", "Reads the text as a date: '2026-11-22'->to_date(), or with the way it is written, '22-11-2026'->to_date(dd-MM-yyyy).", KType.Date, (t, c) =>
            {
                if (c.Has(0) && c.Arg(0) != null)
                {
                    string format = DateFormatOf(c.Arg(0));
                    return DateTime.TryParseExact(S(t).Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime exact)
                        ? exact : throw c.Error($"'{S(t)}' isn't a date written {Values.ToStr(c.Arg(0))}.", "Format");
                }
                return DateTime.TryParse(S(t), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dt) ? dt : throw c.Error($"'{S(t)}' is not a date.", "Format");
            });
            p.Method(s, "to_base64", "url?: bool", "The text encoded as base64. With url: true, it is URL-safe base64 without padding.", "base64", (t, c) =>
            {
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(S(t)));
                if (Values.Truthy(c.Arg(0, "url"))) b64 = b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
                return Native("base64", b64);
            });
            p.Method(s, "from_base64", "", "Base64 text decoded.", s, (t, c) => Encoding.UTF8.GetString(Convert.FromBase64String(S(t))));
            p.Method(s, "md5", "", "The MD5 hash of the text, in hex.", s, (t, c) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(S(t)))).ToLowerInvariant());
            p.Method(s, "sha256", "", "The SHA-256 hash of the text, in hex.", s, (t, c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(S(t)))).ToLowerInvariant());
            // pattern is a regex (/^[a-z]+$/i, flags and all) or text holding one; either way a slow match stops after a second.
            p.Method(s, "matches", "pattern: regex", "True when the text matches a regular expression: ->matches(/^[a-z]+$/i).", KType.Bool, (t, c) =>
                RegexValue.Run(RegexValue.From(c.Arg(0), "->matches()"), r => r.IsMatch(S(t))));
            p.Method(s, "find_all", "pattern: regex", "Every match of a regular expression, as a list.", KType.List, (t, c) =>
                RegexValue.Run(RegexValue.From(c.Arg(0), "->find_all()"), r => r.Matches(S(t)).Select(m => (object)m.Value).ToList()));
            p.Method(s, "replace_pattern", "pattern: regex, replacement", "The text with regular expression matches replaced ($1 or ${name} for groups).", s, (t, c) =>
                RegexValue.Run(RegexValue.From(c.Arg(0), "->replace_pattern()"), r => r.Replace(S(t), c.Str(1))));
        }
    }
}
