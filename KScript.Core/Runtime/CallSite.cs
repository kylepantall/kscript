using System.Collections.Generic;
using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>
    /// Where a command was called, as written in the script: the call's node and the text it was parsed from.
    /// A command such as <c>@assert()</c> uses it to show the expression it was given, not only its value.
    /// </summary>
    public sealed class CallSite
    {
        public CallSite(ExprNode node, IReadOnlyList<ArgumentNode> arguments, EvalContext source)
        {
            Node = node;
            Arguments = arguments;
            Source = source;
        }

        /// <summary>The call: <c>@name(...)</c> or <c>name(...)</c>.</summary>
        public ExprNode Node { get; }

        /// <summary>The arguments as written.</summary>
        public IReadOnlyList<ArgumentNode> Arguments { get; }

        /// <summary>The text the call was parsed from, and the script it is in.</summary>
        public EvalContext Source { get; }

        /// <summary>The expression written for positional argument <paramref name="index"/>, or null when there isn't one.</summary>
        public ExprNode Argument(int index)
        {
            int position = 0;
            foreach (ArgumentNode argument in Arguments)
            {
                if (argument.Name != null) continue;
                if (position++ == index) return argument.Value;
            }
            return null;
        }

        /// <summary>The text of an expression inside the call, as written, or null when the text isn't known.</summary>
        public string TextOf(ExprNode node)
        {
            string text = Source.Origin?.Value;
            if (text == null || node == null || node.Span.End > text.Length) return null;
            return text.Substring(node.Span.Start, node.Span.Length).Trim();
        }
    }
}
