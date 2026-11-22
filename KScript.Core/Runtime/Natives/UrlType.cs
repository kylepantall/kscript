using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>url</c>: an absolute web address, with its scheme and host lowercased. It has no operators of its own:
    /// <c>$url + '/x'</c> joins text as for any value, and <c>->join('/x')</c> gives a url. Urls sort
    /// alphabetically, so a list of links sorts as it always has.
    /// </summary>
    public sealed class UrlType : NativeType
    {
        private static readonly string[] Schemes = { "http", "https", "ftp", "ws", "wss" };

        public override string Name => "url";

        public override string Description => "An absolute http, https, ftp, ws or wss URL. The scheme and host are lowercased.";

        public override string Example => "https://example.com/docs?page=2";

        public override RawText RawText => RawText.Word;

        public override string JsonFormat => "uri";

        public override IReadOnlyList<string> Options => new[] { "schemes", "hosts" };

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split", "replace" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "https://", "http://localhost" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number, M = KType.Map;
            const string Let = "<let name=\"x\" type=\"url\" value=\"'https://example.com:8443/docs?page=2#intro'\" />\n";
            m.Field("scheme", S, "The scheme, in lower case: https.", (t, _) => Part(t, "scheme"), Let + "<echo>{ $x.scheme }</echo>");
            m.Field("host", S, "The host name, in lower case: example.com.", (t, _) => Part(t, "host"), Let + "<echo>{ $x.host }</echo>");
            m.Field("port", N, "The port: the one written, or the scheme's usual one (443 for https).", (t, _) => Part(t, "port"), Let + "<echo>{ $x.port }</echo>");
            m.Field("path", S, "The path, starting with /: /docs/start.", (t, _) => Part(t, "path"), Let + "<echo>{ $x.path }</echo>");
            m.Field("query", M, "The query string as a map: ?page=2&q=cats gives { page: '2', q: 'cats' }.", (t, _) => new Dictionary<string, object>((Dictionary<string, object>)Part(t, "query")),
                Let + "<echo>{ $x.query }</echo>");
            m.Field("fragment", S, "The part after #, without it.", (t, _) => Part(t, "fragment"), Let + "<echo>{ $x.fragment }</echo>");
            m.Field("origin", S, "The scheme, host and any port written: https://example.com.", (t, _) => Part(t, "origin"), Let + "<echo>{ $x.origin }</echo>");
            m.Method("with_query", "values: map", "The url with these query values added or replaced (null removes one).", "url", (t, c) =>
            {
                var query = new Dictionary<string, object>((Dictionary<string, object>)Part(t, "query"));
                foreach (var kv in (Dictionary<string, object>)c.Arg(0) ?? new Dictionary<string, object>())
                {
                    if (kv.Value == null) query.Remove(kv.Key);
                    else query[kv.Key] = Values.ToStr(kv.Value);
                }
                string text = ((NativeText)t).Text;
                int hash = text.IndexOf('#');
                string fragment = hash >= 0 ? text.Substring(hash) : string.Empty;
                string head = (hash >= 0 ? text.Substring(0, hash) : text).Split('?')[0];
                string qs = string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + (Values.ToStr(kv.Value).Length > 0 ? "=" + Uri.EscapeDataString(Values.ToStr(kv.Value)) : string.Empty)));
                return Make(head + (qs.Length > 0 ? "?" + qs : string.Empty) + fragment);
            }, Let + "<echo>{ $x->with_query({ page: 3, q: 'tea' }) }</echo>");
            m.Method("join", "path: string", "The url a link to path leads to from here: ../img/a.png or /about.", "url", (t, c) =>
                Make(new Uri((Uri)Part(t, "uri"), c.Str(0)).ToString()),
                Let + "<echo>{ $x->join('../blog/') }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return (null, "it is empty");
            if (s.Any(char.IsWhiteSpace)) return (null, "it has a space in it (write it as %20)");
            int colon = s.IndexOf("://", StringComparison.Ordinal);
            if (colon <= 0) return (null, "it needs a scheme, as in https://" + s.TrimStart('/'));
            string scheme = s.Substring(0, colon).ToLowerInvariant();
            if (!Schemes.Contains(scheme)) return (null, $"{scheme}: isn't a web scheme (use {string.Join(", ", Schemes)})");
            if (!Uri.TryCreate(s, UriKind.Absolute, out Uri uri) || string.IsNullOrEmpty(uri.Host)) return (null, "it has no host after " + scheme + "://");
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
            return (new NativeText("url", text, new Dictionary<string, object>
            {
                ["scheme"] = scheme,
                ["host"] = uri.IdnHost,
                ["port"] = (double)uri.Port,
                ["path"] = uri.AbsolutePath,
                ["query"] = query,
                ["fragment"] = uri.Fragment.TrimStart('#'),
                ["origin"] = scheme + "://" + hostPort,
                ["uri"] = uri,
            }), null);
        }

        public override IEnumerable<string> OptionProblems(object value, IReadOnlyDictionary<string, object> options)
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

        private static List<string> Texts(object value) =>
            value is List<object> list ? list.Select(v => Values.ToStr(v).Trim()).ToList() : new List<string> { Values.ToStr(value).Trim() };

        private static object Part(object url, string name) => ((NativeText)url).Part(name);

        private NativeText Make(string text)
        {
            (object value, string reason) = Parse(text);
            return value as NativeText ?? throw new KScriptException($"'{text}' isn't {Info.WithArticle}: {reason}.", "Value");
        }
    }
}
