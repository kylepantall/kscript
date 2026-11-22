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
            // slash_path
            static List<string> Segments(object t) => ((List<object>)Part(t, "segments")).Cast<string>().ToList();
            static NativeText SlashPathOf(object value) =>
                value is NativeText path && path.TypeName == "slash_path" ? path : (NativeText)Native("slash_path", Values.ToStr(value));
            p.Field("slash_path", "segments", L, "The names in order: ['My Apps', 'Notes'] for /My Apps/Notes.", (t, _) => Segments(t).Cast<object>().ToList());
            p.Field("slash_path", "name", S, "The last name: Notes in /My Apps/Notes, or '' for /.", (t, _) => Segments(t).LastOrDefault() ?? string.Empty);
            p.Field("slash_path", "parent", "slash_path", "The path one up: /My Apps for /My Apps/Notes, / for /My Apps, and null for /.", (t, _) =>
            {
                List<string> parts = Segments(t);
                return parts.Count == 0 ? null : NativeParsers.SlashPath(parts.Take(parts.Count - 1).ToList());
            });
            p.Field("slash_path", "depth", N, "How many names it has: 2 for /My Apps/Notes, 0 for /.", (t, _) => (double)Segments(t).Count);
            p.Field("slash_path", "is_root", B, "True for / itself.", (t, _) => Segments(t).Count == 0);
            p.Method("slash_path", "join", "more: string", "The path with more names added on the end and tidied: /docs joined with 'guides/../start' is /docs/start. A / at the start of more is ignored.", "slash_path", (t, c) =>
                Native("slash_path", T(t).Text + "/" + Values.ToStr(c.Arg(0))));
            p.Method("slash_path", "is_within", "other: slash_path", "True when it is the other path or under it: /docs/guides/start is within /docs. Names are compared exactly, case included.", B, (t, c) =>
            {
                List<string> mine = Segments(t), theirs = Segments(SlashPathOf(c.Arg(0)));
                return theirs.Count <= mine.Count && theirs.SequenceEqual(mine.Take(theirs.Count), StringComparer.Ordinal);
            });

            // date_format
            static string Pattern(object t) => NativeParsers.DotNetDateFormat(T(t));
            p.Field("date_format", "tokens", L, "Its parts in order: ['dd', '-', 'MM', '-', 'yyyy'].", (t, _) => new List<object>((List<object>)Part(t, "tokens")));
            p.Field("date_format", "has_date", B, "True when it shows a day, month, year or era.", (t, _) => Part(t, "has_date"));
            p.Field("date_format", "has_time", B, "True when it shows an hour, minute, second or AM/PM.", (t, _) => Part(t, "has_time"));
            p.Field("date_format", "example", S, "A date written this way, to show what it looks like: 07-03-2026 for dd-MM-yyyy (Saturday 7 March 2026, 14:05:09).", (t, _) =>
                new DateTime(2026, 3, 7, 14, 5, 9, 123).ToString(Pattern(t), CultureInfo.InvariantCulture));
            p.Method("date_format", "format", "date: date", "The date written this way: 07-03-2026.", S, (t, c) =>
                c.Arg(0) is DateTime when ? when.ToString(Pattern(t), CultureInfo.InvariantCulture) : throw new KScriptException($"->format() needs a date, but got {Values.Describe(c.Arg(0))}.", "Type"));
            p.Method("date_format", "parse", "text: string", "Reads text written this way as a date: dd-MM-yyyy reads '07-03-2026'.", KType.Date, (t, c) =>
                DateTime.TryParseExact(c.Str(0).Trim(), Pattern(t), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime read)
                    ? read : throw new KScriptException($"'{c.Str(0)}' isn't a date written {T(t).Text}, such as {new DateTime(2026, 3, 7, 14, 5, 9).ToString(Pattern(t), CultureInfo.InvariantCulture)}.", "Format"));

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

            // phone, country, currency, locale, timezone
            p.Field("phone", "country_code", S, "The country calling code without +: 44 for a UK number.", (t, _) => Part(t, "country_code"));
            p.Field("phone", "national", S, "The number after the country code.", (t, _) => Part(t, "national"));
            p.Method("phone", "tel", "", "A tel: link for it: tel:+447700900123.", S, (t, c) => "tel:" + T(t).Text);
            p.Field("country", "name", S, "The country's name in English: United Kingdom.", (t, _) => Part(t, "name"));
            p.Field("country", "alpha3", S, "The three-letter code: GBR.", (t, _) => Part(t, "alpha3"));
            p.Field("country", "currency", "currency", "The currency the country uses: GBP.", (t, _) => Native("currency", (string)Part(t, "currency")));
            p.Field("country", "flag", S, "The country's flag emoji: 🇬🇧.", (t, _) => string.Concat(T(t).Text.Select(ch => char.ConvertFromUtf32(0x1F1E6 + ch - 'A'))));
            p.Field("currency", "name", S, "The currency's name in English: British Pound.", (t, _) => Part(t, "name"));
            p.Field("currency", "symbol", S, "Its symbol: £.", (t, _) => Part(t, "symbol"));
            p.Field("currency", "digits", N, "How many decimal places amounts have: 2 for GBP, 0 for JPY.", (t, _) => Part(t, "digits"));
            p.Method("currency", "format", "amount: number", "An amount written in this currency: £1,234.50.", S, (t, c) =>
            {
                int digits = (int)(double)Part(t, "digits");
                string number = Math.Abs(c.Num(0)).ToString("N" + digits, CultureInfo.InvariantCulture);
                return (c.Num(0) < 0 ? "-" : string.Empty) + (string)Part(t, "symbol") + number;
            });
            p.Field("locale", "language", S, "The language code: en in en-GB.", (t, _) => Part(t, "language"));
            p.Field("locale", "region", S, "The region code: GB in en-GB, or '' when there isn't one.", (t, _) => Part(t, "region"));
            p.Field("locale", "name", S, "Its name in English: English (United Kingdom).", (t, _) => Part(t, "name"));
            p.Field("locale", "native_name", S, "Its name in its own language: français (France).", (t, _) => Part(t, "native_name"));
            p.Method("locale", "format", "value: any", "A number or date written the way this locale writes it: 1.234,5 in de-DE.", S, (t, c) =>
            {
                CultureInfo culture = (CultureInfo)Part(t, "culture");
                return c.Arg(0) switch
                {
                    double d => d.ToString("#,0.##########", culture),
                    DateTime dt => dt.ToString("d", culture),
                    object o => Values.ToStr(o),
                    null => string.Empty,
                };
            });
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

            // duration, cron, color
            static double Sec(object t) => (double)Part(t, "seconds");
            p.Field("duration", "seconds", N, "The length in seconds: 5400 for 1h30m.", (t, _) => Sec(t));
            p.Field("duration", "minutes", N, "The length in minutes: 90 for 1h30m.", (t, _) => Sec(t) / 60);
            p.Field("duration", "hours", N, "The length in hours: 1.5 for 1h30m.", (t, _) => Sec(t) / 3600);
            p.Field("duration", "ms", N, "The length in milliseconds.", (t, _) => Sec(t) * 1000);
            p.Method("duration", "iso", "", "The length in ISO 8601: PT1H30M.", S, (t, c) => System.Xml.XmlConvert.ToString(TimeSpan.FromSeconds(Sec(t))));
            p.Method("duration", "after", "time?: date", "The time this long after a date, or after now.", KType.Date, (t, c) => (c.Arg(0) is DateTime d ? d : DateTime.Now).AddSeconds(Sec(t)));
            p.Method("duration", "before", "time?: date", "The time this long before a date, or before now.", KType.Date, (t, c) => (c.Arg(0) is DateTime d ? d : DateTime.Now).AddSeconds(-Sec(t)));
            p.Method("duration", "plus", "other: duration", "The two lengths added: 1h + 30m is 1h30m.", "duration", (t, c) =>
                NativeParsers.DurationOf(Sec(t) + Sec(c.Arg(0) is NativeText o && o.TypeName == "duration" ? o : Native("duration", c.Str(0)))));
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

            // iban, card
            p.Field("iban", "country", "country", "The country the account is in: GB.", (t, _) => Native("country", (string)Part(t, "country")));
            p.Method("iban", "formatted", "", "The IBAN in groups of four, the way it is printed: GB82 WEST 1234 5698 7654 32.", S, (t, c) => Part(t, "formatted"));
            p.Field("iban", "bban", S, "The account number inside it, after the country code and check digits.", (t, _) => Part(t, "bban"));
            p.Field("card", "last4", S, "The last four digits, which are safe to show.", (t, _) => Part(t, "last4"));
            p.Field("card", "brand", S, "visa, mastercard, amex, discover, jcb, diners, unionpay, maestro or unknown, from its first digits.", (t, _) => Part(t, "brand"));
            p.Method("card", "reveal", "", "The whole number. Only use it to pass the card on to a payment provider; never store or log it.", S, (t, c) => T(t).Text);

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
                ("slash_path.segments", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x.segments }</echo>"),
                ("slash_path.name", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x.name }</echo>"),
                ("slash_path.parent", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x.parent }</echo>"),
                ("slash_path.depth", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x.depth }</echo>"),
                ("slash_path.is_root", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x.is_root }</echo>"),
                ("slash_path->join", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x->join('../Tomorrow') }</echo>"),
                ("slash_path->is_within", "<let name=\"x\" type=\"slash_path\" value=\"'/My Apps/Notes/Today'\" />\n<echo>{ $x->is_within('/My Apps') }</echo>"),
                ("date_format.tokens", "<let name=\"x\" type=\"date_format\" value=\"'dd MMM yyyy, HH:mm'\" />\n<echo>{ $x.tokens }</echo>"),
                ("date_format.has_date", "<let name=\"x\" type=\"date_format\" value=\"'dd MMM yyyy, HH:mm'\" />\n<echo>{ $x.has_date }</echo>"),
                ("date_format.has_time", "<let name=\"x\" type=\"date_format\" value=\"'dd MMM yyyy, HH:mm'\" />\n<echo>{ $x.has_time }</echo>"),
                ("date_format.example", "<let name=\"x\" type=\"date_format\" value=\"'dd MMM yyyy, HH:mm'\" />\n<echo>{ $x.example }</echo>"),
                ("date_format->format", "<let name=\"x\" type=\"date_format\" value=\"'dd MMM yyyy, HH:mm'\" />\n<echo>{ $x->format('2026-11-22T16:30:00'->to_date()) }</echo>"),
                ("date_format->parse", "<let name=\"x\" type=\"date_format\" value=\"'dd MMM yyyy, HH:mm'\" />\n<echo>{ $x->parse('22 Nov 2026, 16:30')->format('yyyy-MM-dd') }</echo>"),
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
                ("phone.country_code", "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n<echo>{ $x.country_code }</echo>"),
                ("phone.national", "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n<echo>{ $x.national }</echo>"),
                ("phone->tel", "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n<echo>{ $x->tel() }</echo>"),
                ("country.name", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.name }</echo>"),
                ("country.alpha3", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.alpha3 }</echo>"),
                ("country.currency", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.currency }</echo>"),
                ("country.flag", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.flag }</echo>"),
                ("currency.name", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x.name }</echo>"),
                ("currency.symbol", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x.symbol }</echo>"),
                ("currency.digits", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x.digits }</echo>"),
                ("currency->format", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x->format(1234.5) }</echo>"),
                ("locale.language", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.language }</echo>"),
                ("locale.region", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.region }</echo>"),
                ("locale.name", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.name }</echo>"),
                ("locale.native_name", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.native_name }</echo>"),
                ("locale->format", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x->format(1234.5) }</echo>"),
                ("timezone->offset", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->offset('2026-07-01'->to_date()) }</echo>"),
                ("timezone->now", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->now()->type() }</echo>"),
                ("timezone->convert", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->convert('2026-07-01T12:00:00Z'->to_date()) }</echo>"),
                ("timezone->is_dst", "<let name=\"x\" type=\"timezone\" value=\"'Europe/London'\" />\n<echo>{ $x->is_dst('2026-01-15'->to_date()) }</echo>"),
                ("duration.seconds", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x.seconds }</echo>"),
                ("duration.minutes", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x.minutes }</echo>"),
                ("duration.hours", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x.hours }</echo>"),
                ("duration.ms", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x.ms }</echo>"),
                ("duration->iso", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x->iso() }</echo>"),
                ("duration->after", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x->after('2026-11-21T09:00:00'->to_date()) }</echo>"),
                ("duration->before", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x->before('2026-11-21T09:00:00'->to_date()) }</echo>"),
                ("duration->plus", "<let name=\"x\" type=\"duration\" value=\"'1h30m'\" />\n<echo>{ $x->plus(45m) }</echo>"),
                ("cron->next", "<let name=\"x\" type=\"cron\" value=\"'0 9 * * mon-fri'\" />\n<echo>{ $x->next('2026-11-21T10:00:00'->to_date()) }</echo>"),
                ("cron->next_times", "<let name=\"x\" type=\"cron\" value=\"'0 9 * * mon-fri'\" />\n<echo>{ $x->next_times(3, '2026-11-21T10:00:00'->to_date()) }</echo>"),
                ("color->rgb", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->rgb() }</echo>"),
                ("color->hex", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->hex() }</echo>"),
                ("color->css", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->css() }</echo>"),
                ("color->mix", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->mix('blue') }</echo>"),
                ("color->lighten", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->lighten(0.5) }</echo>"),
                ("color->darken", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->darken() }</echo>"),
                ("color.luminance", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x.luminance }</echo>"),
                ("color->contrast_ratio", "<let name=\"x\" type=\"color\" value=\"'#ff8800'\" />\n<echo>{ $x->contrast_ratio('white') }</echo>"),
                ("iban.country", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x.country }</echo>"),
                ("iban->formatted", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x->formatted() }</echo>"),
                ("iban.bban", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x.bban }</echo>"),
                ("card.last4", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x.last4 }</echo>"),
                ("card.brand", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x.brand }</echo>"),
                ("card->reveal", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x->reveal()->count() }</echo>"));
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
