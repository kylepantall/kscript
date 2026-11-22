using System.Net;
using System.Text;

using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary><c>&lt;type&gt;</c> and <c>&lt;field&gt;</c>: data checked against a declared shape and turned into records.</summary>
public class TypedJsonTests
{
    private const string Types =
        "<enum name=\"Visibility\" values=\"public, private, internal\" />"
        + "<type name=\"User\" fields=\"login: string, id: int, avatar_url?: string\" />"
        + "<type name=\"Repo\" description=\"A repository.\">"
        + "<field name=\"name\" type=\"string\" />"
        + "<field name=\"stars\" type=\"int\" from=\"stargazers_count\" description=\"Stars.\" />"
        + "<field name=\"owner\" type=\"User\" />"
        + "<field name=\"owner_name\" type=\"string\" from=\"owner.login\" />"
        + "<field name=\"topics\" type=\"string[]\" default=\"[]\" />"
        + "<field name=\"license\" type=\"string?\" from=\"license.name\" />"
        + "<field name=\"visibility\" type=\"Visibility\" />"
        + "<field name=\"label\" value=\"$name + ' (' + $stars + ')'\" />"
        + "</type>";

    private const string Good = "{ name: 'kscript', stargazers_count: '12', owner: { login: 'kyle', id: 1 }, visibility: 'PUBLIC', extra: 1 }";

    private static string Echo(string expression) => RunBody(Types + $"<echo>{{ {expression} }}</echo>").TrimEnd('\n');

    private static KScriptException FailsWith(string body) => Fails(Types + body);

    [Theory]
    [InlineData("$Repo->from(" + Good + ").label", "kscript (12)")]
    [InlineData("$Repo->from(" + Good + ").owner_name", "kyle")]
    [InlineData("$Repo->from(" + Good + ").owner.avatar_url ?? 'none'", "none")]
    [InlineData("$Repo->from(" + Good + ").topics->count()", "0")]
    [InlineData("$Repo->from(" + Good + ").visibility", "public")]
    [InlineData("$Repo->from(" + Good + ").license ?? 'none'", "none")]
    [InlineData("$Repo->from(" + Good + ")->keys()->join(',')", "name,stars,owner,owner_name,topics,license,visibility,label")]
    [InlineData("@type($Repo->from(" + Good + "))", "Repo")]
    [InlineData("$Repo->from(" + Good + ")->type()", "Repo")]
    [InlineData("@type($Repo->from(" + Good + ").owner)", "User")]
    [InlineData("@type($Repo)", "type")]
    [InlineData("$Repo", "Repo")]
    [InlineData("$Repo.name + ' ' + $Repo.name + ' ' + $Repo.description", "Repo Repo A repository.")]
    [InlineData("$Repo.fields->count() + $Repo.fields->count()", "16")]
    [InlineData("$Repo.fields[1].from + ' ' + $Repo.fields[1].type + ' ' + $Repo.fields[5].type + ' ' + $Repo.fields[7].computed", "stargazers_count int string? true")]
    [InlineData("$Repo->from([" + Good + ", " + Good + "])->count()", "2")]
    [InlineData("$Repo->from([" + Good + "])[0].stars", "12")]
    [InlineData("$Repo->check(" + Good + ")->count()", "0")]
    [InlineData("$Repo->check([" + Good + ", {}])->count()", "5")]
    [InlineData("$Repo->from(" + Good + ") == $Repo->from(" + Good + ")", "true")]
    [InlineData("$Repo->from(" + Good + ").owner == { login: 'kyle', id: 1, avatar_url: null }", "true")]
    [InlineData("$User->from('{\"login\": \"kyle\", \"id\": 7}').id", "7")]
    [InlineData("@json('{\"login\": \"kyle\", \"id\": 7}', as: User).login", "kyle")]
    [InlineData("'{\"login\": \"kyle\", \"id\": 7}'->json(as: User).login", "kyle")]
    [InlineData("'[{\"login\": \"kyle\", \"id\": 7}]'->json(User)[0].login", "kyle")]
    [InlineData("'[{\"login\": \"kyle\", \"id\": 7}]'->json(as: 'User[]')->count()", "1")]
    [InlineData("'[1, 2]'->json(as: 'int[]')->count()", "2")]
    [InlineData("'\"public\"'->json(as: $Visibility)", "public")]
    [InlineData("'{\"a\": 1}'->json().a", "1")]
    [InlineData("@json('{\"a\": 1}').a", "1")]
    [InlineData("($Repo |> from(" + Good + ")).stars", "12")]
    [InlineData("$User.description ?? 'none'", "none")]
    [InlineData("$Repo->from(" + Good + ") === 'x'", "false")]
    [InlineData("$Repo->check({ name: 'a', stargazers_count: 1, owner: { login: 'k', id: 1 }, visibility: 3 })[0].message", "must be a Visibility (public, private, internal), but got a number (3).")]
    public void DataIsMappedToTypes(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Fact]
    public void ProblemsAreAllReportedWithTheirPaths()
    {
        KScriptException error = FailsWith("<let name=\"r\" value=\"$Repo->from({ name: { a: 1 }, stargazers_count: 'lots', owner: { id: 'x' }, visibility: 'publc', topics: [1, [2]] })\" />");
        Assert.Equal("Shape", error.Type);
        Assert.Contains("The data doesn't fit Repo (6 problems):", error.Message);
        Assert.Contains("$.name: needs a string, but got a map", error.Message);
        Assert.Contains("$.stargazers_count: needs an int, but got the string 'lots'.", error.Message);
        Assert.Contains("$.owner.login: needs a string, but it is missing.", error.Message);
        Assert.Contains("$.owner.id: needs an int, but got the string 'x'.", error.Message);
        Assert.Contains("$.visibility: must be a Visibility (public, private, internal), but got the string 'publc'. Did you mean public?", error.Message);
        Assert.Contains("$.topics[1]: needs a string, but got a list", error.Message);
        var problems = (List<object>)error.Fields["problems"];
        Assert.Equal(6, problems.Count);
        var first = (Dictionary<string, object>)problems[0];
        Assert.Equal("$.name", first["path"]);
        Assert.Equal("name", first["field"]);
    }

