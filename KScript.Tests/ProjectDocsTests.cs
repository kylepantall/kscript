using System.Net;
using System.Net.Sockets;

using KScript.Docs;
using KScript.Projects;

namespace KScript.Tests;

/// <summary>kscript serve --with-docs: the docs' page about the project, and serving it next to the site.</summary>
public sealed class ProjectDocsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-project-docs-" + Guid.NewGuid().ToString("N"));

    public ProjectDocsTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private const string Strings = """
        import "lib/other.kscript" names: [shout]

        /// Lower-cases text and joins words with dashes.
        /// @param text What to turn into a slug.
        /// @returns The slug.
        /// @example @slugify('Hello World')
        fn slugify(text: string) => $text->lower()->replace(' ', '-')

        /// @deprecated Use slugify.
        fn old_slug(text) => @slugify($text)

        /// A size of parcel.
        enum Size { small, large }

        /// A user.
        type User {
          login: string
          id: int from user_id
        }

        const max_items = 10
        let counter = 0
        """;

    private void WriteProject()
    {
        Write("kscript.json", """
            {
              "name": "shop",
              "make": { "format": "xml" },
              "format": { "indent": 4, "printWidth": 100 },
              "lint": { "rules": { "KS6001": "off" }, "exclude": ["drafts"] },
              "packages": ["KScript.IO"],
              "paths": { "lib": "app/lib" }
            }
            """);
        Write("lib/strings.kscript", Strings);
        Write("lib/other.kscript", "fn shout(text) => $text->upper()\n");
        Write("lib/strings.test.kscript", "fn not_listed() => 1\n");
        Write("drafts/draft.kscript", "fn draft() => 1\n");
        Write("_templates/route.kscript", "fn template() => 1\n");
        Write(".hidden/secret.kscript", "fn secret() => 1\n");
        Write("node_modules/pkg/x.kscript", "fn module() => 1\n");
        Write("empty.ks.xml", "<KScript><echo>hi</echo></KScript>\n");
        Write("routes.ks.xml", "<KScript.Routes>\n  <Get path=\"/hi\" name=\"hi\"><return value=\"'hi'\" /></Get>\n</KScript.Routes>\n");
    }

    [Fact]
    public void The_guide_lists_what_each_script_declares_and_the_routes()
    {
        WriteProject();
        ProjectConfig project = ProjectConfig.FindAndLoad(dir);
        ProjectGuide guide = ProjectGuide.Read(dir, project);

        Assert.Equal("shop", guide.Name);
        Assert.Equal("xml", guide.Format);
        Assert.Equal(new[] { "lib/other.kscript", "lib/strings.kscript" }, guide.Files.Select(f => f.Path));
        ProjectGuide.ScriptFile strings = guide.Files[1];
        Assert.Equal("kscript", strings.Format);
        // shout is imported, not declared here, and counter isn't a constant.
        Assert.Equal(new[] { "slugify", "old_slug", "Size", "User", "max_items" }, strings.Declarations.Select(d => d.Name));
        Assert.Equal(new[] { "function", "function", "enum", "type", "constant" }, strings.Declarations.Select(d => d.Kind));

        ProjectGuide.Declaration slugify = strings.Declarations[0];
        Assert.Equal("@slugify(text: string): string", slugify.Signature);
        Assert.Equal("Lower-cases text and joins words with dashes.", slugify.Summary);
        Assert.Equal(7, slugify.Line);
        Assert.Null(slugify.Xml);
        Assert.Contains("<field name=\"id\" type=\"int\" from=\"user_id\" />", strings.Declarations[3].Xml);

        ProjectGuide.RouteRow route = Assert.Single(guide.Routes);
        Assert.Equal(("GET", "/hi", "hi", "inline", "routes.ks.xml:2"), (route.Methods, route.Path, route.Name, route.Target, route.File));
        Assert.Null(guide.RoutesError);
    }

    [Fact]
    public void The_project_page_shows_conventions_routes_and_code()
    {
        WriteProject();
        ProjectConfig project = ProjectConfig.FindAndLoad(dir);
        DocsSite site = DocsSite.Build();
        site.SetProject(ProjectGuide.Read(dir, project));
        DocPage page = site.Find(DocsSite.ProjectPath);

        Assert.Equal(DocsSite.ProjectSection, site.Navigation().First().Section);
        Assert.Contains("<code>.ks.xml</code> files (<code>make.format</code> in kscript.json)", page.Body);
        Assert.Contains("4 spaces per indent, lines up to 100 characters", page.Body);
        Assert.Contains("<code>KS6001</code> off", page.Body);
        Assert.Contains("href=\"/reference/io\"", page.Body);
        Assert.Contains("lib in <code>app/lib/</code>", page.Body);
        Assert.Contains("<code>/hi</code>", page.Body);
        Assert.Contains("id=\"fn-slugify\"", page.Body);
        Assert.Contains("<td><code>text</code></td><td>What to turn into a slug.</td>", page.Body);
        Assert.Contains("<strong>Returns</strong> The slug.", page.Body);
        Assert.Contains("@slugify(&#39;Hello World&#39;)", page.Body);
        Assert.Contains("class=\"entry deprecated\" id=\"fn-old-slug\"", page.Body);
        Assert.Contains("type User {", page.Body);
        Assert.Contains("In <code>lib/strings.kscript</code>, line 7.", page.Body);
        Assert.Contains(page.Toc, t => t.Text == "lib/strings.kscript");

        Assert.Contains(site.Search, s => s.Title == "@slugify" && s.Kind == "project" && s.Url == "/project#fn-slugify");
        Assert.Contains(site.Search, s => s.Title == "User" && s.Url == "/project#type-user");
        Assert.Contains(site.Search, s => s.Title == "GET /hi" && s.Url == "/project#routes");

        // Setting it again replaces the page and its search entries.
        int count = site.Search.Count;
        site.SetProject(ProjectGuide.Read(dir, project));
        Assert.Equal(count, site.Search.Count);
        Assert.Single(site.Pages, p => p.Path == DocsSite.ProjectPath);
    }

    [Fact]
    public void A_folder_without_kscript_json_gets_the_defaults()
    {
        Write("site/page.html", "<h1>Hi</h1>");
        DocsSite site = DocsSite.Build();
        ProjectGuide guide = ProjectGuide.Read(Path.Combine(dir, "site"), null);
        site.SetProject(guide);
        string body = site.Find(DocsSite.ProjectPath).Body;
        Assert.Equal("site", guide.Name);
        Assert.Contains("There is no kscript.json", body);
        Assert.Contains("the default; <code>make.format</code> in kscript.json changes it", body);
        Assert.Contains("No script in the project declares", body);
        Assert.DoesNotContain("id=\"routes\"", body);

        // A folder that isn't there has nothing in it, and no routes.
        Assert.Empty(ProjectGuide.Read(Path.Combine(dir, "nowhere"), null).Files);
        // A file that can't be read declares nothing.
        Assert.Empty(ProjectGuide.ReadScript(Path.Combine(dir, "gone.kscript"), dir).Declarations);
    }

    [Fact]
    public void Routes_that_cant_be_read_are_reported_on_the_page()
    {
        Write("broken/routes.ks.xml", "<KScript.Routes>\n  <Get path=\"/a\" name=\"x\" view=\"a\" />\n  <Get path=\"/b\" name=\"x\" view=\"b\" />\n</KScript.Routes>\n");
        ProjectGuide guide = ProjectGuide.Read(Path.Combine(dir, "broken"), null);
        Assert.Contains("Two routes are named x", guide.RoutesError);
        Assert.Empty(guide.Routes);
        DocsSite site = DocsSite.Build();
        site.SetProject(guide);
        Assert.Contains("The routes couldn't be read", site.Find(DocsSite.ProjectPath).Body);

        Write("missing/kscript.json", "{ \"http\": { \"routes\": [\"nope.ks.xml\"] } }");
        guide = ProjectGuide.Read(Path.Combine(dir, "missing"), ProjectConfig.FindAndLoad(Path.Combine(dir, "missing")));
        Assert.Contains("nope.ks.xml", guide.RoutesError);
    }

    [Fact]
    public void A_big_project_lists_its_first_scripts()
    {
        int max = ProjectGuide.MaxFiles;
        try
        {
            ProjectGuide.MaxFiles = 2;
            for (int i = 0; i < 4; i++) Write($"lib/f{i}.kscript", $"fn f{i}() => {i}\n");
            ProjectGuide guide = ProjectGuide.Read(dir, null);
            Assert.True(guide.Truncated);
            Assert.Equal(2, guide.Files.Count);
            DocsSite site = DocsSite.Build();
            site.SetProject(guide);
            Assert.Contains("more than 2 scripts", site.Find(DocsSite.ProjectPath).Body);
        }
        finally
        {
            ProjectGuide.MaxFiles = max;
        }
    }

    [Fact]
    public void The_page_is_read_again_when_a_script_changes()
    {
        WriteProject();
        ProjectConfig project = ProjectConfig.FindAndLoad(dir);
        string output = dir + "-out";
        var docs = new DocsCommand.ProjectDocs(DocsSite.Build(), dir, project, output);
        string page = Path.Combine(output, "_pages", "project.html");
        Assert.Contains("fn-slugify", File.ReadAllText(page));

        Assert.False(docs.Refresh("/guide/language"));
        Assert.False(docs.Refresh(DocsSite.ProjectPath));
        Write("lib/more.kscript", "/// Says hello.\nfn hello() => 'hi'\n");
        Assert.True(docs.Refresh(DocsSite.ProjectPath));
        Assert.Contains("fn-hello", File.ReadAllText(page));
        Assert.Contains("@hello", File.ReadAllText(Path.Combine(output, "assets", "search.json")));
        Assert.False(docs.Refresh("/assets/search.json"));
        Directory.Delete(output, true);
    }

    [Fact]
    public void Serve_settings_for_the_docs_are_read_from_kscript_json()
    {
        var config = ProjectConfig.Parse("""{ "serve": { "docs": true, "docs_port": 4200 } }""", Path.Combine(dir, ProjectConfig.FileName));
        Assert.True(config.Serve.Docs);
        Assert.Equal(4200, config.Serve.DocsPort);
        Assert.Empty(config.Warnings);

        config = ProjectConfig.Parse("""{ "serve": { "docs": "yes", "docs_port": 0, "nope": 1 } }""", Path.Combine(dir, ProjectConfig.FileName));
        Assert.False(config.Serve.Docs);
        Assert.Null(config.Serve.DocsPort);
        Assert.Contains("serve.docs should be true or false.", config.Warnings);
        Assert.Contains("serve.docs_port should be a number from 1 to 65535.", config.Warnings);
        Assert.Contains("unknown setting 'serve.nope' (expected root, port, host, docs or docs_port).", config.Warnings);
    }

    [Fact]
    public void The_docs_run_next_to_the_site_until_it_stops()
    {
        WriteProject();
        int sitePort = TestServer.FreePort();
        int docsPort = TestServer.FreePort();
        using var stop = new CancellationTokenSource();
        // The server writes from its own thread: write through a synchronized writer and read under its lock,
        // or reading the StringBuilder while it grows can throw.
        var output = new StringWriter();
        TextWriter shared = TextWriter.Synchronized(output);
        string Printed()
        {
            lock (shared) return output.ToString();
        }
        var server = Task.Run(() => new CommandLine { Out = shared, Error = TextWriter.Null, Cancellation = stop.Token }
            .Run(new[] { "serve", dir, "--port", sitePort.ToString(), "--docs-port", docsPort.ToString() }));

        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{docsPort}") };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!Printed().Contains("Docs for shop") && DateTime.UtcNow < deadline && !server.IsCompleted) Thread.Sleep(50);
        Assert.Contains($"Docs for shop: http://localhost:{docsPort}/project", Printed());
        string page = http.GetStringAsync("/project").Result;
        Assert.Contains("data-default-format=\"xml\"", page);
        Assert.Contains("fn-slugify", page);
        Assert.Contains("@slugify", http.GetStringAsync("/assets/search.json").Result);

        stop.Cancel();
        Assert.Equal(0, server.Result);
    }

    [Fact]
    public void The_docs_are_off_for_compiled_sites_production_and_no_docs()
    {
        Write("kscript.json", "{ \"name\": \"shop\", \"serve\": { \"docs\": true } }");
        Write("index.html", "<h1>Hi</h1>");
        string manifest = Write("compiled/kscript-app/kscript-app.json", "{ \"name\": \"Shop\", \"kind\": \"site\", \"main\": \"app\" }");
        Write("compiled/kscript-app/app/kscript.json", "{ \"name\": \"shop\", \"serve\": { \"docs\": true } }");
        Write("compiled/kscript-app/app/index.html", "<h1>Hi</h1>");
        foreach (var (args, compiled) in new[]
        {
            (new[] { "serve", dir, "--no-docs" }, false),
            (new[] { "serve", dir, "--env", "production" }, false),
            (new[] { "--with-docs" }, true),
        })
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var output = new StringWriter();
            var error = new StringWriter();
            var cli = new CommandLine { Out = output, Error = error, Cancellation = stop.Token };
            string[] all = args.Concat(new[] { "--port", TestServer.FreePort().ToString() }).ToArray();
            string env = Environment.GetEnvironmentVariable("KSCRIPT_ENV");
            int code;
            try
            {
                code = compiled ? cli.RunCompiled(manifest, all) : cli.Run(all);
            }
            finally
            {
                // --env sets KSCRIPT_ENV for the scripts it runs.
                Environment.SetEnvironmentVariable("KSCRIPT_ENV", env);
            }
            Assert.True(code == 0, error.ToString());
            Assert.DoesNotContain("Docs for", output.ToString());
            if (args.Contains("production")) Assert.Contains("Docs: not served in the production environment.", error.ToString());
        }
    }

    [Fact]
    public void Docs_options_on_the_command_line()
    {
        var error = new StringWriter();
        var cli = new CommandLine { Out = TextWriter.Null, Error = error };
        Assert.Equal(1, cli.Run(new[] { "serve", "--docs-port", "x" }));
        Assert.Contains("--docs-port needs a number", error.ToString());
        Assert.Equal(1, cli.Run(new[] { "serve", "--docs-port", "70000" }));
    }

    [Fact]
    public void A_docs_port_in_use_leaves_the_site_running_without_docs()
    {
        using var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        int port = ((IPEndPoint)busy.LocalEndpoint).Port;
        var error = new StringWriter();
        Assert.Equal(1, DocsCommand.ServeProjectAsync(dir, null, port, 8080, TextWriter.Null, error, CancellationToken.None).Result);
        Assert.Contains($"Docs: port {port} is in use", error.ToString());

        // The site's own port isn't free for the docs either.
        error = new StringWriter();
        Assert.Equal(1, DocsCommand.ServeProjectAsync(dir, null, 9123, 9123, TextWriter.Null, error, CancellationToken.None).Result);
        Assert.Contains("Docs: port 9123 is in use", error.ToString());
    }

    [Fact]
    public void Docs_pick_another_port_when_the_site_has_theirs()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Equal(0, DocsCommand.ServeProjectAsync(dir, null, null, DocsCommand.DefaultPort, TextWriter.Null, TextWriter.Null, stop.Token).Result);
    }

    [Fact]
    public void Docs_report_a_temp_folder_they_cant_write_to()
    {
        string notAFolder = Write("temp-is-a-file", "x");
        string[] names = { "TMPDIR", "TMP", "TEMP" };
        var previous = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        var error = new StringWriter();
        int code;
        try
        {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, notAFolder);
            code = DocsCommand.ServeProjectAsync(dir, null, null, 8080, TextWriter.Null, error, CancellationToken.None).Result;
        }
        finally
        {
            foreach (var kv in previous) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
        Assert.Equal(1, code);
        Assert.Contains("Could not start the docs server", error.ToString());
    }

    [Fact]
    public void The_projects_docs_are_written_as_markdown_once_turned_on()
    {
        WriteProject();
        Write("lib/strings.kscript", Strings.Replace("/// @example @slugify('Hello World')", "/// @example\n/// @slugify('a')\n/// @slugify('b')"));
        var output = new ProjectDocsOutput();
        Assert.Equal("docs", output.Name);
        Assert.False(output.IsEnabled(dir));
        Assert.False(output.IsEnabled(null));
        Assert.True(output.Owns(".kscript/docs/project.md"));
        Assert.False(output.Owns("lib/strings.kscript"));

        var stdout = new StringWriter();
        Assert.Equal(0, ProjectDocsOutput.Start(Path.Combine(dir, "lib"), stdout, TextWriter.Null));
        Assert.Contains("Wrote ", stdout.ToString());
        Assert.True(output.IsEnabled(dir));
        string md = File.ReadAllText(Path.Combine(dir, ProjectDocsOutput.File));
        Assert.StartsWith("# shop\n", md);
        Assert.Contains("- New scripts, routes and tests are `.ks.xml` files.", md);
        Assert.Contains("`KS6001` off", md);
        Assert.Contains("- Every script can use `KScript.IO` without importing it.", md);
        Assert.Contains("| GET | `/hi` | `hi` | inline |", md);
        Assert.Contains("```xml\n<import path=\"lib/strings.kscript\" />\n```", md);
        Assert.Contains("### @slugify(text: string): string (fn)", md);
        Assert.Contains("- `text`: What to turn into a slug.", md);
        Assert.Contains("Returns The slug.", md);
        Assert.Contains("Example:\n\n```\n@slugify('a')\n@slugify('b')\n```", md);
        Assert.Contains("**Deprecated.** Use slugify.", md);
        Assert.Contains("<field name=\"id\" type=\"int\" from=\"user_id\" />", md);
        Assert.Contains("In `lib/strings.kscript`, line 9.", md);

        // Nothing changed: nothing is written, and check agrees.
        Assert.Empty(output.Write(dir, check: false));
        stdout = new StringWriter();
        Assert.Equal(0, ProjectDocsOutput.Start(dir, stdout, TextWriter.Null));
        Assert.Contains("Up to date:", stdout.ToString());

        // A change is found by check without writing, then written.
        Write("lib/more.kscript", "fn hello() => 'hi'\n");
        Assert.Equal(new[] { ProjectDocsOutput.File }, output.Write(dir, check: true));
        Assert.DoesNotContain("@hello", File.ReadAllText(Path.Combine(dir, ProjectDocsOutput.File)));
        Assert.Equal(new[] { ProjectDocsOutput.File }, output.Write(dir, check: false));
        Assert.Contains("### @hello()", File.ReadAllText(Path.Combine(dir, ProjectDocsOutput.File)));

        // kscript registers it, so kscript watch keeps it up to date.
        CommandLine.AddProjectOutputs();
        Assert.Contains(KScript.Language.Outputs.ProjectOutputs.All, o => o is ProjectDocsOutput);
        Assert.Equal(0, new CommandLine { Out = TextWriter.Null, Error = TextWriter.Null }.Run(new[] { "watch", dir, "--check" }));
    }

    [Fact]
    public void The_markdown_follows_the_projects_format_and_says_what_is_missing()
    {
        Write("site/lib/a.kscript", "const size = 3\n");
        Write("site/routes.ks.xml", "<KScript.Routes>\n  <Get path=\"/a\" name=\"x\" view=\"a\" />\n  <Get path=\"/b\" name=\"x\" view=\"b\" />\n</KScript.Routes>\n");
        string md = ProjectDocsOutput.Markdown(ProjectGuide.Read(Path.Combine(dir, "site"), null));
        Assert.Contains("```kscript\nimport \"lib/a.kscript\"\n```", md);
        Assert.Contains("```kscript\nconst size = 3\n```", md);
        Assert.Contains("The routes couldn't be read: ", md);
        Assert.DoesNotContain("Standard:", md);

        Write("empty/index.html", "<h1>Hi</h1>");
        Assert.Contains("No script declares", ProjectDocsOutput.Markdown(ProjectGuide.Read(Path.Combine(dir, "empty"), null)));

        int max = ProjectGuide.MaxFiles;
        try
        {
            ProjectGuide.MaxFiles = 0;
            Assert.Contains("more than 0 scripts", ProjectDocsOutput.Markdown(ProjectGuide.Read(Path.Combine(dir, "site"), null)));
        }
        finally
        {
            ProjectGuide.MaxFiles = max;
        }
    }

    [Fact]
    public void Docs_project_writes_them_from_the_command_line_and_reports_failures()
    {
        Write("p/kscript.json", "{ \"name\": \"p\" }");
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.Combine(dir, "p"));
            var output = new StringWriter();
            Assert.Equal(0, DocsCommand.Run(new[] { "--project" }, output, TextWriter.Null));
            Assert.True(File.Exists(Path.Combine(dir, "p", ProjectDocsOutput.File)));
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }

        // .kscript is a file, so the folder can't be made.
        Write("q/.kscript", "x");
        var error = new StringWriter();
        Assert.Equal(1, ProjectDocsOutput.Start(Path.Combine(dir, "q"), TextWriter.Null, error));
        Assert.Contains("Could not write the project's docs", error.ToString());
    }
}
