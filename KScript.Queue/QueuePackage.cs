using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax.Markup;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Queueing
{
    /// <summary>
    /// KScript.Queue: <c>job</c> declarations, <c>dispatch</c>, <c>@chain</c>, <c>@queue</c>, <c>@release_job</c>,
    /// <c>@fail_job</c> and <c>@fake_queue</c>. <c>kscript worker</c> runs the queued jobs (see <see cref="Worker"/>).
    /// </summary>
    public sealed class QueuePackage : KScriptPackage
    {
        public override string Name => "KScript.Queue";

        public override string Description => "Queued jobs: job declarations, dispatch, retries with backoff, failed jobs, and the worker that runs them.";

        protected override void Register(PackageBuilder p)
        {
            p.Element(new ElementInfo("job", "Declares a job: work to do later, on a queue, by `kscript worker`. Its fields are the data it needs, typed like a <type>'s; `fn handle()` does the work and sees the fields as variables. `SendWelcome(user_id: 7)->dispatch()` queues one. An error in handle is retried after its backoff until its tries run out; then `fn failed(err)` runs and the job is kept with the failed jobs (`kscript queue failed`).",
                ContentKind.Statements,
                new A("name", K.DeclareVariable, "The job's name, starting with a capital letter: SendWelcome, BuildReport.", true),
                new A("queue", K.Text, "The queue it goes on, unless the dispatch names one. Defaults to default."),
                new A("connection", K.Text, "The connection in kscript.json's queue.connections it goes to, unless the dispatch names one. Defaults to queue.default."),
                new A("tries", K.Text, "Attempts before it counts as failed. Defaults to 3."),
                new A("backoff", K.Expression, "How long to wait before each retry: one time (30s), a list ([10s, 1m, 5m], the last one repeats), or 'exponential' (2s, 4s, 8s, up to 10m; the default).") { Type = Duration.TypeName },
                new A("timeout", K.Expression, "The longest one attempt may run, such as 30s or 5m. Past it the attempt stops with a JobTimeout error. Defaults to 60s.") { Type = Duration.TypeName },
                new A("retry_until", K.Expression, "How long after it was dispatched it may still be retried, such as 1h.") { Type = Duration.TypeName },
                new A("unique_for", K.Expression, "While a job with the same fields is waiting or running, another dispatch of it is dropped, for this long at most, such as 10m.") { Type = Duration.TypeName },
                new A("unique_by", K.Text, "The fields that make two dispatches the same for unique_for: [user_id]. Defaults to all of them."),
                new A("retry_on", K.ErrorType, "Only errors of these types are retried: [Timeout, Network]. Others fail the job at once."),
                new A("fail_on", K.ErrorType, "Errors of these types fail the job at once, without more tries: [Validation, NotFound]."),
                new A("fields", K.Fields, "The fields on one line, like fn params: `user_id: int, email: email`. Bigger jobs list them one per line."),
                new A("description", K.Text, "What the job does, shown on hover."))
            {
                Example = "<job name=\"SendWelcome\" queue=\"mail\" tries=\"5\" backoff=\"[10s, 1m]\">\n  <field name=\"email\" type=\"email\" />\n  <fn name=\"handle\">\n    <echo>Welcome, { $email }</echo>\n  </fn>\n</job>\n<do>SendWelcome(email: 'joan@example.com')->run()</do>",
                Agent = new AgentNotes(prefer: "Keep jobs small and idempotent: a retry runs handle again from the start."),
            }, run =>
            {
                JobType type = DeclareJob(run);
                run.Scope.Declare(type.Name, type, isConst: true);
                return null;
            });

            p.Command("chain", "jobs: list", "Jobs that run one after another: each starts when the one before it succeeds, and a failure stops the rest. `@chain([ImportCsv(path: $f), RebuildIndex()])->dispatch()`.",
                KType.Chain, c =>
                {
                    List<JobValue> jobs = c.List(0).Select((j, i) => j as JobValue ?? throw c.Error($"Item {i + 1} of @chain() isn't a job, but {Values.Describe(j)}.", "Type")).ToList();
                    if (jobs.Count == 0) throw c.Error("@chain() needs at least one job.", "Argument");
                    return new ChainValue(jobs);
                });

            p.Command("queue", "name?: string, connection?: string", "A queue, to look at or empty: `@queue('mail')->size()`. Defaults to the default queue of the default connection.",
                KType.Queue, c => new QueueHandle(QueueContext.For(c.Interpreter, c.Module), c.Has(0) && c.Arg(0) != null ? c.Str(0) : "default", c.Has(1) && c.Arg(1) != null ? c.Str(1) : null));

            p.Command("release_job", "after?: duration", "In a job's handle: puts the job back on its queue without using up a try, to run again after a while: `@release_job(after: 30s)`.",
                KType.Null, c => throw (JobType.InHandle
                    ? new JobReleaseSignal(c.Has(0) && c.Arg(0) != null ? Durations.Seconds(c.Arg(0), "after") : 0)
                    : OutsideAJob(c)));

            p.Command("fail_job", "error?", "In a job's handle: fails the job now, without more tries. Give an error (`@fail_job(Validation('Bad data'))`) or a message.",
                KType.Null, c => throw (JobType.InHandle
                    ? new JobFailSignal(c.Arg(0) switch
                    {
                        ErrorValue e => e.Exception,
                        null => new KScriptException("The job failed.", "JobFailed"),
                        object other => new KScriptException(Values.ToStr(other), "JobFailed"),
                    })
                    : OutsideAJob(c)));

            p.Command("fake_queue", "", "For tests: from now on in this run, dispatched jobs are kept instead of queued or run. `$q->pushed(SendWelcome)` lists them and `$q->run_all()` runs them.",
                KType.FakeQueue, c =>
                {
                    var fake = new FakeQueue();
                    c.Host.Items[QueueRuntime.FakeKey] = fake;
                    return fake;
                });

            Method(p, KType.Job, "dispatch", "delay?: duration, at?: date, queue?: string, connection?: string", "Queues the job and returns what was queued: { id, job, queue, connection, available_at }. delay (10m) or at (a date) make it wait; null when a unique job is already queued.", KType.Map);
            Method(p, KType.Job, "run", "", "Runs the job's handle now, here, without a queue. Errors reach the caller.", KType.Any);
            Method(p, KType.Chain, "dispatch", "delay?: duration, at?: date, queue?: string, connection?: string", "Queues the first job of the chain; each of the others is queued when the one before it succeeds.", KType.Map);
            Method(p, KType.Chain, "run", "", "Runs the jobs now, here, one after another.", KType.Null);
            Method(p, KType.Chain, "count", "", "How many jobs are in the chain.", KType.Number);
            p.Field(KType.Queue, "name", KType.String, "The queue's name.");
            p.Field(KType.Queue, "connection", KType.String, "The connection the queue is on.");
            Method(p, KType.Queue, "size", "", "How many jobs are waiting, including delayed ones.", KType.Number);
            Method(p, KType.Queue, "waiting", "", "The waiting jobs: id, job, queue, fields, attempts, available_at.", KType.List);
            Method(p, KType.Queue, "failed", "", "The jobs of this queue that failed: id, job, fields, attempts, failed_at and error.", KType.List);
            Method(p, KType.Queue, "clear", "", "Removes every waiting job and returns how many there were.", KType.Number);
            Method(p, KType.Queue, "retry", "id: string", "Puts a failed job back on its queue, with its tries reset. True when there was one with that id.", KType.Bool);
            Method(p, KType.FakeQueue, "pushed", "job?", "The jobs dispatched so far, or only those of one job type: pushed(SendWelcome).", KType.List);
            Method(p, KType.FakeQueue, "dispatches", "job?", "What each dispatch asked for: job, queue, connection, delay, available_at.", KType.List);
            Method(p, KType.FakeQueue, "count", "", "How many jobs were dispatched.", KType.Number);
            Method(p, KType.FakeQueue, "count_of", "match", "How many jobs of one job type were dispatched: count_of(SendWelcome).", KType.Number);
            Method(p, KType.FakeQueue, "run_all", "", "Runs the dispatched jobs in order (and any they dispatch), then forgets them. Returns how many ran.", KType.Number);
            Method(p, KType.FakeQueue, "clear", "", "Forgets the dispatched jobs.", KType.Number);

            p.Examples(
                ("@chain", "<job name=\"Step\" fields=\"n: int\">\n  <fn name=\"handle\"><echo>Step { $n }</echo></fn>\n</job>\n<do>@chain([Step(n: 1), Step(n: 2)])->run()</do>"),
                ("@queue", "<echo>{ @queue('mail', connection: 'memory')->size() } waiting</echo>"),
                ("@release_job", "<job name=\"Poll\">\n  <fn name=\"handle\">\n    <do>@release_job(after: 30s)</do>\n  </fn>\n</job>\n<do>Poll()->run()</do>"),
                ("@fail_job", "<job name=\"Check\" fields=\"n: int\">\n  <fn name=\"handle\">\n    <if test=\"$n lt 0\"><do>@fail_job('n must not be negative')</do></if>\n  </fn>\n</job>\n<try>\n  <do>Check(n: -1)->run()</do>\n  <catch as=\"err\"><echo>{ $err.message }</echo></catch>\n</try>"),
                ("@fake_queue", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<let name=\"q\" value=\"@fake_queue()\" />\n<do>Hello(who: 'Joan')->dispatch()</do>\n<echo>{ $q->count() } queued</echo>\n<do>$q->run_all()</do>"),
                ("job->dispatch", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<do>@fake_queue()</do>\n<let name=\"queued\" value=\"Hello(who: 'Joan')->dispatch(delay: 10m, queue: 'high')\" />\n<echo>{ $queued.queue }</echo>"),
                ("job->run", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<do>Hello(who: 'Joan')->run()</do>"),
                ("chain->dispatch", "<job name=\"Step\" fields=\"n: int\">\n  <fn name=\"handle\"><echo>Step { $n }</echo></fn>\n</job>\n<do>@fake_queue()</do>\n<do>@chain([Step(n: 1), Step(n: 2)])->dispatch()</do>"),
                ("chain->count", "<job name=\"Step\" fields=\"n: int\">\n  <fn name=\"handle\"><echo>Step { $n }</echo></fn>\n</job>\n<echo>{ @chain([Step(n: 1), Step(n: 2)])->count() }</echo>"),
                ("chain->run", "<job name=\"Step\" fields=\"n: int\">\n  <fn name=\"handle\"><echo>Step { $n }</echo></fn>\n</job>\n<do>@chain([Step(n: 1), Step(n: 2)])->run()</do>"),
                ("queue->size", "<echo>{ @queue('default', connection: 'memory')->size() }</echo>"),
                ("queue->waiting", "<echo>{ @queue('default', connection: 'memory')->waiting()->count() }</echo>"),
                ("queue->failed", "<echo>{ @queue('default', connection: 'memory')->failed()->count() }</echo>"),
                ("queue->clear", "<echo>{ @queue('default', connection: 'memory')->clear() } removed</echo>"),
                ("queue->retry", "<echo>{ @queue('default', connection: 'memory')->retry('no-such-id') }</echo>"),
                ("fake_queue->pushed", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<let name=\"q\" value=\"@fake_queue()\" />\n<do>Hello(who: 'Joan')->dispatch()</do>\n<echo>{ $q->pushed(Hello)[0].who }</echo>"),
                ("fake_queue->dispatches", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<let name=\"q\" value=\"@fake_queue()\" />\n<do>Hello(who: 'Joan')->dispatch(delay: 60s)</do>\n<echo>{ $q->dispatches()[0].delay }</echo>"),
                ("fake_queue->count", "<let name=\"q\" value=\"@fake_queue()\" />\n<echo>{ $q->count() }</echo>"),
                ("fake_queue->count_of", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<let name=\"q\" value=\"@fake_queue()\" />\n<do>Hello(who: 'Joan')->dispatch()</do>\n<echo>{ $q->count_of(Hello) }</echo>"),
                ("fake_queue->run_all", "<job name=\"Hello\" fields=\"who: string\">\n  <fn name=\"handle\"><echo>Hello { $who }</echo></fn>\n</job>\n<let name=\"q\" value=\"@fake_queue()\" />\n<do>Hello(who: 'Joan')->dispatch()</do>\n<echo>{ $q->run_all() } ran</echo>"),
                ("fake_queue->clear", "<let name=\"q\" value=\"@fake_queue()\" />\n<echo>{ $q->clear() }</echo>"));
        }

        /// <summary>@release_job() and @fail_job() anywhere but a job's handle: an ordinary error, which a script can catch.</summary>
        private static Exception OutsideAJob(CallContext c) => c.Error($"{c.Display} only works inside a job's handle.", "Context");

        /// <summary>A method whose work is done by the value's own TryCallMethod, registered so it is documented and its arguments are bound.</summary>
        private static void Method(PackageBuilder p, string receiver, string name, string parameters, string description, string returns) =>
            p.Method(receiver, name, parameters, description, returns, (target, call) =>
                target is IKObject obj && obj.TryCallMethod(call, name, out object result) ? result : throw call.Error($"->{name}() doesn't work on {Values.Describe(target)}.", "Type"));

        /// <summary>Builds the job type a job element declares.</summary>
        private static JobType DeclareJob(ElementRun run)
        {
            string name = run.VariableName("name");
            if (!char.IsUpper(name[0])) throw run.Error($"Job names start with a capital letter, like types: {char.ToUpperInvariant(name[0]) + name.Substring(1)}.", "name", "Syntax");

            var options = new JobOptions();
            if (run.Raw("queue") is string queue) options.Queue = NonEmpty(run, "queue", Durations.Unquote(queue));
            if (run.Raw("connection") is string connection) options.Connection = NonEmpty(run, "connection", Durations.Unquote(connection));
            if (run.Raw("tries") is string tries)
            {
                if (!int.TryParse(Durations.Unquote(tries), out int n) || n < 1 || n > 1000) throw run.Error($"tries should be a whole number from 1 to 1000, not {tries.Trim()}.", "tries", "Syntax");
                options.Tries = n;
            }
            if (run.Raw("backoff") is string backoff)
            {
                List<string> items = Durations.Items(backoff);
                if (items.Count == 1 && items[0] == "exponential") options.Backoff = null;
                else options.Backoff = items.Select(i => Seconds(run, "backoff", i)).ToList();
                if (options.Backoff != null && options.Backoff.Count == 0) throw run.Error("backoff needs a time, a list of times or exponential.", "backoff", "Syntax");
            }
            if (run.Raw("timeout") is string timeout)
            {
                options.Timeout = Seconds(run, "timeout", Durations.Unquote(timeout));
                if (options.Timeout <= 0) throw run.Error("timeout should be more than 0 seconds.", "timeout", "Syntax");
            }
            if (run.Raw("retry_until") is string until) options.RetryUntil = Seconds(run, "retry_until", Durations.Unquote(until));
            if (run.Raw("unique_for") is string unique) options.UniqueFor = Seconds(run, "unique_for", Durations.Unquote(unique));
            if (run.Raw("unique_by") is string by) options.UniqueBy = Durations.Items(by);
            if (run.Raw("retry_on") is string retryOn) options.RetryOn = ErrorNames(run, "retry_on", retryOn);
            if (run.Raw("fail_on") is string failOn) options.FailOn = ErrorNames(run, "fail_on", failOn);

            var fields = new RecordType(name, run.Raw("description"), ExtraKeys.Error, run.Scope, run.Module);
            Shapes.AddFields(run, fields, rulesAndMethods: true);
            foreach (FieldDefinition f in fields.Fields)
            {
                if (f.Type.Name == KType.Password)
                    throw run.Error($"The field '{f.Name}' is a password, and queued jobs are saved to disk. Pass something that finds it instead, such as the user's id.", "fields", "Syntax");
            }
            foreach (string unknown in options.UniqueBy.Where(u => fields.Field(u) == null))
                throw run.Error($"unique_by names '{unknown}', which isn't a field of {name}.", "unique_by", "Syntax");

            fields.OwnMethods.TryGetValue("handle", out MarkupElement handle);
            if (handle == null) throw run.Error($"<job name=\"{name}\"> needs a handle function that does its work: fn handle() {{ ... }}.", null, "Syntax");
            fields.OwnMethods.TryGetValue("failed", out MarkupElement failed);
            string other = fields.OwnMethods.Keys.FirstOrDefault(k => k is not ("handle" or "failed"));
            if (other != null) throw run.Error($"A job has two functions, handle and failed, not {other}. Put helpers outside the job.", null, "Syntax");
            return new JobType(name, run.Raw("description"), fields, options, handle, failed, run.Scope, run.Module);
        }

        private static string NonEmpty(ElementRun run, string attribute, string value) =>
            string.IsNullOrWhiteSpace(value) ? throw run.Error($"{attribute} can't be empty.", attribute, "Syntax") : value.Trim();

        private static double Seconds(ElementRun run, string attribute, string text)
        {
            try
            {
                return Durations.Seconds(text, attribute);
            }
            catch (KScriptException ex)
            {
                throw run.Error(ex.Words, attribute, "Syntax");
            }
        }

        private static List<string> ErrorNames(ElementRun run, string attribute, string text)
        {
            // Error types declared further down (or not at all) are matched by name when a job fails; the editor checks the names.
            List<string> names = Durations.Items(text);
            if (names.Count == 0) throw run.Error($"{attribute} needs error types, such as [Timeout, Network].", attribute, "Syntax");
            return names;
        }
    }
}
