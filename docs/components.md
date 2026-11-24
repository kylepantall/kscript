# Components

A component is a reusable part of a KScript website. Give it typed inputs, render it with a tag, and add a `<live>` block when the interface needs to respond to the browser. The page runtime takes care of sending actions and updating the page.

## Build your first component

Save this file as `_components/click-counter.kscript` inside a site folder.

````kscript
import KScript.Web

type ClickCounter extends Component {
  instance_id: string
  start: int = 0

  fn render(): Component {
    return ```html
    <live id="{$instance_id}">
      <state name="count" value="$start" />
      <output>{$count}</output>
      <button type="button" on-click="count += 1">Add one</button>
    </live>
    ```
  }
}
````

Add these calls to `index.html`. Attribute names use kebab-case. A lone expression passes its typed value, so `{3}` gives the component an integer.

```html
<h1>Counters</h1>
<click-counter instance-id="first" start="{3}" />
<click-counter instance-id="second" />
```

Run `kscript serve` inside the folder and open the address it prints. Each button updates its own counter. Use distinct live IDs when rendering several instances, including repeated components in a loop.

`kscript make component ClickCounter --live` generates a starting file. For a component that only renders display markup, return the HTML without a live block.

## Component inputs, rendering and slots

| Part | How it works |
| --- | --- |
| Discovery | KScript resolves `<item-card />` to a component such as `_components/item-card.kscript` or `_components/item-card.html`. |
| Class contract | A script component extends `Component` from `KScript.Web` and defines `fn render(): Component`. |
| Inputs | Declare fields such as `title: string` on the type. Defaults make a field optional. The tag passes attributes into the fields. |
| Render-time values | HTML can read a field as `$title` or `$.title`. Computed fields and component methods can prepare display values. |
| Children | Put `<slot />` in the template to render the caller's child markup. |
| Template components | HTML and Markdown components can declare `<prop name="title" required />`. A companion script can expose `load(props: map)` and other functions. |

The page may render more than once during an action. Keep render methods, computed values and companion `load` functions free of writes. Put persistent changes in a live action or a controller. See [template props and companion scripts](web.md#template-props-and-component-scripts) for the template form and its `onLoad` callback.

## How a live update works

1. On the first request, KScript evaluates the `<state>` defaults and renders the live block. The HTML includes signed state and action arguments.
2. On an event, the browser sends the state, the event, and any bound input values to the URL that rendered the page. The original GET route and middleware run again when the page comes from a controller.
3. KScript verifies the route, session, block and action. It checks that the submitted action and fields are still rendered and then runs the handler.
4. The browser applies the result to that block. Matching DOM nodes stay in place, including correctly keyed rows and independent nested live blocks.

HTML escapes ordinary `{ expression }` output. Signed values are still visible to the browser. Read private data from the server when you need it. Authorize writes and validate input in the action even when a control is normally hidden or disabled.

## State and binding

Use `<state name="…" value="…" />` directly inside a live block. Read it with `$name` and assign it with `name = …`. A normal state value survives actions on that rendered block.

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

A form with `on-submit` can send several bindings in one action. Use HTML controls such as `required` for immediate feedback and KScript types or rules for server validation. For persistence to a model, [model forms](web.md#model-forms) support explicit saves and autosave with allowlisted fields, ownership checks and CSRF protection.

```html
<live id="note-form">
  <state name="draft" value="{ title: '' }" />
  <state name="status" value="''" />
  <form on-submit="status = $draft.title->trim() == '' ? 'Enter a title' : 'Title accepted'">
    <label>Title <input bind="draft.title" bind-on="action" required></label>
    <button live-disable>Check title</button>
  </form>
  <p live-dirty="draft.title">Unsaved title</p>
  <p live-loading role="status">Checking…</p>
  <p live-error role="alert"></p>
  <p>{$status}</p>
</live>
```

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

A live block can contain an independent child block. Give the child an explicit stable ID. A parent update keeps the child's DOM and state, and a child action uses the signed ancestor state to reconstruct the context it was rendered in. Changing the child's ID mounts a new instance. To update a mounted child from the parent, send it an event.

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

Mark an element with `live-stream="name"` and call `@stream('name', value)` from a live action. The browser can display each chunk while the action is still running. The action stays on the original signed route, and the target must be rendered in that component.

```html
<live id="report">
  <p live-stream="progress" role="status"></p>
  <button on-click="@stream('progress', 'Starting…'); @stream('progress', 'Ready', mode: 'replace'); @skip_render()">Run report</button>
</live>
```

Chunks append by default. Use `mode: 'replace'` for a progress label, and `html: true` only with markup you trust. A successful action normally replaces the preview with its final rendered fragment. Use `@skip_render()` to keep the streamed output. A failed action restores the pre-stream content. Actions that change session cookies, such as signing in or out, need a regular response.

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

Applications can optionally register JavaScript when the runtime fires `kscript:init`. That event fires once per browser document. Registrations survive in-page navigation.

```javascript
document.addEventListener("kscript:init", () => {
  KScriptLive.directive("autosize", ({ element, signal }) => {
    const resize = () => {
      element.style.height = "auto";
      element.style.height = element.scrollHeight + "px";
    };
    element.addEventListener("input", resize, { signal });
    resize();
  });
}, { once: true });
```

Use `live-use-autosize` on a component element to activate that directive. The directive receives its element, owning component, attribute value, and an abort signal. It can return a cleanup function. The runtime aborts and cleans up when the element, owning component, or directive value changes.

`KScriptLive.on(name, callback)` subscribes to `mount`, `unmount`, `request`, `response`, `stream`, `morph`, `commit`, `error`, `navigate:leave`, or `navigate`. It returns an unsubscribe function. Component hooks include the owning element as `component`. The request hook also receives `url`, `event` and `signal`. The response hook receives `status`, `streamed` and the HTTP `response`. A stream hook receives `element` and the incoming `frame`. Use `commit` to react after the DOM is updated.

Register a browser-only action using `KScriptLive.action('name', callback)` and invoke it from `live-client="name"`. The callback receives `component`, `element`, the browser `event`, and a `call(name, payload)` function for that component.

To call a signed server action from JavaScript, give its rendered handler a name with `live-action="save" on-action="@save($event.data)"`. Call `KScriptLive.call(component, 'save', payload)` using the component element or its ID. The server checks that the action still belongs to the rendered component before it runs.

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
| Component template and properties | `extends Component`, typed fields and `<live>` state |
| `wire:model` | `bind` and `bind-on` |
| `wire:click` and `wire:submit` | `on-click` and `on-submit` |
| Dispatch and listeners | `@emit_event()` and `on-event` |
| `wire:navigate` | `live-navigate` |
| `wire:stream` | `live-stream` and `@stream()` |
| `@teleport` | `<template live-portal="…">` |
| JavaScript hooks, directives and component calls | `KScriptLive.on()`, `KScriptLive.directive()` and `KScriptLive.call()` |

For the surrounding site, see [Web pages](web.md). For middleware, ownership and controller contracts, see [Routes and APIs](http.md). In an MCP client, ask `read_doc` for the `components` page.
