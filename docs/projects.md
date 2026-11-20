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
| `index.html`, `about.md`, `_layout.html`, `_components/site-nav.html`, `site.css` | The site: two pages, the layout they share and a navigation component. |
| `tasks.html` | A to-do list in a [`<live>`](web.md#reactive-pages-with-live-no-javascript) block, with no JavaScript. |
| `lib/tasks.kscript` | The to-do rules (add, tick, remove, clear, count), shared by the page, the desktop app, the API and the tests. |
| `desktop.kscript` | The same to-do list as a [`<KScript.Window>`](desktop.md) desktop app. |
| `routes/api.kscript`, `controllers/tasks.kscript` | A JSON API for the tasks under `/api` ([KScript.Http](http.md)). While `kscript serve` runs, its docs are at `/_api` ([OpenAPI](http.md#api-docs-openapi)). |
| `config/config.yaml` | Settings every script and page reads as `$config` (`config.root` in `kscript.json` points there). |
| `lib/greeting.kscript`, `scripts/hello.kscript` | A function and a command-line script that uses it. |
| `tests/tasks.test.kscript`, `tests/greeting.test.kscript` | Tests for the rules and the script, run with `kscript test`. |
| `.vscode/extensions.json`, `.gitignore` | Asks VS Code to install the KScript extension; keeps built apps (`dist/`) out of git. |
| `AGENTS.md`, `CLAUDE.md`, `.kscript/agents/` | Docs for coding agents, made from the project's code and kept up to date by the editor (see [Coding agents](agents.md)). |

Scripts, tests, routes and the desktop app are written in the [.kscript format](code-format.md). `--format xml` writes them as XML (`.ks.xml`) instead, and records `"make": { "format": "xml" }` in `kscript.json` so that later `kscript make script` and `kscript make test` follow suit. Pages stay HTML either way.

`--no-desktop` leaves out `desktop.kscript` and the `app` section, for a web site only. `--no-http` leaves out the API. `kscript make project .` sets up the current folder; existing files are never replaced without `--force`. `kscript make project --help` lists the template's options.

### A bare project: --bare

`kscript make project my-tool --bare` makes just `kscript.json`, `config/config.yaml`, `main.kscript` (a script) and `tests/main.test.kscript`, plus the VS Code recommendation and the [agent docs](agents.md) (`AGENTS.md`): no pages, desktop app or API. Use it for command-line tools and scripts. `--format xml` works here too. `--bare` is short for `--template bare`; `kscript make project --help --bare` lists its options.

Add pieces to a bare project with options, in any mix:

```sh
kscript make project my-app --bare --web --api   # an empty website and JSON API
kscript make project my-api --bare --api         # just the API: routes/api.kscript, controllers/hello.kscript and its test
kscript make project my-app --bare --desktop     # a script plus an empty KScript.Window desktop app
```

`--web` adds `index.html`, `_layout.html` and `site.css`; `--api` adds `routes/api.kscript` (a `KScript.Routes` file served under `/api`), `controllers/hello.kscript` and `tests/hello.test.kscript`; `--desktop` adds `desktop.kscript` and the `app` section of `kscript.json`. `kscript.json` gets `serve` settings (and `--port`) whenever there is a website or API.

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

`--template` takes a folder or a template name. A name such as `bare` (or your own, such as `api`) is looked up as `project-<name>` in the same places as the default template, which lives in `project`, and then among the built-in templates (`web` and `bare`). `kscript make project` uses the first template it finds: the `--template` folder, then `_templates/project` in the project the new one is made inside (handy for a folder of several apps), then `~/.kscript/templates/project` (or `$KSCRIPT_TEMPLATES/project`), then the built-in one. Edit, add or delete files in the template folder to change what new projects get.

- Write scripts as `.kscript` files and refer to other scripts with `{{ ext }}` (`import "lib/tasks{{ ext }}"`). With `--format xml`, each `.kscript` file is converted to XML and named `.ks.xml`, and `{{ ext }}` is `.ks.xml`; `{{#if xml}}` tells the formats apart. A `.ks.xml` file in the template is copied as it is.
- Each file is a template with the same `{{ }}` placeholders as [`kscript make` templates](#your-own-templates): `{{ title }}` is the project name as a title (`My App`), `{{ kebab }}` is `my-app`, and every option is a value (`{{ port }}`, `{{#if desktop}} … {{/if}}`).
- A file can start with a `--- make` header. `when: desktop` copies it only when the option is on, `unless: desktop` only when it is off, and `to: docs/{{ kebab }}.md` writes it somewhere else.
- A name ending in `.stub` loses the ending: the template keeps `kscript.json.stub` so that editors and `kscript check` don't read it as a real project.
- `kscript-template.json` describes the template and is not copied. `options` lists each option with its `default` and `description` (an option that defaults to `true` is turned off with `--no-name`), and `next` holds the lines printed under "Next:", which can use placeholders too. An option the template doesn't list is reported, so a typo such as `--no-dekstop` doesn't go unnoticed.

```jsonc
// _templates/project/kscript-template.json
{
  "description": "Our standard web app",
  "options": {
    "desktop": { "default": true, "description": "Add the desktop app" },
    "api": { "default": false, "description": "Add an API page" },
    "port": { "default": 8080, "description": "The port kscript serve uses" }
  },
  "next": ["kscript serve   http://localhost:{{ port }}", "{{#if api}}kscript serve, then open /api{{/if}}"]
}
```

## Project settings: kscript.json

`kscript serve`, `kscript check`, `kscript fmt`, `kscript make` and the editor read the nearest `kscript.json` in the folder they work on or a parent folder, so they behave the same from anywhere in the project. Every setting is optional:

```jsonc
{
  "name": "my-site",
  "standard": "kscript:recommended",               // lint rules and format settings to start from (see Standards)
  "packages": ["KScript.Terminal", "KScript.IO"],   // imported by every script in the project
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
    "include": ["**/*.html", "**/*.md", "**/*.ks.xml"],   // what `kscript check` looks at
    "exclude": ["_templates", "node_modules", "drafts/**"], // never checked, on the command line or in the editor
    "rules": { "KS3003": "off", "KS4*": "error" },          // off, hint, info, warning or error, by code or prefix
    "warningsAsErrors": false                               // same as kscript check --warnaserror
  },
  "test": {
    "include": ["tests/**/*.test.kscript"],  // what `kscript test` runs (default: **/*.test.kscript, **/*.test.ks.xml and **/*.test.xml)
    "exclude": ["tests/slow"],
    "timeout": 10,                          // seconds per test, 0 for no limit
    "packages": ["KScript.Terminal"]        // imported by test files only
  },
  "format": {
    "indent": 2,           // spaces per level, or "tab"; used by kscript fmt and Format Document
    "printWidth": 120,     // start tags past this column get one attribute per line
    "rules": { "statementPerLine": false },  // any rule can be turned off
    "exclude": ["vendor"]  // see docs/formatting.md for every format setting
  },
  "app": {
    "main": "app.ks.xml",       // what `kscript compile` builds (see Programs)
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
    "files": ["config", ".env"],            // the base files $config reads (the default)
    "prefix": "APP_",                       // APP_DATABASE__HOST overrides database.host
    "root": "settings",                     // where the files are, relative to kscript.json (default: each script's folder)
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
- `app` is read by `kscript compile`, which builds the script (often a desktop app) into a program for macOS, Windows and Linux (see [Programs](compile.md#names-icons-and-versions)).

## Generating files: kscript make

`kscript make <type> <name>` creates a file where the project keeps that kind of file, makes any folder it needs, and writes scripts in the project's format. [kscript make commands](/cli/make) lists every type with its options and examples, and so does `kscript make --list`; `kscript make <type> --help` shows one.

```sh
kscript make route users                              # adds Get(path: "/users", to: users.index) to routes/web.kscript,
                                                      # and makes controllers/users.kscript with index()
kscript make route "users/{id}" --where "id: int"     # Get(…, to: users.show), and adds show(id) to the controller
kscript make route users --method post --api          # Post(…, to: users.store) in routes/api.kscript, served under /api
kscript make route people --resource --api            # Resource(path: "/people", to: people), with all five functions
kscript make route about --view about                 # Get(path: "/about", view: "about"), and views/about.html
kscript make controller people --resource             # controllers/people.kscript: index, show, store, update, destroy
kscript make request NewPerson --fields "name: string, age?: int"   # requests/new-person.kscript, a typed body
kscript make middleware secure-headers                # middleware/secure-headers.kscript
kscript make view people/show --vars person           # views/people/show.html, for @view('people/show', person: $p)
kscript make component Hero --props title,sub-title   # _components/hero.html, used as <hero title="…" sub-title="…" />
kscript make page about --title "About us"            # about.html, wrapped in _layout.html (created if missing)
kscript make page "people/[id]"                       # people/[id].html, with $id read from the URL
kscript make css theme --primary "#3355ff"            # styles/theme.ks.css, served as /styles/theme.css
kscript make type Repo --fields "name: string, stars: int from stargazers_count"   # types/repo.kscript
kscript make enum Size --values small,medium,large    # types/size.kscript
kscript make module pricing --fns subtotal,total      # lib/pricing.kscript
kscript make window settings                          # settings.kscript, a desktop window
kscript make document annual-report                   # documents/annual-report.kscript, a PDF and Word document
kscript make config staging                           # config/config.staging.yaml
kscript make test checkout                            # tests/checkout.test.kscript
kscript make error PaymentFailed --status 402 --fields "order: int"   # errors/payment-failed.kscript
kscript make auth --api                               # sign up, sign in, resets and API tokens (see Sign-in)
kscript make                                          # list every type
```

A route goes in `routes/web.kscript`, or `routes/api.kscript` with `--api`. A project that already keeps `routes.kscript` or `api.kscript` at the top, or lists its route files in `http.routes`, gets the route there instead. The route file imports the controller, and the route is refused if the file already has one for the same method and path. `struct` and `record` are other names for `type`, and `fn` and `function` for `module`: there is a generator for every kind of thing a script can declare (functions, constants, enums and types).

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
  errors/          make error
  jobs/            make job
  documents/       make document
  config/          make config
  locales/         make locale
  data/            JSON that stores and caches write
  scripts/         make script
  tests/           make test
```

Pages stay where they are, because a page's path is its URL. Desktop windows (`make window`) go at the top, next to `desktop.kscript`. Move any folder with `paths` in `kscript.json`:

```jsonc
{
  "paths": { "controllers": "app/controllers", "styles": "assets/css", "views": "app/views" }
}
```

The names are `routes`, `controllers`, `middleware`, `requests`, `views`, `components`, `layouts`, `partials`, `styles`, `lib`, `types`, `storage`, `jobs`, `windows`, `config`, `locales`, `logging`, `data`, `scripts` and `tests`. `paths.views` is also where `@view()` looks, unless `http.views` says otherwise. `kscript serve` finds `routes/web.kscript` and `routes/api.kscript` by itself; a route file anywhere else needs listing in `http.routes`. Settings files go where `config.root` points, because that's where `$config` reads them. Without `config.root`, they go at the top of the project, unless it already has a `config/` folder. Pages only find components in `_components/`.

`kscript make:component Hero` works too. Names can be written any way (`HeroBanner`, `hero-banner`, `hero_banner`) and files are named in kebab case. Options: `--dir <project>` (default: the project's site root inside a project, otherwise the current folder), `--force` to replace an existing file, `--dry-run` to print the file instead of writing it. Every other `--key value` (or `--key=value`, or a bare `--flag` for `true`) is passed to the template.

Scripts and tests are written as `.kscript` files. `--format xml` writes `.ks.xml` instead, and `"make": { "format": "xml" }` in `kscript.json` makes that the project's default.

<a id="your-own-templates"></a>**Your own templates.** Templates live in the project's `_templates/` folder, which `kscript serve` never serves. `kscript make templates` copies the built-in ones there to edit. A file there replaces the built-in template of the same name (`_templates/component.html`), and any other file adds a type: `_templates/card.html` gives `kscript make card Profile`.

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

- The `--- make` header is optional: `to` is where the file goes (default: the name with the template's extension), `hint` is printed afterwards, `description` shows in `kscript make`. `category`, `usage`, `example:` lines (as many as you like) and `--option: what it does` lines fill in its entry in `kscript make --list`, and `defaults: actions=index` gives options a default. `base: project` makes `to` relative to the project folder (where `kscript.json` is) rather than the site root, as the `test` template does.
- `{{ name }}` is the name as typed; `{{ kebab }}`, `{{ snake }}`, `{{ pascal }}`, `{{ camel }}` and `{{ title }}` are its other spellings; `{{ dir }}` is its folder (`blog/` for `blog/first-post`), `{{ path }}` is folder and kebab name, `{{ url }}` its page URL, and `{{ params }}` lists `[id]` segments. `{{ paths.controllers }}` and the other `paths` are the project's folders, `{{ up }}` leads from the new file back to the project folder (`../` for `lib/x.kscript`), and `{{ ext }}` is `.kscript` or `.ks.xml`.
- `{{ value | kebab }}` applies a filter (`kebab snake pascal camel title var lower upper trim`; `var` turns `sub-title` into the `sub_title` a component sees), and `{{ value ?? fallback }}` gives a default.
- `{{#if value}} … {{else}} … {{/if}}`, `{{#unless value}}`, `{{#if value is text}}`, `{{#if value in a, b}}`, `{{#if list has text}}`, and `{{#each value}} … {{ item }} … {{/each}}` over a comma-separated list (`{{ index }}`, `{{ first }}` and `{{ last }}` are set). A block tag on its own line leaves no blank line behind.
- Single braces are left alone, so KScript's `{$value}` is written as is.