    [Fact]
    public void ProblemsCanBeCaughtAndChecked()
    {
        string output = RunBody(Types + "<try><let name=\"r\" value=\"$Repo->from({})\" />"
            + "<catch as=\"err\"><echo>{ $err.type } { $err.problems->count() } { $err.problems[0].path } { $err.problems[2].field }</echo></catch></try>"
            + "<echo>{ $Repo->check({ name: 1 })->map(fn($p) => $p.path)->join(' ') }</echo>"
            + "<echo>{ $Repo->check(3)[0].message }</echo>");
        Assert.Equal("Shape 5 $.name owner\n$.stargazers_count $.owner $.owner.login $.visibility\nneeds a Repo (a map), but got a number (3).\n", output);
    }

    [Fact]
    public void LongProblemListsAreCutShort()
    {
        KScriptException error = Fails("<type name=\"N\" fields=\"n: int\" /><echo>{ $N->from(@range(0, 30)->map(fn($i) => { n: 'x' })) }</echo>");
        Assert.Contains("(30 problems)", error.Message);
        Assert.Contains("...and 10 more.", error.Message);
        Assert.Equal(30, ((List<object>)error.Fields["problems"]).Count);
    }

    [Fact]
    public void RepeatedProblemsAreListedOnce()
    {
        KScriptException error = Fails("<type name=\"T\"><field name=\"a\" type=\"string\" from=\"x.y\" /><field name=\"b\" type=\"string\" from=\"x.y\" /></type><echo>{ $T->from({}) }</echo>");
        Assert.Contains("(1 problem):", error.Message);
    }

