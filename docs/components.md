# Components

A component is a reusable part of a KScript website. Give it typed inputs and render it with a tag. State, event handlers, bindings and directives such as `live-use-focus` automatically make the component live. The page runtime sends actions and updates the component.

## Build your first component

Create `_components/click-counter.kscript` inside your site folder.

````kscript
import KScript.Web

type ClickCounter extends Component {
  count: int = 0

  fn render(): Component {
    return ```html
    <button type="button" @on-click(count += 1)>Count is {$count}</button>
    ```
  }
}
````

Create `index.html` in the same site folder.

```html
<h1>Counters</h1>
<click-counter />
<click-counter />
```

Run `kscript serve` from the site folder and open the address it prints. Click the first button. It reads **Count is 1**, and the second still reads **Count is 0**.

The click handler makes each counter live. Each instance gets its own root and keeps its own `count` between clicks. Passing `<click-counter count="{5}" />` starts an instance at five.

An explicit `<live>` block lets you choose the root element with `as`, defer rendering, or make only a portion of a component live. Use explicit blocks for interactive markup directly in a page. Plain display components render without loading the live runtime.

## Component inputs, rendering and slots

Give a component typed inputs by defining a KScript type. Save this as `_components/hello-card.kscript`.

````kscript
import KScript.Web

type HelloCard extends Component {
  name: string

  fn render(): Component {
    return ```html
    <p>Hello, {$name}!</p>
    ```
  }
}
````

Add `<hello-card name="Sam" />` to `index.html` to render **Hello, Sam!**. Use kebab-case for attributes like `display-name` and underscores for fields like `display_name`. An attribute that consists of one expression passes its original type. For example, `start="{3}"` passes an integer.

| Part | How it works |
| --- | --- |
| Discovery | KScript resolves `<item-card />` to a component such as `_components/item-card.kscript` or `_components/item-card.html`. |
| Class contract | A script component extends `Component` from `KScript.Web` and defines `fn render(): Component`. |
| Inputs | The caller can initialize stored fields using attributes. A field initializer supplies a default. Simple field types also have built-in initial values. |
| Render-time values | HTML can read a field as `$title` or `$.title`. Computed fields and component methods can prepare display values. |
| Children | Put `<slot />` in the template to render the caller's child markup. |
| Template components | HTML and Markdown components can declare `<prop name="title" required />`. A companion script can expose `load(props: map)` and other functions. |

