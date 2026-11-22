using KScript.Syntax.Text;

namespace KScript.Syntax
{
    /// <summary>
    /// Parser for KScript 2 expressions and statements. It never throws: it returns the best tree it can, with
    /// <see cref="ErrorNode"/> placeholders, and the first syntax error it found (later errors are usually knock-on).
    ///
    /// <code>
    ///   statements := statement ( ';' statement )* ';'?
    ///   statement  := target assign-op expression | expression        assign-op: = += -= *= /= ??=
    ///   expression := nullish ( '?' expression ':' expression )?
    ///   nullish    := or ( '??' or )*
    ///   or         := and ( 'or' and )*
    ///   and        := not ( 'and' not )*
    ///   not        := 'not' not | equality
    ///   equality   := relational ( ( '==' | '!=' | '===' | '!==' | 'eq' | 'ne' ) relational )*
    ///   relational := pipe ( ( 'lt' | 'le' | 'gt' | 'ge' | '&gt;' | '&gt;=' | '&lt;' | '&lt;=' | 'in' ) pipe )*
    ///                 (the words eq ne lt le gt ge are the XML spellings; a .kscript file writes the symbols)
    ///   pipe       := range ( '|&gt;' postfix )*
    ///   range      := additive ( '..' additive ( 'step' additive )? )?
    ///   additive   := term ( ( '+' | '-' ) term )*
    ///   term       := unary ( ( '*' | '/' | '%' ) unary )*
    ///   unary      := ( '-' | '+' ) unary | power
    ///   power      := postfix ( '**' unary )?
    ///   postfix    := primary ( '-&gt;' name args | '.' name | '?.' name | '[' expression ']' | args )*
    ///   primary    := number | string | true | false | null | $name | @name args? | name | '(' expression ')'
    ///               | '[' list ']' | '{' map '}' | 'fn' '(' params ')' '=&gt;' expression | 'match' nullish '{' arms '}'
    ///   params     := ( name ( ',' name )* )?                           declared bare: fn(a, b) =&gt; $a + $b
    ///   arms       := arm ( ( ',' | new line ) arm )*
    ///   arm        := ( '_' | nullish ( ',' nullish )* ) ( 'when' nullish )? '=&gt;' expression
    ///   args       := '(' ( ( name ':' )? expression ( ',' ... )* )? ')'
    /// </code>
    /// </summary>
    public sealed class ExpressionParser
    {
        private static readonly HashSet<string> AssignmentOperators = new HashSet<string> { "=", "+=", "-=", "*=", "/=", "??=" };

        private readonly Lexer lexer;
        private readonly List<Token> buffer = new List<Token>();
        private readonly List<SyntaxError> errors = new List<SyntaxError>();
        private bool textMode;

        // Inside the braces of a match, where a new line ends an arm.
        private bool inMatchArms;
        private int index;
        private int lastEnd;

        private ExpressionParser(string text, int start, bool textMode)
        {
            lexer = new Lexer(text, start);
            this.textMode = textMode;
            lastEnd = start;
        }

        #region Public API

        /// <summary>Parses text that must be exactly one expression (an attribute such as <c>test</c> or <c>value</c>).</summary>
        public static ParseResult ParseExpression(string text)
        {
            ExpressionParser p = new ExpressionParser(text ?? string.Empty, 0, false);
            ExprNode node = p.ParseRootExpression();
            p.ExpectEnd();
            return new ParseResult(text, new List<ExprNode> { node }, p.errors);
        }

        /// <summary>Parses comma separated expressions (for <c>is="2, 3"</c> and <c>args="'World', 'Hi'"</c>).</summary>
        public static ParseResult ParseExpressionList(string text)
        {
            ExpressionParser p = new ExpressionParser(text ?? string.Empty, 0, false);
            List<ExprNode> nodes = new List<ExprNode>();
            while (p.Peek().Kind != TokenKind.EndOfInput)
            {
                nodes.Add(p.ParseRootExpression());
                if (!p.Peek().Is(","))
                {
                    break;
                }
                p.Advance();
            }
            p.ExpectEnd();
            return new ParseResult(text, nodes, p.errors);
        }

