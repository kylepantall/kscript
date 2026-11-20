using System.Text.Json.Nodes;

using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Language.Features;
using KScript.Syntax.Text;
using KScript.Projects;

using KScript.Syntax;

using LspDiagnosticSeverity = KScript.Syntax.DiagnosticSeverity;

namespace KScript.LanguageServer
{
    /// <summary>
    /// The KScript language server. Speaks LSP 3.17 over a pair of streams (stdio for <c>kscript lsp</c>) with full
    /// document sync. All the language work is done by <see cref="LanguageService"/>; this class only translates.
    /// </summary>
    public sealed partial class KScriptLanguageServer
    {
        public static readonly string[] TokenTypes = { "variable", "parameter", "function", "method", "property", "keyword", "operator", "string", "number", "namespace", "type" };
        public static readonly string[] TokenModifiers = { "declaration", "readonly", "defaultLibrary" };

        private readonly JsonRpcConnection connection;
        private readonly Workspace workspace;
        private readonly LanguageService service;
        private bool initialized;
        private bool shutdownRequested;

        // kscript.json files by path, re-parsed when their text or the text of a standard they use changes.
        private readonly Dictionary<string, (string Text, ProjectConfig Config, Dictionary<string, string> Standards)> projectConfigs =
            new Dictionary<string, (string, ProjectConfig, Dictionary<string, string>)>(StringComparer.Ordinal);

        // The last problem reported for each kscript.json that can't be used, so it is logged once.
        private readonly Dictionary<string, string> configErrors = new Dictionary<string, string>(StringComparer.Ordinal);

        public KScriptLanguageServer(Stream input, Stream output, IFileSystem disk = null)
        {
            connection = new JsonRpcConnection(input, output);
            workspace = new Workspace(disk);
            service = new LanguageService(LanguageCatalog.Default, workspace);
        }

        /// <summary>Runs on stdin/stdout until the client sends exit. Returns the process exit code.</summary>
        public static Task<int> RunStdioAsync(Stream input = null, Stream output = null) =>
            new KScriptLanguageServer(input ?? Console.OpenStandardInput(), output ?? Console.OpenStandardOutput()).RunAsync();