The page may render more than once during an action. Keep render methods, computed values and companion `load` functions free of writes. Put persistent changes in a live action or a controller. See [template props and companion scripts](web.md#template-props-and-component-scripts) for the template form and its `onLoad` callback.

## How a live update works

1. On the first request, KScript evaluates the `<state>` defaults and renders the live root. The HTML includes signed state and action arguments.
2. On an event, the browser sends the state, the event, and any bound input values to the URL that rendered the page. The original GET route and middleware run again when the page comes from a controller.
3. KScript verifies the route, session, block and action. It checks that the submitted action and fields are still rendered and then runs the handler.
4. The browser applies the result to that block. Matching DOM nodes stay in place, including correctly keyed rows and independent nested live blocks.

HTML escapes ordinary `{ expression }` output. Signed values are still visible to the browser. Read private data from the server when you need it. Authorize writes and validate input in the action even when a control is normally hidden or disabled.

## State and binding

In a class component, declare each field once. This editor starts with an empty title, an unchecked box and an empty list. Its color defaults to yellow.

````kscript
import KScript.Web

type NoteEditor extends Component {
  title: string
  done: bool
  descriptions: string[]
  color: color = 'yellow'

  fn render(): Component {
    return ```html
    <label>Title <input @bind('title') @bind-on('blur')></label>
    <label><input type="checkbox" @bind('done')> Done</label>
    <button type="button" @on-click(title = ''; done = false)>Clear</button>
    ```
  }
}
````

Mount it with `<note-editor />` or give it an initial title with `<note-editor title="First draft" />`. Numbers start at zero, strings at empty text, booleans at false and collections at an empty list or map. Nullable fields start at `null`. Give domain types such as `color`, enums and records a valid initializer or pass their value from the caller. A named enum such as `enum NoteColor { red, blue }` limits the editor's choices to those members.

KScript restores fields the instance changed on later actions. Fields the component only reads can pick up fresh values from the caller on each render. A browser can submit edits only for controls rendered with a matching binding. Use the current server session to authorize writes.

Put KScript behavior in directives on the opening HTML tag. For example, `@bind('title')` takes a field path, `@live-disable()` is a flag, and `@on-click(count += 1)` takes KScript statements. The IDE completes directive names after `@`, checks binding names, and suggests the allowed values for options such as `@bind-on('blur')`. Existing HTML spellings such as `bind="title"` and `on-click="count += 1"` work too.

Use `<state name="…" value="…" />` at the top level of a component template or directly inside a `<live>` block. Read it with `$name` and assign it with `name = …`. A normal state value survives actions on that rendered block.

| Need | Markup |
| --- | --- |
| Text editing | `<input bind="draft">` |
| An existing map field | `<input bind="draft.title">` |
| An edit that waits for a change event | `<input bind="draft" bind-on="change">` |
| An edit that waits for focus to leave | `<input bind="draft" bind-on="blur">` |
| A value sent with the next action | `<input bind="draft" bind-on="action">` |
| A checkbox | `<input type="checkbox" bind="enabled">` |
| A radio group | Give radios the same binding and each its own `value`. |
| Multiple selected values | Bind `<select multiple>` to a list. |
| A shareable URL | `<state name="page" value="1" query="page" />` |
| A value kept across visits in the current session | `<state name="filter" value="''" session="notes-filter" />` |
| State writable only by server actions | `<state name="owner_id" value="$owner.id" locked />` |

Number defaults let normal numeric input update the state as a number. For query-backed state, use a text, number, Boolean or null value. A change updates the current history entry, and null clears the parameter. Session-backed instances that should remain independent need separate keys.

For database pagination, keep the current page in query-backed state, call `->paginate(page: $page)` on a [model query](data.md#reading), and change the page from the next and previous buttons. The view can use `has_more` and the current page to disable those buttons at the ends.

## Actions and browser events

Handlers contain KScript statements and can call imported functions. Browser details are available as `$event`. A key event includes its key, code and modifiers. Pointer events include coordinates, button, pointer ID and pointer type where applicable.

| Interaction | Attributes |
| --- | --- |
| Buttons and forms | `on-click`, `on-submit` |
| Input changes | `on-input`, `on-change` |
| Keyboard and focus | `on-keydown`, `on-keyup`, `on-enter`, `on-escape`, `on-focusin`, `on-focusout` |
| Dialog and pointer events | `on-cancel`, `on-close`, `on-pointerdown`, `on-pointerup`, `on-pointercancel` |
| Lifecycle | `on-init`, `on-visible`, `on-tick` with `every="1s"` |
| Other components and client code | `on-event` and a named `on-action` |

Use `prevent-default="keydown"` on a handler element to cancel a browser default synchronously. Use `live-confirm="Remove this note?"` to confirm a destructive action before posting it. An action can call `@live_redirect('/notes')` for a same-site redirect or `@skip_render()` to save state without updating the current HTML.

### Events between components

Emit an event from an action with `@emit_event('notes-changed', data: $note.id, target: 'summary')`. Omit the target to broadcast. Each receiving block runs its first matching listener, such as `on-event="latest = $event.data" event-name="notes-changed"`. The handler can read `$event.name`, `$event.data` and `$event.source`.

### Drag, drop and sorting

For a drop target, put `draggable="true" drag-value="{$item.id}"` on the source and `on-drop` on the destination. The action reads `$event.drag_value`.

For sortable siblings, give each item a unique `sort-item="{$item.id}"` and put `on-sort` on their container. The handler gets the item ID, the old and new zero-based indexes, and the proposed order. Users can drag an item or focus it and use Alt+Up or Alt+Down. Set `sort-axis="horizontal"` for left-to-right lists. Validate the item IDs before persisting a new order.

## Forms, uploads and loading feedback

A form with `on-submit` can send several bindings in one action. Paste this block into `index.html`, enter a name, and click **Greet** to see the result. The binding is sent when you submit the form.

```html
<live id="greeting-form">
  <state name="name" value="''" />
  <state name="message" value="''" />
  <form on-submit="message = 'Hello, ' + $name + '!'">
    <label>Name <input bind="name" bind-on="action" required></label>
    <button live-disable>Greet</button>
  </form>
  <p role="status">{$message}</p>
</live>
```

Use HTML controls such as `required` for immediate feedback and KScript types or rules for server validation. To save to a database, use [model forms](web.md#model-forms).

| Attribute | Browser behavior |
| --- | --- |
| `live-loading` | Shows the element while the action is pending. |
| `live-disable` | Disables the control until the request completes. |
| `live-dirty` | Shows the element when a bound value has unsent edits. Give it a binding name to watch one field. |
| `live-offline` | Shows the element while the browser reports that it is offline. |
| `live-error` | Shows feedback when the action fails. |
| `live-cloak` | Hides markup until the live runtime starts. |

Use `live-upload` on a named file input. The upload travels with its `on-change` action or its form's `on-submit` action. For an input named `attachment`, `$event.files.attachment` contains file entries with `name`, `type`, `size` and base64 `bytes`. The request accepts up to 8 MiB of files. Validate content and permissions before saving. Mark the file input `live-replace` to clear its selection on the next redraw.

Use `@live_download('report.txt', bytes: $report->to_base64())` to offer bytes generated by an action as a file.

## Nesting, identity and lazy rendering

A live component can contain an independent child component. A parent update keeps the child's DOM and state, and a child action uses the signed ancestor state to reconstruct the context it was rendered in. Changing the child's ID mounts a new instance. To update a mounted child from the parent, send it an event.

- For a list of components that can reorder or change membership, pass each one a unique ID such as `<row-editor live-id="editor-{$row.id}" record="{$row}" />`. For an explicit `<live>` block, set its `id` attribute.
- Use `live-key="{$item.id}"` for repeated siblings whose DOM identity should survive a reorder.
- Use `live-ignore` for a browser-owned widget. Keep KScript's bound controls and action handlers outside that widget.
- Use `live-replace` to build a fresh node on each redraw.
- Add `lazy` to initialize and render the block when it enters the viewport, or `defer` to load just after the document is ready. The optional direct `<placeholder>` child is rendered before the block mounts.

```html
<live id="summary" lazy>
  <placeholder><p role="status">Loading summary…</p></placeholder>
  <state name="shown" value="true" />
  <if test="$shown"><p>Ready</p></if>
</live>
```

## In-page navigation

Add `live-navigate` to a same-site link to fetch the destination page and replace the document body in place. Back and Forward restore the page and its scroll location. Links keep their ordinary behavior for modified clicks, downloads, other origins, and page policies that require a new document.

```html
<a href="/notes" live-navigate>Notes</a>
```

The browser dispatches `kscript:navigating` before an in-page visit and `kscript:navigated` after it. Call `preventDefault()` on the first event to cancel the visit. Body scripts run on subsequent visits unless marked `live-navigate-once`. Optional JavaScript can also call `KScriptLive.navigate('/notes')`.

## Stream an action's output

Paste this block into `index.html` and click **Run**. The action sends text to the element named `progress`. In a longer action, each chunk can appear before the action finishes.

```html
<live id="stream-demo">
  <p live-stream="progress" role="status"></p>
  <button on-click="@stream('progress', 'Ready'); @skip_render()">Run</button>
</live>
```

Call `@stream()` more than once to send several chunks. The target must be rendered in the same component. Chunks append by default. Use `mode: 'replace'` for a progress label, and `html: true` only with markup you trust. A successful action normally replaces the preview with its final rendered fragment. Use `@skip_render()` to keep the streamed output. A failed action restores the pre-stream content. Actions that change session cookies, such as signing in or out, need a regular response.

## Portals

A portal moves its template contents to a matching HTML container outside the live component. This lets overlays escape local clipping while their events and bindings still belong to the source component. The portal disappears when its template disappears or when the page changes. Include a form in the template with its fields, or associate the fields with an external form using HTML's `form` attribute.

```html
<div id="overlay-root"></div>
<live id="editor">
  <state name="open" value="true" />
  <if test="$open">
    <template live-portal="#overlay-root">
      <aside><button on-click="open = false">Close</button></aside>
    </template>
  </if>
</live>
```

## Client-side extensions

Give a field focus when a component mounts. Save this as `_components/focus-field.html`.

```html
<label>Search <input type="search" live-use-focus></label>
```

Add the component and its directive to `index.html`.

```html
<focus-field />
<script>
document.addEventListener("kscript:init", () => {
  KScriptLive.directive("focus", ({ element }) => {
    element.focus();
  });
}, { once: true });
</script>
```

Reload the page. The field receives focus, and `live-use-focus` gives its component a live root automatically. The runtime fires `kscript:init` once per browser document. Registrations survive in-page navigation. The directive receives its element, owning component, attribute value, and an abort signal. It can return a cleanup function. The runtime aborts and cleans up when the element, owning component, or directive value changes.

To mount a display component so a JavaScript lifecycle hook can observe it, give the invocation a `live-id`, such as `<hello-card name="Sam" live-id="welcome" />`.

`KScriptLive.on(name, callback)` subscribes to `mount`, `unmount`, `request`, `response`, `stream`, `morph`, `commit`, `error`, `navigate:leave`, or `navigate`. It returns an unsubscribe function. Component hooks include the owning element as `component`. The request hook also receives `url`, `event` and `signal`. The response hook receives `status`, `streamed` and the HTTP `response`. A stream hook receives `element` and the incoming `frame`. Use `commit` to react after the DOM is updated.

Register a browser-only action using `KScriptLive.action('name', callback)` and invoke it from `live-client="name"`. The callback receives `component`, `element`, the browser `event`, and a `call(name, payload)` function for that component.

Give a server action a name in the component's HTML. The `on-action` handler receives the payload as `$event.data`.

```html
<span hidden @live-action('save') @on-action(@save($event.data))></span>
```

Call `KScriptLive.call(component, 'save', payload)` with the component element or its live ID. The server checks that the action belongs to the rendered component before it runs. The HTML attribute `live-action="save"` exposes the same action.

## Test a component

Run `kscript check .` from the site folder to catch invalid declarations and missing props. Put parsing, validation, and persistence logic in functions that can also be exercised by `kscript test`. A browser test should serve the project, open an actual page, perform the interaction, and assert what the visitor sees.

For a streamed action, wait for an intermediate value while the request is still pending, then assert the final state and run another action. For a portal, exercise its controls at the destination, trigger a redraw, and verify it disappears when the source is removed. For in-page navigation, test Back and Forward after query and hash changes. Keep a test for a rejected action or unauthorized field when the component writes data.

## Troubleshoot

| Symptom | Check |
| --- | --- |
| A tag renders without its content | Check the component filename, the page's component folder and the diagnostic from `kscript check`. |
| An input keeps its old value | Declare its binding in the block's state. A dotted binding needs an existing map field. Check whether `bind-on` is waiting for a change, blur, or action. |
| A repeated block updates the wrong row | Use a stable live ID per instance and a stable `live-key` for reordered siblings. |
| A live request expires | The registered route, session or visible action may have changed. Refresh the page. Set a stable `KSCRIPT_SECRET` if sessions must survive server restarts. |
| A portal has no visible content | Check that the selector matches an HTML container outside any live block. The template stays inert while the destination is missing. |
| Stream output arrives only at the end | Start the action inside a live block, keep the target visible, and ensure intermediaries can forward chunks without buffering the whole response. |
| A streamed preview disappears after success | The normal final fragment is authoritative. Render the final content from state or use `@skip_render()` to keep the streamed output. |
| A navigation opens a new browser document | Check that the link is opted in and stays on the same origin. Destination policies and certain document features, such as `<base>`, require a native visit. |
| A custom directive appears to run twice | A changed attribute value or replaced element remounts the directive. Use its abort signal or returned cleanup to release listeners. Keep app-wide registrations in a script that runs once. |

## Coming from Livewire 4

KScript uses its own component syntax. These are the common workflows and their equivalents.

| Livewire 4 | KScript |
| --- | --- |
| Component template and properties | `extends Component`, typed fields and `<state>` |
| `wire:model` | `bind` and `bind-on` |
| `wire:click` and `wire:submit` | `on-click` and `on-submit` |
| Dispatch and listeners | `@emit_event()` and `on-event` |
| `wire:navigate` | `live-navigate` |
| `wire:stream` | `live-stream` and `@stream()` |
| `@teleport` | `<template live-portal="…">` |
| JavaScript hooks, directives and component calls | `KScriptLive.on()`, `KScriptLive.directive()` and `KScriptLive.call()` |

For the surrounding site, see [Web pages](web.md). For middleware, ownership and controller contracts, see [Routes and APIs](http.md). In an MCP client, ask `read_doc` for the `components` page.