        /// <summary>Parses <c>;</c>-separated statements: assignments and expressions (for <c>&lt;do&gt;</c>, <c>&lt;let&gt;x = 3&lt;/let&gt;</c>).</summary>
        public static ParseResult ParseStatements(string text)
        {
            ExpressionParser p = new ExpressionParser(text ?? string.Empty, 0, false);
            List<ExprNode> nodes = new List<ExprNode>();
            while (p.Peek().Kind != TokenKind.EndOfInput)
            {
                int before = p.index;
                nodes.Add(p.ParseStatement());
                if (p.Peek().Is(";"))
                {
                    p.Advance();
                    continue;
                }
                if (p.Peek().Kind != TokenKind.EndOfInput)
                {
                    p.Error(p.Peek().Span, $"Expected ';' between statements, found '{p.Raw(p.Peek())}'.", DiagnosticCodes.StatementSyntax);
                    if (p.index == before)
                    {
                        p.Advance();
                    }
                    p.SkipTo(";");
                }
            }
            return new ParseResult(text, nodes, p.errors);
        }

        /// <summary>
        /// Parses an expression that starts at <paramref name="start"/> and ends at the first unmatched '}' (for <c>{ expr }</c>).
        /// <paramref name="end"/> receives the offset of that '}', or -1 if it is missing.
        /// </summary>
        public static ParseResult ParseEmbedded(string text, int start, out int end)
        {
            ExpressionParser p = new ExpressionParser(text, start, false);
            ExprNode node = p.ParseRootExpression();
            Token close = p.Peek();
            if (close.Is("}"))
            {
                end = close.Span.Start;
            }
            else
            {
                if (close.Kind == TokenKind.EndOfInput)
                {
                    p.Error(new TextSpan(Math.Max(start - 1, 0), 1), "This '{' is never closed with '}'.");
                }
                else
                {
                    p.Error(close.Span, $"Expected '}}' to end the expression, found '{p.Raw(close)}'.");
                }
                end = -1;
            }
            return new ParseResult(text, new List<ExprNode> { node }, p.errors);
        }

        /// <summary>
        /// Parses a bare <c>$name...</c> or <c>@cmd(...)</c> reference in running text, with only adjacent postfix parts
        /// (<c>.field</c>, <c>-&gt;method()</c>, <c>[i]</c>). <paramref name="end"/> receives where it stops.
        /// </summary>
        public static ParseResult ParseBareReference(string text, int start, out int end)
        {
            ExpressionParser p = new ExpressionParser(text, start, true);
            ExprNode node = p.ParsePostfix(p.ParsePrimary());
            end = p.lastEnd;
            return new ParseResult(text, new List<ExprNode> { node }, p.errors);
        }

        #endregion

        #region Statements and roots

        private ExprNode ParseRootExpression()
        {
            ExprNode node = ParseExpr();
            Token next = Peek();
            if (next.Kind == TokenKind.Punctuation && AssignmentOperators.Contains(next.Text))
            {
                Error(next.Span, next.Text == "="
                    ? new Wording("Assignment isn't allowed here. Use == (or ===) to compare; assign with <let>, <set> or <do>.",
                        "Assignment isn't allowed here. Use == (or ===) to compare; assign on a line of its own: x = 1.")
                    : new Wording($"'{next.Text}' assigns, which isn't allowed here. Assign with <set> or <do>.",
                        $"'{next.Text}' assigns, which isn't allowed here. Assign on a line of its own: x += 1."), DiagnosticCodes.AssignmentNotAllowed);
                Advance();
                ParseExpr();
            }
            return node;
        }

        private void ExpectEnd()
        {
            Token next = Peek();
            if (next.Kind != TokenKind.EndOfInput)
            {
                Error(next.Span, next.Kind == TokenKind.Error && next.Error != null ? next.Error : $"Unexpected '{Raw(next)}' after the end of the expression.");
            }
        }

