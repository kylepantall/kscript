# Standards

A standard is a named set of lint rules and format settings. KScript's own defaults are a standard,
`kscript:recommended`, and every project uses it unless its `kscript.json` names another. You can write your own
standard, build it on top of KScript's, and share it between projects. Each project can still change single rules.

```jsonc
// kscript.json
{
  "standard": "../shared/acme",                  // a folder holding kscript-standard.json, or a file
  "lint": { "rules": { "KS3003": "off" } },     // this project's own changes win over the standard
  "format": { "print_width": 100 }
}
```

```jsonc
// ../shared/acme/kscript-standard.json
{
  "$schema": "https://raw.githubusercontent.com/kylepantall/kscript/master/editors/vscode/schemas/kscript-standard.schema.json",
  "name": "acme",
  "description": "Acme house style.",
  "extends": "kscript:recommended",
  "lint": {
    "rules": { "KS6*": "error" },
    "warnings_as_errors": true
  },
  "format": { "indent": 4, "max_blank_lines": 2 }
}
```

## Built-in standards

| Standard | What it sets |
| --- | --- |
| `kscript:recommended` | The default. Style rules (KS6xxx, such as [KS6001 early returns](language.md#early-returns-instead-of-else) and the [unused code](language.md#unused-code) rules KS6002 to KS6005) are warnings. The formatter's layout is written out in full: `print_width` 80, `max_blank_lines` 1, `end_of_line` auto, and every format rule on. The indent is left to the editor's tab settings (2 for `kscript fmt`), so set `format.indent` in your own standard to pin it. |
| `kscript:strict` | Extends `kscript:recommended`. Style rules are errors, and `lint.warnings_as_errors` is on, so `kscript check` fails on any warning. |

The correctness checks (KS1xxx to KS5xxx: unknown elements, undefined variables, wrong argument counts and so on) are
always on at their built-in severity. A standard can change them, but it doesn't have to list them. Style rules
(KS6001 to KS6005, KS6007, KS6008, KS6013 and KS6018) are opinions, so they are only on when a standard turns them
on. The old spellings (KS6006, KS6009 to KS6012, KS6014 and KS6019) are not opinions: in a `.kscript` file each is an error
the engine itself refuses to run, and no standard or `lint.rules` entry changes that
(see [Style](errors.md#style)). That means:

- `"standard": []` uses no standard at all: correctness checks only, style rules off, formatter defaults.
- A standard of your own that has no `extends` also starts with style rules off. Add
  `"extends": "kscript:recommended"` to keep them.

To see what a built-in standard sets, run `kscript check --print-config` in a folder without a `kscript.json` (or
with `"standard": "kscript:strict"`). They are defined in `KScript.Core/Projects/Standards.cs`, in the same JSON a
standard file holds.

## Writing a standard

A standard file holds `name`, `description`, `extends`, `lint` and `format`. Comments and trailing commas are allowed.

- `lint` takes `rules` (severity by code or prefix: off, hint, info, warning or error) and `warnings_as_errors`.
- `format` takes every format setting (`indent`, `tab_width`, `print_width`, `max_blank_lines`, `end_of_line`, `preserve`,
  `rules`, `enabled`). See [Formatting](formatting.md#settings).
- Which files are checked and formatted (`include` and `exclude`) depends on each project's folders, so those stay
  in `kscript.json`. A standard that sets them gets a warning and they are ignored. So are `serve`, `test`, `app` and
  `packages`.

Name a standard file `kscript-standard.json` (then a project can name its folder) or `something.kscript-standard.json`.
The VS Code extension validates and completes both, and any other name works too.

### Where a standard can live

`"standard"` in `kscript.json` and `"extends"` in a standard take a built-in name, a path, or a list of them:

- `kscript:recommended`, `kscript:strict`: built in.
- `./standards/team.kscript-standard.json`: a file, relative to the file that names it. Paths in `extends` are
  relative to the standard, so standards can move together.
- `../shared/acme`: a folder holding `kscript-standard.json`. That makes a standard easy to share as a Git
  submodule, a shared folder, or a package your team checks out next to its projects.
- `~/kscript/standard.json`: a file in your home folder.
- `["kscript:recommended", "./team.json", "./this-repo.json"]`: several, applied in order.

A standard that can't be found, isn't valid JSON, or extends itself (directly or through others) is an error, and
`kscript check` stops with exit code 2. Unknown settings are warnings. Settings are written in snake_case
(`warnings_as_errors`, `print_width`); an old camelCase name (`warningsAsErrors`, `printWidth`) is an error that
names the new one.

## Which setting wins

Settings are applied in layers. Later layers win:

1. KScript's built-in defaults.
2. The standards `kscript.json` names, in order. Each standard applies the standards it `extends` first (in order),
   then its own settings.
3. `kscript.json`'s own `lint` and `format` settings.

`kscript check --warnings-as-errors` turns `warnings_as_errors` on whatever the layers say. In an editor, its tab settings
only fill in the indent when no layer sets `format.indent`.

Single settings, each format rule and each lint code are replaced one at a time, so a project that changes
`print_width` keeps the rest of its standard. `preserve` is replaced as a whole list.

Lint rules can be written as a code (`KS6001`) or a prefix (`KS6*`). Inside one layer, the longest match wins, as
before: `{ "KS3*": "warning", "KS3001": "error" }` keeps unknown elements as errors. Across layers, a prefix replaces
what earlier layers said about the codes it covers, so a project's `"KS6*": "off"` really turns off every style
rule, even one its standard set to `"error"` by code.

## Fixing what can be fixed

`kscript check --fix [file-or-folder]` applies the fixes that are always safe, then checks what is left. It rewrites
every old spelling, which is how a script that no longer runs is brought up to date: text written as a bare word is
put in quotes (KS6009), `yes`/`no` become `true`/`false` (KS6011), a list written as text becomes an array (KS6006),
and the other old spellings are replaced by the one that is kept (KS6010, KS6012, KS6014). It also adds the unit to
a length of time (KS6018). A style rule the project turned off in `lint.rules` is not fixed.
Other problems keep their quick fixes in the editor and in `kscript check --output json`, where each one comes with
its exact text edits.

```
kscript check --fix                        # the current project
kscript check --fix scripts/report.kscript
kscript check --warnings-as-errors         # for CI: fail on warnings too
```

## Checking only what changed

In a project, `kscript check` remembers what it found in each file, and everything the check of that file looked at: the scripts it imports, its components and layouts, the folders it listed, and the other files it searched for uses of its functions. The next check analyses a file again only when the file changed, or something it looked at did. Every other file gives the problems it gave last time, so checking a big project after one edit takes a fraction of the time.

```sh
kscript check                             # analyses only what changed since the last check
kscript check --all                       # analyses every file
```

Saving `lib/billing.kscript` checks it again, along with the scripts that import it and any file that now mentions one of its functions. Changing `kscript.json` or a standard checks everything again, and so does a new version of kscript. The record is kept in `.kscript/cache/check.json`, and that folder ignores itself, so git never sees it.

## Seeing the result

`kscript check --print-config [file-or-folder]` prints the settings that apply to a file or folder, after its
standards and `kscript.json` are combined, and which layer set each one:

```
$ kscript check --print-config
Project: /work/app/kscript.json

Standards (applied in this order; later ones win, and kscript.json wins over all of them):
  kscript:recommended
      The default. Style rules (KS6xxx, such as KS6001 early returns) are warnings, and files use the formatter's standard layout.
  ../shared/acme/kscript-standard.json  "acme"  extends kscript:recommended
      Acme house style.

  lint.warnings_as_errors                true      ../shared/acme/kscript-standard.json
  lint.rules.KS3003                    off       kscript.json
  lint.rules.KS6*                      error     ../shared/acme/kscript-standard.json
  format.indent                        4         ../shared/acme/kscript-standard.json
  format.print_width                    100       kscript.json
  format.max_blank_lines                 2         ../shared/acme/kscript-standard.json
  format.end_of_line                     auto      kscript:recommended
  ...
```

`kscript check`, `kscript fmt` and the editor all read standards the same way. The language server notices when you
edit a standard file, even before you save it, and re-checks open files.
