using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using KScript.Runtime;

namespace KScript.Pages
{
    /// <summary>
    /// @visit('/'): a page of the project rendered in a test, as a browser would get it. <c>->click('Add')</c> (or <c>->change('Buy milk')</c>, <c>->submit()</c>) runs
    /// the live handler of the element with that text and redraws its block, <c>->fill('draft', 'milk')</c> types into
    /// a bound input, and <c>.html</c>, <c>.text</c> and <c>->classes('Buy milk')</c> show the result.
    /// </summary>
    public sealed class PageVisit : IKObject
    {
        private static readonly Regex StartTag = new Regex(@"<([a-zA-Z][\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled);
        private static readonly HashSet<string> VoidTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
        };

        private readonly PageSite site;
        private readonly Dictionary<string, object> binds = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>The most redirects a visit follows, as a browser would, before it gives up.</summary>
        private const int MaxRedirects = 5;

        /// <summary>For a page a route served: who asks (the script's host and folder), the headers it gave, and the cookies the app set.</summary>
        private readonly KScriptHost host;
        private readonly string folder;
        private readonly Dictionary<string, object> headers = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> cookies = new Dictionary<string, string>(StringComparer.Ordinal);

        private PageVisit(PageSite site, string path, KScriptHost host, string folder)
        {
            this.site = site;
            this.host = host;
            this.folder = folder;
            Path = path;
        }

        public string TypeName => KScript.Metadata.KType.PageVisit;

        /// <summary>The address visited, such as /tasks.</summary>
        public string Path { get; private set; }

        /// <summary>The page's HTML as it is now, after any clicks.</summary>
        public string Html { get; private set; }

        public double Status { get; private set; }

        public string ToText() => $"page {Path}";

        /// <summary>
        /// Renders a page of the site the calling script belongs to, as kscript serve answers it: the project's routes
        /// first (a view: route or a controller, with their middleware), then the file pages under serve.root (in its
        /// kscript.json, else the script's folder). <paramref name="options"/> takes headers, as @api() does.
        /// </summary>
        public static PageVisit Open(CallContext call, string path, Dictionary<string, object> options = null, string bearer = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw call.Error("@visit() needs the address of a page, such as @visit('/') or @visit('/tasks').", "Argument");
            if (!path.StartsWith('/')) path = "/" + path;
            string start = call.Module?.Directory ?? call.Interpreter.Host.WorkingDirectory;
            Projects.ProjectConfig config = Projects.ProjectConfig.FindAndLoad(start);
            var site = new PageSite(config?.SiteRoot ?? start);
            var visit = new PageVisit(site, path, call.Interpreter.Host, start);
            if (options?.GetValueOrDefault("headers") is object given && Values.Unwrap(given) is Dictionary<string, object> map)
                foreach (var kv in map) visit.headers[kv.Key.ToLowerInvariant()] = Values.ToStr(kv.Value);
            if (!string.IsNullOrEmpty(bearer)) visit.headers["authorization"] = "Bearer " + bearer;

            if (visit.Routed(call, "GET", path, null) is LocalResponse routed)
            {
                visit.FollowRedirects(call, routed);
                return visit;
            }

            var uri = new Uri(new Uri("http://localhost"), path);
            PageRoute route = site.Resolve(uri.AbsolutePath);
            if (route.Kind != PageRoute.RouteKind.Page) throw call.Error($"@visit('{path}'): the site has no page at {uri.AbsolutePath}.", "NotFound");
            var request = new Dictionary<string, object>
            {
                [RequestFields.Path.Name] = uri.AbsolutePath,
                [RequestFields.Url.Name] = uri.PathAndQuery,
                [RequestFields.Query.Name] = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).ToDictionary(q => q.Key, q => (object)q.Value.ToString()),
            };
            if (route.Locale != null) request[RequestFields.Locale.Name] = route.Locale;
            PageResult result;
            try
            {
                result = PageRenderer.RenderFile(new PageContext(site.Root, request) { StartupConfig = site.StartupConfig }, route.File, route.Params);
            }
            catch (PageException ex)
            {
                throw call.Error($"@visit('{path}'): the page didn't render. {ex.Message}", "Http");
            }
            visit.Html = result.Body;
            visit.Status = result.Status;
            return visit;
        }

