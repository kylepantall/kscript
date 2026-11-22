using System.Text.RegularExpressions;
using KScript.Language.Analysis;
using KScript.Syntax;

namespace KScript.Tests.Conventions;

/// <summary>
/// The one way to write a type, everywhere the project shows KScript: a type's name is bare (Repo, never $Repo), an
/// optional field is marked on its name (age?: int), a model's options are in its brackets, and a package is named
/// only in an import (Window { }, never KScript.Window { }). Checked over every sample, every project template and
/// every example in the guides, so a new one that slips is caught here.
/// </summary>
public sealed class TypeConventionTests
{
    private static readonly string Root = ScriptRunner.RepoFile();

    private static readonly Regex Fence = new Regex(@"^```kscript[^\n]*\n(.*?)^```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline);

    /// <summary>The header a template starts with (--- make ... ---), which isn't KScript.</summary>
    private static readonly Regex TemplateHeader = new Regex(@"\A--- make\n.*?\n---\n", RegexOptions.Singleline);

    /// <summary>A statement that starts with a package's name: KScript.Window { }, KScript.Routes(...) { }.</summary>
    private static readonly Regex PrefixedStatement = new Regex(@"^[ \t]*KScript\.[A-Za-z]\w*[ \t]*(\(|\{|$)", RegexOptions.Multiline);

    /// <summary>$Repo-&gt;..., as: $Repo, $Repo(...): a capitalised name read as a variable. Variables are snake_case.</summary>
    private static readonly Regex DollarType = new Regex(@"(?<![\w$\\])\$[A-Z][a-z]\w*");

    private static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static string Relative(string file) => Path.GetRelativePath(Root, file).Replace('\\', '/');

    private static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n");

    /// <summary>Every .kscript file that is shown as an example of the language: samples and what kscript make writes.</summary>
    private static IEnumerable<string> SamplesAndTemplates() =>
        Files("samples", "*.kscript").Concat(Files(Path.Combine("KScript.Cli", "Scaffolding"), "*.kscript"));

    /// <summary>The ```kscript examples of the guides and the README: (file, line of the example, its text).</summary>
    private static IEnumerable<(string File, int Line, string Code)> GuideExamples()
    {
        foreach (string file in Files("docs", "*.md").Append(Path.Combine(Root, "README.md")))
        {
            string markdown = Read(file);
            foreach (Match m in Fence.Matches(markdown))
            {
                yield return (Relative(file), markdown.Substring(0, m.Groups[1].Index).Count(c => c == '\n') + 1, m.Groups[1].Value);
            }
        }
    }

    private static IEnumerable<string> TypeForms(string where, string code, string path, int firstLine = 1)
    {
        var analysis = Analyzer.Analyze(code, path);
        foreach (Diagnostic d in analysis.Diagnostics.Where(d => d.Code == DiagnosticCodes.TypeForm))
        {
            int line = firstLine + code.Substring(0, Math.Min(d.Span.Start, code.Length)).Count(c => c == '\n');
            yield return $"{where}:{line}: {d.Code} {d.Message}";
        }
    }

    private static IEnumerable<string> Lines(string where, string code, Regex pattern, string what, int firstLine = 1) =>
        pattern.Matches(code).Select(m => $"{where}:{firstLine + code.Substring(0, m.Index).Count(c => c == '\n')}: {what}: {m.Value.Trim()}");

    [Fact]
    public void Samples_write_types_the_one_way()
    {
        var problems = new List<string>();
        int count = 0;
        foreach (string file in Files("samples", "*.kscript"))
        {
            count++;
            problems.AddRange(TypeForms(Relative(file), Read(file), file));
        }
        Assert.True(count > 30, $"Only {count} samples were found.");
        Assert.True(problems.Count == 0, "KS6014 in the samples (kscript check --fix rewrites these):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Project_templates_write_types_the_one_way()
    {
        var problems = new List<string>();
        int count = 0;
        // A template with {{ placeholders }} isn't KScript until it is filled in; MakeGeneratorTests check those once made.
        foreach (string file in Files(Path.Combine("KScript.Cli", "Scaffolding", "Projects"), "*.kscript"))
        {
            string text = TemplateHeader.Replace(Read(file), string.Empty);
            if (text.Contains("{{", StringComparison.Ordinal))
            {
                continue;
            }
            count++;
            problems.AddRange(TypeForms(Relative(file), text, file));
        }
        Assert.True(count > 5, $"Only {count} templates without placeholders were found.");
        Assert.True(problems.Count == 0, "KS6014 in the project templates:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Guide_examples_write_types_the_one_way()
    {
        var problems = new List<string>();
        int count = 0;
        foreach (var (file, line, code) in GuideExamples())
        {
            count++;
            problems.AddRange(TypeForms(file, code, Path.Combine(Root, "example.kscript"), line));
            // A type an example gets from a file of the reader's isn't known here, so $Repo is looked for by its shape too.
            problems.AddRange(Lines(file, code, DollarType, "a type is named bare, without $", line));
        }
        Assert.True(count > 40, $"Only {count} examples were found.");
        Assert.True(problems.Count == 0, "Types written the long way in the guides:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Samples_and_templates_never_read_a_type_with_a_dollar()
    {
        var problems = SamplesAndTemplates().SelectMany(f => Lines(Relative(f), Read(f), DollarType, "a type is named bare, without $")).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_statement_is_written_with_the_package_prefix()
    {
        var problems = SamplesAndTemplates().SelectMany(f => Lines(Relative(f), Read(f), PrefixedStatement, "write the element's own name after import KScript...")).ToList();
        foreach (var (file, line, code) in GuideExamples())
        {
            problems.AddRange(Lines(file, code, PrefixedStatement, "write the element's own name after import KScript...", line));
        }
        Assert.True(problems.Count == 0, "A package is named only in an import (Window { }, not KScript.Window { }):\n" + string.Join("\n", problems));
    }

    /// <summary>The checks above find what they look for: each long form is reported, so a clean run means something.</summary>
    [Theory]
    [InlineData("type Repo {\n  name: string\n}\necho '{}'->json(as: $Repo)\n", "Write Repo, without $")]
    [InlineData("type Repo {\n  license: string?\n}\n", "license?: string")]
    [InlineData("import KScript.Data\n\nmodel User {\n  timestamps: yes\n  name: string\n}\n", "model User(timestamps)")]
    public void The_long_forms_are_reported(string code, string message)
    {
        string problem = Assert.Single(TypeForms("example.kscript", code, Path.Combine(Root, "example.kscript")));
        Assert.Contains(message, problem);
    }

    [Theory]
    [InlineData("KScript.Window(title: \"Hi\") {\n}\n", true)]
    [InlineData("  KScript.Routes {\n", true)]
    [InlineData("import KScript.UI\n\nWindow {\n}\n", false)]
    [InlineData("// KScript.Window is the package's element\nx = 'KScript.UI'\n", false)]
    public void A_prefixed_statement_is_recognised(string code, bool prefixed)
    {
        Assert.Equal(prefixed, PrefixedStatement.IsMatch(code));
    }
}
