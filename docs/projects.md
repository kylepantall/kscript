# Projects and generators

## Starting a project: kscript make project

`kscript make project` sets up a new site with everything wired together:

```sh
kscript make project my-site --port 3000
cd my-site
kscript serve      # http://localhost:3000, from kscript.json
kscript check      # checks every page and script in the project
kscript test       # runs the tests in tests/
```

It creates `kscript.json`, `index.html`, `about.md`, `_layout.html`, `_components/site-nav.html`, `site.css`, `lib/greeting.ks.xml` (a function), `scripts/hello.ks.xml` (a script that uses it), `tests/greeting.test.ks.xml` (tests for both, run with `kscript test`) and `.vscode/extensions.json`, which asks VS Code to install the KScript extension. `kscript make project .` sets up the current folder; existing files are never replaced without `--force`.

## Project settings: kscript.json

`kscript serve`, `kscript check`, `kscript make` and the editor read the nearest `kscript.json` in the folder they work on or a parent folder, so they behave the same from anywhere in the project. Every setting is optional:

```jsonc
{
  "name": "my-site",
  "packages": ["KScript.Terminal", "KScript.IO"],   // imported by every script in the project
  "serve": {
    "root": ".",          // the site folder, relative to kscript.json
    "port": 8080,         // --port and --host on the command line still win
    "host": "localhost"
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
  }
}
```

- A pattern without a `/` (such as `_templates` or `*.draft.html`) matches a file or folder of that name anywhere. Otherwise it is matched against the path from `kscript.json`: `*` stays within a folder, `**` spans folders, and a folder matches everything in it.
- `rules` changes how serious a diagnostic is (see [the codes](language-server.md#diagnostic-codes)). The longest match wins, so `"KS3*": "warning"` with `"KS3001": "error"` keeps unknown elements as errors.
- Comments and trailing commas are allowed. Unknown settings are reported as warnings rather than ignored silently, and the VS Code extension validates the file and completes its settings as you type.
- Inside a project, `kscript make page about` creates the page in the site root even when run from a subfolder.
- `kscript serve` never serves `kscript.json`.

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

**Your own templates.** Templates live in the project's `_templates/` folder, which `kscript serve` never serves. `kscript make templates` copies the built-in ones there to edit. A file there replaces the built-in template of the same name (`_templates/component.html`), and any other file adds a type: `_templates/card.html` gives `kscript make card Profile`.

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
