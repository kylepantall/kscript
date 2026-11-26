using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>private fields and methods in the parser, the checker, the converter and completion.</summary>
    public sealed class PrivateMemberAnalysisTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "kscript-private-" + Guid.NewGuid().ToString("N"));
        private readonly LanguageService service = new LanguageService();

        public PrivateMemberAnalysisTests() => Directory.CreateDirectory(folder);

        public void Dispose() => Directory.Delete(folder, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            return path;
        }

        private DocumentAnalysis Check(string name, string text) => TestHelpers.WithoutUnused(service.Analyze(text, Write(name, text)));

        private static string Messages(DocumentAnalysis a) => string.Join("\n", a.Diagnostics.Select(d => d.Code + " " + d.Message));

        private const string Types = """
            type Base {
              private seed: int = 3
              private fn twice() => $seed * 2
            }

            type Child with [Base] {
              fn show() => $this->twice() + $this.seed
            }

            type Counter {
              construct({ start: int = 0 }) {
                count = $start
              }

              /// How far it got.
              private count: int

              /// Twice the count.
              private fn tally() => $count * 2

              fn total() => $this->tally() + $this.count
              fn same(other: Counter) => $other.count == $count
            }

            c = Counter(start: 2)
            echo $c->total()
            echo $c->same($c)
            echo Child()->show()

            """;

        [Fact]
        public void A_type_and_the_types_joined_with_it_use_its_private_members()
        {
            Assert.Equal(string.Empty, Messages(Check("types.kscript", Types)));
        }

        [Fact]
        public void Code_outside_the_type_is_told_the_member_is_private()
        {
            string messages = Messages(Check("types.kscript", Types + """
                echo $c.count
                echo $c->tally()
                c.count = 5
                x = Counter(count: 4)
                echo Child().seed
                type Other {
                  fn peek(b: Base) => $b.seed
                }
                echo $x
                """));
            Assert.Equal(new[]
            {
                "KS5010 'count' is private to Counter. Only Counter's own methods can use it.",
                "KS5010 'tally' is private to Counter. Only Counter's own methods can use it.",
                "KS5010 'count' is private to Counter. Only Counter's own methods can use it.",
                "KS5007 'count' is private to Counter and can't be passed in.",
                "KS5010 'seed' is private to Base. Only Base's own methods can use it.",
                "KS5010 'seed' is private to Base. Only Base's own methods can use it.",
            }, messages.Split('\n'));
        }

        [Fact]
        public void A_components_html_uses_its_private_state()
        {
            Directory.CreateDirectory(Path.Combine(folder, "_components"));
            const string card = """
                import KScript.Web

                type FlipCard extends Component {
                  private open: bool
                  private fn label() => $open ? 'Hide' : 'Show'

                  fn render() => ```html
                    <p>{$this.open} {$this->label()}</p>
                    <button @on-click(toggle: $open)>Toggle</button>
                    ```
                }
                """;
            Assert.Equal(string.Empty, Messages(Check("_components/flip-card.kscript", card)));
        }

        [Fact]
        public void The_word_is_parsed_only_in_front_of_a_member_and_round_trips()
        {
            const string code = "type Box {\n  /// The key.\n  private key: string = 'k'\n  private: bool\n  private_note: string\n  /// Reads it.\n  private fn peek() => $key\n}\n";
            MarkupDocument document = CodeParser.Parse(code);
            Assert.Empty(document.Diagnostics);
            MarkupElement box = document.DescendantElements().First(e => e.Name == "type");
            Assert.Equal(new[] { "key:True", "private:False", "private_note:False", "peek:True" },
                box.ChildElements.Select(e => e.GetAttributeValue("name") + ":" + TypeBody.IsPrivate(e)));
            Assert.Equal("The key.", box.ChildElements.First().GetAttributeValue("description"));
            Assert.StartsWith("private key", code.Substring(box.ChildElements.First().Span.Start));
            Assert.False(TypeBody.IsInput(box.ChildElements.First()));

            string xml = ScriptConverter.Convert(code, "box.kscript").Text;
            Assert.Equal(2, xml.Split("private=\"yes\"").Length - 1);
            Assert.Equal(code, ScriptConverter.Convert(xml, "box.ks.xml").Text);
            Assert.Equal(code, KScript.Language.Formatting.Formatter.Format(code, "box.kscript").Text);

            // Where the attribute can't be written as the word, the element stays as it is.
            Assert.DoesNotContain("private note", ScriptConverter.Convert("<kscript><error name=\"Odd\"><field name=\"note\" type=\"string\" private=\"yes\" /></error></kscript>", "o.ks.xml").Text);
            Assert.DoesNotContain("private fn", ScriptConverter.Convert("<kscript><fn name=\"loose\" private=\"yes\" value=\"1\" /></kscript>", "o.ks.xml").Text);
            Assert.DoesNotContain("private fn", ScriptConverter.Convert("<kscript><type name=\"T\"><fn name=\"f\" private=\"no\" value=\"1\" /></type></kscript>", "o.ks.xml").Text);
        }

        [Fact]
        public void Completion_offers_private_members_only_inside_the_type()
        {
            List<string> Labels(string text)
            {
                int offset = text.IndexOf('‸');
                text = text.Remove(offset, 1);
                return service.GetCompletions(service.Analyze(text, Write("c.kscript", text)), offset, null).Select(i => i.Label).ToList();
            }
            List<string> outsideFields = Labels(Types + "echo $c.‸\n");
            Assert.Contains("start", outsideFields);
            Assert.DoesNotContain("count", outsideFields);
            List<string> outsideMethods = Labels(Types + "echo $c->‸\n");
            Assert.Contains("total", outsideMethods);
            Assert.DoesNotContain("tally", outsideMethods);
            Assert.Contains("count", Labels(Types.Replace("$other.count == $count", "$other.‸ == $count")));
            Assert.Contains("tally", Labels(Types.Replace("$this->tally() + $this.count", "$this->‸")));
        }
    }
}