    [Theory]
    [InlineData("<let name=\"r\" type=\"Repo\" value=\"" + Good + "\" /><echo>{ $r.stars + 1 }</echo>", "13")]
    [InlineData("<const name=\"r\" type=\"Repo\" value=\"" + Good + "\" /><echo>{ $r.stars + 1 }</echo>", "13")]
    [InlineData("<let name=\"r\" value=\"null\" /><set name=\"r\" type=\"Repo\" value=\"" + Good + "\" /><echo>{ @type($r) }</echo>", "Repo")]
    [InlineData("<let name=\"rs\" type=\"Repo[]\" value=\"[" + Good + "]\" /><echo>{ $rs[0].name }</echo>", "kscript")]
    [InlineData("<let name=\"r\" type=\"Repo?\" value=\"null\" /><echo>{ $r ?? 'none' }</echo>", "none")]
    [InlineData("<let name=\"n\" type=\"int\" value=\"'4.5'\" /><echo>{ $n }</echo>", "4")]
    [InlineData("<let name=\"n\" type=\"int?\" value=\"null\" /><echo>{ $n ?? 'none' }</echo>", "none")]
    [InlineData("<fn name=\"stars\" params=\"repo: Repo\" value=\"$repo.stars\" /><echo>{ @stars(" + Good + ") }</echo>", "12")]
    [InlineData("<fn name=\"count\" params=\"repos: Repo[]\" value=\"$repos->count()\" /><echo>{ @count([" + Good + "]) }</echo>", "1")]
    [InlineData("<fn name=\"tag\" params=\"v: Visibility\" value=\"$v\" /><echo>{ @tag(private) }</echo>", "private")]
    [InlineData("<fn name=\"same\" params=\"r: Repo\" value=\"$r\" /><let name=\"r\" value=\"$Repo->from(" + Good + ")\" /><echo>{ @same($r) === $r }</echo>", "true")]
    public void DeclaredTypesConvertVariablesAndParameters(string body, string expected)
    {
        Assert.Equal(expected + "\n", RunBody(Types + body));
    }

    [Theory]
    [InlineData("<let name=\"r\" type=\"Repo\" value=\"{}\" />", "$r doesn't fit Repo (5 problems)")]
    [InlineData("<fn name=\"f\" params=\"repo: Repo\" value=\"1\" /><echo>{ @f({ name: 'x' }) }</echo>", "The 'repo' argument of @f() doesn't fit Repo")]
    [InlineData("<fn name=\"f\" params=\"repo: Rpo\" value=\"1\" /><echo>{ @f({}) }</echo>", "Unknown type 'Rpo' for a parameter of @f(). Did you mean Repo?")]
    [InlineData("<let name=\"r\" type=\"Nope\" value=\"1\" />", "Unknown type 'Nope'. Declare it with <type name=\"Nope\"")]
    [InlineData("<let name=\"r\" type=\"[]\" value=\"1\" />", "'[]' isn't a type.")]
    [InlineData("<echo>{ '{}'->json(as: 'Nope[') }</echo>", "'Nope[' isn't a type.")]
    [InlineData("<echo>{ '{}'->json(as: 3) }</echo>", "as: needs a type, such as Repo or 'Repo[]', but got a number (3).")]
    [InlineData("<echo>{ $Repo->from('[1, 2]') }</echo>", "$[0]: needs a Repo (a map), but got a number (1).")]
    [InlineData("<let name=\"rs\" type=\"Repo[]\" value=\"3\" />", "$rs doesn't fit Repo[]: needs a list of Repo, but got a number (3).")]
    [InlineData("<let name=\"r\" type=\"Repo\" value=\"null\" />", "$r doesn't fit Repo: needs a Repo, but it is null.")]
    [InlineData("<let name=\"r\" value=\"$Repo->from(" + Good + ")\" /><echo>{ $r.starz }</echo>", "Repo has no field 'starz'. Did you mean stars?")]
    [InlineData("<let name=\"r\" value=\"$Repo->from(" + Good + ")\" /><echo>{ $r.zzzzzz }</echo>", "Repo has no field 'zzzzzz'. Its fields are: name, stars")]
    [InlineData("<let name=\"r\" value=\"$Repo->from(" + Good + ")\" /><set>$r.starz = 3</set>", "Repo has no field 'starz'. Did you mean stars?")]
    [InlineData("<let name=\"r\" value=\"$Repo->from(" + Good + ")\" /><set>$r['stars'] = 'many'</set>", "Repo.stars must be an int, but got the string 'many'.")]
    [InlineData("<let name=\"r\" value=\"$Repo->from(" + Good + ")\" /><set>$r.owner = { login: 'x' }</set>", "Repo.owner doesn't fit User (1 problem)")]
    [InlineData("<echo>{ $Repo->nope() }</echo>", "nope")]
    [InlineData("<echo>{ $Repo.nope }</echo>", "has no field 'nope'")]
    [InlineData("<fn name=\"f\" params=\"x: 1a\" value=\"1\" />", "Unknown type '1a' for parameter 'x'.")]
    [InlineData("<type name=\"T\" fields=\"tags: string[]\" /><echo>{ $T->from({}) }</echo>", "$.tags: needs a list of string, but it is missing.")]
    [InlineData("<type name=\"T\"><field name=\"x\" from=\"a.b[0]\" /></type><echo>{ $T->from({}) }</echo>", "$.a.b[0]: needs a value, but it is missing.")]
    public void MistakesAreExplained(string body, string expected)
    {
        Assert.Contains(expected, FailsWith(body).Message);
    }

