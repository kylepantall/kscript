using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// Who owns what a request makes: the signed-in user (<c>user:12</c>), else the browser's guest
    /// (<c>guest:&lt;uuid&gt;</c>). It is what Ownable models keep in their owner column.
    /// </summary>
    internal static class Owners
    {
        /// <summary>
        /// The owner for <paramref name="who"/> (a user, a user's id, or an owner value), or with null the current
        /// owner: the signed-in user, else the guest. Null outside a request, when there is neither.
        /// </summary>
        public static string Of(CallContext call, object who)
        {
            who = Values.Unwrap(who);
            if (who != null) return Named(who);
            if (Policies.User(call.Host) is object user && Values.Unwrap(user) is object signedIn)
            {
                string owner = "user:" + UserStore.IdOf(signedIn, "@owner()");
                AuthRequest.Current(call.Host)?.AdoptGuestRows(call, owner);
                return owner;
            }
            string guest = Guest(call.Host, create: true);
            return guest != null ? "guest:" + guest : null;
        }

        /// <summary>The guest id: a test's @acting_as_guest(), else the request's guest cookie. Null outside a request.</summary>
        public static string Guest(KScriptHost host, bool create)
        {
            if (Policies.ActingGuest(host) is string acting) return acting;
            AuthRequest auth = AuthRequest.Current(host);
            // A signed-in user has no guest id of their own: @guest() is for browsers that haven't signed in.
            if (auth == null || (Policies.User(host) != null)) return null;
            return auth.Guest(create);
        }

        /// <summary>A guest id as a uuid value.</summary>
        public static UuidValue Uuid(string id) => UuidValue.TryParse(id, out UuidValue uuid, out _) ? uuid : null;

        /// <summary>An owner given by name: user:12 or guest:&lt;uuid&gt; as they are, else a user (or a user's id).</summary>
        private static string Named(object who)
        {
            if (who is string text && (text.StartsWith("user:", StringComparison.Ordinal) || text.StartsWith("guest:", StringComparison.Ordinal))) return text;
            return "user:" + UserStore.IdOf(who, "@owner(as:)");
        }
    }
}
