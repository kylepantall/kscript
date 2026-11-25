using System.Text;

using KScript.Language.Analysis;
using KScript.Metadata;
using KScript.Syntax;

namespace KScript.Language.Features
{
    /// <summary>
    /// The SQL a KScript.Data query chain sends, worked out from the code for hover: <c>User->where(age: 3)->get()</c>
    /// is <c>SELECT * FROM users WHERE age = ?</c>. Values are <c>?</c>, since they are only known when it runs, and
    /// a condition the sketch can't follow is <c>…</c>.
    /// </summary>
    internal static class QuerySql
    {
        /// <summary>The SQL of the chain that ends at <paramref name="last"/>, or null when it doesn't start at a model.</summary>
        public static string Sketch(MethodCallNode last, Func<ExprNode, TypeShape> infer)
        {
            var chain = new List<MethodCallNode>();
            ExprNode at = last;
            while (at is MethodCallNode m)
            {
                chain.Insert(0, m);
                at = m.Target;
            }
            TypeShape model = infer(at);
            if (model.Kind != KType.Model || model.Instance?.Name == null)
            {
                return null;
            }
            TypeShape record = model.Instance;
            string table = KScript.Data.ModelInfo.TableName(record.Name);
            (string Text, int Rank) where = (null, Atom);
            var order = new List<string>();
            string select = "*";
            string limit = null;
            string offset = null;
            bool trashed = record.Fields.ContainsKey("deleted_at");
            bool distinct = false;
            var with = new List<string>();

            void And(string op, (string Text, int Rank) condition)
            {
                where = where.Text == null ? condition : Join(op, where, condition);
            }

            foreach (MethodCallNode m in chain)
            {
                IReadOnlyList<ArgumentNode> args = m.Arguments;
                switch (m.Name)
                {
                    case "where":
                    case "or_where":
                    case "where_not":
                    {
                        (string Text, int Rank) condition = Conditions(args);
                        if (m.Name == "where_not")
                        {
                            condition = ("(" + condition.Text + ") IS NOT TRUE", Atom);
                        }
                        And(m.Name == "or_where" ? "OR" : "AND", condition);
                        break;
                    }
                    case "select":
                        select = string.Join(", ", args.Select(Word));
                        break;
                    case "distinct":
                        distinct = true;
                        break;
                    case "order_by":
                        order.AddRange(args.Select(a => a.Name != null ? a.Name + " " + Word(a).ToUpperInvariant() : Word(a)));
                        break;
                    case "latest":
                    case "oldest":
                        string column = args.Count > 0 ? Word(args[0]) : record.Fields.ContainsKey("created_at") ? "created_at" : "id";
                        order.Add(column + (m.Name == "latest" ? " DESC" : " ASC"));
                        break;
                    case "limit":
                        limit = "?";
                        break;
                    case "offset":
                        offset = "?";
                        break;
                    case "with_trashed":
                    case "only_trashed":
                        trashed = false;
                        if (m.Name == "only_trashed")
                        {
                            And("AND", ("deleted_at IS NOT NULL", Atom));
                        }
                        break;
                    case "with":
                        with.AddRange(args.Select(a => (a.Value as MemberNode)?.DottedName ?? Word(a)));
                        break;
                    case "first":
                    case "find":
                    case "first_or_fail":
                    case "find_or_fail":
                        if (m.Name.StartsWith("find", StringComparison.Ordinal))
                        {
                            And("AND", ("id = ?", Atom));
                        }
                        limit = "1";
                        break;
                    case "count":
                        select = "COUNT(*)";
                        break;
                    case "exists":
                        select = "1";
                        limit = "1";
                        break;
                    case "pluck":
                    case "value":
                        select = args.Count > 0 ? Word(args[0]) : select;
                        break;
                    case "sum":
                    case "avg":
                    case "min":
                    case "max":
                        select = m.Name.ToUpperInvariant() + "(" + (args.Count > 0 ? Word(args[0]) : "…") + ")";
                        break;
                }
            }

            var sql = new StringBuilder("SELECT ");
            if (distinct)
            {
                sql.Append("DISTINCT ");
            }
            sql.Append(select).Append(" FROM ").Append(table);
            if (trashed)
            {
                And("AND", ("deleted_at IS NULL", Atom));
            }
            if (where.Text != null)
            {
                sql.Append("\nWHERE ").Append(where.Text);
            }
            if (order.Count > 0)
            {
                sql.Append("\nORDER BY ").Append(string.Join(", ", order));
            }
            if (limit != null)
            {
                sql.Append("\nLIMIT ").Append(limit);
            }
            if (offset != null)
            {
                sql.Append(" OFFSET ").Append(offset);
            }
            foreach (string relation in with)
            {
                sql.Append("\n-- then one query for ").Append(relation);
            }
            return sql.ToString();
        }

