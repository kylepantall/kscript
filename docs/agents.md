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
| `AGENTS.md` | A crash course in KScript (a small program in the project's format), the rules agents get wrong, the mistakes to avoid, the commands to run after each change, and the project's packages | about 1,000 tokens |
| `CLAUDE.md` | `@AGENTS.md`, so Claude Code reads the same file (made by `kscript make project`) | 1 line |
| `.kscript/agents/index.md` | The project's routes, then one line per top-level `fn`, `type`, `enum` and `const`, by file | up to about 4,000 tokens |
| `.kscript/agents/packages/<name>.md` | One line per command, method and statement of each package the project imports | up to about 7,000 tokens each |

Only packages the project uses get a file, and a file whose package is no longer imported is removed. When a file
would go over its budget, it stops at a whole entry and says how many were left out; the [`kscript mcp`](mcp.md) server has the
rest. AGENTS.md only replaces the part between its two `<!-- kscript agents -->` lines, so anything you write above or
below them is kept. Neither file is checked, formatted, tested or served.

## Made from the code

You don't need to tag anything for the docs to be useful. For each function, type, enum and const, the index reads:

- **the signature**: parameter names, types and defaults, and the return type the editor works out (`@store(json): http_response`);
- **a type's fields**: their types, defaults, `from` paths, computed fields and any other rule on the field, such as a validation attribute;
- **an enum's values**;
- **the first sentence of its `///` doc comment**, and whether it is `@deprecated`;
- **the errors its body throws** (`throw Validation "..."` gives `throws: Validation`);
- **the call graph**: the functions it calls and the functions that call it, across the project's files.

```text
## lib/tasks.kscript

- `@add_task(tasks, title): list` The list with a new task at the end. :6
  - used by: @store, @starter_tasks
- `@starter_tasks(): list` The tasks a new list starts with. :31
  - calls: @add_task
```

For a site with [KScript.Http](http.md) routes, the index starts with the same table `kscript routes` prints:
method, path, name, controller and middleware.

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
- `@api(url, options?): response` Makes an HTTP request and returns the response.
  - prefer: @api($url)->check()->json(as: Type) to fail fast and check the data's shape.
  - pitfall: A 4xx or 5xx status doesn't throw: chain ->check() (throws Network) or test .is_ok.
```

A package written in C# adds them with `p.AgentNotes(("list->push", new AgentNotes(pitfall: "...")))`, keyed like
`p.Examples` (`"@api"`, `"list->push"`, or `"<download>"` for an element). Keep each note to one short line,
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
