using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Language.Analysis;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Tests.Conventions;

/// <summary>
/// What KScript says when something is wrong follows three rules, each checked against the source so it can't drift:
/// every diagnostic code is reported somewhere and documented, in order; every error type the runtime raises is a
/// declared built-in type with a description and a place in the docs; and a .kscript file never gets a message in
/// XML terms (a tag, name="value", "attribute" or "element").
/// </summary>
public sealed class DiagnosticConventionTests
{
    private static readonly string Root = CliConventionTests.RepoRoot();

    /// <summary>The C# source of the product (not the tests), as (path from the repository root, text).</summary>
    private static IEnumerable<(string Path, string Text)> Sources()
    {
        foreach (string project in Directory.GetDirectories(Root, "KScript*").OrderBy(d => d, StringComparer.Ordinal))
        {
            if (project.EndsWith("Tests", StringComparison.Ordinal)) continue;
            foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)) continue;
                yield return (relative, File.ReadAllText(file).Replace("\r\n", "\n"));
            }
        }
    }

    // ---- diagnostic codes (KS1001 ...) ----

    private static List<(string Name, string Code)> Codes() =>
        typeof(DiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .OrderBy(f => f.MetadataToken)
            .Select(f => (f.Name, (string)f.GetRawConstantValue())).ToList();

    [Fact]
    public void Diagnostic_codes_are_unique_and_declared_in_numeric_order()
    {
        var codes = Codes();
        Assert.True(codes.Count >= 50, "DiagnosticCodes has fewer constants than expected.");
        Assert.All(codes, c => Assert.Matches(@"^KS[1-9]\d{3}$", c.Code));
        Assert.Equal(codes.Count, codes.Select(c => c.Code).Distinct().Count());
        Assert.Equal(codes.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal), codes.Select(c => c.Code));
    }

    /// <summary>
    /// Codes declared and documented ahead of the rule that reports them, which is being written separately.
    /// Take a code out of this list when its rule lands; the list should end up empty.
    /// </summary>
    private static readonly string[] ReportedElsewhere = { DiagnosticCodes.NamingStyle };

    [Fact]
    public void Every_diagnostic_code_is_reported_somewhere()
    {
        // A code nobody reports is a promise the docs make and the checker doesn't keep (KS5006 was one).
        string all = string.Join("\n", Sources().Where(s => !s.Path.EndsWith("KScript.Syntax/Diagnostic.cs", StringComparison.Ordinal)).Select(s => s.Text));
        var unused = Codes().Where(c => !ReportedElsewhere.Contains(c.Code)).Where(c => !Regex.IsMatch(all, @"DiagnosticCodes\." + c.Name + @"\b")).Select(c => $"{c.Code} ({c.Name})").ToList();
        Assert.True(unused.Count == 0, "Declared in DiagnosticCodes but never reported: " + string.Join(", ", unused));
    }

    [Fact]
    public void Every_diagnostic_code_is_documented_in_order()
    {
        string docs = File.ReadAllText(Path.Combine(Root, "docs", "errors.md"));
        var documented = Regex.Matches(docs, @"^## (KS\d{4})\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        // The same codes, in the same (numeric) order: nothing undocumented, and no entry for a code that is gone.
        Assert.Equal(Codes().Select(c => c.Code), documented);
    }

    [Fact]
    public void A_planned_element_or_command_is_reported_as_KS5006()
    {
        var diagnostics = Analyzer.Analyze("render into: \"#out\" {\n}\nbox = @dom('#name')\n", "page.kscript", EmptyFileSystem.Instance).Diagnostics;
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.PlannedFeature && d.Message == "render is planned but not built yet, so it can't run.");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.PlannedFeature && d.Message.StartsWith("@dom()", StringComparison.Ordinal));
    }

    // ---- error types ----

    /// <summary>The calls that raise an error with a type: the type is a string argument that is one capitalised word.</summary>
    private static readonly Regex Raises = new Regex(@"new KScriptException\(|\bFail\(|\.Error\(|\bError\(", RegexOptions.Compiled);

    /// <summary>The error type strings the source raises, with where: <c>new KScriptException(message, "Type")</c> and the helpers around it.</summary>
    private static Dictionary<string, List<string>> RaisedTypes()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach ((string path, string text) in Sources())
        {
            foreach (Match raise in Raises.Matches(text))
            {
                foreach (string argument in Arguments(text, raise.Index + raise.Length).Skip(1))
                {
                    Match type = Regex.Match(argument, @"^\s*(?:type:\s*)?""([A-Z][A-Za-z]*)""\s*$");
                    if (!type.Success) continue;
                    if (!found.TryGetValue(type.Groups[1].Value, out List<string> places)) found[type.Groups[1].Value] = places = new List<string>();
                    places.Add($"{path}:{text.Take(raise.Index).Count(c => c == '\n') + 1}");
                }
            }
        }
        return found;
    }

    /// <summary>The top-level arguments of a call, from just after its opening parenthesis.</summary>
    private static List<string> Arguments(string text, int start)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        for (int i = start; i < text.Length && i - start < 3000; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                int end = SourceLiterals.EndOfString(text, i);
                current.Append(text, i, end - i);
                i = end - 1;
                continue;
            }
            if (c == '\'' && i + 2 < text.Length && (text[i + 2] == '\'' || (text[i + 1] == '\\' && i + 3 < text.Length && text[i + 3] == '\'')))
            {
                i += text[i + 1] == '\\' ? 3 : 2;
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            if (c is ')' or ']' or '}')
            {
                if (depth == 0) break;
                depth--;
            }
            if (c == ',' && depth == 0)
            {
                arguments.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        arguments.Add(current.ToString());
        return arguments;
    }

    [Fact]
    public void Every_error_type_the_runtime_raises_is_declared()
    {
        var raised = RaisedTypes();
        Assert.True(raised.Count >= 30, $"Only {raised.Count} raised error types were found: the scan is broken.");
        foreach (string known in new[] { "Math", "Index", "Syntax", "Name", "Query", "Connection", "Reference", "UI" }) Assert.Contains(known, raised.Keys);

        var undeclared = raised.Where(r => !ErrorTypes.BuiltIn.TryGetValue(r.Key, out ErrorType b) || b.Name != r.Key)
            .Select(r => $"{r.Key} ({r.Value[0]})").OrderBy(r => r, StringComparer.Ordinal).ToList();
        Assert.True(undeclared.Count == 0,
            "Raised but not declared in ErrorTypes.cs, so catch can't name it and the editor calls it unknown (KS5011): " + string.Join(", ", undeclared));
    }

    [Fact]
    public void Every_built_in_error_type_has_a_description_and_is_in_the_docs_table()
    {
        string language = File.ReadAllText(Path.Combine(Root, "docs", "language.md"));
        int start = language.IndexOf("The built-in error types", StringComparison.Ordinal);
        Assert.True(start > 0, "docs/language.md lost its table of built-in error types.");
        string table = language.Substring(start, language.IndexOf("\n## ", start, StringComparison.Ordinal) - start);

        Assert.All(ErrorTypes.BuiltIn.Values, t => Assert.False(string.IsNullOrWhiteSpace(t.Description), $"{t.Name} has no description."));
        var missing = ErrorTypes.BuiltIn.Values.Where(t => !table.Contains("`" + t.Name + "`", StringComparison.Ordinal)).Select(t => t.Name).ToList();
        Assert.True(missing.Count == 0, "Built-in error types missing from the table in docs/language.md: " + string.Join(", ", missing));

        // And the table names nothing that isn't one.
        var extra = Regex.Matches(table, @"`([A-Z][A-Za-z]*)`").Select(m => m.Groups[1].Value).Distinct()
            .Where(n => !ErrorTypes.BuiltIn.ContainsKey(n) && !ErrorTypes.Renamed.ContainsKey(n)).ToList();
        Assert.True(extra.Count == 0, "docs/language.md lists error types that aren't built in: " + string.Join(", ", extra));
    }

    [Fact]
    public void No_built_in_error_type_ends_in_Error_and_the_old_names_say_what_they_are_now()
    {
        Assert.DoesNotContain(ErrorTypes.BuiltIn.Values, t => t.Name != ErrorTypes.Root && t.Name.EndsWith("Error", StringComparison.Ordinal));
        Assert.Equal(new[] { "ConnectionError", "QueryError" }, ErrorTypes.Renamed.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(ErrorTypes.Renamed, r => Assert.True(ErrorTypes.BuiltIn.ContainsKey(r.Value) && !ErrorTypes.BuiltIn.ContainsKey(r.Key)));

        // At run time: a catch of the old name would never match, so it stops before the try runs.
        KScriptException caught = Assert.Throws<KScriptException>(() => RunCode("try {\n  echo 'in'\n} catch QueryError as e {\n  echo 'caught'\n}\n"));
        Assert.Equal("Retired", caught.Type);
        Assert.Equal("The error type QueryError was renamed to Query.", caught.Message);
        Assert.Equal("The error type ConnectionError was renamed to Connection.",
            Assert.Throws<KScriptException>(() => RunCode("throw ConnectionError \"down\"\n")).Message);
        // A script's own type of that name is its own business.
        Assert.Equal("mine\n", RunCode("error QueryError { }\ntry {\n  throw QueryError \"x\"\n} catch QueryError {\n  echo 'mine'\n}\n"));

        // In the editor and kscript check: an error with a fix.
        var diagnostic = Assert.Single(Analyzer.Analyze("try {\n  echo 'x'\n} catch QueryError {\n  echo 'y'\n}\n", "a.kscript", EmptyFileSystem.Instance).Diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownErrorType, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("The error type QueryError was renamed to Query.", diagnostic.Message);
        Assert.Equal("Change to Query", Assert.Single(diagnostic.Fixes).Title);
    }

    // ---- wording: a .kscript file is never answered in XML terms ----

    private static string RunCode(string code)
    {
        string dir = Directory.CreateTempSubdirectory("kscript-wording").FullName;
        try
        {
            string file = Path.Combine(dir, "main.kscript");
            File.WriteAllText(file, code);
            var output = new StringWriter { NewLine = "\n" };
            new Interpreter(KScriptHost.ForText(output, string.Empty), options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) }).RunFile(file);
            return output.ToString();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>What a message for a .kscript file must not hold: a tag, name="value", or the XML words.</summary>
    private static readonly Regex XmlTerms = new Regex(@"</?[A-Za-z][\w.\-]*|[\w\]]=""|\battributes?\b|\belements?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static string XmlTermIn(string message) => XmlTerms.Match(message) is { Success: true } m ? m.Value : null;

    /// <summary>
    /// Scripts that fail, written in XML: each is converted to .kscript and run, so one list covers the mistakes a
    /// script can make whatever form it is in. (Body of &lt;kscript&gt;, packages to import.)
    /// </summary>
    private static readonly (string Body, string[] Packages)[] Mistakes =
    {
        ("<locale />", null),
        ("<type name=\"int\" fields=\"a: int\" />", null),
        ("<type name=\"Slug\" fields=\"a: int\" min=\"3\" />", null),
        ("<type name=\"Empty\" />", null),
        ("<type name=\"A\" with=\"Nope\" />", null),
        ("<type name=\"A\" fields=\"a: int, a: int\" />", null),
        ("<type name=\"Slug\" with=\"string\" pattern=\"(\" />", null),
        ("<type name=\"Slug\" with=\"string\" fields=\"a: int\" />", null),
        ("<type name=\"A\" fields=\"a: int\"><echo>x</echo></type>", null),
        ("<let name=\"x\" type=\"Nope\" value=\"1\" />", null),
        ("<enum name=\"Color\" />", null),
        ("<enum name=\"Color\" values=\"red, red\" />", null),
        ("<elif test=\"1\"><echo>x</echo></elif>", null),
        ("<else><echo>x</echo></else>", null),
        ("<match value=\"1\"><echo>x</echo></match>", null),
        ("<match value=\"1\"><case><echo>x</echo></case></match>", null),
        ("<case is=\"1\"><echo>x</echo></case>", null),
        ("<default><echo>x</echo></default>", null),
        ("<set name=\"nope\" value=\"1\" />", null),
        ("<set name=\"nope\" op=\"+\" value=\"1\" />", null),
        ("<do>$nope = 1</do>", null),
        ("<for each=\"a, b, c\" in=\"[1]\"><echo>x</echo></for>", null),
        ("<for each=\"x\" in=\"5\"><echo>x</echo></for>", null),
        ("<call fn=\"nope\" />", null),
        ("<throw value=\"3\" />", null),
        ("<throw value=\"NotFound()\" type=\"X\" />", null),
        ("<throw type=\"Oops\" cause=\"3\" />", null),
        ("<error name=\"NotFound\" />", null),
        ("<error name=\"Mine\" status=\"abc\" />", null),
        ("<error name=\"Mine\" fields=\"message: string\" />", null),
        ("<error name=\"Mine\" extends=\"Nope\" /><throw value=\"Mine()\" />", null),
        ("<marray name=\"m\"><echo>x</echo></marray>", null),
        ("<marray name=\"m\"><item key=\"a\">1</item><item>2</item></marray>", null),
        ("<item>1</item>", null),
        ("<break />", null),
        ("<continue />", null),
        ("<nope />", null),
        ("<echoo>x</echoo>", null),
        ("<echo nope=\"1\">x</echo>", null),
        ("<echo format=\"zzz\">x</echo>", null),
        ("<echo format=\"markdown\">x</echo>", null),
        ("<input />", null),
        ("<do>@nope.thing()</do>", null),
        ("<do>@table([])</do>", null),
        ("<echo>{ $nope }</echo>", null),
        ("<echo>{ nope }</echo>", null),
        ("<echo>{ 1 / 0 }</echo>", null),
        ("<echo>{ @nope() }</echo>", null),
        ("<if test=\"$a = 3\"><echo>x</echo></if>", null),
        ("<validate test=\"1\" />", null),
        ("<normalize value=\"1\" />", null),
        ("<field name=\"a\" />", null),
        ("<type name=\"Even\" with=\"int\"><validate /></type>", null),
        ("<type name=\"Even\" fields=\"a: int\"><normalize value=\"1\" /></type>", null),
        ("<config defaults=\"3\" />", null),
        ("<log-provider fn=\"nope\" />", null),
        ("<log-provider type=\"nope\" />", null),
        ("<extend type=\"date\"><echo>x</echo></extend>", null),
        ("<if test=\"1\"><extend type=\"date\"></extend></if>", null),
        ("<try><echo>x</echo><catch type=\"ConnectionError\"><echo>y</echo></catch></try>", null),
        ("<import src=\"nope.kscript\" />", null),
        ("<import package=\"KScript.Nope\" />", null),
        ("<import package=\"KScript.Math\" as=\"1x\" />", null),
        ("<fn name=\"f\" params=\"a\"><return value=\"$a\" /></fn><echo>{ @f() }</echo>", null),
        ("<return value=\"1\" /><fn name=\"\" />", null),
        ("<expect value=\"1\" />", new[] { "KScript.Test" }),
        ("<test name=\"t\" in=\"[1]\"><echo>x</echo></test>", new[] { "KScript.Test" }),
        ("<expect value=\"1\" equals=\"1\" />", new[] { "KScript.Test" }),
        ("<llm to=\"a\"></llm>", new[] { "KScript.AI" }),
        ("<llm to=\"a\" tools=\"nope\">hi</llm>", new[] { "KScript.AI" }),
        ("<get path=\"/\"><echo>x</echo></get>", new[] { "KScript.Http" }),
        ("<route path=\"/\" />", new[] { "KScript.Http" }),
        ("<job name=\"SendMail\" />", new[] { "KScript.Queue" }),
        ("<model name=\"Person\" />", new[] { "KScript.Data" }),
        ("<download url=\"ftp://x\" to=\"y\" />", new[] { "KScript.Net" }),
        ("<read-file path=\"nope.txt\" to=\"t\" />", new[] { "KScript.IO" }),
        ("<Row />", new[] { "KScript.UI" }),
        ("<style><nope /></style>", new[] { "KScript.Style" }),
    };

    private static string AsCode(string body, string[] packages)
    {
        string xml = "<kscript>" + string.Concat((packages ?? Array.Empty<string>()).Select(p => $"<import package=\"{p}\" />")) + body + "</kscript>";
        return ScriptConverter.ToCode(MarkupParser.Parse(xml, DocumentKind.Script)).Text;
    }

    [Fact]
    public void A_failing_kscript_file_is_never_answered_in_XML_terms()
    {
        var wrong = new List<string>();
        int failed = 0;
        foreach ((string body, string[] packages) in Mistakes)
        {
            string code;
            try
            {
                code = AsCode(body, packages);
            }
            catch (Exception)
            {
                continue;
            }
            try
            {
                RunCode(code);
            }
            catch (KScriptException ex)
            {
                failed++;
                if (XmlTermIn(ex.Message) is string term) wrong.Add($"{code.Trim().Replace("\n", " | ")}\n    {ex.Type}: {ex.Message}\n    ({term})");
                // The caught value carries the same words.
                Assert.Equal(ex.Message, ex.ToValue()["message"]);
            }
            catch (Exception)
            {
                // Not a script error (a missing file, say): nothing to check.
            }
        }
        Assert.True(failed >= 55, $"Only {failed} of the {Mistakes.Length} mistakes failed as .kscript: the list no longer exercises the messages.");
        Assert.True(wrong.Count == 0, "Messages for a .kscript file say \"statement\" and \"argument\" and show its own syntax, never a tag or name=\"value\":\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void The_checker_never_answers_a_kscript_file_in_XML_terms()
    {
        var wrong = new List<string>();
        int reported = 0;
        foreach ((string body, string[] packages) in Mistakes)
        {
            string code;
            try
            {
                code = AsCode(body, packages);
            }
            catch (Exception)
            {
                continue;
            }
            foreach (Diagnostic d in Analyzer.Analyze(code, "main.kscript", EmptyFileSystem.Instance).Diagnostics)
            {
                reported++;
                if (XmlTermIn(d.Message) is string term) wrong.Add($"{code.Trim().Replace("\n", " | ")}\n    {d.Code}: {d.Message}\n    ({term})");
                foreach (QuickFix fix in d.Fixes ?? Array.Empty<QuickFix>())
                {
                    if (XmlTermIn(fix.Title) is string inFix) wrong.Add($"{code.Trim().Replace("\n", " | ")}\n    fix: {fix.Title}\n    ({inFix})");
                }
            }
        }
        Assert.True(reported >= 50, $"Only {reported} problems were reported for the mistakes: the list no longer exercises the checker.");
        Assert.True(wrong.Count == 0, "kscript check on a .kscript file says \"statement\" and \"argument\" and shows its own syntax:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void The_same_mistake_in_an_XML_file_keeps_its_XML_wording()
    {
        KScriptException xml = ScriptRunner.Fails("<echo>{ $nope }</echo>");
        Assert.Equal("$nope is not defined. Declare it with <let name=\"nope\" value=\"...\" />.", xml.Message);
        KScriptException code = Assert.Throws<KScriptException>(() => RunCode("echo $nope\n"));
        Assert.Equal("$nope is not defined. Declare it with nope = ....", code.Message);

        Assert.Equal("<llm> needs a prompt: write it inside the element, such as <llm to=\"answer\">What is KScript?</llm>.", ScriptRunner.Fails("<llm to=\"a\"></llm>", "KScript.AI").Message);
        Assert.Equal("llm needs a prompt: write it inside the statement, such as llm(to: answer) \"What is KScript?\".",
            Assert.Throws<KScriptException>(() => RunCode(AsCode("<llm to=\"a\"></llm>", new[] { "KScript.AI" }))).Message);

        Assert.Equal("Unknown element <echoo>. Did you mean <echo>?", ScriptRunner.Fails("<echoo>x</echoo>").Message);
        Assert.Equal("Unknown statement echoo. Did you mean echo?", Assert.Throws<KScriptException>(() => RunCode(AsCode("<echoo>x</echoo>", null))).Message);
    }

    [Fact]
    public void A_wording_rewords_what_the_source_wrote_and_never_what_was_put_in()
    {
        string name = "throw";
        string users = "<b>bold</b> attribute=\"x\"";
        Wording message = Wording.Of($"<{name}> needs a value attribute, such as <{name} value=\"$err\" />, not '{users}'. An element has attributes.");
        Assert.Equal("<throw> needs a value attribute, such as <throw value=\"$err\" />, not '<b>bold</b> attribute=\"x\"'. An element has attributes.", message.Xml);
        Assert.Equal("throw needs a value argument, such as throw $err, not '<b>bold</b> attribute=\"x\"'. A statement has arguments.", message.Code);

        // name="value" on its own, a tag that only names an argument, and a statement the converter doesn't know.
        Assert.Equal("status: 404 isn't a status.", Wording.Of($"status=\"404\" isn't a status.").Code);
        Assert.Equal("format: wide isn't known.", Wording.Of($"format=\"wide\" isn't known.").Code);
        Assert.Equal("title: \"Two words\" is too long.", Wording.Of($"title=\"Two words\" is too long.").Code);
        Assert.Equal("throw value: throws what it is given.", Wording.Of($"<throw value> throws what it is given.").Code);
        Assert.Equal("Only case goes in match.", Wording.Of($"Only <case> goes in <match>.").Code);

        // A plain string is final: a script's own message is never touched.
        Wording final = "Use <b> for the name attribute";
        Assert.Equal("Use <b> for the name attribute", final.Code);
        Assert.Equal("Use <b> for the name attribute", new KScriptException("Use <b> for the name attribute").Message);
        // Parts join, each keeping its own kind.
        Assert.Equal("Unknown statement nope. Did you mean <b>?", (Wording.Of($"Unknown element <{"nope"}>.") + " Did you mean <b>?").Code);
        // Text written elsewhere in XML terms, and a line of XML script.
        Assert.Equal("The message, instead of the statement's text.", Wording.Prose("The message, instead of the element's text.").Code);
        Assert.Equal("echo \"Hello\"", Wording.Snippet("<echo>Hello</echo>").Code);
    }

    // ---- the source: a message with XML wording must be one the runtime can reword ----

    /// <summary>What starts a message: raising an error, reporting a diagnostic, or building a Wording.</summary>
    private static readonly Regex MessageStart = new Regex(@"\bthrow\b|\bReport\(|\.Error\(|\bError\(|\bFail\(|KScriptException\(|Wording\.Of\(|\bWording \w+ =", RegexOptions.Compiled);

    private static readonly Regex XmlWording = new Regex(@"</?[A-Za-z{][\w{}.:\- ()""\\+]*?[ >/]|[\w}]=\\""|\battributes?\b|\belements?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A statement that picks the words for each form itself.</summary>
    private static readonly Regex PicksForm = new Regex(@"\b(IsCode|code|isCode|IsCodeFile)\s*\?|\.IsCode\b|\bnew Wording\(", RegexOptions.Compiled);

    /// <summary>Errors that are never about a .kscript file: pages are HTML, and these are the command line's own.</summary>
    private static readonly Regex OtherErrors = new Regex(@"\b(PageException|CompileException|CliPromptException|StubException|InvalidOperationException|ArgumentException|ProjectConfigException|FormatException|NotSupportedException)\(", RegexOptions.Compiled);

    /// <summary>Source that only ever speaks about XML or HTML files, or to the command line.</summary>
    private static bool OnlyXml(string path) =>
        path.StartsWith("KScript.Web/Pages/", StringComparison.Ordinal) || path.StartsWith("KScriptApp/", StringComparison.Ordinal)
        || path.StartsWith("KScript.Cli/", StringComparison.Ordinal) || path.StartsWith("KScript.Docs/", StringComparison.Ordinal)
        || path.StartsWith("KScript.LanguageServer/", StringComparison.Ordinal)
        || path.EndsWith("/MarkupParser.cs", StringComparison.Ordinal) || path.EndsWith("/XmlFormatter.cs", StringComparison.Ordinal)
        || path.EndsWith("/Retired.cs", StringComparison.Ordinal) || path.EndsWith("/Wording.cs", StringComparison.Ordinal)
        // ```<Type> is how a .kscript text block names its type: its own syntax, not a tag.
        || path.EndsWith("/StringBlocks.cs", StringComparison.Ordinal);

    /// <summary>Where the message that starts before <paramref name="from"/> ends: the parenthesis that closes it, or the statement.</summary>
    private static int EndOfMessage(string masked, int from)
    {
        int depth = 1;
        for (int i = from; i < masked.Length; i++)
        {
            if (masked[i] == '(') depth++;
            else if (masked[i] == ')' && --depth == 0) return i;
        }
        return masked.Length;
    }

    [Fact]
    public void A_message_with_XML_wording_is_written_so_it_can_be_reworded()
    {
        // The rule: a message that shows a tag, name="value", "attribute" or "element" is an interpolated string given
        // straight to something that takes a Wording (new KScriptException($"..."), run.Error($"..."), Report(..., $"..."),
        // Wording.Of($"...")), so a .kscript file gets it in its own terms. Joined with + or picked with ?: it would be
        // a plain string, shown to a .kscript file as written: wrap each part in Wording.Of(...).
        var wrong = new List<string>();
        int seen = 0;
        foreach ((string path, string text) in Sources())
        {
            if (OnlyXml(path)) continue;
            List<string> statements = SourceLiterals.Statements(text, out List<int> firstLines);
            seen += statements.Count;
            for (int s = 0; s < statements.Count; s++)
            {
                string statement = statements[s];
                if (statement.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                List<SourceLiterals.Literal> literals = SourceLiterals.In(statement);
                string masked = SourceLiterals.Mask(statement, literals);
                if (PicksForm.IsMatch(masked) || OtherErrors.IsMatch(masked)) continue;
                List<(int Start, int End)> messages = MessageStart.Matches(masked).Select(m => (m.Index, EndOfMessage(masked, m.Index + m.Length))).ToList();
                foreach (SourceLiterals.Literal literal in literals)
                {
                    if (!messages.Any(m => literal.Start > m.Start && literal.Start < m.End) || !XmlWording.IsMatch(literal.Shown)) continue;
                    string before = statement.Substring(0, literal.Start).TrimEnd();
                    string after = statement.Substring(literal.End).TrimStart();
                    bool argument = (before.EndsWith('(') || before.EndsWith(',')) && (after.StartsWith(',') || after.StartsWith(')'));
                    if (literal.Interpolated && argument) continue;
                    int line = firstLines[s] + statement.Take(literal.Start).Count(c => c == '\n');
                    wrong.Add($"{path}:{line}: {statement.Substring(literal.Start, Math.Min(literal.End - literal.Start, 120))}");
                }
            }
        }
        Assert.True(seen > 5000, "The source scan found too few statements.");
        Assert.True(wrong.Count == 0,
            "These messages show XML wording but are plain strings, so a .kscript file would get them as written. "
            + "Write each as $\"...\" given straight to the error, or wrap the part in Wording.Of($\"...\"):\n" + string.Join("\n", wrong));
    }
}

/// <summary>Reads the string literals of C# source well enough to check messages: plain and interpolated strings, with the holes of an interpolated one left out.</summary>
internal static class SourceLiterals
{
    internal sealed record Literal(int Start, int End, bool Interpolated, string Shown);

    /// <summary>The index after the string that opens at <paramref name="quote"/> (a plain, interpolated or verbatim string).</summary>
    internal static int EndOfString(string text, int quote)
    {
        bool verbatim = quote > 0 && (text[quote - 1] == '@' || (quote > 1 && text[quote - 1] == '$' && text[quote - 2] == '@'));
        bool interpolated = quote > 0 && (text[quote - 1] == '$' || (quote > 1 && text[quote - 1] == '@' && text[quote - 2] == '$'));
        return Read(text, quote, interpolated, verbatim, null);
    }

    private static int Read(string text, int quote, bool interpolated, bool verbatim, List<Literal> found)
    {
        int start = interpolated ? quote - 1 : quote;
        var shown = new StringBuilder();
        int i = quote + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (verbatim && c == '"' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i += 2;
                continue;
            }
            if (!verbatim && c == '\\' && i + 1 < text.Length)
            {
                shown.Append(text, i, 2);
                i += 2;
                continue;
            }
            if (c == '"')
            {
                if (!verbatim) found?.Add(new Literal(start, i + 1, interpolated, shown.ToString()));
                return i + 1;
            }
            if (interpolated && c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    shown.Append("{{");
                    i += 2;
                    continue;
                }
                int depth = 1;
                i++;
                while (i < text.Length && depth > 0)
                {
                    char d = text[i];
                    if (d == '"')
                    {
                        bool inner = text[i - 1] == '$';
                        i = Read(text, i, inner, text[i - 1] == '@', found);
                        continue;
                    }
                    if (d == '\'' && i + 2 < text.Length && text[i + 2] == '\'')
                    {
                        i += 3;
                        continue;
                    }
                    if (d == '\'' && i + 3 < text.Length && text[i + 1] == '\\' && text[i + 3] == '\'')
                    {
                        i += 4;
                        continue;
                    }
                    if (d is '{' or '(') depth++;
                    else if (d is '}' or ')') depth--;
                    i++;
                }
                shown.Append("{}");
                continue;
            }
            if (interpolated && c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                shown.Append("}}");
                i += 2;
                continue;
            }
            shown.Append(c);
            i++;
        }
        return i;
    }

    /// <summary>The plain and interpolated string literals of a piece of source, those inside the holes of another included, in order.</summary>
    internal static List<Literal> In(string text)
    {
        var found = new List<Literal>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int end = text.IndexOf('\n', i);
                i = end < 0 ? text.Length : end;
            }
            else if (c == '\'' && i + 2 < text.Length && text[i + 2] == '\'')
            {
                i += 3;
            }
            else if (c == '\'' && i + 3 < text.Length && text[i + 1] == '\\' && text[i + 3] == '\'')
            {
                i += 4;
            }
            else if (c == '"')
            {
                bool verbatim = i > 0 && (text[i - 1] == '@' || (i > 1 && text[i - 1] == '$' && text[i - 2] == '@'));
                bool interpolated = i > 0 && (text[i - 1] == '$' || (i > 1 && text[i - 1] == '@' && text[i - 2] == '$'));
                i = Read(text, i, interpolated, verbatim, found);
            }
            else
            {
                i++;
            }
        }
        return found.OrderBy(l => l.Start).ToList();
    }

    /// <summary>The source with the inside of every top-level literal blanked, so a search finds code and not words.</summary>
    internal static string Mask(string text, List<Literal> literals)
    {
        var masked = new StringBuilder(text);
        int covered = 0;
        foreach (Literal literal in literals)
        {
            if (literal.Start < covered) continue;
            for (int i = literal.Start + 1; i < literal.End - 1; i++)
            {
                if (masked[i] != '\n') masked[i] = ' ';
            }
            covered = literal.End;
        }
        return masked.ToString();
    }

    /// <summary>The source cut where a line ends a statement or a block, with the line each piece starts on.</summary>
    internal static List<string> Statements(string text, out List<int> firstLines)
    {
        var statements = new List<string>();
        firstLines = new List<int>();
        string[] lines = text.Split('\n');
        int start = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            string trimmed = line.TrimStart();
            bool ends = line.EndsWith(';') || line.EndsWith('{') || line.EndsWith('}') || trimmed.Length == 0
                || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('[');
            if (!ends && i < lines.Length - 1) continue;
            statements.Add(string.Join("\n", lines, start, i - start + 1));
            firstLines.Add(start + 1);
            start = i + 1;
        }
        return statements;
    }
}
