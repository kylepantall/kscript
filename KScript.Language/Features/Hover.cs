using System.Text;

using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Markup;
using KScript.Language.Syntax;
using KScript.Language.Text;

namespace KScript.Language.Features
{
    public sealed partial class LanguageService
    {
        private static readonly Dictionary<string, string> OperatorDocs = new Dictionary<string, string>
        {
            ["=="] = "Loose equality: compares after conversion, so `'3' == 3` is true. Also written `eq`.",
            ["eq"] = "Loose equality: compares after conversion, so `'3' eq 3` is true. Also written `==`.",
            ["!="] = "Loose inequality. Also written `ne`.",
            ["ne"] = "Loose inequality. Also written `!=`.",
            ["==="] = "Strict equality: type and value must both match, so `'3' === 3` is false.",
            ["!=="] = "Strict inequality: true if the type or the value differs.",
            ["lt"] = "Less than. XML doesn't allow a raw `<` in attributes, so the word form is the usual spelling.",
            ["le"] = "Less than or equal.",
            ["gt"] = "Greater than (`>` also works).",
            ["ge"] = "Greater than or equal (`>=` also works).",
            ["and"] = "True if both sides are true.",
            ["or"] = "True if either side is true.",
            ["not"] = "Negates a condition.",
            ["in"] = "Membership: true if the left value is in the list (or a key of the map, or a substring) on the right.",
            ["??"] = "The left value, or the right value if the left is null: `$user.name ?? 'guest'`.",
            ["?."] = "Field access that gives null instead of an error when the value is null.",
            ["|>"] = "Pipe: `a |> f(b)` calls `f(a, b)`, so steps read left to right.",
            ["->"] = "Method call on a value: `$name->upper()`.",
            ["=>"] = "Separates a lambda's parameters from its body: `fn($x) => $x * 2`.",
            [".."] = "Range, end exclusive: `0..10`, or `0..100 step 5`.",
            ["step"] = "The step of a range: `0..100 step 5`.",
            ["**"] = "Power: `2 ** 10` is 1024.",
            ["%"] = "Remainder: `7 % 3` is 1.",
            ["fn"] = "A lambda: `fn($a, $b) => $a + $b`. Lambdas are values; store them, pass them, pipe into them.",
            ["match"] = "Picks a result by value: `match $x { 1 => 'one', 2 => 'two', _ => 'other' }`.",
            ["true"] = "The boolean true (prints as yes).",
            ["false"] = "The boolean false (prints as no).",
            ["null"] = "No value.",
        };

        public HoverResult GetHover(DocumentAnalysis analysis, int offset)
        {
            Cursor cursor = Locate(analysis, offset);

            if (cursor.InElementName || cursor.InEndTagName)
            {
                MarkupElement e = cursor.TagElement;
                TextSpan span = cursor.InElementName ? e.NameSpan : e.EndNameSpan.Value;
                ElementInfo info = Catalog.GetElement(e.Name);
                if (info != null)
                {
                    return new HoverResult(ElementMarkdown(info), span);
                }
                RetiredInfo retired = Catalog.GetRetiredElement(e.Name);
                if (retired != null)
                {
                    return new HoverResult($"**<{e.Name}>** was removed in KScript 2.\n\n{retired.Replacement}", span);
                }
                DocumentLink link = analysis.LinkAt(offset);
                if (link != null)
                {
                    return new HoverResult($"**<{e.Name}>** component\n\n`{link.TargetPath}`", span);
                }
                return null;
            }

            if (cursor.InAttributeName)
            {
                ElementInfo info = Catalog.GetElement(cursor.TagElement.Name);
                AttributeInfo a = info?.GetAttribute(cursor.Attribute.Name);
                if (a != null)
                {
                    return new HoverResult($"**{a.Name}** ({KindLabel(a.Kind)}{(a.Required ? ", required" : string.Empty)})\n\n{a.Description}{OptionsLine(a)}", cursor.Attribute.NameSpan);
                }
                RetiredInfo retired = Catalog.GetRetiredAttribute(cursor.Attribute.Name);
                if (retired != null)
                {
                    return new HoverResult($"**{cursor.Attribute.Name}** was removed in KScript 2.\n\n{retired.Replacement}", cursor.Attribute.NameSpan);
                }
                return null;
            }

            SymbolReference reference = analysis.ReferenceAt(offset);
            if (reference != null)
            {
                return new HoverResult(SymbolMarkdown(analysis, reference.Symbol), reference.Span);
            }

            if (cursor.Region != null && cursor.InCode)
            {
                return ExpressionHover(analysis, cursor.Region, cursor.LocalIndex);
            }

            DocumentLink fileLink = analysis.LinkAt(offset);
            if (fileLink != null)
            {
                return new HoverResult($"`{fileLink.TargetPath}`", fileLink.Span);
            }
            return null;
        }

