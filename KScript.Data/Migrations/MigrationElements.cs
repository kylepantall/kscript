using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Data.Migrations
{
    /// <summary>What a migration file says: its steps, and the steps of its Down block if it has one.</summary>
    internal sealed class MigrationPlan
    {
        public const string HostKey = "KScript.Data.Migration";

        public List<Step> Up { get; } = new List<Step>();

        /// <summary>The steps that undo it, written by hand, or null to undo each step of <see cref="Up"/>.</summary>
        public List<Step> Down { get; set; }

        internal bool InDown { get; set; }

        internal TableSpec Table { get; set; }

        internal void Add(Step step) => (InDown ? Down : Up).Add(step);
    }

    /// <summary>
    /// The elements of a migration file: <c>KScript.Migration { CreateTable(name: posts) { Field(...) } }</c>. Running
    /// the file collects its steps; <c>kscript db migrate</c> works out their SQL and runs it.
    /// </summary>
    internal static class MigrationElements
    {
        private static readonly string[] TypeWords = ColumnSpec.TypeWords.Select(t => t.Word).ToArray();

        private static readonly string[] Steps = { "Migration", "Down" };

        private static A[] ColumnAttributes(bool table, string what) => new[]
        {
            table ? new A("table", K.Text, $"The table {what}.", true) { Literal = true } : null,
            new A("name", K.Text, "The column's name.", true) { Literal = true },
            new A("type", K.Enum, "string, int, number, bool, date, email, handle, uuid, password, json or enum (with values:). Each database stores it the way models do.", true, TypeWords),
            new A("max", K.Expression, "The most characters a string may have: varchar(max)."),
            new A("values", K.Text, "The words an enum column may hold: [member, admin].") { NameList = true },
            new A("optional", K.Bool, "Lets the column be empty (null)."),
            new A("default", K.Expression, "What the database fills in when a row doesn't give it: 'member', 0, true."),
            new A("key", K.Bool, "true makes it the table's key."),
            new A("auto", K.Bool, "true on an int key: the database numbers the rows itself."),
            new A("references", K.Text, "A foreign key: the table the column points to.") { Literal = true },
            new A("references_column", K.Text, "The column it points to there, the table's key by default.") { Literal = true },
            new A("on_delete", K.Enum, "What deleting the row it points to does: cascade (the default for a required column), set_null (the default for one that may be null) or restrict.", false, "cascade", "set_null", "restrict"),
        }.Where(a => a != null).ToArray();

        private static A[] IndexAttributes(bool table) => new[]
        {
            table ? new A("table", K.Text, "The table.", true) { Literal = true } : null,
            new A("columns", K.Text, "The columns, in order: [status, created_at].", true) { NameList = true },
            new A("unique", K.Bool, "true stops two rows from having the same values."),
        }.Where(a => a != null).ToArray();

        private static ElementInfo Step(string name, string description, ContentKind content, string example, params A[] attributes) =>
            new ElementInfo(name, description, content, attributes)
            {
                Context = ElementContext.Script,
                MustBeInside = Steps,
                Example = "<KScript.Migration>\n  " + example + "\n</KScript.Migration>",
            };

        public static void Register(KScriptPackage.PackageBuilder p)
        {
            p.Element(new ElementInfo("Migration", "A migration file, in database/migrations: the steps that change the tables, run in order by kscript db migrate and undone by kscript db rollback. kscript db diff writes them from the models.", ContentKind.Statements)
            {
                Context = ElementContext.Script,
                Example = "<KScript.Migration>\n  <AddField table=\"users\" name=\"bio\" type=\"string\" optional=\"true\" />\n</KScript.Migration>",
            }, run =>
            {
                Plan(run);
                run.RunChildren();
                return null;
            });

            p.Element(Step("CreateTable", "Makes a table, with a Field for each column, its Index lines and a Key of two columns if it has one.", ContentKind.Statements,
                "<CreateTable name=\"posts\">\n    <Field name=\"id\" type=\"int\" key=\"true\" auto=\"true\" />\n    <Field name=\"title\" type=\"string\" max=\"200\" />\n  </CreateTable>",
                new A("name", K.Text, "The table's name.", true) { Literal = true }), run =>
            {
                MigrationPlan plan = Plan(run);
                if (plan.Table != null) throw run.Error("A CreateTable can't be inside another.", null, "Migration");
                plan.Table = new TableSpec { Name = run.RequiredRaw("name").Trim() };
                try
                {
                    run.RunChildren();
                    plan.Add(new CreateTableStep(plan.Table));
                }
                finally
                {
                    plan.Table = null;
                }
                return null;
            });

            p.Element(new ElementInfo("Field", "A column of the table CreateTable makes.", ContentKind.Empty, ColumnAttributes(false, null))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "CreateTable" },
                Example = "<KScript.Migration>\n  <CreateTable name=\"users\">\n    <Field name=\"email\" type=\"email\" />\n  </CreateTable>\n</KScript.Migration>",
            }, run =>
            {
                InTable(run).Columns.Add(Column(run));
                return null;
            });

            p.Element(new ElementInfo("Index", "An index of the table CreateTable makes.", ContentKind.Empty, IndexAttributes(false))
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "CreateTable" },
                Example = "<KScript.Migration>\n  <CreateTable name=\"orders\">\n    <Index columns=\"status, created_at\" />\n  </CreateTable>\n</KScript.Migration>",
            }, run =>
            {
                List<string> columns = Names(run, "columns");
                InTable(run).Indexes.Add(new TableIndex(null, columns, run.Flag("unique")));
                return null;
            });

            p.Element(new ElementInfo("Key", "A key of two or more columns, for the table CreateTable makes: a pivot table's.", ContentKind.Empty,
                new A("columns", K.Text, "The columns: [post_id, tag_id].", true) { NameList = true })
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "CreateTable" },
                Example = "<KScript.Migration>\n  <CreateTable name=\"post_tag\">\n    <Key columns=\"post_id, tag_id\" />\n  </CreateTable>\n</KScript.Migration>",
            }, run =>
            {
                InTable(run).Key = Names(run, "columns");
                return null;
            });

            p.Element(Step("DropTable", "Drops a table and its rows. Rollback makes it again, empty.", ContentKind.Empty, "<DropTable name=\"sessions\" />",
                new A("name", K.Text, "The table.", true) { Literal = true }), run =>
            {
                Plan(run).Add(new DropTableStep(run.RequiredRaw("name").Trim()));
                return null;
            });

            p.Element(Step("RenameTable", "Renames a table. Foreign keys that point to it follow.", ContentKind.Empty, "<RenameTable from=\"people\" to=\"users\" />",
                new A("from", K.Text, "The table's name now.", true) { Literal = true },
                new A("to", K.Text, "Its new name.", true) { Literal = true }), run =>
            {
                Plan(run).Add(new RenameTableStep(run.RequiredRaw("from").Trim(), run.RequiredRaw("to").Trim()));
                return null;
            });

            p.Element(Step("AddField", "Adds a column to a table. A required column needs a default when the table has rows.", ContentKind.Empty,
                "<AddField table=\"users\" name=\"bio\" type=\"string\" optional=\"true\" />", ColumnAttributes(true, "to add it to")), run =>
            {
                Plan(run).Add(new AddColumnStep(run.RequiredRaw("table").Trim(), Column(run)));
                return null;
            });

            p.Element(Step("DropField", "Drops a column, with its indexes. Rollback adds it back, empty.", ContentKind.Empty, "<DropField table=\"users\" name=\"nickname\" />",
                new A("table", K.Text, "The table.", true) { Literal = true },
                new A("name", K.Text, "The column.", true) { Literal = true }), run =>
            {
                Plan(run).Add(new DropColumnStep(run.RequiredRaw("table").Trim(), run.RequiredRaw("name").Trim()));
                return null;
            });

            p.Element(Step("RenameField", "Renames a column, keeping its rows.", ContentKind.Empty, "<RenameField table=\"users\" from=\"nick\" to=\"handle\" />",
                new A("table", K.Text, "The table.", true) { Literal = true },
                new A("from", K.Text, "The column's name now.", true) { Literal = true },
                new A("to", K.Text, "Its new name.", true) { Literal = true }), run =>
            {
                Plan(run).Add(new RenameColumnStep(run.RequiredRaw("table").Trim(), run.RequiredRaw("from").Trim(), run.RequiredRaw("to").Trim()));
                return null;
            });

            p.Element(Step("ChangeField", "Changes a column to the one described: its type, whether it may be null, its default or what it references. The rows are kept.", ContentKind.Empty,
                "<ChangeField table=\"users\" name=\"bio\" type=\"string\" max=\"500\" optional=\"true\" />", ColumnAttributes(true, "the column is in")), run =>
            {
                Plan(run).Add(new ChangeColumnStep(run.RequiredRaw("table").Trim(), Column(run)));
                return null;
            });

            p.Element(Step("AddIndex", "Adds an index to a table.", ContentKind.Empty, "<AddIndex table=\"orders\" columns=\"status, created_at\" />", IndexAttributes(true)), run =>
            {
                Plan(run).Add(new AddIndexStep(run.RequiredRaw("table").Trim(), Names(run, "columns"), run.Flag("unique")));
                return null;
            });

            p.Element(Step("DropIndex", "Drops the index on these columns.", ContentKind.Empty, "<DropIndex table=\"orders\" columns=\"status\" />", IndexAttributes(true)), run =>
            {
                Plan(run).Add(new DropIndexStep(run.RequiredRaw("table").Trim(), Names(run, "columns"), run.Flag("unique")));
                return null;
            });

            p.Element(Step("Sql", "SQL to run as it is, for what the other steps can't do. db diff doesn't see what it changes, and rollback needs its down: to undo it.", ContentKind.Empty,
                "<Sql up=\"UPDATE users SET active = TRUE\" down=\"UPDATE users SET active = FALSE\" />",
                new A("up", K.Text, "The SQL that migrates.", true) { Literal = true },
                new A("down", K.Text, "The SQL that undoes it.") { Literal = true }), run =>
            {
                Plan(run).Add(new SqlStep(run.RequiredRaw("up"), run.Raw("down")));
                return null;
            });

            p.Element(new ElementInfo("Down", "The steps that undo the migration, run by kscript db rollback in place of undoing each step.", ContentKind.Statements)
            {
                Context = ElementContext.Script,
                MustBeInside = new[] { "Migration" },
                Example = "<KScript.Migration>\n  <Sql up=\"...\" />\n  <Down>\n    <Sql up=\"...\" />\n  </Down>\n</KScript.Migration>",
            }, run =>
            {
                MigrationPlan plan = Plan(run);
                if (plan.Down != null) throw run.Error("A migration has one Down block.", null, "Migration");
                plan.Down = new List<Step>();
                plan.InDown = true;
                try
                {
                    run.RunChildren();
                }
                finally
                {
                    plan.InDown = false;
                }
                return null;
            });
        }

        private static MigrationPlan Plan(ElementRun run) =>
            run.Host.Items.TryGetValue(MigrationPlan.HostKey, out object plan) && plan is MigrationPlan p ? p
            : throw run.Error("A migration runs with kscript db migrate (or db rollback), not on its own.", null, "Migration");

        private static TableSpec InTable(ElementRun run) =>
            Plan(run).Table ?? throw run.Error($"<{run.Element.Name}> goes inside a CreateTable.", null, "Migration");

        private static List<string> Names(ElementRun run, string attribute)
        {
            List<string> names = NameList.Names(run.RequiredRaw(attribute));
            if (names.Count == 0) throw run.Error($"{attribute} needs at least one column.", attribute, "Migration");
            return names;
        }

        /// <summary>A column from a Field, AddField or ChangeColumn's attributes.</summary>
        private static ColumnSpec Column(ElementRun run)
        {
            string word = run.RequiredRaw("type").Trim();
            ColumnKind kind = ColumnSpec.KindOf(word)
                ?? throw run.Error($"'{word}' isn't a column type. Use {string.Join(", ", TypeWords)}.", "type", "Migration");
            List<string> words = run.Has("values") ? NameList.Names(run.Raw("values")) : null;
            if (kind == ColumnKind.Enum && (words == null || words.Count == 0)) throw run.Error("An enum column needs its values: values: [member, admin].", "values", "Migration");
            int? max = run.Has("max") ? (int)run.Number("max", 0) : null;
            if (max is <= 0) throw run.Error("max should be a number of characters, more than 0.", "max", "Migration");
            object fallback = run.Has("default") ? run.Eval("default") : null;
            if (fallback is not (null or string or double or bool)) throw run.Error("A default is text, a number, true or false.", "default", "Migration");
            bool key = run.Flag("key");
            bool auto = run.Flag("auto");
            if (auto && (!key || kind != ColumnKind.Integer)) throw run.Error("auto numbers the rows of an int key: type: int, key, auto.", "auto", "Migration");
            string references = run.Raw("references")?.Trim();
            string onDelete = run.Raw("on_delete")?.Trim().Replace('_', ' ');
            bool nullable = run.Flag("optional");
            if (onDelete == "set null" && !nullable) throw run.Error("on_delete: set_null needs a column that may be empty (optional).", "on_delete", "Migration");
            return new ColumnSpec
            {
                Name = run.RequiredRaw("name").Trim(),
                Kind = kind,
                Nullable = nullable && !key,
                Length = max ?? (kind == ColumnKind.Email ? 254 : null),
                Words = kind == ColumnKind.Enum ? words : null,
                IsKey = key,
                AutoIncrement = auto,
                Default = fallback,
                References = references,
                ReferencesColumn = run.Raw("references_column")?.Trim(),
                OnDelete = references == null ? null : onDelete,
            };
        }
    }
}