        private ExprNode ParseStatement()
        {
            ExprNode target = ParseExpr();
            Token op = Peek();
            if (op.Kind != TokenKind.Punctuation || !AssignmentOperators.Contains(op.Text))
            {
                return target;
            }

            Advance();
            ExprNode root = target;
            while (root is MemberNode || root is IndexNode)
            {
                root = root is MemberNode m ? m.Target : ((IndexNode)root).Target;
            }

            string name = root is VariableNode v ? v.Name : root is IdentifierNode id ? id.Name : null;
            if (name == null)
            {
                Error(target.Span, "Only a variable can be assigned to, as in x = 3 or x.total += 1.", DiagnosticCodes.StatementSyntax);
            }

            ExprNode value = ParseExpr();
            return new AssignmentNode
            {
                Name = name,
                NameSpan = root.Span,
                Target = target,
                Operator = op.Text,
                Value = value,
                Span = TextSpan.FromBounds(target.Span.Start, value.Span.End),
            };
        }

        #endregion

        #region Operators

        private ExprNode ParseExpr()
        {
            ExprNode condition = ParseNullish();
            if (!Peek().Is("?"))
            {
                return condition;
            }
            Advance();
            ExprNode whenTrue = ParseExpr();
            Expect(":", "in a ? b : c conditional");
            ExprNode whenFalse = ParseExpr();
            return new ConditionalNode
            {
                Condition = condition,
                WhenTrue = whenTrue,
                WhenFalse = whenFalse,
                Span = TextSpan.FromBounds(condition.Span.Start, whenFalse.Span.End),
            };
        }

        private ExprNode ParseNullish() => ParseLeftAssociative(ParseOr, t => t.Is("??") ? "??" : null);

        private ExprNode ParseOr() => ParseLeftAssociative(ParseAnd, t =>
        {
            if (t.IsWord("or")) return "or";
            if (t.Is("||")) { Error(t.Span, "Use 'or' instead of '||'."); return "or"; }
            return null;
        });

        private ExprNode ParseAnd() => ParseLeftAssociative(ParseNot, t =>
        {
            if (t.IsWord("and")) return "and";
            if (t.Is("&&")) { Error(t.Span, "Use 'and' instead of '&&'."); return "and"; }
            return null;
        });

        private ExprNode ParseNot()
        {
            Token t = Peek();
            if (t.IsWord("not") || t.Is("!"))
            {
                if (t.Is("!"))
                {
                    Error(t.Span, "Use 'not' instead of '!'.");
                }
                Advance();
                ExprNode operand = ParseNot();
                return new UnaryNode { Operator = "not", Operand = operand, Span = TextSpan.FromBounds(t.Span.Start, operand.Span.End) };
            }
            return ParseEquality();
        }

        private ExprNode ParseEquality() => ParseLeftAssociative(ParseRelational, t =>
        {
            if (t.Kind == TokenKind.Punctuation && (t.Text == "==" || t.Text == "!=" || t.Text == "===" || t.Text == "!==")) return t.Text;
            if (t.IsWord("eq")) return "==";
            if (t.IsWord("ne")) return "!=";
            return null;
        });

        private ExprNode ParseRelational() => ParseLeftAssociative(ParsePipe, t =>
        {
            if (t.Kind == TokenKind.Punctuation && (t.Text == ">" || t.Text == ">=" || t.Text == "<" || t.Text == "<=")) return t.Text;
            switch (t.Kind == TokenKind.Identifier ? t.Text : null)
            {
                case "lt": return "<";
                case "le": return "<=";
                case "gt": return ">";
                case "ge": return ">=";
                case "in": return "in";
                default: return null;
            }
        });

        private ExprNode ParsePipe()
        {
            ExprNode left = ParseRange();
            while (Peek().Is("|>"))
            {
                Advance();
                ExprNode right = ParsePostfix(ParsePrimary());
                left = new PipeNode { Left = left, Right = right, Span = TextSpan.FromBounds(left.Span.Start, right.Span.End) };
            }
            return left;
        }

        private ExprNode ParseRange()
        {
            ExprNode start = ParseAdditive();
            if (!Peek().Is(".."))
            {
                return start;
            }
            Advance();
            ExprNode end = ParseAdditive();
            ExprNode step = null;
            if (Peek().IsWord("step"))
            {
                Advance();
                step = ParseAdditive();
            }
            return new RangeNode { Start = start, End = end, Step = step, Span = TextSpan.FromBounds(start.Span.Start, (step ?? end).Span.End) };
        }

