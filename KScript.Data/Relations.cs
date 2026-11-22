using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Data
{
    /// <summary>How two models are linked, worked out from the field's type and the fields of the other model.</summary>
    internal enum RelationKind
    {
        /// <summary><c>author: User</c>: this table has author_id.</summary>
        BelongsTo,

        /// <summary><c>profile: Profile?</c> where Profile has <c>user: User</c>: the other table has user_id, and one row is this one's.</summary>
        HasOne,

        /// <summary><c>posts: Post[]</c> where Post has <c>author: User</c>: the other table has author_id.</summary>
        HasMany,

        /// <summary><c>tags: Tag[]</c> where Tag has no field that is one Post: a pivot table (post_tag) links them.</summary>
        ManyToMany,

        /// <summary><c>comments: Comment[](through: posts)</c>: the comments of this one's posts.</summary>
        Through,
    }

    /// <summary>
    /// A field whose type is another model: <c>author: User</c>, <c>posts: Post[]</c>. Reading it on a record loads
    /// the related records (for the whole list the record came in, so a loop makes one query, not one per record),
    /// <c>$user->posts()</c> is a query of them, and in a query lambda <c>$u.posts->count()</c> is SQL.
    /// </summary>
    internal sealed class Relation
    {
        public string Name { get; init; }

        public ModelInfo Owner { get; init; }

        public ModelInfo Target { get; init; }

        public RelationKind Kind { get; init; }

        /// <summary>For belongs-to: the column may be null.</summary>
        public bool Optional { get; init; }

        /// <summary>The column that links the tables: on this table for belongs-to (author_id), on the other for has-one and has-many.</summary>
        public string ForeignKey { get; init; }

        public string Pivot { get; init; }

        /// <summary>The pivot table's column that holds this model's key (post_id).</summary>
        public string PivotOwner { get; init; }

        /// <summary>The pivot table's column that holds the other model's key (tag_id).</summary>
        public string PivotTarget { get; init; }

        /// <summary>For through: the relation it goes through, by name.</summary>
        public string ThroughName { get; init; }

        /// <summary>A scope of the other model that filters the relation: <c>drafts: Post[](by: author, scope: drafts)</c>.</summary>
        public string Scope { get; init; }

        /// <summary>For belongs-to: what deleting the other row does to this one. cascade, restrict or set null.</summary>
        public string OnDelete { get; init; }

        private Relation via;
        private Relation next;

        /// <summary>For through: the relation on this model it goes through (posts).</summary>
        public Relation Via => via ??= ResolveVia();

        /// <summary>For through: the relation from the model it goes through to the target (Post.comments).</summary>
        public Relation Next => next ??= ResolveNext();

        /// <summary>True when the relation is a list of records rather than one.</summary>
        public bool ToMany => Kind switch
        {
            RelationKind.HasMany or RelationKind.ManyToMany => true,
            RelationKind.Through => Next.ToMany || Via.ToMany,
            _ => false,
        };

        public string Where => $"{Owner.Name}.{Name}";

        /// <summary>The relation as hover and errors show it: "has many Post (Post.author_id)".</summary>
        public string Describe() => Kind switch
        {
            RelationKind.BelongsTo => $"belongs to {Target.Name} ({Owner.Table}.{ForeignKey})",
            RelationKind.HasOne => $"has one {Target.Name} ({Target.Table}.{ForeignKey})",
            RelationKind.HasMany => $"has many {Target.Name} ({Target.Table}.{ForeignKey})",
            RelationKind.ManyToMany => $"many to many with {Target.Name} (pivot table {Pivot})",
            _ => $"{(ToMany ? "has many" : "has one")} {Target.Name} through {ThroughName}",
        };

        #region Working out the kind

        /// <summary>The relation a field of <paramref name="owner"/> is, given the model its type names.</summary>
        public static Relation Infer(ModelInfo owner, FieldDefinition field, ModelInfo target)
        {
            IReadOnlyDictionary<string, object> args = owner.RelationArgs.GetValueOrDefault(field.Name) ?? new Dictionary<string, object>();
            string Option(string name) => args.TryGetValue(name, out object v) && v != null ? Values.ToStr(v).Trim() : null;
            string by = Option("by");
            string through = Option("through");
            string pivot = Option("pivot");
            string scope = Option("scope");
            string onDelete = Option("on_delete")?.Replace('_', ' ').ToLowerInvariant();
            bool list = field.Type.ListDepth == 1;
            string where = $"{owner.Name}.{field.Name}";

            if (scope != null && !target.IsScope(scope))
            {
                string close = Values.Closest(scope, target.Type.OwnMethods.Keys);
                throw new KScriptException($"{where} uses the scope '{scope}', but {target.Name} has no scope by that name." + (close != null ? $" Did you mean {close}?" : string.Empty)
                    + $" A scope is a fn whose first parameter is query: fn {scope}(query) => $query->where(...).", "Type");
            }
            if (onDelete != null && onDelete is not ("cascade" or "restrict" or "set null"))
                throw new KScriptException($"{where}: on_delete is cascade, restrict or set_null, not '{Option("on_delete")}'.", "Type");

            // The other model's fields that are one record of this model: the other side of a has-one or has-many.
            List<FieldDefinition> backs = target.Type.Fields
                .Where(g => !g.Computed && g.Type.ListDepth == 0 && !(target == owner && g.Name == field.Name) && target.ModelOf(g.Type) == owner)
                .ToList();

            if (through != null)
            {
                return new Relation { Name = field.Name, Owner = owner, Target = target, Kind = RelationKind.Through, ThroughName = through, Scope = scope };
            }

            if (onDelete != null && (list || by != null))
                throw new KScriptException($"{where}: on_delete is for a field that is one record, such as author: User(on_delete: cascade).", "Type");

            if (by != null)
            {
                FieldDefinition back = target.Type.Field(by);
                if (back == null || !backs.Contains(back))
                {
                    string close = Values.Closest(by, backs.Select(b => b.Name));
                    throw new KScriptException($"{where} is by {by}, but {target.Name} has no field {by} that is one {owner.Name}." + (close != null ? $" Did you mean {close}?" : string.Empty)
                        + $" Add {by}: {owner.Name} to {target.Name}.", "Type");
                }
                return new Relation { Name = field.Name, Owner = owner, Target = target, Kind = list ? RelationKind.HasMany : RelationKind.HasOne, ForeignKey = ForeignKeyName(by, owner), Scope = scope };
            }

            if (!list)
            {
                bool required = owner.DeclaredRequired(field.Name);
                if (backs.Count == 1)
                {
                    bool backRequired = target.DeclaredRequired(backs[0].Name);
                    // profile: Profile? here and user: User there: the profile holds user_id, so this side has one.
                    if (!required && backRequired)
                        return new Relation { Name = field.Name, Owner = owner, Target = target, Kind = RelationKind.HasOne, ForeignKey = ForeignKeyName(backs[0].Name, owner), Scope = scope };
                    if (required == backRequired)
                        throw new KScriptException($"{where} and {target.Name}.{backs[0].Name} point at each other, so it isn't clear which table holds the link. "
                            + $"Make the side that has one optional, such as {field.Name}: {target.Name}?, or name the other side's field: {field.Name}: {target.Name}(by: {backs[0].Name})?.", "Type");
                }
                return new Relation
                {
                    Name = field.Name,
                    Owner = owner,
                    Target = target,
                    Kind = RelationKind.BelongsTo,
                    ForeignKey = ForeignKeyName(field.Name, target),
                    Optional = !required,
                    OnDelete = onDelete ?? (required ? "cascade" : "set null"),
                    Scope = scope,
                };
            }

            if (backs.Count == 1)
                return new Relation { Name = field.Name, Owner = owner, Target = target, Kind = RelationKind.HasMany, ForeignKey = ForeignKeyName(backs[0].Name, owner), Scope = scope };
            if (backs.Count > 1)
                throw new KScriptException($"{where} is a list of {target.Name}, and {target.Name} has {backs.Count} fields that are a {owner.Name} ({Values.OrList(backs.Select(b => b.Name).ToList())}). "
                    + $"Say which one links them: {field.Name}: {target.Name}[](by: {backs[0].Name}).", "Type");

            // Nothing on the other side is one of these, so a pivot table links them.
            string ownerColumn = ModelInfo.Snake(owner.Name) + "_" + owner.Key;
            string targetColumn = (owner == target ? ModelInfo.Singular(field.Name) : ModelInfo.Snake(target.Name)) + "_" + target.Key;
            string table = pivot ?? (owner == target
                ? ModelInfo.Snake(owner.Name) + "_" + field.Name
                : string.Join("_", new[] { ModelInfo.Snake(owner.Name), ModelInfo.Snake(target.Name) }.OrderBy(n => n, StringComparer.Ordinal)));
            if (!ModelInfo.IsName(table)) throw new KScriptException($"{where}: '{table}' can't be a table name: use letters, digits and _.", "Type");
            return new Relation
            {
                Name = field.Name,
                Owner = owner,
                Target = target,
                Kind = RelationKind.ManyToMany,
                Pivot = table,
                PivotOwner = ownerColumn,
                PivotTarget = targetColumn,
                Scope = scope,
            };
        }

        /// <summary>The column of a belongs-to field: the field and the other model's key, author_id.</summary>
        public static string ForeignKeyName(string field, ModelInfo target) => field + "_" + target.Key;

        private Relation ResolveVia()
        {
            Relation first = Owner.Relation(ThroughName);
            if (first == null)
            {
                string close = Values.Closest(ThroughName, Owner.Relations.Keys);
                throw new KScriptException($"{Where} is through {ThroughName}, but {Owner.Name} has no relation by that name." + (close != null ? $" Did you mean {close}?" : string.Empty), "Type");
            }
            if (first.Kind is not (RelationKind.HasMany or RelationKind.HasOne))
                throw new KScriptException($"{Where} is through {ThroughName}, which {first.Describe()}: through works with a has-one or has-many relation, such as posts: Post[].", "Type");
            return first;
        }

        private Relation ResolveNext()
        {
            ModelInfo middle = Via.Target;
            List<Relation> options = middle.Relations.Values.Where(r => r.Target == Target && r.Kind is RelationKind.HasMany or RelationKind.HasOne).ToList();
            Relation second = options.FirstOrDefault(r => r.Name == Name) ?? (options.Count == 1 ? options[0] : null);
            if (second == null)
                throw new KScriptException($"{Where} is through {ThroughName}, so {middle.Name} needs a field that lists {Target.Name} records, such as {Name}: {Target.Name}[]"
                    + (options.Count > 1 ? $". It has more than one ({Values.OrList(options.Select(o => o.Name).ToList())}): give this field the same name as one." : "."), "Type");
            return second;
        }

        #endregion

        #region Queries

        /// <summary>A query of the related model, with the relation's scope applied.</summary>
        public Query Base(CallContext call)
        {
            var q = new Query(Database.For(call, Target), Target.Table, Target);
            return Scope != null ? Target.ApplyScope(call, q, Scope, Array.Empty<object>(), null) : q;
        }

        /// <summary>The value a record links by: its belongs-to column, or its key.</summary>
        public object OwnerValue(Record owner) => Kind == RelationKind.BelongsTo ? owner.GetValueOrDefault(ForeignKey) : owner.GetValueOrDefault(Owner.Key);

        private static SqlExpr In(SqlColumn column, IEnumerable<object> keys) =>
            new SqlBinary("IN", column, new SqlList(keys.Select(k => (SqlExpr)new SqlValue(k, column.Column)).ToList()));

        /// <summary>The rows of the related model that belong to records with these values (see <see cref="OwnerValue"/>), as a condition on its table.</summary>
        public SqlExpr Condition(IReadOnlyList<object> keys)
        {
            switch (Kind)
            {
                case RelationKind.BelongsTo:
                    return In(new SqlColumn(Target.Key, Target.KeyColumn), keys);
                case RelationKind.HasOne:
                case RelationKind.HasMany:
                    return In(new SqlColumn(ForeignKey, Target.Column(ForeignKey)), keys);
                case RelationKind.ManyToMany:
                {
                    var pivot = new SqlSubquery(null, Pivot, null, In(new SqlColumn(PivotOwner, PivotOwnerColumn, Pivot), keys)) { SelectExpr = new SqlColumn(PivotTarget, PivotTargetColumn, Pivot) };
                    return new SqlBinary("IN", new SqlColumn(Target.Key, Target.KeyColumn), pivot);
                }
                default:
                {
                    ModelInfo middle = Via.Target;
                    SqlExpr where = In(new SqlColumn(Via.ForeignKey, middle.Column(Via.ForeignKey), middle.Table), keys);
                    if (middle.SoftDeletes) where = new SqlBinary("AND", where, new SqlUnary("IS NULL", new SqlColumn(ModelInfo.DeletedAt, null, middle.Table)));
                    var mids = new SqlSubquery(null, middle.Table, null, where) { SelectExpr = new SqlColumn(middle.Key, middle.KeyColumn, middle.Table) };
                    return new SqlBinary("IN", new SqlColumn(Next.ForeignKey, Target.Column(Next.ForeignKey)), mids);
                }
            }
        }

        /// <summary><c>$user->posts()</c>: a query of one record's related records, which ->create() (and for a pivot, ->attach()) adds to.</summary>
        public Query For(CallContext call, Record owner)
        {
            object value = OwnerValue(owner);
            return Base(call).And(Condition(value == null ? Array.Empty<object>() : new[] { value })).Of(this, owner);
        }

        /// <summary>The column a pivot table keeps this model's key in: stored like the key.</summary>
        public Column PivotOwnerColumn => PivotColumn(PivotOwner, Owner);

        public Column PivotTargetColumn => PivotColumn(PivotTarget, Target);

        private Column pivotOwnerColumn;
        private Column pivotTargetColumn;

        private Column PivotColumn(string name, ModelInfo model)
        {
            ref Column cached = ref (model == Owner && name == PivotOwner ? ref pivotOwnerColumn : ref pivotTargetColumn);
            if (cached != null) return cached;
            Column key = model.KeyColumn;
            return cached = new Column { Name = name, Field = key.Field, Kind = key.Kind, Length = key.Length, Words = key.Words };
        }

        #endregion

        #region Loading

        internal static string KeyText(object value) => value == null ? null : Values.ToStr(value);

        /// <summary>
        /// Loads the relation for each record, in as few queries as the kind needs (one, or two through a pivot or
        /// another table), and sets it on each record: a record or null, or a list.
        /// </summary>
        public void Load(CallContext call, IReadOnlyList<Record> owners, ICallable constrain = null)
        {
            var keys = owners.Select(OwnerValue).Where(v => v != null).GroupBy(KeyText).Select(g => g.First()).ToList();
            Query Related(SqlExpr condition)
            {
                Query q = Base(call);
                if (constrain != null) q = call.Call(constrain, q) as Query ?? throw new KScriptException($"The function given to ->with({Name}: ...) must return the query.", "Type");
                return q.And(condition);
            }
            List<Record> Read(SqlExpr condition) => keys.Count == 0 ? new List<Record>() : Related(condition).Get(call).Cast<Record>().ToList();

            var linked = new Dictionary<string, List<Record>>(StringComparer.Ordinal);
            void Link(object ownerKey, Record target)
            {
                string k = KeyText(ownerKey);
                if (!linked.TryGetValue(k, out List<Record> list)) linked[k] = list = new List<Record>();
                list.Add(target);
            }

            switch (Kind)
            {
                case RelationKind.BelongsTo:
                    foreach (Record t in Read(Condition(keys))) Link(t.GetValueOrDefault(Target.Key), t);
                    break;
                case RelationKind.HasOne:
                case RelationKind.HasMany:
                    foreach (Record t in Read(Condition(keys))) Link(t.GetValueOrDefault(ForeignKey), t);
                    break;
                case RelationKind.ManyToMany:
                {
                    if (keys.Count == 0) break;
                    Database db = Database.For(call, Owner);
                    var w = new SqlWriter(db.Dialect).Append("SELECT ").Name(PivotOwner).Append(", ").Name(PivotTarget).Append(" FROM ").Name(Pivot)
                        .Append(" WHERE ").Expr(In(new SqlColumn(PivotOwner, PivotOwnerColumn), keys));
                    var pairs = db.Rows(call.Interpreter, w.Build(), Pivot)
                        .Select(r => (Owner: Rows.FromDb(r[PivotOwner], PivotOwnerColumn), Target: Rows.FromDb(r[PivotTarget], PivotTargetColumn))).ToList();
                    var targets = pairs.Count == 0 ? new List<Record>()
                        : Related(In(new SqlColumn(Target.Key, Target.KeyColumn), pairs.Select(p => p.Target).GroupBy(KeyText).Select(g => g.First()))).Get(call).Cast<Record>().ToList();
                    var byKey = targets.GroupBy(t => KeyText(t.GetValueOrDefault(Target.Key))).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                    foreach ((object o, object t) in pairs)
                    {
                        if (byKey.TryGetValue(KeyText(t), out Record found)) Link(o, found);
                    }
                    break;
                }
                default:
                {
                    if (keys.Count == 0) break;
                    ModelInfo middle = Via.Target;
                    Database db = Database.For(call, middle);
                    var w = new SqlWriter(db.Dialect).Append("SELECT ").Name(middle.Key).Append(", ").Name(Via.ForeignKey).Append(" FROM ").Name(middle.Table)
                        .Append(" WHERE ").Expr(In(new SqlColumn(Via.ForeignKey, middle.Column(Via.ForeignKey)), keys));
                    if (middle.SoftDeletes) w.Append(" AND ").Name(ModelInfo.DeletedAt).Append(" IS NULL");
                    var mids = db.Rows(call.Interpreter, w.Build(), middle.Table)
                        .Select(r => (Key: Rows.FromDb(r[middle.Key], middle.KeyColumn), Owner: Rows.FromDb(r[Via.ForeignKey], middle.Column(Via.ForeignKey)))).ToList();
                    var ownerOf = mids.GroupBy(m => KeyText(m.Key)).ToDictionary(g => g.Key, g => g.First().Owner, StringComparer.Ordinal);
                    var targets = mids.Count == 0 ? new List<Record>()
                        : Related(In(new SqlColumn(Next.ForeignKey, Target.Column(Next.ForeignKey)), mids.Select(m => m.Key))).Get(call).Cast<Record>().ToList();
                    foreach (Record t in targets)
                    {
                        if (ownerOf.TryGetValue(KeyText(t.GetValueOrDefault(Next.ForeignKey)), out object o)) Link(o, t);
                    }
                    break;
                }
            }

            foreach (Record owner in owners)
            {
                string k = KeyText(OwnerValue(owner));
                List<Record> found = k != null && linked.TryGetValue(k, out List<Record> l) ? l : new List<Record>();
                owner[Name] = ToMany ? found.Cast<object>().ToList() : found.FirstOrDefault();
            }
        }

        /// <summary>
        /// Loads relations, and the relations of those (<c>posts.tags</c>), for a list of records. Each entry is a
        /// relation name or a dotted path, with a function that changes the query of the last relation in it.
        /// </summary>
        public static void LoadPaths(CallContext call, ModelInfo model, IReadOnlyList<Record> records, IEnumerable<(string Path, ICallable Constrain)> paths)
        {
            foreach ((string path, ICallable constrain) in paths)
            {
                IReadOnlyList<Record> level = records;
                ModelInfo at = model;
                string[] parts = path.Split('.');
                for (int i = 0; i < parts.Length && level.Count > 0; i++)
                {
                    Relation r = at.NeedRelation(parts[i], " for ->with()");
                    bool last = i == parts.Length - 1;
                    // A relation loaded earlier in the path (posts in posts.tags, after with(posts)) is kept as it is.
                    List<Record> missing = last ? level.ToList() : level.Where(x => !x.ContainsKey(r.Name)).ToList();
                    if (missing.Count > 0) r.Load(call, missing, last ? constrain : null);
                    level = level.SelectMany(x => x.GetValueOrDefault(r.Name) switch
                    {
                        Record one => new[] { one },
                        List<object> many => many.OfType<Record>(),
                        _ => Enumerable.Empty<Record>(),
                    }).Distinct().ToList();
                    at = r.Target;
                }
            }
        }

        #endregion

        #region Subqueries

        /// <summary>
        /// A query on the related table inside another query, for counts and conditions: <c>$u.posts->count()</c> is
        /// <c>(SELECT COUNT(*) FROM posts AS r1 WHERE r1.author_id = users.id)</c>. <paramref name="outer"/> is the table
        /// or alias the outer row is read from.
        /// </summary>
        public SqlSubquery Subquery(CallContext call, string outer, string alias, string select, SqlExpr selectExpr = null, SqlExpr condition = null, int? limit = null, Query constrain = null)
        {
            var joins = new List<(string, string, SqlExpr)>();
            SqlExpr where;
            SqlColumn Outer(string name, Column c) => new SqlColumn(name, c, outer);
            SqlColumn Inner(string name, Column c) => new SqlColumn(name, c, alias);
            switch (Kind)
            {
                case RelationKind.BelongsTo:
                    where = new SqlBinary("=", Inner(Target.Key, Target.KeyColumn), Outer(ForeignKey, Owner.Column(ForeignKey)));
                    break;
                case RelationKind.HasOne:
                case RelationKind.HasMany:
                    where = new SqlBinary("=", Inner(ForeignKey, Target.Column(ForeignKey)), Outer(Owner.Key, Owner.KeyColumn));
                    break;
                case RelationKind.ManyToMany:
                {
                    string p = alias + "_p";
                    joins.Add((Pivot, p, new SqlBinary("=", new SqlColumn(PivotTarget, PivotTargetColumn, p), Inner(Target.Key, Target.KeyColumn))));
                    where = new SqlBinary("=", new SqlColumn(PivotOwner, PivotOwnerColumn, p), Outer(Owner.Key, Owner.KeyColumn));
                    break;
                }
                default:
                {
                    ModelInfo middle = Via.Target;
                    string v = alias + "_v";
                    joins.Add((middle.Table, v, new SqlBinary("=", Inner(Next.ForeignKey, Target.Column(Next.ForeignKey)), new SqlColumn(middle.Key, middle.KeyColumn, v))));
                    where = new SqlBinary("=", new SqlColumn(Via.ForeignKey, middle.Column(Via.ForeignKey), v), Outer(Owner.Key, Owner.KeyColumn));
                    if (middle.SoftDeletes) where = new SqlBinary("AND", where, new SqlUnary("IS NULL", new SqlColumn(ModelInfo.DeletedAt, null, v)));
                    break;
                }
            }
            if (Target.SoftDeletes) where = new SqlBinary("AND", where, new SqlUnary("IS NULL", new SqlColumn(ModelInfo.DeletedAt, null, alias)));
            Query scoped = constrain ?? (Scope != null ? Base(call) : null);
            if (scoped?.Where is SqlExpr filter) where = new SqlBinary("AND", where, Qualify(filter, alias, Target.Table));
            if (condition != null) where = new SqlBinary("AND", where, condition);
            return new SqlSubquery(select, Target.Table, alias, where, joins, limit) { SelectExpr = selectExpr };
        }

        /// <summary>
        /// A condition written for a table on its own (a scope's, with unqualified columns) moved inside a subquery
        /// whose table is called <paramref name="alias"/>.
        /// </summary>
        internal static SqlExpr Qualify(SqlExpr e, string alias, string table) => e switch
        {
            SqlColumn c when c.Table == null || c.Table == table => new SqlColumn(c.Name, c.Column, alias),
            SqlBinary b => new SqlBinary(b.Op, Qualify(b.Left, alias, table), Qualify(b.Right, alias, table)),
            SqlUnary u => new SqlUnary(u.Op, Qualify(u.Operand, alias, table)),
            SqlCall f => new SqlCall(f.Name, f.Args.Select(a => Qualify(a, alias, table)).ToArray()),
            SqlList l => new SqlList(l.Items.Select(i => Qualify(i, alias, table)).ToList()),
            SqlMatch m => new SqlMatch(Qualify(m.Target, alias, table), m.Regex),
            SqlCase k => new SqlCase(Qualify(k.When, alias, table), Qualify(k.Then, alias, table), Qualify(k.Otherwise, alias, table)),
            SqlSubquery q => new SqlSubquery(q.Select, q.Table, q.Alias, q.Where == null ? null : Qualify(q.Where, alias, table),
                q.Joins, q.Limit)
            {
                SelectExpr = q.SelectExpr == null ? null : Qualify(q.SelectExpr, alias, table),
                ValueColumn = q.ValueColumn,
            },
            _ => e,
        };

        #endregion

        #region Changing

        /// <summary>The keys of records or values given to ->attach(), ->detach() and ->sync(): records, keys, or lists of either.</summary>
        private List<object> KeysOf(CallContext call, object value, string method)
        {
            var keys = new List<object>();
            void Add(object v)
            {
                switch (v)
                {
                    case null:
                        return;
                    case List<object> list:
                        foreach (object item in list) Add(item);
                        return;
                    case Record r when r.Type.Tag == Target:
                        keys.Add(r.GetValueOrDefault(Target.Key) ?? throw new KScriptException($"->{method}() was given a {Target.Name} that hasn't been saved. Save it first, so it has a {Target.Key}.", "Value"));
                        return;
                    case Record r:
                        throw new KScriptException($"{Where} links {Target.Name} records, so ->{method}() can't take a {r.Type.Name}.", "Type");
                    default:
                        keys.Add(Records.Convert(call, Target, Target.Key, v));
                        return;
                }
            }
            Add(value);
            return keys.GroupBy(KeyText).Select(g => g.First()).ToList();
        }

        private List<object> Linked(CallContext call, Database db, object owner)
        {
            var w = new SqlWriter(db.Dialect).Append("SELECT ").Name(PivotTarget).Append(" FROM ").Name(Pivot).Append(" WHERE ")
                .Expr(new SqlBinary("=", new SqlColumn(PivotOwner, PivotOwnerColumn), new SqlValue(owner, PivotOwnerColumn)));
            return db.Rows(call.Interpreter, w.Build(), Pivot).Select(r => Rows.FromDb(r[PivotTarget], PivotTargetColumn)).ToList();
        }

        private object SavedKey(Record owner, string method) =>
            owner.GetValueOrDefault(Owner.Key) ?? throw new KScriptException($"This {Owner.Name} hasn't been saved, so ->{Name}()->{method}() has nothing to link to. Save it first.", "Value");

        private void NeedPivot(string method)
        {
            if (Kind != RelationKind.ManyToMany)
                throw new KScriptException($"->{method}() links records through a pivot table, and {Where} {Describe()}."
                    + (Kind is RelationKind.HasMany or RelationKind.HasOne ? $" Set the {Target.Name}'s field instead, or use ->{Name}()->create(...)." : string.Empty), "Type");
        }

        /// <summary><c>->attach(ids)</c>: links the records, leaving links already there as they are. Returns how many were added.</summary>
        public double Attach(CallContext call, Record owner, object ids)
        {
            NeedPivot("attach");
            object key = SavedKey(owner, "attach");
            Database db = Database.For(call, Owner);
            var have = new HashSet<string>(Linked(call, db, key).Select(KeyText), StringComparer.Ordinal);
            double added = 0;
            foreach (object id in KeysOf(call, ids, "attach"))
            {
                if (have.Contains(KeyText(id))) continue;
                db.Execute(call.Interpreter, Query.InsertStatement(db.Dialect, Pivot,
                    new List<(string, SqlExpr)> { (PivotOwner, new SqlValue(key, PivotOwnerColumn)), (PivotTarget, new SqlValue(id, PivotTargetColumn)) }, null), Pivot);
                added++;
            }
            owner.Remove(Name);
            return added;
        }

        /// <summary><c>->detach(ids?)</c>: unlinks the records, or every one. Returns how many links were removed.</summary>
        public double Detach(CallContext call, Record owner, object ids, bool all)
        {
            NeedPivot("detach");
            object key = SavedKey(owner, "detach");
            Database db = Database.For(call, Owner);
            var w = new SqlWriter(db.Dialect).Append("DELETE FROM ").Name(Pivot).Append(" WHERE ")
                .Expr(new SqlBinary("=", new SqlColumn(PivotOwner, PivotOwnerColumn), new SqlValue(key, PivotOwnerColumn)));
            if (!all) w.Append(" AND ").Expr(In(new SqlColumn(PivotTarget, PivotTargetColumn), KeysOf(call, ids, "detach")));
            double removed = db.Execute(call.Interpreter, w.Build(), Pivot);
            owner.Remove(Name);
            return removed;
        }

        /// <summary><c>->sync(ids)</c>: links exactly these records. Returns { attached, detached }: the keys added and taken away.</summary>
        public Dictionary<string, object> Sync(CallContext call, Record owner, object ids, bool toggle = false)
        {
            string method = toggle ? "toggle" : "sync";
            NeedPivot(method);
            object key = SavedKey(owner, method);
            Database db = Database.For(call, Owner);
            List<object> wanted = KeysOf(call, ids, method);
            List<object> have = Linked(call, db, key);
            var haveText = new HashSet<string>(have.Select(KeyText), StringComparer.Ordinal);
            var wantedText = new HashSet<string>(wanted.Select(KeyText), StringComparer.Ordinal);
            List<object> attach = wanted.Where(w => !haveText.Contains(KeyText(w))).ToList();
            List<object> detach = toggle ? wanted.Where(w => haveText.Contains(KeyText(w))).ToList() : have.Where(h => !wantedText.Contains(KeyText(h))).ToList();
            db.InTransaction(call.Interpreter, () =>
            {
                if (detach.Count > 0) Detach(call, owner, detach, all: false);
                if (attach.Count > 0) Attach(call, owner, attach);
                return true;
            });
            owner.Remove(Name);
            return new Dictionary<string, object>(StringComparer.Ordinal) { ["attached"] = attach, ["detached"] = detach };
        }

        #endregion
    }

    internal sealed partial class ModelInfo
    {
        /// <summary>The fields declared without ? or a default, before relation fields were made optional.</summary>
        internal HashSet<string> Required { get; } = new HashSet<string>(StringComparer.Ordinal);

        internal bool DeclaredRequired(string field) => Required.Contains(field);

        /// <summary>A relation by name, or an error naming the closest one.</summary>
        public Relation NeedRelation(string name, string where)
        {
            Relation r = Relation(name);
            if (r != null) return r;
            string close = Values.Closest(name, Relations.Keys);
            throw new KScriptException($"{Name} has no relation '{name}'{where}." + (close != null ? $" Did you mean {close}?"
                : Relations.Count > 0 ? $" Its relations are: {string.Join(", ", Relations.Keys)}." : $" A relation is a field whose type is a model, such as author: User."), "Name");
        }

        #region Scopes

        /// <summary>True when the model has a scope by this name: a fn whose first parameter is query.</summary>
        internal bool IsScope(string name) =>
            Type.OwnMethods.TryGetValue(name, out MarkupElement e) && Parameter.ParseList(e.GetAttributeValue("params"), out _) is { Count: > 0 } p && p[0].Name == "query";

        /// <summary>Runs a scope on a query: <c>fn admins(query) => $query->where(role: admin)</c> makes <c>$User->admins()</c>.</summary>
        internal Query ApplyScope(CallContext call, Query query, string name, IReadOnlyList<object> args, IReadOnlyDictionary<string, object> named)
        {
            MarkupElement e = Type.OwnMethods[name];
            Scope s = Type.Closure.Child();
            call.Interpreter.DeclareFunction(e, s, Type.Module);
            s.TryGet(name, out object fn);
            var all = new List<object> { query };
            all.AddRange(args);
            return call.Interpreter.Call((ICallable)fn, all, named, call.Scope, call.Module) as Query
                ?? throw new KScriptException($"The scope {Name}->{name}() must return the query it is given: fn {name}(query) => $query->where(...).", "Type");
        }

        #endregion

        #region Records of the model (IRecordKind)

        /// <summary>Reading a relation that isn't loaded loads it, for every record of the list the record came in that doesn't have it yet.</summary>
        public bool TryGetMember(Interpreter interpreter, Record record, string name, out object value)
        {
            value = null;
            if (!Relations.TryGetValue(name, out Relation r) || record.ContainsKey(name)) return false;
            var call = new CallContext(interpreter, name, Array.Empty<object>(), null, Type.Closure, Type.Module);
            List<Record> owners = Records.State(record)?.Batch?.Where(o => !o.ContainsKey(name)).ToList() ?? new List<Record>();
            if (!owners.Contains(record)) owners.Add(record);
            r.Load(call, owners);
            value = record[name];
            return true;
        }

        public bool HasMethod(Record record, string name) => Relations.ContainsKey(name);

        /// <summary><c>$user->posts()</c>: the relation as a query.</summary>
        public bool TryCallMethod(CallContext call, Record record, string name, out object result)
        {
            result = null;
            if (!Relations.TryGetValue(name, out Relation r)) return false;
            if (call.Args.Count > 0 || call.Named.Count > 0)
                throw new KScriptException($"->{name}() is the {name} relation as a query, so it takes no arguments: ${ModelInfo.Snake(Name)}->{name}()->where(...).", "Argument");
            result = r.For(call, record);
            return true;
        }

        /// <summary><c>$User->admins()</c>: a scope, on a query of the whole model.</summary>
        public bool TryCallTypeMethod(CallContext call, RecordType type, string name, out object result)
        {
            result = null;
            if (!IsScope(name)) return false;
            result = ApplyScope(call, new Query(Database.For(call, this), Table, this), name, call.Args, call.Named);
            return true;
        }

        /// <summary>Keeps a belongs-to relation and its column together: setting author sets author_id, and setting author_id forgets the author loaded before.</summary>
        public void FieldSet(Record record, string name, object value)
        {
            if (Relations.TryGetValue(name, out Relation r))
            {
                if (r.Kind != RelationKind.BelongsTo)
                {
                    record.Remove(name);
                    throw new KScriptException($"{r.Where} {r.Describe()}, so it can't be set on the record. Add to it with ${ModelInfo.Snake(Name)}->{name}()->create(...)"
                        + (r.Kind == RelationKind.ManyToMany ? $", or link records with ->{name}()->attach(...)." : "."), "Type");
                }
                record[r.ForeignKey] = value is Record target ? KeyOfTarget(r, target) : null;
                return;
            }
            Relation owner = ForeignKeyOf(name);
            if (owner != null && record.TryGetValue(owner.Name, out object loaded)
                && !(loaded is Record t && Data.Relation.KeyText(t.GetValueOrDefault(owner.Target.Key)) == Data.Relation.KeyText(value)))
            {
                record.Remove(owner.Name);
            }
        }

        /// <summary>The key of a record given for a belongs-to relation. It must have been saved, so it has one.</summary>
        internal static object KeyOfTarget(Relation r, Record target) =>
            target.GetValueOrDefault(r.Target.Key)
            ?? throw new KScriptException($"{r.Where} was given a {r.Target.Name} that hasn't been saved, so it has no {r.Target.Key} yet. Save it first: ${ModelInfo.Snake(r.Target.Name)}->save().", "Value");

        #endregion
    }
}