        private static string Word(ArgumentNode a) => a.Value switch
        {
            IdentifierNode id => id.Name,
            LiteralNode l when l.Kind == LiteralKind.String => l.Value,
            _ => "…",
        };

        /// <summary>How tightly a condition binds, so it is only bracketed where it must be: OR, then AND, then the rest.</summary>
        private const int Or = 1, And = 2, Atom = 3;

        private static (string Text, int Rank) Join(string op, (string Text, int Rank) left, (string Text, int Rank) right)
        {
            int rank = op == "OR" ? Or : And;
            string Side((string Text, int Rank) c) => c.Rank < rank ? "(" + c.Text + ")" : c.Text;
            return (Side(left) + " " + op + " " + Side(right), rank);
        }

        /// <summary>A where's arguments as conditions: name: value pairs, and a lambda's body.</summary>
        private static (string Text, int Rank) Conditions(IReadOnlyList<ArgumentNode> args)
        {
            (string Text, int Rank) all = (null, Atom);
            foreach (ArgumentNode a in args)
            {
                (string Text, int Rank) part = a.Name != null
                    ? (a.Value is ListNode ? a.Name + " IN (…)" : a.Value is LiteralNode { Kind: LiteralKind.Null } ? a.Name + " IS NULL" : a.Name + " = ?", Atom)
                    : a.Value is LambdaNode { Parameters.Count: 1 } lambda ? Condition(lambda.Body, lambda.Parameters[0].Name) : ("…", Atom);
                all = all.Text == null ? part : Join("AND", all, part);
            }
            return all.Text == null ? ("…", Atom) : all;
        }

        private static readonly Dictionary<string, string> Operators = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["=="] = "=", ["!="] = "<>", ["<"] = "<", ["<="] = "<=", [">"] = ">", [">="] = ">=", ["and"] = "AND", ["or"] = "OR",
        };

        /// <summary>A lambda's body as SQL: <c>$u.age >= 18 and $u.name->starts_with('A')</c> is <c>age >= ? AND name LIKE ?</c>.</summary>
        private static (string Text, int Rank) Condition(ExprNode node, string parameter)
        {
            switch (node)
            {
                case BinaryNode b when Operators.TryGetValue(b.Operator, out string op):
                    (string Text, int Rank) left = Condition(b.Left, parameter);
                    (string Text, int Rank) right = Condition(b.Right, parameter);
                    if (op is "AND" or "OR")
                    {
                        return Join(op, left, right);
                    }
                    if (op is "=" or "<>" && right.Text == "NULL")
                    {
                        return (left.Text + (op == "=" ? " IS NULL" : " IS NOT NULL"), Atom);
                    }
                    // As the database is asked: a row whose column is empty is different from a value too.
                    if (op == "<>" && right.Text == "?")
                    {
                        return ("(" + left.Text + " = ?) IS NOT TRUE", Atom);
                    }
                    return (left.Text + " " + op + " " + right.Text, Atom);
                case UnaryNode { Operator: "not" } u:
                    return ("(" + Condition(u.Operand, parameter).Text + ") IS NOT TRUE", Atom);
                case MemberNode member when member.Target is VariableNode v && v.Name == parameter:
                    return (member.Name, Atom);
                case MethodCallNode call when Column(call.Target, parameter) is string column:
                    return (call.Name switch
                    {
                        "starts_with" or "ends_with" or "contains" => column + " LIKE ?",
                        "lower" or "upper" => call.Name.ToUpperInvariant() + "(" + column + ")",
                        _ => "…",
                    }, Atom);
                case LiteralNode { Kind: LiteralKind.Null }:
                    return ("NULL", Atom);
                case LiteralNode or VariableNode:
                    return ("?", Atom);
                default:
                    return ("…", Atom);
            }
        }

        private static string Column(ExprNode node, string parameter) =>
            node is MemberNode member && member.Target is VariableNode v && v.Name == parameter ? member.Name : null;
    }
}
