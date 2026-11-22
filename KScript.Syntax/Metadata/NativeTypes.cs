using System;
using System.Collections.Generic;
using System.Linq;

namespace KScript.Metadata
{
    /// <summary>
    /// What the editor and the runtime know about a native type: text with a meaning of its own, such as <c>email</c>,
    /// <c>url</c> or <c>country</c>. It is checked and tidied when it is made, prints as its text and keeps the string
    /// methods that make sense for it. The parser and methods live in the runtime (KScript.Core); this is the part the
    /// parser, analyzer and docs share.
    /// </summary>
    public sealed class NativeTypeInfo
    {
        public NativeTypeInfo(string name, string description, string example, IEnumerable<string> stringMethods = null)
        {
            if (string.IsNullOrWhiteSpace(name) || !name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') || !char.IsAsciiLetterLower(name[0]))
                throw new ArgumentException($"A native type's name is lower case letters, digits and _, such as ip or semver: '{name}' isn't.", nameof(name));
            Name = name;
            Description = description;
            Example = example;
            StringMethods = new HashSet<string>(stringMethods ?? NativeTypes.Searchable, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The type's name, as written in <c>x: url</c>.</summary>
        public string Name { get; }

        /// <summary>One sentence for hovers and the docs.</summary>
        public string Description { get; }

        /// <summary>A value of the type, for docs and hovers: <c>https://example.com/docs</c>.</summary>
        public string Example { get; }

        /// <summary>The string methods its values keep, such as contains and starts_with. Others need ->to_string() first.</summary>
        public HashSet<string> StringMethods { get; }

        /// <summary>The package that adds it: null for the language's own types.</summary>
        public string Package { get; set; }

        /// <summary>How a raw (unquoted) value is read where the type is expected: "word" (most types) or "handle" (@name).</summary>
        public string RawKind { get; init; } = "word";

        /// <summary>Its values print masked (a card number, a password), so they never show up in logs or JSON.</summary>
        public bool Secret { get; init; }

        /// <summary>Arguments it takes where it is used, besides min, max, pattern, one_of and not: uuid(v: 4).</summary>
        public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

        /// <summary>The OpenAPI / JSON Schema string format, such as uri or ipv4, when there is one.</summary>
        public string JsonFormat { get; init; }

        /// <summary>"a" or "an", for messages: an email, a url.</summary>
        public string Article { get; init; }

        public string WithArticle => (Article ?? ("aeiou".Contains(Name[0]) && Name != "uuid" && Name != "url" ? "an" : "a")) + " " + Name;
    }

    /// <summary>
    /// Every native type: the language's own (email, url, ip, country, ...) and those a package adds with
    /// <c>p.NativeType(...)</c>. The runtime registers a parser for each one under the same name.
    /// </summary>
    public static class NativeTypes
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, NativeTypeInfo> Types = new Dictionary<string, NativeTypeInfo>(StringComparer.Ordinal);

        /// <summary>The string methods most native types keep: searching, measuring, cutting up and showing their text.</summary>
        public static readonly string[] Searchable =
        {
            "contains", "starts_with", "ends_with", "index_of", "count", "slice", "matches", "truncate",
            "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json",
        };

        private static string[] With(params string[] more) => Searchable.Concat(more).ToArray();

        static NativeTypes()
        {
            // The first five came first, and keep the string methods they had (PR 86).
            Add(new NativeTypeInfo("email", "An email address such as joan@example.com. Spaces are trimmed and the domain is lowercased.", "joan@example.com",
                With("lower", "upper", "split", "replace")) { Article = "an", JsonFormat = "email", Options = new[] { "domains" } });
            Add(new NativeTypeInfo("password", "A secret. It prints as ********, can't be compared with ==, and is hashed to be stored.", "'correct horse battery staple'",
                Array.Empty<string>()) { Secret = true, Options = new[] { "upper", "lower", "digit", "symbol", "common", "score", "algorithm" }, JsonFormat = "password" });
            Add(new NativeTypeInfo("handle", "@ followed by letters, digits, _, . or -: a username such as @joan. Lowercased.", "@joan",
                new[] { "contains", "starts_with", "ends_with", "count", "upper", "slice", "matches", "truncate", "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json" })
                { RawKind = "handle" });
            Add(new NativeTypeInfo("uuid", "A UUID, versions 1 to 8 and the nil and max UUIDs, lowercased to 8-4-4-4-12.", "3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f",
                new[] { "contains", "starts_with", "ends_with", "upper", "slice", "split", "pad_left", "pad_right", "escape", "url_encode", "markdown_escape", "json" })
                { Options = new[] { "v" }, JsonFormat = "uuid" });
            Add(new NativeTypeInfo("regex", "A regular expression with flags i, m, s and x, written /.../ in code.", "/^[a-z]+$/i",
                new[] { "contains", "starts_with", "ends_with", "count", "escape", "markdown_escape", "json" }) { JsonFormat = "regex" });

            // Web and network.
            Add(new NativeTypeInfo("url", "An absolute http, https, ftp, ws or wss URL. The scheme and host are lowercased.", "https://example.com/docs?page=2",
                With("split", "replace")) { JsonFormat = "uri", Options = new[] { "schemes", "hosts" } });
            Add(new NativeTypeInfo("domain", "A domain name such as example.com or api.example.co.uk, lowercased, without a trailing dot.", "example.com",
                With("split", "upper")) { JsonFormat = "hostname" });
            Add(new NativeTypeInfo("ip", "An IPv4 or IPv6 address, written the standard way: 192.168.0.1 or 2001:db8::1.", "192.168.0.1",
                With("split")) { Article = "an", JsonFormat = "ip", Options = new[] { "version" } });
            Add(new NativeTypeInfo("cidr", "A block of IP addresses: an address, / and a prefix length, such as 10.0.0.0/8.", "10.0.0.0/8",
                With("split")));
            Add(new NativeTypeInfo("mac", "A network card's MAC address, written as six lowercase pairs: 00:1a:2b:3c:4d:5e.", "00:1a:2b:3c:4d:5e",
                With("split", "upper")));
            Add(new NativeTypeInfo("port", "A network port, a whole number from 1 to 65535.", "8080",
                Array.Empty<string>()));

            // Identifiers and formats.
            Add(new NativeTypeInfo("slug", "Lowercase words joined by -, for URLs: my-first-post.", "my-first-post",
                With("split", "upper", "replace")));
            Add(new NativeTypeInfo("semver", "A semantic version such as 1.4.2, 2.0.0-beta.1 or 1.0.0+build.5. A leading v is dropped.", "1.4.2",
                With("split")));
            Add(new NativeTypeInfo("mime", "A media type such as text/html or application/json, lowercased, with any parameters.", "application/json",
                With("split")) { JsonFormat = "media-type" });
            Add(new NativeTypeInfo("hex", "Hexadecimal digits, such as a hash: a3f9c0. Lowercased; 0x is dropped.", "a3f9c0",
                With("upper")) { Options = new[] { "bytes" } });
            Add(new NativeTypeInfo("base64", "Base64 text (standard or URL-safe). ->decode() gives the text it holds.", "aGVsbG8=",
                Searchable) { JsonFormat = "byte" });
            Add(new NativeTypeInfo("jwt", "A JSON Web Token: three base64url parts. Its claims can be read; its signature isn't checked.", "'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig'",
                Searchable));
            Add(new NativeTypeInfo("json", "Text that holds valid JSON. ->json() gives the value.", "'{\"a\": 1}'",
                Searchable));

            // People and places.
            Add(new NativeTypeInfo("phone", "A phone number in E.164: + then the country code and number, 8 to 15 digits. Spaces, dots, dashes and brackets are dropped.", "+447700900123",
                Searchable) { Options = new[] { "countries" } });
            Add(new NativeTypeInfo("country", "An ISO 3166 country code. Two-letter, three-letter and English names are read; it is kept as two capitals, such as GB.", "GB",
                Searchable));
            Add(new NativeTypeInfo("currency", "An ISO 4217 currency code, kept in capitals: GBP, USD, EUR.", "GBP",
                Searchable));
            Add(new NativeTypeInfo("locale", "A language tag (BCP 47) such as en, en-GB or pt-BR, in its usual case.", "en-GB",
                With("split")));
            Add(new NativeTypeInfo("timezone", "An IANA time zone such as Europe/London or America/New_York, or UTC.", "Europe/London",
                With("split")));

            // Time and things that run on it.
            Add(new NativeTypeInfo("duration", "A length of time: 1h30m, 90s, 2d or ISO 8601 such as PT1H30M. Kept in its short form.", "1h30m",
                Searchable));
            Add(new NativeTypeInfo("cron", "A cron schedule of five fields (minute hour day month weekday), or @daily, @hourly and the like.", "'*/15 9-17 * * mon-fri'",
                Searchable));
            Add(new NativeTypeInfo("color", "A colour: #rgb, #rrggbb, #rrggbbaa, rgb(...) or a CSS name. Kept as lowercase #rrggbb (#rrggbbaa with alpha).", "#ff8800",
                With("upper")));

            // Money.
            Add(new NativeTypeInfo("iban", "An international bank account number, checked with its mod-97 digits. Kept in capitals without spaces.", "GB82WEST12345698765432",
                Searchable) { Article = "an" });
            Add(new NativeTypeInfo("card", "A payment card number, checked with the Luhn digit. It prints masked apart from the last four digits.", "'4242 4242 4242 4242'",
                Array.Empty<string>()) { Secret = true });
        }

        private static void Add(NativeTypeInfo info) => Types[info.Name] = info;

        /// <summary>Adds a native type, as a package does when it loads. A name that is taken can't be reused.</summary>
        public static void Register(NativeTypeInfo info)
        {
            lock (Gate)
            {
                if (Types.TryGetValue(info.Name, out NativeTypeInfo existing) && existing.Package != info.Package)
                    throw new InvalidOperationException($"There is already a native type called {info.Name}" + (existing.Package != null ? $" (from {existing.Package})." : "."));
                Types[info.Name] = info;
            }
        }

        /// <summary>The native type with this name (case matters: Color is a script's own type, color is the native one).</summary>
        public static NativeTypeInfo Get(string name)
        {
            if (name == null) return null;
            lock (Gate) return Types.TryGetValue(name, out NativeTypeInfo info) ? info : null;
        }

        public static bool IsNative(string name) => Get(name) != null;

        /// <summary>Every native type, the language's own first.</summary>
        public static IReadOnlyList<NativeTypeInfo> All
        {
            get { lock (Gate) return Types.Values.ToList(); }
        }

        public static IEnumerable<string> Names => All.Select(t => t.Name);
    }
}
