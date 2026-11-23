using KScript.Syntax;

namespace KScript.Runtime
{
    /// <summary>A compact money amount in a value position. The lexer keeps its integer part available as a name.</summary>
    internal static class MoneyLiterals
    {
        internal static object Read(string text)
        {
            if (SemanticTypes.TryConvert("money", text, out object amount, out string reason)) return amount;
            throw new KScriptException($"'{text}' isn't money: {reason}.", "Syntax");
        }

        internal static string AmountReceiverName(MemberNode member)
        {
            ExprNode root = member;
            while (root is MemberNode field) root = field.Target;
            return root is IdentifierNode id && IsAmountName(id.Name) ? id.Name : null;
        }

        internal static bool IsAmountName(string name) =>
            name.Length >= 4 && char.IsAsciiLetterUpper(name[0]) && char.IsAsciiLetterUpper(name[1]) && char.IsAsciiLetterUpper(name[2])
            && name.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0 && CountryCodes.Currency(name[..3]) != null;

        internal static bool TryRead(ExprNode node, out string name, out string text)
        {
            name = text = null;
            if (node is IdentifierNode integer && IsAmountName(integer.Name))
            {
                name = text = integer.Name;
                return true;
            }
            if (node is BinaryNode { Operator: ".", Left: IdentifierNode whole, Right: LiteralNode { Kind: LiteralKind.Number } fraction } dot
                && whole.Span.Length == whole.Name.Length && whole.Span.End == dot.OperatorSpan.Start && fraction.Span.Start == dot.OperatorSpan.End
                && IsAmountName(whole.Name)
                && fraction.Value.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0)
            {
                name = whole.Name;
                text = name + "." + fraction.Value;
                return true;
            }
            return false;
        }
    }
}
