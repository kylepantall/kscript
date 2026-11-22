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
| `KScript.Terminal/`, `KScript.Math/`, `KScript.IO/`, `KScript.Net/`, `KScript.Markdown/`, `KScript.AI/`, `KScript.Style/`, `KScript.Storage/`, `KScript.Cache/`, `KScript.Auth/` | Packages: each registers its elements, commands and methods, with their documentation. |
| `KScript.Terminal/`, `KScript.Math/`, `KScript.IO/`, `KScript.Net/`, `KScript.Markdown/`, `KScript.AI/`, `KScript.Style/`, `KScript.Storage/`, `KScript.Cache/`, `KScript.Document/` | Packages: each registers its elements, commands and methods, with their documentation. |
| `KScript.Web/` | The `kscript serve` page engine (`Pages/`) and the KScript.Web package. |
| `KScript.Http/` | Routes, controllers and middleware: the KScript.Http package, the route table (`Routing.cs`), request handling (`HttpApp`) and the built-in middleware. |
| `KScript.UI/` | Desktop windows: the KScript.UI package, the redraw model (`UISession`) and the Avalonia host that shows it on macOS, Windows and Linux. |
| `KScript.Test/` | The KScript.Test package and `kscript test`: finding test files, running them, and the console and JUnit reports. |
| `KScript.Cli/` | `kscript make` (`Scaffolding/`, built-in templates in `Scaffolding/Templates/`, the `kscript make project` templates in `Scaffolding/Projects/web/` and `Scaffolding/Projects/bare/`, each described by its `kscript-template.json`), `kscript ide`, `kscript update` and `kscript compile` (`Compile/`: the single-file bundle reader and writer, package trimming, Windows resources, Mac signing and icons). |
| `KScript.Docs/` | `kscript docs`: the docs website, built from `docs/*.md` and the language catalog, with the sandbox (`Playground`) that runs its examples. The examples of commands and methods live with them, in each package's `p.Examples(...)`. |
| `KScriptApp/` | The `kscript` command line app. |
| `KScript.Language/`, `KScript.LanguageServer/` | The analyzer and editor features, and the LSP server behind `kscript lsp` and `kscript check`. They read what each package registers, so the editor always matches the interpreter. |
| `KScript.Tests/`, `KScript.Language.Tests/` | xUnit tests for the interpreter and packages, and for the language tools (including contract tests that keep them in step with the packages). |
| `editors/vscode/` | The VS Code extension. |
| `samples/kscript2/` | Sample scripts. |
| `samples/life-lab/` | Life Lab: one KScript program that runs in a terminal, in the browser and as a desktop app. |
| `samples/auth-app/` | A site with accounts, made by `kscript make auth --api` (see [Sign-in](auth.md)). |
| `Example Sites/team` | A sample `kscript serve` site. |

## Tests and CI

`KScript.Tests` covers the interpreter, every package, `kscript serve` pages, `kscript make`, `kscript.json` and the sample scripts; `KScript.Language.Tests` covers the analyzer, the editor features and the language server. Run them with `dotnet test KScript.sln`.

Every push to `master` and every pull request runs [the CI workflow](../.github/workflows/ci.yml), which restores, builds and tests the solution in Release mode, runs `samples/kscript2/oldest.ks.xml`, checks the sample scripts with `kscript check`, checks their formatting with `kscript fmt --check` and runs their KScript tests with `kscript test`, on three platforms:

- **Linux** runs on a self-hosted runner (`home-ci`). Pull requests from forks always use a GitHub-hosted `ubuntu-latest` runner instead, so untrusted code never runs on the self-hosted machine.
- **Windows** and **macOS** run on GitHub-hosted `windows-latest` and `macos-latest` runners.

### Native AOT

A CI job also publishes kscript with Native AOT on Linux (`-p:PublishAot=true`) and runs scripts, `kscript serve`, `kscript docs`, `kscript mcp` and the language server with it. Releases still ship the single-file build; the job keeps the AOT build working so it stays an option. It fails on any new trim or AOT warning, so:

- Write JSON with `JsonNode`/`JsonObject` or a source-generated `JsonSerializerContext` (see `OpenApiJsonContext`), not `JsonSerializer.Serialize(object)`. Add to a `JsonArray` with `array.Add((JsonNode)value)`: the generic `Add<T>` isn't AOT-safe.
- A new built-in package needs a `DynamicDependency` on `PackageRegistry.Get` as well as its line in `PackageRegistry.BuiltIn`; `NativeAotTests` checks the two lists match.

