using KScript.Runtime;
using KScript.Syntax;

namespace KScript.Data
{
    /// <summary>
    /// Turns a query lambda, <c>fn($u) => $u.age >= 18 and $u.email->ends_with('@acme.com')</c>, into a SQL condition.
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

        private LambdaSql(Lambda lambda, ModelInfo model, Interpreter interpreter)
        {
            this.lambda = lambda;
            this.model = model;
            this.interpreter = interpreter;
            row = lambda.Node.Parameters.Count > 0 ? lambda.Node.Parameters[0].Name : null;
        }

        /// <summary>The condition a lambda means, for a query on <paramref name="model"/> (null for a table with no model).</summary>
        public static SqlExpr Compile(ICallable fn, ModelInfo model, Interpreter interpreter, string method)
        {
            if (fn is not Lambda lambda)
                throw new KScriptException($"->{method}() needs a condition written as a lambda, such as fn($u) => $u.age >= 18, so it can run in the database. A function declared with fn name() can't be turned into SQL.", "Argument");
            if (lambda.Node.Parameters.Count != 1)
                throw new KScriptException($"The condition in ->{method}() takes one parameter, the row: fn($u) => $u.active.", "Argument");
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
            if (e is SqlColumn c && (c.Column == null || c.Column.Kind == ColumnKind.Bool)) return new SqlBinary("=", c, new SqlValue(true, c.Column));
            return e;
        }

        private bool UsesRow(ExprNode node) =>
            node.DescendantsAndSelf().Any(n => n is VariableNode v && v.Name == row);

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
                case MemberNode m when m.Target is VariableNode v && v.Name == row:
                    return ColumnNamed(m.Name, m);

                case MemberNode m:
                    throw Cannot(m, $"only the row's own columns can be read, such as ${row}.name. Links between models aren't in this version of KScript.Data yet.");

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

        private SqlColumn ColumnNamed(string name, ExprNode node)
        {
            if (model == null) return new SqlColumn(name);
            Column c = model.Column(name);
            if (c != null) return new SqlColumn(name, c);
            if (model.Type.Field(name) is FieldDefinition f && f.Computed)
                throw Cannot(node, $"{model.Name}.{name} is worked out from other fields, so the database doesn't have it. Use the fields it is made from.");
            return new SqlColumn(name, model.Need(name, " to compare"));
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
                    if (IsNull(b.Right)) return new SqlUnary(equal ? "IS NULL" : "IS NOT NULL", Expr(b.Left));
                    if (IsNull(b.Left)) return new SqlUnary(equal ? "IS NULL" : "IS NOT NULL", Expr(b.Right));
                    (SqlExpr l, SqlExpr r) = Typed(Expr(b.Left), Expr(b.Right), b);
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
                    Column column = (left as SqlColumn)?.Column;
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
            e is SqlValue { Value: string } || e is SqlColumn { Column.Kind: ColumnKind.Text or ColumnKind.Email or ColumnKind.Handle or ColumnKind.Uuid or ColumnKind.Enum };

        /// <summary>A value compared with a column is converted to the column's type: an email is normalised, an enum word checked.</summary>
        private (SqlExpr, SqlExpr) Typed(SqlExpr left, SqlExpr right, BinaryNode b)
        {
            if (left is SqlColumn { Column: Column lc } && right is SqlValue rv && rv.Column == null) right = new SqlValue(rv.Value, lc);
            if (right is SqlColumn { Column: Column rc } && left is SqlValue lv && lv.Column == null) left = new SqlValue(lv.Value, rc);
            Column password = (left as SqlColumn)?.Column ?? (right as SqlColumn)?.Column;
            if (password?.Kind == ColumnKind.Password)
                throw Cannot(b, "passwords are stored as hashes, so they can't be compared in a query. Find the record, then check with $record.password->verify($text).");
            return (left, right);
        }

        private SqlExpr Method(MethodCallNode call)
        {
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
                case "length":
                    Count(0);
                    return new SqlCall(call.Name.ToUpperInvariant(), target);
                case "matches":
                case "test":
                {
                    Count(1);
                    RegexValue regex = RegexValue.From(Arg(0), $"->{call.Name}()");
                    return new SqlMatch(target, regex);
                }
                default:
                    throw Cannot(call, $"->{call.Name}() has no SQL form. Use ->contains(), ->starts_with(), ->ends_with(), ->lower(), ->upper(), ->trim(), ->length() or ->matches().");
            }
        }

        /// <summary>Escapes LIKE's wildcards, so ->contains('50%') looks for 50%.</summary>
        internal static string Like(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
