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
            new CliCommand("check", "kscript check [file-or-folder]... [--warnaserror]",
                "Check scripts and pages without running them (no path: the current project).",
                new[]
                {
                    O("--warnaserror", "Fail on warnings too, for CI."),
                    O("--print-config [path]", "Show the lint and format settings that apply, and which standard set each."),
                },
                "kscript check\nkscript check scripts/ --warnaserror", "standards"),
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
            new CliCommand("serve", "kscript serve [site folder] [--port 8080] [--host localhost] [--env name]",
                "Serve pages, and routes.ks.xml / api.ks.xml routes.",
                new[]
                {
                    O("--port, -p N", "The port to listen on (default 8080, or serve.port in kscript.json)."),
                    O("--host H", "The address to listen on: localhost (default), 0.0.0.0 or * for every address."),
                    O("--env name", "The environment for $config."),
                },
                "kscript serve\nkscript serve site --port 3000", "web"),
            new CliCommand("routes", "kscript routes [site folder]",
                "List the routes of a site.", Array.Empty<CliOption>(), "kscript routes samples/http-app", "http"),
            new CliCommand("docs", "kscript docs [search words] [--port 4180] [--no-open]",
                "Open these docs: a local website with every package, element, command and method, and examples you can run.",
                new[]
                {
                    O("--port N", "The port to serve the docs on (default 4180, or a free port if that one is taken)."),
                    O("--no-open", "Don't open a browser; just print the address."),
                },
                "kscript docs\nkscript docs replace"),
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
            new CliCommand("app", "kscript app build [script] [--name \"My App\"] [--out folder]",
                "Package a script as a desktop app.",
                new[]
                {
                    O("--name \"My App\"", "The app's name (default: from the script's file name)."),
                    O("--out folder", "Where to write the app."),
                },
                "kscript app build counter.ks.xml --name Counter", "desktop"),
            new CliCommand("make", "kscript make project <folder> [--port 8080]\nkscript make <type> <Name> [--key value]",
                "Start a new project with a kscript.json, or generate a file from a template (run kscript make for the list).",
                new[]
                {
                    O("--format kscript|xml", "Write .kscript (the default) or .ks.xml files."),
                    O("--key value", "Values for the template, such as --port 3000."),
                },
                "kscript make project my-site\nkscript make component Hero", "projects"),
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
