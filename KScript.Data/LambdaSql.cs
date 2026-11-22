using KScript.Runtime;
using KScript.Syntax;

namespace KScript.Data
{
    /// <summary>
    /// Turns a query lambda, <c>fn(u) => $u.age >= 18 and $u.email->ends_with('@acme.com')</c>, into a SQL condition.
    /// The lambda's parameter is the row: <c>$u.age</c> is a column. Every part that doesn't use the row (variables,
    /// calls, literals) is worked out once, as KScript, and sent as a parameter. Anything that can't be SQL is an error
    /// that says why, rather than a query that quietly does something else.
    /// </summary>
    internal sealed class LambdaSql
    {
        private readonly Lambda lambda;
        private readonly ModelInfo model;
        private readonly Interpreter interpreter;
        private readonly string row;

        /// <summary>
        /// The rows the lambda can read: its own parameter, and in a lambda inside a relation's ->any() or ->count(),
        /// the outer lambdas' rows too. Each has its model, and the table or alias its columns are read from (null for
        /// the query's own table, read without one).
        /// </summary>
        private readonly Dictionary<string, (ModelInfo Model, string Table)> rows;

        /// <summary>Numbers the subqueries' aliases (r1, r2...) across the whole condition.</summary>
        private readonly int[] aliases;

        private LambdaSql(Lambda lambda, ModelInfo model, Interpreter interpreter, LambdaSql outer = null, string table = null)
        {
            this.lambda = lambda;
            this.model = model;
            this.interpreter = interpreter;
            row = lambda.Node.Parameters.Count > 0 ? lambda.Node.Parameters[0].Name : null;
            rows = new Dictionary<string, (ModelInfo, string)>(StringComparer.Ordinal);
            if (outer != null)
            {
                // Inside a subquery, the outer rows' columns need their table, or they would be read from the subquery's.
                foreach (KeyValuePair<string, (ModelInfo Model, string Table)> kv in outer.rows) rows[kv.Key] = (kv.Value.Model, kv.Value.Table ?? kv.Value.Model?.Table);
            }
            if (row != null) rows[row] = (model, table);
            aliases = outer?.aliases ?? new int[1];
        }

        /// <summary>The condition a lambda means, for a query on <paramref name="model"/> (null for a table with no model).</summary>
        public static SqlExpr Compile(ICallable fn, ModelInfo model, Interpreter interpreter, string method)
        {
            if (fn is not Lambda lambda)
                throw new KScriptException($"->{method}() needs a condition written as a lambda, such as fn(u) => $u.age >= 18, so it can run in the database. A function declared with fn name() can't be turned into SQL.", "Argument");
            if (lambda.Node.Parameters.Count != 1)
                throw new KScriptException($"The condition in ->{method}() takes one parameter, the row: fn(u) => $u.active.", "Argument");
            return new LambdaSql(lambda, model, interpreter).Condition(lambda.Node.Body);
        }

        private KScriptException Cannot(ExprNode node, string why) =>
            new KScriptException($"The condition {Text(node)} can't run in the database: {why}", "Query");

        /// <summary>The part of the lambda as written, such as '$u.name->reverse()'.</summary>
        private string Text(ExprNode node)
        {
            string text = lambda.Context.Origin?.Value;
            return "'" + (text != null && node.Span.End <= text.Length ? text.Substring(node.Span.Start, node.Span.Length).Trim() : lambda.ToString()) + "'";
        }

        /// <summary>A part used as a condition: a bool column on its own means it is true.</summary>
        private SqlExpr Condition(ExprNode node)
        {
            SqlExpr e = Expr(node);
            if (e is SqlColumn or SqlSubquery { ValueColumn: not null } && ColumnOf(e) is var c && (c == null && e is SqlColumn || c?.Kind == ColumnKind.Bool))
                return new SqlBinary("=", e, new SqlValue(true, c));
            return e;
        }

        private bool UsesRow(ExprNode node) =>
            node.DescendantsAndSelf().Any(n => n is VariableNode v && rows.ContainsKey(v.Name));

        private string NextAlias() => "r" + (++aliases[0]).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private CallContext Call => new CallContext(interpreter, "where", Array.Empty<object>(), null, lambda.Closure, lambda.Context.Module);

