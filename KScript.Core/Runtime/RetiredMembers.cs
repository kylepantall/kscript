using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// Methods that were renamed so the standard packages follow one set of conventions (docs/contributing.md), with
    /// what to write instead. Nothing here still works: the runtime stops with the message, and the editor shows it
    /// with a quick fix where the change is a plain rename. Methods that became fields (<c>->status()</c> to
    /// <c>.status</c>) are not listed: the field itself is found and named in the error.
    /// </summary>
    public static class RetiredMembers
    {
        /// <summary>
        /// By "receiver:name", where receiver is the type's name as ->type() gives it. A rename that starts with a dot
        /// is a field: <c>->ok()</c> became <c>.is_ok</c>.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, RetiredInfo> Methods = Table(
            // Bool names are is_x or has_x.
            ("password", "strong", "Use ->is_strong().", "is_strong"),
            ("logger", "enabled", "Use ->is_enabled(level).", "is_enabled"),
            ("timezone", "dst", "Use ->is_dst().", "is_dst"),
            ("ip", "private", "Read the field .is_private (no brackets).", ".is_private"),
            ("ip", "loopback", "Read the field .is_loopback (no brackets).", ".is_loopback"),
            ("jwt", "expired", "Read the field .is_expired (no brackets).", ".is_expired"),
            ("response", "ok", "Read the field .is_ok (no brackets).", ".is_ok"),
            ("http_response", "ok", "Read the field .is_ok (no brackets).", ".is_ok"),
            ("database", "in_transaction", "Read the field .is_in_transaction (no brackets).", ".is_in_transaction"),
            // A name is a field or a method of a type, never both.
            ("storage", "path", "Use ->path_of(name) for a file in the folder, or read the field .path for the folder itself.", null),
            // Keyed things use get, set, has and remove; clear empties them.
            ("cache", "put", "Use ->set(key, value).", "set"),
            ("cache", "forget", "Use ->remove(key).", "remove"),
            ("cache", "flush", "Use ->clear().", "clear"),
            ("session", "put", "Use ->set(key, value).", "set"),
            ("session", "forget", "Use ->remove(key).", "remove"),
            // One name per thing: these were a second name for another method, or for an operator.
            ("list", "where", "Use ->filter(fn).", "filter"),
            ("list", "append", "Use ->push(items).", "push"),
            ("list", "length", "Use ->count().", "count"),
            ("map", "length", "Use ->count().", "count"),
            ("string", "length", "Use ->count().", "count"),
            ("json", "parse", "Use ->json().", "json"),
            ("number", "pow", "Use the ** operator: $n ** 2.", null),
            ("color", "contrast", "Use ->contrast_ratio(other).", "contrast_ratio"),
            // One word for the mean everywhere: $list->average(), @average() and a query's ->average(column).
            ("query", "avg", "Use ->average(column).", "average"),
            ("model", "avg", "Use ->average(column).", "average"));

        /// <summary>The retired methods called <paramref name="name"/>, on any type.</summary>
        public static IEnumerable<RetiredInfo> Named(string name) =>
            Methods.Where(m => m.Key.EndsWith(":" + name, StringComparison.Ordinal)).Select(m => m.Value);

        /// <summary>The retired method called <paramref name="name"/> on values of <paramref name="receiver"/>, or null.</summary>
        public static RetiredInfo Method(string receiver, string name) =>
            receiver != null && name != null && Methods.TryGetValue(receiver + ":" + name, out RetiredInfo info) ? info : null;

        // A plain loop: every package load reads this table (Conventions), and LINQ over tuples is slow to start.
        private static IReadOnlyDictionary<string, RetiredInfo> Table(params (string Receiver, string Name, string Replacement, string Rename)[] rows)
        {
            var table = new Dictionary<string, RetiredInfo>(rows.Length, StringComparer.Ordinal);
            foreach (var r in rows) table.Add(r.Receiver + ":" + r.Name, new RetiredInfo(r.Name, r.Replacement, r.Rename));
            return table;
        }
    }
}
