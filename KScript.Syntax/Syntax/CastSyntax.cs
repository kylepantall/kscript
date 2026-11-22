namespace KScript.Syntax
{
    /// <summary>Line continuations shared by the shorthand parser and formatter for the contextual operator <c>as</c>.</summary>
    public static class CastSyntax
    {
        public static bool StartsLine(string text, int at) =>
            at + 2 < text.Length && text[at] == 'a' && text[at + 1] == 's' && char.IsWhiteSpace(text[at + 2]) &&
            new Lexer(text, at + 2).Next().Kind == TokenKind.Identifier;

        /// <summary>Null when the ordinary continuation rules apply.</summary>
        public static bool? ContinuesAfter(string line)
        {
            bool endsAs = line.EndsWith("as", StringComparison.Ordinal);
            if (!endsAs && !(line.EndsWith('?') && line.Contains("as", StringComparison.Ordinal))) return null;
            List<Token> tokens = Lexer.Tokenize(line);
            if (endsAs)
            {
                if (tokens.Count < 2 || !tokens[^1].IsWord("as")) return null;
                Token value = tokens[^2];
                return value.Kind == TokenKind.Punctuation ? value.Text is ")" or "]" or "}" or "?" :
                    value.Kind != TokenKind.Identifier || !Lexer.Keywords.Contains(value.Text) || value.Text is "true" or "false" or "null";
            }
            // A terminal Type? is complete. A spaced '?' still continues a conditional on the next line.
            for (int i = tokens.Count - 2; i >= 0; i--)
            {
                if (!tokens[i].IsWord("as")) continue;
                var expression = ExpressionParser.ParseExpression("$value " + line.Substring(tokens[i].Span.Start));
                if (expression.Success && expression.Root is CastNode cast && cast.TypeName.EndsWith('?')) return false;
            }
            return null;
        }
    }
}
