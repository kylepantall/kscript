using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>Printing JSON for people: maps and nested lists are indented, and coloured in a terminal.</summary>
public class JsonOutputTests
{
    private const string Person = "<let name=\"p\" value=\"{ name: 'Joan', age: 82, tags: ['a', 'b'], address: { city: 'Leeds' }, ok: true, x: null }\" />";

    private const string Indented = "{\n  \"name\": \"Joan\",\n  \"age\": 82,\n  \"tags\": [\"a\", \"b\"],\n  \"address\": {\n    \"city\": \"Leeds\"\n  },\n  \"ok\": true,\n  \"x\": null\n}\n";

    [Theory]
    [InlineData("<echo>$p</echo>")]
    [InlineData("<echo>{ $p }</echo>")]
    [InlineData("<echo>\n    $p\n  </echo>")]
    [InlineData("<echo value=\"$p\" />")]
    [InlineData("<echo value=\"$p\" format=\"json\" />")]
    public void AMapOnItsOwnPrintsAsIndentedJson(string echo)
    {
        Assert.Equal(Indented, RunBody(Person + echo));
    }

    [Fact]
    public void ValuesInsideTextStayOnOneLine()
    {
        Assert.Equal("Person: {\"name\":\"Joan\"}\n", RunBody("<let name=\"p\" value=\"{ name: 'Joan' }\" /><echo>Person: $p</echo>"));
        Assert.Equal("1, 2\n", RunBody("<echo>{ [1, 2] }</echo>"));
        Assert.Equal("{\"a\":1}\n", RunBody("<echo>{ { a: 1 }->to_json() }</echo>"));
    }

    [Fact]
    public void ListsOfMapsAreIndented()
    {
        Assert.Equal("[\n  {\n    \"n\": 1\n  },\n  {\n    \"n\": 2\n  }\n]\n", RunBody("<echo>{ [{ n: 1 }, { n: 2 }] }</echo>"));
    }

    [Fact]
    public void JsonTextIsReformatted()
    {
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": [1, 2],\n  \"c\": {}\n}\n", RunBody("<echo format=\"json\">{\"a\":1,\"b\":[1,2],\"c\":{}}</echo>"));
    }

    [Fact]
    public void LongListsGetOneValuePerLine()
    {
        string output = RunBody("<echo>{ { xs: (0..40)->map(fn(i) => 'item' + $i) } }</echo>");
        Assert.StartsWith("{\n  \"xs\": [\n    \"item0\",\n", output);
    }

    [Fact]
    public void ColoursChangeNothingButTheColours()
    {
        string json = JsonText.Pretty(Values.ParseJson("{\"name\":\"Jo \\\"J\\\"\",\"n\":-1.5e3,\"ok\":false,\"x\":null,\"l\":[true]}"));
        string coloured = JsonText.Colourize(json);
        Assert.Contains("\u001b[1;34m\"name\"\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Contains("\u001b[32m\"Jo \\\"J\\\"\"\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Contains("\u001b[36m-1500\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Contains("\u001b[33mfalse\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Contains("\u001b[90mnull\u001b[0m", coloured, StringComparison.Ordinal);
        Assert.Equal(json, System.Text.RegularExpressions.Regex.Replace(coloured, "\u001b\\[[0-9;]*m", string.Empty));
    }

    [Fact]
    public void NoColoursWhenNotATerminal()
    {
        Assert.False(RunBody(Person + "<echo>$p</echo>").Contains('\u001b'));
    }
}