        private ExprNode ParseAdditive() => ParseLeftAssociative(ParseTerm, t => t.Is("+") || t.Is("-") ? t.Text : null);

        private ExprNode ParseTerm() => ParseLeftAssociative(ParseUnary, t => t.Is("*") || t.Is("/") || t.Is("%") ? t.Text : null);

        private ExprNode ParseUnary()
        {
            Token t = Peek();
            if (t.Is("-") || t.Is("+"))
            {
                Advance();
                ExprNode operand = ParseUnary();
                return new UnaryNode { Operator = t.Text, Operand = operand, Span = TextSpan.FromBounds(t.Span.Start, operand.Span.End) };
            }
            return ParsePower();
        }

        private ExprNode ParsePower()
        {
            ExprNode b = ParsePostfix(ParsePrimary());
            if (!Peek().Is("**"))
            {
                return b;
            }
            Token op = Advance();
            ExprNode e = ParseUnary();
            return new BinaryNode { Operator = "**", OperatorText = "**", OperatorSpan = op.Span, Left = b, Right = e, Span = TextSpan.FromBounds(b.Span.Start, e.Span.End) };
        }

        private ExprNode ParseLeftAssociative(Func<ExprNode> operand, Func<Token, string> matchOperator)
        {
            ExprNode left = operand();
            while (true)
            {
                Token t = Peek();
                string op = matchOperator(t);
                if (op == null)
                {
                    return left;
                }
                Advance();
                ExprNode right = operand();
                left = new BinaryNode
                {
                    Operator = op,
                    OperatorText = t.Text,
                    OperatorSpan = t.Span,
                    Left = left,
                    Right = right,
                    Span = TextSpan.FromBounds(left.Span.Start, right.Span.End),
                };
            }
        }

        #endregion

        #region Postfix and primary

        private ExprNode ParsePostfix(ExprNode node)
        {
            while (true)
            {
                Token t = Peek();
                bool adjacent = t.Span.Start == lastEnd;
                if (textMode && !adjacent)
                {
                    return node;
                }

                if (t.Is("->"))
                {
                    if (textMode && !(Peek(1).Kind == TokenKind.Identifier && Peek(1).Span.Start == t.Span.End))
                    {
                        return node;
                    }
                    Advance();
                    Token name = Peek();
                    if (name.Kind != TokenKind.Identifier)
                    {
                        Error(name.Span.Length > 0 ? name.Span : t.Span, "Expected a method name after '->'.");
                        return new MethodCallNode { Target = node, Name = string.Empty, NameSpan = new TextSpan(t.Span.End, 0), Span = TextSpan.FromBounds(node.Span.Start, t.Span.End) };
                    }
                    Advance();
                    ArgumentList args = null;
                    if (Peek().Is("("))
                    {
                        args = ParseArguments();
                    }
                    else
                    {
                        Error(name.Span, $"Expected '(' after ->{name.Text}. Methods are always called with parentheses: ->{name.Text}().");
                    }
                    node = new MethodCallNode
                    {
                        Target = node,
                        Name = name.Text,
                        NameSpan = name.Span,
                        ArgumentList = args,
                        Span = TextSpan.FromBounds(node.Span.Start, args?.Span.End ?? name.Span.End),
                    };
                    continue;
                }

                if (t.Is(".") || t.Is("?."))
                {
                    Token name = Peek(1);
                    if (name.Kind != TokenKind.Identifier || (textMode && name.Span.Start != t.Span.End))
                    {
                        if (textMode)
                        {
                            return node;
                        }
                        Advance();
                        Error(name.Span.Length > 0 ? name.Span : t.Span, $"Expected a field name after '{t.Text}'.");
                        return new MemberNode { Target = node, Name = string.Empty, NameSpan = new TextSpan(t.Span.End, 0), NullSafe = t.Text == "?.", Span = TextSpan.FromBounds(node.Span.Start, t.Span.End) };
                    }
                    Advance();
                    Advance();
                    node = new MemberNode { Target = node, Name = name.Text, NameSpan = name.Span, NullSafe = t.Text == "?.", Span = TextSpan.FromBounds(node.Span.Start, name.Span.End) };
                    continue;
                }

                if ((t.Is("[") || t.Is("(")) && inMatchArms && StartsLine(t))
                {
                    // In a match, an arm on a new line starts a new arm: [1, 2] => ... doesn't index the result above.
                    return node;
                }

                if (t.Is("["))
                {
                    Advance();
                    bool outerTextMode = textMode;
                    textMode = false;
                    ExprNode idx = ParseExpr();
                    Token close = Expect("]", "to close the index");
                    textMode = outerTextMode;
                    node = new IndexNode { Target = node, Index = idx, Span = TextSpan.FromBounds(node.Span.Start, Math.Max(close.Span.End, idx.Span.End)) };
                    continue;
                }

                if (t.Is("(") && !textMode && (node is IdentifierNode || node is MemberNode || node is VariableNode))
                {
                    ArgumentList args = ParseArguments();
                    node = new CallNode { Callee = node, ArgumentList = args, Span = TextSpan.FromBounds(node.Span.Start, args.Span.End) };
                    continue;
                }

                return node;
            }
        }

