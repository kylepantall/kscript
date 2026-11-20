using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>Less common diagnostics, and the analyzer's handling of odd or incomplete code.</summary>
    public class AnalyzerCoverageTests
    {
        internal static readonly string Folder = Path.Combine(Path.GetTempPath(), "kscript-analyzer-coverage");

        /// <summary>A few files beside the document being analysed.</summary>
        internal sealed class Files : IFileSystem
        {
            private readonly Dictionary<string, string> files;

            public Files(params (string Name, string Text)[] extra)
            {
                files = new Dictionary<string, string>
                {
                    [Path.Combine(Folder, "empty.ks.xml")] = "",
                    [Path.Combine(Folder, "nameless.ks.xml")] = "<kscript><const value=\"1\" /><fn value=\"2\" /><fn name=\"ok\" value=\"1\" /></kscript>",
                    [Path.Combine(Folder, "limits.ks.xml")] = "<kscript><const name=\"limit\" value=\"5\" /><let name=\"plain\" value=\"'p'\" /></kscript>",
                    [Path.Combine(Folder, "data.json")] = "{ \"a\": 1, \"on\": false, \"deep\": [[[[[[[[[[1]]]]]]]]]] }",
                    [Path.Combine(Folder, "blank.json")] = " ",
                    [Path.Combine(Folder, "broken.json")] = "{ nope",
                };
                foreach (var (name, text) in extra) files[Path.Combine(Folder, name)] = text;
            }

            /// <summary>Files that exist but can't be read.</summary>
            public HashSet<string> Unreadable { get; } = new HashSet<string>();

            public bool FileExists(string path) => files.ContainsKey(Path.GetFullPath(path)) || Unreadable.Contains(Path.GetFullPath(path));

            public bool DirectoryExists(string path) => files.Keys.Any(f => f.StartsWith(Path.GetFullPath(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal));

            public string ReadText(string path) => files.TryGetValue(Path.GetFullPath(path), out string t) ? t : null;

            public IEnumerable<string> ListFiles(string directory, string pattern) => files.Keys.Where(f => Path.GetDirectoryName(f) == Path.GetFullPath(directory));
        }

        internal static DocumentAnalysis Check(string body, string file = "test.ks.xml", IFileSystem files = null, LanguageCatalog catalog = null)
        {
            string text = file.EndsWith(".html", StringComparison.Ordinal) || body.StartsWith("<kscript", StringComparison.Ordinal) ? body : body.StartsWith("!", StringComparison.Ordinal) ? body.Substring(1) : Script(body);
            return WithoutUnused(Analyzer.Analyze(text, Path.Combine(Folder, file), files ?? new Files(), catalog));
        }

        private static void Has(DocumentAnalysis a, string code, string fragment)
        {
            Assert.True(a.Diagnostics.Any(d => d.Code == code && (fragment == null || d.Message.Contains(fragment, StringComparison.Ordinal))),
                $"Expected {code} with '{fragment}', got:\n" + Describe(a));
        }

        [Theory]
        [InlineData("<fn value=\"1\" />", DiagnosticCodes.MissingAttribute, "<fn> needs the 'name' attribute")]
        [InlineData("!<echo>hi</echo>", DiagnosticCodes.MisplacedElement, "Scripts start with <kscript>.")]
        [InlineData("<import package=\"KScript.Web\" /><slot />", DiagnosticCodes.MisplacedElement, "<slot> only works in pages served by kscript serve.")]
        [InlineData("<for each=\"i\" in=\"[1]\"><break>x</break></for>", DiagnosticCodes.StrayText, "<break> doesn't take any content")]
        [InlineData("<echo hidden>x</echo>", DiagnosticCodes.UnknownAttribute, "<echo> has no 'hidden' attribute")]
        [InlineData("<import package=\"KScript.AI\" /><fn name=\"f\" value=\"1\" /><llm to=\"r\" tools=\"f, @nope\">q</llm><echo>{$r}</echo>", DiagnosticCodes.UndefinedFunction, "No function named 'nope'")]
        [InlineData("<let name=\"x\"><b />1</let>", DiagnosticCodes.MisplacedElement, "<let> holds a value or assignments, not elements.")]
        [InlineData("<set value=\"1\" />", DiagnosticCodes.MissingAttribute, "<set> needs a 'name' for its value.")]
        [InlineData("<let />", DiagnosticCodes.MissingAttribute, "<let> needs a name and a value")]
        [InlineData("<let name=\"x\" value=\"1\" /><set name=\"x\" />", DiagnosticCodes.MissingAttribute, "<set> needs a value: a value attribute or text.")]
        [InlineData("<let name value=\"1\" />", DiagnosticCodes.InvalidAttribute, "needs a value")]
        [InlineData("<enum name=\"E\" values=\"a, , 1b\" /><echo>{$E}</echo>", DiagnosticCodes.InvalidAttributeValue, "'1b' can't be an enum value")]
        [InlineData("<enum name=\"E\" values=\"a, A\" /><echo>{$E}</echo>", DiagnosticCodes.InvalidAttributeValue, "'A' is listed twice.")]
        [InlineData("<do></do>", DiagnosticCodes.StatementSyntax, "<do> is empty.")]
        [InlineData("<for each=\"a, b, c\" in=\"[1]\"><echo>{$a}</echo></for>", DiagnosticCodes.InvalidAttributeValue, "each takes one name, or two")]
        [InlineData("<fn name=\"f\" value=\"1\"><echo>x</echo></fn>", DiagnosticCodes.InvalidAttribute, "<fn> has both a value and a body.")]
        [InlineData("<let name=\"x\" value=\"0\" /><echo>{ $x = 1 }</echo>", DiagnosticCodes.AssignmentNotAllowed, "Assignment isn't allowed here.")]
        [InlineData("<echo>{ to_upper('a') }</echo>", DiagnosticCodes.UnknownCommand, "Commands start with @: @to_upper(...).")]
        [InlineData("<echo>{ nothing('a') }</echo>", DiagnosticCodes.UnknownCommand, "Unknown function 'nothing'.")]
        [InlineData("<echo>{ 1 |> 2 }</echo>", DiagnosticCodes.ExpressionSyntax, "A pipe needs a method, function, command or lambda on its right")]
        [InlineData("<try><do>@to_upper(1)</do><catch as=\"e\"><echo>{$e.nope}</echo></catch></try>", DiagnosticCodes.UnknownMethod, "An error has no field 'nope'.")]
        [InlineData("<const name=\"c\" value=\"1\" /><let name=\"c\" value=\"2\" />", DiagnosticCodes.AssignConst, "$c is a constant and is already declared.")]
        [InlineData("<set name=\"1x\" value=\"1\" />", DiagnosticCodes.InvalidAttributeValue, "'1x' isn't a valid name.")]
        [InlineData("<fn name=\"f\" params=\"1a\" value=\"1\" />", DiagnosticCodes.InvalidAttributeValue, "'1a' isn't a valid parameter.")]
        [InlineData("<fn name=\"f\" params=\"a = )\" value=\"1\" />", DiagnosticCodes.ExpressionSyntax, "The default for 'a' isn't a valid expression")]
        [InlineData("<import package=\"KScript.Math\" /><echo>{ @round(1.5, zzzqq) }</echo>", DiagnosticCodes.BareIdentifier, "'zzzqq' isn't a value.")]
        [InlineData("<import package=\"KScript.IO\" /><echo>{ @known_folder(name: 'home', downloads) }</echo>", DiagnosticCodes.ArgumentCount, "has no parameter left for this argument.")]
        [InlineData("<import package=\"KScript.Math\" /><echo>{ @round(1, 2, 3, digits: 1) }</echo>", DiagnosticCodes.ArgumentCount, "@round() takes at most 2 arguments, but 3 were given.")]
        [InlineData("<import package=\"KScript.Math\" /><echo>{ @round(digits: 1, 2, 3) }</echo>", DiagnosticCodes.ArgumentCount, "has no parameter left for this argument.")]
        [InlineData("<enum name=\"Size\" values=\"small, large\" /><fn name=\"f\" params=\"a, size: Size\" value=\"1\" /><echo>{ @f(a: 1, huge) } { @f(a: 1, not large) }</echo>", DiagnosticCodes.InvalidArgumentValue, "'huge' isn't a Size.")]
        [InlineData("<import package=\"KScript.Math\" /><echo>{ @round(digits: 1, xyz) }</echo>", DiagnosticCodes.BareIdentifier, "'number' of @round() isn't an enum")]
        [InlineData("<import package=\"KScript.Nope\" />", DiagnosticCodes.UnknownPackage, "There is no package called KScript.Nope.")]
        [InlineData("<import src=\"limits.ks.xml\" /><echo>{ $limit } { $plain }</echo>", DiagnosticCodes.UndefinedVariable, "$plain is not defined.")]
        [InlineData("<kscript\n<import package=\"KScript.Terminal\" />", DiagnosticCodes.MarkupSyntax, "is missing its closing '>'")]
        [InlineData("<fn name=\"double\" params=\"x\" value=\"$x * 2\" /><echo>{ [1]->map(double) }</echo>", DiagnosticCodes.BareIdentifier, "To pass the function, write @double.")]
        [InlineData("<let name=\"$\" value=\"1\" />", DiagnosticCodes.InvalidAttributeValue, "A name is needed here.")]
        [InlineData("<kscript\n<input to=\"x\" />", DiagnosticCodes.MissingImport, "<input> is part of KScript.Terminal.")]
        public void Reports(string body, string code, string fragment) => Has(Check(body), code, fragment);

        [Theory]
        [InlineData("<import src=\"empty.ks.xml\" /><import src=\"nameless.ks.xml\" /><echo>{ @ok() }</echo>")]
        [InlineData("<echo>hi <b>x</b></echo>")]
        [InlineData("<import package=\"KScript.IO\" /><let name=\"d\" value=\"@read_json('data.json')\" /><echo>{$d.a} {$d.on} {$d.deep}</echo>")]
        [InlineData("<let const=\"yes\">x = 1</let><echo>{$x}</echo>")]
        [InlineData("<let name=\"a\" value=\"{}\" /><do>$a.b = 1</do>")]
        [InlineData("<marray name=\"m\"></marray><echo>{$m}</echo>")]
        [InlineData("<import package=\"KScript.Math\" as=\"m\" /><echo>{ m.pi }</echo>")]
        [InlineData("<echo>{ @to_upper(_) }</echo>")]
        [InlineData("<let name=\"m\" value=\"{}\" /><echo>{ $m.f(1) }</echo>")]
        [InlineData("<let name=\"f\" value=\"fn($x) => $x\" /><echo>{ $f(1) }</echo>")]
        [InlineData("<let name=\"$x\" value=\"1\" /><echo>{$x}</echo>")]
        [InlineData("<let name=\"x\" value=\"1\" /><set name=\"$x\" value=\"2\" />")]
        [InlineData("<fn name=\"f\" value=\"1\" /><call fn=\"@f\" />")]
        [InlineData("<call fn=\"to_upper\" args=\"'a'\" />")]
        [InlineData("<fn name=\"f\" params=\"a, , b\" value=\"1\" /><echo>{ @f(1, 2) }</echo>")]
        [InlineData("<fn name=\"f\" params=\"$a\" value=\"$a\" /><echo>{ @f(1) }</echo>")]
        [InlineData("<for each=\"$k, $v\" in=\"{}\"><echo>{$k}</echo></for>")]
        [InlineData("<import package=\"KScript.IO\" /><echo>{ @read_json('') } { @read_json('blank.json') } { @read_json('broken.json') }</echo>")]
        [InlineData("<fn name=\"f\"><if test=\"true\"><return value=\"1\" /></if><return value=\"'a'\" /></fn><echo>{ @f() }</echo>")]
        [InlineData("<fn name=\"f\"><if test=\"true\"><return value=\"{ a: 1 }\" /></if><return value=\"{ b: 2 }\" /></fn><echo>{ @f().a } { @f().b }</echo>")]
        [InlineData("<echo format=\"json\">x</echo><echo format=\"json\">[1,</echo>")]
        [InlineData("<fn name=\"f\" params=\"m = {}, g = fn($x) => $x, n = 1 + 2, l = []\" value=\"1\" /><echo>{ @f() }</echo>")]
        [InlineData("<import package=\"KScript.IO\" /><let name=\"b\" value=\"@read_json('blank.json')\" /><let name=\"c\" value=\"@read_json('broken.json')\" /><echo>{$b.a} {$c.a}</echo>")]
        [InlineData("<import package=\"KScript.Math\" /><echo>{ @sum(1, 2, extra: 3) }</echo>")]
        [InlineData("<let name=\"a\" value=\"[1]\" /><do>$a[0] = 2</do>")]
        public void Accepts(string body) => AssertClean(Check(body));

        [Theory]
        [InlineData("<let name=\"x\" value=\"0\" /><let name=\"f\" value=\"fn() => [$x = 1]\" />")]
        [InlineData("<echo>{ foo.bar() }</echo>")]
        [InlineData("<do>@to_upper('a').x = 1</do>")]
        [InlineData("<let name=\"f\" value=\"fn($a) => $a\" /><echo>{ $f(a: 1) } { @nope(x: 1) }</echo>")]
        public void Reports_something(string body) => Assert.NotEmpty(Check(body).Diagnostics);

        [Theory]
        [InlineData("<on event=\"click\" target=\"#x\"><do>@to_upper($event)</do></on>", null, null)]
        [InlineData("<button on-click=\" \">x</button>", DiagnosticCodes.MisplacedElement, "on-click only works inside a <live> block")]
        [InlineData("<include src=\"nope.html\" />", DiagnosticCodes.InvalidAttributeValue, "File not found: nope.html")]
        [InlineData("<include src=\"{$x}.html\" />", null, null)]
        [InlineData("<p hidden>x</p>", null, null)]
        [InlineData("<p>{ $request.nope }</p>", DiagnosticCodes.UnknownMethod, "$request has no field 'nope'.")]
        public void Pages(string body, string code, string fragment)
        {
            DocumentAnalysis a = Check(body, "page.html");
            if (code == null) AssertClean(a);
            else Has(a, code, fragment);
        }

        [Fact]
        public void Analysing_text_that_was_never_saved()
        {
            DocumentAnalysis a = Analyzer.Analyze(Script("<import package=\"KScript.IO\" /><let name=\"d\" value=\"@read_json('data.json')\" /><echo>{$d.a}</echo>"));
            AssertClean(a);
            Assert.False(a.IsPartial);
        }
    }
}
