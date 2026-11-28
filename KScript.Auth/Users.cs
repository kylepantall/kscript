using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// The users available through <c>@users()</c>. Password accounts live in the site's configured user store.
    /// Passkey-only profiles share a transaction with their credentials and expose the same stable id to scripts.
    /// </summary>
    public sealed class UserStore : IKObject
    {
        internal const string HashField = "password_hash";

        /// <summary>Fields a user map never shows, and that @register() and ->update() don't take.</summary>
        internal static readonly string[] Private = { HashField, "password" };

        private static readonly object Gate = new object();
        private static string dummyHash;

        public UserStore(AuthApp app) => App = app;

        public AuthApp App { get; }

        public string TypeName => AuthPackage.UsersType;

        public string ToText() => $"the users of {App.Name}";

        public override string ToString() => ToText();

        public bool TryGetMember(string name, out object value)
        {
            value = name == "count" ? (double)Count() : null;
            return value != null;
        }

        /// <summary>Methods go through the package, so their arguments are bound by name first.</summary>
        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        /// <summary>A user as scripts see it: the record without its password hash.</summary>
        public static Dictionary<string, object> Public(Dictionary<string, object> record)
        {
            if (record == null) return null;
            var user = new Dictionary<string, object>(record, StringComparer.Ordinal);
            foreach (string field in Private) user.Remove(field);
            return user;
        }

        public Dictionary<string, object> Record(string id)
        {
            if (PasskeyStore.IsUserId(id)) return App.PasskeyStore.User(id);
            return string.IsNullOrEmpty(id) || !IsId(id) ? null : App.Users.Get(id);
        }

        private static bool IsId(string id) => id.Length <= 128 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

        public Dictionary<string, object> Find(string id) => Show(Record(id));

        /// <summary>A record as scripts see it: without its password hash, or the model's record for users kept in a model.</summary>
        public Dictionary<string, object> Show(Dictionary<string, object> record) => record == null ? null : App.Users.Present(record, Public(record));

        /// <summary>The record with this email (case doesn't matter), or null.</summary>
        public Dictionary<string, object> RecordByEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var record = App.Users.FindBy("email", email.Trim());
            return record != null && !PasskeyStore.IsUserId(AuthApp.Text(record.GetValueOrDefault("id"))) ? record : null;
        }

        /// <summary>An email for the store: checked and normalised by the email type (with the project's rules).</summary>
        internal static string Email(CallContext c, object value)
        {
            object converted = Validate(c, value, "email", "The email", "email");
            return ((ISemanticText)converted).Text;
        }

        /// <summary>A password: checked by the password type, so the project's rules (min length, strength) apply.</summary>
        internal static PasswordValue Password(CallContext c, object value) =>
            (PasswordValue)Validate(c, value, "password", "The password", "password");

        private static object Validate(CallContext c, object value, string type, string what, string field)
        {
            try
            {
                return Shapes.Convert(c.Interpreter, value, type, what, c.Scope, c.Module);
            }
            catch (KScriptException ex) when (ex.Type == "Shape" || ex.Type == "Type" || ex.Type == "Value" || ex.Type == "Argument")
            {
                throw Invalid(field, Sentence(field, ex));
            }
        }

        /// <summary>
        /// One sentence for a form: "The password must be at least 10 characters." or "That isn't an email address:
        /// it has no @." instead of the type error's list of problems.
        /// </summary>
        private static string Sentence(string field, KScriptException ex)
        {
            string message = ex.Fields.TryGetValue("problems", out object p) && p is List<object> problems && problems.FirstOrDefault() is Dictionary<string, object> first
                ? AuthApp.Text(first.GetValueOrDefault("message")) ?? ex.Message
                : ex.Message;
            // The type's message may already name the field: "The email needs an email, but got ...".
            string named = $"The {field} ";
            if (message.StartsWith(named, StringComparison.Ordinal)) message = message.Substring(named.Length);
            int reason = message.IndexOf(": ", StringComparison.Ordinal);
            if (message.StartsWith("needs ", StringComparison.Ordinal))
                return reason > 0 ? $"That isn't {(field == "email" ? "an email address" : "a valid " + field)}: {message.Substring(reason + 2)}" : $"That isn't a valid {field}.";
            return $"The {field} {message}";
        }

        /// <summary>A Validation error naming the field, so a form can show the message beside it.</summary>
        internal static KScriptException Invalid(string field, string message)
        {
            var error = new KScriptException(message, "Validation");
            error.Fields["field"] = field;
            error.Fields["problems"] = new List<object> { new Dictionary<string, object> { ["field"] = field, ["message"] = message } };
            return error;
        }

        public Dictionary<string, object> Register(CallContext c, object email, object password, IReadOnlyDictionary<string, object> fields)
        {
            string address = Email(c, email);
            PasswordValue secret = Password(c, password);
            string hash = secret.Hash(secret.Algorithm);
            lock (Gate)
            {
                if (RecordByEmail(address) != null) throw Invalid("email", $"Someone has already signed up with {address}.");
                var record = new Dictionary<string, object>(StringComparer.Ordinal) { ["email"] = address };
                foreach (var kv in fields ?? new Dictionary<string, object>()) AddField(record, kv.Key, kv.Value);
                record["created_at"] = AuthApp.Stamp();
                record["updated_at"] = record["created_at"];
                record[HashField] = hash;
                App.Users.Add(record);
                return Show(record);
            }
        }

        private static void AddField(Dictionary<string, object> record, string name, object value)
        {
            if (Private.Contains(name) || name == "id" || name == "created_at" || name == "updated_at")
                throw new KScriptException($"'{name}' is kept by KScript.Auth, so it can't be set. Change a password with @users()->set_password(id, password).", "Argument");
            if (value is PasswordValue)
                throw new KScriptException($"'{name}' is a password. Only the sign-in password is kept, and only as a hash.", "Argument");
            record[name] = value is ISemanticText text ? text.Text : value;
        }

        /// <summary>
        /// The user whose email and password these are, or null. It takes about as long when there is no such user,
        /// so the time doesn't tell anyone which emails have accounts. A hash made with older settings is made again,
        /// with the password's own algorithm or else <paramref name="algorithm"/> (the project's), or argon2id.
        /// </summary>
        public Dictionary<string, object> Verify(object email, object password, string algorithm = null)
        {
            string address = email is ISemanticText t ? t.Text : AuthApp.Text(email);
            PasswordValue secret = password as PasswordValue;
            if (secret == null && !PasswordValue.TryCreate(AuthApp.Text(password) ?? string.Empty, out secret, out _)) return null;

            Dictionary<string, object> record = RecordByEmail(address);
            string stored = record != null && record.TryGetValue(HashField, out object h) ? AuthApp.Text(h) : null;
            if (stored == null)
            {
                secret.Verify(DummyHash());
                return null;
            }
            if (!secret.Verify(stored)) return null;
            algorithm = (secret.Algorithm ?? algorithm ?? "argon2id").Trim().ToLowerInvariant();
            if (PasswordValue.NeedsRehash(stored, algorithm))
            {
                record[HashField] = secret.Hash(algorithm);
                App.Users.Put(AuthApp.Text(record["id"]), record);
            }
            return Show(record);
        }

        private static string DummyHash()
        {
            if (dummyHash == null)
            {
                PasswordValue.TryCreate(AuthApp.NewSecret(), out PasswordValue dummy, out _);
                dummyHash = dummy.Hash(null);
            }
            return dummyHash;
        }

        public Dictionary<string, object> Update(CallContext c, string id, IReadOnlyDictionary<string, object> fields)
        {
            lock (Gate)
            {
                bool passkeyOnly = PasskeyStore.IsUserId(id);
                if (passkeyOnly && fields.ContainsKey("email"))
                    throw Invalid("email", "This account uses passkeys and has no sign-in email.");
                Dictionary<string, object> record = Need(id);
                foreach (var kv in fields)
                {
                    if (kv.Key == "email")
                    {
                        string address = Email(c, kv.Value);
                        Dictionary<string, object> other = RecordByEmail(address);
                        if (other != null && AuthApp.Text(other["id"]) != id) throw Invalid("email", $"Someone has already signed up with {address}.");
                        record["email"] = address;
                        continue;
                    }
                    if (kv.Value == null && !Private.Contains(kv.Key) && kv.Key != "id" && kv.Key != "created_at" && kv.Key != "updated_at")
                    {
                        record.Remove(kv.Key);
                        continue;
                    }
                    AddField(record, kv.Key, kv.Value);
                }
                record["updated_at"] = AuthApp.Stamp();
                if (!passkeyOnly) App.Users.Put(id, record);
                else if (!App.PasskeyStore.UpdateUser(id, record)) throw new KScriptException("The account has ended.", "NotFound");
                return Show(record);
            }
        }

        public Dictionary<string, object> SetPassword(CallContext c, string id, object password)
        {
            if (PasskeyStore.IsUserId(id))
                throw Invalid("password", "This account signs in with a passkey.");
            PasswordValue secret = Password(c, password);
            lock (Gate)
            {
                Dictionary<string, object> record = Need(id);
                record[HashField] = secret.Hash(secret.Algorithm);
                record["updated_at"] = AuthApp.Stamp();
                App.Users.Put(id, record);
                return Show(record);
            }
        }

        /// <summary>Deletes a user with their sessions, API tokens and reset tokens.</summary>
        public bool Delete(string id)
        {
            if (Record(id) == null) return false;
            if (!PasskeyStore.IsUserId(id)) App.Users.Delete(id);
            App.PasskeyStore.ForgetUser(id);
            Forget(App.Sessions, id);
            Forget(App.Tokens, id);
            Forget(App.Resets, id);
            return true;
        }

        /// <summary>Removes every record of <paramref name="store"/> that belongs to the user, except <paramref name="keep"/>.</summary>
        internal static int Forget(RecordStore store, string userId, string keep = null)
        {
            int count = 0;
            if (string.IsNullOrEmpty(userId)) return count;
            foreach (var kv in store.All().ToList())
            {
                if (kv.Key != keep && AuthApp.Text(kv.Value.TryGetValue("user_id", out object u) ? u : null) == userId && store.Delete(kv.Key)) count++;
            }
            return count;
        }

        private Dictionary<string, object> Need(string id) =>
            Record(id) ?? throw new KScriptException($"There is no user with the id '{id}'.", "NotFound");

        private int Count() => App.Users.Count() + App.PasskeyStore.UserCount();

        public List<object> All() =>
            App.Users.All().Select(kv => kv.Value).Concat(App.PasskeyStore.Users())
                .OrderBy(u => AuthApp.Text(u.GetValueOrDefault("created_at")), StringComparer.Ordinal)
                .ThenBy(u => AuthApp.Text(u.GetValueOrDefault("email")), StringComparer.OrdinalIgnoreCase)
                .Select(u => (object)Show(u))
                .ToList();

        /// <summary>The id of a user given as a map ({ id: ... }) or as the id itself.</summary>
        internal static string IdOf(object user, string what) => user switch
        {
            // A user kept in a model has the model's key, whatever it is called (key: 'uid').
            Record record when KScript.Data.ModelInfo.Of(record) is KScript.Data.ModelInfo model && record.TryGetValue(model.Key, out object key) && key != null => AuthApp.Text(key),
            Dictionary<string, object> map when map.TryGetValue("id", out object id) && id != null => AuthApp.Text(id),
            string s when s.Length > 0 => s,
            UuidValue u => u.Text,
            double d => AuthApp.Text(d),
            _ => throw new KScriptException($"{what} needs a user: a map with an id (such as what @register() or @users()->find() return), or the id itself, but got {Values.Describe(user)}.", "Argument"),
        };

        internal static readonly (string Name, string Params, string Description, string Returns)[] Methods =
        {
            ("find", "id", "The user with this id, or null.", KType.Map),
            ("find_email", "email", "The user with this email (case doesn't matter), or null.", KType.Map),
            ("all", "", "Every user, oldest first.", KType.List),
            ("count", "", "How many users there are.", KType.Number),
            ("verify", "email, password", "The user with this email and password, or null. Unlike @attempt(), it doesn't sign them in or count failures.", KType.Map),
            ("update", "id, fields...", "Changes a user's fields (null removes one) and returns the user. A new email is checked like @register()'s.", KType.Map),
            ("set_password", "id, password", "Gives a user a new password, checked by the password type, and returns the user.", KType.Map),
            ("delete", "id", "Deletes a user, with their sessions and tokens. True if there was one.", KType.Bool),
        };

        internal object Invoke(CallContext c, string name) => name switch
        {
            "find" => Find(IdOf(c.Arg(0), "->find()")),
            "find_email" => Show(RecordByEmail(c.Arg(0) is ISemanticText t ? t.Text : c.Str(0))),
            "all" => All(),
            "count" => (double)Count(),
            "verify" => Verify(c.Arg(0), c.Arg(1), PasswordValue.ProjectAlgorithm(c.Module)),
            "update" => Update(c, IdOf(c.Arg(0), "->update()"), Fields(c, 1)),
            "set_password" => SetPassword(c, IdOf(c.Arg(0), "->set_password()"), c.Arg(1)),
            _ => Delete(IdOf(c.Arg(0), "->delete()")),
        };

        /// <summary>The fields of a call: a map at <paramref name="index"/>, and named arguments.</summary>
        internal static Dictionary<string, object> Fields(CallContext c, int index)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            if (c.Arg(index) is Dictionary<string, object> map) foreach (var kv in map) fields[kv.Key] = kv.Value;
            foreach (var kv in c.Named.Where(kv => !(c.ParameterNames?.Contains(kv.Key) ?? false))) fields[kv.Key] = kv.Value;
            return fields;
        }
    }
}
