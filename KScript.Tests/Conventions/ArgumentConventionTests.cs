using System.Text.RegularExpressions;

using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Tests.Conventions;

/// <summary>
/// The names of statements (elements) and their arguments (attributes), checked across every package so a new one
/// can't bring a second spelling back. The rules are written down in docs/language.md, "Names of statements and
/// arguments". Old spellings are in <see cref="Retired"/>, which says the new name.
/// </summary>
public class ArgumentConventionTests
{
    /// <summary>
    /// The packages whose elements describe a thing in a tree (a window, a route table, a document), so they are
    /// PascalCase. Every other package's elements are actions or declarations, in lower snake_case. Add a package
    /// here only when its elements build a tree of things.
    /// </summary>
    private static readonly HashSet<string> ThingPackages = new(StringComparer.Ordinal) { "KScript.UI", "KScript.Http", "KScript.Document" };

    /// <summary>
    /// Arguments allowed to be called src or file. Empty on purpose: the file an element reads or acts on is
    /// <c>path</c>, and where a result goes is <c>to</c>. (src stays on real HTML tags in pages, such as
    /// &lt;img src&gt;, which are not KScript elements.)
    /// </summary>
    private static readonly HashSet<string> FileNameExceptions = new(StringComparer.Ordinal);

    private static readonly Regex Snake = new("^[a-z][a-z0-9_]*$");
    private static readonly Regex Pascal = new("^[A-Z][A-Za-z0-9]*$");

    /// <summary>Every element of every package, and the ones the catalog only plans.</summary>
    public static IEnumerable<(string Package, ElementInfo Element)> Elements()
    {
        var seen = new HashSet<ElementInfo>();
        foreach (KScriptPackage package in PackageRegistry.Default.LoadAll())
        {
            foreach (ElementDefinition e in package.Elements.Values)
            {
                if (seen.Add(e.Info)) yield return (package.Name, e.Info);
            }
        }
        foreach (ElementInfo e in LanguageCatalog.Default.Elements)
        {
            if (seen.Add(e)) yield return (e.Package ?? "(planned)", e);
        }
    }

    private static IEnumerable<(string Where, ElementInfo Element, AttributeInfo Attribute)> Attributes() =>
        Elements().SelectMany(x => x.Element.Attributes.Select(a => ($"{x.Package} {x.Element.Name}({a.Name})", x.Element, a)));

    private static void None(IEnumerable<string> offenders, string rule)
    {
        List<string> list = offenders.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(list.Count == 0, rule + "\n  " + string.Join("\n  ", list));
    }

    [Fact]
    public void EveryPackageIsWalked()
    {
        List<string> packages = Elements().Select(x => x.Package).Distinct().ToList();
        foreach (string name in new[] { "KScript.Core", "KScript.Terminal", "KScript.Test", "KScript.UI", "KScript.Http", "KScript.Document", "KScript.AI", "KScript.Queue", "KScript.Web", "KScript.Style", "KScript.Data" })
        {
            Assert.Contains(name, packages);
        }
        Assert.True(Elements().Count() > 100);
    }

    [Fact]
    public void ArgumentNamesAreSnakeCase() =>
        None(Attributes().Where(x => !Snake.IsMatch(x.Attribute.Name)).Select(x => x.Where),
            "Argument names are lower snake_case (max_tokens, on_click), never kebab-case or camelCase:");

    [Fact]
    public void ElementNamesArePascalCaseOrSnakeCaseWithNoHyphen() =>
        None(Elements().Where(x => !Snake.IsMatch(x.Element.Name) && !Pascal.IsMatch(x.Element.Name)).Select(x => $"{x.Package} {x.Element.Name}"),
            "Element names are PascalCase (things) or lower snake_case (actions), with no hyphen:");

    [Fact]
    public void ThingsArePascalCaseAndActionsAreSnakeCase() =>
        None(Elements().Where(x => x.Package.StartsWith("KScript.", StringComparison.Ordinal) && Pascal.IsMatch(x.Element.Name) != ThingPackages.Contains(x.Package))
                .Select(x => $"{x.Package} {x.Element.Name}"),
            "Elements of " + string.Join(", ", ThingPackages) + " describe things and are PascalCase; every other package's are actions in lower snake_case:");

    [Fact]
    public void ToIsADestinationAndNeverTheVariableThatReceivesAResult() =>
        None(Attributes().Where(x => x.Attribute.Name == "to" && x.Attribute.Kind is AttributeKind.DeclareVariable or AttributeKind.DeclareVariables).Select(x => x.Where),
            "to: is where something goes (a file, a URL, a controller). The variable that receives a result is into:");

