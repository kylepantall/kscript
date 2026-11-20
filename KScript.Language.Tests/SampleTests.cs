using KScript.Language.Analysis;

namespace KScript.Language.Tests
{
    /// <summary>The samples (taken from the KScript 2 proposal) must analyze with no problems at all.</summary>
    public class SampleTests
    {
        public static IEnumerable<object[]> Samples() =>
            Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "samples"), "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".xml") || f.EndsWith(".kscript") || f.EndsWith(".html") || f.EndsWith(".md"))
                .Select(f => new object[] { Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "samples"), f) });

        [Theory]
        [MemberData(nameof(Samples))]
        public void SampleHasNoDiagnostics(string relative)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "samples", relative);
            DocumentAnalysis analysis = Analyzer.Analyze(File.ReadAllText(path), path);
            Assert.True(analysis.Diagnostics.Count == 0,
                string.Join("\n", analysis.Diagnostics.Select(d => $"{analysis.Source.GetPosition(d.Span.Start)} {d}")));
        }
    }
}
