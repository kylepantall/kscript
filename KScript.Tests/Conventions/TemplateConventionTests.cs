using System.Text.RegularExpressions;

using KScript.LanguageServer;
using KScript.Scaffolding;

namespace KScript.Tests.Conventions;

/// <summary>
/// What <c>kscript make</c> writes follows the language's own standard: every generator and every project template,
/// in .kscript and in XML, gives files with no warnings under kscript:recommended (so none of the style rules
/// KS6001 to KS6014 fire on a new file), and every file is named in kebab-case.
/// A template that falls behind a rename or a new rule fails here.
/// </summary>
public sealed class TemplateConventionTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-templates").FullName;
    private readonly string cwd = Directory.GetCurrentDirectory();

    public void Dispose()
    {
        Directory.SetCurrentDirectory(cwd);
        KScript.Runtime.WorkingFolder.Remember();
        Directory.Delete(dir, true);
    }

    private static (int Code, string Out, string Err) Make(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        int code = MakeCommand.Run(args.Concat(new[] { "-n" }).ToArray(), o, e);
        return (code, o.ToString(), e.ToString());
    }

    private static (int Code, string Output) Check(string folder)
    {
        var o = new StringWriter { NewLine = "\n" };
        int code = CheckCommand.Run(new[] { folder }, o, warningsAsErrors: true);
        return (code, o.ToString());
    }

    /// <summary>The words of a command line, with "quoted words" kept whole.</summary>
    private static string[] Words(string command) =>
        Regex.Matches(command, @"""((?:[^""\\]|\\.)*)""|(\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value.Replace("\\\"", "\"") : m.Groups[2].Value).ToArray();

    /// <summary>
    /// Every way a generator is documented to be used: its plain form (kscript make type name) and each example in
    /// its header, as the words after "kscript make".
    /// </summary>
    private static IEnumerable<string[]> Commands()
    {
        foreach (StubInfo template in Scaffolder.BuiltInTemplates())
        {
            yield return new[] { template.Type, Scaffolder.Example(template.Type) };
            foreach (string example in template.Examples)
            {
                string[] words = Words(example);
                Assert.True(words.Length >= 4 && words[0] == "kscript" && words[1] == "make", $"The {template.Type} template's example should start with kscript make: {example}");
                yield return words.Skip(2).ToArray();
            }
        }
    }

    [Fact]
    public void Every_make_type_has_examples_to_run()
    {
        var types = Scaffolder.BuiltInTemplates().Select(t => t.Type).ToList();
        Assert.True(types.Count >= 25, "Fewer built-in templates than expected: the embedded resources are missing.");
        Assert.Equal(types.OrderBy(t => t), Scaffolder.Categories.SelectMany(c => c.Types).OrderBy(t => t));
        Assert.All(Scaffolder.BuiltInTemplates(), t => Assert.True(t.Examples.Count > 0, $"kscript make {t.Type} has no example: line, so nothing checks what its options write."));
    }

    [Theory]
    [InlineData("kscript")]
    [InlineData("xml")]
    public void Every_generator_writes_files_with_no_warnings(string syntax)
    {
        // A project to make the files in: its kscript.json has no lint section, so the default standard applies.
        Assert.Equal(0, Make("project", "app", "--bare", "--syntax", syntax, "--dir", dir).Code);
        string app = Path.Combine(dir, "app");
        // Files the examples read.
        Directory.CreateDirectory(Path.Combine(app, "data"));
        File.WriteAllText(Path.Combine(app, "data", "order.json"), "{ \"id\": 7, \"customer\": \"Ada\", \"total\": 12.5, \"paid\": true }");
        File.WriteAllText(Path.Combine(app, "theme.json"), "{ \"colors\": { \"primary\": \"#3355ff\" } }");

        var failed = new List<string>();
        int made = 0;
        foreach (string[] command in Commands().DistinctBy(c => string.Join(" ", c)))
        {
            var result = Make(command.Concat(new[] { "--syntax", syntax, "--dir", app, "--force" }).ToArray());
            if (result.Code != 0) failed.Add($"kscript make {string.Join(" ", command)}: {result.Err.Trim()}");
            else made++;
        }
        foreach (string[] auth in new[] { new[] { "auth" }, new[] { "auth", "--api" } })
        {
            var result = Make(auth.Concat(new[] { "--syntax", syntax, "--dir", app, "--force" }).ToArray());
            if (result.Code != 0) failed.Add($"kscript make {string.Join(" ", auth)}: {result.Err.Trim()}");
        }
        Assert.True(failed.Count == 0, "These generators failed:\n" + string.Join("\n", failed));
        Assert.True(made > 60, $"Only {made} generator commands ran.");

        var (_, output) = Check(app);
        var problems = ProblemLines(output).Where(l => !NotUsedYet.Any(code => l.Contains($"warning {code}:", StringComparison.Ordinal))).ToList();
        Assert.True(problems.Count == 0, $"kscript check --warnings-as-errors on what kscript make wrote ({syntax}):\n{string.Join("\n", problems)}");
        Assert.Matches(@"\d\d files checked: 0 errors", output);
        AssertKebabCase(app);
    }

    public static IEnumerable<object[]> Projects()
    {
        // Each built-in template with its option mixes, as .github/scripts/check-templates.sh makes them.
        string[][] cases =
        {
            new[] { "web" },
            new[] { "web", "--no-desktop", "--no-http" },
            new[] { "bare", "--bare" },
            new[] { "bare", "--bare", "--web", "--api", "--desktop" },
            new[] { "api", "--template", "api" },
            new[] { "api", "--template", "api", "--db", "none", "--no-auth", "--no-cache" },
            new[] { "api", "--template", "api", "--db", "postgres", "--docker" },
            new[] { "fullstack", "--template", "fullstack" },
            new[] { "fullstack", "--template", "fullstack", "--auth" },
            new[] { "fullstack", "--template", "fullstack", "--db", "none", "--no-cache" },
        };
        foreach (string[] c in cases)
        {
            foreach (string syntax in new[] { "kscript", "xml" }) yield return new object[] { c[0], string.Join(" ", c.Skip(1)), syntax };
        }
    }

    [Fact]
    public void Every_project_template_is_covered()
    {
        Assert.Equal(ProjectTemplate.BuiltInNames().OrderBy(n => n), Projects().Select(p => (string)p[0]).Distinct().OrderBy(n => n));
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Every_project_template_writes_files_with_no_warnings(string template, string options, string syntax)
    {
        string[] args = new[] { "project", "app" }.Concat(options.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Concat(new[] { "--syntax", syntax, "--dir", dir }).ToArray();
        var made = Make(args);
        Assert.True(made.Code == 0, $"kscript make {string.Join(" ", args)}: {made.Err}");
        string app = Path.Combine(dir, "app");

        var (code, output) = Check(app);
        Assert.True(code == 0, $"kscript check --warnings-as-errors on a new {template} project ({options} {syntax}):\n{Problems(output)}");
        AssertKebabCase(app);
    }

    /// <summary>The lines of a check that name a problem.</summary>
    private static string Problems(string output) =>
        string.Join("\n", ProblemLines(output).Take(60));

    private static IEnumerable<string> ProblemLines(string output) =>
        output.Split('\n').Where(l => Regex.IsMatch(l, @"\): (error|warning) KS\d+"));

    /// <summary>
    /// The only warnings a file made on its own may have: nothing calls a new library's functions or reads its
    /// constants until the project uses them. (A project template uses everything it ships, so it gets no pass.)
    /// </summary>
    private static readonly string[] NotUsedYet = { KScript.Syntax.DiagnosticCodes.UnusedFunction, KScript.Syntax.DiagnosticCodes.UnusedVariable };

    // ---- file names ----

    /// <summary>Names every tool or convention fixes, whatever the naming style.</summary>
    private static readonly HashSet<string> FixedNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "AGENTS.md", "CLAUDE.md", "Dockerfile", "Makefile", "README.md",
    };

    /// <summary>
    /// True for a kebab-case file or folder name: lower-case words joined by hyphens, with its extensions. A leading
    /// _ or . (a hidden or special folder) and a page's [parameter] are part of how sites name things, not of the style.
    /// </summary>
    internal static bool IsKebab(string name)
    {
        if (FixedNames.Contains(name)) return true;
        string stem = name.TrimStart('_', '.');
        return Regex.IsMatch(stem, @"^(\[[a-z][a-z0-9]*\]|[a-z0-9]+)(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)*$");
    }

    private static void AssertKebabCase(string folder)
    {
        var wrong = Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/'))
            .Where(p => !p.Split('/').Contains(".kscript"))
            // A locale file is named after its locale code, which has its own spelling (en-GB).
            .Where(p => !Regex.IsMatch(p, @"(^|/)locales/[a-z]{2,3}(-[A-Za-z0-9]+)*\.yaml$"))
            .Where(p => !IsKebab(p.Substring(p.LastIndexOf('/') + 1)))
            .OrderBy(p => p).ToList();
        Assert.True(wrong.Count == 0, "kscript make names files in kebab-case (request-id.kscript):\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Kebab_case_is_what_it_says()
    {
        foreach (string good in new[] { "request-id.kscript", "notes-client.test.kscript", "site.ks.css", "_layout.html", ".gitignore", "[id].html", "config.test.yaml", "kscript-template.json", "kscript.json.stub", "Makefile" })
            Assert.True(IsKebab(good), good);
        foreach (string bad in new[] { "request_id.kscript", "notes_client.test.kscript", "NewPerson.kscript", "siteNav.html", "Hero.html" })
            Assert.False(IsKebab(bad), bad);
    }

    [Fact]
    public void Every_file_under_Scaffolding_is_named_in_kebab_case()
    {
        string scaffolding = Path.Combine(CliConventionTests.RepoRoot(), "KScript.Cli", "Scaffolding");
        var files = Directory.EnumerateFiles(scaffolding, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".cs", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(scaffolding, f).Replace('\\', '/')).ToList();
        Assert.True(files.Count > 100, "Fewer template files than expected under KScript.Cli/Scaffolding.");
        // Each part of the path: Projects/, Templates/ and Auth/ are the three source folders, named as C# folders are.
        var wrong = files.Where(f => f.Split('/').Skip(1).Any(part => !IsKebab(part))).ToList();
        Assert.True(wrong.Count == 0, "Template files are named in kebab-case:\n" + string.Join("\n", wrong));

        // And nothing in them imports or names a snake_case script file.
        var mentions = new List<string>();
        foreach (string file in files)
        {
            string text = File.ReadAllText(Path.Combine(scaffolding, file));
            foreach (Match m in Regex.Matches(text, @"[\w\-/.]*[a-z0-9]_[a-z0-9][\w\-]*(\{\{ ext \}\}|\.kscript|\.ks\.xml)"))
            {
                // A placeholder such as {{ snake }}{{ ext }} is the template's own business; a literal name is not.
                if (!m.Value.Contains("}}_", StringComparison.Ordinal)) mentions.Add($"{file}: {m.Value}");
            }
        }
        Assert.True(mentions.Count == 0, "Templates name script files in kebab-case:\n" + string.Join("\n", mentions));
    }
}
