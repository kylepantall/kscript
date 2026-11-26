using KScript.Pages;
using KScript.Syntax.Markup;

namespace KScript.Tests;

/// <summary>@class(...) switches classes on by conditions, class first or condition first.</summary>
public class ClassDirectiveTests
{
    private static string Render(string body)
    {
        using var site = new TempSite(("index.html", body));
        return site.Render("index.html").Body;
    }

    [Theory]
    [InlineData("<li class=\"item\" @class(done: $done, 'is-open': $open)>x</li>", "<li class=\"item done\">x</li>")]
    [InlineData("<li @class(done: $done, is-open: $closed)>x</li>", "<li class=\"done is-open\">x</li>")]
    [InlineData("<li @class(open: $open)>x</li>", "<li>x</li>")]
    [InlineData("<li @class({ $done: ['done', 'struck'] })>x</li>", "<li class=\"done struck\">x</li>")]
    [InlineData("<li @class({ [$done, $closed]: 'both', [$done, $open]: 'never' })>x</li>", "<li class=\"both\">x</li>")]
    [InlineData("<li @class({ $done or $open: 'any', $count > 2 ? true : false: 'many', })>x</li>", "<li class=\"any many\">x</li>")]
    [InlineData("<li class=\"a\" @class({ $done: 'a', $closed: 'b' })>x</li>", "<li class=\"a b\">x</li>")]
    public void Switches_classes_on_by_their_conditions(string tag, string html)
    {
        string page = "<let>done = true; open = false; closed = true; count = 3</let>" + tag;
        Assert.Contains(html, Render(page));
    }

    [Theory]
    [InlineData("<li @class(done: 'yes')>x</li>", "@class: &#39;yes&#39; must be true or false")]
    [InlineData("<li @class({ true: 'not a class' })>x</li>", "isn&#39;t a class name")]
    [InlineData("<li @class(done)>x</li>", "Each entry is a class and a condition")]
    [InlineData("<li @class({ true: 'a' )>x</li>", "is never closed")]
    [InlineData("<li @class('9lives': true)>x</li>", "isn&#39;t a class name")]
    public void A_mistake_says_what_to_write(string tag, string message)
    {
        var ex = Assert.ThrowsAny<Exception>(() => Render(tag));
        Assert.Contains(System.Net.WebUtility.HtmlDecode(message), System.Net.WebUtility.HtmlDecode(ex.Message));
    }

    [Fact]
    public void An_empty_directive_says_what_it_needs()
    {
        Assert.Contains("@class needs classes and conditions", ClassDirective.Read("  ").Problems[0].Message);
        Assert.Empty(ClassDirective.Read("a: true,").Problems);
    }
}
