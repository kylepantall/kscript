# KScript for VS Code

Language support for [KScript](https://github.com/kylepantall/kscript): scripts (`.kscript`, and `.ks.xml` for the XML form) and the HTML and Markdown pages that `kscript serve` runs.

- Errors and warnings as you type, with quick fixes
- Completion for elements, attributes, `$variables`, `@commands` and `->methods`
- Hover documentation and signature help
- Go to definition, find references and rename
- Outline, semantic highlighting and syntax highlighting, including `{ expressions }` in pages
- Run a script with the **Run** button, `F5` or the **▶ Run** line above its first statement, with the error it stops on marked on its line
- Tests in the Testing view and a **▶ Run test** line above each one
- Suggestions with the everyday statements and methods first, parameter names shown beside plain values, and hovers with an example and an **Open docs** link
- How many times each function is used, shown above it, and **Go to Symbol in Workspace** (`Ctrl+T`) across every script
- Format Document and format on save, using the same formatter as `kscript fmt` (XML scripts and pages; `.kscript` files are left as they are)

New to KScript? Open **Help > Welcome**, or run **Welcome: Open Walkthrough…** and pick **Get Started with KScript**: it goes from installing to your first script, running it, fixing a mistake and starting a project.

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

The extension finds `kscript` where the installers put it (`~/.kscript/bin`, or `%LOCALAPPDATA%\Programs\kscript` on Windows), then on your `PATH`. Keep it up to date with `kscript update`, which updates this extension too. When the `kscript` binary changes, the extension restarts the language server on its own, so you never keep getting errors from the old version. **KScript: Restart Language Server** does the same by hand.

You can also install this extension from the command line with `kscript ide install`.

## Settings

| Setting | Default | Description |
| --- | --- | --- |
| `kscript.server.path` | empty | The `kscript` executable to use, if it isn't installed in the usual place. `~` and `${env:NAME}` are expanded. |
| `kscript.server.args` | `["lsp"]` | Arguments that start the language server. |
| `kscript.pages.enabled` | `auto` | Check `.html` and `.md` pages: `auto` in folders that look like a `kscript serve` site (`_components/`, `_layout.html`, `_layouts/` or `kscript.json`), `always`, or `never`. |
| `kscript.codeLens.enabled` | `true` | Show **Run** above a script, **Run test** above each test and how many times each function is used. |
| `kscript.trace.server` | `off` | Log the messages between VS Code and the language server (`messages` or `verbose`). |

## Project settings

A `kscript.json` at the top of a project (created by `kscript make project`) sets the lint rules the editor uses: `lint.rules` changes or turns off a diagnostic by code (`"KS3003": "off"`), and `lint.exclude` silences files and folders such as `_templates`. The extension validates `kscript.json` and completes its settings. See [Project settings](https://github.com/kylepantall/kscript/blob/master/docs/projects.md#project-settings-kscriptjson) for every setting.

The `format` section of `kscript.json` sets the formatting style (`"indent": 4`, `"indent": "tab"`, `"print_width": 100` and more); without it, Format Document follows your editor's tab settings. The extension is the default formatter for KScript scripts. For `.html` pages, choose **KScript** when VS Code asks which formatter to use. See [Formatting](https://github.com/kylepantall/kscript/blob/master/docs/formatting.md).

## Commands

- **KScript: Restart Language Server**: after updating `kscript` or changing settings.
- **KScript: Show Language Server Output**: the server's log, also opened by clicking **KScript** in the status bar.
- **KScript: New Script** (also in **File > New File…**): opens a short script that runs as it is, as a place to start.
- **KScript: Run Script** (`F5`, or the run button in the editor title bar): saves and runs the open script in the terminal. If it stops with an error, the line is underlined and listed in Problems. A new file that hasn't been saved yet runs too.
- **KScript: Run Tests**: runs `kscript test` for the workspace folder in a task.
- **KScript: Run Tests in This File**: runs the open test file. Test files (`*.test.kscript`, `*.test.ks.xml`) also get a run button in the editor title bar, and every test is listed in the Testing view, where it can be run on its own.
- **KScript: Serve Site**: runs `kscript serve` in the project's folder.
- **KScript: Check Project**: runs `kscript check` and lists what it finds in Problems.
- **KScript: Format Project**: runs `kscript fmt` on the whole project.
- **KScript: New Project…**: asks for a name and a folder, runs `kscript make project`, and offers to open it.
- **KScript: Open Docs**: runs `kscript docs`, which opens the docs in your browser.

## Troubleshooting

If the status bar shows a warning, the extension couldn't find `kscript`. Install it as above, or set `kscript.server.path`. Run `kscript --version` in a terminal to check which version you have.
