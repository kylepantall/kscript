using KScript.Data;
using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// Who owns what a request makes: the signed-in user, else the browser's guest. Ownable models keep it in their
    /// user and guest relations. A guest's id is the key of the app's Guest model (KScript.Auth's, or the project's copy).
    /// </summary>
    internal static class Owners
    {
        /// <summary>The model guests are kept in.</summary>
        public const string GuestModel = "Guest";

        /// <summary>
        /// The owner of a record of <paramref name="model"/> that this request makes: the signed-in user, else the
        /// guest. Null outside a request, when there is neither.
        /// </summary>
        public static Owner Of(CallContext call, ModelInfo model)
        {
            if (Policies.User(call.Host) is object user && Values.Unwrap(user) is object signedIn)
            {
                CheckUsers(call, model);
                string id = UserStore.IdOf(signedIn, "Ownable");
                AuthRequest.Current(call.Host)?.AdoptGuestRows(call, id);
                return new Owner(ModelInfo.OwnerUser, id);
            }
            object guest = Guest(call, create: true);
            return guest != null ? new Owner(ModelInfo.OwnerGuest, guest) : null;
        }

        /// <summary>Before an owned model's scope runs: moves the guest's rows to the account they signed in to, if they just did.</summary>
        public static void Adopt(CallContext call)
        {
            if (Policies.User(call.Host) is object user && Values.Unwrap(user) is object signedIn && AuthRequest.Current(call.Host) is AuthRequest auth)
                auth.AdoptGuestRows(call, UserStore.IdOf(signedIn, "Ownable"));
        }

        /// <summary>
        /// An owned model's user is a record of the model the users are kept in (http.auth.users), so the users must be
        /// in a model, and the same one.
        /// </summary>
        private static void CheckUsers(CallContext call, ModelInfo model)
        {
            string target = model.OwnerRelation(ModelInfo.OwnerUser).Target.Name;
            AuthApp app = AuthPackage.App(call);
            if (app?.Users is not ModelRecordStore store)
                throw new KScriptException($"{model.Name} is Ownable, so its user is a {target}, and the users need to be kept in that model. "
                    + $"Add \"users\": \"{target}\" to http.auth in kscript.json (kscript make auth does this).", "Config");
            if (store.Store.Name != target)
                throw new KScriptException($"{model.Name}'s user is a {target}, but http.auth.users is {store.Store.Name}. "
                    + $"Publish Ownable (kscript publish Ownable) and make its user a {store.Store.Name}, or set http.auth.users to {target}.", "Config");
        }

        /// <summary>
        /// The guest id: a test's @acting_as_guest(), else the request's guest cookie, as the Guest model's key. With
        /// <paramref name="create"/>, a browser without one is given one. Null outside a request, and once someone is
        /// signed in.
        /// </summary>
        public static object Guest(CallContext call, bool create)
        {
            if (Policies.ActingGuest(call.Host) is string acting) return Parse(call, acting);
            AuthRequest auth = AuthRequest.Current(call.Host);
            // A signed-in user has no guest id of their own: @guest() is for browsers that haven't signed in.
            if (auth == null || Policies.User(call.Host) != null) return null;
            string id = auth.Guest(create, () => New(call), text => Parse(call, text) != null);
            return id == null ? null : Parse(call, id);
        }

        /// <summary>
        /// A new guest's id. When the Guest model's key has a default (a uuid, by default), that is the id and the
        /// guest is stored when it first owns something. A key the database numbers is stored at once, to get it.
        /// </summary>
        public static string New(CallContext call)
        {
            ModelInfo guests = Guests(call);
            if (guests == null) return Guid.NewGuid().ToString("D");
            FieldDefinition key = guests.Type.Field(guests.Key);
            bool hasDefault = key != null && (key.DefaultNode != null || key.DefaultText != null);
            var none = new Dictionary<string, object>(StringComparer.Ordinal);
            Record guest = hasDefault ? Records.Prepare(call, guests, none) : Records.Create(call, guests, none);
            return Values.ToStr(Values.Unwrap(guest.GetValueOrDefault(guests.Key)));
        }

        /// <summary>A guest id as the Guest model's key (a uuid, by default), or null when it isn't one.</summary>
        public static object Parse(CallContext call, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            ModelInfo guests = Guests(call);
            FieldDefinition key = guests?.Type.Field(guests.Key);
            if (key == null) return UuidValue.TryParse(id, out UuidValue uuid, out _) ? uuid : null;
            try
            {
                RecordType type = guests.Type;
                return Shapes.Convert(call.Interpreter, id, key.Type.WithOptional(false).ToString(), $"{GuestModel}.{guests.Key}", type.Closure, type.Module);
            }
            catch (KScriptException)
            {
                return null;
            }
        }

        /// <summary>The app's Guest model, as this run declared it, or null.</summary>
        private static ModelInfo Guests(CallContext call)
        {
            ModelInfo model = ModelInfo.Named(call.Interpreter, GuestModel);
            model?.Ready();
            return model;
        }
    }
}
