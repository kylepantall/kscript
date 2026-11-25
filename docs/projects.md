# Projects and generators

## Starting a project: kscript make project

`kscript make project` sets up a new web app, with the same app as a desktop window, and everything wired together:

```sh
kscript make project my-app --port 3000
cd my-app
kscript serve            # http://localhost:3000, from kscript.json
kscript desktop.kscript  # the desktop app, in a window
kscript compile          # build it as a program: dist/mac-arm64/My App.app, dist/win-x64/My App.exe...
kscript test             # runs the tests in tests/
kscript check            # checks every page and script in the project
kscript fmt              # tidies every script and page in the project
```

What it makes:

| File | What it is |
| --- | --- |
| `kscript.json` | Settings for `serve`, `check`, `test` and, with the desktop app, the `app` section `kscript compile` reads. |
| `index.html`, `about.md`, `_layout.html`, `_components/site-nav.kscript`, `site.css` | The site: two pages, the layout they share and a navigation component. |
| `tasks.html` | A to-do list in a [`<live>`](web.md#reactive-pages-with-live-no-javascript) block, with no JavaScript. |
| `lib/tasks.kscript` | The to-do rules (add, tick, remove, clear, count), shared by the page, the desktop app, the API and the tests. |
| `desktop.kscript` | The same to-do list as a [desktop app](desktop.md): a `Window { }` block. |
| `routes/api.kscript`, `controllers/tasks.kscript` | A JSON API for the tasks under `/api` ([KScript.Http](http.md)). While `kscript serve` runs, its docs are at `/_api` ([OpenAPI](http.md#api-docs-openapi)). |
| `config/config.kscript` | Settings every script and page reads as `$config` (listed in `kscript.json` under `config.files`). |
| `lib/greeting.kscript`, `scripts/hello.kscript` | A function and a command-line script that uses it. |
| `tests/tasks.test.kscript`, `tests/greeting.test.kscript` | Tests for the rules and the script, run with `kscript test`. |
| `.vscode/extensions.json`, `.gitignore` | Asks VS Code to install the KScript extension; keeps built apps (`dist/`) out of git. |
| `AGENTS.md`, `CLAUDE.md`, `.kscript/agents/` | Docs for coding agents, made from the project's code and kept up to date by the editor (see [Coding agents](agents.md)). |

Scripts, tests, routes and the desktop app are written in the [.kscript format](code-format.md). `--syntax xml` writes them as XML (`.ks.xml`) instead, and records `"make": { "syntax": "xml" }` in `kscript.json` so that later `kscript make script` and `kscript make test` follow suit. Pages stay HTML either way.

`--no-desktop` leaves out `desktop.kscript` and the `app` section, for a web site only. `--no-http` leaves out the API. `kscript make project .` sets up the current folder; existing files are never replaced without `--force`. `kscript make project --help` lists the template's options.

### A bare project: --bare

`kscript make project my-tool --bare` makes just `kscript.json`, `config/config.kscript`, `main.kscript` (a script) and `tests/main.test.kscript`, plus the VS Code recommendation and the [agent docs](agents.md) (`AGENTS.md`): no pages, desktop app or API. Use it for command-line tools and scripts. `--syntax xml` works here too. `--bare` is short for `--template bare`; `kscript make project --help --bare` lists its options.

Add pieces to a bare project with options, in any mix:

```sh
kscript make project my-app --bare --web --api   # an empty website and JSON API
kscript make project my-api --bare --api         # just the API: routes/api.kscript, controllers/hello.kscript and its test
kscript make project my-app --bare --desktop     # a script plus an empty desktop app (a Window block)
```

`--web` adds `index.html`, `_layout.html` and `site.css`; `--api` adds `routes/api.kscript` (a `Routes` file served under `/api`), `controllers/hello.kscript` and `tests/hello.test.kscript`; `--desktop` adds `desktop.kscript` and the `app` section of `kscript.json`. `kscript.json` gets `serve` settings (and `--port`) whenever there is a website or API.

### An API: --template api

`kscript make project shop --template api` makes a JSON API with nothing else to set up: routes, controllers, the app's own middleware, a model in a database, sign-up with API tokens, caching and tests.

```sh
kscript make project shop --template api
cd shop
kscript serve      # http://localhost:8080/api/health, and the API's docs at /_api
kscript test       # 12 tests, against a database in memory
```

| File | What it is |
| --- | --- |
| `routes/api.kscript` | The routes, under `/api`: `GET /health`, `POST /register` and `/tokens` (throttled), `GET` and `DELETE /me`, and a `Resource` for `/notes` behind the `auth` middleware. |
| `controllers/notes.kscript` | The notes resource: `index`, `store`, `show`, `update` and `destroy`, each answering with JSON. The `NoteInput` [type](types.md) it imports checks what is posted (422 when it doesn't fit), and a note that isn't there (or isn't yours) is a 404. |
| `controllers/accounts.kscript` | Sign-up, tokens and closing an account, with [KScript.Auth](auth.md). |
| `models/note.kscript` | The `Note` [model](data.md), whose table is made the first time it is used. |
| `requests/note-input.kscript`, `requests/sign-up.kscript`, `requests/sign-in.kscript` | The typed request bodies, one [type](types.md) per file, that the controllers import. |
| `middleware/request-id.kscript` | The app's own middleware: every response gets an `X-Request-Id` header. |
| `lib/cache.kscript` | The app's [cache](cache.md), whose store and time limit come from `config/config.kscript`. |
| `config/config.kscript` | The config function chooses database and cache settings. It provides an in-memory database for the test environment. |
| `tests/` | Tests that call the API the way a client would, with `@api(/api/notes)` (see [Calling your own API](http.md#calling-your-own-api)). |

### A web app with its API: --template fullstack

`kscript make project my-site --template fullstack` makes a site with a home page, layout, API health endpoint and a test for that endpoint. Choose the database, sign-in, caching and Docker options for your project.

Pass `--examples` or select **Include the Notes example?** in the wizard to add a [live](web.md#reactive-pages-with-live-no-javascript) Notes page, a CRUD API and their tests. The page calls the API through `lib/notes-client.kscript` with `@api(/api/notes)`.

### Database, sign-in, cache and Docker

The api and fullstack templates share these options:

| Option | Default | What it does |
| --- | --- | --- |
| `--db sqlite\|postgres\|mysql\|none` | `sqlite` | The database configured in `config/config.kscript`. With `none`, the Notes example uses a JSON file (`notes.file` in the config). With `postgres` or `mysql` the app connects to that server (`DATABASE_URL` overrides the url, and `--docker` adds it to the compose file), while `kscript test` still uses SQLite in memory. |
| `--auth`, `--no-auth` | on (api), off (fullstack) | Sign-in. In the api template: `POST /api/register`, `/api/tokens`, `GET /api/me`, and notes that belong to who is signed in. In the fullstack template it also runs [`kscript make auth`](auth.md) for the sign-in pages; the pages call the API as whoever is viewing them. |
| `--cache`, `--no-cache` | on | Adds cache configuration and helpers. The Notes example uses them to cache the list and answer repeat requests with `304 Not Modified`. |
| `--docker` | off | Adds a `Dockerfile`, `.dockerignore` and `compose.yaml`, with a Postgres or MySQL service for those databases, and `make up` / `make down`. |
| `--port` | `8080` | The port `kscript serve` uses. |

Options can be written `--db postgres` or `--db=postgres`, and a yes/no option takes `yes`/`no` (`--cache no`) but never the folder name, so `kscript make project --docker shop` makes `shop`.

### Adding Docker to an existing site

Run `kscript make docker` anywhere in a project. It adds `Dockerfile`, `compose.yaml` and `.dockerignore` beside `kscript.json`. Preview the files with `--dry-run`. If any output already exists, the command stops before writing. Use `--force` to replace those files. The same kit is available when creating a bare site.

```sh
kscript make project portal --bare --web --api --port 4820 --docker -n
cd portal
docker compose up --build
```

Open `http://localhost:4820`. The container serves the project's configured `serve.root` and uses `serve.port`. If you change the port, regenerate the kit or update the published port, `EXPOSE` and health check together.

Compose 2.17+ and BuildKit build KScript for Linux ARM64 or AMD64 from the released CLI's Git revision, using pinned multi-architecture .NET base images. A development build requires a source checkout. For a modified or unpublished KScript build, set `KSCRIPT_SOURCE` to its checkout.

```sh
KSCRIPT_SOURCE=/absolute/path/to/kscript docker compose up --build
```

The app runs as an unprivileged user with a read-only application directory. Compose keeps KScript.Storage, authentication state and project-relative SQLite files in the `app-state` volume at `/var/lib/kscript`. The volume survives container replacement. `KSCRIPT_PROJECT_DATA_ROOT` places configured relative auth and SQLite paths under that folder. For an absolute path or a separate database server, configure the corresponding mount or connection for your deployment. Restart `kscript serve` when changing storage paths so it refreshes the private files excluded from public serving. Secrets can be supplied as runtime environment variables. The generated `.dockerignore` excludes dotfiles, common credential files, local databases and runtime state. Add any project-specific secret filenames before building.

### Running the development site in Docker

Run `kscript serve --docker` to start the app service from your Compose file. It bind-mounts the project read-only at `/app` and defaults to the development environment. Open the site and edit pages, styles, or route files locally. The browser reloads when the container sees the edits. Use `--no-reload` to refresh manually. Restart the service after changing startup configuration or image dependencies. Existing Compose data volumes still apply. For a named or anonymous volume mounted under the source directory, such as `/app/data`, KScript creates the local mountpoint directory before starting Compose.

Add this to `kscript.json` to use Docker whenever you run `kscript serve`.

```json
{
  "serve": {
    "docker": {
      "compose_file": "compose.yaml",
      "service": "app",
      "mount": "/app",
      "build": true
    }
  }
}
```

`"docker": true` uses those defaults. Set `enabled: false` to keep the settings and opt in with `--docker`, or use `--no-docker` for a local run. Set `mount` to the app directory in a custom image. Set `mount: null` to serve the files baked into the image, or `build: false` to reuse an existing image. Use `kscript serve --docker --env test` to select test settings. Compose controls the published ports and the service command. Ctrl+C stops the development app container. Its dependencies can remain running, and `docker compose down` stops them.

Use `COMPOSE_PROJECT_NAME=myapp-test kscript serve --docker --env test` to put test services and their Compose-managed volumes in a separate project. Use a separate host port in the test Compose file if both sites run at once.

`docker compose up --build` serves the source copied into the generated image. For a custom image that invokes `kscript serve` directly, set `KSCRIPT_SERVE_IN_DOCKER=1` inside the container so it starts the web server.

### The setup wizard

`kscript make project` at a terminal with no options asks for everything instead: the folder (unless you gave one), the kind of project, and each of that template's options, with its default ready for Enter. Give any option, or `-n` (`--no-interaction`), and nothing is asked: what you didn't give takes its default, which is what scripts and CI want. When the input or output isn't a terminal, nothing is asked either.

### The Makefile

Every new project has a `Makefile`, so common commands are short: `make serve`, `make test`, `make check`, `make fmt`, `make agents`, `make compile`, and `make routes`, `make desktop` and `make app` when the project has an API or desktop app. Any other target is passed to `kscript make`, with `name=` as the name and `args=` for options:

```sh
make page name=contact                          # kscript make page contact
make component name=Hero args="--props title"   # kscript make component Hero --props title
```

`make` comes with macOS (in the Xcode command line tools) and Linux. On Windows, install it (`winget install GnuWin32.Make`, `scoop install make` or `choco install make`) or run the `kscript` commands directly. Set `KSCRIPT=/path/to/kscript` to use another kscript.

### Changing what new projects get

Every file `kscript make project` writes comes from a project template, and you can change it:

```sh
kscript make templates project            # outside a project: copies it to ~/.kscript/templates/project
kscript make templates project --global   # the same, from inside a project
kscript make templates project            # inside a project: copies it to _templates/project
kscript make templates project bare       # the bare template, to project-bare instead of project
kscript make project my-app --template ../starters/api   # any folder
```

`--template` takes a folder or a template name. A name such as `bare` (or your own, such as `api`) is looked up as `project-<name>` in the same places as the default template, which lives in `project`, and then among the built-in templates (`web`, `api`, `bare` and `fullstack`). `kscript make project` uses the first template it finds: the `--template` folder, then `_templates/project` in the project the new one is made inside (handy for a folder of several apps), then `~/.kscript/templates/project` (or `$KSCRIPT_TEMPLATES/project`), then the built-in one. Edit, add or delete files in the template folder to change what new projects get.

- Write scripts as `.kscript` files and refer to other scripts with `{{ ext }}` (`import "lib/tasks{{ ext }}"`). With `--syntax xml`, each `.kscript` file is converted to XML and named `.ks.xml`, and `{{ ext }}` is `.ks.xml`; `{{#if xml}}` tells the formats apart. A `.ks.xml` file in the template is copied as it is.
- Each file is a template with the same `{{ }}` placeholders as [`kscript make` templates](#your-own-templates): `{{ title }}` is the project name as a title (`My App`), `{{ kebab }}` is `my-app`, and every option is a value (`{{ port }}`, `{{#if desktop}} … {{/if}}`).
- A file can start with a `--- make` header. `when: desktop` copies it only when the option is on, `unless: desktop` only when it is off, and `to: docs/{{ kebab }}.md` writes it somewhere else. `when` and `unless` take the same tests as `{{#if}}`: `when: db is none`, `unless: db in sqlite, none`. Two files can write the same path when only one of them is copied (`controllers/notes-file.kscript` with `when: db is none` and `to: controllers/notes.kscript`).
- A name ending in `.stub` loses the ending: the template keeps `kscript.json.stub` so that editors and `kscript check` don't read it as a real project.
- `kscript-template.json` describes the template and is not copied. `options` lists each option with its `default` and `description` (an option that defaults to `true` is turned off with `--no-name`), a `question` for the setup wizard, and `choices` when it takes one of a few values (`--db oracle` is then an error that lists them). `next` holds the lines printed under "Next:", `notes` lines printed as notes (a line that comes out empty is left out), and `make` the kits made in the new project once its files are written: `"make": ["{{#if auth}}auth{{/if}}"]` runs `kscript make auth`. An option the template doesn't list is reported, so a typo such as `--no-dekstop` doesn't go unnoticed.

```jsonc
// _templates/project/kscript-template.json
{
  "description": "Our standard web app",
  "options": {
    "desktop": { "default": true, "description": "Add the desktop app" },
    "api": { "default": false, "description": "Add an API page", "question": "Add an API page?" },
    "db": { "default": "sqlite", "choices": ["sqlite", "none"], "description": "Where data is kept" },
    "port": { "default": 8080, "description": "The port kscript serve uses" }
  },
  "next": ["kscript serve   http://localhost:{{ port }}", "{{#if api}}kscript serve, then open /api{{/if}}"]
}
```

## Installing source libraries

`kscript lib add` installs KScript source files from public GitHub repositories. For a bare name, KScript searches the default branches of its own `kylepantall/kscript` catalog, the trusted public repositories matching `orimovo/*` and `waggr-gb/*`, and repositories registered in your project. In each repository it checks `libraries/<name>/` first. If that folder is absent, the root `kscript.json` can declare a library with the requested name or point to the folder that declares it.

A single match installs immediately. If several distinct packages share the name, the CLI prints the number of matches and each qualified `owner/repo@library` name. Choose one and rerun `kscript lib add` with that name to install it. Each `owner/repo@library` appears once, even if its repository is both trusted and registered locally. A separate catalog that pins the library as a Git submodule has its own qualified name and can pin a different revision.

| Official library | What it provides | Runtime package it uses |
| --- | --- | --- |
| [`integer-math`](../libraries/integer-math/README.md) | Greatest common divisor, least common multiple and factorial | Core only |
| [`app-cache`](../libraries/app-cache/README.md) | Caches computed values using the project's cache store and expiry settings | `KScript.Cache` |
| [`request-id`](../libraries/request-id/README.md) | HTTP middleware that passes through a caller's request ID or generates one for the response | `KScript.Http` |
| [`isolink-connect`](https://github.com/waggr-gb/isolink-connect/blob/HEAD/libraries/isolink-connect/README.md) | Opens an Isolink desktop session and works with spaces and records | `KScript.Net` |

Core is available in every script. The other runtime packages in the table ship with KScript, and each library imports the one it uses. For example, install Isolink Connect by name.

```sh
kscript lib add isolink-connect
```

You can select a public repository under either trusted owner explicitly in a fresh project. For example, the Isolink SDK lives in `waggr-gb/isolink-connect`.

```sh
kscript lib add waggr-gb/isolink-connect@isolink-connect
```

A qualified install skips discovery across other publishers. Use it if discovery cannot finish or you are close to GitHub’s API limit. It still needs API quota to resolve the selected revision. If GitHub has exhausted the quota, retry after it resets.

The installer prints the import to use from the site root. SDK 0.3 uses the manifest entry `client.kscript` and the generic `@with_loopback_json` transport from `KScript.Net`.

```kscript
import "lib/isolink-connect/client.kscript" as connect
```

For a publisher outside the built-in catalog and trusted owners, register its specific repository in your project. You can then install by bare name or select that publisher directly. For example, if your team publishes a `slugs` library in `acme/kscript-libraries`, run both commands from the project.

```sh
kscript repo add acme/kscript-libraries
kscript lib add acme/kscript-libraries@slugs
```

The first command also accepts the repository's HTTPS URL. It records the registration in `.kscript/repos/` beside the nearest `kscript.json`, or in the current folder for standalone scripts. Commit the registration to share it with teammates. The second command downloads `libraries/slugs/` from the registered repository's default branch. A script in the site root can then use `import "lib/slugs/index.kscript" as slugs` with the default layout.

For a direct install, `kscript lib add owner/repo` or `kscript lib add https://github.com/owner/repo` starts at the repository root. If its `kscript.json` points to a library folder, the installer copies just that folder. Otherwise it installs the entire repository. A GitHub tree URL selects a branch, tag, commit, or folder within it, such as `kscript lib add https://github.com/owner/repo/tree/v1.0.0/libraries/slugs`. Direct installs work without registering a repository.

Named libraries install into `lib/<library-name>/`. Direct installs use `lib/<repository-name>/` when you select a repository or `lib/<folder-name>/` when you select a folder. Set `paths.lib` in the nearest `kscript.json` to choose another folder relative to `serve.root`. Outside a project, the command installs into `lib/` in the current folder. Imports are relative to the script containing them, so a script one level down would use `../lib/isolink-connect/client.kscript`.

The installer reads the selected folder's `kscript.json`, validates its library metadata, and prints an import for `library.entry`. For older libraries without a `library` section, it still prefers a root `index.kscript` or `index.ks.xml`. The generated `.kscript-lib.json` receipt inside the installed folder records the source, exact commit and entry path, plus the name, version and runtime packages when declared.

In VS Code, completions inside the project's `packages` array show which runtime packages the installed, pinned libraries require. Add a package there when you also want it available throughout your project. Completions under `types` use the running KScript version's built-in types and accepted options, such as `types.password.min` and `types.uuid.v`.

### Restoring a project's libraries

`kscript lib add` records the source URL and full commit under `libraries` in the consuming project's `kscript.json`. If there is no project yet, a successful add creates this file in the current directory. Commit it so teammates and CI can restore the same revisions after cloning.

A named Isolink install records the selected publisher folder and commit. For example, a project pinned to one revision looks like this.

```json
{
  "libraries": {
    "isolink-connect": {
      "source": "https://github.com/waggr-gb/isolink-connect/tree/HEAD/libraries/isolink-connect",
      "commit": "d12745f9989a9523fcb33922f75625f0c1a96766"
    }
  }
}
```

On a fresh checkout, run the following command from the project or one of its subdirectories.

```sh
kscript lib restore
```

Restore reads the pins from the nearest project and fetches each library directly from its recorded publisher at the exact commit. For the example above, it fetches `libraries/isolink-connect/` from `waggr-gb/isolink-connect`. A direct repository install records the repository URL and commit. Restore follows the library folder reference from that commit. If a team catalog publishes a Git submodule, the recorded catalog commit also fixes the upstream commit. An existing copy at the recorded revision keeps its local edits. To share those edits, commit the modified library files or publish the changes upstream. The installer uses the empty `.kscript/libraries.lock` file to coordinate simultaneous adds. You can ignore it in Git.

### Publishing a named library

On the default branch of your public repository, put each library in an ordinary folder under `libraries/` or pin a separate public GitHub repository there as a Git submodule. Both layouts publish source directly. For an ordinary folder, include the entry script, any scripts it imports by path, and any data it reads. The `acme/kscript-libraries@slugs` example could contain these files.

```text
libraries/
  slugs/
    kscript.json
    index.kscript
    README.md
```

Put the manifest beside the public source. For example, `libraries/slugs/kscript.json` can declare a Core-only library.

```json
{
  "name": "slugs",
  "library": { "version": "1.0.0", "entry": "index.kscript" },
  "packages": []
}
```

`library.version` is a semantic version. `library.entry` is a .kscript or .ks.xml path relative to this file and can point to a nested module. List required native runtime packages such as `KScript.Net` under `packages`, and import them in the source modules that use them. Declare public functions in the entry module. Users can then install your published library by its qualified name.

A repository that publishes a single library can place the same manifest and entry script at its root. Set `name` to the library name users should install. For example, a root manifest named `slugs` in the registered repository `acme/slug-tools` can be installed as `kscript lib add acme/slug-tools@slugs`.

A repository with tests or examples alongside the SDK can point its root `kscript.json` to the package folder. For example, Connect can keep its test settings in the following root manifest.

```json
{
  "library": "libraries/isolink-connect",
  "test": { "include": ["tests/**/*.test.kscript"] }
}
```

The path is relative to this manifest. The target folder must contain its own `kscript.json` declaring a library name, `library.version` and `library.entry`. Declare any runtime packages there, too. Both `kscript lib add waggr-gb/isolink-connect` and `kscript lib add waggr-gb/isolink-connect@isolink-connect` then install that folder and print the same import. The repository's tests and examples stay in the checkout.

To publish `slugs` from a separate repository, run this in your library catalog and commit both `.gitmodules` and `libraries/slugs`.

```sh
git submodule add https://github.com/acme/slug-tools.git libraries/slugs
```

`kscript lib add acme/kscript-libraries@slugs` downloads the upstream commit pinned by the catalog. If the upstream contains `libraries/slugs/`, only that folder is installed, so other SDKs can share the repository. Otherwise the installer uses the upstream root. Keep all scripts and assets in the selected folder. The installer follows one submodule level, and records both repositories' commits in `.kscript-lib.json`. To publish an update, commit the new submodule pin in the catalog.

## Project settings: kscript.json

`kscript serve`, `kscript check`, `kscript fmt`, `kscript make` and the editor read the nearest `kscript.json` in the folder they work on or a parent folder, so they behave the same from anywhere in the project. Every setting is optional:

```jsonc
{
  "name": "my-site",
  "standard": "kscript:recommended",               // lint rules and format settings to start from (see Standards)
  "packages": ["KScript.Terminal", "KScript.IO"],   // imported by every script in the project
  "extensions": ["extensions", "shared/**/*.kscript"], // where the project's extend files are (default: extensions/)
  "natives": ["natives"], // where the project's native types are, used without an import (default: natives/)
  "serve": {
    "root": ".",          // the site folder, relative to kscript.json
    "port": 8080,         // --port and --host on the command line still win
    "host": "localhost",
    "docs": true          // also serve the docs, with a page about this project (see docs/web.md)
  },
  "http": {
    "middleware": ["log"],                     // routes, middleware and their settings (see docs/http.md)
    "groups": { "api": ["cors", "throttle"] }
  },
  "lint": {
    "include": ["**/*.html", "**/*.md", "**/*.kscript"],  // what `kscript check` looks at
    "exclude": ["_templates", "node_modules", "drafts/**"], // never checked, on the command line or in the editor
    "rules": { "KS3003": "off", "KS4*": "error" },          // off, hint, info, warning or error, by code or prefix
    "warnings_as_errors": false                               // same as kscript check --warnings-as-errors
  },
  "test": {
    "include": ["tests/**/*.test.kscript"],  // what `kscript test` runs (default: **/*.test.kscript and **/*.test.ks.xml)
    "exclude": ["tests/slow"],
    "timeout": 10,                          // seconds per test, 0 for no limit
    "packages": ["KScript.Terminal"],       // imported by test files only
    "env": "test"                           // the config function receives this environment during tests
  },
  "format": {
    "indent": 2,           // spaces per level, or "tab"; used by kscript fmt and Format Document
    "print_width": 80,      // the longest a line should be; longer lines are wrapped
    "rules": { "statement_per_line": false },  // any rule can be turned off
    "exclude": ["vendor"]  // see docs/formatting.md for every format setting
  },
  "app": {
    "main": "app.kscript",      // what `kscript compile` builds (see Programs)
    "name": "My App",           // shown in the Dock, Start menu or launcher (default: the project's name)
    "id": "com.example.myapp",  // the macOS bundle id
    "version": "1.0.0",
    "icon": "icon.png"          // a .png, .ico or .icns, used for every platform
  },
  "types": {
    "uuid": { "v": 4 },                     // arguments for every use of a built-in type (see Types and validation)
    "password": { "min": 12 }
  },
  "config": {
    "environment": "development",           // used when neither --env nor KSCRIPT_ENV names one
    "files": ["config/config.kscript", ".env?"], // the sources $config reads
    "prefix": "APP_",                       // APP_DATABASE__HOST overrides database.host
    "root": "settings",                     // where the files are, relative to kscript.json (default: the project folder)
    "required": ["database.host"],          // scripts stop with a clear message when one is missing
    "user": true                            // also read config.json from the app's config folder (KScript.Storage)
  }
}
```

- A pattern without a `/` (such as `_templates` or `*.draft.html`) matches a file or folder of that name anywhere. Otherwise it is matched against the path from `kscript.json`: `*` stays within a folder, `**` spans folders, and a folder matches everything in it.
- `standard` picks the [standard](standards.md) the project's `lint` and `format` settings start from: `kscript:recommended` (the default), `kscript:strict`, or a standard file of your own that you can share between projects. Anything set in `kscript.json` itself wins over the standard. `kscript check --print-config` shows the result and where each setting came from.
- `rules` changes how serious a diagnostic is (see [the codes](language-server.md#diagnostic-codes)). The longest match wins, so `"KS3*": "warning"` with `"KS3001": "error"` keeps unknown elements as errors.
- Comments and trailing commas are allowed. Unknown settings are reported as warnings rather than ignored silently, and the VS Code extension validates the file and completes its settings as you type.
- `format` sets the style for `kscript fmt` and the editor's Format Document, and `format.rules` turns individual rules off. See [Formatting](formatting.md) for every setting and the ignore comments.
- Inside a project, `kscript make page about` creates the page in the site root even when run from a subfolder.
- `kscript serve` never serves `kscript.json`, or the `config.*`, `.yaml`, `.yml` and `.env` files that `$config` reads.
- `http` configures route files, middleware, CORS, auth and rate limits for KScript.Http (see [Routes, controllers and middleware](http.md#settings-kscriptjson)).
- `types` gives built-in types such as `uuid`, `email` and `password` the same arguments everywhere in the project. Arguments written where a type is used, such as `uuid(v: 8)`, win (see [Types and validation](types.md#project-defaults-in-kscriptjson)).
- `app.name` (or `name`) also names the app's own folders for settings, data and caches (see [App folders](storage.md)).
- `queue` names the connections jobs wait on, the queues a worker checks and how long failed jobs are kept (see [Queues and jobs](queues.md#connections-and-drivers)).
- `app` is read by `kscript compile`, which builds the script (often a desktop app) into a program for macOS, Windows and Linux (see [Programs](compile.md#names-icons-and-versions)).

## Generating files: kscript make

`kscript make <type> <name>` creates a file where the project keeps that kind of file, makes any folder it needs, and writes scripts in the project's format. [kscript make commands](/cli/make) lists every type with its options and examples, and so does `kscript make --list`; `kscript make <type> --help` shows one.

```sh
kscript make route users                              # adds Get(path: /users, to: @users.index) to routes/web.kscript,
                                                      # and makes controllers/users.kscript with index()
kscript make route "users/{id}" --where "id: int"     # Get(…, to: @users.show), and adds show(id) to the controller
kscript make route users --method post --api          # Post(…, to: @users.store) in routes/api.kscript, served under /api
kscript make route people --resource --api            # Resource(path: /people, to: people), with all five functions
kscript make route about --view about                 # Get(path: /about, view: "about"), and views/about.html
kscript make controller people --resource             # controllers/people.kscript: index, show, store, update, destroy
kscript make request NewPerson --fields "name: string, age?: int"   # requests/new-person.kscript, a typed body
kscript make middleware secure-headers                # middleware/secure-headers.kscript
kscript make view people/show --vars person           # views/people/show.html, for @view('people/show', person: $p)
kscript make component Hero --props title,sub-title   # _components/hero.kscript, used as <hero title="…" sub-title="…" />
kscript make page about --title "About us"            # about.html, wrapped in _layout.html (created if missing)
kscript make page "people/[id]"                       # people/[id].html, with $id read from the URL
kscript make css theme --primary "#3355ff"            # styles/theme.ks.css, served as /styles/theme.css
kscript make type Repo --fields "name: string, stars: int from stargazers_count"   # types/repo.kscript
kscript make enum Size --values small,medium,large    # types/size.kscript
kscript make native Sku --pattern "/^[A-Z]{3}-\d{4}$/"   # natives/sku.kscript: works in every script without an import
kscript make module pricing --fns subtotal,total      # lib/pricing.kscript
kscript make window settings                          # settings.kscript, a desktop window
kscript make document annual-report                   # documents/annual-report.kscript, a PDF and Word document
kscript make config staging                           # config/config.staging.kscript
kscript make test checkout                            # tests/checkout.test.kscript
kscript make error PaymentFailed --status 402 --fields "order: int"   # errors/payment-failed.kscript
kscript make extension date --methods json,is_weekend # extensions/date.kscript: extend date { fn json() … }
kscript make auth --api                               # sign up, sign in, resets and API tokens (see Sign-in)
kscript make                                          # list every type
```

Leave out the name at a terminal and `kscript make` asks for it: `kscript make page` asks what the page should be called and then, because no options were given either, asks about each of the page's options. A yes/no option such as `--timestamps` is a yes/no question, and pressing Enter skips any other option. Give any option and only the name is asked. With `-n` (`--no-interaction`), or when the input or output isn't a terminal, nothing is asked and a missing name is an error.

The files it makes have the functions each kind needs already written, with a `// TODO:` comment (`<!-- TODO: -->` in HTML) saying what to put in them. A controller's `show(id)` says to find the record and answer 404 when there is none, and a job's `handle()` says to do the work there. The files pass `kscript check` and `kscript fmt --check` as they are.

A route goes in `routes/web.kscript`, or `routes/api.kscript` with `--api`. A project that already keeps `routes.kscript` or `api.kscript` at the top, or lists its route files in `http.routes`, gets the route there instead. The route file imports the controller, and the route is refused if the file already has one for the same method and path. `struct` and `record` are other names for `type`, and `fn` and `function` for `module`: there is a generator for every kind of thing a script can declare (functions, constants, enums and types).

An option's value is checked before anything is written. `kscript make job Send --tries abc`, `make error E --status abc`, `make document d --template nope` and `make route "a b/{id}"` each stop with a message saying what the value should be, and exit with 2. `kscript make project` does the same for an option its template doesn't have, and lists the ones it has. `make document --template` takes `paper`, `report`, `plain`, `letter` or `invoice`.

### Where files go: the recommended layout

`kscript make` puts each kind of file in its own folder, and makes the folder the first time it is needed, so a project only has the folders it uses:

```text
my-app/
  kscript.json     settings, including "paths"
  Makefile
  .env             secrets (make env); never served
  routes/          web.kscript (served at /) and api.kscript (served at /api)
  controllers/     make controller, make route
  middleware/      make middleware
  requests/        make request
  views/           make view
  _components/     make component (folders starting with _ are never served)
  _layouts/        make layout (the default _layout.html stays at the top)
  _partials/       make partial
  styles/          make css
  lib/             make module, const, cache, storage and logger
  types/           make type, make enum
  models/          make model
  natives/         make native: types every script gets without an import
  extensions/      make extension: methods every script gets (extend date { … })
  errors/          make error
  jobs/            make job
  documents/       make document
  config/          make config
  locales/         make locale
  data/            JSON that stores and caches write
  scripts/         make script
  tests/           make test
```

**One type per file.** Each type, model, enum and error type has a file of its own, named after it (`OrderStatus` in `types/order-status.kscript`), and the files that use it import it:

```kscript
// models/order.kscript
import KScript.Data
import "../types/order-status.kscript"

model Order {
  status: OrderStatus = 'open'
}
```

The generators and project templates all work this way, and in a project `kscript check` warns (KS6008) about a file that declares a second type, saying where to move it. A script outside a project, run on its own, can keep its types with it.

Two things are found without an import. Native types in `natives/` work in every script (they are part of the language, like `email`). And a model's field can name another model in the models folder (`paths.models`) without importing it, because two models that link to each other (`author: User` in one, `posts: Post[]` in the other) would otherwise import each other, and circular imports aren't allowed. That lookup is only for the types of model fields: a controller or script that uses `User` still imports `models/user.kscript`.

Pages stay where they are, because a page's path is its URL. Desktop windows (`make window`) go at the top, next to `desktop.kscript`. Move any folder with `paths` in `kscript.json`:

```jsonc
{
  "paths": { "controllers": "app/controllers", "styles": "assets/css", "views": "app/views" }
}
```

The names are `routes`, `controllers`, `middleware`, `requests`, `views`, `components`, `layouts`, `partials`, `styles`, `lib`, `types`, `models`, `errors`, `storage`, `jobs`, `windows`, `config`, `locales`, `logging`, `data`, `scripts` and `tests`. `paths.views` is also where `@view()` looks, unless `http.views` says otherwise. `kscript serve` finds `routes/web.kscript` and `routes/api.kscript` by itself; a route file anywhere else needs listing in `http.routes`. List settings files in `config.files`. Paths are relative to the project folder or to `config.root` when set. Pages look in `paths.components`, which defaults to `_components/`.

Names can be written any way (`HeroBanner`, `hero-banner`, `hero_banner`) and files are named in kebab case. Options: `--dir <project>` (default: the project's site root inside a project, otherwise the current folder), `--force` to replace an existing file, `--dry-run` to print the file instead of writing it. Every other `--key value` (or `--key=value`, or a bare `--flag` for `true`) is passed to the template.

Scripts and tests are written as `.kscript` files. `--syntax xml` writes `.ks.xml` instead, and `"make": { "syntax": "xml" }` in `kscript.json` makes that the project's default.

<a id="your-own-templates"></a>**Your own templates.** Templates live in the project's `_templates/` folder, which `kscript serve` never serves. `kscript make templates` copies the built-in ones there to edit. A file there replaces the built-in template of the same name (`_templates/component.kscript`), and any other file adds a type: `_templates/card.html` gives `kscript make card Profile`.

```html
--- make
description: A card with an optional image
to: _components/{{ kebab }}-card.html
hint: Use it in a page: <{{ kebab }}-card />
---
<article class="card {{ variant ?? plain }}">
{{#if image}}
  <img src="{$image}" alt="">
{{/if}}
{{#each props}}
  <p>{${{ item | var }}}</p>
{{/each}}
  <slot />
</article>
```

- The `--- make` header is optional: `to` is where the file goes (default: the name with the template's extension), `hint` is printed afterwards, `description` shows in `kscript make`. `category`, `usage`, `example:` lines (as many as you like) and `--option: what it does` lines fill in its entry in `kscript make --list`, and `defaults: actions=index` gives options a default. `checks: tries=int; level=debug|info|warn` refuses a value that would not work before anything is written: a list of choices, or `int`, `number`, `status`, `duration`, `durations`, `fields`, `names`, `assignments`, `locale` or `file` (a file in the project); `name=` checks the name itself. `base: project` makes `to` relative to the project folder (where `kscript.json` is) rather than the site root, as the `test` template does.
- `{{ name }}` is the name as typed; `{{ kebab }}`, `{{ snake }}`, `{{ pascal }}`, `{{ camel }}` and `{{ title }}` are its other spellings; `{{ dir }}` is its folder (`blog/` for `blog/first-post`), `{{ path }}` is folder and kebab name, `{{ url }}` its page URL, and `{{ params }}` lists `[id]` segments. `{{ paths.controllers }}` and the other `paths` are the project's folders, `{{ up }}` leads from the new file back to the project folder (`../` for `lib/x.kscript`), and `{{ ext }}` is `.kscript` or `.ks.xml`.
- `{{ value | kebab }}` applies a filter (`kebab snake pascal camel title var lower upper trim`; `var` turns `sub-title` into the `sub_title` a component sees), and `{{ value ?? fallback }}` gives a default.
- `{{#if value}} … {{else}} … {{/if}}`, `{{#unless value}}`, `{{#if value is text}}`, `{{#if value in a, b}}`, `{{#if list has text}}`, and `{{#each value}} … {{ item }} … {{/each}}` over a comma-separated list (`{{ index }}`, `{{ first }}` and `{{ last }}` are set). A block tag on its own line leaves no blank line behind.
- Single braces are left alone, so KScript's `{$value}` is written as is.

## Exit codes

Every `kscript` command ends with one of these, so a CI job can tell "the checks failed" from "the command was wrong":

| Code | Meaning |
| --- | --- |
| 0 | Everything worked. |
| 1 | The command ran and found problems: `kscript check` found errors (or warnings, with `--warnings-as-errors`), tests failed, or a `--check` (`fmt`, `agents`, `watch`) found something out of date. |
| 2 | The command line or the settings are wrong: an unknown command or option, a missing value, a file or folder that is not there, or a `kscript.json` or standard that can't be used. |
| 3 | A script stopped with an error (or the code the script gave to `exit`). |
| 70 | An error inside `kscript` itself, also when it happens while a script runs. |
| 130 | Stopped with Ctrl+C. `kscript serve` and `kscript watch` run until they are stopped, so Ctrl+C is how they finish and they end with 0. |

Options are written one way on every command: `--output text|json|yaml` chooses how results are printed (`check`, `queue`, `openapi`), and `--syntax kscript|xml` chooses the script syntax (`make`, `docs`, `mcp`, `convert`). Flags are kebab-case (`--warnings-as-errors`, `--stop-when-empty`), and settings in `kscript.json` are snake_case (`warnings_as_errors`). `--env name`, `--ansi`, `--no-ansi` and `--debug` work after any command (`kscript worker --env staging`); for a script they go before its name (`kscript --env staging deploy.kscript`, or `kscript run --env staging deploy.kscript`), because what follows the name is the script's `$args`.
