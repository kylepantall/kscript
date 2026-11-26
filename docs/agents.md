# Coding agents

KScript writes its own docs for coding agents such as Claude Code, Codex and Cursor. They come from your code, so they
say what your functions really take and return, and they stay short, so an agent can read them in full every time.

```sh
kscript agents            # write AGENTS.md and .kscript/agents
kscript agents --check    # write nothing; fail if they are out of date (for CI)
kscript agents --print    # print AGENTS.md's generated part
kscript watch             # rewrite them each time a file changes, until Ctrl+C
kscript check --output json   # problems as JSON, with fixes as exact text edits
```

`kscript make project` (and `--bare`) writes them for you. After that they keep themselves up to date: the editor
rewrites `.kscript/agents` and AGENTS.md each time you save a script, and `kscript serve` and `kscript watch` do the
same whenever a file changes, so edits made anywhere (another editor, a coding agent, `git pull`) are picked up too.

## Implement a feature in an existing project

The generated `AGENTS.md` gives coding agents required project conventions and a checklist to run before finishing a change. Read `kscript.json`, the generated symbol index and one neighboring feature first. The manifest sets the entry point, native sources and generator paths. The index shows which types and operations already exist. Follow the project's configured folders and script format. Look up command and method signatures in the generated package reference or MCP.

| When the feature needs | Where the code belongs |
| --- | --- |
| An email address, UUID, duration or URL | Give the parameter or field its built-in type. Its constructor and methods implement the format. |
| A value with an application-specific rule | Use `kscript make native` for a project-wide refinement. Define its normalization, validation and value-specific methods once. Project natives load automatically. |
| A known group of fields | Use `kscript make type`, import the record, and accept it at the function boundary. For external JSON use `->json(as: Type)`. Keep `map` for data with genuinely open keys. |
| A route that accepts a JSON body | Use `kscript make route` and `kscript make request`. The route selects the controller. The controller receives `json: RequestType` and the framework validates the body. |
| Persistent data | Use a model and the project's migrations. Keep shared domain behavior with the type or in a library module. Put rendering in a view or page component. |

Define an item key once and use it as the type of the request field.

```sh
kscript make native ItemKey --base string --normalize '$value->trim()' --pattern '/^item_[a-zA-Z]+$/'
kscript make request CreateItem --fields 'key: ItemKey'
```

The default layout puts these files in `natives/item-key.kscript` and `requests/create-item.kscript`. The request can use `ItemKey` directly. A controller imports the request file and declares `fn store(json: CreateItem)`. A malformed key fails request validation before the controller runs. A worker or CLI command can take `key: ItemKey` and get the same checks.

Test a valid normalized value, an invalid value, and the feature's observable result. Handle specific errors where the program can recover or report failure. Run `kscript check --output json --warnings-as-errors`, `kscript test`, and `kscript fmt --check`. Refresh the generated guidance with `kscript agents` and report the verification results. [Project layout and generators](projects.md#where-files-go-the-recommended-layout) · [Semantic types](types.md)

## Watching