        private ExprNode ParsePrimary()
        {
            Token t = Peek();
            switch (t.Kind)
            {
                case TokenKind.Number:
                    Advance();
                    return new LiteralNode { Kind = LiteralKind.Number, Value = t.Text, Span = t.Span };

                case TokenKind.String:
                    Advance();
                    return TypedBlock(t, new LiteralNode { Kind = LiteralKind.String, Value = t.Text, Span = t.Span, Template = QuotedTemplate(t) });

                case TokenKind.Regex:
                    Advance();
                    return new LiteralNode { Kind = LiteralKind.Regex, Value = t.Text, Span = t.Span };

                case TokenKind.Email:
                    Advance();
                    return new LiteralNode { Kind = LiteralKind.Email, Value = t.Text, Span = t.Span };

                case TokenKind.Uuid:
                    Advance();
                    return new LiteralNode { Kind = LiteralKind.Uuid, Value = t.Text, Span = t.Span };

                case TokenKind.Url:
                    Advance();
                    return new LiteralNode { Kind = LiteralKind.Url, Value = t.Text, Span = t.Span };

                case TokenKind.Duration:
                    Advance();
                    return new LiteralNode { Kind = LiteralKind.Duration, Value = t.Text, Span = t.Span };

                case TokenKind.Variable:
                    Advance();
                    return new VariableNode { Name = t.Text, Span = t.Span };

                case TokenKind.Command:
                {
                    Advance();
                    ArgumentList args = Peek().Is("(") && (!textMode || Peek().Span.Start == t.Span.End) ? ParseArguments() : null;
                    return new CommandCallNode { Name = t.Text, NameSpan = t.Span, ArgumentList = args, Span = TextSpan.FromBounds(t.Span.Start, args?.Span.End ?? t.Span.End) };
                }

                case TokenKind.Identifier:
                    switch (t.Text)
                    {
                        case "true":
                        case "false":
                            Advance();
                            return new LiteralNode { Kind = LiteralKind.Bool, Value = t.Text, Span = t.Span };
                        case "null":
                            Advance();
                            return new LiteralNode { Kind = LiteralKind.Null, Value = "null", Span = t.Span };
                        case "fn" when Peek(1).Is("("):
                            return ParseLambda();
                        case "match" when Peek(1).Kind != TokenKind.Punctuation || Peek(1).Is("(") || Peek(1).Is("["):
                            return ParseMatch();
                    }
                    if (Lexer.Keywords.Contains(t.Text) && t.Text != "fn" && t.Text != "match")
                    {
                        Error(t.Span, $"Expected a value, found the operator '{t.Text}'.");
                        Advance();
                        return new ErrorNode { Span = t.Span };
                    }
                    Advance();
                    return new IdentifierNode { Name = t.Text, Span = t.Span };

                case TokenKind.Punctuation:
                    if (t.Is("("))
                    {
                        Advance();
                        ExprNode inner = ParseExpr();
                        Token close = Expect(")", "to close the bracket");
                        // Keep the bracket in the span so ($a + $b)->round() reports sensibly.
                        inner.Span = inner.Span;
                        return WithSpan(inner, t.Span.Start, Math.Max(close.Span.End, inner.Span.End));
                    }
                    if (t.Is("["))
                    {
                        return ParseList();
                    }
                    if (t.Is("{"))
                    {
                        return ParseMap();
                    }
                    break;

                case TokenKind.Error:
                    Advance();
                    return new ErrorNode { Span = t.Span };

                case TokenKind.EndOfInput:
                    // $a + and nothing more: name what was left unfinished.
                    Error(new TextSpan(t.Span.Start, 0), index > 0 && Raw(buffer[index - 1]) is { Length: > 0 } before
                        ? $"Expected an expression after '{before}'."
                        : "Expected an expression.");
                    return new ErrorNode { Span = new TextSpan(t.Span.Start, 0) };
            }

            Error(t.Span, $"Expected a value, found '{Raw(t)}'.");
            return new ErrorNode { Span = new TextSpan(t.Span.Start, 0) };
        }

