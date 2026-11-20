# Standards

A standard is a named set of lint rules and format settings. KScript's own defaults are a standard,
`kscript:recommended`, and every project uses it unless its `kscript.json` names another. You can write your own
standard, build it on top of KScript's, and share it between projects. Each project can still change single rules.

```jsonc
// kscript.json
{
  "standard": "../shared/acme",                  // a folder holding kscript-standard.json, or a file
  "lint": { "rules": { "KS3003": "off" } },     // this project's own changes win over the standard
  "format": { "printWidth": 100 }
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
    "warningsAsErrors": true
  },
  "format": { "indent": 4, "maxBlankLines": 2 }
}
```

## Built-in standards

| Standard | What it sets |
| --- | --- |
| `kscript:recommended` | The default. Style rules (KS6xxx, such as [KS6001 early returns](language.md#early-returns-instead-of-else) and the [unused code](language.md#unused-code) rules KS6002 to KS6005) are warnings. The formatter's layout is written out in full: `printWidth` 120, `maxBlankLines` 1, `endOfLine` auto, and every format rule on. The indent is left to the editor's tab settings (2 for `kscript fmt`), so set `format.indent` in your own standard to pin it. |
| `kscript:strict` | Extends `kscript:recommended`. Style rules are errors, and `lint.warningsAsErrors` is on, so `kscript check` fails on any warning. |

The correctness checks (KS1xxx to KS5xxx: unknown elements, undefined variables, wrong argument counts and so on) are
always on at their built-in severity. A standard can change them, but it doesn't have to list them. Style rules
(KS6xxx) are opinions, so they are only on when a standard turns them on. That means:

- `"standard": []` uses no standard at all: correctness checks only, style rules off, formatter defaults.
- A standard of your own that has no `extends` also starts with style rules off. Add
  `"extends": "kscript:recommended"` to keep them.

To see what a built-in standard sets, run `kscript check --print-config` in a folder without a `kscript.json` (or
with `"standard": "kscript:strict"`). They are defined in `KScript.Core/Projects/Standards.cs`, in the same JSON a
standard file holds.

## Writing a standard

A standard file holds `name`, `description`, `extends`, `lint` and `format`. Comments and trailing commas are allowed.

- `lint` takes `rules` (severity by code or prefix: off, hint, info, warning or error) and `warningsAsErrors`.
- `format` takes every format setting (`indent`, `tabWidth`, `printWidth`, `maxBlankLines`, `endOfLine`, `preserve`,
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
`kscript check` stops with exit code 2. Unknown settings are warnings.

## Which setting wins

Settings are applied in layers. Later layers win:

1. KScript's built-in defaults.
2. The standards `kscript.json` names, in order. Each standard applies the standards it `extends` first (in order),
   then its own settings.
3. `kscript.json`'s own `lint` and `format` settings.

`kscript check --warnaserror` turns `warningsAsErrors` on whatever the layers say. In an editor, its tab settings
only fill in the indent when no layer sets `format.indent`.

Single settings, each format rule and each lint code are replaced one at a time, so a project that changes
`printWidth` keeps the rest of its standard. `preserve` is replaced as a whole list.

Lint rules can be written as a code (`KS6001`) or a prefix (`KS6*`). Inside one layer, the longest match wins, as
before: `{ "KS3*": "warning", "KS3001": "error" }` keeps unknown elements as errors. Across layers, a prefix replaces
what earlier layers said about the codes it covers, so a project's `"KS6*": "off"` really turns off every style
rule, even one its standard set to `"error"` by code.

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

  lint.warningsAsErrors                true      ../shared/acme/kscript-standard.json
  lint.rules.KS3003                    off       kscript.json
  lint.rules.KS6*                      error     ../shared/acme/kscript-standard.json
  format.indent                        4         ../shared/acme/kscript-standard.json
  format.printWidth                    100       kscript.json
  format.maxBlankLines                 2         ../shared/acme/kscript-standard.json
  format.endOfLine                     auto      kscript:recommended
  ...
```

`kscript check`, `kscript fmt` and the editor all read standards the same way. The language server notices when you
edit a standard file, even before you save it, and re-checks open files.
