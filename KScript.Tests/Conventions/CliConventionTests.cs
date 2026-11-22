using System.Text.Json;
using System.Text.RegularExpressions;

using KScript.Projects;
using KScript.Syntax.Markup;

namespace KScript.Tests.Conventions;

/// <summary>
/// The spelling rules of the command line and of kscript.json, checked over everything that is declared, so a new
/// command, flag or setting can't bring a second spelling back:
/// flags are kebab-case and one flag has one meaning (--output is how results print, --syntax is which script
/// syntax); settings are snake_case; scripts are .kscript or .ks.xml; exit codes mean the same on every command.
/// </summary>
public sealed class CliConventionTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("kscript-cli-rules").FullName;
    private readonly TextWriter stdout = Console.Out;
    private readonly TextWriter stderr = Console.Error;

    public void Dispose()
    {
        Console.SetOut(stdout);
        Console.SetError(stderr);
        Directory.Delete(dir, true);
    }

    internal static string RepoRoot()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "KScript.sln"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("KScript.sln was not found above the tests.");
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private (int Code, string Out, string Err) Kscript(params string[] args)
    {
        var o = new StringWriter { NewLine = "\n" };
        var e = new StringWriter { NewLine = "\n" };
        Console.SetOut(o);
        Console.SetError(e);
        try
        {
            int code = new CommandLine().Run(args);
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }

    // ---- flags ----

    /// <summary>Every --flag named in a command's options, usage, details and examples.</summary>
    private static IEnumerable<(string Command, string Flag)> Flags()
    {
        foreach (CliCommand c in CliReference.Commands)
        {
            string text = string.Join("\n", new[] { c.Usage, c.Summary, c.Details, c.Example }.Concat(c.Options.SelectMany(o => new[] { o.Name, o.Description })).Where(t => t != null));
            foreach (Match m in Regex.Matches(text, @"(?<![\w-])--[A-Za-z][\w-]*")) yield return (c.Name, m.Value);
        }
        foreach (CliOption o in CliReference.GlobalOptions)
        {
            foreach (Match m in Regex.Matches(o.Name, @"(?<![\w-])--[A-Za-z][\w-]*")) yield return ("(every command)", m.Value);
        }
    }

    [Fact]
    public void Every_flag_is_kebab_case()
    {
        var flags = Flags().Distinct().ToList();
        Assert.True(flags.Count > 40, "CliReference lists fewer flags than expected: the walk is broken.");
        var wrong = flags.Where(f => !Regex.IsMatch(f.Flag, @"^--[a-z][a-z0-9]*(-[a-z0-9]+)*$")).Select(f => $"kscript {f.Command}: {f.Flag}").ToList();
        Assert.True(wrong.Count == 0, "Flags are lower-case words joined by hyphens (--warnings-as-errors):\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void No_command_uses_format_as_a_flag_and_each_flag_has_one_meaning()
    {
        var flags = Flags().Distinct().ToList();
        Assert.DoesNotContain(flags, f => f.Flag == "--format");
        // The old spellings of the two meanings are gone from the reference too.
        Assert.DoesNotContain(flags, f => f.Flag is "--warnaserror" or "--yaml" or "--json");
        Assert.DoesNotContain(flags, f => f.Flag == "--to" && f.Command == "convert");

        // --output is how results are printed, --syntax is which script syntax: the words after them say so.
        foreach (CliCommand c in CliReference.Commands)
        {
            foreach (CliOption o in c.Options.Where(o => o.Name.StartsWith("--output ", StringComparison.Ordinal)))
                Assert.Matches(@"^--output (text|json|yaml)(\|(text|json|yaml))*$", o.Name);
            foreach (CliOption o in c.Options.Where(o => o.Name.StartsWith("--syntax ", StringComparison.Ordinal)))
                Assert.Equal("--syntax kscript|xml", o.Name);
        }
        Assert.Equal(new[] { "check", "db", "openapi", "queue" }, CliReference.Commands.Where(c => c.Options.Any(o => o.Name.StartsWith("--output", StringComparison.Ordinal))).Select(c => c.Name).OrderBy(n => n));
        Assert.Equal(new[] { "convert", "db", "docs", "make", "mcp" }, CliReference.Commands.Where(c => c.Options.Any(o => o.Name.StartsWith("--syntax", StringComparison.Ordinal))).Select(c => c.Name).OrderBy(n => n));
    }

    [Fact]
    public void A_retired_flag_never_comes_back_as_an_option()
    {
        foreach (var ((command, flag), now) in CliFlags.RetiredFlags.Select(kv => (kv.Key, kv.Value)))
        {
            CliCommand c = CliReference.Find(command);
            Assert.True(c != null, $"CliFlags.RetiredFlags names the command '{command}', which CliReference doesn't have.");
            Assert.DoesNotContain(c.Options, o => o.Name.Split(' ', ',')[0] == flag);
            Assert.DoesNotContain(flag + " ", c.Usage + " ");
            Assert.Contains(c.Options, o => o.Name.StartsWith(now.Split(' ')[0], StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(new[] { "check", "--format", "json" }, "--output text|json")]
    [InlineData(new[] { "check", "--format=json" }, "--output text|json")]
    [InlineData(new[] { "check", "--warnaserror" }, "--warnings-as-errors")]
    [InlineData(new[] { "queue", "status", "--format", "json" }, "--output text|json")]
    [InlineData(new[] { "openapi", "--yaml" }, "--output yaml")]
    [InlineData(new[] { "openapi", "--json" }, "--output json")]
    [InlineData(new[] { "make", "test", "cart", "--format", "xml" }, "--syntax kscript|xml")]
    [InlineData(new[] { "docs", "--format", "xml" }, "--syntax kscript|xml")]
    [InlineData(new[] { "convert", "a.kscript", "--to", "xml" }, "--syntax kscript|xml")]
    public void An_old_flag_is_an_error_that_names_the_new_one(string[] args, string now)
    {
        var (code, output, err) = Kscript(args);
        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("was renamed. Use " + now, output + err);
    }

    [Fact]
    public void The_help_names_scripts_as_kscript_files()
    {
        // .ks.xml is only named where the XML form itself is the subject (convert, and the lists of both extensions).
        foreach (CliCommand c in CliReference.Commands.Where(c => c.Example != null && c.Name != "convert"))
        {
            Assert.DoesNotContain(".ks.xml", c.Example);
            Assert.DoesNotMatch(@"\.(ks|xml)\b", c.Example.Replace(".kscript", string.Empty).Replace("openapi.yaml", string.Empty));
        }
        string help = Kscript("--help").Out;
        Assert.DoesNotContain("routes.ks.xml", help);
        Assert.Contains("Exit codes:", help);
    }

    [Fact]
    public void Check_fix_is_in_the_reference_and_the_docs()
    {
        Assert.Contains(CliReference.Find("check").Options, o => o.Name == "--fix");
        Assert.Contains("kscript check --fix", File.ReadAllText(Path.Combine(RepoRoot(), "docs", "standards.md")));
    }

    // ---- kscript.json keys ----

    private static IEnumerable<(string Path, string Key)> SchemaKeys(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object) yield break;
        if (schema.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in props.EnumerateObject())
            {
                yield return (path, p.Name);
                foreach (var inner in SchemaKeys(p.Value, path + p.Name + ".")) yield return inner;
            }
        }
        foreach (string holder in new[] { "additionalProperties", "items" })
        {
            if (schema.TryGetProperty(holder, out JsonElement e))
                foreach (var inner in SchemaKeys(e, path)) yield return inner;
        }
        foreach (string list in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (schema.TryGetProperty(list, out JsonElement e) && e.ValueKind == JsonValueKind.Array)
                foreach (JsonElement option in e.EnumerateArray())
                    foreach (var inner in SchemaKeys(option, path)) yield return inner;
        }
    }

    // The rule is the engine's: the lists of settings the code declares go through Conventions.Settings, which refuses a name that breaks it.
    private static bool IsSnake(string key) => KScript.Runtime.Conventions.IsSettingName(key);

    [Theory]
    [InlineData("kscript.schema.json")]
    [InlineData("kscript-standard.schema.json")]
    public void Every_setting_in_the_schemas_is_snake_case(string file)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "editors", "vscode", "schemas", file)));
        var keys = SchemaKeys(doc.RootElement, string.Empty).ToList();
        Assert.True(keys.Count > 20, "The schema walk found too few settings.");
        var wrong = keys.Where(k => !IsSnake(k.Key)).Select(k => k.Path + k.Key).ToList();
        Assert.True(wrong.Count == 0, "Settings in kscript.json are snake_case (print_width):\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Every_format_rule_and_setting_the_code_reads_is_snake_case()
    {
        Assert.All(FormatSettings.RuleNames, n => Assert.True(IsSnake(n), n));
        Assert.All(AuthSettings.SettingNames, n => Assert.True(IsSnake(n.Substring(n.LastIndexOf('.') + 1)), n));
        Assert.All(QueueSettings.SettingNames, n => Assert.True(IsSnake(n.Substring(n.LastIndexOf('.') + 1)), n));
        Assert.All(ProjectConfig.DefaultPaths.Keys, n => Assert.True(IsSnake(n), n));

        // Every key ProjectConfig switches on: `case "name":` in the file that reads kscript.json.
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "KScript.Core", "Projects", "ProjectConfig.cs"));
        var cases = Regex.Matches(source, @"case ""([^""]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(cases.Count > 60, "ProjectConfig reads fewer settings than expected: the scan is broken.");
        Assert.All(cases, n => Assert.True(IsSnake(n), $"ProjectConfig reads \"{n}\", which is not snake_case."));

        // The built-in standards are written with the same keys the schema allows.
        foreach (string json in Standards.BuiltIn.Values)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.All(Keys(doc.RootElement), n => Assert.True(IsSnake(n) || Regex.IsMatch(n, @"^KS\d*\*?$"), n));
        }
    }

    private static IEnumerable<string> Keys(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) yield break;
        foreach (JsonProperty p in e.EnumerateObject())
        {
            yield return p.Name;
            foreach (string inner in Keys(p.Value)) yield return inner;
        }
    }

    [Fact]
    public void Every_kscript_json_the_repo_ships_uses_only_settings_that_are_read()
    {
        string root = RepoRoot();
        var files = new List<string>();
        foreach (string folder in new[] { "samples", "Example Sites", Path.Combine("KScript.Cli", "Scaffolding") })
        {
            string full = Path.Combine(root, folder);
            if (!Directory.Exists(full)) continue;
            files.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) is "kscript.json" or "kscript.json.stub" || f.EndsWith("kscript-standard.json", StringComparison.Ordinal))
                .Where(f => !f.Replace('\\', '/').Contains("/bin/") && !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/node_modules/")));
        }
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            foreach (string old in ProjectConfig.RetiredSettings.Keys.Where(k => k != "make.format").Select(k => k.Substring(k.LastIndexOf('.') + 1)))
                Assert.False(Regex.IsMatch(text, "\"" + old + "\"\\s*:"), $"{file} still has the old setting \"{old}\".");
            Assert.False(Regex.IsMatch(text, @"""make""\s*:\s*\{[^}]*""format"""), $"{file} still has make.format (now make.syntax).");
        }
    }

    [Theory]
    [InlineData("""{ "lint": { "warningsAsErrors": true } }""", "'lint.warningsAsErrors' was renamed to 'lint.warnings_as_errors'")]
    [InlineData("""{ "format": { "printWidth": 100 } }""", "'format.printWidth' was renamed to 'format.print_width'")]
    [InlineData("""{ "format": { "maxBlankLines": 2 } }""", "'format.maxBlankLines' was renamed to 'format.max_blank_lines'")]
    [InlineData("""{ "format": { "endOfLine": "lf" } }""", "'format.endOfLine' was renamed to 'format.end_of_line'")]
    [InlineData("""{ "format": { "tabWidth": 4 } }""", "'format.tabWidth' was renamed to 'format.tab_width'")]
    [InlineData("""{ "format": { "rules": { "wrapStrings": false } } }""", "'format.rules.wrapStrings' was renamed to 'format.rules.wrap_strings'")]
    [InlineData("""{ "make": { "format": "xml" } }""", "'make.format' was renamed to 'make.syntax'")]
    public void An_old_setting_is_an_error_that_names_the_new_one(string json, string message)
    {
        var ex = Assert.Throws<ProjectConfigException>(() => ProjectConfig.Parse(json, Path.Combine(dir, ProjectConfig.FileName)));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Every_retired_setting_points_at_one_that_is_read()
    {
        foreach (var (old, now) in ProjectConfig.RetiredSettings.Select(kv => (kv.Key, kv.Value)))
        {
            Assert.False(IsSnake(old.Substring(old.LastIndexOf('.') + 1)) && old != "make.format", $"{old} is already snake_case: why is it retired?");
            Assert.True(IsSnake(now.Substring(now.LastIndexOf('.') + 1)), now);
            // Setting the new name must not be refused or warned about as unknown.
            string[] parts = now.Split('.');
            string value = parts[0] == "make" ? "\"xml\"" : now.Contains(".rules.") || now.EndsWith("errors", StringComparison.Ordinal) ? "true" : now.EndsWith("end_of_line", StringComparison.Ordinal) ? "\"lf\"" : now.EndsWith("print_width", StringComparison.Ordinal) ? "40" : "2";
            string json = parts.Length == 3 ? $"{{ \"{parts[0]}\": {{ \"{parts[1]}\": {{ \"{parts[2]}\": {value} }} }} }}" : $"{{ \"{parts[0]}\": {{ \"{parts[1]}\": {value} }} }}";
            ProjectConfig config = ProjectConfig.Parse(json, Path.Combine(dir, ProjectConfig.FileName));
            Assert.Empty(config.Warnings);
        }
    }

    // ---- script file names ----

    [Fact]
    public void Scripts_have_one_extension_per_form()
    {
        Assert.Equal(new[] { ".kscript", ".ks.xml" }, ScriptFiles.Extensions);
        Assert.True(ScriptFiles.IsScript("a/hello.kscript"));
        Assert.True(ScriptFiles.IsScript("a/hello.ks.xml"));
        Assert.False(ScriptFiles.IsScript("a/hello.ks"));
        Assert.False(ScriptFiles.IsScript("a/hello.xml"));
        Assert.True(TestSettings.IsTestFileName("cart.test.kscript"));
        Assert.True(TestSettings.IsTestFileName("cart.test.ks.xml"));
        Assert.False(TestSettings.IsTestFileName("cart.test.xml"));
        Assert.False(TestSettings.IsTestFileName("cart.test.ks"));
        Assert.Equal("old.ks.xml", ScriptFiles.RenamedFrom("old.ks"));
        Assert.Equal("old.ks.xml", ScriptFiles.RenamedFrom("old.xml"));
        Assert.Null(ScriptFiles.RenamedFrom("fine.kscript"));

        foreach (string[] include in new[] { LintSettings.DefaultInclude, FormatSettings.DefaultInclude, TestSettings.DefaultInclude })
        {
            Assert.DoesNotContain(include, g => g.EndsWith(".ks", StringComparison.Ordinal) || (g.EndsWith(".xml", StringComparison.Ordinal) && !g.EndsWith(".ks.xml", StringComparison.Ordinal)));
            Assert.Contains(include, g => g.EndsWith(".kscript", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void No_tool_looks_for_scripts_by_an_old_extension()
    {
        // ".ks" and ".test.xml" as string literals are how the old extensions were detected. The one place that still
        // names an old extension says why.
        var allowed = new Dictionary<string, string>
        {
            ["KScript.Syntax/Markup/ScriptFiles.cs"] = "names .ks to tell people what to rename the file to",
        };
        string root = RepoRoot();
        var found = new List<string>();
        foreach (string project in Directory.GetDirectories(root, "KScript*").Where(d => !d.EndsWith("Tests", StringComparison.Ordinal)))
        {
            foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (relative.Contains("/bin/") || relative.Contains("/obj/") || allowed.ContainsKey(relative)) continue;
                string text = File.ReadAllText(file);
                if (Regex.IsMatch(text, @"""(\*\*/)?\*?\.ks""|\.test\.xml|LooksLikeScript")) found.Add(relative);
            }
        }
        Assert.True(found.Count == 0, "Scripts are .kscript or .ks.xml (ScriptFiles); these files still look for .ks or .test.xml:\n" + string.Join("\n", found));

        using JsonDocument package = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "editors", "vscode", "package.json")));
        var extensions = package.RootElement.GetProperty("contributes").GetProperty("languages").EnumerateArray()
            .Where(l => l.GetProperty("id").GetString() is "kscript" or "kscript-code")
            .SelectMany(l => l.GetProperty("extensions").EnumerateArray().Select(x => x.GetString())).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { ".ks.xml", ".kscript" }, extensions);
    }

    [Fact]
    public void An_old_script_extension_is_told_its_new_name()
    {
        string old = Write("hello.xml", "<kscript><echo>hi</echo></kscript>");
        var run = Kscript(old);
        Assert.Equal(ExitCodes.Usage, run.Code);
        Assert.Contains("Rename it to hello.ks.xml", run.Err);

        // check, fmt and test no longer pick the file up from a folder.
        Assert.Equal(ExitCodes.Usage, Kscript("check", dir).Code);
        Assert.Equal(ExitCodes.Usage, Kscript("convert", old).Code);
    }

    // ---- make and exit codes ----

    [Fact]
    public void Make_has_one_spelling()
    {
        var colon = Kscript("make:component", "Hero", "--dir", dir);
        Assert.Equal(ExitCodes.Usage, colon.Code);
        Assert.Contains("kscript make component Hero", colon.Err);
        Assert.False(Directory.Exists(Path.Combine(dir, "_components")));
        Assert.Equal(ExitCodes.Usage, Kscript("help", "make:").Code);
    }

    [Fact]
    public void Exit_codes_mean_the_same_on_every_command()
    {
        Assert.Equal((0, 1, 2, 3), (ExitCodes.Ok, ExitCodes.Failed, ExitCodes.Usage, ExitCodes.ScriptError));
        Assert.Equal(ExitCodes.All.Select(c => c.Code), ExitCodes.All.Select(c => c.Code).OrderBy(c => c));

        string good = Write("ok/good.kscript", "echo \"hi\"\n");
        string warns = Write("warn/warns.kscript", "fn shout(text) {\n  return $text->upper()\n}\necho @shout(\"a\")\n");
        string broken = Write("bad/broken.kscript", "echo $missing\n");
        string fails = Write("fail/boom.kscript", "throw \"no\"\n");
        string failingTest = Write("t/sum.test.kscript", "test \"sum\" {\n  expect { 1 + 1 } to_be 3\n}\n");
        Write("cfg/kscript.json", "{ not json");
        string inBadProject = Write("cfg/main.kscript", "echo \"hi\"\n");

        // 0: it worked.
        Assert.Equal(ExitCodes.Ok, Kscript(good).Code);
        Assert.Equal(ExitCodes.Ok, Kscript("check", good).Code);

        // 1: the command ran and found problems.
        Assert.Equal(ExitCodes.Failed, Kscript("check", broken).Code);
        Assert.Equal(ExitCodes.Ok, Kscript("check", warns).Code);
        Assert.Equal(ExitCodes.Failed, Kscript("check", warns, "--warnings-as-errors").Code);
        Assert.Equal(ExitCodes.Failed, Kscript("test", failingTest).Code);

        // 3: a script stopped with an error.
        Assert.Equal(ExitCodes.ScriptError, Kscript(fails).Code);

        // 2: the command line or the settings are wrong, whichever command it is.
        var usage = new List<string[]>
        {
            Array.Empty<string>(),
            new[] { "--nope" },
            new[] { "nope.kscript" },
            new[] { "help", "nope" },
            new[] { "check", "--nope" },
            new[] { "check", "--output", "xml" },
            new[] { "check", Path.Combine(dir, "missing") },
            new[] { "fmt", "--nope" },
            new[] { "fmt", Path.Combine(dir, "missing") },
            new[] { "test", "--nope" },
            new[] { "test", "--timeout" },
            new[] { "test", Path.Combine(dir, "missing") },
            new[] { "serve", "--port" },
            new[] { "serve", "--host" },
            new[] { "routes", Path.Combine(dir, "missing") },
            new[] { "openapi", "--nope" },
            new[] { "openapi", "--output", "xml" },
            new[] { "openapi", "--out" },
            new[] { "convert" },
            new[] { "convert", "--nope" },
            new[] { "convert", "--syntax", "json", good },
            new[] { "convert", Path.Combine(dir, "missing.kscript") },
            new[] { "css", "--nope" },
            new[] { "css", Path.Combine(dir, "missing.ks.css") },
            new[] { "queue" },
            new[] { "queue", "status", "--output", "xml" },
            new[] { "queue", "nope" },
            new[] { "worker", "--nope" },
            new[] { "worker", "--concurrency", "many" },
            new[] { "docs", "--nope" },
            new[] { "docs", "--syntax", "yaml" },
            new[] { "compile", "--nope" },
            new[] { "agents", "--nope" },
            new[] { "watch", "--nope" },
            new[] { "ide", "nope" },
            new[] { "update", "--version" },
            new[] { "make", "nope", "thing", "--dir", dir },
            new[] { "make", "test", "--dir", Path.Combine(dir, "missing") },
            new[] { "make", "test", "cart", "--syntax", "yaml", "--dir", dir },
            new[] { "--env" },
            // a kscript.json that can't be read
            new[] { inBadProject },
            new[] { "check", inBadProject },
            new[] { "fmt", inBadProject },
            new[] { "test", Path.Combine(dir, "cfg") },
            new[] { "routes", Path.Combine(dir, "cfg") },
            new[] { "serve", Path.Combine(dir, "cfg") },
        };
        var wrong = new List<string>();
        foreach (string[] args in usage)
        {
            var (code, _, _) = Kscript(args);
            if (code != ExitCodes.Usage) wrong.Add($"kscript {string.Join(" ", args)} -> {code}");
        }
        Assert.True(wrong.Count == 0, "A bad command line or bad settings exit with 2:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void The_exit_codes_are_documented()
    {
        string docs = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "projects.md"));
        Assert.Contains("## Exit codes", docs);
        foreach (var (code, _) in ExitCodes.All) Assert.Contains($"| {code} |", docs);
        string help = Kscript("--help").Out;
        foreach (var (code, meaning) in ExitCodes.All) Assert.Contains(meaning.Substring(0, Math.Min(18, meaning.Length)), help);
    }
}
