using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

using KScript.Language.Analysis;
using KScript.LanguageServer;

namespace KScript.Language.Tests
{
    /// <summary>Drives the real server over a pipe with LSP framing, the way an editor does.</summary>
    public sealed class ProtocolTests : IDisposable
    {
        private const string Uri = "file:///work/test.ks.xml";

        private readonly AnonymousPipeServerStream toServer = new AnonymousPipeServerStream(PipeDirection.Out);
        private readonly AnonymousPipeServerStream fromServer = new AnonymousPipeServerStream(PipeDirection.In);
        private readonly Task<int> server;
        private readonly List<JsonObject> notifications = new List<JsonObject>();
        private int nextId = 1;

        public ProtocolTests()
        {
            Stream serverIn = new AnonymousPipeClientStream(PipeDirection.In, toServer.ClientSafePipeHandle);
            Stream serverOut = new AnonymousPipeClientStream(PipeDirection.Out, fromServer.ClientSafePipeHandle);
            server = Task.Run(() => new KScriptLanguageServer(serverIn, serverOut, EmptyFileSystem.Instance).RunAsync());
        }

        public void Dispose()
        {
            toServer.Dispose();
            fromServer.Dispose();
        }

        #region Client

        private void Send(JsonObject message)
        {
            message["jsonrpc"] = "2.0";
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            toServer.Write(header);
            toServer.Write(body);
            toServer.Flush();
        }

        private JsonObject Read()
        {
            StringBuilder header = new StringBuilder();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int b = fromServer.ReadByte();
                if (b < 0)
                {
                    return null;
                }
                header.Append((char)b);
            }
            int length = int.Parse(header.ToString().Split("Content-Length:")[1].Split("\r\n")[0].Trim());
            byte[] body = new byte[length];
            int read = 0;
            while (read < length)
            {
                read += fromServer.Read(body, read, length - read);
            }
            return (JsonObject)JsonNode.Parse(body);
        }

