using System.Globalization;

using KScript.Output;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Queueing
{
    /// <summary>
    /// <c>kscript worker</c> (runs queued jobs until stopped) and <c>kscript queue status|list|failed|retry|forget|clear</c>
    /// (looks after the queues). Both work on the project of the current folder, or of --project.
    /// </summary>
    public static class QueueCommand
    {
        public const string WorkerUsage = "kscript worker [--connection name] [--queue a,b] [--concurrency n] [--once] [--stop-when-empty] [--max-jobs n] [--max-time 1h] [--sleep 1s] [--env name]";

        public const string QueueUsage = "kscript queue status|list [queue]|failed|retry <id>|--all|forget <id>|--all|clear [queue] [--connection name] [--format json]";

        /// <summary>kscript worker. Returns 0, or 1 when a job failed with --stop-when-empty or --once, so scripts can tell.</summary>
        public static int RunWorker(string[] args, TextWriter output, TextWriter error, CancellationToken stop, string workingDirectory = null, string environment = null, Func<KScriptHost> host = null)
        {
            var options = new WorkerOptions { Host = host };
            string project = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Value() => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
                switch (arg)
                {
                    case "--connection":
                    case "-c":
                        options.Connection = Value();
                        if (options.Connection == null) return Fail(error, "--connection needs a name, e.g. --connection file");
                        break;
                    case "--queue":
                    case "-q":
                        string queues = Value();
                        if (queues == null) return Fail(error, "--queue needs queue names, first checked first, e.g. --queue high,default");
                        options.Queues = queues.Split(',').Select(q => q.Trim()).Where(q => q.Length > 0).ToList();
                        break;
                    case "--concurrency":
                        if (!int.TryParse(Value(), out int n) || n < 1 || n > 64) return Fail(error, "--concurrency needs a number from 1 to 64, e.g. --concurrency 4");
                        options.Concurrency = n;
                        break;
                    case "--max-jobs":
                        if (!int.TryParse(Value(), out int max) || max < 1) return Fail(error, "--max-jobs needs a number, e.g. --max-jobs 500");
                        options.MaxJobs = max;
                        break;
                    case "--max-time":
                        if (ProjectConfig.Seconds(Value()) is not double seconds) return Fail(error, "--max-time needs a time, e.g. --max-time 1h");
                        options.MaxTime = seconds;
                        break;
                    case "--sleep":
                        string sleep = Value();
                        if (sleep == "0") options.Sleep = 0.05;
                        else if (ProjectConfig.Seconds(sleep) is double s) options.Sleep = s;
                        else return Fail(error, "--sleep needs a time, e.g. --sleep 1s");
                        break;
                    case "--once":
                        options.Once = true;
                        break;
                    case "--stop-when-empty":
                        options.StopWhenEmpty = true;
                        break;
                    case "--project":
                        project = Value();
                        if (project == null) return Fail(error, "--project needs a folder");
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: " + WorkerUsage);
                        output.WriteLine("Runs queued jobs until Ctrl+C. Stopping lets running jobs finish. Settings: queue in kscript.json.");
                        return 0;
                    default:
                        return Fail(error, $"Unknown option '{arg}'. Usage: {WorkerUsage}");
                }
            }

            QueueContext context = Context(project ?? workingDirectory ?? KScript.Runtime.WorkingFolder.Current, environment, error);
            if (context == null) return 1;
            var worker = new Worker(context, options, output);
            try
            {
                QueueSettings.Connection connection = context.Connection(options.Connection);
                if (connection.Driver == "memory")
                    new CliOutput(error).Warn("The memory connection only holds jobs dispatched in this process, so this worker won't see jobs from other runs. Use kscript serve --with-worker, or a file connection.");
                if (!options.Once && !options.StopWhenEmpty)
                {
                    var cli = new CliOutput(output);
                    cli.NewLine();
                    cli.Info($"Processing jobs from the [{connection.Name}] connection ({connection.Driver}), queues [{worker.QueuesText}].\nCtrl+C stops it after the running jobs finish.");
                    cli.NewLine();
                }
                int failures = worker.Run(stop);
                if (options.Once || options.StopWhenEmpty)
                    new CliOutput(output).Label(failures > 0 ? "WARN" : "DONE", failures > 0 ? CliOutput.WarnCode : CliOutput.DoneCode, $"Ran {worker.Processed} job{(worker.Processed == 1 ? string.Empty : "s")}" + (failures > 0 ? $", {failures} failed." : "."));
                return failures > 0 && (options.Once || options.StopWhenEmpty) ? 1 : 0;
            }
            catch (KScriptException ex)
            {
                return Fail(error, ex);
            }
        }

        /// <summary>kscript queue ...</summary>
        public static int RunQueue(string[] args, TextWriter output, TextWriter error, string workingDirectory = null, string environment = null)
        {
            string connectionName = null;
            string project = null;
            bool json = false;
            bool all = false;
            var rest = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--connection":
                    case "-c":
                        if (i + 1 >= args.Length) return Fail(error, "--connection needs a name, e.g. --connection file");
                        connectionName = args[++i];
                        break;
                    case "--project":
                        if (i + 1 >= args.Length) return Fail(error, "--project needs a folder");
                        project = args[++i];
                        break;
                    case "--format":
                        if (i + 1 >= args.Length || args[i + 1] is not ("json" or "text")) return Fail(error, "--format needs json or text");
                        json = args[++i] == "json";
                        break;
                    case "--all":
                        all = true;
                        break;
                    case "--help":
                    case "-h":
                        output.WriteLine("Usage: " + QueueUsage);
                        return 0;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal)) return Fail(error, $"Unknown option '{args[i]}'. Usage: {QueueUsage}");
                        rest.Add(args[i]);
                        break;
                }
            }
            if (rest.Count == 0) return Fail(error, "Usage: " + QueueUsage);

            QueueContext context = Context(project ?? workingDirectory ?? KScript.Runtime.WorkingFolder.Current, environment, error);
            if (context == null) return 1;
            IQueueDriver driver;
            try
            {
                driver = context.Driver(connectionName);
            }
            catch (KScriptException ex)
            {
                return Fail(error, ex);
            }
            if (driver == null) return Fail(error, "The sync connection runs jobs as they are dispatched, so it has no queue to look at. Name another with --connection.");
            string target = rest.Count > 1 ? rest[1] : null;

            switch (rest[0])
            {
                case "status":
                {
                    List<QueuedJob> waiting = driver.Waiting(null);
                    List<QueuedJob> running = driver.Running();
                    List<QueuedJob> failed = driver.Failed();
                    IEnumerable<string> names = context.Settings.Queues.Concat(waiting.Select(j => j.Queue)).Concat(running.Select(j => j.Queue)).Concat(failed.Select(j => j.Queue)).Distinct();
                    DateTime now = QueueRuntime.Clock();
                    var rows = names.Select(q => new
                    {
                        Queue = q,
                        Waiting = waiting.Count(j => j.Queue == q && j.AvailableAt <= now),
                        Delayed = waiting.Count(j => j.Queue == q && j.AvailableAt > now),
                        Running = running.Count(j => j.Queue == q),
                        Failed = failed.Count(j => j.Queue == q),
                    }).ToList();
                    if (json)
                    {
                        output.WriteLine(Values.ToJson(new Dictionary<string, object>
                        {
                            ["connection"] = driver.Name,
                            ["driver"] = driver.Kind,
                            ["queues"] = rows.Select(r => (object)new Dictionary<string, object> { ["queue"] = r.Queue, ["waiting"] = (double)r.Waiting, ["delayed"] = (double)r.Delayed, ["running"] = (double)r.Running, ["failed"] = (double)r.Failed }).ToList(),
                        }, indented: true));
                        return 0;
                    }
                    var cli = new CliOutput(output);
                    cli.NewLine();
                    cli.TwoColumn(cli.Style.Bold("Connection"), $"{driver.Name} ({driver.Kind})");
                    if (driver is FileQueueDriver f) cli.TwoColumn(cli.Style.Bold("Folder"), f.Folder);
                    if (driver is DatabaseQueueDriver d) cli.TwoColumn(cli.Style.Bold("Database"), d.Where);
                    cli.NewLine();
                    string Count(int n, Func<string, string> colour) => n == 0 ? cli.Style.Gray("0") : colour(N(n));
                    cli.Table(new[] { "Queue", "Waiting", "Delayed", "Running", "Failed" },
                        rows.Select(r => new[] { r.Queue, Count(r.Waiting, cli.Style.Green), Count(r.Delayed, cli.Style.Yellow), Count(r.Running, cli.Style.Cyan), Count(r.Failed, cli.Style.Red) }),
                        new[] { CliAlign.Left, CliAlign.Right, CliAlign.Right, CliAlign.Right, CliAlign.Right });
                    return 0;
                }
                case "list":
                {
                    List<QueuedJob> jobs = driver.Waiting(target);
                    if (json)
                    {
                        output.WriteLine(Values.ToJson(jobs.Select(j => (object)j.ToValue()).ToList(), indented: true));
                        return 0;
                    }
                    if (jobs.Count == 0)
                    {
                        new CliOutput(output).Info(target == null ? "No jobs are waiting." : $"No jobs are waiting on [{target}].");
                        return 0;
                    }
                    new CliOutput(output).Table(new[] { "Id", "Job", "Queue", "Runs", "Fields" },
                        jobs.Select(j => new[] { j.Id, j.Job, j.Queue, j.AvailableAt <= QueueRuntime.Clock() ? "now" : When(j.AvailableAt), Fields(j) }));
                    return 0;
                }
                case "failed":
                {
                    List<QueuedJob> jobs = driver.Failed();
                    if (json)
                    {
                        output.WriteLine(Values.ToJson(jobs.Select(j => (object)j.ToValue()).ToList(), indented: true));
                        return 0;
                    }
                    if (jobs.Count == 0)
                    {
                        new CliOutput(output).Success("No failed jobs.");
                        return 0;
                    }
                    var failedOut = new CliOutput(output);
                    failedOut.Table(new[] { "Id", "Job", "Queue", "Failed", "Tries", "Error" },
                        jobs.Select(j => new[] { j.Id, j.Job, j.Queue, When(j.FailedAt ?? j.AvailableAt), j.Attempts + "/" + j.Tries, failedOut.Style.Red(Error(j)) }));
                    failedOut.NewLine();
                    failedOut.Info("kscript queue retry <id> (or --all) runs one again; kscript queue forget <id> deletes it.");
                    return 0;
                }
                case "retry":
                case "forget":
                {
                    bool retry = rest[0] == "retry";
                    if (!all && target == null) return Fail(error, $"kscript queue {rest[0]} needs a failed job's id (from kscript queue failed), or --all.");
                    List<string> ids = all ? driver.Failed().Select(j => j.Id).ToList() : new List<string> { target };
                    int done = ids.Count(id => retry ? driver.Retry(id) : driver.Forget(id));
                    if (!all && done == 0) return Fail(error, $"There is no failed job '{target}'. kscript queue failed lists them.");
                    new CliOutput(output).Success(retry ? $"{done} failed job{(done == 1 ? " is" : "s are")} back on {(done == 1 ? "its queue" : "their queues")}." : $"{done} failed job{(done == 1 ? string.Empty : "s")} deleted.");
                    return 0;
                }
                case "clear":
                {
                    int removed = driver.Clear(target);
                    new CliOutput(output).Success($"Removed {removed} waiting job{(removed == 1 ? string.Empty : "s")}" + (target != null ? $" from {target}." : "."));
                    return 0;
                }
                default:
                    return Fail(error, $"Unknown queue command '{rest[0]}'. Usage: {QueueUsage}");
            }
        }

        private static QueueContext Context(string directory, string environment, TextWriter error)
        {
            ProjectConfig project;
            try
            {
                project = ProjectConfig.FindAndLoad(directory);
            }
            catch (ProjectConfigException ex)
            {
                KScript.Runtime.ErrorReport.Write(error, ex);
                return null;
            }
            if (project != null)
            {
                foreach (string warning in project.Warnings) error.WriteLine($"{project.FilePath}: {warning}");
            }
            environment ??= project?.ConfigOptions(null).Environment ?? System.Environment.GetEnvironmentVariable("KSCRIPT_ENV");
            return new QueueContext(project, Path.GetFullPath(directory), environment);
        }

        private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

        private static string When(DateTime time)
        {
            double seconds = (time - QueueRuntime.Clock()).TotalSeconds;
            if (seconds > 1) return "in " + Durations.Describe(seconds);
            if (seconds < -60) return Durations.Describe(-seconds) + " ago";
            return "now";
        }

        private static string Fields(QueuedJob job)
        {
            string text = Values.ToJson(job.Fields);
            return text.Length > 60 ? text.Substring(0, 57) + "..." : text;
        }

        private static string Error(QueuedJob job)
        {
            if (job.LastError == null) return string.Empty;
            string type = job.LastError.TryGetValue("type", out object t) ? Values.ToStr(t) : "Error";
            string message = job.LastError.TryGetValue("message", out object m) ? Values.ToStr(m) : string.Empty;
            string text = type + ": " + message;
            return text.Length > 80 ? text.Substring(0, 77) + "..." : text;
        }

        private static int Fail(TextWriter error, Exception ex)
        {
            KScript.Runtime.ErrorReport.Write(error, ex);
            return 1;
        }

        private static int Fail(TextWriter error, string message)
        {
            new CliOutput(error).Error(message);
            return 1;
        }
    }
}
