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

        // Where // and /* */ comments go as MarkupComment nodes (null while looking ahead or inside a header).
        private MarkupElement commentTarget;

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

            // A file that is a single package element, such as KScript.Window { ... }, or an explicit kscript { ... },
            // uses it as the root, as an XML script would.
            List<MarkupElement> top = root.ChildElements.ToList();
            if (top.Count == 1 && root.Children.All(c => c is MarkupElement || c is MarkupComment || (c is MarkupText t && t.IsWhitespace)) && (top[0].Name.StartsWith("KScript.", StringComparison.Ordinal) || top[0].Name == "kscript"))
            {
                foreach (MarkupNode node in root.Children.Where(n => !(n is MarkupText)))
                {
                    node.Parent = null;
                    document.Children.Add(node);
                }
                return;
            }
            document.Children.Add(root);
        }

        /// <summary>Parses statements into a parent, up to its closing '}' (or the end of the file).</summary>
        private void ParseBody(MarkupElement parent, bool braced, bool fields = false)
        {
            while (true)
            {
                commentTarget = parent;
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

                if (fields && (Lookahead("fn ") || Lookahead("fn\t")))
                {
                    // A method of the type: fn width(unit: Unit) => ...
                    int fnStart = pos;
                    string fnDoc = TakeDoc();
                    pos += 2;
                    ParseFn(parent, fnStart, fnDoc);
                }
                else if (fields && RuleLineAhead() is string rule)
                {
                    ParseRule(parent, rule);
                }
                else if (fields)
                {
                    ParseField(parent);
                }
                else
                {
                    ParseStatement(parent);
                }
                commentTarget = parent;
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

            if (LooksLikeAssignment())
            {
                ParseAssignment(parent, start, doc);
                return;
            }

            // true, false, null and fn(...) start expressions, not elements.
            if (word is "true" or "false" or "null" or "not" || (word == "fn" && Peek(2) == '('))
            {
                ParseDo(parent, start);
                return;
            }

            switch (word)
            {
                case "import": ParseImport(parent, start); return;
                case "const": ParseLet(parent, start, isConst: true, doc, keywordLength: 5); return;
                case "let":
                case "set":
                    {
                        // One way to assign: x = 3 declares x the first time and changes it after that.
                        int keywordEnd = pos + word.Length;
                        pos = keywordEnd;
                        SkipSpaces();
                        if (LooksLikeAssignment())
                        {
                            Error(TextSpan.FromBounds(start, keywordEnd), DiagnosticCodes.RetiredElement,
                                $"Drop '{word}': in a .kscript file, x = 3 declares x the first time and changes it after that.");
                            ParseAssignment(parent, pos, doc);
                            return;
                        }
                        pos = start;
                        break;
                    }
                case "echo": ParseEcho(parent, start); return;
                case "if": ParseIf(parent, start); return;
                case "while": ParseWhile(parent, start); return;
                case "for": ParseFor(parent, start); return;
                case "match": ParseMatch(parent, start); return;
                case "fn": ParseFn(parent, start, doc); return;
                case "type" when StartsDeclaration(4): ParseType(parent, start, doc); return;
                case "enum" when StartsDeclaration(4): ParseEnum(parent, start, doc); return;
                case "error" when StartsDeclaration(5): ParseError(parent, start, doc); return;
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
        private void ParseLet(MarkupElement parent, int start, bool isConst, string doc, int keywordLength)
        {
            MarkupElement e = Begin(parent, "let", start, new TextSpan(start, keywordLength));
            SkipSpaces();
            TextSpan name = ReadNameSpan(allowDash: false);
            if (name.Length == 0)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the constant's name after 'const'.");
                Finish(e, start);
                return;
            }
            AddRawAttribute(e, "name", name);
            SkipSpaces();
            if (Peek(0) == ':')
            {
                pos++;
                SkipSpaces();
                TextSpan type = ReadTypeSpan();
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
                AddAttribute(e, "const", DecodedText.Plain("yes", start), new TextSpan(e.NameSpan.Start, 0));
            }
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), new TextSpan(e.NameSpan.Start, 0));
            }
            Finish(e, start);
        }

        /// <summary>
        /// <c>total = $price * 2</c>, <c>total += 1</c>, <c>point.x = 3</c>: assignments, which declare a new name or
        /// change one already in scope. <c>count: int = $raw</c> declares with a type.
        /// </summary>
        private void ParseAssignment(MarkupElement parent, int start, string doc)
        {
            int save = pos;
            TextSpan name = ReadNameSpan(allowDash: false);
            SkipSpaces();
            if (name.Length > 0 && Peek(0) == ':' && Peek(1) != ':')
            {
                pos = save;
                ParseLet(parent, start, isConst: false, doc, keywordLength: 0);
                return;
            }
            pos = save;
            ParseDo(parent, start);
        }

        /// <summary>
        /// True at <c>name =</c>, <c>name += </c>, <c>name.field =</c>, <c>name[i] =</c> or <c>name: type =</c>.
        /// </summary>
        private bool LooksLikeAssignment()
        {
            int p = pos;
            if (p < text.Length && text[p] == '$')
            {
                p++;
            }
            if (p >= text.Length || !Lexer.IsIdentStart(text[p]))
            {
                return false;
            }
            while (p < text.Length && (Lexer.IsIdentChar(text[p]) || (text[p] == '.' && p + 1 < text.Length && Lexer.IsIdentStart(text[p + 1]))))
            {
                p++;
            }
            while (p < text.Length && text[p] == '[')
            {
                int depth = 0;
                do
                {
                    if (text[p] == '[') depth++;
                    else if (text[p] == ']') depth--;
                    p++;
                }
                while (p < text.Length && depth > 0 && text[p] != '\n');
            }
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
            {
                p++;
            }
            if (p >= text.Length)
            {
                return false;
            }
            if (text[p] == ':' && p + 1 < text.Length && text[p + 1] != ':')
            {
                // name: type = value
                int q = p + 1;
                while (q < text.Length && (text[q] == ' ' || text[q] == '\t')) q++;
                int typeStart = q;
                q = TypeEnd(q);
                if (q < 0)
                {
                    // name: type(... with no closing ): parsed as an assignment, which reports the missing ).
                    return true;
                }
                while (q < text.Length && (text[q] == ' ' || text[q] == '\t')) q++;
                return q > typeStart && q < text.Length && text[q] == '=' && (q + 1 >= text.Length || (text[q + 1] != '=' && text[q + 1] != '>'));
            }
            foreach (string op in new[] { "??=", "+=", "-=", "*=", "/=" })
            {
                if (string.CompareOrdinal(text, p, op, 0, op.Length) == 0)
                {
                    return true;
                }
            }
            return text[p] == '=' && (p + 1 >= text.Length || (text[p + 1] != '=' && text[p + 1] != '>'));
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
                bool alone = AtStatementEnd();
                pos = after;
                if (alone)
                {
                    // The text isn't part of the "start tag", so the editor treats it as text (completing {$x.}).
                    e.StartTagSpan = TextSpan.FromBounds(start, span.Start);
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
                SkipTriviaLookingAhead();
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
                commentTarget = e;
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
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), new TextSpan(e.NameSpan.Start, 0));
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

        /// <summary>True at <c>type Name</c> or <c>enum Name</c>: the keyword, then a name (<c>type(...)</c> stays an element).</summary>
        private bool StartsDeclaration(int keywordLength)
        {
            int p = pos + keywordLength;
            if (p >= text.Length || (text[p] != ' ' && text[p] != '\t'))
            {
                return false;
            }
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
            {
                p++;
            }
            return p < text.Length && Lexer.IsIdentStart(text[p]);
        }

        /// <summary>
        /// <c>type Repo(sample: "repo.json") { name: string; stars: int from stargazers_count }</c>: a &lt;type&gt;
        /// whose block holds one field per line.
        /// </summary>
        private void ParseType(MarkupElement parent, int start, string doc)
        {
            MarkupElement e = Begin(parent, "type", start);
            SkipSpaces();
            AddRawAttribute(e, "name", ReadNameSpan(allowDash: false));
            if (Peek(0) == '(')
            {
                ParseAttributeList(e);
            }
            SkipSpaces();
            if (TryWord("with"))
            {
                // type Admin with [User, Auditable] { }: with="User, Auditable".
                SkipSpaces();
                if (Peek(0) == '[')
                {
                    int open = pos;
                    int close = ClosingBracket(open);
                    if (close < 0)
                    {
                        Error(new TextSpan(open, 1), DiagnosticCodes.UnclosedElement, "This [ is missing its closing ].");
                        close = text.IndexOf('\n', open) is int nl && nl >= 0 ? nl : text.Length;
                    }
                    int s = open + 1;
                    int end = close;
                    while (s < end && char.IsWhiteSpace(text[s])) s++;
                    while (end > s && char.IsWhiteSpace(text[end - 1])) end--;
                    AddRawAttribute(e, "with", TextSpan.FromBounds(s, end));
                    pos = Math.Min(text.Length, close + 1);
                }
                else
                {
                    TextSpan single = ReadTypeSpan();
                    if (single.Length == 0)
                    {
                        Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the types to build on after 'with', such as with [User, Auditable] or with [int].");
                    }
                    else
                    {
                        AddRawAttribute(e, "with", single);
                    }
                }
            }
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), new TextSpan(e.NameSpan.Start, 0));
            }
            SkipSpaces();
            if (Peek(0) == '{')
            {
                ParseBlock(e, fields: true);
            }
            else
            {
                // type User(fields: "login: string, id: int") needs no block.
                e.StartTagSpan = TextSpan.FromBounds(start, pos);
            }
            Finish(e, start);
        }

        /// <summary>
        /// <c>error NotFound(status: 404, message: "No {$kind} {$id}") extends Validation { kind: string; id: int }</c>:
        /// an &lt;error&gt;. The options, <c>extends</c> and the block of fields are all optional.
        /// </summary>
        private void ParseError(MarkupElement parent, int start, string doc)
        {
            MarkupElement e = Begin(parent, "error", start);
            SkipSpaces();
            AddRawAttribute(e, "name", ReadNameSpan(allowDash: false));
            if (Peek(0) == '(')
            {
                ParseAttributeList(e);
            }
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), new TextSpan(e.NameSpan.Start, 0));
            }
            int save = pos;
            SkipSpaces();
            if (TryWord("extends"))
            {
                SkipSpaces();
                TextSpan parentName = ReadNameSpan(allowDash: false);
                if (parentName.Length == 0)
                {
                    Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the error type it extends after 'extends', such as Validation.");
                }
                else
                {
                    AddRawAttribute(e, "extends", parentName);
                }
                save = pos;
                SkipSpaces();
            }
            if (Peek(0) == '{')
            {
                ParseBlock(e, fields: true);
            }
            else
            {
                // error Timeout extends Network needs no block.
                pos = save;
                e.StartTagSpan = TextSpan.FromBounds(start, pos);
            }
            Finish(e, start);
        }

        /// <summary><c>enum Visibility { public, private, internal }</c>: an &lt;enum&gt;; the values are separated by commas.</summary>
        private void ParseEnum(MarkupElement parent, int start, string doc)
        {
            MarkupElement e = Begin(parent, "enum", start);
            SkipSpaces();
            AddRawAttribute(e, "name", ReadNameSpan(allowDash: false));
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), new TextSpan(e.NameSpan.Start, 0));
            }
            SkipSpaces();
            e.StartTagSpan = TextSpan.FromBounds(start, pos);
            if (Peek(0) != '{')
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MarkupSyntax, "Write the values in braces: enum Size { small, medium, large }.");
                Finish(e, start);
                return;
            }
            int open = pos++;
            int close = text.IndexOf('}', pos);
            if (close < 0)
            {
                Error(new TextSpan(open, 1), DiagnosticCodes.UnclosedElement, "This { is missing its closing }.");
                close = text.Length;
            }
            int s = open + 1;
            int end = close;
            while (s < end && char.IsWhiteSpace(text[s]))
            {
                s++;
            }
            while (end > s && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
            AddRawAttribute(e, "values", TextSpan.FromBounds(s, end));
            pos = Math.Min(text.Length, close + 1);
            Finish(e, start);
        }

        /// <summary>
        /// One line of a type: <c>name: string</c>, <c>avatar?: string</c>, <c>stars: int from stargazers_count</c>,
        /// <c>tags: string[] = []</c> or <c>label: string => $name + '!'</c>. Becomes a &lt;field&gt;.
        /// </summary>
        private void ParseField(MarkupElement parent)
        {
            int start = pos;
            string doc = TakeDoc();
            if (!Lexer.IsIdentStart(text[pos]))
            {
                Error(new TextSpan(pos, 1), DiagnosticCodes.MarkupSyntax, "A type holds one field per line, such as name: string.");
                while (pos < text.Length && text[pos] != '\n' && text[pos] != ';' && text[pos] != '}')
                {
                    pos++;
                }
                return;
            }
            MarkupElement e = Begin(parent, "field", start, new TextSpan(start, 0));
            ReadName(allowDash: false);
            if (Peek(0) == '?')
            {
                pos++;
            }
            AddRawAttribute(e, "name", TextSpan.FromBounds(start, pos));
            SkipSpaces();
            if (Peek(0) == ':')
            {
                int colon = pos++;
                SkipSpaces();
                int typeStart = pos;
                ReadTypeSpan();
                if (pos == typeStart)
                {
                    Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write the field's type after ':', such as string or int[].");
                }
                // Kept even when empty (as the spaces after ':'), so the editor offers type names at a: |.
                AddRawAttribute(e, "type", TextSpan.FromBounds(pos == typeStart ? colon + 1 : typeStart, pos));
                SkipSpaces();
            }
            if (TryWord("from"))
            {
                SkipSpaces();
                if (Peek(0) == '"')
                {
                    (DecodedText path, TextSpan span) = ReadString();
                    AddAttribute(e, "from", path, span, null, '"');
                }
                else
                {
                    int pathStart = pos;
                    while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not (';' or '}' or '='))
                    {
                        pos++;
                    }
                    if (pos == pathStart)
                    {
                        Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write where to read the field after 'from', such as owner.login.");
                    }
                    else
                    {
                        AddRawAttribute(e, "from", TextSpan.FromBounds(pathStart, pos));
                    }
                }
                SkipSpaces();
            }
            if (Lookahead("=>"))
            {
                pos += 2;
                AddExpressionAttribute(e, "value", Stop.Line);
            }
            else if (Peek(0) == '=')
            {
                pos++;
                AddExpressionAttribute(e, "default", Stop.Line);
            }
            if (doc != null)
            {
                AddAttribute(e, "description", DecodedText.Plain(doc, pendingDocStart), new TextSpan(start, 0));
            }
            Finish(e, start);
        }

        private static readonly string[] RuleWords = { "min", "max", "pattern", "one_of", "not", "validate", "normalize", "parse" };

        /// <summary>
        /// The rule a type's line starts with (<c>min: 0</c>, <c>pattern: /^[a-z]+$/</c>, <c>validate: $value > 0, 'too small'</c>),
        /// or null for a field. A line such as <c>max: int</c> is still a field: the value after ':' is a type and nothing more.
        /// </summary>
        private string RuleLineAhead()
        {
            string word = RuleWords.FirstOrDefault(w => Lookahead(w) && pos + w.Length < text.Length && !Lexer.IsIdentChar(text[pos + w.Length]));
            if (word == null)
            {
                return null;
            }
            int p = pos + word.Length;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || text[p] != ':')
            {
                return null;
            }
            p++;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (p >= text.Length || !Lexer.IsIdentStart(text[p]))
            {
                return word;
            }
            int end = TypeEnd(p);
            if (end < 0)
            {
                return word;
            }
            int saved = pos;
            pos = end;
            bool field = AtStatementEnd() || PeekAfterSpaces('=') || LookaheadWordAfterSpaces("from");
            pos = saved;
            return field ? null : word;
        }

        private bool PeekAfterSpaces(char c)
        {
            int p = pos;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            return p < text.Length && text[p] == c;
        }

        private bool LookaheadWordAfterSpaces(string word)
        {
            int saved = pos;
            SkipSpaces();
            bool found = TryWord(word);
            pos = saved;
            return found;
        }

        /// <summary>
        /// A rule line of a type. <c>min</c>, <c>max</c>, <c>pattern</c>, <c>one_of</c>, <c>not</c> and <c>parse</c> become
        /// attributes of the &lt;type&gt;; <c>validate: test, 'message'</c> and <c>normalize: value</c> become child elements.
        /// </summary>
        private void ParseRule(MarkupElement type, string rule)
        {
            int start = pos;
            TextSpan nameSpan = new TextSpan(pos, rule.Length);
            pos += rule.Length;
            SkipSpaces();
            pos++; // ':'
            SkipSpaces();
            switch (rule)
            {
                case "validate":
                {
                    MarkupElement e = Begin(type, "validate", start, nameSpan);
                    pos = nameSpan.End;
                    SkipSpaces();
                    pos++;
                    AddExpressionAttribute(e, "test", Stop.Argument);
                    if (Peek(0) == ',')
                    {
                        pos++;
                        SkipSpaces();
                        if (Peek(0) == '\'' || Peek(0) == '"')
                        {
                            // The message is text, as message="..." is in XML: its quotes go.
                            int quoteStart = pos;
                            SkipQuoted(Peek(0));
                            TextSpan inner = TextSpan.FromBounds(quoteStart + 1, Math.Max(quoteStart + 1, pos - 1));
                            AddAttribute(e, "message", DecodedText.Plain(text.Substring(inner.Start, inner.Length), inner.Start), inner, null, '\0');
                        }
                        else
                        {
                            AddExpressionAttribute(e, "message", Stop.Line);
                        }
                    }
                    Finish(e, start);
                    return;
                }
                case "normalize":
                {
                    MarkupElement e = Begin(type, "normalize", start, nameSpan);
                    pos = nameSpan.End;
                    SkipSpaces();
                    pos++;
                    AddExpressionAttribute(e, "value", Stop.Line);
                    Finish(e, start);
                    return;
                }
            }

            TextSpan value;
            if (pos >= text.Length || Peek(0) is '\n' or '\r' or '}' or ';')
            {
                value = new TextSpan(pos, 0);
            }
            else if (Peek(0) == '/' && rule == "pattern")
            {
                value = ReadRegexSpan();
            }
            else if ((Peek(0) == '\'' || Peek(0) == '"') && rule is "pattern" or "min" or "max")
            {
                // pattern: '^[a-z]+$' is the pattern itself, without its quotes.
                int quoteStart = pos;
                SkipQuoted(Peek(0));
                value = TextSpan.FromBounds(quoteStart + 1, Math.Max(quoteStart + 1, pos - 1));
            }
            else
            {
                (int s, int end) = ReadExpression(Stop.Line);
                value = TextSpan.FromBounds(s, end);
            }
            if (value.Length == 0)
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, $"Write the value of {rule}, such as {rule}: {RuleExample(rule)}.");
                return;
            }
            if (type.GetAttribute(rule) != null)
            {
                Error(nameSpan, DiagnosticCodes.MarkupSyntax, $"This type already has a {rule} rule.");
                return;
            }
            AddAttribute(type, rule, DecodedText.Plain(text.Substring(value.Start, value.Length), value.Start), value, nameSpan, '\0');
        }

        private static string RuleExample(string rule) => rule switch
        {
            "min" => "0",
            "max" => "100",
            "pattern" => "/^[a-z]+$/",
            "parse" => "$value->split('x')",
            _ => "[a, b]",
        };

        /// <summary>A regex literal such as <c>/^H(?&lt;h&gt;\d+)$/i</c>: the slashes, escapes, a [class] and the flags.</summary>
        private TextSpan ReadRegexSpan()
        {
            int start = pos++;
            bool inClass = false;
            while (pos < text.Length && text[pos] != '\n')
            {
                char c = text[pos];
                if (c == '\\')
                {
                    pos += 2;
                    continue;
                }
                if (c == '[') inClass = true;
                else if (c == ']') inClass = false;
                else if (c == '/' && !inClass)
                {
                    pos++;
                    while (pos < text.Length && char.IsLetter(text[pos])) pos++;
                    return TextSpan.FromBounds(start, pos);
                }
                pos++;
            }
            Error(new TextSpan(start, 1), DiagnosticCodes.MarkupSyntax, "This regular expression is missing its closing /.");
            return TextSpan.FromBounds(start, pos);
        }

        /// <summary><c>try { } catch err when test { } catch { } finally { }</c>: catch and finally go inside the try, as in XML.</summary>
        private void ParseTry(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "try", start);
            ParseBlock(e);
            while (true)
            {
                int save = pos;
                SkipTriviaLookingAhead();
                int clauseStart = pos;
                string word = Lexer.IsIdentStart(Peek(0)) ? PeekName() : null;
                if (word == "catch")
                {
                    MarkupElement c = Begin(e, "catch", clauseStart);
                    if (Peek(0) == '(')
                    {
                        // catch(type: "FileNotFound, Io") err { }
                        ParseAttributeList(c);
                    }
                    SkipSpaces();
                    ParseCatchHead(c);
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

        /// <summary>
        /// What follows <c>catch</c>: error types (capitalised, joined by <c>|</c>) and then <c>as name</c>, or just a
        /// lower-case name for the error. <c>catch NotFound | Conflict as e</c>, <c>catch IO</c>, <c>catch err</c>.
        /// </summary>
        private void ParseCatchHead(MarkupElement c)
        {
            if (!Lexer.IsIdentStart(Peek(0)) || PeekName() == "when")
            {
                return;
            }
            int first = pos;
            string word = PeekName();
            if (!char.IsUpper(word[0]))
            {
                AddRawAttribute(c, "as", ReadNameSpan(allowDash: false));
                SkipSpaces();
                return;
            }
            int last = pos;
            while (true)
            {
                ReadName(allowDash: false);
                last = pos;
                int save = pos;
                SkipSpaces();
                if (Peek(0) is '|' or ',')
                {
                    pos++;
                    SkipSpaces();
                    if (Lexer.IsIdentStart(Peek(0)))
                    {
                        continue;
                    }
                    Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write another error type after |, such as catch NotFound | Conflict as e.");
                    break;
                }
                pos = save;
                break;
            }
            AddRawAttribute(c, "type", TextSpan.FromBounds(first, last));
            SkipSpaces();
            if (TryWord("as"))
            {
                SkipSpaces();
                TextSpan name = ReadNameSpan(allowDash: false);
                if (name.Length == 0)
                {
                    Error(new TextSpan(pos, 0), DiagnosticCodes.MissingAttribute, "Write a name for the error after 'as', such as catch NotFound as e.");
                }
                else
                {
                    AddRawAttribute(c, "as", name);
                }
                SkipSpaces();
            }
        }

        /// <summary>
        /// <c>throw "message"</c>, <c>throw Validation "Age must be positive, got {$age}"</c>, <c>throw NotFound(id: 42)</c>,
        /// <c>throw $err</c> (a caught error, again) or a bare <c>throw</c>, which rethrows the error a catch is handling.
        /// </summary>
        private void ParseThrow(MarkupElement parent, int start)
        {
            MarkupElement e = Begin(parent, "throw", start);
            SkipSpaces();
            if (Peek(0) is '$' or '@' || (Lexer.IsIdentStart(Peek(0)) && LooksLikeCall()))
            {
                AddExpressionAttribute(e, "value", Stop.Line);
                Finish(e, start);
                return;
            }
            if (Lexer.IsIdentStart(Peek(0)))
            {
                AddRawAttribute(e, "type", ReadNameSpan());
                SkipSpaces();
            }
            if (Peek(0) == '"')
            {
                (DecodedText message, TextSpan span) = ReadString();
                e.StartTagSpan = TextSpan.FromBounds(start, span.Start);
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

        /// <summary>True at <c>Name(</c>: a call such as <c>NotFound(id: 42)</c> rather than a bare type name.</summary>
        private bool LooksLikeCall()
        {
            int p = pos;
            while (p < text.Length && Lexer.IsIdentChar(text[p]))
            {
                p++;
            }
            return p < text.Length && text[p] == '(';
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
            MarkupElement savedTarget = commentTarget;
            commentTarget = null;
            try
            {
                ParseAttributes(e);
            }
            finally
            {
                commentTarget = savedTarget;
            }
        }

        private void ParseAttributes(MarkupElement e)
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
        private void ParseBlock(MarkupElement e, bool fields = false)
        {
            SkipSpaces();
            e.StartTagSpan = TextSpan.FromBounds(e.Span.Start, pos);
            if (Peek(0) != '{')
            {
                Error(new TextSpan(pos, 0), DiagnosticCodes.MarkupSyntax, $"Write {{ to start the body of this {e.Name}.");
                return;
            }
            int open = pos++;
            ParseBody(e, braced: true, fields);
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

        // Attributes implied by a keyword (let's name, if's test) have no name in the source: an empty name span.
        private void AddRawAttribute(MarkupElement e, string name, TextSpan span) =>
            AddAttribute(e, name, DecodedText.Plain(text.Substring(span.Start, span.Length), span.Start), span, new TextSpan(span.Start, 0), '\0');

        private void AddAttribute(MarkupElement e, string name, DecodedText value, TextSpan span, TextSpan? nameSpan = null, char quote = '\0') =>
            e.Attributes.Add(new MarkupAttribute { Name = name, NameSpan = nameSpan ?? new TextSpan(span.Start, 0), Value = value, Span = span, Quote = quote, Owner = e });

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
                if (c == '/' && Peek(1) != '/' && Peek(1) != '*' && RegexCanStart(start) && SkipRegex())
                {
                    // A regex literal: its brackets and quotes are its own.
                    continue;
                }
                if (c == '/' && Peek(1) == '*')
                {
                    // At the end of a statement it is a comment of its own (kept after the statement); inside an
                    // expression, or after an operator that continues on the next line (x = 1 + /* tax */), it stays
                    // there, and the expression lexer skips it. A new line inside doesn't end it.
                    if (brackets.Count == 0 && TrailingCommentAt(pos) && !ContinuesAfter(start, pos))
                    {
                        break;
                    }
                    int close = text.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                    pos = close < 0 ? text.Length : close + 2;
                    continue;
                }
                if (c == '/' && Peek(1) == '/')
                {
                    // Inside brackets: a comment to the end of the line, skipped by the expression lexer too.
                    while (pos < text.Length && text[pos] != '\n')
                    {
                        pos++;
                    }
                    continue;
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

        /// <summary>True where a value can start, so a / begins a regex rather than dividing: after =, (, a comma, an operator or a keyword.</summary>
        private bool RegexCanStart(int start)
        {
            int p = pos - 1;
            while (p >= start && (text[p] == ' ' || text[p] == '\t'))
            {
                p--;
            }
            if (p < start)
            {
                return true;
            }
            char before = text[p];
            if (Lexer.IsIdentChar(before))
            {
                int w = p;
                while (w >= start && Lexer.IsIdentChar(text[w]))
                {
                    w--;
                }
                string word = text.Substring(w + 1, p - w);
                return word is "return" or "not" or "and" or "or" or "in" or "is" or "when" or "if" or "elif" or "while" or "match";
            }
            return before is '=' or '(' or ',' or ':' or '[' or '{' or '!' or '&' or '|' or '?' or ';' or '>' or '<' or '+' or '-' or '*' or '%';
        }

        /// <summary>Moves past a regex literal at pos (<c>/^[a-z]+$/i</c>). False, without moving, if it doesn't close on this line.</summary>
        private bool SkipRegex()
        {
            int p = pos + 1;
            bool inClass = false;
            while (p < text.Length && text[p] != '\n')
            {
                char ch = text[p];
                if (ch == '\\')
                {
                    p += 2;
                    continue;
                }
                if (ch == '[') inClass = true;
                else if (ch == ']') inClass = false;
                else if (ch == '/' && !inClass)
                {
                    // The caller has ruled out // and /*, so the literal isn't empty.
                    p++;
                    while (p < text.Length && char.IsLetter(text[p])) p++;
                    pos = p;
                    return true;
                }
                p++;
            }
            return false;
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
            string line = WithoutTrailingComments(text.Substring(start, end - start));
            if (TrailingContinuations.Any(op => line.EndsWith(op, StringComparison.Ordinal))
                && !(line.EndsWith('/') && Lexer.Tokenize(line) is { Count: > 0 } tokens && tokens[^1].Kind == TokenKind.Regex))
            {
                return true;
            }
            return WordContinuations.Any(w => line.EndsWith(" " + w, StringComparison.Ordinal));
        }

        /// <summary>The text with any /* */ comments at its end removed, so a closing */ isn't read as an operator.</summary>
        private static string WithoutTrailingComments(string line)
        {
            line = line.TrimEnd();
            while (line.EndsWith("*/", StringComparison.Ordinal))
            {
                int open = line.LastIndexOf("/*", line.Length - 2, StringComparison.Ordinal);
                if (open < 0)
                {
                    break;
                }
                line = line.Substring(0, open).TrimEnd();
            }
            return line;
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

        /// <summary>
        /// A type: a name, then any of <c>[]</c>, arguments in brackets (<c>uuid(v: 4)</c>, <c>string(min: 1)[]</c>)
        /// and a final <c>?</c>.
        /// </summary>
        private TextSpan ReadTypeSpan()
        {
            int start = pos;
            int end = TypeEnd(pos);
            if (end < 0)
            {
                int open = text.IndexOf('(', start);
                Error(new TextSpan(open, 1), DiagnosticCodes.UnclosedElement, "This ( is missing its closing ).");
                end = text.IndexOf('\n', open) is int nl && nl >= 0 ? nl : text.Length;
            }
            pos = end;
            return TextSpan.FromBounds(start, end);
        }

        /// <summary>Where the type starting at <paramref name="p"/> ends, or -1 if its arguments aren't closed on the line.</summary>
        private int TypeEnd(int p)
        {
            int start = p;
            while (p < text.Length && (Lexer.IsIdentChar(text[p]) || text[p] == '.'))
            {
                p++;
            }
            if (p == start)
            {
                return p;
            }
            while (p < text.Length)
            {
                if (text[p] == '[' && p + 1 < text.Length && text[p + 1] == ']')
                {
                    p += 2;
                }
                else if (text[p] == '(')
                {
                    int close = ClosingBracket(p);
                    if (close < 0)
                    {
                        return -1;
                    }
                    p = close + 1;
                }
                else
                {
                    break;
                }
            }
            return p < text.Length && text[p] == '?' ? p + 1 : p;
        }

        /// <summary>The bracket closing the one at <paramref name="open"/>, skipping strings and nested brackets; -1 if none on this line.</summary>
        private int ClosingBracket(int open)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0')
                {
                    if (c == '\\')
                    {
                        i++;
                    }
                    else if (c == quote)
                    {
                        quote = '\0';
                    }
                    continue;
                }
                switch (c)
                {
                    case '\'' or '"':
                        quote = c;
                        break;
                    case '(' or '[' or '{':
                        depth++;
                        break;
                    case ')' or ']' or '}':
                        depth--;
                        if (depth == 0)
                        {
                            return i;
                        }
                        break;
                    case '\n':
                        return -1;
                }
            }
            return -1;
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
                || (text[p] == '/' && p + 1 < text.Length && text[p + 1] == '/')
                || TrailingCommentAt(p);
        }

        /// <summary>True when a /* */ comment starts at <paramref name="p"/> and only comments or the line's end follow it.</summary>
        private bool TrailingCommentAt(int p)
        {
            while (p + 1 < text.Length && text[p] == '/' && text[p + 1] == '*')
            {
                int close = text.IndexOf("*/", p + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    return true;
                }
                p = close + 2;
                while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
                {
                    p++;
                }
            }
            return p >= text.Length || text[p] == '\n' || text[p] == '\r' || text[p] == '}' || text[p] == ';'
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
                if (newlines && c == '\n' && commentTarget != null && IsBlankLineAhead())
                {
                    // A blank line between statements is kept (as whitespace text), so conversion keeps the layout.
                    int start = pos;
                    while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    {
                        pos++;
                    }
                    commentTarget.Children.Add(new MarkupText { Content = DecodedText.Plain(text.Substring(start, pos - start), start), Span = TextSpan.FromBounds(start, pos), Parent = commentTarget });
                    pendingDoc = null;
                    continue;
                }
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
                        // One space after /// is the separator; more is indentation (kept for @example code).
                        string line = text.Substring(start + 3, pos - start - 3).TrimEnd();
                        if (line.StartsWith(" ", StringComparison.Ordinal))
                        {
                            line = line.Substring(1);
                        }
                        if (pendingDoc == null)
                        {
                            pendingDoc = new StringBuilder();
                            pendingDocStart = start + 3;
                        }
                        else
                        {
                            pendingDoc.Append('\n');
                        }
                        pendingDoc.Append(line);
                    }
                    else
                    {
                        if (newlines)
                        {
                            pendingDoc = null;
                        }
                        string body = text.Substring(start + 2, pos - start - 2).TrimEnd('\r');
                        AddComment(start, body.StartsWith(" ", StringComparison.Ordinal) ? body.Substring(1) : body);
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
                    AddComment(start, BlockCommentText(text.Substring(start + 2, (close < 0 ? text.Length : close) - start - 2)), block: true);
                    continue;
                }
                return;
            }
        }

        private bool IsBlankLineAhead()
        {
            int p = pos + 1;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t' || text[p] == '\r'))
            {
                p++;
            }
            return p < text.Length && text[p] == '\n';
        }

        private void AddComment(int start, string body, bool block = false)
        {
            commentTarget?.Children.Add(new MarkupComment { Text = body, Plain = true, IsBlock = block, Span = TextSpan.FromBounds(start, pos), Parent = commentTarget });
        }

        /// <summary>A /* */ comment's lines, trimmed at the ends and without the indentation (or " * ") they share.</summary>
        private static string BlockCommentText(string body)
        {
            if (body.StartsWith("*", StringComparison.Ordinal))
            {
                body = body.Substring(1); // /** ... */
            }
            List<string> lines = body.Replace("\r", string.Empty).Split('\n').Select(l => l.TrimEnd()).ToList();
            // Text on the opening line has lost its indentation; lines below keep theirs.
            string first = lines[0].Trim();
            bool sameLine = first.Length > 0;
            List<string> rest = lines.Skip(1).ToList();
            while (rest.Count > 0 && rest[^1].Trim().Length == 0) rest.RemoveAt(rest.Count - 1);
            if (!sameLine)
            {
                while (rest.Count > 0 && rest[0].Trim().Length == 0) rest.RemoveAt(0);
            }
            // A leading "*" on every line below (javadoc style) is decoration, not text.
            if (rest.Count > 0 && rest.All(l => l.TrimStart().StartsWith("*", StringComparison.Ordinal)))
            {
                rest = rest.Select(l => l.TrimStart().Substring(1)).ToList();
            }
            int indent = rest.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            rest = rest.Select(l => l.Trim().Length == 0 ? string.Empty : l.Substring(indent)).ToList();
            return string.Join("\n", sameLine ? rest.Prepend(first) : rest);
        }

        /// <summary>Skips trivia to look at the next word (else, catch, ...) without keeping comments or doc lines.</summary>
        private void SkipTriviaLookingAhead()
        {
            MarkupElement savedTarget = commentTarget;
            StringBuilder savedDoc = pendingDoc == null ? null : new StringBuilder(pendingDoc.ToString());
            int savedDocStart = pendingDocStart;
            commentTarget = null;
            SkipTrivia(newlines: true);
            commentTarget = savedTarget;
            pendingDoc = savedDoc;
            pendingDocStart = savedDocStart;
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