        /// <summary>"Double quotes" fill in <c>$name</c> and <c>{ expr }</c>; 'single quotes' and ``` blocks are kept as written.</summary>
        private Template QuotedTemplate(Token t)
        {
            string text = lexer.Text;
            if (t.Span.Length < 2 || t.Span.End > text.Length || text[t.Span.Start] != '"' || text[t.Span.End - 1] != '"'
                || (t.Text.IndexOf('$') < 0 && t.Text.IndexOf('{') < 0 && t.Text.IndexOf('@') < 0 && t.Text.IndexOf('}') < 0))
            {
                return null;
            }
            Template template = TemplateParser.ParseQuoted(text, t.Span.Start + 1, t.Span.End - 1);
            if (template != null)
            {
                errors.AddRange(template.Errors);
            }
            return template;
        }

        private static ExprNode WithSpan(ExprNode node, int start, int end)
        {
            // Brackets don't get their own node; widening the span keeps hover and errors pointing at the whole group.
            node.Span = TextSpan.FromBounds(Math.Min(start, node.Span.Start), Math.Max(end, node.Span.End));
            return node;
        }

        private ExprNode ParseLambda()
        {
            Token fn = Advance();
            LambdaNode lambda = new LambdaNode();
            Advance(); // (
            while (!Peek().Is(")") && Peek().Kind != TokenKind.EndOfInput)
            {
                Token p = Peek();
                if (p.Kind == TokenKind.Variable || (p.Kind == TokenKind.Identifier && !Lexer.Keywords.Contains(p.Text)))
                {
                    Advance();
                    lambda.Parameters.Add(new ParameterNode { Name = p.Text, Span = p.Span });
                }
                else
                {
                    Error(p.Span, $"Expected a parameter name such as x, found '{Raw(p)}'.");
                    break;
                }
                if (!Peek().Is(","))
                {
                    break;
                }
                Advance();
            }
            Expect(")", "to close the lambda's parameters");
            Expect("=>", "after fn(...)");
            lambda.Body = ParseExpr();
            lambda.Span = TextSpan.FromBounds(fn.Span.Start, lambda.Body.Span.End);
            return lambda;
        }

