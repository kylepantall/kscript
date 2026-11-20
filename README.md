# KScript

[![CI](https://github.com/kylepantall/kscript/actions/workflows/ci.yml/badge.svg)](https://github.com/kylepantall/kscript/actions/workflows/ci.yml)

**One readable language for your scripts, your websites and your desktop apps.**

KScript is an open source language that looks like the HTML you already know. The same elements, expressions and functions run a terminal script, render a web page, update that page live as people click and type, and draw native desktop windows, all from one small binary on Windows, macOS and Linux. There is no JavaScript to write, no build step, no `node_modules` and no virtual environment.

```xml
<kscript>
  <import package="KScript.Terminal" />

  <input to="name">What's your name? </input>
  <echo>Hello, { $name->trim()->title() }!</echo>
</kscript>
```

```sh
kscript hello.ks.xml
```

## Why KScript

- **Learn it once, use it everywhere.** Scripts and web pages run on the same interpreter, so a function you write for a script works in a page unchanged. Import it with `<import src="lib/text.ks.xml" />` and call it.
- **Interactive pages with no JavaScript.** Put a `<live>` block in a page and its buttons, inputs and forms run KScript handlers like `on-click="count += 1"` on the server. Only that block is redrawn. You write no JavaScript and no API, and there is no build step.
- **Mistakes show up before anything runs.** Unknown elements, bad attributes, missing imports and syntax errors are reported before the first line runs, so a typo near the end can't leave a job half done. The errors say what to write instead.
- **An editor that understands your code.** The VS Code extension knows the type of every value, so `$person.` offers that person's fields and `$name->` offers string methods, including what your own functions return.
- **Batteries included, loaded only when used.** Files, HTTP, maths, Markdown, terminal prompts and web pages come as packages. A script imports the ones it needs, and a terminal script never loads the web server.
- **One binary, every OS.** Install a single file that has the .NET runtime built in. The same script behaves the same on all three platforms, down to how numbers are printed.
- **Everything else you need is built in.** `kscript make` scaffolds projects and files, `kscript test` runs your tests, `kscript check` lints for CI, `kscript fmt` tidies your files, and `kscript serve` runs your site.

## Scripts that read like what they do

Find everything over 1 MB in a folder, list it largest first, and offer to move it into an archive:

```xml
<kscript>
  <import package="KScript.IO" />
  <import package="KScript.Terminal" />

  <let name="folder" value="$args[0] ?? @known_folder(downloads)" />
  <let name="big" value="@files($folder, pattern: '*', recursive)
      |> filter(fn($f) => $f->size() gt 1000000)
      |> sort_by(fn($f) => -$f->size())" />

  <for each="f" in="$big">
    <echo>{ @bold($f->name()) }  { ($f->size() / 1000000)->round(digits: 1) } MB</echo>
  </for>
  <else>
    <echo>Nothing over 1 MB in {$folder}.</echo>
  </else>

  <if test="$big->count() gt 0">
    <confirm to="ok">Move them to an archive folder?</confirm>
    <if test="$ok">
      <for each="f" in="$big">
        <move from="$f" to="@join_path(base: $folder, ['archive', $f->name()])" />
      </for>
    </if>
  </if>
</kscript>
```

You get real values (strings, numbers, lists, maps, dates, files), methods on all of them with `->`, pipes with `|>`, lambdas with `fn($x) => …`, `try`/`catch`, and functions you call like commands. Calling an API takes one line too:

```xml
<import package="KScript.Net" />
<let name="repo" value="@fetch('https://api.github.com/repos/kylepantall/kscript')->check()->json()" />
<echo>{$repo.full_name} has {$repo.stargazers_count} stars</echo>
```

Declare the shape of the data once and the response is checked and turned into records your editor knows the fields of. Every mismatch is reported at once, with its JSON path:

```xml
<type name="Repo">
  <field name="name" type="string" />
  <field name="stars" type="int" from="stargazers_count" />
  <field name="owner" type="string" from="owner.login" />
</type>
<let name="repo" value="@fetch($url)->check()->json(as: Repo)" />
<echo>{$repo.name} by {$repo.owner} has {$repo.stars} stars</echo>
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

```xml
<import package="KScript.Math" as="math" />    <!-- then @math.sqrt(2) -->
<import src="lib/strings.ks.xml" />            <!-- your own functions -->
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
| `KScript.UI` | Native desktop windows, and `kscript app build` to package them |
| `KScript.AI` | Language models: `@llm`, `@chat` and `<llm>`. **Coming soon** |

If you use something without importing its package, the error and the editor both tell you which import to add. The editor can add it for you.

## Desktop apps, still just KScript

A script whose root is `<KScript.Window>` opens a native window on macOS, Windows or Linux. It works like `<live>`: the `<let>` elements hold the state, the rest is the layout, and the window is redrawn after every handler.

```xml
<KScript.Window title="Counter" width="320" height="200">
  <let name="count" value="0" />
  <Text size="32">{$count}</Text>
  <Button variant="primary" on-click="count += 1">Add one</Button>
</KScript.Window>
```

```sh
kscript counter.ks.xml           # opens the window
kscript app build counter.ks.xml # packages it: Counter.app on a Mac, Counter/Counter.exe on Windows
```

There is no Swift, C#, XAML or shell script to write. [`samples/life-lab`](samples/life-lab) is Conway's Game of Life with one rules script shared by a terminal version, a web page and a desktop app.

## Tooling that comes with it

```sh
kscript make project my-site   # a site with a layout, a component, a script, tests and editor settings
kscript serve                  # run it
kscript test                   # run the tests in tests/
kscript check                  # report problems in every script and page, for CI
kscript fmt                    # tidy every script and page (--check for CI)
kscript ide install            # add the extension to VS Code, Cursor, VSCodium or Windsurf
kscript app build              # package a <KScript.Window> script as a desktop app
kscript update                 # get the latest release
```

The editor extension shows errors as you type and gives completion, hover docs, go to definition, rename, inlay hints for the types you didn't write, and notebook-style dividers between blocks. It reads the same package definitions as the interpreter, so the editor never disagrees with what runs. Neovim, Helix, Sublime Text and Emacs work through `kscript lsp`.

Tests are KScript too:

```xml
<test name="greets by name">
  <expect value="@greeting('Joan')" equals="'Hello, Joan!'" />
</test>
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

Then save the first example above as `hello.ks.xml`, run `kscript hello.ks.xml`, and run `kscript ide install` to set up your editor. [`samples/kscript2/`](samples/kscript2) and [`Example Sites/team`](Example%20Sites/team) have more to read and run.

## Documentation

- [Installing KScript](docs/install.md): installers, updating, manual downloads and editor setup
- [The language](docs/language.md): values, expressions, control flow, functions, errors and every package
- [Web pages and `<live>`](docs/web.md): `kscript serve`, layouts, components, routing and reactive blocks
- [Projects and generators](docs/projects.md): `kscript make project`, `kscript.json` and `kscript make`
- [Desktop apps](docs/desktop.md): `<KScript.Window>`, its elements, and `kscript app build`
- [Testing](docs/testing.md): `kscript test` and every check
- [Formatting](docs/formatting.md): `kscript fmt`, Format Document and the `format` settings
- [Language server](docs/language-server.md): how the editor features work, and the diagnostic codes
- [Working on KScript](docs/contributing.md): building from source, the repository layout and CI

KScript is open source and contributions are welcome. Start with [Working on KScript](docs/contributing.md).
