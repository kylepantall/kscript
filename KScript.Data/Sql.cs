using System.Text;

namespace KScript.Data
{
    /// <summary>
    /// One part of a query condition or value, built by the query builder and the lambda compiler. A dialect turns the
    /// tree into SQL text with every value as a parameter, so no value is ever written into the SQL itself.
    /// </summary>
    internal abstract class SqlExpr
    {
    }

    /// <summary>A column of the query's table.</summary>
    internal sealed class SqlColumn : SqlExpr
    {
        public SqlColumn(string name, Column column = null, string table = null)
        {
            Name = name;
            Column = column;
            Table = table;
        }

        public string Name { get; }

        /// <summary>The table or alias the column is read from, written before it (<c>"r1"."author_id"</c>), or null for the query's own table.</summary>
        public string Table { get; }

        /// <summary>The model's column, when the query is on a model: values compared with it are converted to its type.</summary>
        public Column Column { get; }
    }

    /// <summary>A value, sent as a parameter.</summary>
    internal sealed class SqlValue : SqlExpr
    {
        public SqlValue(object value, Column column = null)
        {
            Value = value;
            Column = column;
        }

        /// <summary>The KScript value. It is converted for the database (checked against <see cref="Column"/>) when the SQL is written.</summary>
        public object Value { get; }

        /// <summary>The column the value is compared with or stored in, if known.</summary>
        public Column Column { get; }
    }

    /// <summary>A list of values: the right side of IN.</summary>
    internal sealed class SqlList : SqlExpr
    {
        public SqlList(IReadOnlyList<SqlExpr> items) => Items = items;

        public IReadOnlyList<SqlExpr> Items { get; }
    }

    /// <summary>
    /// Two parts joined by an operator: <c>=</c>, <c>&lt;&gt;</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>,
    /// <c>AND</c>, <c>OR</c>, <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>%</c>, <c>||</c>, <c>IN</c>, <c>NOT IN</c>,
    /// and <c>LIKE</c>.
    /// </summary>
    internal sealed class SqlBinary : SqlExpr
    {
        public SqlBinary(string op, SqlExpr left, SqlExpr right)
        {
            Op = op;
            Left = left;
            Right = right;
        }

        public string Op { get; }
        public SqlExpr Left { get; }
        public SqlExpr Right { get; }
    }

    /// <summary><c>NOT x</c>, <c>-x</c>, <c>x IS NULL</c> and <c>x IS NOT NULL</c>.</summary>
    internal sealed class SqlUnary : SqlExpr
    {
        public SqlUnary(string op, SqlExpr operand)
        {
            Op = op;
            Operand = operand;
        }

        public string Op { get; }
        public SqlExpr Operand { get; }
    }

    /// <summary>A function: LOWER, UPPER, LENGTH, TRIM, COALESCE, COUNT, SUM, AVG, MIN, MAX.</summary>
    internal sealed class SqlCall : SqlExpr
    {
        public SqlCall(string name, params SqlExpr[] args)
        {
            Name = name;
            Args = args;
        }

        public string Name { get; }
        public IReadOnlyList<SqlExpr> Args { get; }
    }

    /// <summary>A text column matched against a regex: <c>$u.name->matches(/^[a-z]+$/i)</c>.</summary>
    internal sealed class SqlMatch : SqlExpr
    {
        public SqlMatch(SqlExpr target, KScript.Runtime.RegexValue regex)
        {
            Target = target;
            Regex = regex;
        }

        public SqlExpr Target { get; }
        public KScript.Runtime.RegexValue Regex { get; }
    }

    /// <summary>SQL written as it is, from KScript.Data itself (never from a script): <c>1</c> in <c>ORDER BY 1</c>.</summary>
    internal sealed class SqlRaw : SqlExpr
    {
        public SqlRaw(string text) => Text = text;

        public string Text { get; }
    }

    /// <summary><c>CASE WHEN c THEN a ELSE b END</c>.</summary>
    internal sealed class SqlCase : SqlExpr
    {
        public SqlCase(SqlExpr when, SqlExpr then, SqlExpr otherwise)
        {
            When = when;
            Then = then;
            Otherwise = otherwise;
        }

        public SqlExpr When { get; }
        public SqlExpr Then { get; }
        public SqlExpr Otherwise { get; }
    }

    /// <summary>
    /// A query inside a query, on another table: <c>(SELECT COUNT(*) FROM "posts" AS "r1" WHERE ...)</c>. Relations use
    /// it to count, test and read related rows. <see cref="Joins"/> are inner joins after the first table, such as a
    /// pivot table.
    /// </summary>
    internal sealed class SqlSubquery : SqlExpr
    {
        public SqlSubquery(string select, string table, string alias, SqlExpr where, IReadOnlyList<(string Table, string Alias, SqlExpr On)> joins = null, int? limit = null)
        {
            Select = select;
            Table = table;
            Alias = alias;
            Where = where;
            Joins = joins ?? Array.Empty<(string, string, SqlExpr)>();
            Limit = limit;
        }

        /// <summary>What it reads: <c>1</c>, <c>COUNT(*)</c>, or an expression written by <see cref="SelectExpr"/>.</summary>
        public string Select { get; }