        /// <summary>
        /// A row the condition can read: a lambda's parameter (<c>$p</c>), or one record reached from it through
        /// belongs-to and has-one relations (<c>$p.author</c>, <c>$p.author.team</c>). <c>Wrap</c> puts an expression on
        /// that row inside the subqueries that reach it.
        /// </summary>
        private sealed record RowRef(ModelInfo Model, string Table, Func<SqlExpr, Column, SqlExpr> Wrap, string Text);

        /// <summary>The row a part of the condition is, or null when it isn't one.</summary>
        private RowRef Ref(ExprNode node)
        {
            switch (node)
            {
                case VariableNode v when rows.TryGetValue(v.Name, out (ModelInfo Model, string Table) r):
                    return new RowRef(r.Model, r.Table, (e, _) => e, "$" + v.Name);
                case MemberNode m when Ref(m.Target) is RowRef outer && outer.Model?.Relation(m.Name) is Relation rel:
                {
                    if (rel.ToMany)
                        throw Cannot(m, $"{outer.Text}.{m.Name} is a list of {rel.Target.Name} records. Use ->any(), ->count() or ->all() on it, such as {outer.Text}.{m.Name}->any(fn(x) => ...).");
                    string alias = NextAlias();
                    string from = outer.Table ?? outer.Model.Table;
                    return new RowRef(rel.Target, alias,
                        (e, c) => outer.Wrap(new SqlSubquery(null, rel.Target.Table, alias, rel.Subquery(Call, from, alias, null).Where, rel.Subquery(Call, from, alias, null).Joins, 1) { SelectExpr = e, ValueColumn = c }, c),
                        outer.Text + "." + m.Name);
                }
                default:
                    return null;
            }
        }

        private SqlExpr Expr(ExprNode node)
        {
            if (node is IdentifierNode word && !IsDeclared(word.Name))
            {
                // An enum value written without quotes: $u.role == admin.
                return new SqlValue(new BareWord(word.Name, false, false), null);
            }
            if (!UsesRow(node)) return new SqlValue(Evaluate(node), null);

            switch (node)
            {
                case MemberNode m when Ref(m.Target) is RowRef owner:
                {
                    if (owner.Model?.Relation(m.Name) is Relation rel)
                    {
                        if (rel.Kind == RelationKind.BelongsTo) return owner.Wrap(Column(owner, rel.ForeignKey, m), owner.Model.Column(rel.ForeignKey));
                        throw Cannot(m, rel.ToMany
                            ? $"{owner.Text}.{m.Name} is a list of {rel.Target.Name} records. Use ->any(), ->count() or ->all() on it, such as {owner.Text}.{m.Name}->any(fn(x) => ...)."
                            : $"{owner.Text}.{m.Name} is a {rel.Target.Name} record. Read one of its fields, such as {owner.Text}.{m.Name}.{rel.Target.Key}, or compare it with null.");
                    }
                    SqlColumn c = Column(owner, m.Name, m);
                    return owner.Wrap(c, c.Column);
                }

                case MemberNode m:
                    throw Cannot(m, $"only the row's columns can be read, such as ${row}.name, and the fields of records it links to, such as ${row}.author.name.");

                case VariableNode:
                    throw Cannot(node, $"use one of the row's columns, such as ${row}.id, not the whole row.");

                case BinaryNode b:
                    return Binary(b);

                case UnaryNode u when u.Operator == "not":
                    return new SqlUnary("NOT", Condition(u.Operand));

                case UnaryNode u when u.Operator == "-":
                    return new SqlUnary("-", Expr(u.Operand));

                case UnaryNode u:
                    return Expr(u.Operand);

                case ConditionalNode c:
                    return new SqlCase(Condition(c.Condition), Expr(c.WhenTrue), Expr(c.WhenFalse));

                case MethodCallNode call:
                    return Method(call);

                default:
                    throw Cannot(node, "only comparisons, and, or, not, in, arithmetic and string methods such as ->contains() can be turned into SQL.");
            }
        }

        private bool IsDeclared(string name) =>
            lambda.Closure.TryGet(name, out object found) && (found is RecordType || found is ErrorType);

        private object Evaluate(ExprNode node) => interpreter.Evaluate(node, lambda.Closure, lambda.Context);

