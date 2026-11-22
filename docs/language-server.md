# KScript 2 language server

The language server gives editors diagnostics, completion, hover, signature help, go to definition, find references,
rename, an outline, semantic highlighting, quick fixes, document links and formatting for KScript 2 scripts (`.kscript`, and `.ks.xml` for the XML form),
pages (`.html`) and Markdown pages (`.md`). It targets KScript 2 only: v1 elements, attributes and commands are
reported as retired, with the KScript 2 replacement and a quick fix where one is mechanical.

## Using it

| Command | What it does |
| --- | --- |
| `kscript lsp` | Runs the server over stdio (LSP 3.17). Any LSP client can use it. |
| `kscript check [file-or-folder]... [--warnings-as-errors]` | Prints problems as `file(line,col): error KS4001: ...` and exits 1 on errors, for CI. With no path it checks the project the current folder is in. A folder with `_layout.html`, `_components/`, `_layouts/` or `kscript.json` is a site, so its `.html` and `.md` pages are checked too. |
| `kscript check --print-config [file-or-folder]` | Prints the lint and format settings that apply, after the project's [standards](standards.md) and `kscript.json` are combined, with the layer that set each one. |
| `kscript fmt [file-or-folder]... [--check]` | Tidies scripts and pages in place, or with `--check` lists the files that aren't formatted and exits 1. The server's `textDocument/formatting` uses the same formatter (`KScript.Language/Formatting`). See [Formatting](formatting.md). |

Both follow the project's `kscript.json` (see [Project settings](projects.md#project-settings-kscriptjson)) and the [standard](standards.md) it names: `lint.rules` changes or turns off diagnostics by code, `lint.exclude` silences
files and folders, and `lint.include` chooses what a folder check covers. The server re-reads `kscript.json` and its standard files when they change, using the open (unsaved) text of either.
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

For running code from the editor, `kscript/tests` lists the tests and `describe` groups of a test file (open or on
disk), each with its full name (`group › test`, as `kscript test --filter` matches it) and range. `textDocument/codeLens`
puts "Run" above a script's first statement and "Run test" above each test. The lenses call the editor commands
`kscript.run`, `kscript.runTest` and `kscript.runTestsInFile` with the document's URI first, so the server only offers
them when the editor sends `initializationOptions: { "codeLens": true }` to say it has those commands. A new, unsaved
`.kscript` document (language id `kscript-code`) is parsed as code even though it has no file name yet.

The same lenses put "3 uses" above each function something calls, or "used in other files" when only another file of
the project does. That lens calls `kscript.showReferences` with the URI and the position of the function's name.
`workspace/symbol` finds the functions and constants of every script in the folders the editor opened
(`workspaceFolders` or `rootUri`), by any part of the name.

Suggestions are ordered by `sortText`: the statements and methods used most come first (`Ranking` in
KScript.Language/Features holds the lists, and a test checks every name in them exists), then the rest by name, and a
script's own variables come before built-in ones, nearest first. Inlay hints name the parameter beside a number, text,
list or map written straight into a call with several arguments. Hovers on commands and methods show the catalog's
example in the file's own format, and end with an "Open docs" link when the editor sends
`initializationOptions: { "docsCommand": "<command>" }`: the link runs that command with the name as its argument.

### Imports and typed values

An import with an alias (`import "../controllers/accounts.kscript" as accounts`) gives the alias a symbol for each
function and constant the file declares (`Symbol.Members`, each with `Alias` set). Uses such as `@accounts.show_login`
and `accounts.limit` are references to those symbols, so completion after `@accounts.` lists them with their signatures
and docs, hover shows them, go to definition opens the imported file, and calls through the alias have their arguments
checked. A name the file doesn't have is KS4005. `textDocument/references` on a function or constant other scripts
import also searches the rest of the project (`LanguageService.GetProjectReferences`). A package alias
(`import KScript.Math as math`) completes and hovers its package's commands.

Where the type of a value is known (`tz: timezone = ‸`, a typed parameter, a named argument), completion lists the
values the type accepts before anything else: an enum's values, and what a native type suggests. A native type supplies
those through `NativeParser.Suggest` (KScript.Core/Runtime/NativeValues.cs), which gets a `NativeValueContext` (the
file, the project folder, what is typed, the type's options and the project's routes, read from the route files
without running them) and returns `NativeValueSuggestion`s. Time zones, countries, currencies, locales, durations,
cron macros, colours and media types use fixed lists (`NativeParser.Fixed`). Hover on a type name and signature help
say what the type accepts. Straight after `@` only commands and functions are offered (those that give the expected type
first), and straight after `$` only variables.

