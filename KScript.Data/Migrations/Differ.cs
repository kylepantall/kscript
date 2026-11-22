namespace KScript.Data.Migrations
{
    /// <summary>
    /// The steps from the schema the migrations make to the one the models want, for <c>kscript db diff</c>. A table
    /// or column that went and another that came may be a rename: <c>rename</c> is asked, and says no when it is null.
    /// </summary>
    internal static class Differ
    {
        /// <summary>Asks whether a table or column was renamed: ("table", null, "people", "users") or ("column", "users", "nick", "handle").</summary>
        internal delegate bool RenameQuestion(string kind, string table, string from, string to);

        public static List<Step> Diff(Schema current, Schema target, RenameQuestion rename = null)
        {
            var dropIndexes = new List<Step>();
            var renameTables = new List<Step>();
            var createTables = new List<TableSpec>();
            var columns = new List<Step>();
            var dropColumns = new List<Step>();
            var addIndexes = new List<Step>();
            var dropTables = new List<TableSpec>();

            // Tables: renames first, so their columns are compared with the table's new name.
            var gone = current.Tables.Where(t => target.Table(t.Name) == null).ToList();
            var added = target.Tables.Where(t => current.Table(t.Name) == null).ToList();
            var renamedTo = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TableSpec old in gone.ToList())
            {
                TableSpec now = rename == null ? null : added.FirstOrDefault(t => rename("table", null, old.Name, t.Name));
                if (now == null) continue;
                renameTables.Add(new RenameTableStep(old.Name, now.Name));
                renamedTo[old.Name] = now.Name;
                gone.Remove(old);
                added.Remove(now);
            }
            createTables.AddRange(added);
            dropTables.AddRange(gone);

            foreach (TableSpec before in current.Tables.Where(t => !gone.Contains(t)))
            {
                string name = renamedTo.GetValueOrDefault(before.Name) ?? before.Name;
                TableSpec after = target.Table(name);
                // The columns, as they are once renamed tables' foreign keys follow them.
                TableSpec was = Retargeted(before, renamedTo);

                var oldOnly = was.Columns.Where(c => after.Column(c.Name) == null).ToList();
                var newOnly = after.Columns.Where(c => was.Column(c.Name) == null).ToList();
                var renamedColumns = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (ColumnSpec old in oldOnly.ToList())
                {
                    ColumnSpec now = rename == null ? null : newOnly.FirstOrDefault(c => c.Kind == old.Kind && rename("column", name, old.Name, c.Name));
                    if (now == null) continue;
                    columns.Add(new RenameColumnStep(name, old.Name, now.Name));
                    renamedColumns[old.Name] = now.Name;
                    oldOnly.Remove(old);
                    newOnly.Remove(now);
                    if (!old.With(name: now.Name).SameAs(now)) columns.Add(new ChangeColumnStep(name, now));
                }

                // Indexes that go are dropped before their columns; new ones are added once their columns are there.
                foreach (TableIndex index in was.Indexes)
                {
                    List<string> cols = index.Columns.Select(c => renamedColumns.GetValueOrDefault(c) ?? c).ToList();
                    if (cols.Any(c => oldOnly.Any(o => o.Name == c))) continue;
                    // Dropped first, before any renames, so with the names it has now.
                    if (after.IndexOf(cols, index.Unique) == null) dropIndexes.Add(new DropIndexStep(before.Name, index.Columns, index.Unique));
                }
                foreach (TableIndex index in after.Indexes)
                {
                    IReadOnlyList<string> cols = index.Columns.Select(c => renamedColumns.FirstOrDefault(r => r.Value == c).Key ?? c).ToList();
                    if (was.IndexOf(cols, index.Unique) == null) addIndexes.Add(new AddIndexStep(name, index.Columns, index.Unique));
                }

                foreach (ColumnSpec c in newOnly) columns.Add(new AddColumnStep(name, c));
                foreach (ColumnSpec now in after.Columns)
                {
                    ColumnSpec old = was.Column(now.Name);
                    if (old != null && !old.SameAs(now)) columns.Add(new ChangeColumnStep(name, now));
                }
                foreach (ColumnSpec c in oldOnly) dropColumns.Add(new DropColumnStep(name, c.Name));
            }

            var steps = new List<Step>();
            steps.AddRange(dropIndexes);
            steps.AddRange(renameTables);
            steps.AddRange(ByParents(createTables).Select(t => new CreateTableStep(t)));
            steps.AddRange(columns);
            steps.AddRange(dropColumns);
            steps.AddRange(addIndexes);
            // Tables that others point to go last.
            steps.AddRange(ByParents(dropTables).AsEnumerable().Reverse().Select(t => new DropTableStep(t.Name)));
            return steps;
        }

        private static TableSpec Retargeted(TableSpec t, Dictionary<string, string> renamedTo)
        {
            if (renamedTo.Count == 0) return t;
            TableSpec copy = t.Clone();
            for (int i = 0; i < copy.Columns.Count; i++)
            {
                ColumnSpec c = copy.Columns[i];
                if (c.References != null && renamedTo.TryGetValue(c.References, out string to)) copy.Columns[i] = RenameTableStep.Retarget(c, to, c.ReferencesColumn);
            }
            return copy;
        }

        /// <summary>Tables in an order where each comes after the ones (among them) it points to.</summary>
        private static List<TableSpec> ByParents(List<TableSpec> tables)
        {
            var ordered = new List<TableSpec>();
            var left = tables.ToList();
            while (left.Count > 0)
            {
                TableSpec next = left.FirstOrDefault(t => !t.Parents.Any(p => left.Any(o => o.Name == p))) ?? left[0];
                ordered.Add(next);
                left.Remove(next);
            }
            return ordered;
        }

        /// <summary>A name for a migration from its steps: create_posts, update_users, or update_schema.</summary>
        public static string NameFor(IReadOnlyList<Step> steps)
        {
            var creates = steps.OfType<CreateTableStep>().ToList();
            if (creates.Count == steps.Count) return "create_" + string.Join("_", creates.Take(3).Select(c => c.Table.Name)) + (creates.Count > 3 ? "_and_more" : string.Empty);
            var tables = steps.Select(TableOf).Distinct().ToList();
            if (tables.Count == 1 && tables[0] != null) return "update_" + tables[0];
            return "update_schema";
        }

        private static string TableOf(Step s) => s switch
        {
            CreateTableStep c => c.Table.Name,
            DropTableStep d => d.Name,
            RenameTableStep r => r.To,
            AddColumnStep a => a.Table,
            DropColumnStep d => d.Table,
            RenameColumnStep r => r.Table,
            ChangeColumnStep c => c.Table,
            AddIndexStep a => a.Table,
            DropIndexStep d => d.Table,
            _ => null,
        };
    }
}
