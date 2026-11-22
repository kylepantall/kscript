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
            foreach (NativeType type in NativeTypeRegistry.BuiltIn) type.Members(p);
            ListFilters.Register(p);

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
                ("iban.country", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x.country }</echo>"),
                ("iban->formatted", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x->formatted() }</echo>"),
                ("iban.bban", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x.bban }</echo>"),
                ("card.last4", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x.last4 }</echo>"),
                ("card.brand", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x.brand }</echo>"),
                ("card->reveal", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x->reveal()->count() }</echo>"));
        }
    }
}
