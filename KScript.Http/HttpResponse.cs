using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Http
{
    /// <summary>
    /// What a controller or middleware answers with: a status, headers, a content type and a body. Made by
    /// <c>@response()</c>, <c>@view()</c> and <c>@redirect()</c>, and by <c>$next($request)</c> in middleware, which
    /// can change it with <c>->with_header()</c> and <c>->with_status()</c> before passing it on.
    /// </summary>
    public sealed class HttpResponse : IKObject
    {
        public HttpResponse(int status = 200, string body = "", string contentType = "text/html; charset=utf-8")
        {
            Status = status;
            Body = body ?? string.Empty;
            ContentType = contentType;
        }

        public int Status { get; set; }
        public string Body { get; set; }
        public string ContentType { get; set; }
        public Dictionary<string, object> Headers { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public string TypeName => KType.HttpResponse;

        public string ToText() => Body;

        public override string ToString() => $"{Status} {ContentType}";

        /// <summary>A response holding JSON.</summary>
        public static HttpResponse Json(object value, int status = 200) =>
            new HttpResponse(status, Values.ToJson(value), "application/json; charset=utf-8");

        public static HttpResponse Text(string text, int status = 200) =>
            new HttpResponse(status, text, "text/plain; charset=utf-8");

        /// <summary>
        /// Turns what a controller returned into a response: a response stays as it is, nothing is 204 No Content,
        /// text is HTML, and maps, lists, numbers and yes/no are JSON.
        /// </summary>
        public static HttpResponse From(object value) => value switch
        {
            HttpResponse r => r,
            null => new HttpResponse(204, string.Empty, null),
            string s => new HttpResponse(200, s),
            IKObject o => new HttpResponse(200, o.ToText()),
            ICallable => throw new KScriptException("A controller returned a function. Return a value, a map (sent as JSON) or @view(...).", "Http"),
            _ => Json(value),
        };

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "status" => (double)Status,
                "body" => Body,
                "type" => ContentType,
                "headers" => Headers,
                "ok" => Status >= 200 && Status < 300,
                _ => null,
            };
            return value != null;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "status": result = (double)Status; return true;
                case "body": case "text": result = Body; return true;
                case "type": result = ContentType; return true;
                case "headers": result = Headers; return true;
                case "ok": result = Status >= 200 && Status < 300; return true;
                case "header":
                    result = Headers.TryGetValue(call.Str(0), out object h) ? h : null;
                    return true;
                case "json":
                    result = Values.ParseJson(Body);
                    return true;
                case "with_status":
                    Status = call.Int(0);
                    result = this;
                    return true;
                case "with_header":
                    Headers[call.Str(0)] = Values.ToStr(call.Arg(1));
                    result = this;
                    return true;
                case "with_type":
                    ContentType = call.Str(0);
                    result = this;
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("status", "", "The HTTP status code, such as 200 or 404.", KType.Number),
            ("body", "", "The body as text.", KType.String),
            ("type", "", "The Content-Type.", KType.String),
            ("headers", "", "The headers as a map.", KType.Map),
            ("header", "name", "One header (or null).", KType.String),
            ("ok", "", "True for a 2xx status.", KType.Bool),
            ("json", "", "The body read as JSON.", KType.Any),
            ("with_status", "code", "Changes the status code and returns the response.", KType.HttpResponse),
            ("with_header", "name, value", "Sets a header and returns the response: $next($request)->with_header('X-Frame-Options', 'DENY').", KType.HttpResponse),
            ("with_type", "type", "Changes the Content-Type and returns the response.", KType.HttpResponse),
        };
    }

    /// <summary>
    /// Thrown by <c>@abort(404)</c>: ends the request with that status. It isn't a KScript error, so
    /// <c>&lt;try&gt;</c> doesn't catch it.
    /// </summary>
    public sealed class HttpAbortSignal : ControlSignal
    {
        public HttpAbortSignal(HttpResponse response) : base("abort") => Response = response;

        public HttpResponse Response { get; }
    }
}