    [Fact]
    public void IntoIsAlwaysTheVariableThatReceivesAResult() =>
        None(Attributes().Where(x => x.Attribute.Name == "into" && !(x.Attribute.Kind is AttributeKind.DeclareVariable or AttributeKind.DeclareVariables)).Select(x => x.Where),
            "into: always names the variable that receives the result. Call a destination to: and a page element target:");

    [Fact]
    public void AResultIsNeverDeclaredByAnArgumentWithAnotherName()
    {
        // The names an argument that declares a variable may have: what is declared (name), a result (into), a
        // caught error or import prefix (as), loop variables (each, at).
        var allowed = new HashSet<string> { "name", "into", "as", "each", "at" };
        None(Attributes().Where(x => x.Attribute.Kind is AttributeKind.DeclareVariable or AttributeKind.DeclareVariables && !allowed.Contains(x.Attribute.Name)).Select(x => x.Where),
            "An argument that declares a variable is called name, into, as, each or at:");
    }

    [Fact]
    public void TheFileAnElementReadsIsCalledPath() =>
        None(Attributes().Where(x => x.Attribute.Name is "src" or "file" or "filename" or "file_name" or "source" or "sample" && !FileNameExceptions.Contains($"{x.Element.Name}.{x.Attribute.Name}"))
                .Select(x => x.Where),
            "The file an element reads or acts on is path:, and where its result goes is to:");

    [Fact]
    public void APositionIsCalledAt() =>
        None(Attributes().Where(x => x.Attribute.Name is "index" or "position" or "idx" && x.Attribute.Kind is AttributeKind.DeclareVariable or AttributeKind.DeclareVariables).Select(x => x.Where),
            "The variable for a position is at:, as in for item at i in $list:");

    [Fact]
    public void ATestHasNoLoopOfItsOwn()
    {
        ElementInfo test = LanguageCatalog.Default.GetElement("test");
        Assert.Null(test.GetAttribute("each"));
        Assert.Null(test.GetAttribute("in"));
        Assert.Null(test.GetAttribute("index"));
    }

    [Fact]
    public void FunctionArgumentsAreDocumentedWithAt() =>
        None(Attributes().Where(x => CodeArguments.TakesFunctions(x.Element.Name, x.Attribute) && !Regex.IsMatch(x.Attribute.Description, @"@[a-z_]")).Select(x => x.Where),
            "An argument that names a function shows the @name form in its description (fn: @send):");

    [Fact]
    public void RenamedElementsPointAtRealElements()
    {
        foreach (var kv in Retired.RenamedElements)
        {
            Assert.True(kv.Value.Renamed);
            Assert.True(Elements().All(x => x.Element.Name != kv.Key), $"{kv.Key} is still an element.");
            Assert.True(Elements().Any(x => x.Element.Name == kv.Value.Rename), $"{kv.Key} renames to {kv.Value.Rename}, which isn't an element.");
        }
    }

    [Fact]
    public void RenamedArgumentsPointAtRealArguments()
    {
        foreach (var row in Retired.RenamedAttributes)
        {
            List<ElementInfo> elements = Elements().Where(x => x.Element.Name == row.Element).Select(x => x.Element).ToList();
            Assert.True(elements.Count > 0, $"{row.Element} isn't an element.");
            Assert.True(elements.All(e => e.GetAttribute(row.Old) == null), $"{row.Element} still has {row.Old}.");
            if (row.New != null)
            {
                Assert.True(elements.Any(e => e.GetAttribute(row.New) != null), $"{row.Element} has no {row.New}.");
            }
            RetiredInfo told = Retired.RenamedAttribute(elements.First(e => row.New == null || e.GetAttribute(row.New) != null), row.Old);
            Assert.NotNull(told);
            Assert.Equal(row.New, told.Rename);
        }
    }

    [Theory]
    [InlineData("picker", "must-exist", "must_exist")]
    [InlineData("llm", "max-tokens", "max_tokens")]
    [InlineData("Button", "on-click", "on_click")]
    [InlineData("expect", "not-equals", "not_equals")]
    [InlineData("config", "export-env", "export_env")]
    [InlineData("Section", "new-page", "new_page")]
    public void AHyphenatedArgumentSaysItsSnakeCaseName(string element, string old, string now)
    {
        RetiredInfo told = Retired.RenamedAttribute(LanguageCatalog.Default.GetElement(element), old);
        Assert.Equal(now, told.Rename);
        Assert.Contains("is now " + now, told.Message("'" + old + "'"));
    }

