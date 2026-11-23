using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using KScript.Output;

namespace KScript
{
    /// <summary>One option of a kscript command, such as <c>--port 8080</c>.</summary>
    public sealed record CliOption(string Name, string Description);

    /// <summary>One kscript command: how to call it, what it does, its options and an example.</summary>
    public sealed record CliCommand(string Name, string Usage, string Summary, IReadOnlyList<CliOption> Options, string Example = null, string Guide = null)
    {
        /// <summary>The heading kscript --help lists it under, such as "web" or "queue".</summary>
        public string Group { get; init; }

        /// <summary>More about the command, shown by kscript help &lt;command&gt; under its description.</summary>
        public string Details { get; init; }
    }

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
                    O("--debug", "Show the .NET exception and stack trace behind an error (also KSCRIPT_DEBUG=1). Works with every command."),
                },
                "kscript hello.kscript Joan\nkscript --env staging deploy.kscript", "language") { Group = "run" },
            new CliCommand("check", "kscript check [file-or-folder]... [--fix] [--warnings-as-errors] [--output text|json]",
                "Check scripts and pages without running them (no path: the current project).",
                new[]
                {
                    O("--fix", "Apply the fixes that are always safe (text in quotes, true and false, lists as arrays), then check what is left."),
                    O("--warnings-as-errors", "Fail on warnings too, for CI (lint.warnings_as_errors in kscript.json does the same)."),
                    O("--output text|json", "How to print the problems: text (the default), or json with each one's range and its fixes as exact text edits (for coding agents and tools)."),
                    O("--print-config [path]", "Show the lint and format settings that apply, and which standard set each."),
                },
                "kscript check\nkscript check scripts/ --warnings-as-errors\nkscript check --fix", "standards") { Group = "code" },
            new CliCommand("agents", "kscript agents [folder] [--check] [--print]",
                "Write AGENTS.md and .kscript/agents: compact docs for coding agents, made from the project's code and packages.",
                new[]
                {
                    O("--check", "Write nothing; fail if the agent docs are out of date (for CI)."),
                    O("--print", "Print AGENTS.md's generated section instead of writing files."),
                },
                "kscript agents\nkscript agents --check", "agents") { Group = "code" },
            new CliCommand("watch", "kscript watch [folder] [--once] [--check]",
                "Rebuild everything the project makes from its code (the agent docs, the OpenAPI file http.openapi.file names, and more as packages add to it) each time a file changes, until Ctrl+C. kscript serve and the editor (on save) do the same.",
                new[]
                {
                    O("--once", "Build once and stop."),
                    O("--check", "Write nothing; fail if anything is out of date (for CI)."),
                },
                "kscript watch\nkscript watch --check", "agents") { Group = "run" },
            new CliCommand("fmt", "kscript fmt [file-or-folder]... [--check]",
                "Tidy scripts and pages.",
                new[] { O("--check", "Only report files that would change, and fail if any would (for CI).") },
                "kscript fmt\nkscript fmt --check", "formatting") { Group = "code", Details = "Tidies .kscript and .ks.xml scripts and kscript serve .html pages. With no path, formats the current project." },
            new CliCommand("test", "kscript test [file-or-folder]... [--filter text] [--timeout s] [--junit file]",
                "Run *.test.kscript and *.test.ks.xml tests.",
                new[]
                {
                    O("--filter text", "Only run tests whose name contains the text."),
                    O("--timeout s", "Stop a test that runs longer than this many seconds (default 10)."),
                    O("--junit file", "Also write a JUnit XML report, for CI."),
                },
                "kscript test\nkscript test tests/cart.test.kscript --filter total", "testing") { Group = "run", Details = "Runs *.test.kscript and *.test.ks.xml files (or the files test.include names in kscript.json). With no paths, runs the current project's tests." },
            new CliCommand("serve", "kscript serve [site folder] [--port 8080] [--host localhost] [--env name] [--with-docs [--docs-port 4180]] [--with-worker]",
                "Serve pages, and routes.kscript / api.kscript routes.",
                new[]
                {
                    O("--port, -p N", "The port to listen on (default 8080, or serve.port in kscript.json)."),
                    O("--host H", "The address to listen on: localhost (default), 0.0.0.0 or * for every address."),
                    O("--env name", "The environment for $config."),
                    O("--with-docs", "Also serve the docs on localhost, with a page about this project: its conventions, routes, and the functions, types and constants its scripts declare. serve.docs in kscript.json turns it on by default; --no-docs turns it off. Never for compiled programs or the production environment."),
                    O("--docs-port N", "The port for the docs (default 4180, or a free one if that is in use)."),
                    O("--with-worker", "Also run a queue worker in the server's process, for working on a site: it runs the jobs routes dispatch, including on the memory connection."),
                },
                "kscript serve\nkscript serve site --port 3000\nkscript serve --with-docs\nkscript serve --with-worker", "web") { Group = "web" },
            new CliCommand("worker", "kscript worker [--connection name] [--queue a,b] [--concurrency n] [--once] [--stop-when-empty] [--max-jobs n] [--max-time 1h] [--sleep 1s]",
                "Run queued jobs until stopped. Ctrl+C lets the running jobs finish first.",
                new[]
                {
                    O("--connection, -c name", "The connection in queue.connections to work (default queue.default)."),
                    O("--queue, -q a,b", "The queues to check, first to last (default queue.queues, else default and then every other queue)."),
                    O("--concurrency n", "How many jobs to run at once (default queue.worker.concurrency, else 1)."),
                    O("--once", "Run one job, if there is one, then stop: handy from cron."),
                    O("--stop-when-empty", "Stop once every queue is empty instead of waiting for more jobs."),
                    O("--max-jobs n", "Stop after this many jobs."),
                    O("--max-time t", "Stop after this long, such as 1h."),
                    O("--sleep t", "How long to wait when the queues are empty (default 1s)."),
                },
                "kscript worker\nkscript worker --queue high,default --concurrency 4\nkscript worker --stop-when-empty", "queues") { Group = "queue", Details = "Settings come from the queue section of kscript.json." },
            new CliCommand("queue", "kscript queue status|list [queue]|failed|retry <id>|delete <id>|clear [queue] [--all] [--connection name] [--output text|json]",
                "Look after the queues: what is waiting, what failed and why, and run failed jobs again.",
                new[]
                {
                    O("status", "Waiting, delayed, running and failed jobs per queue."),
                    O("list [queue]", "The waiting jobs with their fields."),
                    O("failed", "The failed jobs with their errors."),
                    O("retry <id> | --all", "Put failed jobs back on their queues, with their tries reset."),
                    O("delete <id> | --all", "Delete failed jobs."),
                    O("clear [queue]", "Remove the waiting jobs of a queue (or of every queue)."),
                    O("--connection, -c name", "The connection to look at (default queue.default)."),
                    O("--output text|json", "How to print: text (a table, the default) or json."),
                },
                "kscript queue status\nkscript queue failed\nkscript queue retry --all", "queues") { Group = "queue" },
            new CliCommand("db", "kscript db diff [name] [--syntax kscript|xml]|migrate|rollback [--steps n]|status [--output text|json]|fresh [--seed]|seed [name] [--pretend] [--force]",
                "Migrations and seeders: write a migration from the models, run it on the database, undo it, and fill the tables.",
                new[]
                {
                    O("diff [name]", "Write a migration to database/migrations with the changes the models want that the migrations don't make yet."),
                    O("migrate", "Run the migrations that haven't run, as one batch. --seed runs the seeders after."),
                    O("rollback", "Undo the last batch. --steps n undoes the last n migrations, --all every one."),
                    O("status", "Which migrations have run, and the changes the models want that no migration makes."),
                    O("fresh", "Drop every table and run every migration again. --seed runs the seeders after."),
                    O("seed [name]", "Run the seeders in database/seeders (or the one named)."),
                    O("--pretend", "Print the SQL instead of running it (migrate, rollback), or the migration instead of writing it (diff)."),
                    O("--force", "Go ahead in the production environment without asking."),
                    O("--output text|json", "How status prints: text (a table, the default) or json."),
                    O("--syntax kscript|xml", "The syntax diff writes the migration in: .kscript (the default, or make.syntax in kscript.json) or .ks.xml."),
                    O("--project folder", "The project to work on (default: the current folder's)."),
                },
                "kscript db diff\nkscript db migrate\nkscript db rollback\nkscript db fresh --seed", "data")
            { Group = "data", Details = "The database is the project's default connection: database in config, else SQLite at data/app.sqlite." },
            new CliCommand("routes", "kscript routes [site folder] [-v]",
                "List the routes of a site.", new[] { O("-v, --verbose", "Also show each route's middleware, and the file and line it is declared on.") }, "kscript routes\nkscript routes samples/http-app -v", "http") { Group = "web" },
            new CliCommand("openapi", "kscript openapi [site folder] [--output json|yaml] [--out file]",
                "Write the OpenAPI 3.1 document for a site's routes, made from the code. kscript serve also answers /_openapi.json, /_openapi.yaml and /_api.",
                new[]
                {
                    O("--output json|yaml", "How to print the document: json (the default) or yaml (also chosen by an --out file ending in .yaml or .yml)."),
                    O("--out file", "Write to a file instead of the terminal."),
                },
                "kscript openapi\nkscript openapi --output yaml --out openapi.yaml", "http") { Group = "web" },
            new CliCommand("docs", "kscript docs [search words] [--port 4180] [--syntax kscript|xml] [--no-open] [--project]",
                "Open these docs: a local website with every package, element, command and method, and examples you can run.",
                new[]
                {
                    O("--port N", "The port to serve the docs on (default 4180, or a free port if that one is taken)."),
                    O("--syntax kscript|xml", "Show examples as .kscript or XML (default: the project's make.syntax, else .kscript)."),
                    O("--no-open", "Don't open a browser; just print the address."),
                    O("--project", "Write .kscript/docs/project.md, the docs of this project's own code (its conventions, routes, functions and types). From then on kscript watch, kscript serve and saving in the editor keep it up to date."),
                },
                "kscript docs\nkscript docs replace\nkscript docs --syntax xml\nkscript docs --project") { Group = "docs", Details = "Examples show in the project's make.syntax (kscript.json), or .kscript outside a project; --syntax picks one." },
            new CliCommand("mcp", "kscript mcp [--syntax kscript|xml] [--dir folder] [--http [--port 4181]]",
                "Serve these docs to AI coding assistants (Claude Code, Cursor, VS Code...) over MCP, offline.",
                new[]
                {
                    O("--syntax kscript|xml", "Show examples as .kscript or XML (default: the project's make.syntax, else .kscript)."),
                    O("--dir folder", "The project whose kscript.json sets the syntax and the check and fmt rules (default: the current folder)."),
                    O("--http", "Serve on http://localhost:4181/mcp instead of stdin and stdout."),
                    O("--port N", "The port for --http (default 4181)."),
                },
                "claude mcp add kscript -- kscript mcp\nkscript mcp --http", "mcp") { Group = "docs", Details = "Setup for Claude Code, Cursor, VS Code and other clients: the \"AI assistants (MCP)\" page of kscript docs." },
            new CliCommand("css", "kscript css [file-or-folder]... [--out folder] [--var name=value]",
                "Compile .ks.css, .ks.scss and .ks.sass stylesheets.",
                new[]
                {
                    O("--out folder", "Where to write the .css files (default: next to each stylesheet)."),
                    O("--var name=value", "Set a stylesheet variable; repeat for more."),
                },
                "kscript css styles/ --out public", "stylesheets") { Group = "web" },
            new CliCommand("convert", "kscript convert <file-or-folder>... [--syntax kscript|xml] [--stdout]",
                "Write the .kscript version of an XML script, or the reverse.",
                new[]
                {
                    O("--syntax kscript|xml", "The syntax to write (default: the other one)."),
                    O("--out file", "Where to write the result, for one file (default: next to it, with the new extension)."),
                    O("--stdout", "Print the result instead of writing a file."),
                    O("--force", "Replace a file that is already there."),
                },
                "kscript convert hello.ks.xml\nkscript convert hello.kscript --syntax xml --stdout", "code-format") { Group = "code", Details = "The original is left as it is. A folder converts every script in it (*.kscript and *.ks.xml, tests too).\nImports, and file names in comments and strings, that point at a converted script are changed to its new name." },
            new CliCommand("repo", "kscript repo add <owner/repo|github-url>",
                "Register a GitHub repository that provides named libraries.",
                Array.Empty<CliOption>(),
                "kscript repo add acme/kscript-libraries\nkscript lib add acme/kscript-libraries@slugs", "projects")
            {
                Group = "build",
                Details = "Give a custom publisher as owner/repo or its HTTPS GitHub URL. The command records the repository in .kscript/repos/ beside the nearest kscript.json, or in the current folder outside a project. The built-in catalog kylepantall/kscript and public repositories under the trusted GitHub owners waggr-gb and orimovo are available in every project.",
            },
            new CliCommand("lib", "kscript lib add <library|owner/repo@library|owner/repo|github-url>\nkscript lib restore",
                "Install a KScript source library or restore the project's pinned libraries.",
                Array.Empty<CliOption>(),
                "kscript lib add isolink-connect\nkscript lib add waggr-gb/isolink-connect@isolink-connect\nkscript lib restore", "projects")
            {
                Group = "build",
                Details = "A bare name searches kylepantall/kscript, all public repositories under orimovo and waggr-gb, and repositories registered in this project. Each repository can publish libraries/<name>/ or a matching library manifest at its root. One match installs immediately. For multiple matches, the CLI prints the count and each distinct owner/repo@library name. Rerun lib add with owner/repo@library to install your choice. A qualified name avoids the catalog search and uses GitHub API quota only for the selected publisher. Register another publisher with kscript repo add owner/repo. You can also install a GitHub repository or tree URL directly. The installer prints the import path and records the source and commit in the project's kscript.json. Commit that file so kscript lib restore fetches directly from the recorded repository at that commit.",
            },
            new CliCommand("compile", "kscript compile [script or folder] [--target mac-arm64,win-x64,linux-x64|all] [--out dist]",
                "Build programs that run without kscript.",
                new[]
                {
                    O("--target name[,name]", "mac-arm64, mac-x64, linux-x64, linux-arm64, win-x64, or all (default: this computer)."),
                    O("--out folder", "Where programs go (default: dist/ next to the script or kscript.json), one folder per target."),
                    O("--name \"My App\"", "The program's name (default: the app section of kscript.json, or the script's name)."),
                    O("--runtime file", "A kscript release binary or archive for the target, instead of downloading one."),
                },
                "kscript compile hello.kscript\nkscript compile --target all", "compile") { Group = "build", Details = "It holds the script, its imports and files, and only the packages it uses: a terminal script, a KScript.Window desktop app, or a kscript serve site.\nWith no script, kscript.json names it: { \"app\": { \"main\": \"app.kscript\", \"name\": \"My App\", \"icon\": \"icon.png\" } }. A project without app.main but with pages compiles to a program that serves the site." },
            new CliCommand("make", "kscript make project [folder] [--template web|api|fullstack|bare] [--db sqlite] [--auth] [--docker]\nkscript make <type> <name> [--key value]\nkscript make auth [--passkeys] [--origin URL] [--rp-id DOMAIN] [--api]\nkscript make --list",
                "Start a new project, or generate a file: a route (added to the routes file), controller, request, middleware, view, page, component, stylesheet, type, enum, window, config, test and more. kscript make --list shows them all, with their options.",
                new[]
                {
                    O("--list", "Every type of file kscript make can make, with its options and examples. kscript make <type> --help shows one."),
                    O("--template name", "kscript make project: web (the default), api (a JSON API), fullstack (pages and their API) or bare. With no options at a terminal, a setup wizard asks."),
                    O("-n, --no-interaction", "kscript make project: ask nothing, and take the defaults."),
                    O("--syntax kscript|xml", "Write .kscript (the default) or .ks.xml files."),
                    O("--dry-run", "Show what would be made, and make nothing."),
                    O("--force", "Replace a file that is already there."),
                    O("--key value", "Values for the template, such as --resource or --fields \"name: string\"."),
                },
                "kscript make project my-site\nkscript make project shop --template api --db postgres --docker\nkscript make route users --api\nkscript make controller people --resource", "projects") { Group = "build" },
            new CliCommand("ide", "kscript ide [install|uninstall|config] [editor]",
                "Set up VS Code, Cursor, VSCodium or another editor.", Array.Empty<CliOption>(),
                "kscript ide install\nkscript ide config neovim", "install") { Group = "tools" },
            new CliCommand("lsp", "kscript lsp", "Start the language server (LSP over stdio).", Array.Empty<CliOption>(), null, "install") { Group = "tools" },
            new CliCommand("update", "kscript update [--check] [--force] [--version <tag>]",
                "Update to the latest release.",
                new[]
                {
                    O("--check", "Only report whether there is a newer release."),
                    O("--force", "Install even if this version is already the latest."),
                    O("--version tag", "Install a specific release, such as v0.3.0."),
                    O("--no-ide", "Leave the KScript extension in your editors as it is."),
                },
                "kscript update", "install") { Group = "tools", Details = "The KScript extension is updated to the same release in every editor that has it (VS Code, Cursor, VSCodium, Windsurf)." },
            new CliCommand("version", "kscript --version | --help", "Print the version, or this help.", Array.Empty<CliOption>()) { Group = "tools" },
        };

        /// <summary>The headings of kscript --help, in order, with the commands under each.</summary>
        public static readonly IReadOnlyList<(string Group, string[] Commands)> Groups = new[]
        {
            ("run", new[] { "run", "test", "watch" }),
            ("code", new[] { "check", "fmt", "convert", "agents" }),
            ("web", new[] { "serve", "routes", "openapi", "css" }),
            ("data", new[] { "db" }),
            ("queue", new[] { "worker", "queue" }),
            ("build", new[] { "make", "repo", "lib", "compile" }),
            ("docs", new[] { "docs", "mcp" }),
            ("tools", new[] { "ide", "lsp", "update", "version" }),
        };

        /// <summary>The options every command takes.</summary>
        public static readonly IReadOnlyList<CliOption> GlobalOptions = new[]
        {
            O("-h, --help", "Show help: kscript help <command> (or kscript <command> --help) shows one command."),
            O("--version", "Show the version of kscript."),
            O("--env name", "The environment for $config: reads <name>.yaml and .env.<name>, and sets KSCRIPT_ENV."),
            O("--ansi | --no-ansi", "Force colours on, or turn them off. NO_COLOR and FORCE_COLOR do the same."),
        };

        /// <summary>What kscript's exit code means, the same for every command.</summary>
        public static IReadOnlyList<(int Code, string Meaning)> ExitCodeMeanings => ExitCodes.All;

        /// <summary>The command called <paramref name="name"/>, or null.</summary>
        public static CliCommand Find(string name) => Commands.FirstOrDefault(c => c.Name == name);

        /// <summary><c>kscript --help</c>: like artisan list, the version, usage, options and the commands under headings.</summary>
        public static void PrintUsage(TextWriter output)
        {
            var o = new CliOutput(output);
            o.Line($"{o.Style.Bold("KScript")} {o.Style.Green(KScript.Updates.SelfUpdater.CurrentVersion)}");
            o.NewLine();
            o.Heading("Usage:");
            o.Line("  kscript <command> [options] [arguments]");
            o.Line("  kscript <script> [script arguments]");
            o.NewLine();
            o.Heading("Options:");
            int width = Math.Max(GlobalOptions.Max(g => g.Name.Length), Commands.Max(c => c.Name.Length));
            o.Definitions(GlobalOptions.Select(g => (g.Name, g.Description)), nameWidth: width);
            o.NewLine();
            o.Heading("Available commands:");
            bool first = true;
            foreach (var (group, names) in Groups)
            {
                if (!first) o.Line(" " + o.Style.Yellow(group));
                o.Definitions(names.Select(Find).Where(c => c != null).Select(c => (c.Name, FirstSentence(c.Summary))), nameWidth: width);
                first = false;
            }
            o.NewLine();
            o.Heading("Exit codes:");
            o.Definitions(ExitCodes.All.Select(c => (c.Code.ToString(), c.Meaning)), nameWidth: width);
            o.NewLine();
            o.Comment("  kscript help <command> shows a command's options and examples.");
        }

        /// <summary><c>kscript help &lt;command&gt;</c>: its description, usage, options and examples, as artisan's help.</summary>
        public static void PrintCommand(TextWriter output, CliCommand command)
        {
            var o = new CliOutput(output);
            o.Heading("Description:");
            foreach (string line in o.Wrap(command.Summary, o.Width - 2)) o.Line("  " + line);
            if (command.Details != null)
            {
                foreach (string paragraph in command.Details.Split('\n'))
                {
                    foreach (string line in o.Wrap(paragraph, o.Width - 2)) o.Line(("  " + line).TrimEnd());
                }
            }
            o.NewLine();
            o.Heading("Usage:");
            foreach (string usage in command.Usage.Split('\n')) o.Line("  " + usage);
            var options = command.Options.Concat(GlobalOptions.Where(g => g.Name != "--version" && !command.Options.Any(c => c.Name == g.Name))).ToList();
            o.NewLine();
            o.Heading("Options:");
            o.Definitions(options.Select(c => (c.Name, c.Description)));
            if (command.Example != null)
            {
                o.NewLine();
                o.Heading("Examples:");
                foreach (string example in command.Example.Split('\n')) o.Line("  " + o.Style.Gray("$") + " " + example);
            }
            if (command.Guide != null)
            {
                o.NewLine();
                o.Comment($"  More in the {command.Guide} guide: kscript docs {command.Guide}");
            }
        }

        private static string FirstSentence(string text)
        {
            int end = text.IndexOf(". ", StringComparison.Ordinal);
            return end > 0 ? text.Substring(0, end + 1) : text;
        }
    }
}
