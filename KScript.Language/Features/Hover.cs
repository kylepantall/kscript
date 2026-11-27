using System.Text;

using KScript.Language.Analysis;
using KScript.Metadata;
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
            ["=="] = "Loose equality: compares after conversion, so `'3' == 3` is true. In XML, also written `eq`.",
            ["eq"] = "Loose equality: compares after conversion, so `'3' eq 3` is true. The XML spelling of `==`.",
            ["!="] = "Loose inequality. In XML, also written `ne`.",
            ["ne"] = "Loose inequality. The XML spelling of `!=`.",
            ["==="] = "Strict equality: type and value must both match, so `'3' === 3` is false.",
            ["!=="] = "Strict inequality: true if the type or the value differs.",
            ["lt"] = "Less than. The XML spelling of `<` (XML doesn't allow a raw `<` in attributes).",
            ["le"] = "Less than or equal. The XML spelling of `<=`.",
            ["gt"] = "Greater than. The XML spelling of `>`.",
            ["ge"] = "Greater than or equal. The XML spelling of `>=`.",
            ["and"] = "True if both sides are true.",
            ["or"] = "True if either side is true.",
            ["not"] = "Negates a condition.",
            ["!"] = "Negates a condition like `not`. After a method arrow, `->!method()` negates the result of that call.",
            ["in"] = "Membership: true if the left value is in the list (or a key of the map, or a substring) on the right.",
            ["??"] = "The left value, or the right value if the left is null: `$user.name ?? 'guest'`.",
            ["?."] = "Field access that gives null instead of an error when the value is null.",
            ["|>"] = "Pipe: `a |> f(b)` calls `f(a, b)`, so steps read left to right.",
            ["->"] = "Method call on a value: `$name->upper()`.",
            ["=>"] = "Separates a lambda's parameters from its body: `fn(x) => $x * 2`.",
            [".."] = "Range, end exclusive: `0..10`, or `0..100 step 5`.",
            ["step"] = "The step of a range: `0..100 step 5`.",
            ["**"] = "Power: `2 ** 10` is 1024.",
            ["%"] = "Remainder: `7 % 3` is 1.",
            ["fn"] = "A lambda: `fn(a, b) => $a + $b`. Lambdas are values; store them, pass them, pipe into them.",
            ["match"] = "Picks a result by value: `match $x { 1 => 'one', 2 => 'two', _ => 'other' }`.",
            ["true"] = "The boolean true.",
            ["false"] = "The boolean false.",
            ["null"] = "No value.",
        };

        public HoverResult GetHover(DocumentAnalysis analysis, int offset)
        {
            if (DocCommentAt(analysis, offset) is DocCommentSpot docComment)
            {
                return DocCommentHover(analysis, offset, docComment);
            }
            if (TypeBodyHover(analysis, offset) is HoverResult body)
            {
                return body;
            }
            if (TypeNameHover(analysis, offset) is HoverResult typeName)
            {
                return typeName;
            }
            Cursor cursor = Locate(analysis, offset);
            if (cursor.InAttributeValue && ShorthandHover(cursor) is HoverResult shorthand)
            {
                return shorthand;
            }

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
                    return new HoverResult(retired.Renamed ? retired.Message($"**{e.Name}**") : $"**<{e.Name}>** was removed in KScript 2.\n\n{retired.Replacement}", span);
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
                if (a == null && analysis.MarkupAt(offset).Kind != DocumentKind.Script &&
                    !global::KScript.Pages.PageElementNames.KScript.Contains(cursor.TagElement.Name) && ComponentFile(analysis.Path, cursor.TagElement.Name) == null)
                    a = global::KScript.Pages.LiveAttributes.Get(cursor.Attribute.Name);
                if (a != null)
                {
                    return new HoverResult($"**{a.Name}** ({KindLabel(a.Kind)}{(a.Required ? ", required" : string.Empty)})\n\n{DeprecationLine(a.Deprecated, a.Name)}{a.Description}{OptionsLine(a)}", cursor.Attribute.NameSpan);
                }
                RetiredInfo retired = Catalog.GetRetiredAttribute(info, cursor.Attribute.Name);
                if (retired != null)
                {
                    return new HoverResult(retired.Renamed ? retired.Message($"**{cursor.Attribute.Name}**") : $"**{cursor.Attribute.Name}** was removed in KScript 2.\n\n{retired.Replacement}", cursor.Attribute.NameSpan);
                }
                return null;
            }

            SymbolReference reference = analysis.ReferenceAt(offset);
            if (reference != null)
            {
                string markdown = reference.IsPatternLiteral
                    ? $"```kscript\n{analysis.Source[reference.Span]}: {reference.Symbol.Name}\n```"
                    : SymbolMarkdown(analysis, reference.Symbol);
                return new HoverResult(markdown, reference.Span);
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
                case CastNode cast when cast.OperatorSpan.Contains(index):
                    return new HoverResult("**as**\n\nConverts the value to the named type and checks its rules.", region.Source.ToSource(cast.OperatorSpan));

                case CastNode cast when cast.TypeSpan.Contains(index) && InferAt(analysis, region, cast).Type != KType.Any:
                    return new HoverResult($"`{cast.TypeName}`", region.Source.ToSource(cast.TypeSpan));

                case CommandCallNode c when c.NameSpan.Contains(index):
                {
                    CallableInfo command = Catalog.GetCommand(c.Name) ?? PackageAliasCommand(analysis, region, c.Name);
                    TextSpan span = region.Source.ToSource(c.NameSpan);
                    if (command != null)
                    {
                        return new HoverResult(CallableMarkdown(command) + HoverExtras(command, analysis), span);
                    }
                    RetiredInfo retired = Catalog.GetRetiredCommand(c.Name);
                    return retired != null ? new HoverResult($"**@{c.Name}** was removed in KScript 2.\n\n{retired.Replacement}", span) : null;
                }

                case MethodCallNode m when m.NameSpan.Contains(index):
                    return MethodHover(analysis, region, m.Name, m.Target, m.NameSpan, m);

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

                // 1h + 30m: the operator a native type declares, with what it gives.
                case BinaryNode binary when binary.OperatorSpan.Contains(index)
                    && TypeInference.NativeBinary(binary.Operator, InferAt(analysis, region, binary.Left), InferAt(analysis, region, binary.Right)) is NativeOperatorInfo native:
                    return new HoverResult($"```kscript\n{native.Display}: {native.Result}\n```\n\n{native.Summary}", region.Source.ToSource(binary.OperatorSpan));

                case MemberNode member when member.NameSpan.Contains(index):
                {
                    if (ConfigField(analysis, region, member) is ConfigKey setting)
                        return new HoverResult($"```kscript\n{setting.Name}: {setting.Type}\n```", region.Source.ToSource(member.NameSpan));
                    TypeShape target = InferAt(analysis, region, member.Target);
                    if (target.Fields.TryGetValue(member.Name, out FieldInfo field))
                    {
                        return new HoverResult($"```kscript\n.{field.Name}: {field.Shape.Pretty()}\n```" + (field.Description != null ? "\n\n" + field.Description : string.Empty),
                            region.Source.ToSource(member.NameSpan));
                    }
                    if ((Catalog.GetField(target.Kind, member.Name) ?? Catalog.GetField(target.Type, member.Name)) is ValueFieldInfo builtIn)
                    {
                        return new HoverResult($"```kscript\n{builtIn.Receiver}.{builtIn.Name}: {builtIn.Type ?? KType.Any}\n```\n\n{builtIn.Description}"
                            + (builtIn.Example != null ? "\n\n```kscript\n" + builtIn.Example + "\n```" : string.Empty), region.Source.ToSource(member.NameSpan));
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

        /// <summary>
        /// A native type or enum named where a type is written (<c>tz: timezone</c>, <c>fn f(size: Size)</c>,
        /// <c>type="timezone"</c>): what it is and what it accepts.
        /// </summary>
        private HoverResult TypeNameHover(DocumentAnalysis analysis, int offset)
        {
            string text = analysis.Source.Text;
            int start = WordStart(text, offset), end = offset;
            while (end < text.Length && Lexer.IsIdentChar(text[end]))
            {
                end++;
            }
            if (start == end || (start > 0 && (text[start - 1] == '$' || text[start - 1] == '@' || text[start - 1] == '.')))
            {
                return null;
            }
            int p = start;
            while (p > 0 && (text[p - 1] == ' ' || text[p - 1] == '\t'))
            {
                p--;
            }
            bool typePosition = (p > 0 && text[p - 1] == ':' && !(p > 1 && text[p - 2] == ':'))
                || (p >= 6 && text.Substring(p - 6, 6) == "type=\"");
            string name = text.Substring(start, end - start);
            return typePosition && TypeMarkdown(analysis, name) is string markdown ? new HoverResult(markdown, TextSpan.FromBounds(start, end)) : null;
        }

        /// <summary>Markdown for a native type or enum: what it is, an example, and the values it accepts. Null for other names.</summary>
        internal string TypeMarkdown(DocumentAnalysis analysis, string name)
        {
            if (EnumValuesOf(analysis, name) is IReadOnlyList<string> members)
            {
                return $"```kscript\nenum {name}\n```\n\nOne of: " + string.Join(", ", members.Select(m => "`" + m + "`")) + ".";
            }
            if (KScript.Metadata.NativeTypes.Get(name) is not KScript.Metadata.NativeTypeInfo native)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder($"```kscript\n{name}\n```\n\n{native.Description}");
            sb.Append("\n\nExample: `").Append(native.Example.Trim('\'')).Append('`');
            if (native.Options.Count > 0)
            {
                sb.Append("\n\nOptions: ").Append(string.Join(", ", native.Options.Select(o => "`" + o + "`")));
            }
            List<KScript.Runtime.NativeValueSuggestion> values = AcceptedValues(analysis, name);
            if (values.Count > 0)
            {
                sb.Append("\n\nAccepts ").Append(values.Count > 12 ? $"{values.Count} values, such as " : string.Empty)
                  .Append(string.Join(", ", values.Take(12).Select(v => "`" + v.Value + "`"))).Append(values.Count > 12 ? ", ..." : string.Empty)
                  .Append(". The editor suggests them where a ").Append(name).Append(" is expected.");
            }
            return sb.ToString();
        }

        /// <summary><c>math.sqrt</c> through <c>import KScript.Math as math</c>: the package's command, or null.</summary>
        private CallableInfo PackageAliasCommand(DocumentAnalysis analysis, ExpressionRegion region, string name)
        {
            int dot = name.IndexOf('.');
            if (dot <= 0 || analysis.RootScope?.Lookup(name.Substring(0, dot), int.MaxValue) is not Symbol { Kind: SymbolKind.Import, Package: string package })
            {
                return null;
            }
            CallableInfo command = Catalog.GetCommand(name.Substring(dot + 1));
            return command?.Package == package ? command : null;
        }

        private HoverResult MethodHover(DocumentAnalysis analysis, ExpressionRegion region, string name, ExprNode target, TextSpan nameSpan, MethodCallNode call = null)
        {
            TypeShape receiver = InferAt(analysis, region, target);
            Symbol fn = analysis.RootScope.Lookup(name, int.MaxValue);
            if (fn?.Kind == SymbolKind.Function)
            {
                return new HoverResult(SymbolMarkdown(analysis, fn), region.Source.ToSource(nameSpan));
            }
            if (analysis.ExtensionMethod(receiver, name) is Symbol extension)
            {
                return new HoverResult(SymbolMarkdown(analysis, extension), region.Source.ToSource(nameSpan));
            }
            CallableInfo method = Catalog.GetMethods(name, new TypeInference(Catalog, analysis).ReceiverType(name, receiver)).FirstOrDefault();
            if (method == null)
            {
                return null;
            }
            // A KScript.Data query chain: the SQL it sends.
            string sql = call != null && method.Package == "KScript.Data" ? QuerySql.Sketch(call, n => InferAt(analysis, region, n)) : null;
            string extra = sql != null ? "\n\nThe SQL, roughly (? are the values):\n```sql\n" + sql + "\n```" : string.Empty;
            return new HoverResult(CallableMarkdown(method, receiver) + extra + HoverExtras(method, analysis), region.Source.ToSource(nameSpan));
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
            string codeHelp = code ? CodeStatementHelp.For(e.Name) : null;
            sb.Append(code ? "**" + e.Name + "**" : "**<" + e.Name + ">**");
            if (e.Planned)
            {
                sb.Append(" (KScript 2, planned: no package implements it yet)");
            }
            sb.Append("\n\n").Append(DeprecationLine(e.Deprecated, code ? e.Name : "<" + e.Name + ">")).Append(codeHelp ?? TagsAsCode(e.Description, code));
            if (e.Package != null && e.Package != "KScript.Core")
            {
                sb.Append("\n\nFrom ").Append(e.Package).Append(e.Context == ElementContext.Page ? " (pages)." : ".");
            }
            var options = code ? CodeStatementHelp.Options(e).ToList() : e.Attributes;
            if (options.Count > 0)
            {
                sb.Append(code ? "\n\n| Argument | |\n|---|---|\n" : "\n\n| Attribute | |\n|---|---|\n");
                foreach (AttributeInfo a in options)
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

        /// <summary>
        /// What a hover adds below a command's or method's description: its example, written the way this file is
        /// written, and a link that opens its page in the docs (when the editor can run the link: <see cref="DocsCommand"/>).
        /// </summary>
        internal string HoverExtras(CallableInfo c, DocumentAnalysis analysis)
        {
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(c.Example))
            {
                bool code = analysis.Markup.Syntax == SourceSyntax.Code;
                string example = c.Example.Trim();
                bool markup = example.StartsWith('<');
                string shown = markup ? (code ? ExampleAsCode(example) : example)
                    : code ? "echo " + example
                    : "<echo>{ " + example.Replace("&", "&amp;").Replace("<", "&lt;") + " }</echo>";
                if (shown != null)
                {
                    sb.Append("\n\n**Example**\n```").Append(code ? "kscript" : "xml").Append('\n').Append(shown).Append("\n```");
                }
            }
            if (DocsCommand != null && !c.Planned)
            {
                // The command's arguments as a JSON list, written by hand so nothing is serialised by reflection.
                string search = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create(c.Name)).ToJsonString();
                sb.Append("\n\n[Open docs](command:").Append(DocsCommand).Append('?').Append(Uri.EscapeDataString(search)).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>A constant's value as written, when it is short enough to show on one line of a hover.</summary>
        private static string ShortValue(string written) =>
            written != null && written.Length <= 60 && !written.Contains('\n', StringComparison.Ordinal) ? written : null;

        /// <summary>The file an aliased member comes from, as the import writes it, or null for anything else.</summary>
        private static string ImportedFrom(Symbol s) =>
            s.Alias == null ? null
            : s.Alias.DeclaringElement?.GetAttributeValue("path")?.Trim() is { Length: > 0 } path ? path
            : s.Alias.SourcePath != null ? System.IO.Path.GetFileName(s.Alias.SourcePath) : null;

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
                case SymbolKind.Constant when s.Alias != null:
                    sb.Append("const ").Append(s.Alias.Name).Append('.').Append(s.Name);
                    if (s.Shape.Type != KType.Any)
                    {
                        sb.Append(": ").Append(s.Shape.Pretty());
                    }
                    if (ShortValue(s.ConstantText) is string value)
                    {
                        sb.Append(" = ").Append(value);
                    }
                    break;
                case SymbolKind.Constant when s.IsTypeDeclaration:
                    sb.Append(s.Shape.Kind == KType.Model ? s.Shape.Display() : s.Name);
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
            if (s.Shape.Fields.Count > 0 && s.Kind != SymbolKind.Import && (s.Shape.Type != KType.Map || s.Shape.Name != null))
            {
                // Built-in values such as $request, $err or $event: their type name doesn't show the fields, so list
                // them with their types. Hovering a field (.data) gives its description.
                sb.Append("\n\nFields: ").Append(string.Join(", ", s.Shape.Fields.Values.Select(f => "`" + f.Name + (f.Shape.IsVague ? string.Empty : ": " + f.Shape.Display(1)) + "`")));
            }
            // An aliased member with no doc comment of its own has a stand-in description naming its file, which the
            // "Imported as" line below says already.
            bool standIn = ImportedFrom(s) != null && s.Description?.StartsWith("Imported from ", StringComparison.Ordinal) == true;
            DocComment doc = !standIn && s.Kind is SymbolKind.Function or SymbolKind.Constant or SymbolKind.Variable ? s.Doc : null;
            if (!standIn && s.Kind == SymbolKind.Function && s.DocumentationWithErrors() is { Length: > 0 } functionDoc)
            {
                sb.Append("\n\n").Append(functionDoc);
            }
            else if (doc != null)
            {
                sb.Append("\n\n").Append(doc.ToMarkdown());
            }
            else if (!string.IsNullOrEmpty(s.Description) && !standIn)
            {
                sb.Append("\n\n").Append(s.Description);
            }
            if (s.Kind == SymbolKind.Parameter && s.DeclaringElement != null
                && analysis.Symbols.FirstOrDefault(f => f.Kind == SymbolKind.Function && f.DeclaringElement == s.DeclaringElement)?.Doc?.ParamText(s.Name) is string paramDoc
                && paramDoc.Length > 0)
            {
                sb.Append("\n\n").Append(paramDoc);
            }
            if (s.Kind == SymbolKind.Import && s.Members.Count > 0)
            {
                List<string> fns = s.Members.Values.Where(m => m.Kind == SymbolKind.Function).Select(m => "`" + m.Name + "`").ToList();
                List<string> others = s.Members.Values.Where(m => m.Kind != SymbolKind.Function).Select(m => "`" + m.Name + "`").ToList();
                if (fns.Count > 0)
                {
                    sb.Append("\n\nFunctions: ").Append(string.Join(", ", fns));
                }
                if (others.Count > 0)
                {
                    sb.Append("\n\nConstants and types: ").Append(string.Join(", ", others));
                }
            }
            if (ImportedFrom(s) is string from)
            {
                sb.Append("\n\nImported as ").Append(s.Alias.Name).Append(" from ").Append(from);
            }
            if (s.ExtensionOf != null)
            {
                sb.Append("\n\nAdded to ").Append(s.ExtensionOf).Append(" by `extend` in ")
                  .Append(s.SourcePath != null ? System.IO.Path.GetFileName(s.SourcePath) : "this file").Append('.');
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
