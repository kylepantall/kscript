using System.Text;

using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// A tolerant parser for KScript documents that keeps exact positions for every element, attribute and text node.
    /// Scripts are parsed as strict XML (errors are reported, parsing carries on); pages are parsed like HTML5
    /// (void elements, unquoted and boolean attributes, optional end tags). Markdown pages get front matter and
    /// then the HTML rules, with everything that isn't a tag treated as text.
    /// </summary>
    public sealed class MarkupParser
    {
        private static readonly HashSet<string> VoidElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
        };

        private static readonly HashSet<string> OptionalEndTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "html", "head", "body", "p", "li", "dt", "dd", "option", "optgroup", "tr", "td", "th", "thead", "tbody",
            "tfoot", "colgroup", "rp", "rt", "caption",
        };

        private static readonly HashSet<string> RawTextElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "textarea", "title",
        };

        private static readonly Dictionary<string, string> NamedEntities = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lt"] = "<", ["gt"] = ">", ["amp"] = "&", ["quot"] = "\"", ["apos"] = "'",
            ["nbsp"] = " ", ["copy"] = "©", ["reg"] = "®", ["trade"] = "™", ["hellip"] = "…",
            ["mdash"] = "—", ["ndash"] = "–", ["middot"] = "·", ["laquo"] = "«", ["raquo"] = "»",
            ["times"] = "×", ["euro"] = "€", ["pound"] = "£",
        };

        private readonly SourceText source;
        private readonly string text;
        private readonly DocumentKind kind;
        private readonly MarkupDocument document;
        private readonly Stack<MarkupElement> open = new Stack<MarkupElement>();
        private int pos;

        private MarkupParser(SourceText source, DocumentKind kind)
        {
            this.source = source;
            text = source.Text;
            this.kind = kind;
            document = new MarkupDocument { Source = source, Kind = kind };
        }

        private bool IsHtml => kind != DocumentKind.Script;

        public static MarkupDocument Parse(string text, DocumentKind kind) => Parse(new SourceText(text), kind);

        /// <summary>Parses a file, choosing the syntax from its path: <c>.kscript</c> scripts use the brace-and-newline format.</summary>
        public static MarkupDocument Parse(SourceText source, DocumentKind kind, string path) =>
            kind == DocumentKind.Script && IsCodePath(path) ? CodeParser.Parse(source) : Parse(source, kind);

        /// <summary>True for <c>.kscript</c> files, which are written in the brace-and-newline format rather than XML.</summary>
        public static bool IsCodePath(string path) =>
            path != null && path.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase);

        public static MarkupDocument Parse(SourceText source, DocumentKind kind)
        {
            MarkupParser parser = new MarkupParser(source, kind);
            parser.Run();
            return parser.document;
        }

        /// <summary>Chooses the document kind from a file name or URI.</summary>
        public static DocumentKind KindFromPath(string path)
        {
            string p = (path ?? string.Empty).ToLowerInvariant();
            if (p.EndsWith(".md") || p.EndsWith(".markdown"))
            {
                return DocumentKind.Markdown;
            }
            if (p.EndsWith(".html") || p.EndsWith(".htm"))
            {
                return DocumentKind.Page;
            }
            return DocumentKind.Script;
        }

        #region Driver

        private void Run()
        {
            if (kind == DocumentKind.Markdown)
            {
                ParseFrontMatter();
            }

            int textStart = pos;
            while (pos < text.Length)
            {
                if (text[pos] == '<' && TryParseMarkup(textStart))
                {
                    textStart = pos;
                    continue;
                }

                if (text[pos] == '<' && !IsHtml)
                {
                    Error(new TextSpan(pos, 1), DiagnosticCodes.MarkupSyntax,
                        "A raw '<' is not allowed in XML text. Write &lt; or, in an expression, use the 'lt' operator.");
                }
                pos++;
            }

            FlushText(textStart, pos);

            while (open.Count > 0)
            {
                MarkupElement e = open.Pop();
                CloseImplicitly(e, text.Length, reportIfRequired: true);
            }
        }

        /// <summary>Parses a tag, comment, CDATA section or declaration at '&lt;'. Returns false if it is just text.</summary>
        private bool TryParseMarkup(int textStart)
        {
            if (Peek(1) == '!' && Lookahead("<!--"))
            {
                FlushText(textStart, pos);
                ParseComment();
                return true;
            }
            if (Lookahead("<![CDATA["))
            {
                FlushText(textStart, pos);
                ParseCData();
                return true;
            }
            if (Peek(1) == '!' || Peek(1) == '?')
            {
                FlushText(textStart, pos);
                SkipDeclaration();
                return true;
            }
            if (Peek(1) == '/' && IsNameStart(Peek(2)))
            {
                FlushText(textStart, pos);
                ParseEndTag();
                return true;
            }
            if (IsNameStart(Peek(1)))
            {
                FlushText(textStart, pos);
                ParseStartTag();
                return true;
            }
            return false;
        }

        #endregion

        #region Tags

        private void ParseStartTag()
        {
            int start = pos;
            pos++; // <
            int nameStart = pos;
            while (pos < text.Length && IsNameChar(text[pos]))
            {
                pos++;
            }

            MarkupElement element = new MarkupElement
            {
                Name = text.Substring(nameStart, pos - nameStart),
                NameSpan = TextSpan.FromBounds(nameStart, pos),
                Parent = open.Count > 0 ? open.Peek() : null,
            };

            if (!ParseShorthand(element))
            {
                ParseAttributes(element);
            }

            if (pos < text.Length && text[pos] == '/' && Peek(1) == '>')
            {
                pos += 2;
                element.SelfClosing = true;
                element.StartTagClosed = true;
            }
            else if (pos < text.Length && text[pos] == '>')
            {
                pos++;
                element.StartTagClosed = true;
            }
            else
            {
                Error(element.NameSpan, DiagnosticCodes.MarkupSyntax, $"The start tag <{element.Name}> is missing its closing '>'.");
            }

            element.StartTagSpan = TextSpan.FromBounds(start, pos);
            AddNode(element);

            bool isVoid = IsHtml && VoidElements.Contains(element.Name);
            if (element.SelfClosing || isVoid || !element.StartTagClosed)
            {
                element.Span = element.StartTagSpan;
                element.ContentSpan = new TextSpan(pos, 0);
                return;
            }

            if (IsHtml && RawTextElements.Contains(element.Name) && !IsKScriptBlock(element))
            {
                ParseRawText(element);
                return;
            }

            element.ContentSpan = new TextSpan(pos, 0);
            open.Push(element);
        }

        private static bool IsKScriptBlock(MarkupElement element) =>
            string.Equals(element.Name, "script", StringComparison.OrdinalIgnoreCase)
            && string.Equals(element.GetAttributeValue("type")?.Trim(), "text/kscript", StringComparison.OrdinalIgnoreCase);

        private void ParseRawText(MarkupElement element)
        {
            int contentStart = pos;
            int end = text.IndexOf("</" + element.Name, pos, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                end = text.Length;
            }

            if (end > contentStart)
            {
                MarkupText raw = new MarkupText
                {
                    Content = DecodedText.Plain(text.Substring(contentStart, end - contentStart), contentStart),
                    Span = TextSpan.FromBounds(contentStart, end),
                    Parent = element,
                    IsCData = true,
                };
                element.Children.Add(raw);
            }

            element.ContentSpan = TextSpan.FromBounds(contentStart, end);
            pos = end;
            if (pos < text.Length)
            {
                int nameStart = pos + 2;
                pos = nameStart + element.Name.Length;
                element.EndNameSpan = TextSpan.FromBounds(nameStart, pos);
                SkipWhitespace();
                if (pos < text.Length && text[pos] == '>')
                {
                    pos++;
                }
            }
            element.Span = TextSpan.FromBounds(element.StartTagSpan.Start, pos);
        }

        /// <summary>
        /// Reads <c>&lt;if $ready&gt;</c> as <c>&lt;if test="$ready"&gt;</c> (see <see cref="ShorthandCondition"/>). The
        /// attribute has an empty name span at the start of the expression, like the ones .kscript statements make.
        /// </summary>
        private bool ParseShorthand(MarkupElement element)
        {
            string name = ShorthandCondition.AttributeFor(IsHtml ? element.Name.ToLowerInvariant() : element.Name);
            if (name == null || !ShorthandCondition.Scan(text, pos, out ShorthandCondition.Head head))
            {
                return false;
            }

            TextSpan span = TextSpan.FromBounds(head.Start, head.End);
            element.Attributes.Add(new MarkupAttribute
            {
                Name = name,
                NameSpan = new TextSpan(head.Start, 0),
                Value = Decode(head.Start, head.End, reportErrors: false),
                Span = span,
                Owner = element,
                IsShorthand = true,
            });

            // Leave pos on the '>' or '/>' so the caller closes the tag as usual.
            pos = !head.Closed ? head.TagEnd : head.SelfClosing ? head.TagEnd - 2 : head.TagEnd - 1;
            return true;
        }

        private void ParseAttributes(MarkupElement element)
        {
            while (true)
            {
                SkipWhitespace();
                if (pos >= text.Length)
                {
                    return;
                }

                char c = text[pos];
                if (c == '>' || (c == '/' && Peek(1) == '>'))
                {
                    return;
                }
                if (c == '<')
                {
                    // The user is still typing this tag and has started another one.
                    return;
                }

                int nameStart = pos;
                while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '=' && text[pos] != '>'
                       && text[pos] != '<' && !(text[pos] == '/' && Peek(1) == '>') && text[pos] != '"' && text[pos] != '\'')
                {
                    pos++;
                }

                if (pos == nameStart)
                {
                    Error(new TextSpan(pos, 1), DiagnosticCodes.InvalidAttribute, $"Unexpected '{text[pos]}' in the start tag.");
                    pos++;
                    continue;
                }

                MarkupAttribute attribute = new MarkupAttribute
                {
                    Name = text.Substring(nameStart, pos - nameStart),
                    NameSpan = TextSpan.FromBounds(nameStart, pos),
                    Owner = element,
                };

                if (!IsHtml && !IsValidXmlName(attribute.Name))
                {
                    Error(attribute.NameSpan, DiagnosticCodes.InvalidAttribute, $"'{attribute.Name}' is not a valid attribute name.");
                }

                int afterName = pos;
                SkipWhitespace();
                if (pos < text.Length && text[pos] == '=')
                {
                    pos++;
                    SkipWhitespace();
                    ParseAttributeValue(attribute);
                }
                else
                {
                    pos = afterName;
                    if (!IsHtml)
                    {
                        Error(attribute.NameSpan, DiagnosticCodes.InvalidAttribute, $"The attribute '{attribute.Name}' needs a value, for example {attribute.Name}=\"...\".");
                    }
                }

                attribute.Span = TextSpan.FromBounds(nameStart, pos);

                if (element.Attributes.Any(a => string.Equals(a.Name, attribute.Name, IsHtml ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                {
                    Error(attribute.NameSpan, DiagnosticCodes.DuplicateAttribute, $"The attribute '{attribute.Name}' is set more than once.");
                }

                element.Attributes.Add(attribute);
            }
        }

        private void ParseAttributeValue(MarkupAttribute attribute)
        {
            if (pos >= text.Length)
            {
                Error(attribute.NameSpan, DiagnosticCodes.InvalidAttribute, $"The attribute '{attribute.Name}' has no value.");
                attribute.Value = DecodedText.Plain(string.Empty, pos);
                return;
            }

            char quote = text[pos];
            if (quote == '"' || quote == '\'')
            {
                attribute.Quote = quote;
                pos++;
                int valueStart = pos;
                int end = text.IndexOf(quote, pos);
                bool unterminated = end < 0;
                if (unterminated)
                {
                    // Recover at the end of the line so one missing quote doesn't swallow the document.
                    end = text.IndexOfAny(new[] { '\n', '\r' }, pos);
                    if (end < 0)
                    {
                        end = text.Length;
                    }
                    Error(attribute.NameSpan, DiagnosticCodes.InvalidAttribute, $"The value of '{attribute.Name}' is missing its closing {quote}.");
                }

                if (!IsHtml)
                {
                    int lt = text.IndexOf('<', valueStart, end - valueStart);
                    if (lt >= 0)
                    {
                        Error(new TextSpan(lt, 1), DiagnosticCodes.InvalidAttribute,
                            "'<' is not allowed in an XML attribute. Use the 'lt' or 'le' operator, or write &lt;.");
                    }
                }

                attribute.Value = Decode(valueStart, end, reportErrors: !IsHtml);
                pos = unterminated ? end : end + 1;
                return;
            }

            int start = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '>' && !(text[pos] == '/' && Peek(1) == '>'))
            {
                pos++;
            }
            if (!IsHtml)
            {
                Error(TextSpan.FromBounds(start, Math.Max(pos, start + 1)), DiagnosticCodes.InvalidAttribute,
                    $"Attribute values must be quoted in XML: {attribute.Name}=\"...\".");
            }
            attribute.Value = Decode(start, pos, reportErrors: false);
        }

        private void ParseEndTag()
        {
            int start = pos;
            pos += 2;
            int nameStart = pos;
            while (pos < text.Length && IsNameChar(text[pos]))
            {
                pos++;
            }
            string name = text.Substring(nameStart, pos - nameStart);
            TextSpan nameSpan = TextSpan.FromBounds(nameStart, pos);
            SkipWhitespace();
            if (pos < text.Length && text[pos] == '>')
            {
                pos++;
            }
            else
            {
                Error(nameSpan, DiagnosticCodes.MarkupSyntax, $"The end tag </{name}> is missing its closing '>'.");
            }

            StringComparison cmp = IsHtml ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            MarkupElement match = open.FirstOrDefault(e => string.Equals(e.Name, name, cmp));
            if (match == null)
            {
                if (!IsHtml || !VoidElements.Contains(name))
                {
                    Error(nameSpan, DiagnosticCodes.MismatchedEndTag, $"Unexpected end tag </{name}>: no <{name}> is open here.");
                }
                return;
            }

            while (open.Count > 0)
            {
                MarkupElement e = open.Pop();
                if (e == match)
                {
                    e.EndNameSpan = nameSpan;
                    e.ContentSpan = TextSpan.FromBounds(e.ContentSpan.Start, start);
                    e.Span = TextSpan.FromBounds(e.StartTagSpan.Start, pos);
                    break;
                }

                CloseImplicitly(e, start, reportIfRequired: true);
                if (!IsHtml)
                {
                    Error(nameSpan, DiagnosticCodes.MismatchedEndTag, $"Expected </{e.Name}> before </{name}>.");
                }
            }
        }

        private void CloseImplicitly(MarkupElement e, int end, bool reportIfRequired)
        {
            e.ContentSpan = TextSpan.FromBounds(e.ContentSpan.Start, end);
            e.Span = TextSpan.FromBounds(e.StartTagSpan.Start, end);
            if (reportIfRequired && (!IsHtml || !OptionalEndTags.Contains(e.Name)))
            {
                Error(e.NameSpan, DiagnosticCodes.UnclosedElement, $"<{e.Name}> is never closed. Add </{e.Name}> or write <{e.Name} />.");
            }
        }

        #endregion

        #region Other nodes

        private void ParseComment()
        {
            int start = pos;
            int end = text.IndexOf("-->", pos + 4, StringComparison.Ordinal);
            if (end < 0)
            {
                Error(new TextSpan(start, 4), DiagnosticCodes.MarkupSyntax, "This comment is never closed with -->.");
                pos = text.Length;
            }
            else
            {
                pos = end + 3;
            }
            AddNode(new MarkupComment { Span = TextSpan.FromBounds(start, pos), Text = text.Substring(start + 4, Math.Max(0, (end < 0 ? text.Length : end) - start - 4)) });
        }

        private void ParseCData()
        {
            int start = pos;
            int contentStart = pos + 9;
            int end = text.IndexOf("]]>", contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                Error(new TextSpan(start, 9), DiagnosticCodes.MarkupSyntax, "This CDATA section is never closed with ]]>.");
                end = text.Length;
                pos = end;
            }
            else
            {
                pos = end + 3;
            }

            AddNode(new MarkupText
            {
                Content = DecodedText.Plain(text.Substring(contentStart, end - contentStart), contentStart),
                Span = TextSpan.FromBounds(start, pos),
                IsCData = true,
            });
        }

        private void SkipDeclaration()
        {
            string close = Peek(1) == '?' ? "?>" : ">";
            int end = text.IndexOf(close, pos + 2, StringComparison.Ordinal);
            pos = end < 0 ? text.Length : end + close.Length;
        }

        private void ParseFrontMatter()
        {
            if (!(Lookahead("---\n") || Lookahead("---\r\n")))
            {
                return;
            }

            FrontMatter fm = new FrontMatter();
            int lineStart = text.IndexOf('\n') + 1;
            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }
                string line = text.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
                if (line.Trim() == "---")
                {
                    fm.Span = TextSpan.FromBounds(0, lineEnd);
                    document.FrontMatter = fm;
                    pos = Math.Min(text.Length, lineEnd + 1);
                    return;
                }

                int colon = line.IndexOf(':');
                if (colon > 0 && !char.IsWhiteSpace(line[0]) && line[0] != '#')
                {
                    string key = line.Substring(0, colon).Trim();
                    int valueStart = colon + 1;
                    while (valueStart < line.Length && line[valueStart] == ' ')
                    {
                        valueStart++;
                    }
                    fm.Entries.Add(new FrontMatterEntry
                    {
                        Key = key,
                        KeySpan = new TextSpan(lineStart, key.Length),
                        Value = DecodedText.Plain(line.Substring(valueStart), lineStart + valueStart),
                    });
                }

                lineStart = lineEnd + 1;
            }

            Error(new TextSpan(0, 3), DiagnosticCodes.MarkupSyntax, "The front matter is never closed with a '---' line.");
            document.FrontMatter = null;
        }

        private void FlushText(int start, int end)
        {
            if (end <= start)
            {
                return;
            }
            AddNode(new MarkupText
            {
                Content = Decode(start, end, reportErrors: !IsHtml),
                Span = TextSpan.FromBounds(start, end),
            });
        }

        private void AddNode(MarkupNode node)
        {
            if (open.Count > 0)
            {
                node.Parent = open.Peek();
                open.Peek().Children.Add(node);
            }
            else
            {
                document.Children.Add(node);
            }
        }

        #endregion

        #region Helpers

        /// <summary>Decodes entities in text[start..end) and records where each decoded character came from.</summary>
        private DecodedText Decode(int start, int end, bool reportErrors)
        {
            if (text.IndexOf('&', start, end - start) < 0)
            {
                return DecodedText.Plain(text.Substring(start, end - start), start);
            }

            StringBuilder sb = new StringBuilder(end - start);
            List<int> map = new List<int>(end - start + 1);
            int i = start;
            while (i < end)
            {
                if (text[i] == '&')
                {
                    int semi = text.IndexOf(';', i + 1, Math.Min(12, end - i - 1 < 0 ? 0 : end - i - 1));
                    string decoded = semi > i ? DecodeEntity(text.Substring(i + 1, semi - i - 1)) : null;
                    if (decoded != null)
                    {
                        foreach (char c in decoded)
                        {
                            sb.Append(c);
                            map.Add(i);
                        }
                        i = semi + 1;
                        continue;
                    }
                    if (reportErrors)
                    {
                        Error(new TextSpan(i, 1), DiagnosticCodes.MarkupSyntax,
                            "A raw '&' is not allowed in XML. Write &amp;, or use the 'and' operator in expressions.");
                    }
                }
                sb.Append(text[i]);
                map.Add(i);
                i++;
            }
            map.Add(end);
            return new DecodedText(sb.ToString(), map.ToArray());
        }

        private static string DecodeEntity(string name)
        {
            if (name.Length > 1 && name[0] == '#')
            {
                try
                {
                    int code = name[1] == 'x' || name[1] == 'X'
                        ? Convert.ToInt32(name.Substring(2), 16)
                        : int.Parse(name.Substring(1), System.Globalization.CultureInfo.InvariantCulture);
                    return char.ConvertFromUtf32(code);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return NamedEntities.TryGetValue(name, out string value) ? value : null;
        }

        private void Error(TextSpan span, string code, string message) =>
            document.Diagnostics.Add(new Diagnostic(span, DiagnosticSeverity.Error, code, message));

        private char Peek(int offset) => pos + offset < text.Length ? text[pos + offset] : '\0';

        private bool Lookahead(string s) => string.CompareOrdinal(text, pos, s, 0, s.Length) == 0;

        private void SkipWhitespace()
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }
        }

        private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_' || c == ':';

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '-' || c == '.';

        private static bool IsValidXmlName(string name) => name.Length > 0 && IsNameStart(name[0]) && name.All(IsNameChar);

        #endregion
    }
}
