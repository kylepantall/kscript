using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

using KScript.Docs;
using KScript.Docs.Mcp;
using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

using Microsoft.AspNetCore.Http;

namespace KScript.Tests;

/// <summary>kscript mcp: the docs served to AI coding assistants over the Model Context Protocol.</summary>
public sealed class McpTests : IDisposable
{
    private static readonly Lazy<DocsSite> Site = new Lazy<DocsSite>(() => DocsSite.Build());

    private readonly string dir = Directory.CreateTempSubdirectory("kscript-mcp").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string Write(string name, string text)
    {
        string path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private McpServer Server(string format = "kscript") => new McpServer(Site.Value, format, dir);

    private static JsonObject Request(string method, JsonObject parameters = null, int id = 1) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };

    private static JsonNode Result(McpServer server, string method, JsonObject parameters = null)
    {
        JsonNode answer = server.Handle(Request(method, parameters));
        Assert.Null(answer!["error"]);
        return answer["result"]!;
    }

    /// <summary>Calls a tool and returns its text and whether it was an error.</summary>
    private static (string Text, bool IsError) Call(McpServer server, string tool, object arguments = null)
    {
        JsonNode args = arguments == null ? new JsonObject() : JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(arguments));
        JsonNode result = Result(server, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = args });
        return (result["content"]![0]!["text"]!.GetValue<string>(), result["isError"]!.GetValue<bool>());
    }

    #region The protocol

    [Fact]
    public void Initialize_agrees_on_a_protocol_version_and_describes_the_server()
    {
        McpServer server = Server();
        JsonNode result = Result(server, "initialize", new JsonObject { ["protocolVersion"] = "2025-06-18" });
        Assert.Equal("2025-06-18", result["protocolVersion"]!.GetValue<string>());
        Assert.Equal("kscript-docs", result["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(result["capabilities"]!["tools"]);
        Assert.NotNull(result["capabilities"]!["resources"]);
        Assert.Contains(".kscript unless", result["instructions"]!.GetValue<string>());

        // A version the server doesn't know gets its newest one.
        Assert.Equal(McpServer.ProtocolVersions[0], Result(server, "initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" })["protocolVersion"]!.GetValue<string>());
        Assert.Contains("as XML unless", Server("xml").Instructions);
    }

    [Fact]
    public void Messages_get_the_answers_json_rpc_expects()
    {
        McpServer server = Server();
        Assert.NotNull(Result(server, "ping"));
        Assert.Empty(Result(server, "prompts/list")["prompts"]!.AsArray());

        Assert.Contains("-32700", server.HandleLine("{ not json"));
        Assert.Equal(-32600, server.Handle(JsonNode.Parse("42"))!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32600, server.Handle(new JsonObject { ["id"] = 1 })!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32601, server.Handle(Request("nope/nope"))!["error"]!["code"]!.GetValue<int>());

        // Notifications and responses get no answer, even when they would fail.
        Assert.Null(server.HandleLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        Assert.Null(server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = "nope" } }));
        Assert.Null(server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "ping" }));
        Assert.Null(server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 5, ["result"] = new JsonObject() }));
        Assert.Null(server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 5, ["error"] = new JsonObject() }));

        // A batch gets an array of the answers, or nothing if all were notifications.
        var batch = (JsonArray)server.Handle(new JsonArray(Request("ping", id: 1), new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "ping" }, Request("ping", id: 2)))!;
        Assert.Equal(2, batch.Count);
        Assert.Null(server.Handle(new JsonArray(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" })));
    }

    [Fact]
    public void Tools_are_listed_with_schemas_and_unknown_ones_are_refused()
    {
        McpServer server = Server();
        JsonArray tools = Result(server, "tools/list")["tools"]!.AsArray();
        string[] names = tools.Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "search_docs", "read_doc", "lookup", "list_make_commands", "check_code", "format_code", "convert_code", "run_code" }, names);
        foreach (JsonNode t in tools)
        {
            Assert.Equal("object", t!["inputSchema"]!["type"]!.GetValue<string>());
            Assert.True(t["annotations"]!["readOnlyHint"]!.GetValue<bool>());
            Assert.False(string.IsNullOrWhiteSpace(t["description"]!.GetValue<string>()));
        }
        Assert.Equal("query", tools[0]!["inputSchema"]!["required"]![0]!.GetValue<string>());
        Assert.Equal(new[] { "kscript", "xml" }, tools[1]!["inputSchema"]!["properties"]!["format"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));

        JsonNode unknown = server.Handle(Request("tools/call", new JsonObject { ["name"] = "nope" }))!;
        Assert.Equal(-32602, unknown["error"]!["code"]!.GetValue<int>());

        // Arguments that aren't an object are taken as none.
        JsonNode noArgs = Result(server, "tools/call", new JsonObject { ["name"] = "search_docs", ["arguments"] = 3 });
        Assert.True(noArgs["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void Every_page_is_a_resource_and_reads_as_markdown()
    {
        McpServer server = Server();
        JsonArray resources = Result(server, "resources/list")["resources"]!.AsArray();
        var uris = resources.Select(r => r!["uri"]!.GetValue<string>()).ToList();
        Assert.Contains("kscript://docs", uris);
        Assert.Contains("kscript://docs/guide/mcp", uris);
        Assert.Contains("kscript://docs/reference/io", uris);
        Assert.Contains("kscript://docs/cli/make", uris);
        Assert.DoesNotContain("kscript://docs/playground", uris);
        Assert.DoesNotContain("kscript://docs/404", uris);
        Assert.Equal("home", resources.First(r => r!["uri"]!.GetValue<string>() == "kscript://docs")!["name"]!.GetValue<string>());

        foreach (string uri in uris)
        {
            JsonNode content = Result(server, "resources/read", new JsonObject { ["uri"] = uri })["contents"]![0]!;
            Assert.Equal("text/markdown", content["mimeType"]!.GetValue<string>());
            Assert.StartsWith("# ", content["text"]!.GetValue<string>());
        }

        Assert.Equal(2, Result(server, "resources/templates/list")["resourceTemplates"]!.AsArray().Count);
        Assert.Equal(-32002, server.Handle(Request("resources/read", new JsonObject { ["uri"] = "kscript://docs/nope" }))!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32002, server.Handle(Request("resources/read", new JsonObject { ["uri"] = "file:///etc/passwd" }))!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32002, server.Handle(Request("resources/read"))!["error"]!["code"]!.GetValue<int>());
    }

    #endregion

    #region The docs as Markdown

    [Fact]
    public void Names_and_uris_resolve_to_pages()
    {
        DocsMarkdown docs = Server().Docs;
        Assert.Equal("/guide/language", docs.Resolve("kscript://docs/guide/language#loops"));
        Assert.Equal("/guide/language", docs.Resolve("/guide/language"));
        Assert.Equal("/guide/language", docs.Resolve("language"));
        Assert.Equal("/guide/language", docs.Resolve("language.md"));
        Assert.Equal("/", docs.Resolve("kscript://docs"));
        Assert.Equal("/reference/io", docs.Resolve("KScript.IO"));
        Assert.Equal("/reference/io", docs.Resolve("io"));
        Assert.Equal("/reference/methods/string", docs.Resolve("String methods"));
        Assert.Equal("/reference/methods/list", docs.Resolve("list"));
        Assert.Equal("/cli", docs.Resolve("cli"));
        Assert.Null(docs.Resolve("playground"));
        Assert.Null(docs.Resolve("404"));
        Assert.Null(docs.Resolve(null));
        Assert.Null(docs.Resolve("nothing-like-this"));

        Assert.Null(docs.Page(null, "kscript"));
        Assert.Null(docs.Page("/guide/nope", "kscript"));
        Assert.Null(docs.Page("/reference/methods/nope", "kscript"));
        Assert.Null(docs.Page("/reference/nope", "kscript"));
        Assert.Null(new DocsMarkdown(Site.Value, _ => null).Page("/guide/language", "kscript"));
    }

    [Fact]
    public void Guides_show_examples_in_the_format_asked_for()
    {
        DocsMarkdown docs = Server().Docs;
        string kscript = docs.Page("/guide/getting-started", "kscript");
        string xml = docs.Page("/guide/getting-started", "xml");
        Assert.Contains("```kscript", kscript);
        Assert.Contains("```xml", xml);
        Assert.NotEqual(kscript, xml);

        string guide = DocsMarkdown.Guide("```\n<echo>a</echo>\n```\n```try\n<echo>b</echo>\n```\n```sh\nkscript x\n```\n```xml\n<echo>unclosed\n```\n```kscript\necho \"c\"\n```\n", "xml", "kscript");
        Assert.Contains("```kscript\necho \"a\"\n```", guide);
        Assert.Contains("```kscript\necho \"b\"\n```", guide);
        Assert.Contains("```sh\nkscript x\n```", guide);
        Assert.Contains("```xml\n<echo>unclosed\n```", guide);
        Assert.Contains("```kscript\necho \"c\"\n```", guide);
    }

    [Fact]
    public void Reference_pages_cover_packages_types_variables_and_the_command_line()
    {
        DocsMarkdown docs = Server().Docs;
        string home = docs.Page("/", "kscript");
        Assert.Contains("kscript://docs/guide/language", home);

        string reference = docs.Page("/reference", "xml");
        Assert.Contains("[KScript.IO](kscript://docs/reference/io)", reference);
        Assert.Contains("<import package=\"KScript.IO\" />", reference);
        Assert.Contains("`import KScript.IO`", docs.Page("/reference", "kscript"));

        string core = docs.Page("/reference/core", "kscript");
        Assert.Contains("nothing to import", core);
        Assert.Contains("## Elements", core);
        Assert.Contains("## Commands", core);
        Assert.Contains("## Methods", core);
        Assert.Contains("## Variables", core);

        string web = docs.Page("/reference/web", "kscript");
        Assert.Contains("`import KScript.Web`", web);
        Assert.Contains("It brings KScript.Markdown with it.", web);

        Assert.Contains("# Built-in variables", docs.Page("/reference/variables", "kscript"));
        Assert.Contains("`$args`", docs.Page("/reference/variables", "kscript"));

        string withFields = docs.Catalog.Methods.Where(m => !m.Planned).Select(m => m.ReceiverType).First(t => docs.Catalog.FieldsOf(t).Count > 0);
        Assert.Contains("Fields: ", docs.Page("/reference/methods/" + withFields, "kscript"));
        Assert.DoesNotContain("Fields: ", docs.Page("/reference/methods/string", "kscript"));

        string cli = docs.Page("/cli", "kscript");
        Assert.Contains("## kscript mcp", cli);
        Assert.Contains("## Running a script", cli);
        Assert.Contains("## --version and --help", cli);
    }

    [Fact]
    public void Every_element_and_callable_renders()
    {
        DocsMarkdown docs = Server().Docs;
        foreach (ElementInfo e in docs.Catalog.Elements) Assert.StartsWith("# <", docs.Element(e, "kscript"));
        foreach (CallableInfo c in docs.Catalog.Commands.Concat(docs.Catalog.Methods)) Assert.StartsWith("# ", docs.Callable(c, "xml"));

        // Every kind of content, context and rule an element can have.
        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            var e = new ElementInfo("thing", "A thing.", kind, new AttributeInfo("mode", AttributeKind.Enum, "How.", true, "a", "b"), new AttributeInfo("x", AttributeKind.Expression, "An x."))
            {
                Context = kind == ContentKind.Empty ? ElementContext.Page : ElementContext.Script,
                MustBeInside = new[] { "box" },
                MustFollow = new[] { "if" },
                IsLoop = true,
                AllowsExtraAttributes = true,
            };
            string md = docs.Element(e, "kscript");
            Assert.Contains("planned", md);
            Assert.Contains("| `mode` (required) | `a` \\| `b` | How. |", md);
            Assert.Contains("Goes inside <box>. Follows <if>. A loop", md);
        }
        Assert.DoesNotContain("only.", docs.Element(new ElementInfo("bare", "Bare.", ContentKind.Empty), "kscript"));

        // Agent notes from the package show up in lookups too.
        Assert.Contains("\n- Avoid: Loops.\n", docs.Element(new ElementInfo("noisy", "Noisy.", ContentKind.Empty) { Agent = new AgentNotes(avoid: "Loops.") }, "kscript"));
        Assert.Contains("\n- Pitfall: Returns a new list", docs.Callable(docs.Catalog.GetMethods("push", "list").First(), "kscript"));
        string noted = docs.Callable(new CallableInfo(CallableKind.Command, "n", "N.", Array.Empty<Signature>()) { Agent = new AgentNotes(prefer: "P.") }, "kscript");
        Assert.Contains("- Prefer: P.", noted);
        Assert.DoesNotContain("- Avoid", docs.Callable(new CallableInfo(CallableKind.Command, "m", "M.", Array.Empty<Signature>()) { Agent = new AgentNotes(llm: "only a line") }, "kscript"));

        var planned = new CallableInfo(CallableKind.Command, "someday", "Not yet.", Array.Empty<Signature>()) { ReturnType = null };
        string plannedMd = docs.Callable(planned, "kscript");
        Assert.Contains("# @someday()\n", plannedMd);
        Assert.Contains("planned", plannedMd);

        var rich = new CallableInfo(CallableKind.Method, "go", "Goes.", new[] { Signature.Parse("where: up|down, fast?: bool, n?: number, rest..."), Signature.Parse("") }) { ReceiverType = "list", ReturnType = KType.Null };
        string richMd = docs.Callable(rich, "kscript");
        Assert.Contains("# list->go(where: up | down, fast?: bool, n?: number, rest...)\n# list->go()\n", richMd);
        Assert.Contains("- `where` (required): one of up | down", richMd);
        Assert.Contains("- `fast` (optional): a flag: `fast` or `not fast`", richMd);
        Assert.Contains("- `n` (optional): number", richMd);
        Assert.Contains("- `rest` (any number): any value", richMd);
        Assert.Contains("Arguments can be named: `->go(…, fast: …, n: …)`", richMd);
        Assert.Contains("**", "**" + docs.Callable(new CallableInfo(CallableKind.Command, "d", "D.", new[] { new Signature(new ParameterInfo("p", description: "The p.")) }), "kscript"));
        Assert.Contains("- `p` (required): any value. The p.", docs.Callable(new CallableInfo(CallableKind.Command, "d", "D.", new[] { new Signature(new ParameterInfo("p", description: "The p.")) }), "kscript"));
    }

    private sealed class RetiringPackage : KScriptPackage
    {
        public override string Name => "KScript.Retiring";

        public override string Description => "Deprecated things, for the docs server.";

        protected override void Register(PackageBuilder p)
        {
            p.Command("old_sum", "a, b", "Adds two numbers.", KType.Number, call => null);
            p.Command("fetch_text", "url, retries?", "Fetches text.", KType.String, call => null);
            p.Method(KType.String, "shout", "", "Upper-cases text.", KType.String, (target, call) => null);
            p.Element(new ElementInfo("beep", "Beeps.", ContentKind.Empty, new AttributeInfo("tone", AttributeKind.Template, "The sound."), new AttributeInfo("sound", AttributeKind.Template, "The sound.")), run => null);
            p.Element(new ElementInfo("old-beep", "Beeps.", ContentKind.Empty), run => null);
            p.Deprecate("@old_sum", "Sums moved to KScript.Math.", "@sum");
            p.Deprecate("@fetch_text(retries)", "Requests are retried for you");
            p.Deprecate("string->shout", "", "->upper");
            p.Deprecate("<beep tone>", "Renamed", "sound");
            p.Deprecate("<old-beep>", "");
        }
    }

    [Fact]
    public void Deprecated_names_say_so_with_why_and_what_to_use()
    {
        var registry = new PackageRegistry();
        foreach (var (name, type, description) in PackageRegistry.BuiltIn.Skip(1)) registry.Register(name, type, description);
        registry.Register(new RetiringPackage());
        var server = new McpServer(DocsSite.Build(new LanguageCatalog(registry), readGuide: _ => "# Guide\n"), "kscript", dir);

        string sum = Call(server, "lookup", new { name = "@old_sum" }).Text;
        Assert.StartsWith("# @old_sum(a, b) → number (deprecated)\n", sum);
        Assert.Contains("\n**Deprecated.** Sums moved to KScript.Math. Use `@sum` instead.\n", sum);

        string fetch = Call(server, "lookup", new { name = "@fetch_text" }).Text;
        Assert.DoesNotContain("(deprecated)\n", fetch);
        Assert.Contains("- `retries` (optional, deprecated): any value. **Deprecated.** Requests are retried for you.", fetch);

        Assert.Contains("**Deprecated.** Use `->upper` instead.", Call(server, "lookup", new { name = "string->shout" }).Text);

        string beep = Call(server, "lookup", new { name = "<beep>" }).Text;
        Assert.Contains("| `tone` (deprecated) | text with `{ }` | **Deprecated.** Renamed. Use `sound` instead. The sound. |", beep);
        Assert.Contains("| `sound` | text with `{ }` | The sound. |", beep);
        string oldBeep = Call(server, "lookup", new { name = "<old-beep>" }).Text;
        Assert.StartsWith("# <old-beep> (deprecated)\n", oldBeep);
        Assert.Contains("\n**Deprecated.**\n", oldBeep);

        Assert.Contains("Deprecated. Adds two numbers.", Call(server, "search_docs", new { query = "old_sum" }).Text);
    }

    #endregion

    #region Tools: search, read, lookup

    [Fact]
    public void Search_finds_reference_entries_and_guide_sections()
    {
        McpServer server = Server();
        var (text, error) = Call(server, "search_docs", new { query = "read a json file" });
        Assert.False(error);
        Assert.StartsWith("- @read_json() (command, KScript.IO): kscript://docs/reference/io#cmd-read-json [lookup: @read_json]", text);

        // Guide sections are searched by their text, not only their headings.
        var (sections, _) = Call(server, "search_docs", new { query = "early returns", limit = 50 });
        Assert.Contains("(heading, ", sections);
        Assert.Contains("(page, ", Call(server, "search_docs", new { query = "language" }).Text);

        Assert.Equal(3, Call(server, "search_docs", new { query = "list", limit = 3 }).Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(l => l.StartsWith("- ")));
        Assert.Contains("Nothing in the docs matches", Call(server, "search_docs", new { query = "zzqqxxyy" }).Text);
        Assert.True(Call(server, "search_docs", new { query = " " }).IsError);
        Assert.True(Call(server, "search_docs", new { query = 5 }).IsError);
    }

    [Fact]
    public void Read_doc_lists_pages_and_reads_one()
    {
        McpServer server = Server();
        var (list, _) = Call(server, "read_doc");
        Assert.Contains("- The KScript language: kscript://docs/guide/language", list);
        Assert.DoesNotContain("playground", list);

        Assert.StartsWith("# KScript.IO", Call(server, "read_doc", new { page = "KScript.IO" }).Text);
        Assert.Contains("<import package=\"KScript.IO\" />", Call(server, "read_doc", new { page = "io", format = "xml" }).Text);
        Assert.Contains("```kscript", Call(server, "read_doc", new { page = "kscript://docs/guide/getting-started" }).Text);
        Assert.Contains("```xml", Server("xml").Tools.ReadDoc(new JsonObject { ["page"] = "getting-started" }).Text);

        var (missing, error) = Call(server, "read_doc", new { page = "nope" });
        Assert.True(error);
        Assert.Contains("no docs page 'nope'", missing);
    }

    [Fact]
    public void Lookup_explains_any_name()
    {
        McpServer server = Server();
        Assert.StartsWith("# <for>", Call(server, "lookup", new { name = "<for each>" }).Text);
        Assert.StartsWith("# <for>", Call(server, "lookup", new { name = "</for>" }).Text);
        Assert.StartsWith("# @read_text(", Call(server, "lookup", new { name = "@read_text()" }).Text);
        Assert.StartsWith("# string->replace(", Call(server, "lookup", new { name = "string->replace" }).Text);
        Assert.StartsWith("# string->replace(", Call(server, "lookup", new { name = "->replace", type = "string" }).Text);
        Assert.StartsWith("# $args", Call(server, "lookup", new { name = "$args.count" }).Text);

        // A method on a type that doesn't have it still shows the method on the types that do.
        Assert.Contains("->replace(", Call(server, "lookup", new { name = "number->replace" }).Text);

        // A bare name finds everything called that; a package name finds the package.
        var (map, _) = Call(server, "lookup", new { name = "map" });
        Assert.Contains("list->map(", map);
        Assert.StartsWith("# KScript.Math", Call(server, "lookup", new { name = "KScript.Math" }).Text);
        Assert.Contains("\n---\n", Call(server, "lookup", new { name = "length" }).Text + "\n---\n");

        // Retired v1 names say what to write instead.
        LanguageCatalog catalog = server.Docs.Catalog;
        string retiredElement = catalog.RetiredElementNames.First();
        Assert.Contains("(retired)", Call(server, "lookup", new { name = "<" + retiredElement + ">" }).Text);
        string retiredCommand = Retired.Commands.Keys.First(k => catalog.GetCommand(k) == null);
        Assert.Contains("removed in KScript 2", Call(server, "lookup", new { name = "@" + retiredCommand }).Text);

        var (none, error) = Call(server, "lookup", new { name = "@read_jsn" });
        Assert.True(error);
        Assert.Contains("Close matches:", none);
        Assert.DoesNotContain("Close matches", Call(server, "lookup", new { name = "zzqqxxyy" }).Text);
        Assert.True(Call(server, "lookup").IsError);
    }

    #endregion

    #region Tools: make, check, format, convert, run

    [Fact]
    public void List_make_commands_shows_the_projects_templates()
    {
        McpServer server = Server();
        var (outside, _) = Call(server, "list_make_commands");
        Assert.Contains("outside a project", outside);
        Assert.Contains("- kscript make component <Name>:", outside);
        Assert.Contains("- web (default):", outside);
        Assert.Contains("  --port (default 8080):", outside);

        Write("site/kscript.json", "{ }");
        Write("site/_templates/component.html", "--- make\nto: _components/{{ kebab }}.html\n---\n<div></div>\n");
        Write("site/_templates/card.html", "--- make\ndescription: A card\nto: _cards/{{ kebab }}.html\n---\n<div></div>\n");
        var (inside, _) = Call(server, "list_make_commands", new { folder = "site" });
        Assert.Contains("for the project in", inside);
        Assert.Contains("[_templates/component.html, replaces the built-in one]", inside);
        Assert.Contains("- kscript make card <Name>: A card [_templates/card.html]", inside);

        Assert.True(Call(server, "list_make_commands", new { folder = "nope" }).IsError);
    }

    [Fact]
    public void Check_code_reports_problems_with_their_positions()
    {
        McpServer server = Server();
        var (kscript, error) = Call(server, "check_code", new { code = "x = @nope(1)\necho \"$x\"" });
        Assert.True(error);
        Assert.Contains("1 error", kscript);
        Assert.Contains("1:5: error KS5001: Unknown command @nope.", kscript);

        // An XML snippet gets a <kscript> root, without moving its columns.
        var (xml, _) = Call(server, "check_code", new { code = "<let name=\"x\" value=\"@nope(1)\" />\n<echo>$x</echo>" });
        Assert.Contains("1:22: error KS5001", xml);
        Assert.Contains("1:31: error KS5001", Call(server, "check_code", new { code = "<kscript><let name=\"x\" value=\"@nope(1)\" /><echo>$x</echo></kscript>", format = "xml" }).Text);

        Assert.Equal("No problems found.\n", Call(server, "check_code", new { code = "echo \"hi\"" }).Text);
        var (warning, warningIsError) = Call(server, "check_code", new { code = "x = 1" });
        Assert.False(warningIsError);
        Assert.Equal("0 errors, 1 warning:\n1:1: warning KS6003: The variable $x is never used.\n", warning);

        // A file is checked with its project's lint rules: here, unused variables are info and else is a hint.
        Write("proj/kscript.json", "{ \"lint\": { \"rules\": { \"KS6003\": \"info\", \"KS6001\": \"hint\" } } }");
        Write("proj/a.kscript", "x = 1\nfn f(a) {\n  if $a {\n    return 1\n  } else {\n    return 2\n  }\n}\necho @f(true)\n");
        var (file, fileError) = Call(server, "check_code", new { path = "proj/a.kscript" });
        Assert.False(fileError);
        Assert.Contains("info KS6003", file);
        Assert.Contains("hint KS6001", file);
        Assert.Contains("0 errors, 0 warnings:", file);

        Assert.True(Call(server, "check_code", new { path = "nope.kscript" }).IsError);
        Assert.True(Call(server, "check_code").IsError);

        // A kscript.json that can't be read means the default rules.
        Write("broken/kscript.json", "{ nope");
        Write("broken/b.kscript", "echo \"hi\"");
        Assert.Equal("No problems found.\n", Call(server, "check_code", new { path = "broken/b.kscript" }).Text);
    }

    [Fact]
    public void Format_code_formats_like_kscript_fmt()
    {
        McpServer server = Server();
        Assert.Equal("<let name=\"n\" value=\"3\" />\n<for each=\"i\" in=\"1..$n\">\n  <echo>$i</echo>\n</for>\n",
            Call(server, "format_code", new { code = "<let name=\"n\" value=\"3\"/>\n    <for each=\"i\" in=\"1..$n\"><echo>$i</echo></for>" }).Text);
        Assert.Equal("<kscript>\n  <echo>hi</echo>\n</kscript>\n", Call(server, "format_code", new { code = "<kscript><echo>hi</echo></kscript>" }).Text);

        Write("p/kscript.json", "{ \"format\": { \"indent\": 4 } }");
        string script = Write("p/a.ks.xml", "<kscript><echo>hi</echo></kscript>");
        Assert.Contains("\n    <echo>hi</echo>", Call(server, "format_code", new { path = "p/a.ks.xml" }).Text);
        Assert.Equal("<kscript><echo>hi</echo></kscript>", File.ReadAllText(script));

        var (kscript, error) = Call(server, "format_code", new { code = "echo \"hi\"" });
        Assert.True(error);
        Assert.Contains("not reformatted yet. The code was left as it is.", kscript);
        Assert.True(Call(server, "format_code", new { code = "<echo>unclosed" }).IsError);
        Assert.True(Call(server, "format_code").IsError);
    }

    [Fact]
    public void Convert_code_switches_formats()
    {
        McpServer server = Server();
        Assert.Equal("n = 3\n", Call(server, "convert_code", new { code = "<let name=\"n\" value=\"3\" />" }).Text);
        Assert.Equal("<let name=\"n\" value=\"3\" />\n", Call(server, "convert_code", new { code = "n = 3" }).Text);
        Assert.Equal("<let name=\"n\" value=\"3\" />\n", Call(server, "convert_code", new { code = "n = 3", to = "xml", format = "kscript" }).Text);
        Assert.Contains("already .kscript", Call(server, "convert_code", new { code = "n = 3", to = "kscript" }).Text);
        Assert.True(Call(server, "convert_code", new { code = "<echo>unclosed", to = "kscript" }).IsError);
        Assert.True(Call(server, "convert_code").IsError);
    }

    [Fact]
    public void Run_code_runs_in_the_sandbox()
    {
        McpServer server = Server();
        var (ran, error) = Call(server, "run_code", new { code = "n = 3\necho \"n is $n\"" });
        Assert.False(error);
        Assert.Contains("Exit code 0", ran);
        Assert.Contains("Output:\nn is 3\n", ran);

        Assert.Contains("Joan", Call(server, "run_code", new { code = "<import package=\"KScript.Terminal\" />\n<input to=\"name\">Name? </input><echo>$name</echo>", input = "Joan\n" }).Text);
        Assert.Contains("It printed nothing.", Call(server, "run_code", new { code = "n = 1" }).Text);
        Assert.Contains("cut short", Call(server, "run_code", new { code = "for i in 1..20000 {\n  echo \"0123456789\"\n}" }).Text);

        var (failed, failedError) = Call(server, "run_code", new { code = "throw 'boom'" });
        Assert.True(failedError);
        Assert.Contains("Error:\n", failed);
        Assert.True(Call(server, "run_code").IsError);
    }

    #endregion

    #region Transports and the command

    [Fact]
    public async Task Stdio_answers_one_line_per_request()
    {
        McpServer server = Server();
        var input = new StringReader("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}\n\n{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n");
        var output = new StringWriter();
        await server.RunStdioAsync(input, output);
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", lines[0]);
        Assert.Contains("search_docs", lines[1]);
    }

    private static async Task<(int Status, string Body, HttpResponse Response)> Http(McpServer server, string method, string path, string body = null, string origin = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        if (origin != null) http.Request.Headers.Origin = origin;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        var responseBody = new MemoryStream();
        http.Response.Body = responseBody;
        await server.HandleHttpAsync(http);
        return (http.Response.StatusCode, Encoding.UTF8.GetString(responseBody.ToArray()), http.Response);
    }

    [Fact]
    public async Task Http_answers_posts_to_the_mcp_endpoint_from_local_clients()
    {
        McpServer server = Server();
        var ok = await Http(server, "POST", "/mcp", "{\"jsonrpc\":\"2.0\",\n\"id\":1,\"method\":\"ping\"}", "http://localhost:3000");
        Assert.Equal(200, ok.Status);
        Assert.Equal("application/json", ok.Response.ContentType);
        Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}", ok.Body);

        Assert.Equal(202, (await Http(server, "POST", "/mcp", "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")).Status);
        Assert.Equal(404, (await Http(server, "POST", "/other", "{}")).Status);
        Assert.Equal(403, (await Http(server, "POST", "/mcp", "{}", "https://example.com")).Status);
        Assert.Equal(403, (await Http(server, "POST", "/mcp", "{}", "not a url")).Status);
        var get = await Http(server, "GET", "/mcp", origin: "http://127.0.0.1:9");
        Assert.Equal(405, get.Status);
        Assert.Equal("POST", get.Response.Headers.Allow.ToString());
        Assert.Equal(200, (await Http(server, "POST", "/mcp", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", "http://[::1]:9")).Status);
    }

    [Fact]
    public async Task Http_server_listens_on_localhost_until_stopped()
    {
        McpServer server = Server();
        int port = FreePort();
        using var stop = new CancellationTokenSource();
        var started = new TaskCompletionSource<string>();
        Task running = server.RunHttpAsync(port, stop.Token, url => started.SetResult(url));
        string endpoint = await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal($"http://localhost:{port}/mcp", endpoint);

        using var client = new HttpClient();
        var response = await client.PostAsync(endpoint, new StringContent("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"ping\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":7", await response.Content.ReadAsStringAsync());

        stop.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(30));

        // Stopped before it started: a normal stop too.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await server.RunHttpAsync(FreePort(), cancelled.Token);
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static (int Code, string Out, string Log) Command(string input, params string[] args)
    {
        var output = new MemoryStream();
        var log = new StringWriter();
        int code = McpCommand.Run(args, new MemoryStream(Encoding.UTF8.GetBytes(input)), output, log);
        return (code, Encoding.UTF8.GetString(output.ToArray()), log.ToString());
    }

    [Fact]
    public void The_command_serves_stdio_and_checks_its_options()
    {
        var (code, output, log) = Command("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}\n");
        Assert.Equal(0, code);
        Assert.Equal(string.Empty, log);
        Assert.StartsWith("{\"jsonrpc\":\"2.0\",\"id\":1,", output);
        Assert.EndsWith("}\n", output);
        Assert.NotEqual(0xEF, Encoding.UTF8.GetBytes(output)[0]);

        // The project's make.format decides how examples are shown.
        Write("xmlproj/kscript.json", "{ \"make\": { \"format\": \"xml\" } }");
        Assert.Contains("as XML unless", Command("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n", "--dir", Path.Combine(dir, "xmlproj")).Out);
        Assert.Contains("as XML unless", Command("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n", "--format", "xml").Out);

        Assert.Contains("Usage: kscript mcp", Command("", "--help").Log);
        Assert.Equal(1, Command("", "--port", "nope").Code);
        Assert.Equal(1, Command("", "--port").Code);
        Assert.Equal(1, Command("", "--format", "yaml").Code);
        Assert.Equal(1, Command("", "--format").Code);
        Assert.Contains("--dir needs a folder", Command("", "--dir").Log);
        Assert.Contains("There is no folder", Command("", "--dir", Path.Combine(dir, "nope")).Log);
        Assert.Contains("Unknown option '--nope'", Command("", "--nope").Log);
    }

    [Fact]
    public void The_command_stops_when_cancelled()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var log = new StringWriter();
        Assert.Equal(0, McpCommand.Run(Array.Empty<string>(), new BlockingStream(), new MemoryStream(), log, stop.Token));

        // --http on a port that's taken says so; on a free one it serves until stopped.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int taken = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Equal(1, McpCommand.Run(new[] { "--http", "--port", taken.ToString() }, new MemoryStream(), new MemoryStream(), log, stop.Token));
        Assert.Contains($"Port {taken} is in use", log.ToString());
        listener.Stop();
        Assert.Equal(0, McpCommand.Run(new[] { "--http", "-p", FreePort().ToString() }, new MemoryStream(), new MemoryStream(), log, stop.Token));
    }

    /// <summary>Input that never arrives, like a client that is still connected.</summary>
    private sealed class BlockingStream : MemoryStream
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => 0, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new ValueTask<int>(ReadAsync(Array.Empty<byte>(), 0, 0, cancellationToken));
    }

    [Fact]
    public void Kscript_mcp_runs_from_the_command_line()
    {
        var output = new MemoryStream();
        var cli = new CommandLine
        {
            StandardInput = () => new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"ping\"}\n")),
            StandardOutput = () => output,
            Error = new StringWriter(),
        };
        Assert.Equal(0, cli.Run(new[] { "mcp" }));
        Assert.Equal("{\"jsonrpc\":\"2.0\",\"id\":3,\"result\":{}}\n", Encoding.UTF8.GetString(output.ToArray()));
    }

    #endregion
}
