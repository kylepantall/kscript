using System.Text.RegularExpressions;

using KScript.Language.Catalog;

namespace KScript.Language.Tests
{
    /// <summary>
    /// Keeps the language server in step with the <c>kscript serve</c> page engine (KScript.Web/Pages). Pages run their
    /// expressions on the KScript.Core interpreter, so methods and commands come from packages; the elements the
    /// renderer handles itself are a switch statement, so these tests read its source: every <c>case "name":</c>
    /// must be an element the catalog knows works in pages.
    /// </summary>
    public class PageEngineContractTests
    {
        private static readonly LanguageCatalog Catalog = LanguageCatalog.Default;

        private static string PagesFolder()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "KScript.Web", "Pages");
                if (File.Exists(Path.Combine(dir, "KScript.sln")) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>The <c>case "x":</c> labels of each top-level method in a source file, by method name.</summary>
        internal static Dictionary<string, List<string>> CasesByMethod(string source)
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>();
            string current = null;
            foreach (string line in source.Split('\n'))
            {
                Match method = Regex.Match(line, @"^\s{8}(?:public|private|internal)\s+static\s+\S+\s+(\w+)\(");
                Match cls = Regex.Match(line, @"^\s{4}(?:public|private|internal)\s+(?:static\s+)?(?:sealed\s+)?class\s+(\w+)");
                if (cls.Success)
                {
                    current = null;
                    result[cls.Groups[1].Value] = new List<string>();
                    current = cls.Groups[1].Value;
                }
                if (method.Success)
                {
                    current = current?.Split('.')[0] + "." + method.Groups[1].Value;
                    result[current] = new List<string>();
                }
                if (current == null)
                {
                    continue;
                }
                foreach (Match c in Regex.Matches(line, "case \"([a-z_][a-z0-9_-]*)\":"))
                {
                    result[current].Add(c.Groups[1].Value);
                }
            }
            return result;
        }

        public static IEnumerable<object[]> EngineElements()
        {
            string dir = PagesFolder();
            if (dir == null)
            {
                yield break;
            }
            Dictionary<string, List<string>> cases = CasesByMethod(File.ReadAllText(Path.Combine(dir, "PageRenderer.cs")));
            foreach (string name in cases.Where(c => c.Key.StartsWith("PageRenderer.RenderElement", StringComparison.Ordinal) || c.Key == "PageRenderer.RenderNodes")
                         .SelectMany(c => c.Value).Distinct())
            {
                yield return new object[] { name };
            }
        }

        [Theory]
        [MemberData(nameof(EngineElements))]
        public void EveryPageElementIsInTheCatalog(string name)
        {
            ElementInfo e = Catalog.GetElement(name);
            Assert.True(e != null, $"The page engine renders <{name}> but the catalog doesn't know it.");
            Assert.True(e.Context.HasFlag(KScript.Metadata.ElementContext.Page), $"<{name}> works in pages, so its Context must include Page.");
        }

        [Fact]
        public void TheScannerFindsTheEngine()
        {
            if (PagesFolder() == null)
            {
                return;
            }
            Assert.True(EngineElements().Count() > 10);
        }
    }
}
