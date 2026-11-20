# Projects and generators

## Starting a project: kscript make project

`kscript make project` sets up a new web app, with the same app as a desktop window, and everything wired together:

```sh
kscript make project my-app --port 3000
cd my-app
kscript serve            # http://localhost:3000, from kscript.json
kscript desktop.ks.xml   # the desktop app, in a window
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
| `lib/tasks.ks.xml` | The to-do rules (add, tick, remove, clear, count), shared by the page, the desktop app and the tests. |
| `desktop.ks.xml` | The same to-do list as a [`<KScript.Window>`](desktop.md) desktop app. |
| `lib/greeting.ks.xml`, `scripts/hello.ks.xml` | A function and a command-line script that uses it. |
| `tests/tasks.test.ks.xml`, `tests/greeting.test.ks.xml` | Tests for the rules and the script, run with `kscript test`. |
| `.vscode/extensions.json`, `.gitignore` | Asks VS Code to install the KScript extension; keeps built apps (`dist/`) out of git. |

`--no-desktop` leaves out `desktop.ks.xml` and the `app` section, for a web site only. `kscript make project .` sets up the current folder; existing files are never replaced without `--force`. `kscript make project --help` lists the template's options.

### Changing what new projects get

Every file `kscript make project` writes comes from a project template, and you can change it:

```sh
kscript make templates project            # outside a project: copies it to ~/.kscript/templates/project
kscript make templates project --global   # the same, from inside a project
kscript make templates project            # inside a project: copies it to _templates/project
kscript make project my-app --template ../starters/api   # any folder
```

`kscript make project` uses the first template it finds: the `--template` folder, then `_templates/project` in the project the new one is made inside (handy for a folder of several apps), then `~/.kscript/templates/project` (or `$KSCRIPT_TEMPLATES/project`), then the built-in one. Edit, add or delete files in the template folder to change what new projects get.

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
    "host": "localhost"
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
    "include": ["tests/**/*.test.ks.xml"],  // what `kscript test` runs (default: **/*.test.ks.xml and **/*.test.xml)
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
  "config": {
    "environment": "development",           // used when neither --env nor KSCRIPT_ENV names one
    "files": ["config", ".env"],            // the base files $config reads (the default)
    "prefix": "APP_",                       // APP_DATABASE__HOST overrides database.host
    "root": "settings",                     // where the files are, relative to kscript.json (default: each script's folder)
    "required": ["database.host"]           // scripts stop with a clear message when one is missing
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
- `app` is read by `kscript compile`, which builds the script (often a desktop app) into a program for macOS, Windows and Linux (see [Programs](compile.md#names-icons-and-versions)).

## Generating files: kscript make

`kscript make` creates components, pages and scripts from templates, so a new file starts out right:

```sh
kscript make component Hero --props title,sub-title   # _components/hero.html, used as <hero title="…" sub-title="…" />
kscript make page about --title "About us"            # about.html, wrapped in _layout.html (created if missing)
kscript make page "people/[id]"                       # people/[id].html, with $id read from the URL
kscript make markdown blog/first-post                 # blog/first-post.md with front matter
kscript make layout main --css /site.css              # _layouts/main.html
kscript make script tidy-downloads                    # tidy-downloads.xml
kscript make test checkout                            # tests/checkout.test.ks.xml, in the project folder
kscript make                                          # list every template
```

`kscript make:component Hero` works too. Names can be written any way (`HeroBanner`, `hero-banner`, `hero_banner`) and files are named in kebab case. Options: `--dir <project>` (default: the project's site root inside a project, otherwise the current folder), `--force` to replace an existing file, `--dry-run` to print the file instead of writing it. Every other `--key value` (or `--key=value`, or a bare `--flag` for `true`) is passed to the template.

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

- The `--- make` header is optional: `to` is where the file goes (default: the name with the template's extension), `hint` is printed afterwards, `description` shows in `kscript make`. `base: project` makes `to` relative to the project folder (where `kscript.json` is) rather than the site root, as the `test` template does.
- `{{ name }}` is the name as typed; `{{ kebab }}`, `{{ snake }}`, `{{ pascal }}`, `{{ camel }}` and `{{ title }}` are its other spellings; `{{ dir }}` is its folder (`blog/` for `blog/first-post`), `{{ path }}` is folder and kebab name, `{{ url }}` its page URL, and `{{ params }}` lists `[id]` segments.
- `{{ value | kebab }}` applies a filter (`kebab snake pascal camel title var lower upper trim`; `var` turns `sub-title` into the `sub_title` a component sees), and `{{ value ?? fallback }}` gives a default.
- `{{#if value}} … {{else}} … {{/if}}`, `{{#unless value}}`, and `{{#each value}} … {{ item }} … {{/each}}` over a comma-separated list (`{{ index }}`, `{{ first }}` and `{{ last }}` are set). A block tag on its own line leaves no blank line behind.
- Single braces are left alone, so KScript's `{$value}` is written as is.
