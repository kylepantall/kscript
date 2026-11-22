using KScript.Syntax.Text;

namespace KScript.Syntax
{
    /// <summary>
    /// A node of a KScript expression. Spans are offsets into the string that was parsed; use
    /// <see cref="Markup.DecodedText.ToSource(TextSpan)"/> to map them into the document.
    /// </summary>
    public abstract class ExprNode
    {
        public TextSpan Span { get; internal set; }

        public virtual IEnumerable<ExprNode> Children => Array.Empty<ExprNode>();

        /// <summary>This node and every node below it, parents first.</summary>
        public IEnumerable<ExprNode> DescendantsAndSelf()
        {
            yield return this;
            foreach (ExprNode child in Children)
            {
                if (child == null)
                {
                    continue;
                }
                foreach (ExprNode d in child.DescendantsAndSelf())
                {
                    yield return d;
                }
            }
        }
    }

    public enum LiteralKind
    {
        Number,
        String,
        Bool,
        Null,

        /// <summary><c>/pattern/flags</c>: the value is the whole literal.</summary>
        Regex,

        /// <summary><c>kyle@example.com</c>, written without quotes.</summary>
        Email,

        /// <summary><c>3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f</c>, written without quotes.</summary>
        Uuid,

        /// <summary><c>https://example.com/docs</c>, written without quotes.</summary>
        Url,
    }

    public sealed class LiteralNode : ExprNode
    {
        public LiteralKind Kind { get; internal set; }

        /// <summary>The literal's value as text: the unquoted string, the number as written, "true"/"false", or "null".</summary>
        public string Value { get; internal set; }

        /// <summary>
        /// For text in "double quotes" that holds <c>$name</c> or <c>{ $expr }</c>: its parts, filled in when the
        /// literal is evaluated. Null for 'single quotes', which are kept as written, and for text with nothing to fill in.
        /// </summary>
        public Template Template { get; internal set; }

        public override IEnumerable<ExprNode> Children =>
            Template == null ? Array.Empty<ExprNode>() : Template.Expressions.Select(s => s.Node).Where(n => n != null);
    }

    /// <summary><c>$name</c>.</summary>
    public sealed class VariableNode : ExprNode
    {
        public string Name { get; internal set; }
    }

    /// <summary>A bare identifier: a function name in <c>|&gt; slugify()</c>, an import alias, or <c>_</c>.</summary>
    public sealed class IdentifierNode : ExprNode
    {
        public string Name { get; internal set; }
    }

    public sealed class ArgumentNode : ExprNode
    {
        /// <summary>The name of a named argument (<c>system: '...'</c>), or null.</summary>
        public string Name { get; internal set; }

        public TextSpan NameSpan { get; internal set; }
        public ExprNode Value { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Value };
    }

    /// <summary>The parenthesised argument list of a call, with its span for signature help.</summary>
    public sealed class ArgumentList
    {
        public List<ArgumentNode> Arguments { get; } = new List<ArgumentNode>();

        /// <summary>From '(' to ')' inclusive (to the end of input if unclosed).</summary>
        public TextSpan Span { get; internal set; }

        public bool Closed { get; internal set; }
    }

    /// <summary><c>@name(args)</c>: a built-in command or a <c>&lt;fn&gt;</c> function.</summary>
    public sealed class CommandCallNode : ExprNode
    {
        public string Name { get; internal set; }

        /// <summary>The span of <c>@name</c>.</summary>
        public TextSpan NameSpan { get; internal set; }

        /// <summary>Null for a function reference without parentheses (<c>$people-&gt;map(@greet)</c>).</summary>
        public ArgumentList ArgumentList { get; internal set; }

        public IReadOnlyList<ArgumentNode> Arguments => (IReadOnlyList<ArgumentNode>)ArgumentList?.Arguments ?? Array.Empty<ArgumentNode>();
        public override IEnumerable<ExprNode> Children => Arguments;
    }

    /// <summary><c>target-&gt;name(args)</c>.</summary>
    public sealed class MethodCallNode : ExprNode
    {
        public ExprNode Target { get; internal set; }
        public string Name { get; internal set; }
        public TextSpan NameSpan { get; internal set; }
        public ArgumentList ArgumentList { get; internal set; }
        public IReadOnlyList<ArgumentNode> Arguments => (IReadOnlyList<ArgumentNode>)ArgumentList?.Arguments ?? Array.Empty<ArgumentNode>();
        public override IEnumerable<ExprNode> Children => new[] { Target }.Concat(Arguments);
    }

    /// <summary><c>callee(args)</c>, where the callee is a bare name (<c>filter(...)</c> after a pipe), a member (<c>str.slugify()</c>) or a value.</summary>
    public sealed class CallNode : ExprNode
    {
        public ExprNode Callee { get; internal set; }
        public ArgumentList ArgumentList { get; internal set; }
        public IReadOnlyList<ArgumentNode> Arguments => ArgumentList.Arguments;
        public override IEnumerable<ExprNode> Children => new[] { Callee }.Concat(Arguments);
    }

    /// <summary><c>target.name</c> or <c>target?.name</c>.</summary>
    public sealed class MemberNode : ExprNode
    {
        public ExprNode Target { get; internal set; }
        public string Name { get; internal set; }
        public TextSpan NameSpan { get; internal set; }
        public bool NullSafe { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Target };

