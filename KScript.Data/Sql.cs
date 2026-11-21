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
        public SqlColumn(string name, Column column = null)
        {
            Name = name;
            Column = column;
        }

        public string Name { get; }

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
    /// <c>LIKE</c> and <c>REGEXP</c>.
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
                    Name(c.Name);
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
                    Expr(b.Left, nested: true);
                    if (b.Op == "LIKE")
                    {
                        sb.Append(" LIKE ");
                        Expr(b.Right, nested: true);
                        sb.Append(" ESCAPE '\\'");
                    }
                    else
                    {
                        sb.Append(' ').Append(b.Op).Append(' ');
                        Expr(b.Right, nested: true);
                    }
                    if (nested) sb.Append(')');
                    break;
                case SqlUnary u when u.Op is "IS NULL" or "IS NOT NULL":
                    if (nested) sb.Append('(');
                    Expr(u.Operand, nested: true);
                    sb.Append(' ').Append(u.Op);
                    if (nested) sb.Append(')');
                    break;
                case SqlUnary u:
                    if (nested) sb.Append('(');
                    sb.Append(u.Op == "NOT" ? "NOT " : u.Op);
                    Expr(u.Operand, nested: true);
                    if (nested) sb.Append(')');
                    break;
                case SqlCall call:
                    sb.Append(call.Name).Append('(');
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
