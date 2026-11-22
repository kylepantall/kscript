using KScript.Runtime;

namespace KScript.Tests;

/// <summary>email, password, handle, uuid and regex; refinement and joined types; values written without quotes for their type.</summary>
public class SemanticTypesTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "types-test.kscript")).TrimEnd('\n');

    private static KScriptException CodeFails(string code) => Assert.Throws<KScriptException>(() => Code(code));

    [Theory]
    // email
    [InlineData("e: email = ' Joan.Smith@Example.COM '\necho \"$e { $e.local } { $e.domain } { $e->type() }\"", "Joan.Smith@example.com Joan.Smith example.com email")]
    [InlineData("e = joan@example.com\necho \"{ $e->upper() } { $e == 'joan@EXAMPLE.com' } { $e == 'x' } { $e->starts_with('jo') }\"", "JOAN@EXAMPLE.COM true false true")]
    [InlineData("e: email = 'joan@bücher.example'\necho $e", "joan@bücher.example")]
    [InlineData("echo \"{ { e: joan@example.com }->to_json() }\"", "{\"e\":\"joan@example.com\"}")]
    // handle
    [InlineData("h: handle = @Joan_S\necho \"$h { $h.name } { $h == @joan_s } { $h != @sam } { @joan_s == $h }\"", "@joan_s joan_s true true true")]
    [InlineData("h: handle = '@a.b-c'\necho $h->count()", "6")]
    // uuid
    [InlineData("u: uuid = '{3F2A8C1E-9B7D-4C2A-8E1F-0A9B8C7D6E5F}'\necho \"$u { $u.version }\"", "3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f 4")]
    [InlineData("u: uuid = 'urn:uuid:3f2a8c1e9b7d4c2a8e1f0a9b8c7d6e5f'\necho $u", "3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f")]
    [InlineData("u: uuid = 00000000-0000-0000-0000-000000000000\necho $u.version", "0")]
    [InlineData("u: uuid = 'ffffffff-ffff-ffff-ffff-ffffffffffff'\necho $u.version", "15")]
    [InlineData("echo \"{ @uuid().version } { @uuid(v: 8).version } { @uuid(v: 8, name: 'a') == @uuid(v: 8, name: 'a') } { @uuid(v: 8, name: 'a') == @uuid(v: 8, name: 'a', namespace: 'b') }\"", "4 8 true false")]
    [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f.version", "4")]
    // regex
    [InlineData("r = /(?<y>\\d{4})-(\\d\\d)/\necho \"{ $r->test('on 2026-11') } { $r->match('on 2026-11').groups.y } { $r->match('on 2026-11').captures } { $r->match('none') ?? 'null' }\"", "true 2026 2026, 11 null")]
    [InlineData("echo \"{ /\\d+/->match_all('3 cats, 12 dogs')->map(fn(m) => $m.index) } { /\\s*,\\s*/->split('a , b,c') }\"", "0, 8 a, b, c")]
    [InlineData("echo \"{ /(\\d+)/->replace('a1b22', '<$1>') } { /\\d+/->replace('a1b22', fn(m) => $m.text + '!') }\"", "a<1>b<22> a1!b22!")]
    [InlineData("r = /^[a-z]+$/im\necho \"$r { $r.pattern } { $r.flags } { $r->type() } { $r->test('ABC') }\"", "/^[a-z]+$/im ^[a-z]+$ im regex true")]
    [InlineData("r: regex = '^a+$'\necho \"$r { $r->test('aa') }\"", "/^a+$/ true")]
    [InlineData("r: regex = '/x/s'\necho $r.flags", "s")]
    [InlineData("echo \"{ 10 / 2 / 5 }\"", "1")]
    [InlineData("a = 10\nb = 2\necho \"{ $a / $b }\"", "5")]
    // password
    [InlineData("p: password = 'correct horse battery staple'\necho \"$p { { p: $p }->to_json() } { $p->type() } { $p->reveal()->count() }\"", "******** {\"p\":\"********\"} password 28")]
    [InlineData("p: password = 'correct horse battery staple'\nh = $p->hash(algorithm: 'bcrypt')\necho \"{ $p->verify($h) } { $h->starts_with('$2') } { $p->needs_rehash($h) }\"", "true true true")]
    [InlineData("p: password = 'correct horse battery staple'\nh = $p->hash()\nq: password = 'Correct horse battery staple'\necho \"{ $q->verify($h) } { $p->needs_rehash($h) }\"", "false false")]
    [InlineData("p: password = 'password1'\ns = $p->strength(min: 12, upper: true, symbol: true, not: ['pass'])\necho \"{ $s.ok } { $s.label } { $s.problems->count() }\"", "false very weak 5")]
    [InlineData("p: password = 'Zebra!Kettle#Lamp9'\necho \"{ $p->is_strong(min: 12, upper, lower, digit, symbol) } { $p->strength().score }\"", "true 4")]
    [InlineData("p: password = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaa'\necho $p->strength().label", "very weak")]
    [InlineData("p: password(algorithm: bcrypt) = 'correct horse battery staple'\necho $p->hash()->starts_with('$2')", "true")]
    [InlineData("pin: password(min: 4, max: 8, common: false, score: 0) = '1234'\necho $pin", "********")]
    public void TypesRun(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("e: email = 'joan@'", "$e needs an email, but got the string 'joan@': there's nothing after the @.")]
    [InlineData("e: email = '@example.com'", "nothing before the @")]
    [InlineData("e: email = 'joan@example'", "the domain needs a dot")]
    [InlineData("e: email = 'jo an@example.com'", "can't be used before the @")]
    [InlineData("e: email = 5", "needs an email")]
    [InlineData("h: handle = 'joan'", "must start with @")]
    [InlineData("h: handle = '@'", "needs a name after the @")]
    [InlineData("h: handle = '@jo an'", "letters, digits")]
    [InlineData("h: handle = '@abcdefghijabcdefghijabcdefghijx'", "longer than 30 characters")]
    [InlineData("u: uuid = 'nope'", "32 hex digits")]
    [InlineData("u: uuid = '3f2a8c1e-9b7d-9c2a-8e1f-0a9b8c7d6e5f'", "version")]
    [InlineData("u: uuid = '3f2a8c1e-9b7d-4c2a-0e1f-0a9b8c7d6e5f'", "variant")]
    [InlineData("echo @uuid(v: 5)", "version 4 (random) or 8 (custom)")]
    [InlineData("r: regex = '(['", "the pattern is invalid at character 2. Unterminated [] set.")]
    [InlineData("r: regex = '/a/q'", "flag")]
    [InlineData("p: password = 5", "needs a password")]
    [InlineData("p: password = ''", "empty")]
    [InlineData("p: password = 'x'\nok = $p == 'x'", "can't be compared")]
    [InlineData("p: password = 'x'\nok = $p < 'y'", "can't be compared")]
    [InlineData("p: password = 'x'\nok = $p === 'x'", "can't be compared")]
    [InlineData("p: password = 'x'\necho $p->hash(algorithm: 'md5')", "isn't a valid algorithm")]
    [InlineData("p: password = 'x'\necho $p->strength(min: 'a')", "needs a number")]
    [InlineData("ok = 'x' == @nobody", "Unknown command @nobody()")]
    [InlineData("echo { @bad name == 1 }", "")]
    public void MistakesAreExplained(string code, string expected)
    {
        KScriptException error = CodeFails(code);
        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("x'", error.Message.Contains("password") ? error.Message : string.Empty);
    }

    [Fact]
    public void LongBcryptPasswordsAndRegexTimeoutsAreErrors()
    {
        Assert.Contains("72 bytes", CodeFails("p: password = '" + new string('a', 80) + "'\necho $p->hash(algorithm: bcrypt)").Message);
        KScriptException slow = CodeFails("r = /^(a+)+$/\necho $r->test('" + new string('a', 40) + "!')");
        Assert.Equal("Timeout", slow.Type);
        Assert.Contains("took longer than a second", slow.Message);
    }

    [Theory]
    [InlineData("type Age with [int] { min: 0; max: 150 }\na: Age = '42'\necho \"{ $a + 1 } { $a->type() }\"", "43 number")]
    [InlineData("type Slug with [string] {\n  pattern: /^[a-z0-9-]+$/\n  normalize: $value->trim()->lower()\n}\ns: Slug = ' Hello-World '\necho \"[$s]\"", "[hello-world]")]
    [InlineData("type Slug with [string] { pattern: '^[a-z]+$' }\ns: Slug = abc\necho $s", "abc")]
    [InlineData("type Size with [string] { one_of: [small, large] }\ns: Size = small\necho $s", "small")]
    [InlineData("type Tags with [string[]] { max: 2 }\nt: Tags = ['a']\necho $t", "a")]
    [InlineData("type Username with [handle] {\n  not: [@admin, @root]\n  max: 8\n}\ntype Staff with [Username] { pattern: /^@staff_/ }\ns: Staff = @Staff_Jo\necho \"$s { $s.name } { Staff.name } { Staff.base } { Staff->check('@admin')->count() }\"", "@staff_jo staff_jo Staff Username 1")]
    [InlineData("type Even with [int] { validate: $value % 2 == 0, 'must be even' }\necho \"{ Even->check(3)[0].message } { Even->check(4)->count() } { Even('6') }\"", "must be even. 0 6")]
    [InlineData("type Even with [int] { validate: $value % 2 == 0 }\necho Even->check(3)[0].message", "doesn't pass the rule $value % 2 == 0.")]
    [InlineData("/// A short code.\ntype Code with [string] { max: 3 }\necho \"{ Code.description } { Code->type() }\"", "A short code. type")]
    [InlineData("type Work with [email] { validate: $value.domain == 'example.com', 'must be at example.com' }\nfn f(e: Work) => $e.local\necho @f('Joan@example.com')", "Joan")]
    [InlineData("type Tiny with [int] { max: 3 }\ntype T { n: Tiny; m: int(min: 1) = 1 }\necho T->schema().properties->to_json()", "{\"n\":{\"type\":\"integer\",\"maximum\":3},\"m\":{\"type\":\"integer\",\"minimum\":1}}")]
    [InlineData("type Mail with [email] {}\ntype T { e: Mail; u: uuid; p: password; h: handle; r: regex }\necho T->schema().properties->to_json()", "{\"e\":{\"type\":\"string\",\"format\":\"email\"},\"u\":{\"type\":\"string\",\"format\":\"uuid\"},\"p\":{\"type\":\"string\",\"format\":\"password\",\"writeOnly\":true},\"h\":{\"type\":\"string\",\"pattern\":\"^@[A-Za-z0-9_][A-Za-z0-9_.-]{0,29}$\"},\"r\":{\"type\":\"string\",\"format\":\"regex\"}}")]
    public void RefinementTypes(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("type Age with [int] { min: 0; max: 150 }\na: Age = 200", "$a doesn't fit Age: must be at most 150.")]
    [InlineData("type Age with [int] { min: 0 }\na: Age = 'old'", "needs an int")]
    [InlineData("type Short with [string] { max: 2 }\na: Short = 'abc'", "must be at most 2 characters")]
    [InlineData("type Long with [string] { min: 2 }\na: Long = 'a'", "must be at least 2 characters")]
    [InlineData("type Few with [string[]] { max: 1 }\na: Few = ['a', 'b']", "at most 1 item")]
    [InlineData("type Size with [string] { one_of: [small, large] }\ns: Size = 'huge'", "must be one of small, large")]
    [InlineData("type Slug with [string] { pattern: /^[a-z]+$/ }\ns: Slug = 'A1'", "must match /^[a-z]+$/")]
    [InlineData("type Bad with [string] { pattern: '([' }", "isn't a regular expression")]
    [InlineData("type Bad with [int] { min: abc }", "can only hold validate and normalize")]
    [InlineData("type Bad with [int, string] { min: 1 }", "")]
        [InlineData("type Bad with [uuid] { v: 9 }", "")]
    [InlineData("type Bad with [Nope] { min: 1 }", "Unknown type 'Nope' in with")]
    [InlineData("type T { a: int; min: 1 }", "")]
    [InlineData("x: uuid(bad: 1) = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f", "bad")]
    [InlineData("x: uuid(v: 8) = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f", "must be a version 8 UUID, but it is version 4")]
    [InlineData("x: email(domains: ['example.com']) = joan@other.com", "must be at example.com")]
    [InlineData("x: password(min: 12, upper: true) = 'short'", "(2 problems)")]
    [InlineData("x: string(min: 2)[] = ['ab', 'c']", "$[1]")]
    [InlineData("x: string[](max: 1) = ['a', 'b']", "at most 1 item")]
    [InlineData("x: uuid(v: 4 = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f", "")]
    public void RefinementMistakes(string code, string expected) => Assert.Contains(expected, CodeFails(code).Message);

    [Fact]
    public void JoinedTypesMergeFieldsRulesAndMethods()
    {
        const string types = "type User {\n  name: string\n  email: email\n  fn hello() => 'Hi ' + $name\n}\n"
            + "type Auditable {\n  created: date\n  updated: date\n  validate: $updated >= $created, 'updated must not be before created'\n}\n"
            + "type Admin with [User, Auditable] {\n  level: int = 1\n  fn hello() => 'Hello admin ' + $this.name\n}\n"
            + "fn welcome(u: User) => 'Welcome ' + $u.name\n";
        Assert.Equal("Welcome Joan true true false 1 Hello admin Joan",
            Code(types + "a = Admin({ name: 'Joan', email: 'j@example.com', created: '2026-01-01', updated: '2026-02-01' })\n"
                + "echo \"{ @welcome($a) } { User->is($a) } { User->is(Admin) } { Admin->is(User) } { $a.level } { $a->hello() }\""));
        Assert.Contains("updated must not be before created",
            CodeFails(types + "a = Admin({ name: 'J', email: 'j@example.com', created: '2026-02-01', updated: '2026-01-01' })").Message);
        Assert.Contains("updated must not be before created",
            CodeFails(types + "a = Admin({ name: 'J', email: 'j@example.com', created: '2026-01-01', updated: '2026-02-01' })\n$a.updated = '2025-01-01'->to_date()").Message);
        Assert.Equal("Hi J", Code(types + "u = User({ name: 'J', email: 'j@example.com' })\necho $u->hello()"));
        Assert.Contains("A User has no method ->nope()", CodeFails(types + "u = User({ name: 'J', email: 'j@example.com' })\necho $u->nope()").Message);
    }

    [Theory]
    [InlineData("type A { x: int }\ntype B { x: string }\ntype C with [A, B] { y: int }", "x")]
    [InlineData("type A { x: int; fn f() => 1 }\ntype B { y: int; fn f() => 2 }\ntype C with [A, B] { z: int }", "f")]
    [InlineData("type A { x: int }\ntype C with [A] { x: string }", "x")]
    [InlineData("type A { pattern: /a/; x: int }\ntype B { pattern: /b/; y: int }\ntype C with [A, B] { }", "pattern")]
    [InlineData("type A { x: int }\ntype C with [A, int] { }", "")]
    [InlineData("type A { x: int }\ntype C with [A, A] { }", "")]
    public void JoinMistakes(string code, string expected) => Assert.Contains(expected, CodeFails(code).Message);

    [Fact]
    public void JoinsThatResolveTheirConflicts()
    {
        Assert.Equal("5", Code("type A { x: int }\ntype B { x: string }\ntype C with [A, B] { x: int }\nc = C({ x: 5 })\necho $c.x"));
        Assert.Equal("2", Code("type A { x: int; fn f() => 1 }\ntype B { y: int; fn f() => 2 }\ntype C with [A, B] { fn f() => 2 }\nc = C({ x: 1, y: 1 })\necho $c->f()"));
        Assert.Equal("3", Code("type Small with [int] { max: 9 }\ntype A { x: int }\ntype C with [A] { x: Small }\nc = C({ x: 3 })\necho $c.x"));
    }

    [Theory]
    [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/\n  w: int\n  h: int\n  fn area() => $w * $h\n}\ns: Size = 1920x1080\necho \"{ $s.w } { $s->area() } { Size('2x3')->area() }\"", "1920 2073600 6")]
    [InlineData("type Pair {\n  pattern: /^(?<a>\\w+)=(?<b>\\w+)$/\n  parse: { a: $match.groups.b, b: $match.groups.a }\n  a: string\n  b: string\n}\np = Pair('x=y')\necho \"$p.a $p.b\"", "y x")]
    [InlineData("type Csv {\n  parse: { items: $value->split(',') }\n  items: string[]\n}\nc = Csv('a,b')\necho $c.items->count()", "2")]
    [InlineData("type Size {\n  pattern: /^(?<w>\\d+)x(?<h>\\d+)$/\n  w: int\n  h: int\n}\necho Size->check('big')[0].message", "needs text matching /^(?<w>\\d+)x(?<h>\\d+)$/ to make a Size, but got the string 'big'.")]
    public void TypesReadFromText(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("enum Color { red, green }\nc: Color = red\nfn paint(c: Color, by: handle = @guest, size: Slug = small-box) => $c + ' ' + $by + ' ' + $size\ntype Slug with [string] { pattern: /^[a-z-]+$/ }\necho @paint(green)\necho @paint(red, @Joan, big-box)", "green @guest small-box\nred @joan big-box")]
    [InlineData("fn who(h: handle) => $h.name\necho @who(@Sam)\necho @who(h: @Ann)", "sam\nann")]
    [InlineData("fn greet() => 'hi'\nfn call_it(f) => $f()\necho @call_it(@greet)", "hi")]
    [InlineData("type Code with [string] { max: 5 }\nc: Code = abc-1\necho $c", "abc-1")]
    public void ValuesWithoutQuotes(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("fn who(n: int) => $n\necho @who(@Sam)", "@Sam")]
    [InlineData("fn who(n: string) => $n\necho @who(@Sam)", "@Sam")]
    [InlineData("enum Color { red }\nfn p(c: Color) => $c\necho @p(@red)", "")]
    public void BareHandlesOnlyFitHandleParameters(string code, string expected) => Assert.Contains(expected, CodeFails(code).Message);

    [Fact]
    public void ProjectDefaultsComeFromKscriptJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ks-types-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "kscript.json"), "{ \"types\": { \"uuid\": { \"v\": 4 }, \"password\": { \"min\": 12, \"algorithm\": \"bcrypt\" }, \"email\": { \"domains\": [\"example.com\"] } } }");
            string script = "try { u: uuid = 0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f } catch err { echo $err.message }\n"
                + "v8: uuid(v: 8) = 0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f\necho $v8\n"
                + "try { p: password = 'short' } catch err { echo $err.message }\n"
                + "p: password = 'a long passphrase'\necho $p->hash()->starts_with('$2')\n"
                + "e: email = joan@example.com\necho $e\n"
                + "type T { e: email }\ntry { t = T({ e: 'joan@other.com' }) } catch err { echo $err.message }";
            string path = Path.Combine(dir, "main.kscript");
            File.WriteAllText(path, script);
            string output = ScriptRunner.Run(File.ReadAllText(path), path: path);
            Assert.Contains("must be a version 4 UUID, but it is version 8", output);
            Assert.Contains("0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f", output);
            Assert.Contains("must be at least 12 characters", output);
            Assert.Contains("true", output);
            Assert.Contains("joan@example.com", output);
            Assert.Contains("must be at example.com", output);

            // A changed kscript.json is read again.
            File.WriteAllText(Path.Combine(dir, "kscript.json"), "{ \"types\": { \"uuid\": { \"v\": 8 } } }");
            File.SetLastWriteTimeUtc(Path.Combine(dir, "kscript.json"), DateTime.UtcNow.AddMinutes(1));
            Assert.Equal("0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f", ScriptRunner.Run("u: uuid = 0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f\necho $u", path: path).TrimEnd());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BadProjectTypeSettingsAreReported()
    {
        KScript.Projects.ProjectConfig config = KScript.Projects.ProjectConfig.Parse("{ \"types\": { \"uuid\": { \"v\": 9 }, \"nope\": { \"min\": 1 }, \"email\": 3, \"int\": { \"min\": 1 } } }", "/tmp/kscript.json");
        Assert.Equal(3, config.Warnings.Count(w => w.StartsWith("types", StringComparison.Ordinal)));
        Assert.True(config.Types.ContainsKey("int"));
        Assert.Contains("should be an object", KScript.Projects.ProjectConfig.Parse("{ \"types\": 3 }", "/tmp/kscript.json").Warnings.Single());
    }

    [Fact]
    public void XmlFormWorksTheSame()
    {
        string xml = "<kscript>"
            + "<type name=\"Username\" with=\"handle\" min=\"3\" max=\"15\" not=\"@admin, @root\">"
            + "<validate test=\"not $value.name->contains('--')\" message=\"can't have two dashes in a row\" />"
            + "<normalize value=\"$value\" />"
            + "</type>"
            + "<type name=\"Tag\" with=\"string\" one_of=\"['a', 'b']\" />"
            + "<type name=\"T\"><field name=\"n\" type=\"int\" min=\"1\" /><field name=\"tag\" type=\"Tag\" /></type>"
            + "<let name=\"u\" type=\"Username\" value=\"@Joan\" />"
            + "<echo>$u { T->check({ n: 0, tag: 'c' })->count() }</echo>"
            + "<try><let name=\"x\" type=\"Username\" value=\"@a--b\" /><catch as=\"err\"><echo>$err.message</echo></catch></try>"
            + "</kscript>";
        Assert.Equal("@joan 2\n$x doesn't fit Username: can't have two dashes in a row.", ScriptRunner.Run(xml).TrimEnd('\n'));
        Assert.Contains("one_of", Assert.Throws<KScriptException>(() => ScriptRunner.Run("<kscript><type name=\"T\" with=\"string\" one_of=\"[$x]\" /></kscript>")).Message);
        Assert.Contains("<validate>", Assert.Throws<KScriptException>(() => ScriptRunner.Run("<kscript><validate test=\"1\" /></kscript>")).Message);
    }

    [Fact]
    public void SamplesRun()
    {
        Assert.Equal("12 in is 30.48 cm", ScriptRunner.Run(File.ReadAllText(ScriptRunner.RepoFile("samples", "types", "dimensions.kscript")), path: ScriptRunner.RepoFile("samples", "types", "dimensions.kscript")).TrimEnd());
        string accounts = ScriptRunner.Run(File.ReadAllText(ScriptRunner.RepoFile("samples", "types", "accounts.kscript")), path: ScriptRunner.RepoFile("samples", "types", "accounts.kscript"));
        Assert.Contains("\"handle\": \"@joan_s\"", accounts);
        Assert.Contains("(3 problems)", accounts);
    }

    [Theory]
    [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->upper()->slice(0, 8)", "3F2A8C1E")]
    [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->starts_with('3f2a')", "true")]
    [InlineData("echo joan@example.com->split('@')", "joan, example.com")]
    [InlineData("h: handle = @joan\necho $h->count()", "5")]
    [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->to_string()->count()", "36")]
    public void NativeValuesKeepTheStringMethodsThatMakeSense(string code, string expected) => Assert.Equal(expected, Code(code));

    [Theory]
    [InlineData("echo 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f->to_date()", "have no method ->to_date(). Use ->to_string()->to_date() to work on its text.")]
    [InlineData("echo joan@example.com->to_number()", "have no method ->to_number().")]
    [InlineData("h: handle = @joan\necho $h->lower()", "have no method ->lower().")]
    [InlineData("r: regex = /a/\necho $r->upper()", "have no method ->upper().")]
    public void NativeValuesDontHaveStringMethodsThatDontMakeSense(string code, string expected) => Assert.Contains(expected, CodeFails(code).Message);

    [Fact]
    public void TheTypesGuideListsTheStringMethodsEachTypeKeeps()
    {
        string guide = File.ReadAllText(ScriptRunner.RepoFile("docs", "types.md"));
        // Types a package adds (a test package may have added one) are documented by the package.
        foreach ((string type, HashSet<string> kept) in KScript.Metadata.KType.StringMethodsOf.Where(t => KScript.Metadata.NativeTypes.Get(t.Key).Package == null))
        {
            string row = guide.Split('\n').Single(l => l.StartsWith($"| `{type}` | `contains`", StringComparison.Ordinal));
            Assert.Equal(kept.OrderBy(n => n), row.Split('|')[2].Split(',').Select(n => n.Trim().Trim('`')).OrderBy(n => n));
        }
    }
}
