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
    <for each="p" in="$people |> filter(fn(p) => $p.age ge $min) |> sort_by(.name)">
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
| `<my-card user="{$u}">`, `<hero>` | A component from `_components/my-card.kscript`, with `<slot />` for its children. The tag matches the component filename. |
| `<include path="_part.html">` | Insert another template here. |
| `<markdown>` | Render Markdown, with `{ }` values escaped. |
| `<respond status json redirect type header-x>` | Send a different response, e.g. JSON, a 404, or a redirect (`<respond redirect="/thanks" />`). |

- **Expressions**: `+ - * / %`, `== !=` (loose: `'3' == 3`), `=== !==` (strict: type and value), `lt le gt ge` (or `> >=`), `and or not`, `??`, `?.`, `a ? b : c`, `in`, list `[1, 2]` and map `{ name: 'Joan' }` literals, `$p.name`, `$list[0]`, `->method()`, `|>` and `fn(x) => …`. Use the words `lt`, `le`, `and`, `or` in attributes, since XML can't hold a raw `<` or `&&` there.
- **Escaping**: `{ value }` is HTML-escaped (Markdown-escaped in `.md` pages); `{! value }` outputs trusted HTML; `{{` and `}}` are literal braces. An attribute that is exactly `{ value }` is left out when the value is `false` or `null`, so `checked="{$done}"` just works.
- **Markdown pages**: `.md` files can start with `---` front matter (`title: About`, `layout: _layout.html`); its keys become variables. Inline code and fenced code blocks keep their contents literal.
- **Routing**: `/people` serves `people.html`, `people.md` or `people/index.html`; `/people/42` can match `people/[id].html` (`$request.params.id`). Files and folders starting with `_` or `.` and KScript sources (`.kscript`, `.ks.xml` and any other `.xml`) are never served. `_404.html` is used for missing pages, and can use layouts, components and live blocks like any page.
- **`$request`** contains `method`, `scheme`, `path`, `url`, `query`, `form`, `headers`, `cookies`, `params` and `body`. For `https://example.com/people?page=2`, `$request.scheme` is `https`, `$request.path` is `/people` and `$request.url` is `/people?page=2`. A locale-prefixed page also receives `$request.locale`. A value in `query` or `form` is text; a name sent more than once (`?tag=a&tag=b`, a group of checkboxes) is a list of texts. For an ordinary form post `body` is the text as sent (`name=Ada&tag=a`); for a form with files it is empty.
- **Same language as scripts**: pages run on the KScript 2 interpreter, so every KScript.Core command and method works in them. Pages also get KScript.IO, KScript.Markdown and KScript.Web without importing them: `@read_json`, `@read_text`, `@read_markdown` (gives `html`, `title` and `meta`), `@files`, `@exists`, `@markdown`. Other packages are imported at the top of the page, as in a script: `<import package="KScript.Math" as="math" />` and then `{ @math.sqrt(2) }`.
- **Sharing code with scripts**: `<import path="lib/text.kscript" />` at the top of a page gives it that script's top-level functions and `const` values. `as="text"` (then `@text.shout(...)`) and `names="shout"` work as they do in scripts.
- **Files**: paths start in the site folder and can't leave it, and page rendering can only read files. A live action in a controller view can write when `http.write_files` permits it.
- **Safety**: errors show the file and line; endless loops and runaway recursion are stopped. In the production environment (`kscript serve --env production`) a visitor gets a plain `500` page instead, and the message, file and line go to the server's log only.
- **What can be sent**: a status from 100 to 999, and header values in plain ASCII without line breaks. Anything else is an error page that says which value was wrong, so a visitor's text put in a header can't add a header of its own. A `redirect` to an address with other characters (`/café`) is sent percent-encoded.

During development, open the site in a browser and save changes to a page, component, script, stylesheet, or asset. Open HTML pages reload automatically. Use `kscript serve --no-reload` to turn it off. Production and compiled sites serve pages without a browser reload script.

