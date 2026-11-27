using System.Runtime.CompilerServices;

using KScript.Projects;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Auth
{
    /// <summary>One <c>policy for Post { }</c> block: the functions that say who may do what to a model's records.</summary>
    internal sealed class Policy
    {
        public Policy(RecordType model, Module module, MarkupElement element)
        {
            Model = model;
            Module = module;
            Element = element;
        }

        public RecordType Model { get; }
        public Module Module { get; }
        public MarkupElement Element { get; }
        public Dictionary<string, UserFunction> Abilities { get; } = new Dictionary<string, UserFunction>(StringComparer.Ordinal);

        /// <summary>Where it was declared, for errors: <c>post.kscript line 3</c>.</summary>
        public string Where => $"{Module.DisplayName} line {Module.PositionOf(Element.Span.Start).Line}";
    }

    /// <summary>
    /// The rules of a policy that the runtime and the editor both read: the names it reserves, where its files are
    /// kept, what an ability takes, and the words for each mistake.
    /// </summary>
    public static class PolicyRules
    {
        /// <summary>The function that runs before every ability: true or false decides, null carries on.</summary>
        public const string Before = "before";

        /// <summary>The project folder whose files hold the policies, one per model: kscript.json's paths.policies, or policies.</summary>
        public const string Folder = "policies";

        /// <summary>The policies folder of a project, as a full path.</summary>
        public static string FolderOf(ProjectConfig project) => Path.GetFullPath(Path.Combine(project.Directory, project.PathFor(Folder)));

        /// <summary>The abilities a new policy starts with.</summary>
        public static readonly IReadOnlyList<string> Standard = new[] { "create", "read", "update", "delete" };

        /// <summary>The commands that ask a policy: their first argument is an ability, their second the record or model.</summary>
        public static readonly IReadOnlyList<string> Commands = new[] { "can", "cannot", "authorize" };

        public const string SignIn = "Sign in first.";

        public static string NoPolicy(string model) =>
            $"{model} has no policy. Write one in {Folder}/{Conventions.ToSnakeCase(model)}.kscript: policy for {model} {{ }}.";

        public static string NoAbility(string model, string ability, IEnumerable<string> has) =>
            $"The {model} policy has no '{ability}'. It has: {string.Join(", ", has.Where(a => a != Before))}.";

        /// <summary>What a refused user is told: about this record, or about the model when there is no record yet (create).</summary>
        public static string Denied(string model, string ability, bool record) =>
            record ? $"You are not allowed to {ability} this {model}." : $"You are not allowed to {ability} a {model}.";

        /// <summary>The message for an ability with more parameters than it is called with, or null when it fits.</summary>
        public static string TooManyParameters(string model, string ability, int count) =>
            count <= (ability == Before ? 1 : 2) ? null
            : ability == Before ? "before takes the user only: fn before(user: User)."
            : $"An ability takes the user, then the {model}: fn {ability}(user: User, {Conventions.ToSnakeCase(model)}: {model}).";

        public static string NoModel(string name) => $"Unknown type '{name}' for the policy. Import the file that declares the model.";

        public static string Unchecked(string model) =>
            $"{model} has a policy, and this takes a {model} without asking it. Write @inject(can: read) to ask, or check with @can() where it is used.";
    }

    /// <summary>What a policy said about one ability.</summary>
    internal enum Verdict
    {
        Allowed,
        Denied,

        /// <summary>Nobody is signed in, and the ability takes a user: sign in first.</summary>
        Guest,
    }

    /// <summary>
    /// The one place policies are declared, found and asked. <c>@can</c>, <c>@cannot</c>, <c>@authorize</c>, a
    /// route's <c>can:</c> and <c>@inject(can:)</c> all come through <see cref="Ask"/>, so they agree.
    /// </summary>
    internal static class Policies
    {
        public const string Before = PolicyRules.Before;
        public const string Folder = PolicyRules.Folder;
        public const string SignIn = PolicyRules.SignIn;

        /// <summary>The host item <c>@acting_as()</c> sets: who a test asks about.</summary>
        public const string ActingKey = "KScript.Auth.ActingAs";

        private sealed class Registry
        {
            public readonly Dictionary<RecordType, Policy> ByModel = new Dictionary<RecordType, Policy>();
            public readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<Interpreter, Registry> Registries = new ConditionalWeakTable<Interpreter, Registry>();

        /// <summary>Who is asking: the user @acting_as() set, else the signed-in user, else null for a guest.</summary>
        public static object User(KScriptHost host)
        {
            if (host.Items.TryGetValue(ActingKey, out object acting)) return acting;
            AuthRequest auth = AuthRequest.Current(host);
            return auth != null && auth.Request.TryGetValue(ActingKey, out acting) ? acting : auth?.User;
        }

        /// <summary>
        /// Sets who is asking, until the test ends. While a request is answered the request carries it too, so the
        /// view a controller renders asks about the same user.
        /// </summary>
        public static object ActAs(KScriptHost host, object user)
        {
            if (!host.Items.ContainsKey(ActingKey)) host.AfterTest.Add(() => host.Items.Remove(ActingKey));
            host.Items[ActingKey] = user;
            AuthRequest auth = AuthRequest.Current(host);
            if (auth != null) auth.Request[ActingKey] = user;
            if (user != null)
            {
                // Acting as a user ends acting as a guest.
                host.Items.Remove(GuestKey);
                auth?.Request.Remove(GuestKey);
            }
            return user;
        }

        /// <summary>Where a test's <c>@acting_as_guest()</c> keeps the guest id it acts as (on the host, and on the request).</summary>
        public const string GuestKey = "KScript.Auth.ActingAsGuest";

        /// <summary>
        /// Acts as a guest until the test ends: no one is signed in, and <c>@guest()</c> is <paramref name="id"/> (a new
        /// uuid when it is null). Returns the guest id.
        /// </summary>
        public static string ActAsGuest(KScriptHost host, string id)
        {
            if (id != null && !Guid.TryParse(id, out _))
                throw new KScriptException($"@acting_as_guest() takes a guest id, which is a uuid, not '{id}'.", "Argument");
            id = (id != null ? Guid.Parse(id) : Guid.NewGuid()).ToString("D");
            ActAs(host, null);
            if (!host.Items.ContainsKey(GuestKey)) host.AfterTest.Add(() => host.Items.Remove(GuestKey));
            host.Items[GuestKey] = id;
            if (AuthRequest.Current(host) is AuthRequest auth) auth.Request[GuestKey] = id;
            return id;
        }

        /// <summary>The guest id a test acts as, or null.</summary>
        internal static string ActingGuest(KScriptHost host) =>
            host.Items.TryGetValue(GuestKey, out object id) ? id as string : null;

        /// <summary>Runs a &lt;policy&gt;: checks it and keeps its functions for its model.</summary>
        public static void Declare(ElementRun run)
        {
            Module module = run.Module;
            bool code = module.IsCode;
            string name = run.Raw("for")?.Trim();
            if (string.IsNullOrEmpty(name))
                throw run.Error(code ? "Write the model the policy is for: policy for Post { }." : "<policy> needs a model, such as <policy for=\"Post\">.", "for", "Syntax");
            if (run.Element.Parent != module.Root)
                throw run.Error(code ? "A policy goes at the top level of a file, not inside a block." : "<policy> must be a direct child of <kscript>.", null, "Syntax");
            if (!run.Scope.TryGet(name, out object value) || value is not RecordType model)
                throw run.Error(PolicyRules.NoModel(name), "for", "Name");

            Registry registry = Registries.GetOrCreateValue(run.Interpreter);
            if (registry.ByModel.TryGetValue(model, out Policy existing) && existing.Element != run.Element)
                throw run.Error($"{name} has two policies: at {existing.Where} and here. Keep one.", "for", "Syntax");

            var policy = new Policy(model, module, run.Element);
            foreach (MarkupNode node in run.Element.Children)
            {
                if (node is not MarkupElement e) continue;
                if (e.Name != "fn")
                    throw Located(e, code ? $"policy for {name} {{ }} holds fn abilities only, not {e.Name}." : $"<policy> holds <fn> elements only, not <{e.Name}>.");
                string ability = e.GetAttributeValue("name")?.Trim().TrimStart('@');
                if (string.IsNullOrEmpty(ability)) throw Located(e, code ? "Write the ability's name: fn update(user, post) => ..." : "<fn> needs a name, such as <fn name=\"update\">.");
                if (policy.Abilities.ContainsKey(ability)) throw Located(e, $"The {name} policy has '{ability}' twice. Keep one.");
                Scope scope = run.Scope.Child();
                UserFunction fn = run.Interpreter.DeclareFunction(e, scope, module);
                if (PolicyRules.TooManyParameters(name, ability, fn.Parameters.Count) is string tooMany) throw Located(e, tooMany);
                policy.Abilities[ability] = fn;
            }
            registry.ByModel[model] = policy;
        }

        private static KScriptException Located(MarkupElement e, string message) =>
            new KScriptException(message, "Syntax") { Offset = e.NameSpan.Start };

        /// <summary>
        /// The policy for a model: one a script that has run declared, else the one in the project's policies
        /// folder, whose files are run the first time a policy is asked for. Null when the model has none.
        /// </summary>
        public static Policy Find(Interpreter interpreter, Module caller, RecordType model)
        {
            Registry registry = Registries.GetOrCreateValue(interpreter);
            if (Held(registry, model) is Policy held) return held;

            string dir = caller?.Directory;
            string folder = ProjectFolder(string.IsNullOrEmpty(dir) ? null : ProjectConfig.Find(dir));
            if (folder == null || !registry.Loaded.Add(folder) || !Directory.Exists(folder)) return null;
            foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(ScriptFiles.IsScript).OrderBy(p => p, StringComparer.Ordinal))
            {
                interpreter.RunModule(interpreter.Load(path));
            }
            return Held(registry, model);
        }

        private static string ProjectFolder(string file)
        {
            try
            {
                return file == null ? null : PolicyRules.FolderOf(ProjectConfig.Load(file));
            }
            catch (ProjectConfigException)
            {
                return null; // the project's own commands report a broken kscript.json
            }
        }

        /// <summary>The policy kept for the model, also when the model was declared again from the same file.</summary>
        private static Policy Held(Registry registry, RecordType model)
        {
            if (registry.ByModel.TryGetValue(model, out Policy exact)) return exact;
            foreach (Policy policy in registry.ByModel.Values)
            {
                if (model.IsA(policy.Model) || (policy.Model.Name == model.Name && policy.Model.Module?.Path != null && policy.Model.Module.Path == model.Module?.Path))
                    return policy;
            }
            return null;
        }

        /// <summary>The ability an argument names: a bare word or text.</summary>
        public static string AbilityName(CallContext call, object value)
        {
            string name = Values.Unwrap(value) is BareWord word ? word.Name : Values.Unwrap(value) as string;
            if (string.IsNullOrWhiteSpace(name))
                throw call.Error($"@{call.Name}() takes an ability's name first, such as @{call.Name}(update, $post).", "Argument");
            return name.Trim();
        }

        /// <summary>Asks the model's policy whether the user may use the ability on the target (a record, or the model itself for create).</summary>
        public static Verdict Ask(CallContext call, string ability, object target, out RecordType model)
        {
            target = Values.Unwrap(target);
            model = target is Record record ? record.Type : target as RecordType;
            if (model == null)
                throw call.Error($"@{call.Name}() asks about a record or a model, such as @{call.Name}({ability}, $post) or @{call.Name}(create, Post), but got {Values.Describe(target)}.", "Argument");
            Policy policy = Find(call.Interpreter, call.Module, model) ?? throw call.Error(PolicyRules.NoPolicy(model.Name), "Name");
            if (!policy.Abilities.TryGetValue(ability, out UserFunction fn) || ability == Before)
                throw call.Error(PolicyRules.NoAbility(model.Name, ability, policy.Abilities.Keys), "Name");

            object user = User(call.Host);
            if (policy.Abilities.TryGetValue(Before, out UserFunction before) && Run(call, before, user, null, out bool _) is bool decided)
                return decided ? Verdict.Allowed : Verdict.Denied;
            object result = Run(call, fn, user, target as Record, out bool guest);
            if (guest) return Verdict.Guest;
            return Values.Truthy(result) ? Verdict.Allowed : Verdict.Denied;
        }

        /// <summary>
        /// Calls an ability with the user, then the record. A guest only reaches a function whose user may be null
        /// (<c>user: User?</c>, or no type); one that needs a user isn't run.
        /// </summary>
        private static object Run(CallContext call, UserFunction fn, object user, Record record, out bool guest)
        {
            guest = false;
            var args = new List<object>();
            if (fn.Parameters.Count > 0)
            {
                string type = fn.Parameters[0].Type;
                if (user == null && type != null && !type.TrimEnd().EndsWith('?'))
                {
                    guest = true;
                    return null;
                }
                args.Add(user);
            }
            if (fn.Parameters.Count > 1) args.Add(record);
            return Values.Unwrap(call.Interpreter.Call(fn, args, null, call.Scope, call.Module));
        }

        /// <summary>Stops with Unauthorized (401) for a guest the ability turns away, or Forbidden (403) when the policy says no.</summary>
        public static void Authorize(CallContext call, string ability, object target)
        {
            switch (Ask(call, ability, target, out RecordType model))
            {
                case Verdict.Guest: throw call.Error(SignIn, "Unauthorized");
                case Verdict.Denied: throw call.Error(PolicyRules.Denied(model.Name, ability, Values.Unwrap(target) is Record), "Forbidden");
            }
        }
    }
}