    #region The old spellings at run time and in the editor

    [Theory]
    [InlineData("<import package=\"KScript.Terminal\" /><input to=\"x\">?</input>", "<input>: 'to' is now into.")]
    [InlineData("<import package=\"KScript.Terminal\" /><picker into=\"x\" must-exist=\"no\">?</picker>", "'must-exist' is now must_exist")]
    [InlineData("<import src=\"lib.ks.xml\" />", "'src' is now path.")]
    [InlineData("<marray name=\"x\"></marray>", "<marray> is now collection.")]
    [InlineData("<for each=\"x\" index=\"i\" in=\"[1]\"></for>", "'index' is now at.")]
    public void TheRuntimeSaysTheNewName(string body, string message)
    {
        KScriptException ex = ScriptRunner.Fails(body);
        Assert.Equal("Retired", ex.Type);
        Assert.Contains(message, ex.Message);
    }

    private static List<Diagnostic> Check(string code) =>
        Analyzer.Analyze(code, Path.Combine(Path.GetTempPath(), "conventions", "a.kscript")).Diagnostics.ToList();

    [Theory]
    [InlineData("import KScript.Terminal\ninput(to: name) \"Name? \"\necho $name", "import KScript.Terminal\ninput(into: name) \"Name? \"\necho $name")]
    [InlineData("import KScript.Terminal\npicker(into: f, must-exist: false) \"File? \"\necho $f", "import KScript.Terminal\npicker(into: f, must_exist: false) \"File? \"\necho $f")]
    [InlineData("import KScript.Terminal\nmenu(into: c, options: ['a'], index: i) \"Pick\"\necho \"$c $i\"", "import KScript.Terminal\nmenu(into: c, options: ['a'], at: i) \"Pick\"\necho \"$c $i\"")]
    [InlineData("import KScript.UI\nKScript.Window(title: \"t\") {\n  n = 0\n  Button(on-click: { n += 1 }) \"{$n}\"\n}", "import KScript.UI\nKScript.Window(title: \"t\") {\n  n = 0\n  Button(on_click: { n += 1 }) \"{$n}\"\n}")]
    public void CheckFixRenamesAnOldArgument(string code, string expected)
    {
        Assert.Contains(Check(code), d => d.Code == DiagnosticCodes.RetiredAttribute && d.Severity == DiagnosticSeverity.Error && d.Message.Contains("is now"));
        (string text, int count) = Fixer.Apply(code, Path.Combine(Path.GetTempPath(), "conventions", "a.kscript"));
        Assert.Equal(expected, text);
        Assert.Equal(1, count);
        Assert.DoesNotContain(Check(text), d => d.Code == DiagnosticCodes.RetiredAttribute);
    }

    [Fact]
    public void CheckFixRenamesAnOldElement()
    {
        string path = Path.Combine(Path.GetTempPath(), "conventions", "a.test.kscript");
        const string code = "x = 1\nbefore-each {\n  x = 2\n}\ntest(name: \"t\") {\n  expect-error(type: Validation) {\n    throw Validation(\"no {$x}\")\n  }\n}\n";
        List<Diagnostic> found = Analyzer.Analyze(code, path).Diagnostics.Where(d => d.Code == DiagnosticCodes.RetiredElement).ToList();
        Assert.Equal(new[] { "before-each is now before_each.", "expect-error is now expect_error." }, found.Select(d => d.Message));
        Assert.Equal(code.Replace("before-each", "before_each").Replace("expect-error", "expect_error"), Fixer.Apply(code, path).Text);
    }

    #endregion

    #region KS6012: a function is @name wherever one is named

    private static List<Diagnostic> OneSpelling(string code) => Check(code).Where(d => d.Code == DiagnosticCodes.OneSpelling).ToList();

