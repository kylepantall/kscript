using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    /// <summary>@inject() in the checker: the model it is given, or the model of the input it fills.</summary>
    public sealed class InjectAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-inject-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public InjectAnalysisTests() => Directory.CreateDirectory(folder);

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private DocumentAnalysis Check(string text)
        {
            string path = Path.Combine(folder, "app.kscript");
            File.WriteAllText(path, text);
            return TestHelpers.WithoutUnused(service.Analyze(text, path));
        }

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        private const string Types = """
            import KScript.Data

            model Post {
              title: string
            }

            type Plain {
              name: string
            }

            """;

        [Fact]
        public void An_input_or_a_named_model_is_fine_and_typed()
        {
            DocumentAnalysis a = Check(Types + """
                type Card {
                  construct({
                    post: Post = @inject(),
                    other?: Post = @inject(from: article),
                  })
                }

                post = @inject(Post)
                same = @inject(model: Post, from: 'article')
                held = { model: Post }
                loose = @inject($held.model)
                echo $loose
                echo $post.title
                echo $same.title
                echo $post.nope

                """);
            Assert.Equal("KS5010 Post has no field 'nope'. Its fields are: id, title.", Messages(a));
        }

        [Fact]
        public void What_it_cannot_find_is_reported_as_it_would_fail()
        {
            DocumentAnalysis a = Check(Types + """
                type Card {
                  construct({
                    count: int = @inject(),
                    tags: Post[] = @inject(),
                    plain: Plain = @inject(),
                  })

                  loose = @inject()
                }

                a = @inject()
                b = @inject(Plain)
                c = @inject(3)

                """);
            string messages = Messages(a);
            Assert.Contains("KS5005 @inject() fills an input whose type is a model, and 'count' is int.", messages);
            Assert.Contains("KS5005 @inject() fills an input whose type is a model, and 'tags' is Post[].", messages);
            Assert.Contains("KS5005 @inject() finds the record of a model, and Plain isn't a model.", messages);
            Assert.Contains("KS5005 @inject() on its own needs the model to find: @inject(Post).", messages);
            Assert.Contains("KS5005 @inject() takes a model, such as @inject(Post), but got number.", messages);
            Assert.Contains("KS5005 @inject() fills an input whose type is a model, and 'loose' is any.", messages);
            Assert.Equal(7, a.Diagnostics.Count);
        }
    }
}
