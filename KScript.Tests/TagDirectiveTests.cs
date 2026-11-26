using KScript.Pages;
using KScript.Runtime;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>@if and @each on a tag, and short functions (.field, ->method()).</summary>
public class TagDirectiveTests
{
    [Fact]
    public void IfAndEachOnATagRenderLikeTheWrappingTags()
    {
        using var site = new TempSite(("index.html", """
            <let name="tasks" value="[{ title: 'Write', done: true }, { title: 'Ship', done: false }]" />
            <let name="note" value="''" />
            <ul><li @each($tasks, as: task, at: i)>{$i}:{$task.title}</li></ul>
            <ol><li @each($tasks, as: task) @if(not $task.done)>{$task.title}</li></ol>
            <p @if($note)>Note</p><p @if(not $note)>No note</p>
            """));
        string body = site.Render("index.html").Body;
        Assert.Contains("<ul><li>0:Write</li><li>1:Ship</li></ul>", body);
        Assert.Contains("<ol><li>Ship</li></ol>", body);
        Assert.DoesNotContain(">Note<", body);
        Assert.Contains("<p>No note</p>", body);
        Assert.DoesNotContain("@if", body);
        Assert.DoesNotContain("@each", body);
    }

    [Theory]
    [InlineData("<li @each(as: task)>x</li>", "@each")]
    [InlineData("<li @each($tasks)>x</li>", "@each needs as: with a name for each item")]
    [InlineData("<li @each($tasks, as: task, by: 2)>x</li>", "@each takes the list, then as: for each item's name and at: for its position")]
    [InlineData("<li @each($tasks, as: task, as: other)>x</li>", "twice")]
    public void AnEachThatIsMissingAPartSaysWhatToWrite(string tag, string message)
    {
        using var site = new TempSite(("index.html", "<let name=\"tasks\" value=\"[1]\" />" + tag));
        var error = Assert.Throws<PageException>(() => site.Render("index.html"));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("[{ n: 2 }, { n: 1 }]->sort_by(.n)->map(.n)->join(',')", "1,2")]
    [InlineData("[' b', 'a ']->map(->trim())->join(',')", "b,a")]
    [InlineData("[' b ', 'a']->map(->trim()->upper())->join(',')", "B,A")]
    [InlineData("[{ o: { n: 'x' } }]->map(.o.n)->join(',')", "x")]
    [InlineData("[{ paid: true, n: 1 }, { paid: false, n: 2 }]->filter(.paid)->map(.n)->join(',')", "1")]
    public void AShortFunctionStandsForTheItem(string expression, string expected)
    {
        Assert.Equal(expected, Eval(expression));
    }
}
