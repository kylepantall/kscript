using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Testing
{
    internal static class TestHelpers
    {
        internal static void Register(KScriptPackage.PackageBuilder p, string assertion)
        {
            p.Command("with_clock", "time: date, work: fn", "Runs work with @now() and @today() fixed to the given date.", KType.Any, call =>
            {
                if (Values.Unwrap(call.Need(0)) is not DateTime time) throw call.Error("time must be a date.", "Type");
                ICallable work = call.Fn(1);
                DateTime? previous = call.Host.ClockOverride;
                call.Host.ClockOverride = time;
                try { return call.Call(work); }
                finally { call.Host.ClockOverride = previous; }
            });
            p.Command("with_environment", "values: map, work: fn", "Runs work with overrides for @env() and $env. A null value hides a variable in this scope.", KType.Any, call =>
            {
                var overrides = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                var previous = call.Host.EnvironmentOverrides;
                if (previous != null)
                    foreach (var entry in previous) overrides[entry.Key] = entry.Value;
                foreach (var entry in call.Map(0)) overrides[entry.Key] = entry.Value == null ? null : Values.ToStr(entry.Value);
                ICallable work = call.Fn(1);
                call.Host.EnvironmentOverrides = overrides;
                try { return call.Call(work); }
                finally { call.Host.EnvironmentOverrides = previous; }
            });
            p.Command("assert_response", "response, status?: number, headers?: map, redirect?: string, json?, as?: type",
                "Checks an HTTP response's status, headers, redirect or JSON. An optional type validates the body.", KType.Null,
                call =>
                {
                    AssertResponse(call, assertion);
                    return null;
                });
        }

        private static void AssertResponse(CallContext call, string assertion)
        {
            if (Values.Unwrap(call.Need(0)) is not IKObject response || response.TypeName is not (KType.Response or KType.HttpResponse))
                throw call.Error("assert_response requires an HTTP response.", "Type");
            response.TryGetMember("status", out object status);
            response.TryGetMember("headers", out object value);
            var headers = value as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (call.Has(1) && !Values.StrictEquals(status, call.Arg(1))) Fail("status", call.Arg(1), status);
            if (call.Arg(2) is object)
                foreach (var header in call.Map(2))
                    if (!Values.StrictEquals(header.Value, Header(header.Key))) Fail($"header '{header.Key}'", header.Value, Header(header.Key));
            if (call.Has(3))
            {
                if (status is not double number || number < 300 || number >= 400)
                    throw call.Error($"Expected a redirect, but the response status was {Values.ToStr(status)}.", assertion);
                if (!Values.StrictEquals(call.Arg(3), Header("Location"))) Fail("redirect location", call.Arg(3), Header("Location"));
            }
            if (call.Has(4) || call.Has(5))
            {
                response.TryGetMember(response.TypeName == KType.Response ? "text" : "body", out object body);
                object json;
                try
                {
                    json = Values.ParseJson(Values.ToStr(body));
                    if (call.Arg(5) is object type) _ = Shapes.MapAs(call.Interpreter, json, type, "The response JSON", call.Scope, call.Module);
                }
                catch (KScriptException ex)
                {
                    throw call.Error(ex.Message, assertion);
                }
                if (call.Has(4) && !Values.StrictEquals(call.Arg(4), json))
                {
                    var (path, expected, actual) = Difference("$", call.Arg(4), json);
                    Fail($"JSON at {path}", expected, actual);
                }
            }
            return;

            object Header(string name) => headers.FirstOrDefault(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

            void Fail(string subject, object expected, object actual) =>
                throw call.Error($"Expected {subject} to be {Values.ToJson(expected)}, got {Values.ToJson(actual)}.", assertion);
        }

        private static (string Path, object Expected, object Actual) Difference(string path, object expected, object actual)
        {
            if (expected is Dictionary<string, object> left && actual is Dictionary<string, object> right)
            {
                foreach (string key in left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal))
                {
                    bool hasExpected = left.TryGetValue(key, out object a);
                    bool hasActual = right.TryGetValue(key, out object b);
                    string field = path + "[" + Values.ToJson(key) + "]";
                    if (!hasExpected || !hasActual) return (field, hasExpected ? a : "<absent>", hasActual ? b : "<absent>");
                    if (!Values.StrictEquals(a, b)) return Difference(field, a, b);
                }
            }
            if (expected is List<object> first && actual is List<object> second)
            {
                for (int i = 0; i < Math.Min(first.Count, second.Count); i++)
                    if (!Values.StrictEquals(first[i], second[i])) return Difference($"{path}[{i}]", first[i], second[i]);
                return (path + ".length", (double)first.Count, (double)second.Count);
            }
            return (path, expected, actual);
        }
    }
}
