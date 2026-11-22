using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>->only(what)</c> and <c>->without(what)</c> on any list, and <c>->is(type)</c> on any value. <c>what</c> is
    /// a type name (<c>$loads->only(semver)</c>, <c>->without(string)</c>) or a filter the items' native type declares
    /// (<c>$versions->only(releases)</c>), written bare.
    /// </summary>
    internal static class ListFilters
    {
        public static void Register(KScriptPackage.PackageBuilder p)
        {
            const string Let = "<let name=\"loads\" value=\"[1, 'two', 3]\" />\n";
            p.Method(KType.List, "only", "what: word", "The items of a type (->only(semver), ->only(number)) or that a filter of the items' type keeps (->only(releases) on a semver[]).", KType.List,
                (t, c) => Filter((List<object>)t, c.Str(0), keep: true, c));
            p.Method(KType.List, "without", "what: word", "The items that aren't of a type (->without(string)), or without the ones a filter of the items' type keeps (->without(prereleases)).", KType.List,
                (t, c) => Filter((List<object>)t, c.Str(0), keep: false, c));
            p.Method(KType.Any, "is_a", "type: word", "True when the value is of this type: $x->is_a(semver), $x->is_a(string).", KType.Bool,
                (t, c) => IsOfType(t, c.Str(0)));
            p.Examples(
                ("list->only", Let + "<echo>{ $loads->only(number) }</echo>"),
                ("list->without", Let + "<echo>{ $loads->without(string) }</echo>"),
                ("any->is_a", "<echo>{ 1h->is_a(duration) }</echo>"));
        }

        /// <summary>True when <paramref name="value"/> is a <paramref name="type"/>: its type's name, or a number for int when whole.</summary>
        public static bool IsOfType(object value, string type)
        {
            string name = Shapes.TypeName(value);
            if (string.Equals(name, type, StringComparison.OrdinalIgnoreCase)) return true;
            return type switch
            {
                "int" => value is double d && d == Math.Floor(d),
                "float" => value is double,
                "text" => value is string,
                _ => false,
            };
        }

        private static bool IsTypeName(string what) => Values.IsDeclarable(what) || what is "int" or "float" or "date" or "null" or "error";

        private static List<object> Filter(List<object> items, string what, bool keep, CallContext c)
        {
            if (IsTypeName(what)) return items.Where(x => IsOfType(x, what) == keep).ToList();
            NativeType type = items.Count > 0 && items.All(x => x is IKObject k && k.TypeName == ((IKObject)items[0]).TypeName) ? NativeTypeRegistry.Of(items[0]) : null;
            if (type?.ListFilter(what) is Func<object, bool> test) return items.Where(x => test(x) == keep).ToList();
            string filters = type != null && type.ListFilters.Count > 0
                ? $" The filters for a list of {type.Name} values are {string.Join(", ", type.ListFilters.Select(f => f.Name))}."
                : items.Count > 0 && NativeTypeRegistry.Of(items[0]) is NativeType { ListFilters.Count: > 0 } first ? $" Filters such as {string.Join(", ", first.ListFilters.Select(f => f.Name))} work when every item is {first.Info.WithArticle}." : string.Empty;
            throw c.Error($"'{what}' isn't a type or a filter of these items.{filters}", "Argument");
        }
    }
}
