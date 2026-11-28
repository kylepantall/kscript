using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>Types, enums, errors and models inside a component: the checker, the converter and completion.</summary>
    public sealed class NestedDeclarationAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-nested-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public NestedDeclarationAnalysisTests() => Directory.CreateDirectory(Path.Combine(folder, "_components"));

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return path;
        }

        private DocumentAnalysis Check(string name, string text) => TestHelpers.WithoutUnused(service.Analyze(text, Write(name, text)));

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        private const string Component = """
            import KScript.Web
            import KScript.Data

            type CommentThread extends Component {
              model Comment extends Timestamps {
                post_id: int
                body: string(min: 1, max: 2000)
              }

              /// What is being typed.
              type Draft {
                body: string = ''
              }

              enum Order { newest, oldest }

              error TooLong(message: "That comment is too long")

              construct({ post_id: int, order: Order = 'newest' })

              draft: Draft = Draft({ })

              fn add() {
                if $draft.body->count() > 2000 {
                  throw TooLong()
                }
                Comment->create(post_id: $post_id, body: $draft.body)
                draft = Draft({ })
              }

              fn render(): Component {
                return ```html
                <p>{Comment->where(post_id: $post_id)->count()} comments, {$order} first</p>
                ```
              }
            }

            """;

        [Fact]
        public void A_component_uses_the_declarations_inside_it()
        {
            Assert.Equal(string.Empty, Messages(Check("_components/comment-thread.kscript", Component)));
            // The rest of the file sees them too.
            Assert.Equal(string.Empty, Messages(Check("_components/comment-thread.kscript", Component + "first: Draft = Draft({ })\necho $first.body\n")));
        }

        [Fact]
        public void A_nested_declaration_does_not_see_the_components_fields()
        {
            string text = Component.ReplaceLineEndings("\n").Replace("    body: string = ''\n", "    body: string = ''\n    validate: $post_id > 0, 'no'\n");
            Assert.Contains("post_id", Messages(Check("_components/comment-thread.kscript", text)));
        }

        [Fact]
        public void Only_a_component_holds_declarations()
        {
            string message = Messages(Check("box.kscript", "type Box {\n  enum Size { small, large }\n  size: Size\n}\n"));
            Assert.Contains("KS3007 Only a component can hold its own types, so enum Size can't sit inside type Box. Give it its own file.", message);
        }

        [Fact]
        public void Another_file_is_told_why_it_cannot_use_one()
        {
            Write("_components/comment-thread.kscript", Component);
            const string why = "is declared inside the component CommentThread, so only that file can use it. To share it, move it to its own file and import that.";
            string messages = Messages(Check("other.kscript", "import \"_components/comment-thread.kscript\"\n\nd = Draft({ })\nc: Comment = Comment->first()\nfn pick(order: Order) => $order\necho $d\necho $c\necho @pick('newest')\n"));
            Assert.Contains("KS5001 No type named Draft is declared or imported here. Draft " + why, messages);
            Assert.Contains("KS3006 Unknown type 'Comment'. Comment " + why, messages);
            Assert.Contains("KS4001 No type named Comment is declared or imported here. Comment " + why, messages);
            // A name no component holds keeps the usual message.
            Assert.Contains("Text needs quotes ('Missing')", Messages(Check("other.kscript", "import \"_components/comment-thread.kscript\"\n\necho Missing\n")));
        }

        [Fact]
        public void The_converter_writes_them_in_both_formats()
        {
            string source = Component.ReplaceLineEndings("\n");
            string xml = ScriptConverter.Convert(source, "comment-thread.kscript").Text;
            Assert.Contains("    <model name=\"Comment\" with=\"Timestamps\">", xml);
            Assert.Contains("    <enum name=\"Order\" values=\"newest, oldest\" />", xml);
            Assert.Contains("    <error name=\"TooLong\" message=\"That comment is too long\" />", xml);
            string code = ScriptConverter.Convert(xml, "comment-thread.ks.xml").Text;
            Assert.Contains("type CommentThread extends Component {\n  model Comment extends Timestamps {\n    post_id: int\n", code);
            Assert.Contains("  /// What is being typed.\n  type Draft {\n    body: string = ''\n  }\n", code);
            Assert.Contains("  enum Order { newest, oldest }\n", code);
            Assert.Contains("  error TooLong(message: \"That comment is too long\")\n", code);
            // A nested declaration the code format can't hold keeps the whole component in its long form.
            string odd = ScriptConverter.Convert("<kscript><type name=\"T\" extends=\"Component\"><enum name=\"E\" values=\"a\"><echo>x</echo></enum></type></kscript>", "t.ks.xml").Text;
            Assert.DoesNotContain("type T extends Component", odd);
        }

        [Fact]
        public void Completion_offers_them_in_a_component_only()
        {
            List<string> Labels(string text)
            {
                int offset = text.IndexOf('‸');
                text = text.Remove(offset, 1);
                return service.GetCompletions(service.Analyze(text, Write("_components/c.kscript", text)), offset, null).Select(i => i.Label).ToList();
            }
            List<string> inside = Labels("import KScript.Web\n\ntype Card extends Component {\n  ‸\n  title: string\n}\n");
            Assert.Equal(new[] { "type", "model", "enum", "error" }, inside.Where(ComponentScript.NestedDeclarations.Contains));
            Assert.DoesNotContain(Labels("type Card {\n  ‸\n  title: string\n}\n"), ComponentScript.NestedDeclarations.Contains);
        }
    }
}