    [Fact]
    public void SettingAFieldConvertsTheValue()
    {
        Assert.Equal("13 number\nmap\n", RunBody(Types + "<let name=\"r\" value=\"$Repo->from(" + Good + ")\" />"
            + "<set>$r.stars = '13'</set><set>$r['label'] = 'x'</set><echo>{ $r.stars } { @type($r.stars) }</echo>"
            + "<let name=\"m\" value=\"{}\" /><set>$m.a = 1</set><set>$m['b'] = 2</set><echo>{ @type($m) }</echo>"));
    }

    [Theory]
    [InlineData("drop", "{ a: 1, b: 2 }", "a")]
    [InlineData("keep", "{ a: 1, b: 2 }", "a,b")]
    [InlineData("keep", "{ b: 2 }", "a,b")]
    public void ExtraKeysAreDroppedOrKept(string extra, string data, string keys)
    {
        Assert.Equal(keys + "\n", RunBody($"<type name=\"T\" fields=\"a?: int\" extra=\"{extra}\" /><echo>{{ $T->from({data})->keys()->join(',') }}</echo>"));
    }

    [Fact]
    public void ExtraKeysCanBeErrorsOrBeSet()
    {
        Assert.Contains("$.b: T has no field 'b'.", Fails("<type name=\"T\" fields=\"a: int\" extra=\"error\" /><echo>{ $T->from({ a: 1, b: 2 }) }</echo>").Message);
        Assert.Contains("$['odd key']: T has no field 'odd key'.", Fails("<type name=\"T\" fields=\"a: int\" extra=\"error\" /><echo>{ $T->from({ a: 1, 'odd key': 2 }) }</echo>").Message);
        Assert.Equal("5\n", RunBody("<type name=\"T\" fields=\"a: int\" extra=\"keep\" /><let name=\"t\" value=\"$T->from({ a: 1 })\" /><set>$t.z = 5</set><echo>{ $t.z }</echo>"));
    }

    [Theory]
    [InlineData("login: string, id: int = 0", "{ login: 'k' }", "k 0")]
    [InlineData("login, id?", "{ login: 'k', id: 3 }", "k 3")]
    [InlineData("login: string, id: int = 1 + 1", "{ login: 'k', id: null }", "k 2")]
    [InlineData("login: string, tags: string[] = ['a', 'b']", "{ login: 'k' }", "k a")]
    [InlineData("login = 'x,y=z', tags: list = [{ a: (1) }]", "{}", "x,y=z {\"a\":1}")]
    public void InlineFieldsTakeDefaultsAndOptionalMarks(string fields, string data, string expected)
    {
        Assert.Equal(expected + "\n", RunBody($"<type name=\"T\" fields=\"{fields}\" /><let name=\"t\" value=\"$T->from({data})\" />"
            + "<echo>{ $t.login } { ($t.tags ?? [$t.id])[0] }</echo>"));
    }

