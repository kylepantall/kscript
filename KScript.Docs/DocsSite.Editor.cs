using System.Text;
using System.Text.Json.Nodes;

namespace KScript.Docs
{
    /// <summary>
    /// The editor page: every command and setting of the VS Code extension, read from its package.json, so a new
    /// setting shows up here with the description VS Code shows for it. Commands have no description in
    /// package.json, so each one's is written in <see cref="CommandDescriptions"/>, and a test fails when one is missing.
    /// </summary>
    public sealed partial class DocsSite
    {
        internal const string EditorPath = "/reference/editor";

        internal static JsonObject ExtensionManifest => (JsonObject)JsonNode.Parse(ReadResource("KScript.Docs.Schemas.package.json"));

        /// <summary>What each command in the Command Palette does, by its id.</summary>
        internal static readonly Dictionary<string, string> CommandDescriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kscript.run"] = "Saves the open script and runs it in the terminal. Runtime errors link to the file and line. A new file that hasn't been saved yet runs too.",
            ["kscript.runTests"] = "Runs `kscript test` for the project in a task.",
            ["kscript.runTestsInFile"] = "Runs the tests in the open test file (`*.test.kscript` or `*.test.ks.xml`). Each test is also in the Testing view, where it can run on its own.",
            ["kscript.serve"] = "Runs `kscript serve` in the project's folder.",
            ["kscript.checkFile"] = "Saves the open script, runs `kscript check` on it and lists what it finds in Problems. It works on a file outside a project too.",
            ["kscript.check"] = "Runs `kscript check` on the whole project and lists what it finds in Problems.",
            ["kscript.formatProject"] = "Runs `kscript fmt` on the whole project.",
            ["kscript.newScript"] = "Opens a short script that runs as it is, as a place to start. Also in **File > New File…**.",
            ["kscript.newProject"] = "Asks for a name and a folder, runs `kscript make project` and offers to open it.",
            ["kscript.openDocs"] = "Runs `kscript docs`, which opens these docs in your browser. The **Open docs** link in a hover opens them on what the hover is about.",
            ["kscript.restartServer"] = "Restarts the language server, such as after changing a setting. It restarts by itself when `kscript` is updated.",
            ["kscript.showOutput"] = "Shows the language server's log. Clicking **KScript** in the status bar does the same.",
            ["kscript.showSetup"] = "Opens the install guide. It works in an empty window, before KScript is installed.",
        };

        /// <summary>Commands the Command Palette lists: the rest (when: false) are run by the editor itself, such as the Enter key's.</summary>
        internal static IEnumerable<JsonObject> PaletteCommands(JsonObject manifest)
        {
            var hidden = (manifest["contributes"]?["menus"]?["commandPalette"]?.AsArray() ?? new JsonArray())
                .OfType<JsonObject>().Where(m => (string)m["when"] == "false").Select(m => (string)m["command"]).ToHashSet(StringComparer.Ordinal);
            return manifest["contributes"]["commands"].AsArray().OfType<JsonObject>().Where(c => !hidden.Contains((string)c["command"]));
        }