        public async Task<int> RunAsync(CancellationToken cancellation = default)
        {
            while (!cancellation.IsCancellationRequested)
            {
                JsonObject message;
                try
                {
                    message = await connection.ReadAsync(cancellation).ConfigureAwait(false);
                }
                catch (System.Text.Json.JsonException)
                {
                    connection.SendError(null, ErrorCodes.ParseError, "Invalid JSON.");
                    continue;
                }

                if (message == null)
                {
                    return shutdownRequested ? 0 : 1;
                }

                string method = message["method"]?.GetValue<string>();
                JsonNode id = message["id"];
                JsonNode parameters = message["params"];

                if (method == "exit")
                {
                    return shutdownRequested ? 0 : 1;
                }

                if (method == null)
                {
                    continue; // A response to something we sent; we send no requests.
                }

                try
                {
                    JsonNode result = Handle(method, parameters, id != null, out bool handled);
                    if (id != null)
                    {
                        if (handled)
                        {
                            connection.SendResponse(id, result);
                        }
                        else
                        {
                            connection.SendError(id, ErrorCodes.MethodNotFound, $"Unhandled method {method}.");
                        }
                    }
                }
                catch (LspException ex)
                {
                    if (id != null)
                    {
                        connection.SendError(id, ex.Code, ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    if (id != null)
                    {
                        connection.SendError(id, ErrorCodes.InternalError, ex.Message);
                    }
                    Log($"Error handling {method}: {ex}");
                }
            }
            return 0;
        }

        private sealed class LspException : Exception
        {
            public LspException(int code, string message) : base(message) => Code = code;
            public int Code { get; }
        }

        private JsonNode Handle(string method, JsonNode p, bool isRequest, out bool handled)
        {
            handled = true;
            if (!initialized && method != "initialize")
            {
                if (isRequest)
                {
                    throw new LspException(ErrorCodes.ServerNotInitialized, "The server has not been initialized.");
                }
                return null;
            }

            switch (method)
            {
                case "initialize":
                    initialized = true;
                    return Initialize();
                case "initialized":
                case "$/cancelRequest":
                case "$/setTrace":
                case "workspace/didChangeConfiguration":
                    return null;
                case "shutdown":
                    shutdownRequested = true;
                    return null;

                case "textDocument/didOpen":
                {
                    JsonNode doc = p["textDocument"];
                    workspace.Open(doc["uri"].GetValue<string>(), doc["languageId"]?.GetValue<string>(), doc["text"].GetValue<string>(), doc["version"]?.GetValue<int>() ?? 0);
                    RefreshAll();
                    return null;
                }
                case "textDocument/didChange":
                {
                    OpenDocument doc = Document(p);
                    if (doc != null)
                    {
                        JsonArray changes = p["contentChanges"].AsArray();
                        foreach (JsonNode change in changes)
                        {
                            doc.Text = ApplyChange(doc.Text, change);
                        }
                        doc.Version = p["textDocument"]["version"]?.GetValue<int>() ?? doc.Version;
                        RefreshAll();
                    }
                    return null;
                }
                case "textDocument/didClose":
                {
                    string uri = p["textDocument"]["uri"].GetValue<string>();
                    workspace.Close(uri);
                    connection.SendNotification("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() });
                    RefreshAll();
                    return null;
                }
                case "textDocument/didSave":
                case "workspace/didChangeWatchedFiles":
                    RefreshAll();
                    return null;

                case "textDocument/completion": return Completion(p);
                case "textDocument/hover": return Hover(p);
                case "textDocument/signatureHelp": return SignatureHelp(p);
                case "textDocument/definition": return Definition(p);
                case "textDocument/references": return References(p);
                case "textDocument/documentHighlight": return Highlights(p);
                case "textDocument/prepareRename": return PrepareRename(p);
                case "textDocument/rename": return Rename(p);
                case "textDocument/documentSymbol": return DocumentSymbols(p);
                case "textDocument/semanticTokens/full": return SemanticTokens(p);
                case "textDocument/inlayHint": return InlayHints(p);
                case "textDocument/foldingRange": return FoldingRanges(p);
                case "kscript/blocks": return Blocks(p);
                case "textDocument/codeAction": return CodeActions(p);
                case "textDocument/documentLink": return DocumentLinks(p);
                case "textDocument/formatting": return Formatting(p);
            }

            handled = !isRequest;
            return null;
        }

        private static JsonObject Initialize() => new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["positionEncoding"] = "utf-16",
                ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 2, ["save"] = true },
                ["completionProvider"] = new JsonObject
                {
                    ["triggerCharacters"] = new JsonArray("<", "$", "@", ">", ".", "\"", "'", "/", "{"),
                    ["resolveProvider"] = false,
                },
                ["hoverProvider"] = true,
                ["signatureHelpProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("(", ","), ["retriggerCharacters"] = new JsonArray(",") },
                ["definitionProvider"] = true,
                ["referencesProvider"] = true,
                ["documentHighlightProvider"] = true,
                ["renameProvider"] = new JsonObject { ["prepareProvider"] = true },
                ["documentSymbolProvider"] = true,
                ["codeActionProvider"] = new JsonObject { ["codeActionKinds"] = new JsonArray("quickfix") },
                ["documentLinkProvider"] = new JsonObject { ["resolveProvider"] = false },
                ["inlayHintProvider"] = new JsonObject { ["resolveProvider"] = false },
                ["foldingRangeProvider"] = true,
                ["documentFormattingProvider"] = true,
                ["semanticTokensProvider"] = new JsonObject
                {
                    ["legend"] = new JsonObject
                    {
                        ["tokenTypes"] = new JsonArray(TokenTypes.Select(t => (JsonNode)t).ToArray()),
                        ["tokenModifiers"] = new JsonArray(TokenModifiers.Select(t => (JsonNode)t).ToArray()),
                    },
                    ["full"] = true,
                },
            },
            ["serverInfo"] = new JsonObject { ["name"] = "kscript-lsp", ["version"] = typeof(KScriptLanguageServer).Assembly.GetName().Version?.ToString() },
        };

        #region Documents

        private OpenDocument Document(JsonNode p) => workspace.Get(p?["textDocument"]?["uri"]?.GetValue<string>());

        private DocumentAnalysis Analysis(JsonNode p, out OpenDocument doc)
        {
            doc = Document(p);
            if (doc == null)
            {
                return null;
            }
            doc.Analysis ??= Analyze(doc);
            return doc.Analysis;
        }

        /// <summary>Analyzes a document and applies its project's kscript.json lint settings (rule severities, excluded paths).</summary>
        private DocumentAnalysis Analyze(OpenDocument doc)
        {
            DocumentAnalysis analysis = Analyzer.Analyze(doc.Text, doc.Path, workspace, service.Catalog, doc.Kind);
            LintRules.Apply(analysis.Diagnostics, ProjectFor(doc.Path), doc.Path);
            return analysis;
        }

        /// <summary>The nearest kscript.json above a file, or null. A file that doesn't parse is reported once and ignored.</summary>
        internal ProjectConfig ProjectFor(string path)
        {
            if (path == null)
            {
                return null;
            }
            string dir;
            try
            {
                dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }

            for (; !string.IsNullOrEmpty(dir); dir = System.IO.Path.GetDirectoryName(dir))
            {
                string file = System.IO.Path.Combine(dir, ProjectConfig.FileName);
                if (!workspace.FileExists(file))
                {
                    continue;
                }

                string text;
                try
                {
                    text = workspace.ReadText(file);
                }
                catch (IOException)
                {
                    return null;
                }
                if (projectConfigs.TryGetValue(file, out var cached) && cached.Text == text && StandardsUnchanged(cached.Standards))
                {
                    return cached.Config;
                }

                ProjectConfig config = null;
                var standards = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    // Standards are read through the workspace, so an edited (unsaved) standard file applies straight away.
                    config = ProjectConfig.Parse(text, file, workspace.FileExists, path =>
                    {
                        string standardText = workspace.ReadText(path);
                        standards[path] = standardText;
                        return standardText;
                    });
                    foreach (string warning in config.Warnings)
                    {
                        Log($"{file}: {warning}");
                    }
                }
                catch (ProjectConfigException ex)
                {
                    // Not cached, so that creating a missing standard file fixes it; logged once per problem.
                    if (!configErrors.TryGetValue(file, out string last) || last != ex.Message)
                    {
                        Log(ex.Message + " Using the default lint settings until it is fixed.");
                    }
                    configErrors[file] = ex.Message;
                    projectConfigs.Remove(file);
                    return null;
                }
                configErrors.Remove(file);
                projectConfigs[file] = (text, config, standards);
                return config;
            }
            return null;
        }

        private bool StandardsUnchanged(Dictionary<string, string> standards)
        {
            foreach (var (path, text) in standards)
            {
                try
                {
                    if (!workspace.FileExists(path) || workspace.ReadText(path) != text)
                    {
                        return false;
                    }
                }
                catch (IOException)
                {
                    return false;
                }
            }
            return true;
        }

        private static string ApplyChange(string text, JsonNode change)
        {
            JsonNode range = change["range"];
            string newText = change["text"].GetValue<string>();
            if (range == null)
            {
                return newText;
            }
            SourceText source = new SourceText(text);
            int start = Offset(source, range["start"]);
            int end = Offset(source, range["end"]);
            return text.Substring(0, start) + newText + text.Substring(Math.Max(start, end));
        }

        /// <summary>Re-analyzes every open document (imports and components may depend on the one that changed) and publishes diagnostics.</summary>
        private void RefreshAll()
        {
            foreach (OpenDocument doc in workspace.Documents.ToList())
            {
                doc.Analysis = Analyze(doc);
                JsonArray diagnostics = new JsonArray();
                foreach (Diagnostic d in doc.Analysis.Diagnostics)
                {
                    diagnostics.Add(DiagnosticJson(doc.Analysis.Source, d));
                }
                connection.SendNotification("textDocument/publishDiagnostics", new JsonObject
                {
                    ["uri"] = doc.Uri,
                    ["version"] = doc.Version,
                    ["diagnostics"] = diagnostics,
                });
            }
        }

        #endregion

        #region Features

        private JsonNode Completion(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }
            int offset = Offset(a.Source, p["position"]);
            JsonArray items = new JsonArray();
            foreach (CompletionItem item in service.GetCompletions(a, offset))
            {
                JsonObject json = new JsonObject
                {
                    ["label"] = item.Label,
                    ["kind"] = CompletionKindToLsp(item.Kind),
                    ["textEdit"] = new JsonObject { ["range"] = Range(a.Source, item.ReplaceSpan), ["newText"] = item.InsertText ?? item.Label },
                    ["insertTextFormat"] = item.IsSnippet ? 2 : 1,
                };
                if (item.Detail != null) json["detail"] = item.Detail;
                if (item.Documentation != null) json["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = item.Documentation };
                if (item.SortText != null) json["sortText"] = item.SortText;
                if (item.FilterText != null) json["filterText"] = item.FilterText;
                if (item.Deprecated) json["tags"] = new JsonArray(1);
                items.Add(json);
            }
            return new JsonObject { ["isIncomplete"] = false, ["items"] = items };
        }

        private JsonNode Hover(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            HoverResult hover = a == null ? null : service.GetHover(a, Offset(a.Source, p["position"]));
            return hover == null ? null : new JsonObject
            {
                ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = hover.Markdown },
                ["range"] = Range(a.Source, hover.Span),
            };
        }

