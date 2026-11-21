# KScript

[![CI](https://github.com/kylepantall/kscript/actions/workflows/ci.yml/badge.svg)](https://github.com/kylepantall/kscript/actions/workflows/ci.yml)

**One readable language for your scripts, your websites and your desktop apps.**

KScript is an open source language for scripts, web pages and desktop apps. You write it in the short `.kscript` format, or as XML that looks like the HTML you already know; both run the same. The same statements, expressions and functions run a terminal script, render a web page, update that page live as people click and type, and draw native desktop windows, all from one small binary on Windows, macOS and Linux. There is no JavaScript to write, no build step, no `node_modules` and no virtual environment.

```kscript
import KScript.Terminal

input(to: name) "What's your name? "
echo "Hello, { $name->trim()->title() }!"
```

```sh
kscript hello.kscript
```

## Why KScript

- **Learn it once, use it everywhere.** Scripts and web pages run on the same interpreter, so a function you write for a script works in a page unchanged. Import it with `import "lib/text.kscript"` and call it.
- **Interactive pages with no JavaScript.** Put a `<live>` block in a page and its buttons, inputs and forms run KScript handlers like `on-click="count += 1"` on the server. Only that block is redrawn. You write no JavaScript and no API, and there is no build step.
- **Mistakes show up before anything runs.** Unknown elements, bad attributes, missing imports and syntax errors are reported before the first line runs, so a typo near the end can't leave a job half done. The errors say what to write instead.
- **An editor that understands your code.** The VS Code extension knows the type of every value, so `$person.` offers that person's fields and `$name->` offers string methods, including what your own functions return.
- **Batteries included, loaded only when used.** Files, HTTP, maths, Markdown, terminal prompts and web pages come as packages. A script imports the ones it needs, and a terminal script never loads the web server.
- **One binary, every OS.** Install a native program that needs no .NET and starts in milliseconds. The same script behaves the same on all three platforms, down to how numbers are printed.
- **Everything else you need is built in.** `kscript make` scaffolds projects and files, `kscript test` runs your tests, `kscript check` lints for CI, `kscript fmt` tidies your files, and `kscript serve` runs your site.

## Scripts that read like what they do

Find everything over 1 MB in a folder, list it largest first, and offer to move it into an archive:

```kscript
import KScript.IO
import KScript.Terminal

folder = $args[0] ?? @known_folder(downloads)
big = @files($folder, pattern: '*', recursive)
  |> filter(fn($f) => $f->size() gt 1000000)
  |> sort_by(fn($f) => -$f->size())

for f in $big {
  echo "{ @bold($f->name()) }  { ($f->size() / 1000000)->round(digits: 1) } MB"
} else {
  echo "Nothing over 1 MB in {$folder}."
}

if $big->count() gt 0 {
  confirm(to: ok) "Move them to an archive folder?"
  if $ok {
    for f in $big {
      move(from: $f, to: @join_path(base: $folder, ['archive', $f->name()]))
    }
  }
}
```

You get real values (strings, numbers, lists, maps, dates, files), methods on all of them with `->`, pipes with `|>`, lambdas with `fn($x) => …`, `try`/`catch`, and functions you call like commands. Calling an API takes one line too:

```kscript
import KScript.Net
repo = @api('https://api.github.com/repos/kylepantall/kscript')->check()->json()
echo "{$repo.full_name} has {$repo.stargazers_count} stars"
```

Declare the shape of the data once and the response is checked and turned into records your editor knows the fields of. Every mismatch is reported at once, with its JSON path:

```kscript
type Repo {
  name: string
  stars: int from stargazers_count
  owner: string from owner.login
}
repo = @api($url)->check()->json(as: Repo)
echo "{$repo.name} by {$repo.owner} has {$repo.stars} stars"
```

## Web pages that react without JavaScript

`kscript serve` turns a folder of `.html` and `.md` files into a website. KScript elements go straight into the HTML and `{ }` works in text and attributes. Add `<live>` and the page becomes interactive:

```html
<live id="todos">
  <state name="todos" value="[]" />
  <state name="draft" value="''" />

  <form on-submit="todos = $todos->push($draft); draft = ''">
    <input bind="draft" placeholder="What needs doing?">
    <button>Add</button>
  </form>

  <ul>
    <for each="t" in="$todos"><li>{$t}</li></for>
    <else><li>Nothing yet.</li></else>
  </ul>
  <p>{ $todos->count() } to do</p>
</live>
```

That's a working to-do list. The state travels with the page and is signed so the browser can't change it, so the server keeps nothing between requests. `kscript serve` adds its own small runtime to the page. Layouts, components, Markdown pages with front matter, dynamic routes like `people/[id].html` and JSON endpoints are all built in. Values are HTML-escaped by default.

```sh
kscript serve "Example Sites/team"    # then open http://localhost:8080/demo
```

## Packages: import what you use

```kscript
import KScript.Math as math // then @math.sqrt(2)
import "lib/strings.kscript" // your own functions
```

| Package | What it gives you |
| --- | --- |
| `KScript.Core` | The language itself, always there: variables, loops, `match`, functions, errors, strings, lists, maps, JSON and dates |
| `KScript.Terminal` | Prompts, menus, confirmations, file pickers, colours |
| `KScript.IO` | Files, folders and paths |
| `KScript.Net` | HTTP requests and downloads |
| `KScript.Math` | Maths, random numbers and statistics |
| `KScript.Markdown` | Markdown to HTML |
| `KScript.Web` | Pages, layouts, components and `<live>` blocks for `kscript serve` |
| `KScript.Test` | Tests for `kscript test` |
| `KScript.UI` | Native desktop windows, which `kscript compile` turns into Mac, Windows and Linux apps |
| `KScript.AI` | Language models: `@llm`, `@chat` and `llm`, with Claude |
| `KScript.Style` | Stylesheets written in KScript: themes from JSON, breakpoints, fluid sizes and colours |
| `KScript.Storage` | Settings, data, caches and logs in the right folders on macOS, Windows and Linux |
| `KScript.Cache` | A memory and file cache with time limits, and ETags and `Cache-Control` for routes |
| `KScript.Auth` | Sign-in for `kscript serve` apps: users, sessions, the `auth` and `guest` middleware, CSRF, password resets and API tokens |
| `KScript.Document` | Papers, reports, letters and invoices with LaTeX maths, tables, figures and citations, saved as PDF, Word, LaTeX, Markdown or HTML |

If you use something without importing its package, the error and the editor both tell you which import to add. The editor can add it for you.

## Desktop apps, still just KScript

A `KScript.Window { }` block opens a native window on macOS, Windows or Linux. It works like `<live>`: the variables hold the state, the rest is the layout, and the window is redrawn after every handler.

```kscript
KScript.Window(title: Counter, width: 320, height: 200) {
  count = 0
  Text(size: 32) "{$count}"
  Button(variant: primary, on-click: count += 1) "Add one"
}
```

```sh
kscript counter.kscript          # opens the window
kscript compile counter.kscript --target all   # Counter.app for Macs, Counter.exe for Windows, a Linux program
```

There is no Swift, C#, XAML or shell script to write, and the programs `kscript compile` builds run on computers without kscript. It works for terminal scripts and sites too, and keeps only the packages the script imports. [`samples/life-lab`](samples/life-lab) is Conway's Game of Life with one rules script shared by a terminal version, a web page and a desktop app.

## Tooling that comes with it

```sh
kscript make project my-app    # a web app and desktop app with a layout, a live page, scripts, tests and editor settings
kscript make project my-tool --bare   # just kscript.json, a script and a test
kscript serve                  # run it (--with-docs also serves the docs, with a page about your project)
kscript test                   # run the tests in tests/
kscript check                  # report problems in every script and page, for CI
kscript fmt                    # tidy every script and page (--check for CI)
kscript agents                 # AGENTS.md and compact docs for coding agents, made from your code
kscript watch                  # keep the agent docs (and other generated files) up to date as you code
kscript ide install            # add the extension to VS Code, Cursor, VSCodium or Windsurf
kscript compile --target all   # build programs for macOS, Windows and Linux that run without kscript
kscript update                 # get the latest release
kscript docs                   # open the docs in your browser, with examples you can run
```

The editor extension shows errors as you type and gives completion, hover docs, go to definition, rename, inlay hints for the types you didn't write, and notebook-style dividers between blocks. It reads the same package definitions as the interpreter, so the editor never disagrees with what runs. Neovim, Helix, Sublime Text and Emacs work through `kscript lsp`.

Tests are KScript too:

```kscript
test(name: greets by name) {
  expect(value: @greeting('Joan'), equals: 'Hello, Joan!')
}
```

## How it compares

| If you'd normally use | KScript gives you |
| --- | --- |
| Bash or PowerShell | One syntax on every OS, real lists and maps instead of string juggling, and errors caught before the script starts |
| Python or Node for small tools | Nothing to set up: no interpreter version, no `pip`, `venv` or `npm install`. One binary runs the script |
| PHP, Jinja or Liquid templates | The same language in templates and scripts, typed values, components, and live updates without writing JavaScript |
| React or Vue for a small interactive page | No bundler, no build, no client state to sync and no API to write. The handler runs where the data is |

**When KScript isn't the right fit (yet):** large applications that need a big library ecosystem, heavy number crunching, or rich client-side interactions that need to work offline. KScript is young, and it is aimed at the scripts, internal tools and small sites that usually end up spread across three languages.

## Get started

macOS and Linux:

```sh
curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/kylepantall/kscript/master/install.ps1 | iex
```

Then save the first example above as `hello.kscript`, run `kscript hello.kscript`, and run `kscript ide install` to set up your editor. [`samples/kscript2/`](samples/kscript2) and [`Example Sites/team`](Example%20Sites/team) have more to read and run.

## Documentation

Run `kscript docs` to open the full documentation in your browser. It is built into kscript, so it works offline and always matches your version: the guides below, a reference for every package, element, command and method, search, light and dark themes, and an editable **Run** box on each example. While you work on a site, `kscript serve --with-docs` serves them next to it, with a page about your own project: its conventions, routes, and the functions and types its scripts declare.

- [Getting started](docs/getting-started.md): a ten-minute tour, with examples to run
- [Installing KScript](docs/install.md): installers, updating, manual downloads and editor setup
- [The language](docs/language.md): values, expressions, control flow, functions, errors, settings, logging, translations, language models and every package
- [Web pages and `<live>`](docs/web.md): `kscript serve`, layouts, components, routing and reactive blocks
- [Stylesheets](docs/stylesheets.md): `.ks.css`, `.ks.scss` and `.ks.sass`, and `kscript css`
- [Routes, controllers and middleware](docs/http.md): KScript.Http route files, JSON APIs, views and middleware
- [Projects and generators](docs/projects.md): `kscript make project`, `kscript.json` and `kscript make`
- [Desktop apps](docs/desktop.md): `<KScript.Window>` and its elements
- [Programs](docs/compile.md): `kscript compile`, targets, icons and what a program holds
- [Testing](docs/testing.md): `kscript test` and every check
- [Formatting](docs/formatting.md): `kscript fmt`, Format Document and the `format` settings
- [Coding agents](docs/agents.md): `kscript agents`, AGENTS.md, agent doc tags and `kscript check --format json`
- [Standards](docs/standards.md): shareable lint and format standards, `kscript:recommended` and `kscript check --print-config`
- [Language server](docs/language-server.md): how the editor features work, and the diagnostic codes
- [Working on KScript](docs/contributing.md): building from source, the repository layout and CI
- [Roadmap](docs/roadmap.md): what's planned next, such as compiling KScript to TypeScript

KScript is open source and contributions are welcome. Start with [Working on KScript](docs/contributing.md).
