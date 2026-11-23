using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of email, password, handle, uuid and regex, refinement and joined types, and raw values.</summary>
    public class SemanticTypesAnalysisTests
    {
        private static readonly LanguageService Service = new LanguageService();

        private static DocumentAnalysis Code(string code) => Analyze(code, "types.kscript");

        [Theory]
        [InlineData("e: email = 'joan@'", "'joan@' isn't an email: there's nothing after the @.")]
        [InlineData("e: email = joan@example", "'joan@example' isn't an email: the domain needs a dot")]
        [InlineData("u: uuid(v: 4) = 3f2a8c1e-9b7d-8c2a-8e1f-0a9b8c7d6e5f", "This doesn't fit uuid(v: 4): it must be a version 4 UUID, but it is version 8.")]
        [InlineData("w: email(domains: ['example.com']) = joan@other.com", "it must be at example.com.")]
        [InlineData("p: password = 3", "This can't be a password: a password is text, in quotes.")]
        [InlineData("p: password(min: 12) = 'short'", "This doesn't fit password(min: 12): it must be at least 12 characters.")]
        [InlineData("h: handle = '@'", "'@' isn't a handle: it needs a name after the @.")]
        [InlineData("enum Color { red, green }\nc: Color = blu", "'blu' isn't a Color. Use one of: red, green.")]
        [InlineData("enum Color { red, green }\nc: Color = gren", "Did you mean green?")]
        [InlineData("type Age with [int] { min: 0; max: 150 }\na: Age = 200", "'200' doesn't fit Age: it must be at most 150.")]
        [InlineData("type Mail with [email] { max: 10 }\nm: Mail = 'joan@example.com'", "it must be at most 10 characters.")]
        [InlineData("type Mail with [email] {}\nm: Mail = 'joan@'", "'joan@' doesn't fit Mail: there's nothing after the @.")]
        [InlineData("type Slug with [string] { pattern: /^[a-z]+$/ }\ns: Slug = A1", "'A1' doesn't fit Slug: it must match /^[a-z]+$/.")]
        [InlineData("type Name with [handle] { not: [@admin] }\ntype Staff with [Name] { max: 5 }\nn: Staff = @admin", "'@admin' doesn't fit Staff: it is reserved ('@admin').")]
        [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/\n  w: int\n  h: int\n}\ns: Size = big", "'big' doesn't fit Size: it must match /^(?<w>\\d+)x(?<h>\\d+)$/.")]
        [InlineData("x: string(min: 3) = ab", "This doesn't fit string(min: 3): it must be at least 3 characters.")]
        [InlineData("n: int(max: 3) = 5", "This doesn't fit int(max: 3): it must be at most 3.")]
        [InlineData("h: handle = @a\nok = $h == '@'", "'@' isn't a handle")]
        [InlineData("<kscript><let name=\"h\" type=\"handle\" value=\"'@'\" /></kscript>", "isn't a handle")]
        [InlineData("<kscript><let name=\"e\" type=\"email\">'joan@'</let></kscript>", "isn't an email")]
        public void LiteralsThatDontFitTheirTypeAreReported(string code, string message)
        {
            DocumentAnalysis a = code.StartsWith('<') ? Analyze(code) : Code(code);
            if (message == "'@' isn't a handle")
            {
                // Compared with quoted text, a handle isn't checked: only @name next to it is.
                Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.InvalidTypedValue);
                return;
            }
            Diagnostic d = AssertSingle(a, DiagnosticCodes.InvalidTypedValue);
            Assert.Contains(message, d.Message);
        }

        [Theory]
        [InlineData("enum Color { red, green }\nc: Color = red\necho $c")]
        [InlineData("h: handle = @Joan\necho $h.name")]
        [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/\n  w: int\n  h: int\n}\ns: Size = 3x4\necho $s.w")]
        [InlineData("type Slug with [string] { pattern: /^[a-z]+$/; normalize: $value->lower() }\ns: Slug = A1\necho $s")]
        [InlineData("type Slug with [string] { pattern: /^[a-z]+$/ }\ns: Slug = 3\necho $s")]
        [InlineData("type Name with [handle] { one_of: [$x] }\nn: Name = @admin\necho $n")]
        [InlineData("type Tags with [string[]] { max: 2 }\nt: Tags = ['a']\necho $t")]
        [InlineData("type Mail with [email] {}\nm: Mail = 4\necho $m")]
        [InlineData("type Age with [int] { min: 0 }\na: Age = 'x'\necho $a")]
        [InlineData("type Age with [int] { min: 0 }\nfn f(a: Age = 5) => $a\necho @f()")]
        [InlineData("type Who with [handle] {}\nfn f(h: Who = @me) => $h\necho @f()")]
        [InlineData("fn who(h: handle) => $h.name\necho @who(@Sam)\necho @who(h: @Ann)")]
        [InlineData("h: handle = @a\nok = $h == @a\nno = @b != $h\necho \"$ok $no\"")]
        [InlineData("u: uuid = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho $u.version")]
        [InlineData("e = joan@example.com\necho $e.domain\necho $e->upper()")]
        [InlineData("r = /ab+/i\necho $r->test('abb')")]
        [InlineData("type User { name: string; fn hi(to: string = 'you') => 'hi ' + $to }\nu = User({ name: 'x' })\necho $u->hi()\necho $u->hi('me')\necho $u->hi(to: 'me')")]
        [InlineData("type Slug with [string] { pattern: 'a+' }\ns: Slug = aa\necho $s")]
        [InlineData("type A { a: int }\ntype B with [A] { b: int }\nx = B({ a: 1, b: 2 })\necho $x.a")]
        [InlineData("<kscript><let name=\"h\" type=\"handle\" value=\"@me\" /><echo>$h</echo></kscript>")]
        [InlineData("<kscript><let name=\"h\" type=\"handle\">@me</let><echo>$h</echo></kscript>")]
        [InlineData("<kscript><type name=\"Who\" with=\"[handle]\" max=\"5\" /><let name=\"h\" type=\"Who?\" value=\"@me\" /><echo>$h</echo></kscript>")]
        [InlineData("type Pair {\n  pattern: /^(?<a>\\w+)-(?<b>\\w+)$/\n  parse: { a: $value, b: $match.groups.b }\n  a: string\n  b: string\n}\np: Pair = x-y\necho $p.b")]
        public void ValuesThatFitAreClean(string code) => AssertClean(WithoutUnused(code.StartsWith('<') ? Analyze(code) : Code(code)));

        [Theory]
        [InlineData("x = @nobody", "KS5001")]
        [InlineData("fn who(h: handle, n: int) => $h.name\necho @who(@Sam, @nobody)", "KS5001")]
        [InlineData("type User { name: string; fn hi() => 'hi' }\nu = User({ name: 'x' })\necho $u->nope()", "KS5003")]
        [InlineData("type User { name: string; fn hi() => 'hi' }\nu = User({ name: 'x' })\necho $u->hi(1, 2)", "KS5004")]
        [InlineData("type User { name: string; fn hi(a: int) => 'hi' }\nu = User({ name: 'x' })\necho $u->hi(b: 1)", null)]
        [InlineData("r = /ab+/i\necho $r->nope()", "KS5003")]
        [InlineData("type C with [Nope] {}", "KS3006")]
        public void MistakesAroundTypesAreReported(string code, string expected)
        {
            DocumentAnalysis a = WithoutUnused(Code(code));
            Assert.NotEmpty(a.Diagnostics);
            if (expected != null) Assert.Contains(a.Diagnostics, d => d.Code == expected);
        }

        [Fact]
        public void TypesAreColouredWhereverTheyAreNamed()
        {
            string code = "enum Unit { cm, in }\ntype Age with [int] { min: 0 }\ntype Size with [Age] { max: 9 }\nfn f(a: Age, u: uuid(v: 4)[]?) => $a\nx: Age = 3\nr = /a+/i\ne = joan@example.com";
            DocumentAnalysis a = Service.Analyze(code, "t.kscript");
            List<SemanticToken> tokens = Service.GetSemanticTokens(a);
            Assert.Contains(tokens, t => a.Source[t.Span] == "Age" && t.Type == SemanticTokenType.Type && t.Modifiers.HasFlag(SemanticTokenModifiers.Declaration));
            Assert.Contains(tokens, t => a.Source[t.Span] == "Unit" && t.Modifiers.HasFlag(SemanticTokenModifiers.Declaration));
            Assert.Contains(tokens, t => a.Source[t.Span] == "uuid" && t.Type == SemanticTokenType.Type);
            Assert.Contains(tokens, t => a.Source[t.Span] == "int" && t.Type == SemanticTokenType.Type);
            Assert.DoesNotContain(tokens, t => a.Source[t.Span] == "v" && t.Type == SemanticTokenType.Type);
            Assert.Contains(tokens, t => a.Source[t.Span] == "/a+/i" && t.Type == SemanticTokenType.Regexp);
            Assert.Contains(tokens, t => a.Source[t.Span] == "joan@example.com" && t.Type == SemanticTokenType.String);
        }

        [Fact]
        public void TypeNamesComplete()
        {
            (string text, int offset) = WithCaret("<kscript><type name=\"Age\" with=\"int\" /><let name=\"x\" type=\"‸\" /></kscript>");
            List<CompletionItem> items = Service.GetCompletions(Service.Analyze(text, "test.ks.xml"), offset);
            Assert.Contains(items, i => i.Label == "email");
            Assert.Contains(items, i => i.Label == "Age");
        }

        [Theory]
        [InlineData("type Username with [handle] {\n  max: 16\n  not: [@admin, @root]\n  validate: not $value.name->starts_with('_'), 'A username cannot start with _'\n}")]
        [InlineData("type Age with [int] { min: 0; max: 150 }")]
        [InlineData("type Admin with [User, Auditable] {\n  level: int = 1\n  fn up(by: int = 1) => $level + $by\n}")]
        [InlineData("type Slug with [string] {\n  pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/\n  normalize: $value->trim()->lower()\n}")]
        [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/i\n  w: int\n  h: int\n}")]
        [InlineData("type Pair {\n  parse: { a: $value, b: $value }\n  a: string\n  b: string\n}")]
        [InlineData("id: uuid(v: 4) = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\nwho: handle = @joan")]
        public void TheNewSyntaxConvertsBothWays(string code)
        {
            ConversionResult xml = ScriptConverter.Convert(code, "a.kscript");
            Assert.Empty(xml.Warnings);
            ConversionResult back = ScriptConverter.Convert(xml.Text, "a.ks.xml");
            Assert.Empty(back.Warnings);
            Assert.Equal(ScriptConverter.Convert(code, "a.kscript").Text, ScriptConverter.Convert(back.Text, "a.kscript").Text);
        }

        [Theory]
        [InlineData("type A with [User\ntype User { a: int }", "This [ is missing its closing ].")]
        [InlineData("type A with [User", "This [ is missing its closing ].")]
        [InlineData("type A with {\n}", "Write the types to build on after 'with'")]
        [InlineData("type A with [int] {\n  min:\n}", "Write the value of min, such as min: 0.")]
        [InlineData("type A with [int] {\n  max:\n}", "such as max: 100.")]
        [InlineData("type A with [string] {\n  pattern:\n}", "such as pattern: /^[a-z]+$/.")]
        [InlineData("type A {\n  parse:\n  a: string\n}", "such as parse: $value->split('x').")]
        [InlineData("type A with [string] {\n  one_of:\n}", "such as one_of: [a, b].")]
        [InlineData("type A with [int] {\n  min: 1\n  min: 2\n}", "This type already has a min rule.")]
        [InlineData("type A with [string] {\n  pattern: /abc\n}", "This regular expression is missing its closing /.")]
        public void TypeBodyMistakesAreReported(string code, string message)
        {
            DocumentAnalysis a = Code(code);
            Assert.Contains(a.Diagnostics, d => d.Message.Contains(message));
        }

        [Theory]
        [InlineData("fn (c)")]
        [InlineData("fn(c)")]
        public void NormalizerParametersAndRefinedMethodsKeepTheirTypes(string callback)
        {
            var (code, at) = WithCaret($$"""
                type PasskeySessionKey with [string] {
                  min: 8
                  normalize: {{callback}} => $c->tr‸im()
                  pattern: /^passkey_[a-zA-Z]+$/

                  fn break() {
                    return $value->count()
                  }
                }
                fn size(key: PasskeySessionKey) => $key->break()
                echo @size(' passkey_Test ')
                """);
            DocumentAnalysis analysis = Service.Analyze(code, "types.kscript");
            AssertClean(analysis);
            var methods = Service.GetCompletions(analysis, at);
            Assert.Contains(methods, i => i.Label == "trim");
            Assert.DoesNotContain(methods, i => i.Label == "break");
            var declaration = Assert.Single(Service.GetDefinition(analysis, code.IndexOf("$c->", StringComparison.Ordinal) + 1));
            Assert.Equal("c", analysis.Source[declaration.Span]);
            int method = code.IndexOf("->break", StringComparison.Ordinal) + 3;
            Assert.Contains(Service.GetCompletions(analysis, method), i => i.Label == "break");
            Assert.Equal("break", analysis.Source[Assert.Single(Service.GetDefinition(analysis, method)).Span]);
        }

        [Fact]
        public void RefinementMemberMistakesKeepTheCallbackAndOfferSeparateFixes()
        {
            string code = """
                type PasskeySessionKey with [string] {
                  min: 8
                  normalize: fn($value) => $value->trim()
                  pattern: ^/passkey_[a-zA-Z]+$/
                  fn break() { return $value->count() }
                }
                """;
            DocumentAnalysis analysis = Code(code);
            Assert.Collection(analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).OrderBy(d => d.Span.Start),
                d => {
                    Assert.Equal(DiagnosticCodes.BareDeclaration, d.Code);
                    Assert.Equal("$value", analysis.Source[d.Span]);
                    var change = Assert.Single(Assert.Single(d.Fixes).Changes);
                    Assert.Equal("$", analysis.Source[change.Span]);
                    Assert.Equal(string.Empty, change.NewText);
                },
                d => { Assert.Equal("^/", analysis.Source[d.Span]); Assert.Equal("/^", Assert.Single(Assert.Single(d.Fixes).Changes).NewText); });
        }

        [Theory]
        [InlineData("fn() => 'ok'", DiagnosticCodes.ArgumentCount)]
        [InlineData("fn(a, b) => $a + $b", DiagnosticCodes.ArgumentCount)]
        [InlineData("fn(c) => $c->push('ok')", DiagnosticCodes.TypeMismatch)]
        public void NormalizerCallbacksCheckTheirArityAndUnderlyingReceiver(string callback, string error)
        {
            AssertSingle(Code($"type Key with [string] {{ normalize: {callback} }}"), error);
        }

        [Fact]
        public void ARefinementCanUseANamedNormalizer()
        {
            AssertClean(Service.Analyze("""
                fn trim_key(c: string) => $c->trim()
                type PasskeySessionKey with [string] { normalize: @trim_key }
                echo PasskeySessionKey(' passkey_Test ')
                """, "types.kscript"));
        }

        [Theory]
        [InlineData("@trim_key")]
        [InlineData("@trim_key($value)")]
        public void NormalizersResolveLaterLocalMethodsBeforeModuleFunctions(string call)
        {
            string code = $$"""
                fn trim_key() => 'global'
                type PasskeySessionKey with [string] {
                  normalize: {{call}}
                  fn trim_key(key: string) => $key->trim()
                }
                echo PasskeySessionKey(' passkey_Test ')
                echo @trim_key()
                """;
            var analysis = Service.Analyze(code, "types.kscript");
            AssertClean(analysis);
            int local = code.IndexOf(call, StringComparison.Ordinal) + 2;
            int global = code.LastIndexOf("@trim_key", StringComparison.Ordinal) + 2;
            Assert.Equal(code.LastIndexOf("fn trim_key", StringComparison.Ordinal) + 3, Assert.Single(Service.GetDefinition(analysis, local)).Span.Start);
            Assert.Equal(code.IndexOf("fn trim_key", StringComparison.Ordinal) + 3, Assert.Single(Service.GetDefinition(analysis, global)).Span.Start);
            Assert.Contains("key: string", Assert.Single(Service.GetCompletions(analysis, local), i => i.Label == "trim_key").Detail);
            Assert.DoesNotContain("key: string", Assert.Single(Service.GetCompletions(analysis, global), i => i.Label == "trim_key").Detail);
        }

        [Fact]
        public void NormalizerCanUseAnInheritedMethodWithAnOptionalArgument()
        {
            AssertClean(Service.Analyze("""
                type BaseKey with [string] {
                  fn trim_key(key: string, suffix: string = '') => $key->trim() + $suffix
                }
                type PasskeySessionKey with [BaseKey] { normalize: @trim_key }
                echo PasskeySessionKey(' passkey_Test ')
                """, "types.kscript"));
        }

        [Fact]
        public void AMethodNamedValueKeepsTheNormalizersInputVariable()
        {
            AssertClean(Service.Analyze("""
                type Key with [string] {
                  normalize: @value($value)
                  fn value(text: string) => $text->trim()
                }
                echo Key(' ok ')
                """, "types.kscript"));
        }

        [Theory]
        [InlineData("fn trim_key() => 'key'")]
        [InlineData("fn trim_key(key: string, suffix: string) => $key + $suffix")]
        public void NamedNormalizerMustAcceptOneArgument(string declaration)
        {
            string code = $"type Key with [string] {{ normalize: @trim_key; {declaration} }}";
            var analysis = Code(code);
            Assert.Equal("@trim_key", analysis.Source[AssertSingle(analysis, DiagnosticCodes.ArgumentCount).Span]);
        }

        [Fact]
        public void LocalNormalizerHelpersStayOutOfModuleCompletions()
        {
            var (code, at) = WithCaret("""
                type Key with [string] {
                  normalize: @trim_key($value)
                  fn trim_key(key: string) => $key->trim()
                }
                echo @tr‸im_key('outside')
                """);
            var analysis = Code(code);
            AssertSingle(analysis, DiagnosticCodes.UnknownCommand);
            Assert.DoesNotContain(Service.GetCompletions(analysis, at), i => i.Label == "trim_key");
        }

        [Fact]
        public void ProjectReferencesDistinguishAnInheritedHelperFromOtherMethodsAndFunctions()
        {
            string folder = Path.GetFullPath("normalizer-project");
            string file = Path.Combine(folder, "keys.kscript");
            string client = Path.Combine(folder, "client.kscript");
            string source = """
                fn clean() => 'module'
                type Key with [string] {
                  normalize: @clean
                  fn clean(text: string) => $text->trim()
                }
                type Other with [string] { fn clean() => $value->upper(); fn secret() => $value }
                echo @clean()
                echo Other('x')->clean()
                """;
            var files = new MemoryFiles()
                .Add(Path.Combine(folder, "kscript.json"), "{}")
                .Add(file, source)
                .Add(Path.Combine(folder, "sessions.kscript"), "import \"keys.kscript\"\ntype Session with [Key] {}")
                .Add(client, "import \"sessions.kscript\"\necho Session('x')->clean(' ok ')");
            var service = new LanguageService(files: files);
            var analysis = service.Analyze(source, file);
            var references = service.GetProjectReferences(analysis, source.IndexOf("normalize: @clean", StringComparison.Ordinal) + 13, true);
            Assert.Equal(3, references.Count);
            Assert.Contains(references, r => r.Path == client && r.Start.Line == 1);
            Assert.All(references.Where(r => r.Path == null), r => Assert.InRange(analysis.Source.GetPosition(r.Span.Start).Line, 2, 3));
            var (import, at) = WithCaret("import ‸ from \"keys.kscript\"");
            var exported = service.GetCompletions(service.Analyze(import, client), at);
            Assert.StartsWith("@clean()", Assert.Single(exported, i => i.Label == "clean").Detail);
            Assert.DoesNotContain(exported, i => i.Label == "secret");
        }

        [Fact]
        public void RecordFieldsNamedNormalizeAndRecordMethodsStillWork()
        {
            const string code = "type Key { text: string; normalize: fn = fn(value) => $value->trim(); fn size() => $this.text->count() }\n"
                + "key = Key({ text: 'abc' })\necho $key->size()";
            AssertClean(WithoutUnused(Code(code)));
        }

        [Fact]
        public void RefinedMethodsCompleteAndNavigateThroughAnImportedBase()
        {
            string path = Path.GetFullPath("key.kscript");
            var service = new LanguageService(files: new FakeFiles(new Dictionary<string, string> {
                [path] = "type Key with [string] { fn custom_size() => $value->count() }"
            }));
            (string text, int offset) = WithCaret("""
                import "key.kscript"
                type SessionKey with [Key] { fn twice() => $this->custom_size() * 2 }
                key: SessionKey = 'abc'
                echo $key->cus‸tom_size()
                echo $key->twice()
                """);
            DocumentAnalysis analysis = service.Analyze(text, Path.GetFullPath("main.kscript"));
            AssertClean(WithoutUnused(analysis));
            var items = service.GetCompletions(analysis, offset);
            Assert.Contains(items, i => i.Label == "custom_size");
            Assert.Contains(items, i => i.Label == "twice");
            Assert.Equal(path, Assert.Single(service.GetDefinition(analysis, offset)).Path);
        }

        [Theory]
        [InlineData("type Key with [string] { fn own_size() => $value->count() }\nplain = 'abc'\necho $plain->own_size()", "Unknown method ->own_size()")]
        [InlineData("type Key with [string] { fn own_size() => $value->count() }\nmixed = [Key('x'), 'y']\necho $mixed[1]->own_size()", "Unknown method ->own_size()")]
        [InlineData("type Key with [string] { fn own_size() => $value->own_size() }", "Unknown method ->own_size()")]
        [InlineData("type Key with [string] { fn prefix(len: int) => $value->slice(0, $len) }\necho Key('abc')->prefix()", "takes 1 argument")]
        public void RefinedMethodsBelongToTheTypedReceiver(string code, string message) =>
            Assert.Contains(Code(code).Diagnostics, d => d.Message.Contains(message));

        [Fact]
        public void ARefinementOfAnEnumOffersItsOwnAndItsBaseMethods()
        {
            var (text, at) = WithCaret("enum Color { red, blue }\ntype Shade with [Color] { fn label() => $value->upper() }\necho Shade('red')->la‸bel()");
            DocumentAnalysis analysis = Code(text);
            AssertClean(analysis);
            var items = Service.GetCompletions(analysis, at);
            Assert.Contains(items, i => i.Label == "label");
            Assert.Contains(items, i => i.Label == "upper");
        }

        [Fact]
        public void RefinedMethodSignaturesUseTheDeclaringType()
        {
            var (text, at) = WithCaret("type Key with [string] { fn prefix(len: int) => $value->slice(0, $len) }\necho Key('abc')->prefix(‸2)");
            DocumentAnalysis analysis = Code(text);
            AssertClean(analysis);
            var signature = Assert.Single(Service.GetSignatureHelp(analysis, at).Signatures);
            Assert.StartsWith("Key->prefix(len: int)", signature.Label);
        }

        [Fact]
        public void RefinedListMethodsKeepTheTypeOfEachReturnedItem()
        {
            AssertClean(Code("""
                type Release with [semver] { fn label() => $value->to_string() }
                type Releases with [Release[]] { fn newest() => $value->latest() }
                versions = Releases(['1.0.0'])
                echo $versions->newest()->label()
                echo $versions->only(semver)->latest()->label()
                for v in $versions { echo $v->label() }
                """));
        }

        [Theory]
        [InlineData("type User { a: int }\ntype A with User {\n  b: int\n}\nx = A({ a: 1, b: 2 })\necho $x.a")]
        [InlineData("type T {\n  min\n  max: int(\n}")]
        [InlineData("msg = 'too small'\ntype A with [int] {\n  validate: $value > 0, $msg\n}\nx: A = 3\necho $x")]
        [InlineData("x = (//c\n  1)\necho $x")]
        [InlineData("x = 1 +\n  /* c */\n  2\necho $x")]
        [InlineData("x = /* c */\n  2\necho $x")]
        [InlineData("type User { a: int }\ntype A with [User, 'its'] {}")]
        public void TypeBodiesParse(string code)
        {
            MarkupDocument doc = CodeParser.Parse(code);
            Assert.NotNull(doc);
            Code(code);
        }

        [Theory]
        [InlineData("<kscript><type name=\"A\" fields=\"a: int\"><fn name=\"hi\" value=\"'x'\" /></type><type name=\"B\" with=\"A\" /><let name=\"b\" value=\"B({ a: 1 })\" /><echo>{ $b->hi() }</echo></kscript>", null)]
        [InlineData("<kscript><type name=\"T\" fields=\"a: int\"><fn value=\"1\" /></type></kscript>", "needs the 'name' attribute")]
        [InlineData("<kscript><type name=\"A\" with=\"int\"><field name=\"x\" /></type></kscript>", "A refines int, so it has no fields.")]
        [InlineData("<kscript><type name=\"P\" fields=\"a: string\" parse=\"{ a: $value + $match.text }\" /></kscript>", null)]
        [InlineData("<kscript><type name=\"P\" fields=\"value: string\" parse=\"{ value: $value }\" /></kscript>", null)]
        [InlineData("<kscript><type name=\"A\" with=\"\" fields=\"a: int\" /></kscript>", "Name the types to build on")]
        [InlineData("<kscript><type name=\"A\" with=\"[ ]\" fields=\"a: int\" /></kscript>", "Name the types to build on")]
        [InlineData("<kscript><type name=\"U\" fields=\"a: int\" /><type name=\"A\" with=\"int, U\" /></kscript>", "A type built on int refines it, so int must be the only type in with [...].")]
        [InlineData("<kscript><let name=\"x\" type=\"uuid(v: 4\" value=\"3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\" /></kscript>", "*")]
        [InlineData("<kscript><let name=\"x\" type=\"uuid(v: 4)\" value=\"3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\" /><echo>$x</echo></kscript>", null)]
        [InlineData("<kscript><let name=\"r\" type=\"regex\" value=\"/a/\" /><echo>{ $r->test('a') }</echo></kscript>", null)]
        [InlineData("<kscript><let name=\"e\" type=\"email\" value=\"4\" /><echo>$e</echo></kscript>", null)]
        [InlineData("<kscript><let name=\"x\" type=\"string(min: )\" value=\"'a'\" /><echo>$x</echo></kscript>", "*")]
        [InlineData("<kscript><let name=\"n\" value=\"2\" /><let name=\"x\" type=\"string(min: $n)\" value=\"'a'\" /><echo>$x</echo></kscript>", null)]
        [InlineData("<kscript><type name=\"N\" with=\"handle\" not=\"[3]\" /><let name=\"n\" type=\"N\" value=\"@a\" /><echo>$n</echo></kscript>", null)]
        [InlineData("<kscript><type name=\"S\" fields=\"a: string\" pattern=\"/^(a+)+$/\" /><let name=\"s\" type=\"S\" value=\"'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!'\" /><echo>$s</echo></kscript>", null)]
        public void TypeElementsAreChecked(string xml, string message)
        {
            DocumentAnalysis a = WithoutUnused(Analyze(xml));
            if (message == null) AssertClean(a);
            else if (message != "*") Assert.Contains(a.Diagnostics, d => d.Message.Contains(message));
        }

        [Fact]
        public void TypeArgumentsAreSkippedWhenColouring()
        {
            DocumentAnalysis a = Service.Analyze("<kscript><let name=\"x\" type=\"uuid(v: 4)\" value=\"3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\" /><echo>$x</echo></kscript>", "t.ks.xml");
            List<SemanticToken> tokens = Service.GetSemanticTokens(a);
            Assert.Contains(tokens, t => a.Source[t.Span] == "uuid" && t.Type == SemanticTokenType.Type);
            Assert.DoesNotContain(tokens, t => a.Source[t.Span] == "v" && t.Type == SemanticTokenType.Type);
        }

        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => false;
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) => Enumerable.Empty<string>();
        }

        /// <summary>Native types made by kscript make native, in types/, imported by the files that use them.</summary>
        private static readonly LanguageService WithNativeTypes = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
        {
            ["/tmp/natives/types/sku.kscript"] = "type Sku with [string] {\n  pattern: /^[A-Z]{3}-\\d{4}$/\n}\n",
            ["/tmp/natives/types/username.kscript"] = "type Name with [handle] { max: 16 }\ntype Username with [Name] { not: [@admin, @root] }\n",
            ["/tmp/natives/types/percent.ks.xml"] = "<kscript><type name=\"Percent\" with=\"int\" min=\"0\" max=\"100\" /></kscript>",
        }));

        private static DocumentAnalysis Native(string code) => WithNativeTypes.Analyze(code, "/tmp/natives/app.kscript");

        [Fact]
        public void ACastUsesAnImportedTypeAndNavigatesToItsDefinition()
        {
            (string text, int offset) = WithCaret("import \"types/sku.kscript\"\ns = 'ABC-1234' as Sk‸u\necho $s");
            DocumentAnalysis analysis = Native(text);
            AssertClean(analysis);
            Assert.EndsWith("types/sku.kscript", Assert.Single(WithNativeTypes.GetDefinition(analysis, offset)).Path.Replace('\\', '/'));
        }

        [Fact]
        public void ImportedNativeTypesReadRawValuesAndCheckLiterals()
        {
            AssertClean(Native("""
                import "types/sku.kscript"
                import "types/username.kscript"
                import "types/percent.ks.xml"
                s: Sku = ABC-1234
                u: Username = @joan
                fn grow(by: Percent) => $by
                echo "$s $u { @grow(5) }"
                """));

            DocumentAnalysis bad = Native("""
                import "types/missing.kscript" as missing
                import "types/sku.kscript"
                import "types/username.kscript"
                import "types/percent.ks.xml"
                s: Sku = abc
                u: Username = @admin
                p: Percent = 101
                echo "$s $u $p"
                """);
            List<Diagnostic> typed = bad.Diagnostics.Where(d => d.Code == DiagnosticCodes.InvalidTypedValue).ToList();
            Assert.Equal(3, typed.Count);
            Assert.StartsWith("'abc' doesn't fit Sku: it must match", typed[0].Message);
            Assert.Equal("'@admin' doesn't fit Username: it is reserved ('@admin').", typed[1].Message);
            Assert.Equal("'101' doesn't fit Percent: it must be at most 100.", typed[2].Message);
        }

        [Fact]
        public void AnImportUsedOnlyAsATypeIsUsed()
        {
            DocumentAnalysis a = Native("""
                import "types/sku.kscript"
                import "types/percent.ks.xml"
                fn label(sku: Sku, off: Percent?) => $sku
                echo @label('ABC-1234', null)
                """);
            Assert.DoesNotContain(a.Diagnostics, d => d.Code == DiagnosticCodes.UnusedImport);
            DocumentAnalysis unused = Native("import \"types/sku.kscript\"\necho 'hi'");
            Assert.Contains(unused.Diagnostics, d => d.Code == DiagnosticCodes.UnusedImport);
        }

        [Fact]
        public void MultiLineFieldDefaultsStayInTheAttribute()
        {
            ConversionResult r = ScriptConverter.Convert("<kscript><type name=\"A\" with=\"int\" min=\"1&#10;2\" /></kscript>", "a.ks.xml");
            Assert.Contains("A", r.Text);
        }
    }
}