        /// <summary>
        /// Sends a request for <paramref name="path"/> to the project's routes, with the visit's headers and cookies, and
        /// keeps the cookies it sets. Null when no route answers it.
        /// </summary>
        private LocalResponse Routed(CallContext call, string method, string path, string body, Dictionary<string, object> extra = null)
        {
            var uri = new Uri(new Uri("http://localhost"), path);
            var sent = new Dictionary<string, object>(headers, StringComparer.OrdinalIgnoreCase);
            if (extra != null) foreach (var kv in extra) sent[kv.Key] = kv.Value;
            if (cookies.Count > 0)
            {
                string jar = string.Join("; ", cookies.Select(c => c.Key + "=" + c.Value));
                sent["cookie"] = sent.GetValueOrDefault("cookie") is object own && Values.ToStr(own).Length > 0 ? Values.ToStr(own) + "; " + jar : jar;
            }
            var request = new Dictionary<string, object>
            {
                ["method"] = method,
                ["path"] = uri.AbsolutePath,
                ["url"] = uri.PathAndQuery,
                ["query"] = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).ToDictionary(q => q.Key, q => (object)q.Value.ToString()),
                ["headers"] = sent,
                ["body"] = body ?? string.Empty,
            };
            LocalResponse response = LocalRequests.TrySendToRoutes(host, folder, request);
            if (response == null) return null;
            object setCookie = response.Headers.GetValueOrDefault("Set-Cookie");
            foreach (object one in setCookie is List<object> list ? list : setCookie != null ? new List<object> { setCookie } : new List<object>())
            {
                string pair = Values.ToStr(one).Split(';')[0];
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string name = pair.Substring(0, eq).Trim(), value = pair.Substring(eq + 1).Trim();
                // A cookie set to expire is the server clearing it.
                if (value.Length == 0 || Values.ToStr(one).Contains("max-age=0", StringComparison.OrdinalIgnoreCase)) cookies.Remove(name);
                else cookies[name] = value;
            }
            return response;
        }

        /// <summary>Takes a route's answer as the page, following its redirects (to this site) as a browser does.</summary>
        private void FollowRedirects(CallContext call, LocalResponse response)
        {
            for (int hops = 0; ; hops++)
            {
                Status = response.Status;
                string location = response.Status is >= 300 and < 400 ? response.Headers.GetValueOrDefault("Location") as string : null;
                if (location == null || !location.StartsWith('/') || location.StartsWith("//", StringComparison.Ordinal))
                {
                    if (response.Status == 404) throw call.Error($"@visit('{Path}'): the site has no page at {Path} (its route answered 404).", "NotFound");
                    Html = response.Body ?? string.Empty;
                    return;
                }
                if (hops >= MaxRedirects) throw call.Error($"@visit('{Path}'): the page redirected more than {MaxRedirects} times.", "Http");
                Path = location;
                response = Routed(call, "GET", location, null)
                    ?? throw call.Error($"@visit(): {Path} redirected to {location}, which has no route.", "NotFound");
            }
        }

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "path" => Path,
                "status" => Status,
                "html" => Html,
                "text" => Text(Html),
                _ => null,
            };
            return name is "path" or "status" or "html" or "text";
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = this;
            switch (name)
            {
                case "click":
                    Fire(call, "click", Label(call));
                    return true;
                case "change":
                    Fire(call, "change", Label(call));
                    return true;
                case "press":
                {
                    // press('enter', in: 'Search') or press([ctrl, s]) sends a keydown, as typing it would.
                    var keys = (call.Arg(0) is List<object> list ? list : new List<object> { call.Arg(0) }).Select(k => Values.ToStr(k).ToLowerInvariant()).ToList();
                    string key = keys.LastOrDefault(k => !KScript.Syntax.Markup.HandlerShorthands.ModifierKeys.Contains(k))
                        ?? throw call.Error("->press() needs a key, such as press('enter') or press(['ctrl', 's']).", "Argument");
                    var ev = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "keydown",
                        ["key"] = KScript.Syntax.Markup.HandlerShorthands.KeyNames.TryGetValue(key, out string browser) ? browser : key,
                    };
                    foreach (string m in KScript.Syntax.Markup.HandlerShorthands.ModifierKeys) ev[m] = keys.Contains(m);
                    Fire(call, "keydown", call.Arg(1, "in") is string target && target.Trim().Length > 0 ? target.Trim() : null, ev);
                    return true;
                }
                case "submit":
                    Fire(call, "submit", call.Has(0) ? Label(call) : null);
                    return true;
                case "fill":
                {
                    string field = Values.ToStr(call.Arg(0)).TrimStart('$');
                    if (!Html.Contains($"data-ks-bind=\"{Values.HtmlEscape(field)}\"", StringComparison.Ordinal))
                        throw call.Error($"->fill('{field}', ...): the page has no input bound to {field}.", "NotFound");
                    binds[field] = call.Arg(1);
                    return true;
                }
                case "classes":
                    result = Classes(call, Label(call));
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        private static string Label(CallContext call) =>
            call.Arg(0) is string label && label.Trim().Length > 0 ? label.Trim() : throw call.Error($"{call.Display} needs the text of a button or link on the page, such as 'Add'.", "Argument");

        /// <summary>Runs the live handler of the element whose text is <paramref name="label"/> and puts its block's new HTML in the page.</summary>
        private void Fire(CallContext call, string eventName, string label, Dictionary<string, object> ev = null)
        {
            string attribute = "data-ks-on-" + eventName;
            var candidates = new List<(int Start, string Handler, string Args)>();
            foreach (Match tag in StartTag.Matches(Html))
            {
                Match handler = Regex.Match(tag.Groups[2].Value, $@"\b{attribute}=""(\d+)""");
                if (!handler.Success) continue;
                int end = ElementEnd(tag);
                string text = Text(Html[tag.Index..end]);
                // An input has no text of its own: the label around it names it.
                if (text.Length == 0 && EnclosingLabel(tag.Index) is string around) text = around;
                if (label == null || text == label || (eventName == "submit" && text.Contains(label, StringComparison.Ordinal)) || Attribute(tag, "aria-label") == label || Attribute(tag, "title") == label || Attribute(tag, "placeholder") == label)
                    candidates.Add((tag.Index, handler.Groups[1].Value, Attribute(tag, "data-ks-args-" + handler.Groups[1].Value) ?? Attribute(tag, "data-ks-args")));
            }
            if (candidates.Count == 0)
            {
                throw call.Error(label == null
                    ? (eventName == "submit" ? $"{call.Display}: the page has no form with on-{eventName}." : $"{call.Display}: nothing on the page has on-{eventName}.")
                    : $"{call.Display}: nothing on the page with the text '{label}' has on-{eventName}.", "NotFound");
            }
            (int start, string number, string args) = candidates[0];
            Match block = StartTag.Matches(Html).Where(t => t.Index < start && t.Groups[2].Value.Contains("data-ks-live=\"", StringComparison.Ordinal) && ElementEnd(t) > start)
                .LastOrDefault() ?? throw call.Error($"{call.Display}: '{label}' isn't inside a live block, so it has nothing to run.", "Context");
            Run(call, eventName, label, block, number, args, ev ?? new Dictionary<string, object> { ["type"] = eventName }, 0);
        }

        /// <summary>The most times events may set off other events (one block's handler emitting to the next), as in the browser.</summary>
        private const int MaxEventHops = 16;

        /// <summary>Runs handler <paramref name="number"/> of <paramref name="block"/>, redraws it, then delivers the events it emitted.</summary>
        private void Run(CallContext call, string eventName, string label, Match block, string number, string args, Dictionary<string, object> ev, int hop)
        {
            int blockEnd = ElementEnd(block);
            string blockHtml = Html[block.Index..blockEnd];
            var blockBinds = binds.Where(b => blockHtml.Contains($"data-ks-bind=\"{Values.HtmlEscape(b.Key)}\"", StringComparison.Ordinal))
                .ToDictionary(b => b.Key, b => b.Value, StringComparer.Ordinal);
            var parents = StartTag.Matches(Html).Where(t => t.Index < block.Index && t.Groups[2].Value.Contains("data-ks-live=\"", StringComparison.Ordinal) && ElementEnd(t) > block.Index)
                .Select(t => (object)new Dictionary<string, object>
                {
                    ["id"] = System.Net.WebUtility.HtmlDecode(Attribute(t, "data-ks-live")),
                    ["state"] = System.Net.WebUtility.HtmlDecode(Attribute(t, "data-ks-state") ?? string.Empty),
                }).ToList();
            var payload = new Dictionary<string, object>
            {
                ["page"] = Path,
                ["block"] = System.Net.WebUtility.HtmlDecode(Attribute(block, "data-ks-live")),
                ["handler"] = double.Parse(number, System.Globalization.CultureInfo.InvariantCulture),
                ["state"] = System.Net.WebUtility.HtmlDecode(Attribute(block, "data-ks-state") ?? string.Empty),
                ["args"] = args == null ? null : System.Net.WebUtility.HtmlDecode(args),
                ["binds"] = blockBinds,
                ["event"] = ev,
            };
            // A component inside another, on a page a route served, sends its ancestors' state, as the browser does.
            if (parents.Count > 0 && Attribute(block, "data-ks-route") != null) payload["parents"] = parents;
            string json = Values.ToJson(payload);
            string source = Attribute(block, "id") is string id ? System.Net.WebUtility.HtmlDecode(id) : null;
            if (Attribute(block, "data-ks-route") is string proof)
            {
                bool redrawn = FireRouted(call, eventName, label, json, System.Net.WebUtility.HtmlDecode(proof), block.Index, blockEnd);
                foreach (string key in blockBinds.Keys) binds.Remove(key);
                if (redrawn) DeliverEvents(call, block.Index, source, hop);
                return;
            }
            PageResult result = PageServer.HandleLive(site, json, new Dictionary<string, object>());
            Status = result.Status;
            if (result.Status != 200) throw call.Error($"{call.Display} on '{label ?? eventName}' failed ({result.Status}): {result.Body}", "Http");
            foreach (string key in blockBinds.Keys) binds.Remove(key);
            Html = Html[..block.Index] + result.Body + Html[blockEnd..];
            DeliverEvents(call, block.Index, source, hop);
        }

        /// <summary>
        /// The events a handler emitted with @emit_event() (the redrawn block's data-ks-events): each goes to the block
        /// with its target: id, or to every block, and runs that block's first on-event listener for the name, as the
        /// browser does (#202).
        /// </summary>
        private void DeliverEvents(CallContext call, int blockStart, string source, int hop)
        {
            Match sender = StartTag.Match(Html, blockStart);
            if (!sender.Success || sender.Index != blockStart || Attribute(sender, "data-ks-events") is not string encoded) return;
            // The attribute is read once, as the browser removes it.
            Html = Html[..sender.Index] + Regex.Replace(sender.Value, @"\sdata-ks-events=""[^""]*""", string.Empty) + Html[(sender.Index + sender.Length)..];
            if (hop >= MaxEventHops || PageLive.ParseJson(System.Net.WebUtility.HtmlDecode(encoded)) is not List<object> effects) return;
            foreach (Dictionary<string, object> effect in effects.OfType<Dictionary<string, object>>())
            {
                string name = effect.GetValueOrDefault("name") is object n ? Values.ToStr(n) : null;
                string target = effect.GetValueOrDefault("target") is object t ? Values.ToStr(t) : null;
                // Each recipient is found again, by its id, since the one before may have changed the page.
                var recipients = StartTag.Matches(Html).Where(IsBlock).Select(m => System.Net.WebUtility.HtmlDecode(Attribute(m, "data-ks-live"))).ToList();
                foreach (string live in recipients)
                {
                    Match recipient = StartTag.Matches(Html).FirstOrDefault(m => IsBlock(m) && System.Net.WebUtility.HtmlDecode(Attribute(m, "data-ks-live")) == live);
                    if (recipient == null) continue;
                    if (target != null && (Attribute(recipient, "id") is not string id || System.Net.WebUtility.HtmlDecode(id) != target)) continue;
                    Match listener = OwnedBy(recipient).FirstOrDefault(m => Attribute(m, "data-ks-on-event") != null
                        && (Attribute(m, "event-name") is not string only || System.Net.WebUtility.HtmlDecode(only) == name));
                    if (listener == null) continue;
                    string number = Attribute(listener, "data-ks-on-event");
                    var ev = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "component",
                        ["name"] = name,
                        ["data"] = effect.GetValueOrDefault("data"),
                        ["source"] = source,
                        ["hop"] = (double)(hop + 1),
                    };
                    Run(call, "event", name, recipient, number, Attribute(listener, "data-ks-args-" + number) ?? Attribute(listener, "data-ks-args"), ev, hop + 1);
                }
            }
        }

        private static bool IsBlock(Match tag) => tag.Groups[2].Value.Contains("data-ks-live=\"", StringComparison.Ordinal);

        /// <summary>The start tags inside a live block that belong to it, not to a block inside it.</summary>
        private IEnumerable<Match> OwnedBy(Match block)
        {
            int end = ElementEnd(block);
            var inner = new List<(int Start, int End)>();
            foreach (Match tag in StartTag.Matches(Html).Where(t => t.Index > block.Index && t.Index < end))
            {
                if (inner.Any(r => tag.Index >= r.Start && tag.Index < r.End)) continue;
                if (IsBlock(tag))
                {
                    inner.Add((tag.Index, ElementEnd(tag)));
                    continue;
                }
                yield return tag;
            }
        }

        /// <summary>
        /// A live action on a page a route served: posted to the page's own address with its proof, as the browser does,
        /// so the route's middleware and controller run again before the handler.
        /// </summary>
        private bool FireRouted(CallContext call, string eventName, string label, string json, string proof, int blockStart, int blockEnd)
        {
            var extra = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["content-type"] = "application/json",
                ["x-kscript-live"] = proof,
            };
            LocalResponse response = Routed(call, "POST", Path, json, extra)
                ?? throw call.Error($"{call.Display}: {Path} has no route any more, so '{label ?? eventName}' has nothing to run.", "NotFound");
            Status = response.Status;
            if (response.Status is >= 200 and < 300 && response.Headers.GetValueOrDefault("X-KScript-Fragment") as string == "1")
            {
                // The fragment is the block's new HTML.
                Html = Html[..blockStart] + response.Body + Html[blockEnd..];
                return true;
            }
            if (response.Status is >= 200 and < 400)
            {
                // Anything else (a redirect, a whole page) makes the browser load the page again.
                FollowRedirects(call, response.Status is >= 300 ? response : Routed(call, "GET", Path, null) ?? response);
                return false;
            }
            throw call.Error($"{call.Display} on '{label ?? eventName}' failed ({response.Status}): {response.Body}", "Http");
        }

        /// <summary>The text of the &lt;label&gt; an element is inside, or null.</summary>
        private string EnclosingLabel(int index)
        {
            Match label = StartTag.Matches(Html).Where(t => t.Index < index && t.Groups[1].Value.Equals("label", StringComparison.OrdinalIgnoreCase) && ElementEnd(t) > index).LastOrDefault();
            return label == null ? null : Text(Html[label.Index..ElementEnd(label)]);
        }

        /// <summary>The classes of the first element whose own text is <paramref name="label"/>.</summary>
        private List<object> Classes(CallContext call, string label)
        {
            foreach (Match tag in StartTag.Matches(Html))
            {
                int after = tag.Index + tag.Length;
                int next = Html.IndexOf('<', after);
                string own = System.Net.WebUtility.HtmlDecode(Html[after..(next < 0 ? Html.Length : next)]).Trim();
                if (own != label) continue;
                string classes = System.Net.WebUtility.HtmlDecode(Attribute(tag, "class") ?? string.Empty);
                return classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(c => (object)c).ToList();
            }
            throw call.Error($"{call.Display}: nothing on the page has the text '{label}'.", "NotFound");
        }

        private static string Attribute(Match tag, string name) =>
            Regex.Match(tag.Groups[2].Value, $@"(?:^|\s){Regex.Escape(name)}=""([^""]*)""") is { Success: true } m ? m.Groups[1].Value : null;

        /// <summary>Where the element a start tag opens ends: after its matching end tag.</summary>
        private int ElementEnd(Match tag)
        {
            string name = tag.Groups[1].Value;
            int after = tag.Index + tag.Length;
            if (VoidTags.Contains(name) || tag.Value.EndsWith("/>", StringComparison.Ordinal)) return after;
            var tags = new Regex($@"<(/?){Regex.Escape(name)}\b((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.IgnoreCase);
            int depth = 1;
            for (Match m = tags.Match(Html, after); m.Success; m = m.NextMatch())
            {
                if (m.Groups[1].Value == "/") depth--;
                else if (!m.Value.EndsWith("/>", StringComparison.Ordinal)) depth++;
                if (depth == 0) return m.Index + m.Length;
            }
            return Html.Length;
        }

        /// <summary>The words a reader sees: no tags, scripts or styles, and runs of spaces made one.</summary>
        private static string Text(string html)
        {
            string text = Regex.Replace(html ?? string.Empty, @"<(script|style)\b.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]*>", " ");
            return Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        }
    }
}
