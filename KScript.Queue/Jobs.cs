using System.Globalization;

using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Queueing
{
    /// <summary>A job's options: where it goes, how often it is tried and how long it may run.</summary>
    public sealed class JobOptions
    {
        /// <summary>The queue, unless the dispatch names one.</summary>
        public string Queue { get; set; } = "default";

        /// <summary>The connection, unless the dispatch names one; null means queue.default in kscript.json.</summary>
        public string Connection { get; set; }

        /// <summary>Attempts before the job counts as failed.</summary>
        public int Tries { get; set; } = 3;

        /// <summary>Seconds to wait before each retry (the last one repeats); null means exponential: 2s, 4s, 8s, up to 10 minutes.</summary>
        public List<double> Backoff { get; set; }

        /// <summary>The longest one attempt may run, in seconds.</summary>
        public double Timeout { get; set; } = 60;

        /// <summary>Seconds after dispatch after which no more retries happen.</summary>
        public double? RetryUntil { get; set; }

        /// <summary>While a copy is waiting or running, an identical dispatch is dropped, for this many seconds at most.</summary>
        public double? UniqueFor { get; set; }

        /// <summary>The fields that make two dispatches identical for <see cref="UniqueFor"/>; empty means all of them.</summary>
        public List<string> UniqueBy { get; set; } = new List<string>();

        /// <summary>Error types that retry; empty means every error does (unless it is in <see cref="FailOn"/>).</summary>
        public List<string> RetryOn { get; set; } = new List<string>();

        /// <summary>Error types that fail the job at once.</summary>
        public List<string> FailOn { get; set; } = new List<string>();

        /// <summary>The longest exponential backoff waits, in seconds.</summary>
        public const double MaxExponential = 600;

        /// <summary>Seconds to wait before retrying after the attempt-th failed attempt (1 for the first).</summary>
        public double BackoffAfter(int attempt)
        {
            if (Backoff == null || Backoff.Count == 0) return Math.Min(MaxExponential, Math.Pow(2, Math.Max(1, attempt)));
            return Backoff[Math.Min(Backoff.Count, Math.Max(1, attempt)) - 1];
        }
    }

    /// <summary>
    /// A job type: <c>job SendWelcome(queue: mail, tries: 5) { user_id: int; fn handle() { } }</c>. Calling it with
    /// its fields (<c>SendWelcome(user_id: 7)</c>) makes a job, which <c>dispatch</c> queues.
    /// </summary>
    public sealed class JobType : IKObject, ICallable, IDeclaredType, KScript.Runtime.Dependencies.IDeclaredSource
    {
        internal JobType(string name, string description, RecordType fields, JobOptions options, MarkupElement handle, MarkupElement failed, Scope closure, Module module)
        {
            Name = name;
            Description = description;
            Fields = fields;
            Options = options;
            Handle = handle;
            Failed = failed;
            Closure = closure;
            Module = module;
        }

        public string Name { get; }
        public string Description { get; }
        public RecordType Fields { get; }
        public JobOptions Options { get; }
        internal MarkupElement Handle { get; }
        internal MarkupElement Failed { get; }
        internal Scope Closure { get; }
        internal Module Module { get; }

        /// <summary>Where it was declared, so reaching it can be recorded: the job's handle (or failed) method, inside its declaration.</summary>
        Module KScript.Runtime.Dependencies.IDeclaredSource.DeclaredIn => (Handle ?? Failed) == null ? null : Module;
        int KScript.Runtime.Dependencies.IDeclaredSource.DeclaredAt => (Handle ?? Failed)?.Span.Start ?? 0;

        /// <summary>The file the job is declared in, which a worker loads to run it.</summary>
        public string File => Module?.Path;

        public string TypeName => KScript.Metadata.KType.JobType;

        public string ToText() => Name;

        public override string ToString() => Name;

        /// <summary>Makes a job from its fields, given by name: <c>SendWelcome(user_id: 7, email: 'joan@example.com')</c>.</summary>
        public object Call(CallContext call)
        {
            if (call.Args.Count > 0)
                throw new KScriptException($"{Name}() takes its fields by name, such as {Name}({Example()}).", "Argument");
            var data = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> pair in call.Named) data[pair.Key] = pair.Value;
            return Make(call.Interpreter, data);
        }

        /// <summary>A job of this type with these fields, checked and converted to the fields' types.</summary>
        public JobValue Make(Interpreter interpreter, Dictionary<string, object> data) =>
            new JobValue(this, (Record)Shapes.MapAs(interpreter, data, Fields, $"{Name}'s fields"));

        private string Example() => Fields.Fields.Count == 0 ? string.Empty : string.Join(", ", Fields.Fields.Take(2).Select(f => f.Name + ": ..."));

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "name" => Name,
                "description" => Description,
                "queue" => Options.Queue,
                "connection" => Options.Connection,
                "tries" => (double)Options.Tries,
                "timeout" => Options.Timeout,
                "fields" => Fields.Fields.Select(f => (object)f.Name).ToList(),
                _ => null,
            };
            return value != null || name is "description" or "connection";
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        /// <summary>Runs <c>fn handle()</c> for a job, with its fields (and $this and $job) as variables.</summary>
        internal object RunHandle(Interpreter interpreter, JobValue job, Dictionary<string, object> context)
        {
            handles.Value++;
            try
            {
                return RunMethod(interpreter, Handle, "handle", job, context, Array.Empty<object>());
            }
            finally
            {
                handles.Value--;
            }
        }

        private static readonly AsyncLocal<int> handles = new AsyncLocal<int>();

        /// <summary>True while a job's handle runs here: @release_job() and @fail_job() only mean something there.</summary>
        internal static bool InHandle => handles.Value > 0;

        /// <summary>Runs <c>fn failed(err)</c> when a job has used up its tries; nothing happens without one.</summary>
        internal void RunFailed(Interpreter interpreter, JobValue job, Dictionary<string, object> context, KScriptException error)
        {
            if (Failed == null) return;
            RunMethod(interpreter, Failed, "failed", job, context, new object[] { error.ToValue() });
        }

        private object RunMethod(Interpreter interpreter, MarkupElement method, string name, JobValue job, Dictionary<string, object> context, object[] args)
        {
            KScript.Runtime.Dependencies.DependencyTrace.Reached(Module, method.Span.Start);
            Scope scope = Closure.Child();
            foreach (KeyValuePair<string, object> field in job.Fields) scope.Declare(field.Key, field.Value);
            scope.Declare("this", job);
            scope.Declare("job", context);
            interpreter.DeclareFunction(method, scope, Module);
            scope.TryGet(name, out object fn);
            ICallable callable = (ICallable)fn;
            int wanted = method.GetAttributeValue("params") is string p && p.Trim().Length > 0 ? Parameter.SplitTopLevel(p, ',').Count(x => x.Trim().Length > 0) : 0;
            return interpreter.Call(callable, args.Take(wanted).ToList(), null, scope, Module);
        }
    }

    /// <summary>A job: its type and its fields. <c>->dispatch()</c> queues it and <c>->run()</c> runs it here.</summary>
    public sealed class JobValue : IKObject
    {
        internal JobValue(JobType type, Record fields)
        {
            Type = type;
            Fields = fields;
        }

        public JobType Type { get; }

        public Record Fields { get; }

        public string TypeName => KScript.Metadata.KType.Job;

        public string ToText() =>
            Type.Name + "(" + string.Join(", ", Fields.Select(f => f.Key + ": " + (f.Value is string s ? "'" + s.Replace("'", "\\'") + "'" : f.Value == null ? "null" : Values.ToStr(f.Value)))) + ")";

        public override string ToString() => ToText();

        /// <summary>A field by name; then .job (the job type's name) and .fields (all of them), unless fields have those names.</summary>
        public bool TryGetMember(string name, out object value)
        {
            if (Fields.TryGetValue(name, out value)) return true;
            switch (name)
            {
                case "job":
                    value = Type.Name;
                    return true;
                case "fields":
                    value = new Dictionary<string, object>(Fields, StringComparer.Ordinal);
                    return true;
                default:
                    return false;
            }
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "dispatch":
                    Dispatcher.NeedsImport(call, "->dispatch()");
                    result = Dispatcher.Dispatch(call, new[] { this }, DispatchOptions.From(call, 0));
                    return true;
                case "run":
                    result = Dispatcher.RunHere(call.Interpreter, this);
                    return true;
                default:
                    result = null;
                    return false;
            }
        }
    }

    /// <summary>Jobs that run one after another: each starts when the one before it succeeds.</summary>
    public sealed class ChainValue : IKObject
    {
        internal ChainValue(List<JobValue> jobs) => Jobs = jobs;

        public List<JobValue> Jobs { get; }

        public string TypeName => KScript.Metadata.KType.Chain;

        public string ToText() => "chain [" + string.Join(", ", Jobs.Select(j => j.Type.Name)) + "]";

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "jobs" => Jobs.Cast<object>().ToList(),
                _ => null,
            };
            return value != null;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "dispatch":
                    Dispatcher.NeedsImport(call, "->dispatch()");
                    result = Dispatcher.Dispatch(call, Jobs, DispatchOptions.From(call, 0));
                    return true;
                case "count":
                    result = (double)Jobs.Count;
                    return true;
                case "run":
                    foreach (JobValue job in Jobs) Dispatcher.RunHere(call.Interpreter, job);
                    result = null;
                    return true;
                default:
                    result = null;
                    return false;
            }
        }
    }

    /// <summary>What <c>->dispatch()</c> may change: when the job runs, and where it goes.</summary>
    internal sealed class DispatchOptions
    {
        public double? Delay;
        public DateTime? At;
        public string Queue;
        public string Connection;

        public static readonly string[] Names = { "delay", "at", "queue", "connection" };

        /// <summary>The error for a dispatch given both a delay and a time: (delay: duration | at: date)? is one group.</summary>
        public static readonly string BothTimes = KScript.Syntax.ParameterList.GroupProblem("->dispatch()", new[] { "delay", "at" }, optional: true, passed: 2);

        public static DispatchOptions From(CallContext call, int first)
        {
            var options = new DispatchOptions();
            foreach (KeyValuePair<string, object> pair in call.Named)
            {
                if (!Names.Contains(pair.Key))
                    throw new KScriptException($"->dispatch() has no option '{pair.Key}'. It takes {string.Join(", ", Names.Select(n => n + ":"))}.", "Argument");
            }
            object delay = call.Arg(first, "delay");
            object at = call.Arg(first + 1, "at");
            if (delay != null && at != null)
                throw new KScriptException(BothTimes, "Argument");
            if (delay != null) options.Delay = Durations.Seconds(delay, "delay");
            if (at != null)
            {
                options.At = at switch
                {
                    DateTime d => d.ToUniversalTime(),
                    string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal, out DateTime parsed) => parsed,
                    _ => throw new KScriptException($"at: should be a date, such as @now()->add(hours: 1), not {Values.Describe(at)}.", "Argument"),
                };
            }
            if (call.Arg(first + 2, "queue") is object q) options.Queue = NonEmpty("queue", Values.ToStr(q));
            if (call.Arg(first + 3, "connection") is object c) options.Connection = NonEmpty("connection", Values.ToStr(c));
            return options;
        }

        /// <summary>A queue or connection name given to dispatch: a job's own options refuse an empty one too.</summary>
        private static string NonEmpty(string option, string value) =>
            string.IsNullOrWhiteSpace(value) ? throw new KScriptException($"->dispatch() {option}: can't be empty.", "Argument") : value.Trim();
    }

    /// <summary>@release_job(after:) in a handle: put the job back without using up a try.</summary>
    public sealed class JobReleaseSignal : ControlSignal
    {
        public JobReleaseSignal(double delay) : base("@release_job() outside a job.") => Delay = delay;

        public double Delay { get; }
    }

    /// <summary>@fail_job(err) in a handle: fail the job now, without more tries.</summary>
    public sealed class JobFailSignal : ControlSignal
    {
        public JobFailSignal(KScriptException error) : base("@fail_job() outside a job.") => Error = error;

        public KScriptException Error { get; }
    }
}
