using KScript.Runtime;

namespace KScript.Tests;

/// <summary>The edges of the semantic and refinement types: every rule, bad hashes and patterns, schemas and joins.</summary>
public class SemanticTypeEdgeTests
{
    [Theory]
    [InlineData("type Age with [int] { min: 0 }\necho \"{ Age.name } { Age.base } { Age.description ?? 'none' } { Age.name }\"", "Age int none Age")]
    [InlineData("type Age with [int] { min: 0 }\necho Age.fields", "has no .fields. It has ->check() and .name.")]
    [InlineData("type Age with [int] { min: 0 }\necho Age.nope ?? 'x'", "x")]
    [InlineData("type T with [string] { one_of: 'a, b' }\nt: T = 'c'", "must be one of 'a, b'")]
    [InlineData("type T with [string] { pattern: '[' }\nt: T = 'c'", "pattern [ isn't a regular expression")]
    [InlineData("type D with [date] { min: '2020-01-01'; max: '2030-01-01' }\nd: D = '2019-01-01'->to_date()", "must be on or after 2020-01-01")]
    [InlineData("type D with [date] { max: '2030-01-01' }\nd: D = '2031-01-01'->to_date()", "must be on or before 2030-01-01")]
    [InlineData("type D with [date] { min: 'soon' }", "min on <type name=\"D\"> must be a date")]
    [InlineData("type N with [int] { min: 'x' }", "min on <type name=\"N\"> must be a number")]
    [InlineData("type L with [map] { max: 1 }\nm: L = { a: 1, b: 2 }", "must have at most 1 key")]
    [InlineData("type B with [bool] { max: 1 }\nb: B = true\necho $b", "true")]
    [InlineData("type P with [password] { min: 3 }\np: P = 'ab'", "must be at least 3 characters")]
    [InlineData("type R with [regex] { max: 3 }\nr: R = /abcd/", "must be at most 3 characters")]
    [InlineData("type N with [int] { min: -5 }\nn: N = -6", "must be at least -5")]
    [InlineData("type N with [int] { one_of: [1, null] }\nn: N = 2", "must be one of 1, null")]
    [InlineData("type N with [string] { one_of: [/a/] }\nn: N = 'b'", "must be one of /a/")]
    [InlineData("type U with [uuid] {}\nu: U = 'nope'", "a UUID is 32 hex digits")]
    [InlineData("type V with [uuid(v: 4)] { }\nv: V = 3f2a8c1e-9b7d-8c2a-8e1f-0a9b8c7d6e5f", "version 4")]
    [InlineData("type Age with [int] { min: 0 }\ntype Small with [Age(max: 9)] { }\ns: Small = 12", "must be at most 9")]
    [InlineData("type Age with [int] { min: 0 }\ntype Small with [Age(max: 9)] { }\ns: Small = 3\necho $s", "3")]
    [InlineData("type T with [string] { pattern: /^(a+)+$/ }\nt: T = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!'", "must match /^(a+)+$/")]
    [InlineData("type S {\n  pattern: /^(a+)+$/\n  a: string\n}\ns = S('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!')", "needs text matching")]
    [InlineData("type S {\n  pattern: '['\n  a: string\n}", "pattern on <type name=\"S\"> isn't a regular expression")]
    [InlineData("type S with [int] {\n  a: string\n}", "so it can only hold <validate> and <normalize>")]
    [InlineData("type S {\n  parse: 3\n  a: string\n}\ns = S('x')", "parse on S must make a map of its fields, but made a number (3)")]
    [InlineData("type S {\n  parse: { a: $value }\n  a: string\n}\necho S('x').a", "x")]
    [InlineData("type A { n: int(min: 1) }\ntype B { n: int(max: 5) }\ntype C with [A, B] {}\necho \"{ C->check({ n: 9 })[0].message } { C->check({ n: 0 })->count() } { C->check({ n: 3 })->count() }\"", "must be at most 5. 1 0")]
    [InlineData("type A { n: int(min: 1)? }\ntype B { n: int(max: 5)? }\ntype C with [A, B] {}\necho \"{ C->check({})->count() } { C->check({ n: 9 })->count() }\"", "0 1")]
    [InlineData("type B { a: int; b?: int; validate: ($b ?? 0) < 5, 'b is too big' }\nx = B({ a: 1 })\ntry {\n  x.b = 9\n} catch err {\n  echo $err.message\n}\necho $x->keys()", "b is too big")]
    [InlineData("type Age with [int] { max: 3 }\ntype T { a: Age[] }\necho T->schema().properties->to_json()", "{\"a\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"maximum\":3}}}")]
    [InlineData("type Tags with [string[]] { max: 2 }\ntype T { a: Tags }\necho T->schema().properties->to_json()", "\"maxItems\":2")]
    [InlineData("type F with [bool] { }\ntype T { a: F }\necho T->schema().properties->to_json()", "{\"a\":{\"type\":\"boolean\"}}")]
    [InlineData("type D with [date] { min: '2020-01-01' }\ntype T { a: D }\necho T->schema().properties->to_json()", "{\"a\":{\"type\":\"string\",\"format\":\"date-time\"}}")]
    [InlineData("type T { a: uuid(v: 4) }\necho T->schema().properties->to_json()", "{\"a\":{\"type\":\"string\",\"format\":\"uuid\"}}")]
    [InlineData("type Age with [int] { max: 3 }\ntype Old with [Age] { min: 1 }\necho \"{ Old(2) } { Old->check(9)->count() }\"", "2 1")]
    [InlineData("type A { a: int }\ntype B with [A] {}\nfn f(x: A) => $x.a\necho @f(B({ a: 1 }))", "1")]
    [InlineData("type A { a: int }\ntype Later { b: int }\nfn f(x: A) => $x.a\necho @f(Later({ b: 1 }))", "needs an int, but it is missing")]
    [InlineData("type A { a: int }\ntype B with [A] {}\ntype C { b: B }\necho C({ b: { a: 1 } }).b.a", "1")]
    [InlineData("p: password = 'abc'\necho $p->verify('$2b$12$' + 'x' * 53)", "false")]
    [InlineData("p: password = 'abc'\necho $p->verify('$argon2id$v=19$m=1,t=1,p=1,x=3$c2FsdA$aGFzaA')", "needs a hash made by ->hash()")]
    [InlineData("p: password = 'abc'\necho $p->verify('$argon2id$v=19$m=65536,t=3,p=1$!!!$aGFzaA')", "needs a hash made by ->hash()")]
    [InlineData("p: password = 'abc'\necho $p->needs_rehash('nope')", "needs a hash made by ->hash()")]
    [InlineData("p: password = 'abc'\necho $p->needs_rehash($p->hash())", "false")]
    [InlineData("p: password = 'abc'\necho \"{ $p->needs_rehash($p->hash(bcrypt)) } { $p->needs_rehash($p->hash()) }\"", "true false")]
    [InlineData("p: password = 'abc'\necho $p->hash(algorithm: md5)", "'md5' isn't a valid algorithm for ->hash(). Use argon2id or bcrypt.")]
    [InlineData("p: password = 'abc'\necho $p->strength(not: 'abc').problems", "mustn't contain 'abc'")]
    [InlineData("p: password = 'abc'\necho $p->strength(not: ['abc', 'x']).problems", "mustn't contain 'abc'")]
    [InlineData("p: password = 'abc'\necho \"{ $p } { $p->type() }\"", "******** password")]
    [InlineData("e: email = 'a@exa mple.com'", "isn't a domain")]
    [InlineData("e: email = 'a@a..com'", "isn't a domain")]
    [InlineData("e: email = 'a@\\u0000.com'", "isn't a domain")]
    [InlineData("e: email = 'a@a.c'", "'c' isn't a top-level domain")]
    [InlineData("e: email = 'a@a.123'", "'123' isn't a top-level domain")]
    [InlineData("e: email = 'a@' + 'x' * 64 + '.com'", "isn't a domain")]
    [InlineData("r: regex = '/a/xx'", "a flag is given twice")]
    [InlineData("r: regex = '/a/q'", "'q' isn't a regex flag")]
    [InlineData("r = /a b/x\necho $r->test('ab')", "true")]
    [InlineData("import KScript.Math\necho \"{ @convert_length(1, 'm', 'cm') } { @convert_length(1, 'km', 'm') } { @convert_length(1, 'ft', 'in') } { @convert_length(1, 'yd', 'ft') } { @convert_length(1, 'mi', 'km')->round(3) } { @convert_length(72, 'pt', 'in') } { @convert_length(96, 'px', 'in') } { @convert_length(10, 'mm', 'cm') }\"", "100 1000 12 3 1.609 1 1 1")]
    [InlineData("import KScript.Math\necho @convert_length(1, 'furlong', 'm')", "'furlong' isn't a length unit")]
    [InlineData("x = /a/\necho \"{ $x == /a/ } { $x } { [/a/, /a/]->unique()->count() }\"", "true /a/ 1")]
    [InlineData("x = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho \"{ $x == 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f } { [$x, $x]->unique()->count() }\"", "true 1")]
    [InlineData("e = joan@example.com\nh: handle = @j\necho \"{ [$e, $e]->unique()->count() } { [$h, $h]->unique()->count() }\"", "1 1")]
    [InlineData("echo joan@example.c", "joan@example.c isn't an email: 'c' isn't a top-level domain.")]
    [InlineData("fn f(size: string(min: 1) = big-box) => $size\necho @f()", "big-box")]
    [InlineData("fn f(id: uuid(v: 4)) => $id.version\necho @f(3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f)", "4")]
    [InlineData("fn f(n: int = big-box) => $n\necho @f()", "Unknown name 'big'")]
    [InlineData("fn f(h: handle = @a--b) => $h\necho @f()", "@a--b")]
    [InlineData("h: handle = @a--b\necho $h", "@a--b")]
    [InlineData("x: string(one_of: 'a, b') = 'c'", "must be one of a, b")]
    [InlineData("x: string(pattern: /^a/i) = 'Abc'\necho $x", "Abc")]
    [InlineData("x: int(min: -5) = -6", "must be at least -5")]
    [InlineData("p: password(pattern: /\\d/) = 'no digits here'", "must match /\\d/")]
    [InlineData("type Age with [int] { max: 9 }\necho '5'->json(as: Age)", "5")]
    [InlineData("<kscript><type name=\"A\" with=\"int\" fields=\"x: int\" /></kscript>", "so it has rules (min, max, pattern, validate...) rather than fields")]
    [InlineData("type P { x: int }\ntype Q with [P] {}\ntype Base { owner: P }\ntype Sub with [Base] { owner: Q }\necho Sub({ owner: { x: 1 } }).owner.x", "1")]
    [InlineData("type P { x: int }\ntype A { y: int }\ntype Base { owner: P }\ntype Sub with [Base] { owner: A }", "Sub declares 'owner' as A, but it joins a type where it is P")]
    [InlineData("type P { x: int }\ntype Base { owner: P }\ntype Sub with [Base] { owner: Later }\ntype Later with [P] {}\necho 'ok'", "ok")]
    [InlineData("type Base { n: int }\ntype Sub with [Base] { n: string }", "Sub declares 'n' as string")]
    [InlineData("<kscript><type name=\"T\"><field name=\"n\" type=\"int\" min=\"1\" /></type><echo>{ T->schema().properties->to_json() }</echo></kscript>", "{\"n\":{\"type\":\"integer\",\"minimum\":1}}")]
    public void EdgesBehave(string code, string expected)
    {
        string result;
        try
        {
            result = ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), code.StartsWith('<') ? "edges.ks.xml" : "edges.kscript")).TrimEnd('\n');
        }
        catch (KScript.Runtime.KScriptException e)
        {
            result = e.Message;
        }
        Assert.Contains(expected, result);
    }

    [Fact]
    public void SemanticValuesActLikeTheirText()
    {
        Assert.True(EmailAddress.TryParse("joan@example.com", out EmailAddress email, out _));
        Assert.True(HandleName.TryParse("@joan", out HandleName handle, out _));
        Assert.True(UuidValue.TryParse("3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f", out UuidValue uuid, out _));
        Assert.True(RegexValue.TryParse("/a+/i", out RegexValue regex, out _));
        Assert.True(PasswordValue.TryCreate("secret", out PasswordValue password, out _));
        foreach (KScript.Runtime.IKObject value in new KScript.Runtime.IKObject[] { email, handle, uuid, regex, password })
        {
            Assert.False(value.TryGetMember("nope", out object member));
            Assert.Null(member);
            Assert.False(value.TryCallMethod(null, "nope", out object result));
            Assert.Null(result);
        }
        Assert.Equal("joan@example.com", email.ToString());
        Assert.Equal("@joan", handle.ToString());
        Assert.Equal("3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f", uuid.ToString());
        Assert.Equal("/a+/i", regex.ToString());
        Assert.Equal("********", password.ToString());
        Assert.Equal("joan@example.com".GetHashCode(StringComparison.Ordinal), email.GetHashCode());
        Assert.Equal(handle.Name.GetHashCode(StringComparison.Ordinal), handle.GetHashCode());
        Assert.Equal(uuid.Text.GetHashCode(StringComparison.Ordinal), uuid.GetHashCode());
        Assert.Equal(regex.Text.GetHashCode(StringComparison.Ordinal), regex.GetHashCode());
        Assert.True(uuid.Equals(UuidValue.New(4, null, null)) == false && regex.Equals(regex) && !regex.Equals("x"));
        Assert.Contains("isn't a password hashing algorithm", Assert.Throws<KScript.Runtime.KScriptException>(() => password.Hash("md5")).Message);
        Assert.Contains("needs a hash made by ->hash()", Assert.Throws<KScript.Runtime.KScriptException>(() => password.Verify("$2b$1x$" + new string('a', 53))).Message);
        Assert.Null(TypeRef.Parse("int)"));
    }

    [Fact]
    public void ABrokenKscriptJsonLeavesTypesAlone()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ks-types-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "kscript.json"), "{ nope");
            string path = Path.Combine(dir, "main.kscript");
            Assert.Equal("0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f", ScriptRunner.Run("u: uuid = 0190b6c2-1f2a-8c3d-9e4f-5a6b7c8d9e0f\necho $u", path: path).TrimEnd('\n'));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
