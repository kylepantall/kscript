# KScript 2 language server

The language server gives editors diagnostics, completion, hover, signature help, go to definition, find references,
rename, an outline, semantic highlighting, quick fixes and document links for KScript 2 scripts (`.ks.xml`, `.ks`),
pages (`.html`) and Markdown pages (`.md`). It targets KScript 2 only: v1 elements, attributes and commands are
reported as retired, with the KScript 2 replacement and a quick fix where one is mechanical.

## Using it

| Command | What it does |
| --- | --- |
| `kscript lsp` | Runs the server over stdio (LSP 3.17). Any LSP client can use it. |
| `kscript check [file-or-folder]... [--warnaserror]` | Prints problems as `file(line,col): error KS4001: ...` and exits 1 on errors, for CI. With no path it checks the project the current folder is in. |

Both follow the project's `kscript.json` (see the README): `lint.rules` changes or turns off diagnostics by code, `lint.exclude` silences
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
| `KScript/Metadata` | The attributes the interpreter uses to describe itself (below). |
| `KScript.Language` | Tolerant markup parser (XML for scripts, HTML5 rules for pages), the KScript 2 expression and template parsers, the catalog, the analyzer and the editor features. No dependencies beyond `KScript`. |
| `KScript.LanguageServer` | A small JSON-RPC/LSP layer over `KScript.Language`, plus `kscript check`. |
| `KScript.Language.Tests` | Parser, analyzer, feature, protocol and catalog contract tests. |

The parsers never throw and keep exact source positions (including through `&lt;` entities), so every feature works
on half-typed code. Entry points: `ExpressionParser.ParseExpression`, `TemplateParser.Parse`, `MarkupParser.Parse`
and `Analyzer.Analyze`.

## Describing the language: the catalog

`LanguageCatalog` is the single source of truth for what the tools know. It is built by reflection from the
interpreter, so a new element, command or method shows up in completion, hover and diagnostics without touching the
language server:

| Attribute | Put it on | Meaning |
| --- | --- | --- |
| `[KScriptElement(content, ...)]` | An element class | It is a KScript 2 element. Sets the content kind, `Context` (script, page or both), `MustFollow`, `MustBeInside`, `IsLoop`, `HtmlWins`. |
| `[KScriptAttribute(name, kind, description)]` | An element class | Declares an attribute, its value kind (expression, template, variable to declare, path, enum...) and `Required`/`Options`. |
| `[KScriptDoc(description)]` | Elements and commands | The hover text; `Example` (checked by tests) and `Returns`. |
| `[KScriptParam(name, description)]` | A command class | Documents a constructor parameter. |
| `[KScriptValue(kind)]` | An element property | Overrides the inferred kind of a v1 `[KScriptProperty]`. |
| `[KScriptRetired(replacement)]` | Classes or properties | Not part of KScript 2. `Rename` names the replacement for an automatic fix. |

Value methods (`->upper()`) come from `ValueMethods.Catalog()`. Parts of the KScript 2 proposal that the interpreter
doesn't implement yet are described in `LanguageCatalog` itself and marked *planned*; once a class implements one,
the class wins.

The `kscript serve` page engine (`KScript/Pages`) already runs pages, including `<live>`, `<state>`, `on-*`
handlers and `bind`. What it adds is described in `LanguageCatalog.AddPageEngine`, and `PageEngineContractTests`
read the engine's source and fail when one of its methods, commands or elements is missing from the catalog. The
tests also check that `Example Sites/team` analyzes without errors.

`CatalogContractTests` keep the two in step: every interpreter element and command class must be classified, every
documented parameter must exist, renames must point at real elements and attributes, examples must analyze cleanly,
and every `ValueMethods` entry must be in the catalog. When one fails after an interpreter change, the fix is almost
always an attribute on the class you touched.

## Diagnostic codes

| Range | Area |
| --- | --- |
| KS1xxx | Markup (malformed XML, unclosed tags, front matter) |
| KS2xxx | Expressions (syntax, assignment outside `let`/`set`/`do`, bare identifiers) |
| KS3xxx | Elements and attributes (unknown, retired, missing, invalid values, placement) |
| KS4xxx | Names (undefined variables and functions, constants, duplicates) |
| KS5xxx | Calls (unknown or retired commands and methods, argument counts, type mismatches) |

The constants are in `DiagnosticCodes`; each has positive and negative tests in `AnalyzerTests`.
