using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Net
{
    internal sealed class IsolinkValue : IKObject
    {
        private readonly IsolinkSession session;

        private IsolinkValue(IsolinkSession session) => this.session = session;

        public string TypeName => KType.IsolinkSession;
        public string ToText() => "Isolink Connect session";

        public bool TryGetMember(string name, out object value)
        {
            value = session.Hello.GetValueOrDefault(name);
            return name is "token" or "me";
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        internal static void Register(KScriptPackage.PackageBuilder p, Func<string, string, string, CancellationToken, IsolinkSession> connect)
        {
            p.Command("with_isolink", "app_id: string, name: string, action: fn, token?: string",
                "Connects to the local desktop Isolink app, runs action with the consented session, and returns the action's result. Pass a saved token to reconnect.",
                KType.Any, c =>
                {
                    ICallable action = c.Fn(2);
                    using var session = connect(c.Str(0), c.Str(1), c.Arg(3) as string, c.Interpreter.Options.Cancellation);
                    return c.Call(action, new IsolinkValue(session));
                });
            p.Field(KType.IsolinkSession, "token", KType.String, "The reconnect token issued by Isolink, or the supplied token if the host kept it. Null when neither is present.");
            p.Field(KType.IsolinkSession, "me", KType.Map, "The current person as identified by Isolink for this app.");
            p.Method(KType.IsolinkSession, "request", "method: string, params?: map",
                "Runs an Isolink status, space or record operation on the active desktop session.", KType.Any,
                (target, c) => ((IsolinkValue)target).session.Request(c.Str(0), c.Arg(1) == null ? null : c.Map(1), c.Interpreter.Options.Cancellation));
        }
    }
}
