using KScript.Http;
using KScript.Pages;
using KScript.Runtime;

namespace KScript.Tests;

public class HttpCachingTests
{
    private const string Controller = """
        <kscript>
          <import package="KScript.Http" />
          <fn name="person" params="id">
            <do>@etag('v' + $id)</do>
            <return value="{ id: $id }" />
          </fn>
          <fn name="weak">
            <do>@etag({ v: 1 }, weak: true)</do>
            <return value="'weak'" />
          </fn>
          <fn name="tagged">
            <return value="@response('hi')->with_etag()->with_cache(300)" />
          </fn>
          <fn name="tagged_value">
            <return value="@response('hi')->with_etag('v1', weak: true)->with_cache('no-store')" />
          </fn>
          <fn name="private">
            <return value="@response('mine')->with_cache('private, max-age=60')" />
          </fn>
          <fn name="missing">
            <do>@abort(404)</do>
          </fn>
        </kscript>
        """;

    private const string Routes = """
        <KScript.Routes>
          <import package="KScript.IO" />
          <import path="c.ks.xml" as="c" />
          <Get path="/tagged" etag="yes" cache="60"><return value="'same'" /></Get>
          <Get path="/weak" middleware="etag:weak"><return value="'w'" /></Get>
          <Get path="/strong" middleware="etag:strong, cache_control:private;max_age=10"><return value="'s'" /></Get>
          <Get path="/person/{id}" to="c.person" />
          <Get path="/weak-person" to="c.weak" />
          <Get path="/by-hand" to="c.tagged" middleware="etag" />
          <Get path="/by-hand-value" to="c.tagged_value" cache="300" />
          <Get path="/missing" to="c.missing" etag="yes" cache="60" />
          <Post path="/post" etag="yes" cache="60"><return value="'posted'" /></Post>
          <Get path="/counted" middleware="cache_response:1m"><return value="'count ' + @counter()" /></Get>
          <Get path="/counted-default" middleware="cache_response, etag"><return value="'count ' + @counter()" /></Get>
          <Get path="/private" to="c.private" middleware="cache_response" />
          <Get path="/sets-cookie" middleware="cache_response"><return value="@response('count ' + @counter())->with_header('Set-Cookie', 'seen=1')" /></Get>
          <Post path="/counted-post" middleware="cache_response"><return value="'count ' + @counter()" /></Post>
          <fn name="counter">
            <do>@write_text('count.txt', ((@exists('count.txt') ? @read_text('count.txt') : '0')->to_number() + 1)->to_string())</do>
            <return value="@read_text('count.txt')" />
          </fn>
        </KScript.Routes>
        """;

    private static TempSite Site(string routes = Routes) => new TempSite(("c.ks.xml", Controller), ("routes.ks.xml", routes));

    private static PageResult Send(HttpApp app, string method, string path, string ifNoneMatch = null, string authorization = null)
    {
        var headers = new Dictionary<string, object>();
        if (ifNoneMatch != null) headers["if-none-match"] = ifNoneMatch;
        if (authorization != null) headers["authorization"] = authorization;
        return app.Handle(new Dictionary<string, object> { ["method"] = method, ["path"] = path, ["url"] = path, ["headers"] = headers });
    }

    [Fact]
    public void EtagRoutesAnswerNotModifiedWhenTheClientHasTheVersion()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        PageResult first = Send(app, "GET", "/tagged");
        Assert.Equal(200, first.Status);
        string tag = first.Headers["ETag"];
        Assert.Matches("^\"[A-Za-z0-9_-]{27}\"$", tag);
        Assert.Equal("public, max-age=60", first.Headers["Cache-Control"]);

        PageResult again = Send(app, "GET", "/tagged", ifNoneMatch: "\"other\", W/" + tag);
        Assert.Equal(304, again.Status);
        Assert.Equal(string.Empty, again.Body);
        Assert.Equal(tag, again.Headers["ETag"]);
        Assert.Equal("public, max-age=60", again.Headers["Cache-Control"]);

