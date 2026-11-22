using System.Text.Json.Nodes;
using KScript.Language.Agents;
using KScript.Language.Catalog;
using KScript.LanguageServer;
using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax.Markup;

namespace KScript.Language.Tests
{
    /// <summary>A package for agent notes: an element with notes, a command with an XML example, and a bad key.</summary>
    internal sealed class AgentNotesPackage : KScriptPackage
    {
        public static bool BadKey;
        public static bool BadElement;

        public override string Name => "KScript.AgentNotes";

        public override string Description => "Agent notes for tests.";

        protected override void Register(PackageBuilder p)
        {
            p.Element(new ElementInfo("beep", "Beeps.", ContentKind.Empty, new AttributeInfo("times", AttributeKind.Expression, "How often.", required: true), new AttributeInfo("loud", AttributeKind.Bool, "Louder.")), null);
            p.Command("ping", "host", "Pings a host.", KType.String, c => "pong", example: "<echo>{ @ping('a') }</echo>");
            p.AgentNotes(("<beep>", new AgentNotes(llm: "Make a noise.", avoid: "Beeping in a loop.")), ("@ping", new AgentNotes(pitfall: "Slow.")));
            if (BadKey) p.AgentNotes(("@nope", new AgentNotes()));
            if (BadElement) p.AgentNotes(("<nope>", new AgentNotes()));
        }
    }

    /// <summary>A package with nothing to call.</summary>
    internal sealed class EmptyAgentPackage : KScriptPackage
    {
        public override string Name => "KScript.Empty";

        public override string Description => "Nothing here.";

        protected override void Register(PackageBuilder p)
        {
        }
    }

    /// <summary>kscript agents: AGENTS.md and .kscript/agents from the code graph, the agent doc tags, and check --format json.</summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class AgentDocsTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-agents-" + Guid.NewGuid().ToString("N"));

