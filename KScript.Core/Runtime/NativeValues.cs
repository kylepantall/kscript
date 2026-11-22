using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// A value of a native type that has no class of its own (url, ip, country, ...): its tidied text and the parts
    /// its methods read. It prints as its text (masked, for a secret type such as card), keeps the string methods its
    /// type allows, and equals text that means the same once tidied.
    /// </summary>
    public sealed class NativeText : ISemanticText
    {
        public NativeText(string type, string text, IReadOnlyDictionary<string, object> parts = null, string display = null)
        {
            TypeName = type;
            Text = text;
            Parts = parts ?? new Dictionary<string, object>();
            Display = display;
        }

        public string TypeName { get; }

        /// <summary>The tidied text. For a secret type this is the real value: only its methods hand it out.</summary>
        public string Text { get; }

        /// <summary>What it was read into: a url's host and port, a semver's major, ...</summary>
        public IReadOnlyDictionary<string, object> Parts { get; }

        /// <summary>How it prints, when that isn't its text (a card shows only its last four digits).</summary>
        public string Display { get; }

        public object Part(string name) => Parts.TryGetValue(name, out object v) ? v : null;

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        public string ToText() => Display ?? Text;

        public override bool Equals(object obj) => obj is NativeText n && n.TypeName == TypeName && n.Text == Text;

        public override int GetHashCode() => HashCode.Combine(TypeName, Text);

        public override string ToString() => ToText();
    }

    /// <summary>How a native type's value is made from text, and the options it checks where it is used.</summary>
    public sealed class NativeParser
    {
        public NativeParser(Func<string, (object Value, string Reason)> parse, Func<object, IReadOnlyDictionary<string, object>, IEnumerable<string>> options = null)
        {
            Parse = parse;
            OptionProblems = options;
        }

        /// <summary>The value, or null and why the text doesn't fit (a reason such as "it has no host").</summary>
        public Func<string, (object Value, string Reason)> Parse { get; }

        /// <summary>Why a value breaks the options given where the type is used, such as ip(version: 4).</summary>
        public Func<object, IReadOnlyDictionary<string, object>, IEnumerable<string>> OptionProblems { get; }

        /// <summary>Also takes numbers (port takes 8080 as well as '8080').</summary>
        public bool TakesNumbers { get; init; }
    }

    /// <summary>The parsers of the native types, by name: the language's own here, and those packages add.</summary>
    public static class NativeParsers
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, NativeParser> Parsers = new Dictionary<string, NativeParser>(StringComparer.Ordinal);

        static NativeParsers() => BuiltIn.Register();

        /// <summary>Adds a native type: what the editor knows about it, how its values are made and what they do.</summary>
        public static void Register(NativeType type) => NativeTypeRegistry.Register(type);

        internal static void Add(string name, NativeParser parser)
        {
            lock (Gate) Parsers[name] = parser;
        }

        public static NativeParser Get(string name)
        {
            lock (Gate)
            {
                if (name == null) return null;
                if (Parsers.TryGetValue(name, out NativeParser p)) return p;
                if (NativeTypeRegistry.Get(name) is not NativeType type) return null;
                // A type on the NativeType contract: its parser, as the older callers ask for it.
                return Parsers[name] = new NativeParser(type.Parse, type.OptionProblems) { TakesNumbers = type.TakesNumbers };
            }
        }

        private static (object, string) Ok(object value) => (value, null);

        private static (object, string) No(string reason) => (null, reason);

        /// <summary>The language's own native types beyond email, password, handle, uuid and regex.</summary>
        private static class BuiltIn
        {
            public static void Register()
            {
                Add("url", new NativeParser(Url, UrlOptions));
                Add("domain", new NativeParser(Domain));
                Add("ip", new NativeParser(Ip, IpOptions));
                Add("cidr", new NativeParser(Cidr));
                Add("mac", new NativeParser(Mac));
                Add("port", new NativeParser(Port) { TakesNumbers = true });
                Add("slug", new NativeParser(Slug));
                Add("semver", new NativeParser(Semver));
                Add("mime", new NativeParser(Mime));
                Add("hex", new NativeParser(Hex, HexOptions));
                Add("base64", new NativeParser(Base64));
                Add("jwt", new NativeParser(Jwt));
                Add("json", new NativeParser(Json));
                Add("phone", new NativeParser(Phone, PhoneOptions));
                Add("country", new NativeParser(Country));
                Add("currency", new NativeParser(Currency));
                Add("locale", new NativeParser(Locale));
                Add("timezone", new NativeParser(TimeZone));
                Add("cron", new NativeParser(Cron));
                Add("color", new NativeParser(Color));
                Add("iban", new NativeParser(Iban));
                Add("card", new NativeParser(Card));
            }

            private static List<string> Texts(object value) =>
                value is List<object> list ? list.Select(v => Values.ToStr(v).Trim()).ToList() : new List<string> { Values.ToStr(value).Trim() };

            // ---- url ------------------------------------------------------------------------------------------

            private static readonly string[] UrlSchemes = { "http", "https", "ftp", "ws", "wss" };

            private static (object, string) Url(string s)
            {
                s = s.Trim();
                if (s.Length == 0) return No("it is empty");
                if (s.Any(char.IsWhiteSpace)) return No("it has a space in it (write it as %20)");
                int colon = s.IndexOf("://", StringComparison.Ordinal);
                if (colon <= 0) return No("it needs a scheme, as in https://" + s.TrimStart('/'));
                string scheme = s.Substring(0, colon).ToLowerInvariant();
                if (!UrlSchemes.Contains(scheme)) return No($"{scheme}: isn't a web scheme (use {string.Join(", ", UrlSchemes)})");
                if (!Uri.TryCreate(s, UriKind.Absolute, out Uri uri) || string.IsNullOrEmpty(uri.Host)) return No("it has no host after " + scheme + "://");
                int rest = s.IndexOfAny(new[] { '/', '?', '#' }, colon + 3);
                string authority = rest < 0 ? s.Substring(colon + 3) : s.Substring(colon + 3, rest - colon - 3);
                int at = authority.LastIndexOf('@');
                string userInfo = at >= 0 ? authority.Substring(0, at + 1) : string.Empty;
                string hostPort = (at >= 0 ? authority.Substring(at + 1) : authority).ToLowerInvariant();
                string text = scheme + "://" + userInfo + hostPort + (rest < 0 ? string.Empty : s.Substring(rest));
                var query = new Dictionary<string, object>();
                foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    string key = Uri.UnescapeDataString((eq < 0 ? pair : pair.Substring(0, eq)).Replace('+', ' '));
                    query[key] = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                }
                return Ok(new NativeText("url", text, new Dictionary<string, object>
                {
                    ["scheme"] = scheme,
                    ["host"] = uri.IdnHost,
                    ["port"] = (double)uri.Port,
                    ["path"] = uri.AbsolutePath,
                    ["query"] = query,
                    ["fragment"] = uri.Fragment.TrimStart('#'),
                    ["origin"] = scheme + "://" + hostPort,
                    ["uri"] = uri,
                }));
            }

            private static IEnumerable<string> UrlOptions(object value, IReadOnlyDictionary<string, object> options)
            {
                if (value is not NativeText url) yield break;
                if (options.TryGetValue("schemes", out object s))
                {
                    List<string> schemes = Texts(s).Select(x => x.TrimEnd(':').ToLowerInvariant()).ToList();
                    if (!schemes.Contains((string)url.Part("scheme"))) yield return $"must use {string.Join(" or ", schemes)}";
                }
                if (options.TryGetValue("hosts", out object h))
                {
                    List<string> hosts = Texts(h).Select(x => x.ToLowerInvariant()).ToList();
                    string host = (string)url.Part("host");
                    if (!hosts.Any(a => host == a || host.EndsWith("." + a, StringComparison.Ordinal))) yield return $"must be on {string.Join(" or ", hosts)}";
                }
            }

            // ---- network ------------------------------------------------------------------------------------------

            private static string DomainProblem(string d)
            {
                if (d.Length == 0) return "it is empty";
                if (d.Length > 253) return "it is longer than 253 characters";
                string[] labels = d.Split('.');
                if (labels.Length < 2) return "it needs a dot, as in example.com";
                foreach (string label in labels)
                {
                    if (label.Length == 0) return "it has two dots in a row";
                    if (label.Length > 63) return $"'{label}' is longer than 63 characters";
                    if (label[0] == '-' || label[^1] == '-') return $"'{label}' can't start or end with -";
                    char bad = label.FirstOrDefault(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'));
                    if (bad != default) return $"'{bad}' can't be used in a domain";
                }
                if (labels[^1].All(char.IsAsciiDigit)) return "its last part can't be a number (that is an IP address)";
                return null;
            }

            private static (object, string) Domain(string s)
            {
                string d = s.Trim().TrimEnd('.').ToLowerInvariant();
                try
                {
                    d = new IdnMapping().GetAscii(d);
                }
                catch (ArgumentException)
                {
                    return No("it isn't a valid domain name");
                }
                string problem = DomainProblem(d);
                if (problem != null) return No(problem);
                // Kept as it is written (bücher.example); ->ascii() gives the form DNS uses (xn--bcher-kva.example).
                string ascii = d;
                d = new IdnMapping().GetUnicode(ascii);
                string[] labels = d.Split('.');
                return Ok(new NativeText("domain", d, new Dictionary<string, object>
                {
                    ["tld"] = labels[^1],
                    ["labels"] = labels.Cast<object>().ToList(),
                    ["ascii"] = ascii,
                }));
            }

            private static (object, string) Ip(string s)
            {
                s = s.Trim();
                if (s.StartsWith('[') && s.EndsWith(']')) s = s.Substring(1, s.Length - 2);
                if (s.Length == 0) return No("it is empty");
                if (!IPAddress.TryParse(s, out IPAddress ip) || (ip.AddressFamily == AddressFamily.InterNetwork && s.Count(c => c == '.') != 3))
                    return No("it isn't an IPv4 address (such as 192.168.0.1) or an IPv6 address (such as 2001:db8::1)");
                if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId != 0) return No("an IPv6 zone (%...) can't be kept");
                return Ok(IpValue(ip));
            }

            internal static NativeText IpValue(IPAddress ip) =>
                new NativeText("ip", ip.ToString(), new Dictionary<string, object> { ["address"] = ip, ["version"] = ip.AddressFamily == AddressFamily.InterNetwork ? 4.0 : 6.0 });

            private static IEnumerable<string> IpOptions(object value, IReadOnlyDictionary<string, object> options)
            {
                if (value is NativeText ip && options.TryGetValue("version", out object v) && Values.ToStr(v) is string want && want != "any"
                    && want != ((double)ip.Part("version")).ToString(CultureInfo.InvariantCulture))
                    yield return $"must be an IPv{want} address";
            }

            private static (object, string) Cidr(string s)
            {
                s = s.Trim();
                int slash = s.IndexOf('/');
                if (slash < 0) return No("it needs / and a prefix length, as in 10.0.0.0/8");
                if (Ip(s.Substring(0, slash)) is not (NativeText address, null)) return No($"'{s.Substring(0, slash)}' isn't an IP address");
                IPAddress ip = (IPAddress)address.Part("address");
                int bits = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                if (!int.TryParse(s.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int prefix) || prefix > bits)
                    return No($"the prefix length must be 0 to {bits}");
                byte[] bytes = ip.GetAddressBytes();
                for (int i = 0; i < bytes.Length; i++)
                {
                    int keep = Math.Clamp(prefix - i * 8, 0, 8);
                    bytes[i] &= (byte)(0xFF << (8 - keep));
                }
                var network = new IPAddress(bytes);
                return Ok(new NativeText("cidr", network + "/" + prefix, new Dictionary<string, object>
                {
                    ["network"] = network,
                    ["prefix"] = (double)prefix,
                    ["bits"] = bits,
                }));
            }

            /// <summary>True if <paramref name="ip"/> is in the block.</summary>
            internal static bool InBlock(NativeText cidr, IPAddress ip)
            {
                IPAddress network = (IPAddress)cidr.Part("network");
                if (ip.IsIPv4MappedToIPv6 && network.AddressFamily == AddressFamily.InterNetwork) ip = ip.MapToIPv4();
                if (ip.AddressFamily != network.AddressFamily) return false;
                byte[] a = ip.GetAddressBytes(), n = network.GetAddressBytes();
                int prefix = (int)(double)cidr.Part("prefix");
                for (int i = 0; i < a.Length; i++)
                {
                    int keep = Math.Clamp(prefix - i * 8, 0, 8);
                    byte mask = (byte)(0xFF << (8 - keep));
                    if ((a[i] & mask) != n[i]) return false;
                }
                return true;
            }

            private static (object, string) Mac(string s)
            {
                string hex = new string(s.Trim().Where(c => c is not (':' or '-' or '.')).ToArray()).ToLowerInvariant();
                if (hex.Length != 12 || !hex.All(Uri.IsHexDigit)) return No("it needs 12 hex digits, as in 00:1a:2b:3c:4d:5e");
                return Ok(new NativeText("mac", string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)))));
            }

            private static (object, string) Port(string s)
            {
                if (!int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)) return No("it must be a whole number from 1 to 65535");
                if (port < 1 || port > 65535) return No("it must be from 1 to 65535");
                return Ok((double)port);
            }

            // ---- identifiers and formats -------------------------------------------------------------------------

            private static (object, string) Slug(string s)
            {
                s = s.Trim();
                if (s.Length == 0) return No("it is empty");
                if (s.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
                    return No($"use lowercase letters, digits and - (such as {Slugify(s)})");
                if (s[0] == '-' || s[^1] == '-') return No("it can't start or end with -");
                if (s.Contains("--")) return No("it can't have two dashes in a row");
                return Ok(new NativeText("slug", s, new Dictionary<string, object> { ["words"] = s.Split('-').Cast<object>().ToList() }));
            }

            /// <summary>Text made into a slug: 'Hello, World!' becomes hello-world.</summary>
            internal static string Slugify(string s)
            {
                string plain = new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
                var sb = new StringBuilder();
                foreach (char c in plain.ToLowerInvariant())
                {
                    if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
                    else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
                }
                string slug = sb.ToString().Trim('-');
                return slug.Length == 0 ? "post" : slug;
            }

            private static (object, string) Semver(string s)
            {
                s = s.Trim();
                if (s.StartsWith('v') || s.StartsWith('V')) s = s.Substring(1);
                string build = null, pre = null;
                int plus = s.IndexOf('+');
                if (plus >= 0) { build = s.Substring(plus + 1); s = s.Substring(0, plus); }
                int dash = s.IndexOf('-');
                if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); }
                string[] parts = s.Split('.');
                if (parts.Length != 3) return No("it needs three numbers, as in 1.4.2");
                var numbers = new List<double>();
                foreach (string p in parts)
                {
                    if (p.Length == 0 || !p.All(char.IsAsciiDigit)) return No($"'{p}' isn't a number");
                    if (p.Length > 1 && p[0] == '0') return No($"'{p}' can't start with 0");
                    numbers.Add(double.Parse(p, CultureInfo.InvariantCulture));
                }
                foreach (string id in new[] { pre, build }.Where(x => x != null))
                {
                    if (id.Split('.').Any(x => x.Length == 0 || x.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))) return No($"'{id}' must be dot-separated letters, digits and -");
                }
                string text = string.Join(".", parts) + (pre != null ? "-" + pre : string.Empty) + (build != null ? "+" + build : string.Empty);
                return Ok(new NativeText("semver", text, new Dictionary<string, object>
                {
                    ["major"] = numbers[0],
                    ["minor"] = numbers[1],
                    ["patch"] = numbers[2],
                    ["prerelease"] = pre ?? string.Empty,
                    ["build"] = build ?? string.Empty,
                }));
            }

            /// <summary>Compares two semvers by precedence (build metadata is ignored): -1, 0 or 1.</summary>
            internal static int CompareSemver(NativeText a, NativeText b)
            {
                foreach (string part in new[] { "major", "minor", "patch" })
                {
                    int c = ((double)a.Part(part)).CompareTo((double)b.Part(part));
                    if (c != 0) return Math.Sign(c);
                }
                string pa = (string)a.Part("prerelease"), pb = (string)b.Part("prerelease");
                if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
                string[] xa = pa.Split('.'), xb = pb.Split('.');
                for (int i = 0; i < Math.Min(xa.Length, xb.Length); i++)
                {
                    bool na = long.TryParse(xa[i], out long ia), nb = long.TryParse(xb[i], out long ib);
                    int c = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(xa[i], xb[i]);
                    if (c != 0) return Math.Sign(c);
                }
                return Math.Sign(xa.Length.CompareTo(xb.Length));
            }

            /// <summary>
            /// True if the version meets the range: ^1.2.0, ~1.2, &gt;=1.0.0 &lt;2.0.0, 1.x, 1.2.3, or several joined by ||.
            /// </summary>
            internal static bool Satisfies(NativeText version, string range, out string problem)
            {
                problem = null;
                foreach (string alternative in range.Split("||"))
                {
                    bool all = true;
                    foreach (string raw in alternative.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string op = new string(raw.TakeWhile(c => c is '^' or '~' or '>' or '<' or '=').ToArray());
                        string rest = raw.Substring(op.Length);
                        if (rest == "*" || rest.Length == 0 && op.Length == 0) continue;
                        string[] bits = rest.TrimStart('v').Split('.');
                        int given = Math.Min(3, bits.TakeWhile(b => b != "x" && b != "X" && b != "*").Count());
                        string full = string.Join(".", Enumerable.Range(0, 3).Select(i => i < given ? bits[i] : "0"));
                        if (Semver(full) is not (NativeText low, null))
                        {
                            problem = $"'{raw}' isn't a version range part (use ^1.2.0, ~1.2, >=1.0.0, 1.x or 1.2.3)";
                            return false;
                        }
                        if (rest.Contains('-') && given == 3 && Semver(rest) is (NativeText withPre, null)) low = withPre;
                        int cmp = CompareSemver(version, low);
                        double major = (double)low.Part("major"), minor = (double)low.Part("minor"), patch = (double)low.Part("patch");
                        bool ok;
                        switch (op)
                        {
                            case "":
                            case "=":
                                ok = given == 3 ? cmp == 0 : (double)version.Part("major") == major && (given < 2 || (double)version.Part("minor") == minor);
                                break;
                            case ">": ok = cmp > 0; break;
                            case ">=": ok = cmp >= 0; break;
                            case "<": ok = cmp < 0; break;
                            case "<=": ok = cmp <= 0; break;
                            case "~":
                                ok = cmp >= 0 && (double)version.Part("major") == major && (given < 2 || (double)version.Part("minor") == minor);
                                break;
                            case "^":
                                ok = cmp >= 0 && (major > 0 ? (double)version.Part("major") == major
                                    : minor > 0 || given < 3 ? (double)version.Part("major") == 0 && (given < 2 || (double)version.Part("minor") == minor)
                                    : (double)version.Part("major") == 0 && (double)version.Part("minor") == 0 && (double)version.Part("patch") == patch);
                                break;
                            default:
                                problem = $"'{op}' isn't a version comparison (use ^, ~, >, >=, <, <= or =)";
                                return false;
                        }
                        if (!ok) all = false;
                    }
                    if (all) return true;
                }
                return false;
            }

            private static (object, string) Mime(string s)
            {
                s = s.Trim();
                string main = s.Split(';')[0].Trim().ToLowerInvariant();
                int slash = main.IndexOf('/');
                if (slash <= 0 || slash == main.Length - 1) return No("it needs a type and a subtype, as in text/html");
                bool Token(string t) => t.Length > 0 && t.All(c => char.IsAsciiLetterOrDigit(c) || "!#$&-^_.+".Contains(c));
                string type = main.Substring(0, slash), sub = main.Substring(slash + 1);
                if (!Token(type) || !Token(sub)) return No("the type and subtype can only use letters, digits and !#$&-^_.+");
                var parameters = new Dictionary<string, object>();
                var text = new StringBuilder(main);
                foreach (string p in s.Split(';').Skip(1).Select(x => x.Trim()).Where(x => x.Length > 0))
                {
                    int eq = p.IndexOf('=');
                    if (eq <= 0) return No($"'{p}' needs a name and a value, as in charset=utf-8");
                    string name = p.Substring(0, eq).Trim().ToLowerInvariant(), value = p.Substring(eq + 1).Trim().Trim('"');
                    parameters[name] = value;
                    text.Append("; ").Append(name).Append('=').Append(value);
                }
                return Ok(new NativeText("mime", text.ToString(), new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["subtype"] = sub,
                    ["essence"] = main,
                    ["params"] = parameters,
                }));
            }

            private static (object, string) Hex(string s)
            {
                s = s.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (s.Length == 0) return No("it is empty");
                char bad = s.FirstOrDefault(c => !Uri.IsHexDigit(c));
                if (bad != default) return No($"'{bad}' isn't a hex digit (0-9, a-f)");
                return Ok(new NativeText("hex", s.ToLowerInvariant()));
            }

            private static IEnumerable<string> HexOptions(object value, IReadOnlyDictionary<string, object> options)
            {
                if (value is NativeText hex && options.TryGetValue("bytes", out object b) && Values.ToInt(b, "bytes") is int bytes && hex.Text.Length != bytes * 2)
                    yield return $"must be {bytes} bytes ({bytes * 2} hex digits), but it has {hex.Text.Length} digits";
            }

            internal static byte[] Base64Bytes(string s)
            {
                string t = s.Trim().Replace('-', '+').Replace('_', '/');
                t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
                return Convert.FromBase64String(t);
            }

            private static (object, string) Base64(string s)
            {
                s = s.Trim();
                if (s.Length == 0) return No("it is empty");
                if (s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '-' or '_' or '='))) return No("it can only use letters, digits, + / - _ and =");
                try
                {
                    Base64Bytes(s);
                }
                catch (FormatException)
                {
                    return No("it isn't complete base64 (check its length and = padding)");
                }
                return Ok(new NativeText("base64", s));
            }

            private static (object, string) Jwt(string s)
            {
                s = s.Trim();
                if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
                string[] parts = s.Split('.');
                if (parts.Length != 3) return No("it needs three parts separated by dots (header.payload.signature)");
                var decoded = new List<object>();
                foreach (string part in parts.Take(2))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(Base64Bytes(part)));
                        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return No("its header and payload must be JSON objects");
                        decoded.Add(Values.FromJson(doc.RootElement));
                    }
                    catch (Exception e) when (e is FormatException or System.Text.Json.JsonException)
                    {
                        return No("its header and payload must be base64url JSON");
                    }
                }
                return Ok(new NativeText("jwt", s, new Dictionary<string, object> { ["header"] = decoded[0], ["claims"] = decoded[1] }));
            }

            private static (object, string) Json(string s)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(s);
                    return Ok(new NativeText("json", s.Trim(), new Dictionary<string, object> { ["value"] = Values.FromJson(doc.RootElement) }));
                }
                catch (System.Text.Json.JsonException e)
                {
                    string message = e.Message.Split(" Path:")[0].Split(" LineNumber:")[0].TrimEnd('.');
                    return No("it isn't valid JSON: " + char.ToLowerInvariant(message[0]) + message.Substring(1));
                }
            }

            // ---- people and places ------------------------------------------------------------------------------

            private static (object, string) Phone(string s)
            {
                string t = s.Trim();
                if (t.StartsWith("00", StringComparison.Ordinal)) t = "+" + t.Substring(2);
                if (!t.StartsWith('+')) return No("it needs + and the country code, as in +447700900123");
                // +44 (0)7700 ...: the (0) is the trunk prefix dialled at home, not part of the number.
                string digits = new string(t.Substring(1).Replace("(0)", string.Empty).Where(c => !(c is ' ' or '-' or '.' or '(' or ')')).ToArray());
                char bad = digits.FirstOrDefault(c => !char.IsAsciiDigit(c));
                if (bad != default) return No($"'{bad}' can't be used in a phone number");
                if (digits.Length < 8 || digits.Length > 15) return No("it must have 8 to 15 digits after the +");
                if (digits[0] == '0') return No("a country code can't start with 0");
                string code = CountryCodes.CallingCode(digits);
                if (code == null) return No($"+{digits.Substring(0, Math.Min(3, digits.Length))} isn't a country calling code");
                return Ok(new NativeText("phone", "+" + digits, new Dictionary<string, object>
                {
                    ["country_code"] = code,
                    ["national"] = digits.Substring(code.Length),
                }));
            }

            private static IEnumerable<string> PhoneOptions(object value, IReadOnlyDictionary<string, object> options)
            {
                if (value is NativeText phone && options.TryGetValue("countries", out object c))
                {
                    List<string> codes = Texts(c).Select(x => x.TrimStart('+')).ToList();
                    if (!codes.Contains((string)phone.Part("country_code"))) yield return $"must be a +{string.Join(" or +", codes)} number";
                }
            }

            private static (object, string) Country(string s)
            {
                string t = s.Trim();
                if (t.Length == 0) return No("it is empty");
                RegionInfo region = CountryCodes.Find(t);
                if (region == null)
                {
                    string close = CountryCodes.Closest(t);
                    return No("it isn't an ISO 3166 country code or an English country name" + (close != null ? $". Did you mean {close}?" : string.Empty));
                }
                return Ok(new NativeText("country", region.TwoLetterISORegionName, new Dictionary<string, object>
                {
                    ["name"] = region.EnglishName,
                    ["alpha3"] = region.ThreeLetterISORegionName,
                    ["currency"] = region.ISOCurrencySymbol,
                }));
            }

            private static (object, string) Currency(string s)
            {
                string t = s.Trim().ToUpperInvariant();
                if (t.Length != 3 || !t.All(char.IsAsciiLetter)) return No("it must be a three-letter ISO 4217 code, such as GBP");
                CountryCodes.CurrencyInfo info = CountryCodes.Currency(t);
                if (info == null) return No("it isn't an ISO 4217 currency code");
                return Ok(new NativeText("currency", t, new Dictionary<string, object>
                {
                    ["name"] = info.Name,
                    ["symbol"] = info.Symbol,
                    ["digits"] = (double)info.Digits,
                }));
            }

            private static (object, string) Locale(string s)
            {
                string t = s.Trim().Replace('_', '-');
                if (t.Length == 0) return No("it is empty");
                string[] parts = t.Split('-');
                if (parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter)) return No("it must start with a two- or three-letter language code, as in en or en-GB");
                CultureInfo culture;
                try
                {
                    culture = CultureInfo.GetCultureInfo(t, predefinedOnly: true);
                }
                catch (CultureNotFoundException)
                {
                    return No("it isn't a known language tag");
                }
                string tag = string.Join("-", parts.Select((p, i) => i == 0 ? p.ToLowerInvariant() : p.Length == 2 ? p.ToUpperInvariant() : p.Length == 4 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : p.ToLowerInvariant()));
                string region = parts.Skip(1).FirstOrDefault(p => p.Length == 2 || (p.Length == 3 && p.All(char.IsAsciiDigit)))?.ToUpperInvariant() ?? string.Empty;
                return Ok(new NativeText("locale", tag, new Dictionary<string, object>
                {
                    ["language"] = parts[0].ToLowerInvariant(),
                    ["region"] = region,
                    ["name"] = culture.EnglishName,
                    ["native_name"] = culture.NativeName,
                    ["culture"] = culture,
                }));
            }

            private static (object, string) TimeZone(string s)
            {
                string t = s.Trim();
                if (t.Length == 0) return No("it is empty");
                if (t.Equals("utc", StringComparison.OrdinalIgnoreCase) || t.Equals("z", StringComparison.OrdinalIgnoreCase)) t = "UTC";
                TimeZoneInfo zone;
                try
                {
                    zone = TimeZoneInfo.FindSystemTimeZoneById(t);
                }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // An id is found in the case it was written, or not at all on a first lookup: europe/london only
                    // worked once Europe/London had been looked up. Find it without case, so it doesn't depend on
                    // what ran before or on the system (Windows lists its own names, so match their IANA names too).
                    zone = WithoutCase(t);
                    if (zone == null) return No("it isn't an IANA time zone, such as Europe/London");
                }
                string id = zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string iana) ? iana : zone.Id;
                if (!t.Contains('/') && t != "UTC" && id != t) return No($"it isn't an IANA time zone; use {id}");
                return Ok(new NativeText("timezone", id, new Dictionary<string, object> { ["zone"] = zone }));
            }

            private static TimeZoneInfo WithoutCase(string id)
            {
                foreach (TimeZoneInfo z in TimeZoneInfo.GetSystemTimeZones())
                {
                    if (string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase)) return z;
                    if (!z.HasIanaId && TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out string iana) && string.Equals(iana, id, StringComparison.OrdinalIgnoreCase)) return z;
                }

                // A zone the system doesn't list under that name (an alias, or a second city of a Windows zone):
                // IANA names start each word with a capital, so try it written that way.
                var sb = new StringBuilder(id.Length);
                bool start = true;
                foreach (char c in id)
                {
                    sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                    start = c == '/' || c == '_' || c == '-';
                }

                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(sb.ToString());
                }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    return null;
                }
            }

            // ---- time ---------------------------------------------------------------------------------------------

            private static readonly Dictionary<string, string> CronMacros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["@yearly"] = "0 0 1 1 *", ["@annually"] = "0 0 1 1 *", ["@monthly"] = "0 0 1 * *", ["@weekly"] = "0 0 * * 0",
                ["@daily"] = "0 0 * * *", ["@midnight"] = "0 0 * * *", ["@hourly"] = "0 * * * *",
            };

            private static readonly string[] CronMonths = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
            private static readonly string[] CronDays = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

            private static (object, string) Cron(string s)
            {
                string t = string.Join(" ", s.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                string expanded = CronMacros.TryGetValue(t, out string macro) ? macro : t;
                string[] fields = expanded.Split(' ');
                if (fields.Length != 5) return No("it needs five fields: minute hour day month weekday (or @daily, @hourly, ...)");
                var names = new[] { "minute", "hour", "day", "month", "weekday" };
                var ranges = new[] { (0, 59), (0, 23), (1, 31), (1, 12), (0, 7) };
                var sets = new List<object>();
                for (int f = 0; f < 5; f++)
                {
                    var allowed = new HashSet<int>();
                    foreach (string item in fields[f].ToLowerInvariant().Split(','))
                    {
                        string range = item, step = null;
                        int slash = item.IndexOf('/');
                        if (slash >= 0) { range = item.Substring(0, slash); step = item.Substring(slash + 1); }
                        (int lo, int hi) = ranges[f];
                        int from = lo, to = hi;
                        if (range != "*")
                        {
                            string[] ends = range.Split('-');
                            if (ends.Length > 2 || !CronValue(ends[0], f, out from) || !CronValue(ends[^1], f, out to))
                                return No($"'{item}' isn't a {names[f]} (use * , - / and {lo}-{hi}{(f == 3 ? " or jan-dec" : f == 4 ? " or sun-sat" : string.Empty)})");
                            if (ends.Length == 1 && step != null) to = hi;
                        }
                        if (from < lo || to > hi || from > to) return No($"'{item}' is outside {lo}-{hi} for the {names[f]}");
                        int by = 1;
                        if (step != null && (!int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out by) || by == 0)) return No($"'{step}' isn't a step for the {names[f]}");
                        for (int v = from; v <= to; v += by) allowed.Add(f == 4 && v == 7 ? 0 : v);
                    }
                    sets.Add(allowed);
                }
                return Ok(new NativeText("cron", t, new Dictionary<string, object>
                {
                    ["sets"] = sets,
                    ["any_day"] = fields[2] == "*",
                    ["any_weekday"] = fields[4] == "*",
                }));
            }

            private static bool CronValue(string text, int field, out int value)
            {
                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return true;
                value = field == 3 ? Array.IndexOf(CronMonths, text) + 1 : field == 4 ? Array.IndexOf(CronDays, text) : -1;
                return value > (field == 3 ? 0 : -1);
            }

            /// <summary>The next time after <paramref name="after"/> that the schedule fires, to the minute.</summary>
            internal static DateTime CronNext(NativeText cron, DateTime after)
            {
                var sets = ((List<object>)cron.Part("sets")).Cast<HashSet<int>>().ToArray();
                bool anyDay = (bool)cron.Part("any_day"), anyWeekday = (bool)cron.Part("any_weekday");
                DateTime t = new DateTime(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Kind).AddMinutes(1);
                DateTime limit = t.AddYears(5);
                while (t < limit)
                {
                    if (!sets[3].Contains(t.Month)) { t = new DateTime(t.Year, t.Month, 1, 0, 0, 0, t.Kind).AddMonths(1); continue; }
                    bool day = sets[2].Contains(t.Day), weekday = sets[4].Contains((int)t.DayOfWeek);
                    bool dayOk = anyDay && anyWeekday ? true : anyDay ? weekday : anyWeekday ? day : day || weekday;
                    if (!dayOk) { t = t.Date.AddDays(1); continue; }
                    if (!sets[1].Contains(t.Hour)) { t = t.Date.AddHours(t.Hour + 1); continue; }
                    if (!sets[0].Contains(t.Minute)) { t = t.AddMinutes(1); continue; }
                    return t;
                }
                throw new KScriptException($"The cron schedule '{cron.Text}' never runs.", "Value");
            }

            private static (object, string) Color(string s)
            {
                string t = s.Trim().ToLowerInvariant();
                if (t.Length == 0) return No("it is empty");
                if (ColorNames.TryGetValue(t, out string named)) t = named;
                double r, g, b, a = 1;
                if (t.StartsWith('#'))
                {
                    string h = t.Substring(1);
                    if (!h.All(Uri.IsHexDigit) || h.Length is not (3 or 4 or 6 or 8)) return No("a hex colour has 3, 4, 6 or 8 hex digits after the #, as in #ff8800");
                    if (h.Length <= 4) h = string.Concat(h.Select(c => new string(c, 2)));
                    r = Convert.ToInt32(h.Substring(0, 2), 16);
                    g = Convert.ToInt32(h.Substring(2, 2), 16);
                    b = Convert.ToInt32(h.Substring(4, 2), 16);
                    if (h.Length == 8) a = Math.Round(Convert.ToInt32(h.Substring(6, 2), 16) / 255.0, 3);
                }
                else if ((t.StartsWith("rgb(") || t.StartsWith("rgba(")) && t.EndsWith(')'))
                {
                    string[] parts = t.Substring(t.IndexOf('(') + 1).TrimEnd(')').Replace("/", ",").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length is not (3 or 4)) return No("rgb() takes three numbers and an optional alpha, as in rgb(255, 136, 0)");
                    var values = new double[4];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string p = parts[i];
                        bool percent = p.EndsWith('%');
                        if (!double.TryParse(p.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return No($"'{p}' isn't a number");
                        values[i] = i < 3 ? (percent ? v * 2.55 : v) : (percent ? v / 100 : v);
                        if (i < 3 && (values[i] < 0 || values[i] > 255)) return No($"'{p}' must be 0 to 255");
                        if (i == 3 && (values[i] < 0 || values[i] > 1)) return No($"the alpha '{p}' must be 0 to 1");
                    }
                    (r, g, b) = (Math.Round(values[0]), Math.Round(values[1]), Math.Round(values[2]));
                    if (parts.Length == 4) a = values[3];
                }
                else
                {
                    return No("use #rrggbb, rgb(r, g, b) or a CSS colour name such as orange");
                }
                return Ok(ColorOf(r, g, b, a));
            }

            internal static NativeText ColorOf(double r, double g, double b, double a)
            {
                string hex = "#" + string.Concat(new[] { r, g, b }.Select(v => ((int)Math.Clamp(Math.Round(v), 0, 255)).ToString("x2", CultureInfo.InvariantCulture)))
                    + (a < 1 ? ((int)Math.Round(Math.Clamp(a, 0, 1) * 255)).ToString("x2", CultureInfo.InvariantCulture) : string.Empty);
                return new NativeText("color", hex, new Dictionary<string, object> { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = a });
            }

            private static readonly Dictionary<string, string> ColorNames = new Dictionary<string, string>
            {
                ["black"] = "#000000", ["white"] = "#ffffff", ["red"] = "#ff0000", ["green"] = "#008000", ["blue"] = "#0000ff",
                ["yellow"] = "#ffff00", ["orange"] = "#ffa500", ["purple"] = "#800080", ["pink"] = "#ffc0cb", ["brown"] = "#a52a2a",
                ["gray"] = "#808080", ["grey"] = "#808080", ["silver"] = "#c0c0c0", ["gold"] = "#ffd700", ["navy"] = "#000080",
                ["teal"] = "#008080", ["lime"] = "#00ff00", ["aqua"] = "#00ffff", ["cyan"] = "#00ffff", ["magenta"] = "#ff00ff",
                ["fuchsia"] = "#ff00ff", ["maroon"] = "#800000", ["olive"] = "#808000", ["indigo"] = "#4b0082", ["violet"] = "#ee82ee",
                ["coral"] = "#ff7f50", ["salmon"] = "#fa8072", ["crimson"] = "#dc143c", ["tomato"] = "#ff6347", ["turquoise"] = "#40e0d0",
                ["beige"] = "#f5f5dc", ["ivory"] = "#fffff0", ["khaki"] = "#f0e68c", ["lavender"] = "#e6e6fa", ["plum"] = "#dda0dd",
                ["orchid"] = "#da70d6", ["tan"] = "#d2b48c", ["chocolate"] = "#d2691e", ["skyblue"] = "#87ceeb", ["steelblue"] = "#4682b4",
                ["slategray"] = "#708090", ["darkgray"] = "#a9a9a9", ["lightgray"] = "#d3d3d3", ["transparent"] = "#00000000",
            };

            // ---- money --------------------------------------------------------------------------------------------

            private static (object, string) Iban(string s)
            {
                string t = new string(s.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
                if (t.Length < 15 || t.Length > 34) return No("it must be 15 to 34 letters and digits");
                if (!char.IsAsciiLetter(t[0]) || !char.IsAsciiLetter(t[1]) || !char.IsAsciiDigit(t[2]) || !char.IsAsciiDigit(t[3])) return No("it must start with a country code and two check digits, as in GB82");
                if (t.Any(c => !char.IsAsciiLetterOrDigit(c))) return No("it can only use letters and digits");
                if (CountryCodes.Find(t.Substring(0, 2)) == null) return No($"{t.Substring(0, 2)} isn't a country code");
                string moved = t.Substring(4) + t.Substring(0, 4);
                int remainder = 0;
                foreach (char c in moved)
                {
                    string digits = char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture);
                    foreach (char d in digits) remainder = (remainder * 10 + (d - '0')) % 97;
                }
                if (remainder != 1) return No("its check digits don't match: check it for a typing mistake");
                return Ok(new NativeText("iban", t, new Dictionary<string, object>
                {
                    ["country"] = t.Substring(0, 2),
                    ["bban"] = t.Substring(4),
                    ["formatted"] = string.Join(" ", Enumerable.Range(0, (t.Length + 3) / 4).Select(i => t.Substring(i * 4, Math.Min(4, t.Length - i * 4)))),
                }));
            }

            private static (object, string) Card(string s)
            {
                string t = new string(s.Where(c => !(c is ' ' or '-')).ToArray());
                if (t.Length == 0) return No("it is empty");
                if (!t.All(char.IsAsciiDigit)) return No("it can only use digits, spaces and -");
                if (t.Length < 12 || t.Length > 19) return No("it must have 12 to 19 digits");
                int sum = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    int d = t[t.Length - 1 - i] - '0';
                    if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
                    sum += d;
                }
                if (sum % 10 != 0) return No("its check digit doesn't match: check it for a typing mistake");
                string last4 = t.Substring(t.Length - 4);
                return Ok(new NativeText("card", t, new Dictionary<string, object> { ["last4"] = last4, ["brand"] = CardBrand(t) }, "•••• " + last4));
            }

            private static string CardBrand(string n)
            {
                int Prefix(int digits) => int.Parse(n.AsSpan(0, digits), CultureInfo.InvariantCulture);
                if (n[0] == '4') return "visa";
                if (Prefix(2) is >= 51 and <= 55 || Prefix(4) is >= 2221 and <= 2720) return "mastercard";
                if (Prefix(2) is 34 or 37) return "amex";
                if (Prefix(4) == 6011 || Prefix(2) == 65 || Prefix(3) is >= 644 and <= 649) return "discover";
                if (Prefix(2) == 35) return "jcb";
                if (Prefix(2) is 36 or 38 or 39 || Prefix(3) is >= 300 and <= 305) return "diners";
                if (Prefix(2) == 62) return "unionpay";
                if (Prefix(2) is 50 or >= 56 and <= 69) return "maestro";
                return "unknown";
            }
        }

        // Methods use these.
        internal static string Slugify(string s) => BuiltIn.Slugify(s);
        internal static bool InBlock(NativeText cidr, IPAddress ip) => BuiltIn.InBlock(cidr, ip);
        internal static NativeText IpValue(IPAddress ip) => BuiltIn.IpValue(ip);
        internal static int CompareSemver(NativeText a, NativeText b) => BuiltIn.CompareSemver(a, b);
        internal static bool Satisfies(NativeText v, string range, out string problem) => BuiltIn.Satisfies(v, range, out problem);
        internal static byte[] Base64Bytes(string s) => BuiltIn.Base64Bytes(s);
        internal static NativeText DurationOf(double seconds) => DurationType.Of(seconds);
        internal static DateTime CronNext(NativeText cron, DateTime after) => BuiltIn.CronNext(cron, after);
        internal static NativeText ColorOf(double r, double g, double b, double a) => BuiltIn.ColorOf(r, g, b, a);
    }
}
