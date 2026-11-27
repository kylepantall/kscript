using System.Globalization;
using System.Runtime.CompilerServices;

using KScript.Projects;
using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// The sign-in state of one request: its session (read from the session cookie the first time something needs it),
    /// the user, and the cookie to send back. It is kept beside the request's <c>$request</c> map, so a route's
    /// controller and the view it renders share it. When the request is answered, <see cref="Finish"/> saves the
    /// session and returns the Set-Cookie header.
    /// </summary>
    public sealed class AuthRequest
    {
        /// <summary>Where a route's interpreter keeps the request's <see cref="AuthRequest"/>.</summary>
        public const string HostKey = "KScript.Auth.Request";

        /// <summary>The session's keys for the user, the CSRF token and flashed values.</summary>
        private const string UserId = "user_id", UserCopy = "user", Csrf = "csrf", Data = "data", Flashed = "flash", Remember = "remember", Expires = "expires", Seen = "last_seen";

        private static readonly ConditionalWeakTable<Dictionary<string, object>, AuthRequest> Requests = new ConditionalWeakTable<Dictionary<string, object>, AuthRequest>();

        private bool loaded;
        private string sessionId;
        private Dictionary<string, object> session;
        private List<string> aging = new List<string>();
        private bool dirty, sendCookie, clearCookie, started;
        private bool tokenLoaded;
        private Dictionary<string, object> tokenUser;
        private Dictionary<string, object> freshUser;
        private bool freshLoaded;
        private string guestId;
        private bool guestLoaded, sendGuest, clearGuest;

        private AuthRequest(AuthApp app, Dictionary<string, object> request)
        {
            App = app;
            Request = request;
        }

        public AuthApp App { get; }

        public Dictionary<string, object> Request { get; }

        public UserStore Users => new UserStore(App);

        /// <summary>The request's sign-in state, made the first time it is asked for.</summary>
        public static AuthRequest For(Dictionary<string, object> request, AuthApp app)
        {
            lock (Requests)
            {
                if (Requests.TryGetValue(request, out AuthRequest existing)) return existing;
                var created = new AuthRequest(app, request);
                Requests.Add(request, created);
                RequestHooks.OnFinish(request, created.Finish);
                RequestHooks.BeforeCommitHeaders(request, created.CheckPendingCookie);
                return created;
            }
        }

        /// <summary>
        /// The sign-in state of the request a script is answering: a route's (set by KScript.Http) or a page's. Null
        /// outside kscript serve.
        /// </summary>
        public static AuthRequest Current(KScriptHost host)
        {
            if (host.Items.TryGetValue(HostKey, out object value) && value is AuthRequest auth) return auth;
            if (host.Items.TryGetValue(Translator.RequestKey, out object request) && request is Dictionary<string, object> map)
                return For(map, AuthApp.ForFolder(host.FileRoot ?? host.WorkingDirectory));
            return null;
        }

        #region Request data

        internal string Header(string name) =>
            Request.TryGetValue("headers", out object h) && h is Dictionary<string, object> headers && headers.TryGetValue(name, out object v) ? AuthApp.Text(v) : null;

        private string Cookie(string name) =>
            Request.TryGetValue("cookies", out object c) && c is Dictionary<string, object> cookies && cookies.TryGetValue(name, out object v) ? AuthApp.Text(v) : null;

        /// <summary>The token in an <c>Authorization: Bearer</c> header, or null.</summary>
        public string BearerToken
        {
            get
            {
                string header = Header("authorization") ?? string.Empty;
                return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null;
            }
        }

        private bool Https =>
            App.Settings.Secure ?? (string.Equals(AuthApp.Text(Request.GetValueOrDefault("scheme")), "https", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(Header("x-forwarded-proto"), "https", StringComparison.OrdinalIgnoreCase));

        #endregion

        #region Session

        /// <summary>The session, read from the cookie. With <paramref name="create"/>, a new one is started when there isn't one.</summary>
        private Dictionary<string, object> Session(bool create)
        {
            if (!loaded)
            {
                loaded = true;
                string id = Cookie(App.Settings.Cookie);

                Dictionary<string, object> found = string.IsNullOrEmpty(id) || id.Length > 128 ? null : App.Sessions.Get(AuthApp.KeyOf(id));
                if (found != null && Number(found, Expires) > AuthApp.Now())
                {
                    sessionId = id;
                    session = found;
                    // Values flashed by the last request are seen by this one, then removed.
                    aging = Keys(found);
                    found[Flashed] = new List<object>();
                    if (aging.Count > 0) dirty = true;
                }
                else if (found != null)
                {
                    App.Sessions.Delete(AuthApp.KeyOf(id));
                }
                // A cookie for a session that has ended is taken away.
                if (session == null && id != null) clearCookie = true;
            }
            if (session == null && create) Start(new Dictionary<string, object>(StringComparer.Ordinal));
            return session;
        }

        private static List<string> Keys(Dictionary<string, object> s) =>
            s.TryGetValue(Flashed, out object f) && f is List<object> keys ? keys.Select(Values.ToStr).ToList() : new List<string>();

        private static double Number(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object v) && v is double d ? d : 0;

        /// <summary>Starts a new session with a new id (keeping <paramref name="data"/>), and drops the old one.</summary>
        private void Start(Dictionary<string, object> data)
        {
            RequestHooks.RequireWritableCookies(Request);
            if (sessionId != null) App.Sessions.Delete(AuthApp.KeyOf(sessionId));
            sessionId = AuthApp.NewSecret();
            session = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [Data] = data,
                [Flashed] = new List<object>(),
                [Csrf] = AuthApp.NewSecret(),
                ["created_at"] = AuthApp.Stamp(),
            };
            dirty = sendCookie = started = true;
            clearCookie = false;
        }

        private Dictionary<string, object> DataOf(Dictionary<string, object> s)
        {
            if (s[Data] is not Dictionary<string, object> data) s[Data] = data = new Dictionary<string, object>(StringComparer.Ordinal);
            return data;
        }

        /// <summary>The session's id, or null when the request has none.</summary>
        public string SessionId => Session(create: false) != null ? sessionId : null;

        public object Get(string key) =>
            Session(create: false) is { } s && DataOf(s).TryGetValue(key, out object v) ? v : null;

        public bool Has(string key) => Session(create: false) is { } s && DataOf(s).ContainsKey(key);

        public void Put(string key, object value)
        {
            Dictionary<string, object> s = Session(create: true);
            DataOf(s)[key] = Stored(value);
            aging.Remove(key);
            dirty = true;
        }

        /// <summary>Keeps a value for this request and the next one only, such as a message after a redirect.</summary>
        public void Flash(string key, object value)
        {
            Put(key, value);
            var keys = (List<object>)session[Flashed];
            if (!keys.Contains(key)) keys.Add(key);
        }

        public bool Forget(string key)
        {
            if (Session(create: false) is not { } s || !DataOf(s).Remove(key)) return false;
            dirty = true;
            return true;
        }

        public Dictionary<string, object> All() =>
            Session(create: false) is { } s ? new Dictionary<string, object>(DataOf(s), StringComparer.Ordinal) : new Dictionary<string, object>();

        /// <summary>Values go through JSON, so they are kept the way they will come back; passwords are never kept.</summary>
        private static object Stored(object value)
        {
            if (Contains(value, v => v is PasswordValue))
                throw new KScriptException("A password can't be kept in the session. Keep the fields you need without it.", "Argument");
            return Values.ParseJson(Values.ToJson(value));
        }

        private static bool Contains(object value, Func<object, bool> test) => test(value) || value switch
        {
            Dictionary<string, object> map => map.Values.Any(v => Contains(v, test)),
            List<object> list => list.Any(v => Contains(v, test)),
            _ => false,
        };

        /// <summary>A new session id with the same data: after signing in, so an id someone saw before can't be used.</summary>
        public void Regenerate()
        {
            Dictionary<string, object> old = Session(create: true);
            if (started) return;
            Start(DataOf(old));
            foreach (string key in new[] { UserId, UserCopy, Remember, "stored" }) if (old.TryGetValue(key, out object v)) session[key] = v;
            session[Flashed] = old[Flashed];
        }

        internal string PasskeySessionKey(bool create) => Session(create) == null ? null : AuthApp.KeyOf(sessionId);

        /// <summary>The CSRF token forms send back as _token (and scripts as an X-CSRF-Token header). It starts a session if there isn't one.</summary>
        public string CsrfToken => AuthApp.Text(Session(create: true)[Csrf]);

        /// <summary>
        /// True when a request that changes something carries the session's CSRF token, in the _token field or the
        /// X-CSRF-Token (or X-XSRF-Token) header. Requests with a bearer token and no session cookie don't need one,
        /// since a browser never adds that header by itself.
        /// </summary>
        public bool CsrfValid()
        {
            string method = AuthApp.Text(Request.GetValueOrDefault("method"))?.ToUpperInvariant();
            if (method is "GET" or "HEAD" or "OPTIONS") return true;
            if (BearerToken != null && Cookie(App.Settings.Cookie) == null) return true;
            string sent = Header("x-csrf-token") ?? Header("x-xsrf-token")
                ?? (Request.GetValueOrDefault("form") is Dictionary<string, object> form && form.TryGetValue("_token", out object f) ? AuthApp.Text(f) : null)
                ?? (Request.GetValueOrDefault("json") is Dictionary<string, object> json && json.TryGetValue("_token", out object j) ? AuthApp.Text(j) : null);
            Dictionary<string, object> s = Session(create: false);
            return s != null && AuthApp.SameSecret(sent, AuthApp.Text(s.GetValueOrDefault(Csrf)));
        }

        #endregion

        #region Users

        /// <summary>The signed-in user: from the session, else from an API token. Null for a guest.</summary>
        public Dictionary<string, object> User => SessionUser ?? TokenUser;

        /// <summary>
        /// Whether a test's <c>@acting_as()</c> names who this request is from (an in-process <c>@api()</c> call made
        /// while acting as someone). That user stands in for both the session's and the token's user; null is a guest.
        /// </summary>
        private bool Acting(out Dictionary<string, object> user)
        {
            user = null;
            if (!Request.TryGetValue(Policies.ActingKey, out object acting)) return false;
            user = Values.Unwrap(acting) as Dictionary<string, object>;
            return true;
        }

        /// <summary>
        /// The session's user, read again from the users so changes show at once. A user who isn't in the app's
        /// users (one signed in with @login() from a store of your own) is the copy made at sign-in.
        /// </summary>
        public Dictionary<string, object> SessionUser
        {
            get
            {
                if (Acting(out Dictionary<string, object> acting)) return acting;
                if (freshLoaded) return freshUser;
                freshLoaded = true;
                Dictionary<string, object> s = Session(create: false);
                string id = s != null ? AuthApp.Text(s.GetValueOrDefault(UserId)) : null;
                if (id == null) return null;
                bool stored = Values.Truthy(s.GetValueOrDefault("stored"));
                if (!stored && PasskeyStore.IsUserId(id))
                {
                    Logout(everywhere: false);
                    return null;
                }
                Dictionary<string, object> record = Users.Find(id);
                if (record == null && s.GetValueOrDefault(UserCopy) is Dictionary<string, object> copy && !stored)
                    record = copy;
                if (record == null)
                {
                    // The user was deleted: the session ends.
                    Logout(everywhere: false);
                    return null;
                }
                return freshUser = record;
            }
        }

        /// <summary>The user of the request's API token (Authorization: Bearer), or null.</summary>
        public Dictionary<string, object> TokenUser
        {
            get
            {
                if (Acting(out Dictionary<string, object> acting)) return acting;
                if (tokenLoaded) return tokenUser;
                tokenLoaded = true;
                string token = BearerToken;
                if (string.IsNullOrEmpty(token) || token.Length > 256) return null;
                string key = AuthApp.KeyOf(token);
                Dictionary<string, object> record = App.Tokens.Get(key);
                if (record == null || (record.GetValueOrDefault("expires") is double ends && ends <= AuthApp.Now())) return null;
                tokenUser = Users.Find(AuthApp.Text(record.GetValueOrDefault("user_id")));
                if (tokenUser != null && AuthApp.Now() - Number(record, "last_used") >= 60)
                {
                    record["last_used"] = AuthApp.Now();
                    record["last_used_at"] = AuthApp.Stamp();
                    App.Tokens.Put(key, record);
                }
                return tokenUser;
            }
        }

        /// <summary>
        /// Signs a user in: a new session id (so one seen before can't be used), the user's id, and a copy of their
        /// fields for users from a store of your own. With <paramref name="remember"/> the cookie lasts for
        /// http.auth.remember (30 days) instead of ending with the browser.
        /// </summary>
        public Dictionary<string, object> Login(object user, bool remember) => Login(user, remember, passkeyVerified: false);

        internal Dictionary<string, object> LoginPasskey(string id) => Login(id, remember: false, passkeyVerified: true);

        private Dictionary<string, object> Login(object user, bool remember, bool passkeyVerified)
        {
            RequestHooks.RequireWritableCookies(Request);
            string id = UserStore.IdOf(user, "@login()");
            if (!passkeyVerified && PasskeyStore.IsUserId(id))
                throw new KScriptException("This account signs in with a verified passkey.", "Unauthorized");
            Dictionary<string, object> record = Users.Find(id);
            Dictionary<string, object> copy = record ?? (user is Dictionary<string, object> map ? UserStore.Public(map) : null)
                ?? throw new KScriptException($"There is no user with the id '{id}'. Give @login() a user map, such as what @register() or @users()->find() return.", "NotFound");
            Regenerate();
            session[UserId] = id;
            session[UserCopy] = Stored(copy);
            session["stored"] = record != null;
            session[Remember] = remember;
            session[Csrf] = AuthApp.NewSecret();
            NoteAdoption();
            freshLoaded = true;
            freshUser = copy;
            dirty = sendCookie = true;
            return copy;
        }

        /// <summary>Signs out: the session ends and its cookie is taken away. <paramref name="everywhere"/> ends the user's other sessions too.</summary>
        public void Logout(bool everywhere)
        {
            RequestHooks.RequireWritableCookies(Request);
            Dictionary<string, object> s = Session(create: false);
            if (s != null)
            {
                string id = AuthApp.Text(s.GetValueOrDefault(UserId));
                if (everywhere && id != null) UserStore.Forget(App.Sessions, id);
                App.Sessions.Delete(AuthApp.KeyOf(sessionId));
            }
            session = null;
            sessionId = null;
            freshLoaded = true;
            freshUser = null;
            dirty = sendCookie = false;
            clearCookie = true;
        }

        /// <summary>
        /// The browser's guest id (a uuid), from its guest cookie. With <paramref name="create"/>, a browser without
        /// one is given one, sent in a long-lived cookie when the request is answered. A test's @acting_as_guest() id
        /// wins.
        /// </summary>
        public string Guest(bool create)
        {
            if (Request.TryGetValue(Policies.GuestKey, out object acting) && acting is string id) return id;
            if (!guestLoaded)
            {
                guestLoaded = true;
                if (Guid.TryParse(Cookie(App.Settings.GuestCookie), out Guid known)) guestId = known.ToString("D");
            }
            if (guestId == null && create)
            {
                RequestHooks.RequireWritableCookies(Request);
                guestId = Guid.NewGuid().ToString("D");
                sendGuest = true;
                clearGuest = false;
            }
            return guestId;
        }

        /// <summary>The session key holding the guest whose rows wait to be moved to the signed-in user.</summary>
        private const string Adopt = "adopt_guest", Adopted = "adopted";

        /// <summary>
        /// At sign-in, with http.auth.adopt_guest: remembers the browser's guest so its rows of Ownable models move to
        /// the user, and takes the guest cookie away.
        /// </summary>
        private void NoteAdoption()
        {
            if (!App.Settings.AdoptGuest || Guest(create: false) is not string guest) return;
            session[Adopt] = "guest:" + guest;
            session[Adopted] = new List<object>();
            guestId = null;
            sendGuest = false;
            clearGuest = true;
        }

        /// <summary>
        /// Moves the rows of the owned models this run knows from the guest who signed in to <paramref name="owner"/>,
        /// each model once a session: models other files declare move when they are first used.
        /// </summary>
        internal void AdoptGuestRows(CallContext call, string owner)
        {
            Dictionary<string, object> s = Session(create: false);
            if (s == null || s.GetValueOrDefault(Adopt) is not string from) return;
            if (s.GetValueOrDefault(Adopted) is not List<object> done) s[Adopted] = done = new List<object>();
            var tables = done.Select(Values.ToStr).ToList();
            if (KScript.Data.ModelInfo.AdoptOwner(call, from, owner, tables) > 0 || tables.Count != done.Count)
            {
                s[Adopted] = tables.Cast<object>().ToList();
                dirty = true;
            }
        }

        /// <summary>Remembers the address a guest wanted, for @intended() after they sign in. Only paths on this site are kept.</summary>
        public void RememberIntended(string url)
        {
            if (Safe(url)) Put("_intended", url);
        }

        /// <summary>The address remembered by the auth middleware (then forgotten), else <paramref name="fallback"/>.</summary>
        public string Intended(string fallback)
        {
            string url = AuthApp.Text(Get("_intended"));
            Forget("_intended");
            if (Safe(url)) return url;
            return Safe(fallback) ? fallback : App.Settings.Home;
        }

        /// <summary>True for a path on this site (/people?page=2), not another site (//evil.example, https://...).</summary>
        internal static bool Safe(string url) =>
            !string.IsNullOrEmpty(url) && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal) && !url.Any(char.IsControl);

        #endregion

        #region Finishing

        private void CheckPendingCookie()
        {
            if (sendCookie || clearCookie || sendGuest || clearGuest)
                throw new KScriptException("An action that changes the session cookie cannot start streaming.", "Context");
        }

        /// <summary>Saves the session and returns the Set-Cookie header (if the cookie changed). Run once the request is answered.</summary>
        public IEnumerable<string> Finish()
        {
            var cookies = new List<string>();
            if (session != null)
            {
                foreach (string key in aging) DataOf(session).Remove(key);
                bool remember = Values.Truthy(session.GetValueOrDefault(Remember));
                double lifetime = remember ? App.Settings.Remember : App.Settings.Lifetime;
                double now = AuthApp.Now();
                // A session is saved when it changed, or now and then so it doesn't run out while it is used.
                if (dirty || now - Number(session, Seen) >= 60)
                {
                    session[Seen] = now;
                    session[Expires] = now + lifetime;
                    App.Sessions.Put(AuthApp.KeyOf(sessionId), session);
                    App.Wrote();
                }
                if (sendCookie) cookies.Add(CookieHeader(sessionId, remember ? lifetime : null));
            }
            else if (clearCookie)
            {
                cookies.Add(CookieHeader(string.Empty, 0));
            }
            if (clearGuest) cookies.Add(CookieHeader(string.Empty, 0, App.Settings.GuestCookie));
            else if (sendGuest) cookies.Add(CookieHeader(guestId, App.Settings.GuestLifetime, App.Settings.GuestCookie));
            return cookies;
        }

        private string CookieHeader(string value, double? maxAge, string name = null)
        {
            AuthSettings s = App.Settings;
            string header = $"{name ?? s.Cookie}={value}; Path=/; HttpOnly; SameSite={s.SameSite}";
            if (maxAge != null) header += "; Max-Age=" + ((long)maxAge.Value).ToString(CultureInfo.InvariantCulture);
            if (maxAge == 0) header += "; Expires=Thu, 01 Jan 1970 00:00:00 GMT";
            // SameSite=None only works on Secure cookies, so it makes the cookie Secure too.
            if (Https || s.SameSite == "None") header += "; Secure";
            return header;
        }

        #endregion
    }
}
