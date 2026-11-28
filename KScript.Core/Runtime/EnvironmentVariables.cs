namespace KScript.Runtime
{
    /// <summary>
    /// Every environment variable kscript reads, with what it does. The docs' environment variables page is made from
    /// this list, and a test fails when kscript's code, its install scripts or its Docker files read a KSCRIPT_
    /// variable that isn't here, so a new one can't ship without being documented.
    /// </summary>
    public static class EnvironmentVariables
    {
        /// <summary>One environment variable.</summary>
        /// <param name="Name">The variable, such as KSCRIPT_ENV.</param>
        /// <param name="Area">The heading it is listed under on the docs page.</param>
        /// <param name="Default">What kscript does when it isn't set.</param>
        /// <param name="Description">What it does, in Markdown.</param>
        /// <param name="Example">A shell line that sets it.</param>
        /// <param name="Guide">The guide page that explains it in context, or null.</param>
        public sealed record Variable(string Name, string Area, string Default, string Description, string Example, string Guide);

        public const string Environment = "KSCRIPT_ENV";
        public const string Debug = "KSCRIPT_DEBUG";
        public const string ErrorPaths = "KSCRIPT_ERROR_PATHS";
        public const string Locale = "KSCRIPT_LOCALE";
        public const string Secret = "KSCRIPT_SECRET";
        public const string StorageRoot = "KSCRIPT_STORAGE_ROOT";
        public const string ProjectDataRoot = "KSCRIPT_PROJECT_DATA_ROOT";
        public const string ServeInDocker = "KSCRIPT_SERVE_IN_DOCKER";
        public const string ServeMigrate = "KSCRIPT_SERVE_MIGRATE";
        public const string LiveReload = "KSCRIPT_LIVE_RELOAD";
        public const string Source = "KSCRIPT_SOURCE";
        public const string Templates = "KSCRIPT_TEMPLATES";
        public const string AiModel = "KSCRIPT_AI_MODEL";
        public const string Repository = "KSCRIPT_REPO";
        public const string Version = "KSCRIPT_VERSION";
        public const string Install = "KSCRIPT_INSTALL";
        public const string NoModifyPath = "KSCRIPT_NO_MODIFY_PATH";

        /// <summary>The headings of the docs page, in order.</summary>
        public static readonly string[] Areas = { "Running scripts", "Serving and Docker", "Projects and data", "Language models", "Installing and updating", "Terminal output" };

        public static readonly IReadOnlyList<Variable> All = new[]
        {
            new Variable(Environment, "Running scripts", "`config.environment` in kscript.json, else none",
                "The environment a script or server runs in, such as `staging` or `production`. Its settings files (`kscript.staging.json`, `.env.staging`) are layered over the base ones, and the database connection for it is used. `kscript --env` wins over it, and sets it for the scripts it starts.",
                "KSCRIPT_ENV=production kscript serve", "/guide/language"),
            new Variable(Debug, "Running scripts", "off",
                "Set to `1` to show the .NET exception and stack trace under an error, as `kscript --debug` does. `0` and `false` leave it off.",
                "KSCRIPT_DEBUG=1 kscript app.kscript", "/guide/errors"),
            new Variable(ErrorPaths, "Running scripts", "paths relative to the folder",
                "Set to `full` and the error a script stops with names its file by its whole path, so an editor that runs scripts can link to the line.",
                "KSCRIPT_ERROR_PATHS=full kscript tools/report.kscript", "/reference/editor"),
            new Variable(Locale, "Running scripts", "the computer's language",
                "The locale for translations when the script doesn't choose one with `locale(set: ...)` and `$config.i18n.locale` isn't set.",
                "KSCRIPT_LOCALE=fr kscript invoice.kscript", "/guide/language"),

            new Variable(Secret, "Serving and Docker", "a new random key each time the server starts",
                "The key `kscript serve` signs live pages and components with. Set it to a long random value so pages already open keep working after the server restarts, and so every server behind a load balancer accepts them.",
                "KSCRIPT_SECRET=\"$(openssl rand -hex 32)\" kscript serve", "/guide/components"),
            new Variable(ServeInDocker, "Serving and Docker", "`kscript serve` hands over to Docker when the project has a Compose file",
                "Tells `kscript serve` it is already inside a container, so it starts the web server instead of handing over to Docker. The Dockerfiles `kscript make docker` writes set it. Set it yourself in a custom image that runs `kscript serve`.",
                "docker run -e KSCRIPT_SERVE_IN_DOCKER=1 my-app kscript serve", "/guide/projects"),
            new Variable(ServeMigrate, "Serving and Docker", "migrations are left to `kscript db migrate`",
                "Set to `1` and `kscript serve` applies pending migrations before it serves, and stops if one fails. `kscript serve --docker` sets it in the container, so you don't need to.",
                "KSCRIPT_SERVE_MIGRATE=1 kscript serve", "/guide/projects"),
            new Variable(LiveReload, "Serving and Docker", "on",
                "Set to `0` to turn off live reload in `kscript serve`, as `--no-reload` does, so open pages aren't refreshed when a file changes. `kscript serve --docker --no-reload` passes it into the container.",
                "KSCRIPT_LIVE_RELOAD=0 kscript serve", "/guide/web"),
            new Variable(Source, "Serving and Docker", "the released KScript source",
                "The KScript checkout the Docker files `kscript make docker` writes build from, for an unpublished or modified runtime. It is read by `docker compose`, not by kscript.",
                "KSCRIPT_SOURCE=/absolute/path/to/kscript docker compose up --build", "/guide/projects"),

            new Variable(StorageRoot, "Projects and data", "each app's usual folders for this computer",
                "Moves every app's folders to `<root>/<app>/<kind>`, such as `<root>/notes/data`: KScript.Auth's users and sessions, `@storage()` files and the file cache. It needs an absolute path. It is handy for tests, portable installs and containers.",
                "KSCRIPT_STORAGE_ROOT=/var/lib/kscript/storage kscript serve", "/guide/storage"),
            new Variable(ProjectDataRoot, "Projects and data", "the project folder",
                "The folder relative auth and SQLite paths in kscript.json are under, instead of the project folder, such as when the project folder is read-only. It needs an absolute path, and a path that leaves it is an error.",
                "KSCRIPT_PROJECT_DATA_ROOT=/var/lib/kscript/project kscript serve", "/guide/storage"),
            new Variable(Templates, "Projects and data", "`~/.kscript/templates`",
                "The folder of your own `kscript make project` templates. `kscript make templates project` copies the built-in ones there to start from.",
                "KSCRIPT_TEMPLATES=~/work/kscript-templates kscript make project shop --template api", "/guide/projects"),
            new Variable("DATABASE_URL", "Projects and data", "`data/app.sqlite` in the project folder",
                "The database to use when kscript.json and the config files don't set one, such as `postgres://app:secret@localhost:5432/app`.",
                "DATABASE_URL=postgres://app:secret@localhost:5432/app kscript serve", "/guide/data"),

            new Variable("ANTHROPIC_API_KEY", "Language models", "KScript.AI uses `ANTHROPIC_AUTH_TOKEN`, or the `ant auth login` profile",
                "The API key KScript.AI sends to Claude. Scripts never hold the key themselves.",
                "ANTHROPIC_API_KEY=sk-ant-... kscript triage.kscript", "/guide/language"),
            new Variable("ANTHROPIC_AUTH_TOKEN", "Language models", "KScript.AI uses the `ant auth login` profile, if there is one",
                "An OAuth token KScript.AI sends instead of an API key, when `ANTHROPIC_API_KEY` isn't set. Without either, it uses the profile `ant auth login` saved.",
                "ANTHROPIC_AUTH_TOKEN=... kscript triage.kscript", "/guide/language"),
            new Variable("ANTHROPIC_BASE_URL", "Language models", "Anthropic's API",
                "Sends KScript.AI's requests to another host, such as a proxy or a stand-in server for tests.",
                "ANTHROPIC_BASE_URL=http://127.0.0.1:8765 kscript triage.kscript", "/guide/language"),
            new Variable(AiModel, "Language models", "`claude-opus-5-5`",
                "The model `llm` and `@llm()` use when a script doesn't name one with `model:`.",
                "KSCRIPT_AI_MODEL=claude-haiku-5-5 kscript triage.kscript", "/guide/language"),

            new Variable(Version, "Installing and updating", "the latest release",
                "The release the install script installs, such as `v0.34.0`. The Dockerfiles `kscript make project` writes take it as a build argument to pin one.",
                "curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | KSCRIPT_VERSION=v0.34.0 sh", "/guide/install"),
            new Variable(Install, "Installing and updating", "`~/.kscript`, or `%LOCALAPPDATA%\\Programs\\kscript` on Windows",
                "The folder the install script puts kscript in. The binary goes in its `bin` folder.",
                "curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | KSCRIPT_INSTALL=/opt/kscript sh", "/guide/install"),
            new Variable(NoModifyPath, "Installing and updating", "the install script adds kscript to your PATH",
                "Set to `1` and the install script doesn't add kscript to your shell profile or your Windows PATH.",
                "curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | KSCRIPT_NO_MODIFY_PATH=1 sh", "/guide/install"),
            new Variable(Repository, "Installing and updating", "`kylepantall/kscript`",
                "The GitHub repository the install scripts, `kscript update`, `kscript compile` and `kscript ide install` download releases from, such as a fork. kscript says which repository it used when it isn't the usual one.",
                "KSCRIPT_REPO=my-org/kscript kscript update", "/guide/install"),
            new Variable("GITHUB_TOKEN", "Installing and updating", "requests go without a token",
                "A GitHub token `kscript update` and `kscript ide install` send with their requests, so they aren't limited the way requests without one are, such as on a shared CI runner.",
                "GITHUB_TOKEN=ghp_... kscript update", "/guide/install"),

            new Variable("NO_COLOR", "Terminal output", "colours are used in a terminal",
                "Set to anything and kscript, `echo` and KScript.Terminal print without colours.",
                "NO_COLOR=1 kscript test", "/guide/testing"),
            new Variable("FORCE_COLOR", "Terminal output", "colours are used only in a terminal",
                "Set to `1` to keep colours when the output isn't a terminal, such as in CI logs. `0` and `false` turn them off.",
                "FORCE_COLOR=1 kscript test | tee test.log", "/guide/testing"),
            new Variable("TERM", "Terminal output", "set by your terminal",
                "When it is `dumb`, kscript prints without colours.",
                "TERM=dumb kscript check", null),
            new Variable("COLUMNS", "Terminal output", "the terminal's width, or 80",
                "How wide kscript's tables, lines of dots and wrapped text are, instead of the terminal's own width.",
                "COLUMNS=120 kscript db status", null),
        };

        /// <summary>The variable named, or null.</summary>
        public static Variable Find(string name) => All.FirstOrDefault(v => v.Name == name);
    }
}
