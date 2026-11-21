using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>Queues jobs: <c>dispatch Job(...)</c>, <c>->dispatch()</c> and chains.</summary>
    internal static class Dispatcher
    {
        /// <summary>
        /// Queues the first job, with the rest as its chain, and returns what was queued: id, job, queue, connection,
        /// available_at. A unique job that is already queued returns null.
        /// </summary>
        public static object Dispatch(CallContext call, IReadOnlyList<JobValue> jobs, DispatchOptions options) =>
            Dispatch(call.Interpreter, call.Module, jobs, options);

        public static object Dispatch(Interpreter interpreter, Module module, IReadOnlyList<JobValue> jobs, DispatchOptions options)
        {
            if (jobs.Count == 0) throw new KScriptException("@chain() needs at least one job.", "Argument");
            if (interpreter.Host.Items.TryGetValue(QueueRuntime.FakeKey, out object f) && f is FakeQueue fake)
            {
                return fake.Record(jobs, options);
            }

            QueueContext context = QueueContext.For(interpreter, module);
            JobValue first = jobs[0];
            string connection = options.Connection ?? first.Type.Options.Connection;
            IQueueDriver driver = context.Driver(connection);
            if (driver == null)
            {
                // sync: run each job now, here, in order.
                foreach (JobValue job in jobs) RunHere(interpreter, job);
                return null;
            }

            DateTime now = QueueRuntime.Clock();
            QueuedJob queued = Build(context, first, options.Queue, driver.Name, now);
            queued.AvailableAt = options.At ?? (options.Delay is double delay ? now.AddSeconds(delay) : now);
            if (queued.AvailableAt < now) queued.AvailableAt = now;
            foreach (JobValue next in jobs.Skip(1)) queued.Chain.Add(Build(context, next, options.Queue ?? (next.Type.Options.Queue != "default" ? next.Type.Options.Queue : null), driver.Name, now).ToMap());

            if (first.Type.Options.UniqueFor is double uniqueFor)
            {
                queued.UniqueKey = QueueRuntime.UniqueKey(first);
                if (!driver.Lock(queued.UniqueKey, queued.AvailableAt.AddSeconds(uniqueFor), now)) return null;
            }
            driver.Push(queued);
            return Describe(queued);
        }

        /// <summary>What a script gets back from a dispatch.</summary>
        public static Dictionary<string, object> Describe(QueuedJob queued) => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = queued.Id,
            ["job"] = queued.Job,
            ["queue"] = queued.Queue,
            ["connection"] = queued.Connection,
            ["dispatched_at"] = queued.DispatchedAt,
            ["available_at"] = queued.AvailableAt,
        };

        /// <summary>A job as a queue keeps it, with its type's options.</summary>
        public static QueuedJob Build(QueueContext context, JobValue job, string queue, string connection, DateTime now)
        {
            if (job.Type.File == null)
                throw new KScriptException($"{job.Type.Name} isn't declared in a file, so a worker couldn't load it. Declare jobs in a script file, such as jobs/{job.Type.Name}.kscript.", "Context");
            JobOptions o = job.Type.Options;
            return new QueuedJob
            {
                Id = QueuedJob.NewId(),
                Job = job.Type.Name,
                File = context.StoredPath(job.Type.File),
                Queue = queue ?? o.Queue,
                Connection = connection,
                Fields = QueueRuntime.Data(job),
                Tries = o.Tries,
                Backoff = o.Backoff,
                Timeout = o.Timeout,
                RetryUntil = o.RetryUntil is double until ? now.AddSeconds(until) : null,
                RetryOn = o.RetryOn,
                FailOn = o.FailOn,
                DispatchedAt = now,
                AvailableAt = now,
            };
        }

        /// <summary>Runs a job's handle now, in this run: <c>->run()</c> and the sync connection. Errors reach the caller.</summary>
        public static object RunHere(Interpreter interpreter, JobValue job)
        {
            var context = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = null,
                ["name"] = job.Type.Name,
                ["attempt"] = 1.0,
                ["tries"] = (double)job.Type.Options.Tries,
                ["queue"] = job.Type.Options.Queue,
                ["connection"] = "sync",
                ["dispatched_at"] = QueueRuntime.Clock(),
            };
            try
            {
                return job.Type.RunHandle(interpreter, job, context);
            }
            catch (JobReleaseSignal)
            {
                // Outside a worker there is nowhere to put it back to; releasing just ends this run.
                return null;
            }
            catch (JobFailSignal failed)
            {
                throw failed.Error;
            }
        }
    }
}
