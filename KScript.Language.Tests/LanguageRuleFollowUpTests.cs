using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>The language's one-spelling rules, where they first missed a case.</summary>
    public class LanguageRuleFollowUpTests
    {
        private static string Folder => AnalyzerCoverageTests.Folder;

        private static DocumentAnalysis Code(string text, IFileSystem files = null, string file = "app.kscript") =>
            TestHelpers.WithoutUnused(Analyzer.Analyze(text, Path.Combine(Folder, file), files ?? EmptyFileSystem.Instance));

        [Fact]
        public void AnImportedTypeIsNamedBareToo()
        {
            var files = new AnalyzerCoverageTests.Files(("models/product.kscript", "import KScript.Data\n\nmodel Product {\n  name: string\n}\n"));
            DocumentAnalysis a = Code("""
                import KScript.Data
                import "models/product.kscript"

                echo $Product->count()
                for p in $Product->get() {
                  echo $p.name
                }
                kind = Product
                echo $kind
                """, files);
            List<Diagnostic> found = a.Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeForm).ToList();
            Assert.Equal(2, found.Count);
            Assert.All(found, d => Assert.Equal("Write Product", d.Fixes[0].Title));
        }

        private static List<string> Names(DocumentAnalysis a) =>
            a.Diagnostics.Where(d => d.Code == DiagnosticCodes.NamingStyle).OrderBy(d => d.Span.Start).Select(d => d.Message.Substring(0, d.Message.IndexOf(':'))).ToList();

        private static string Apply(string text, QuickFix fix) =>
            fix.Changes.OrderByDescending(c => c.Span.Start).Aggregate(text, (t, c) => t.Substring(0, c.Span.Start) + c.NewText + t.Substring(c.Span.End));

        [Fact]
        public void DeclaredNamesFollowTheLanguagesOwn()
        {
            const string code = """
                type repo_info {
                  fullName: string
                  stars: int
                }

                enum colour { red, green }

                error Not_Found {
                  itemId: int
                }

                const MaxItems = 5

                fn showRepo(repoInfo: repo_info, Count: int = 1) {
                  totalStars = $repoInfo.stars * $Count
                  totalStars += $MaxItems
                  for eachOne in [1, 2] {
                    echo $eachOne + $totalStars
                  }
                  echo [1]->map(fn(theItem) => $theItem * 2)
                }

                @showRepo(repo_info(fullName: 'a', stars: 1))
                echo colour.red
                throw Not_Found(itemId: 1)
                """;
            DocumentAnalysis a = Code(code);
            Assert.Equal(new[]
            {
                "Name the type RepoInfo", "Name the field full_name", "Name the enum Colour", "Name the error type NotFound", "Name the field item_id",
                "Name the constant max_items", "Name the function show_repo", "Name the parameter repo_info", "Name the parameter count",
                "Name the variable total_stars", "Name the variable each_one", "Name the parameter the_item",
            }, Names(a));
            Assert.All(a.Diagnostics.Where(d => d.Code == DiagnosticCodes.NamingStyle), d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));

            // A variable is this file's alone, so its fix renames every use. A name other files or callers may use has no fix.
            Diagnostic variable = a.Diagnostics.Single(d => d.Message.StartsWith("Name the variable total_stars", StringComparison.Ordinal));
            string renamed = Apply(code, variable.Fixes[0]);
            Assert.DoesNotContain("totalStars", renamed);
            Assert.Contains("total_stars = $repoInfo.stars * $Count\n  total_stars += $MaxItems", renamed.Replace("\r", string.Empty));
            Assert.Contains("echo $eachOne + $total_stars", renamed);
            Diagnostic loop = a.Diagnostics.Single(d => d.Message.StartsWith("Name the variable each_one", StringComparison.Ordinal));
            Assert.Contains("for each_one in [1, 2] {", Apply(code, loop.Fixes[0]));
            Assert.Contains("echo $each_one + $totalStars", Apply(code, loop.Fixes[0]));
            Assert.All(a.Diagnostics.Where(d => d.Code == DiagnosticCodes.NamingStyle && !d.Message.StartsWith("Name the variable", StringComparison.Ordinal)), d => Assert.True(d.Fixes == null || d.Fixes.Count == 0));
            // It isn't applied by check --fix: a rename is the writer's to make.
            Assert.DoesNotContain(DiagnosticCodes.NamingStyle, Fixer.Rules);
        }

        [Fact]
        public void NamesThatFollowTheRulesAreLeftAlone()
        {
            DocumentAnalysis a = Code("""
                type RepoInfo {
                  full_name: string
                  html5_url?: string
                }

                enum Size { small, large }

                const max_items = 5

                fn _unused_helper(_ignored: int) => 1

                fn show_repo(repo: RepoInfo, count: int = 1) {
                  x2 = $count + $max_items
                  taken = $x2
                  for _ in [1, 2] {
                    echo $repo.full_name + $taken
                  }
                }

                @show_repo(RepoInfo(full_name: 'a'))
                echo Size.small
                """);
            Assert.Empty(Names(a));

            // A variable isn't renamed onto a name the file already has.
            DocumentAnalysis taken = Code("total_stars = 1\ntotalStars = 2\necho $total_stars + $totalStars\n".Replace("\\n", "\n"));
            Diagnostic d = Assert.Single(taken.Diagnostics, x => x.Code == DiagnosticCodes.NamingStyle);
            Assert.True(d.Fixes == null || d.Fixes.Count == 0);
        }

        [Fact]
        public void XmlDeclarationsAreNamedTheSameWay()
        {
            const string xml = """
                <kscript>
                  <type name="repo_info" fields="fullName: string, stars: int" />
                  <let name="totalStars" value="1" />
                  <fn name="showRepo" params="repoInfo: int">
                    <do>totalStars += 1</do>
                    <echo>{ $repoInfo } { $totalStars }</echo>
                  </fn>
                  <for each="eachOne" in="[1, 2]">
                    <echo>$eachOne</echo>
                  </for>
                  <call fn="showRepo" args="1" />
                  <let name="good_name" value="1" />
                  <echo>$good_name</echo>
                </kscript>
                """;
            DocumentAnalysis a = Code(xml, file: "app.ks.xml");
            Assert.Equal(new[]
            {
                "Name the type RepoInfo", "Name the field full_name", "Name the variable total_stars", "Name the function show_repo",
                "Name the parameter repo_info", "Name the variable each_one",
            }, Names(a));
            Diagnostic variable = a.Diagnostics.Single(d => d.Message.StartsWith("Name the variable total_stars", StringComparison.Ordinal));
            string renamed = Apply(xml, variable.Fixes[0]);
            Assert.Contains("<let name=\"total_stars\" value=\"1\" />", renamed);
            Assert.Contains("<do>total_stars += 1</do>", renamed);
            Assert.Contains("{ $total_stars }", renamed);
        }

        [Theory]
        [InlineData("userName", "user_name")]
        [InlineData("UserName", "user_name")]
        [InlineData("HTMLParser", "html_parser")]
        [InlineData("MAX_ITEMS", "max_items")]
        [InlineData("_privateThing", "_private_thing")]
        public void NamesAreSuggestedInSnakeCase(string name, string expected)
        {
            DocumentAnalysis a = Code(name + " = 1\necho $" + name + "\n");
            Assert.Contains("Name the variable " + expected + ":", Assert.Single(a.Diagnostics, d => d.Code == DiagnosticCodes.NamingStyle).Message);
        }
    }
}