        private SqlColumn Column(RowRef owner, string name, ExprNode node)
        {
            ModelInfo m = owner.Model;
            if (m == null) return new SqlColumn(name, null, owner.Table);
            Column c = m.Column(name);
            if (c != null) return new SqlColumn(name, c, owner.Table);
            if (m.Type.Field(name) is FieldDefinition f && f.Computed)
                throw Cannot(node, $"{m.Name}.{name} is worked out from other fields, so the database doesn't have it. Use the fields it is made from.");
            return new SqlColumn(name, m.Need(name, " to compare"), owner.Table);
        }

        /// <summary>The column a part reads, when it is one: a column, or a field of a linked record.</summary>
        private static Column ColumnOf(SqlExpr e) => e switch
        {
            SqlColumn c => c.Column,
            SqlSubquery q => q.ValueColumn,
            _ => null,
        };

        /// <summary>A record compared with a link: <c>$p.author == $user</c> compares the author's key.</summary>
        private bool TryLink(BinaryNode b, bool equal, out SqlExpr result)
        {
            result = null;
            ExprNode linkNode = b.Left is MemberNode && UsesRow(b.Left) && !UsesRow(b.Right) ? b.Left : b.Right is MemberNode && UsesRow(b.Right) && !UsesRow(b.Left) ? b.Right : null;
            if (linkNode is not MemberNode m || Ref(m.Target) is not RowRef owner || owner.Model?.Relation(m.Name) is not Relation rel) return false;
            ExprNode other = linkNode == b.Left ? b.Right : b.Left;
            if (IsNull(other))
            {
                if (rel.Kind == RelationKind.BelongsTo)
                {
                    result = owner.Wrap(new SqlUnary(equal ? "IS NULL" : "IS NOT NULL", Column(owner, rel.ForeignKey, m)), null);
                    return true;
                }
                if (rel.ToMany) throw Cannot(b, $"{owner.Text}.{m.Name} is a list, so it is never null. Use {owner.Text}.{m.Name}->any() to ask whether it has records.");
                SqlExpr exists = new SqlUnary("EXISTS", rel.Subquery(Call, owner.Table ?? owner.Model.Table, NextAlias(), "1"));
                result = owner.Wrap(equal ? new SqlUnary("NOT", exists) : exists, null);
                return true;
            }
            if (rel.Kind != RelationKind.BelongsTo)
                throw Cannot(b, $"{owner.Text}.{m.Name} {rel.Describe()}: compare one of its fields instead, such as {owner.Text}.{m.Name}->any(fn(x) => $x.{rel.Target.Key} == ...).");
            object value = Evaluate(other);
            object key = value is Record r ? ModelInfo.KeyOfTarget(rel, r) : value;
            Column fk = owner.Model.Column(rel.ForeignKey);
            result = owner.Wrap(new SqlBinary(equal ? "=" : "<>", Column(owner, rel.ForeignKey, m), new SqlValue(key, fk)), null);
            return true;
        }

