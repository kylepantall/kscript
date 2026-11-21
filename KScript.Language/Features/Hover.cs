using System.Text;

using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

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
            if (DocCommentAt(analysis, offset) is DocCommentSpot docComment)
            {
                return DocCommentHover(analysis, offset, docComment);
            }
            Cursor cursor = Locate(analysis, offset);

            if (cursor.InElementName || cursor.InEndTagName)
            {
                MarkupElement e = cursor.TagElement;
                TextSpan span = cursor.InElementName ? e.NameSpan : e.EndNameSpan.Value;
                ElementInfo info = Catalog.GetElement(e.Name);
                if (info != null)
                {
                    return new HoverResult(ElementMarkdown(info, analysis.Markup.Syntax == SourceSyntax.Code), span);
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
                    return new HoverResult($"**{a.Name}** ({KindLabel(a.Kind)}{(a.Required ? ", required" : string.Empty)})\n\n{DeprecationLine(a.Deprecated, a.Name)}{a.Description}{OptionsLine(a)}", cursor.Attribute.NameSpan);
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
            if (ArgumentNameHover(analysis, region, index) is HoverResult argument)
            {
                return argument;
            }

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

                // In |> upper(), the node under the cursor is the identifier, not the call around it.
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
                        return new HoverResult($"```kscript\n.{field.Name}: {field.Shape.Pretty()}\n```" + (field.Description != null ? "\n\n" + field.Description : string.Empty),
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

        /// <summary>"**Deprecated.** Why. Use X instead." and a blank line, or empty when not deprecated.</summary>
        internal static string DeprecationLine(Deprecation d, string subject)
        {
            if (d == null)
            {
                return string.Empty;
            }
            string rest = d.Describe(subject).Substring(subject.Length + " is deprecated.".Length).Trim();
            return rest.Length == 0 ? "**Deprecated.**\n\n" : "**Deprecated.** " + rest + "\n\n";
        }

        /// <summary>An element's docs, written for the file: <paramref name="code"/> shows names and the example in .kscript form.</summary>
        internal static string ElementMarkdown(ElementInfo e, bool code = false)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(code ? "**" + e.Name + "**" : "**<" + e.Name + ">**");
            if (e.Planned)
            {
                sb.Append(" (KScript 2, planned: no package implements it yet)");
            }
            sb.Append("\n\n").Append(DeprecationLine(e.Deprecated, code ? e.Name : "<" + e.Name + ">")).Append(TagsAsCode(e.Description, code));
            if (e.Package != null && e.Package != "KScript.Core")
            {
                sb.Append("\n\nFrom ").Append(e.Package).Append(e.Context == ElementContext.Page ? " (pages)." : ".");
            }
            if (e.Attributes.Count > 0)
            {
                sb.Append(code ? "\n\n| Argument | |\n|---|---|\n" : "\n\n| Attribute | |\n|---|---|\n");
                foreach (AttributeInfo a in e.Attributes)
                {
                    sb.Append("| ").Append(a.Deprecated != null ? "~~`" + a.Name + "`~~" : "`" + a.Name + "`").Append(a.Required ? " (required)" : string.Empty).Append(" | ")
                      .Append((DeprecationLine(a.Deprecated, a.Name).Trim() + " " + TagsAsCode(a.Description, code)).Trim().Replace("|", "\\|").Replace("\n", " ")).Append(" |\n");
                }
            }
            if (!string.IsNullOrEmpty(e.Example))
            {
                string example = code ? ExampleAsCode(e.Example) : null;
                sb.Append(example != null ? "\n```kscript\n" + example : "\n```xml\n" + e.Example).Append("\n```");
            }
            return sb.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex TagMention = new System.Text.RegularExpressions.Regex(@"<(/?)([A-Za-z][\w.-]*)(\s*/)?>");

        /// <summary>
        /// Markdown hides a bare &lt;describe&gt; as an HTML tag, so element names in prose become code: <c>`&lt;describe&gt;`</c>,
        /// or <c>`describe`</c> in a .kscript file. Text already in backticks is left as it is.
        /// </summary>
        internal static string TagsAsCode(string text, bool code)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            string[] parts = text.Split('`');
            for (int i = 0; i < parts.Length; i += 2)
            {
                parts[i] = TagMention.Replace(parts[i], m => code ? "`" + m.Groups[2].Value + "`" : "`" + m.Value + "`");
            }
            return string.Join("`", parts);
        }

        /// <summary>An XML example in .kscript form, or null when it doesn't convert cleanly.</summary>
        internal static string ExampleAsCode(string xml)
        {
            MarkupDocument document = MarkupParser.Parse("<kscript>\n" + xml + "\n</kscript>", DocumentKind.Script);
            if (document.Diagnostics.Count > 0)
            {
                return null;
            }
            ConversionResult converted = ScriptConverter.ToCode(document);
            return converted.Warnings.Count > 0 ? null : converted.Text.Trim();
        }

        internal static string CallableMarkdown(CallableInfo c, TypeShape receiver = null)
        {
            StringBuilder sb = new StringBuilder("```kscript\n");
            foreach (string label in c.Labels)
            {
                sb.Append(label);
                TypeShape returns = c.Kind == CallableKind.Method && receiver != null ? TypeInference.MethodResult(c, receiver) : new TypeShape(c.ReturnType);
                if (returns.Type != KType.Any)
                {
                    sb.Append(": ").Append(returns.Display());
                }
                sb.Append('\n');
            }
            sb.Append("```\n");
            if (c.Deprecated != null)
            {
                sb.Append('\n').Append(DeprecationLine(c.Deprecated, c.Prefix + c.Name).TrimEnd());
            }
            if (!string.IsNullOrEmpty(c.Description))
            {
                sb.Append('\n').Append(c.Description);
            }
            foreach (ParameterInfo p in c.Signatures.SelectMany(s => s.Parameters).Where(p => p.Deprecated != null).GroupBy(p => p.Name).Select(g => g.First()))
            {
                sb.Append("\n\n").Append(DeprecationLine(p.Deprecated, "`" + p.Name + "`").TrimEnd());
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
            if (c.Planned)
            {
                sb.Append("\n\n_KScript 2, planned: no package implements it yet._");
            }
            else if (c.Package != null && c.Package != "KScript.Core")
            {
                sb.Append("\n\nFrom ").Append(c.Package).Append('.');
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
                    if (s.ReturnShape.Type != KType.Any)
                    {
                        sb.Append(": ").Append(s.ReturnShape.Pretty());
                    }
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
                        sb.Append(": ").Append(s.Shape.Pretty());
                    }
                    break;
            }
            sb.Append("\n```");
            if (s.Shape.Fields.Count > 0 && s.Kind != SymbolKind.Import && s.Shape.Type != KType.Map)
            {
                // Built-in values such as $request or $err: their type name doesn't show the fields.
                sb.Append("\n\nFields: ").Append(string.Join(", ", s.Shape.Fields.Keys.Select(k => "`" + k + "`")));
            }
            DocComment doc = s.Kind is SymbolKind.Function or SymbolKind.Constant or SymbolKind.Variable ? s.Doc : null;
            if (doc != null)
            {
                sb.Append("\n\n").Append(doc.ToMarkdown());
            }
            else if (!string.IsNullOrEmpty(s.Description))
            {
                sb.Append("\n\n").Append(s.Description);
            }
            if (s.Kind == SymbolKind.Parameter && s.DeclaringElement != null
                && analysis.Symbols.FirstOrDefault(f => f.Kind == SymbolKind.Function && f.DeclaringElement == s.DeclaringElement)?.Doc?.ParamText(s.Name) is string paramDoc
                && paramDoc.Length > 0)
            {
                sb.Append("\n\n").Append(paramDoc);
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
