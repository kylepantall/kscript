using System.Globalization;

using KScript.Pages;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class I18nTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-i18n-" + Guid.NewGuid().ToString("N"));

    public I18nTests()
    {
        Directory.CreateDirectory(dir);
        FileOf("locales/en.json", "{ \"hello\": \"Hello, {name}!\", \"cart\": { \"items\": { \"=0\": \"Your cart is empty\", \"one\": \"{count} item\", \"other\": \"{count} items\" } }, \"only_en\": \"English only\", \"total\": \"Total: {amount}\", \"n\": 5, \"list\": [1] }");
        FileOf("locales/fr.yaml", "hello: Bonjour, {name} !\ncart:\n  items:\n    one: \"{count} article\"\n    other: \"{count} articles\"\n");
        FileOf("locales/ru.json", "{ \"files\": { \"one\": \"{count} файл\", \"few\": \"{count} файла\", \"many\": \"{count} файлов\", \"other\": \"{count} файла\" } }");
        FileOf("locales/de/shop.json", "{ \"title\": \"Laden\" }");
        FileOf("locales/de.json", "{ \"hello\": \"Hallo {name}\" }");
        FileOf("locales/not a locale.json", "{}");
        FileOf("locales/notes.txt", "ignored");
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string FileOf(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
        return path;
    }

    private (string Output, string Error) Run(string body, Action<KScriptHost> setup = null)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var error = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = new KScriptHost(output, error);
        host.Logs.Clock = () => new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        setup?.Invoke(host);
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) }).RunText("<kscript>" + body + "</kscript>", Path.Combine(dir, "app.ks.xml"));
        return (output.ToString(), error.ToString());
    }

    private string Out(string body, Action<KScriptHost> setup = null) => Run(body, setup).Output;

    [Fact]
    public void Translates_with_values_plurals_and_fallbacks()
    {
        string output = Out("<locale set=\"fr\" />" +
            "<echo>{ @t('hello', { name: 'Ann' }) }</echo>" +
            "<echo>{ @t('cart.items', { count: 0 }) } | { @t('cart.items', { count: 1 }) } | { @t('cart.items', { count: 3 }) }</echo>" +
            "<echo>{ @t('only_en') } | { @t('n') } | { @t('hello', { name: 'Bo' }, 'en') } | { @t('hello', {}, 'de') } | { @t('shop.title', {}, 'de-AT') }</echo>" +
            "<echo>{ @locale() } { @locales()->join(',') } { @has_t('only_en') } { @has_t('nope') }</echo>");
        Assert.Equal("Bonjour, Ann !\n0 article | 1 article | 3 articles\nEnglish only | 5 | Hello, Bo! | Hallo {name} | Laden\nfr de,en,fr,ru true false\n", output);
    }

    [Fact]
    public void Exact_forms_numbers_and_missing_keys()
    {
        var (output, error) = Run("<locale set=\"en\" />" +
            "<echo>{ @t('cart.items', { count: 0 }) } | { @t('cart.items', { count: 1234 }) } | { @t('total', { amount: @format_currency(9.5, 'GBP') }) }</echo>" +
            "<echo>{ @t('missing.key') } { @t('missing.key') } { @t('list') }</echo>");
        Assert.Equal("Your cart is empty | 1,234 items | Total: £9.50\nmissing.key missing.key list\n", output);
        Assert.Equal("09:00:00 WARN  [i18n] Missing translation 'missing.key' for en\n09:00:00 WARN  [i18n] Missing translation 'list' for en\n", error);
    }

    [Fact]
    public void Plural_rules_for_many_languages()
    {
        Assert.Equal("1 файл, 3 файла, 5 файлов, 21 файл, 1.5 файла", Out("<locale set=\"ru\" /><echo>{ [1, 3, 5, 21, 1.5]->map(fn(n) => @t('files', { count: $n }))->join(', ') }</echo>").TrimEnd().Replace("1,5", "1.5"));
        string Plural(string locale, double n) => Translator.Plural(locale, n);
        Assert.Equal("one", Plural("en", 1));
        Assert.Equal("other", Plural("en", 1.5));
        Assert.Equal("other", Plural("ja", 1));
        Assert.Equal("one", Plural("fr", 0));
        Assert.Equal("one", Plural("pt-BR", 1));
        Assert.Equal("other", Plural("pt-PT", 0));
        Assert.Equal("few", Plural("uk", 22));
        Assert.Equal("many", Plural("ru", 11));
        Assert.Equal("one", Plural("pl", 1));
        Assert.Equal("few", Plural("pl", 3));
        Assert.Equal("many", Plural("pl", 12));
        Assert.Equal("other", Plural("pl", 2.5));
        Assert.Equal("one", Plural("cs", 1));
        Assert.Equal("few", Plural("sk", 3));
        Assert.Equal("other", Plural("cs", 5));
        Assert.Equal("many", Plural("cs", 1.5));
        Assert.Equal(new[] { "zero", "one", "two", "few", "many", "other", "other" }, new double[] { 0, 1, 2, 5, 11, 100, 0.5 }.Select(n => Plural("ar", n)));
        Assert.Equal(new[] { "one", "two", "other", "other" }, new double[] { 1, 2, 3, 1.5 }.Select(n => Plural("he", n)));
        Assert.Equal(new[] { "one", "two", "few", "many", "other", "other" }, new double[] { 1, 2, 4, 8, 11, 0.5 }.Select(n => Plural("ga", n)));
        Assert.Equal(new[] { "zero", "one", "two", "few", "many", "other", "other" }, new double[] { 0, 1, 2, 3, 6, 4, 0.5 }.Select(n => Plural("cy", n)));
        Assert.Equal("other", Plural(null, 0));
        Assert.Equal("other", Plural("ru", 2.5));
        Assert.Equal("few\n", Out("<echo>{ @plural(3, 'ru') }</echo>"));
        Assert.Equal("one\n", Out("<echo>{ @plural(1) }</echo>"));
    }

    [Fact]
    public void Locale_formatting()
    {
        string output = Out("<locale set=\"fr\" />" +
            "<echo>{ @format_number(1234.5) }|{ @format_number(2, 2) }|{ @format_percent(0.256, 1) }|{ @format_currency(9.5, 'EUR') }|{ @format_currency(1000, 'JPY', 'en') }|{ @format_currency(3, 'xyz', 'en') }</echo>" +
            "<echo>{ @format_date('2026-01-02', 'short', 'en-US') }|{ @format_date('2026-01-02', 'long', 'en-US') }|{ @format_date('2026-01-02', 'medium', 'en-US') }|{ @format_date('2026-01-02', 'd MMMM yyyy', 'en') }|{ @format_number(1, null, 'en') }</echo>");
        string[] lines = output.Split('\n');
        Assert.Equal("1 234,5|2,00|25,6 %|9,50 €|¥1,000|XYZ3.00", lines[0].Replace('\u202f', ' ').Replace('\u00a0', ' '));
        Assert.Equal("1/2/2026|January 2, 2026|Jan 2, 2026|2 January 2026|1", lines[1]);
        Assert.Contains("isn't a currency code", ScriptRunner.Fails("<echo>{ @format_currency(1, 'EURO') }</echo>").Message);
        Assert.Contains("needs a date", ScriptRunner.Fails("<echo>{ @format_date(3) }</echo>").Message);
        Assert.Contains("needs a date", ScriptRunner.Fails("<echo>{ @format_date('not a date') }</echo>").Message);
        Assert.NotEmpty(Formats.Date(new DateTime(2026, 1, 2, 13, 5, 0), "full", "en"));
        Assert.Equal("13:05", Formats.Date(new DateTime(2026, 1, 2, 13, 5, 0), "time", "fr"));
        Assert.Equal("02/01/2026 13:05", Formats.Date(new DateTime(2026, 1, 2, 13, 5, 0), "datetime", "fr"));
        Assert.Equal("Jan 2, 2026", Formats.Date(new DateTime(2026, 1, 2), null, "en-US"));
        Assert.Equal("1,234.5", Formats.Number(1234.5, null, null));
        Assert.Equal(CultureInfo.InvariantCulture, Formats.Culture("not a culture!"));
    }

    [Fact]
    public void Locale_element_picks_from_wishes_and_config_sets_folder_and_default()
    {
        Assert.Equal("fr fr en\n", Out("<locale from=\"'fr-CH, fr;q=0.9, en;q=0.8'\" /><let name=\"a\" value=\"@locale()\" />" +
            "<locale from=\"['xx', 'FR_ca']\" /><let name=\"b\" value=\"@locale()\" /><locale from=\"'zz'\" /><echo>{$a} {$b} { @locale() }</echo>"));
        Assert.Contains("<locale> needs set", Assert.Throws<KScriptException>(() => Out("<locale />")).Message);
        Assert.Contains("isn't a locale", Assert.Throws<KScriptException>(() => Out("<locale set=\"en gb\" />")).Message);

        FileOf("lang/en-GB.yaml", "colour: Colour\n");
        FileOf("lang/es.yaml", "colour: Color\n");
        FileOf("kscript.json", """{"config": {"i18n": {"folder": "lang", "default": "en_gb", "locale": "es"}}}""");
        Assert.Equal("es Color Colour /es/x\n", Out("<echo>{ @locale() } { @t('colour') } { @t('colour', {}, 'de') } { @locale_path('/x') }</echo>"));
        File.Delete(Path.Combine(dir, "kscript.json"));
        Assert.Equal("Colour\n", Out("<translations folder=\"lang\" default=\"en-GB\" /><locale set=\"en-GB\" /><echo>{ @t('colour') }</echo>"));
        FileOf("kscript.json", """{"config": {"i18n": 3}}""");
        Assert.Contains("i18n should be a map", Assert.Throws<KScriptException>(() => Out("<echo>{ @t('x') }</echo>")).Message);
        File.Delete(Path.Combine(dir, "kscript.json"));
    }

    [Fact]
    public void Locale_paths_for_language_links()
    {
        Assert.Equal("/fr/about /about /fr/about /de / /fr/x\n", Out(
            "<echo>{ @locale_path('/about', 'fr') } { @locale_path('/fr/about', 'en') } { @locale_path('/de/about', 'fr') } { @locale_path('/', 'de') } { @locale_path('', 'en') } { @locale_path('x', 'fr') }</echo>"));
        Assert.Equal("/fr/shop\n", Out("<locale set=\"fr\" /><echo>{ @locale_path('/shop') }</echo>"));
    }

    [Fact]
    public void Bad_translations_are_reported()
    {
        Assert.Contains("needs a count", Assert.Throws<KScriptException>(() => Out("<echo>{ @t('cart.items') }</echo>")).Message);
        Assert.Contains("values should be a map", Assert.Throws<KScriptException>(() => Out("<echo>{ @t('hello', 'Ann') }</echo>")).Message);
        FileOf("locales/xx.json", "[1]");
        Assert.Contains("xx.json should hold translations by key", Assert.Throws<KScriptException>(() => Out("<echo>{ @t('a', {}, 'xx') }</echo>")).Message);
        FileOf("locales/yy.yaml", "");
        Assert.Equal("a\n", Out("<echo>{ @t('a', {}, 'yy') }</echo>"));
        FileOf("locales/zz.json", "{ \"p\": { \"one\": \"x\" } }");
        Assert.Equal("p\n", Out("<echo>{ @t('p', { count: 5 }, 'zz') }</echo>"));
        FileOf("locales/zz.json", "{ \"p\": { \"one\": \"x\", \"other\": \"{count} {missing} {name.first}\" } }");
        Assert.Equal("5 {missing} Ann\n", Out("<echo>{ @t('p', { count: 5, name: { first: 'Ann' } }, 'zz') }</echo>"));
    }

    [Fact]
    public void Accept_language_and_normalising()
    {
        Assert.Equal(new[] { "fr-CH", "en", "de" }, Translator.AcceptLanguage("de;q=0.5, fr-CH, *, en;q=0.9, it;q=0"));
        Assert.Empty(Translator.AcceptLanguage(null));
        Assert.Equal("en-GB", Translator.Normalise("EN_gb"));
        Assert.Equal("zh-Hant-TW", Translator.Normalise("zh-Hant-tw"));
        Assert.Null(Translator.Normalise(" "));
        Assert.Contains("isn't a locale", Assert.Throws<KScriptException>(() => Translator.Normalise("-")).Message);
        var tr = new Translator(KScriptHost.ForText(new StringWriter()), dir);
        Assert.Null(tr.Best(new[] { "!!", "xx" }));
        Assert.Equal("de", tr.Best(new[] { "de-CH" }));
        Assert.Equal(new[] { "pt-BR", "pt", "en-GB", "en" }, new Translator(KScriptHost.ForText(new StringWriter()), dir, null, "en-GB").Chain("pt-BR"));
        Assert.Null(tr.FromRequest(new Dictionary<string, object>()));
        Assert.Equal("de", tr.FromRequest(new Dictionary<string, object> { ["cookies"] = new Dictionary<string, object> { ["lang"] = "de" } }));
        Assert.Equal("ru", tr.FromRequest(new Dictionary<string, object> { ["query"] = new Dictionary<string, object> { ["lang"] = "ru" }, ["cookies"] = new Dictionary<string, object> { ["lang"] = "de" } }));
        Assert.Equal("fr", tr.FromRequest(new Dictionary<string, object> { ["headers"] = new Dictionary<string, object> { ["accept-language"] = "fr-FR,en;q=0.5" } }));
        Assert.Empty(new Translator(KScriptHost.ForText(new StringWriter()), Path.Combine(dir, "nowhere")).Available);
    }

    [Fact]
    public void KSCRIPT_LOCALE_picks_the_locale_for_scripts()
    {
        string before = Environment.GetEnvironmentVariable("KSCRIPT_LOCALE");
        try
        {
            Environment.SetEnvironmentVariable("KSCRIPT_LOCALE", "de");
            Assert.Equal("de\n", Out("<echo>{ @locale() }</echo>"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("KSCRIPT_LOCALE", before);
        }
    }

    [Fact]
    public async Task Pages_pick_the_locale_from_the_path_query_cookie_or_browser()
    {
        string site = Path.Combine(dir, "site");
        FileOf("site/locales/en.json", "{ \"welcome\": \"Welcome\" }");
        FileOf("site/locales/fr.json", "{ \"welcome\": \"Bienvenue\" }");
        FileOf("site/index.html", "<p>{ @t('welcome') } { @locale() } { @locale_path($request.path, 'fr') }</p>");
        FileOf("site/about.html", "<p>{ @t('welcome') }</p>");
        FileOf("site/fr-files/x.txt", "x");
        var pages = new PageSite(site);

        Assert.Equal("Bienvenue", Body(await Get(pages, "/fr/about")));
        Assert.Equal("Bienvenue fr /fr", Body(await Get(pages, "/fr/")));
        Assert.Equal("Welcome en /fr", Body(await Get(pages, "/")));
        Assert.Equal("Bienvenue fr /fr", Body(await Get(pages, "/?lang=fr")));
        Assert.Equal("Bienvenue fr /fr", Body(await Get(pages, "/", accept: "fr-CA,en;q=0.5")));
        Assert.Equal("Bienvenue fr /fr", Body(await Get(pages, "/", cookie: "lang=fr")));
        Assert.Equal(PageRoute.RouteKind.NotFound, pages.Resolve("/de/about").Kind);
        Assert.Equal(PageRoute.RouteKind.Static, pages.Resolve("/fr-files/x.txt").Kind);
        Assert.Equal("fr", pages.Resolve("/fr/about").Locale);
        Assert.Null(pages.Resolve("/a/b").Locale);
    }

    private static string Body(PageResult result)
    {
        string body = result.Body;
        int start = body.IndexOf("<p>", StringComparison.Ordinal) + 3;
        return body.Substring(start, body.IndexOf("</p>", StringComparison.Ordinal) - start);
    }

    private static Task<PageResult> Get(PageSite site, string url, string accept = null, string cookie = null)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = "GET";
        int q = url.IndexOf('?');
        context.Request.Path = q < 0 ? url : url.Substring(0, q);
        if (q >= 0) context.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(url.Substring(q));
        if (accept != null) context.Request.Headers["Accept-Language"] = accept;
        if (cookie != null) context.Request.Headers["Cookie"] = cookie;
        return PageServer.HandleAsync(site, context.Request);
    }
}