        /// <summary><c>posts.tags</c> when this is bare names joined by dots (no variable, call or <c>?.</c>), for a parameter that reads names; otherwise null.</summary>
        public string DottedName
        {
            get
            {
                if (NullSafe) return null;
                string left = Target is IdentifierNode id && id.Name != "_" ? id.Name : (Target as MemberNode)?.DottedName;
                return left == null ? null : left + "." + Name;
            }
        }
    }

    /// <summary><c>target[index]</c>.</summary>
    public sealed class IndexNode : ExprNode
    {
        public ExprNode Target { get; internal set; }
        public ExprNode Index { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Target, Index };
    }

    public sealed class UnaryNode : ExprNode
    {
        /// <summary><c>-</c>, <c>+</c> or <c>not</c>.</summary>
        public string Operator { get; internal set; }

        public ExprNode Operand { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Operand };
    }

    public sealed class BinaryNode : ExprNode
    {
        /// <summary>The operator in its canonical form: word operators are normalised (<c>eq</c> → <c>==</c>, <c>lt</c> → <c>&lt;</c>...).</summary>
        public string Operator { get; internal set; }

        /// <summary>The operator as written.</summary>
        public string OperatorText { get; internal set; }

        public TextSpan OperatorSpan { get; internal set; }
        public ExprNode Left { get; internal set; }
        public ExprNode Right { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Left, Right };
    }

    public sealed class ConditionalNode : ExprNode
    {
        public ExprNode Condition { get; internal set; }
        public ExprNode WhenTrue { get; internal set; }
        public ExprNode WhenFalse { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Condition, WhenTrue, WhenFalse };
    }

    /// <summary><c>left |&gt; right</c>: calls right with left as its first argument.</summary>
    public sealed class PipeNode : ExprNode
    {
        public ExprNode Left { get; internal set; }
        public ExprNode Right { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Left, Right };
    }

    public sealed class ParameterNode : ExprNode
    {
        public string Name { get; internal set; }
    }

    /// <summary><c>fn($a, $b) =&gt; body</c>.</summary>
    public sealed class LambdaNode : ExprNode
    {
        public List<ParameterNode> Parameters { get; } = new List<ParameterNode>();
        public ExprNode Body { get; internal set; }
        public override IEnumerable<ExprNode> Children => Parameters.Cast<ExprNode>().Append(Body);
    }

    public sealed class ListNode : ExprNode
    {
        public List<ExprNode> Items { get; } = new List<ExprNode>();
        public override IEnumerable<ExprNode> Children => Items;
    }

    public sealed class MapEntryNode : ExprNode
    {
        public string Key { get; internal set; }
        public TextSpan KeySpan { get; internal set; }
        public ExprNode Value { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Value };
    }

    public sealed class MapNode : ExprNode
    {
        public List<MapEntryNode> Entries { get; } = new List<MapEntryNode>();
        public override IEnumerable<ExprNode> Children => Entries;
    }

    /// <summary><c>start..end</c> or <c>start..end step n</c> (end exclusive).</summary>
    public sealed class RangeNode : ExprNode
    {
        public ExprNode Start { get; internal set; }
        public ExprNode End { get; internal set; }
        public ExprNode Step { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Start, End, Step };
    }

    public sealed class MatchArmNode : ExprNode
    {
        /// <summary>The pattern, or null for the <c>_</c> default arm.</summary>
        public ExprNode Pattern { get; internal set; }

        public ExprNode Result { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Pattern, Result };
    }

    /// <summary><c>match subject { pattern =&gt; result, _ =&gt; fallback }</c>.</summary>
    public sealed class MatchNode : ExprNode
    {
        public ExprNode Subject { get; internal set; }
        public List<MatchArmNode> Arms { get; } = new List<MatchArmNode>();
        public override IEnumerable<ExprNode> Children => new[] { Subject }.Concat(Arms);
    }

    /// <summary><c>x = 3</c>, <c>$total += $price</c>. Only valid as a statement.</summary>
    public sealed class AssignmentNode : ExprNode
    {
        /// <summary>The variable being assigned (with or without <c>$</c> in the source).</summary>
        public string Name { get; internal set; }

        public TextSpan NameSpan { get; internal set; }

        /// <summary>The full target, which may include <c>.field</c> or <c>[index]</c> parts.</summary>
        public ExprNode Target { get; internal set; }

        public string Operator { get; internal set; }
        public ExprNode Value { get; internal set; }
        public override IEnumerable<ExprNode> Children => new[] { Target, Value };
    }

    /// <summary>A placeholder where the parser could not read an expression.</summary>
    public sealed class ErrorNode : ExprNode
    {
    }

    public sealed class SyntaxError
    {
        public SyntaxError(TextSpan span, string message, string code = DiagnosticCodes.ExpressionSyntax)
        {
            Span = span;
            Message = message;
            Code = code;
        }

        public TextSpan Span { get; }
        public string Message { get; }
        public string Code { get; }
        public override string ToString() => $"{Span}: {Message}";
    }

    /// <summary>The result of parsing one expression or statement list.</summary>
    public sealed class ParseResult
    {
        public ParseResult(string text, List<ExprNode> nodes, List<SyntaxError> errors)
        {
            Text = text;
            Nodes = nodes;
            Errors = errors;
        }

        public string Text { get; }

        /// <summary>One node for an expression; one per statement for a statement list.</summary>
        public List<ExprNode> Nodes { get; }

        public ExprNode Root => Nodes.Count > 0 ? Nodes[0] : null;
        public List<SyntaxError> Errors { get; }
        public bool Success => Errors.Count == 0;
    }
}