        private JsonNode SignatureHelp(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            SignatureHelpResult help = a == null ? null : service.GetSignatureHelp(a, Offset(a.Source, p["position"]));
            if (help == null)
            {
                return null;
            }
            JsonArray signatures = new JsonArray();
            foreach (SignatureInfo s in help.Signatures)
            {
                JsonArray ps = new JsonArray();
                foreach ((string label, string doc) in s.Parameters)
                {
                    JsonObject pj = new JsonObject { ["label"] = label };
                    if (doc != null) pj["documentation"] = doc;
                    ps.Add(pj);
                }
                JsonObject sj = new JsonObject { ["label"] = s.Label, ["parameters"] = ps };
                if (s.Documentation != null) sj["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = s.Documentation };
                signatures.Add(sj);
            }
            return new JsonObject { ["signatures"] = signatures, ["activeSignature"] = help.ActiveSignature, ["activeParameter"] = help.ActiveParameter };
        }

        private JsonNode Definition(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out OpenDocument doc);
            if (a == null)
            {
                return null;
            }
            JsonArray locations = new JsonArray();
            foreach (SourceLocation location in service.GetDefinition(a, Offset(a.Source, p["position"])))
            {
                locations.Add(Location(doc, a, location));
            }
            return locations;
        }

        private JsonNode References(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out OpenDocument doc);
            if (a == null)
            {
                return null;
            }
            bool includeDeclaration = p["context"]?["includeDeclaration"]?.GetValue<bool>() ?? true;
            JsonArray locations = new JsonArray();
            foreach (SymbolReference r in service.GetReferences(a, Offset(a.Source, p["position"]), includeDeclaration))
            {
                locations.Add(new JsonObject { ["uri"] = doc.Uri, ["range"] = Range(a.Source, r.Span) });
            }
            return locations;
        }

