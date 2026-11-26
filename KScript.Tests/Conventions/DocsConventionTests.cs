using System.Text.RegularExpressions;

using KScript.Language.Analysis;
using KScript.LanguageServer;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Tests.Conventions;

/// <summary>
/// What a reader copies is current. Every <c>kscript</c> and <c>xml</c> example in the guides and READMEs, and every
/// sample project, checks clean under the default standard (kscript:recommended): no errors and no style warnings
/// (KS6001 to KS6014), so a rename or a new rule that the docs and samples don't follow fails here.
/// What <c>kscript make</c> writes is held to the same rule by <see cref="TemplateConventionTests"/>.
/// <para>
/// An example that shows a mistake on purpose is marked in its fence: <c>```kscript wrong</c>.
/// </para>
/// </summary>
public sealed class DocsConventionTests
{
    private static readonly string Root = CliConventionTests.RepoRoot();

    [Theory]
    [InlineData("README.md", true)]
    [InlineData("docs/language.md", false)]
    public void Package_indexes_match_the_built_in_registry(string file, bool includesCore)
    {
        string guide = File.ReadAllText(Path.Combine(Root, file));
        var names = Regex.Matches(guide, @"^\|\s*`(KScript\.\w+)`\s*\|", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value);
        var expected = KScript.Runtime.PackageRegistry.BuiltIn.Select(p => p.Name)
            .Where(name => includesCore || name != "KScript.Core");
        Assert.Equal(expected.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
    }

    /// <summary>The Markdown files a reader learns from.</summary>
    public static IEnumerable<object[]> Guides()
    {
        yield return new object[] { "README.md" };
        yield return new object[] { "editors/vscode/README.md" };
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Root, "docs"), "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return new object[] { "docs/" + Path.GetFileName(file) };
        }
    }

    internal sealed record Fence(string Language, string[] Words, string Code, int Line);

    private static readonly Regex FenceBlock = new Regex(@"^([ \t]*)```(kscript|xml)([^\n]*)\n(.*?)^\1```[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline);

    /// <summary>Each ```kscript and ```xml block of a Markdown file, with the words after the language and the line its code starts on.</summary>
    internal static IEnumerable<Fence> Fences(string markdown)
    {
        markdown = markdown.Replace("\r\n", "\n");
        foreach (Match m in FenceBlock.Matches(markdown))
        {
            int line = markdown.Take(m.Groups[4].Index).Count(c => c == '\n') + 1;
            yield return new Fence(m.Groups[2].Value, m.Groups[3].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries), m.Groups[4].Value, line);
        }
    }

    /// <summary>The word in a fence that says its code is wrong on purpose.</summary>
    public const string WrongMarker = "wrong";

    /// <summary>True for an xml block that holds KScript (not a page of HTML, a .csproj or plain data).</summary>
    private static bool IsScriptXml(string body) =>
        Regex.IsMatch(body, @"<(kscript|let|set|echo|do|if|for|fn|import|try|type|enum|error|match|while|const)\b");

    /// <summary>
    /// What an example can't help, because it is a few lines out of a longer script: a name the lines before it
    /// declared, a file of the reader's, and declarations nothing in the example uses.
    /// </summary>
    private static bool FromBeingAnExcerpt(Diagnostic d, string body, string line)
    {
        if (DiagnosticCodes.IsUnnecessary(d.Code)) return true;
        switch (d.Code)
        {
            case DiagnosticCodes.TypeMismatch:
            case DiagnosticCodes.BareIdentifier when !Regex.IsMatch(d.Message, "^'[A-Z]"):
                // The line uses a type (User->with(...), sku: Sku = abc-1234) that an earlier example or the project declares.
                return Regex.Matches(line, @"\b([A-Z]\w*)(?=->|\s*=)").Any(m => !Regex.IsMatch(body, @"\b(type|model|enum)\s+" + m.Groups[1].Value + @"\b"));
            case DiagnosticCodes.InvalidTypedValue:
                // A value that is wrong on purpose, inside the try block that catches it.
                return InsideTry(body, d.Span.Start);
            case DiagnosticCodes.UndefinedVariable:
            case DiagnosticCodes.UndefinedFunction:
            case DiagnosticCodes.OneTypePerFile:
            case DiagnosticCodes.MissingImport:
                return true;
            case DiagnosticCodes.InvalidAttributeValue:
                return d.Message.StartsWith("File not found", StringComparison.Ordinal) || Regex.IsMatch(d.Message, "^Unknown type '[A-Z]");
            case DiagnosticCodes.UnknownCommand:
                return Regex.IsMatch(d.Message, @"^Unknown (function|command) ");
            case DiagnosticCodes.UnknownElement:
                // A component drawn as a statement (NoteCard(title: "Milk")) comes from a file the example imports.
                return Regex.IsMatch(d.Message, "^Unknown statement '[A-Z]") && body.Contains("import \"");
            case DiagnosticCodes.UnknownErrorType:
                return d.Message.StartsWith("Unknown error type", StringComparison.Ordinal);
            case DiagnosticCodes.BareIdentifier:
                // A type or job declared in an earlier example: Repo(...), SendWelcome(...).
                return Regex.IsMatch(d.Message, "^'[A-Z]");
            default:
                return false;
        }
    }

    /// <summary>True when the position is inside the braces of a <c>try { }</c> (or inside a <c>&lt;try&gt;</c> element).</summary>
    private static bool InsideTry(string body, int position)
    {
        foreach (Match open in Regex.Matches(body, @"\btry\s*\{"))
        {
            int depth = 0;
            for (int i = open.Index + open.Length - 1; i < body.Length; i++)
            {
                if (body[i] == '{') depth++;
                else if (body[i] == '}' && --depth == 0)
                {
                    if (position > open.Index && position < i) return true;
                    break;
                }
            }
        }
        return Regex.Matches(body, @"<try\b.*?(<catch\b|</try>)", RegexOptions.Singleline).Any(m => position > m.Index && position < m.Index + m.Length);
    }

    internal static List<string> ProblemsIn(Fence fence, string file)
    {
        bool code = fence.Language == "kscript";
        string body = fence.Code;
        int offset = 0;
        if (!code)
        {
            if (!IsScriptXml(body)) return new List<string>();
            if (!Regex.IsMatch(body.TrimStart(), @"^(<!--.*?-->\s*)*<(kscript|KScript\.)", RegexOptions.Singleline))
            {
                body = "<kscript>\n" + body + "\n</kscript>";
                offset = 1;
            }
        }
        var source = new SourceText(body);
        string[] lines = body.Split('\n');
        string LineOf(Diagnostic d) => lines[Math.Clamp(source.GetPosition(Math.Min(d.Span.Start, body.Length)).Line - 1, 0, lines.Length - 1)];
        string ruleFile = KScript.Language.Rules.DocumentRules.FileName;
        string path = code && fence.Words.Contains(ruleFile) ? ruleFile : code ? "example.kscript" : "example.ks.xml";
        return Analyzer.Analyze(body, path, EmptyFileSystem.Instance).Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => !FromBeingAnExcerpt(d, body, LineOf(d)))
            .OrderBy(d => d.Span.Start)
            .Select(d => $"{file}:{fence.Line + source.GetPosition(Math.Min(d.Span.Start, body.Length)).Line - offset}: {d.Code} {d.Message}")
            .ToList();
    }

    [Fact]
    public void The_guard_sees_the_examples()
    {
        var fences = Guides().SelectMany(g => Fences(File.ReadAllText(Path.Combine(Root, (string)g[0])))).ToList();
        Assert.True(fences.Count(f => f.Language == "kscript") > 120, "Fewer kscript examples than expected were found in the guides.");
        Assert.Contains(fences, f => f.Words.Contains(WrongMarker));
        // Every guide the docs site shows is one this test reads.
        Assert.All(KScript.Docs.DocsSite.GuideFiles, f => Assert.Contains(Guides(), g => (string)g[0] == "docs/" + f));
    }

    [Theory]
    [MemberData(nameof(Guides))]
    public void Every_example_in_a_guide_checks_clean(string file)
    {
        var problems = new List<string>();
        var notWrong = new List<string>();
        foreach (Fence fence in Fences(File.ReadAllText(Path.Combine(Root, file))))
        {
            List<string> found = ProblemsIn(fence, file);
            if (fence.Words.Contains(WrongMarker))
            {
                // The mark is kept honest: an example marked wrong that checks clean has lost its point.
                if (found.Count == 0) notWrong.Add($"{file}:{fence.Line}: marked \"{WrongMarker}\", but nothing is wrong with it.");
                continue;
            }
            problems.AddRange(found);
        }
        Assert.True(problems.Count == 0,
            "Examples in the guides check clean under kscript:recommended (mark one that shows a mistake on purpose as ```kscript wrong):\n" + string.Join("\n", problems));
        Assert.True(notWrong.Count == 0, string.Join("\n", notWrong));
    }

    [Fact]
    public void Every_example_in_the_reference_checks_clean()
    {
        // The reference is generated from each package's examples; the guides' examples are read from their files above.
        var problems = new List<string>();
        int seen = 0;
        foreach ((string page, string format, string code, _) in DocsTests.Examples())
        {
            if (page.StartsWith("/guide", StringComparison.Ordinal)) continue;
            seen++;
            problems.AddRange(ProblemsIn(new Fence(format == "kscript" ? "kscript" : "xml", Array.Empty<string>(), code, 1), page + " (" + format + ")")
                .Select(p => p + "\n    " + code.TrimEnd().Replace("\n", "\n    ")));
        }
        Assert.True(seen > 300, "Fewer reference examples than expected were found.");
        Assert.True(problems.Count == 0, "Examples in the reference check clean under kscript:recommended:\n" + string.Join("\n", problems.Distinct()));
    }

    [Fact]
    public void The_what_changed_page_lists_every_rename()
    {
        // docs/upgrading.md is written by hand from the tables the errors are made from: a new row needs a line there.
        string page = File.ReadAllText(Path.Combine(Root, "docs", "upgrading.md"));
        var missing = new List<string>();
        void Needs(string old, string what) { if (!page.Contains(old, StringComparison.Ordinal)) missing.Add($"{what}: {old}"); }

        foreach (var flag in KScript.CliFlags.RetiredFlags.Keys) Needs(flag.Flag, "kscript " + flag.Command);
        foreach (string setting in KScript.Projects.ProjectConfig.RetiredSettings.Keys) Needs(setting.Split('.').Last(), "kscript.json");
        foreach (string type in KScript.Runtime.ErrorTypes.Renamed.Keys) Needs(type, "error type");
        foreach (string element in KScript.Runtime.Retired.RenamedElements.Keys) Needs("`" + element + "`", "statement");
        foreach (var row in KScript.Runtime.Retired.RenamedAttributes) Needs("`" + row.Element + "`", "argument " + row.Old + " of");
        foreach (var method in KScript.Runtime.RetiredMembers.Methods) Needs("->" + method.Value.Name + "(", "method of " + method.Key.Split(':')[0]);
        Assert.True(missing.Count == 0, "docs/upgrading.md doesn't mention:\n" + string.Join("\n", missing));
    }

    /// <summary>The sample projects: each folder under samples/, and the sample site.</summary>
    public static IEnumerable<object[]> Samples()
    {
        foreach (string folder in Directory.GetDirectories(Path.Combine(Root, "samples")).OrderBy(d => d, StringComparer.Ordinal))
        {
            yield return new object[] { "samples/" + Path.GetFileName(folder) };
        }
        if (Directory.Exists(Path.Combine(Root, "Example Sites", "team"))) yield return new object[] { "Example Sites/team" };
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_checks_clean(string folder)
    {
        var output = new StringWriter { NewLine = "\n" };
        int code = CheckCommand.Run(new[] { Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar)) }, output, warningsAsErrors: true);
        var problems = output.ToString().Split('\n').Where(l => Regex.IsMatch(l, @"\): (error|warning) KS\d+")).ToList();
        Assert.True(code == 0 && problems.Count == 0, $"kscript check --warnings-as-errors {folder}:\n{string.Join("\n", problems.Take(80))}\n{output.ToString().Split('\n').LastOrDefault(l => l.Trim().Length > 0)}");
    }

    [Fact]
    public void Samples_are_kscript_first_with_one_labelled_XML_set()
    {
        // Every sample is written in .kscript. samples/xml holds the one set kept in XML, for readers who use that form.
        var xml = Directory.EnumerateFiles(Path.Combine(Root, "samples"), "*.ks.xml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("samples/xml/", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(xml.Count == 0, "XML samples live in samples/xml; the others are .kscript:\n" + string.Join("\n", xml));
        Assert.True(Directory.EnumerateFiles(Path.Combine(Root, "samples", "xml"), "*.ks.xml", SearchOption.AllDirectories).Any(), "samples/xml lost its XML samples.");
        Assert.True(File.Exists(Path.Combine(Root, "samples", "xml", "README.md")), "samples/xml says what it is in a README.md.");
    }
}
