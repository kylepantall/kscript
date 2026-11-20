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
| `KScript.Test/` | The KScript.Test package and `kscript test`: finding test files, running them, and the console and JUnit reports. |
| `KScript.Cli/` | `kscript make` (`Scaffolding/`, built-in templates in `Scaffolding/Templates/`, the `kscript make project` starter in `Scaffolding/Project/`), `kscript ide` and `kscript update`. |
| `KScriptApp/` | The `kscript` command line app. |
| `KScript.Language/`, `KScript.LanguageServer/` | The analyzer and editor features, and the LSP server behind `kscript lsp` and `kscript check`. They read what each package registers, so the editor always matches the interpreter. |
| `KScript.Tests/`, `KScript.Language.Tests/` | xUnit tests for the interpreter and packages, and for the language tools (including contract tests that keep them in step with the packages). |
| `editors/vscode/` | The VS Code extension. |
| `samples/kscript2/` | Sample scripts. |
| `Example Scripts/` | More sample scripts. |
| `Example Sites/team` | A sample `kscript serve` site. |

## Tests and CI

`KScript.Tests` covers the interpreter, every package, `kscript serve` pages, `kscript make`, `kscript.json` and the sample scripts; `KScript.Language.Tests` covers the analyzer, the editor features and the language server. Run them with `dotnet test KScript.sln`.

Every push to `master` and every pull request runs [the CI workflow](../.github/workflows/ci.yml), which restores, builds and tests the solution in Release mode, runs `Example Scripts/Find Oldest Person.xml`, checks the sample scripts with `kscript check` and runs their KScript tests with `kscript test`, on three platforms:

- **Linux** runs on a self-hosted runner (`home-ci`). Pull requests from forks always use a GitHub-hosted `ubuntu-latest` runner instead, so untrusted code never runs on the self-hosted machine.
- **Windows** and **macOS** run on GitHub-hosted `windows-latest` and `macos-latest` runners.