        private JsonNode Highlights(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }
            JsonArray highlights = new JsonArray();
            foreach (SymbolReference r in service.GetReferences(a, Offset(a.Source, p["position"]), includeDeclaration: true))
            {
                highlights.Add(new JsonObject { ["range"] = Range(a.Source, r.Span), ["kind"] = r.IsWrite ? 3 : 2 });
            }
            return highlights;
        }

        private JsonNode PrepareRename(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            TextSpan? span = a == null ? null : service.PrepareRename(a, Offset(a.Source, p["position"]));
            return span.HasValue ? Range(a.Source, span.Value) : null;
        }

        private JsonNode Rename(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out OpenDocument doc);
            if (a == null)
            {
                return null;
            }
            RenameResult result = service.Rename(a, Offset(a.Source, p["position"]), p["newName"]?.GetValue<string>());
            if (result.Error != null)
            {
                throw new LspException(ErrorCodes.RequestFailed, result.Error);
            }
            return new JsonObject { ["changes"] = new JsonObject { [doc.Uri] = Edits(a.Source, result.Changes) } };
        }

        private JsonNode FoldingRanges(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }
            JsonArray ranges = new JsonArray();
            foreach (FoldingRegion r in service.GetFoldingRegions(a))
            {
                JsonObject json = new JsonObject { ["startLine"] = r.StartLine, ["endLine"] = r.EndLine };
                if (r.IsComment) json["kind"] = "comment";
                ranges.Add(json);
            }
            return ranges;
        }

        /// <summary>KScript's own request: the top-level blocks of a script, for the editor's notebook-style divider lines.</summary>
        private JsonNode Blocks(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            JsonArray blocks = new JsonArray();
            if (a != null)
            {
                foreach (CodeBlock b in service.GetBlocks(a))
                {
                    blocks.Add(new JsonObject { ["range"] = Range(a.Source, b.Span), ["label"] = b.Label });
                }
            }
            return new JsonObject { ["blocks"] = blocks };
        }

        private JsonNode InlayHints(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }
            TextSpan? range = null;
            if (p["range"] is JsonNode r)
            {
                range = TextSpan.FromBounds(Offset(a.Source, r["start"]), Offset(a.Source, r["end"]));
            }
            JsonArray hints = new JsonArray();
            foreach (InlayHint hint in service.GetInlayHints(a, range))
            {
                JsonObject json = new JsonObject
                {
                    ["position"] = Position(a.Source.GetPosition(hint.Offset)),
                    ["label"] = hint.Label,
                    ["kind"] = 1,
                    ["paddingLeft"] = false,
                };
                if (hint.Tooltip != null) json["tooltip"] = new JsonObject { ["kind"] = "markdown", ["value"] = hint.Tooltip };
                hints.Add(json);
            }
            return hints;
        }

        private JsonNode DocumentSymbols(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }

            JsonArray Convert(IEnumerable<OutlineItem> items)
            {
                JsonArray array = new JsonArray();
                foreach (OutlineItem item in items)
                {
                    JsonObject json = new JsonObject
                    {
                        ["name"] = item.Name,
                        ["kind"] = OutlineKindToLsp(item.Kind),
                        ["range"] = Range(a.Source, item.Span),
                        ["selectionRange"] = Range(a.Source, item.SelectionSpan),
                        ["children"] = Convert(item.Children),
                    };
                    if (item.Detail != null) json["detail"] = item.Detail;
                    array.Add(json);
                }
                return array;
            }

            return Convert(service.GetOutline(a));
        }

        private JsonNode SemanticTokens(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }

            JsonArray data = new JsonArray();
            int lastLine = 0;
            int lastChar = 0;
            foreach (SemanticToken token in service.GetSemanticTokens(a))
            {
                // LSP tokens can't span lines: split multi-line tokens (strings, mostly).
                int start = token.Span.Start;
                while (start < token.Span.End)
                {
                    LinePosition pos = a.Source.GetPosition(start);
                    int lineEnd = Math.Min(token.Span.End, a.Source.GetOffset(pos.Line + 1, 0));
                    int length = lineEnd - start;
                    string piece = a.Source.Text.Substring(start, length).TrimEnd('\r', '\n');
                    if (piece.Length > 0)
                    {
                        data.Add(pos.Line - lastLine);
                        data.Add(pos.Line == lastLine ? pos.Character - lastChar : pos.Character);
                        data.Add(piece.Length);
                        data.Add((int)token.Type);
                        data.Add((int)token.Modifiers);
                        lastLine = pos.Line;
                        lastChar = pos.Character;
                    }
                    start = lineEnd; // Always past start: a token ends inside the text, and the next line starts after it.
                }
            }
            return new JsonObject { ["data"] = data };
        }

        /// <summary>A diagnostic as LSP sends it. Unused code (KS6002-KS6005) is tagged Unnecessary (1), which editors show faded.</summary>
        private static JsonObject DiagnosticJson(SourceText source, Diagnostic d)
        {
            JsonObject json = new JsonObject
            {
                ["range"] = Range(source, d.Span),
                ["severity"] = (int)d.Severity,
                ["code"] = d.Code,
                ["source"] = "kscript",
                ["message"] = d.Message,
            };
            if (DiagnosticCodes.IsUnnecessary(d.Code))
            {
                json["tags"] = new JsonArray(1);
            }
            return json;
        }

        private JsonNode CodeActions(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out OpenDocument doc);
            if (a == null)
            {
                return null;
            }
            TextSpan span = TextSpan.FromBounds(Offset(a.Source, p["range"]["start"]), Offset(a.Source, p["range"]["end"]));
            JsonArray actions = new JsonArray();
            foreach ((Diagnostic d, QuickFix fix) in service.GetQuickFixes(a, span))
            {
                actions.Add(new JsonObject
                {
                    ["title"] = fix.Title,
                    ["kind"] = "quickfix",
                    ["isPreferred"] = d.Fixes.Count == 1,
                    ["diagnostics"] = new JsonArray(DiagnosticJson(a.Source, d)),
                    ["edit"] = new JsonObject { ["changes"] = new JsonObject { [doc.Uri] = Edits(a.Source, fix.Changes) } },
                });
            }
            return actions;
        }

        private JsonNode DocumentLinks(JsonNode p)
        {
            DocumentAnalysis a = Analysis(p, out _);
            if (a == null)
            {
                return null;
            }
            JsonArray links = new JsonArray();
            foreach (DocumentLink link in a.Links)
            {
                links.Add(new JsonObject { ["range"] = Range(a.Source, link.Span), ["target"] = Workspace.PathToUri(link.TargetPath), ["tooltip"] = "Open " + link.Kind });
            }
            return links;
        }

        #endregion

        #region Conversion

        private static int Offset(SourceText source, JsonNode position) =>
            source.GetOffset(position["line"].GetValue<int>(), position["character"].GetValue<int>());

        private static JsonObject Position(LinePosition p) => new JsonObject { ["line"] = p.Line, ["character"] = p.Character };

        private static JsonObject Range(SourceText source, TextSpan span) => new JsonObject
        {
            ["start"] = Position(source.GetPosition(span.Start)),
            ["end"] = Position(source.GetPosition(span.End)),
        };

        private static JsonArray Edits(SourceText source, IEnumerable<TextChange> changes)
        {
            JsonArray edits = new JsonArray();
            foreach (TextChange c in changes)
            {
                edits.Add(new JsonObject { ["range"] = Range(source, c.Span), ["newText"] = c.NewText });
            }
            return edits;
        }

        private static JsonObject Location(OpenDocument doc, DocumentAnalysis a, SourceLocation location)
        {
            if (location.Path == null)
            {
                return new JsonObject { ["uri"] = doc.Uri, ["range"] = Range(a.Source, location.Span) };
            }
            return new JsonObject
            {
                ["uri"] = Workspace.PathToUri(location.Path),
                ["range"] = new JsonObject { ["start"] = Position(location.Start), ["end"] = Position(location.End) },
            };
        }

        internal static int OutlineKindToLsp(OutlineKind kind) => kind switch
        {
            OutlineKind.Function => 12,
            OutlineKind.Constant => 14,
            OutlineKind.Module => 2,
            OutlineKind.Element => 8,
            _ => 13,                          // Variable
        };

        internal static int CompletionKindToLsp(CompletionKind kind) => kind switch
        {
            CompletionKind.Element => 10,     // Property
            CompletionKind.Attribute => 5,    // Field
            CompletionKind.Value => 12,       // Value
            CompletionKind.Variable => 6,     // Variable
            CompletionKind.Parameter => 6,
            CompletionKind.Constant => 21,    // Constant
            CompletionKind.Function => 3,     // Function
            CompletionKind.Command => 3,
            CompletionKind.Method => 2,       // Method
            CompletionKind.Field => 5,
            CompletionKind.Keyword => 14,     // Keyword
            CompletionKind.File => 17,        // File
            CompletionKind.Folder => 19,      // Folder
            _ => 15,                          // Snippet
        };

        private void Log(string message) =>
            connection.SendNotification("window/logMessage", new JsonObject { ["type"] = 1, ["message"] = message });

        #endregion
    }
}
