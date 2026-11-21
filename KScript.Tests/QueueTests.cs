using System.Globalization;

using KScript.Projects;
using KScript.Queueing;
using KScript.Runtime;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Tests;

/// <summary>
/// KScript.Queue: jobs, dispatch, the drivers, the worker and kscript queue. Each test has its own project (with its
/// own app name, so memory queues never carry over), its own app folders and a clock it moves by hand.
/// </summary>
public sealed class QueueTests : StorageFixture
{
    private DateTime now = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private readonly string app = "queue" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private readonly StringWriter jobOutput = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };

    public QueueTests()
    {
        QueueRuntime.Clock = () => now;
        QueueContext.ForgetProjects();
        UseProject("{ \"name\": \"" + app + "\" }");
        FileOf("jobs/welcome.kscript", WelcomeJob);
    }

    protected override void OnDispose()
    {
        QueueRuntime.Clock = () => DateTime.UtcNow;
        QueueContext.ForgetProjects();
        MemoryQueueDriver.ResetAll();
    }

    private const string WelcomeJob = """
        /// Says welcome.
        job Welcome(queue: "mail", tries: 3, backoff: ['10s', '1m'], timeout: "5s") {
          user_id: int
          email: email
          name?: string

          fn handle() {
            echo "Welcome {$name ?? 'there'} <{$email}> #{$user_id} attempt {$job.attempt} of {$job.tries} on {$job.queue}"
            if $user_id == 13 {
              throw Validation "unlucky"
            }
          }

          fn failed(err) {
            echo "Gave up on {$user_id}: {$err.type}: {$err.message} ({$err.cause.type})"
          }
        }
        """;

    private void UseProject(string json)
    {
        FileOf("kscript.json", json);
        QueueContext.ForgetProjects();
    }

    private void Later(double seconds) => now = now.AddSeconds(seconds);

    /// <summary>Runs a .kscript script saved in the project folder.</summary>
    private string Code(string code, string file = "main.kscript", Action<KScriptHost> setup = null)
    {
        FileOf(file, code);
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = KScriptHost.ForText(output);
        host.WorkingDirectory = Project;
        setup?.Invoke(host);
        new Interpreter(host).RunFile(Path.Combine(Project, file));
        return output.ToString();
    }

    private KScriptException CodeFails(string code, Action<KScriptHost> setup = null) => Assert.Throws<KScriptException>(() => Code(code, setup: setup));

    private QueueContext Context(string environment = null)
    {
        QueueContext.ForgetProjects();
        return new QueueContext(ProjectConfig.FindAndLoad(Project), Project, environment);
    }

    private IQueueDriver Driver(string connection = null) => Context().Driver(connection);

    /// <summary>Runs a worker until the queues are empty; returns its log.</summary>
    private string Work(string connection = null, int concurrency = 1, params string[] queues)
    {
        var log = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var options = new WorkerOptions
        {
            Connection = connection,
            StopWhenEmpty = true,
            Concurrency = concurrency,
            Queues = queues.ToList(),
            Host = () => JobHost(),
        };
        new Worker(Context(), options, log).Run(CancellationToken.None);
        return log.ToString();
    }

    private KScriptHost JobHost()
    {
        TextWriter writer = TextWriter.Synchronized(jobOutput);
        return new KScriptHost(writer, writer, new StringReader(string.Empty));
    }

    private string JobOutput()
    {
        string text = jobOutput.ToString();
        jobOutput.GetStringBuilder().Clear();
        return text;
    }

    private (int Code, string Out, string Error) Cli(params string[] args)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var error = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        int code = args.Length > 0 && args[0] == "worker"
            ? QueueCommand.RunWorker(args.Skip(1).ToArray(), output, error, CancellationToken.None, Project, host: () => JobHost())
            : QueueCommand.RunQueue(args, output, error, Project);
        return (code, output.ToString(), error.ToString());
    }

    // ---- declaring and running jobs ------------------------------------------------------------------------------

    [Fact]
    public void RunRunsTheHandleHereWithTheFieldsAsVariables()
    {
        string output = Code("""
            import "jobs/welcome.kscript"
            Welcome(user_id: 1, email: 'joan@example.com', name: 'Joan')->run()
            job = Welcome(user_id: 2, email: 'ann@example.com')
            echo "{$job.user_id} {$job.email} {$job.job} {$job.fields.user_id} {@type($job)}"
            echo "{$job}"
            """);
        Assert.Equal("Welcome Joan <joan@example.com> #1 attempt 1 of 3 on mail\n2 ann@example.com Welcome 2 job\nWelcome(user_id: 2, email: ann@example.com, name: null)\n", output);
    }

    [Fact]
    public void JobsAreDeclaredInXmlToo()
    {
        string output = Run("""
            <job name="Hello" fields="who: string" tries="2" backoff="exponential" timeout="30s" retry_until="1h" unique_for="5m" unique_by="[who]" retry_on="[Timeout, Network]" fail_on="Validation">
              <fn name="handle"><echo>Hello { $who } { $this.who }</echo></fn>
            </job>
            <do>Hello(who: 'Joan')->run()</do>
            <echo>{ $Hello.name } { $Hello.queue } { $Hello.tries } { $Hello.timeout } { $Hello.fields } { $Hello }</echo>
            """, setup: h => h.WorkingDirectory = Project);
        Assert.Equal("Hello Joan Joan\nHello default 2 30 who Hello\n", output);
    }

    [Theory]
    [InlineData("<job name=\"lower\"><fn name=\"handle\" /></job>", "start with a capital letter")]
    [InlineData("<job name=\"NoHandle\" fields=\"a: int\" />", "needs a handle function")]
    [InlineData("<job name=\"Extra\"><fn name=\"handle\" /><fn name=\"helper\" /></job>", "two functions, handle and failed")]
    [InlineData("<job name=\"Pw\" fields=\"pw: password\"><fn name=\"handle\" /></job>", "saved to disk")]
    [InlineData("<job name=\"T\" tries=\"0\"><fn name=\"handle\" /></job>", "tries should be a whole number")]
    [InlineData("<job name=\"T\" timeout=\"soon\"><fn name=\"handle\" /></job>", "timeout should be a length of time")]
    [InlineData("<job name=\"T\" timeout=\"0\"><fn name=\"handle\" /></job>", "more than 0 seconds")]
    [InlineData("<job name=\"T\" backoff=\"[]\"><fn name=\"handle\" /></job>", "backoff needs a time")]
    [InlineData("<job name=\"T\" queue=\"''\"><fn name=\"handle\" /></job>", "queue can't be empty")]
    [InlineData("<job name=\"T\" retry_on=\"[]\"><fn name=\"handle\" /></job>", "needs error types")]
    [InlineData("<job name=\"T\" fields=\"a: int\" unique_for=\"1m\" unique_by=\"b\"><fn name=\"handle\" /></job>", "isn't a field")]
    [InlineData("<job name=\"T\" fields=\"a: int\"><fn name=\"handle\" /></job><do>T(1)</do>", "takes its fields by name")]
    [InlineData("<job name=\"T\" fields=\"a: int\"><fn name=\"handle\" /></job><do>T(a: 'x')</do>", "T's fields")]
    [InlineData("<dispatch value=\"1\" />", "dispatch needs a job")]
    [InlineData("<import package=\"KScript.Queue\" /><do>@chain([1])</do>", "isn't a job")]
    [InlineData("<import package=\"KScript.Queue\" /><do>@chain([])</do>", "at least one job")]
    public void BadJobsSayWhatIsWrong(string body, string message) =>
        Assert.Contains(message, Assert.Throws<KScriptException>(() => Run(body, setup: h => h.WorkingDirectory = Project)).Message);

    [Fact]
    public void AJobWithoutAFileCanRunButNotBeQueued()
    {
        var output = new StringWriter();
        var host = KScriptHost.ForText(output);
        host.WorkingDirectory = Project;
        var error = Assert.Throws<KScriptException>(() => new Interpreter(host).RunText(
            "<kscript><job name=\"Hi\"><fn name=\"handle\"><echo>hi</echo></fn></job><do>Hi()->run()</do><do>Hi()->dispatch()</do></kscript>"));
        Assert.Contains("isn't declared in a file", error.Message);
        Assert.Equal("hi\n", output.ToString().Replace("\r\n", "\n"));
    }

    // ---- dispatch and the file driver ------------------------------------------------------------------------------

    [Fact]
    public void DispatchQueuesJobsInFilesAndTheWorkerRunsThem()
    {
        string output = Code("""
            import "jobs/welcome.kscript"
            q = Welcome(user_id: 2, email: 'ann@example.com')->dispatch()
            echo "{$q.job} {$q.queue} {$q.connection}"
            dispatch Welcome(user_id: 3, email: 'bo@example.com', name: 'Bo')
            """);
        Assert.Equal("Welcome mail file\n", output);
        IQueueDriver driver = Driver();
        List<QueuedJob> waiting = driver.Waiting("mail");
        Assert.Equal(new[] { 2.0, 3.0 }, waiting.Select(j => (double)j.Fields["user_id"]));
        Assert.Equal("jobs/welcome.kscript", waiting[0].File);
        Assert.True(Directory.Exists(Path.Combine(AppFolder(app, "data"), "queue", "file", "queues", "mail")));

        string log = Work();
        Assert.Contains("Welcome #", log);
        Assert.Contains(": done (", log);
        Assert.Equal("Welcome there <ann@example.com> #2 attempt 1 of 3 on mail\nWelcome Bo <bo@example.com> #3 attempt 1 of 3 on mail\n", JobOutput());
        Assert.Empty(driver.Waiting(null));
        Assert.Empty(driver.Running());
    }

    [Fact]
    public void FailedAttemptsRetryAfterTheirBackoffThenFailForGood()
    {
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 13, email: 'x@example.com')");
        Assert.Contains("Validation: unlucky (retrying in 10s", Work(null, 1, "mail"));
        Assert.Equal(string.Empty, Work(null, 1, "mail"));
        Later(10);
        Assert.Contains("retrying in 60s", Work(null, 1, "mail"));
        Later(60);
        string log = Work(null, 1, "mail");
        Assert.Contains("failed: Validation: unlucky", log);
        string output = JobOutput();
        Assert.Contains("attempt 3 of 3", output);
        Assert.EndsWith("Gave up on 13: JobFailed: Welcome failed: unlucky (Validation)\n", output);

        List<QueuedJob> failed = Driver().Failed();
        QueuedJob job = Assert.Single(failed);
        Assert.Equal(3, job.Attempts);
        Assert.Equal("Validation", job.LastError["type"]);
        Assert.Equal(now, job.FailedAt);

        Assert.True(Driver().Retry(job.Id));
        Assert.False(Driver().Retry(job.Id));
        Assert.Equal(0, Driver().Waiting("mail")[0].Attempts);
        Assert.Equal(1, Driver().Clear("mail"));
    }

    [Fact]
    public void RetryOnAndFailOnChooseWhichErrorsRetry()
    {
        FileOf("jobs/picky.kscript", """
            error Flaky extends Network {
            }

            job Picky(retry_on: [Network], fail_on: [NotFound], tries: 5, backoff: "1s") {
              kind: string

              fn handle() {
                match $kind {
                  case 'flaky' { throw Flaky "try again" }
                  case 'missing' { throw NotFound "gone" }
                  default { throw Validation "bad" }
                }
              }
            }
            """);
        Code("import \"jobs/picky.kscript\"\ndispatch Picky(kind: 'flaky')\ndispatch Picky(kind: 'missing')\ndispatch Picky(kind: 'other')");
        string log = Work();
        Assert.Contains("Flaky: try again (retrying in 1s", log);
        Assert.Contains("failed: NotFound: gone", log);
        Assert.Contains("failed: Validation: bad", log);
        Assert.Equal(2, Driver().Failed().Count);
    }

    [Fact]
    public void ReleaseAndFailInAHandle()
    {
        FileOf("jobs/poll.kscript", """
            job Poll(tries: 2) {
              n: int

              fn handle() {
                if $n == 1 {
                  @release_job(after: '30s')
                }
                if $n == 2 {
                  @fail_job(Validation(message: 'no good'))
                }
                if $n == 3 {
                  @fail_job('plain')
                }
                if $n == 4 {
                  @fail_job()
                }
                @release_job()
              }
            }
            """);
        Code("import \"jobs/poll.kscript\"\nfor n in [1, 2, 3, 4] {\n  dispatch Poll(n: $n)\n}");
        string log = Work();
        Assert.Contains("released, back in 30s", log);
        Assert.Contains("failed: Validation: no good", log);
        Assert.Contains("failed: JobFailed: plain", log);
        Assert.Contains("failed: JobFailed: The job failed.", log);
        QueuedJob released = Assert.Single(Driver().Waiting(null));
        Assert.Equal(0, released.Attempts);
        Assert.Equal(now.AddSeconds(30), released.AvailableAt);

        // Run here, a release just ends the run and a fail throws.
        Assert.Equal(string.Empty, Code("import \"jobs/poll.kscript\"\nPoll(n: 1)->run()"));
        Assert.Equal("no good", CodeFails("import \"jobs/poll.kscript\"\nPoll(n: 2)->run()").Message);
        Assert.Contains("@release_job() outside a job", Assert.ThrowsAny<Exception>(() => Code("import KScript.Queue\n@release_job()")).Message);
    }

    [Fact]
    public void ATimeoutStopsTheAttempt()
    {
        FileOf("jobs/slow.kscript", """
            job Slow(timeout: "0.2s", tries: 1) {
              fn handle() {
                n = 0
                while true {
                  n = $n + 1
                }
              }
            }
            """);
        Code("import \"jobs/slow.kscript\"\ndispatch Slow()");
        Assert.Contains("failed: JobTimeout: Slow ran for more than 0.2s, its timeout.", Work());
        Assert.Equal("JobTimeout", Driver().Failed()[0].LastError["type"]);
    }

    [Fact]
    public void DelaysAndDatesHoldJobsBack()
    {
        string output = Code("""
            import "jobs/welcome.kscript"
            a = Welcome(user_id: 1, email: 'a@example.com')->dispatch(delay: '10m', queue: 'later')
            b = Welcome(user_id: 2, email: 'b@example.com')->dispatch(at: @date('2026-11-21T10:00:00Z'), queue: 'later')
            c = Welcome(user_id: 3, email: 'c@example.com')->dispatch(delay: 30, queue: 'later', connection: 'memory')
            d = Welcome(user_id: 4, email: 'd@example.com')->dispatch(at: '2000-01-01', queue: 'later')
            echo "{$a.available_at->format('HH:mm')} {$b.available_at->format('HH:mm')} {$c.connection} {$d.available_at->format('HH:mm')}"
            """);
        Assert.Equal("09:10 10:00 memory 09:00\n", output);
        Assert.Contains("#", Work(null, 1, "later"));
        Assert.Equal(2, Driver().Waiting("later").Count);
        Later(600);
        Work(null, 1, "later");
        Assert.Single(Driver().Waiting("later"));
        Assert.Single(Driver("memory").Waiting("later"));
        Assert.Contains("has no option 'soon'", CodeFails("import \"jobs/welcome.kscript\"\nWelcome(user_id: 1, email: 'a@example.com')->dispatch(soon: 1)").Message);
        Assert.Contains("at: should be a date", CodeFails("import \"jobs/welcome.kscript\"\nWelcome(user_id: 1, email: 'a@example.com')->dispatch(at: 5)").Message);
        Assert.Contains("delay should be a length of time", CodeFails("import \"jobs/welcome.kscript\"\nWelcome(user_id: 1, email: 'a@example.com')->dispatch(delay: 'soon')").Message);
    }

    [Fact]
    public void TheDispatchElementTakesOptionsAndAVariable()
    {
        string output = Run("""
            <import src="jobs/welcome.kscript" />
            <dispatch value="Welcome(user_id: 1, email: 'a@example.com')" delay="'1m'" queue="'q1'" connection="file" to="queued" />
            <dispatch value="@chain([Welcome(user_id: 2, email: 'b@example.com')])" at="@date('2026-11-21T09:30:00Z')" />
            <echo>{ $queued.queue } { $queued.available_at->format('HH:mm') }</echo>
            """, file: "main.ks.xml", setup: h => h.WorkingDirectory = Project);
        Assert.Equal("q1 09:01\n", output);
        Assert.Contains("at should be a date", Assert.Throws<KScriptException>(() => Run("<import src=\"jobs/welcome.kscript\" /><dispatch value=\"Welcome(user_id: 1, email: 'a@example.com')\" at=\"5\" />", file: "main.ks.xml")).Message);
    }

    [Fact]
    public void ChainsRunInOrderAndStopAtAFailure()
    {
        FileOf("jobs/step.kscript", """
            job Step(tries: 1) {
              n: int

              fn handle() {
                echo "step {$n}"
                if $n == 2 {
                  throw Validation "stop"
                }
              }
            }
            """);
        string output = Code("""
            import KScript.Queue
            import "jobs/step.kscript"
            c = @chain([Step(n: 1), Step(n: 2), Step(n: 3)])
            echo "{$c.count} {$c}"
            $c->dispatch(queue: 'steps')
            """);
        Assert.Equal("3 chain [Step, Step, Step]\n", output);
        Assert.Equal(2, Driver().Waiting("steps")[0].Chain.Count);
        Work(null, 1, "steps");
        Assert.Equal("step 1\nstep 2\n", JobOutput());
        Assert.Single(Driver().Failed());

        Assert.Equal("step 1\nstep 3\n", Code("import KScript.Queue\nimport \"jobs/step.kscript\"\n@chain([Step(n: 1), Step(n: 3)])->run()"));
    }

    [Fact]
    public void UniqueJobsAreQueuedOnce()
    {
        FileOf("jobs/sync.kscript", """
            job SyncUser(unique_for: "10m", unique_by: [user_id]) {
              user_id: int
              note?: string

              fn handle() {
                echo "sync {$user_id}"
              }
            }
            """);
        const string dispatch = "import \"jobs/sync.kscript\"\nq = SyncUser(user_id: 1, note: 'x')->dispatch()\necho \"{$q == null}\"";
        Assert.Equal("False\n", Code(dispatch).Replace("false", "False"));
        Assert.Equal("True\n", Code(dispatch.Replace("'x'", "'y'")).Replace("true", "True"));
        Work();
        Assert.Equal("False\n", Code(dispatch).Replace("false", "False"));
        Later(3600);
        Assert.Equal("False\n", Code(dispatch).Replace("false", "False"));

        UseProject("{ \"name\": \"" + app + "\", \"queue\": { \"default\": \"memory\" } }");
        Assert.Equal("False\n", Code(dispatch).Replace("false", "False"));
        Assert.Equal("True\n", Code(dispatch).Replace("true", "True"));
        Work("memory");
        Assert.Equal("False\n", Code(dispatch).Replace("false", "False"));
    }

    [Fact]
    public void TheMemoryDriverKeepsJobsInThisProcess()
    {
        UseProject("{ \"name\": \"" + app + "\", \"queue\": { \"default\": \"fast\", \"connections\": { \"fast\": { \"driver\": \"memory\" } } } }");
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 13, email: 'x@example.com')\ndispatch Welcome(user_id: 5, email: 'y@example.com')");
        IQueueDriver driver = Driver();
        Assert.Equal("memory", driver.Kind);
        Assert.Equal(2, driver.Waiting("mail").Count);
        Work(null, 2, "mail");
        Later(10);
        Work(null, 1, "mail");
        Later(60);
        Work(null, 1, "mail");
        QueuedJob failed = Assert.Single(driver.Failed());
        Assert.Equal(1, driver.Prune(now.AddSeconds(1)));
        Assert.False(driver.Forget(failed.Id));

        // A worker that stops mid-job loses its lease, and the job waits again.
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 6, email: 'z@example.com')");
        QueuedJob taken = driver.Reserve(new[] { "mail" }, now);
        Assert.Empty(driver.Waiting("mail"));
        Assert.Single(driver.Running());
        Later(36);
        Assert.Equal(taken.Id, driver.Reserve(new[] { "mail" }, now).Id);
        Assert.Equal(2, driver.Running()[0].Attempts);
    }

    [Fact]
    public void TheFileDriverRecoversLostJobsAndSkipsBrokenFiles()
    {
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 6, email: 'z@example.com')");
        var driver = (FileQueueDriver)Driver();
        QueuedJob taken = driver.Reserve(new[] { "mail" }, now);
        Assert.Single(driver.Running());
        Assert.Null(driver.Reserve(new[] { "mail" }, now));
        Later(36);
        QueuedJob again = driver.Reserve(new[] { "mail" }, now);
        Assert.Equal(taken.Id, again.Id);
        Assert.Equal(2, again.Attempts);
        driver.Fail(again);
        Assert.True(driver.Forget(again.Id));
        Assert.False(driver.Forget(again.Id));

        string folder = Path.Combine(driver.Folder, "queues", "mail");
        File.WriteAllText(Path.Combine(folder, "20260101000000000-broken.json"), "not json");
        Assert.Null(driver.Reserve(new[] { "mail" }, now));
        Assert.True(File.Exists(Path.Combine(driver.Folder, "failed", "unreadable-broken.json")));
        Assert.Equal(0, driver.Clear("missing"));
        Assert.Equal(0, driver.Prune(now));
    }

    [Fact]
    public void TheFileDriverLocksUniqueKeys()
    {
        var driver = new FileQueueDriver("x", Path.Combine(Root, "locks"));
        Assert.True(driver.Lock("k", now.AddMinutes(1), now));
        Assert.False(driver.Lock("k", now.AddMinutes(1), now));
        Assert.True(driver.Lock("k", now.AddMinutes(5), now.AddMinutes(2)));
        driver.Unlock("k");
        driver.Unlock("k");
        Assert.True(driver.Lock("k", now.AddMinutes(1), now));
    }

    [Fact]
    public void SeveralWorkersShareAQueue()
    {
        FileOf("jobs/count.kscript", "job Count {\n  n: int\n\n  fn handle() {\n    echo \"n{$n}\"\n  }\n}");
        Code("import \"jobs/count.kscript\"\nfor n in 0..20 {\n  dispatch Count(n: $n)\n}");
        Work(null, 4);
        string[] lines = JobOutput().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(20, lines.Length);
        Assert.Equal(20, lines.Distinct().Count());
    }

    [Fact]
    public void WorkerLimitsStopIt()
    {
        FileOf("jobs/count.kscript", "job Count {\n  n: int\n\n  fn handle() {\n    echo \"n{$n}\"\n  }\n}");
        Code("import \"jobs/count.kscript\"\nfor n in 0..5 {\n  dispatch Count(n: $n)\n}");
        var options = new WorkerOptions { MaxJobs = 2, Sleep = 0.01, Host = () => JobHost() };
        var worker = new Worker(Context(), options, null);
        worker.Run(CancellationToken.None);
        Assert.Equal(2, worker.Processed);
        Assert.Equal(3, Driver().Waiting(null).Count);

        new Worker(Context(), new WorkerOptions { Once = true, Host = () => JobHost() }, null).Run(CancellationToken.None);
        Assert.Equal(2, Driver().Waiting(null).Count);

        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var forever = new Worker(Context(), new WorkerOptions { Sleep = 0.01, Host = () => JobHost() }, null);
        forever.Run(stop.Token);
        Assert.Equal(2, forever.Processed);

        var timed = new Worker(Context(), new WorkerOptions { MaxTime = 0.05, Sleep = 0.01 }, null);
        timed.Run(CancellationToken.None);
        Assert.Equal(0, timed.Processed);
    }

    [Fact]
    public void AJobWhoseFileOrDeclarationIsGoneFails()
    {
        FileOf("jobs/gone.kscript", "job Gone {\n  fn handle() {\n  }\n}");
        Code("import \"jobs/welcome.kscript\"\nimport \"jobs/gone.kscript\"\ndispatch Welcome(user_id: 1, email: 'a@example.com')\ndispatch Gone()");
        IQueueDriver driver = Driver();
        QueuedJob first = driver.Reserve(new[] { "mail" }, now);
        first.Fields["email"] = "not an email";
        driver.Release(first);
        string bad = Work(null, 1, "mail");
        Assert.Contains("needs an email", bad);

        File.Delete(Path.Combine(Project, "jobs/gone.kscript"));
        Assert.Contains("isn't there any more", Work());

        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 1, email: 'a@example.com')");
        FileOf("jobs/welcome.kscript", "job Other {\n  fn handle() {\n  }\n}");
        Assert.Contains("doesn't declare a job called Welcome", Work(null, 1, "mail"));
    }

    [Fact]
    public void TheSyncConnectionRunsJobsAsTheyAreDispatched()
    {
        UseProject("{ \"name\": \"" + app + "\", \"queue\": { \"default\": \"sync\" } }");
        Assert.Equal("Welcome there <a@example.com> #1 attempt 1 of 3 on mail\nnull\n",
            Code("import \"jobs/welcome.kscript\"\nq = Welcome(user_id: 1, email: 'a@example.com')->dispatch()\necho \"{$q ?? 'null'}\""));
        Assert.Equal("unlucky", CodeFails("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 13, email: 'a@example.com')").Message);
        Assert.Contains("nothing for a worker to do", Assert.Throws<KScriptException>(() => Work()).Message);
        Assert.Contains("no queue to look at", Cli("status").Error);
    }

    [Fact]
    public void TestRunsUseSyncUnlessTheProjectSaysOtherwise()
    {
        void Testing(KScriptHost h) => h.Items[QueueRuntime.TestSessionKey] = new object();
        Assert.StartsWith("Welcome", Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 1, email: 'a@example.com')", setup: Testing));
        UseProject("{ \"name\": \"" + app + "\", \"queue\": { \"env\": { \"test\": { \"default\": \"memory\" } } } }");
        Assert.Equal(string.Empty, Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 1, email: 'a@example.com')", setup: Testing));
        Assert.Single(Driver("memory").Waiting(null));
        Assert.Equal("memory", Context("test").DefaultConnection);
        Assert.Equal("file", Context("production").DefaultConnection);
    }

    [Fact]
    public void FakeQueuesRecordDispatchesForTests()
    {
        string output = Code("""
            import KScript.Queue
            import "jobs/welcome.kscript"
            q = @fake_queue()
            dispatch Welcome(user_id: 1, email: 'a@example.com')
            Welcome(user_id: 2, email: 'b@example.com')->dispatch(delay: '1m', queue: 'later', connection: 'x')
            @chain([Welcome(user_id: 3, email: 'c@example.com')])->dispatch()
            echo "{$q.count} {$q->count($Welcome)} {$q->count('Other')} {$q->pushed($Welcome)[1].user_id} {$q->dispatches()[1].queue} {$q->dispatches()[1].connection} {$q->dispatches()[1].delay} {$q}"
            echo "{$q->run_all()} ran, {$q->count()} left, {$q->clear()}"
            """);
        Assert.Equal("3 3 0 2 later x 60 fake queue (3 pushed)\nWelcome there <a@example.com> #1 attempt 1 of 3 on mail\nWelcome there <b@example.com> #2 attempt 1 of 3 on mail\nWelcome there <c@example.com> #3 attempt 1 of 3 on mail\n3 ran, 0 left, 0\n", output);
        Assert.Empty(Driver().Waiting(null));
        Assert.Contains("pushed() takes a job type", CodeFails("import KScript.Queue\nq = @fake_queue()\n$q->pushed(5)").Message);
        Assert.Contains("10,000 jobs", CodeFails("""
            import KScript.Queue
            job Again {
              fn handle() {
                dispatch Again()
              }
            }
            q = @fake_queue()
            dispatch Again()
            $q->run_all()
            """).Message);
    }

    [Fact]
    public void QueueHandlesLookAtAQueue()
    {
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 13, email: 'a@example.com')\ndispatch Welcome(user_id: 2, email: 'b@example.com')");
        Work(null, 1, "mail");
        Later(10);
        Work(null, 1, "mail");
        Later(60);
        Work(null, 1, "mail");
        string id = Driver().Failed()[0].Id;
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 4, email: 'c@example.com')");
        string output = Code($$"""
            import KScript.Queue
            q = @queue('mail')
            echo "{$q.name} {$q.connection} {$q} {$q->size()} {$q->waiting()[0].fields.user_id} {$q->failed()[0].error.type}"
            echo "{$q->retry('{{id}}')} {$q->size()} {$q->clear()} {$q->size()} {@queue()->size()} {@queue('x', 'sync')->size()}"
            """);
        Assert.Equal("mail file queue 'mail' on file 1 4 Validation\ntrue 2 2 0 0 0\n", output);
        Assert.Contains("can't", CodeFails("import KScript.Queue\n@queue('mail')->clear()", setup: h => h.CanWriteFiles = false).Message);
    }

    [Fact]
    public void JobValuesAnswerOnlyWhatTheyHave()
    {
        string output = Code("""
            import KScript.Queue
            import "jobs/welcome.kscript"
            c = @chain([Welcome(user_id: 1, email: 'a@example.com')])
            echo "{$Welcome.description} {$Welcome.connection ?? 'none'} {$c.jobs[0].user_id}"
            """);
        Assert.Equal("Says welcome. none 1\n", output);
        Assert.Contains("nope", CodeFails("import \"jobs/welcome.kscript\"\necho $Welcome.nope").Message);
        Assert.Contains("nope", CodeFails("import \"jobs/welcome.kscript\"\n$Welcome->nope()").Message);
        Assert.Contains("nope", CodeFails("import \"jobs/welcome.kscript\"\nx = Welcome(user_id: 1, email: 'a@example.com')\n$x->nope()").Message);
        Assert.Contains("nope", CodeFails("import \"jobs/welcome.kscript\"\nx = Welcome(user_id: 1, email: 'a@example.com')\necho $x.nope").Message);
        Assert.Contains("nope", CodeFails("import KScript.Queue\nimport \"jobs/welcome.kscript\"\nx = @chain([Welcome(user_id: 1, email: 'a@example.com')])\necho $x.nope").Message);
        Assert.Contains("nope", CodeFails("import KScript.Queue\nimport \"jobs/welcome.kscript\"\nx = @chain([Welcome(user_id: 1, email: 'a@example.com')])\n$x->nope()").Message);
        Assert.Contains("nope", CodeFails("import KScript.Queue\nx = @fake_queue()\n$x->nope()").Message);
        Assert.Contains("nope", CodeFails("import KScript.Queue\nx = @queue()\necho $x.nope").Message);
        Assert.Contains("nope", CodeFails("import KScript.Queue\nx = @queue()\n$x->nope()").Message);

        var host = KScriptHost.ForText(new StringWriter());
        host.WorkingDirectory = Project;
        var interpreter = new Interpreter(host);
        Module module = interpreter.Load(Path.Combine(Project, "jobs/welcome.kscript"));
        interpreter.Run(module);
        JobType type = Assert.IsType<JobType>(module.Exports["Welcome"]);
        Assert.Equal("Welcome", type.ToString());
        Assert.Equal("job_type", type.TypeName);
        JobValue job = type.Make(interpreter, new Dictionary<string, object> { ["user_id"] = 1.0, ["email"] = "a@example.com" });
        Assert.Equal("Welcome(user_id: 1, email: a@example.com, name: null)", job.ToString());
        Assert.Equal("2h", Durations.Describe(7200));
        Assert.Equal("3d", Durations.Describe(3 * 86400));
        Assert.Equal("0.5s", Durations.Describe(0.5));
    }

    [Fact]
    public void TheMemoryDriverClearsAndRetries()
    {
        UseProject("{ \"name\": \"" + app + "\", \"queue\": { \"default\": \"memory\" } }");
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 13, email: 'x@example.com')\ndispatch Welcome(user_id: 1, email: 'y@example.com')");
        IQueueDriver driver = Driver();
        QueuedJob job = driver.Reserve(new[] { "mail" }, now);
        driver.Fail(job);
        Assert.Equal(new List<string> { "mail" }, driver.QueueNames());
        Assert.True(driver.Retry(job.Id));
        Assert.False(driver.Retry(job.Id));
        Assert.Equal(2, driver.Clear(null));
        Assert.Empty(driver.QueueNames());
    }

    [Fact]
    public void FileJobsAnotherWorkerIsRecoveringAreLeftAlone()
    {
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 6, email: 'z@example.com')");
        var driver = (FileQueueDriver)Driver();
        QueuedJob taken = driver.Reserve(new[] { "mail" }, now);
        string running = Path.Combine(driver.Folder, "running", taken.Id + ".json");
        File.WriteAllText(running + ".recovering", "taken");
        Later(600);
        Assert.Null(driver.Reserve(new[] { "mail" }, now));
        Assert.Single(driver.Running());

        QueuedJob lost = driver.Running()[0];
        lost.FailedAt = now;
        driver.Fail(lost);
        Assert.Equal(0, driver.Prune(now.AddSeconds(-1)));
        Assert.Equal(1, driver.Prune(now.AddSeconds(1)));
    }

    [Fact]
    public void StatusCountsRunningAndFailedJobs()
    {
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 1, email: 'a@example.com')\ndispatch Welcome(user_id: 2, email: 'b@example.com')");
        IQueueDriver driver = Driver();
        driver.Reserve(new[] { "mail" }, now);
        driver.Fail(driver.Reserve(new[] { "mail" }, now));
        Assert.Contains("mail     0        0        1        1", Cli("status").Out);
    }

    [Fact]
    public void AFailedHookThatThrowsIsReported()
    {
        FileOf("jobs/bad.kscript", "job Bad(tries: 1) {\n  fn handle() {\n    throw Validation \"no\"\n  }\n\n  fn failed(err) {\n    throw Error \"worse\"\n  }\n}");
        Code("import \"jobs/bad.kscript\"\ndispatch Bad()");
        Assert.Contains("failed: Validation: no", Work());
        Assert.Contains("Bad's fn failed() stopped with an error", JobOutput());
    }

    [Fact]
    public void ImportingAJobThatIsntThereSaysWhatScriptsExport() =>
        Assert.Contains("<error>s and <job>s", Assert.Throws<KScriptException>(() => Run("<import src=\"jobs/welcome.kscript\" names=\"Nope\" />", setup: h => h.WorkingDirectory = Project)).Message);

    [Fact]
    public void ABrokenKscriptJsonMeansTheDefaults()
    {
        FileOf("kscript.json", "{ broken");
        QueueContext.ForgetProjects();
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 6, email: 'z@example.com')");
        Assert.Single(new QueueContext(null, Project, null).Driver(null).Waiting(null));
    }

    // ---- kscript worker and kscript queue -----------------------------------------------------------------------------

    [Fact]
    public void TheQueueCommandShowsAndFixesTheQueues()
    {
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 13, email: 'a@example.com')\nWelcome(user_id: 2, email: 'b@example.com')->dispatch(delay: '5m')");
        IQueueDriver statusDriver = Driver();
        QueuedJob busy = statusDriver.Reserve(new[] { "mail" }, now);
        statusDriver.Release(busy);
        var status = Cli("status", "--project", Project);
        Assert.Equal(0, status.Code);
        Assert.Contains("Connection file (file): ", status.Out);
        Assert.Contains("mail     1        1        0        0", status.Out);
        Assert.Contains("\"waiting\": 1", Cli("status", "--format", "json").Out);
        var list = Cli("list", "mail");
        Assert.Contains("now", list.Out);
        Assert.Contains("in 5m", list.Out);
        Assert.Contains("\"user_id\":13", list.Out);
        Assert.Contains("\"job\": \"Welcome\"", Cli("list", "--format", "json").Out);
        Assert.Equal("No jobs are waiting on other.\n", Cli("list", "other").Out);
        Assert.Equal("No failed jobs.\n", Cli("failed").Out);

        var worker = Cli("worker", "--queue", "mail", "--stop-when-empty");
        Assert.Equal(0, worker.Code);
        Assert.Contains("Ran 1 job.", worker.Out);
        Later(10);
        Assert.Equal(0, Cli("worker", "-q", "mail", "--once").Code);
        Later(60);
        var last = Cli("worker", "--queue", "mail", "--once", "--connection", "file");
        Assert.Equal(1, last.Code);
        Assert.Contains("Ran 1 job, 1 failed.", last.Out);

        var failed = Cli("failed");
        Assert.Contains("Validation: unlucky", failed.Out);
        Assert.Contains("3/3", failed.Out);
        Assert.Contains("\"failed_at\"", Cli("failed", "--format", "json").Out);
        string id = Driver().Failed()[0].Id;
        Assert.Equal("1 failed job is back on its queue.\n", Cli("retry", id).Out);
        Assert.Contains("There is no failed job", Cli("retry", id).Error);
        Assert.Contains("needs a failed job's id", Cli("forget").Error);
        Later(400);
        Cli("worker", "--queue", "mail", "--stop-when-empty", "--sleep", "0");
        Later(10);
        Cli("worker", "--queue", "mail", "--stop-when-empty", "--max-jobs", "5", "--max-time", "1h", "--concurrency", "2", "--sleep", "1s");
        Later(60);
        Cli("worker", "--queue", "mail", "--stop-when-empty");
        Assert.Equal("1 failed job deleted.\n", Cli("forget", "--all").Out);
        Assert.Equal("0 failed jobs are back on their queues.\n", Cli("retry", "--all").Out);
        Code("import \"jobs/welcome.kscript\"\ndispatch Welcome(user_id: 2, email: 'b@example.com')");
        Assert.Equal("Removed 1 waiting job from mail.\n", Cli("clear", "mail").Out);
        Assert.Equal("Removed 0 waiting jobs.\n", Cli("clear").Out);
        Assert.Equal("No jobs are waiting.\n", Cli("list").Out);
    }

    [Theory]
    [InlineData(new[] { "--connection" }, "--connection needs a name")]
    [InlineData(new[] { "--connection", "nope", "status" }, "There is no queue connection 'nope'")]
    [InlineData(new[] { "--project" }, "--project needs a folder")]
    [InlineData(new[] { "--format", "xml" }, "--format needs json or text")]
    [InlineData(new[] { "--bogus" }, "Unknown option '--bogus'")]
    [InlineData(new string[0], "Usage: kscript queue")]
    [InlineData(new[] { "dance" }, "Unknown queue command 'dance'")]
    public void TheQueueCommandRefusesBadArguments(string[] args, string message)
    {
        var result = Cli(args);
        Assert.Equal(1, result.Code);
        Assert.Contains(message, result.Error);
    }

    [Theory]
    [InlineData(new[] { "--connection" }, "--connection needs a name")]
    [InlineData(new[] { "--queue" }, "--queue needs queue names")]
    [InlineData(new[] { "--concurrency", "0" }, "--concurrency needs a number")]
    [InlineData(new[] { "--max-jobs", "x" }, "--max-jobs needs a number")]
    [InlineData(new[] { "--max-time", "x" }, "--max-time needs a time")]
    [InlineData(new[] { "--sleep", "x" }, "--sleep needs a time")]
    [InlineData(new[] { "--project" }, "--project needs a folder")]
    [InlineData(new[] { "--bogus" }, "Unknown option '--bogus'")]
    [InlineData(new[] { "--connection", "nope" }, "There is no queue connection 'nope'")]
    public void TheWorkerRefusesBadArguments(string[] args, string message)
    {
        var result = Cli(new[] { "worker" }.Concat(args).ToArray());
        Assert.Equal(1, result.Code);
        Assert.Contains(message, result.Error);
    }

    [Fact]
    public void TheWorkerSaysWhatItWorksAndStopsWhenAsked()
    {
        Assert.Contains("Usage: kscript worker", Cli("worker", "--help").Out);
        Assert.Contains("Usage: kscript queue", Cli("--help").Out);
        using (var quick = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            var said = new StringWriter(CultureInfo.InvariantCulture);
            QueueCommand.RunWorker(new[] { "--sleep", "0.01" }, said, new StringWriter(), quick.Token, Project);
            Assert.Contains("queues every queue, default first.", said.ToString());
        }
        UseProject("{ \"name\": \"" + app + "\", \"queue\": { \"default\": \"memory\", \"queues\": [\"high\", \"default\"], \"worker\": { \"concurrency\": 2, \"sleep\": \"1s\" } } }");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0, QueueCommand.RunWorker(new[] { "--project", Project }, output, error, stop.Token));
        Assert.Contains("Worker: memory (memory), queues high, default.", output.ToString());
        Assert.Contains("only holds jobs dispatched in this process", error.ToString());

        FileOf("kscript.json", "{ \"queue\": 5 }");
        Assert.Contains("queue should be an object", Cli("status").Error);
        FileOf("kscript.json", "{ broken");
        Assert.Equal(1, Cli("status").Code);
        Assert.Equal(1, Cli("worker").Code);
    }

    // ---- kscript.json -------------------------------------------------------------------------------------------------

    [Fact]
    public void QueueSettingsAreReadFromKscriptJson()
    {
        ProjectConfig config = ProjectConfig.Parse("""
            {
              "queue": {
                "default": "local",
                "connections": {
                  "local": { "driver": "file", "path": "storage/queue" },
                  "fast": { "driver": "Memory" },
                  "db": { "driver": "database", "connection": "main" },
                  "cache": { "driver": "redis", "url_env": "REDIS_URL" },
                  "odd": { "driver": "carrier-pigeon" },
                  "none": { "path": "x" },
                  "bad": 5,
                  "extra": { "driver": "file", "colour": "red" }
                },
                "queues": ["high", "default"],
                "failed": { "keep": "3d", "other": 1 },
                "worker": { "concurrency": 3, "sleep": "2s", "other": 1 },
                "env": { "test": { "default": "fast", "other": 1 }, "bad": 1 },
                "surprise": true
              }
            }
            """, Path.Combine(Project, "kscript.json"));
        QueueSettings q = config.Queue;
        Assert.Equal("local", q.Default);
        Assert.Equal("storage/queue", q.Connections["local"].Path);
        Assert.Equal("memory", q.Connections["fast"].Driver);
        Assert.False(q.Connections.ContainsKey("db"));
        Assert.Equal(new[] { "high", "default" }, q.Queues);
        Assert.Equal(3 * 86400, q.FailedKeep);
        Assert.Equal(3, q.WorkerConcurrency);
        Assert.Equal(2, q.WorkerSleep);
        Assert.Equal("fast", q.DefaultFor("test"));
        Assert.Equal("local", q.DefaultFor(null));
        string warnings = string.Join("\n", config.Warnings);
        Assert.Contains("the database driver isn't available yet", warnings);
        Assert.Contains("the redis driver isn't available yet", warnings);
        Assert.Contains("there is no queue driver 'carrier-pigeon'", warnings);
        Assert.Contains("queue.connections.none needs a driver", warnings);
        Assert.Contains("queue.connections.bad should be an object", warnings);
        Assert.Contains("unknown setting 'queue.connections.extra.colour'", warnings);
        Assert.Contains("unknown setting 'queue.failed.other'", warnings);
        Assert.Contains("unknown setting 'queue.worker.other'", warnings);
        Assert.Contains("unknown setting 'queue.env.test.other'", warnings);
        Assert.Contains("queue.env.bad should be an object", warnings);
        Assert.Contains("unknown setting 'queue.surprise'", warnings);

        var context = new QueueContext(config, Project, null);
        Assert.Equal(Path.Combine(Project, "storage", "queue"), ((FileQueueDriver)context.Driver(null)).Folder);
        Assert.Equal("/elsewhere/job.kscript", context.StoredPath("/elsewhere/job.kscript"));

        ProjectConfig bad = ProjectConfig.Parse("{ \"queue\": { \"default\": \"missing\", \"queues\": [], \"worker\": { \"concurrency\": 0 } } }", Path.Combine(Project, "kscript.json"));
        string badWarnings = string.Join("\n", bad.Warnings);
        Assert.Contains("queue.default names the connection 'missing'", badWarnings);
        Assert.Contains("queue.queues should be a list", badWarnings);
        Assert.Contains("concurrency should be a whole number", badWarnings);
        Assert.Contains("Did you mean", Assert.Throws<KScriptException>(() => new QueueContext(null, Project, null).Connection("fil")).Message);
    }

    // ---- syntax ---------------------------------------------------------------------------------------------------

    [Fact]
    public void JobAndDispatchConvertBetweenFormats()
    {
        const string code = """
            /// Says hello.
            job Hello(queue: "mail", tries: 2) {
              who: string

              fn handle() {
                echo "Hello {$who}"
              }
            }

            dispatch Hello(who: 'Joan')
            Hello(who: 'Ann')->run()
            """;
        MarkupDocument doc = MarkupParser.Parse(new SourceText(code), DocumentKind.Script, "x.kscript");
        Assert.Empty(doc.Diagnostics);
        string xml = ScriptConverter.ToXml(doc).Text;
        Assert.Contains("<job name=\"Hello\" queue=\"mail\" tries=\"2\" description=\"Says hello.\">", xml);
        Assert.Contains("<dispatch value=\"Hello(who: 'Joan')\" />", xml);
        string back = ScriptConverter.ToCode(MarkupParser.Parse(new SourceText(xml), DocumentKind.Script, "x.ks.xml")).Text;
        Assert.Contains("job Hello(queue: mail, tries: 2) {", back);
        Assert.Contains("dispatch Hello(who: 'Joan')", back);
        Assert.Contains("Hello(who: 'Ann')->run()", back);

        // dispatch(...) and job(...) without a name stay elements.
        MarkupDocument generic = MarkupParser.Parse(new SourceText("dispatch(value: Hello())\njob(name: X) {\n}\n"), DocumentKind.Script, "y.kscript");
        Assert.Equal(new[] { "dispatch", "job" }, generic.RootElements.First().ChildElements.Select(e => e.Name));
        Assert.Equal("job Bare\n", ScriptConverter.ToCode(MarkupParser.Parse(new SourceText("<kscript><job name=\"Bare\" /></kscript>"), DocumentKind.Script, "b.ks.xml")).Text);
        Assert.Contains("  <!-- note -->", ScriptConverter.ToXml(MarkupParser.Parse(new SourceText("job Noted {\n  // note\n  fn handle() {\n  }\n}\n"), DocumentKind.Script, "n.kscript")).Text);
        Assert.Contains("  // note\n", ScriptConverter.ToCode(MarkupParser.Parse(new SourceText("<kscript><job name=\"Noted\"><!-- note --><fn name=\"handle\" /></job></kscript>"), DocumentKind.Script, "n.ks.xml")).Text);
        MarkupDocument bare = MarkupParser.Parse(new SourceText("job Bare\n"), DocumentKind.Script, "b.kscript");
        Assert.Equal("Bare", bare.RootElements.First().ChildElements.Single().GetAttributeValue("name"));
        Assert.Contains("job Empty {\n  a: int\n}", ScriptConverter.ToCode(MarkupParser.Parse(new SourceText("<kscript><job name=\"Empty\" fields=\"a: int\" /></kscript>"), DocumentKind.Script, "z.ks.xml")).Text);
    }
}