        private void AddEditorPage()
        {
            JsonObject manifest = ExtensionManifest;
            JsonObject contributes = (JsonObject)manifest["contributes"];
            const string title = "VS Code commands and settings";
            var sb = new StringBuilder();
            var md = new StringBuilder($"# {title}\n\nEvery command and setting of the KScript extension for VS Code.\n");
            sb.Append($"<h1>{title}</h1><p class=\"lead\">Every command and setting of the KScript extension for VS Code. Install it with <code>kscript ide install</code>. Everything the editor does as you type, such as completion and quick fixes, comes from the language server in <code>kscript</code>, so other editors get it too: see <a href=\"/guide/install#editor-support\">Editor support</a>.</p>");
            var page = new DocPage { Path = EditorPath, Title = title, NavTitle = "VS Code", Section = "Reference", Description = "Every command and setting of the KScript extension for VS Code, with their keys and defaults." };

            var keys = (contributes["keybindings"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
                .GroupBy(k => (string)k["command"]).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(k => Key((string)k["key"]))), StringComparer.Ordinal);

            sb.Append("<h2 id=\"commands\">Commands</h2><p>Run these from the Command Palette (<code>Ctrl+Shift+P</code>, or <code>Cmd+Shift+P</code> on macOS). In a workspace with several folders, the project commands use the folder of the file you have open, and save its changed files first.</p>");
            page.Toc.Add((2, "commands", "Commands"));
            md.Append("\n## Commands\n\nRun these from the Command Palette.\n\n| Command | Key | What it does |\n| --- | --- | --- |\n");
            sb.Append("<div class=\"table\"><table><thead><tr><th>Command</th><th>Key</th><th>What it does</th></tr></thead><tbody>");
            foreach (JsonObject c in PaletteCommands(manifest))
            {
                string id = (string)c["command"];
                string name = $"{(string)c["category"] ?? "KScript"}: {(string)c["title"]}";
                string key = keys.GetValueOrDefault(id, string.Empty);
                string does = CommandDescriptions.GetValueOrDefault(id, string.Empty);
                string anchor = "command-" + Html.Slug(id.Replace('.', '-'));
                sb.Append($"<tr id=\"{anchor}\"><td><strong>{Html.H(name)}</strong></td><td>{(key.Length > 0 ? $"<kbd>{Html.H(key)}</kbd>" : string.Empty)}</td><td>{Html.Inline(does)}</td></tr>");
                md.Append($"| **{name}** | {key} | {does} |\n");
                Search.Add(new SearchEntry { Title = name, Url = EditorPath + "#" + anchor, Section = "VS Code", Kind = "editor command", Text = Shorten(Html.Text(Html.Inline(does))) });
            }
            sb.Append("</tbody></table></div>");

            sb.Append("<h2 id=\"settings\">Settings</h2><p>Change these in VS Code's settings (<code>Ctrl+,</code>), or in <code>.vscode/settings.json</code> for one project. The lint and format rules a project uses are in <a href=\"/reference/settings\">kscript.json</a> instead, so everyone on the project gets the same ones.</p>");
            page.Toc.Add((2, "settings", "Settings"));
            md.Append("\n## Settings\n\n| Setting | Default | What it does |\n| --- | --- | --- |\n");
            sb.Append("<div class=\"table\"><table><thead><tr><th>Setting</th><th>Default</th><th>What it does</th></tr></thead><tbody>");
            JsonObject properties = (JsonObject)contributes["configuration"]["properties"];
            foreach ((string name, JsonNode node) in properties)
            {
                string description = (string)node["markdownDescription"] ?? (string)node["description"] ?? string.Empty;
                if (node["enum"] is JsonArray choices)
                {
                    var notes = node["enumDescriptions"]?.AsArray();
                    description += " One of " + string.Join(", ", choices.Select((v, i) => notes != null && i < notes.Count ? $"`{v}` ({TrimStop((string)notes[i])})" : $"`{v}`")) + ".";
                }
                string fallback = node["default"] switch
                {
                    null => "none",
                    JsonValue v when v.TryGetValue(out string s) => s.Length == 0 ? "empty" : s,
                    JsonNode other => other.ToJsonString(),
                };
                string anchor = "setting-" + Html.Slug(name.Replace('.', '-'));
                sb.Append($"<tr id=\"{anchor}\"><td><code>{Html.H(name)}</code></td><td><code>{Html.H(fallback)}</code></td><td>{Html.Inline(description)}</td></tr>");
                md.Append($"| `{name}` | `{fallback}` | {description} |\n");
                Search.Add(new SearchEntry { Title = name, CodeTitle = name, Url = EditorPath + "#" + anchor, Section = "VS Code", Kind = "editor setting", Text = Shorten(Html.Text(Html.Inline(description))) });
            }
            sb.Append("</tbody></table></div>");

            page.Body = sb.ToString();
            page.Markdown = md.ToString();
            Add(page);

            static string Key(string key) => string.Join("+", key.Split('+').Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p.Substring(1)));
            static string TrimStop(string s) => s.TrimEnd('.').Length > 0 ? char.ToLowerInvariant(s[0]) + s.TrimEnd('.').Substring(1) : s;
        }

        /// <summary>The A to Z entries for the editor page.</summary>
        internal IEnumerable<IndexEntry> EditorIndexEntries()
        {
            JsonObject manifest = ExtensionManifest;
            foreach ((string name, JsonNode node) in (JsonObject)manifest["contributes"]["configuration"]["properties"])
                yield return new IndexEntry(name, "VS Code setting", EditorPath + "#setting-" + Html.Slug(name.Replace('.', '-')), (string)node["markdownDescription"] ?? (string)node["description"]);
        }
    }
}
