namespace KScript.Runtime
{
    /// <summary>
    /// The arguments every table takes, read one way: <c>table</c> (KScript.Terminal) and <c>Table</c>
    /// (KScript.Document) both call this, so the same <c>rows</c>, <c>columns</c>, <c>align</c> and <c>header</c>
    /// work in a terminal and in a document.
    /// <list type="bullet">
    /// <item><c>rows</c>: a list of maps (each a row) or a list of lists.</item>
    /// <item><c>columns</c>: a list of keys <c>['name', 'age']</c>, or a map from heading to key
    /// <c>{ Name: 'name', Age: 'age' }</c>. Without it, maps show every key of the first row, and the first of a
    /// list of lists is the heading row.</item>
    /// <item><c>align</c>: a word for each column: <c>['left', 'right', 'center']</c>.</item>
    /// <item><c>header: false</c>: no heading row.</item>
    /// </list>
    /// </summary>
    public sealed class TableArguments
    {
        /// <summary>The argument names tables share.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "rows", "columns", "align", "header" };

        /// <summary>The words <c>align</c> takes.</summary>
        public static readonly IReadOnlyList<string> AlignWords = new[] { "left", "center", "right" };

        private TableArguments()
        {
        }

        /// <summary>The heading of each column, or null when there is no heading row.</summary>
        public IReadOnlyList<string> Headings { get; private set; }

        /// <summary>The cells of each row, in column order.</summary>
        public List<List<object>> Cells { get; } = new List<List<object>>();

        /// <summary>left, center or right for each column that <c>align</c> names; empty without it.</summary>
        public List<string> Align { get; } = new List<string>();

        /// <summary>True when <c>columns</c> or <c>align</c> said how the columns line up, so numbers aren't lined up by guessing.</summary>
        public bool AlignGiven => Align.Count > 0;

        /// <summary>How many columns the widest row has.</summary>
        public int ColumnCount => Math.Max(Headings?.Count ?? 0, Cells.Count == 0 ? 0 : Cells.Max(r => r.Count));

        /// <summary>
        /// Reads the arguments. <paramref name="fail"/> makes the error for a wrong one, given the message and the
        /// argument's name, so each element can point at its own argument.
        /// </summary>
        public static TableArguments Read(object rows, object columns, object align, bool header, Func<string, string, Exception> fail)
        {
            if (Values.Unwrap(rows) is not List<object> list)
                throw fail($"rows should be a list of rows: maps such as {{ name: 'Ada', age: 36 }}, or lists, not {Values.Describe(rows)}.", "rows");

            List<(string Heading, string Key)> cols = Values.Unwrap(columns) switch
            {
                null => null,
                Dictionary<string, object> map => map.Select(kv => (kv.Key, Values.ToStr(kv.Value))).ToList(),
                List<object> keys => keys.Select(k => (Values.ToStr(k), Values.ToStr(k))).ToList(),
                _ => throw fail("columns should be a list of keys ['name', 'age'] or a map from heading to key { Name: 'name' }.", "columns"),
            };
            object firstRow = Values.Unwrap(list.FirstOrDefault());
            if (cols == null && firstRow is Dictionary<string, object> first) cols = first.Keys.Select(k => (k, k)).ToList();

            var table = new TableArguments();
            int skip = 0;
            if (header && cols != null)
            {
                table.Headings = cols.Select(c => c.Heading).ToList();
            }
            else if (header && firstRow is List<object> headingRow)
            {
                // A list of lists: its first row holds the headings.
                table.Headings = headingRow.Select(Values.ToStr).ToList();
                skip = 1;
            }
            foreach (object row in list.Skip(skip))
            {
                table.Cells.Add(Values.Unwrap(row) switch
                {
                    Dictionary<string, object> m when cols != null => cols.Select(c => Path(m, c.Key)).ToList(),
                    Dictionary<string, object> m => m.Values.ToList(),
                    List<object> l => l.ToList(),
                    _ => new List<object> { row },
                });
            }

            switch (Values.Unwrap(align))
            {
                case null:
                    break;
                case List<object> words:
                    foreach (object word in words)
                    {
                        string w = Values.ToStr(word).Trim().ToLowerInvariant();
                        if (w == "centre") w = "center";
                        if (!AlignWords.Contains(w)) throw fail($"align: '{Values.ToStr(word)}' isn't a column alignment. Use left, center or right for each column, such as ['left', 'right'].", "align");
                        table.Align.Add(w);
                    }
                    break;
                default:
                    throw fail($"align should be a list with left, center or right for each column, such as ['left', 'right'], not {Values.Describe(align)}.", "align");
            }
            return table;
        }

        /// <summary>
        /// How column <paramref name="index"/> lines up: what <c>align</c> says, otherwise right when every cell in it
        /// that has a value is a number, and left for anything else.
        /// </summary>
        public string AlignOf(int index)
        {
            if (index < Align.Count) return Align[index];
            bool any = false;
            foreach (List<object> row in Cells)
            {
                object cell = index < row.Count ? row[index] : null;
                if (cell == null) continue;
                if (Values.Unwrap(cell) is not double) return "left";
                any = true;
            }
            return any ? "right" : "left";
        }

        /// <summary>A value from a map by key, or by a dotted path such as address.city.</summary>
        private static object Path(Dictionary<string, object> map, string key)
        {
            if (map.TryGetValue(key, out object v)) return v;
            object at = map;
            foreach (string part in key.Split('.'))
            {
                if (Values.Unwrap(at) is Dictionary<string, object> m && m.TryGetValue(part, out object next)) at = next;
                else return null;
            }
            return at;
        }
    }
}