        /// <summary>
        /// <c>match subject { arms }</c>. An arm is <c>pattern =&gt; result</c>: one value or several
        /// (<c>1, 2 =&gt;</c>), <c>_</c> for anything, and an optional <c>when test</c> after the pattern
        /// (<c>_ when $n &gt; 9 =&gt;</c>). Arms are separated by a comma or a new line. The statement form in a .kscript
        /// file (<see cref="Markup.CodeParser"/>) writes its arms the same way.
        /// </summary>
        private ExprNode ParseMatch()
        {
            Token keyword = Advance();
            MatchNode match = new MatchNode { Subject = ParseNullish() };
            Expect("{", "after the match value");
            bool outer = inMatchArms;
            inMatchArms = true;
            while (!Peek().Is("}") && Peek().Kind != TokenKind.EndOfInput)
            {
                MatchArmNode arm = new MatchArmNode();
                Token first = Peek();
                if (first.IsWord("default") && Peek(1).Is("=>"))
                {
                    Error(first.Span, "Write _ for the arm that matches anything: _ => ...");
                }
                if (first.IsWord("_") || (first.IsWord("default") && Peek(1).Is("=>")))
                {
                    Advance();
                }
                else if (first.IsWord("when"))
                {
                    Error(first.Span, "Write the test after a pattern: _ when $n > 9 => ...");
                }
                else
                {
                    while (true)
                    {
                        arm.Patterns.Add(ParseNullish());
                        if (!Peek().Is(","))
                        {
                            break;
                        }
                        Advance();
                    }
                }
                if (Peek().IsWord("when"))
                {
                    Advance();
                    arm.Guard = ParseNullish();
                }
                Expect("=>", "in a match arm");
                arm.Result = ParseExpr();
                arm.Span = TextSpan.FromBounds(first.Span.Start, Math.Max(first.Span.End, arm.Result.Span.End));
                match.Arms.Add(arm);
                if (Peek().Is(","))
                {
                    Advance();
                    continue;
                }
                if (Peek().Is("}") || Peek().Kind == TokenKind.EndOfInput || !StartsLine(Peek()))
                {
                    break;
                }
            }
            inMatchArms = outer;
            Token close = Expect("}", "to close the match");
            match.Span = TextSpan.FromBounds(keyword.Span.Start, Math.Max(close.Span.End, lastEnd));
            return match;
        }

        /// <summary>True when a new line comes between the token before and this one.</summary>
        private bool StartsLine(Token t) =>
            t.Span.Start <= lexer.Text.Length && lastEnd <= t.Span.Start && lexer.Text.IndexOf('\n', lastEnd, t.Span.Start - lastEnd) >= 0;

        private ExprNode ParseList()
        {
            Token open = Advance();
            ListNode list = new ListNode();
            while (!Peek().Is("]") && Peek().Kind != TokenKind.EndOfInput)
            {
                list.Items.Add(ParseExpr());
                if (!Peek().Is(","))
                {
                    break;
                }
                Advance();
            }
            Token close = Expect("]", "to close the list");
            list.Span = TextSpan.FromBounds(open.Span.Start, Math.Max(close.Span.End, lastEnd));
            return list;
        }

        private ExprNode ParseMap()
        {
            Token open = Advance();
            MapNode map = new MapNode();
            while (!Peek().Is("}") && Peek().Kind != TokenKind.EndOfInput)
            {
                Token key = Peek();
                if (key.Kind != TokenKind.Identifier && key.Kind != TokenKind.String && key.Kind != TokenKind.Number)
                {
                    Error(key.Span, $"Expected a key such as name: or 'name':, found '{Raw(key)}'.");
                    break;
                }
                Advance();
                Expect(":", $"after the key '{key.Text}'");
                ExprNode value = ParseExpr();
                map.Entries.Add(new MapEntryNode { Key = key.Text, KeySpan = key.Span, Value = value, Span = TextSpan.FromBounds(key.Span.Start, value.Span.End) });
                if (!Peek().Is(","))
                {
                    break;
                }
                Advance();
            }
            Token close = Expect("}", "to close the map");
            map.Span = TextSpan.FromBounds(open.Span.Start, Math.Max(close.Span.End, lastEnd));
            return map;
        }

        private ArgumentList ParseArguments()
        {
            Token open = Advance(); // (
            ArgumentList list = new ArgumentList();
            bool outerTextMode = textMode;
            textMode = false;
            try
            {
                ParseArgumentsBody(open, list);
            }
            finally
            {
                textMode = outerTextMode;
            }
            return list;
        }

