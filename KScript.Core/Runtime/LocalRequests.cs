namespace KScript.Runtime
{
    /// <summary>The answer to a request a script made to its own app: <c>@api('/api/notes')</c>.</summary>
    public sealed class LocalResponse
    {
        public int Status { get; init; }
        public string Reason { get; init; }
        public string Body { get; init; } = string.Empty;
        public byte[] BodyBytes { get; init; }
        public Dictionary<string, object> Headers { get; init; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Requests for a path on the script's own app, such as <c>@api('/api/notes')</c> in a page, test or route. They
    /// are answered in the same process, by the routes of the project the script is in, without going through the
    /// network. KScript.Http sets <see cref="Handler"/> when it loads; KScript.Net calls <see cref="Send"/>.
    /// </summary>
    public static class LocalRequests
    {
        /// <summary>The most requests that may be inside one another (a route that calls itself), so a loop stops.</summary>
        public const int MaxDepth = 8;

        private static readonly AsyncLocal<int> depth = new AsyncLocal<int>();

        /// <summary>
        /// Answers a request (a <c>$request</c> map: method, path, query, headers, body) for the project around
        /// <c>folder</c>, made by a script running on <c>host</c>. Returns null when the folder isn't in a project.
        /// </summary>
        public static Func<KScriptHost, string, Dictionary<string, object>, LocalResponse> Handler { get; set; }

        /// <summary>
        /// A request key: when it is set and no route of the app matches, <see cref="Handler"/> answers null instead of a
        /// 404, so the caller (@visit) can look for a file page there instead.
        /// </summary>
        public const string RoutesOnlyKey = "KScript.LocalRequests.RoutesOnly";

        /// <summary>Sends a request to the app the script in <paramref name="folder"/> belongs to.</summary>
        public static LocalResponse Send(KScriptHost host, string folder, Dictionary<string, object> request)
        {
            if (Handler == null) PackageRegistry.Default.Get("KScript.Http");
            if (Handler == null)
                throw new KScriptException("A path such as '/api/notes' is a request to this app, which needs KScript.Http, and it isn't installed. Give a full http:// address instead.", "Network");
            return Nested(host, folder, request);
        }

        /// <summary>
        /// Like <see cref="Send"/>, but null when KScript.Http isn't installed or no route of the project answers the
        /// path: @visit('/') then renders a file page.
        /// </summary>
        public static LocalResponse TrySendToRoutes(KScriptHost host, string folder, Dictionary<string, object> request)
        {
            if (Handler == null) PackageRegistry.Default.Get("KScript.Http");
            if (Handler == null) return null;
            request[RoutesOnlyKey] = true;
            return Nested(host, folder, request);
        }

        private static LocalResponse Nested(KScriptHost host, string folder, Dictionary<string, object> request)
        {
            if (depth.Value >= MaxDepth)
                throw new KScriptException($"Requests to this app are nested more than {MaxDepth} deep. Does a route call itself with @api()?", "Network");
            depth.Value++;
            try
            {
                return Handler(host, folder, request);
            }
            finally
            {
                depth.Value--;
            }
        }
    }
}
