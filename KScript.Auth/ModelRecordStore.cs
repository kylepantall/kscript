using System.Runtime.CompilerServices;

using KScript.Data;
using KScript.Runtime;

namespace KScript.Auth
{
    /// <summary>
    /// Users kept in a KScript.Data model (http.auth.users), so they are rows of the app's database and scripts get
    /// the model's records: <c>@user().posts</c>. The model needs an email field and a password field; the hash
    /// KScript.Auth makes is kept in the password field.
    /// </summary>
    public sealed class ModelRecordStore : RecordStore
    {
        /// <summary>The fields KScript.Auth sets that the model keeps itself, or that aren't fields of the model.</summary>
        private static readonly string[] Kept = { "id", "created_at", "updated_at" };

        /// <summary>The record each map this store handed out was made from.</summary>
        private readonly ConditionalWeakTable<Dictionary<string, object>, Record> made = new ConditionalWeakTable<Dictionary<string, object>, Record>();

        public ModelRecordStore(ModelStore store, bool passkeySignup = false)
        {
            Store = store;
            string what = "http.auth.users";
            if (!store.Has("email"))
                throw new KScriptException($"{what} is {store.Name}, so {store.Name} needs an email field: email: email.", "Config");
            Password = store.PasswordField
                ?? throw new KScriptException($"{what} is {store.Name}, so {store.Name} needs a password field: password: password.", "Config");
            // Someone who signs up with a passkey has no email or password yet.
            string required = passkeySignup ? new[] { "email", Password }.FirstOrDefault(store.Requires) : null;
            if (required != null)
                throw new KScriptException($"http.auth.passkey_signup is on, so people can sign up with only a passkey, and {store.Name}.{required} must be optional: {required}?: {(required == Password ? "password" : "email")}.", "Config");
        }

        public ModelStore Store { get; }

        /// <summary>The model's password field, which holds the hash.</summary>
        public string Password { get; }

        private Dictionary<string, object> Map(Record record)
        {
            if (record == null) return null;
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> kv in record)
            {
                if (kv.Key == Password) map[UserStore.HashField] = ModelStore.HashOf(kv.Value);
                else map[kv.Key] = kv.Key == Store.Key ? AuthApp.Text(kv.Value) : kv.Value;
            }
            made.AddOrUpdate(map, record);
            return map;
        }

        /// <summary>The fields of a map as the model's: the hash in the password field, the model's own fields left out.</summary>
        private Dictionary<string, object> Values(Dictionary<string, object> map, bool all)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            if (all)
            {
                // A field missing from the map was removed: ->update(nickname: null).
                foreach (string field in Store.Fields.Where(f => f != Password)) values[field] = null;
            }
            foreach (KeyValuePair<string, object> kv in map)
            {
                if (Kept.Contains(kv.Key) || kv.Key == Store.Key) continue;
                if (kv.Key == UserStore.HashField)
                {
                    if (kv.Value != null) values[Password] = ModelStore.Hash(AuthApp.Text(kv.Value));
                    continue;
                }
                if (!Store.Has(kv.Key) || kv.Key == Password)
                    throw new KScriptException($"{Store.Name} has no field '{kv.Key}', so a user can't have one. Add it to the {Store.Name} model.", "Argument");
                values[kv.Key] = kv.Value;
            }
            return values;
        }

        public override Dictionary<string, object> Get(string key) => Map(Store.Find(key));

        public override void Put(string key, Dictionary<string, object> record)
        {
            Record found = (made.TryGetValue(record, out Record r) ? r : null) ?? Store.Find(key)
                ?? throw new KScriptException($"There is no {Store.Name} with the {Store.Key} '{key}'.", "NotFound");
            Store.Update(found, Values(record, all: true));
            made.AddOrUpdate(record, found);
        }

        public override string Add(Dictionary<string, object> record)
        {
            Record created = Store.Create(Values(record, all: false));
            string key = AuthApp.Text(created[Store.Key]);
            record["id"] = key;
            made.AddOrUpdate(record, created);
            return key;
        }

        public override bool Delete(string key)
        {
            Record found = Store.Find(key);
            return found != null && Store.Delete(found);
        }

        public override IEnumerable<KeyValuePair<string, Dictionary<string, object>>> All() =>
            Store.All().Select(r => new KeyValuePair<string, Dictionary<string, object>>(AuthApp.Text(r[Store.Key]), Map(r))).ToList();

        public override Dictionary<string, object> FindBy(string field, string value) => Map(Store.FindBy(field, value));

        public override int Count() => Store.Count();

        /// <summary>The model's record, so scripts can read its relations and call its methods.</summary>
        public override Dictionary<string, object> Present(Dictionary<string, object> record, Dictionary<string, object> shown) =>
            made.TryGetValue(record, out Record r) ? r : shown;
    }
}