        private HoverResult ExpressionHover(DocumentAnalysis analysis, ExpressionRegion region, int index)
        {
            ExprNode node = NodeAt(region, index);
            switch (node)
            {
                case CommandCallNode c when c.NameSpan.Contains(index):
                {
                    CallableInfo command = Catalog.GetCommand(c.Name);
                    TextSpan span = region.Source.ToSource(c.NameSpan);
                    if (command != null)
                    {
                        return new HoverResult(CallableMarkdown(command), span);
                    }
                    RetiredInfo retired = Catalog.GetRetiredCommand(c.Name);
                    return retired != null ? new HoverResult($"**@{c.Name}** was removed in KScript 2.\n\n{retired.Replacement}", span) : null;
                }

                case MethodCallNode m when m.NameSpan.Contains(index):
                    return MethodHover(analysis, region, m.Name, m.Target, m.NameSpan);

                case CallNode call when call.Callee is IdentifierNode id && id.Span.Contains(index):
                {
                    PipeNode pipe = region.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<PipeNode>().FirstOrDefault(p => p.Right == call);
                    return pipe != null ? MethodHover(analysis, region, id.Name, pipe.Left, id.Span) : null;
                }

                case IdentifierNode id2:
                {
                    PipeNode pipe = region.Nodes.SelectMany(n => n.DescendantsAndSelf()).OfType<PipeNode>().FirstOrDefault(p => p.Right is CallNode cn && cn.Callee == id2);
                    if (pipe != null)
                    {
                        return MethodHover(analysis, region, id2.Name, pipe.Left, id2.Span);
                    }
                    break;
                }

                case MemberNode member when member.NameSpan.Contains(index):
                {
                    TypeShape target = InferAt(analysis, region, member.Target);
                    if (target.Fields.TryGetValue(member.Name, out FieldInfo field))
                    {
                        return new HoverResult($"```kscript\n.{field.Name}: {field.Shape}\n```" + (field.Description != null ? "\n\n" + field.Description : string.Empty),
                            region.Source.ToSource(member.NameSpan));
                    }
                    break;
                }
            }

            // Operators and keywords, from the token under the cursor.
            foreach (TextSpan code in region.CodeSpans)
            {
                foreach (Token token in Lexer.Tokenize(region.Source.Value, code.Start, Math.Min(code.End, region.Source.Value.Length)))
                {
                    if (token.Span.Start <= index && index <= token.Span.End
                        && (token.Kind == TokenKind.Punctuation || token.Kind == TokenKind.Identifier) && OperatorDocs.TryGetValue(token.Text, out string doc))
                    {
                        return new HoverResult($"**{token.Text}**\n\n{doc}", region.Source.ToSource(token.Span));
                    }
                }
            }

            if (node is LiteralNode || node is ListNode || node is MapNode || node is BinaryNode)
            {
                TypeShape shape = InferAt(analysis, region, node);
                if (shape.Type != KType.Any)
                {
                    return new HoverResult($"```kscript\n{shape}\n```", region.Source.ToSource(node.Span));
                }
            }
            return null;
        }

        private HoverResult MethodHover(DocumentAnalysis analysis, ExpressionRegion region, string name, ExprNode target, TextSpan nameSpan)
        {
            TypeShape receiver = InferAt(analysis, region, target);
            Symbol fn = analysis.RootScope.Lookup(name, int.MaxValue);
            if (fn?.Kind == SymbolKind.Function)
            {
                return new HoverResult(SymbolMarkdown(analysis, fn), region.Source.ToSource(nameSpan));
            }
            CallableInfo method = Catalog.GetMethods(name, receiver.Type).FirstOrDefault();
            return method != null ? new HoverResult(CallableMarkdown(method, receiver), region.Source.ToSource(nameSpan)) : null;
        }