        private JsonObject Request(string method, JsonNode parameters = null)
        {
            int id = nextId++;
            Send(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters });
            Task<JsonObject> wait = Task.Run(() =>
            {
                while (true)
                {
                    JsonObject message = Read();
                    if (message == null)
                    {
                        return null;
                    }
                    if (message["id"] != null && message["id"].GetValue<int>() == id)
                    {
                        return message;
                    }
                    notifications.Add(message);
                }
            });
            Assert.True(wait.Wait(TimeSpan.FromSeconds(20)), method + " timed out.");
            return wait.Result;
        }

        private void Notify(string method, JsonNode parameters) => Send(new JsonObject { ["method"] = method, ["params"] = parameters });

        private JsonObject Initialize()
        {
            JsonObject response = Request("initialize", new JsonObject { ["processId"] = null, ["rootUri"] = null, ["capabilities"] = new JsonObject() });
            Notify("initialized", new JsonObject());
            return response;
        }

        private void Open(string text, string uri = Uri) => Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "kscript", ["version"] = 1, ["text"] = text },
        });

        /// <summary>The latest diagnostics published for a document (waits for them with a harmless request).</summary>
        private JsonArray Diagnostics(string uri = Uri)
        {
            Request("textDocument/documentSymbol", Doc(uri));
            JsonObject published = notifications.LastOrDefault(n => n["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" && n["params"]["uri"].GetValue<string>() == uri);
            Assert.NotNull(published);
            return published["params"]["diagnostics"].AsArray();
        }

        private static JsonObject Doc(string uri = Uri) => new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } };

        private static JsonObject At(int line, int character, string uri = Uri) => new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
        };

        private static JsonObject Range(int line, int start, int endLine, int end) => new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = line, ["character"] = start },
            ["end"] = new JsonObject { ["line"] = endLine, ["character"] = end },
        };

        #endregion

        [Fact]
        public void RequestsBeforeInitializeAreRejected()
        {
            JsonObject response = Request("textDocument/hover", At(0, 0));
            Assert.Equal(ErrorCodes.ServerNotInitialized, response["error"]["code"].GetValue<int>());
        }

        [Fact]
        public void InitializeAdvertisesCapabilities()
        {
            JsonObject caps = Initialize()["result"]["capabilities"].AsObject();
            Assert.Equal(2, caps["textDocumentSync"]["change"].GetValue<int>());
            foreach (string provider in new[] { "completionProvider", "hoverProvider", "signatureHelpProvider", "definitionProvider", "referencesProvider",
                         "renameProvider", "documentSymbolProvider", "semanticTokensProvider", "codeActionProvider", "documentLinkProvider", "documentHighlightProvider" })
            {
                Assert.True(caps.ContainsKey(provider), provider);
            }
            JsonArray legend = caps["semanticTokensProvider"]["legend"]["tokenTypes"].AsArray();
            Assert.Equal(KScriptLanguageServer.TokenTypes, legend.Select(t => t.GetValue<string>()));
            Assert.Equal(Enum.GetNames<Features.SemanticTokenType>().Length, legend.Count);
        }

        [Fact]
        public void PublishesDiagnosticsAndFollowsIncrementalEdits()
        {
            Initialize();
            Open("<kscript>\n<ehco>hi</ehco>\n</kscript>");
            JsonArray diagnostics = Diagnostics();
            JsonObject d = Assert.Single(diagnostics).AsObject();
            Assert.Equal("KS3001", d["code"].GetValue<string>());
            Assert.Equal(1, d["range"]["start"]["line"].GetValue<int>());
            Assert.Equal(1, d["range"]["start"]["character"].GetValue<int>());
            Assert.Equal(1, d["severity"].GetValue<int>());

            // Fix both tag names with two incremental edits.
            Notify("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri, ["version"] = 2 },
                ["contentChanges"] = new JsonArray
                {
                    new JsonObject { ["range"] = Range(1, 10, 1, 14), ["text"] = "echo" },
                    new JsonObject { ["range"] = Range(1, 1, 1, 5), ["text"] = "echo" },
                },
            });
            Assert.Empty(Diagnostics());
        }

        [Fact]
        public void ClosingADocumentClearsItsDiagnostics()
        {
            Initialize();
            Open("<kscript><ehco /></kscript>");
            Assert.NotEmpty(Diagnostics());
            Notify("textDocument/didClose", Doc());
            Request("shutdown");
            JsonObject last = notifications.Last(n => n["method"]?.GetValue<string>() == "textDocument/publishDiagnostics");
            Assert.Empty(last["params"]["diagnostics"].AsArray());
        }

        [Fact]
        public void CompletionAddsTheImportAPackageNeeds()
        {
            Initialize();
            Open("<kscript>\n<echo>{ @ }</echo>\n</kscript>");
            JsonArray items = Request("textDocument/completion", At(1, 9))["result"]["items"].AsArray();
            JsonNode readText = items.Single(i => i["label"].GetValue<string>() == "read_text");
            JsonNode edit = Assert.Single(readText["additionalTextEdits"].AsArray());
            Assert.Equal("\n<import package=\"KScript.IO\" />", edit["newText"].GetValue<string>());
            Assert.Equal(0, edit["range"]["start"]["line"].GetValue<int>());
            Assert.Null(items.Single(i => i["label"].GetValue<string>() == "now")["additionalTextEdits"]);
        }

        [Fact]
        public void CompletionAfterABracketOffersNamedArgumentsAndSuggestsAgainForEnums()
        {
            JsonArray triggers = Initialize()["result"]["capabilities"]["completionProvider"]["triggerCharacters"].AsArray();
            Assert.Contains(triggers, t => t.GetValue<string>() == "(");
            Assert.Contains(triggers, t => t.GetValue<string>() == ",");
            Open("<kscript>\n<enum name=\"Size\" values=\"small, large\" />\n<fn name=\"pack\" params=\"size: Size, n = 1\" value=\"1\" />\n<echo>{ @pack() }</echo>\n<echo>{ (1) }</echo>\n</kscript>");
            JsonArray items = Request("textDocument/completion", At(3, 14).Also(o => o["context"] = new JsonObject { ["triggerKind"] = 2, ["triggerCharacter"] = "(" }))["result"]["items"].AsArray();
            JsonNode size = items.Single(i => i["label"].GetValue<string>() == "size:");
            Assert.Equal("editor.action.triggerSuggest", size["command"]["command"].GetValue<string>());
            Assert.Null(items.Single(i => i["label"].GetValue<string>() == "n:")["command"]);
            Assert.Contains(items, i => i["label"].GetValue<string>() == "large");
            JsonArray quiet = Request("textDocument/completion", At(4, 9).Also(o => o["context"] = new JsonObject { ["triggerKind"] = 2, ["triggerCharacter"] = "(" }))["result"]["items"].AsArray();
            Assert.Empty(quiet);
        }

        [Fact]
        public void SignatureHelpSendsEachParameterAsItsPlaceInTheLabel()
        {
            Initialize();
            // The second parameter's name, "number", also appears earlier as the first one's type.
            Open("<kscript>\n<fn name=\"f\" params=\"a: number, number = 1\" value=\"1\" />\n<echo>{ @f(1, ) }</echo>\n</kscript>");
            JsonObject help = Request("textDocument/signatureHelp", At(2, 14))["result"].AsObject();
            JsonObject signature = help["signatures"][0].AsObject();
            string label = signature["label"].GetValue<string>();
            JsonArray second = signature["parameters"][1]["label"].AsArray();
            Assert.Equal("number = 1", label.Substring(second[0].GetValue<int>(), second[1].GetValue<int>() - second[0].GetValue<int>()));
            Assert.Equal(1, help["activeParameter"].GetValue<int>());
            Assert.Equal(1, signature["activeParameter"].GetValue<int>());
        }

        [Fact]
        public void AnswersEditorRequests()
        {
            Initialize();
            //    0         1         2         3
            //    0123456789012345678901234567890123456
            Open("<kscript>\n" +
                 "<let name=\"total\" value=\"1\" />\n" +   // line 1
                 "<echo>{ $total->round() }</echo>\n" +   // line 2
                 "<echo>@join_path('a', )</echo>\n" +     // line 3
                 "<if condition=\"true\"></if>\n" +       // line 4
                 "</kscript>");

            JsonArray items = Request("textDocument/completion", At(2, 16))["result"]["items"].AsArray();
            Assert.Contains(items, i => i["label"].GetValue<string>() == "round");

            string hover = Request("textDocument/hover", At(2, 10))["result"]["contents"]["value"].GetValue<string>();
            Assert.Contains("total", hover);

            JsonObject help = Request("textDocument/signatureHelp", At(3, 22))["result"].AsObject();
            Assert.Equal(1, help["activeParameter"].GetValue<int>());

            JsonArray definition = Request("textDocument/definition", At(2, 10))["result"].AsArray();
            Assert.Equal(1, Assert.Single(definition)["range"]["start"]["line"].GetValue<int>());

            JsonArray references = Request("textDocument/references", At(2, 10).Also(o => o["context"] = new JsonObject { ["includeDeclaration"] = true }))["result"].AsArray();
            Assert.Equal(2, references.Count);

            JsonObject prepare = Request("textDocument/prepareRename", At(2, 10))["result"].AsObject();
            Assert.Equal(9, prepare["start"]["character"].GetValue<int>());

            JsonObject edit = Request("textDocument/rename", At(2, 10).Also(o => o["newName"] = "sum"))["result"].AsObject();
            Assert.Equal(2, edit["changes"][Uri].AsArray().Count);

            JsonObject badRename = Request("textDocument/rename", At(2, 10).Also(o => o["newName"] = "2x"));
            Assert.NotNull(badRename["error"]);

            JsonArray symbols = Request("textDocument/documentSymbol", Doc())["result"].AsArray();
            Assert.Contains(symbols, s => s["name"].GetValue<string>() == "$total");

            JsonArray data = Request("textDocument/semanticTokens/full", Doc())["result"]["data"].AsArray();
            Assert.True(data.Count > 0 && data.Count % 5 == 0);

            JsonArray actions = Request("textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["range"] = Range(4, 4, 4, 4),
                ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() },
            })["result"].AsArray();
            JsonObject action = Assert.Single(actions).AsObject();
            Assert.Equal("quickfix", action["kind"].GetValue<string>());
            Assert.Equal("test", action["edit"]["changes"][Uri][0]["newText"].GetValue<string>());

            JsonArray highlights = Request("textDocument/documentHighlight", At(2, 10))["result"].AsArray();
            Assert.Equal(2, highlights.Count);
        }

        [Fact]
        public void UnusedCodeIsTaggedUnnecessary()
        {
            Initialize();
            Open("<kscript>\n  <let name=\"spare\" value=\"1\" />\n  <echo>{ $nope }</echo>\n</kscript>");
            JsonArray diagnostics = Diagnostics();
            JsonNode unused = diagnostics.Single(d => d["code"].GetValue<string>() == "KS6003");
            Assert.Equal(1, Assert.Single(unused["tags"].AsArray()).GetValue<int>());
            Assert.Null(diagnostics.Single(d => d["code"].GetValue<string>() == "KS4001")["tags"]);

            JsonArray actions = Request("textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = Uri },
                ["range"] = Range(1, 14, 1, 14),
                ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() },
            })["result"].AsArray();
            Assert.Equal(new[] { "Remove $spare", "Rename to _spare (unused on purpose)" }, actions.Select(a => a["title"].GetValue<string>()));
            Assert.All(actions, a => Assert.Equal(1, a["diagnostics"][0]["tags"][0].GetValue<int>()));
        }

                [Fact]
        public void UnknownRequestsGetMethodNotFound()
        {
            Initialize();
            Assert.Equal(ErrorCodes.MethodNotFound, Request("textDocument/frobnicate", Doc())["error"]["code"].GetValue<int>());
        }

        [Fact]
        public void RequestsForUnopenedDocumentsDoNotCrash()
        {
            Initialize();
            JsonObject response = Request("textDocument/hover", At(0, 0, "file:///nowhere/x.ks.xml"));
            Assert.True(response["error"] != null || response["result"] == null);
            Assert.NotNull(Request("shutdown"));
        }

        [Fact]
        public async Task ExitAfterShutdownReturnsZero()
        {
            Initialize();
            Assert.Null(Request("shutdown")["error"]);
            Notify("exit", null);
            Assert.Equal(0, await server.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public async Task ExitWithoutShutdownReturnsOne()
        {
            Initialize();
            Notify("exit", null);
            Assert.Equal(1, await server.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void PositionsUseUtf16()
        {
            Initialize();
            // The emoji is two UTF-16 code units, so the name in $nope starts at character 12 on line 1.
            Open("<kscript>\n<echo>😀 { $nope }</echo>\n</kscript>");
            JsonObject d = Assert.Single(Diagnostics()).AsObject();
            Assert.Equal(12, d["range"]["start"]["character"].GetValue<int>());
        }

        [Fact]
        public void ProjectLintRulesApplyInTheEditor()
        {
            Initialize();
            const string config = "file:///work/kscript.json";
            Notify("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = config, ["languageId"] = "json", ["version"] = 1,
                    ["text"] = "{ \"lint\": { \"rules\": { \"KS3001\": \"hint\", \"KS4001\": \"off\" }, \"exclude\": [\"_templates\"] } }",
                },
            });
            Open("<kscript>\n<ehco />\n<echo>$nope</echo>\n</kscript>");
            JsonObject d = Assert.Single(Diagnostics()).AsObject();
            Assert.Equal("KS3001", d["code"].GetValue<string>());
            Assert.Equal(4, d["severity"].GetValue<int>());

            const string template = "file:///work/_templates/page.ks.xml";
            Open("<kscript><ehco>{{ name }}</ehco></kscript>", template);
            Assert.Empty(Diagnostics(template));

            // Editing kscript.json takes effect without restarting the server.
            Notify("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = config, ["version"] = 2 },
                ["contentChanges"] = new JsonArray { new JsonObject { ["text"] = "{ \"lint\": { \"exclude\": [] } }" } },
            });
            Assert.Equal(2, Diagnostics().Count);
            Assert.NotEmpty(Diagnostics(template));
        }

        [Fact]
        public void StandardsApplyInTheEditorAndEditsToThemTakeEffect()
        {
            Initialize();
            void OpenJson(string uri, string text) => Notify("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "json", ["version"] = 1, ["text"] = text },
            });
            const string standard = "file:///work/team.kscript-standard.json";
            OpenJson(standard, "{ \"lint\": { \"rules\": { \"KS3001\": \"hint\" } } }");
            OpenJson("file:///work/kscript.json", "{ \"standard\": \"team.kscript-standard.json\" }");
            Open("<kscript>\n<ehco />\n</kscript>");
            Assert.Equal(4, Assert.Single(Diagnostics()).AsObject()["severity"].GetValue<int>());

            // Editing the standard (without saving) changes the project's rules straight away.
            Notify("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = standard, ["version"] = 2 },
                ["contentChanges"] = new JsonArray { new JsonObject { ["text"] = "{ \"lint\": { \"rules\": { \"KS3001\": \"off\" } } }" } },
            });
            Assert.Empty(Diagnostics());
        }

        [Fact]
        public void FormatsDocuments()
        {
            JsonObject caps = Initialize()["result"]["capabilities"].AsObject();
            Assert.True(caps["documentFormattingProvider"].GetValue<bool>());

            string text = "<kscript>\n<if test=\"yes\">\n<echo>hi</echo>\n</if>\n</kscript>";
            Open(text);
            JsonObject request = Doc().Also(o => o["options"] = new JsonObject { ["tabSize"] = 4, ["insertSpaces"] = true });
            JsonArray edits = Request("textDocument/formatting", request)["result"].AsArray();
            JsonObject edit = Assert.Single(edits).AsObject();
            Syntax.Text.SourceText source = new Syntax.Text.SourceText(text);
            int start = source.GetOffset(edit["range"]["start"]["line"].GetValue<int>(), edit["range"]["start"]["character"].GetValue<int>());
            int end = source.GetOffset(edit["range"]["end"]["line"].GetValue<int>(), edit["range"]["end"]["character"].GetValue<int>());
            string formatted = text.Substring(0, start) + edit["newText"].GetValue<string>() + text.Substring(end);
            Assert.Equal("<kscript>\n    <if test=\"yes\">\n        <echo>hi</echo>\n    </if>\n</kscript>\n", formatted);

            // A document with markup errors is left alone.
            const string broken = "file:///work/broken.ks.xml";
            Open("<kscript>\n<if test=\"yes\">\n</kscript>", broken);
            Assert.Empty(Request("textDocument/formatting", Doc(broken))["result"].AsArray());
        }

        [Fact]
        public void FormatsKscriptDocuments()
        {
            Initialize();
            const string uri = "file:///work/code.kscript";
            string text = "x=1\nif $x > 0 {\necho \"hi\"\n}\n";
            Open(text, uri);
            JsonArray edits = Request("textDocument/formatting", Doc(uri))["result"].AsArray();
            JsonObject edit = Assert.Single(edits).AsObject();
            Syntax.Text.SourceText source = new Syntax.Text.SourceText(text);
            int start = source.GetOffset(edit["range"]["start"]["line"].GetValue<int>(), edit["range"]["start"]["character"].GetValue<int>());
            int end = source.GetOffset(edit["range"]["end"]["line"].GetValue<int>(), edit["range"]["end"]["character"].GetValue<int>());
            string formatted = text.Substring(0, start) + edit["newText"].GetValue<string>() + text.Substring(end);
            Assert.Equal("x = 1\nif $x > 0 {\n  echo \"hi\"\n}\n", formatted);
        }
    }

    internal static class JsonExtensions
    {
        public static JsonObject Also(this JsonObject o, Action<JsonObject> change)
        {
            change(o);
            return o;
        }
    }
}
