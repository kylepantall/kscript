using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    public class PatternLiteralAnalysisTests
    {
        private const string KeyType = "type SessionKey with [string] { pattern: /passkey_[a-z]+/ }\n";
        private static readonly LanguageService Service = new LanguageService();

        [Fact]
        public void ARecognizedWordIsAnInstanceAndNavigatesWithoutRenamingItsText()
        {
            (string text, int at) = WithCaret(KeyType + "k = passkey_‸login\nquoted = 'passkey_login'\necho $k");
            DocumentAnalysis a = Analyze(text, "main.kscript");
            AssertClean(a);
            Assert.Equal("SessionKey", a.Symbols.Single(s => s.Name == "k").Shape.Name);
            Assert.Null(a.Symbols.Single(s => s.Name == "quoted").Shape.Name);
            Assert.Equal("SessionKey", a.Source[Assert.Single(Service.GetDefinition(a, at)).Span]);
            Assert.Contains("passkey_login: SessionKey", Service.GetHover(a, at).Markdown);
            Assert.Contains("$k: SessionKey", Service.GetHover(a, text.LastIndexOf("$k", StringComparison.Ordinal) + 1).Markdown);
            Assert.Contains(Service.GetSemanticTokens(a), t => t.Span.Contains(at) && t.Type == SemanticTokenType.String);
            Assert.Null(Service.PrepareRename(a, at));
            var rename = Service.Rename(a, text.IndexOf("SessionKey", StringComparison.Ordinal), "Key");
            Assert.Null(rename.Error);
            Assert.Equal(text.Replace("type SessionKey", "type Key"), AnalyzerTests.Apply(text, rename.Changes));
        }

        [Fact]
        public void MatcherChecksTheWholeWordAndUsesTheDeclaredRegexFlags()
        {
            DocumentAnalysis a = Analyze("type Tag with [string] { pattern: /TAG|TAG_[a-z]+/i }\nfound = tag_blue\nother = prefixed_tag_blue", "main.kscript");
            Assert.Equal("Tag", a.Symbols.Single(s => s.Name == "found").Shape.Name);
            Assert.Equal("prefixed_tag_blue", a.Source[AssertSingle(a, DiagnosticCodes.BareIdentifier).Span]);
        }

        [Fact]
        public void SelectingTheTypeChecksConstraintsAndDefersRuntimeNormalization()
        {
            const string type = "type Tag with [string] { min: 8; pattern: /tag_[a-z]+/ }\n";
            DocumentAnalysis bad = Analyze(type + "tag = tag_x", "main.kscript");
            Assert.Contains("at least 8", AssertSingle(bad, DiagnosticCodes.InvalidTypedValue).Message);
            DocumentAnalysis normalizes = Analyze(type.Replace("min: 8", "normalize: $value + 'long'; min: 8") + "tag = tag_x", "main.kscript");
            AssertClean(normalizes);
        }

        [Fact]
        public void AmbiguousNamesAreSortedAndTypedOrQuotedValuesDisambiguate()
        {
            DocumentAnalysis a = Analyze("""
                type Zebra with [string] { pattern: /word_[a-z]+/ }
                type Alpha with [string] { pattern: /word_.*/ }
                k = word_test
                explicit: Zebra = word_test
                quoted = 'word_test'
                """, "main.kscript");
            Diagnostic error = AssertSingle(a, DiagnosticCodes.InvalidTypedValue);
            Assert.Equal("word_test", a.Source[error.Span]);
            Assert.Contains("Alpha, Zebra", error.Message);
            Assert.Contains("'word_test' as Alpha", error.Message);
        }

        [Fact]
        public void AnExplicitCastPicksItsTypeAndKeepsExpressionsAndResolvedNames()
        {
            DocumentAnalysis a = Analyze("""
                type Alpha with [string] { pattern: /word_[a-z]+/ }
                type Zebra with [string] { pattern: /word_.+/ }
                kind = 'test'
                picked = word_test as Alpha
                dynamic = "word_{$kind}" as Zebra
                wrong = wrong_word as Alpha
                word_bound = 'word_ok'
                needs_sigil = word_bound as Alpha
                """, "main.kscript");
            Assert.Equal("wrong_word", a.Source[AssertSingle(a, DiagnosticCodes.InvalidTypedValue).Span]);
            Assert.Equal("word_bound", a.Source[AssertSingle(a, DiagnosticCodes.BareIdentifier).Span]);
            Assert.Equal("Alpha", a.Symbols.Single(s => s.Name == "picked").Shape.Name);
            Assert.Equal("Zebra", a.Symbols.Single(s => s.Name == "dynamic").Shape.Name);
            Symbol kind = a.Symbols.Single(s => s.Name == "kind");
            Assert.Contains(a.ReferencesTo(kind), r => !r.IsDeclaration && r.Span.Start > a.Source.Text.IndexOf("dynamic", StringComparison.Ordinal));
        }

        [Fact]
        public void NamedArgumentsFlagsEnumsAndColumnNamesHavePriority()
        {
            DocumentAnalysis a = Analyze("""
                type Token with [string] { pattern: /[a-z]+/ }
                enum Size { large, small }
                fn show(item, size: Size, urgent: bool = false) => $item
                first = @show(apple, large, urgent)
                second = @show(item: apple, size: small)
                rows = [{ apple: 1 }]
                picked = $rows->pluck(apple)
                """, "main.kscript");
            Assert.DoesNotContain(a.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            var literals = a.References.Where(r => r.IsPatternLiteral).ToList();
            Assert.Equal(new[] { "apple", "apple" }, literals.Select(r => a.Source[r.Span]));
        }

        [Fact]
        public void ExplicitConstructorAndListParameterTypesClaimBareValuesBeforeInference()
        {
            DocumentAnalysis a = Analyze("""
                type Zebra with [string] { pattern: /word_[a-z]+/ }
                type Alpha with [string] { pattern: /word_[a-zA-Z]+/ }
                type Row { tag: Zebra; tags: Zebra[]; label: string }
                fn pass(tags: Alpha[]) => $tags
                first = Zebra(word_lower)
                second = Row(tag: word_lower, tags: [word_lower], label: word_UPPER)
                third = @pass([word_lower])
                """, "main.kscript");
            Assert.DoesNotContain(a.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            SymbolReference literal = Assert.Single(a.References, r => r.IsPatternLiteral);
            Assert.Equal("word_UPPER", a.Source[literal.Span]);
            Assert.Equal("Alpha", literal.Symbol.Name);
        }

        [Fact]
        public void ExistingVariableAndFunctionSpellingsKeepTheirDiagnostics()
        {
            DocumentAnalysis a = Analyze(KeyType + "fn passkey_fn() => 1\npasskey_var = 2\na = passkey_fn\nb = passkey_var", "main.kscript");
            Assert.Equal(2, a.Diagnostics.Count(d => d.Code == DiagnosticCodes.BareIdentifier));
            Assert.DoesNotContain(a.References, r => r.IsPatternLiteral);
        }

        [Fact]
        public void ReusingTheSameWordHonorsAParameterThatShadowsItsType()
        {
            DocumentAnalysis a = Analyze(KeyType + """
                before = passkey_login
                fn with_shadow(SessionKey: string) => passkey_login
                after = passkey_login
                """, "main.kscript");
            Diagnostic error = AssertSingle(a, DiagnosticCodes.BareIdentifier);
            Assert.Equal(2, a.Source.GetPosition(error.Span.Start).Line);
            Assert.Equal("SessionKey", a.Symbols.Single(s => s.Name == "after").Shape.Name);
            Assert.Equal(2, a.References.Count(r => r.IsPatternLiteral));
        }

        [Fact]
        public void XmlUsesTheSameRulesAndInheritedPatternsAreNotAdditionalCandidates()
        {
            DocumentAnalysis a = Analyze(Script("""
                <type name="Tag" with="string" pattern="/tag_[a-z]+/" />
                <type name="Special" with="Tag" min="10" />
                <let name="label" value="tag_blue" />
                <echo>$label</echo>
                """));
            Assert.DoesNotContain(a.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal("Tag", a.Symbols.Single(s => s.Name == "label").Shape.Name);
        }

        [Fact]
        public void ExplicitImportsAndProjectNativesAreVisibleButAliasOnlyImportsAreNot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kscript-literal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "natives"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "kscript.json"), "{}");
                string main = Path.Combine(dir, "main.kscript");
                string typeFile = Path.Combine(dir, "key.kscript");
                File.WriteAllText(typeFile, KeyType);
                DocumentAnalysis imported = Service.Analyze("import \"key.kscript\"\nk = passkey_login\necho $k", main);
                AssertClean(imported);
                Assert.Equal(typeFile, Assert.Single(Service.GetDefinition(imported, imported.Source.Text.IndexOf("passkey_login", StringComparison.Ordinal))).Path);
                DocumentAnalysis alias = Service.Analyze("import \"key.kscript\" as lib\nk = passkey_login", main);
                AssertSingle(alias, DiagnosticCodes.BareIdentifier);

                // An alias-only module can declare its own type of the same name with different constraints.
                string hidden = Path.Combine(dir, "hidden.kscript");
                File.WriteAllText(hidden, KeyType.Replace("pattern:", "min: 50; pattern:"));
                DocumentAnalysis visible = Service.Analyze("import \"hidden.kscript\" as lib\nimport \"key.kscript\"\nk = passkey_login", main);
                Assert.DoesNotContain(visible.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

                string native = Path.Combine(dir, "natives", "key.kscript");
                File.Move(typeFile, native);
                DocumentAnalysis implicitImport = Service.Analyze("k = passkey_login\necho $k", main);
                AssertClean(implicitImport);
                Assert.Equal("SessionKey", implicitImport.Symbols.Single(s => s.Name == "k").Shape.Name);
                // A later edit with the same word re-resolves it against the current file contents.
                File.WriteAllText(native, KeyType.Replace("passkey_", "other_"));
                AssertSingle(Service.Analyze("k = passkey_login", main), DiagnosticCodes.BareIdentifier);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
