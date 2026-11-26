using System.Text;
using System.Text.Json.Nodes;

using KScript.Language.Analysis;
using KScript.Language.Features;
using KScript.Language.Rules;
using KScript.LanguageServer;

namespace KScript.Language.Tests
{
    /// <summary>
    /// The language server's less common paths, driven by a whole conversation written up front: the server reads
    /// it to the end and the replies are read back afterwards.
    /// </summary>
    public class ServerCoverageTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "kscript-server-coverage");
        private static readonly string Main = Path.Combine(Root, "main.ks.xml");
        private static readonly string Lib = Path.Combine(Root, "lib", "strings.ks.xml");
        private static string MainUri => Workspace.PathToUri(Main);

        /// <summary>Files held in memory. Paths in <see cref="Broken"/> exist but throw when read.</summary>
        private sealed class Memory : IFileSystem
        {
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            public HashSet<string> Broken { get; } = new HashSet<string>(StringComparer.Ordinal);

            public bool FileExists(string path) => Files.ContainsKey(path) || Broken.Contains(path);

            public bool DirectoryExists(string path) => Files.Keys.Any(f => f.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal));

            public string ReadText(string path) => Broken.Contains(path) ? throw new IOException("locked") : Files.TryGetValue(path, out string t) ? t : null;

            public IEnumerable<string> ListFiles(string directory, string pattern) => Files.Keys.Where(f => Path.GetDirectoryName(f) == directory);
        }

        #region Conversation

        private static byte[] Frame(JsonObject message)
        {
            message["jsonrpc"] = "2.0";
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            return Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n").Concat(body).ToArray();
        }

        private static JsonObject Req(int id, string method, JsonNode p = null) => new JsonObject { ["id"] = id, ["method"] = method, ["params"] = p };

        private static JsonObject Note(string method, JsonNode p = null) => new JsonObject { ["method"] = method, ["params"] = p };

        private static JsonObject Init(int id = 1) => Req(id, "initialize", new JsonObject { ["capabilities"] = new JsonObject() });

        private static JsonObject OpenDoc(string uri, string text, string languageId = "kscript") => Note("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = languageId, ["version"] = 1, ["text"] = text },
        });

        private static JsonObject Doc(string uri) => new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } };

        private static JsonObject Pos(int line, int character) => new JsonObject { ["line"] = line, ["character"] = character };

        private static JsonObject At(string uri, int line, int character) => Doc(uri).Also(o => o["position"] = Pos(line, character));

        /// <summary>Runs the server over the messages (a <see cref="JsonObject"/> is framed, a string is sent as it is) and returns its exit code and output.</summary>
        private static (int Exit, List<JsonObject> Output) Converse(IFileSystem files, params object[] messages)
        {
            using MemoryStream input = new MemoryStream(messages.SelectMany(m => m is string raw ? Encoding.ASCII.GetBytes(raw) : Frame((JsonObject)m)).ToArray());
            using MemoryStream output = new MemoryStream();
            int exit = new KScriptLanguageServer(input, output, files).RunAsync().GetAwaiter().GetResult();
            return (exit, Parse(output.ToArray()));
        }

        private static List<JsonObject> Parse(byte[] bytes)
        {
            List<JsonObject> messages = new List<JsonObject>();
            string text = Encoding.UTF8.GetString(bytes);
            int at = 0;
            while (at < text.Length)
            {
                int headerEnd = text.IndexOf("\r\n\r\n", at, StringComparison.Ordinal);
                int length = int.Parse(text.Substring(at, headerEnd - at).Split(':')[1].Trim());
                messages.Add((JsonObject)JsonNode.Parse(text.Substring(headerEnd + 4, length)));
                at = headerEnd + 4 + length;
            }
            return messages;
        }

        private static JsonObject Reply(List<JsonObject> output, int id) => output.Single(m => m["id"] is JsonValue v && v.GetValue<int>() == id);

        private static IEnumerable<string> Logs(List<JsonObject> output) =>
            output.Where(m => m["method"]?.GetValue<string>() == "window/logMessage").Select(m => m["params"]["message"].GetValue<string>());

        #endregion

        [Fact]
        public void ProjectRulesWrittenInKScriptWorkInCliAndLsp()
        {
            string folder = Directory.CreateTempSubdirectory("kscript-rules-").FullName;
            try
            {
                string page = Path.Combine(folder, "page.html");
                string config = Path.Combine(folder, "kscript.json");
                string ruleFile = Path.Combine(folder, DocumentRules.FileName);
                const string source = "<img src=\"/logo.svg\">\n";
                const string expected = "<img\n    src=\"/logo.svg\">\n";
                const string settings = "{\"lint\":{\"rules\":{\"KS9001\":\"error\"}}}";
                const string rules = """
                    Style(indent_size: 4)
                    Lint(code: 'KS9001', element: 'img', missing_attribute: 'alt', message: 'Give this image alternative text.')
                    Format(element: 'img', attributes: 'one_per_line')
                    Format(model: 'Attachments', options: 'one_per_line')
                    """;
                File.WriteAllText(page, source);
                File.WriteAllText(config, settings);
                File.WriteAllText(ruleFile, rules);
                using StringWriter report = new StringWriter();
                Assert.Equal(1, CheckCommand.Run(new[] { page }, report, json: true));
                JsonNode problem = Assert.Single(JsonNode.Parse(report.ToString())["problems"].AsArray());
                Assert.Equal("KS9001", problem["code"].GetValue<string>());
                Assert.Equal("error", problem["severity"].GetValue<string>());
                Assert.Equal(0, CheckCommand.Run(new[] { ruleFile }, TextWriter.Null));
                Assert.Equal(1, FormatCommand.Run(new[] { page, "--check" }, TextWriter.Null));
                Assert.Equal(0, FormatCommand.Run(new[] { page }, TextWriter.Null));
                Assert.Equal(expected, File.ReadAllText(page).Replace("\r\n", "\n"));
                Assert.Equal(0, FormatCommand.Run(new[] { page, "--check" }, TextWriter.Null));

                string model = Path.Combine(folder, "model.kscript");
                File.WriteAllText(model, "model Attachments(timestamps) { name: string }\n");
                Assert.Equal(0, FormatCommand.Run(new[] { model }, TextWriter.Null));
                Assert.Contains("\n    timestamps,\n) {", File.ReadAllText(model).Replace("\r\n", "\n"));

                string uri = Workspace.PathToUri(page);
                Memory files = new Memory();
                files.Files[config] = settings;
                files.Files[ruleFile] = rules;
                var (_, output) = Converse(files, Init(), OpenDoc(uri, source, "html"),
                    Req(2, "textDocument/formatting", Doc(uri)));
                Assert.Equal("\n   ", Assert.Single(Reply(output, 2)["result"].AsArray())["newText"].GetValue<string>());
                JsonNode notification = output.Single(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics");
                JsonNode diagnostic = Assert.Single(notification["params"]["diagnostics"].AsArray());
                Assert.Equal("KS9001", diagnostic["code"].GetValue<string>());
                Assert.Null(diagnostic["codeDescription"]);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void FunctionRulesInspectMarkupTrimValuesAndChooseArgumentLayout()
        {
            string folder = Directory.CreateTempSubdirectory("kscript-functions-").FullName;
            try
            {
                File.WriteAllText(Path.Combine(folder, "kscript.json"), "{}");
                File.WriteAllText(Path.Combine(folder, DocumentRules.FileName), """
                    Lint(code: 'KS9001', element: 'img', check: fn(node) => !$node.attributes->has('alt') ? 'Give ' + $node.name + ' alternative text.' : null)
                    Format(element: 'img', attribute: 'alt', value: fn(text) => $text->trim())
                    Format(statement: 'Something', arguments: fn(node) => $node.has_body_content ? 'one_per_line' : 'inline')
                    """);
                string page = Path.Combine(folder, "page.html");
                File.WriteAllText(page, """
                    <main><img src="/a.svg"><img src="/b.svg" alt='  A &amp; B  '><img src="/c.svg" alt=" {'Templated'} "></main>
                    """);
                using StringWriter report = new StringWriter();
                Assert.Equal(1, CheckCommand.Run(new[] { page }, report, warningsAsErrors: true, json: true));
                Assert.True(report.ToString().Contains("Give img alternative text.", StringComparison.Ordinal), report.ToString());
                Assert.Equal(0, FormatCommand.Run(new[] { page }, TextWriter.Null));
                Assert.Contains("alt='A &amp; B'", File.ReadAllText(page));
                Assert.Contains("alt=\" {'Templated'} \"", File.ReadAllText(page));

                string script = Path.Combine(folder, "script.kscript");
                File.WriteAllText(script, "Something(\n  val1,\n  val2,\n) {\n}\nSomething(val1,val2) { echo 'body' }\n");
                Assert.Equal(0, FormatCommand.Run(new[] { script }, TextWriter.Null));
                Assert.Equal("Something(val1, val2) {}\nSomething(\n  val1,\n  val2,\n) {\n  echo 'body'\n}\n",
                    File.ReadAllText(script).Replace("\r\n", "\n"));
                Assert.Equal(0, FormatCommand.Run(new[] { script, page, "--check" }, TextWriter.Null));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void RuleFilesRejectExecutableStatementsAndArguments()
        {
            DocumentAnalysis result = Analyzer.Analyze(
                "import KScript.IO\nFormat(element: @env('TAG'), attributes: 'inline')\nFormat(element: 'img', attribute: 'alt', value: fn(text) => @env($text))\n",
                DocumentRules.FileName);
            Assert.Contains(result.Diagnostics, d => d.Code == KScript.Syntax.DiagnosticCodes.InvalidToolingRule && d.Message.Contains("declarations only"));
            Assert.Contains(result.Diagnostics, d => d.Code == KScript.Syntax.DiagnosticCodes.InvalidToolingRule && d.Message.Contains("quoted literal"));
            Assert.Contains(result.Diagnostics, d => d.Code == KScript.Syntax.DiagnosticCodes.InvalidToolingRule && d.Message.Contains("'value'"));
        }

        [Fact]
        public void ManifestOptionsReportNativeRulesAndPinnedLibraryRequirements()
        {
            var files = new Memory();
            string config = Path.Combine(Root, "kscript.json");
            const string commit = "0123456789abcdef0123456789abcdef01234567";
            files.Files[config] = $$"""
                {
                  "paths": { "lib": "packages" },
                  "libraries": {
                    "client": { "source": "https://github.com/example/client", "commit": "{{commit}}" },
                    "stale": { "source": "https://github.com/example/stale", "commit": "{{commit}}" }
                  }
                }
                """;
            files.Files[Path.Combine(Root, "packages", "client", ".kscript-lib.json")] = $$"""
                { "source": "https://github.com/example/client", "commit": "{{commit}}", "packages": ["Core", "Net"] }
                """;
            files.Files[Path.Combine(Root, "packages", "stale", ".kscript-lib.json")] = """{ "commit": "different", "packages": ["KScript.Math"] }""";
            var (_, output) = Converse(files, Init(), Req(2, "kscript/manifestOptions", Doc(Workspace.PathToUri(config))));
            JsonNode options = Reply(output, 2)["result"];
            Assert.Equal("client", Assert.Single(options["requiredPackages"]["KScript.Net"].AsArray()).GetValue<string>());
            Assert.Null(options["requiredPackages"]["KScript.Math"]);
            Assert.Null(options["requiredPackages"]["KScript.Core"]);
            Assert.Contains(options["types"]["password"].AsArray(), n => n.GetValue<string>() == "min");
            Assert.Contains(options["types"]["uuid"].AsArray(), n => n.GetValue<string>() == "v");
        }

        [Fact]
        public void TheTestListReadsTestFilesThatAreNotOpen()
        {
            Memory files = new Memory();
            string path = Path.Combine(Path.GetDirectoryName(Main), "cart.test.kscript");
            files.Files[path] = "test(name: adds an item) {\n  expect(1, equals: 1)\n}\n";
            var (_, output) = Converse(files,
                Init(),
                Req(2, "kscript/tests", Doc(Workspace.PathToUri(path))),
                Req(3, "kscript/tests", Doc(Workspace.PathToUri(path + ".missing"))),
                Req(4, "kscript/tests", Doc("untitled:Untitled-1")));
            Assert.Equal("adds an item", Assert.Single(Reply(output, 2)["result"]["tests"].AsArray())["fullName"].GetValue<string>());
            Assert.Empty(Reply(output, 3)["result"]["tests"].AsArray());
            Assert.Empty(Reply(output, 4)["result"]["tests"].AsArray());
        }

        [Fact]
        public void SurvivesBadInput()
        {
            Memory files = new Memory();
            var (exit, output) = Converse(files,
                Note("textDocument/didOpen", null),                       // before initialize: ignored
                "\r\nContent-Length: 1\r\n\r\n{",                         // a stray blank line, then invalid JSON
                Init(),
                new JsonObject { ["id"] = 99, ["result"] = null },         // a response: the server sends no requests
                OpenDoc(MainUri, "<kscript></kscript>"),
                Req(2, "textDocument/completion", Doc(MainUri)),          // no position: the handler fails
                Note("textDocument/didChange", Doc(MainUri)),             // no changes: fails, and nobody is told but the log
                Note("textDocument/didSave", Doc(MainUri)),
                Note("workspace/didChangeWatchedFiles", new JsonObject()),
                "Content-Length: 50\r\n\r\n{}");                          // cut off mid-message

            Assert.Equal(1, exit);
            JsonObject parseError = output.First(m => m["error"] != null);
            Assert.Equal(ErrorCodes.ParseError, parseError["error"]["code"].GetValue<int>());
            Assert.Equal(ErrorCodes.InternalError, Reply(output, 2)["error"]["code"].GetValue<int>());
            Assert.Equal(2, Logs(output).Count(l => l.StartsWith("Error handling", StringComparison.Ordinal)));
            Assert.DoesNotContain(output, m => m["id"] is JsonValue v && v.GetValue<int>() == 99);
        }

        [Fact]
        public async Task StopsWhenCancelled()
        {
            using MemoryStream input = new MemoryStream();
            int exit = await new KScriptLanguageServer(input, Stream.Null).RunAsync(new CancellationToken(true));
            Assert.Equal(0, exit);
        }

        [Fact]
        public async Task RunsOnGivenStreams()
        {
            using MemoryStream input = new MemoryStream(Frame(Init()).Concat(Frame(Req(2, "shutdown"))).Concat(Frame(Note("exit"))).ToArray());
            using MemoryStream output = new MemoryStream();
            Assert.Equal(0, await KScriptLanguageServer.RunStdioAsync(input, output));
            Assert.Contains("kscript-lsp", Encoding.UTF8.GetString(output.ToArray()));
        }

        [Fact]
        public void DocumentsThatArentOpenGetNoAnswer()
        {
            const string uri = "file:///nowhere/x.ks.xml";
            JsonObject p = At(uri, 0, 0).Also(o =>
            {
                o["newName"] = "y";
                o["range"] = new JsonObject { ["start"] = Pos(0, 0), ["end"] = Pos(0, 0) };
                o["context"] = new JsonObject { ["includeDeclaration"] = true, ["diagnostics"] = new JsonArray() };
                o["options"] = new JsonObject { ["tabSize"] = 2, ["insertSpaces"] = true };
            });
            string[] methods =
            {
                "textDocument/completion", "textDocument/hover", "textDocument/signatureHelp", "textDocument/definition",
                "textDocument/references", "textDocument/documentHighlight", "textDocument/prepareRename", "textDocument/rename",
                "textDocument/documentSymbol", "textDocument/semanticTokens/full", "textDocument/inlayHint", "textDocument/foldingRange",
                "textDocument/codeAction", "textDocument/documentLink", "textDocument/formatting",
            };
            List<object> messages = new List<object> { Init() };
            messages.AddRange(methods.Select((m, i) => (object)Req(10 + i, m, p.DeepClone())));
            messages.Add(Req(100, "kscript/blocks", p.DeepClone()));
            var (_, output) = Converse(new Memory(), messages.ToArray());

            for (int i = 0; i < methods.Length; i++)
            {
                JsonObject reply = Reply(output, 10 + i);
                Assert.True(reply["error"] == null && reply["result"] == null, methods[i] + ": " + reply.ToJsonString());
            }
            Assert.Empty(Reply(output, 100)["result"]["blocks"].AsArray());
        }

        [Fact]
        public void AnswersAcrossFiles()
        {
            Memory files = new Memory();
            files.Files[Lib] = "<kscript>\n<fn name=\"slugify\" params=\"text\" value=\"$text->lower()\" />\n</kscript>";
            //           0         1         2         3
            //           0123456789012345678901234567890123
            string main = "<kscript>\n" +
                          "<import path=\"lib/strings.ks.xml\" as=\"str\" />\n" +    // line 1
                          "<let name=\"people\" value=\"[1, 2]\" />\n" +           // line 2
                          "<fn name=\"twice\" params=\"n: int\">\n" +             // line 3
                          "<return value=\"$n * 2\" />\n" +                      // line 4
                          "</fn>\n" +
                          "<echo>{ str.slugify('A B') }</echo>\n" +               // line 6
                          "<!-- a\nlong\ncomment -->\n" +
                          "</kscript>";
            var (_, output) = Converse(files,
                Init(),
                OpenDoc(MainUri, main),
                Req(2, "textDocument/definition", At(MainUri, 6, 13)),
                Req(3, "textDocument/documentLink", Doc(MainUri)),
                Req(4, "textDocument/inlayHint", Doc(MainUri)),
                Req(5, "textDocument/inlayHint", Doc(MainUri).Also(o => o["range"] = new JsonObject { ["start"] = Pos(4, 0), ["end"] = Pos(4, 0) })),
                Req(6, "textDocument/foldingRange", Doc(MainUri)),
                Req(7, "kscript/blocks", Doc(MainUri)));

            JsonObject target = Assert.Single(Reply(output, 2)["result"].AsArray()).AsObject();
            Assert.Equal(Workspace.PathToUri(Lib), target["uri"].GetValue<string>());
            Assert.Equal(1, target["range"]["start"]["line"].GetValue<int>());

            JsonObject link = Assert.Single(Reply(output, 3)["result"].AsArray()).AsObject();
            Assert.Equal(Workspace.PathToUri(Lib), link["target"].GetValue<string>());

            JsonArray hints = Reply(output, 4)["result"].AsArray();
            Assert.Contains(hints, h => h["label"].GetValue<string>().Contains("list", StringComparison.Ordinal) && h["tooltip"] != null);
            Assert.Empty(Reply(output, 5)["result"].AsArray());

            JsonArray folds = Reply(output, 6)["result"].AsArray();
            Assert.Contains(folds, f => f["kind"]?.GetValue<string>() == "comment");
            Assert.Contains(folds, f => f["kind"] == null);

            Assert.NotEmpty(Reply(output, 7)["result"]["blocks"].AsArray());
        }

        [Fact]
        public void ReadsTheProjectFile()
        {
            Memory files = new Memory();
            string config = Path.Combine(Root, "kscript.json");
            string configUri = Workspace.PathToUri(config);
            var (_, output) = Converse(files,
                Init(),
                OpenDoc(configUri, "{ \"format\": { \"enabled\": false }, \"bogus\": 1 }", "json"),
                OpenDoc(MainUri, "<kscript>\n<echo>hi</echo>\n</kscript>"),
                Req(2, "textDocument/formatting", Doc(MainUri)),
                Note("textDocument/didChange", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = configUri, ["version"] = 2 },
                    ["contentChanges"] = new JsonArray { new JsonObject { ["text"] = "{ nope" } },
                }),
                Req(3, "textDocument/formatting", Doc(MainUri)));

            Assert.Empty(Reply(output, 2)["result"].AsArray());
            Assert.Contains(Logs(output), l => l.Contains("unknown setting 'bogus'", StringComparison.Ordinal));
            Assert.Contains(Logs(output), l => l.Contains("Using the default lint settings", StringComparison.Ordinal));
            Assert.NotEmpty(Reply(output, 3)["result"].AsArray());
        }

        [Fact]
        public void ProjectFilesThatCantBeReadAreIgnored()
        {
            Memory files = new Memory();
            files.Broken.Add(Path.Combine(Root, "kscript.json"));
            KScriptLanguageServer server = new KScriptLanguageServer(Stream.Null, Stream.Null, files);
            Assert.Null(server.ProjectFor(Main));
            Assert.Null(server.ProjectFor("bad\0path"));
            Assert.Null(server.ProjectFor(null));
        }

        [Fact]
        public void AStandardThatStopsBeingReadableIsReloaded()
        {
            Memory files = new Memory();
            string team = Path.Combine(Root, "team.json");
            files.Files[Path.Combine(Root, "kscript.json")] = "{ \"standard\": \"team.json\" }";
            files.Files[team] = "{ \"format\": { \"print_width\": 70 } }";
            KScriptLanguageServer server = new KScriptLanguageServer(Stream.Null, Stream.Null, files);
            Assert.Equal(70, server.ProjectFor(Main).Format.PrintWidth);

            files.Files.Remove(team);
            files.Broken.Add(team);
            Assert.Null(server.ProjectFor(Main));
        }

        [Fact]
        public void WorkspaceLayersOverTheDisk()
        {
            Memory files = new Memory();
            files.Files[Lib] = "<kscript />";
            Workspace workspace = new Workspace(files);
            Assert.True(workspace.DirectoryExists(Path.GetDirectoryName(Lib)));
            Assert.Equal(new[] { Lib }, workspace.ListFiles(Path.GetDirectoryName(Lib), "*.ks.xml"));

            Assert.Equal(KScript.Syntax.Markup.DocumentKind.Page, workspace.Open("untitled:1", "html", "", 1).Kind);
            Assert.Equal(KScript.Syntax.Markup.DocumentKind.Markdown, workspace.Open("untitled:2", "markdown", "", 1).Kind);
            Assert.Equal(KScript.Syntax.Markup.DocumentKind.Script, workspace.Open("untitled:3", "kscript", "", 1).Kind);
            Assert.Null(Workspace.UriToPath("file://a:b:c"));
            Assert.Null(Workspace.UriToPath(null));
        }

        [Fact]
        public void KindsMapToTheProtocol()
        {
            int[] completion = Enum.GetValues<CompletionKind>().Select(KScriptLanguageServer.CompletionKindToLsp).ToArray();
            Assert.Equal(new[] { 10, 5, 12, 6, 6, 21, 3, 3, 2, 5, 14, 17, 15, 19, 9 }, completion);
            int[] outline = Enum.GetValues<OutlineKind>().Select(KScriptLanguageServer.OutlineKindToLsp).ToArray();
            Assert.Equal(new[] { 12, 13, 14, 2, 8 }, outline);
        }
        [Fact]
        public void WorkspaceSymbolsFollowAddedAndRemovedFolders()
        {
            string first = Path.Combine(Root, "one"), second = Path.Combine(Root, "two");
            Memory files = new Memory();
            files.Files[Path.Combine(first, "first.kscript")] = "fn first() => 1\n";
            files.Files[Path.Combine(second, "second.kscript")] = "fn second() => 2\n";
            JsonObject Folder(string path) => new JsonObject { ["uri"] = Workspace.PathToUri(path), ["name"] = Path.GetFileName(path) };
            var (_, output) = Converse(files,
                Req(1, "initialize", new JsonObject { ["rootUri"] = Workspace.PathToUri(first) }),
                Req(2, "workspace/symbol", new JsonObject()),
                Note("workspace/didChangeWorkspaceFolders", new JsonObject
                {
                    ["event"] = new JsonObject { ["removed"] = new JsonArray(Folder(first)), ["added"] = new JsonArray(Folder(second)) },
                }),
                Req(3, "workspace/symbol", new JsonObject()));
            JsonNode capability = Reply(output, 1)["result"]["capabilities"]["workspace"]["workspaceFolders"];
            Assert.True(capability["supported"].GetValue<bool>());
            Assert.True(capability["changeNotifications"].GetValue<bool>());
            Assert.Equal("first", Assert.Single(Reply(output, 2)["result"].AsArray())["name"].GetValue<string>());
            Assert.Equal("second", Assert.Single(Reply(output, 3)["result"].AsArray())["name"].GetValue<string>());
        }

        [Fact]
        public void TheFoldersOpenInTheEditorAreSearchedForSymbols()
        {
            string root = Path.Combine(Path.GetTempPath(), "kscript-symbols");
            string other = Path.Combine(Path.GetTempPath(), "kscript-symbols-two");
            Memory files = new Memory();
            files.Files[Path.Combine(root, "cart.kscript")] = "fn total(items) => $items->count()\nfn total_with_tax(items) => @total(items: $items) * 1.2\n";
            files.Files[Path.Combine(root, "page.html")] = "<p>total</p>";
            files.Files[Path.Combine(root, "notes.txt")] = "fn total() => 1";
            files.Files[Path.Combine(other, "report.kscript")] = "const TOTAL_ROWS = 10\n";
            JsonObject Search(int id, string query) => Req(id, "workspace/symbol", new JsonObject { ["query"] = query });
            JsonObject init = Req(1, "initialize", new JsonObject
            {
                ["capabilities"] = new JsonObject(),
                ["workspaceFolders"] = new JsonArray(
                    new JsonObject { ["uri"] = Workspace.PathToUri(root), ["name"] = "one" },
                    new JsonObject { ["uri"] = Workspace.PathToUri(other), ["name"] = "two" },
                    new JsonObject { ["uri"] = "untitled:nowhere", ["name"] = "three" }),
            });

            // The second search reads the same files again and answers from what it kept; an open file is searched as typed.
            var (_, output) = Converse(files, init, Search(2, "total"), Search(3, "total"),
                OpenDoc(Workspace.PathToUri(Path.Combine(root, "cart.kscript")), "fn sum(items) => 1\n", "kscript-code"), Search(4, "total"), Search(5, "sum"));
            Assert.Equal(new[] { "total", "total_with_tax", "$TOTAL_ROWS" }, Reply(output, 2)["result"].AsArray().Select(f => f["name"].GetValue<string>()));
            Assert.Equal(Reply(output, 2)["result"].ToJsonString(), Reply(output, 3)["result"].ToJsonString());
            Assert.Equal("$TOTAL_ROWS", Assert.Single(Reply(output, 4)["result"].AsArray())["name"].GetValue<string>());
            Assert.Equal("sum", Assert.Single(Reply(output, 5)["result"].AsArray())["name"].GetValue<string>());

            // One folder given the old way, and a project with more functions than one answer holds.
            Memory many = new Memory();
            many.Files[Path.Combine(root, "many.kscript")] = string.Concat(Enumerable.Range(0, KScriptLanguageServer.MaxWorkspaceSymbols + 5).Select(i => $"fn step{i}() => {i}\n"));
            JsonObject old = Req(1, "initialize", new JsonObject { ["capabilities"] = new JsonObject(), ["rootUri"] = Workspace.PathToUri(root) });
            (_, output) = Converse(many, old, Search(2, "step"));
            Assert.Equal(KScriptLanguageServer.MaxWorkspaceSymbols, Reply(output, 2)["result"].AsArray().Count);
        }
    }
}
