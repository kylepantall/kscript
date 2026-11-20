using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Http
{
    /// <summary>
    /// Makes an OpenAPI 3.1 document from the route table, with nothing to write by hand. Everything comes from the
    /// code: paths and methods, <c>{id}</c> parameters with their <c>where</c> rule or type, typed request bodies
    /// (<c>json: NewPerson</c>) and query strings, <c>/// @returns Person[]</c> for the answer, <c>///</c> summaries,
    /// <c>@deprecated</c>, the <c>auth</c> middleware as a bearer security scheme, and the error answers a route can
    /// give (422 for data that doesn't fit, 401, 429, and each <c>@abort(404)</c> the controller has).
    /// </summary>
    public static class OpenApi
    {
        private const string Refs = "#/components/schemas/";

        private static readonly string[] AnyMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

        private static readonly Regex AbortStatus = new Regex(@"@abort\(\s*(\d{3})", RegexOptions.CultureInvariant);
        private static readonly Regex ResponseStatus = new Regex(@"status\s*[:=]\s*(\d{3})", RegexOptions.CultureInvariant);

        /// <summary>The document for an app's routes, as nested maps and lists ready for JSON or YAML.</summary>
        public static Dictionary<string, object> Document(HttpApp app, RouteTable table, ProjectConfig project = null)
        {
            OpenApiSettings settings = app.Settings.OpenApi;
            var schemas = new Dictionary<string, object>(StringComparer.Ordinal);
            var paths = new SortedDictionary<string, object>(StringComparer.Ordinal);
            bool usesAuth = false;
            var operationIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (Route route in Included(table, settings.Routes))
            {
                if (route.RedirectTo != null) continue;
                string path = OpenApiPath(route.Pattern);
                if (!paths.TryGetValue(path, out object item)) paths[path] = item = new Dictionary<string, object>(StringComparer.Ordinal);
                var pathItem = (Dictionary<string, object>)item;
                foreach (string method in route.Methods.Count == 0 ? AnyMethods : route.Methods)
                {
                    string key = method.ToLowerInvariant();
                    if (pathItem.ContainsKey(key)) continue; // The first route for a method answers, as when serving.
                    Dictionary<string, object> operation = Operation(route, method, schemas, operationIds, out bool auth);
                    usesAuth |= auth;
                    pathItem[key] = operation;
                }
            }

            string title = settings.Title ?? project?.Name ?? Path.GetFileName(app.SiteRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var info = new Dictionary<string, object>
            {
                ["title"] = string.IsNullOrWhiteSpace(title) ? "API" : title,
                ["version"] = settings.Version ?? project?.App.Version ?? "1.0.0",
            };
            if (settings.Description != null) info["description"] = settings.Description;

            var document = new Dictionary<string, object>
            {
                ["openapi"] = "3.1.0",
                ["info"] = info,
                ["paths"] = paths.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            };
            schemas.TryAdd("Error", ErrorSchema());
            var components = new Dictionary<string, object>
            {
                ["schemas"] = new SortedDictionary<string, object>(schemas, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            };
            if (usesAuth)
            {
                components["securitySchemes"] = new Dictionary<string, object>
                {
                    ["bearerAuth"] = new Dictionary<string, object>
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer",
                        ["description"] = $"The token in the {app.Settings.AuthTokenEnv} environment variable (or the one auth:NAME names).",
                    },
                };
            }
            document["components"] = components;
            return document;
        }

        /// <summary>The routes the document covers: API routes, all routes, or (auto) the API routes when there are any.</summary>
        private static IEnumerable<Route> Included(RouteTable table, string which) => which switch
        {
            "all" => table.Routes,
            "api" => table.Routes.Where(r => r.Api),
            _ => table.Routes.Any(r => r.Api) ? table.Routes.Where(r => r.Api) : table.Routes,
        };

        /// <summary>/people/{id?} and /files/{path*} as OpenAPI writes them: /people/{id}, /files/{path}.</summary>
        public static string OpenApiPath(RoutePattern pattern) => pattern.Path.Replace("?}", "}").Replace("*}", "}");

        private static Dictionary<string, object> Operation(Route route, string method, Dictionary<string, object> schemas, HashSet<string> operationIds, out bool auth)
        {
            var operation = new Dictionary<string, object>();
            UserFunction fn = route.Controller as UserFunction;
            DocComment doc = Doc(route.Element) ?? Doc(fn?.Element);

            string tag = Tag(route);
            if (tag != null) operation["tags"] = new List<object> { tag };
            if (doc != null && doc.Summary.Length > 0)
            {
                string[] lines = doc.Summary.Split('\n', 2);
                operation["summary"] = lines[0].Trim();
                if (lines.Length > 1 && lines[1].Trim().Length > 0) operation["description"] = lines[1].Trim();
            }
            else if (route.ControllerName != null)
            {
                operation["summary"] = route.ControllerName;
            }

            string id = route.Name ?? route.ControllerName ?? (method.ToLowerInvariant() + OpenApiPath(route.Pattern).Replace('/', '_').Replace("{", string.Empty).Replace("}", string.Empty));
            if (!operationIds.Add(id)) operationIds.Add(id = id + "." + method.ToLowerInvariant());
            operation["operationId"] = id;
            if (doc?.Deprecated != null)
            {
                operation["deprecated"] = true;
                if (doc.Deprecated.Length > 0) operation["description"] = ((operation.TryGetValue("description", out object d) ? d + "\n\n" : string.Empty) + "Deprecated: " + doc.Deprecated).Trim();
            }

            var parameters = new List<object>();
            var responses = new SortedDictionary<string, object>(StringComparer.Ordinal);
            bool typedInput = false;

            // Path parameters: their type comes from the controller's parameter, else the where rule.
            foreach (string name in route.Pattern.Parameters)
            {
                Parameter p = fn?.Parameters.FirstOrDefault(x => x.Name == name);
                Dictionary<string, object> schema = p?.Type != null ? Shapes.SchemaFor(p.Type, fn.Closure, fn.Module, schemas, Refs) : null;
                schema ??= RuleSchema(route.Pattern.Rules.TryGetValue(name, out string rule) ? rule : null);
                var parameter = new Dictionary<string, object> { ["name"] = name, ["in"] = "path", ["required"] = true, ["schema"] = schema };
                string text = doc?.ParamText(name);
                if (text != null) parameter["description"] = text;
                else if (route.Pattern.IsOptional(name)) parameter["description"] = "Optional: the route also matches without it.";
                else if (route.Pattern.IsCatchAll(name)) parameter["description"] = "The rest of the path, which may hold / characters.";
                parameters.Add(parameter);
                if (p?.Type != null || route.Pattern.Rules.ContainsKey(name)) responses["404"] = Response($"No such resource: the {{{name}}} in the path doesn't fit.", error: true);
            }

            if (fn != null)
            {
                foreach (Parameter p in fn.Parameters.Where(p => p.Type != null))
                {
                    switch (p.Name)
                    {
                        case "query":
                            typedInput |= AddQueryParameters(p, fn, parameters, schemas);
                            break;
                        case "json":
                        case "body":
                        case "input":
                        case "form":
                            if (method is "GET" or "HEAD" or "DELETE" && p.Name == "input") break;
                            Dictionary<string, object> body = Shapes.SchemaFor(p.Type, fn.Closure, fn.Module, schemas, Refs);
                            if (body == null) break;
                            string media = p.Name == "form" ? "application/x-www-form-urlencoded" : "application/json";
                            var content = new Dictionary<string, object> { [media] = new Dictionary<string, object> { ["schema"] = body } };
                            if (p.Name == "input") content["application/x-www-form-urlencoded"] = new Dictionary<string, object> { ["schema"] = body };
                            var requestBody = new Dictionary<string, object> { ["required"] = p.Default == null && !p.Type.EndsWith("?", StringComparison.Ordinal), ["content"] = content };
                            string text = doc?.ParamText(p.Name);
                            if (text != null) requestBody["description"] = text;
                            operation["requestBody"] = requestBody;
                            typedInput = true;
                            if (p.Name is "json" or "body") responses["400"] = Response("The body isn't valid JSON.", error: true);
                            break;
                    }
                }
            }
            if (parameters.Count > 0) operation["parameters"] = parameters;
            if (typedInput) responses["422"] = ValidationResponse();

            // Middleware: auth needs a bearer token, throttle can answer 429.
            auth = route.Middleware.Any(m => m.Function == null && m.Name == "auth");
            if (auth)
            {
                operation["security"] = new List<object> { new Dictionary<string, object> { ["bearerAuth"] = new List<object>() } };
                responses["401"] = Response("The bearer token is missing or wrong.", error: true);
            }
            if (route.Middleware.Any(m => m.Function == null && m.Name == "throttle")) responses["429"] = Response("Too many requests: wait for Retry-After seconds.", error: true);
            if (method == "GET" && (route.Middleware.Any(m => m.Function == null && m.Name == "etag") || UsesEtag(route, fn)))
                responses["304"] = Response("Not modified: the client's copy (If-None-Match) is current.", error: false);
            if (route.Middleware.Count > 0) operation["x-middleware"] = route.Middleware.Select(m => (object)m.ToString()).ToList();

            // Statuses the controller can answer with: @abort(404), @response(..., status: 201).
            int success = 200;
            foreach (int status in StatusesIn(route, fn))
            {
                if (status >= 200 && status < 300)
                {
                    success = success == 200 ? status : success;
                    continue;
                }
                responses.TryAdd(status.ToString(CultureInfo.InvariantCulture), Response(Reason(status), error: status >= 400));
            }

            responses[success.ToString(CultureInfo.InvariantCulture)] = SuccessResponse(route, fn, doc, schemas, success);
            operation["responses"] = responses.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return operation;
        }

        private static DocComment Doc(MarkupElement element)
        {
            string text = element?.Documentation;
            if (string.IsNullOrWhiteSpace(text)) return null;
            DocComment doc = DocComment.Parse(text);
            return doc.IsEmpty ? null : doc;
        }

        /// <summary>The controller's import (people for people.show), else the first part of the path.</summary>
        private static string Tag(Route route)
        {
            if (route.ControllerName != null && route.ControllerName.IndexOf('.') is int dot && dot > 0) return route.ControllerName.Substring(0, dot);
            string first = route.Pattern.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(p => !p.StartsWith("{", StringComparison.Ordinal) && !string.Equals("/" + p, "/api", StringComparison.OrdinalIgnoreCase));
            return first;
        }

        private static bool AddQueryParameters(Parameter p, UserFunction fn, List<object> parameters, Dictionary<string, object> schemas)
        {
            if (Shapes.ResolveNamed(p.Type, fn.Closure, fn.Module) is not RecordType record) return false;
            foreach (FieldDefinition field in record.Fields.Where(f => !f.Computed))
            {
                var parameter = new Dictionary<string, object>
                {
                    ["name"] = field.From ?? field.Name,
                    ["in"] = "query",
                    ["required"] = !field.Optional,
                    ["schema"] = Shapes.SchemaFor(field.Type.ToString(), record.Closure, record.Module, schemas, Refs) ?? new Dictionary<string, object>(),
                };
                if (field.Description != null) parameter["description"] = field.Description;
                parameters.Add(parameter);
            }
            return true;
        }

        /// <summary>A path parameter's schema from its where rule: int is an integer, uuid a uuid, a regex a pattern.</summary>
        private static Dictionary<string, object> RuleSchema(string rule)
        {
            switch (rule?.ToLowerInvariant())
            {
                case null: return new Dictionary<string, object> { ["type"] = "string" };
                case "int": return new Dictionary<string, object> { ["type"] = "integer" };
                case "number": return new Dictionary<string, object> { ["type"] = "number" };
                case "uuid": return new Dictionary<string, object> { ["type"] = "string", ["format"] = "uuid" };
            }
            string pattern = RoutePattern.NamedConstraints.TryGetValue(rule, out string named) ? named : rule;
            return new Dictionary<string, object> { ["type"] = "string", ["pattern"] = "^(?:" + pattern + ")$" };
        }

        private static IEnumerable<int> StatusesIn(Route route, UserFunction fn)
        {
            var found = new SortedSet<int>();
            foreach (string text in SourceOf(route, fn))
            {
                foreach (Match m in AbortStatus.Matches(text)) found.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                foreach (Match m in ResponseStatus.Matches(text)) found.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            return found.Where(s => s >= 100 && s <= 599);
        }

        private static Dictionary<string, object> SuccessResponse(Route route, UserFunction fn, DocComment doc, Dictionary<string, object> schemas, int status)
        {
            string returns = doc?.Returns?.Trim();
            if (route.View != null) return Html("The " + route.View + " view.");
            if (status == 204) return new Dictionary<string, object> { ["description"] = Reason(204) };

            // /// @returns Person[] the people: a type first, then words.
            Dictionary<string, object> schema = null;
            string description = null;
            if (!string.IsNullOrEmpty(returns))
            {
                string first = returns.Split((char[])null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
                Scope scope = fn?.Closure ?? route.Scope;
                Module module = fn?.Module ?? route.Module;
                schema = scope != null ? Shapes.SchemaFor(first, scope, module, schemas, Refs) : null;
                description = schema != null ? returns.Substring(first.Length).Trim() : returns;
                if (description.Length == 0) description = null;
                if (schema == null && Regex.IsMatch(first, "^(html|view)$", RegexOptions.IgnoreCase)) return Html(description ?? "An HTML page.");
            }
            bool html = !route.Api && schema == null && UsesView(route, fn);
            if (html) return Html(description ?? "An HTML page.");
            return new Dictionary<string, object>
            {
                ["description"] = description ?? Reason(status),
                ["content"] = new Dictionary<string, object> { ["application/json"] = new Dictionary<string, object> { ["schema"] = schema ?? new Dictionary<string, object>() } },
            };
        }

        private static bool UsesEtag(Route route, UserFunction fn) => SourceOf(route, fn).Any(t => t.Contains("@etag(", StringComparison.Ordinal));

        private static IEnumerable<string> SourceOf(Route route, UserFunction fn)
        {
            foreach ((MarkupElement element, Module module) in new[] { (route.Inline, route.Module), (fn?.Element, fn?.Module) })
            {
                if (element == null || module?.Source == null || element.Span.End > module.Source.Text.Length) continue;
                yield return module.Source.Text.Substring(element.Span.Start, element.Span.Length);
            }
        }

        private static bool UsesView(Route route, UserFunction fn) => SourceOf(route, fn).Any(t => t.Contains("@view(", StringComparison.Ordinal));

        private static Dictionary<string, object> Html(string description) => new Dictionary<string, object>
        {
            ["description"] = description,
            ["content"] = new Dictionary<string, object> { ["text/html"] = new Dictionary<string, object> { ["schema"] = new Dictionary<string, object> { ["type"] = "string" } } },
        };

        private static Dictionary<string, object> Response(string description, bool error)
        {
            var response = new Dictionary<string, object> { ["description"] = description };
            if (error)
            {
                response["content"] = new Dictionary<string, object> { ["application/json"] = new Dictionary<string, object> { ["schema"] = new Dictionary<string, object> { ["$ref"] = Refs + "Error" } } };
            }
            return response;
        }

        private static Dictionary<string, object> ValidationResponse()
        {
            Dictionary<string, object> response = Response("The data doesn't fit the type: problems lists each field that is wrong.", error: true);
            return response;
        }

        private static Dictionary<string, object> ErrorSchema() => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["description"] = "How API routes answer errors.",
            ["properties"] = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "What went wrong." },
                ["status"] = new Dictionary<string, object> { ["type"] = "integer" },
                ["problems"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["description"] = "For 422: each field that doesn't fit the type.",
                    ["items"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object> { ["type"] = "string" },
                            ["field"] = new Dictionary<string, object> { ["type"] = "string" },
                            ["message"] = new Dictionary<string, object> { ["type"] = "string" },
                        },
                    },
                },
            },
            ["required"] = new List<object> { "error", "status" },
        };

        internal static string Reason(int status) => status switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No content",
            301 => "Moved permanently",
            302 => "Found",
            304 => "Not modified",
            400 => "Bad request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not found",
            405 => "Method not allowed",
            409 => "Conflict",
            410 => "Gone",
            415 => "Unsupported media type",
            422 => "Unprocessable content",
            429 => "Too many requests",
            500 => "Server error",
            503 => "Service unavailable",
            _ => "Status " + status.ToString(CultureInfo.InvariantCulture),
        };

        #region Writing

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = OpenApiJsonContext.Default,
        };

        public static string ToJson(Dictionary<string, object> document) => JsonSerializer.Serialize(document, JsonOptions.GetTypeInfo(typeof(Dictionary<string, object>))) + "\n";

        /// <summary>The document as YAML (block style, strings quoted when they need it).</summary>
        public static string ToYaml(Dictionary<string, object> document)
        {
            var sb = new StringBuilder();
            WriteMap(sb, document, 0);
            return sb.ToString();
        }

        private static void WriteMap(StringBuilder sb, IDictionary<string, object> map, int indent)
        {
            foreach (var kv in map)
            {
                sb.Append(' ', indent).Append(Scalar(kv.Key)).Append(':');
                WriteValue(sb, kv.Value, indent);
            }
        }

        private static void WriteValue(StringBuilder sb, object value, int indent)
        {
            switch (value)
            {
                case IDictionary<string, object> m when m.Count == 0:
                    sb.Append(" {}\n");
                    break;
                case IDictionary<string, object> m:
                    sb.Append('\n');
                    WriteMap(sb, m, indent + 2);
                    break;
                case IList<object> l when l.Count == 0:
                    sb.Append(" []\n");
                    break;
                case IList<object> l:
                    sb.Append('\n');
                    foreach (object item in l)
                    {
                        sb.Append(' ', indent + 2).Append('-');
                        if (item is IDictionary<string, object> im && im.Count > 0)
                        {
                            // - name: id
                            //   in: path
                            bool first = true;
                            foreach (var kv in im)
                            {
                                sb.Append(first ? " " : new string(' ', indent + 4)).Append(Scalar(kv.Key)).Append(':');
                                WriteValue(sb, kv.Value, indent + 4);
                                first = false;
                            }
                        }
                        else
                        {
                            WriteValue(sb, item, indent + 2);
                        }
                    }
                    break;
                default:
                    sb.Append(' ').Append(Scalar(value)).Append('\n');
                    break;
            }
        }

        private static string Scalar(object value)
        {
            switch (value)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case int or long or double or float or decimal: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            string s = value.ToString();
            bool plain = s.Length > 0 && Regex.IsMatch(s, @"^[A-Za-z_/$][A-Za-z0-9_./{}$ -]*$") && !s.EndsWith(" ", StringComparison.Ordinal)
                && !Regex.IsMatch(s, "^(true|false|null|yes|no|on|off|~)$", RegexOptions.IgnoreCase) && !s.Contains(": ") && !s.Contains(" #");
            return plain ? s : JsonSerializer.Serialize(s, JsonOptions.GetTypeInfo(typeof(string)));
        }

        #endregion
    }
}