    [Theory]
    [InlineData("<field name=\"first\" from=\"items[0].name\" />", "{ items: [{ name: 'a' }] }", "a")]
    [InlineData("<field name=\"first\" from=\"items[1].name\" optional=\"yes\" />", "{ items: [{ name: 'a' }] }", "")]
    [InlineData("<field name=\"first?\" from=\"items.name\" />", "{ items: 3 }", "")]
    [InlineData("<field name=\"first\" from=\"items[0][1]\" />", "{ items: [[1, 2]] }", "2")]
    [InlineData("<field name=\"first\" from=\"Items\" />", "{ items: 'case' }", "case")]
    [InlineData("<field name=\"first\" from=\"a.b\" default=\"'d'\" />", "{ a: {} }", "d")]
    [InlineData("<field name=\"first\" from=\"weird key\" />", "{ 'weird key': 'w' }", "w")]
    public void FieldsReadFromPaths(string field, string data, string expected)
    {
        Assert.Equal(expected + "\n", RunBody($"<type name=\"T\">{field}</type><echo>{{ $T->from({data}).first }}</echo>"));
    }

    [Fact]
    public void MissingPathsAreReportedWhereTheyStopped()
    {
        Assert.Contains("$.items[3]: needs a value, but it is missing.", Fails("<type name=\"T\"><field name=\"x\" from=\"items[3]\" /></type><echo>{ $T->from({ items: [] }) }</echo>").Message);
        Assert.Contains("$.a.b: needs a value, but it is missing.", Fails("<type name=\"T\"><field name=\"x\" from=\"a.b\" /></type><echo>{ $T->from({ a: 1 }) }</echo>").Message);
    }

    [Fact]
    public void ComputedFieldsSeeTheOtherFieldsAndAreConverted()
    {
        Assert.Equal("Joan Smith 3\n", RunBody("<type name=\"P\"><field name=\"first\" type=\"string\" /><field name=\"last\" type=\"string\" />"
            + "<field name=\"full\" value=\"$first + ' ' + $last\" /><field name=\"n\" type=\"int\" value=\"'3'\" /></type>"
            + "<let name=\"p\" value=\"$P->from({ first: 'Joan', last: 'Smith' })\" /><echo>{ $p.full } { $p.n }</echo>"));
        Assert.Contains("$.n: needs an int", Fails("<type name=\"P\"><field name=\"n\" type=\"int\" value=\"'x'\" /></type><echo>{ $P->from({}) }</echo>").Message);
    }

    [Fact]
    public void TypesCanReferToEachOtherInAnyOrderAndToThemselves()
    {
        Assert.Equal("c 2\n", RunBody("<echo>{ $Tree->from({ name: 'a', kids: [{ name: 'b', kids: [{ name: 'c' }] }] }).kids[0].kids[0].name } { @depth() }</echo>"
            + "<fn name=\"depth\" value=\"$Tree->from({ name: 'x', kids: [{ name: 'y' }, { name: 'z' }] }).kids->count()\" />"
            + "<type name=\"Tree\" fields=\"name: string, kids: Tree[] = []\" />"));
    }

    [Fact]
    public void DeeplyNestedDataStops()
    {
        Assert.Contains("is nested too deeply", Fails("<type name=\"L\" fields=\"next?: L\" /><let name=\"d\" value=\"{}\" /><for each=\"i\" in=\"1..70\"><set name=\"d\" value=\"{ next: $d }\" /></for><echo>{ $L->from($d) }</echo>").Message);
    }