        /// <summary>
        /// A method on a relation that is a list: <c>$u.posts->count()</c>, <c>->any(fn(p) => ...)</c>,
        /// <c>->all(fn(p) => ...)</c>, <c>->sum(fn(p) => $p.views)</c>. The lambda inside runs in the subquery, and
        /// can read the outer rows too.
        /// </summary>
        private bool TryRelationMethod(MethodCallNode call, out SqlExpr result)
        {
            result = null;
            if (call.Target is not MemberNode m || Ref(m.Target) is not RowRef owner || owner.Model?.Relation(m.Name) is not Relation rel) return false;
            string text = owner.Text + "." + m.Name;
            if (!rel.ToMany) throw Cannot(call, $"{text} is one {rel.Target.Name} record, so ->{call.Name}() has no meaning on it. Read its fields instead, such as {text}.{rel.Target.Key}.");
            string alias = NextAlias();
            string from = owner.Table ?? owner.Model.Table;
            SqlExpr Inner(int index, bool condition)
            {
                if (call.Arguments.Count <= index) return null;
                ExprNode arg = call.Arguments[index].Value;
                if (arg is not LambdaNode)
                    throw Cannot(call, $"->{call.Name}() on {text} takes a lambda on one {rel.Target.Name}, such as fn(x) => $x.{rel.Target.Key} > 1.");
                var fn = (Lambda)Evaluate(arg);
                if (fn.Node.Parameters.Count != 1) throw Cannot(call, $"the lambda in ->{call.Name}() takes one parameter, a {rel.Target.Name}.");
                var inner = new LambdaSql(fn, rel.Target, interpreter, this, alias);
                return condition ? inner.Condition(fn.Node.Body) : inner.Expr(fn.Node.Body);
            }
            void Most(int n)
            {
                if (call.Arguments.Count > n) throw Cannot(call, $"->{call.Name}() on {text} takes {(n == 0 ? "no arguments" : "one lambda")}.");
            }
            switch (call.Name)
            {
                case "count":
                    Most(1);
                    result = owner.Wrap(rel.Subquery(Call, from, alias, "COUNT(*)", condition: Inner(0, true)), null);
                    return true;
                case "any":
                case "is_empty":
                {
                    Most(call.Name == "is_empty" ? 0 : 1);
                    SqlExpr exists = new SqlUnary("EXISTS", rel.Subquery(Call, from, alias, "1", condition: Inner(0, true)));
                    result = owner.Wrap(call.Name == "any" ? exists : new SqlUnary("NOT", exists), null);
                    return true;
                }
                case "all":
                {
                    Most(1);
                    SqlExpr test = Inner(0, true) ?? throw Cannot(call, $"->all() on {text} needs a lambda: fn(x) => $x.published.");
                    result = owner.Wrap(new SqlUnary("NOT", new SqlUnary("EXISTS", rel.Subquery(Call, from, alias, "1", condition: new SqlUnary("NOT", test)))), null);
                    return true;
                }
                case "sum":
                case "average":
                case "min":
                case "max":
                {
                    Most(1);
                    SqlExpr value = Inner(0, false) ?? throw Cannot(call, $"->{call.Name}() on {text} needs a lambda that picks the value: fn(x) => $x.total.");
                    SqlExpr aggregate = new SqlCall(call.Name == "average" ? "AVG" : call.Name.ToUpperInvariant(), value);
                    if (call.Name == "sum") aggregate = new SqlCall("COALESCE", aggregate, new SqlValue(0.0));
                    result = owner.Wrap(rel.Subquery(Call, from, alias, null, selectExpr: aggregate), null);
                    return true;
                }
                default:
                    throw Cannot(call, $"->{call.Name}() on a relation has no SQL form. Use ->count(), ->any(), ->is_empty(), ->all(), ->sum(), ->average(), ->min() or ->max().");
            }
        }

        private SqlExpr Binary(BinaryNode b)
        {
            switch (b.Operator)
            {
                case "and":
                    return new SqlBinary("AND", Condition(b.Left), Condition(b.Right));
                case "or":
                    return new SqlBinary("OR", Condition(b.Left), Condition(b.Right));
                case "==":
                case "===":
                case "!=":
                case "!==":
                {
                    bool equal = b.Operator is "==" or "===";
                    if (TryLink(b, equal, out SqlExpr link)) return link;
                    if (IsNull(b.Right)) return new SqlUnary(equal ? "IS NULL" : "IS NOT NULL", Expr(b.Left));
                    if (IsNull(b.Left)) return new SqlUnary(equal ? "IS NULL" : "IS NOT NULL", Expr(b.Right));
                    (SqlExpr l, SqlExpr r) = Typed(Expr(b.Left), Expr(b.Right), b);
                    if (l is SqlSubquery { ValueColumn.Kind: ColumnKind.Email } || r is SqlSubquery { ValueColumn.Kind: ColumnKind.Email })
                    {
                        // A subquery's value loses its column's case-blind collation on SQLite, so emails are compared in lower case.
                        l = new SqlCall("LOWER", l);
                        r = new SqlCall("LOWER", r);
                    }
                    return new SqlBinary(equal ? "=" : "<>", l, r);
                }
                case "<":
                case "<=":
                case ">":
                case ">=":
                {
                    (SqlExpr l, SqlExpr r) = Typed(Expr(b.Left), Expr(b.Right), b);
                    return new SqlBinary(b.Operator, l, r);
                }
                case "in":
                {
                    SqlExpr left = Expr(b.Left);
                    if (UsesRow(b.Right)) throw Cannot(b, "the right of 'in' must be a list worked out before the query, such as [editor, admin] or $ids.");
                    object list = Evaluate(b.Right);
                    if (list is not List<object> items) throw Cannot(b, $"the right of 'in' must be a list, but it is {Values.Describe(list)}.");
                    Column column = ColumnOf(left);
                    if (column?.References is Relation link) items = items.Select(i => i is Record r ? ModelInfo.KeyOfTarget(link, r) : i).ToList();
                    return new SqlBinary("IN", left, new SqlList(items.Select(i => (SqlExpr)new SqlValue(i, column)).ToList()));
                }
                case "??":
                {
                    (SqlExpr l, SqlExpr r) = Typed(Expr(b.Left), Expr(b.Right), b);
                    return new SqlCall("COALESCE", l, r);
                }
                case "+":
                case "-":
                case "*":
                case "/":
                case "%":
                {
                    SqlExpr l = Expr(b.Left);
                    SqlExpr r = Expr(b.Right);
                    bool text = b.Operator == "+" && (IsText(l) || IsText(r));
                    return new SqlBinary(text ? "||" : b.Operator, l, r);
                }
                default:
                    throw Cannot(b, $"'{b.OperatorText}' has no SQL form here.");
            }
        }

