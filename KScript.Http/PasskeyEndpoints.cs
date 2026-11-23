using System.Net.Http.Headers;
using System.Text;

using KScript.Auth;
using KScript.Pages;
using KScript.Runtime;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace KScript.Http
{
    internal static class PasskeyEndpoints
    {
        internal const string Prefix = "/_ks/passkeys/";
        private const int MaxBodyBytes = Passkeys.MaxBodyBytes;

        internal static async Task<PageResult> HandleAsync(HttpApp app, HttpRequest request)
        {
            var error = Check(app, request.Path.Value, request.Method, request.ContentType, request.Headers.Origin.ToString());
            if (error != null) return error;
            if (request.ContentLength > MaxBodyBytes) return TooLarge();

            // Enforce the same limit when the client streams the body without a Content-Length.
            var limit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit != null && !limit.IsReadOnly && (limit.MaxRequestBodySize == null || limit.MaxRequestBodySize > MaxBodyBytes))
                limit.MaxRequestBodySize = MaxBodyBytes;
            try
            {
                var map = await PageServer.BuildRequestAsync(request);
                map["ip"] = request.HttpContext.Connection.RemoteIpAddress?.ToString();
                return Finish(app, map);
            }
            catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) when (ex.StatusCode == 413)
            {
                return TooLarge();
            }
        }

        internal static PageResult Handle(HttpApp app, Dictionary<string, object> request)
        {
            var headers = (Dictionary<string, object>)request["headers"];
            return Check(app, Values.ToStr(request["path"]), Values.ToStr(request["method"]),
                Values.ToStr(headers.GetValueOrDefault("content-type")), Values.ToStr(headers.GetValueOrDefault("origin")))
                ?? Finish(app, request);
        }

        private static PageResult Check(HttpApp app, string path, string method, string contentType, string origin)
        {
            var parts = path[Prefix.Length..].Split('/');
            if (app.Settings.Auth.PasskeyOrigin == null || parts.Length != 2 ||
                parts[0] is not ("login" or "register" or "signup") || (parts[0] == "signup" && !app.Settings.Auth.PasskeySignup) || parts[1] is not ("options" or "verify"))
                return Error(404, "Passkey endpoint not found.");
            if (method != "POST")
            {
                var error = Error(405, "Use POST for passkeys.");
                error.Headers["Allow"] = "POST";
                return error;
            }
            if (!string.Equals(origin, app.Settings.Auth.PasskeyOrigin, StringComparison.Ordinal))
                return Error(403, "The request must come from the site's passkey origin.");
            if (!MediaTypeHeaderValue.TryParse(contentType, out var type) ||
                !string.Equals(type.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                return Error(415, "Send an application/json body.");
            return null;
        }

        private static PageResult Finish(HttpApp app, Dictionary<string, object> request)
        {
            string body = Values.ToStr(request["body"]);
            if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes) return TooLarge();
            string[] parts = Values.ToStr(request["path"])[Prefix.Length..].Split('/');
            PageResult result;
            try
            {
                result = Json(200, Passkeys.Handle(AuthRequest.For(request, app.Auth), parts[0], parts[1], body));
            }
            catch (KScriptException ex) when (ErrorTypes.StatusOf(ex) is int status)
            {
                result = Error(status, ex.Message);
                if (ex.Fields.TryGetValue("retry_after", out var delay)) result.Headers["Retry-After"] = Values.ToStr(delay);
            }
            result.Cookies.AddRange(RequestHooks.Finish(request));
            return result;
        }

        private static PageResult TooLarge() => Error(413, "Passkey response is too large.");

        private static PageResult Error(int status, string message) =>
            Json(status, Values.ToJson(new Dictionary<string, object> { ["error"] = message }));

        private static PageResult Json(int status, string body)
        {
            var result = new PageResult { Status = status, ContentType = "application/json; charset=utf-8", Body = body };
            result.Headers["Cache-Control"] = "no-store";
            return result;
        }
    }
}