    [Fact]
    public void TypesFromSampleFiles()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-types").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "repo.json"), "{ \"name\": \"k\", \"stargazers_count\": 1, \"private\": false, \"owner\": { \"login\": \"kyle\" }, "
                + "\"topics\": [\"a\"], \"mixed\": [1, \"a\"], \"empty\": [], \"nums\": [1, 2], \"flags\": [true], \"license\": null, \"avatar-url\": \"x\", \"123\": 1, \"2x\": 2, "
                + "\"items\": [{ \"id\": 1, \"note\": \"a\" }, { \"id\": 2 }, null], \"deep\": {\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{\"h\":{\"i\":1}}}}}}}}}, \"lists\": [[1]] }");
            File.WriteAllText(Path.Combine(dir, "list.json"), "[{ \"id\": 1, \"tag\": \"x\" }, { \"id\": 2 }]");
            File.WriteAllText(Path.Combine(dir, "bad.json"), "{ nope");
            File.WriteAllText(Path.Combine(dir, "scalar.json"), "3");
            string script = Path.Combine(dir, "t.ks.xml");
            File.WriteAllText(script, "<kscript>"
                + "<type name=\"Repo\" sample=\"repo.json\"><field name=\"stars\" type=\"int\" from=\"stargazers_count\" /><field name=\"name\" type=\"string\" description=\"Mine.\" /></type>"
                + "<echo>{ $Repo.fields[0].description }</echo>"
                + "<type name=\"Item\" sample=\"list.json\" />"
                + "<echo>{ $Repo.fields->map(fn($f) => $f.name + ':' + $f.type + ($f.optional ? '?' : ''))->join(' ') }</echo>"
                + "<echo>{ $Item.fields->map(fn($f) => $f.name + ($f.optional ? '?' : ''))->join(' ') }</echo>"
                + "<let name=\"r\" value=\"$Repo->from({ name: 'k', stargazers_count: 5, stars: 9, private: true, owner: { login: 'k' }, topics: [], mixed: [], empty: [], nums: [], flags: [], 'avatar-url': 'y', '2x': 1, items: [{ id: 3 }], deep: { a: { b: { c: { d: { e: { f: { g: { h: { i: 1 } } } } } } } } }, lists: [] })\" />"
                + "<echo>{ $r.stars } { $r.avatar_url } { $r.items[0].note ?? '-' } { $Item->from({ id: 1 }).tag ?? '-' }</echo>"
                + "</kscript>");
            string output = RunScriptFile(script);
            Assert.Equal("Mine.\nname:string stargazers_count:number private:bool owner:Repo.owner topics:string[] mixed:any[] empty:any[] nums:number[] flags:bool[] license:any?? avatar_url:string _2x:number "
                + "items:Repo.items[] deep:Repo.deep lists:any[] stars:int\nid tag?\n5 y - -\n", output);

            foreach ((string attrs, string message) in new[]
            {
                ("sample=\"missing.json\"", "The sample file 'missing.json' doesn't exist."),
                ("sample=\"bad.json\"", "The sample file 'bad.json' isn't JSON"),
                ("sample=\"scalar.json\"", "must hold a JSON object, or a list of objects"),
            })
            {
                File.WriteAllText(script, $"<kscript><type name=\"T\" {attrs} /></kscript>");
                Assert.Contains(message, Assert.Throws<KScriptException>(() => RunScriptFile(script)).Message);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string RunScriptFile(string path)
    {
        var output = new StringWriter { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output, string.Empty)).RunFile(path);
        return output.ToString();
    }

