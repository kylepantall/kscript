using System.Globalization;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// The native types beyond the first five (url, ip, country, duration, color, card, ...): how each is read and tidied,
/// its methods, values written without quotes, types a package adds, and a project's own natives/ folder.
/// </summary>
public class MoreNativeTypesTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "natives-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    [Theory]
    // Web and network
    [InlineData("url", "'HTTPS://Example.COM/Docs'", "https://example.com/Docs")]
    [InlineData("domain", "'API.Example.com.'", "api.example.com")]
    [InlineData("domain", "'Bücher.example'", "bücher.example")]
    [InlineData("domain", "'xn--bcher-kva.example'", "bücher.example")]
    [InlineData("ip", "'192.168.000.001'", "192.168.0.1")]
    [InlineData("ip", "'2001:0DB8:0000::0001'", "2001:db8::1")]
    [InlineData("cidr", "'10.1.2.3/8'", "10.0.0.0/8")]
    [InlineData("mac", "'00-1A-2B-3C-4D-5E'", "00:1a:2b:3c:4d:5e")]
    [InlineData("port", "8080", "8080")]
    [InlineData("port", "'443'", "443")]
    // Identifiers and formats
    [InlineData("slug", "'my-first-post'", "my-first-post")]
    [InlineData("semver", "'v1.4.2'", "1.4.2")]
    [InlineData("semver", "'2.0.0-beta.1+build.5'", "2.0.0-beta.1+build.5")]
    [InlineData("mime", "'Text/HTML; charset=UTF-8'", "text/html; charset=UTF-8")]
    [InlineData("hex", "'0xA3F9C0'", "a3f9c0")]
    [InlineData("base64", "'aGVsbG8='", "aGVsbG8=")]
    [InlineData("json", "'[1, 2]'", "[1, 2]")]
    // People and places
    [InlineData("phone", "'+44 (0)7700 900-123'", "+447700900123")]
    [InlineData("phone", "'+1 415.555.0100'", "+14155550100")]
    [InlineData("country", "'gb'", "GB")]
    [InlineData("country", "'GBR'", "GB")]
    [InlineData("country", "'United Kingdom'", "GB")]
    [InlineData("country", "'UK'", "GB")]
    [InlineData("currency", "'gbp'", "GBP")]
    [InlineData("locale", "'en_gb'", "en-GB")]
    [InlineData("timezone", "'europe/london'", "Europe/London")]
    [InlineData("timezone", "'UTC'", "UTC")]
    // Time
    [InlineData("duration", "'90m'", "1h30m")]
    [InlineData("duration", "'PT1H30M'", "1h30m")]
    [InlineData("duration", "90", "1m30s")]
    [InlineData("duration", "'2d 3h'", "2d3h")]
    [InlineData("cron", "'*/15 9-17 * * MON-FRI'", "*/15 9-17 * * mon-fri")]
    [InlineData("cron", "'@daily'", "@daily")]
    [InlineData("color", "'#F80'", "#ff8800")]
    [InlineData("color", "'rgb(255, 136, 0)'", "#ff8800")]
    [InlineData("color", "'orange'", "#ffa500")]
    [InlineData("color", "'#ff880080'", "#ff880080")]
    // Money
    [InlineData("iban", "'gb82 west 1234 5698 7654 32'", "GB82WEST12345698765432")]
    [InlineData("card", "'4242 4242 4242 4242'", "•••• 4242")]
    public void EachTypeReadsAndTidiesItsValues(string type, string value, string expected)
    {
        Assert.Equal(expected, Code($"x: {type} = {value}\necho $x"));
        Assert.Equal(type == "port" ? "number" : type, Code($"x: {type} = {value}\necho $x->type()"));
    }

    [Theory]
    [InlineData("url", "'example.com'", "needs a url")]
    [InlineData("url", "'mailto:joan@example.com'", "needs a url")]
    [InlineData("domain", "'-bad.example'", "needs a domain")]
    [InlineData("domain", "'localhost'", "needs a domain")]
    [InlineData("ip", "'256.1.1.1'", "needs an ip")]
    [InlineData("cidr", "'10.0.0.0/33'", "needs a cidr")]
    [InlineData("mac", "'00:1a:2b'", "needs a mac")]
    [InlineData("port", "70000", "needs a port")]
    [InlineData("port", "0", "needs a port")]
    [InlineData("slug", "'Not A Slug'", "needs a slug")]
    [InlineData("semver", "'1.2'", "needs a semver")]
    [InlineData("mime", "'html'", "needs a mime")]
    [InlineData("hex", "'xyz'", "needs a hex")]
    [InlineData("base64", "'a!b'", "needs a base64")]
    [InlineData("jwt", "'a.b'", "needs a jwt")]
    [InlineData("json", "'{a: 1}'", "needs a json")]
    [InlineData("phone", "'07700 900123'", "needs a phone")]
    [InlineData("country", "'Atlantis'", "needs a country")]
    [InlineData("currency", "'ABC'", "needs a currency")]
    [InlineData("locale", "'xx-QQ'", "needs a locale")]
    [InlineData("timezone", "'Mars/Olympus'", "needs a timezone")]
    [InlineData("duration", "'soon'", "needs a duration")]
    [InlineData("cron", "'* * *'", "needs a cron")]
    [InlineData("cron", "'61 * * * *'", "needs a cron")]
    [InlineData("color", "'#ggg'", "needs a color")]
    [InlineData("iban", "'GB00 WEST 1234 5698 7654 32'", "needs an iban")]
    [InlineData("card", "'4242 4242 4242 4241'", "needs a card")]
    public void ValuesOfTheWrongFormFailWhereTheyAreMade(string type, string value, string message)
    {
        Assert.Contains(message, Fails($"x: {type} = {value}\necho $x"));
    }

    [Theory]
    [InlineData("u: url = 'https://example.com:8443/docs?page=2&q=tea#intro'\necho \"{ $u->scheme() } { $u->host() } { $u->port() } { $u->path() } { $u->query().q } { $u->fragment() } { $u->origin() }\"",
        "https example.com 8443 /docs tea intro https://example.com:8443")]
    [InlineData("u: url = 'https://example.com/docs/intro'\necho \"{ $u->join('../blog') } { $u->with_query({ page: 2 }) } { $u->port() }\"",
        "https://example.com/blog https://example.com/docs/intro?page=2 443")]
    [InlineData("d: domain = 'api.example.co.uk'\necho \"{ $d->tld() } { $d->labels()->length() } { $d->is_subdomain_of('example.co.uk') } { $d->is_subdomain_of('other.com') }\"", "uk 4 true false")]
    [InlineData("ip: ip = '10.1.2.3'\necho \"{ $ip->version() } { $ip->in('10.0.0.0/8') } { $ip->private() } { $ip->loopback() }\"", "4 true true false")]
    [InlineData("ip: ip = '::1'\necho \"{ $ip->version() } { $ip->loopback() }\"", "6 true")]
    [InlineData("b: cidr = '192.168.0.0/24'\necho \"{ $b->contains('192.168.0.77') } { $b->contains('192.168.1.1') } { $b->network() } { $b->prefix() } { $b->size() }\"", "true false 192.168.0.0 24 256")]
    [InlineData("echo \"{ @slugify('Crème Brûlée, recipes!') } { @slugify('Crème Brûlée')->words() }\"", "creme-brulee-recipes creme, brulee")]
    [InlineData("v: semver = '1.4.2'\necho \"{ $v->major() } { $v->compare('1.10.0') } { $v->satisfies('^1.2.0') } { $v->satisfies('~1.3') } { $v->satisfies('>=1.0.0 <1.4.0 || 1.4.x') } { $v->bump('major') }\"", "1 -1 true false true 2.0.0")]
    [InlineData("v: semver = '1.0.0-beta.2'\necho \"{ $v->compare('1.0.0') } { $v->compare('1.0.0-beta.10') } { $v->prerelease() } { $v->bump('patch') }\"", "-1 -1 beta.2 1.0.0")]
    [InlineData("m: mime = 'application/vnd.api+json; charset=utf-8'\necho \"{ $m->main_type() } { $m->subtype() } { $m->essence() } { $m->params().charset } { $m->is('application/*') } { $m->is('text/*') }\"", "application vnd.api+json application/vnd.api+json utf-8 true false")]
    [InlineData("h: hex = 'ff80'\necho \"{ $h->bytes() } { $h->to_number() } { $h->upper() }\"", "2 65408 FF80")]
    [InlineData("b = @base64('hello world')\necho \"$b { $b->decode() } { $b->bytes() } { @base64('??>', url: true) }\"", "aGVsbG8gd29ybGQ= hello world 11 Pz8-")]
    [InlineData("j: json = '{ \"a\": [1, 2] }'\necho \"{ $j->parse().a[1] }\"", "2")]
    [InlineData("p: phone = '+44 7700 900123'\necho \"{ $p->country_code() } { $p->national() } { $p->tel() }\"", "44 7700900123 tel:+447700900123")]
    [InlineData("c: country = 'de'\necho \"{ $c->name() } { $c->alpha3() } { $c->currency() } { $c->flag() }\"", "Germany DEU EUR 🇩🇪")]
    [InlineData("c: currency = 'GBP'\necho \"{ $c->name() } { $c->symbol() } { $c->digits() } { $c->format(1234.5) }\"", "British Pound £ 2 £1,234.50")]
    [InlineData("l: locale = 'fr-FR'\necho \"{ $l->language() } { $l->region() } { $l->name() }\"", "fr FR French (France)")]
    [InlineData("l: locale = 'de-DE'\necho \"{ $l->format(1234.5) }\"", "1.234,5")]
    [InlineData("z: timezone = 'Europe/London'\necho \"{ $z->offset('2026-07-01'->to_date()) } { $z->offset('2026-01-01'->to_date()) } { $z->dst('2026-07-01'->to_date()) } { $z->convert('2026-07-01T12:00:00Z'->to_date())->format('HH:mm') }\"", "+01:00 +00:00 true 13:00")]
    [InlineData("d: duration = '1h30m'\necho \"{ $d->seconds() } { $d->minutes() } { $d->hours() } { $d->iso() } { $d->plus('45m') } { $d->after('2026-11-21T09:00:00'->to_date())->format('HH:mm') } { $d->before('2026-11-21T09:00:00'->to_date())->format('HH:mm') }\"", "5400 90 1.5 PT1H30M 2h15m 10:30 07:30")]
    [InlineData("c: cron = '0 9 * * mon-fri'\necho \"{ $c->next('2026-11-21T10:00:00'->to_date())->format('ddd HH:mm') } { $c->next_times(2, '2026-11-23T10:00:00'->to_date())->map(fn(t) => $t->format('ddd')) }\"", "Mon 09:00 Tue, Wed")]
    [InlineData("c: cron = '@hourly'\necho \"{ $c->next('2026-11-21T10:20:00'->to_date())->format('HH:mm') }\"", "11:00")]
    [InlineData("c: color = '#ff8800'\necho \"{ $c->rgb().g } { $c->mix('#0000ff') } { $c->lighten(0.5) } { $c->darken() } { $c->luminance() } { $c->contrast('white') }\"", "136 #804480 #ffc480 #cc6d00 0.3887 2.39")]
    [InlineData("c: color = 'rgb(255 0 0 / 0.5)'\necho \"$c { $c->hex() } { $c->css() } { $c->rgb().a }\"", "#ff000080 #ff0000 rgb(255 0 0 / 0.5) 0.5")]
    [InlineData("i: iban = 'GB82WEST12345698765432'\necho \"{ $i->country() } { $i->country()->name() } { $i->formatted() } { $i->bban() }\"", "GB United Kingdom GB82 WEST 1234 5698 7654 32 WEST12345698765432")]
    [InlineData("c: card = '5555 5555 5555 4444'\necho \"$c { $c->last4() } { $c->brand() } { $c->reveal() } { { card: $c }->to_json() }\"", "•••• 4444 4444 mastercard 5555555555554444 {\"card\":\"•••• 4444\"}")]
    public void EachTypeHasMethodsOfItsOwn(string code, string expected)
    {
        Assert.Equal(expected, Code(code));
    }

    [Fact]
    public void JwtClaimsCanBeRead()
    {
        string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2FuIiwiZXhwIjoxNzAwMDAwMDAwfQ.sig";
        Assert.Equal("HS256 joan true 2023", Code($"t: jwt = '{token}'\necho \"{{ $t->header().alg }} {{ $t->claims().sub }} {{ $t->expired() }} {{ $t->expires()->format('yyyy') }}\""));
        Assert.Equal("null false", Code("t: jwt = 'eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.'\necho \"{ $t->expires() ?? 'null' } { $t->expired() }\""));
    }

    [Fact]
    public void NativeValuesStayText()
    {
        // The string methods that make sense are kept, and a value equals the same text once tidied.
        Assert.Equal("true true example.com/docs", Code("u: url = 'https://Example.com/docs'\necho \"{ $u->contains('docs') } { $u == 'https://example.com/docs' } { $u->slice(8) }\""));
        Assert.Contains("->to_date()", Fails("u: url = 'https://example.com'\necho $u->to_date()"));
        Assert.Equal("https://example.com/x", Code("u: url = 'https://example.com/x'\necho $u->to_string()"));
        // A card or a port keeps no string methods at all.
        Assert.Contains("->length()", Fails("c: card = '4242424242424242'\necho $c->length()"));
        Assert.Equal("8081", Code("p: port = 8080\necho $p + 1"));
    }

    [Theory]
    [InlineData("u: url = https://example.com/docs?page=2\necho $u->query().page", "2")]
    [InlineData("u = https://example.com/docs // the docs\necho \"{ $u->type() } { $u->path() }\"", "url /docs")]
    [InlineData("echo https://example.com/a->host()", "example.com")]
    [InlineData("urls = [https://a.example, https://b.example]\necho $urls->map(fn(u) => $u->host())", "a.example, b.example")]
    [InlineData("d: domain = example.com\necho $d->tld()", "com")]
    [InlineData("ip: ip = 192.168.0.1\necho $ip->private()", "true")]
    [InlineData("b: cidr = 10.0.0.0/8\necho $b->prefix()", "8")]
    [InlineData("z: timezone = Europe/London\necho $z", "Europe/London")]
    [InlineData("z: timezone = America/New_York\necho $z", "America/New_York")]
    [InlineData("v: semver = 1.4.2\necho $v->minor()", "4")]
    [InlineData("v: semver = 1.4.2-beta.1\necho $v->prerelease()", "beta.1")]
    [InlineData("c: country = GB\necho $c->name()", "United Kingdom")]
    [InlineData("c: currency = EUR\necho $c->symbol()", "€")]
    [InlineData("l: locale = en-GB\necho $l->region()", "GB")]
    [InlineData("d: duration = 1h30m\necho $d->minutes()", "90")]
    [InlineData("c: color = red\necho \"$c { $c->contrast(white) }\"", "#ff0000 4")]
    [InlineData("m: mime = text/html\necho $m->subtype()", "html")]
    [InlineData("s: slug = my-post\necho $s->words()", "my, post")]
    [InlineData("fn host(u: url) => $u->host()\necho @host(https://example.com/x)", "example.com")]
    public void ValuesCanBeWrittenWithoutQuotes(string code, string expected)
    {
        Assert.Equal(expected, Code(code));
    }

    [Fact]
    public void UrlsWithoutQuotesWorkInXmlAndInText()
    {
        Assert.Equal("example.com\n", ScriptRunner.RunBody("<let name=\"u\" type=\"url\" value=\"https://example.com/docs\" /><echo>{ $u->host() }</echo>"));
        Assert.Equal("example.com\n", ScriptRunner.RunBody("<echo>{ https://example.com/docs->host() }</echo>"));
        // In text, a URL is only text: the // isn't a comment and nothing is evaluated.
        Assert.Equal("See https://example.com/docs.", Code("echo \"See https://example.com/docs.\""));
        Assert.Equal("See https://example.com", Code("echo 'See https://example.com' // a comment"));
        // A // comment still works after a value, and http: alone is no URL.
        Assert.Equal("3", Code("x = 1 + 2 // three\necho $x"));
    }

    [Theory]
    [InlineData("u: url(schemes: ['https']) = 'http://example.com'", "must use https")]
    [InlineData("u: url(hosts: ['example.com']) = 'https://other.com'", "example.com")]
    [InlineData("ip: ip(version: 6) = '10.0.0.1'", "IPv6")]
    [InlineData("h: hex(bytes: 4) = 'ff'", "4 bytes")]
    [InlineData("p: phone(countries: ['+44', '+353']) = '+14155550100'", "must be a +44 or +353 number")]
    public void TypesTakeArgumentsWhereTheyAreUsed(string code, string message)
    {
        Assert.Contains(message, Fails(code + "\necho 1"));
    }

    [Fact]
    public void RefinementsBuildOnNativeTypes()
    {
        Assert.Equal("https://docs.example.com/x", Code("type CompanySite with [url] {\n  validate: $value->host()->ends_with('example.com'), 'Use a company site'\n}\ns: CompanySite = https://docs.example.com/x\necho $s"));
        Assert.Contains("Use a company site", Fails("type CompanySite with [url] {\n  validate: $value->host()->ends_with('example.com'), 'Use a company site'\n}\ns: CompanySite = https://elsewhere.org\necho $s"));
        Assert.Equal("GB", Code("type Market with [country] { one_of: [GB, IE] }\nm: Market = 'gbr'\necho $m"));
        Assert.Contains("must be one of GB", Fails("type Market with [country] { one_of: [GB, IE] }\nm: Market = FR\necho $m"));
    }

    [Fact]
    public void NewTypesAreCaseSensitiveSoScriptTypesOfTheSameNameKeepWorking()
    {
        // email and the first types ignore case; the newer ones are lowercase only, so a script's Color is its own.
        Assert.Equal("green", Code("enum Color { red, green }\nc: Color = green\necho $c"));
        Assert.Equal("5", Code("type Duration { minutes: int }\nd = $Duration->from({ minutes: 5 })\necho $d.minutes"));
        Assert.Equal("#008000", Code("c: color = green\necho $c"));
    }

    [Fact]
    public void ValuesTurnIntoNativeTypesForJsonAndArguments()
    {
        Assert.Equal("example.com", Code("type Link { href: url }\nl = $Link->from({ href: 'https://example.com' })\necho $l.href->host()"));
        Assert.Contains("$.href: needs a url", Fails("type Link { href: url }\nl = $Link->from({ href: 'nope' })\necho 1"));
        Assert.Equal("\"https://example.com/\"", Code("u: url = 'https://example.com/'\necho $u->to_json()"));
    }

    [Fact]
    public void EveryNativeTypeIsDescribedAndHasAnExampleThatFits()
    {
        foreach (NativeTypeInfo info in NativeTypes.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.Description), info.Name);
            Assert.NotNull(NativeParsers.Get(info.Name) ?? (object)SemanticTypes.IsSemantic(info.Name));
            string value = info.Example.StartsWith('\'') ? info.Example : "'" + info.Example + "'";
            if (info.Name == "port") value = info.Example;
            Assert.Equal(info.Name == "port" ? "number" : info.Name, Code($"x: {info.Name} = {value}\necho $x->type()"));
        }
        Assert.Throws<ArgumentException>(() => new NativeTypeInfo("Bad", "x", "x"));
    }

    private sealed class SkuPackage : KScriptPackage
    {
        public override string Name => "KScript.Sku";

        public override string Description => "SKUs, for tests.";

        protected override void Register(PackageBuilder p)
        {
            p.NativeType(new NativeTypeInfo("sku", "A stock code: three letters, - and four digits.", "ABC-1234", new[] { "contains", "starts_with", "length" }),
                new NativeParser(text =>
                {
                    string t = text.Trim().ToUpperInvariant();
                    return System.Text.RegularExpressions.Regex.IsMatch(t, "^[A-Z]{3}-\\d{4}$") ? (new NativeText("sku", t, new Dictionary<string, object> { ["family"] = t.Substring(0, 3) }), null) : (null, "it must be three letters, - and four digits");
                }));
            p.Method("sku", "family", "", "The three letters.", KType.String, (t, c) => ((NativeText)t).Part("family"));
            p.Examples(("sku->family", "<let name=\"s\" type=\"sku\" value=\"'abc-1234'\" />\n<echo>{ $s->family() }</echo>"));
        }
    }

    [Fact]
    public void APackageCanAddANativeType()
    {
        var registry = new PackageRegistry();
        registry.Register(new SkuPackage());
        string Run(string body)
        {
            var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            new Interpreter(KScriptHost.ForText(output), registry).RunText("<kscript><import package=\"KScript.Sku\" />" + body + "</kscript>");
            return output.ToString();
        }

        Assert.Equal("ABC-1234 ABC true sku\n", Run("<let name=\"s\" type=\"sku\" value=\"abc-1234\" /><echo>$s { $s->family() } { $s->starts_with('AB') } { $s->type() }</echo>"));
        Assert.Contains("it must be three letters", Assert.Throws<KScriptException>(() => Run("<let name=\"s\" type=\"sku\" value=\"'nope'\" />")).Message);
        Assert.Equal("KScript.Sku", NativeTypes.Get("sku").Package);
        Assert.Contains("sku", KType.Declarable);

        // A name that is taken can't be reused by another package.
        Assert.Contains("There is already a native type called url", Assert.Throws<InvalidOperationException>(() =>
            NativeTypes.Register(new NativeTypeInfo("url", "x", "x") { Package = "KScript.Other" })).Message);
    }

    private static string Project(string json, params (string Path, string Text)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-natives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        if (json != null) File.WriteAllText(Path.Combine(dir, "kscript.json"), json);
        foreach ((string path, string text) in files)
        {
            string full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text);
        }
        return dir;
    }

    private static string RunFile(string file)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunFile(file);
        return output.ToString();
    }

    [Fact]
    public void TheNativesFolderAppliesToTheWholeProject()
    {
        string dir = Project("{ \"name\": \"p\" }",
            ("natives/sku.kscript", "/// A stock code.\ntype Sku with [string] {\n  normalize: $value->trim()->upper()\n  pattern: /^[A-Z]{3}-\\d{4}$/\n}\n"),
            ("natives/money/tier.kscript", "enum Tier { gold, silver }\n"),
            ("natives/site.ks.xml", "<kscript><type name=\"Site\" with=\"url\"><validate test=\"$value->host()->ends_with('example.com')\" message=\"Use a company site\" /></type></kscript>"),
            ("scripts/main.kscript", "sku: Sku = abc-1234\ntier: Tier = gold\nsite: Site = https://www.example.com\nfn shop(s: Sku) => 'shop ' + $s\necho \"$sku $tier { @shop('xyz-0001') } { $site->host() }\"\n"),
            ("scripts/own.kscript", "enum Tier { bronze }\nt: Tier = bronze\necho $t\n"));
        Assert.Equal("ABC-1234 gold shop XYZ-0001 www.example.com\n", RunFile(Path.Combine(dir, "scripts", "main.kscript")));
        // A script's own type of the same name wins.
        Assert.Equal("bronze\n", RunFile(Path.Combine(dir, "scripts", "own.kscript")));
        Assert.Contains("doesn't fit Sku", Assert.Throws<KScriptException>(() =>
            RunFile(Path.Combine(dir, "scripts", "main.kscript").Also(p => File.WriteAllText(p, "s: Sku = nope\n")))).Message);
    }

    [Fact]
    public void KscriptJsonCanNameTheNativesFolders()
    {
        string dir = Project("{ \"natives\": [\"src/kinds\", \"more/*.kscript\"] }",
            ("src/kinds/sku.kscript", "type Sku with [string] { pattern: /^[A-Z]+$/ }\n"),
            ("more/level.kscript", "type Level with [int] { min: 1; max: 3 }\n"),
            ("natives/ignored.kscript", "type Ignored with [string] {}\n"),
            ("main.kscript", "s: Sku = ABC\nl: Level = 2\necho \"$s $l\"\n"),
            ("other.kscript", "i: Ignored = 'x'\necho $i\n"));
        Assert.Equal("ABC 2\n", RunFile(Path.Combine(dir, "main.kscript")));
        Assert.Throws<KScriptException>(() => RunFile(Path.Combine(dir, "other.kscript")));
    }
}
