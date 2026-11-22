using KScript.Projects;
using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// One model's records for code outside a script, such as KScript.Auth keeping its users in a <c>User</c> model.
    /// The model is loaded from the project's models folder by a run of its own, and every call goes through that
    /// run, one at a time.
    /// </summary>
    public sealed class ModelStore
    {
        private readonly object gate = new object();
        private readonly Interpreter interpreter;
        private readonly Module module;
        private readonly ModelInfo model;

        private ModelStore(Interpreter interpreter, Module module, ModelInfo model)
        {
            this.interpreter = interpreter;
            this.module = module;
            this.model = model;
        }

        /// <summary>
        /// The store for the model called <paramref name="name"/> in the models folder of the project at
        /// <paramref name="folder"/>. <paramref name="what"/> names the setting that asked for it, in errors.
        /// </summary>
        public static ModelStore Load(KScriptHost host, string folder, string name, string what)
        {
            ProjectConfig config = ProjectConfig.FindAndLoad(folder);
            string file = ProjectConfig.ModelFileFor(config.ModelFiles(), name);
            if (file == null)
                throw new KScriptException($"{what} is {name}, but there is no {name} model in {config.PathFor("models")}. Make one with kscript make model {name}.", "Config");
            var options = new InterpreterOptions();
            options.DefaultPackages.AddRange(config.Packages);
            var interpreter = new Interpreter(host, options: options);
            Module module = interpreter.Load(file);
            interpreter.RunModule(module);
            ModelInfo model = ModelInfo.DeclaredBy(interpreter).FirstOrDefault(m => m.Name == name)
                ?? throw new KScriptException($"{what} is {name}, but {Path.GetFileName(file)} doesn't declare a model called {name}.", "Config");
            return new ModelStore(interpreter, module, model);
        }

        /// <summary>The model's name.</summary>
        public string Name => model.Name;

        /// <summary>The key field's name.</summary>
        public string Key => model.Key;

        /// <summary>The fields the model stores, apart from its key and timestamps.</summary>
        public IReadOnlyList<string> Fields => model.Columns.Select(c => c.Name)
            .Where(c => c != model.Key && c != ModelInfo.CreatedAt && c != ModelInfo.UpdatedAt && c != ModelInfo.DeletedAt).ToList();

        /// <summary>True when the model has this field.</summary>
        public bool Has(string field) => model.Column(field) != null;

        /// <summary>The first field that holds password hashes, or null.</summary>
        public string PasswordField => model.Columns.FirstOrDefault(c => c.Kind == ColumnKind.Password)?.Name;

        private CallContext Call(string name) => new CallContext(interpreter, name, Array.Empty<object>(), scope: module.Globals, module: module);

        private Query Rows() => new Query(Database.For(Call("query"), model), model.Table, model);

        private T Locked<T>(Func<T> work)
        {
            lock (gate) return work();
        }

        /// <summary>The record with this key, or null (also for a key that can't be one).</summary>
        public Record Find(string key) => Locked(() =>
        {
            object id = key;
            if (model.KeyColumn.Kind == ColumnKind.Integer)
            {
                if (!long.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n)) return null;
                id = (double)n;
            }
            return (Record)Rows().ById(id).First(Call("find"));
        });

        /// <summary>The first record whose field has this value, or null.</summary>
        public Record FindBy(string field, object value) => Locked(() =>
        {
            Query query = Rows();
            return (Record)query.And(query.Equalities(new[] { new KeyValuePair<string, object>(field, value) }, "where")).First(Call("find"));
        });

        /// <summary>Every record, in key order.</summary>
        public List<Record> All() => Locked(() =>
        {
            Query query = Rows();
            return query.OrderBy(query.ColumnNamed(model.Key, " for ->all()"), false).Get(Call("all")).Cast<Record>().ToList();
        });

        public int Count() => Locked(() => (int)Rows().Count(Call("count")));

        /// <summary>A new record with these fields, checked against the model and saved. Guarded fields can be set.</summary>
        public Record Create(Dictionary<string, object> values) => Locked(() =>
        {
            CallContext call = Call("create");
            // A hash made elsewhere can't be checked as a password, so a random one stands in while the record is
            // checked, and the hash goes in before it is saved.
            var hashes = values.Where(kv => kv.Value is PasswordHash).ToList();
            var check = new Dictionary<string, object>(values, StringComparer.Ordinal);
            foreach (var kv in hashes) check[kv.Key] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var record = (Record)Shapes.MapAs(interpreter, check, model.Type, $"The {model.Name}");
            foreach (var kv in hashes) record[kv.Key] = kv.Value;
            Records.Link(model, record);
            Records.Insert(call, model, record);
            return record;
        });

        /// <summary>Sets fields of a record (null clears one) and saves the ones that changed.</summary>
        public Record Update(Record record, IEnumerable<KeyValuePair<string, object>> values) => Locked(() =>
        {
            CallContext call = Call("update");
            foreach (KeyValuePair<string, object> v in values)
            {
                // A hash made elsewhere goes in as it is: the password type takes passwords, not hashes.
                if (v.Value is PasswordHash) record[v.Key] = v.Value;
                else record.Set(interpreter, v.Key, v.Value);
            }
            return Records.Save(call, record);
        });

        /// <summary>Deletes a record for good, even from a model with soft_deletes.</summary>
        public bool Delete(Record record) => Locked(() => Records.Delete(Call("delete"), record, force: true));

        /// <summary>A password hash made elsewhere (by KScript.Auth), as a password field holds it.</summary>
        public static object Hash(string stored) => new PasswordHash(stored);

        /// <summary>The stored hash of a password field's value, or null.</summary>
        public static string HashOf(object value) => (value as PasswordHash)?.Stored;
    }
}
