# KScript 2 language server

The language server gives editors diagnostics, completion, hover, signature help, go to definition, find references,
rename, an outline, semantic highlighting, quick fixes, document links and formatting for KScript 2 scripts (`.ks.xml`, `.ks`),
pages (`.html`) and Markdown pages (`.md`). It targets KScript 2 only: v1 elements, attributes and commands are
reported as retired, with the KScript 2 replacement and a quick fix where one is mechanical.

## Using it

| Command | What it does |
| --- | --- |
| `kscript lsp` | Runs the server over stdio (LSP 3.17). Any LSP client can use it. |
| `kscript check [file-or-folder]... [--warnaserror]` | Prints problems as `file(line,col): error KS4001: ...` and exits 1 on errors, for CI. With no path it checks the project the current folder is in. A folder with `_layout.html`, `_components/`, `_layouts/` or `kscript.json` is a site, so its `.html` and `.md` pages are checked too. |
| `kscript fmt [file-or-folder]... [--check]` | Tidies scripts and pages in place, or with `--check` lists the files that aren't formatted and exits 1. The server's `textDocument/formatting` uses the same formatter (`KScript.Language/Formatting`). See [Formatting](formatting.md). |

Both follow the project's `kscript.json` (see [Project settings](projects.md#project-settings-kscriptjson)): `lint.rules` changes or turns off diagnostics by code, `lint.exclude` silences
files and folders, and `lint.include` chooses what a folder check covers. The server re-reads `kscript.json` when it changes.
The rules are applied in `LintRules.Apply` (KScript.Language), after analysis, so the analyzer itself never needs to know about them.

The VS Code extension is in [`editors/vscode`](../editors/vscode). It starts the installed `kscript lsp` (found
through `kscript.server.path`, then the installers' folders, then `PATH`) and adds a TextMate grammar, plus an injection
grammar for `{ expressions }` in pages. In that folder: `npm test` runs the unit tests, `npm run package` builds
`kscript-vscode.vsix`, `npm run smoke -- <KScript.dll>` checks the protocol against a built server, and
`npm run test:vscode -- <KScript.dll>` runs the extension inside a downloaded VS Code. `kscript ide install --vsix
kscript-vscode.vsix` installs a local build.

## How it is built

| Project | Contents |
| --- | --- |
| `KScript.Syntax` | Tolerant markup parser (XML for scripts, HTML5 rules for pages), the KScript 2 expression and template parsers, and the metadata types (`ElementInfo`, `CallableInfo`) packages describe themselves with. The interpreter uses the same parsers. |
| `KScript.Language` | The catalog, the analyzer and the editor features. |
| `KScript.LanguageServer` | A small JSON-RPC/LSP layer over `KScript.Language`, plus `kscript check` and `kscript fmt`. |
| `KScript.Language.Tests` | Parser, analyzer, feature, protocol and catalog contract tests. |

### Types

`TypeInference` gives every expression a `TypeShape`: a type, the fields of a map and the item shape of a list.
Shapes come from literals, the catalog's return types, `@read_json` files, loop and lambda items, parameter
types and defaults, and what functions return (`Analyzer.Types.cs`). A function's return type comes from its
`value` or its `<return>`s; because a call can come before the function, `Analyzer.Analyze` repeats the analysis
(at most five passes) until the return types stop changing. Imported files are analysed for their functions'
return types and top-level variables. `<set>` fills in a variable whose type wasn't known (declared as `null`).

Shapes drive member completion, hovers (`TypeShape.Pretty`), inlay hints (`textDocument/inlayHint`) and method
details. The server also answers `textDocument/foldingRange` and its own `kscript/blocks` request, which the VS Code
extension uses to draw notebook-style lines between a script's top-level blocks.

The parsers never throw and keep exact source positions (including through `&lt;` entities), so every feature works
on half-typed code. Entry points: `ExpressionParser.ParseExpression`, `TemplateParser.Parse`, `MarkupParser.Parse`
and `Analyzer.Analyze`.

## Describing the language: the catalog

`LanguageCatalog` is the single source of truth for what the tools know. It is built from the packages the
interpreter has (`PackageRegistry`): each package registers its elements, commands, methods and variables with an
`ElementInfo` or `CallableInfo` that holds the description, attributes, parameters, example and placement rules, so a
new element, command or method shows up in completion, hover and diagnostics without touching the language server.
Every entry records its package, which is how the analyzer knows a script needs `<import package="...">`.

KScript 1 elements, attributes and commands are in `KScript.Runtime.Retired`, with what to write instead; both the
interpreter and the analyzer report them from there. Parts of the KScript 2 proposal that no package implements yet
(such as `<llm>`) are described in `LanguageCatalog` itself and marked *planned*; once a package implements one, the
package wins.

A script imports KScript.Core, the packages in the nearest `kscript.json`, the ones named in `<import package>`, and
the owner of any `<KScript.X>` element. Pages need no imports.

The `kscript serve` page engine (`KScript.Web/Pages`) runs pages, including `<live>`, `<state>`, `on-*` handlers and
`bind`. `PageEngineContractTests` read the engine's source and fail when one of its methods, commands or elements is
missing from the catalog. The tests also check that `Example Sites/team` analyzes without errors.

`CatalogContractTests` keep packages and tools in step: every package element, command and method must be in the
catalog with a description and a handler, no two packages may define the same name, renames must point at real
elements and attributes, and examples must analyze cleanly. When one fails after a package change, the fix is almost
always in the description the package registers.

## Diagnostic codes

| Range | Area |
| --- | --- |
| KS1xxx | Markup (malformed XML, unclosed tags, front matter) |
| KS2xxx | Expressions (syntax, assignment outside `let`/`set`/`do`, bare identifiers) |
| KS3xxx | Elements and attributes (unknown, retired, missing, invalid values, placement), imports (KS3009 missing import, KS3010 unknown package) |
| KS4xxx | Names (undefined variables and functions, constants, duplicates) |
| KS5xxx | Calls (unknown or retired commands and methods, argument counts, type mismatches), KS5010 a field a `<type>`'s records don't have |

The constants are in `DiagnosticCodes`; each has positive and negative tests in `AnalyzerTests`.
