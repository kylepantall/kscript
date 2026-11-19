# KScript

[![CI](https://github.com/kylepantall/kscript/actions/workflows/ci.yml/badge.svg)](https://github.com/kylepantall/kscript/actions/workflows/ci.yml)

KScript is a scripting language written in XML. A KScript document is parsed into a tree of objects and run by a small .NET 8 interpreter on Windows, Linux and macOS.

KScript is still in active development, and its syntax may change between versions.

## A first script

```xml
<kscript dynamic_defs="yes">
  <!-- With dynamic_defs, variables are created the first time they are used -->

  <!-- Ask a question and store the answer in 'entered_name' -->
  <input to="entered_name" type="text">What's your name?</input>

  <!-- Trim the answer, capitalise it and print it -->
  <echo>Hello, $entered_name->trim()->upper()!</echo>
</kscript>
```

Add `print_info="yes"` to the `<kscript>` tag to print extra information while the script runs.

More examples live in [`DebugScripts/`](DebugScripts) and [`Example Scripts/`](Example%20Scripts).

## Building and running

KScript targets .NET 8 and runs on Windows, Linux and macOS. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download), then:

```sh
dotnet build KScript.sln
dotnet test KScript.sln

# Run a script
dotnet run --project KScriptApp -- "path/to/script.xml"
```

To produce a standalone executable for a platform:

```sh
dotnet publish KScriptApp -c Release -r linux-x64   # or win-x64, osx-x64, osx-arm64, ...
```

Command line options:

| Option | Description |
| --- | --- |
| `-s` | Quiet: don't print the script timing, even if the script sets `quiet="no"`. |
| `-nowait` | Don't wait for a key press when the script finishes (never waits when input is piped). |
| `-cai` | Clear the console after each input. |
| `-all` | Print information about all available KScript objects. |
| `-lang=<culture>` | Set the script language/culture. |
| `-guid` | Print a new GUID and exit. |
| `-root` | Open the KScript install directory. |
| `-admin` | Restart with administrator privileges (Windows only; use `sudo` elsewhere). |

Anything after the script path is available to the script through `@args()`.

## Repository layout

| Path | Contents |
| --- | --- |
| `KScript/` | The language: parser, interpreter, elements (`Arguments/`), `@commands` (`Commands/`) and value methods. |
| `KScriptApp/` | The `KScript` command line app that runs a script file. |
| `KScript.Tests/` | xUnit test suite. |
| `DebugScripts/`, `Example Scripts/` | Sample scripts. |

## Tests and CI

`KScript.Tests` is an xUnit project covering the command parser, commands, control flow, variables, value methods, pipes and lambdas, platform helpers, and the sample scripts. Run it with `dotnet test KScript.sln`. The tests capture console output, so they run one at a time rather than in parallel.

Every push to `master` and every pull request runs [the CI workflow](.github/workflows/ci.yml), which restores, builds and tests the solution in Release mode and then runs `DebugScripts/filterOldestFromArray.xml` on three platforms:

- **Linux** runs on a self-hosted runner (`home-ci`). Pull requests from forks always use a GitHub-hosted `ubuntu-latest` runner instead, so untrusted code never runs on the self-hosted machine.
- **Windows** and **macOS** run on GitHub-hosted `windows-latest` and `macos-latest` runners.

## Elements

A script is a `<kscript>` document made of elements. The main groups are:

| Group | Elements |
| --- | --- |
| Output and input | `echo` `menu` `input` `ginput` `picker` `anykey` `clear` |
| Variables | `def` `defm` `update` `updateContent` `temp` `guard` |
| Control flow | `if` `else` `for` `forxy` `dowhile` `forever` `break` `continue` `end` `wait` `group` |
| Arrays | `array` `marray` `ainsert` `aremove` `minsert` `afor` `aecho` |
| Errors | `try` `catch` `throw` `onexception` |
| Methods | `method` `call` `run` `func` |
| Files and network | `file_info` `move` `new_dir` `write` `download` `loadHtml` |
| Other | `load` `ref` `thread` `watcher` `watch` `rule` `registry` `languages` `math` |

Run any script with `-all` (for example `dotnet run --project KScriptApp -- script.xml -all`) to print every element and command with its properties and examples.

## Command syntax

Commands are written `@name(argument, argument, ...)`. Arguments can be `'quoted strings'`, nested commands or plain text, and spaces around arguments are ignored:

```xml
<echo>@to_lower(@split('A,B,C', ',', '1'))</echo>   <!-- b -->
<echo>@math(2 * (3 + 4))</echo>                     <!-- 14 -->
```

An `@` that isn't followed by a name and `(` is plain text, so e-mail addresses and apostrophes in text are safe.

## Methods, pipes and lambdas

Any value (a variable, a `'quoted string'`, a number or a command's result) can have methods called on it with `->`. Calls can be chained and can take arguments:

```xml
<echo>$name->trim()->upper()</echo>
<echo>'a.b.c'->replace('.', '/')</echo>
<echo>3.14159->round(2)</echo>
<echo>@math('2+2')->mul(10)</echo>
<for to="i" while="$i->lt(10)" math="$i->increment()"> ... </for>
```

Values are typed on use: a value is a number if it parses as one, a bool if it is yes/no/true/false, and a string otherwise (`$x->type()` tells you which). Calling a number method on text gives a clear error. On a variable, `increment()`/`decrement()` also update the variable; on other values they just return the result.

| Kind | Methods |
| --- | --- |
| Any | `type` `length` `is_number` `is_bool` `is_empty` `has_value` `is_url` `equals(x, ignore_case?)` `default(x)` `call(args...)` |
| String | `upper` `lower` `trim` `trim_start` `trim_end` `reverse` `contains(x)` `starts_with(x)` `ends_with(x)` `index_of(x)` `replace(a, b)` `substring(start, len?)` `char_at(i)` `split(sep, i)` `count(x)` `repeat(n)` `pad_left(w, c?)` `pad_right(w, c?)` |
| Number | `add` `sub` `mul` `div` `mod` `pow` `round(d?)` `floor` `ceil` `abs` `min` `max` `lt` `lte` `mt` `mte` `between(a, b)` `is_zero` `not_zero` `positive` `negative` `increment(by?)` `decrement(by?)` |
| Bool | `not` `and(x)` `or(x)` `xor(x)` |

`|>` passes a value on to a method, a command (as its first argument) or a lambda. Lambdas are written `fn($x) => expression`:

```xml
<echo>$name |> trim() |> upper()</echo>
<echo>$csv |> @split(',', '1')</echo>
<echo>$name |> fn($s) => $s->lower()->reverse()</echo>
```

A lambda stored with `<def>` can be piped into or called later:

```xml
<def id="shout">fn($s) => $s->upper()->replace('.', '!')</def>
<echo>'hi.' |> $shout</echo>        <!-- HI! -->
<echo>$shout->call('hey.')</echo>   <!-- HEY! -->
```

A lambda body is one expression; use brackets to group: `fn($n) => ($n->mul(2) |> fn($m) => $m->add(1))`. Lambda parameters are only visible inside the lambda.

Variables are resolved after a line is parsed, so values containing `,`, `)` or `'` are always passed through intact and are never run as commands.

## Writing portable scripts

- Build paths with `@join_path($dir, 'file.txt')` rather than hard-coding `\` or `/`.
- `@k('os.platform')` returns `windows`, `linux` or `macos`; `@k('path.separator')`, `@k('user.home')` and `@k('temp.directory')` are also available.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.
- `<registry>` and `@registry` only work on Windows and report a clear error elsewhere.

## Roadmap

Work in progress, not yet on `master`:

- A browser mode (`kscript serve`) for KScript pages.
- A language server for editor support.
- A proposal for a cleaner KScript 2 syntax.

Further documentation is still in development.
