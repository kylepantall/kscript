# Web pages: kscript serve

`kscript serve` runs a folder as a website. Every `.html` and `.md` page is a template: KScript elements sit directly in the HTML, `{ expression }` works in text and attributes, and the visitor gets plain HTML.

```sh
kscript serve "Example Sites/team" --port 8080     # then open http://localhost:8080
```

```html
<!-- people.html -->
<let name="people" value="@read_json('data/people.json')" />
<let name="min" value="($request.query.min_age ?? '0')->to_number(default: 0)" />

<layout path="_layout.html" title="People">
  <h1>People aged {$min} and over</h1>
  <ul>
    <for each="p" in="$people |> filter(fn(p) => $p.age ge $min) |> sort_by(fn(p) => $p.name)">
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
| `<set name value op>` / `<set>x += 1</set>` / `<do>a = 1; b += 2</do>` | Change existing variables. |
| `<if test>`, `<elif test>`, `<else>` | Branches. `<else>` after a `<for>` runs when the loop had nothing to loop over. |
| `<for each="p" in="$list" at="i">`, `<for each="key, value" in="$map">` | Loops. `$loop.index` and `$loop.first` are set too. |
| `<while test>` | Loop while true (stopped after 100,000 runs). |
| `<match value>` with `<case is="1, 2">`, `<case when="$it gt 9">`, `<default>` | Pick one branch. |
| `<layout path="_layout.html" title="…">` and `<slot />` | Wrap the page in a layout; extra attributes become variables. |
| `<my-card user="{$u}">`, `<hero>` | A component: `_components/my-card.html`, with `<slot />` for its children. Any element that isn't an HTML or KScript element can be a component. Components only see their own attributes. |
| `<include path="_part.html">` | Insert another template here. |
| `<markdown>` | Render Markdown, with `{ }` values escaped. |
| `<respond status json redirect type header-x>` | Send a different response, e.g. JSON, a 404, or a redirect (`<respond redirect="/thanks" />`). |

- **Expressions**: `+ - * / %`, `== !=` (loose: `'3' == 3`), `=== !==` (strict: type and value), `lt le gt ge` (or `> >=`), `and or not`, `??`, `?.`, `a ? b : c`, `in`, list `[1, 2]` and map `{ name: 'Joan' }` literals, `$p.name`, `$list[0]`, `->method()`, `|>` and `fn(x) => …`. Use the words `lt`, `le`, `and`, `or` in attributes, since XML can't hold a raw `<` or `&&` there.
- **Escaping**: `{ value }` is HTML-escaped (Markdown-escaped in `.md` pages); `{! value }` outputs trusted HTML; `{{` and `}}` are literal braces. An attribute that is exactly `{ value }` is left out when the value is `false` or `null`, so `checked="{$done}"` just works.
- **Markdown pages**: `.md` files can start with `---` front matter (`title: About`, `layout: _layout.html`); its keys become variables.
- **Routing**: `/people` serves `people.html`, `people.md` or `people/index.html`; `/people/42` can match `people/[id].html` (`$request.params.id`). Files and folders starting with `_` or `.` and KScript sources (`.xml`) are never served. `_404.html` is used for missing pages.
- **`$request`**: `method`, `path`, `query`, `form`, `headers`, `cookies`, `params` and `body`.
- **Same language as scripts**: pages run on the KScript 2 interpreter, so every KScript.Core command and method works in them. Pages also get KScript.IO, KScript.Markdown and KScript.Web without importing them: `@read_json`, `@read_text`, `@read_markdown` (gives `html`, `title` and `meta`), `@files`, `@exists`, `@markdown`. Other packages are imported at the top of the page, as in a script: `<import package="KScript.Math" as="math" />` and then `{ @math.sqrt(2) }`.
- **Sharing code with scripts**: `<import path="lib/text.ks.xml" />` at the top of a page gives it that script's top-level `<fn>` functions and `<let const="true">` values. `as="text"` (then `@text.shout(...)`) and `names="shout"` work as they do in scripts.
- **Files**: paths start in the site folder and can't leave it, and pages can only read files, not write, move or delete them.
- **Safety**: errors show the file and line; endless loops and runaway recursion are stopped.

## Docs while you work: --with-docs

`kscript serve --with-docs` also serves these docs on localhost, next to the site, with a page about your project at `/project`:

```sh
kscript serve --with-docs
# Serving /home/me/shop on http://localhost:8080
# Docs for shop: http://localhost:4180/project
```

The project page is built from the project itself, and is rebuilt when a script changes:

- **How code is written here**: the project's format (`make.syntax`), standard, layout and lint rules, the packages every script gets, and the folders `kscript make` puts files in.
- **Routes**: what `kscript routes` lists, with what answers each route and its middleware.
- **Functions, types and constants**: everything each script declares at its top level, with its `///` doc comment (`@param`, `@returns`, `@example`, `@deprecated`), file by file. Search finds them too.

Examples show in the project's format, and the switch at the top shows them in the other. The docs listen on localhost only, even when the site is served with `--host 0.0.0.0`, because their examples run code. `--docs-port 4190` picks their port (4180, or a free one, otherwise).

For the same docs as a file, run `kscript docs --project`. It writes `.kscript/docs/project.md`, Markdown that reads well in an editor, on GitHub or in a code review. From then on `kscript watch`, `kscript serve` and saving a script in the editor keep it up to date. Delete `.kscript/docs` to stop.

To have them every time, set `"docs": true` in the `serve` section of kscript.json (and `"docs_port"` to pick the port); `--no-docs` turns them off for one run. The docs are for working on a site: compiled programs (`kscript compile`) never serve them, and neither does `kscript serve` in the `production` environment (`--env production`, or `KSCRIPT_ENV=production`).

For controllers, JSON APIs and middleware, add a route file: see [Routes, controllers and middleware](http.md). Routes are matched before pages, and anything they don't match is served as described here.

See `Example Sites/team` for a complete site with a layout, a component, a Markdown page, a dynamic route and a JSON endpoint.

## Reactive pages with `<live>` (no JavaScript)

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
  <for each="p" in="$people->filter(fn(p) => $p.name->lower()->contains($query->lower()))">
    <li>{$p.name}</li>
  </for>
</live>
```

- `<state name value>` declares the block's state. It travels with the page, signed so the browser can't change it, so the server keeps nothing between requests.
- `on-click`, `on-change`, `on-input`, `on-submit`, `on-keydown` and `on-enter` run statements like `count += 1; draft = ''`. Inside a loop, the loop variable is available to the handler (`on-click="items = $items->reject(fn(i) => $i.id == $item.id)"`). `$event.value`, `$event.key` and `$event.checked` describe the browser event.
- `on-tick="…" every="200ms"` runs a handler every 200 milliseconds for as long as that element is in the block, counted from when the last answer arrived. Put it in an `<if>` to start and stop it: `<if $playing><span on-tick="frame += 1" every="100"></span></if>`.
- `bind="name"` ties an input to a state variable both ways and redraws the block as you type.
- `kscript serve` adds its small runtime (`/_ks/live.js`) to pages that use `<live>`; pages never include it themselves. Set `KSCRIPT_SECRET` so pages keep working across server restarts.

See `/demo` in `Example Sites/team` for a counter, live search, a to-do list and a temperature converter, and [`samples/life-lab`](../samples/life-lab) for a game whose rules are a KScript script shared with a terminal version and a desktop app.