    [Theory]
    [InlineData("fn job() {\n  echo \"hi\"\n}\nspawn(fn: job)", "fn job() {\n  echo \"hi\"\n}\nspawn(fn: @job)")]
    [InlineData("fn note(entry: map) {\n  echo $entry.message\n}\nlog_provider(fn: note, level: error)", "fn note(entry: map) {\n  echo $entry.message\n}\nlog_provider(fn: @note, level: error)")]
    [InlineData("import KScript.AI\nfn a() => 'a'\nfn b() => 'b'\nllm(into: r, tools: [a, @b]) \"Hi\"\necho $r", "import KScript.AI\nfn a() => 'a'\nfn b() => 'b'\nllm(into: r, tools: [@a, @b]) \"Hi\"\necho $r")]
    [InlineData("import KScript.Http\nKScript.Routes {\n  fn show() => 'x'\n  Get(path: \"/x\", to: show)\n}", "import KScript.Http\nKScript.Routes {\n  fn show() => 'x'\n  Get(path: \"/x\", to: @show)\n}")]
    public void ABareFunctionNameIsFlaggedAndFixed(string code, string expected)
    {
        Diagnostic d = Assert.Single(OneSpelling(code));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("a function is referred to as @name", d.Message);
        Assert.Contains(DiagnosticCodes.OneSpelling, Fixer.Rules);
        string text = Fixer.Apply(code, Path.Combine(Path.GetTempPath(), "conventions", "a.kscript")).Text;
        Assert.Equal(expected, text);
        Assert.Empty(OneSpelling(text));
        Assert.DoesNotContain(Check(text), x => x.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    // Middleware lists hold built-ins, groups and functions under one kind of name, and an import lists names to bring in.
    [InlineData("import KScript.Http\nKScript.Routes {\n  fn check(request, next) => $next($request)\n  Middleware(use: [log, check, throttle:30])\n  Get(path: \"/x\", view: \"x.html\", middleware: check)\n}")]
    // A controller is the name a script is imported as, not a function.
    [InlineData("import KScript.Http\nKScript.Routes {\n  Resource(path: \"/people\", to: people)\n}")]
    public void NamesThatAreNotFunctionsStayBare(string code) => Assert.Empty(OneSpelling(code));

    [Fact]
    public void XmlKeepsBareNamesAndTheConverterAddsAndStripsTheAt()
    {
        const string xml = "<kscript>\n  <fn name=\"job\" value=\"1\" />\n  <spawn fn=\"job\" />\n</kscript>\n";
        Assert.DoesNotContain(Analyzer.Analyze(xml, "a.ks.xml").Diagnostics, d => d.Code == DiagnosticCodes.OneSpelling);
        string code = ScriptConverter.Convert(xml, "a.ks.xml").Text;
        Assert.Contains("spawn(fn: @job)", code);
        Assert.Contains("<spawn fn=\"job\" />", ScriptConverter.Convert(code, "a.kscript").Text);

        const string tools = "<kscript>\n  <import package=\"KScript.AI\" />\n  <fn name=\"a\" value=\"1\" />\n  <llm into=\"r\" tools=\"a\">Hi</llm>\n</kscript>\n";
        string toolsCode = ScriptConverter.Convert(tools, "a.ks.xml").Text;
        Assert.Contains("tools: @a", toolsCode);
        Assert.Contains("tools=\"a\"", ScriptConverter.Convert(toolsCode, "a.kscript").Text);
    }

    [Theory]
    [InlineData("<fn name=\"job\" params=\"n\"><echo>ran $n</echo></fn><spawn fn=\"@job\" args=\"1\" />")]
    [InlineData("<fn name=\"job\" params=\"n\"><echo>ran $n</echo></fn><call fn=\"@job\" args=\"1\" />")]
    public void TheRuntimeTakesTheAtForm(string body) => Assert.Equal("ran 1\n", ScriptRunner.RunBody(body));

    #endregion

    #region A test inside a for loop

    [Fact]
    public void ATestInsideAForLoopIsOneTestPerItem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ks-conventions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "loop.test.kscript");
            File.WriteAllText(file, "cases = [{ n: 1, want: 2 }, { n: 2, want: 5 }]\nfor case at i in $cases {\n  test(name: \"case {$i} doubles {$case.n}\") {\n    expect(value: $case.n * 2, equals: $case.want)\n  }\n}\n");
            var result = KScript.Testing.TestRunner.RunFile(file, new KScript.Testing.TestRunSettings { BaseDirectory = dir });
            Assert.Null(result.Error);
            Assert.Equal(new[] { "case 0 doubles 1", "case 1 doubles 2" }, result.Tests.Select(t => t.Name));
            Assert.Equal(new[] { KScript.Testing.TestOutcome.Passed, KScript.Testing.TestOutcome.Failed }, result.Tests.Select(t => t.Outcome));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    #endregion
}
