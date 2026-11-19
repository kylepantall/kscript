# KScript

[![CI](https://github.com/kylepantall/kscript/actions/workflows/ci.yml/badge.svg)](https://github.com/kylepantall/kscript/actions/workflows/ci.yml)

KScript is a scripting language written in XML. A KScript document is parsed into a tree of objects and run by a small .NET 8 interpreter on Windows, Linux and macOS. `kscript serve` also runs a folder of HTML and Markdown templates as a website.

KScript is still in active development, and its syntax may change between versions.

## A first script

```xml
<kscript dynamic_defs="yes">
  <!-- With dynamic_defs, variables are created the first time they are used -->

  <!-- Ask a question and store the answer in 'entered_name' -->
  <input to="entered_name" type="text">What's your name?</input>

  <!-- Trim the answer, capitalise it and print it -->
  <echo>Hello, $entered_name->trim()->upper()!</echo>
</kscript>
```

Add `print_info="yes"` to the `<kscript>` tag to print extra information while the script runs.

More examples live in [`Example Scripts/`](Example%20Scripts).

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

Command line options:

| Option | Description |
| --- | --- |
| `-s` | Quiet: don't print the script timing, even if the script sets `quiet="no"`. |
| `-nowait` | Don't wait for a key press when the script finishes (never waits when input is piped). |
| `-cai` | Clear the console after each input. |
| `-all` | Print information about all available KScript objects. |
| `-lang=<culture>` | Set the script language/culture. |
| `-guid` | Print a new GUID and exit. |
| `-root` | Open the KScript install directory. |
| `-admin` | Restart with administrator privileges (Windows only; use `sudo` elsewhere). |

Anything after the script path is available to the script through `@args()`.

## Editor support

Install the KScript extension into VS Code, Cursor, VSCodium or Windsurf:

```sh
kscript ide install           # every editor it finds; or name one: kscript ide install cursor
kscript ide                   # list the editors it found
kscript ide uninstall
```

The extension shows errors as you type and adds completion, hover, go to definition, rename and highlighting for scripts and `kscript serve` pages. It runs `kscript lsp` from your installed `kscript`, so keep that current with `kscript update`. To use a different `kscript`, set `kscript.server.path` in the editor's settings.

Other editors with LSP support work too: `kscript ide config neovim` (or `helix`, `sublime`, `emacs`) prints the settings to add. `kscript check <folder>` reports the same problems on the command line, for CI. See [docs/language-server.md](docs/language-server.md) for how the language server works.

## Repository layout

| Path | Contents |
| --- | --- |
| `KScript/` | The language: parser, interpreter, elements (`Arguments/`), `@commands` (`Commands/`), value methods, the `kscript serve` page engine (`Pages/`) and `kscript make` (`Scaffolding/`, built-in templates in `Scaffolding/Templates/`, the `kscript make project` starter in `Scaffolding/Project/`) and `kscript.json` (`Projects/`). |
| `KScriptApp/` | The `KScript` command line app that runs a script file. |
| `KScript.Tests/` | xUnit test suite. |
| `KScript.Language/`, `KScript.LanguageServer/` | The KScript 2 parser, analyzer and editor features, and the LSP server behind `kscript lsp` and `kscript check`. |
| `KScript.Language.Tests/` | Tests for the language tools, including contract tests that keep them in step with the interpreter. |
| `editors/vscode/` | The VS Code extension. |
| `samples/kscript2/` | KScript 2 sample scripts and site. |
| `Example Scripts/` | Sample scripts. |
| `Example Sites/team` | A sample `kscript serve` site. |

## Tests and CI

`KScript.Tests` is an xUnit project covering the command parser, commands, control flow, variables, value methods, pipes and lambdas, platform helpers, `kscript serve` pages, and the sample scripts. Run it with `dotnet test KScript.sln`. The tests capture console output, so they run one at a time rather than in parallel.

Every push to `master` and every pull request runs [the CI workflow](.github/workflows/ci.yml), which restores, builds and tests the solution in Release mode and then runs `Example Scripts/Find Oldest Person.xml` on three platforms:

- **Linux** runs on a self-hosted runner (`home-ci`). Pull requests from forks always use a GitHub-hosted `ubuntu-latest` runner instead, so untrusted code never runs on the self-hosted machine.
- **Windows** and **macOS** run on GitHub-hosted `windows-latest` and `macos-latest` runners.

## Elements

A script is a `<kscript>` document made of elements. The main groups are:

| Group | Elements |
| --- | --- |
| Output and input | `echo` `menu` `input` `ginput` `picker` `anykey` `clear` |
| Variables | `def` `defm` `update` `updateContent` `temp` `guard` |
| Control flow | `if` `else` `for` `forxy` `dowhile` `forever` `break` `continue` `end` `wait` `group` |
| Arrays | `array` `marray` `ainsert` `aremove` `minsert` `afor` `aecho` |
| Errors | `try` `catch` `throw` `onexception` |
| Methods | `method` `call` `run` `func` |
| Files and network | `file_info` `move` `new_dir` `write` `download` `loadHtml` |
| Other | `load` `ref` `thread` `watcher` `watch` `rule` `registry` `languages` `math` |

Run any script with `-all` (for example `dotnet run --project KScriptApp -- script.xml -all`) to print every element and command with its properties and examples.

## Command syntax

Commands are written `@name(argument, argument, ...)`. Arguments can be `'quoted strings'`, nested commands or plain text, and spaces around arguments are ignored:

```xml
<echo>@to_lower(@split('A,B,C', ',', '1'))</echo>   <!-- b -->
<echo>@math(2 * (3 + 4))</echo>                     <!-- 14 -->
```

An `@` that isn't followed by a name and `(` is plain text, so e-mail addresses and apostrophes in text are safe.

## Methods, pipes and lambdas

Any value (a variable, a `'quoted string'`, a number or a command's result) can have methods called on it with `->`. Calls can be chained and can take arguments:

```xml
<echo>$name->trim()->upper()</echo>
<echo>'a.b.c'->replace('.', '/')</echo>
<echo>3.14159->round(2)</echo>
<echo>@math('2+2')->mul(10)</echo>
<for to="i" while="$i->lt(10)" math="$i->increment()"> ... </for>
```

Values are typed on use: a value is a number if it parses as one, a bool if it is yes/no/true/false, and a string otherwise (`$x->type()` tells you which). Calling a number method on text gives a clear error. On a variable, `increment()`/`decrement()` also update the variable; on other values they just return the result.

| Kind | Methods |
| --- | --- |
| Any | `type` `length` `is_number` `is_bool` `is_empty` `has_value` `is_url` `equals(x, ignore_case?)` `default(x)` `call(args...)` |
| String | `upper` `lower` `trim` `trim_start` `trim_end` `reverse` `contains(x)` `starts_with(x)` `ends_with(x)` `index_of(x)` `replace(a, b)` `substring(start, len?)` `char_at(i)` `split(sep, i)` `count(x)` `repeat(n)` `pad_left(w, c?)` `pad_right(w, c?)` |
| Number | `add` `sub` `mul` `div` `mod` `pow` `round(d?)` `floor` `ceil` `abs` `min` `max` `lt` `lte` `mt` `mte` `between(a, b)` `is_zero` `not_zero` `positive` `negative` `increment(by?)` `decrement(by?)` |
| Bool | `not` `and(x)` `or(x)` `xor(x)` |

`|>` passes a value on to a method, a command (as its first argument) or a lambda. Lambdas are written `fn($x) => expression`:

```xml
<echo>$name |> trim() |> upper()</echo>
<echo>$csv |> @split(',', '1')</echo>
<echo>$name |> fn($s) => $s->lower()->reverse()</echo>
```

A lambda stored with `<def>` can be piped into or called later:

```xml
<def id="shout">fn($s) => $s->upper()->replace('.', '!')</def>
<echo>'hi.' |> $shout</echo>        <!-- HI! -->
<echo>$shout->call('hey.')</echo>   <!-- HEY! -->
```

A lambda body is one expression; use brackets to group: `fn($n) => ($n->mul(2) |> fn($m) => $m->add(1))`. Lambda parameters are only visible inside the lambda.

Variables are resolved after a line is parsed, so values containing `,`, `)` or `'` are always passed through intact and are never run as commands.

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
```

It creates `kscript.json`, `index.html`, `about.md`, `_layout.html`, `_components/site-nav.html`, `site.css`, `scripts/hello.ks.xml` and `.vscode/extensions.json`, which asks VS Code to install the KScript extension. `kscript make project .` sets up the current folder; existing files are never replaced without `--force`.

### Project settings: kscript.json

`kscript serve`, `kscript check`, `kscript make` and the editor read the nearest `kscript.json` in the folder they work on or a parent folder, so they behave the same from anywhere in the project. Every setting is optional:

```jsonc
{
  "name": "my-site",
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
kscript make page about --title "About us"            # about.html, wrapped in _layout.html
kscript make page "people/[id]"                       # people/[id].html, with $id read from the URL
kscript make markdown blog/first-post                 # blog/first-post.md with front matter
kscript make layout main --css /site.css              # _layouts/main.html
kscript make script tidy-downloads                    # tidy-downloads.xml
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

- The `--- make` header is optional: `to` is where the file goes (default: the name with the template's extension), `hint` is printed afterwards, `description` shows in `kscript make`.
- `{{ name }}` is the name as typed; `{{ kebab }}`, `{{ snake }}`, `{{ pascal }}`, `{{ camel }}` and `{{ title }}` are its other spellings; `{{ dir }}` is its folder (`blog/` for `blog/first-post`), `{{ path }}` is folder and kebab name, `{{ url }}` its page URL, and `{{ params }}` lists `[id]` segments.
- `{{ value | kebab }}` applies a filter (`kebab snake pascal camel title var lower upper trim`; `var` turns `sub-title` into the `sub_title` a component sees), and `{{ value ?? fallback }}` gives a default.
- `{{#if value}} … {{else}} … {{/if}}`, `{{#unless value}}`, and `{{#each value}} … {{ item }} … {{/each}}` over a comma-separated list (`{{ index }}`, `{{ first }}` and `{{ last }}` are set). A block tag on its own line leaves no blank line behind.
- Single braces are left alone, so KScript's `{$value}` is written as is.

## Writing portable scripts

- Build paths with `@join_path($dir, 'file.txt')` rather than hard-coding `\` or `/`.
- `@k('os.platform')` returns `windows`, `linux` or `macos`; `@k('path.separator')`, `@k('user.home')` and `@k('temp.directory')` are also available.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.
- `<registry>` and `@registry` only work on Windows and report a clear error elsewhere.

## Roadmap

Work in progress, not yet on `master`:

- A language server for editor support.
- A proposal for a cleaner KScript 2 syntax.

Further documentation is still in development.
