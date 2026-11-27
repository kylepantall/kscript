using KScript.Runtime;

namespace KScript.Tests;

/// <summary>One-of parameter groups at run time: <c>add|remove: file</c> takes exactly one member, by name.</summary>
public sealed class OneOfParameterTests
{
    private const string Board = """
        fn file(note_id: int, add|remove: string, notify: bool = true) {
          echo "{ $note_id } { $add ?? '-' } { $remove ?? '-' } { $notify }"
        }

        fn send(to: string, (text: string | template: int), at|after?: string) {
          echo "{ $to } { $text ?? '-' } { $template ?? 0 } { $at ?? '-' } { $after ?? '-' }"
        }

        """;

    private static string Run(string code) => ScriptRunner.Run(Board + code, path: Path.Combine(Path.GetTempPath(), "one-of.kscript"));

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Run(code)).Message;

    [Theory]
    [InlineData("@file(1, add: 'a')", "1 a - true\n")]
    [InlineData("@file(2, remove: 'b', false)", "2 - b false\n")]
    [InlineData("@file(remove: 'b', note_id: 3)", "3 - b true\n")]
    [InlineData("@file(4, add: null, remove: 'b')", "4 - b true\n")]
    [InlineData("@send('x', template: 3, after: '5m')", "x - 3 - 5m\n")]
    [InlineData("@send('y', text: 'hi')", "y hi 0 - -\n")]
    public void A_call_passes_one_member_of_each_group(string call, string output) => Assert.Equal(output, Run(call));

    [Theory]
    [InlineData("@file(1, add: 'a', remove: 'b')", "@file() takes add: or remove:, not both.")]
    [InlineData("@file(1)", "@file() needs add: or remove:.")]
    [InlineData("@send('z', text: 'a', at: 'now', after: '1m')", "@send() takes at: or after:, not both.")]
    [InlineData("@send('z', 'hi')", "@send() takes 'text' by name, as one of a group: write text: and the value.")]
    public void Two_members_none_of_a_required_group_or_a_positional_member_is_an_error(string call, string message) =>
        Assert.Equal(message, Fails(call));

    [Theory]
    [InlineData("fn f(to: string, text: string | template: int) {\n  echo 1\n}\n@f('a', text: 'b')", "Put brackets around a group")]
    [InlineData("fn f(add|remove: string = 'x') {\n  echo 1\n}\n@f(add: 'a')", "can't have a default")]
    public void A_badly_written_group_says_how_to_write_it(string code, string message) =>
        Assert.Contains(message, Fails(code));

    [Fact]
    public void A_function_shows_its_groups_as_written()
    {
        List<Parameter> parameters = Parameter.ParseList("note_id: int, add|remove: file, (text: string | template: Template)?", out string error);
        Assert.Null(error);
        Assert.Equal("note_id: int, add|remove: file, (text: string | template: Template)?", Parameter.Label(parameters));
    }

    [Theory]
    [InlineData("@api('/x', post: 1, put: 2)", "@api() takes one of post:, put:, patch: or delete:, not more than one.")]
    public void Built_in_groups_are_checked_by_the_binder(string call, string message)
    {
        var error = Assert.Throws<KScriptException>(() => ScriptRunner.Run("import KScript.Net\n" + call + "\n", path: Path.Combine(Path.GetTempPath(), "api.kscript")));
        Assert.Equal(message, error.Message);
    }
}
