using System.Text;

using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// Parses the brace-and-newline KScript format (<c>.kscript</c> files) into the same <see cref="MarkupDocument"/>
    /// that <see cref="MarkupParser"/> builds from XML, so the analyzer, the interpreter and every package run it
    /// unchanged. Keywords such as <c>let</c>, <c>if</c>, <c>for</c> and <c>fn</c> become the elements of the same name;
    /// any other name, such as <c>input(to: name) "Prompt: "</c>, is an element written as
    /// <c>name(attribute: value, ...) "text" { children }</c>. Expressions are kept as the exact source text, so
    /// positions in errors and in the editor point into the file.
    /// </summary>
    public sealed class CodeParser
    {
        private static readonly string[] SetOperators = { "??=", "+=", "-=", "*=", "/=", "=" };

        // A line that ends with one of these carries on onto the next line.
        private static readonly string[] TrailingContinuations =
        {
            "|>", "->", "=>", "??", "==", "!=", ">=", "<=", "+", "-", "*", "/", "%", ",", "?", ":", "=", "<", ">", "..",
        };

        // A line that starts with one of these continues the line before.
        private static readonly string[] LeadingContinuations = { "|>", "->", "??", "?.", "?", ":", "." };

        private static readonly string[] WordContinuations = { "and", "or", "not", "step", "in" };

        private readonly SourceText source;
        private readonly string text;
        private readonly MarkupDocument document;
        private int pos;
        private StringBuilder pendingDoc;
        private int pendingDocStart;

        private CodeParser(SourceText source)
        {
            this.source = source;
            text = source.Text;
            document = new MarkupDocument { Source = source, Kind = DocumentKind.Script, Syntax = SourceSyntax.Code };
        }

        public static MarkupDocument Parse(string text) => Parse(new SourceText(text));

        public static MarkupDocument Parse(SourceText source)
        {
            CodeParser parser = new CodeParser(source);
            parser.Run();
            return parser.document;
        }

        private enum Stop
        {
            /// <summary>A statement's expression: ends at a new line, ';' or the '}' that closes the block.</summary>
            Line,

            /// <summary>The head of a block (if, for, while, match, case): also ends at '{'.</summary>
            Header,

            /// <summary>An attribute value inside name(...): ends at ',' or ')'.</summary>
            Argument,

            /// <summary>Inside parentheses, such as a function's parameters: ends at the closing ')'.</summary>
            Group,
        }

        #region Driver

        private void Run()
        {
            MarkupElement root = new MarkupElement
            {
                Name = "kscript",
                NameSpan = new TextSpan(0, 0),
                StartTagSpan = new TextSpan(0, 0),
                StartTagClosed = true,
                Span = new TextSpan(0, text.Length),
                ContentSpan = new TextSpan(0, text.Length),
            };

            ParseBody(root, braced: false);

            // A file that is a single package element, such as KScript.Window { ... }, uses it as the root,
            // as an XML script would.
            List<MarkupElement> top = root.ChildElements.ToList();
            if (top.Count == 1 && root.Children.Count == 1 && top[0].Name.StartsWith("KScript.", StringComparison.Ordinal))
            {
                top[0].Parent = null;
                document.Children.Add(top[0]);
                return;
            }
            document.Children.Add(root);
        }

        /// <summary>Parses statements into a parent, up to its closing '}' (or the end of the file).</summary>
        private void ParseBody(MarkupElement parent, bool braced)
        {
            while (true)
            {
                SkipTrivia(newlines: true);
                if (pos >= text.Length)
                {
                    if (braced)
                    {
                        Error(new TextSpan(text.Length, 0), DiagnosticCodes.UnclosedElement, $"This <{parent.Name}> block is missing its closing }}.");
                    }
                    return;
                }

                char c = text[pos];
                if (c == '}')
                {
                    if (braced)
                    {
                        pos++;
                        return;
                    }
                    Error(new TextSpan(pos, 1), DiagnosticCodes.MarkupSyntax, "This } doesn't close anything.");
                    pos++;
                    continue;
                }
                if (c == ';')
                {
                    pos++;
                    continue;
                }

                ParseStatement(parent);
                EndStatement();
            }
        }

        /// <summary>After a statement only a comment, a new line, ';' or '}' may follow on the same line.</summary>
        private void EndStatement()
        {
            SkipTrivia(newlines: false);
            if (pos >= text.Length || text[pos] == '\n' || text[pos] == '\r' || text[pos] == ';' || text[pos] == '}')
            {
                return;
            }
            int start = pos;
            while (pos < text.Length && text[pos] != '\n')
            {
                pos++;
            }
            Error(TextSpan.FromBounds(start, pos), DiagnosticCodes.MarkupSyntax, "Put the next statement on a new line (or after ';').");
        }

        #endregion

        #region Statements

        private void ParseStatement(MarkupElement parent)
        {
            int start = pos;
            string doc = TakeDoc();
            if (!Lexer.IsIdentStart(text[pos]))
            {
                ParseDo(parent, start);
                return;
            }

            string word = PeekName();

            // true, false, null and fn(...) start expressions, not elements.
            if (word is "true" or "false" or "null" or "not" || (word == "fn" && Peek(2) == '('))
            {
                ParseDo(parent, start);
                return;
            }

            switch (word)
            {
                case "import": ParseImport(parent, start); return;
                case "let": ParseLet(parent, start, isConst: false, doc); return;
                case "const": ParseLet(parent, start, isConst: true, doc); return;
                case "set": ParseSet(parent, start); return;
                case "echo": ParseEcho(parent, start); return;
                case "if": ParseIf(parent, start); return;
                case "while": ParseWhile(parent, start); return;
                case "for": ParseFor(parent, start); return;
                case "match": ParseMatch(parent, start); return;
                case "fn": ParseFn(parent, start, doc); return;
                case "try": ParseTry(parent, start); return;
                case "return": ParseOptionalValue(parent, start, "return", "value"); return;
                case "exit": ParseOptionalValue(parent, start, "exit", "code"); return;
                case "throw": ParseThrow(parent, start); return;
                case "break":
                case "continue":
                    Finish(Begin(parent, word, start), start);
                    return;
                case "elif":
                case "else":
                case "catch":
                case "finally":
                case "case":
                case "when":
                case "default":
                    {
                        MarkupElement stray = ParseKeywordBlock(parent, word, start);
                        Error(stray.NameSpan, DiagnosticCodes.MisplacedElement, word switch
                        {
                            "elif" or "else" => $"'{word}' goes straight after the }} of an if (or for).",
                            "catch" or "finally" => $"'{word}' goes straight after the }} of a try.",
                            _ => $"'{word}' goes inside a match {{ }} block.",
                        });
                        return;
                    }
            }

            ParseGenericElement(parent, start);
        }

        /// <summary><c>$x += 1</c>, <c>@greet('Hi')</c>: assignments and calls become &lt;do&gt;.</summary>
        private void ParseDo(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "do", start, new TextSpan(start, 0));
            (int s, int end) = ReadExpression(Stop.Line);
            if (end == s)
            {
                Error(new TextSpan(start, Math.Min(1, text.Length - start)), DiagnosticCodes.MarkupSyntax, $"Unexpected '{text[start]}'.");
                pos = Math.Max(pos, start + 1);
            }
            else
            {
                AddText(e, DecodedText.Plain(text.Substring(s, end - s), s));
            }
            e.ContentSpan = TextSpan.FromBounds(s, end);
            Finish(e, start);
        }

        /// <summary><c>import KScript.IO</c>, <c>import KScript.Math as math</c>, <c>import "lib/x.kscript" as x</c>, <c>import a, b from "lib/x.kscript"</c>.</summary>
        private void ParseImport(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "import", start);
            SkipSpaces();
            if (Peek(0) == '"')
            {
                (DecodedText path, TextSpan span) = ReadString();
                AddAttribute(e, "src", path, span);
            }
            else
            {
                int listStart = pos;
                List<TextSpan> names = ReadNameList();
                if (names.Count == 0)
                {
                    Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write a package (import KScript.IO) or a file in quotes (import \"lib/x.kscript\").");
                    Finish(e, start);
                    return;
                }
                SkipSpaces();
                if (TryWord("from"))
                {
                    AddRawAttribute(e, "names", TextSpan.FromBounds(listStart, names[names.Count - 1].End));
                    SkipSpaces();
                    if (Peek(0) == '"')
                    {
                        (DecodedText path, TextSpan span) = ReadString();
                        AddAttribute(e, "src", path, span);
                    }
                    else
                    {
                        Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the file to import from in quotes: import a, b from \"lib/x.kscript\".");
                    }
                }
                else
                {
                    if (names.Count > 1)
                    {
                        Error(names[1], DiagnosticCodes.MarkupSyntax, "Import one package per line, or write `import a, b from \"file\"` for functions from a script.");
                    }
                    AddRawAttribute(e, "package", names[0]);
                }
            }

            SkipSpaces();
            if (TryWord("as"))
            {
                SkipSpaces();
                TextSpan alias = ReadNameSpan();
                if (alias.Length == 0)
                {
                    Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write a name after 'as'.");
                }
                else
                {
                    AddRawAttribute(e, "as", alias);
                }
            }
            Finish(e, start);
        }

        /// <summary><c>let name = expr</c>, <c>let name: int = expr</c>, <c>const name = expr</c>.</summary>
        private void ParseLet(MarkupElement parent, int start, bool isConst, string doc)
        {
            MarkupElement e = Begin(parent, "let", start, new TextSpan(start, isConst ? 5 : 3));
            SkipSpaces();
            TextSpan name = ReadNameSpan(allowDash: false);
            if (name.Length == 0)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, $"Write the variable's name after '{(isConst ? "const" : "let")}'.");
                Finish(e, start);
                return;
            }
            AddRawAttribute(e, "name", name);
            SkipSpaces();
            if (Peek(0) == ':')
            {
                pos++;
                SkipSpaces();
                TextSpan type = ReadNameSpan();
                if (type.Length > 0)
                {
                    AddRawAttribute(e, "type", type);
                }
                SkipSpaces();
            }
            if (Peek(0) == '=' && Peek(1) != '=')
            {
                pos++;
                AddExpressionAttribute(e, "value", Stop.Line);
            }
            if (isConst)
            {
                AddAttribute(e, "const", DecodedText.Plain("yes", start), e.NameSpan);
            }
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), e.NameSpan);
            }
            Finish(e, start);
        }

        /// <summary><c>set total = $total + 1</c>, <c>set total += 1</c>.</summary>
        private void ParseSet(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "set", start);
            SkipSpaces();
            TextSpan name = ReadNameSpan(allowDash: false);
            if (name.Length == 0)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the variable's name after 'set'.");
                Finish(e, start);
                return;
            }
            AddRawAttribute(e, "name", name);
            SkipSpaces();
            string op = SetOperators.FirstOrDefault(o => Lookahead(o) && !(o == "=" && Peek(1) == '='));
            if (op == null)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write = (or +=, -=, *=, /=, ??=) and the new value.");
                Finish(e, start);
                return;
            }
            if (op != "=")
            {
                AddRawAttribute(e, "op", new TextSpan(pos, op.Length));
            }
            pos += op.Length;
            AddExpressionAttribute(e, "value", Stop.Line);
            Finish(e, start);
        }

        /// <summary><c>echo "Hello, $name"</c> prints a template; <c>echo $x</c> prints a value; <c>echo(format: json) $data</c>.</summary>
        private void ParseEcho(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "echo", start);
            if (Peek(0) == '(')
            {
                ParseAttributeList(e);
            }
            SkipSpaces();
            if (AtStatementEnd())
            {
                Finish(e, start);
                return;
            }
            if (Peek(0) == '"')
            {
                int save = pos;
                (DecodedText content, TextSpan span) = ReadString();
                int after = pos;
                SkipTrivia(newlines: false);
                bool alone = AtStatementEnd();
                pos = after;
                if (alone)
                {
                    AddText(e, content, span);
                    e.ContentSpan = span;
                    Finish(e, start);
                    return;
                }
                pos = save;
            }
            AddExpressionAttribute(e, "value", Stop.Line);
            Finish(e, start);
        }

        /// <summary><c>if test { } elif test { } else { }</c>: the branches are sibling elements, as in XML.</summary>
        private void ParseIf(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "if", start);
            AddExpressionAttribute(e, "test", Stop.Header);
            ParseBlock(e);
            Finish(e, start);
            ParseElseChain(parent, allowElif: true);
        }

        private void ParseElseChain(MarkupElement parent, bool allowElif)
        {
            while (true)
            {
                int save = pos;
                SkipTrivia(newlines: true);
                int start = pos;
                string word = Lexer.IsIdentStart(Peek(0)) ? PeekName() : null;
                if (allowElif && (word == "elif" || (word == "else" && FollowedByIf())))
                {
                    MarkupElement elif = Begin(parent, "elif", start);
                    if (word == "else")
                    {
                        SkipSpaces();
                        pos += 2;
                    }
                    AddExpressionAttribute(elif, "test", Stop.Header);
                    ParseBlock(elif);
                    Finish(elif, start);
                    continue;
                }
                if (word == "else")
                {
                    MarkupElement other = Begin(parent, "else", start);
                    ParseBlock(other);
                    Finish(other, start);
                    return;
                }
                pos = save;
                return;
            }
        }

        private bool FollowedByIf()
        {
            int p = pos + 4;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
            {
                p++;
            }
            return p + 2 <= text.Length && string.CompareOrdinal(text, p, "if", 0, 2) == 0 && (p + 2 == text.Length || !Lexer.IsIdentChar(text[p + 2]));
        }

        private void ParseWhile(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "while", start);
            AddExpressionAttribute(e, "test", Stop.Header);
            ParseBlock(e);
            Finish(e, start);
        }

        /// <summary><c>for item in $list { }</c>, <c>for key, value in $map { }</c>, <c>for item at i in $list { }</c>.</summary>
        private void ParseFor(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "for", start);
            SkipSpaces();
            List<TextSpan> each = ReadNameList(allowDash: false);
            if (each.Count == 0)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the loop variable: for item in $list { }.");
            }
            else
            {
                AddRawAttribute(e, "each", TextSpan.FromBounds(each[0].Start, each[each.Count - 1].End));
            }
            SkipSpaces();
            if (TryWord("at"))
            {
                SkipSpaces();
                TextSpan index = ReadNameSpan(allowDash: false);
                if (index.Length > 0)
                {
                    AddRawAttribute(e, "index", index);
                }
                SkipSpaces();
            }
            if (TryWord("in"))
            {
                AddExpressionAttribute(e, "in", Stop.Header);
            }
            else
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write 'in' and what to loop over: for item in $list { }.");
                ReadExpression(Stop.Header);
            }
            ParseBlock(e);
            Finish(e, start);
            ParseElseChain(parent, allowElif: false);
        }

        /// <summary><c>match $x { case 1, 2 { } when $x > 9 { } default { } }</c>.</summary>
        private void ParseMatch(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "match", start);
            AddExpressionAttribute(e, "value", Stop.Header);
            SkipSpaces();
            if (Peek(0) != '{')
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MarkupSyntax, "Write { after the value, then the cases.");
                Finish(e, start);
                return;
            }
            int open = pos++;
            while (true)
            {
                SkipTrivia(newlines: true);
                if (pos >= text.Length)
                {
                    Error(new TextSpan(text.Length, 0), DiagnosticCodes.UnclosedElement, "This match block is missing its closing }.");
                    break;
                }
                if (text[pos] == '}')
                {
                    pos++;
                    break;
                }
                int caseStart = pos;
                string word = Lexer.IsIdentStart(text[pos]) ? PeekName() : null;
                if (word == "case" || word == "when")
                {
                    MarkupElement c = Begin(e, "case", caseStart);
                    AddExpressionAttribute(c, word == "case" ? "is" : "when", Stop.Header);
                    ParseBlock(c);
                    Finish(c, caseStart);
                }
                else if (word == "default")
                {
                    MarkupElement d = Begin(e, "default", caseStart);
                    ParseBlock(d);
                    Finish(d, caseStart);
                }
                else
                {
                    while (pos < text.Length && text[pos] != '\n' && text[pos] != '}')
                    {
                        pos++;
                    }
                    Error(TextSpan.FromBounds(caseStart, pos), DiagnosticCodes.MisplacedElement, "Inside match, write 'case value { }', 'when test { }' or 'default { }'.");
                }
            }
            e.ContentSpan = TextSpan.FromBounds(open + 1, Math.Max(open + 1, pos - 1));
            Finish(e, start);
        }

        /// <summary><c>fn greet(who, greeting = 'Hello') { }</c> or <c>fn double(x: int) => $x * 2</c>.</summary>
        private void ParseFn(MarkupElement parent, int start, string doc)
        {
            MarkupElement e = Begin(parent, "fn", start);
            SkipSpaces();
            TextSpan name = ReadNameSpan(allowDash: false);
            if (name.Length == 0)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the function's name: fn greet(who) { }.");
            }
            else
            {
                AddRawAttribute(e, "name", name);
            }
            SkipSpaces();
            if (Peek(0) == '(')
            {
                int open = pos++;
                (int s, int end) = ReadExpression(Stop.Group);
                if (Peek(0) == ')')
                {
                    pos++;
                }
                else
                {
                    Error(new TextSpan(open, 1), DiagnosticCodes.MarkupSyntax, "This ( is missing its closing ).");
                }
                if (end > s)
                {
                    AddRawAttribute(e, "params", TextSpan.FromBounds(s, end));
                }
            }
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), e.NameSpan);
            }
            SkipSpaces();
            if (Lookahead("=>"))
            {
                pos += 2;
                AddExpressionAttribute(e, "value", Stop.Line);
            }
            else
            {
                ParseBlock(e);
            }
            Finish(e, start);
        }

        /// <summary><c>try { } catch err when test { } catch { } finally { }</c>: catch and finally go inside the try, as in XML.</summary>
        private void ParseTry(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "try", start);
            ParseBlock(e);
            while (true)
            {
                int save = pos;
                SkipTrivia(newlines: true);
                int clauseStart = pos;
                string word = Lexer.IsIdentStart(Peek(0)) ? PeekName() : null;
                if (word == "catch")
                {
                    MarkupElement c = Begin(e, "catch", clauseStart);
                    SkipSpaces();
                    if (Lexer.IsIdentStart(Peek(0)) && PeekName() != "when")
                    {
                        AddRawAttribute(c, "as", ReadNameSpan(allowDash: false));
                        SkipSpaces();
                    }
                    if (TryWord("when"))
                    {
                        AddExpressionAttribute(c, "when", Stop.Header);
                    }
                    ParseBlock(c);
                    Finish(c, clauseStart);
                    continue;
                }
                if (word == "finally")
                {
                    MarkupElement f = Begin(e, "finally", clauseStart);
                    ParseBlock(f);
                    Finish(f, clauseStart);
                    continue;
                }
                pos = save;
                break;
            }
            Finish(e, start);
        }

        /// <summary><c>throw "message"</c> or <c>throw Validation "Age must be positive, got {$age}"</c>.</summary>
        private void ParseThrow(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "throw", start);
            SkipSpaces();
            if (Lexer.IsIdentStart(Peek(0)))
            {
                AddRawAttribute(e, "type", ReadNameSpan());
                SkipSpaces();
            }
            if (Peek(0) == '"')
            {
                (DecodedText message, TextSpan span) = ReadString();
                AddText(e, message, span);
                e.ContentSpan = span;
            }
            else if (!AtStatementEnd())
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MarkupSyntax, "Write the message in double quotes: throw Validation \"Age must be positive\".");
                ReadExpression(Stop.Line);
            }
            Finish(e, start);
        }

        /// <summary><c>return</c>, <c>return expr</c>, <c>exit</c>, <c>exit 1</c>.</summary>
        private void ParseOptionalValue(MarkupElement parent, int start, string name, string attribute)
        {
            MarkupElement e = Begin(parent, name, start);
            SkipSpaces();
            if (!AtStatementEnd())
            {
                AddExpressionAttribute(e, attribute, Stop.Line);
            }
            Finish(e, start);
        }

        /// <summary>A keyword followed by a block, used to recover from a misplaced else/catch/case.</summary>
        private MarkupElement ParseKeywordBlock(MarkupElement parent, string word, int start)
        {
            MarkupElement e = Begin(parent, word, start);
            ReadExpression(Stop.Header);
            ParseBlock(e);
            Finish(e, start);
            return e;
        }

        /// <summary>
        /// Any other element: <c>name</c>, <c>name(attr: value, other: "text, with commas")</c>, then optional
        /// <c>"text"</c> and an optional <c>{ children }</c> block.
        /// </summary>
        private void ParseGenericElement(MarkupElement parent, int start)
        {
            int nameStart = pos;
            string name = ReadName();
            MarkupElement e = Begin(parent, name, start, new TextSpan(nameStart, name.Length), consumeName: false);
            if (Peek(0) == '(')
            {
                ParseAttributeList(e);
            }
            e.StartTagSpan = TextSpan.FromBounds(start, pos);
            SkipSpaces();
            int contentStart = pos;
            if (Peek(0) == '"')
            {
                (DecodedText content, TextSpan span) = ReadString();
                AddText(e, content, span);
                contentStart = span.Start;
                e.ContentSpan = span;
                SkipSpaces();
            }
            if (Peek(0) == '{')
            {
                ParseBlock(e);
                e.ContentSpan = TextSpan.FromBounds(contentStart, pos);
            }
            else if (e.Children.Count == 0)
            {
                e.SelfClosing = true;
            }
            Finish(e, start);
        }

        /// <summary><c>(name: value, name: "text", flag)</c>, which may span lines.</summary>
        private void ParseAttributeList(MarkupElement e)
        {
            int open = pos++;
            while (true)
            {
                SkipTrivia(newlines: true);
                if (pos >= text.Length)
                {
                    Error(new TextSpan(open, 1), DiagnosticCodes.MarkupSyntax, "This ( is missing its closing ).");
                    return;
                }
                if (text[pos] == ')')
                {
                    pos++;
                    return;
                }
                if (text[pos] == ',')
                {
                    pos++;
                    continue;
                }

                int nameStart = pos;
                string name = ReadName();
                if (name.Length == 0)
                {
                    Error(new TextSpan(pos, 1), DiagnosticCodes.InvalidAttribute, $"Expected an attribute name, such as to: name, not '{text[pos]}'.");
                    while (pos < text.Length && text[pos] != ',' && text[pos] != ')' && text[pos] != '\n')
                    {
                        pos++;
                    }
                    continue;
                }
                TextSpan nameSpan = new TextSpan(nameStart, name.Length);
                if (e.GetAttribute(name) != null)
                {
                    Error(nameSpan, DiagnosticCodes.DuplicateAttribute, $"{name} is given twice.");
                }
                SkipSpaces();
                if (Peek(0) == ':' || Peek(0) == '=')
                {
                    pos++;
                    SkipTrivia(newlines: true);
                    if (Peek(0) == '"')
                    {
                        (DecodedText value, TextSpan span) = ReadString();
                        AddAttribute(e, name, value, TextSpan.FromBounds(nameStart, span.End), nameSpan, '"');
                    }
                    else
                    {
                        (int s, int end) = ReadExpression(Stop.Argument);
                        if (end == s)
                        {
                            Error(new TextSpan(pos, 0), DiagnosticCodes.InvalidAttribute, $"Write a value for {name}.");
                        }
                        AddAttribute(e, name, DecodedText.Plain(text.Substring(s, end - s), s), TextSpan.FromBounds(nameStart, end), nameSpan, '\0');
                    }
                }
                else
                {
                    // A bare name is a yes/no flag that is on.
                    AddAttribute(e, name, DecodedText.Plain("yes", nameStart), nameSpan, nameSpan, '\0');
                }
            }
        }

        /// <summary>Parses '{' statements '}' into an element.</summary>
        private void ParseBlock(MarkupElement e)
        {
            SkipSpaces();
            e.StartTagSpan = TextSpan.FromBounds(e.Span.Start, pos);
            if (Peek(0) != '{')
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MarkupSyntax, $"Write {{ to start the body of this {e.Name}.");
                return;
            }
            int open = pos++;
            ParseBody(e, braced: true);
            e.ContentSpan = TextSpan.FromBounds(open + 1, Math.Max(open + 1, text[Math.Max(0, pos - 1)] == '}' ? pos - 1 : pos));
        }

        #endregion

        #region Building the tree

        private MarkupElement Begin(MarkupElement parent, string name, int start, TextSpan? nameSpan = null, bool consumeName = true)
        {
            TextSpan span = nameSpan ?? new TextSpan(start, name.Length);
            MarkupElement e = new MarkupElement
            {
                Name = name,
                NameSpan = span,
                Parent = parent,
                StartTagClosed = true,
                Span = new TextSpan(start, 0),
            };
            parent.Children.Add(e);
            if (consumeName)
            {
                pos = span.End;
            }
            return e;
        }

        private void Finish(MarkupElement e, int start)
        {
            e.Span = TextSpan.FromBounds(start, Math.Max(start, pos));
            if (e.StartTagSpan.Length == 0)
            {
                e.StartTagSpan = e.Span;
            }
            if (e.Children.Count == 0 && e.ContentSpan.Length == 0)
            {
                e.SelfClosing = true;
                e.ContentSpan = new TextSpan(e.Span.End, 0);
            }
        }

        private void AddText(MarkupElement e, DecodedText content, TextSpan? span = null) =>
            e.Children.Add(new MarkupText { Content = content, Parent = e, Span = span ?? content.SourceSpan });

        private void AddRawAttribute(MarkupElement e, string name, TextSpan span) =>
            AddAttribute(e, name, DecodedText.Plain(text.Substring(span.Start, span.Length), span.Start), span, span, '\0');

        private void AddAttribute(MarkupElement e, string name, DecodedText value, TextSpan span, TextSpan? nameSpan = null, char quote = '\0') =>
            e.Attributes.Add(new MarkupAttribute { Name = name, NameSpan = nameSpan ?? span, Value = value, Span = span, Quote = quote, Owner = e });

        private void AddExpressionAttribute(MarkupElement e, string name, Stop stop)
        {
            (int s, int end) = ReadExpression(stop);
            if (end == s)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, $"Write the {name} for this {e.Name}.");
                return;
            }
            AddRawAttribute(e, name, TextSpan.FromBounds(s, end));
        }

        #endregion

        #region Reading

        /// <summary>
        /// Reads an expression as raw text, keeping brackets and strings balanced. A new line ends it unless a bracket
        /// is open, the line ends with an operator, or the next line starts with one (such as |>).
        /// </summary>
        private (int Start, int End) ReadExpression(Stop stop)
        {
            SkipSpaces();
            int start = pos;
            Stack<char> brackets = new Stack<char>();
            while (pos < text.Length)
            {
                char c = text[pos];
                if (c == '\'' || c == '"')
                {
                    SkipQuoted(c);
                    continue;
                }
                if (c == '/' && Peek(1) == '/' && brackets.Count == 0)
                {
                    break;
                }
                if (c == '(' || c == '[' || c == '{')
                {
                    if (c == '{' && brackets.Count == 0 && stop == Stop.Header)
                    {
                        break;
                    }
                    brackets.Push(c);
                    pos++;
                    continue;
                }
                if (c == ')' || c == ']' || c == '}')
                {
                    if (brackets.Count == 0)
                    {
                        break;
                    }
                    brackets.Pop();
                    pos++;
                    continue;
                }
                if (brackets.Count == 0)
                {
                    if (c == ';' && stop is Stop.Line or Stop.Header)
                    {
                        break;
                    }
                    if (c == ',' && stop == Stop.Argument)
                    {
                        break;
                    }
                    if (c == '\n' || c == '\r')
                    {
                        if (stop is Stop.Argument or Stop.Group || ContinuesAfter(start, pos) || ContinuesBefore(pos))
                        {
                            pos++;
                            continue;
                        }
                        break;
                    }
                }
                pos++;
            }

            int end = pos;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
            return (start, end);
        }

        private bool ContinuesAfter(int start, int newline)
        {
            int end = newline;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
            if (end == start)
            {
                return true;
            }
            string line = text.Substring(start, end - start);
            if (TrailingContinuations.Any(op => line.EndsWith(op, StringComparison.Ordinal)))
            {
                return true;
            }
            return WordContinuations.Any(w => line.EndsWith(" " + w, StringComparison.Ordinal));
        }

        private bool ContinuesBefore(int newline)
        {
            int p = newline;
            while (p < text.Length && char.IsWhiteSpace(text[p]))
            {
                p++;
            }
            if (LeadingContinuations.Any(op => string.CompareOrdinal(text, p, op, 0, op.Length) == 0))
            {
                // "..." would be a spread, but a line starting with '.' is always a continuation here.
                return true;
            }
            foreach (string w in WordContinuations)
            {
                if (string.CompareOrdinal(text, p, w, 0, w.Length) == 0 && p + w.Length < text.Length && text[p + w.Length] == ' ')
                {
                    return true;
                }
            }
            return false;
        }

        private void SkipQuoted(char quote)
        {
            int start = pos++;
            while (pos < text.Length && text[pos] != quote)
            {
                if (text[pos] == '\\' && pos + 1 < text.Length)
                {
                    pos++;
                }
                pos++;
            }
            if (pos >= text.Length)
            {
                Error(new TextSpan(start, 1), DiagnosticCodes.MarkupSyntax, $"This string is missing its closing {quote}.");
                return;
            }
            pos++;
        }

        /// <summary>
        /// Reads "text" as the text of an element or attribute. \" and \\ are escapes; everything else, including
        /// { } and new lines, is kept as written.
        /// </summary>
        private (DecodedText Value, TextSpan Span) ReadString()
        {
            int open = pos++;
            StringBuilder value = new StringBuilder();
            List<int> map = new List<int>();
            while (pos < text.Length && text[pos] != '"')
            {
                if (text[pos] == '\\' && (Peek(1) == '"' || Peek(1) == '\\'))
                {
                    map.Add(pos);
                    value.Append(text[pos + 1]);
                    pos += 2;
                    continue;
                }
                map.Add(pos);
                value.Append(text[pos]);
                pos++;
            }
            map.Add(pos);
            if (pos >= text.Length)
            {
                Error(new TextSpan(open, 1), DiagnosticCodes.MarkupSyntax, "This text is missing its closing \".");
            }
            else
            {
                pos++;
            }
            return (new DecodedText(value.ToString(), map.ToArray()), TextSpan.FromBounds(open, pos));
        }

        private List<TextSpan> ReadNameList(bool allowDash = true)
        {
            List<TextSpan> names = new List<TextSpan>();
            while (true)
            {
                TextSpan name = ReadNameSpan(allowDash);
                if (name.Length == 0)
                {
                    return names;
                }
                names.Add(name);
                int save = pos;
                SkipSpaces();
                if (Peek(0) != ',')
                {
                    pos = save;
                    return names;
                }
                pos++;
                SkipSpaces();
            }
        }

        private TextSpan ReadNameSpan(bool allowDash = true)
        {
            int start = pos;
            string name = ReadName(allowDash);
            return new TextSpan(start, name.Length);
        }

        /// <summary>A name: letters, digits, '_', and inside it '.' and '-' (KScript.Window, on-click).</summary>
        private string ReadName(bool allowDash = true)
        {
            int start = pos;
            if (pos >= text.Length || !Lexer.IsIdentStart(text[pos]))
            {
                return string.Empty;
            }
            pos++;
            while (pos < text.Length && (Lexer.IsIdentChar(text[pos])
                || (allowDash && (text[pos] == '.' || text[pos] == '-') && pos + 1 < text.Length && Lexer.IsIdentChar(text[pos + 1]))))
            {
                pos++;
            }
            return text.Substring(start, pos - start);
        }

        private string PeekName()
        {
            int save = pos;
            string name = ReadName();
            pos = save;
            return name;
        }

        private bool TryWord(string word)
        {
            if (Lookahead(word) && (pos + word.Length == text.Length || !Lexer.IsIdentChar(text[pos + word.Length])))
            {
                pos += word.Length;
                return true;
            }
            return false;
        }

        private bool AtStatementEnd()
        {
            int p = pos;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
            {
                p++;
            }
            return p >= text.Length || text[p] == '\n' || text[p] == '\r' || text[p] == ';' || text[p] == '}'
                || (text[p] == '/' && p + 1 < text.Length && text[p + 1] == '/');
        }

        private void SkipSpaces()
        {
            while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t'))
            {
                pos++;
            }
        }

        /// <summary>Skips spaces, comments and (optionally) new lines. /// comments are kept as the next declaration's description.</summary>
        private void SkipTrivia(bool newlines)
        {
            while (pos < text.Length)
            {
                char c = text[pos];
                if (c == ' ' || c == '\t' || (newlines && (c == '\n' || c == '\r')))
                {
                    pos++;
                    continue;
                }
                if (c == '/' && Peek(1) == '/')
                {
                    int start = pos;
                    while (pos < text.Length && text[pos] != '\n')
                    {
                        pos++;
                    }
                    if (newlines && string.CompareOrdinal(text, start, "///", 0, 3) == 0)
                    {
                        string line = text.Substring(start + 3, pos - start - 3).Trim();
                        if (pendingDoc == null)
                        {
                            pendingDoc = new StringBuilder();
                            pendingDocStart = start + 3;
                        }
                        else
                        {
                            pendingDoc.Append(' ');
                        }
                        pendingDoc.Append(line);
                    }
                    else if (newlines)
                    {
                        pendingDoc = null;
                    }
                    continue;
                }
                if (c == '/' && Peek(1) == '*')
                {
                    int start = pos;
                    int close = text.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                    pos = close < 0 ? text.Length : close + 2;
                    if (close < 0)
                    {
                        Error(new TextSpan(start, 2), DiagnosticCodes.MarkupSyntax, "This comment is missing its closing */.");
                    }
                    continue;
                }
                return;
            }
        }

        private string TakeDoc()
        {
            string doc = pendingDoc?.ToString();
            pendingDoc = null;
            return string.IsNullOrWhiteSpace(doc) ? null : doc;
        }

        private char Peek(int offset) => pos + offset >= 0 && pos + offset < text.Length ? text[pos + offset] : '\0';

        private bool Lookahead(string s) => pos + s.Length <= text.Length && string.CompareOrdinal(text, pos, s, 0, s.Length) == 0;

        private void Error(TextSpan span, string code, string message) =>
            document.Diagnostics.Add(new Diagnostic(span, DiagnosticSeverity.Error, code, message));

        #endregion
    }
}
