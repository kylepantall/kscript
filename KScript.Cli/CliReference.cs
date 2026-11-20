using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KScript
{
    /// <summary>One option of a kscript command, such as <c>--port 8080</c>.</summary>
    public sealed record CliOption(string Name, string Description);

    /// <summary>One kscript command: how to call it, what it does, its options and an example.</summary>
    public sealed record CliCommand(string Name, string Usage, string Summary, IReadOnlyList<CliOption> Options, string Example = null, string Guide = null);

    /// <summary>
    /// Every kscript command. <c>kscript --help</c> prints it and the docs site (<c>kscript docs</c>) builds its command
    /// line page from it, so the two never disagree.
    /// </summary>
    public static class CliReference
    {
        private static CliOption O(string name, string description) => new CliOption(name, description);

        public static readonly IReadOnlyList<CliCommand> Commands = new[]
        {
            new CliCommand("run", "kscript [--env name] <script> [script arguments]",
                "Run a script ($args holds the arguments; --env picks staging.yaml, .env.staging...).",
                new[]
                {
                    O("--env name", "The environment for $config: reads <name>.yaml, .env.<name> and sets KSCRIPT_ENV."),
                },
                "kscript hello.ks.xml Joan\nkscript --env staging deploy.kscript", "language"),
            new CliCommand("check", "kscript check [file-or-folder]... [--warnaserror] [--format text|json]",
                "Check scripts and pages without running them (no path: the current project).",
                new[]
                {
                    O("--warnaserror", "Fail on warnings too, for CI."),
                    O("--format json", "Print the problems as JSON, with each one's range and its fixes as exact text edits (for coding agents and tools)."),
                    O("--print-config [path]", "Show the lint and format settings that apply, and which standard set each."),
                },
                "kscript check\nkscript check scripts/ --warnaserror", "standards"),
            new CliCommand("agents", "kscript agents [folder] [--check] [--print]",
                "Write AGENTS.md and .kscript/agents: compact docs for coding agents, made from the project's code and packages.",
                new[]
                {
                    O("--check", "Write nothing; fail if the agent docs are out of date (for CI)."),
                    O("--print", "Print AGENTS.md's generated section instead of writing files."),
                },
                "kscript agents\nkscript agents --check", "agents"),
            new CliCommand("watch", "kscript watch [folder] [--once] [--check]",
                "Rebuild everything the project makes from its code (the agent docs, the OpenAPI file http.openapi.file names, and more as packages add to it) each time a file changes, until Ctrl+C. kscript serve and the editor (on save) do the same.",
                new[]
                {
                    O("--once", "Build once and stop."),
                    O("--check", "Write nothing; fail if anything is out of date (for CI)."),
                },
                "kscript watch\nkscript watch --check", "agents"),
            new CliCommand("fmt", "kscript fmt [file-or-folder]... [--check]",
                "Tidy scripts and pages.",
                new[] { O("--check", "Only report files that would change, and fail if any would (for CI).") },
                "kscript fmt\nkscript fmt --check", "formatting"),
            new CliCommand("test", "kscript test [file-or-folder]... [--filter text] [--timeout s] [--junit file]",
                "Run *.test.kscript and *.test.ks.xml tests.",
                new[]
                {
                    O("--filter text", "Only run tests whose name contains the text."),
                    O("--timeout s", "Stop a test that runs longer than this many seconds (default 10)."),
                    O("--junit file", "Also write a JUnit XML report, for CI."),
                },
                "kscript test\nkscript test tests/cart.test.ks.xml --filter total", "testing"),
            new CliCommand("serve", "kscript serve [site folder] [--port 8080] [--host localhost] [--env name] [--with-docs [--docs-port 4180]]",
                "Serve pages, and routes.ks.xml / api.ks.xml routes.",
                new[]
                {
                    O("--port, -p N", "The port to listen on (default 8080, or serve.port in kscript.json)."),
                    O("--host H", "The address to listen on: localhost (default), 0.0.0.0 or * for every address."),
                    O("--env name", "The environment for $config."),
                    O("--with-docs", "Also serve the docs on localhost, with a page about this project: its conventions, routes, and the functions, types and constants its scripts declare. serve.docs in kscript.json turns it on by default; --no-docs turns it off. Never for compiled programs or the production environment."),
                    O("--docs-port N", "The port for the docs (default 4180, or a free one if that is in use)."),
                },
                "kscript serve\nkscript serve site --port 3000\nkscript serve --with-docs", "web"),
            new CliCommand("routes", "kscript routes [site folder]",
                "List the routes of a site.", Array.Empty<CliOption>(), "kscript routes samples/http-app", "http"),
            new CliCommand("openapi", "kscript openapi [site folder] [--yaml] [--out file]",
                "Write the OpenAPI 3.1 document for a site's routes, made from the code. kscript serve also answers /_openapi.json, /_openapi.yaml and /_api.",
                new[]
                {
                    O("--yaml", "Write YAML instead of JSON (also chosen by an --out file ending in .yaml or .yml)."),
                    O("--out file", "Write to a file instead of the terminal."),
                },
                "kscript openapi\nkscript openapi --yaml --out openapi.yaml", "http"),
            new CliCommand("docs", "kscript docs [search words] [--port 4180] [--format kscript|xml] [--no-open] [--project]",
                "Open these docs: a local website with every package, element, command and method, and examples you can run.",
                new[]
                {
                    O("--port N", "The port to serve the docs on (default 4180, or a free port if that one is taken)."),
                    O("--format kscript|xml", "Show examples as .kscript or XML (default: the project's make.format, else .kscript)."),
                    O("--no-open", "Don't open a browser; just print the address."),
                    O("--project", "Write .kscript/docs/project.md, the docs of this project's own code (its conventions, routes, functions and types). From then on kscript watch, kscript serve and saving in the editor keep it up to date."),
                },
                "kscript docs\nkscript docs replace\nkscript docs --format xml\nkscript docs --project"),
            new CliCommand("mcp", "kscript mcp [--format kscript|xml] [--dir folder] [--http [--port 4181]]",
                "Serve these docs to AI coding assistants (Claude Code, Cursor, VS Code...) over MCP, offline.",
                new[]
                {
                    O("--format kscript|xml", "Show examples as .kscript or XML (default: the project's make.format, else .kscript)."),
                    O("--dir folder", "The project whose kscript.json sets the format and the check and fmt rules (default: the current folder)."),
                    O("--http", "Serve on http://localhost:4181/mcp instead of stdin and stdout."),
                    O("--port N", "The port for --http (default 4181)."),
                },
                "claude mcp add kscript -- kscript mcp\nkscript mcp --http", "mcp"),
            new CliCommand("css", "kscript css [file-or-folder]... [--out folder] [--var name=value]",
                "Compile .ks.css, .ks.scss and .ks.sass stylesheets.",
                new[]
                {
                    O("--out folder", "Where to write the .css files (default: next to each stylesheet)."),
                    O("--var name=value", "Set a stylesheet variable; repeat for more."),
                },
                "kscript css styles/ --out public", "stylesheets"),
            new CliCommand("convert", "kscript convert <file-or-folder>... [--to xml|kscript] [--stdout]",
                "Write the .kscript version of an XML script, or the reverse.",
                new[]
                {
                    O("--to xml|kscript", "The format to write (default: the other one)."),
                    O("--stdout", "Print the result instead of writing a file."),
                },
                "kscript convert hello.ks.xml\nkscript convert hello.kscript --to xml --stdout", "code-format"),
            new CliCommand("compile", "kscript compile [script or folder] [--target mac-arm64,win-x64,linux-x64|all] [--out dist]",
                "Build programs that run without kscript.",
                new[]
                {
                    O("--target name[,name]", "mac-arm64, mac-x64, linux-x64, linux-arm64, win-x64, or all (default: this computer)."),
                    O("--out folder", "Where programs go (default: dist/ next to the script or kscript.json), one folder per target."),
                    O("--name \"My App\"", "The program's name (default: the app section of kscript.json, or the script's name)."),
                    O("--runtime file", "A kscript release binary or archive for the target, instead of downloading one."),
                },
                "kscript compile hello.ks.xml\nkscript compile --target all", "compile"),
            new CliCommand("make", "kscript make project <folder> [--bare] [--port 8080]\nkscript make <type> <name> [--key value]\nkscript make --list",
                "Start a new project, or generate a file: a route (added to the routes file), controller, request, middleware, view, page, component, stylesheet, type, enum, window, config, test and more. kscript make --list shows them all, with their options.",
                new[]
                {
                    O("--list", "Every type of file kscript make can make, with its options and examples. kscript make <type> --help shows one."),
                    O("--format kscript|xml", "Write .kscript (the default) or .ks.xml files."),
                    O("--dry-run", "Show what would be made, and make nothing."),
                    O("--force", "Replace a file that is already there."),
                    O("--key value", "Values for the template, such as --resource or --fields \"name: string\"."),
                },
                "kscript make project my-site\nkscript make route users --api\nkscript make controller people --resource", "projects"),
            new CliCommand("ide", "kscript ide [install|uninstall|config] [editor]",
                "Set up VS Code, Cursor, VSCodium or another editor.", Array.Empty<CliOption>(),
                "kscript ide install\nkscript ide config neovim", "install"),
            new CliCommand("lsp", "kscript lsp", "Start the language server (LSP over stdio).", Array.Empty<CliOption>(), null, "install"),
            new CliCommand("update", "kscript update [--check] [--force] [--version <tag>]",
                "Update to the latest release.",
                new[]
                {
                    O("--check", "Only report whether there is a newer release."),
                    O("--force", "Install even if this version is already the latest."),
                    O("--version tag", "Install a specific release, such as v0.3.0."),
                },
                "kscript update", "install"),
            new CliCommand("version", "kscript --version | --help", "Print the version, or this help.", Array.Empty<CliOption>()),
        };

        /// <summary>The usage text of <c>kscript --help</c>.</summary>
        public static void PrintUsage(TextWriter output)
        {
            var lines = Commands.SelectMany(c =>
            {
                string[] usages = c.Usage.Split('\n');
                return usages.Select((u, i) => (Usage: u, Summary: i == usages.Length - 1 ? c.Summary : string.Empty));
            }).ToList();
            int width = Math.Min(70, lines.Max(l => l.Usage.Length));
            bool first = true;
            foreach (var (usage, summary) in lines)
            {
                string text = summary.Length == 0 ? usage : usage.PadRight(width) + "   " + summary;
                output.WriteLine((first ? "Usage: " : "       ") + text.TrimEnd());
                first = false;
            }
        }
    }
}
