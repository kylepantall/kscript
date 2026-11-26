using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Tests
{
    /// <summary>policy for Model { } in the parser, the checker and the converter.</summary>
    public sealed class PolicyAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-policy-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public PolicyAnalysisTests()
        {
            Directory.CreateDirectory(Path.Combine(folder, "models"));
            Directory.CreateDirectory(Path.Combine(folder, "policies", "more"));
            File.WriteAllText(Path.Combine(folder, "kscript.json"), "{ \"name\": \"blog\" }");
            File.WriteAllText(Path.Combine(folder, "models", "post.kscript"), "import KScript.Data\n\nmodel Post {\n  title: string\n  author_id: int\n}\n");
            File.WriteAllText(Path.Combine(folder, "models", "tag.kscript"), "import KScript.Data\n\nmodel Tag {\n  name: string\n}\n");
            File.WriteAllText(Path.Combine(folder, "policies", "post.kscript"), Policy);
            File.WriteAllText(Path.Combine(folder, "policies", "more", "notes.txt"), "not a script");
        }

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private const string Policy = """
            import KScript.Auth
            import "../models/post.kscript"

            /// Who may do what to a post.
            policy for Post {
              fn before(user: map?) => $user?.role == "admin" ? true : null

              fn read(user: map?, post: Post) => true
              fn update(user: map, post: Post) => $user.id == $post.author_id
            }

            """;

        private DocumentAnalysis Check(string name, string text)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return service.Analyze(text, path);
        }

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        [Fact]
        public void A_policy_checks_clean_and_its_abilities_are_not_unused()
        {
            Assert.Equal(string.Empty, Messages(Check("policies/post.kscript", Policy)));
        }

        [Fact]
        public void A_variable_called_policy_is_still_a_variable()
        {
            Assert.Equal(string.Empty, Messages(Check("plain.kscript", "policy = 3\npolicy += 1\necho $policy\n")));
            MarkupDocument doc = MarkupParser.Parse(new SourceText("policy forty = 1\n"), MarkupParser.KindFromPath("a.kscript"), "a.kscript");
            Assert.DoesNotContain(doc.DescendantElements(), e => e.Name == "policy" && e.GetAttribute("for") != null);
            doc = MarkupParser.Parse(new SourceText("policy for(x)\n"), MarkupParser.KindFromPath("a.kscript"), "a.kscript");
            Assert.DoesNotContain(doc.DescendantElements(), e => e.Name == "policy" && e.GetAttribute("for")?.ValueText == "x");
        }

        [Fact]
        public void The_block_is_checked_as_it_would_run()
        {
            string messages = Messages(Check("broken.kscript", """
                import KScript.Auth
                import "models/post.kscript"

                policy for Nope {
                }

                policy for Post {
                  fn read(a: map?, b: Post, c: int) => true
                  fn read() => true
                  fn before(a: map?, b: Post) => null
                  echo "no"
                }

                if true {
                  policy for Post {
                  }
                }

                """));
            Assert.Contains("KS3006 Unknown type 'Nope' for the policy.", messages);
            Assert.Contains("KS5004 An ability takes the user, then the Post: fn read(user: User, post: Post).", messages);
            Assert.Contains("KS4004 The Post policy has 'read' twice. Keep one.", messages);
            Assert.Contains("KS5004 before takes the user only: fn before(user: User).", messages);
            Assert.Contains("KS3007 policy for Post { } holds fn abilities only, not echo.", messages);
            Assert.Contains("KS3007 A policy goes at the top level of a file, not inside a block.", messages);

            string xml = Messages(Check("broken.ks.xml", """
                <kscript>
                  <import package="KScript.Auth" />
                  <import path="models/post.kscript" />
                  <policy for="">
                    <fn name="" value="true" />
                  </policy>
                  <policy for="Post">
                    <echo>no</echo>
                  </policy>
                  <if test="true">
                    <policy for="Post" />
                  </if>
                </kscript>
                """));
            Assert.Contains("KS3007 <policy> holds <fn> elements only, not <echo>.", xml);
            Assert.Contains("KS3007 <policy> must be a direct child of <kscript>.", xml);
        }

        [Fact]
        public void An_ability_must_be_one_the_policy_has()
        {
            string messages = Messages(Check("ask.kscript", """
                import KScript.Auth
                import "models/post.kscript"
                import "models/tag.kscript"

                fn ask(post: Post, tag: Tag, what: string) {
                  echo @can(update, $post)
                  echo @cannot('read', $post)
                  echo @can(publish, $post)
                  echo @can(ability: 'before', target: $post)
                  echo @can(create, Post)
                  echo @can(read, $tag)
                  echo @can($what, $post)
                  echo @can(read)
                  @authorize(update, $post)
                }

                @ask(Post->find(1), Tag->find(1), 'read')

                """));
            Assert.Contains("KS5009 The Post policy has no 'publish'. It has: read, update.", messages);
            Assert.Contains("KS5009 The Post policy has no 'before'. It has: read, update.", messages);
            Assert.Contains("KS5009 The Post policy has no 'create'. It has: read, update.", messages);
            Assert.DoesNotContain("Tag policy", messages);
            Assert.Equal(3, messages.Split('\n').Count(m => m.StartsWith("KS5009", StringComparison.Ordinal)));
        }

        [Fact]
        public void An_injection_that_asks_nothing_is_pointed_out()
        {
            DocumentAnalysis a = Check("card.kscript", """
                import KScript.Auth
                import "models/post.kscript"
                import "models/tag.kscript"

                type Card {
                  construct({
                    post: Post = @inject(),
                    asked: Post = @inject(can: read),
                    tag: Tag = @inject(),
                  })
                }

                fn both() => [@inject(Post), @inject(Post, can: read), @inject(Tag), Card()]

                echo @both()

                """);
            var warned = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.UncheckedInjection).ToList();
            Assert.Equal(2, warned.Count);
            Assert.All(warned, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
            Assert.Equal("Post has a policy, and this takes a Post without asking it. Write @inject(can: read) to ask, or check with @can() where it is used.", warned[0].Message);
        }

        [Fact]
        public void A_policy_in_the_file_is_read_before_the_folder()
        {
            string messages = Messages(Check("own.kscript", """
                import KScript.Auth
                import "models/tag.kscript"

                policy for Tag {
                  fn rename(user: map?, tag: Tag) => true
                }

                policy for Tag {
                  fn other(user: map?) => true
                }

                fn ask(tag: Tag) => @can(rename, $tag) and @can(read, $tag)

                echo @ask(Tag->find(1))

                """));
            Assert.Contains("KS5009 The Tag policy has no 'read'. It has: rename.", messages);
        }

        [Fact]
        public void It_converts_to_xml_and_back()
        {
            string xml = ScriptConverter.Convert(Policy, "post.kscript").Text;
            Assert.Contains("<policy for=\"Post\" description=\"Who may do what to a post.\">", xml);
            Assert.Contains("<fn name=\"read\" params=\"user: map?, post: Post\" value=\"true\" />", xml);
            string back = ScriptConverter.Convert(xml, "post.ks.xml").Text;
            Assert.Contains("/// Who may do what to a post.\npolicy for Post {", back);
            Assert.Contains("  fn update(user: map, post: Post) => $user.id == $post.author_id", back);

            // A policy that holds something else stays an element call.
            string odd = ScriptConverter.Convert("<kscript><policy for=\"Post\"><echo>x</echo></policy></kscript>", "odd.ks.xml").Text;
            Assert.DoesNotContain("policy for Post {", odd);
        }
    }
}