To try it locally: `dotnet publish KScriptApp/KScriptApp.csproj -c Release -r linux-x64 -p:PublishAot=true -o aot` (needs `clang` and `zlib1g-dev`). `KScript.AI` calls the Messages API directly rather than through the Anthropic SDK, because the SDK serialises with reflection.

## Changing a command, method or element

The reference in `kscript docs`, editor completion and hover, and `kscript check` all read the same catalog: what each package registers in its `Register` method. Change a command's parameters there and every one of them changes with it.

To retire something, deprecate it first, so scripts keep working while they move off it:

```csharp
p.Deprecate("@read_file", "Reading text has a clearer name", "@read_text");   // a command
p.Deprecate("@api(retries)", "Requests are retried for you");                // one of its parameters
p.Deprecate("string->shout", "", "->upper");                                   // a method
p.Deprecate("<beep>", "", "<sound>");                                          // an element
p.Deprecate("<sound tone>", "Renamed", "pitch");                               // one of its attributes
```

The first text says why and the second is what to use instead, as written in a script. Then:

- `kscript check` and the editor warn (KS4008) where the script uses it, with a quick fix when the replacement is a plain rename.
- Completion shows it struck through and lists it last, and hover says it is deprecated.
- The reference strikes it through, labels it **deprecated** and shows why and what to use instead.

`Deprecate` throws if the key names nothing, so removing a parameter later can't leave a stale deprecation behind.

The guides in `docs/` and the README are written by hand, so a test keeps them honest: `DocsTests.Every_example_in_the_guides_and_readme_matches_the_code` checks every `kscript` and `xml` example with the analyzer. It fails if an example uses an element, attribute, command, method or parameter that no longer exists, passes the wrong number of arguments, or uses something deprecated.

## How the standard packages behave

Every package follows the same rules, so what you learn in one works in the next. `StdlibBehaviourConventionTests` walks the whole catalog and fails when a package breaks one.

**A name that answers true or false starts with `is_` or `has_`.** `$ip.is_private`, `$response.is_ok`, `$password->is_strong()`, `@is_signed_in()`, `$cache->has(key)`. The only other names allowed are verbs that ask a question about an argument, such as `->contains(x)`, `->starts_with(x)`, `->matches(x)` and `@exists(path)`, and actions that report whether they worked, such as `->remove(key)`. Both lists are short and are written out in the test. Descriptions say "True when ...".

**A plain fact is a field; a method takes arguments or does work.** A field is read with a dot and no brackets: `$response.status`, `$file.name`, `$file.size`, `$date.year`, `$e.status`. A method is called with `->`: `$response->json()`, `$file->read_text()`, `$date->format('yyyy')`. A package adds a field with `p.Field(receiver, name, type, description)`. Calling a field (`$response->status()`) is an error that says to drop the brackets. A value whose fields are the user's own (a record's columns, a map's keys, `$config`'s settings) keeps its own facts as methods, so they can't clash with a column or setting of the same name.

**Counting is `->count()` everywhere.** A list, a text and a map have no fields of their own (a map's are the user's keys), so "how many" is the method on all three, and on a query, a range and a fake queue too: `$list->count()`, `$name->count()`, `$map->count()`. It takes no arguments. "How many match" is `->count_of(match)`. There is no `->length()` and no `.count` or `.length` field.

**One name for each thing.** `StdlibNameConventionTests` walks every command and method and fails on a second spelling. Methods are the main form: there is no command that only repeats a method of its first argument (`$text->upper()`, not `@to_upper($text)`), apart from a short list in the test, such as KScript.Math's `@sqrt(x)` beside `$x->sqrt()`. A command and a method with the same name take the same parameter names. An action has one form: a command when it takes values and gives a value back (`@move(from, to)`, `@download(url, to)`, `@assert(condition)`, `Job(...)->dispatch()`), a statement only when it needs a block or prompt text (`transaction { }`, `llm`, `<respond>` in a page). Every identifier says `color`; prose stays British.

**A name that is taken away says what to write instead.** `Retired` (KScript.Core/Runtime/Retired.cs) holds removed statements, arguments and commands, and `RetiredMembers` holds removed methods by receiver type. The runtime stops with the replacement and the editor offers a quick fix when it is a plain rename. Nothing retired keeps working.