    [Fact]
    public void ReadJsonMapsFiles()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-types").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "users.json"), "[{ \"login\": \"a\", \"id\": 1 }, { \"login\": \"b\", \"id\": 2 }]");
            string script = Path.Combine(dir, "t.ks.xml");
            File.WriteAllText(script, "<kscript><import package=\"KScript.IO\" /><type name=\"User\" fields=\"login: string, id: int\" />"
                + "<echo>{ @read_json('users.json', as: User)->map(fn($u) => $u.login + @type($u))->join(',') } { @read_json('users.json')[0]->type() }</echo></kscript>");
            Assert.Equal("aUser,bUser map\n", RunScriptFile(script));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("<type name=\"string\" fields=\"a\" />", "'string' is a built-in type")]
    [InlineData("<type name=\"T\" fields=\"a\" extra=\"maybe\" />", "extra=\"maybe\" isn't one of: drop, keep, error.")]
    [InlineData("<type name=\"T\" />", "needs fields")]
    [InlineData("<type name=\"T\" fields=\"a, a\" />", "lists the field 'a' twice")]
    [InlineData("<type name=\"T\" fields=\"a\"><field name=\"a\" /></type>", "lists the field 'a' twice")]
    [InlineData("<type name=\"T\" fields=\"a = (\" />", "The default of 'a' isn't a valid expression")]
    [InlineData("<type name=\"T\" fields=\"1a\" />", "'1a' can't be a field name")]
    [InlineData("<type name=\"T\"><field name=\" ? \" /></type>", "A field needs a name")]
    [InlineData("<type name=\"T\" fields=\"a: [x]\" />", "'[x]' isn't a type for the field 'a'")]
    [InlineData("<type name=\"T\"><echo>x</echo></type>", "<type> can only hold <field>, <validate>, <normalize> and <fn> elements, not <echo>.")]
    [InlineData("<type name=\"T\"><field type=\"int\" /></type>", "<field> needs a name")]
    [InlineData("<type name=\"T\"><field name=\"a\" from=\"a..b\" /></type>", "from=\"a..b\" on the field 'a' isn't a path")]
    [InlineData("<type name=\"T\"><field name=\"a\" optional=\"maybe\" /></type>", "optional=\"maybe\" should be true or false.")]
    [InlineData("<type name=\"T\"><field name=\"a\" value=\"1\" from=\"b\" /></type>", "it can't also have from or default")]
    [InlineData("<type name=\"T\"><field name=\"a\" value=\"1\" default=\"2\" /></type>", "it can't also have from or default")]
    [InlineData("<field name=\"a\" />", "<field> must be inside <type>, <error>, <model> or <job>.")]
    [InlineData("<type name=\"T\"><if test=\"yes\"><field name=\"a\" /></if></type>", "<type> can only hold <field>, <validate>, <normalize> and <fn> elements, not <if>.")]
    public void DeclarationMistakesAreExplained(string body, string expected)
    {
        Assert.Contains(expected, Fails(body).Message);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("owner.login", true)]
    [InlineData("items[0].name", true)]
    [InlineData("items[0][2]", true)]
    [InlineData("", false)]
    [InlineData("[0]", false)]
    [InlineData("a.", false)]
    [InlineData("a[x]", false)]
    [InlineData("a[-1]", false)]
    [InlineData("a[0", false)]
    [InlineData("a[0]b", false)]
    [InlineData("a..b", false)]
    public void FieldPaths(string path, bool valid)
    {
        Assert.Equal(valid, FieldDefinition.ParsePath(path) != null);
    }

    [Theory]
    [InlineData("string", "string")]
    [InlineData(" INT ", "int")]
    [InlineData(null, null)]
    [InlineData("Repo[]", "Repo[]")]
    [InlineData("Repo[][]?", "Repo[][]?")]
    [InlineData("string ?", "string?")]
    [InlineData("1x", null)]
    [InlineData("", null)]
    [InlineData("a-b", null)]
    public void TypeRefs(string text, string expected)
    {
        Assert.Equal(expected, TypeRef.Parse(text)?.ToString());
    }

