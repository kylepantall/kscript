using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>
    /// @fake_queue(): from then on in the run, dispatched jobs are kept here instead of queued or run, so a test can
    /// check what was dispatched (<c>$queue->pushed(SendWelcome)</c>) and then run them (<c>$queue->run_all()</c>).
    /// </summary>
    public sealed class FakeQueue : IKObject
    {
        private readonly List<(JobValue Job, Dictionary<string, object> Queued)> pushed = new List<(JobValue, Dictionary<string, object>)>();
        private int next;

        public string TypeName => KScript.Metadata.KType.FakeQueue;

        public string ToText() => $"fake queue ({pushed.Count} pushed)";

        internal object Record(IReadOnlyList<JobValue> jobs, DispatchOptions options)
        {
            Dictionary<string, object> first = null;
            foreach (JobValue job in jobs)
            {
                var queued = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = "fake-" + (++next),
                    ["job"] = job.Type.Name,
                    ["queue"] = options.Queue ?? job.Type.Options.Queue,
                    ["connection"] = options.Connection ?? job.Type.Options.Connection ?? "fake",
                    ["dispatched_at"] = QueueRuntime.Clock(),
                    ["available_at"] = options.At ?? (options.Delay is double d ? QueueRuntime.Clock().AddSeconds(d) : QueueRuntime.Clock()),
                    ["delay"] = options.Delay,
                };
                pushed.Add((job, queued));
                first ??= queued;
            }
            return first;
        }

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            switch (name)
            {
                case "pushed":
                    result = Matching(call).Select(p => (object)p.Job).ToList();
                    return true;
                case "dispatches":
                    result = Matching(call).Select(p => (object)new Dictionary<string, object>(p.Queued, StringComparer.Ordinal)).ToList();
                    return true;
                case "count":
                    if (call.Count > 0) throw new KScriptException("->count() takes no arguments. To count the jobs of one type, use ->count_of(SendWelcome).", "Argument");
                    result = (double)pushed.Count;
                    return true;
                case "count_of":
                    result = (double)Matching(call).Count();
                    return true;
                case "run_all":
                {
                    // Jobs that dispatch more jobs add them to the end, and they run too.
                    int ran = 0;
                    while (pushed.Count > 0)
                    {
                        (JobValue job, _) = pushed[0];
                        pushed.RemoveAt(0);
                        Dispatcher.RunHere(call.Interpreter, job);
                        ran++;
                        if (ran > 10_000) throw new KScriptException("run_all() ran 10,000 jobs: the jobs keep dispatching more.", "Limit");
                    }
                    result = (double)ran;
                    return true;
                }
                case "clear":
                    result = (double)pushed.Count;
                    pushed.Clear();
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        /// <summary>The pushed jobs, or only those of the job type given as the first argument.</summary>
        private IEnumerable<(JobValue Job, Dictionary<string, object> Queued)> Matching(CallContext call)
        {
            object filter = call.Arg(0);
            return filter switch
            {
                null => pushed,
                JobType type => pushed.Where(p => p.Job.Type == type || p.Job.Type.Name == type.Name),
                string text => pushed.Where(p => p.Job.Type.Name == text),
                _ => throw new KScriptException($"pushed() takes a job type, such as pushed(SendWelcome), not {Values.Describe(filter)}.", "Argument"),
            };
        }
    }

    /// <summary>@queue(name): one queue of a connection, to look at or empty.</summary>
    public sealed class QueueHandle : IKObject
    {
        internal QueueHandle(QueueContext context, string name, string connection)
        {
            Context = context;
            Name = name;
            Connection = connection ?? context.DefaultConnection;
        }

        internal QueueContext Context { get; }

        public string Name { get; }

        public string Connection { get; }

        public string TypeName => KScript.Metadata.KType.Queue;

        public string ToText() => $"queue '{Name}' on {Connection}";

        private IQueueDriver Driver => Context.Driver(Connection);

        public bool TryGetMember(string name, out object value)
        {
            value = name switch
            {
                "name" => Name,
                "connection" => Connection,
                _ => null,
            };
            return value != null;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            IQueueDriver driver = Driver;
            switch (name)
            {
                case "size":
                    result = (double)(driver?.Waiting(Name).Count ?? 0);
                    return true;
                case "waiting":
                    result = (driver?.Waiting(Name) ?? new List<QueuedJob>()).Select(j => (object)j.ToValue()).ToList();
                    return true;
                case "failed":
                    result = (driver?.Failed() ?? new List<QueuedJob>()).Where(j => j.Queue == Name).Select(j => (object)j.ToValue()).ToList();
                    return true;
                case "clear":
                    call.Host.CheckWrite("Clearing a queue");
                    result = (double)(driver?.Clear(Name) ?? 0);
                    return true;
                case "retry":
                    call.Host.CheckWrite("Retrying a failed job");
                    result = driver?.Retry(call.Str(0)) ?? false;
                    return true;
                default:
                    result = null;
                    return false;
            }
        }
    }
}