`kscript watch` builds everything the project makes from its code, then rebuilds it a moment after its files stop
changing: the agent docs, the project's own docs (`.kscript/docs/project.md`, once `kscript docs --project` has
written it; see [Docs while you work](web.md#docs-while-you-work---with-docs)) and, when `http.openapi.file` is set, the
[OpenAPI document](http.md#api-docs-openapi). Other generated files join the same watch as packages add them, so one
command, the editor and `kscript serve` keep all of them in step with the code.

```sh
kscript watch            # build, then rebuild on every change until Ctrl+C
kscript watch --once     # build once and stop
kscript watch --check    # write nothing; fail if anything is out of date (for CI)
```

It ignores its own files, hidden folders (such as `.git` and `.kscript`), `bin`, `obj`, `node_modules` and `dist`.

## What gets written

| File | What it holds | Size |
| --- | --- | --- |
| `AGENTS.md` | Links to the symbol index and package references, a short KScript example, project-specific feature guidance and a checklist | up to about 2,100 tokens plus project guidance |
| `CLAUDE.md` | `@AGENTS.md`, so Claude Code reads the same file (made by `kscript make project`) | 1 line |
| `.kscript/agents/index.md` | The project's routes, then one line per top-level `fn`, `type`, `enum` and `const`, by file | up to about 4,000 tokens |
| `.kscript/agents/packages/<name>.md` | Package guidance and one line per command, method and statement | up to about 7,000 tokens each |

Only packages the project uses get a file, and a file whose package is no longer used is removed. When a file
would go over its budget, it stops at a whole entry and says how many were left out; the [`kscript mcp`](mcp.md) server has the
rest. KScript leaves AGENTS.md and CLAUDE.md out of script checking, formatting, testing and serving.

## Add project guidance

Write project instructions above the generated block in AGENTS.md or on a new line below
`<!-- kscript agents: end -->`. KScript keeps both sections when it updates the file. Keep the two generated
marker comments on their own lines.

To maintain guidance in separate Markdown files, list them in `kscript.json`:

```json
{
  "agents": {
    "merge": ["docs/team-guidance.md", "docs/review-checklist.md"]
  }
}
```

Run `kscript agents` to include the files in a **Project guidance** section near the top of the generated
block, in the order listed. Edit the source Markdown and regenerate. File paths are relative to
`kscript.json`. Each file must be a local `.md` or `.markdown` file inside the project. Generated files, symbolic
links and reserved marker lines outside code fences are rejected. Close any code fences within each source file.
Missing or invalid inputs leave the current AGENTS.md untouched.

`kscript agents --print` includes the merged guidance and `kscript agents --check` detects changes to it
without updating files. The built-in reference budget leaves the project guidance whole.

## Made from the code

You don't need to tag anything for the docs to be useful. For each function, type, enum and const, the index reads:

- **the signature**: parameter names, types and defaults, and the return type the editor works out (`@store(json): http_response`);
- **a type's fields**: their types, defaults, `from` paths, computed fields and any other rule on the field, such as a validation attribute;
- **an enum's values**;
- **the first sentence of its `///` doc comment**, and whether it is `@deprecated`;
- **the errors that can escape a call**, from explicit throws, resolved calls and `@throws` annotations;
- **the call graph**: the functions it calls and the functions that call it, across the project's files.

```text
## lib/tasks.kscript

- `@add_task(tasks, title): list` The list with a new task at the end. :6
  - used by: @store, @starter_tasks
- `@starter_tasks(): list` The tasks a new list starts with. :31
  - calls: @add_task
```

For a site with [KScript.Http](http.md) routes, the index starts with a summary of explicit route declarations.
The generator reads them without running startup code, imports or route files. Run `kscript routes` for the
resolved table, including routes added by resource controllers or conditional code.

Names that start with `_`, test files and pages are left out.

## Agent tags

Tags in a `///` doc comment add what the code can't say. All of them are optional.

```kscript
/// Adds a task and saves the list.
/// @llm Append a task and save; returns the new list.
/// @prefer @add_task($tasks, title: 'x') over $tasks->push(), which doesn't save.
/// @avoid Calling it in a loop: it saves each time. Build the list, then call @save_tasks once.
/// @pitfall title is trimmed, and an empty title throws Validation.
/// @example @add_task($tasks, title: 'Buy milk')
fn add_task(tasks: list, title: string) {
  // …
}

/// Only for the tests.
/// @agent hide
fn reset_store() {
  // …
}
```

| Tag | What it does |
| --- | --- |
| `@llm` | The line agents read instead of the summary. Without it they get the summary's first sentence. Both are cut to 120 characters. |
| `@prefer` | The way to write something that agents should choose. |
| `@avoid` | A way of using it that agents should not choose. |
| `@pitfall` | A mistake that is easy to make. |
| `@example` | Shown when it fits on one line of up to 100 characters. |
| `@agent hide` | Leaves the symbol out of the agent docs. |

The editor shows `@prefer`, `@avoid` and `@pitfall` in hover too, so they help people as well. In XML, write the same
tags in the `<!--- ... -->` comment before the element.

## Notes on built-in commands

Packages add the same kind of notes to their commands, methods and elements, next to their examples. The package files
show them under each entry:

```text
- `->push(items...): list` A new list with items added to the end.
  - pitfall: Returns a new list and leaves the old one alone: write items = $items->push($x).
  - e.g. `[1, 2]->push(3, 4)`
- `@api(url: string, options?: map): response` Makes an HTTP request and returns the response.
  - `options.timeout: duration` Maximum wait for an outbound HTTP(S) request. Example `30s`.
  - prefer: @api($url, as: Type) (the same as ->check()->json(as: Type)) to fail fast and check the data's shape.
  - pitfall: A 4xx or 5xx status doesn't throw: chain ->check() (throws Network) or test .is_ok.
```

A package written in C# adds them with `p.AgentNotes(("list->push", new AgentNotes(pitfall: "...")))`, keyed like
`p.Examples` (`"@api"`, `"list->push"`, or `"<input>"` for an element). Keep each note to one short line,
since agents read all of them.

## JSON diagnostics

`kscript check --output json` prints one JSON document, so an agent can fix problems without parsing text:

```json
{
  "files": 1,
  "errors": 0,
  "warnings": 1,
  "problems": [
    {
      "file": "main.kscript",
      "line": 2, "column": 8, "endLine": 2, "endColumn": 15,
      "severity": "warning",
      "code": "KS6005",
      "message": "slugify is imported but never used.",
      "fixes": [
        { "title": "Remove the import", "edits": [ { "line": 2, "column": 1, "endLine": 2, "endColumn": 43, "newText": "" } ] }
      ]
    }
  ]
}
```

Lines and columns count from 1, and each range ends just past its last character. A fix is the same quick fix the
editor offers: apply its edits from the last to the first, so the earlier positions stay right. The exit code is the
same as for text: 0 with no errors, 1 with errors (or warnings with `--warnings-as-errors`), and 2 for bad usage, where the
JSON is `{ "error": "..." }`.

## CI

Add `kscript agents --check` next to `kscript check`, so a pull request that changes a signature also updates the agent
docs:

```sh
kscript check --warnings-as-errors
kscript agents --check
```
