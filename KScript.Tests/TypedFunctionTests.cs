using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Built-in functions that take the new types: regex patterns, typed input, regex file lists.</summary>
public class TypedFunctionTests
{
    private static string Code(string code) => Run(code, path: Path.Combine(Path.GetTempPath(), "typed-functions.kscript")).TrimEnd('\n');

    [Theory]
    [InlineData("echo \"{ 'Hello'->matches('^hello') } { 'Hello'->matches(/^hello/i) }\"", "false true")]
    [InlineData("echo \"{ 'Hello hello'->find_all(/hello/i) } { 'a1b22'->find_all('\\d+') }\"", "Hello, hello 1, 22")]
    [InlineData("echo \"{ 'Hi World'->replace_pattern(/(?<w>world)/i, '[${w}]') } { 'a1'->replace_pattern('(\\d)', '<$1>') }\"", "Hi [World] a<1>")]
    [InlineData("echo \"{ 'a  b   c'->replace(/\\s+/, '_') } { 'a b'->replace('b', 'c') } { 'ab'->replace('', 'x') }\"", "a_b_c a c ab")]
    [InlineData("echo \"{ 'a , b,c'->split(/\\s*,\\s*/) } { 'a-b'->split('-') } { 'a  b'->split() }\"", "a, b, c a, b a, b")]
    public void StringMethodsTakeARegexOrText(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void BadAndSlowPatternsAreErrors()
    {
        KScriptException bad = Assert.Throws<KScriptException>(() => Code("echo 'x'->matches('[')"));
        Assert.Contains("'[' isn't a regular expression for ->matches()", bad.Message);
        KScriptException slow = Assert.Throws<KScriptException>(() => Code("echo '" + new string('a', 40) + "!'->replace(/^(a+)+$/, 'x')"));
        Assert.Equal("Timeout", slow.Type);
    }

    [Fact]
    public void InputReadsTheNewTypes()
    {
        Assert.Equal("Email: \nThat isn't an email: there's nothing after the @. Try again.\nEmail: \nJoan@example.com example.com\n",
            RunBody("<input into=\"e\" type=\"email\">Email: </input><echo>$e { $e->domain() }</echo>", "joan@\n Joan@Example.COM \n", "KScript.Terminal"));
        Assert.Equal("Who: \n@joan\n", RunBody("<input into=\"h\" type=\"handle\">Who: </input><echo>$h</echo>", "@Joan\n", "KScript.Terminal"));
        Assert.Equal("Id: \n4\n", RunBody("<input into=\"u\" type=\"uuid\">Id: </input><echo>{ $u->version() }</echo>", "3F2A8C1E-9B7D-4C2A-8E1F-0A9B8C7D6E5F\n", "KScript.Terminal"));
        Assert.Equal("Password: \n******** true\n", RunBody("<input into=\"p\" type=\"password\">Password: </input><echo>$p { $p->reveal() == ' secret ' }</echo>", " secret \n", "KScript.Terminal"));
        Assert.Contains("Nope", RunBody("<input into=\"u\" type=\"uuid\" error=\"Nope\">Id: </input>", "x\n3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\n", "KScript.Terminal"));
        string many = string.Concat(Enumerable.Repeat("x\n", 101));
        Assert.Contains("No valid uuid after 100 tries", Assert.Throws<KScriptException>(() => RunBody("<input into=\"u\" type=\"uuid\">?</input>", many, "KScript.Terminal")).Message);
    }

    [Fact]
    public void SchemasForOtherNamesAndUnknownTypes()
    {
        var scope = new Scope();
        var defs = new Dictionary<string, object>();
        Assert.Equal("uri", Shapes.SchemaFor("url", scope, null, defs, "#/components/schemas/")["format"]);
        Assert.Null(Shapes.SchemaFor("Nope", scope, null, defs, "#/components/schemas/"));
        Assert.Null(Shapes.ResolveNamed("Nope", scope, null));
    }
}
