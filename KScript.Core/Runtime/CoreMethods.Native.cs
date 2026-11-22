using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        private static NativeText T(object target) => (NativeText)target;

        private static object Part(object target, string name) => T(target).Part(name);

        /// <summary>Makes a native value from text, or fails with why it doesn't fit.</summary>
        private static object Native(string type, string text)
        {
            if (SemanticTypes.TryConvert(type, text, out object value, out string reason)) return value;
            throw new KScriptException($"'{text}' isn't {NativeTypes.Get(type).WithArticle}: {reason}.", "Value");
        }

        /// <summary>The methods of url, ip, country and the other native types that have no class of their own.</summary>
        private static void RegisterNativeMethods(PackageBuilder p)
        {
            const string S = KType.String, N = KType.Number, B = KType.Bool, M = KType.Map, L = KType.List;

            // The types on the NativeType contract declare their own fields and methods.
            foreach (NativeType type in NativeTypeRegistry.BuiltIn) type.Members(new NativeMembers(p, type.Name));

            // url
            p.Field("url", "scheme", S, "The scheme, in lower case: https.", (t, _) => Part(t, "scheme"));
            p.Field("url", "host", S, "The host name, in lower case: example.com.", (t, _) => Part(t, "host"));
            p.Field("url", "port", N, "The port: the one written, or the scheme's usual one (443 for https).", (t, _) => Part(t, "port"));
            p.Field("url", "path", S, "The path, starting with /: /docs/start.", (t, _) => Part(t, "path"));
            p.Field("url", "query", M, "The query string as a map: ?page=2&q=cats gives { page: '2', q: 'cats' }.", (t, _) => new Dictionary<string, object>((Dictionary<string, object>)Part(t, "query")));
            p.Field("url", "fragment", S, "The part after #, without it.", (t, _) => Part(t, "fragment"));
            p.Field("url", "origin", S, "The scheme, host and any port written: https://example.com.", (t, _) => Part(t, "origin"));
            p.Method("url", "with_query", "values: map", "The url with these query values added or replaced (null removes one).", "url", (t, c) =>
            {
                var query = new Dictionary<string, object>((Dictionary<string, object>)Part(t, "query"));
                foreach (var kv in (Dictionary<string, object>)c.Arg(0) ?? new Dictionary<string, object>())
                {
                    if (kv.Value == null) query.Remove(kv.Key);
                    else query[kv.Key] = Values.ToStr(kv.Value);
                }
                string text = T(t).Text;
                int hash = text.IndexOf('#');
                string fragment = hash >= 0 ? text.Substring(hash) : string.Empty;
                string head = (hash >= 0 ? text.Substring(0, hash) : text).Split('?')[0];
                string qs = string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + (Values.ToStr(kv.Value).Length > 0 ? "=" + Uri.EscapeDataString(Values.ToStr(kv.Value)) : string.Empty)));
                return Native("url", head + (qs.Length > 0 ? "?" + qs : string.Empty) + fragment);
            });
            p.Method("url", "join", "path: string", "The url a link to path leads to from here: ../img/a.png or /about.", "url", (t, c) =>
                Native("url", new Uri((Uri)Part(t, "uri"), c.Str(0)).ToString()));

            // domain
            p.Field("domain", "tld", S, "The last part: com, or uk for example.co.uk.", (t, _) => Part(t, "tld"));
            p.Field("domain", "labels", L, "Its parts: ['api', 'example', 'com'].", (t, _) => new List<object>((List<object>)Part(t, "labels")));
            p.Method("domain", "is_subdomain_of", "other: string", "True when it is the other domain or under it: api.example.com is under example.com.", B, (t, c) =>
            {
                string other = c.Str(0).Trim().TrimEnd('.').ToLowerInvariant();
                try
                {
                    other = new System.Globalization.IdnMapping().GetUnicode(other);
                }
                catch (ArgumentException)
                {
                    return false;
                }
                return T(t).Text == other || T(t).Text.EndsWith("." + other, StringComparison.Ordinal);
            });
            p.Method("domain", "ascii", "", "The form DNS uses, with international labels as xn--: xn--bcher-kva.example for bücher.example.", S, (t, c) => Part(t, "ascii"));

            // ip and cidr
            static IPAddress A(object t) => (IPAddress)Part(t, "address");
            p.Field("ip", "version", N, "4 or 6.", (t, _) => Part(t, "version"));
            p.Method("ip", "in", "block: cidr", "True when the address is in the block, such as '10.0.0.0/8'.", B, (t, c) =>
                NativeParsers.InBlock((NativeText)(c.Arg(0) is NativeText block && block.TypeName == "cidr" ? block : Native("cidr", c.Str(0))), A(t)));
            p.Field("ip", "is_private", B, "True for addresses that only work inside a network: 10.x, 172.16-31.x, 192.168.x, fc00::/7, and loopback and link-local ones.", (t, _) =>
            {
                IPAddress ip = A(t);
                if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal) return true;
                if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
                byte[] b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
            });
            p.Field("ip", "is_loopback", B, "True for 127.x.x.x and ::1.", (t, _) => IPAddress.IsLoopback(A(t)));
            p.Method("cidr", "contains", "address: ip", "True when the address is in the block.", B, (t, c) =>
                NativeParsers.InBlock(T(t), A(c.Arg(0) is NativeText ip && ip.TypeName == "ip" ? ip : Native("ip", c.Str(0)))));
            p.Field("cidr", "network", "ip", "The first address of the block.", (t, _) => NativeParsers.IpValue((IPAddress)Part(t, "network")));
            p.Field("cidr", "prefix", N, "The prefix length: 8 for 10.0.0.0/8.", (t, _) => Part(t, "prefix"));
            p.Field("cidr", "size", N, "How many addresses the block holds: 256 for a /24.", (t, _) => Math.Pow(2, (int)Part(t, "bits") - (double)Part(t, "prefix")));

            // slug, semver, mime, hex, base64, jwt, json
            p.Field("slug", "words", L, "The words: ['my', 'first', 'post'].", (t, _) => new List<object>((List<object>)Part(t, "words")));
            p.Command("slugify", "text: string", "Turns text into a slug: 'Hello, World!' gives hello-world. Accents are dropped.", "slug",
                c => Native("slug", NativeParsers.Slugify(c.Str(0))), "@slugify('Crème Brûlée recipes!')");
            p.Field("semver", "major", N, "The first number: 1 in 1.4.2.", (t, _) => Part(t, "major"));
            p.Field("semver", "minor", N, "The second number: 4 in 1.4.2.", (t, _) => Part(t, "minor"));
            p.Field("semver", "patch", N, "The third number: 2 in 1.4.2.", (t, _) => Part(t, "patch"));
            p.Field("semver", "prerelease", S, "What comes after -, such as beta.1, or '' for a release.", (t, _) => Part(t, "prerelease"));
            p.Method("semver", "compare", "other: semver", "-1, 0 or 1 as this version comes before, with or after the other (build metadata is ignored).", N, (t, c) =>
                (double)NativeParsers.CompareSemver(T(t), (NativeText)(c.Arg(0) is NativeText o && o.TypeName == "semver" ? o : Native("semver", c.Str(0)))));
            p.Method("semver", "satisfies", "range: string", "True when the version is in the range: ^1.2.0, ~1.2, >=1.0.0 <2.0.0, 1.x, or several joined with ||.", B, (t, c) =>
            {
                bool ok = NativeParsers.Satisfies(T(t), c.Str(0), out string problem);
                if (problem != null) throw new KScriptException(problem + ".", "Value");
                return ok;
            });
            p.Method("semver", "bump", "part: string", "The next version: bump('major'), bump('minor') or bump('patch'). The prerelease is dropped.", "semver", (t, c) =>
            {
                double major = (double)Part(t, "major"), minor = (double)Part(t, "minor"), patch = (double)Part(t, "patch");
                string text = c.Str(0) switch
                {
                    "major" => $"{major + 1}.0.0",
                    "minor" => $"{major}.{minor + 1}.0",
                    "patch" => ((string)Part(t, "prerelease")).Length > 0 ? $"{major}.{minor}.{patch}" : $"{major}.{minor}.{patch + 1}",
                    string other => throw new KScriptException($"bump takes major, minor or patch, not '{other}'.", "Value"),
                };
                return Native("semver", text);
            });
            p.Field("mime", "main_type", S, "The part before /: text in text/html. (->type() is mime, as for any value.)", (t, _) => Part(t, "type"));
            p.Field("mime", "subtype", S, "The part after /: html in text/html.", (t, _) => Part(t, "subtype"));
            p.Field("mime", "essence", S, "The type and subtype without parameters: text/html.", (t, _) => Part(t, "essence"));
            p.Field("mime", "params", M, "The parameters as a map: { charset: 'utf-8' }.", (t, _) => new Dictionary<string, object>((Dictionary<string, object>)Part(t, "params")));
            p.Method("mime", "is", "pattern: string", "True when it matches text/html, text/* or */*.", B, (t, c) =>
            {
                string[] want = c.Str(0).Trim().ToLowerInvariant().Split('/');
                return want.Length == 2 && (want[0] == "*" || want[0] == (string)Part(t, "type")) && (want[1] == "*" || want[1] == (string)Part(t, "subtype"));
            });
            p.Field("hex", "bytes", N, "How many bytes it holds: half its number of digits.", (t, _) => Math.Ceiling(T(t).Text.Length / 2.0));
            p.Method("hex", "to_number", "", "Its value as a number: 'ff' gives 255.", N, (t, c) =>
            {
                if (T(t).Text.Length > 13) throw new KScriptException("A hex value with more than 13 digits is too big to be a number.", "Value");
                return (double)long.Parse(T(t).Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            });
            p.Method("base64", "decode", "", "The text it holds (read as UTF-8).", S, (t, c) => Encoding.UTF8.GetString(NativeParsers.Base64Bytes(T(t).Text)));
            p.Field("base64", "bytes", N, "How many bytes it holds.", (t, _) => (double)NativeParsers.Base64Bytes(T(t).Text).Length);
            p.Field("jwt", "header", M, "The token's header: { alg, typ, ... }.", (t, _) => Part(t, "header"));
            p.Field("jwt", "claims", M, "The token's claims: { sub, exp, ... }. The signature isn't checked, so don't trust them for sign-in.", (t, _) => Part(t, "claims"));
            p.Field("jwt", "expires", KType.Date, "When the token expires (its exp claim), or null if it doesn't say.", (t, _) =>
                ((Dictionary<string, object>)Part(t, "claims")).TryGetValue("exp", out object exp) && exp is double seconds ? DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime : null);
            p.Field("jwt", "is_expired", B, "True when its exp claim is in the past.", (t, _) =>
                ((Dictionary<string, object>)Part(t, "claims")).TryGetValue("exp", out object exp) && exp is double seconds && DateTimeOffset.FromUnixTimeSeconds((long)seconds) < DateTimeOffset.UtcNow);

            // timezone
            static TimeZoneInfo Z(object t) => (TimeZoneInfo)Part(t, "zone");
            p.Method("timezone", "offset", "at?: date", "Its offset from UTC, such as +01:00, now or at a date.", S, (t, c) =>
            {
                DateTime at = c.Arg(0) is DateTime d ? d : DateTime.UtcNow;
                TimeSpan offset = Z(t).GetUtcOffset(at);
                return (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            });
            p.Method("timezone", "now", "", "The time now in this zone.", KType.Date, (t, c) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Z(t)));
            p.Method("timezone", "convert", "time: date", "The same moment as the local time in this zone.", KType.Date, (t, c) =>
                TimeZoneInfo.ConvertTime(c.Arg(0) is DateTime d ? d : throw new KScriptException("convert needs a date.", "Type"), Z(t)));
            p.Method("timezone", "is_dst", "at?: date", "True when summer time is in effect, now or at a date.", B, (t, c) => Z(t).IsDaylightSavingTime(c.Arg(0) is DateTime d ? d : DateTime.UtcNow));

            // cron, color
            p.Method("cron", "next", "after?: date", "The next time the schedule runs, after a date or after now.", KType.Date, (t, c) =>
                NativeParsers.CronNext(T(t), c.Arg(0) is DateTime d ? d : DateTime.Now));
            p.Method("cron", "next_times", "count: int, after?: date", "The next few times it runs.", L, (t, c) =>
            {
                var times = new List<object>();
                DateTime at = c.Arg(1) is DateTime d ? d : DateTime.Now;
                for (int i = 0; i < Math.Clamp(c.Int(0), 0, 1000); i++) times.Add(at = NativeParsers.CronNext(T(t), at));
                return times;
            });
            static double Ch(object t, string n) => (double)Part(t, n);
            p.Method("color", "rgb", "", "Its red, green, blue (0-255) and alpha (0-1): { r, g, b, a }.", M, (t, c) =>
                new Dictionary<string, object> { ["r"] = Ch(t, "r"), ["g"] = Ch(t, "g"), ["b"] = Ch(t, "b"), ["a"] = Ch(t, "a") });
            p.Method("color", "hex", "", "Its #rrggbb form, without the alpha.", S, (t, c) => T(t).Text.Substring(0, 7));
            p.Method("color", "css", "", "Its CSS form: #rrggbb, or rgb(r g b / a) with transparency.", S, (t, c) =>
                Ch(t, "a") < 1 ? FormattableString.Invariant($"rgb({Ch(t, "r")} {Ch(t, "g")} {Ch(t, "b")} / {Math.Round(Ch(t, "a"), 3)})") : T(t).Text);
            p.Method("color", "mix", "other: color, amount?: number", "The colour part way to the other: amount 0.5 (the default) is halfway.", "color", (t, c) =>
            {
                NativeText o = (NativeText)(c.Arg(0) is NativeText n && n.TypeName == "color" ? n : Native("color", c.Str(0)));
                double w = c.Arg(1, "amount") is double a ? Math.Clamp(a, 0, 1) : 0.5;
                double Mix(string ch) => Ch(t, ch) + (Ch(o, ch) - Ch(t, ch)) * w;
                return NativeParsers.ColorOf(Mix("r"), Mix("g"), Mix("b"), Mix("a"));
            });
            p.Method("color", "lighten", "amount?: number", "Mixed with white: amount 0.2 (the default) is a fifth of the way.", "color", (t, c) =>
            {
                double w = c.Arg(0, "amount") is double a ? Math.Clamp(a, 0, 1) : 0.2;
                return NativeParsers.ColorOf(Ch(t, "r") + (255 - Ch(t, "r")) * w, Ch(t, "g") + (255 - Ch(t, "g")) * w, Ch(t, "b") + (255 - Ch(t, "b")) * w, Ch(t, "a"));
            });
            p.Method("color", "darken", "amount?: number", "Mixed with black: amount 0.2 (the default) is a fifth of the way.", "color", (t, c) =>
            {
                double w = 1 - (c.Arg(0, "amount") is double a ? Math.Clamp(a, 0, 1) : 0.2);
                return NativeParsers.ColorOf(Ch(t, "r") * w, Ch(t, "g") * w, Ch(t, "b") * w, Ch(t, "a"));
            });
            p.Field("color", "luminance", N, "How light it looks, from 0 (black) to 1 (white), as WCAG measures it.", (t, _) => Luminance(T(t)));
            p.Method("color", "contrast_ratio", "other: color", "The WCAG contrast ratio with the other colour, from 1 to 21. Text needs 4.5 or more.", N, (t, c) =>
            {
                double a = Luminance(T(t)), b = Luminance((NativeText)(c.Arg(0) is NativeText n && n.TypeName == "color" ? n : Native("color", c.Str(0))));
                return Math.Round((Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05), 2);
            });

            p.Examples(
                ("url.scheme", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.scheme }</echo>"),
                ("url.host", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.host }</echo>"),
                ("url.port", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.port }</echo>"),
                ("url.path", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.path }</echo>"),
                ("url.query", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.query }</echo>"),
                ("url.fragment", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.fragment }</echo>"),
                ("url.origin", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x.origin }</echo>"),
                ("url->with_query", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x->with_query({ page: 3, q: 'tea' }) }</echo>"),
                ("url->join", "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n<echo>{ $x->join('../blog/') }</echo>"),
                ("domain->ascii", "<let name=\"x\" type=\"domain\" value=\"'Bücher.example'\" />\n<echo>{ $x } { $x->ascii() }</echo>"),
                ("domain.tld", "<let name=\"x\" type=\"domain\" value=\"'api.example.co.uk'\" />\n<echo>{ $x.tld }</echo>"),
                ("domain.labels", "<let name=\"x\" type=\"domain\" value=\"'api.example.co.uk'\" />\n<echo>{ $x.labels }</echo>"),
                ("domain->is_subdomain_of", "<let name=\"x\" type=\"domain\" value=\"'api.example.co.uk'\" />\n<echo>{ $x->is_subdomain_of('example.co.uk') }</echo>"),
                ("ip.version", "<let name=\"x\" type=\"ip\" value=\"'192.168.0.12'\" />\n<echo>{ $x.version }</echo>"),
                ("ip->in", "<let name=\"x\" type=\"ip\" value=\"'192.168.0.12'\" />\n<echo>{ $x->in('192.168.0.0/16') }</echo>"),
                ("ip.is_private", "<let name=\"x\" type=\"ip\" value=\"'192.168.0.12'\" />\n<echo>{ $x.is_private }</echo>"),
                ("ip.is_loopback", "<let name=\"x\" type=\"ip\" value=\"'192.168.0.12'\" />\n<echo>{ $x.is_loopback }</echo>"),
                ("cidr->contains", "<let name=\"x\" type=\"cidr\" value=\"'10.0.0.0/8'\" />\n<echo>{ $x->contains('10.20.30.40') }</echo>"),
                ("cidr.network", "<let name=\"x\" type=\"cidr\" value=\"'10.0.0.0/8'\" />\n<echo>{ $x.network }</echo>"),
                ("cidr.prefix", "<let name=\"x\" type=\"cidr\" value=\"'10.0.0.0/8'\" />\n<echo>{ $x.prefix }</echo>"),
                ("cidr.size", "<let name=\"x\" type=\"cidr\" value=\"'10.0.0.0/8'\" />\n<echo>{ $x.size }</echo>"),
                ("slug.words", "<let name=\"x\" type=\"slug\" value=\"'my-first-post'\" />\n<echo>{ $x.words }</echo>"),
                ("semver.major", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x.major }</echo>"),
                ("semver.minor", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x.minor }</echo>"),
                ("semver.patch", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x.patch }</echo>"),
                ("semver.prerelease", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x.prerelease }</echo>"),
                ("semver->compare", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x->compare('1.4.2') }</echo>"),
                ("semver->satisfies", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x->satisfies('^1.2.0') }</echo>"),
                ("semver->bump", "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n<echo>{ $x->bump('minor') }</echo>"),
                ("mime.main_type", "<let name=\"x\" type=\"mime\" value=\"'text/html; charset=utf-8'\" />\n<echo>{ $x.main_type }</echo>"),
                ("mime.subtype", "<let name=\"x\" type=\"mime\" value=\"'text/html; charset=utf-8'\" />\n<echo>{ $x.subtype }</echo>"),
                ("mime.essence", "<let name=\"x\" type=\"mime\" value=\"'text/html; charset=utf-8'\" />\n<echo>{ $x.essence }</echo>"),
                ("mime.params", "<let name=\"x\" type=\"mime\" value=\"'text/html; charset=utf-8'\" />\n<echo>{ $x.params }</echo>"),
                ("mime->is", "<let name=\"x\" type=\"mime\" value=\"'text/html; charset=utf-8'\" />\n<echo>{ $x->is('text/*') }</echo>"),
                ("hex.bytes", "<let name=\"x\" type=\"hex\" value=\"'0xFF80'\" />\n<echo>{ $x.bytes }</echo>"),
                ("hex->to_number", "<let name=\"x\" type=\"hex\" value=\"'0xFF80'\" />\n<echo>{ $x->to_number() }</echo>"),
                ("base64->decode", "<let name=\"x\" type=\"base64\" value=\"'aGVsbG8gd29ybGQ='\" />\n<echo>{ $x->decode() }</echo>"),
                ("base64.bytes", "<let name=\"x\" type=\"base64\" value=\"'aGVsbG8gd29ybGQ='\" />\n<echo>{ $x.bytes }</echo>"),
                ("jwt.header", "<let name=\"x\" type=\"jwt\" value=\"'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2FuIiwiZXhwIjoxNzAwMDAwMDAwfQ.sig'\" />\n<echo>{ $x.header }</echo>"),
                ("jwt.claims", "<let name=\"x\" type=\"jwt\" value=\"'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2FuIiwiZXhwIjoxNzAwMDAwMDAwfQ.sig'\" />\n<echo>{ $x.claims }</echo>"),
                ("jwt.expires", "<let name=\"x\" type=\"jwt\" value=\"'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2FuIiwiZXhwIjoxNzAwMDAwMDAwfQ.sig'\" />\n<echo>{ $x.expires }</echo>"),
                ("jwt.is_expired", "<let name=\"x\" type=\"jwt\" value=\"'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJqb2FuIiwiZXhwIjoxNzAwMDAwMDAwfQ.sig'\" />\n<echo>{ $x.is_expired }</echo>"),
                ("timezone->offset", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->offset('2026-07-01'->to_date()) }</echo>"),
                ("timezone->now", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->now()->type() }</echo>"),
                ("timezone->convert", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->convert('2026-07-01T12:00:00Z'->to_date()) }</echo>"),
                ("timezone->is_dst", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->is_dst('2026-01-15'->to_date()) }</echo>"),
                ("cron->next", "<let name=\"x\" type=\"cron\" value=\"'0 9 * * mon-fri'\" />\n<echo>{ $x->next('2026-11-21T10:00:00'->to_date()) }</echo>"),
                ("cron->next_times", "<let name=\"x\" type=\"cron\" value=\"'0 9 * * mon-fri'\" />\n<echo>{ $x->next_times(3, '2026-11-21T10:00:00'->to_date()) }</echo>"),
                ("color->rgb", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->rgb() }</echo>"),
                ("color->hex", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->hex() }</echo>"),
                ("color->css", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->css() }</echo>"),
                ("color->mix", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->mix('blue') }</echo>"),
                ("color->lighten", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->lighten(0.5) }</echo>"),
                ("color->darken", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->darken() }</echo>"),
                ("color.luminance", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x.luminance }</echo>"),
                ("color->contrast_ratio", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->contrast_ratio('white') }</echo>"));
        }

        private static double Luminance(NativeText color)
        {
            double Channel(string n)
            {
                double v = (double)color.Part(n) / 255;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return Math.Round(0.2126 * Channel("r") + 0.7152 * Channel("g") + 0.0722 * Channel("b"), 4);
        }
    }
}
