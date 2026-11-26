using KScript.Pages;
using KScript.Syntax.Markup;

namespace KScript.Tests;

/// <summary>@focus(), @autogrow() and @pending(...): what the renderer hands the browser runtime.</summary>
public class BrowserDirectiveTests
{
    private static string Render(string body)
    {
        using var site = new TempSite(("index.html", "<live id=\"note\"><state name=\"editing\" value=\"true\" /><state name=\"title\" value=\"''\" />" + body + "</live>"));
        return site.Render("index.html").Body;
    }

    [Theory]
    [InlineData("<input @focus()>", "<input data-ks-focus>")]
    [InlineData("<input @focus($editing)>", "<input data-ks-focus>")]
    [InlineData("<input @focus(not $editing)>", "<input>")]
    [InlineData("<input focus=\"{$editing}\">", "<input data-ks-focus>")]
    [InlineData("<input focus>", "<input data-ks-focus>")]
    public void Focus_marks_the_element_while_its_condition_is_true(string tag, string html)
    {
        Assert.Contains(html, Render(tag));
    }

    [Fact]
    public void Autogrow_marks_a_textarea()
    {
        Assert.Contains("<textarea data-ks-autogrow></textarea>", Render("<textarea @autogrow()></textarea>"));
    }

    [Fact]
    public void Pending_names_its_classes_and_the_handlers_that_run_the_method()
    {
        string html = Render("""
            <article @pending('crumple', for: 'delete')>
              <button @on-click(title = 'x')>Rename</button>
              <button @on-click($this->delete())>Delete</button>
            </article>
            """);
        Assert.Contains("data-ks-pending=\"crumple\" data-ks-pending-for=\"1\"", html);
    }

    [Fact]
    public void Pending_without_for_counts_every_action()
    {
        string html = Render("<p @pending(['busy', 'dim'])>Saving</p><button @on-click(title = 'x')>Go</button>");
        Assert.Contains("<p data-ks-pending=\"busy dim\">", html);
    }

    [Theory]
    [InlineData("<input @focus($title)>", "@focus: $title must be true or false")]
    [InlineData("<div @autogrow()></div>", "Use @autogrow() on a textarea.")]
    [InlineData("<p @pending('busy', for: 'delete')>x</p>", "no handler in this component runs delete")]
    [InlineData("<p @pending('not a class')>x</p>", "isn't a class name")]
    public void A_mistake_says_what_to_write(string tag, string message)
    {
        var ex = Assert.ThrowsAny<Exception>(() => Render(tag));
        Assert.Contains(message, System.Net.WebUtility.HtmlDecode(ex.Message));
    }

    [Fact]
    public void The_directives_need_a_live_owner()
    {
        using var site = new TempSite(("index.html", "<textarea @autogrow()></textarea>"));
        var ex = Assert.ThrowsAny<Exception>(() => site.Render("index.html"));
        Assert.Contains("only works in a live component or a <live> block", System.Net.WebUtility.HtmlDecode(ex.Message));
    }

    [Theory]
    [InlineData("$delete", true)]
    [InlineData("call: $delete", true)]
    [InlineData("call: $delete, with: [$note.id]", true)]
    [InlineData("$this->delete($note.id)", true)]
    [InlineData("$this -> delete()", true)]
    [InlineData("$deleted", false)]
    [InlineData("set: $deleted, to: 0", false)]
    [InlineData("$this->delete_all()", false)]
    public void A_handler_runs_a_method_when_it_calls_it(string handler, bool calls)
    {
        Assert.Equal(calls, PendingDirective.HandlerCalls(handler, "delete"));
    }
}
