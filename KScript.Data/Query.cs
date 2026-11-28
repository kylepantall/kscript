using System.Globalization;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// A query being built: <c>User->where(role: admin)->latest()->limit(10)</c>. Each method returns a new query and
    /// leaves the old one as it was, so a query kept in a variable can be the start of several others. Nothing runs
    /// until a method that reads or writes rows, such as <c>->get()</c>, <c>->count()</c> or <c>->update()</c>.
    /// </summary>
    internal sealed record Query : IKObject
    {
        public Query(Database database, string table, ModelInfo model)
        {
            Database = database;
            Table = table;
            Model = model;
        }

        public Database Database { get; private init; }

        public string Table { get; private init; }

        /// <summary>The model whose records the query reads, or null for a table read as maps.</summary>
        public ModelInfo Model { get; private init; }

        public SqlExpr Where { get; private init; }

        /// <summary>The part of <see cref="Where"/> every row must meet whatever is added with ->or_where(), and the part ->or_where() widens.</summary>
        private SqlExpr Pinned { get; init; }

        private SqlExpr Loose { get; init; }

        public IReadOnlyList<(SqlExpr Expr, bool Descending)> Order { get; private init; } = Array.Empty<(SqlExpr, bool)>();

        public int? Limit { get; private init; }

        public int? Offset { get; private init; }

        public IReadOnlyList<string> Columns { get; private init; }

        public bool Distinct { get; private init; }

        /// <summary>For soft-deleting models: null leaves deleted records out, "with" keeps them, "only" reads only them.</summary>
        public string Trashed { get; private init; }

        public string LockMode { get; private init; }

        public bool SkipLocked { get; private init; }

        /// <summary>The relations ->with() loads after the rows are read: names or paths (posts.tags), each with an optional function that changes its query.</summary>
        public IReadOnlyList<(string Path, ICallable Constrain)> Withs { get; private init; } = Array.Empty<(string, ICallable)>();

        /// <summary>The relations ->with_count() counts, as a <c>name_count</c> field on each record.</summary>
        public IReadOnlyList<(string Name, ICallable Constrain)> Counts { get; private init; } = Array.Empty<(string, ICallable)>();

        /// <summary>For <c>$user->posts()</c>: the relation and the record, so ->create() and ->attach() know what to link to.</summary>
        public Relation Relation { get; private init; }

        public Record Parent { get; private init; }

        public string TypeName => KType.Query;

        public string ToText() => "query on " + Table + ": " + SelectStatement().Text;

        public override string ToString() => ToText();

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "table" => Table,
                "model" => Model?.Type,
                _ => null,
            };
            return value != null || name == "model";
        }

        /// <summary>A scope of the model: <c>User->where(...)->admins()</c>.</summary>
        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            if (Model == null || !Model.IsScope(name)) return false;
            result = Model.ApplyScope(call, this, name, call.Args, call.Named);
            return true;
        }

        #region Building

        public Query And(SqlExpr condition) => Narrowed(Loose == null ? condition : new SqlBinary("AND", Loose, condition));

        /// <summary>Widens what the script asked for. What the query is tied to (the record of <c>$user->posts()</c>) still holds.</summary>
        public Query Or(SqlExpr condition) => Narrowed(Loose == null ? condition : new SqlBinary("OR", Loose, condition));

        private Query Narrowed(SqlExpr loose) => this with { Loose = loose, Where = Pinned == null ? loose : new SqlBinary("AND", Pinned, loose) };

        /// <summary>Ties the query to its conditions so far: a later ->or_where() adds to what follows, never to these.</summary>
        public Query Pin() => this with { Pinned = Where, Loose = null };

        public Query OrderBy(SqlExpr expr, bool descending) => this with { Order = Order.Append((expr, descending)).ToList() };

        public Query Taking(int? limit) => this with { Limit = limit };

        public Query Skipping(int? offset) => this with { Offset = offset };

        public Query Selecting(IReadOnlyList<string> columns) => this with { Columns = columns };

        public Query Unique() => this with { Distinct = true };

        public Query WithTrashed(string mode) => this with { Trashed = mode };

        public Query Locking(string mode, bool skip) => this with { LockMode = mode ?? LockMode ?? "update", SkipLocked = skip || SkipLocked };

        public Query Of(Relation relation, Record parent) => this with { Relation = relation, Parent = parent };

        public Query Loading(string path, ICallable constrain)
        {
            ModelInfo at = Model ?? throw new KScriptException($"->with() loads a model's relations, and this query is on the table {Table}, which has no model.", "Argument");
            foreach (string part in path.Split('.')) at = at.NeedRelation(part.Trim(), " for ->with()").Target;
            return this with { Withs = Withs.Where(w => w.Path != path).Append((path, constrain)).ToList() };
        }

        public Query Counting(string name, ICallable constrain)
        {
            Relation r = (Model ?? throw new KScriptException($"->with_count() counts a model's relations, and this query is on the table {Table}, which has no model.", "Argument"))
                .NeedRelation(name, " for ->with_count()");
            if (!r.ToMany) throw new KScriptException($"->with_count() counts a list of records, and {r.Where} is one {r.Target.Name}.", "Argument");
            return this with { Counts = Counts.Where(c => c.Name != name).Append((name, constrain)).ToList() };
        }

        /// <summary>A column of this query's table, checked against the model when there is one.</summary>
        public SqlColumn ColumnNamed(string name, string where)
        {
            if (Model == null)
            {
                if (!ModelInfo.IsName(name)) throw new KScriptException($"'{name}' isn't a column name: use letters, digits and _.", "Argument");
                return new SqlColumn(name);
            }
            if (Model.Type.Field(name) is FieldDefinition f && f.Computed)
                throw new KScriptException($"{Model.Name}.{name} is worked out from other fields, so the database doesn't have it{where}.", "Argument");
            if (Model.Relation(name) is Relation r)
            {
                if (r.Kind == RelationKind.BelongsTo) return new SqlColumn(r.ForeignKey, Model.Column(r.ForeignKey));
                throw new KScriptException($"{r.Where} {r.Describe()}, so it isn't a column{where}. Use a lambda: fn(x) => $x.{name}->any().", "Argument");
            }
            return new SqlColumn(name, Model.Need(name, where));
        }

        /// <summary>Conditions from a map or named arguments: each key equals its value (a list means one of them, null means none).</summary>
        public SqlExpr Equalities(IEnumerable<KeyValuePair<string, object>> pairs, string method)
        {
            SqlExpr all = null;
            foreach (KeyValuePair<string, object> pair in pairs)
            {
                SqlColumn column = ColumnNamed(pair.Key, $" for ->{method}()");
                object value = pair.Value;
                // author: $user means author_id is the user's key.
                if (Model?.Relation(pair.Key) is Relation r)
                {
                    object KeyOf(object v) => v is Record t ? ModelInfo.KeyOfTarget(r, t) : v;
                    value = value is List<object> many ? many.Select(KeyOf).ToList() : KeyOf(value);
                }
                SqlExpr one = value switch
                {
                    null => new SqlUnary("IS NULL", column),
                    List<object> list => new SqlBinary("IN", column, new SqlList(list.Select(v => (SqlExpr)new SqlValue(v, column.Column)).ToList())),
                    _ => new SqlBinary("=", column, new SqlValue(value, column.Column)),
                };
                all = all == null ? one : new SqlBinary("AND", all, one);
            }
            return all;
        }

        #endregion

        #region SQL

        private SqlExpr Conditions(bool forWrite = false)
        {
            SqlExpr where = Where;
            if (Model?.SoftDeletes == true && Trashed != "with")
            {
                var deleted = new SqlUnary(Trashed == "only" ? "IS NOT NULL" : "IS NULL", new SqlColumn(ModelInfo.DeletedAt));
                where = where == null ? deleted : new SqlBinary("AND", where, deleted);
            }
            return where;
        }

        private void WriteWhere(SqlWriter w)
        {
            SqlExpr where = Conditions();
            if (where != null) w.Append(" WHERE ").Expr(where);
        }

        private string SelectList(Dialect d) =>
            Columns == null || Columns.Count == 0 ? "*" : string.Join(", ", Columns.Select(d.Quote));

        public SqlStatement SelectStatement(string select = null, bool withOrder = true)
        {
            Dialect d = Database.Dialect;
            var w = new SqlWriter(d).Append("SELECT ").Append(Distinct ? "DISTINCT " : string.Empty).Append(select ?? SelectList(d));
            if (select == null)
            {
                // ->with_count(posts): (SELECT COUNT(*) FROM posts AS c1 WHERE c1.author_id = users.id) AS posts_count
                var call = Counts.Count > 0 ? CountCall : null;
                for (int i = 0; i < Counts.Count; i++)
                {
                    Relation r = Model.Relation(Counts[i].Name);
                    Query constrain = Counts[i].Constrain == null ? null
                        : call.Call(Counts[i].Constrain, r.Base(call)) as Query ?? throw new KScriptException($"The function given to ->with_count({r.Name}: ...) must return the query.", "Type");
                    w.Append(", ").Expr(r.Subquery(call, Table, "c" + (i + 1).ToString(CultureInfo.InvariantCulture), "COUNT(*)", constrain: constrain)).Append(" AS ").Name(r.Name + "_count");
                }
            }
            w.Append(" FROM ").Name(Table);
            WriteWhere(w);
            if (withOrder && Order.Count > 0)
            {
                w.Append(" ORDER BY ");
                for (int i = 0; i < Order.Count; i++)
                {
                    if (i > 0) w.Append(", ");
                    w.Expr(Order[i].Expr).Append(d.Direction(Order[i].Descending));
                }
            }
            if (withOrder) w.Append(d.Limit(Limit, Offset));
            if (LockMode != null && withOrder) w.Append(d.Lock(LockMode, SkipLocked));
            return w.Build();
        }

        /// <summary>An aggregate (COUNT(*), SUM(x)...) of the rows the query reads; limit, offset and distinct are kept by counting a subquery.</summary>
        private SqlStatement Aggregate(SqlExpr aggregate)
        {
            Dialect d = Database.Dialect;
            if (Limit != null || Offset != null || Distinct)
            {
                // Aggregates only name columns, so all the parameters are the inner query's.
                SqlStatement inner = (this with { Counts = Array.Empty<(string, ICallable)>() }).SelectStatement();
                string head = new SqlWriter(d).Expr(aggregate).Build().Text;
                return new SqlStatement("SELECT " + head + " AS v FROM (" + inner.Text + ") AS q", inner.Parameters);
            }
            var sw = new SqlWriter(d).Append("SELECT ").Expr(aggregate).Append(" AS v FROM ").Name(Table);
            WriteWhere(sw);
            return sw.Build();
        }

        #endregion

        #region Reading

        /// <summary>The call a query's relation counts and scopes run with, set when the query runs.</summary>
        private CallContext CountCall { get; init; }

        /// <summary>The statement, with what it needs to run a scope in ->with_count().</summary>
        private SqlStatement Statement(CallContext call) => (Counts.Count > 0 ? this with { CountCall = call } : this).SelectStatement();

        public List<object> Get(CallContext call)
        {
            List<object> records = Database.Rows(call.Interpreter, Statement(call), Table).Select(r => Records.Load(this, r, call)).ToList();
            if (Model == null || records.Count == 0) return records;
            List<Record> batch = records.Cast<Record>().ToList();
            if (batch.Count > 1)
            {
                foreach (Record r in batch) Records.State(r).Batch = batch;
            }
            if (Withs.Count > 0) Data.Relation.LoadPaths(call, Model, batch, Withs);
            return records;
        }

        public object First(CallContext call) => (Limit == null ? Taking(1) : this).Get(call).FirstOrDefault();

        public object FirstOrFail(CallContext call, object id = null) =>
            First(call) ?? throw Records.NotFound(Model, Table, id);

        public Query ById(object id)
        {
            if (Model == null) return And(new SqlBinary("=", new SqlColumn("id"), new SqlValue(id)));
            Column key = Model.KeyColumn;
            return And(new SqlBinary("=", new SqlColumn(key.Name, key), new SqlValue(id, key)));
        }

        public double Count(CallContext call) =>
            Convert.ToDouble(Database.Rows(call.Interpreter, Aggregate(new SqlCall("COUNT")), Table)[0].Values.First(), CultureInfo.InvariantCulture);

        public object Aggregate(CallContext call, string function, string column)
        {
            SqlColumn c = ColumnNamed(column, $" for ->{function.ToLowerInvariant()}()");
            List<Dictionary<string, object>> rows = Database.Rows(call.Interpreter, Aggregate(new SqlCall(function, c)), Table);
            object raw = rows.Count == 0 ? null : rows[0].Values.FirstOrDefault();
            if (raw is DBNull) raw = null;
            if (function == "SUM") return raw == null ? 0.0 : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            if (raw == null) return null;
            if (function == "AVG") return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            // ->min() and ->max() are a value of the column: a number, a date, text.
            return Rows.FromDb(raw, c.Column, call.Interpreter);
        }

        public bool Exists(CallContext call) => Database.Rows(call.Interpreter, (this with { Distinct = false }).Taking(1).SelectStatement("1"), Table).Count > 0;

        public List<object> Pluck(CallContext call, string column)
        {
            SqlColumn c = ColumnNamed(column, " for ->pluck()");
            return Database.Rows(call.Interpreter, SelectStatement(Database.Dialect.Quote(c.Name)), Table)
                .Select(r => Model == null ? Rows.Plain(r.Values.First()) : Rows.FromDb(r.Values.First(), c.Column, call.Interpreter)).ToList();
        }

        public object Value(CallContext call, string column) => Taking(1).Pluck(call, column).FirstOrDefault();

        public Dictionary<string, object> Paginate(CallContext call, int perPage, int page)
        {
            if (perPage < 1) throw new KScriptException("->paginate() needs per_page of 1 or more.", "Argument");
            if (page < 1) page = 1;
            double total = (this with { Limit = null, Offset = null }).Count(call);
            int last = Math.Max(1, (int)Math.Ceiling(total / perPage));
            // A page past the last one a database can count to has no rows, like any page past the end.
            long skip = (long)(page - 1) * perPage;
            List<object> items = skip > int.MaxValue ? new List<object>() : Taking(perPage).Skipping((int)skip).Get(call);
            int from = items.Count == 0 ? 0 : (int)skip + 1;
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["items"] = items,
                ["page"] = (double)page,
                ["per_page"] = (double)perPage,
                ["total"] = total,
                ["last_page"] = (double)last,
                ["from"] = (double)from,
                ["to"] = (double)(items.Count == 0 ? 0 : from + items.Count - 1),
                ["has_more"] = page < last,
            };
        }

        public Dictionary<string, object> CursorPaginate(CallContext call, int perPage, object after, string key, bool descending)
        {
            if (perPage is < 1 or > 1000) throw call.Error("per_page must be between 1 and 1000.", "Argument");
            key ??= Model?.Key ?? throw call.Error("Specify a unique, non-null key column for cursor pagination.", "Argument");
            if (Columns != null && !Columns.Contains(key)) throw call.Error($"Select '{key}' to use it as a cursor.", "Argument");
            SqlColumn column = ColumnNamed(key, " for cursor pagination");
            var query = this with { Order = new[] { ((SqlExpr)column, descending) }, Offset = null, Limit = perPage + 1 };
            if (after != null)
                query = query.And(new SqlBinary(descending ? "<" : ">", column, new SqlValue(after, column.Column)));
            List<object> items = query.Get(call);
            if (items.Any(item => item is not Dictionary<string, object> row || row.GetValueOrDefault(key) == null))
                throw call.Error($"Cursor key '{key}' must have a value on every row.", "Argument");
            bool more = items.Count > perPage;
            if (more) items.RemoveAt(perPage);
            return new Dictionary<string, object>
            {
                ["items"] = items,
                ["has_more"] = more,
                ["next"] = more ? ((Dictionary<string, object>)items[^1])[key] : null,
            };
        }

        /// <summary>Reads the rows a few at a time, in key order, calling <paramref name="fn"/> with each batch. Returning false stops.</summary>
        public void Chunk(CallContext call, int size, ICallable fn)
        {
            if (size < 1) throw new KScriptException("->chunk() needs a size of 1 or more.", "Argument");
            Query ordered = Order.Count > 0 ? this : OrderBy(Model != null ? new SqlColumn(Model.Key, Model.KeyColumn) : Database.Dialect.RowOrder, false);
            int? left = Limit;
            for (int page = 0; ; page++)
            {
                // ->limit(n) is the most rows read in all, so the last batch may be smaller.
                int take = left is int rest ? Math.Min(size, rest) : size;
                if (take == 0) return;
                List<object> batch = ordered.Taking(take).Skipping((Offset ?? 0) + page * size).Get(call);
                if (batch.Count == 0) return;
                if (call.Call(fn, batch, (double)page) is false) return;
                if (batch.Count < size) return;
                if (left != null) left -= batch.Count;
            }
        }

        #endregion

        #region Writing

        /// <summary>
        /// A statement that changes rows has no LIMIT or OFFSET that every database takes, so a query with one is
        /// refused, rather than changing every row that matches.
        /// </summary>
        private void NeedNoLimit(string method)
        {
            if (Limit == null && Offset == null) return;
            string key = Model?.Key ?? "id";
            throw new KScriptException($"->{method}() changes every row that matches, so it can't follow ->limit() or ->offset(). "
                + $"Read the keys first, then change those rows: ->where({key}: $query->pluck({key}))->{method}(...).", "Argument");
        }

        /// <summary>The method name <see cref="Adopt"/> reports in errors.</summary>
        private const string Adopting = "adopt";

        /// <summary>
        /// Moves every row of an owned model from a guest to a user, deleted rows too: a guest's rows become the
        /// account's when the guest signs in. Returns how many rows moved.
        /// </summary>
        public static double Adopt(CallContext call, ModelInfo model, object guest, object user)
        {
            string guestColumn = model.OwnerRelation(ModelInfo.OwnerGuest).ForeignKey, userColumn = model.OwnerRelation(ModelInfo.OwnerUser).ForeignKey;
            var rows = new Query(Database.For(call, model), model.Table, model).WithTrashed("with");
            rows = rows.And(rows.Equalities(new[] { new KeyValuePair<string, object>(guestColumn, guest) }, Adopting));
            return rows.Update(call, new[] { new KeyValuePair<string, object>(userColumn, user), new KeyValuePair<string, object>(guestColumn, null) }, method: Adopting);
        }

        /// <summary>UPDATE ... SET for every row the query reads. Returns how many rows changed.</summary>
        public double Update(CallContext call, IEnumerable<KeyValuePair<string, object>> values, bool converted = false, string method = "update")
        {
            NeedNoLimit(method);
            Dialect d = Database.Dialect;
            var w = new SqlWriter(d).Append("UPDATE ").Name(Table).Append(" SET ");
            bool any = false;
            var list = values.ToList();
            if (Model?.Timestamps == true && !list.Any(v => v.Key == ModelInfo.UpdatedAt))
                list.Add(new KeyValuePair<string, object>(ModelInfo.UpdatedAt, Rows.Clock()));
            foreach (KeyValuePair<string, object> v in list)
            {
                SqlColumn c = ColumnNamed(v.Key, " for ->update()");
                object value = v.Value;
                // author: $user sets author_id to the user's key, as ->where(author: $user) compares it.
                if (Model?.Relation(v.Key) is Relation r && value is Record target) value = ModelInfo.KeyOfTarget(r, target);
                if (Model != null && !converted) value = Records.Convert(call, Model, c.Name, value);
                if (any) w.Append(", ");
                w.Name(c.Name).Append(" = ").Expr(new SqlValue(value, c.Column));
                any = true;
            }
            if (!any) return 0;
            WriteWhere(w);
            return Database.Execute(call.Interpreter, w.Build(), Table);
        }

        /// <summary>Adds to a number column on every row the query reads.</summary>
        public double Increment(CallContext call, string column, double by, string method = "increment")
        {
            NeedNoLimit(method);
            SqlColumn c = ColumnNamed(column, $" for ->{method}()");
            if (c.Column != null && c.Column.Kind is not (ColumnKind.Integer or ColumnKind.Real))
                throw new KScriptException($"->{method}() needs a number column, but {Model.Name}.{column} is {c.Column.Field.Type}.", "Type");
            if (c.Column?.Kind == ColumnKind.Integer && by != Math.Floor(by))
                throw new KScriptException($"{Model.Name}.{column} holds whole numbers, so ->{method}() can't change it by {Values.FormatNumber(Math.Abs(by))}.", "Argument");
            Dialect d = Database.Dialect;
            var w = new SqlWriter(d).Append("UPDATE ").Name(Table).Append(" SET ").Name(c.Name).Append(" = ").Name(c.Name).Append(" + ").Expr(new SqlValue(by, c.Column));
            if (Model?.Timestamps == true) w.Append(", ").Name(ModelInfo.UpdatedAt).Append(" = ").Expr(new SqlValue(Rows.Clock()));
            WriteWhere(w);
            return Database.Execute(call.Interpreter, w.Build(), Table);
        }

        /// <summary>Deletes every row the query reads: soft-deleting models only mark them, unless <paramref name="force"/>.</summary>
        public double Delete(CallContext call, bool force)
        {
            NeedNoLimit(force ? "force_delete" : "delete");
            if (Model?.SoftDeletes == true && !force) return Update(call, new[] { new KeyValuePair<string, object>(ModelInfo.DeletedAt, Rows.Clock()) }, method: "delete");
            var w = new SqlWriter(Database.Dialect).Append("DELETE FROM ").Name(Table);
            WriteWhere(w);
            return Database.Execute(call.Interpreter, w.Build(), Table);
        }

        /// <summary>Brings back soft-deleted rows.</summary>
        public double Restore(CallContext call)
        {
            if (Model?.SoftDeletes != true) throw new KScriptException($"->restore() is for models with soft_deletes: true, and {Model?.Name ?? Table} doesn't have it.", "Argument");
            return (this with { Trashed = "only" }).Update(call, new[] { new KeyValuePair<string, object>(ModelInfo.DeletedAt, null) }, method: "restore");
        }

        /// <summary>Inserts rows into a table with no model (a model uses ->create()). Returns how many were added.</summary>
        public double Insert(CallContext call, object rows)
        {
            List<Dictionary<string, object>> list = rows switch
            {
                Dictionary<string, object> one => new List<Dictionary<string, object>> { one },
                List<object> many => many.Select(r => r as Dictionary<string, object> ?? throw new KScriptException($"->insert() needs maps, one per row, but one is {Values.Describe(r)}.", "Argument")).ToList(),
                _ => throw new KScriptException($"->insert() needs a map, or a list of maps, but got {Values.Describe(rows)}.", "Argument"),
            };
            if (list.Count == 0) return 0;
            return Database.InTransaction(call.Interpreter, () =>
            {
                if (Model != null) return (double)list.Select(r => Records.Create(call, Model, r)).Count();
                return (double)list.Sum(row => Database.Execute(call.Interpreter, InsertStatement(Database.Dialect, Table,
                    row.Select(kv => (kv.Key, (SqlExpr)new SqlValue(kv.Value))).ToList(), null), Table));
            });
        }

        public double Upsert(CallContext call, Dictionary<string, object> row, string key)
        {
            key ??= Model?.Key ?? throw call.Error("upsert requires a unique conflict key.", "Argument");
            ColumnNamed(key, " for upsert");
            if (row.GetValueOrDefault(key) == null) throw call.Error($"upsert requires a value for '{key}'.", "Argument");
            if (Model != null && Model.Guarded.Contains(key)) throw call.Error($"'{key}' is a guarded field.", "Argument");
            var values = Model == null
                ? row.Select(entry => (entry.Key, (SqlExpr)new SqlValue(entry.Value))).ToList()
                : Records.InsertValues(Model, Records.Prepare(call, Model, row));
            var update = values.Select(value => value.Item1).Where(name => name != key && (Model == null
                || (name != Model.Key && name != ModelInfo.CreatedAt && (row.ContainsKey(name) || (Model.Timestamps && name == ModelInfo.UpdatedAt))))).ToList();
            Dialect dialect = Database.Dialect;
            SqlStatement insert = InsertStatement(dialect, Table, values, null);
            if (dialect is MySqlDialect)
            {
                // ON DUPLICATE KEY can match another unique index. Keep that row intact and report the conflict.
                string keyColumn = dialect.Quote(key);
                var writeColumns = update.Count > 0 ? update : new List<string> { key };
                string assignments = string.Join(", ", writeColumns.Select(name =>
                {
                    string column = dialect.Quote(name);
                    return $"{column} = IF({keyColumn} <=> VALUES({keyColumn}), VALUES({column}), {column})";
                }));
                return Database.InTransaction(call.Interpreter, () =>
                {
                    int changed = Database.Execute(call.Interpreter, new SqlStatement(insert.Text + " ON DUPLICATE KEY UPDATE " + assignments, insert.Parameters), Table);
                    var sought = new Query(Database, Table, null).And(new SqlBinary("=", new SqlColumn(key, null), values.Single(v => v.Item1 == key).Item2));
                    if (!sought.Exists(call))
                        throw call.Error($"upsert encountered a unique conflict on a column other than '{key}'.", "UniqueViolation");
                    return changed;
                });
            }
            string sql = update.Count == 0 ? dialect.InsertOrIgnore(insert.Text, key) : dialect.Upsert(insert.Text, key, update);
            return Database.Execute(call.Interpreter, new SqlStatement(sql, insert.Parameters), Table);
        }

        internal static SqlStatement InsertStatement(Dialect d, string table, List<(string Name, SqlExpr Value)> values, string returning)
        {
            var w = new SqlWriter(d).Append("INSERT INTO ").Name(table);
            if (values.Count == 0)
            {
                w.Append(d.DefaultValues);
            }
            else
            {
                w.Append(" (").Append(string.Join(", ", values.Select(v => d.Quote(v.Name)))).Append(") VALUES (");
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0) w.Append(", ");
                    w.Expr(values[i].Value);
                }
                w.Append(")");
            }
            if (returning != null) w.Append(" RETURNING ").Name(returning);
            return w.Build();
        }

        #endregion

        /// <summary>The SQL and its parameters, without running it: <c>->sql()</c>.</summary>
        public Dictionary<string, object> Describe()
        {
            SqlStatement s = SelectStatement();
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sql"] = s.Text,
                ["params"] = s.Parameters.Select(Rows.Plain).ToList(),
            };
        }
    }
}
