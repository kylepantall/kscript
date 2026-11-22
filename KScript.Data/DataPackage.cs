using System.Data;

using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax.Markup;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Data
{
    /// <summary>
    /// KScript.Data: <c>model</c> declarations, a query builder whose conditions are lambdas compiled to SQL,
    /// transactions, and raw SQL whose <c>{ }</c> values are always parameters. SQLite is the database.
    /// </summary>
    public sealed class DataPackage : KScriptPackage
    {
        public override string Name => "KScript.Data";

        public override string Description => "Databases: models, a query builder, transactions and raw SQL, on SQLite, Postgres, MySQL and MariaDB.";

        /// <summary>A query method: its name, parameters, what it does, what it returns, and the work it does on a query.</summary>
        private sealed record QueryMethod(string Name, string Params, string Description, string Returns, Func<Query, CallContext, object> Run, bool Bare = false);

        protected override void Register(PackageBuilder p)
        {
            RegisterModel(p);
            RegisterQueries(p);
            RegisterRecords(p);
            RegisterDatabase(p);
        }

        #region Models

        private static void RegisterModel(PackageBuilder p)
        {
            var attributes = new List<A>
            {
                new A("name", K.DeclareVariable, "The model's name, such as User: $User->where(...) queries it, and its records have its fields.", true),
                new A("fields", K.Fields, "A short model's fields on one line: `name: string, email: email`. Bigger models list one <field> per line."),
                new A("table", K.Text, "The table its records are kept in. Default: the plural of the name in snake case (User is users, BlogPost blog_posts)."),
                new A("connection", K.Text, "The database connection, from database.connections in your config. Default: the default connection (main)."),
                new A("key", K.Text, "The field that is the key. Default: id, an int the database numbers, added when the model doesn't declare it."),
                new A("timestamps", K.Bool, "yes adds created_at and updated_at, set when records are saved."),
                new A("soft_deletes", K.Bool, "yes adds deleted_at: ->delete() sets it instead of removing the row, and queries leave those records out (->with_trashed() keeps them)."),
                new A("unique", K.Text, "Fields no two records may share: `[email]`, or `[email, [team_id, slug]]` for a pair that is unique together. Saving a clash fails with UniqueViolation."),
                new A("index", K.Text, "Fields to index, so queries on them are fast: `[team_id, [team_id, created_at]]`."),
                new A("hidden", K.Text, "Fields left out of JSON (and so out of route responses): `[password]`."),
                new A("guarded", K.Text, "Fields ->create($form) and ->update($form) don't set, so a form can't change them: `[role]`. Set them on the record instead: `$user.role = 'admin'`."),
                new A("extra", K.Enum, "Keys ->create() is given that the model doesn't have: drop them (the default), keep them, or make them an error.", false, "drop", "keep", "error"),
                new A("description", K.Text, "What the model is, shown on hover."),
            };
            p.Element(new ElementInfo("model", "Declares a model: a <type> whose records are kept in a database table. `$User->where(role: admin)->get()` reads them, `$User->create({...})` adds one, and `$user->save()` writes changes. Its fields are checked by their types and rules on every save.",
                ContentKind.Statements, attributes.ToArray())
            {
                Example = "<model name=\"User\" timestamps=\"yes\" unique=\"email\">\n  <field name=\"name\" type=\"string(max: 120)\" />\n  <field name=\"email\" type=\"email\" />\n</model>",
            }, run =>
            {
                RecordType type = ModelInfo.Declare(run);
                run.Scope.Declare(type.Name, type, isConst: true);
                return null;
            }, hoisted: true);
        }

        #endregion

        #region Queries

        /// <summary>The query a method works on: the query itself, or (for $User->...) one on the model with no conditions yet.</summary>
        private static Query QueryOf(object target, CallContext call)
        {
            if (target is Query q) return q;
            ModelInfo m = ModelInfo.Of(target);
            return new Query(Database.For(call, m), m.Table, m);
        }

        /// <summary>A word, text or column name argument: <c>name</c>, <c>'name'</c>.</summary>
        private static string Word(object value, string what)
        {
            if (value is BareWord w) return w.Name;
            if (value is string s && s.Trim().Length > 0) return s.Trim();
            throw new KScriptException($"{what} needs a column name, such as name or created_at, but got {Values.Describe(value)}.", "Argument");
        }

        /// <summary>The conditions of ->where(): lambdas and maps given in order, then the named arguments as equalities.</summary>
        private static SqlExpr Conditions(Query q, CallContext c, string method)
        {
            SqlExpr all = null;
            void Add(SqlExpr e) => all = all == null ? e : new SqlBinary("AND", all, e);
            foreach (object a in c.Args)
            {
                switch (a)
                {
                    case null:
                        continue;
                    case ICallable fn:
                        Add(LambdaSql.Compile(fn, q.Model, c.Interpreter, method));
                        break;
                    case Dictionary<string, object> map when map.Count > 0:
                        Add(q.Equalities(map, method));
                        break;
                    case Dictionary<string, object>:
                        break;
                    default:
                        throw new KScriptException($"->{method}() takes conditions as a lambda, fn($u) => $u.age >= 18, or as names and values, role: admin, not {Values.Describe(a)}.", "Argument");
                }
            }
            if (c.Named.Count > 0) Add(q.Equalities(c.Named, method));
            if (all == null) throw new KScriptException($"->{method}() needs a condition: a lambda, fn($u) => $u.active, or names and values, role: admin.", "Argument");
            return all;
        }

        /// <summary>The values of an update or create: a map, named arguments, or both (named win).</summary>
        private static Dictionary<string, object> ValuesOf(CallContext c, string method, bool allowEmpty = false)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (object a in c.Args)
            {
                if (a == null) continue;
                if (a is not Dictionary<string, object> map) throw new KScriptException($"->{method}() takes a map of values, or names and values (name: 'Joan'), not {Values.Describe(a)}.", "Argument");
                foreach (KeyValuePair<string, object> kv in map) values[kv.Key] = kv.Value;
            }
            foreach (KeyValuePair<string, object> kv in c.Named) values[kv.Key] = kv.Value;
            if (values.Count == 0 && !allowEmpty) throw new KScriptException($"->{method}() needs values to set: a map, or names and values such as name: 'Joan'.", "Argument");
            return values;
        }

        private static int Count(object value, string what)
        {
            double n = Values.ToNumber(value, what);
            if (n < 0 || n != Math.Floor(n)) throw new KScriptException($"{what} must be a whole number of 0 or more, not {Values.ToStr(value)}.", "Argument");
            return (int)n;
        }

        private static readonly QueryMethod[] QueryMethods =
        {
            new("where", "conditions...", "Keeps the rows that match: a lambda compiled to SQL, `fn($u) => $u.age >= 18 and $u.name->starts_with('J')`, or names and values, `role: admin, team_id: 3` (a list means any of them; null means none).", KType.Query,
                (q, c) => q.And(Conditions(q, c, "where")), Bare: true),
            new("or_where", "conditions...", "Like ->where(), joined with or: rows that match the conditions so far, or these.", KType.Query,
                (q, c) => q.Or(Conditions(q, c, "or_where")), Bare: true),
            new("where_not", "conditions...", "Leaves out the rows that match.", KType.Query,
                (q, c) => q.And(new SqlUnary("NOT", Conditions(q, c, "where_not"))), Bare: true),
            new("when", "test, then: fn, otherwise?: fn", "Changes the query only when the test is true: `->when($search != null, fn($q) => $q->where(fn($u) => $u.name->contains($search)))`.", KType.Query,
                (q, c) =>
                {
                    ICallable fn = Values.Truthy(c.Arg(0)) ? c.Fn(1) : c.Has(2) && c.Arg(2) != null ? c.Fn(2) : null;
                    if (fn == null) return q;
                    return c.Call(fn, q) as Query ?? throw new KScriptException("The function given to ->when() must return the query.", "Type");
                }),
            new("select", "columns...", "Reads only these columns: `->select(id, name)`. Records get null for the others.", KType.Query,
                (q, c) => q.Selecting(Flatten(c.Args).Select(a => { string w = Word(a, "->select()"); q.ColumnNamed(w, " for ->select()"); return w; }).ToList()), Bare: true),
            new("distinct", "", "Leaves out rows that are the same as another, in the columns selected.", KType.Query, (q, c) => q.Unique()),
            new("order_by", "columns...", "Sorts the rows: `->order_by(name)`, `->order_by(created_at: desc, name: asc)`.", KType.Query,
                (q, c) =>
                {
                    foreach (object a in Flatten(c.Args)) q = q.OrderBy(q.ColumnNamed(Word(a, "->order_by()"), " for ->order_by()"), false);
                    foreach (KeyValuePair<string, object> kv in c.Named)
                    {
                        string direction = Word(kv.Value, "->order_by()").ToLowerInvariant();
                        if (direction is not ("asc" or "desc")) throw new KScriptException($"->order_by() sorts asc or desc, not '{direction}': {kv.Key}: desc.", "Argument");
                        q = q.OrderBy(q.ColumnNamed(kv.Key, " for ->order_by()"), direction == "desc");
                    }
                    if (c.Args.Count == 0 && c.Named.Count == 0) throw new KScriptException("->order_by() needs a column: ->order_by(name), or ->order_by(created_at: desc).", "Argument");
                    return q;
                }, Bare: true),
            new("latest", "column?", "Newest first: sorts by created_at (or the column given) from the newest down.", KType.Query,
                (q, c) => q.OrderBy(q.ColumnNamed(c.Has(0) ? Word(c.Arg(0), "->latest()") : DefaultTimeColumn(q), " for ->latest()"), true), Bare: true),
            new("oldest", "column?", "Oldest first: sorts by created_at (or the column given) from the oldest up.", KType.Query,
                (q, c) => q.OrderBy(q.ColumnNamed(c.Has(0) ? Word(c.Arg(0), "->oldest()") : DefaultTimeColumn(q), " for ->oldest()"), false), Bare: true),
            new("limit", "count: number", "Reads at most this many rows.", KType.Query, (q, c) => q.Taking(Count(c.Arg(0), "->limit()"))),
            new("offset", "count: number", "Skips this many rows first.", KType.Query, (q, c) => q.Skipping(Count(c.Arg(0), "->offset()"))),
            new("with_trashed", "", "For models with soft_deletes: keeps the deleted records, which queries leave out by default.", KType.Query, (q, c) => q.WithTrashed("with")),
            new("only_trashed", "", "For models with soft_deletes: reads only the deleted records.", KType.Query, (q, c) => q.WithTrashed("only")),
            new("lock_for_update", "", "Locks the rows read until the transaction ends, so nobody else changes them first. Use it inside a transaction. On SQLite, which locks the whole database for a transaction's writes instead, it changes nothing.", KType.Query, (q, c) => q.Locking("update", false)),
            new("skip_locked", "", "With ->lock_for_update(): skips rows another transaction has locked, instead of waiting. Good for taking jobs from a queue. On SQLite, which has no row locks, it changes nothing.", KType.Query, (q, c) => q.Locking(null, true)),

            new("get", "", "Runs the query: a list of records (maps, for a table with no model).", KType.List, (q, c) => q.Get(c)),
            new("first", "", "The first row, or null when none match.", KType.Any, (q, c) => q.First(c)),
            new("first_or_fail", "", "The first row, or a ModelNotFound error (a 404 in a route) when none match.", KType.Any, (q, c) => q.FirstOrFail(c)),
            new("find", "id", "The record with this key, or null.", KType.Any, (q, c) => q.ById(c.Arg(0)).First(c)),
            new("find_or_fail", "id", "The record with this key, or a ModelNotFound error (a 404 in a route).", KType.Any, (q, c) => q.ById(c.Arg(0)).FirstOrFail(c, c.Arg(0))),
            new("exists", "", "yes if any row matches.", KType.Bool, (q, c) => q.Exists(c)),
            new("count", "", "How many rows match.", KType.Number, (q, c) => q.Count(c)),
            new("sum", "column", "The total of a column over the rows that match (0 when none do).", KType.Number, (q, c) => q.Aggregate(c, "SUM", Word(c.Arg(0), "->sum()")), Bare: true),
            new("avg", "column", "The average of a column, or null when no rows match.", KType.Number, (q, c) => q.Aggregate(c, "AVG", Word(c.Arg(0), "->avg()")), Bare: true),
            new("min", "column", "The smallest value of a column, or null when no rows match.", KType.Any, (q, c) => q.Aggregate(c, "MIN", Word(c.Arg(0), "->min()")), Bare: true),
            new("max", "column", "The largest value of a column, or null when no rows match.", KType.Any, (q, c) => q.Aggregate(c, "MAX", Word(c.Arg(0), "->max()")), Bare: true),
            new("pluck", "column", "One column of every row that matches, as a list: `$User->pluck(email)`.", KType.List, (q, c) => q.Pluck(c, Word(c.Arg(0), "->pluck()")), Bare: true),
            new("value", "column", "One column of the first row, or null.", KType.Any, (q, c) => q.Value(c, Word(c.Arg(0), "->value()")), Bare: true),
            new("paginate", "per_page?: number, page?: number", "One page of rows: { items, page, per_page, total, last_page, from, to, has_more }. per_page is 15 by default; page (1 by default) can be text such as $query.page.", KType.Map,
                (q, c) =>
                {
                    int perPage = c.Has(0) && c.Arg(0) != null ? Count(c.Arg(0), "per_page") : 15;
                    object pageArg = c.Arg(1);
                    int page = pageArg == null || (pageArg is string s && !double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) ? 1 : (int)Values.ToNumber(pageArg, "page");
                    return q.Paginate(c, perPage, page);
                }),
            new("chunk", "size: number, work: fn", "Reads the rows a batch at a time and calls work with each batch (and its number from 0): for going through more rows than fit in memory. Return false from work to stop.", KType.Null,
                (q, c) => { q.Chunk(c, Count(c.Arg(0), "->chunk() size"), c.Fn(1)); return null; }),
            new("update", "values...", "Sets columns on every row that matches, without reading them: `->update(active: false)`. Returns how many rows changed.", KType.Number,
                (q, c) => q.Update(c, q.Model != null ? Records.Fillable(q.Model, ValuesOf(c, "update")) : ValuesOf(c, "update")), Bare: true),
            new("increment", "column, by?: number", "Adds to a number column on every row that matches (1 by default). Returns how many rows changed.", KType.Number,
                (q, c) => q.Increment(c, Word(c.Arg(0), "->increment()"), c.Num(1, 1)), Bare: true),
            new("decrement", "column, by?: number", "Takes away from a number column on every row that matches (1 by default). Returns how many rows changed.", KType.Number,
                (q, c) => q.Increment(c, Word(c.Arg(0), "->decrement()"), -c.Num(1, 1)), Bare: true),
            new("delete", "", "Deletes every row that matches: models with soft_deletes only mark them deleted. Returns how many.", KType.Number, (q, c) => q.Delete(c, force: false)),
            new("force_delete", "", "Deletes every row that matches, even for models with soft_deletes. Returns how many.", KType.Number, (q, c) => q.Delete(c, force: true)),
            new("restore", "", "For models with soft_deletes: brings back the deleted records that match. Returns how many.", KType.Number, (q, c) => q.Restore(c)),
            new("insert", "rows", "Adds rows: a map, or a list of maps. On a model each one is checked like ->create(). Returns how many were added.", KType.Number, (q, c) => q.Insert(c, c.Arg(0))),
            new("sql", "", "The SQL the query runs and its parameters, without running it: { sql, params }.", KType.Map, (q, c) => q.Describe()),
        };

        private static string DefaultTimeColumn(Query q) =>
            q.Model == null ? "created_at" : q.Model.Timestamps ? ModelInfo.CreatedAt : q.Model.Key;

        private static IEnumerable<object> Flatten(IEnumerable<object> args) =>
            args.SelectMany(a => a is List<object> list ? (IEnumerable<object>)list : new[] { a }).Where(a => a != null);

        private static readonly QueryMethod[] ModelMethods =
        {
            new("create", "values...", "Checks the values against the model, saves a new record and returns it: `$User->create({ name: 'Joan', email: 'joan@example.com' })` or `$User->create(name: 'Joan', ...)`. Guarded fields are left out.", KType.Record,
                (q, c) => Records.Create(c, q.Model, ValuesOf(c, "create", allowEmpty: true)), Bare: true),
            new("all", "", "Every record (leaving out soft-deleted ones).", KType.List, (q, c) => q.Get(c)),
            new("first_or_create", "match: map, values?: map", "The first record that matches, or a new one made from both maps.", KType.Record,
                (q, c) =>
                {
                    Dictionary<string, object> match = c.Map(0);
                    if (match.Count > 0 && q.And(q.Equalities(match, "first_or_create")).First(c) is object found) return found;
                    var values = new Dictionary<string, object>(match, StringComparer.Ordinal);
                    if (c.Has(1) && c.Arg(1) != null) foreach (KeyValuePair<string, object> kv in c.Map(1)) values[kv.Key] = kv.Value;
                    return Records.Create(c, q.Model, values);
                }),
            new("query", "", "A query on the model with no conditions yet, to build on.", KType.Query, (q, c) => q),
        };

        private static void RegisterQueries(PackageBuilder p)
        {
            foreach (QueryMethod m in QueryMethods)
            {
                p.Method(KType.Query, m.Name, m.Params, m.Description, m.Returns, (t, c) => m.Run(QueryOf(t, c), c)).Info.KeepsBareWords = m.Bare;
                p.Method(KType.Model, m.Name, m.Params, m.Description, m.Returns, (t, c) => m.Run(QueryOf(t, c), c)).Info.KeepsBareWords = m.Bare;
            }
            foreach (QueryMethod m in ModelMethods)
            {
                p.Method(KType.Model, m.Name, m.Params, m.Description, m.Returns, (t, c) => m.Run(QueryOf(t, c), c)).Info.KeepsBareWords = m.Bare;
            }
        }

        #endregion

        #region Records

        private static Record RecordOf(object target, CallContext call) =>
            target is Record r && r.Type.Tag is ModelInfo ? r : throw new KScriptException($"{call.Display} works on model records, not {Values.Describe(target)}.", "Type");

        private static void RegisterRecords(PackageBuilder p)
        {
            p.Method(KType.Record, "save", "", "Saves the record: adds it when it is new, otherwise writes the fields that changed (and updated_at). Returns the record.", KType.Record,
                (t, c) => Records.Save(c, RecordOf(t, c)));
            p.Method(KType.Record, "update", "values...", "Sets fields and saves: `$user->update(name: 'Joan')` or `->update($form)`. Each value is checked by its field's type; guarded fields are left out. Returns the record.", KType.Record,
                (t, c) => Records.Update(c, RecordOf(t, c), ValuesOf(c, "update"))).Info.KeepsBareWords = true;
            p.Method(KType.Record, "delete", "", "Deletes the record. With soft_deletes it is only marked deleted (->restore() brings it back). yes if it was deleted.", KType.Bool,
                (t, c) => Records.Delete(c, RecordOf(t, c), force: false));
            p.Method(KType.Record, "force_delete", "", "Deletes the record from the table, even with soft_deletes.", KType.Bool,
                (t, c) => Records.Delete(c, RecordOf(t, c), force: true));
            p.Method(KType.Record, "restore", "", "Brings back a soft-deleted record. Returns the record.", KType.Record,
                (t, c) => Records.Restore(c, RecordOf(t, c)));
            p.Method(KType.Record, "fresh", "", "The record as the database has it now (a new record), or null if it was deleted.", KType.Record,
                (t, c) => Records.Fresh(c, RecordOf(t, c)));
            p.Method(KType.Record, "is_dirty", "field?", "yes if the record has changes that aren't saved: any field, or the one named.", KType.Bool,
                (t, c) =>
                {
                    Record r = RecordOf(t, c);
                    List<string> dirty = Records.Dirty(ModelInfo.Of(r), r);
                    return c.Has(0) ? dirty.Contains(Word(c.Arg(0), "->is_dirty()")) : dirty.Count > 0;
                }).Info.KeepsBareWords = true;
            p.Method(KType.Record, "dirty", "", "The names of the fields with changes that aren't saved.", KType.List,
                (t, c) =>
                {
                    Record r = RecordOf(t, c);
                    return Records.Dirty(ModelInfo.Of(r), r).Cast<object>().ToList();
                });
            p.Method(KType.Record, "is_saved", "", "yes if the record is in the database: it was read from it or has been saved.", KType.Bool,
                (t, c) => Records.State(RecordOf(t, c))?.Exists == true);

            p.Method(KType.PasswordHash, "verify", "password", "yes if the password someone typed is the one this hash was made from: `$user.password->verify($form.password)`.", KType.Bool,
                (t, c) => ((PasswordHash)t).Verify(c.Arg(0)));
            p.Method(KType.PasswordHash, "needs_rehash", "algorithm?: argon2id|bcrypt", "yes if the hash was made with weaker settings (or another algorithm) than ->hash() uses now, so it should be made again at the next sign-in.", KType.Bool,
                (t, c) => PasswordValue.NeedsRehash(((PasswordHash)t).Stored, c.Has(0) ? c.Str(0) : "argon2id"));
        }

        #endregion

        #region Databases, SQL and transactions

        private static Database DatabaseOf(object target, CallContext call) => target is DefaultDatabase d ? d.Resolve() : (Database)target;

        /// <summary>
        /// <c>@sql("select * from users where email = {$email}")</c>: each <c>{ }</c> is worked out and sent as a parameter,
        /// never pasted into the SQL. <c>{{</c> and <c>}}</c> are a brace.
        /// </summary>
        internal static SqlStatement Bind(CallContext call, string sql, Dialect dialect)
        {
            var w = new SqlWriter(dialect);
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < sql.Length; i++)
            {
                char ch = sql[i];
                if (ch == '{' && i + 1 < sql.Length && sql[i + 1] == '{')
                {
                    text.Append('{');
                    i++;
                    continue;
                }
                if (ch == '}' && i + 1 < sql.Length && sql[i + 1] == '}')
                {
                    text.Append('}');
                    i++;
                    continue;
                }
                if (ch != '{')
                {
                    text.Append(ch);
                    continue;
                }
                int depth = 1;
                int j = i + 1;
                char quote = '\0';
                for (; j < sql.Length && depth > 0; j++)
                {
                    char x = sql[j];
                    if (quote != '\0')
                    {
                        if (x == quote) quote = '\0';
                        continue;
                    }
                    if (x is '\'' or '"') quote = x;
                    else if (x == '{') depth++;
                    else if (x == '}') depth--;
                }
                if (depth > 0) throw new KScriptException("This @sql has a { without its }. Write {{ for a brace.", "Syntax");
                string expression = sql.Substring(i + 1, j - i - 2).Trim();
                if (expression.Length == 0) throw new KScriptException("This @sql has an empty { }. Write {{ }} for braces.", "Syntax");
                object value = call.Interpreter.EvaluateText(DecodedText.Plain(expression, 0), call.Scope, call.Module);
                w.Append(text.ToString());
                text.Clear();
                if (value is List<object> list)
                {
                    // {$ids} in "where id in {$ids}" is a list of parameters.
                    w.Append("(");
                    for (int k = 0; k < list.Count; k++)
                    {
                        if (k > 0) w.Append(", ");
                        w.Value(Rows.ToDb(list[k], null, dialect));
                    }
                    if (list.Count == 0) w.Append("NULL");
                    w.Append(")");
                }
                else
                {
                    w.Value(Rows.ToDb(value, null, dialect));
                }
                i = j - 1;
            }
            w.Append(text.ToString());
            return w.Build();
        }

        private static object RunSql(CallContext call, Database db, string sql, object type)
        {
            object result = db.Run(call.Interpreter, Bind(call, sql, db.Dialect));
            if (result is not List<Dictionary<string, object>> rows) return result;
            List<object> maps = rows.Select(r => (object)Rows.Map(r)).ToList();
            return type == null ? maps : Shapes.MapAs(call.Interpreter, maps, type, "The rows", call.Scope, call.Module);
        }

        private static object Transaction(CallContext call, Database db, ICallable work, object retries)
        {
            int tries = retries == null ? 0 : Count(retries, "retries");
            return db.InTransaction(call.Interpreter, () => call.Call(work), tries);
        }

        private static void RegisterDatabase(PackageBuilder p)
        {
            p.Variable("db", KType.Database,
                "The default database connection: $db->table('users'), $db->tables(), $db->create_table(User). With no config it is SQLite in data/app.sqlite in the project folder; database.connections in your config sets others.",
                (i, m) => new DefaultDatabase(i.Host, m?.Directory ?? i.Host.WorkingDirectory));

            p.Command("db", "table?, connection?", "A query on a table with no model (`@db('users')->where(active: 1)->get()` gives maps), or with no table the database itself. connection names one from database.connections in your config.", KType.Any,
                c =>
                {
                    string connection = c.Has(1, "connection") && c.Arg(1, "connection") != null ? Word(c.Arg(1, "connection"), "connection") : null;
                    Database db = Database.For(c, connection);
                    if (!c.Has(0, "table") || c.Arg(0, "table") == null) return db;
                    string table = Word(c.Arg(0, "table"), "@db()");
                    if (!ModelInfo.IsName(table)) throw new KScriptException($"'{table}' isn't a table name: use letters, digits and _.", "Argument");
                    return new Query(db, table, null);
                }).Info.KeepsBareWords = true;

            p.Command("sql", "text, as?, connection?", "Runs SQL. Each { } in it is worked out and sent as a parameter, never pasted in: `@sql(\"select * from users where email = {$email}\")`. A query gives its rows as maps (or as: a type); anything else gives how many rows it changed.", KType.Any,
                c =>
                {
                    string connection = c.Arg(2, "connection") != null ? Word(c.Arg(2, "connection"), "connection") : null;
                    return RunSql(c, Database.For(c, connection), c.Str(0), c.Arg(1, "as"));
                }).Info.KeepsBareWords = true;

            p.Command("transaction", "work: fn, retries?: number, connection?", "Runs work in a transaction and returns what it returns: everything it saves is kept together, or (when it fails) not at all. retries tries again when the database is busy.", KType.Any,
                c =>
                {
                    string connection = c.Arg(2, "connection") != null ? Word(c.Arg(2, "connection"), "connection") : null;
                    return Transaction(c, Database.For(c, connection), c.Fn(0), c.Arg(1, "retries"));
                }).Info.KeepsBareWords = true;

            p.Element(new ElementInfo("transaction", "Runs its statements in a transaction: what they save is kept when the block ends, and undone if anything in it fails (the error then carries on). A transaction inside another is a savepoint, so only its own changes are undone.",
                ContentKind.Statements,
                new A("retries", K.Expression, "Tries the block again this many times when the database is busy or a deadlock stops it."),
                new A("connection", K.Text, "The database connection, from database.connections in your config. Default: the default connection."))
            {
                Example = "<transaction>\n  <do>$Order->create({ total: 10 })</do>\n</transaction>",
            }, run =>
            {
                string connection = run.Raw("connection")?.Trim();
                Database db = Database.For(run.Host, run.Module.Directory, connection);
                int retries = run.Has("retries") ? Count(run.Eval("retries"), run.Module.ArgumentHint("transaction", "retries")) : 0;
                db.InTransaction<object>(run.Interpreter, () =>
                {
                    run.RunChildren();
                    return null;
                }, retries);
                return null;
            });

            p.Method(KType.Database, "table", "name", "A query on a table with no model: rows come back as maps.", KType.Query,
                (t, c) =>
                {
                    string table = Word(c.Arg(0), "->table()");
                    if (!ModelInfo.IsName(table)) throw new KScriptException($"'{table}' isn't a table name: use letters, digits and _.", "Argument");
                    return new Query(DatabaseOf(t, c), table, null);
                }).Info.KeepsBareWords = true;
            p.Method(KType.Database, "sql", "text, as?", "Runs SQL, like @sql, on this connection.", KType.Any,
                (t, c) => RunSql(c, DatabaseOf(t, c), c.Str(0), c.Arg(1, "as"))).Info.KeepsBareWords = true;
            p.Method(KType.Database, "tables", "", "The names of the tables in the database.", KType.List,
                (t, c) => DatabaseOf(t, c).Tables(c.Interpreter).Cast<object>().ToList());
            p.Method(KType.Database, "has_table", "name", "yes if the database has the table: a name, or a model's table.", KType.Bool,
                (t, c) =>
                {
                    string table = ModelInfo.Of(c.Arg(0))?.Table ?? Word(c.Arg(0), "->has_table()");
                    return DatabaseOf(t, c).Tables(c.Interpreter).Contains(table);
                }).Info.KeepsBareWords = true;
            p.Method(KType.Database, "create_table", "model", "Makes a model's table and indexes, if the database doesn't have them yet. Good for scripts and tests; projects use migrations.", KType.Bool,
                (t, c) =>
                {
                    ModelInfo model = ModelInfo.Of(c.Arg(0)) ?? throw new KScriptException($"->create_table() needs a model, such as User, but got {Values.Describe(c.Arg(0))}.", "Argument");
                    Database db = DatabaseOf(t, c);
                    bool existed = db.Tables(c.Interpreter).Contains(model.Table);
                    foreach (string statement in db.Dialect.CreateTable(model, ifMissing: true))
                    {
                        db.Execute(c.Interpreter, new SqlStatement(statement, Array.Empty<object>()), model.Table);
                    }
                    return !existed;
                });
            p.Method(KType.Database, "drop_table", "table", "Deletes a table and everything in it, if it is there: a name, or a model's table. yes if there was one.", KType.Bool,
                (t, c) =>
                {
                    string table = ModelInfo.Of(c.Arg(0))?.Table ?? Word(c.Arg(0), "->drop_table()");
                    if (!ModelInfo.IsName(table)) throw new KScriptException($"'{table}' isn't a table name: use letters, digits and _.", "Argument");
                    Database db = DatabaseOf(t, c);
                    bool existed = db.Tables(c.Interpreter).Contains(table);
                    db.Execute(c.Interpreter, new SqlStatement("DROP TABLE IF EXISTS " + db.Dialect.Quote(table), Array.Empty<object>()), table);
                    return existed;
                }).Info.KeepsBareWords = true;
            p.Method(KType.Database, "transaction", "work: fn, retries?: number", "Runs work in a transaction on this connection, like @transaction.", KType.Any,
                (t, c) => Transaction(c, DatabaseOf(t, c), c.Fn(0), c.Arg(1, "retries")));
            p.Method(KType.Database, "in_transaction", "", "yes while a transaction is open on this connection.", KType.Bool,
                (t, c) => DatabaseOf(t, c).InTransaction(c.Interpreter));
        }

        #endregion
    }
}
