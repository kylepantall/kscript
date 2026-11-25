using System.Text;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Net
{
    public sealed partial class NetPackage
    {
        private const string HttpStubKey = "KScript.Net.HttpStub";

        private static void RegisterHttpHelpers(PackageBuilder p)
        {
            p.Command("parse_query", "text: string", "Parses URL query text into a map. Repeated names have a list of values in order.", KType.Map,
                c => ParseQuery(c, c.Str(0)));
            p.Command("http_client", "base_url: string, options?: map", "Returns a callable HTTP client. Call it with a relative path and optional request options.", KType.Fn, c =>
            {
                if (!Uri.TryCreate(c.Str(0), UriKind.Absolute, out Uri root) || root.Scheme is not ("http" or "https"))
                    throw c.Error("base_url must be an HTTP or HTTPS URL.", "Argument");
                // A path is added to the base URL, so https://api.example.com/v1 and 'users' make .../v1/users.
                Uri folder = root.AbsolutePath.EndsWith('/') ? root : new UriBuilder(root) { Path = root.AbsolutePath + "/", Query = string.Empty, Fragment = string.Empty }.Uri;
                var defaults = c.Arg(1) == null ? new Dictionary<string, object>() : new Dictionary<string, object>(c.Map(1));
                return new NativeFunction("http_client", call =>
                {
                    string path = call.Str(0).TrimStart('/');
                    Uri url = root;
                    if (path.Length > 0 && (!Uri.TryCreate(folder, path, out url) || url.Scheme != root.Scheme || url.Authority != root.Authority
                        || !url.AbsolutePath.StartsWith(folder.AbsolutePath, StringComparison.Ordinal)))
                        throw call.Error($"A client path must stay under the base URL {folder.AbsoluteUri}.", "Argument");
                    var options = new Dictionary<string, object>(defaults);
                    if (call.Arg(1, "options") is object supplied)
                    {
                        if (Values.Unwrap(supplied) is not Dictionary<string, object> overridesByName)
                            throw call.Error("options must be a map.", "Type");
                        foreach (var option in overridesByName)
                        {
                            if (option.Key == "headers" && option.Value is Dictionary<string, object> overrides
                                && defaults.GetValueOrDefault("headers") is Dictionary<string, object> headers)
                            {
                                var combined = new Dictionary<string, object>(headers, StringComparer.OrdinalIgnoreCase);
                                foreach (var header in overrides) combined[header.Key] = header.Value;
                                options["headers"] = combined;
                            }
                            else options[option.Key] = option.Value;
                        }
                    }
                    return Api(call, url.AbsoluteUri, options);
                });
            });
            p.Command(new CallableInfo(CallableKind.Command, "with_http_stub",
                "Runs work with outbound @api and HTTP client calls intercepted by handler(request).",
                new[] { new Signature(
                    new ParameterInfo("handler", type: KType.Fn, description: "A stub that handles one request map.") { FunctionSignature = Signature.Parse("request: map") },
                    new ParameterInfo("work", type: KType.Fn)) })
            {
                ReturnType = KType.Any,
            }, c =>
            {
                ICallable handler = c.Fn(0), work = c.Fn(1);
                bool hadPrevious = c.Host.Items.TryGetValue(HttpStubKey, out object previous);
                c.Host.Items[HttpStubKey] = handler;
                try { return c.Call(work); }
                finally
                {
                    if (hadPrevious) c.Host.Items[HttpStubKey] = previous;
                    else c.Host.Items.Remove(HttpStubKey);
                }
            });
        }

        private static ResponseValue StubResponse(CallContext call, ICallable stub, Uri url, string method, Dictionary<string, object> options)
        {
            object result = call.Call(stub, new Dictionary<string, object>
            {
                ["url"] = url.AbsoluteUri, ["method"] = method, ["options"] = new Dictionary<string, object>(options),
            });
            if (result is ResponseValue response) return response;
            if (result is not Dictionary<string, object> row)
                throw call.Error("The HTTP stub must return a response or a map with status, body and headers.", "Type");
            object statusValue = row.GetValueOrDefault("status");
            double status = statusValue == null ? 200 : Values.ToNumber(statusValue, "stub status");
            if (status is < 100 or > 599 || status != Math.Truncate(status)) throw call.Error("The stub status must be a valid HTTP status code.", "Argument");
            var headers = row.GetValueOrDefault("headers") as Dictionary<string, object> ?? new Dictionary<string, object>();
            object body = row.GetValueOrDefault("body");
            bool binary = Base64Type.TryDecodeBytes(body, out byte[] bytes);
            string text = binary ? Encoding.UTF8.GetString(bytes) : body is Dictionary<string, object> or List<object>
                ? Values.ToJson(body) : Values.ToStr(body);
            return new ResponseValue(url.AbsoluteUri, (int)status, Values.ToStr(row.GetValueOrDefault("reason")), text, headers, bytes);
        }

        private static Dictionary<string, object> ParseQuery(CallContext call, string query)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string field in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] halves = field.Split('=', 2);
                string key = Decode(halves[0]);
                string value = halves.Length == 2 ? Decode(halves[1]) : string.Empty;
                if (!fields.TryGetValue(key, out object current)) fields[key] = value;
                else if (current is List<object> list) list.Add(value);
                else fields[key] = new List<object> { current, value };
            }
            return fields;

            string Decode(string encoded)
            {
                var output = new StringBuilder();
                for (int at = 0; at < encoded.Length;)
                {
                    char ch = encoded[at++];
                    if (ch != '%')
                    {
                        output.Append(ch == '+' ? ' ' : ch);
                        continue;
                    }
                    var bytes = new List<byte>();
                    do
                    {
                        if (at + 1 >= encoded.Length || !Uri.IsHexDigit(encoded[at]) || !Uri.IsHexDigit(encoded[at + 1]))
                            throw call.Error("A query string contains an invalid percent escape.", "Format");
                        bytes.Add((byte)(Uri.FromHex(encoded[at]) * 16 + Uri.FromHex(encoded[at + 1])));
                        at += 2;
                        if (at >= encoded.Length || encoded[at] != '%') break;
                        at++;
                    } while (true);
                    try { output.Append(new UTF8Encoding(false, true).GetString(bytes.ToArray())); }
                    catch (DecoderFallbackException) { throw call.Error("A query string contains invalid UTF-8.", "Format"); }
                }
                return output.ToString();
            }
        }
    }
}
