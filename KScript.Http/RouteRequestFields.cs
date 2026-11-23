using KScript.Metadata;

namespace KScript.Http
{
    /// <summary>Fields added to the page request map when handling an HTTP route.</summary>
    public static class RouteRequestFields
    {
        public static readonly ValueFieldInfo Ip = Field("ip", KType.String, "The client IP address.");
        public static readonly ValueFieldInfo Json = Field("json", KType.Any, "The parsed JSON body, or null.");
        public static readonly ValueFieldInfo Input = Field("input", KType.Map, "Query, form and JSON fields together.");
        public static readonly ValueFieldInfo Route = Field("route", KType.String, "The matched route's name, or null for an unnamed route.");

        public static IReadOnlyList<ValueFieldInfo> Matched { get; } = new[] { Ip, Json, Input, Route };

        private static ValueFieldInfo Field(string name, string type, string description) => new ValueFieldInfo(KType.Request, name, type, description);
    }
}
