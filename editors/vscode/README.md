# KScript for VS Code

Language support for [KScript](https://github.com/kylepantall/kscript): scripts (`.ks.xml`, `.ks`, `.kscript`) and the HTML and Markdown pages that `kscript serve` runs.

- Errors and warnings as you type, with quick fixes
- Completion for elements, attributes, `$variables`, `@commands` and `->methods`
- Hover documentation and signature help
- Go to definition, find references and rename
- Outline, semantic highlighting and syntax highlighting, including `{ expressions }` in pages

## Requirements

The extension uses the language server built into the `kscript` command, version 0.1.0 or later. Install KScript first.

Linux and macOS:

```sh
curl -fsSL https://raw.githubusercontent.com/kylepantall/kscript/master/install.sh | sh
```

Windows (PowerShell):

```powershell
irm https://raw.githubusercontent.com/kylepantall/kscript/master/install.ps1 | iex
```

The extension finds `kscript` where the installers put it (`~/.kscript/bin`, or `%LOCALAPPDATA%\Programs\kscript` on Windows), then on your `PATH`. Keep it up to date with `kscript update`, then run **KScript: Restart Language Server**.

You can also install this extension from the command line with `kscript ide install`.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `kscript.server.path` | empty | The `kscript` executable to use, if it isn't installed in the usual place. `~` and `${env:NAME}` are expanded. |
| `kscript.server.args` | `["lsp"]` | Arguments that start the language server. |
| `kscript.pages.enabled` | `auto` | Check `.html` and `.md` pages: `auto` in folders that look like a `kscript serve` site (`_components/`, `_layout.html`, `_layouts/` or `kscript.json`), `always`, or `never`. |
| `kscript.trace.server` | `off` | Log the messages between VS Code and the language server (`messages` or `verbose`). |

## Project settings

A `kscript.json` at the top of a project (created by `kscript make project`) sets the lint rules the editor uses: `lint.rules` changes or turns off a diagnostic by code (`"KS3003": "off"`), and `lint.exclude` silences files and folders such as `_templates`. The extension validates `kscript.json` and completes its settings. See [Project settings](https://github.com/kylepantall/kscript/blob/master/docs/projects.md#project-settings-kscriptjson) for every setting.

## Commands

- **KScript: Restart Language Server**: after updating `kscript` or changing settings.
- **KScript: Show Language Server Output**: the server's log, also opened by clicking **KScript** in the status bar.
- **KScript: Run Tests**: runs `kscript test` for the workspace folder in a task.
- **KScript: Run Tests in This File**: runs the open test file. Test files (`*.test.ks.xml`) also get a run button in the editor title bar.

## Troubleshooting

If the status bar shows a warning, the extension couldn't find `kscript`. Install it as above, or set `kscript.server.path`. Run `kscript --version` in a terminal to check which version you have.
