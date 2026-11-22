using KScript.Language.Analysis;
using KScript.Syntax;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class ExpressionParserTests
    {
        [Theory]
        // Literals and references
        [InlineData("42", "42")]
        [InlineData("2.5", "2.5")]
        [InlineData("'a b'", "'a b'")]
        [InlineData("\"double\"", "'double'")]
        [InlineData("true", "true")]
        [InlineData("null", "null")]
        [InlineData("$name", "$name")]
        [InlineData("@now()", "@now()")]
        [InlineData("@greet", "@greet")]
        // Arithmetic precedence
        [InlineData("1 + 2 * 3", "(+ 1 (* 2 3))")]
        [InlineData("(1 + 2) * 3", "(* (+ 1 2) 3)")]
        [InlineData("1 - 2 - 3", "(- (- 1 2) 3)")]
        [InlineData("2 ** 3 ** 2", "(** 2 (** 3 2))")]
        [InlineData("-$x * 2", "(* (- $x) 2)")]
        [InlineData("$a % 2", "(% $a 2)")]
        // Comparison and logic (word operators are normalised)
        [InlineData("$n lt 10 and $name ne ''", "(and (< $n 10) (!= $name ''))")]
        [InlineData("$a eq 1 or $b gt 2", "(or (== $a 1) (> $b 2))")]
        [InlineData("$a le 1", "(<= $a 1)")]
        [InlineData("$a ge 1", "(>= $a 1)")]
        [InlineData("$a > 1", "(> $a 1)")]
        [InlineData("$a >= 1", "(>= $a 1)")]
        [InlineData("not $a and $b", "(and (not $a) $b)")]
        [InlineData("not $a == $b", "(not (== $a $b))")]
        [InlineData("$a === 3", "(=== $a 3)")]
        [InlineData("$a !== '3'", "(!== $a '3')")]
        [InlineData("$a or $b and $c", "(or $a (and $b $c))")]
        [InlineData("$ext in ['.cs', '.xml']", "(in $ext ['.cs' '.xml'])")]
        // Null handling and conditionals
        [InlineData("$user?.name ?? 'guest'", "(?? (?. $user name) 'guest')")]
        [InlineData("$a ?? $b ?? 'c'", "(?? (?? $a $b) 'c')")]
        [InlineData("$n eq 1 ? 'item' : 'items'", "(? (== $n 1) 'item' 'items')")]
        [InlineData("$a ? $b ? 1 : 2 : 3", "(? $a (? $b 1 2) 3)")]
        // Members, indexes, methods
        [InlineData("$p.age", "(. $p age)")]
        [InlineData("$people[0].name", "(. ([] $people 0) name)")]
        [InlineData("$person['age']", "([] $person 'age')")]
        [InlineData("$s->trim()->upper()", "(-> (-> $s trim) upper)")]
        [InlineData("($price * $qty)->round(2)", "(-> (* $price $qty) round 2)")]
        [InlineData("'abc'->count()", "(-> 'abc' count)")]
        [InlineData("str.slugify($x)", "(call (. str slugify) $x)")]
        // Ranges
        [InlineData("0..10", "(.. 0 10)")]
        [InlineData("0..100 step 5", "(.. 0 100 5)")]
        [InlineData("1..$n + 1", "(.. 1 (+ $n 1))")]
        // Lists and maps
        [InlineData("[]", "[]")]
        [InlineData("[1, 2, 3,]", "[1 2 3]")]
        [InlineData("{ name: 'Joan', age: 82 }", "{name:'Joan' age:82}")]
        [InlineData("{ 'quoted key': 1 }", "{quoted key:1}")]
        [InlineData("[{ a: [1] }]", "[{a:[1]}]")]
        // Lambdas and pipes
        [InlineData("fn(x) => $x * 2", "(fn [x] (* $x 2))")]
        [InlineData("fn(a, b) => $a.age - $b.age", "(fn [a b] (- (. $a age) (. $b age)))")]
        [InlineData("fn() => 1", "(fn [] 1)")]
        [InlineData("$people->filter(fn(p) => $p.age ge 18)", "(-> $people filter (fn [p] (>= (. $p age) 18)))")]
        [InlineData("$x |> upper()", "(|> $x (call upper))")]
        [InlineData("$title |> lower() |> slugify()", "(|> (|> $title (call lower)) (call slugify))")]
        [InlineData("$a + 1 |> round()", "(|> (+ $a 1) (call round))")]
        [InlineData("$x |> f() == 3", "(== (|> $x (call f)) 3)")]
        [InlineData("$x |> fn(v) => $v + 1", "(|> $x (fn [v] (+ $v 1)))")]
        [InlineData("@read_text($path) |> json()", "(|> @read_text($path) (call json))")]
        // Named arguments
        [InlineData("@chat(system: 'You are a tutor.')", "@chat(system:'You are a tutor.')")]
        [InlineData("@range(1, step: 2)", "@range(1 step:2)")]
        // Match
        [InlineData("match $choice { 1 => 'settings', 2 => 'exit', _ => '?' }", "(match $choice 1=>'settings' 2=>'exit' _=>'?')")]
        public void ParsesWithExpectedShape(string source, string expected)
        {
            ParseResult result = ExpressionParser.ParseExpression(source);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(expected, Print(result.Root));
        }

        [Theory]
        [InlineData("$a +", "Expected an expression after '+'.")]
        [InlineData("($a + 1", "Expected ')'")]
        [InlineData("@f($a", "never closed")]
        [InlineData("[1, 2", "Expected ']'")]
        [InlineData("{ a 1 }", "Expected ':'")]
        [InlineData("$a $b", "Unexpected '$b'")]
        [InlineData("$a && $b", "Use 'and'")]
        [InlineData("$a || $b", "Use 'or'")]
        [InlineData("!$a", "Use 'not'")]
        [InlineData("'open", "missing its closing '")]
        [InlineData("$x->", "Expected a method name")]
        [InlineData("$x->upper", "Methods are always called with parentheses")]
        [InlineData("fn($x) $x", "Expected '=>'")]
        [InlineData("$a ? 1", "Expected ':'")]
        [InlineData("~people", "no longer used")]
        [InlineData("$a lt", "Expected an expression")]
        [InlineData("and", "found the operator 'and'")]
        public void ReportsSyntaxErrors(string source, string message)
        {
            ParseResult result = ExpressionParser.ParseExpression(source);
            Assert.False(result.Success);
            Assert.Single(result.Errors);
            Assert.Contains(message, result.Errors[0].Message);
        }

        [Theory]
        [InlineData("$x = 3", "=")]
        [InlineData("$x += 1", "+=")]
        [InlineData("$x ??= 1", "??=")]
        public void AssignmentIsRejectedInExpressions(string source, string op)
        {
            ParseResult result = ExpressionParser.ParseExpression(source);
            SyntaxError error = Assert.Single(result.Errors);
            Assert.Equal(DiagnosticCodes.AssignmentNotAllowed, error.Code);
            Assert.Equal(op, source.Substring(error.Span.Start, error.Span.Length));
        }

        [Theory]
        [InlineData("x = 3", "(= x 3)")]
        [InlineData("$x = 3", "(= $x 3)")]
        [InlineData("$total += $price * $qty", "(+= $total (* $price $qty))")]
        [InlineData("$p.age = 3", "(= (. $p age) 3)")]
        [InlineData("$list[0] = 'a'", "(= ([] $list 0) 'a')")]
        [InlineData("x ??= 'd'", "(??= x 'd')")]
        public void ParsesAssignmentStatements(string source, string expected)
        {
            ParseResult result = ExpressionParser.ParseStatements(source);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(expected, Print(Assert.Single(result.Nodes)));
            AssignmentNode assign = (AssignmentNode)result.Root;
            Assert.Equal(source.TrimStart('$').Split(new[] { ' ', '.', '[' })[0], assign.Name);
        }

        [Fact]
        public void ParsesSeveralStatements()
        {
            ParseResult result = ExpressionParser.ParseStatements("$x = 3; $total += $x; @log($x);");
            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(new[] { "(= $x 3)", "(+= $total $x)", "@log($x)" }, result.Nodes.Select(Print));
        }

        [Fact]
        public void StatementsNeedSeparators()
        {
            ParseResult result = ExpressionParser.ParseStatements("x = 1 y = 2");
            Assert.Contains("Expected ';'", Assert.Single(result.Errors).Message);
        }

        [Fact]
        public void ParsesExpressionLists()
        {
            ParseResult result = ExpressionParser.ParseExpressionList("'World', 1 + 2, [3]");
            Assert.True(result.Success);
            Assert.Equal(new[] { "'World'", "(+ 1 2)", "[3]" }, result.Nodes.Select(Print));
        }

        [Fact]
        public void SpansPointAtTheSource()
        {
            const string source = "$people->filter(fn(p) => $p.age ge 18)";
            ParseResult result = ExpressionParser.ParseExpression(source);
            MethodCallNode call = Assert.IsType<MethodCallNode>(result.Root);
            Assert.Equal("filter", source.Substring(call.NameSpan.Start, call.NameSpan.Length));
            Assert.Equal(source.Length, call.Span.End);
            Assert.Equal("(fn(p) => $p.age ge 18)", source.Substring(call.ArgumentList.Span.Start, call.ArgumentList.Span.Length));

            MemberNode member = result.Root.DescendantsAndSelf().OfType<MemberNode>().Single();
            Assert.Equal("age", source.Substring(member.NameSpan.Start, member.NameSpan.Length));
            BinaryNode ge = result.Root.DescendantsAndSelf().OfType<BinaryNode>().Single();
            Assert.Equal("ge", source.Substring(ge.OperatorSpan.Start, ge.OperatorSpan.Length));
            Assert.Equal(">=", ge.Operator);
        }

        [Fact]
        public void EmbeddedExpressionsStopAtTheClosingBrace()
        {
            const string text = "Total: { $a + { b: 1 }.b } and more";
            ParseResult result = ExpressionParser.ParseEmbedded(text, text.IndexOf('{') + 1, out int end);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal(text.LastIndexOf('}'), end);
            Assert.Equal("(+ $a (. {b:1} b))", Print(result.Root));
        }

        [Fact]
        public void EmbeddedExpressionReportsMissingBrace()
        {
            ParseResult result = ExpressionParser.ParseEmbedded("x { $a + 1", 3, out int end);
            Assert.Equal(-1, end);
            Assert.Contains("never closed", Assert.Single(result.Errors).Message);
        }

        [Theory]
        [InlineData("Hello $name, it's late", 6, "$name", 11)]
        [InlineData("$p.name is here.", 0, "(. $p name)", 7)]
        [InlineData("$max years old.", 0, "$max", 4)]
        [InlineData("$name->upper()!", 0, "(-> $name upper)", 14)]
        [InlineData("$a -> b", 0, "$a", 2)]
        [InlineData("$list[1] ok", 0, "([] $list 1)", 8)]
        [InlineData("@join_path($a, $b->name()) done", 0, "@join_path($a (-> $b name))", 26)]
        [InlineData("$file.", 0, "$file", 5)]
        public void BareReferencesTakeOnlyAdjacentPostfixes(string text, int start, string expected, int expectedEnd)
        {
            ParseResult result = ExpressionParser.ParseBareReference(text, start, out int end);
            Assert.Equal(expected, Print(result.Root));
            Assert.Equal(expectedEnd, end);
        }

        [Fact]
        public void NeverThrowsOnArbitraryInput()
        {
            Random random = new Random(1234);
            const string alphabet = "$@abc_ 0123.,;:'\"()[]{}<>=!?+-*/%|&~fnmatchlteqandor\n\t";
            for (int i = 0; i < 3000; i++)
            {
                char[] chars = new char[random.Next(0, 40)];
                for (int j = 0; j < chars.Length; j++)
                {
                    chars[j] = alphabet[random.Next(alphabet.Length)];
                }
                string s = new string(chars);
                ExpressionParser.ParseExpression(s);
                ExpressionParser.ParseStatements(s);
                ExpressionParser.ParseExpressionList(s);
                TemplateParser.Parse(s, allowBareReferences: true);
                foreach (ExprNode node in ExpressionParser.ParseExpression(s).Nodes.SelectMany(n => n.DescendantsAndSelf()))
                {
                    Assert.InRange(node.Span.Start, 0, s.Length);
                    Assert.InRange(node.Span.End, 0, s.Length);
                }
            }
        }
    }
}