**Settings are read one way.** `$config.database.host`, and `$config->get('database.host', 'localhost')` when the path is built at run time or needs a default.

**Things kept by key use `get`, `set`, `has` and `remove`.** `clear` empties the whole thing. `delete` is for files and records (`$storage->delete(name)`, `$record->delete()`, `kscript queue delete`). `flush` only ever means "write out what is being held back" (`$log->flush()`). Storage reads and writes files, so it keeps `read`, `write` and `delete`.

**A length of time is a duration.** `wait(duration: 500ms)`, `Timer(every: 100ms)`, `test(timeout: 5s)`, `@api($url, timeout: 30s)`, `job(timeout: 5m)`, `$cache->set(key, value, ttl: 10m)`. A duration is written as a number and a unit with no quotes: `ms`, `s`, `m`, `h`, `d` or `w`, and they can be joined (`1h30m`). A plain number is always seconds. There are no `ms:` or `seconds:` arguments. Declare an attribute as a duration with `new A(name, K.Expression, ...) { Type = Duration.TypeName }` and a parameter as `name?: duration`, and read it with `run.Seconds(name)` or `Duration.Seconds(value, what)`.

**A table takes the same arguments everywhere.** The terminal's `table` and a document's `Table` both take `rows` (a list of maps, or a list of lists whose first row is the headings), `columns` (a list of keys, or a map from heading to key), `align` (a list of `left`, `center` or `right`) and `header: false` for no heading row. Both read them with `TableArguments`.

## Writing an error message

A script is a `.kscript` file or an XML one, and a message is written once for both. Write it in XML terms, as an interpolated string given straight to the error:

```csharp
throw run.Error($"<{run.Name}> needs a {name} attribute, such as <{run.Name} {name}=\"5\" />.", name, "Syntax");
```

The compiler turns that string into a `Wording` (`KScript.Syntax/Text/Wording.cs`). An XML script gets it as written. A `.kscript` file gets it in its own terms: tags become statements as the converter writes them, `name="value"` becomes `name: value`, and "element" and "attribute" become "statement" and "argument". Only the words in the source are changed. The values put into the holes are left alone, and so is any plain string, such as a message a script wrote itself.

- `new KScriptException($"...")`, `run.Error($"...")`, `c.Error($"...")` and the analyzer's `Report(..., $"...")` all take a `Wording`.
- A message joined with `+` or picked with `?:` is a plain string, so wrap each part that has XML wording: `Wording.Of($"Unknown element <{name}>.") + (close != null ? Wording.Of($" Did you mean <{close}>?") : null)`.
- `Wording.Prose(text)` rewords text that was written elsewhere, such as an attribute's description. `Wording.Snippet(xml)` converts a line of XML script. `new Wording(xml, code)` gives each form its own sentence when rewording wouldn't say it well.
- To pass an error's message on, use `ex.Words`, not `ex.Message`.

`DiagnosticConventionTests` holds the guards: it runs a list of failing scripts as `.kscript` and fails if a message shows a tag, `name="value"`, "attribute" or "element", and it scans the source for messages with XML wording that are plain strings. The same file checks that every error type the runtime raises is declared in `ErrorTypes.cs` and listed in [the language guide](language.md#errors), and that every diagnostic code is reported somewhere and documented in [errors.md](errors.md) in numeric order.

## Syntax highlighting on GitHub

GitHub highlights code with [Linguist](https://github.com/github-linguist/linguist), which only knows the languages that have been added to it. A repository can't bring its own grammar, so until KScript is in Linguist:

- Code blocks in Markdown use ` ```xml ` for scripts and ` ```html ` for `kscript serve` pages. GitHub highlights the markup, though not KScript's `{ }` expressions.
- `.gitattributes` marks `*.ks` and `*.ks.xml` files as XML, so they are highlighted in the file view and counted as XML in the language bar.

KScript already has a full TextMate grammar in [`editors/vscode/syntaxes/kscript.tmLanguage.json`](../editors/vscode/syntaxes/kscript.tmLanguage.json), the format Linguist uses. Linguist adds a new language once its file extension is used in at least 200 different repositories on GitHub. When KScript gets there, the grammar can be submitted to Linguist, and ` ```kscript ` code blocks and `.ks.xml` files will then be highlighted as KScript everywhere on GitHub.
