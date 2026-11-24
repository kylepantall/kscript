# The samples in XML

KScript scripts are written in `.kscript` files, and every other sample folder uses that form. This folder is the one set kept in the XML form (`.ks.xml`), for readers who use it. Both forms are the same language: `kscript convert` turns either into the other, and [the .kscript format guide](../../docs/code-format.md) lists how each statement is written in XML.

Each script here has a `.kscript` twin with the same name in [`samples/scripts`](../scripts), and the tests check that the two print the same.

| File | What it shows |
| --- | --- |
| `tour.ks.xml` | The language in one script: values, loops, functions, types, errors. |
| `oldest.ks.xml` | Reading a list of people and finding the oldest. |
| `move-files.ks.xml` | Asking questions and moving files. |
| `imports.ks.xml`, `lib/strings.ks.xml` | Importing functions from another script. |
| `repos.ks.xml`, `types/` | Typed JSON: data checked against a declared type. |
| `errors.ks.xml`, `errors/` | Declared error types, thrown and caught by type. |
| `tests/` | Tests, run by `kscript test`. |
| `counter.ks.xml` | A desktop app: a window with a number and two buttons. |
| `site/` | A small site with an XML component class. Its `render()` reads tag props with `$.person`. Serve it with `kscript serve site`. |

```sh
kscript oldest.ks.xml
kscript test
kscript convert oldest.ks.xml --stdout     # the same script as .kscript
```
