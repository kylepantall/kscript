using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using KScript.Runtime;

namespace KScript.Pages
{
    /// <summary>
    /// A browser event for a &lt;live&gt; block: which block, which handler, the block's state and any
    /// loop variables the handler needs. State and arguments are signed so the browser can't change them.
    /// </summary>
    public sealed class LiveEvent
    {
        public string BlockId { get; set; }

        /// <summary>The handler's number within the block, or -1 when only bound inputs changed.</summary>
        public int Handler { get; set; } = -1;

        public Dictionary<string, object> State { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> Args { get; set; } = new Dictionary<string, object>();

        internal string StateToken { get; set; }
        internal string ArgsToken { get; set; }
        internal IReadOnlyList<(string Id, string State)> Parents { get; set; } = Array.Empty<(string, string)>();

        /// <summary>Current values of inputs with bind="name".</summary>
        public Dictionary<string, object> Binds { get; set; } = new Dictionary<string, object>();

        /// <summary>Browser-supplied event details such as input state, key modifiers, coordinates and drag value.</summary>
        public Dictionary<string, object> Event { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> Files { get; set; }

        /// <summary>The re-rendered block, set by the renderer.</summary>
        internal string Output { get; set; }
    }

    /// <summary>Signing, handler numbering and the browser runtime for &lt;live&gt; blocks.</summary>
    public static class PageLive
    {
        private static readonly byte[] _key = LoadKey();

        private static byte[] LoadKey()
        {
            string secret = Environment.GetEnvironmentVariable("KSCRIPT_SECRET");
            return string.IsNullOrEmpty(secret) ? RandomNumberGenerator.GetBytes(32) : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }

        public static string Sign(Dictionary<string, object> values, string scope = null)
        {
            string json = Values.ToJson(values);
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            using var hmac = new HMACSHA256(_key);
            string sig = Convert.ToBase64String(hmac.ComputeHash(SignedBytes(payload, scope)));
            return payload + "." + sig;
        }

        private static byte[] SignedBytes(string payload, string scope) =>
            Encoding.UTF8.GetBytes(scope == null ? payload : scope + "\0" + payload);

        public static Dictionary<string, object> Verify(string token, string scope = null)
        {
            if (string.IsNullOrEmpty(token)) return scope == null ? new Dictionary<string, object>() : throw new LiveExpiredException();
            int dot = token.LastIndexOf('.');
            if (dot <= 0) throw new LiveExpiredException();
            string payload = token.Substring(0, dot);
            using var hmac = new HMACSHA256(_key);
            byte[] expected = hmac.ComputeHash(SignedBytes(payload, scope));
            byte[] actual;
            try { actual = Convert.FromBase64String(token.Substring(dot + 1)); }
            catch (FormatException) { throw new LiveExpiredException(); }
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) throw new LiveExpiredException();
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return ParseJson(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static object ParseJson(string json)
        {
            try { return Values.ParseJson(json); }
            catch (KScriptException) { throw new PageException("Invalid live request."); }
        }

        /// <summary>Numbers the on-* handlers inside a live block in document order (once per parse).</summary>
        internal static List<PageAttribute> Handlers(PageElement live)
        {
            var list = new List<PageAttribute>();
            void Walk(PageElement el)
            {
                foreach (var child in el.Children.OfType<PageElement>())
                {
                    if (child.Name == "placeholder") continue;
                    foreach (var a in child.Attributes.Where(a => a.Name.StartsWith("on-", StringComparison.Ordinal)))
                    {
                        a.HandlerIndex = list.Count;
                        list.Add(a);
                    }
                    if (child.Name != "live") Walk(child);
                }
            }
            Walk(live);
            return list;
        }

        /// <summary>The browser runtime, served at /_ks/live.js. Pages never need to include it themselves.</summary>
        public static string Script => BrowserRuntime.Value;

        private static readonly Lazy<string> BrowserRuntime = new Lazy<string>(() =>
            "(function () {\n" + ReadScript("model-forms.js") + "\n" + ReadScript("live-dom.js") + "\n"
                + ReadScript("live-sort.js") + "\n" + ReadScript("live.js") + "\n}());\n" + ReadScript("passkeys.js"));

        private static string ReadScript(string name)
        {
            var asm = typeof(PageLive).GetTypeInfo().Assembly;
            using var stream = asm.GetManifestResourceStream("KScript.Pages." + name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Reads the JSON body the runtime posts to /_ks/live.</summary>
        public static (string Page, LiveEvent Event) ParseRequest(string json, bool routed = false)
        {
            var body = ParseJson(json) as Dictionary<string, object>
                ?? throw new PageException("Invalid live request.");
            string Get(string k) => body.TryGetValue(k, out var v) ? Values.ToStr(v) : null;

            var ev = new LiveEvent
            {
                BlockId = Get("block") ?? throw new PageException("Live request has no block id."),
                Handler = body.TryGetValue("handler", out var h) && h != null && Values.TryNumber(h, out double hn) ? (int)hn : -1,
                StateToken = Get("state"),
                ArgsToken = Get("args"),
                Binds = body.TryGetValue("binds", out var b) && b is Dictionary<string, object> bm ? bm : new Dictionary<string, object>(),
                Event = body.TryGetValue("event", out var e) && e is Dictionary<string, object> em ? em : new Dictionary<string, object>(),
            };
            if (body.TryGetValue("parents", out object parents) && parents != null)
            {
                if (!routed || parents is not List<object> list || list.Count > 32)
                    throw new PageException("Invalid live component ancestry.");
                var chain = new List<(string, string)>();
                foreach (object parent in list)
                {
                    if (parent is not Dictionary<string, object> record || record.GetValueOrDefault("id") is not string id ||
                        record.GetValueOrDefault("state") is not string token)
                        throw new PageException("Invalid live component ancestry.");
                    chain.Add((id, token));
                }
                ev.Parents = chain;
            }
            if (!routed)
            {
                ev.State = Verify(ev.StateToken);
                ev.Args = Verify(ev.ArgsToken);
            }
            return (Get("page") ?? "/", ev);
        }
    }

    /// <summary>The signed state didn't verify, usually because the server restarted. The browser reloads the page.</summary>
    public sealed class LiveExpiredException : Exception
    {
        public LiveExpiredException() : base("This page has expired. Reload to continue.") { }
    }
}
