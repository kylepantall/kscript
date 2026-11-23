using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests
{
    public class ExpectedVariableCompletionTests
    {
        private static readonly LanguageService Service = new(files: EmptyFileSystem.Instance);
        private const string Variables = "good: int = 1\nrecent: int = 2\nwrong: string = 'nearer'\n";

        private static List<string> Choices(string code, params string[] names)
        {
            (string text, int offset) = TestHelpers.WithCaret(code);
            return Service.GetCompletions(Service.Analyze(text, "main.kscript"), offset, "$")
                .Where(item => names.Contains(item.Label)).OrderBy(item => item.SortText, StringComparer.Ordinal)
                .Select(item => item.Label).ToList();
        }

        [Theory]
        [InlineData("fn f(count: int) => $count\necho @f($‸)")]
        [InlineData("fn f(count: int) => $count\necho @f(count: $go‸)")]
        [InlineData("fn f(label: string, count: int) => $count\necho @f(label: 'item', $‸)")]
        [InlineData("fn f(label: string, count: int) => $count\necho 'item' |> @f($‸)")]
        [InlineData("fn f(counts: int[]) => $counts\necho @f([1, $‸])")]
        [InlineData("count: int = $‸")]
        public void Matching_types_come_first_and_ties_keep_their_normal_proximity_order(string use)
        {
            Assert.Equal(new[] { "recent", "good", "wrong" }, Choices(Variables + use, "good", "recent", "wrong"));
        }

        [Theory]
        [InlineData("fn f(x) => $x\necho @f($‸)")]
        [InlineData("fn f(x: any) => $x\necho @f($‸)")]
        [InlineData("fn f(x: UnknownThing) => $x\necho @f($‸)")]
        [InlineData("fn f(x: int) => $x\necho @f(1 + $‸)")]
        public void Unknown_or_indirect_expectations_keep_normal_variable_order(string use)
        {
            Assert.Equal(new[] { "wrong", "recent", "good" }, Choices(Variables + use, "good", "recent", "wrong"));
        }

        [Fact]
        public void List_slots_compare_element_types()
        {
            const string code = "counts: int[] = [1]\nlabels: string[] = ['new']\nfn f(v: int[]) => $v\necho @f($‸)";
            Assert.Equal(new[] { "counts", "labels" }, Choices(code, "counts", "labels"));
        }

        [Fact]
        public void Enum_slots_prefer_variables_declared_with_the_same_enum()
        {
            const string code = "enum Mode { quiet, loud }\nfn f(v: Mode) => $v\ngood: Mode = quiet\nwrong: string = 'other'\necho @f($‸)";
            Assert.Equal(new[] { "good", "wrong" }, Choices(code, "good", "wrong"));
        }
    }
}
