using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>The editor side of error types: declarations, catch and throw checks, completion and hover.</summary>
    public class ErrorTypeAnalysisTests
    {
        private sealed class FakeFiles : IFileSystem
        {
            private readonly Dictionary<string, string> files;
            public FakeFiles(Dictionary<string, string> files) => this.files = files.ToDictionary(k => Path.GetFullPath(k.Key), k => k.Value);
            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path));
            public bool DirectoryExists(string path) => false;
            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;
            public IEnumerable<string> ListFiles(string directory, string pattern) => Enumerable.Empty<string>();
        }

        private static readonly LanguageService Service = new LanguageService(files: new FakeFiles(new Dictionary<string, string>
        {
            ["/tmp/errors/lib.kscript"] = "error Shared(status: 418) { code: int }\nfn f() { }\n",
        }));

        private const string DocPath = "/tmp/errors/test.kscript";

        private const string Declarations = """
            /// Nothing has that id.
            error PersonNotFound(message: "No person with id {$id}") extends NotFound {
              id: int
              kind?: string
            }
            error OutOfStock(status: 409, message: "{$item} is out of stock") {
              item: string
              left: int = 0
            }
            error Plain

            """;

        private static DocumentAnalysis Open(string code) => WithoutUnused(Service.Analyze(code, DocPath));

        private static string Codes(string code) => string.Join(" ", Open(code).Diagnostics.Select(d => d.Code));

        private static List<CompletionItem> Complete(string code, string path = DocPath)
        {
            (string text, int offset) = WithCaret(code);
            return Service.GetCompletions(Service.Analyze(text, path), offset).ToList();
        }

        private static List<string> Labels(string code, string path = DocPath) => Complete(code, path).Select(i => i.Label).ToList();

        [Fact]
        public void ValidErrorCodeIsClean()
        {
            AssertClean(Open(Declarations + """
                fn find(id: int) {
                  throw PersonNotFound(id: $id)
                }
                try {
                  @find(1)
                  throw OutOfStock(item: "tea", message: "custom", cause: null)
                  throw Validation "Too short"
                  throw Validation(field: "name")
                  throw Plain()
                } catch PersonNotFound | OutOfStock as e {
                  echo "{ $e.id } { $e.item } { $e.left } { $e.status } { $e.cause } { $e->is(NotFound) } { $e->is(PersonNotFound) }"
                  throw
                } catch IO {
                  echo "{ $err.message } { $err.cause.message } { $err.cause.cause.cause.cause.anything }"
                } catch err {
                  saved = Plain()
                  throw $saved
                }
                """));
        }

        [Theory]
        [InlineData("try { } catch Notfound as e { }", "KS5011")]
        [InlineData("try { } catch Notfoundd as e { }", "KS5011")]
        [InlineData("throw Valdation \"x\"", "KS5011")]
        [InlineData("error Bad extends Nope", "KS5011")]
        [InlineData("error lower", "KS3006")]
        [InlineData("error NotFound", "KS3006")]
        [InlineData("error Twice\nerror Twice", "KS4004")]
        [InlineData("error Bad(status: 99)", "KS3006")]
        [InlineData("error Bad { message: string }", "KS3006")]
        [InlineData("error Bad { a: int; a: int }", "KS4004")]
        [InlineData("error A extends B\nerror B extends A", "KS3006 KS3006")]
        [InlineData("throw PersonNotFound()", "KS5004")]
        [InlineData("throw PersonNotFound(id: 1, idd: 2)", "KS5007")]
        [InlineData("throw Plain(x: 1)", "KS5007")]
        [InlineData("throw PersonNotFound(1, 2, id: 1)", "KS5004")]
        [InlineData("throw Validation(1, 2)", "KS5004")]
        [InlineData("try { } catch PersonNotFound as e { echo \"{ $e.nope }\" }", "KS5003")]
        public void MistakesAreFlagged(string code, string codes)
        {
            Assert.Equal(codes, Codes(Declarations + code));
        }

        [Fact]
        public void XmlDeclarationsAreCheckedToo()
        {
            DocumentAnalysis a = WithoutUnused(Service.Analyze(Script("""
                <error name="Gone" fields="what: string, when?: string" message="{$what} is gone {$nope}">
                  <field name="extra" type="int" optional="yes" />
                  <field name="extra" type="int" />
                  <echo>no</echo>
                </error>
                <error name="Gone2" fields="a: int, a: int" />
                <error />
                <throw value="Gone(what: 'x')" />
                <expect_error type="Gone2" />
                """), "/tmp/errors/test.ks.xml"));
            Assert.Equal("KS4004 KS3007 KS4001 KS4004 KS3005 KS5004 KS3009", string.Join(" ", a.Diagnostics.Select(d => d.Code)));
        }

        [Fact]
        public void ImportedErrorTypesAreKnown()
        {
            AssertClean(Open("""
                import "lib.kscript"
                try {
                  throw Shared(code: 1)
                } catch Shared as e {
                  echo "{ $e.code } { $e.status }"
                }
                """));
        }

        [Fact]
        public void ErrorTypeNamesComplete()
        {
            List<CompletionItem> items = Complete(Declarations + "try { } catch P‸ { }");
            Assert.Equal("PersonNotFound", items[0].Label);
            Assert.Equal("error type", items[0].Detail);
            CompletionItem notFound = items.First(i => i.Label == "NotFound");
            Assert.Equal("built-in error, status 404", notFound.Detail);
            Assert.Equal("built-in error, extends IO", items.First(i => i.Label == "FileNotFound").Detail);
            Assert.Equal("built-in error", items.First(i => i.Label == "Syntax").Detail);
            Assert.Contains("PersonNotFound", Labels(Declarations + "throw P‸"));
            Assert.Contains("Validation", Labels(Declarations + "error X extends V‸"));
            Assert.Contains("OutOfStock", Labels(Script("<error name=\"OutOfStock\" /><try><catch type=\"‸\" /></try>"), "/tmp/errors/test.ks.xml"));
        }

        [Fact]
        public void CaughtErrorsCompleteTheirFields()
        {
            List<string> fields = Labels(Declarations + "try { } catch PersonNotFound as e {\n  echo \"{ $e.‸ }\"\n}");
            Assert.Contains("id", fields);
            Assert.Contains("kind", fields);
            Assert.Contains("status", fields);
            Assert.Contains("cause", fields);
            Assert.Contains("item", Labels(Declarations + "x = OutOfStock(item: 'a')\necho \"{ $x.‸ }\""));
            Assert.Contains("is", Labels(Declarations + "try { } catch e {\n  echo \"{ $e->‸ }\"\n}"));
            Assert.Contains("fields", Labels(Declarations + "echo \"{ Plain.‸ }\""));
            Assert.Contains("fields", Labels(Declarations + "t = Plain\necho \"{ $t.‸ }\""));
            Assert.DoesNotContain("new", Labels(Declarations + "echo \"{ Plain->‸ }\""));
        }

        [Fact]
        public void HoverDescribesTheErrorType()
        {
            (string text, int offset) = WithCaret(Declarations + "throw Person‸NotFound(id: 1)");
            string hover = Service.GetHover(Service.Analyze(text, DocPath), offset).Markdown;
            Assert.Contains("Nothing has that id.", hover);
            Assert.Contains("error PersonNotFound extends NotFound (status 404) {", hover);
            Assert.Contains("id: number", hover);
            (text, offset) = WithCaret(Declarations + "error Child extends OutOfStock\nthrow Ch‸ild(item: 'x')");
            Assert.Contains("error Child extends OutOfStock (status 409) {", Service.GetHover(Service.Analyze(text, DocPath), offset).Markdown);
            (text, offset) = WithCaret(Declarations + "throw Pl‸ain()");
            Assert.Contains("\nerror Plain\n", Service.GetHover(Service.Analyze(text, DocPath), offset).Markdown);
        }

        [Fact]
        public void DeclarationsAreNotReportedAsUnused()
        {
            Assert.DoesNotContain(Service.Analyze("error Unused { a: int }\n", DocPath).Diagnostics, d => d.Code == DiagnosticCodes.UnusedVariable);
        }

        [Theory]
        [InlineData("error A", "<error name=\"A\" />")]
        [InlineData("error A(status: 404, message: \"No {$id}\") extends NotFound {\n  id: int\n}", "<error name=\"A\" status=\"404\" message=\"No {$id}\" extends=\"NotFound\">\n    <field name=\"id\" type=\"int\" />\n  </error>")]
        [InlineData("error A extends NotFound", "<error name=\"A\" extends=\"NotFound\" />")]
        [InlineData("error A {\n  // why\n  id: int\n}", "<error name=\"A\">\n    <!-- why -->\n    <field name=\"id\" type=\"int\" />\n  </error>")]
        public void ConvertsBothWays(string code, string xml)
        {
            ConversionResult r = ScriptConverter.Convert(code + "\n", "a.kscript");
            Assert.Equal("<kscript>\n  " + xml + "\n</kscript>\n", r.Text);
            Assert.Equal(code + "\n", ScriptConverter.Convert(r.Text, "a.ks.xml").Text);
        }

        [Theory]
        [InlineData("try {\n} catch NotFound | Conflict as e { }\n")]
        [InlineData("try {\n} catch IO { }\n")]
        [InlineData("try {\n} catch err when $err.status == 404 { }\n")]
        [InlineData("try {\n} catch Error as Upper { }\n")]
        [InlineData("throw NotFound(id: 1)\n")]
        [InlineData("throw $err\n")]
        [InlineData("throw\n")]
        public void CatchAndThrowRoundTrip(string code)
        {
            string xml = ScriptConverter.Convert(code, "a.kscript").Text;
            Assert.Equal(code, ScriptConverter.Convert(xml, "a.ks.xml").Text);
        }

        [Fact]
        public void XmlCatchesWithOddTypesStayReadable()
        {
            Assert.Contains("catch(type: lower) e { }", ScriptConverter.Convert("<kscript><try><catch type=\"lower\" as=\"e\" /></try></kscript>", "a.ks.xml").Text);
            Assert.Contains("catch Error as E { }", ScriptConverter.Convert("<kscript><try><catch as=\"E\" /></try></kscript>", "a.ks.xml").Text);
            Assert.Contains("catch(type: x y) as E { }", ScriptConverter.Convert("<kscript><try><catch type=\"x y\" as=\"E\" /></try></kscript>", "a.ks.xml").Text);
            Assert.Contains("catch(type: lower) { }", ScriptConverter.Convert("<kscript><try><catch type=\"lower\" /></try></kscript>", "a.ks.xml").Text);
            // An error whose fields can't be written one per line stays an element.
            Assert.Contains("error(name: A, extends: x y)", ScriptConverter.Convert("<kscript><error name=\"A\" extends=\"x y\" /></kscript>", "a.ks.xml").Text);
        }

        [Theory]
        [InlineData("error A extends", "Write the error type it extends after 'extends'")]
        [InlineData("try { } catch NotFound | { }", "Write another error type after |")]
        [InlineData("try { } catch NotFound as { }", "Write a name for the error after 'as'")]
        public void ParserMistakesAreExplained(string code, string message)
        {
            Assert.Contains(CodeParser.Parse(code).Diagnostics, d => d.Message.Contains(message));
        }
    }
}
