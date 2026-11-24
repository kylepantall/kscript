using KScript.Language.Analysis;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        // In a typed map, field values are ordinary expressions. Its text values need quotes.
        private readonly record struct ExpectedValueSlot(string Type, bool InMap = false, bool InContainer = false, bool IsField = false, TypeShape Shape = null);

        private static bool IsFiniteRefinement(DocumentAnalysis analysis, string type) =>
            FiniteValueChoices(analysis, type) != null;

        private static bool IsTextualChoice(DocumentAnalysis analysis, string type)
        {
            string name = SplitType(type).Name;
            string root = analysis.RootScope.Lookup(name, int.MaxValue)?.Shape.Instance?.Type ?? name;
            return root is not (KType.Number or "int" or "float" or KType.Bool or "port");
        }

        private static bool InQuotedText(ExpressionRegion region, int index)
        {
            string text = region.Source.Value;
            TextSpan span = region.CodeSpans.FirstOrDefault(s => s.Contains(index));
            if (index <= span.Start) return false;

            // The surrounding HTML attribute quote belongs to markup, outside the expression.
            var lexer = new Lexer(text, span.Start, span.End);
            for (Token token = lexer.Next(); token.Kind != TokenKind.EndOfInput && token.Span.Start < index; token = lexer.Next())
            {
                if (token.Kind != TokenKind.String || index > token.Span.End
                    || index == token.Span.End && token.Error == null)
                    continue;
                if (text[token.Span.Start] == '"')
                {
                    var template = TemplateParser.ParseQuoted(text, token.Span.Start + 1, token.Span.End - (token.Error == null ? 1 : 0));
                    if (template?.Expressions.Any(part => part.Span.Contains(index)) == true)
                        return false;
                }
                return true;
            }
            return false;
        }

        private static Token? QuotedValueAt(DocumentAnalysis analysis, int offset)
        {
            if (offset == 0 || analysis.Markup.Syntax != SourceSyntax.Code) return null;
            string text = analysis.Source.Text;
            int line = text.LastIndexOf('\n', offset - 1) + 1;
            if (text.AsSpan(line, offset - line).IndexOfAny('\'', '"') < 0) return null;
            MarkupElement element = analysis.Markup.FindElementAt(offset - 1);
            int start = element?.Span.Start ?? line;
            int end = element?.Span.End ?? text.Length;
            var lexer = new Lexer(text, start, end);
            for (Token token = lexer.Next(); token.Kind != TokenKind.EndOfInput && token.Span.Start < offset; token = lexer.Next())
            {
                if (token.Kind == TokenKind.String && (offset < token.Span.End || token.Error != null && offset == token.Span.End))
                    return token;
            }
            return null;
        }

        /// <summary>Resolves a value's declared type through unfinished list items, map fields and constructor arguments.</summary>
        private ExpectedValueSlot? ExpectedSlotAt(DocumentAnalysis analysis, int start)
        {
            const int maxContainerDepth = 32;
            if (XmlFieldValueAt(analysis, start) is MarkupAttribute attrValue
                && analysis.Source.Text.AsSpan(attrValue.Value.SourceSpan.Start, start - attrValue.Value.SourceSpan.Start).Trim().IsEmpty
                && analysis.Markup.FindElementAt(start)?.GetAttributeValue("type") is string fieldType)
                return new ExpectedValueSlot(fieldType, IsField: true);
            if (AssignmentTypeAt(analysis, start) is string declared)
                return new ExpectedValueSlot(declared, IsField: BlockOwnerAt(analysis, start) != null);
            string text = analysis.Source.Text;
            int before = start;
            while (before > 0 && char.IsWhiteSpace(text[before - 1])) before--;
            if (before > 0 && text[before - 1] == '=') return AssignedFieldSlot(analysis, before - 1);
            if (before == 0 || text[before - 1] is not ('[' or ',' or ':')) return null;

            // A trailing comma or a missing field value can leave the expression parser without a child at
            // the cursor. Lex just the containing statement so punctuation inside strings stays opaque.
            MarkupElement element = analysis.Markup.FindElementAt(before - 1);
            var attr = XmlFieldValueAt(analysis, before - 1) ?? element?.GetAttribute("value");
            bool inValue = attr?.Value != null && attr.Value.SourceSpan.Contains(before - 1);
            bool code = analysis.Markup.Syntax == SourceSyntax.Code;
            if (!code && !inValue) return null;
            int from = inValue ? attr.Value.SourceSpan.Start : element?.Span.Start ?? text.LastIndexOf('\n', before - 1) + 1;
            List<Token> tokens = Lexer.Tokenize(text, from, before);
            return tokens.Count == 0 ? null : SlotBefore(tokens.Count, 0);

            ExpectedValueSlot? SlotBefore(int index, int depth)
            {
                if (depth > maxContainerDepth) return null;
                if (index == 0)
                {
                    string type = inValue ? element.GetAttributeValue("type") : null;
                    type ??= AssignmentTypeAt(analysis, tokens[index].Span.Start);
                    return type == null ? null : new ExpectedValueSlot(type, IsField: element?.Name == "field");
                }
                Token previous = tokens[index - 1];
                if (previous.Is("="))
                    return AssignmentTypeAt(analysis, previous.Span.End) is string typeName
                        ? new ExpectedValueSlot(typeName, IsField: BlockOwnerAt(analysis, previous.Span.End) != null)
                        : AssignedFieldSlot(analysis, previous.Span.Start);
                if (previous.Is("[") || previous.Is(","))
                {
                    int open = previous.Is("[") ? index - 1 : EnclosingDelimiter(index - 1);
                    if (open < 0 || !tokens[open].Is("[") || SlotBefore(open, depth + 1) is not ExpectedValueSlot list) return null;
                    TypeRef type = TypeRef.Parse(list.Type);
                    return type?.ListDepth > 0 ? new ExpectedValueSlot(type.Item.ToString(), list.InMap, true, list.IsField, list.Shape?.Element) : null;
                }
                if (!previous.Is(":") || index < 3) return null;

                Token key = tokens[index - 2];
                if (key.Kind is not (TokenKind.Identifier or TokenKind.String)) return null;
                int container = EnclosingDelimiter(index - 2);
                if (container < 0) return null;
                Token opening = tokens[container];
                TypeShape record;
                if (opening.Is("{"))
                {
                    ExpectedValueSlot? owner = SlotBefore(container, depth + 1);
                    record = owner?.Shape ?? analysis.ScopeAt(start).Lookup(SplitType(owner?.Type).Name, start)?.Shape.Instance;
                }
                else if (opening.Is("(") && container > 0 && tokens[container - 1].Kind == TokenKind.Identifier
                    && (container < 2 || !tokens[container - 2].Is("->") && !tokens[container - 2].Is(".")))
                    record = analysis.ScopeAt(start).Lookup(tokens[container - 1].Text, start)?.Shape.Instance;
                else return null;

                return record?.Fields.TryGetValue(key.Text, out FieldInfo field) == true && field.ValueType is string valueType
                    ? new ExpectedValueSlot(valueType, opening.Is("{"), true, IsField: true, field.Shape) : null;
            }

            int EnclosingDelimiter(int index)
            {
                int depth = 0;
                for (int i = index - 1; i >= 0; i--)
                {
                    Token t = tokens[i];
                    if (t.Is("]") || t.Is("}") || t.Is(")")) depth++;
                    else if (t.Is("[") || t.Is("{") || t.Is("("))
                    {
                        if (depth == 0) return i;
                        depth--;
                    }
                }
                return -1;
            }
        }

        private static MarkupAttribute XmlFieldValueAt(DocumentAnalysis analysis, int offset)
        {
            if (analysis.Markup.Syntax != SourceSyntax.Xml || analysis.Markup.FindElementAt(offset) is not { Name: "field" } field) return null;
            return field.Attributes.FirstOrDefault(a => a.Name is "default" or "value" && a.Value?.SourceSpan.Contains(offset) == true);
        }

        private ExpectedValueSlot? AssignedFieldSlot(DocumentAnalysis analysis, int equals)
        {
            ExpressionRegion region = analysis.RegionAt(equals);
            if (region == null) return null;
            int index = region.Source.FromSource(equals);
            AssignmentNode assignment = region.Nodes.OfType<AssignmentNode>().FirstOrDefault(a => a.Operator == "="
                && a.Target.Span.End <= index && index < a.Value.Span.Start);
            (ExprNode owner, string key) = assignment?.Target switch
            {
                MemberNode member => (member.Target, member.Name),
                IndexNode { Index: LiteralNode { Kind: LiteralKind.String } name } item => (item.Target, name.Value),
                _ => (null, null),
            };
            if (owner == null) return null;
            TypeShape receiver = InferAt(analysis, region, Interpreter.AssignedThrough(owner));
            // Assignment evaluates an ordinary expression, so text choices need quotes here.
            return receiver.Fields.TryGetValue(key, out FieldInfo field) && field.ValueType is string type
                ? new ExpectedValueSlot(type, InMap: true, IsField: true, Shape: field.Shape) : null;
        }
    }
}
