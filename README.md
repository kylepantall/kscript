# KScript

[![CI](https://github.com/kylepantall/kscript/actions/workflows/ci.yml/badge.svg)](https://github.com/kylepantall/kscript/actions/workflows/ci.yml)

KScript is a scripting language written in XML. Scripts run on a small .NET 8 interpreter on Windows, Linux and macOS, and `kscript serve` runs a folder of HTML and Markdown templates as a website.

This is KScript 2: expressions, `{ }` interpolation, typed values, lambdas and pipes, functions you can call like commands, and packages you import for what a script needs. Scripts written for KScript 1 don't run unchanged; the error for each old element or command names what to write instead.

## A first script

```xml
<kscript>
  <import package="KScript.Terminal" />

  <input to="name">What's your name? </input>
  <echo>Hello, { $name->trim()->title() }!</echo>
</kscript>
```

Save it as `hello.ks.xml` and run `kscript hello.ks.xml`. More examples live in [`Example Scripts/`](Example%20Scripts) and [`samples/kscript2/`](samples/kscript2).

## Building and running

KScript targets .NET 8 and runs on Windows, Linux and macOS. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download), then:

```sh
dotnet build KScript.sln
dotnet test KScript.sln

# Run a script
dotnet run --project KScriptApp -- "path/to/script.xml"
```

### Install (no .NET needed)

Linux and macOS:

```sh
curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/kylepantall/kscript/master/install.ps1 | iex
```

The scripts pick the right build for your OS and CPU, install it to `~/.kscript/bin` (Windows: `%LOCALAPPDATA%\Programs\kscript`) and add that folder to your PATH. On macOS they also clear the quarantine flag, since the binary isn't notarized. Set `KSCRIPT_VERSION=v0.1.0` to install a specific release, `KSCRIPT_INSTALL` to choose the folder, or `KSCRIPT_NO_MODIFY_PATH=1` to leave your PATH alone.

To update later:

```sh
kscript update                    # install the latest release if it's newer
kscript update --check            # only report whether there is one
kscript update --version v0.1.0   # install a specific release
kscript --version
```

`kscript update` replaces the binary in place. On Windows the running `kscript.exe` is renamed to `kscript.exe.old` and deleted the next time kscript starts. It uses `GITHUB_TOKEN` if set, to avoid GitHub's rate limit.

You can also download a build by hand from the [Releases page](https://github.com/kylepantall/kscript/releases): `linux-x64`, `linux-arm64`, `osx-arm64` (Apple Silicon), `osx-x64` and `win-x64`. Each is a single `kscript` file that includes the .NET runtime:

```sh
tar -xzf kscript-osx-arm64.tar.gz
xattr -d com.apple.quarantine kscript   # macOS only: the binary isn't notarized
./kscript serve "Example Sites/team"
```

To build one yourself from source:

```sh
dotnet publish KScriptApp -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out
```

Run a script with `kscript script.ks.xml [arguments]`. The arguments are in `$args`, a list. The exit code is 0, the code given to `<exit code="..."/>`, or 3 when the script stops with an error (printed as `file:line:column: Type: message`). Ctrl+C stops a script; `<finally>` blocks still run.

## Editor support

Install the KScript extension into VS Code, Cursor, VSCodium or Windsurf:

```sh
kscript ide install           # every editor it finds; or name one: kscript ide install cursor
kscript ide                   # list the editors it found
kscript ide uninstall
```

The extension shows errors as you type and adds completion, hover, go to definition, rename and highlighting for scripts and `kscript serve` pages. It works out types as it goes: `$person.` and `$name->` offer the fields and methods of that value (including what your own functions return), inlay hints show the types you didn't write, and thin lines divide a script into blocks like a notebook. It runs `kscript lsp` from your installed `kscript`, so keep that current with `kscript update`. To use a different `kscript`, set `kscript.server.path` in the editor's settings.

Other editors with LSP support work too: `kscript ide config neovim` (or `helix`, `sublime`, `emacs`) prints the settings to add. `kscript check <folder>` reports the same problems on the command line, for CI. See [docs/language-server.md](docs/language-server.md) for how the language server works.

## Repository layout

| Path | Contents |
| --- | --- |
| `KScript.Syntax/` | The parser shared by the interpreter and the editor tools: markup, expressions, `{ }` templates, and the metadata types elements and commands are described with. |
| `KScript.Core/` | The interpreter (`Runtime/`), the KScript.Core package (variables, control flow, functions, errors, `<echo>`, value methods), the package system and `kscript.json` (`Projects/`). |
| `KScript.Terminal/`, `KScript.Math/`, `KScript.IO/`, `KScript.Net/`, `KScript.Markdown/` | Packages: each registers its elements, commands and methods, with their documentation. |
| `KScript.Web/` | The `kscript serve` page engine (`Pages/`) and the KScript.Web package. |
| `KScript.Test/` | The KScript.Test package and `kscript test`: finding test files, running them, and the console and JUnit reports. |
| `KScript.Cli/` | `kscript make` (`Scaffolding/`, built-in templates in `Scaffolding/Templates/`, the `kscript make project` starter in `Scaffolding/Project/`), `kscript ide` and `kscript update`. |
| `KScriptApp/` | The `kscript` command line app. |
| `KScript.Language/`, `KScript.LanguageServer/` | The analyzer and editor features, and the LSP server behind `kscript lsp` and `kscript check`. They read what each package registers, so the editor always matches the interpreter. |
| `KScript.Tests/`, `KScript.Language.Tests/` | xUnit tests for the interpreter and packages, and for the language tools (including contract tests that keep them in step with the packages). |
| `editors/vscode/` | The VS Code extension. |
| `samples/kscript2/` | Sample scripts. |
| `Example Scripts/` | More sample scripts. |
| `Example Sites/team` | A sample `kscript serve` site. |

## Tests and CI

`KScript.Tests` covers the interpreter, every package, `kscript serve` pages, `kscript make`, `kscript.json` and the sample scripts; `KScript.Language.Tests` covers the analyzer, the editor features and the language server. Run them with `dotnet test KScript.sln`.

Every push to `master` and every pull request runs [the CI workflow](.github/workflows/ci.yml), which restores, builds and tests the solution in Release mode, runs `Example Scripts/Find Oldest Person.xml`, checks the sample scripts with `kscript check` and runs their KScript tests with `kscript test`, on three platforms:

- **Linux** runs on a self-hosted runner (`home-ci`). Pull requests from forks always use a GitHub-hosted `ubuntu-latest` runner instead, so untrusted code never runs on the self-hosted machine.
- **Windows** and **macOS** run on GitHub-hosted `windows-latest` and `macos-latest` runners.

## The language

### Variables and output

```xml
<let name="count" value="0" />                      <!-- declare -->
<let name="people">[{ name: 'Joan', age: 82 }]</let> <!-- longer values go in the text -->
<set name="count" value="$count + 1" />             <!-- change; fails if never declared -->
<set name="count" op="+=" value="1" />
<do>$total = $price * $qty; $count += 1</do>
<let name="limit" value="10" const="yes" />
<let name="n" value="$args[0]" type="int" />        <!-- convert, or stop with a clear error -->

<echo>Hello, {$name}! You have { $count } new { $count eq 1 ? 'message' : 'messages' }.</echo>
<echo>$person.name is $person.age</echo>           <!-- bare $name works in text too -->
<echo>$people</echo>                               <!-- a map or list of maps prints as indented JSON -->
```

A map, or a list of maps, on its own in `<echo>` prints as indented JSON, coloured in a terminal (unless `NO_COLOR` is set). Inside text it stays on one line. `<echo format="json">` reformats JSON text, and `->to_json()` gives compact JSON.

Variables belong to the block they are declared in. `{{` and `}}` are literal braces.

### Expressions

`+ - * / % **`, `== !=` (loose: `'3' == 3`), `=== !==` (strict), `lt le gt ge` (or `> >=`), `eq ne`, `and or not`, `??`, `?.`, `a ? b : c`, `in`, list `[1, 2]` and map `{ name: 'Joan' }` literals, `$p.name`, `$list[0]` (negative indexes count from the end), ranges `0..10` and `0..100 step 5` (end excluded), `match $x { 1 => 'one', _ => 'other' }`, `->method()`, `|>` and `fn($x) => …`. Use the word forms in attributes, since XML can't hold a raw `<` or `&&` there.

### Control flow

```xml
<if test="$n lt 10"> … </if>
<elif test="$n lt 100"> … </elif>
<else> … </else>

<for each="p" in="$people"> … </for>
<for each="i" index="n" in="0..10 step 2"> … </for>
<for each="key, value" in="$map"> … </for>
<while test="$queue->count() gt 0"> … <break /> … <continue /> … </while>

<match value="$choice">
  <case is="1"> … </case>
  <case is="2, 3"> … </case>
  <case when="$it gt 9"> … </case>
  <default> … </default>
</match>
```

### Functions

```xml
<fn name="greet" params="who, greeting = 'Hello'">
  <return value="$greeting + ', ' + $who + '!'" />
</fn>
<fn name="square" params="n: number" value="$n * $n" />

<echo>{ @greet('World') } { 4 |> @square() }</echo>
<call fn="greet" args="'Kyle'" />
<spawn fn="background_job" />
```

Functions can be called before they are declared, recurse, and be passed around: `@square` is the function itself. Lambdas are values too: `<let name="by_age" value="fn($a, $b) => $a.age - $b.age" />`.

### Values and methods

Values are strings, numbers, bools, lists, maps, dates, functions and null, plus values packages add (files, HTTP responses). Every value has methods, called with `->` and chained:

```xml
<echo>{ $name->trim()->upper() }</echo>
<echo>{ $people->filter(fn($p) => $p.age ge 18)->map(fn($p) => $p.name)->join(', ') }</echo>
<echo>{ $people |> sort_by(fn($p) => $p.name) |> first() }</echo>
<echo>{ @now()->add_days(7)->format('d MMMM yyyy') }</echo>
```

The editor completes the methods for each type and shows what they do. `@json(text)` and `$value->to_json()` convert to and from JSON.

### Errors

```xml
<try>
  <let name="data" value="@read_json($path)" />
  <catch as="err" when="$err.type eq 'FileNotFound'">
    <echo>No file at {$path}, using defaults.</echo>
  </catch>
  <catch as="err"><echo>Failed: {$err.message} (line {$err.line})</echo></catch>
  <finally><echo>done</echo></finally>
</try>
<throw type="Validation" message="Age must be positive, got {$age}" />
```

Unknown elements, attributes and syntax errors are reported before a script starts, so a typo near the end doesn't leave a job half done.

## Packages

KScript.Core (everything above) is always there. Everything else comes from a package, imported at the top of the script, so a script only loads what it uses: a terminal script never loads the web server.

```xml
<kscript>
  <import package="KScript.Terminal" />
  <import package="KScript.Math" as="math" />      <!-- commands stay under the prefix: @math.sqrt(2) -->
  <import src="lib/strings.ks.xml" as="str" />      <!-- another script's top-level <fn> and <let const> -->
  <import src="lib/strings.ks.xml" names="slugify" />
  …
</kscript>
```

| Package | What it adds |
| --- | --- |
| `KScript.Terminal` | `<input to type default secret>`, `<picker>` (a file or folder path), `<menu options>`, `<confirm>`, `<pause>`, `<clear>`, `@color(text, 'green')`, `@bold`, `@dim`, `@underline`, `<echo format="markdown">` |
| `KScript.Math` | `@sqrt` `@pow` `@round(x, digits)` `@clamp` `@log` `@sin` `@cos` `@atan2` `@hypot` `@gcd` `@factorial` `@random(low, high)` `@pick` `@shuffle` `@sum` `@average` `@median` `@stdev`, `$pi` `$e` |
| `KScript.IO` | `@read_text` `@read_lines` `@read_json` `@write_text` `@append_text` `@write_json` `@files(folder, '*.txt', recursive)` `@folders` `@file(path)` `@exists` `@join_path` `@known_folder('downloads')`, `<write>` `<move>` `<copy>` `<delete>` `<mkdir>`. File values have `->name()` `->extension()` `->size()` `->modified()`… |
| `KScript.Net` | `@fetch(url, { method, headers, body, timeout })` returning a response (`->status()` `->ok()` `->json()` `->text()` `->check()`), `<download url to>`, `@url_encode`, `@query_string` |
| `KScript.Markdown` | `@markdown(text)` and `@read_markdown(path)` (`html`, `title`, `meta` from front matter), `->markdown()` |
| `KScript.Web` | The elements of `kscript serve` pages (below). Pages don't need imports. |
| `KScript.Test` | `<test>`, `<describe>`, `<before-each>`, `<after-each>`, `<expect>`, `<assert>`, `<expect-error>`, `<capture>`, `@run_script`, `@assert`, `@fail`. Test files import it on their own (see [Testing scripts](#testing-scripts-kscript-test)). |

Using something from a package that isn't imported stops with `<input> is part of KScript.Terminal. Add <import package="KScript.Terminal" />`, and the editor offers to add the import. An element written with the `KScript.` prefix, such as `<KScript.input>`, imports the package that has it without an `<import>`. A project can import packages for every script in `kscript.json` (below).

## Web pages: kscript serve

`kscript serve` runs a folder as a website. Every `.html` and `.md` page is a template: KScript elements sit directly in the HTML, `{ expression }` works in text and attributes, and the visitor gets plain HTML.

```sh
kscript serve "Example Sites/team" --port 8080     # then open http://localhost:8080
```

```html
<!-- people.html -->
<let name="people" value="@read_json('data/people.json')" />
<let name="min" value="($request.query.min_age ?? '0')->to_number(0)" />

<layout src="_layout.html" title="People">
  <h1>People aged {$min} and over</h1>
  <ul>
    <for each="p" in="$people |> filter(fn($p) => $p.age ge $min) |> sort_by(fn($p) => $p.name)">
      <li class="{ $p.age ge 65 ? 'senior' : '' }">
        <a href="/people/{$p.id}">{$p.name}</a> ({$p.age})
      </li>
    </for>
    <else>
      <li>Nobody matches.</li>
    </else>
  </ul>
</layout>
```

| Element | Use |
| --- | --- |
| `<let name value>` / `<let>x = 3</let>` | Declare a variable in the current block. |
| `<set name value op>` / `<set>x += 1</set>` / `<do>$a = 1; $b += 2</do>` | Change existing variables. |
| `<if test>`, `<elif test>`, `<else>` | Branches. `<else>` after a `<for>` runs when the loop had nothing to loop over. |
| `<for each="p" in="$list" index="i">`, `<for each="key, value" in="$map">` | Loops. `$loop.index` and `$loop.first` are set too. |
| `<while test>` | Loop while true (stopped after 100,000 runs). |
| `<match value>` with `<case is="1, 2">`, `<case when="$it gt 9">`, `<default>` | Pick one branch. |
| `<layout src="_layout.html" title="…">` and `<slot />` | Wrap the page in a layout; extra attributes become variables. |
| `<my-card user="{$u}">`, `<hero>` | A component: `_components/my-card.html`, with `<slot />` for its children. Any element that isn't an HTML or KScript element can be a component. Components only see their own attributes. |
| `<include src="_part.html">` | Insert another template here. |
| `<markdown>` | Render Markdown, with `{ }` values escaped. |
| `<respond status json type header-x>` / `<redirect to>` | Send a different response, e.g. JSON or a 404. |

- **Expressions**: `+ - * / %`, `== !=` (loose: `'3' == 3`), `=== !==` (strict: type and value), `lt le gt ge` (or `> >=`), `and or not`, `??`, `?.`, `a ? b : c`, `in`, list `[1, 2]` and map `{ name: 'Joan' }` literals, `$p.name`, `$list[0]`, `->method()`, `|>` and `fn($x) => …`. Use the words `lt`, `le`, `and`, `or` in attributes, since XML can't hold a raw `<` or `&&` there.
- **Escaping**: `{ value }` is HTML-escaped (Markdown-escaped in `.md` pages); `{! value }` outputs trusted HTML; `{{` and `}}` are literal braces. An attribute that is exactly `{ value }` is left out when the value is `false` or `null`, so `checked="{$done}"` just works.
- **Markdown pages**: `.md` files can start with `---` front matter (`title: About`, `layout: _layout.html`); its keys become variables.
- **Routing**: `/people` serves `people.html`, `people.md` or `people/index.html`; `/people/42` can match `people/[id].html` (`$request.params.id`). Files and folders starting with `_` or `.` and KScript sources (`.xml`) are never served. `_404.html` is used for missing pages.
- **`$request`**: `method`, `path`, `query`, `form`, `headers`, `cookies`, `params` and `body`.
- **Commands**: `@read_json`, `@read_text`, `@read_markdown` (gives `html`, `title` and `meta`), `@files`, `@exists`, `@json`, `@markdown`, `@now`, `@today`, `@date`, `@range`, `@random`, `@guid`. File access is limited to the site folder.
- **Safety**: errors show the file and line; endless loops and runaway recursion are stopped.

See `Example Sites/team` for a complete site with a layout, a component, a Markdown page, a dynamic route and a JSON endpoint.

### Reactive pages with `<live>` (no JavaScript)

A `<live>` block reacts to clicks and typing without any JavaScript in the page. Its handlers are KScript statements; the server runs them and sends back just that block's new HTML.

```html
<live id="counter">
  <state name="count" value="0" />
  <p>{$count}</p>
  <button on-click="count -= 1">−</button>
  <button on-click="count += 1">+</button>
</live>

<live id="search">
  <state name="query" value="''" />
  <input bind="query" placeholder="Search…">
  <for each="p" in="$people->filter(fn($p) => $p.name->lower()->contains($query->lower()))">
    <li>{$p.name}</li>
  </for>
</live>
```

- `<state name value>` declares the block's state. It travels with the page, signed so the browser can't change it, so the server keeps nothing between requests.
- `on-click`, `on-change`, `on-input`, `on-submit`, `on-keydown` and `on-enter` run statements like `count += 1; draft = ''`. Inside a loop, the loop variable is available to the handler (`on-click="items = $items->reject(fn($i) => $i.id == $item.id)"`). `$event.value`, `$event.key` and `$event.checked` describe the browser event.
- `bind="name"` ties an input to a state variable both ways and redraws the block as you type.
- `kscript serve` adds its small runtime (`/_ks/live.js`) to pages that use `<live>`; pages never include it themselves. Set `KSCRIPT_SECRET` so pages keep working across server restarts.

See `/demo` in `Example Sites/team` for a counter, live search, a to-do list and a temperature converter.

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

### Project settings: kscript.json

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
- `rules` changes how serious a diagnostic is (see [the codes](docs/language-server.md#diagnostic-codes)). The longest match wins, so `"KS3*": "warning"` with `"KS3001": "error"` keeps unknown elements as errors.
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

## Testing scripts: kscript test

Tests are scripts named `name.test.ks.xml`, usually in a `tests/` folder. `kscript test` runs every one in the project, prints each result, and exits with 1 if any test failed, so it can gate CI.

```xml
<!-- tests/greeting.test.ks.xml -->
<kscript>
  <import src="../lib/greeting.ks.xml" />

  <describe name="greeting">
    <test name="says hello">
      <expect value="@greeting('Joan')" equals="'Hello, Joan!'" />
    </test>

    <test name="tidies '{$name}'" each="name" in="['  joan ', 'JOAN']">
      <expect value="@greeting($name)" equals="'Hello, Joan!'" />
    </test>
  </describe>

  <test name="hello.ks.xml greets the name it is given">
    <let name="run" value="@run_script('../scripts/hello.ks.xml', { args: ['ellie'] })" />
    <expect value="$run.output" equals="'Hello, Ellie!'" />
    <assert test="$run.code eq 0" />
  </test>
</kscript>
```

```sh
kscript test                            # every test in the project
kscript test tests/cart                 # a folder, or name one or more files
kscript test --filter "greeting › says" # tests whose full name contains the text
kscript test --junit test-results.xml   # a JUnit XML report for CI
kscript make test checkout              # start tests/checkout.test.ks.xml
```

- A file's top-level code (imports, variables, functions, `<describe>`) runs once; then each `<test>` runs on its own, so one failing test doesn't stop the others. `<before-each>` and `<after-each>` run around every test in their file or `<describe>`.
- `<expect value="…">` takes one check: `equals`, `not-equals`, `same` (`===`), `contains`, `not-contains`, `matches`, `type` or `count`. `<assert test="$total eq 30" />` shows the value of each side when it fails.
- `<expect-error type contains as>` expects its body to stop with an error, `<capture to input>` collects what its body prints (and answers its prompts), and `@run_script(path, { args, input })` runs a whole script and returns `{ output, code, error }`.
- `skip="reason"` and `only="yes"` go on a `<test>` or `<describe>`, and `timeout="30"` on a test (10 seconds by default).
- Test files get KScript.Test without an `<import>`, in the runner and in the editor. In VS Code, **KScript: Run Tests** runs them.

See [docs/testing.md](docs/testing.md) for every element, check and setting.

## Writing portable scripts

- Build paths with `@join_path($dir, 'file.txt')` (KScript.IO) rather than hard-coding `\` or `/`.
- `$os.name` is `windows`, `linux` or `macos`, and `$os.home`, `$os.temp` and `$os.separator` are there too; `$env.HOME` reads an environment variable.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.

## Roadmap

KScript 2 is landing in steps: pages running on the same interpreter as scripts, desktop windows (`KScript.UI`), and language models (`KScript.AI`).

