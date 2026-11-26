using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// The request's session as <c>$session</c>: values kept between requests by the session cookie, such as a cart
    /// or a message flashed before a redirect.
    /// </summary>
    public sealed class SessionValue : IKObject
    {
        public SessionValue(AuthRequest auth) => Auth = auth;

        /// <summary>The request's sign-in state, or null outside kscript serve.</summary>
        public AuthRequest Auth { get; }

        public string TypeName => AuthPackage.SessionType;

        public string ToText() => Auth?.SessionId != null ? "a session" : "no session";

        public override string ToString() => ToText();

        /// <summary>The session's own facts: <c>$session.token</c>. Values kept in it are read with <c>->get(key)</c>.</summary>
        public bool TryGetMember(string name, out object value)
        {
            value = null;
            if (name != "token") return false;
            value = (Auth ?? throw new KScriptException("$session.token works while kscript serve handles a request.", "Context")).CsrfToken;
            return true;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        internal object Invoke(CallContext c, string name)
        {
            AuthRequest auth = Auth ?? throw c.Error($"$session{c.Display} works while kscript serve handles a request.", "Context");
            switch (name)
            {
                case "get": return auth.Has(Key(c)) ? auth.Get(Key(c)) : c.Arg(1);
                case "has": return auth.Has(Key(c));
                case "set": auth.Put(Key(c), c.Arg(1)); return c.Arg(1);
                case "flash": auth.Flash(Key(c), c.Arg(1)); return c.Arg(1);
                case "remove": return auth.Forget(Key(c));
                case "all": return auth.All();
                case "regenerate": auth.Regenerate(); return null;
                default: throw c.Error($"$session has no method ->{name}().", "Name");
            }
        }

        private static string Key(CallContext c)
        {
            string key = c.Str(0);
            if (string.IsNullOrEmpty(key) || key.StartsWith('_')) throw c.Error($"'{key}' can't be a session key. Use a name such as 'cart' (names starting with _ are kept for KScript.Auth).", "Argument");
            return key;
        }

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("get", "key, default?", "The value kept under a key, or default (null).", KType.Any),
            ("has", "key", "True when a value is kept under the key.", KType.Bool),
            ("set", "key, value", "Keeps a value in the session, for this request and the ones after it. It starts a session (and sends its cookie) if there isn't one.", KType.Any),
            ("flash", "key, value", "Keeps a value for this request and the next one only: a message to show after a redirect, or the form's input to fill it in again.", KType.Any),
            ("remove", "key", "Removes a value. True when it was there.", KType.Bool),
            ("all", "", "Every value in the session, as a map.", KType.Map),
            ("regenerate", "", "Gives the session a new id, keeping its values. @login() does this for you.", KType.Null),
        };

        internal static readonly (string Name, string Type, string Description)[] Fields =
        {
            ("token", KType.String, "The session's CSRF token, which forms send back as _token. Same as @csrf_token()."),
        };
    }

    /// <summary>
    /// KScript.Auth: signing people in to a <c>kscript serve</c> app. Users with argon2id password hashes, cookie
    /// sessions with safe defaults, the auth, guest and csrf middleware (in KScript.Http), sign-in rate limits,
    /// password reset tokens and API tokens.
    /// </summary>
    public sealed class AuthPackage : KScriptPackage
    {
        public const string SessionType = KType.Session;
        public const string UsersType = KType.Users;

        public override string Name => "KScript.Auth";

        public override string Description => "Sign-in for kscript serve apps: users, sessions, the auth and guest middleware, CSRF, password resets and API tokens.";

        /// <summary>The request's sign-in state, or an error when there is no request.</summary>
        internal static AuthRequest Need(CallContext c) =>
            AuthRequest.Current(c.Host) ?? throw c.Error($"{c.Display} works while kscript serve handles a request (in a route's controller, middleware or a page).", "Context");

        /// <summary>The app's sign-in state for a call: the request's app, else the one for the script's folder.</summary>
        internal static AuthApp App(CallContext c) =>
            AuthRequest.Current(c.Host)?.App ?? AuthApp.ForFolder(c.Module?.Directory ?? c.Host.WorkingDirectory);

        protected override void Register(PackageBuilder p)
        {
            foreach (var f in SessionValue.Fields)
                p.Field(SessionType, f.Name, f.Type, f.Description);
            foreach (var m in SessionValue.Methods)
                p.Method(SessionType, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((SessionValue)t).Invoke(c, m.Name));
            foreach (var m in UserStore.Methods)
                p.Method(UsersType, m.Name, m.Params, m.Description, m.Returns, (t, c) => ((UserStore)t).Invoke(c, m.Name));

            p.Variable("session", SessionType,
                "The request's session: values kept between requests by a cookie. $session->set('cart', $items), $session->get('cart'), and $session->flash('status', 'Saved') for a message after a redirect.",
                (i, m) => new SessionValue(AuthRequest.Current(i.Host)));

            p.Command("user", "", "The signed-in user (a map with id, email and the fields they registered with), or null for a guest. It comes from the session cookie, or from an API token (Authorization: Bearer).",
                KType.Map, c => AuthRequest.Current(c.Host)?.User,
                "<if test=\"@user() != null\"><echo>Hello { @user().email }</echo></if>");
            p.Command("has_passkeys", "", "True when this site has a configured passkey origin.", KType.Bool,
                c => App(c).Settings.PasskeyOrigin != null,
                "<if test=\"@has_passkeys()\"><button type=\"button\" passkey=\"login\">Sign in with a passkey</button></if>");
            p.Command("passkeys", "", "Lists the signed-in session user's passkeys for this site, with each credential's id and creation date.", KType.List,
                c => Passkeys.List(Need(c)),
                "<for each=\"key\" in=\"@passkeys()\"><echo>{ $key.created_at }</echo></for>");
            p.Command("revoke_passkey", "id", "Revokes one of the signed-in session user's passkeys by its id. True when it was removed.", KType.Bool,
                c => Passkeys.Revoke(Need(c), c.Str(0)),
                "<do>@revoke_passkey($id)</do>");
            p.Command("is_signed_in", "", "True when someone is signed in (by the session or an API token).", KType.Bool,
                c => AuthRequest.Current(c.Host)?.User != null,
                "<if test=\"@is_signed_in()\"><echo>Welcome back</echo></if>");
            p.Command("login", "user, remember?: bool",
                "Signs a user in: the session gets a new id and the user. remember keeps them signed in for http.auth.remember (30 days) after the browser closes. The user is a map with an id: from @register(), @users()->find(), or a store of your own.",
                KType.Map, c => Need(c).Login(c.Arg(0, "user"), c.Bool(1, false) || Values.Truthy(c.Named.GetValueOrDefault("remember"))),
                "<do>@login($user, remember: true)</do>");
            p.Command("logout", "everywhere?: bool", "Signs out: the session ends and its cookie is removed. everywhere ends the user's sessions on every device.",
                KType.Null, c =>
                {
                    Need(c).Logout(c.Bool(0, false) || Values.Truthy(c.Named.GetValueOrDefault("everywhere")));
                    return null;
                }, "<do>@logout()</do>");
            p.Command("attempt", "email, password, remember?: bool, session?: bool",
                "Signs someone in with an email and password: returns the user, or null when they don't match. session: false only checks them, for an API that answers with a token instead of a cookie. After http.auth.attempts (5) failures for the same email and address in http.auth.lockout (a minute), it throws a TooManyRequests error that says how long to wait (kscript serve answers it with 429).",
                KType.Map, Attempt,
                "<let name=\"user\" value=\"@attempt($form.email, $form.password, remember: $form.remember)\" />");
            p.Command("register", "email, password, fields...",
                "Adds a user and returns it (without signing them in). The email and password are checked by the email and password types, with your project's rules (\"types\": { \"password\": { \"min\": 12 } } in kscript.json); a problem, or an email that is taken, is a Validation error naming the field. The password is kept only as an argon2id hash. Other named values are kept as the user's fields.",
                KType.Map, c => new UserStore(App(c)).Register(c, c.Arg(0, "email"), c.Arg(1, "password"), UserStore.Fields(c, 2)),
                "<let name=\"user\" value=\"@register($form.email, $form.password, name: $form.name)\" />");
            p.Command("users", "", "The app's users, kept in its data folder: ->find(id), ->find_email(email), ->all(), ->count(), ->verify(email, password), ->update(id, ...), ->set_password(id, password), ->delete(id).",
                UsersType, c => new UserStore(App(c)),
                "<echo>{ @users()->count() } people have signed up</echo>");
            p.Command("csrf_token", "", "The session's CSRF token. Forms send it back as _token, and scripts as an X-CSRF-Token header.",
                KType.String, c => Need(c).CsrfToken,
                "<meta name=\"csrf-token\" content=\"{ @csrf_token() }\" />");
            p.Command("csrf_field", "", "A hidden form field holding the CSRF token: write {! @csrf_field() } inside each <form method=\"post\">.",
                KType.String, c => $"<input type=\"hidden\" name=\"_token\" value=\"{Values.HtmlEscape(Need(c).CsrfToken)}\">",
                "<form method=\"post\" action=\"/login\">{! @csrf_field() }</form>");
            p.Command("intended", "default?", "Where to go after signing in: the page the auth middleware sent someone away from, else default (http.auth.home, /). It is always a path on this site.",
                KType.String, c => Need(c).Intended(c.Has(0, "default") ? AuthApp.Text(c.Arg(0, "default")) : null),
                "<return value=\"@redirect(@intended())\" />");
            p.Command("reset_token", "email",
                "Starts a password reset: a token to put in a link you email to the person, or null when no user has that email. It works once, for http.auth.reset_lifetime (an hour), and a new one replaces the last. Tell the person the same thing either way, so the page doesn't show who has an account.",
                KType.String, ResetToken,
                "<let name=\"token\" value=\"@reset_token($form.email)\" />");
            p.Command("reset_password", "token, password",
                "Finishes a password reset: gives the token's user the new password (checked by the password type) and ends their sessions. Returns the user, or null when the token is wrong, used or too old.",
                KType.Map, ResetPassword,
                "<let name=\"user\" value=\"@reset_password($form.token, $form.password)\" />");
            p.Command("api_token", "user, name?, expires?: duration",
                "Makes an API token for a user and returns it. Show it once: only its hash is kept. Requests that send it as Authorization: Bearer <token> are signed in as the user (the auth middleware lets them through). expires is how long it works for, such as 30d; by default it lasts until it is revoked.",
                KType.String, ApiToken,
                "<let name=\"token\" value=\"@api_token(@user(), name: 'laptop')\" />");
            p.Command("api_tokens", "user", "A user's API tokens, without the tokens themselves: [{ id, name, created_at, last_used_at, expires_at }].",
                KType.List, c => Tokens(App(c), UserStore.IdOf(c.Arg(0, "user") ?? AuthRequest.Current(c.Host)?.User, "@api_tokens()")),
                "<for each=\"t\" in=\"@api_tokens(@user())\"><echo>{ $t.name }</echo></for>");
            p.Command("revoke_token", "token, user?", "Revokes an API token, given as the token or its id (from @api_tokens()). With user, only a token of theirs is revoked, so a page can let people revoke their own. True if there was one.",
                KType.Bool, c => Revoke(App(c), c.Str(0), c.Has(1, "user") && c.Arg(1, "user") != null ? UserStore.IdOf(c.Arg(1, "user"), "@revoke_token()") : null),
                "<do>@revoke_token($id)</do>");

            // A user has the fields they registered with too (name: ...), so these are the ones every user has.
            const string user = "{ id: string, email: email, created_at: string, updated_at: string }";
            p.ResultShapes(
                ("@user", user),
                ("@attempt", user),
                ("@register", user),
                ("@login", user),
                ("@reset_password", user),
                ("@api_tokens", "list<{ id: string, name: string, created_at: string, last_used_at: string, expires_at: string }>"));

            p.Examples(
                ("session->get", "<echo>{ $session->get('cart', default: []) }</echo>"),
                ("session->has", "<if test=\"$session->has('cart')\"><echo>You have a cart</echo></if>"),
                ("session->set", "<do>$session->set('theme', 'dark')</do>"),
                ("session->flash", "<do>$session->flash('status', 'Your profile was saved.')</do>"),
                ("session->remove", "<do>$session->remove('cart')</do>"),
                ("session->all", "<echo>{ $session->all() }</echo>"),
                ("session->regenerate", "<do>$session->regenerate()</do>"),
                ("session.token", "<echo>{ $session.token }</echo>"),
                ("users->find", "<let name=\"user\" value=\"@users()->find($id)\" />"),
                ("users->find_email", "<let name=\"user\" value=\"@users()->find_email('joan@example.com')\" />"),
                ("users->all", "<for each=\"u\" in=\"@users()->all()\"><echo>{ $u.email }</echo></for>"),
                ("users->count", "<echo>{ @users()->count() }</echo>"),
                ("users->verify", "<let name=\"user\" value=\"@users()->verify($email, $password)\" />"),
                ("users->update", "<do>@users()->update($user.id, name: 'Joan Clarke')</do>"),
                ("users->set_password", "<do>@users()->set_password($user.id, $form.password)</do>"),
                ("users->delete", "<do>@users()->delete($user.id)</do>"));
        }

        private static object Attempt(CallContext c)
        {
            AuthRequest auth = Need(c);
            bool session = !c.Has(3, "session") || Values.Truthy(c.Arg(3, "session"));
            object email = c.Arg(0, "email");
            string address = email is ISemanticText t ? t.Text : AuthApp.Text(email) ?? string.Empty;
            string key = address.Trim().ToLowerInvariant() + "|" + AuthApp.Text(auth.Request.GetValueOrDefault("ip"));
            auth.App.CheckAttempts(key);
            Dictionary<string, object> user = auth.Users.Verify(email, c.Arg(1, "password"), PasswordValue.ProjectAlgorithm(c.Module));
            if (user == null)
            {
                auth.App.Failed(key);
                return null;
            }
            auth.App.Succeeded(key);
            if (!session) return user;
            return auth.Login(user, c.Bool(2, false) || Values.Truthy(c.Named.GetValueOrDefault("remember")));
        }

        private static object ResetToken(CallContext c)
        {
            AuthApp app = App(c);
            object email = c.Arg(0, "email");
            Dictionary<string, object> record = new UserStore(app).RecordByEmail(email is ISemanticText t ? t.Text : AuthApp.Text(email));
            if (record == null) return null;
            string userId = AuthApp.Text(record["id"]);
            UserStore.Forget(app.Resets, userId);
            string token = AuthApp.NewSecret();
            app.Resets.Put(AuthApp.KeyOf(token), new Dictionary<string, object>
            {
                ["user_id"] = userId,
                ["created_at"] = AuthApp.Stamp(),
                ["expires"] = AuthApp.Now() + app.Settings.ResetLifetime,
            });
            app.Wrote();
            return token;
        }

        private static object ResetPassword(CallContext c)
        {
            AuthApp app = App(c);
            string token = c.Str(0);
            if (string.IsNullOrEmpty(token) || token.Length > 256) return null;
            string key = AuthApp.KeyOf(token);
            Dictionary<string, object> record = app.Resets.Get(key);
            if (record == null) return null;
            if (!(record.GetValueOrDefault("expires") is double ends && ends > AuthApp.Now()))
            {
                app.Resets.Delete(key);
                return null;
            }
            var users = new UserStore(app);
            string userId = AuthApp.Text(record.GetValueOrDefault("user_id"));
            if (users.Record(userId) == null)
            {
                app.Resets.Delete(key);
                return null;
            }
            // The password is checked before the token is used up, so a weak one can be tried again with the same link.
            Dictionary<string, object> user = users.SetPassword(c, userId, c.Arg(1, "password"));
            app.Resets.Delete(key);
            UserStore.Forget(app.Sessions, userId);
            return user;
        }

        private static object ApiToken(CallContext c)
        {
            AuthApp app = App(c);
            string userId = UserStore.IdOf(c.Arg(0, "user"), "@api_token()");
            if (new UserStore(app).Record(userId) == null)
                throw c.Error($"There is no user with the id '{userId}'. API tokens are for the app's users (@register()).", "NotFound");
            string token = "ks_" + AuthApp.NewSecret();
            var record = new Dictionary<string, object>
            {
                ["id"] = AuthApp.KeyOf(token).Substring(0, 12),
                ["user_id"] = userId,
                ["name"] = c.Has(1, "name") ? AuthApp.Text(c.Arg(1, "name")) : "token",
                ["created_at"] = AuthApp.Stamp(),
                ["last_used_at"] = null,
            };
            if (c.Has(2, "expires") && c.Arg(2, "expires") != null)
            {
                double seconds = Duration.Seconds(c.Arg(2, "expires"), "expires");
                if (seconds <= 0) throw c.Error("expires should be more than 0 seconds, such as 12h or 30d.", "Argument");
                record["expires"] = AuthApp.Now() + seconds;
                record["expires_at"] = AuthApp.Clock().AddSeconds(seconds).ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            }
            app.Tokens.Put(AuthApp.KeyOf(token), record);
            return token;
        }

        private static List<object> Tokens(AuthApp app, string userId) =>
            app.Tokens.All().Where(kv => AuthApp.Text(kv.Value.GetValueOrDefault("user_id")) == userId)
                .Select(kv => (object)new Dictionary<string, object>
                {
                    ["id"] = kv.Value.GetValueOrDefault("id"),
                    ["name"] = kv.Value.GetValueOrDefault("name"),
                    ["created_at"] = kv.Value.GetValueOrDefault("created_at"),
                    ["last_used_at"] = kv.Value.GetValueOrDefault("last_used_at"),
                    ["expires_at"] = kv.Value.GetValueOrDefault("expires_at"),
                })
                .OrderBy(t => AuthApp.Text(((Dictionary<string, object>)t)["created_at"]), StringComparer.Ordinal)
                .ToList();

        private static bool Revoke(AuthApp app, string tokenOrId, string userId)
        {
            if (string.IsNullOrEmpty(tokenOrId)) return false;
            string key = tokenOrId.StartsWith("ks_", StringComparison.Ordinal)
                ? AuthApp.KeyOf(tokenOrId)
                : app.Tokens.All().Where(kv => AuthApp.Text(kv.Value.GetValueOrDefault("id")) == tokenOrId).Select(kv => kv.Key).FirstOrDefault();
            if (key == null || app.Tokens.Get(key) is not { } record) return false;
            if (userId != null && AuthApp.Text(record.GetValueOrDefault("user_id")) != userId) return false;
            return app.Tokens.Delete(key);
        }
    }
}