An editor that runs a script can set `KSCRIPT_ERROR_PATHS=full`, and the error a script stops with then names its file
with the whole path (`ERROR  /work/app/hello.kscript:3:5: ...`) so the editor can link to the line.

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
the owner of any `<KScript.X>` element. Pages need no imports, but a page's `<import package>` gives it the package's
variables (`$session` from KScript.Auth).

A KScript.Http view (`views/auth/account.html`) is typed from the scripts that render it: `Analysis/ViewInputs.cs` finds
each `@view('auth/account', ...)` in the project's scripts and declares the values it passes, with their shapes, as the
view's variables. A package says what fields a command's result has with `ResultShapes` (`("@api_tokens", "list<{ id:
string, name: string }>")`), read by `Analysis/ShapeText.cs`, so a loop over `$data.tokens` knows each token's fields.

Completion only offers what the file can use: built-ins and items from the packages it imports, at the top level, inside
functions and after `->`. Using an item from a package that isn't imported is KS3009, with a quick fix that adds the import.

In the block of a .kscript `type` or `model`, a line is a field, a rule or a setting, so completion there offers the
model's settings it doesn't have yet (`timestamps`, `hidden`...) or the type's rules (`min`, `pattern`, `validate`...),
with their docs on hover. After `timestamps: ` it offers yes and no, and inside `hidden: [ ]` and the other field lists
it offers the model's fields. A type given rules where it is used, `title: string(min: 1)`, offers the rules and the
native type's own options (`uuid(v: 4)`). This lives in `Features/Completion.TypeBody.cs`.

A parameter with no type takes one from the calls in its file when they all agree (`Analyzer.Parameters.cs`): the
analysis runs again until those types settle, as it does for return types. The type shows as an inlay hint, and KS6007
warns about the missing type with a quick fix that writes it in. Functions inside a `job` are skipped, because the
queue calls them.

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
| KS2xxx | Expressions (syntax, assignment outside `let`/`set`/`do`, bare identifiers), KS2005 `'single quotes'` around text that names a variable (only `"double quotes"` fill it in) |
| KS3xxx | Elements and attributes (unknown, retired, missing, invalid values, placement), imports (KS3009 missing import, KS3010 unknown package) |
| KS4xxx | Names (undefined variables and functions, constants, duplicates) |
| KS5xxx | Calls (unknown or retired commands and methods, argument counts, type mismatches), KS5010 a field a `<type>`'s records don't have or a misspelt field of a map, KS5011 an error type in `catch`, `throw`, `extends` or `expect_error` that isn't declared or built in, KS5012 a literal that doesn't fit its declared type (`a: Age = 200`, `e: email = bob@`), KS5013 a column a KScript.Data model doesn't have (`User->where(nmae: 'x')`), KS5014 a model the migrations don't match |
| KS6xxx | Old spellings and style. An old spelling in a `.kscript` file (KS6006, KS6009 to KS6012, KS6014) is an error with a quick fix, whatever the standard says, because the script doesn't run. The style rules keep code easy to read: they are only on when a standard turns them on; `kscript:recommended`, the default, makes them warnings. KS6001 flags `else` and `elif` in scripts, where an early `return`, `continue` or `break` reads better (see [Early returns instead of else](language.md#early-returns-instead-of-else)). KS6002 to KS6005 flag unused functions, variables, parameters and imports, counting uses from the rest of the project, and are sent with the `Unnecessary` tag so editors fade the code (see [Unused code](language.md#unused-code)). KS6006 marks a list written as one string in a `.kscript` file, with a fix that writes `[a, b]` |

Every code is explained for users in [Error codes](errors.md), and the server sends that page as each diagnostic's `codeDescription`, so the code in the editor is a link. `DiagnosticCodeDocsTests` fails when a code has no section there.

In a `.kscript` file, messages use that format's words (`Unknown statement 'x'`, never `<x>`), and words from other languages (`print`, `function`, `x++`, `@len()`) are answered with what KScript writes and a quick fix: see `Analyzer.Habits.cs`.

The constants are in `DiagnosticCodes`; each has positive and negative tests in `AnalyzerTests`.
