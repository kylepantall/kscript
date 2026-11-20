# Working on KScript

KScript is open source, and contributions are welcome. This page covers building it from source, how the repository is laid out, and how it is tested.

## Building from source

KScript targets .NET 8 and runs on Windows, Linux and macOS. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download), then:

```sh
dotnet build KScript.sln
dotnet test KScript.sln

# Run a script
dotnet run --project KScriptApp -- "path/to/script.xml"
```

To build a single-file `kscript` binary for your machine:

```sh
dotnet publish KScriptApp -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out

## Repository layout

| Path | Contents |
| --- | --- |
| `KScript.Syntax/` | The parser shared by the interpreter and the editor tools: markup, expressions, `{ }` templates, and the metadata types elements and commands are described with. |
| `KScript.Core/` | The interpreter (`Runtime/`), the KScript.Core package (variables, control flow, functions, errors, `<echo>`, value methods), the package system and `kscript.json` (`Projects/`). |
| `KScript.Terminal/`, `KScript.Math/`, `KScript.IO/`, `KScript.Net/`, `KScript.Markdown/` | Packages: each registers its elements, commands and methods, with their documentation. |
| `KScript.Web/` | The `kscript serve` page engine (`Pages/`) and the KScript.Web package. |
| `KScript.Http/` | Routes, controllers and middleware: the KScript.Http package, the route table (`Routing.cs`), request handling (`HttpApp`) and the built-in middleware. |
| `KScript.UI/` | Desktop windows: the KScript.UI package, the redraw model (`UISession`) and the Avalonia host that shows it on macOS, Windows and Linux. |
| `KScript.Test/` | The KScript.Test package and `kscript test`: finding test files, running them, and the console and JUnit reports. |
| `KScript.Cli/` | `kscript make` (`Scaffolding/`, built-in templates in `Scaffolding/Templates/`, the `kscript make project` template in `Scaffolding/Project/`, described by its `kscript-template.json`), `kscript ide`, `kscript update` and `kscript app build` (`Apps/`). |
| `KScriptApp/` | The `kscript` command line app. |
| `KScript.Language/`, `KScript.LanguageServer/` | The analyzer and editor features, and the LSP server behind `kscript lsp` and `kscript check`. They read what each package registers, so the editor always matches the interpreter. |
| `KScript.Tests/`, `KScript.Language.Tests/` | xUnit tests for the interpreter and packages, and for the language tools (including contract tests that keep them in step with the packages). |
| `editors/vscode/` | The VS Code extension. |
| `samples/kscript2/` | Sample scripts. |
| `samples/life-lab/` | Life Lab: one KScript program that runs in a terminal, in the browser and as a desktop app. |
| `Example Scripts/` | More sample scripts. |
| `Example Sites/team` | A sample `kscript serve` site. |

## Tests and CI

`KScript.Tests` covers the interpreter, every package, `kscript serve` pages, `kscript make`, `kscript.json` and the sample scripts; `KScript.Language.Tests` covers the analyzer, the editor features and the language server. Run them with `dotnet test KScript.sln`.

Every push to `master` and every pull request runs [the CI workflow](../.github/workflows/ci.yml), which restores, builds and tests the solution in Release mode, runs `Example Scripts/Find Oldest Person.xml`, checks the sample scripts with `kscript check`, checks their formatting with `kscript fmt --check` and runs their KScript tests with `kscript test`, on three platforms:

- **Linux** runs on a self-hosted runner (`home-ci`). Pull requests from forks always use a GitHub-hosted `ubuntu-latest` runner instead, so untrusted code never runs on the self-hosted machine.
- **Windows** and **macOS** run on GitHub-hosted `windows-latest` and `macos-latest` runners.

## Syntax highlighting on GitHub

GitHub highlights code with [Linguist](https://github.com/github-linguist/linguist), which only knows the languages that have been added to it. A repository can't bring its own grammar, so until KScript is in Linguist:

- Code blocks in Markdown use ` ```xml ` for scripts and ` ```html ` for `kscript serve` pages. GitHub highlights the markup, though not KScript's `{ }` expressions.
- `.gitattributes` marks `*.ks` and `*.ks.xml` files as XML, so they are highlighted in the file view and counted as XML in the language bar.

KScript already has a full TextMate grammar in [`editors/vscode/syntaxes/kscript.tmLanguage.json`](../editors/vscode/syntaxes/kscript.tmLanguage.json), the format Linguist uses. Linguist adds a new language once its file extension is used in at least 200 different repositories on GitHub. When KScript gets there, the grammar can be submitted to Linguist, and ` ```kscript ` code blocks and `.ks.xml` files will then be highlighted as KScript everywhere on GitHub.