        private void ParseArgumentsBody(Token open, ArgumentList list)
        {
            while (!Peek().Is(")") && Peek().Kind != TokenKind.EndOfInput)
            {
                ArgumentNode arg = new ArgumentNode();
                Token first = Peek();
                // A word followed by ':' at the start of an argument names it. Keywords count too (@range(step: 2)):
                // no expression starts with a keyword and a colon.
                if (first.Kind == TokenKind.Identifier && Peek(1).Is(":"))
                {
                    Advance();
                    Advance();
                    arg.Name = first.Text;
                    arg.NameSpan = first.Span;
                }
                arg.Value = ParseExpr();
                arg.Span = TextSpan.FromBounds(arg.Name != null ? first.Span.Start : arg.Value.Span.Start, arg.Value.Span.End);
                list.Arguments.Add(arg);
                if (!Peek().Is(","))
                {
                    break;
                }
                Advance();
            }

            Token close = Peek();
            if (close.Is(")"))
            {
                Advance();
                list.Closed = true;
                list.Span = TextSpan.FromBounds(open.Span.Start, close.Span.End);
            }
            else
            {
                Error(close.Kind == TokenKind.EndOfInput ? open.Span : close.Span,
                    close.Kind == TokenKind.EndOfInput ? "This '(' is never closed." : $"Expected ',' or ')' in the arguments, found '{Raw(close)}'.");
                list.Span = TextSpan.FromBounds(open.Span.Start, Math.Max(lastEnd, open.Span.End));
            }
        }

        #endregion

        #region Tokens

        private string Raw(Token t) => t.Span.End <= lexer.Text.Length ? lexer.Text.Substring(t.Span.Start, t.Span.Length) : t.Text;

        private Token Peek(int k = 0)
        {
            while (buffer.Count <= index + k)
            {
                buffer.Add(lexer.Next());
            }
            return buffer[index + k];
        }

        /// <summary>
        /// A ``` block tagged with a type parses its text: <c>```&lt;json&gt;</c> is <c>'...'-&gt;json()</c> and
        /// <c>```&lt;Repo&gt;</c> is <c>'...'-&gt;json(as: Repo)</c>. Any other block is the string itself.
        /// </summary>
        private static ExprNode TypedBlock(Token t, LiteralNode literal)
        {
            string tag = t.Tag;
            if (tag == null || tag.Length < 3 || tag[0] != '<' || tag[tag.Length - 1] != '>')
            {
                return literal;
            }
            string type = tag.Substring(1, tag.Length - 2).Trim();
            if (type is "text" or "string")
            {
                return literal;
            }
            TextSpan at = new TextSpan(t.Span.Start, 0);
            ArgumentList args = new ArgumentList { Span = at, Closed = true };
            if (type != "json")
            {
                args.Arguments.Add(new ArgumentNode { Name = "as", NameSpan = at, Value = new VariableNode { Name = type, Span = at }, Span = at });
            }
            return new MethodCallNode { Target = literal, Name = "json", NameSpan = at, ArgumentList = args, Span = t.Span };
        }

        private Token Advance()
        {
            Token t = Peek();
            if (t.Kind != TokenKind.EndOfInput)
            {
                index++;
                lastEnd = t.Span.End;
            }
            if (t.Error != null)
            {
                Error(t.ErrorSpan, t.Error, t.Kind == TokenKind.Error && t.Text.StartsWith("~") ? DiagnosticCodes.RetiredSigil : DiagnosticCodes.ExpressionSyntax);
            }
            return t;
        }

        private Token Expect(string punctuation, string context)
        {
            Token t = Peek();
            if (t.Is(punctuation))
            {
                return Advance();
            }
            Error(t.Kind == TokenKind.EndOfInput ? new TextSpan(lastEnd, 0) : t.Span,
                t.Kind == TokenKind.EndOfInput ? $"Expected '{punctuation}' {context}." : $"Expected '{punctuation}' {context}, found '{Raw(t)}'.");
            return new Token(TokenKind.Punctuation, punctuation, new TextSpan(lastEnd, 0));
        }

        private void SkipTo(string punctuation)
        {
            while (Peek().Kind != TokenKind.EndOfInput && !Peek().Is(punctuation))
            {
                index++;
            }
        }

        private void Error(TextSpan span, Wording message, string code = DiagnosticCodes.ExpressionSyntax)
        {
            if (errors.Count == 0)
            {
                errors.Add(new SyntaxError(span, message, code));
            }
        }

        #endregion
    }
}