        Assert.Equal(304, Send(app, "HEAD", "/tagged", ifNoneMatch: "*").Status);
        Assert.Equal(200, Send(app, "GET", "/tagged", ifNoneMatch: "\"other\"").Status);
        Assert.Equal(200, Send(app, "GET", "/tagged", ifNoneMatch: " ").Status);
    }

    [Fact]
    public void EtagsAreOnlyForSuccessfulReads()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        PageResult posted = Send(app, "POST", "/post");
        Assert.False(posted.Headers.ContainsKey("ETag"));
        Assert.False(posted.Headers.ContainsKey("Cache-Control"));
        PageResult missing = Send(app, "GET", "/missing");
        Assert.Equal(404, missing.Status);
        Assert.False(missing.Headers.ContainsKey("ETag"));
        Assert.False(missing.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public void WeakAndStrongTagsAndCacheControlMiddleware()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        string weak = Send(app, "GET", "/weak").Headers["ETag"];
        Assert.StartsWith("W/\"", weak);
        Assert.Equal(304, Send(app, "GET", "/weak", ifNoneMatch: weak.Substring(2)).Status);
        PageResult strong = Send(app, "GET", "/strong");
        Assert.StartsWith("\"", strong.Headers["ETag"]);
        Assert.Equal("private, max-age=10", strong.Headers["Cache-Control"]);
    }

    [Fact]
    public void ControllersNameTheirVersionWithEtag()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        PageResult first = Send(app, "GET", "/person/3");
        Assert.Equal("{\"id\":\"3\"}", first.Body);
        Assert.Equal(HttpCaching.Tag("v3"), first.Headers["ETag"]);
        PageResult again = Send(app, "GET", "/person/3", ifNoneMatch: first.Headers["ETag"]);
        Assert.Equal(304, again.Status);
        Assert.Equal(first.Headers["ETag"], again.Headers["ETag"]);
        Assert.Equal(200, Send(app, "GET", "/person/4", ifNoneMatch: first.Headers["ETag"]).Status);
        Assert.Equal(HttpCaching.Tag("{\"v\":1}", weak: true), Send(app, "GET", "/weak-person").Headers["ETag"]);
    }

    [Fact]
    public void ResponsesCanBeTaggedByHand()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        PageResult hand = Send(app, "GET", "/by-hand");
        Assert.Equal(HttpCaching.Tag("hi"), hand.Headers["ETag"]);
        Assert.Equal("public, max-age=300", hand.Headers["Cache-Control"]);
        Assert.Equal(304, Send(app, "GET", "/by-hand", ifNoneMatch: HttpCaching.Tag("hi")).Status);
        PageResult value = Send(app, "GET", "/by-hand-value");
        Assert.Equal(HttpCaching.Tag("v1", weak: true), value.Headers["ETag"]);
        Assert.Equal("no-store", value.Headers["Cache-Control"]);
    }

    [Fact]
    public void KeptAnswersAreNeverSharedWithOrTakenFromVisitorsWithCookies()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        PageResult WithCookie(string path) => app.Handle(new Dictionary<string, object>
        {
            ["method"] = "GET", ["path"] = path, ["url"] = path,
            ["headers"] = new Dictionary<string, object> { ["cookie"] = "who=ada" },
            ["cookies"] = new Dictionary<string, object> { ["who"] = "ada" },
        });

        // A visitor with a cookie (a session, a login) always gets an answer made for them, and it isn't kept.
        PageResult mine = WithCookie("/counted");
        Assert.Equal(("count 1", "MISS"), (mine.Body, mine.Headers["X-Cache"]));
        PageResult anonymous = Send(app, "GET", "/counted");
        Assert.Equal(("count 2", "MISS"), (anonymous.Body, anonymous.Headers["X-Cache"]));
        Assert.Equal("count 3", WithCookie("/counted").Body);
        Assert.Equal(("count 2", "HIT"), (Send(app, "GET", "/counted").Body, Send(app, "GET", "/counted").Headers["X-Cache"]));

        // An answer that sets a cookie is one visitor's: it is made again for the next.
        PageResult first = Send(app, "GET", "/sets-cookie");
        Assert.Equal(("count 4", "MISS"), (first.Body, first.Headers["X-Cache"]));
        PageResult second = Send(app, "GET", "/sets-cookie");
        Assert.Equal(("count 5", "MISS"), (second.Body, second.Headers["X-Cache"]));
    }

    [Fact]
    public void CacheResponseKeepsWholeAnswersInMemory()
    {
        using var site = Site();
        var app = new HttpApp(site.Root);
        PageResult miss = Send(app, "GET", "/counted");
        Assert.Equal(("count 1", "MISS"), (miss.Body, miss.Headers["X-Cache"]));
        PageResult hit = Send(app, "GET", "/counted");
        Assert.Equal(("count 1", "HIT"), (hit.Body, hit.Headers["X-Cache"]));
        Assert.Equal("count 2", Send(app, "GET", "/counted", authorization: "Bearer x").Body);
        Assert.Equal("count 3", Send(app, "POST", "/counted-post").Body);
        Assert.Equal("count 4", Send(app, "POST", "/counted-post").Body);

        PageResult tagged = Send(app, "GET", "/counted-default");
        Assert.Equal("count 5", tagged.Body);
        PageResult cachedTag = Send(app, "GET", "/counted-default", ifNoneMatch: tagged.Headers["ETag"]);
        Assert.Equal((304, "HIT"), (cachedTag.Status, cachedTag.Headers["X-Cache"]));
        Assert.Equal(("count 5", "HIT"), (Send(app, "GET", "/counted-default").Body, "HIT"));

        Assert.Equal("MISS", Send(app, "GET", "/private").Headers["X-Cache"]);
        Assert.Equal("MISS", Send(app, "GET", "/private").Headers["X-Cache"]);
    }

    [Theory]
    [InlineData("300", "public, max-age=300")]
    [InlineData("private; 60", "private, max-age=60")]
    [InlineData("no-store", "no-store")]
    [InlineData("Public, MAX_AGE=10, immutable", "public, max-age=10, immutable")]
    [InlineData("s-maxage = 30, stale-while-revalidate=5", "s-maxage=30, stale-while-revalidate=5")]
    [InlineData("no-cache, no-cache", "no-cache")]
    public void CacheControlShortForms(string text, string expected) => Assert.Equal(expected, HttpCaching.CacheControl(text));

    [Theory]
    [InlineData("forever", "isn't a Cache-Control directive")]
    [InlineData("max-age=soon", "isn't a Cache-Control directive")]
    [InlineData("max-age=", "isn't a Cache-Control directive")]
    [InlineData("color=1", "isn't a Cache-Control directive")]
    [InlineData(" , ", "at least one directive")]
    [InlineData(null, "at least one directive")]
    public void BadCacheControlIsExplained(string text, string message) =>
        Assert.Contains(message, Assert.Throws<KScriptException>(() => HttpCaching.CacheControl(text)).Message);

    [Fact]
    public void MistakesAreReportedWithTheRoute()
    {
        using var badAttribute = Site("<KScript.Routes><Get path=\"/x\" cache=\"forever\"><return value=\"1\" /></Get></KScript.Routes>");
        Assert.Contains("t a Cache-Control directive", Send(new HttpApp(badAttribute.Root), "GET", "/x").Body);

        using var badEtag = Site("<KScript.Routes><Get path=\"/x\" middleware=\"etag:sometimes\"><return value=\"1\" /></Get></KScript.Routes>");
        Assert.Contains("use etag, etag:strong or etag:weak", Send(new HttpApp(badEtag.Root), "GET", "/x").Body);

        using var noDirectives = Site("<KScript.Routes><Get path=\"/x\" middleware=\"cache_control\"><return value=\"1\" /></Get></KScript.Routes>");
        Assert.Contains("cache_control needs directives", Send(new HttpApp(noDirectives.Root), "GET", "/x").Body);

        using var badTtl = Site("<KScript.Routes><Get path=\"/x\" middleware=\"cache_response:later\"><return value=\"1\" /></Get></KScript.Routes>");
        Assert.Contains("should be a length of time", Send(new HttpApp(badTtl.Root), "GET", "/x").Body);
    }

    [Fact]
    public void EtagOutsideARequestIsExplained()
    {
        var ex = ScriptRunner.Fails("<do>@etag(1)</do>", "KScript.Http");
        Assert.Contains("@etag() works while kscript serve handles a request", ex.Message);
    }

    [Fact]
    public void TagsAndMatching()
    {
        Assert.Equal(HttpCaching.Tag(string.Empty), HttpCaching.Tag(null));
        Assert.Equal(HttpCaching.Tag("[1,2]"), HttpCaching.TagFor(new List<object> { 1.0, 2.0 }, weak: false));
        Assert.False(HttpCaching.Matches("\"a\"", null));
        Assert.False(HttpCaching.Matches(null, "\"a\""));
        Assert.True(HttpCaching.Matches("W/\"a\"", "\"a\""));
        Assert.True(HttpCaching.Matches("\"a\"", "W/\"a\""));
        Assert.Contains("etag", HttpPackage.BuiltInMiddleware);
    }
}