        private static bool IsNull(ExprNode node) => node is LiteralNode { Kind: LiteralKind.Null };

        private static bool IsText(SqlExpr e) =>
            e is SqlValue { Value: string } || ColumnOf(e)?.Kind is ColumnKind.Text or ColumnKind.Email or ColumnKind.Handle or ColumnKind.Uuid or ColumnKind.Enum;

        /// <summary>A value compared with a column is converted to the column's type: an email is normalised, an enum word checked.</summary>
        private (SqlExpr, SqlExpr) Typed(SqlExpr left, SqlExpr right, BinaryNode b)
        {
            if (ColumnOf(left) is Column lc && right is SqlValue rv && rv.Column == null) right = new SqlValue(rv.Value, lc);
            if (ColumnOf(right) is Column rc && left is SqlValue lv && lv.Column == null) left = new SqlValue(lv.Value, rc);
            Column password = ColumnOf(left) ?? ColumnOf(right);
            if (password?.Kind == ColumnKind.Password)
                throw Cannot(b, "passwords are stored as hashes, so they can't be compared in a query. Find the record, then check with $record.password->verify($text).");
            return (left, right);
        }

        private SqlExpr Method(MethodCallNode call)
        {
            if (TryRelationMethod(call, out SqlExpr relation)) return relation;
            if (!UsesRow(call.Target) || call.Arguments.Any(a => UsesRow(a.Value)))
                throw Cannot(call, $"->{call.Name}() can only be used on a column, with arguments worked out before the query.");
            SqlExpr target = Expr(call.Target);
            object Arg(int i) => i < call.Arguments.Count ? Evaluate(call.Arguments[i].Value) : null;
            void Count(int n)
            {
                if (call.Arguments.Count != n)
                    throw Cannot(call, $"->{call.Name}() takes {n} argument{(n == 1 ? string.Empty : "s")} here.");
            }
            switch (call.Name)
            {
                case "contains":
                case "starts_with":
                case "ends_with":
                {
                    Count(1);
                    string text = Like(Values.ToStr(Arg(0)));
                    string pattern = call.Name == "contains" ? "%" + text + "%" : call.Name == "starts_with" ? text + "%" : "%" + text;
                    return new SqlBinary("LIKE", target, new SqlValue(pattern, null));
                }
                case "lower":
                case "upper":
                case "trim":
                    Count(0);
                    return new SqlCall(call.Name.ToUpperInvariant(), target);
                case "count":
                    Count(0);
                    return new SqlCall("LENGTH", target);
                case "matches":
                case "test":
                {
                    Count(1);
                    RegexValue regex = RegexValue.From(Arg(0), $"->{call.Name}()");
                    return new SqlMatch(target, regex);
                }
                default:
                    throw Cannot(call, $"->{call.Name}() has no SQL form. Use ->contains(), ->starts_with(), ->ends_with(), ->lower(), ->upper(), ->trim(), ->count() or ->matches().");
            }
        }

        /// <summary>Escapes LIKE's wildcards, so ->contains('50%') looks for 50%.</summary>
        internal static string Like(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
