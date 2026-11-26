using KScript.Pages;
using KScript.Syntax.Text;

namespace KScript.Syntax.Markup;

/// <summary>A live attribute written as a directive in an HTML start tag, with ranges in the original source.</summary>
public readonly record struct LiveAttributeDirective(string Name, TextSpan NameSpan, TextSpan? Value, char Quote, int End, string Error)
{
    /// <summary>What to say about a directive written with nothing between its parentheses, or null when that is fine.</summary>
    private static string EmptyMessage(string name) => name switch
    {
        ClassDirective.Attribute => "@class needs classes and conditions, such as @class(done: $todo.done).",
        "live-disable" => null,
        "bind" => "@bind needs the field it binds, such as @bind($draft).",
        "show" => "@show needs a condition, such as @show($open).",
        PendingDirective.Attribute => "@pending needs a class, such as @pending('saving') or @pending('crumple', for: 'delete').",
        _ => $"@{name} needs a handler.",
    };

    public static bool TryRead(string text, int start, out LiveAttributeDirective directive)
    {
        directive = default;
        if (start >= text.Length || text[start] != '@') return false;
        int pos = start + 1;
        while (pos < text.Length && (Lexer.IsIdentChar(text[pos]) || text[pos] == '-')) pos++;
        string name = text[(start + 1)..pos].ToLowerInvariant();
        if (name == "class") name = ClassDirective.Attribute; // @class(done: $todo.done) adds to the tag's class
        if (LiveAttributes.ArgumentFor(name) is not LiveDirectiveArgument kind) return false;
        var nameSpan = TextSpan.FromBounds(start, pos);
        pos = SkipSpace(text, pos);
        if (pos == text.Length || text[pos] != '(') return false;
        int argument = pos + 1;
        pos = SkipSpace(text, argument);
        // @bind($query, on: blur) and @live-disable(while: $saving) take a field or named arguments, read like code.
        if (name == "bind" && pos < text.Length && text[pos] == '$'
            || name == "live-disable" && text.AsSpan(pos).StartsWith("while", StringComparison.Ordinal))
        {
            kind = LiveDirectiveArgument.Statements;
        }

        if (kind is LiveDirectiveArgument.Statements or LiveDirectiveArgument.OptionalExpression)
        {
            // KScript's lexer keeps parentheses in strings, interpolations, paths and regexes inside their token.
            var lexer = new Lexer(text, argument);
            int depth = 1, recovery = text.Length;
            for (Token token = lexer.Next(); token.Kind != TokenKind.EndOfInput; token = lexer.Next())
            {
                int at = token.Span.Start;
                if (token.Is("<") && text.AsSpan(at).StartsWith("</", StringComparison.Ordinal)) { recovery = Math.Min(at, recovery); break; }
                if (token.Is(">") && recovery == text.Length) recovery = at;
                if (token.Is("(")) depth++;
                if (token.Is(")") && --depth == 0)
                {
                    if (at == pos && kind == LiveDirectiveArgument.OptionalExpression)
                    {
                        directive = new(name, nameSpan, null, '\0', token.Span.End, null); // @focus(): a flag
                        return true;
                    }
                    directive = new(name, nameSpan, TextSpan.FromBounds(argument, at), '\0', token.Span.End,
                        at == pos ? EmptyMessage(name) : null);
                    return true;
                }
            }
            directive = new(name, nameSpan, TextSpan.FromBounds(argument, recovery), '\0', recovery, $"@{(name == ClassDirective.Attribute ? "class" : name)} is missing its closing ')'.");
            return true;
        }

        if (pos < text.Length && text[pos] == ')')
        {
            directive = new(name, nameSpan, kind == LiveDirectiveArgument.Text ? new TextSpan(pos, 0) : null, '\0', pos + 1,
                kind == LiveDirectiveArgument.Text ? $"@{name} needs a quoted value." : null);
            return true;
        }

        if (pos < text.Length && text[pos] is '\'' or '"')
        {
            char quote = text[pos++];
            int from = pos;
            int quoteEnd = text.IndexOf(quote, from);
            int close = quoteEnd < 0 ? text.Length : SkipSpace(text, quoteEnd + 1);
            if (close < text.Length && text[close] == ')')
            {
                string error = kind == LiveDirectiveArgument.Flag ? $"@{name} takes no arguments."
                    : name == "live-action" && text.AsSpan(from, quoteEnd - from).Trim().IsEmpty ? "@live-action needs a nonempty action name." : null;
                directive = new(name, nameSpan, TextSpan.FromBounds(from, quoteEnd), quote, close + 1, error);
                return true;
            }
            int end = quoteEnd < 0 ? text.Length : quoteEnd;
            while (pos < end && text[pos] is not ('<' or '>' or '\r' or '\n')) pos++;
            bool missingQuote = pos != quoteEnd;
            directive = new(name, nameSpan, TextSpan.FromBounds(from, pos), quote, missingQuote ? pos : pos + 1,
                missingQuote ? $"@{name} is missing its closing {quote}." : $"@{name} is missing its closing ')'.");
            return true;
        }

        int invalid = pos;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not (')' or '<' or '>')) pos++;
        TextSpan unquoted = TextSpan.FromBounds(invalid, pos);
        if (pos < text.Length && text[pos] == ')') pos++;
        directive = new(name, nameSpan, unquoted, '\0', pos, kind == LiveDirectiveArgument.Flag
            ? $"@{name} takes no arguments." : name == "live-action" ? "Expected a quoted action name, e.g. @live-action('save')."
            : $"@{name} needs a quoted value.");
        return true;
    }

    private static int SkipSpace(string text, int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        return pos;
    }
}
