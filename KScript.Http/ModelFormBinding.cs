using KScript.Auth;
using KScript.Data;
using KScript.Metadata;
using KScript.Pages;
using KScript.Runtime;

namespace KScript.Http;

/// <summary>A named, session-only POST handler generated from a ModelUpdate declaration.</summary>
internal sealed record ModelFormBinding(string Model, string Owner, HashSet<string> Fields)
{
    internal static bool? Declare(ElementRun run)
    {
        RequestContext ctx = HttpPackage.Context(run);
        string name = run.VariableName("name");
        var binding = new ModelFormBinding(run.VariableName("model"), run.VariableName("owner"),
            NameList.Names(run.RequiredRaw("fields")).ToHashSet(StringComparer.Ordinal));
        if (binding.Fields.Count == 0 || binding.Fields.Overlaps(new[] { "_token", "_key", "_return" }))
            throw run.Error("List the model fields the form may edit. _token, _key and _return are reserved.", "fields", "Config");

        var middleware = ctx.MiddlewareFor(run).ToList();
        middleware.Add(new MiddlewareRef("auth", "session", null));
        middleware.Add(new MiddlewareRef("csrf", null, null));
        middleware.AddRange(HttpPackage.ResolveMiddleware(run, run.Raw("middleware")));
        ctx.Add(run, new Route
        {
            Name = "model-form." + name,
            Pattern = new RoutePattern(ModelFormPath.For(name)),
            Methods = new[] { "POST" },
            Middleware = middleware,
            Controller = new NativeFunction(name, call => binding.Submit(ctx, call)),
            ControllerName = $"{binding.Model} model form",
            Api = ctx.Api,
            Module = run.Module,
            File = run.Module.Path,
            Line = run.Module.Source.GetPosition(run.Element.Span.Start).Line + 1,
        });
        return null;
    }

    private HttpResponse Submit(RequestContext ctx, CallContext call)
    {
        var request = call.Map(0);
        bool asyncSave = BuiltInMiddleware.Header(request, "x-kscript-model-form") == "save";
        HttpResponse Error(int code, string message) => asyncSave ? HttpResponse.Text(message, code) : ctx.ErrorResponse(code, message);
        var form = (Dictionary<string, object>)request["form"];
        if (form.GetValueOrDefault("_key") is not string key || string.IsNullOrEmpty(key))
            return Error(422, "The form needs a record key.");
        var input = new Dictionary<string, object>(form, StringComparer.Ordinal);
        input.Remove("_token");
        input.Remove("_key");
        input.Remove("_return");

        var user = ctx.Auth.SessionUser;
        if (user?.GetValueOrDefault("id") is not object id) return Error(401, "Sign in to save this form.");
        var users = ctx.Auth.App.Users as ModelRecordStore;
        if (users?.Store.Name == Model && Fields.Overlaps(new[] { "email", "password", "password_hash" }))
            throw new KScriptException("Use KScript.Auth to change sign-in details.", "Config");
        ModelStore store = users?.Store.Name == Model ? users.Store : ModelStore.Load(call.Host, ctx.Auth.App.Root, Model, "ModelUpdate model");
        try
        {
            if (!store.UpdateOwned(key, Owner, Values.ToStr(id), Fields, input))
                return Error(404, "No editable record matches this form.");
        }
        catch (KScriptException ex) when (ex.Type is "Shape" or "Type" or "Value" or "Validation")
        {
            return Error(422, ex.Message);
        }

        if (asyncSave)
            return new HttpResponse(204, string.Empty, null);
        string back = form.GetValueOrDefault("_return") as string;
        return HttpPackage.Redirect(AuthRequest.Safe(back) ? back : "/", 303);
    }
}