For a containerised development site, add a Compose file and run `kscript serve`. Use `--no-docker` to serve on the host. The bind-mounted source lets the container see edits. Set `serve.docker` in `kscript.json` to make Docker the default. See [Running the development site in Docker](projects.md#running-the-development-site-in-docker).

## Model forms

Create a component for an existing model, or create the two files together.

```sh
kscript make component NoteCard --model Note --fields title,body   # a component that shows a Note
kscript make model Note --fields "title: string, body: string" --component   # a model, and a component for it
```

The first command writes `_components/note-card.kscript`. A page renders it with `<note-card record="{$note}" />`, where `$note` is a model row.

````kscript
import KScript.Web
import "../models/note.kscript"

type NoteCard extends Component {
  record: Note
  heading: string => $.record.title

  fn render() {
    return ```html
    <article>
      <h2>{$.heading}</h2>
      <p>{$.record.body}</p>
      <slot />
    </article>
    ```
  }
}
````

The tag passes attributes into fields, which `render()` reads as `$record` or `$.record`. Computed fields such as `$.heading`, local constants, and methods called with `$this->method()` are available to the HTML. The editor completes props on the tag and model fields inside the fence. `Component` requires `fn render()`. Existing HTML and Markdown components also work, and the editor recognises `$record` as an input when a legacy template uses it.

A component can save edits directly to a [KScript.Data model](data.md). Declare the writable fields and the ownership rule once in the web routes. For example, a `Profile` model in `models/profile.kscript` holds a `user_id` and a `bio`. Its `bio` field can declare a length limit such as `string(max: 500)`.

```kscript
import KScript.Http

Routes {
  ModelUpdate(name: edit_profile, model: Profile, owner: user_id, fields: [bio])
}
```

Generate the reusable editor after declaring the binding.

```sh
kscript make component ProfileEditor --model-update edit_profile
```

Pass the owned row as `<profile-editor record="{$profile}" />`. The generated form has a Save button. Add `--autosave` to the command to save after typing pauses. For a component with its own interactive state, start with `kscript make component OrderDetails --live`.

A protected GET controller loads the signed-in user's row and passes it to the page. Render `<profile-editor record="{$data.profile}" />` to use the [profile component](../samples/auth-app/_components/profile-editor.kscript). Its stable live ID identifies the record during a redraw. The scaffold generates a `<live>` wrapper for `--live` and `--model-update`.

**Save on click.** Omit `autosave` for a manual editor. Typing leaves the database untouched until the user chooses Save. In a live block, Save updates the model in place and a status message confirms the write. A standalone form uses a native POST and redirects back to the page. Native submission also works with JavaScript disabled.

**Save while typing.** Add `autosave` for a 500 ms pause, or choose a delay such as `autosave="750ms"`. The example binds this setting to a checkbox. Save submits pending edits immediately in either mode. The runtime sends edited fields, waits for each save to finish, and keeps the draft when validation fails. Live redraws preserve matching editable controls and their caret. Opening the saved bio preview runs the page's protected GET controller again and reads the stored value.

KScript generates the POST handler and includes a CSRF token. Every save checks the session and token, matches the row's `user_id` against the signed-in user's ID, and validates the submitted fields against the model and the binding's allowlist. Use `owner: id` when the row's key is the account ID. Add route middleware to `ModelUpdate` for project-specific permissions. Apply ownership when reading the row as well. Keep custom SQLite files outside the public site or in an underscore-prefixed folder. The [auth sample](../samples/auth-app/README.md) includes this editor and its migration.

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

## Template props and component scripts

Declare required inputs at the top level of a layout or component.

```html
<!-- _layout.html -->
<prop name="page-title" required />
<prop name="subtitle" />
<h1>{$page_title}</h1>
<slot />
```

Call the layout with `<layout path="_layout.html" page-title="Home">…</layout>`. The editor suggests the declared props and reports missing required values for a known layout path. Rendering checks required props even for layout paths computed at runtime. Markdown pages pass layout props in front matter. Optional props default to `null` when omitted.

Place `note.kscript` next to `_components/note.html`. The component template can call the companion's exported functions. An optional `load(props: map)` function receives the props and supplies `$data` to the template. The template can also read `$props` and its individual prop variables.

```kscript
fn load(props: map) => { title: $props.title->trim() }
fn heading(title: string) => $title->upper()
```

```html
<!-- _components/note.html -->
<prop name="title" required />
<article>{@heading($data.title)}<slot /></article>
```

```html
<Note title="A note" onLoad={fn(data) => @note_loaded($data)}>
  <p>Content supplied by the page</p>
</Note>
```

`onLoad` is an optional server-side callback defined in the calling page. It receives the result of `load`, or `null` when the component has no load function. Loading can run again when the component renders, including during live redraws, so keep render-time hooks free of persistent writes.

## Reactive pages with `<live>` (no JavaScript)

A `<live>` block reacts to clicks and typing without any JavaScript in the page. Its handlers are KScript statements; the server runs them and sends back just that block's new HTML.

Reusable components become live automatically when their templates use state, event handlers, bindings or live directives. Use a `<live>` block in a page, or when a component needs to customize its live root.

```html
<live id="counter">
  <state name="count" value="0" />
  <p>{$count}</p>
  <button on-click="decrement: $count">−</button>
  <button on-click="increment: $count">+</button>
</live>

<live id="search">
  <state name="query" value="''" />
  <input @bind($query) placeholder="Search…">
  <li @each($people->filter(fn(p) => $p.name->lower()->contains($query->lower())), as: p)>{$p.name}</li>
</live>
```

- `<state name value>` declares the block's state. It travels with the page, signed so the browser can't change it, so the server can restore the component from the browser's next request. A signed state is not used up: the browser can send the same one again (the back button, a second tab), so a handler that must run once checks that for itself, for example against the database.
- A live block calls back by asking for its page again with `GET`. A view shown in answer to a `POST` needs a `GET` route at the same address, or better a redirect to a `GET` page after the `POST`; without one the page is an error that says so.
- `on-click`, `on-change`, `on-input`, `on-submit`, `on-keydown` and `on-enter` run statements like `count += 1; draft = ''`. Inside a loop, the loop variable is available to the handler (`on-click="items = $items->reject(fn(i) => $i.id == $item.id)"`). `$event.value`, `$event.key` and `$event.checked` describe the browser event.
- A component can declare its own `<live>` block and use `on-keyup`, `on-escape`, `on-focusin`, `on-focusout`, `on-cancel`, `on-close`, `on-pointerdown`, `on-pointerup` and `on-pointercancel`. The event includes `code`, keyboard modifiers (`alt`, `ctrl`, `meta`, `shift`), `repeat`, pointer coordinates (`client_x`, `client_y`, `page_x`, `page_y`), `button`, `pointer_id` and `pointer_type` where applicable. The handler modifier `prevent: true` cancels the browser's own action before the request, as in `@on-keydown(call: $save, key: [ctrl, s], prevent: true)`.
- To drag one element onto another, mark the source `@drag(copy: true, value: $item.id)` and handle `on-drop` on the destination. Read `$event.drag_value`. Validate the ID and permissions in a handler that saves changes. Offer a keyboard action for the same operation.
- To reorder siblings, use `on-sort` on their container and a unique `sort-item="{$item.id}"` on each child. Users can drag a child or focus it and press Alt+Up or Alt+Down. Set `sort-axis="horizontal"` for left-to-right lists. The action receives `$event.item`, zero-based `$event.from` and `$event.index`, and the proposed `$event.order` list. Validate the order before saving it.
- `on-tick="…" every="200ms"` runs a handler every 200 milliseconds for as long as that element is in the block, counted from when the last answer arrived. Give it `@if` to start and stop it: `<span on-tick="frame += 1" every="100" @if($playing)></span>`.
- `@bind($name)` ties an input to a state variable both ways and redraws the block as you type. A bound value is what a form control sends: text, a number, true or false, or a list of those. Anything else from the browser is refused with a `422`.
- A radio group binds to a single value. A multiple select binds to a list of selected values. Give `@bind` an `on:` option to choose when an edit triggers an update: `@bind($name, on: change)` or `on: blur`. With `on: action`, the value travels with the next component action.
- Use `@bind($draft.title)` to update an existing field in a live state map while retaining its other fields.
- Add `query="page"` to `<state name="page" value="1" />` to initialize it from the query string and reflect changes in the URL. Use this for filters or pagination. Query-backed state holds text, a number, a boolean or null. Null clears the parameter. Each update replaces the browser's current history entry.
- Use `session="search-filters"` to carry a state value across visits in the current session. Use a separate key for independent instances. Add `locked` when only server actions should change a state variable. Signed state is visible in the page, so keep secrets in server-side values.
- Use `on-init` to run a handler when the block mounts and `on-visible` when its element first enters the viewport. Each runs once per block. Use `live-loading` for a status message, `live-disable` to disable a control during a request, `live-offline` to display an offline notice, and a handler's `confirm:` modifier (`@on-click(delete: $item, confirm: 'Remove this item?')`) to ask before an action.
- Add `lazy` to a live block to defer its state initialization and rendering until it enters the viewport. Add `defer` to load it just after the initial document is ready. Put optional `<placeholder>` markup directly inside the block to reserve space while it loads.
- Live updates retain matching DOM nodes. Add `live-key="{$item.id}"` to repeated siblings so they keep their identity when reordered. Use `live-replace` on an element that should be rebuilt on every update.
- Use `live-dirty` to show an unsent edit notice. Set it to a binding name to watch only that field. Use `live-cloak` to hide an element until the browser runtime starts. Use `live-ignore` on a client-owned widget whose markup should survive updates. Keep server event controls and bindings outside that widget.
- You can place an independent live block inside another live component. Give the child a unique, stable ID so it keeps its state through parent renders. Change the child's ID when a parent change should mount a fresh instance. Requests from a child carry the signed ancestor state used to render it.
- Use `@emit_event('items-changed', data: $item.id, target: 'summary')` in a live handler to notify another block. Omit `target` to broadcast to the page. The first matching `on-event` in each block runs. Use `event-name="items-changed"` to filter by name and read `$event.name`, `$event.data` and `$event.source` in the handler. Validate event data before persisting a change.
- A named `<input type="file" name="attachment" live-upload>` sends its selection with its `on-change` handler or its form's `on-submit` handler. The handler receives `$event.files.attachment`, a list with `name`, `type`, `size` and base64 `bytes` for each file. Uploads are limited to 8 MiB per request. Treat filenames and media types as client input and validate the content before saving it. Use `live-replace` on the input to reset the selection after the action.
- Add `<span role="alert" live-error></span>` to display a failed request. Call `@live_redirect('/account')` from a live action to navigate to a same-site path after it succeeds.
- Call `@skip_render()` for an action that should update state or send an event while keeping its current HTML.
- Call `@live_download('report.txt', bytes: $report->to_base64())` to download a generated file from a component action.
- `kscript serve` adds its small runtime (`/_ks/live.js`) to pages that use `<live>`; pages never include it themselves. Set `KSCRIPT_SECRET` so pages keep working across server restarts.

When you use `kscript serve`, a live component posts back to the GET URL that rendered it. For a controller view, each interaction runs the original route and its middleware again. KScript binds the component state and actions to the current session, URL and component ID. It checks that a submitted field or action is still rendered before using it. Use a unique ID per instance when rendering a component in a loop, for example `id="profile-{$profile.id}"`. Keep render expressions free of side effects because the server may evaluate them more than once. Put persistent changes in an authorized action or model binding. State and action arguments travel in the HTML and are readable by the browser.

See `/demo` in `Example Sites/team` for a counter, live search, a to-do list and a temperature converter, and [`samples/life-lab`](../samples/life-lab) for a game whose rules are a KScript script shared with a terminal version and a desktop app.

See the [component guide](components.md) for reusable component classes, advanced live interactions, and client extensions.