    [Fact]
    public void ImportingANameThatIsntExportedListsWhatIs()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-types").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib.ks.xml"), "<kscript><type name=\"User\" fields=\"login: string\" /></kscript>");
            string script = Path.Combine(dir, "t.ks.xml");
            File.WriteAllText(script, "<kscript><import src=\"lib.ks.xml\" names=\"Usr\" /></kscript>");
            Assert.Contains("Did you mean User? Scripts export their top-level <fn> functions, <const> values, <enum>s, <type>s, <error>s, <model>s and <job>s.", Assert.Throws<KScriptException>(() => RunScriptFile(script)).Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TypesAreExportedFromImportedScripts()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-types").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib.ks.xml"), "<kscript><type name=\"User\" fields=\"login: string\" /></kscript>");
            string script = Path.Combine(dir, "t.ks.xml");
            File.WriteAllText(script, "<kscript><import src=\"lib.ks.xml\" /><echo>{ @json('{\"login\": \"k\"}', as: User)->type() }</echo></kscript>");
            Assert.Equal("User\n", RunScriptFile(script));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SchemasDescribeTypes()
    {
        string json = RunBody(Types + "<type name=\"Node\" fields=\"name: string, next?: Node, any?: any, f?: fn, ok: bool, n: number, x: float, d: date, l: list, m: map, gone?: Gone\" extra=\"keep\" />"
            + "<type name=\"Holder\" fields=\"node: Node, nodes: Node[]\" />"
            + "<echo value=\"$Repo->schema()->to_json()\" /><echo value=\"$Node->schema()->to_json()\" /><echo value=\"$Holder->schema()->to_json()\" />");
        string[] lines = json.Split('\n');
        Assert.Equal("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"type\":\"object\",\"title\":\"Repo\",\"description\":\"A repository.\",\"properties\":{"
            + "\"name\":{\"type\":\"string\"},\"stars\":{\"type\":\"integer\",\"description\":\"Stars.\"},\"owner\":{\"$ref\":\"#/$defs/User\"},\"owner_name\":{\"type\":\"string\"},"
            + "\"topics\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"license\":{\"type\":\"string\"},\"visibility\":{\"type\":\"string\",\"enum\":[\"public\",\"private\",\"internal\"]},"
            + "\"label\":{\"readOnly\":true}},\"required\":[\"name\",\"stars\",\"owner\",\"owner_name\",\"visibility\"],\"additionalProperties\":false,"
            + "\"$defs\":{\"User\":{\"type\":\"object\",\"title\":\"User\",\"properties\":{\"login\":{\"type\":\"string\"},\"id\":{\"type\":\"integer\"},\"avatar_url\":{\"type\":\"string\"}},"
            + "\"required\":[\"login\",\"id\"],\"additionalProperties\":false}}}", lines[0]);
        Assert.Contains("\"next\":{\"$ref\":\"#\"}", lines[1]);
        Assert.Contains("\"any\":{},\"f\":{},\"ok\":{\"type\":\"boolean\"},\"n\":{\"type\":\"number\"},\"x\":{\"type\":\"number\"},\"d\":{\"type\":\"string\",\"format\":\"date-time\"},\"l\":{\"type\":\"array\"},\"m\":{\"type\":\"object\"},\"gone\":{}", lines[1]);
        Assert.Contains("\"additionalProperties\":true", lines[1]);
        Assert.Contains("\"nodes\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/$defs/Node\"}}", lines[2]);
        Assert.Equal(1, lines[2].Split("\"title\":\"Node\"").Length - 1);
    }

    [Fact]
    public void SampleTypesGiveNestedSchemas()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-types").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "s.json"), "{ \"owner\": { \"login\": \"k\" } }");
            string script = Path.Combine(dir, "t.ks.xml");
            File.WriteAllText(script, "<kscript><type name=\"S\" sample=\"s.json\" /><echo value=\"$S->schema().properties.owner.title\" /></kscript>");
            Assert.Equal("S.owner\n", RunScriptFile(script));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ResponsesMapToTypes()
    {
        // A tiny HTTP server on a raw socket, so the test needs no URL permissions on any OS.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                using System.Net.Sockets.TcpClient client = await listener.AcceptTcpClientAsync();
                using System.Net.Sockets.NetworkStream stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[4096];
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0) break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                string body = request.ToString().StartsWith("GET /list", StringComparison.Ordinal) ? "[{\"login\":\"a\",\"id\":1},{\"login\":\"b\",\"id\":2}]" : "{\"login\":\"kyle\",\"id\":\"x\"}";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
                await stream.WriteAsync(bytes);
            }
        });
        try
        {
            string url = $"http://127.0.0.1:{port}";
            string output = RunBody($"<type name=\"User\" fields=\"login: string, id: int\" />"
                + $"<echo>{{ @api('{url}/list')->json(as: User)->map(fn($u) => $u.login)->join(',') }}</echo>"
                + $"<echo>{{ @api('{url}/one')->json().login }}</echo>"
                + $"<try><echo>{{ @api('{url}/one')->json(as: User) }}</echo><catch as=\"err\"><echo>{{ $err.message->split('\n')[0] }}</echo></catch></try>", "", "KScript.Net");
            Assert.Equal($"a,b\nkyle\nThe response from {url}/one doesn't fit User (1 problem):\n", output);
            await served.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            listener.Stop();
        }
    }
}
