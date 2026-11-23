using System.Collections.Generic;

using KScript.Metadata;

namespace KScript.Pages
{
    /// <summary>The fields used to build and describe the request map shared by pages and HTTP routes.</summary>
    public static class RequestFields
    {
        public static readonly ValueFieldInfo Method = Field("method", KType.String, "The HTTP method, such as GET or POST.");
        public static readonly ValueFieldInfo Scheme = Field("scheme", KType.String, "The request scheme, such as http or https.");
        public static readonly ValueFieldInfo Path = Field("path", KType.String, "The path, such as /people.");
        public static readonly ValueFieldInfo Url = Field("url", KType.String, "The path with its query string, such as /people?page=2.");
        public static readonly ValueFieldInfo Query = Field("query", KType.Map, "Query string values: $request.query.page.");
        public static readonly ValueFieldInfo Form = Field("form", KType.Map, "Posted form fields: $request.form.email.");
        public static readonly ValueFieldInfo Headers = Field("headers", KType.Map, "Request headers with lowercase names.");
        public static readonly ValueFieldInfo Cookies = Field("cookies", KType.Map, "Cookies sent by the client.");
        public static readonly ValueFieldInfo Body = Field("body", KType.String, "The raw request body.");
        public static readonly ValueFieldInfo Params = Field("params", KType.Map, "Parameters captured from the request path.");
        public static readonly ValueFieldInfo Locale = Field("locale", KType.String, "The locale when a page matches a locale-prefixed path.");

        public static IReadOnlyList<ValueFieldInfo> Common { get; } = new[] { Method, Scheme, Path, Url, Query, Form, Headers, Cookies, Body, Params };

        private static ValueFieldInfo Field(string name, string type, string description) => new ValueFieldInfo(KType.Request, name, type, description);
    }
}
