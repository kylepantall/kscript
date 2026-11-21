using System.Diagnostics;
using System.Globalization;

using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>What happened to one attempt at a job.</summary>
    public enum JobOutcome
    {
        Done,
        Retrying,
        Released,
        Failed,
    }

    /// <summary>The result of one attempt: what happened, the error if any, and when a retry is due.</summary>
    public sealed class AttemptResult
    {
        public JobOutcome Outcome { get; init; }
        public KScriptException Error { get; init; }
        public DateTime? RetryAt { get; init; }
        public TimeSpan Elapsed { get; init; }
    }

    /// <summary>Settings for <see cref="Worker"/>: <c>kscript worker</c>'s options.</summary>
    public sealed class WorkerOptions
    {
        /// <summary>The connection to work; null for queue.default.</summary>
        public string Connection { get; set; }

        /// <summary>The queues to check, first to last; empty for queue.queues.</summary>
        public List<string> Queues { get; set; } = new List<string>();

        public int Concurrency { get; set; }

        /// <summary>Seconds to wait when every queue is empty.</summary>
        public double? Sleep { get; set; }

        /// <summary>Stop after this many jobs (0: no limit).</summary>
        public int MaxJobs { get; set; }

        /// <summary>Stop after running this long, in seconds (0: no limit).</summary>
        public double MaxTime { get; set; }

        /// <summary>Stop once the queues are empty, instead of waiting for more jobs.</summary>
        public bool StopWhenEmpty { get; set; }

        /// <summary>Run at most one job, then stop.</summary>
        public bool Once { get; set; }

        /// <summary>Makes the host each job runs with; tests use it to capture output.</summary>
        public Func<KScriptHost> Host { get; set; }
    }

    /// <summary>
    /// Runs queued jobs until it is stopped: <c>kscript worker</c> and <c>kscript serve --with-worker</c>. Each job runs
    /// in a fresh interpreter, as each request does in kscript serve, with a time limit of its timeout. Stopping lets
    /// the jobs that are running finish.
    /// </summary>
    public sealed class Worker
    {
        private readonly QueueContext context;
        private readonly WorkerOptions options;
        private readonly TextWriter log;
        private int started;

        public Worker(QueueContext context, WorkerOptions options, TextWriter log)
        {
            this.context = context;
            this.options = options;
            this.log = log;
        }

        /// <summary>Jobs that finished, for --max-jobs and the summary.</summary>
        public int Processed => processed;

        private int processed;

        private int failures;

        /// <summary>The queues this worker checks, first to last; null when it checks every queue (default first).</summary>
        public IReadOnlyList<string> Queues => options.Queues.Count > 0 ? options.Queues : context.Settings.QueuesListed ? context.Settings.Queues : null;

        /// <summary>What it checks, for the log: "high, default" or "every queue".</summary>
        public string QueuesText => Queues == null ? "every queue, " + string.Join(", ", context.Settings.Queues) + " first" : string.Join(", ", Queues);

        private IReadOnlyList<string> QueuesNow(IQueueDriver driver) =>
            Queues ?? context.Settings.Queues.Concat(driver.QueueNames()).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>Runs until <paramref name="stop"/> is cancelled or a limit is reached. Returns the number of jobs that failed.</summary>
        public int Run(CancellationToken stop)
        {
            IQueueDriver driver = context.Driver(options.Connection);
            if (driver == null) throw new KScriptException("The sync connection runs jobs as they are dispatched, so there is nothing for a worker to do. Use a file, database or memory connection.", "Config");
            int concurrency = options.Once ? 1 : options.Concurrency > 0 ? options.Concurrency : context.Settings.WorkerConcurrency;
            double sleep = options.Sleep ?? context.Settings.WorkerSleep;
            var clock = Stopwatch.StartNew();
            driver.Prune(QueueRuntime.Clock().AddSeconds(-context.Settings.FailedKeep));

            int limit = options.Once ? 1 : options.MaxJobs;
            bool Stopping() => stop.IsCancellationRequested || (options.MaxTime > 0 && clock.Elapsed.TotalSeconds >= options.MaxTime);

            void Loop()
            {
                while (!Stopping())
                {
                    // Take a place among the jobs this worker may start before taking a job, so threads never start more than the limit.
                    if (Interlocked.Increment(ref started) > limit && limit > 0)
                    {
                        Interlocked.Decrement(ref started);
                        return;
                    }
                    QueuedJob job;
                    try
                    {
                        job = driver.Reserve(QueuesNow(driver), QueueRuntime.Clock());
                    }
                    catch (Exception ex) when (QueueRuntime.StoreBusy(ex))
                    {
                        // Another worker has the files (or the database is busy or down) for now: try again after a pause.
                        Interlocked.Decrement(ref started);
                        Line($"Couldn't read the queue ({ex.Message}); trying again.");
                        stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(0.05, sleep)));
                        continue;
                    }
                    if (job != null)
                    {
                        try
                        {
                            Process(driver, job);
                        }
                        catch (Exception ex) when (QueueRuntime.StoreBusy(ex))
                        {
                            // The job stays reserved and goes back on its queue when its lease runs out.
                            Line($"{job.Job} #{Short(job.Id)}: couldn't save what happened ({ex.Message}); it will run again after its lease runs out.");
                        }
                        continue;
                    }
                    Interlocked.Decrement(ref started);
                    if (options.StopWhenEmpty || options.Once) return;
                    stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(sleep));
                }
            }

            var threads = Enumerable.Range(0, concurrency).Select(i => new Thread(Loop, 64 * 1024 * 1024) { IsBackground = true, Name = "kscript worker " + (i + 1) }).ToList();
            foreach (Thread t in threads) t.Start();
            foreach (Thread t in threads) t.Join();
            return failures;
        }

        /// <summary>Runs one reserved job and records what happened in its driver.</summary>
        internal AttemptResult Process(IQueueDriver driver, QueuedJob job)
        {
            Line($"{job.Job} #{Short(job.Id)} on {job.Queue}: running (attempt {job.Attempts} of {job.Tries})");
            AttemptResult result = JobRunner.Attempt(context, job, options.Host);
            Interlocked.Increment(ref processed);
            string took = $"{result.Elapsed.TotalMilliseconds:0} ms";
            switch (result.Outcome)
            {
                case JobOutcome.Done:
                    driver.Complete(job);
                    if (job.UniqueKey != null) driver.Unlock(job.UniqueKey);
                    DispatchChain(driver, job);
                    Line($"{job.Job} #{Short(job.Id)}: done ({took})");
                    break;
                case JobOutcome.Released:
                    job.Attempts = Math.Max(0, job.Attempts - 1);
                    job.AvailableAt = result.RetryAt ?? QueueRuntime.Clock();
                    driver.Release(job);
                    Line($"{job.Job} #{Short(job.Id)}: released, back in {Durations.Describe(Math.Max(0, (job.AvailableAt - QueueRuntime.Clock()).TotalSeconds))} ({took})");
                    break;
                case JobOutcome.Retrying:
                    job.LastError = ErrorInfo(result.Error);
                    job.AvailableAt = result.RetryAt ?? QueueRuntime.Clock();
                    driver.Release(job);
                    Line($"{job.Job} #{Short(job.Id)}: {result.Error.Type}: {result.Error.Message} (retrying in {Durations.Describe(Math.Max(0, (job.AvailableAt - QueueRuntime.Clock()).TotalSeconds))}, {took})");
                    break;
                default:
                    Interlocked.Increment(ref failures);
                    job.LastError = ErrorInfo(result.Error);
                    job.FailedAt = QueueRuntime.Clock();
                    driver.Fail(job);
                    if (job.UniqueKey != null) driver.Unlock(job.UniqueKey);
                    Line($"{job.Job} #{Short(job.Id)}: failed: {result.Error.Type}: {result.Error.Message} ({took})");
                    break;
            }
            return result;
        }

        /// <summary>After a job in a chain succeeds, the next one is queued with the rest of the chain.</summary>
        private static void DispatchChain(IQueueDriver driver, QueuedJob job)
        {
            if (job.Chain.Count == 0) return;
            QueuedJob next = QueuedJob.FromMap(job.Chain[0]);
            next.Chain = job.Chain.Skip(1).ToList();
            next.DispatchedAt = QueueRuntime.Clock();
            next.AvailableAt = next.DispatchedAt;
            driver.Push(next);
        }

        internal static Dictionary<string, object> ErrorInfo(KScriptException error)
        {
            Dictionary<string, object> map = error.ToValue();
            return map;
        }

        private static string Short(string id) => id != null && id.Length > 8 ? id.Substring(id.Length - 8) : id;

        private readonly object logGate = new object();

        private void Line(string text)
        {
            if (log == null) return;
            lock (logGate) log.WriteLine(QueueRuntime.Clock().ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + text);
        }
    }

    /// <summary>Runs one attempt at a queued job in a fresh interpreter.</summary>
    internal static class JobRunner
    {
        public static AttemptResult Attempt(QueueContext context, QueuedJob job, Func<KScriptHost> makeHost = null)
        {
            var clock = Stopwatch.StartNew();
            KScriptHost host = makeHost?.Invoke() ?? KScriptHost.Console();
            host.WorkingDirectory = context.Directory ?? host.WorkingDirectory;
            host.Config = context.Project?.ConfigOptions(context.Environment);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(0.001, job.Timeout)));
            var options = new InterpreterOptions { Cancellation = timeout.Token };
            if (context.Project != null) options.DefaultPackages.AddRange(context.Project.Packages);
            var interpreter = new Interpreter(host, options: options);

            JobType type;
            JobValue value;
            var jobContext = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = job.Id,
                ["name"] = job.Job,
                ["attempt"] = (double)job.Attempts,
                ["tries"] = (double)job.Tries,
                ["queue"] = job.Queue,
                ["connection"] = job.Connection,
                ["dispatched_at"] = job.DispatchedAt,
            };
            try
            {
                string file = context.FullPath(job.File);
                if (file == null || !File.Exists(file))
                    return Failed(new KScriptException($"The file that declares {job.Job} ({job.File}) isn't there any more.", "FileNotFound"), clock);
                Module module = interpreter.Load(file);
                interpreter.Run(module);
                if (!module.Exports.TryGetValue(job.Job, out object exported) || exported is not JobType declared)
                    return Failed(new KScriptException($"{job.File} doesn't declare a job called {job.Job} any more.", "Name"), clock);
                type = declared;
                value = type.Make(interpreter, job.Fields);
            }
            catch (KScriptException ex)
            {
                return Failed(ex, clock);
            }

            KScriptException error;
            try
            {
                type.RunHandle(interpreter, value, jobContext);
                interpreter.Host.FlushLogs();
                return new AttemptResult { Outcome = JobOutcome.Done, Elapsed = clock.Elapsed };
            }
            catch (JobReleaseSignal release)
            {
                return new AttemptResult { Outcome = JobOutcome.Released, RetryAt = QueueRuntime.Clock().AddSeconds(release.Delay), Elapsed = clock.Elapsed };
            }
            catch (JobFailSignal fail)
            {
                error = fail.Error;
                return Final(interpreter, type, value, jobContext, error, clock);
            }
            catch (Exception ex) when (timeout.IsCancellationRequested && ex is KScriptException or OperationCanceledException)
            {
                error = new KScriptException($"{job.Job} ran for more than {Durations.Describe(job.Timeout)}, its timeout.", "JobTimeout", ex);
            }
            catch (KScriptException ex)
            {
                error = ex;
            }
            interpreter.Host.FlushLogs();

            if (ShouldRetry(job, error, QueueRuntime.Clock()))
            {
                return new AttemptResult { Outcome = JobOutcome.Retrying, Error = error, RetryAt = QueueRuntime.Clock().AddSeconds(job.Options.BackoffAfter(job.Attempts)), Elapsed = clock.Elapsed };
            }
            return Final(interpreter, type, value, jobContext, error, clock);
        }

        /// <summary>The job has failed for good: its <c>fn failed(err)</c> runs, with a JobFailed error whose cause is the last error.</summary>
        private static AttemptResult Final(Interpreter interpreter, JobType type, JobValue value, Dictionary<string, object> jobContext, KScriptException error, Stopwatch clock)
        {
            var failed = new KScriptException($"{type.Name} failed: {error.Message}", "JobFailed", error);
            try
            {
                type.RunFailed(interpreter, value, jobContext, failed);
            }
            catch (Exception ex) when (ex is KScriptException or ControlSignal)
            {
                interpreter.Host.Error.WriteLine($"{type.Name}'s fn failed() stopped with an error: {(ex is KScriptException k ? k.ToString() : ex.Message)}");
            }
            interpreter.Host.FlushLogs();
            return new AttemptResult { Outcome = JobOutcome.Failed, Error = error, Elapsed = clock.Elapsed };
        }

        private static AttemptResult Failed(KScriptException error, Stopwatch clock) =>
            new AttemptResult { Outcome = JobOutcome.Failed, Error = error, Elapsed = clock.Elapsed };

        /// <summary>True when the error is worth another try: tries are left, retry_until hasn't passed, and its type retries.</summary>
        public static bool ShouldRetry(QueuedJob job, KScriptException error, DateTime now)
        {
            if (job.Attempts >= job.Tries) return false;
            if (job.RetryUntil != null && now >= job.RetryUntil) return false;
            if (job.FailOn.Any(t => ErrorTypes.Matches(error, t))) return false;
            return job.RetryOn.Count == 0 || job.RetryOn.Any(t => ErrorTypes.Matches(error, t));
        }
    }
}