        public AgentDocsTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, recursive: true);

        private string Write(string name, string text)
        {
            string path = Path.Combine(dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        private string Read(string name) => File.ReadAllText(Path.Combine(dir, name));

        private static (int Code, string Output) Agents(params string[] args)
        {
            var output = new StringWriter();
            int code = AgentsCommand.Run(args, output);
            return (code, output.ToString());
        }

        private const string Tasks = """
            import KScript.Math

            /// Adds a task. It saves too.
            /// @llm Append a task; returns the new list.
            /// @prefer @add_task($tasks, title: 'x') over ->push().
            /// @avoid Calling it in a loop.
            /// @pitfall title is trimmed;
            ///   an empty one throws.
            /// @example @add_task([], title: 'Milk')
            fn add_task(tasks, title: string) {
              if $title->trim() eq '' {
                throw Validation "Empty title"
              }
              return $tasks->push({ title: @clean($title) })
            }

            fn clean(text) {
              fn inner(x) => $x
              if $text eq 'x' {
                throw "bad"
              }
              return @inner($text->trim())
            }

            fn _private() => 1

            /// Internal.
            /// @agent hide
            fn hidden() => 2

            /// @deprecated Use @add_task.
            fn old(tasks) => @add_task($tasks, title: 'a')

            enum Size { small, large }
            const limit = 3

            /// A task.
            type Task(extra: keep) {
              title: string from name
              done?: bool = false
              label => $title->upper()
            }
            type Pair(fields: "a: int, b?: string")
            """;

        [Fact]
        public void DocCommentsHaveAgentTags()
        {
            DocComment doc = DocComment.Parse("Adds.\n@llm One line\n  wrapped.\n@prefer A\n@avoid B\n@pitfall C\n@pitfall D\n@agent hide");
            Assert.Equal("One line wrapped.", doc.Llm);
            Assert.Equal(new[] { "A" }, doc.Prefer);
            Assert.Equal(new[] { "B" }, doc.Avoid);
            Assert.Equal(new[] { "C", "D" }, doc.Pitfalls);
            Assert.True(doc.AgentHidden);
            string md = doc.ToMarkdown();
            Assert.Contains("**Prefer** A", md);
            Assert.Contains("**Avoid** B", md);
            Assert.Contains("**Pitfall** C\n**Pitfall** D", md);

            Assert.NotNull(DocComment.Parse("@agent hide"));
            Assert.Null(DocComment.Parse("@agent other"));
            Assert.NotNull(DocComment.Parse("@llm x"));
            Assert.False(DocComment.Parse("@llm x").AgentHidden);
        }

        [Fact]
        public void TheIndexComesFromTheCode()
        {
            Write("kscript.json", "{ \"name\": \"todo\", \"packages\": [\"KScript.IO\", \"nope\"], \"app\": { \"main\": \"lib/tasks.kscript\" } }");
            Write("lib/tasks.kscript", Tasks);
            Write("lib/use.kscript", "import add_task from \"tasks.kscript\"\nfn seed() => @add_task([], title: 'a')\n");
            Write("data.xml", "<root />");
            Write("script.xml", "<kscript><fn name=\"x_fn\" params=\"a\" value=\"$a\" /></kscript>");
            Write("tests/tasks.test.kscript", "import KScript.Test\n");
            Write("index.html", "<p>hi</p>");
            Write("about.md", "# About");
            Write("README.md", "# Read me");
            Write("node_modules/x.kscript", "fn skipped() => 1");
            Write(".hidden/x.kscript", "fn skipped2() => 1");

            AgentProject project = AgentDocs.Read(dir);
            Assert.Equal("todo", project.Name);
            Assert.Equal("lib/tasks.kscript", project.Main);
            Assert.Equal(1, project.Tests);
            Assert.Equal(2, project.Pages);
            Assert.Equal(new[] { "lib/tasks.kscript", "lib/use.kscript", "script.xml" }, project.Scripts);
            Assert.Equal(new[] { "KScript.Core", "KScript.IO", "KScript.Math" }, project.Packages);
            Assert.DoesNotContain(project.Symbols, s => s.Name is "_private" or "hidden" or "inner" or "skipped" or "skipped2");

            AgentSymbol add = project.Symbols.Single(s => s.Name == "add_task");
            Assert.Equal("Append a task; returns the new list.", add.Summary);
            Assert.Equal(new[] { "Validation" }, add.Throws);
            Assert.Equal(new[] { "@clean" }, add.Calls);
            Assert.Equal(new[] { "@old", "@seed" }, add.CalledBy.OrderBy(c => c));
            Assert.Equal("@add_task([], title: 'Milk')", add.Example);
            Assert.Equal(new[] { "Error" }, project.Symbols.Single(s => s.Name == "clean").Throws);
            Assert.True(project.Symbols.Single(s => s.Name == "old").Deprecated);

            string index = AgentDocs.Index(project);
            Assert.Contains("## lib/tasks.kscript", index);
            Assert.Contains("- `@add_task(tasks, title: string): list` Append a task; returns the new list. :10", index);
            Assert.Contains("  - prefer: @add_task($tasks, title: 'x') over ->push().", index);
            Assert.Contains("  - avoid: Calling it in a loop.", index);
            Assert.Contains("  - pitfall: title is trimmed; an empty one throws.", index);
            Assert.Contains("  - throws: Validation", index);
            Assert.Contains("  - calls: @clean", index);
            Assert.Contains("  - e.g. `@add_task([], title: 'Milk')`", index);
            Assert.Contains("(deprecated)", index);
            Assert.Contains("`enum Size { small, large }`", index);
            Assert.Contains("`const $limit: number`", index);
            Assert.Contains("`type Task(extra: keep) { title: string from name, done?: bool = false, label => $title->upper() }` A task.", index);
            Assert.Contains("`type Pair { a: int, b?: string }`", index);
            Assert.Contains("@x_fn(a)", index);
            Assert.DoesNotContain("## Routes", index);
        }

        [Fact]
        public void CallGraphListsAreCapped()
        {
            string callees = string.Concat(Enumerable.Range(1, 10).Select(i => $"fn f{i}() => @hub()\n"));
            Write("a.kscript", $"fn hub() => {string.Join(" + ", Enumerable.Range(1, 10).Select(i => $"@g{i}()"))}\n" + callees
                + string.Concat(Enumerable.Range(1, 10).Select(i => $"fn g{i}() => {i}\n")));
            string index = AgentDocs.Index(AgentDocs.Read(dir));
            Assert.Contains("  - calls: @g1, @g2, @g3, @g4, @g5, @g6, @g7, @g8, …", index);
            Assert.Contains("  - used by: @f1, @f2, @f3, @f4, @f5, @f6, @f7, @f8, …", index);
        }

        [Fact]
        public void TypesKeepTheirFieldRules()
        {
            Write("t.ks.xml", "<kscript><type name=\"Age\"><field name=\"years\" type=\"int\" min=\"0\" /><field /></type><type name=\"Blank\" /></kscript>");
            string index = AgentDocs.Index(AgentDocs.Read(dir));
            Assert.Contains("`type Age { years: int min: 0 }`", index);
            Assert.Contains("`type Blank { }` :1", index);
        }

        [Fact]
        public void SummariesAreShortAndPlain()
        {
            Assert.Null(AgentDocs.Summarize(null));
            Assert.Null(AgentDocs.Summarize("  "));
            Assert.Equal("First.", AgentDocs.Summarize("First. Second."));
            Assert.Equal("One two", AgentDocs.Summarize("**One**\ntwo\n\nThree"));
            string cut = AgentDocs.Summarize(string.Join(" ", Enumerable.Repeat("word", 40)));
            Assert.True(cut.Length <= AgentDocs.SummaryLimit);
            Assert.EndsWith("word…", cut);
            Assert.Equal(new string('x', 19) + "…", AgentDocs.Summarize(new string('x', 50), 20));
            Assert.Equal(0, AgentDocs.EstimateTokens(null));
            Assert.Equal(1, AgentDocs.EstimateTokens("abcd"));
        }

        [Fact]
        public void MergeKeepsNotesAroundTheGeneratedPart()
        {
            string section = AgentDocs.Begin + "\nnew\n" + AgentDocs.End + "\n";
            Assert.Equal(section, AgentDocs.Merge(null, section));
            Assert.Equal("# Mine\n\n" + section, AgentDocs.Merge("# Mine\n", section));
            Assert.Equal("top\n" + section + "bottom\n", AgentDocs.Merge("top\r\n<!-- kscript agents: begin -->\nold\n" + AgentDocs.End + "\nbottom\n", section));
            Assert.Equal("top\n" + section + "tail", AgentDocs.Merge("top\n<!-- kscript agents: begin -->\n" + AgentDocs.End + "tail", section));
            Assert.Equal("top\n" + section, AgentDocs.Merge("top\n<!-- kscript agents: begin -->", section).Replace("<!-- kscript agents: begin -->\n\n", string.Empty));
            Assert.Equal("x\n" + section, AgentDocs.Merge("x\n" + AgentDocs.Begin + "\nold\n" + AgentDocs.End, section));
        }

        [Fact]
        public void AgentsMdHasTheCrashCourseAndTheProject()
        {
            Write("kscript.json", "{ \"name\": \"demo\" }");
            Write("main.kscript", "fn one() => 1\n");
            string section = AgentDocs.AgentsSection(AgentDocs.Read(dir));
            Assert.StartsWith(AgentDocs.Begin, section);
            Assert.EndsWith(AgentDocs.End + "\n", section);
            Assert.Contains("```kscript\nimport KScript.IO", section);
            Assert.Contains("kscript check --format json", section);
            Assert.Contains("1 script, 0 test files. Its own 1 function and 0 types, enums and consts", section);
            Assert.Contains("runs `*.test.kscript` files", section);
            Assert.Contains("- `KScript.Core`: The language itself", section);
            Assert.DoesNotContain("Entry point", section);
            Assert.True(AgentDocs.EstimateTokens(section) < 2000);

            Write("kscript.json", "{ \"name\": \"demo\", \"make\": { \"format\": \"xml\" } }");
            Write("a.test.kscript", "import KScript.Test\n");
            Write("b.test.kscript", "import KScript.Test\n");
            Write("index.html", "<p>hi</p>");
            string xml = AgentDocs.AgentsSection(AgentDocs.Read(dir));
            Assert.Contains("```xml\n<kscript>", xml);
            Assert.Contains("XML attributes can't hold a raw `<`", xml);
            Assert.Contains("runs the 2 test files", xml);
            Assert.Contains("1 page", xml);
            Assert.Contains("`.ks.xml`", xml);
        }

        [Fact]
        public void TheCrashCourseChecksCleanInBothFormats()
        {
            Write("tasks.json", "[{ \"title\": \"a\" }]");
            Write("course.kscript", AgentDocs.CrashCourse);
            Write("kscript.json", "{ \"make\": { \"format\": \"xml\" } }");
            string section = AgentDocs.AgentsSection(AgentDocs.Read(dir));
            int start = section.IndexOf("```xml\n", StringComparison.Ordinal) + 7;
            Write("course.ks.xml", section.Substring(start, section.IndexOf("\n```", start, StringComparison.Ordinal) - start));
            foreach (string file in new[] { "course.kscript", "course.ks.xml" })
            {
                var output = new StringWriter();
                Assert.True(CheckCommand.Run(new[] { Path.Combine(dir, file) }, output) == 0, output.ToString());
                Assert.Contains("0 errors, 0 warnings", output.ToString());
            }
        }

        [Fact]
        public void PackageFilesListOnlyWhatThePackageHas()
        {
            string core = AgentDocs.Package("KScript.Core");
            Assert.Contains("Always available.", core);
            Assert.Contains("- `->push(items...): list` A new list with items added to the end.\n  - pitfall: Returns a new list", core);
            Assert.Contains("- `->each(fn): list`", core);
            Assert.DoesNotContain("- `if(", core);
            Assert.Contains("- `<if test>`", AgentDocs.Package("KScript.Core", format: "xml"));

            string io = AgentDocs.Package("KScript.IO");
            Assert.Contains("Import with `import KScript.IO`.", io);
            Assert.Contains("  - prefer: @read_json($path, as: $Type)", io);
            Assert.Contains("  - pitfall: A missing file throws FileNotFound", io);
            Assert.Contains("Import with `<import package=\"KScript.IO\" />`.", AgentDocs.Package("KScript.IO", format: "xml"));
            Assert.Contains("  - pitfall: A 4xx or 5xx status doesn't throw", AgentDocs.Package("KScript.Net"));

            var registry = new PackageRegistry();
            registry.Register(new AgentNotesPackage());
            registry.Register(new EmptyAgentPackage());
            var catalog = new LanguageCatalog(registry);
            string notes = AgentDocs.Package("KScript.AgentNotes", catalog);
            Assert.Contains("- `@ping(host): string` Pings a host.\n  - pitfall: Slow.\n", notes);
            Assert.DoesNotContain("e.g.", notes);
            Assert.Contains("- `beep(times, loud?)` Make a noise.\n  - avoid: Beeping in a loop.", notes);
            Assert.Contains("- `<beep times loud?>`", AgentDocs.Package("KScript.AgentNotes", catalog, "xml"));
            Assert.Contains("Nothing to call", AgentDocs.Package("KScript.Empty", catalog));
            Assert.DoesNotContain("Agent notes for tests.\n", AgentDocs.Package("KScript.Unknown", catalog));
        }

        [Fact]
        public void AgentNoteKeysMustNameSomething()
        {
            foreach (Action set in new Action[] { () => AgentNotesPackage.BadKey = true, () => AgentNotesPackage.BadElement = true })
            {
                set();
                try
                {
                    var registry = new PackageRegistry();
                    registry.Register(new AgentNotesPackage());
                    var ex = Assert.Throws<InvalidOperationException>(() => registry.Get("KScript.AgentNotes").Commands);
                    Assert.Contains("to give agent notes to", ex.Message);
                }
                finally
                {
                    AgentNotesPackage.BadKey = AgentNotesPackage.BadElement = false;
                }
            }
            Assert.NotNull(LanguageCatalog.Default.GetCommand("range").Agent);
            Assert.Contains("someone", Assert.Throws<InvalidOperationException>(() =>
            {
                var registry = new PackageRegistry();
                registry.Register(new ExampleKeyPackage());
                _ = registry.Get("KScript.ExampleKey").Commands;
            }).Message.Replace("an example", "someone"));
        }

        private sealed class ExampleKeyPackage : KScriptPackage
        {
            public override string Name => "KScript.ExampleKey";

            public override string Description => "A bad example key.";

            protected override void Register(PackageBuilder p) => p.Examples(("nothing", "1"));
        }

        [Fact]
        public void BudgetsCutWholeEntriesAndPointAtMcp()
        {
            Write("main.kscript", string.Concat(Enumerable.Range(1, 30).Select(i => $"/// Function number {i}.\nfn f{i}() => {i}\n")));
            int index = AgentDocs.IndexBudget, agents = AgentDocs.AgentsBudget, package = AgentDocs.PackageBudget;
            try
            {
                AgentDocs.IndexBudget = 700;
                AgentDocs.AgentsBudget = 600;
                AgentDocs.PackageBudget = 600;
                AgentProject project = AgentDocs.Read(dir);
                Assert.Contains("@f1()", AgentDocs.Index(project));
                Assert.Contains("more symbols left out", AgentDocs.Index(project));
                string section = AgentDocs.AgentsSection(project);
                Assert.Contains("cut to keep this file short", section);
                Assert.EndsWith(AgentDocs.End + "\n", section);
                Assert.Contains("more entries left out", AgentDocs.Package("KScript.Core"));

                AgentDocs.IndexBudget = 1;
                Assert.Contains("## main.kscript", AgentDocs.Index(project));
            }
            finally
            {
                AgentDocs.IndexBudget = index;
                AgentDocs.AgentsBudget = agents;
                AgentDocs.PackageBudget = package;
            }
        }

        [Fact]
        public void WriteKeepsFilesCurrentAndRemovesStalePackages()
        {
            Write("main.kscript", "fn one() => 1\n");
            Write(".kscript/agents/packages/KScript.Old.md", "old");
            IReadOnlyList<string> changed = AgentDocs.Write(dir, check: true);
            Assert.Contains("AGENTS.md", changed);
            Assert.Contains(".kscript/agents/packages/KScript.Old.md", changed);
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));
            Assert.True(File.Exists(Path.Combine(dir, ".kscript/agents/packages/KScript.Old.md")));

            changed = AgentDocs.Write(dir);
            Assert.Equal(new[] { "AGENTS.md", ".kscript/agents/index.md", ".kscript/agents/packages/KScript.Core.md", ".kscript/agents/packages/KScript.Old.md" }, changed);
            Assert.False(File.Exists(Path.Combine(dir, ".kscript/agents/packages/KScript.Old.md")));
            Assert.Empty(AgentDocs.Write(dir));
            Assert.True(AgentDocs.IsEnabled(dir));
            Assert.False(AgentDocs.IsEnabled(null));

            File.WriteAllText(Path.Combine(dir, "AGENTS.md"), "# My notes\n\n" + Read("AGENTS.md"));
            Write("main.kscript", "fn one() => 1\nfn two() => 2\n");
            Assert.Equal(new[] { "AGENTS.md", ".kscript/agents/index.md" }, AgentDocs.Write(dir));
            Assert.StartsWith("# My notes\n", Read("AGENTS.md"));
            Assert.Contains("2 functions", Read("AGENTS.md"));
        }

        [Fact]
        public void TheIndexStartsWithTheRoutes()
        {
            Write("kscript.json", "{ \"name\": \"site\" }");
            Write("api.ks.xml", "<KScript.Routes prefix=\"/api\"><import src=\"controllers/ping.ks.xml\" as=\"ping\" /><Get path=\"/ping\" to=\"ping.show\" /></KScript.Routes>");
            Write("controllers/ping.ks.xml", "<kscript><fn name=\"show\" value=\"'pong'\" /></kscript>");
            string index = AgentDocs.Index(AgentDocs.Read(dir));
            Assert.Contains("## Routes (kscript routes)", index);
            Assert.Contains("/api/ping", index);
            Assert.Contains("api.ks.xml:", index);

            // kscript convert names the new files while the old ones are still there.
            string renamed = AgentDocs.Index(AgentDocs.Read(dir, skip: new HashSet<string> { Path.Combine(dir, "api.ks.xml") }));
            Assert.Contains("api.kscript:", renamed);
        }

        [Fact]
        public void FindRootUsesTheNearestProject()
        {
            Write("kscript.json", "{}");
            string script = Write("lib/a.kscript", "fn a() => 1");
            Assert.Equal(Path.GetFullPath(dir), AgentDocs.FindRoot(script));
            string loose = Path.Combine(Path.GetTempPath(), "kscript-agents-loose-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(loose);
            try
            {
                Assert.Equal(loose, AgentDocs.FindRoot(loose));
                Assert.Equal(loose, AgentDocs.FindRoot(Path.Combine(loose, "x.kscript")));
            }
            finally
            {
                Directory.Delete(loose, true);
            }
        }

        [Fact]
        public void TheCommandWritesChecksAndPrints()
        {
            Assert.Equal((0, AgentsCommand.Usage + Environment.NewLine), Agents("--help"));
            Assert.Equal(2, Agents("-x").Code);
            Assert.Equal(2, Agents(dir, dir).Code);
            Assert.Contains("folder not found", Agents(Path.Combine(dir, "missing")).Output);

            Write("kscript.json", "{ \"name\": \"cmd\" }");
            Write("main.kscript", "fn one() => 1\n");
            Assert.Contains("# KScript for agents", Agents(dir, "--print").Output);
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));

            var (code, output) = Agents(dir, "--check");
            Assert.Equal(1, code);
            Assert.Contains("AGENTS.md: out of date", output);
            Assert.Contains("Run kscript agents", output);

            (code, output) = Agents(dir);
            Assert.Equal(0, code);
            Assert.Contains("wrote AGENTS.md", output);
            Assert.Contains($"Agent docs in {Path.GetRelativePath(Directory.GetCurrentDirectory(), dir)} updated: AGENTS.md is about", output);
            Assert.Contains("up to date", Agents(dir).Output);
            Assert.Equal(0, Agents(dir, "--check").Code);

            string previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                Assert.Equal("Agent docs are up to date." + Environment.NewLine, Agents("--check").Output);
                Assert.Equal("Agent docs are up to date." + Environment.NewLine, Agents().Output);
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }

            Write("kscript.json", "{ \"standard\": \"missing-standard\" }");
            Assert.Equal(2, Agents(dir).Code);
        }

        [Fact]
        public void CheckPrintsJsonWithFixes()
        {
            string file = Write("main.kscript", "import KScript.Math\necho \"hi\"\n");
            var output = new StringWriter();
            Assert.Equal(0, CheckCommand.Run(new[] { file }, output, json: true));
            JsonNode result = JsonNode.Parse(output.ToString());
            Assert.Equal(1, (int)result["files"]);
            Assert.Equal(0, (int)result["errors"]);
            Assert.Equal(1, (int)result["warnings"]);
            JsonNode problem = result["problems"][0];
            Assert.Equal("KS6005", (string)problem["code"]);
            Assert.Equal("warning", (string)problem["severity"]);
            Assert.Equal(1, (int)problem["line"]);
            Assert.Equal(8, (int)problem["column"]);
            JsonNode edit = problem["fixes"][0]["edits"][0];
            Assert.Equal(1, (int)edit["line"]);
            Assert.Equal("", (string)edit["newText"]);
            Assert.EndsWith("main.kscript", (string)problem["file"]);

            Write("bad.kscript", "echo $nope\n");
            output = new StringWriter();
            Assert.Equal(1, CheckCommand.Run(new[] { Path.Combine(dir, "bad.kscript") }, output, json: true));
            Assert.Equal(1, (int)JsonNode.Parse(output.ToString())["errors"]);

            output = new StringWriter();
            Assert.Equal(2, CheckCommand.Run(new[] { Path.Combine(dir, "missing.kscript") }, output, json: true));
            Assert.Contains("file not found", (string)JsonNode.Parse(output.ToString())["error"]);
        }

        [Fact]
        public void GeneratedFilesAreNeverCheckedOrServed()
        {
            Write("kscript.json", "{ \"lint\": { \"include\": [\"**/*.kscript\", \"**/*.md\"] }, \"test\": { \"include\": [\"**/*.md\"] }, \"format\": { \"include\": [\"**/*.md\"] } }");
            var config = KScript.Projects.ProjectConfig.Load(Path.Combine(dir, "kscript.json"));
            foreach (string generated in new[] { ".kscript/agents/index.md", "AGENTS.md", "CLAUDE.md" })
            {
                Assert.False(config.IsIncluded(Path.Combine(dir, generated)));
                Assert.False(config.IsTestIncluded(Path.Combine(dir, generated)));
                Assert.False(config.IsFormatIncluded(Path.Combine(dir, generated)));
            }
            Assert.True(config.IsIncluded(Path.Combine(dir, "notes.md")));
        }

        private static byte[] Frame(JsonObject message)
        {
            message["jsonrpc"] = "2.0";
            byte[] body = System.Text.Encoding.UTF8.GetBytes(message.ToJsonString());
            return System.Text.Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n").Concat(body).ToArray();
        }

        private static List<string> Logs(params JsonObject[] messages)
        {
            var all = new[] { new JsonObject { ["id"] = 1, ["method"] = "initialize", ["params"] = new JsonObject { ["capabilities"] = new JsonObject() } } }.Concat(messages);
            using var input = new MemoryStream(all.SelectMany(Frame).ToArray());
            using var output = new MemoryStream();
            new KScriptLanguageServer(input, output).RunAsync().GetAwaiter().GetResult();
            string text = System.Text.Encoding.UTF8.GetString(output.ToArray());
            // Each line as "<level> <message>": 1 is an error, 3 is info (editors label 1 as "[Error - ...]").
            return System.Text.RegularExpressions.Regex.Matches(text, "\"type\":(\\d),\"message\":\"([^\"]*)\"").Select(m => m.Groups[1].Value + " " + m.Groups[2].Value).ToList();
        }

        private static JsonObject Saved(string path) => new JsonObject
        {
            ["method"] = "textDocument/didSave",
            ["params"] = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = path == null ? null : Workspace.PathToUri(path) } },
        };

        [Fact]
        public void TheEditorRefreshesAgentDocsOnSave()
        {
            string main = Write("main.kscript", "fn one() => 1\n");
            Write("kscript.json", "{ \"name\": \"lsp\" }");

            // Not set up for agents yet: a save writes nothing.
            Logs(Saved(main));
            Assert.False(File.Exists(Path.Combine(dir, "AGENTS.md")));

            AgentDocs.Write(dir);
            Write("main.kscript", "fn one() => 1\nfn two() => 2\n");
            List<string> logs = Logs(Saved(main), Saved(Path.Combine(dir, "AGENTS.md")), Saved(null),
                new JsonObject { ["method"] = "textDocument/didSave", ["params"] = new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = "untitled:1" } } });
            Assert.Contains(logs, l => l.StartsWith("3 agents: updated AGENTS.md", StringComparison.Ordinal));
            Assert.Contains("@two()", Read(".kscript/agents/index.md"));
            Assert.DoesNotContain(Logs(Saved(main)), l => l.Contains(" agents:", StringComparison.Ordinal));

            Write("kscript.json", "{ \"standard\": \"missing-standard\" }");
            Assert.Contains(Logs(Saved(main)), l => l.StartsWith("1 agents: ", StringComparison.Ordinal) && l.Contains("standard"));
        }
    }
}