        /// <summary>A column or expression it reads instead of <see cref="Select"/>.</summary>
        public SqlExpr SelectExpr { get; init; }

        /// <summary>The column it reads, when it reads one (<c>$p.author.email</c>): values compared with it are converted to its type.</summary>
        public Column ValueColumn { get; init; }

        public string Table { get; }
        public string Alias { get; }
        public SqlExpr Where { get; }
        public IReadOnlyList<(string Table, string Alias, SqlExpr On)> Joins { get; }
        public int? Limit { get; }
    }

    /// <summary>A statement ready to run: its SQL and its parameters in order.</summary>
    internal sealed class SqlStatement
    {
        public SqlStatement(string text, IReadOnlyList<object> parameters)
        {
            Text = text;
            Parameters = parameters;
        }

        public string Text { get; }
        public IReadOnlyList<object> Parameters { get; }

        public override string ToString() => Text;
    }

    /// <summary>Writes SQL for one statement, collecting its parameters.</summary>
    internal sealed class SqlWriter
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly List<object> parameters = new List<object>();

        public SqlWriter(Dialect dialect) => Dialect = dialect;

        public Dialect Dialect { get; }

        public SqlWriter Append(string text)
        {
            sb.Append(text);
            return this;
        }

        public SqlWriter Name(string name)
        {
            sb.Append(Dialect.Quote(name));
            return this;
        }

        public SqlWriter Value(object value)
        {
            parameters.Add(value);
            sb.Append(Dialect.Parameter(parameters.Count));
            return this;
        }

        public SqlWriter Expr(SqlExpr e, bool nested = false)
        {
            switch (e)
            {
                case SqlColumn c:
                    if (c.Table != null) Name(c.Table).Append(".");
                    Name(c.Name);
                    break;
                case SqlSubquery q:
                    sb.Append("(SELECT ");
                    if (q.SelectExpr != null) Expr(q.SelectExpr);
                    else sb.Append(q.Select);
                    sb.Append(" FROM ");
                    Name(q.Table);
                    if (q.Alias != null) sb.Append(" AS ").Append(Dialect.Quote(q.Alias));
                    foreach ((string table, string alias, SqlExpr on) in q.Joins)
                    {
                        sb.Append(" INNER JOIN ");
                        Name(table).Append(" AS ").Name(alias).Append(" ON ");
                        Expr(on, nested: true);
                    }
                    if (q.Where != null)
                    {
                        sb.Append(" WHERE ");
                        Expr(q.Where);
                    }
                    sb.Append(Dialect.Limit(q.Limit, null));
                    sb.Append(')');
                    break;
                case SqlValue v:
                    Value(Rows.ForColumn(v.Value, v.Column, Dialect));
                    break;
                case SqlList list:
                    sb.Append('(');
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        Expr(list.Items[i]);
                    }
                    sb.Append(')');
                    break;
                case SqlBinary b when b.Op is "IN" or "NOT IN" && b.Right is SqlList { Items.Count: 0 }:
                    // Nothing is in an empty list.
                    sb.Append(b.Op == "IN" ? "1 = 0" : "1 = 1");
                    break;
                case SqlBinary b:
                    if (nested) sb.Append('(');
                    if (b.Op == "LIKE") Dialect.Like(this, b.Left, b.Right);
                    else if (b.Op == "||") Dialect.Concat(this, b.Left, b.Right);
                    else
                    {
                        Expr(b.Left, nested: true);
                        sb.Append(' ').Append(b.Op).Append(' ');
                        Expr(b.Right, nested: true);
                    }
                    if (nested) sb.Append(')');
                    break;
                case SqlMatch m:
                    if (nested) sb.Append('(');
                    Dialect.Matches(this, m.Target, m.Regex);
                    if (nested) sb.Append(')');
                    break;
                case SqlRaw raw:
                    sb.Append(raw.Text);
                    break;
                case SqlUnary u when u.Op is "IS NULL" or "IS NOT NULL":
                    if (nested) sb.Append('(');
                    Expr(u.Operand, nested: true);
                    sb.Append(' ').Append(u.Op);
                    if (nested) sb.Append(')');
                    break;
                case SqlUnary u:
                    if (nested) sb.Append('(');
                    sb.Append(u.Op is "NOT" or "EXISTS" ? u.Op + " " : u.Op);
                    Expr(u.Operand, nested: true);
                    if (nested) sb.Append(')');
                    break;
                case SqlCall call:
                    sb.Append(Dialect.Function(call.Name)).Append('(');
                    if (call.Args.Count == 0) sb.Append('*');
                    for (int i = 0; i < call.Args.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        Expr(call.Args[i]);
                    }
                    sb.Append(')');
                    break;
                case SqlCase k:
                    sb.Append("CASE WHEN ");
                    Expr(k.When, nested: true);
                    sb.Append(" THEN ");
                    Expr(k.Then, nested: true);
                    sb.Append(" ELSE ");
                    Expr(k.Otherwise, nested: true);
                    sb.Append(" END");
                    break;
            }
            return this;
        }

        public SqlStatement Build() => new SqlStatement(sb.ToString(), parameters.ToList());
    }
}