        #region Markdown

        internal static string ElementMarkdown(ElementInfo e)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("**<").Append(e.Name).Append(">**");
            if (e.Planned)
            {
                sb.Append(LanguageCatalog.Default.PageImplemented.Contains(e.Name)
                    ? " (runs in pages today; not in the script interpreter yet)"
                    : " (KScript 2, not in the interpreter yet)");
            }
            sb.Append("\n\n").Append(e.Description);
            if (e.Attributes.Count > 0)
            {
                sb.Append("\n\n| Attribute | |\n|---|---|\n");
                foreach (AttributeInfo a in e.Attributes)
                {
                    sb.Append("| `").Append(a.Name).Append('`').Append(a.Required ? " (required)" : string.Empty).Append(" | ")
                      .Append(a.Description.Replace("|", "\\|").Replace("\n", " ")).Append(" |\n");
                }
            }
            if (!string.IsNullOrEmpty(e.Example))
            {
                sb.Append("\n```xml\n").Append(e.Example).Append("\n```");
            }
            return sb.ToString();
        }

        internal static string CallableMarkdown(CallableInfo c, TypeShape receiver = null)
        {
            StringBuilder sb = new StringBuilder("```kscript\n");
            foreach (string label in c.Labels)
            {
                sb.Append(label);
                if (c.ReturnType != KType.Any)
                {
                    sb.Append(": ").Append(c.ReturnType);
                }
                sb.Append('\n');
            }
            sb.Append("```\n");
            if (!string.IsNullOrEmpty(c.Description))
            {
                sb.Append('\n').Append(c.Description);
            }
            if (c.Kind == CallableKind.Method && c.ReceiverType != KType.Any)
            {
                sb.Append("\n\nA ").Append(c.ReceiverType).Append(" method");
                if (receiver != null && receiver.Type != KType.Any)
                {
                    sb.Append("; this value is ").Append(receiver);
                }
                sb.Append('.');
            }
            foreach (ParameterInfo p in c.Signatures.SelectMany(s => s.Parameters).Where(p => p.Description != null).GroupBy(p => p.Name).Select(g => g.First()))
            {
                sb.Append("\n\n`").Append(p.Name).Append("`: ").Append(p.Description);
            }
            if (c.Planned)
            {
                sb.Append("\n\n_KScript 2: not in the interpreter yet._");
            }
            return sb.ToString();
        }

        internal string SymbolMarkdown(DocumentAnalysis analysis, Symbol s)
        {
            StringBuilder sb = new StringBuilder("```kscript\n");
            switch (s.Kind)
            {
                case SymbolKind.Function:
                    sb.Append(s.Signature);
                    break;
                case SymbolKind.Import:
                    sb.Append("import ").Append(s.Name);
                    break;
                default:
                    sb.Append(s.Kind switch
                    {
                        SymbolKind.Constant => "const ",
                        SymbolKind.Parameter => "param ",
                        SymbolKind.LoopVariable => "loop ",
                        SymbolKind.BuiltIn => "built-in ",
                        SymbolKind.FrontMatter => "front matter ",
                        _ => "let ",
                    }).Append('$').Append(s.Name);
                    if (s.Shape.Type != KType.Any)
                    {
                        sb.Append(": ").Append(s.Shape);
                    }
                    break;
            }
            sb.Append("\n```");
            if (s.Shape.Fields.Count > 0 && s.Kind != SymbolKind.Import)
            {
                sb.Append("\n\nFields: ").Append(string.Join(", ", s.Shape.Fields.Keys.Select(k => "`" + k + "`")));
            }
            else if (s.Shape.Element?.Fields.Count > 0)
            {
                sb.Append("\n\nItems have: ").Append(string.Join(", ", s.Shape.Element.Fields.Keys.Select(k => "`" + k + "`")));
            }
            if (!string.IsNullOrEmpty(s.Description))
            {
                sb.Append("\n\n").Append(s.Description);
            }
            if (s.Kind != SymbolKind.BuiltIn && s.DeclarationSpan.Length > 0 && s.SourcePath == null)
            {
                sb.Append("\n\nDeclared on line ").Append(analysis.Source.GetPosition(s.DeclarationSpan.Start).Line + 1).Append('.');
            }
            return sb.ToString();
        }

        #endregion
    }
}
