using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

using KScript.Runtime;

namespace KScript.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-log-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 11, 20, 12, 30, 15, TimeSpan.Zero);

    public LoggingTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private void FileOf(string name, string text) => File.WriteAllText(Path.Combine(dir, name), text);

    private (string Output, string Error, KScriptHost Host) RunWith(string body, Action<KScriptHost> setup = null, bool interactive = false)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var error = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var host = new KScriptHost(output, error, new StringReader(string.Empty), interactive);
        host.Logs.Clock = () => Noon;
        setup?.Invoke(host);
        new Interpreter(host, options: new InterpreterOptions { Timeout = TimeSpan.FromSeconds(20) })
            .RunText("<kscript>" + body + "</kscript>", Path.Combine(dir, "job.ks.xml"));
        return (output.ToString(), error.ToString(), host);
    }

    private string Errors(string body, Action<KScriptHost> setup = null, bool interactive = false) => RunWith(body, setup, interactive).Error;

    private KScriptException Fails(string body) => Assert.Throws<KScriptException>(() => RunWith(body));

    [Fact]
    public void By_default_info_and_above_go_to_stderr_as_text()
    {
        var (output, error, _) = RunWith("<echo>out</echo><log>Started</log><log level=\"debug\">hidden</log><do>$log->warn('Low disk', { free: 5, path: '/var log', q: '' })</do>");
        Assert.Equal("out\n", output);
        Assert.Equal("12:30:15 INFO  Started\n12:30:15 WARN  Low disk free=5 path=\"/var log\" q=\"\"\n", error);
    }

    [Fact]
    public void Levels_categories_and_fields_come_from_config()
    {
        FileOf("config.yaml", "logging:\n  level: warn\n  levels:\n    db: debug\n    db.noisy: off\n  providers:\n    - type: memory\n");
        var (output, error, _) = RunWith(
            "<log>skipped</log><log level=\"error\" category=\"pay\" fields=\"{ id: 7 }\">Declined</log>" +
            "<let name=\"db\" value=\"$log->named('db')->with({ host: 'x' })\" /><do>$db->debug('Query'); $db->named('noisy')->error('never'); $db->named('slow')->trace('no')</do>" +
            "<for each=\"e\" in=\"$log->entries()\"><echo>{$e.level} {$e.category} {$e.message} { @to_json($e.fields) } {$e.script} {$e.line ?? '-'}</echo></for>" +
            "<echo>{ $log->enabled('info') } { $db->enabled('debug') } { $db.category } { $db.fields.host } {$log} {$db}</echo>");
        Assert.Equal("error pay Declined {\"id\":7} job.ks.xml 1\ndebug db Query {\"host\":\"x\"} job.ks.xml -\nfalse true db x logger logger (db)\n", output);
        Assert.Equal("", error);
    }

    [Fact]
    public void File_provider_writes_text_or_json_lines_and_rolls_daily()
    {
        FileOf("config.json", "{ \"logging\": { \"level\": \"trace\", \"providers\": [ { \"type\": \"file\", \"path\": \"logs/app.log\" }, { \"type\": \"file\", \"path\": \"logs/app.json\", \"format\": \"json\", \"roll\": \"daily\", \"level\": \"info\" } ] } }");
        var (_, error, _) = RunWith("<do>$log->trace('t'); $log->info('Saved', { id: 3, tags: ['a'] }); $log->log('fatal', 'Boom', 'not a map')</do>");
        Assert.Equal("", error);
        Assert.Equal(
            "2026-11-20T12:30:15.0000000+00:00 TRACE t\n2026-11-20T12:30:15.0000000+00:00 INFO  Saved id=3 tags=[\"a\"]\n2026-11-20T12:30:15.0000000+00:00 FATAL Boom value=\"not a map\"\n",
            File.ReadAllText(Path.Combine(dir, "logs", "app.log")));
        string[] json = File.ReadAllLines(Path.Combine(dir, "logs", "app-2026-11-20.json"));
        Assert.Equal(2, json.Length);
        Assert.Equal("{\"time\":\"2026-11-20T12:30:15.0000000+00:00\",\"level\":\"info\",\"message\":\"Saved\",\"script\":\"job.ks.xml\",\"id\":3,\"tags\":[\"a\"]}", json[0]);
    }

    [Fact]
    public void Function_providers_get_each_entry_and_can_log_without_looping()
    {
        var (output, _, _) = RunWith(
            "<fn name=\"alert\" params=\"entry\"><echo>ALERT {$entry.level}: {$entry.message} ({$entry.fields.order})</echo><do>$log->error('inside')</do></fn>" +
            "<log-provider fn=\"alert\" level=\"error\" replace=\"yes\" />" +
            "<log level=\"warn\">ignored</log><log level=\"error\" fields=\"{ order: 9 }\">Card declined</log>" +
            "<log-provider type=\"memory\" /><let name=\"hook\" value=\"fn($e) => @echo('fn ' + $e.message)\" />");
        Assert.Equal("ALERT error: Card declined (9)\n", output);
    }

    [Fact]
    public void Function_providers_from_config_and_errors_as_fields()
    {
        FileOf("config.yaml", "logging:\n  providers:\n    - fn: notify\n    - type: console\n      stream: stdout\n      format: json\n      level: error\n");
        var (output, _, _) = RunWith(
            "<fn name=\"notify\" params=\"entry\"><echo>notify {$entry.message} {$entry.fields.error} {$entry.fields.error_type} {$entry.fields.error_line}</echo></fn>" +
            "<try><throw message=\"bad thing\" /><catch as=\"err\"><do>$log->error('Failed', $err)</do></catch></try>");
        Assert.Equal("notify Failed bad thing Error 1\n{\"time\":\"2026-11-20T12:30:15.0000000+00:00\",\"level\":\"error\",\"message\":\"Failed\",\"script\":\"job.ks.xml\",\"error\":\"bad thing\",\"error_type\":\"Error\",\"error_line\":1}\n", output);
    }

    [Fact]
    public void Console_colours_at_a_terminal_and_time_formats()
    {
        string error = Errors("<log-provider type=\"console\" replace=\"yes\" time=\"\" /><do>$log->debug('d'); $log->info('i'); $log->warn('w'); $log->error('e')</do><config defaults=\"{}\" />", h => h.Logs.Level = LogLevel.Debug, interactive: true);
        Assert.Equal("\u001b[90mDEBUG d\u001b[0m\n\u001b[36mINFO  i\u001b[0m\n\u001b[33mWARN  w\u001b[0m\n\u001b[31mERROR e\u001b[0m\n", error);
        Assert.Equal("12:30 INFO  plain\n", Errors("<log-provider type=\"console\" replace=\"yes\" colour=\"no\" time=\"HH:mm\" /><log>plain</log>", interactive: true));
    }

    [Fact]
    public void Bad_logging_settings_are_reported_clearly()
    {
        FileOf("config.yaml", "logging: 3\n");
        Assert.Contains("logging should be a map", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  level: loud\n");
        Assert.Contains("logging.level: 'loud' isn't a log level", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  levels: [1]\n");
        Assert.Contains("logging.levels should be a map", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers: { type: file }\n");
        Assert.Contains("logging.providers should be a list", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers: [1]\n");
        Assert.Contains("logging.providers[0] should be a map", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - level: info\n");
        Assert.Contains("logging.providers[0] needs a type", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - type: carrier-pigeon\n");
        Assert.Contains("there is no log provider type 'carrier-pigeon'. Types: console, file, memory, fn.", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - type: file\n");
        Assert.Contains("logging.providers[0] needs a path", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - fn: missing\n");
        Assert.Contains("there is no function called missing", Fails("<log>x</log>").Message);
        File.Delete(Path.Combine(dir, "config.yaml"));

        FileOf("config.yaml", "logging:\n  providers:\n    - type: console\n      format: xml\n");
        Assert.Contains("logging.providers[0]: format should be text or json", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - type: console\n      stream: tty\n");
        Assert.Contains("stream should be stdout or stderr", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - type: file\n      path: a.log\n      roll: weekly\n");
        Assert.Contains("roll should be never or daily", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - type: memory\n      level: loud\n");
        Assert.Contains("logging.providers[0] level: 'loud' isn't a log level", Fails("<log>x</log>").Message);
        FileOf("config.yaml", "logging:\n  providers:\n    - type: console\n      colour: off\n      stream: stdout\n    - type: console\n      colour: true\n      stream: stdout\n");
        Assert.Equal("12:30:15 INFO  x\n\u001b[36m12:30:15 INFO  x\u001b[0m\n", RunWith("<log>x</log>").Output);
        File.Delete(Path.Combine(dir, "config.yaml"));
        Assert.Contains("there is no function called nope", Fails("<log-provider fn=\"nope\" />").Message);
        Assert.Contains("fn should be a function", Fails("<let name=\"n\" value=\"1\" /><log-provider fn=\"n\" />").Message);
        Assert.Contains("level=\"loud\" isn't one of", Fails("<log level=\"loud\">x</log>").Message);
        Assert.Contains("$log->log() level", Fails("<do>$log->log('loud', 'x')</do>").Message);
    }

    [Fact]
    public void A_failing_provider_is_reported_once_and_the_others_keep_going()
    {
        var failing = new Failing();
        var (_, error, host) = RunWith("<log-provider type=\"memory\" /><do>$log->info('a'); $log->info('b'); $log->flush()</do><echo>{ $log->entries()->count() }</echo>",
            h => h.Logs.AddProvider(failing, LogLevel.Info, "broken"));
        Assert.Equal("   ERROR  Log provider 'broken' failed.\n          Error: nope\n", error.Replace("12:30:15 INFO  a\n12:30:15 INFO  b\n", string.Empty));
        Assert.Equal(2, failing.Writes);
        Assert.Contains(host.Logs.Providers, p => p is MemoryLogProvider);
    }

    private sealed class Failing : ILogProvider
    {
        public int Writes;

        public void Write(LogEntry entry)
        {
            Writes++;
            throw new InvalidOperationException("nope");
        }

        public void Flush() => throw new InvalidOperationException("flush");
    }

    [Fact]
    public void Pages_and_read_only_hosts_cannot_log_to_files()
    {
        var ex = Assert.Throws<KScriptException>(() => RunWith("<log-provider type=\"file\" path=\"x.log\" />", h => h.CanWriteFiles = false));
        Assert.Contains("Logging to a file can't change files here", ex.Message);
    }

    [Fact]
    public void Http_provider_from_KScript_Net_posts_batches_as_json()
    {
        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var bodies = new List<string>();
        var headers = new List<string>();
        var serve = Task.Run(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                HttpListenerContext ctx = listener.GetContext();
                using (var reader = new StreamReader(ctx.Request.InputStream)) bodies.Add(reader.ReadToEnd());
                headers.Add(ctx.Request.Headers["X-Key"]);
                ctx.Response.StatusCode = i == 2 ? 500 : 200;
                ctx.Response.Close();
            }
        });

        var (_, error, _) = RunWith("<import package=\"KScript.Net\" />" +
            $"<log-provider type=\"http\" url=\"http://localhost:{port}/logs\" batch=\"2\" headers=\"{{ 'X-Key': 'k1' }}\" replace=\"yes\" />" +
            "<do>$log->info('one'); $log->info('two'); $log->warn('three')</do>");
        Assert.Equal("", error);
        var (_, error2, _) = RunWith("<import package=\"KScript.Net\" />" +
            $"<log-provider type=\"http\" url=\"http://localhost:{port}/logs\" replace=\"yes\" /><log>lost</log>");
        serve.Wait(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "one", "two" }, JsonDocument.Parse(bodies[0]).RootElement.EnumerateArray().Select(e => e.GetProperty("message").GetString()));
        Assert.Equal("three", JsonDocument.Parse(bodies[1]).RootElement[0].GetProperty("message").GetString());
        Assert.Equal("k1", headers[0]);
        Assert.Contains("Log provider 'http' failed.", error2);
        Assert.Contains("answered 500", error2);
    }

    [Fact]
    public void Http_provider_options_are_checked_and_need_the_import()
    {
        Assert.Contains("there is no log provider type 'http'", Fails("<log-provider type=\"http\" url=\"http://x\" />").Message);
        Assert.Contains("url should be an http or https address", Fails("<import package=\"KScript.Net\" /><log-provider type=\"http\" url=\"ftp://x\" />").Message);
        Assert.Contains("needs a url", Fails("<import package=\"KScript.Net\" /><log-provider type=\"http\" />").Message);
        Assert.Contains("headers should be a map", Fails("<import package=\"KScript.Net\" /><log-provider type=\"http\" url=\"http://x\" headers=\"[1]\" />").Message);
        Assert.Contains("batch should be 1 or more", Fails("<import package=\"KScript.Net\" /><log-provider type=\"http\" url=\"http://x\" batch=\"0\" />").Message);
        Assert.Contains("Types: console, file, memory, http, fn", Fails("<import package=\"KScript.Net\" /><log-provider type=\"pigeon\" />").Message);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public void Entries_maps_levels_and_hosts()
    {
        Assert.Equal(LogLevel.Warn, LogLevels.Parse("Warning", "x"));
        Assert.Equal("fatal", LogLevels.Name(LogLevel.Fatal));
        var entry = new LogEntry { Time = Noon, Level = LogLevel.Info, Message = "m", Category = "c", Line = 4, Fields = { ["a"] = 1.0 } };
        Assert.Equal("INFO  [c] m a=1", entry.ToText(null));
        Assert.Contains("\"category\":\"c\"", entry.ToJson());
        Assert.Contains("\"line\":4", entry.ToJson());
        Assert.Equal(4.0, entry.ToMap()["line"]);
        Assert.Null(new LogEntry { Time = Noon, Message = "m" }.ToMap()["line"]);

        var host = KScriptHost.ForText(new StringWriter());
        var hub = host.Logs;
        Assert.Same(hub, host.Logs);
        hub.SetCategoryLevel("a.b", LogLevel.Error);
        Assert.Equal(LogLevel.Error, hub.LevelFor("a.b.c"));
        Assert.Equal(LogLevel.Info, hub.LevelFor("a"));
        Assert.False(hub.IsEnabled(LogLevel.Off, null));
        var context = new LogProviderContext();
        Assert.Equal("the log provider", context.Where);
        hub.ClearProviders();
        Assert.Empty(hub.Providers);
        host.FlushLogs();
        Assert.Equal("logger", new LoggerValue(null, null).TypeName);
        Assert.False(new LoggerValue(null, null).TryGetMember("x", out _));
        Assert.False(new LoggerValue(null, null).TryCallMethod(null, "nope", out _));
        Assert.Equal("KScript.Net", PackageRegistry.Default.Get("KScript.Net").LogProviders["http"].Package);
        Assert.NotEmpty(PackageRegistry.Default.Get("KScript.Net").LogProviders["http"].Description);
    }
}
