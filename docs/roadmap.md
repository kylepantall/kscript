# Roadmap

These are ideas KScript is heading towards. None of them is built yet, and the details may change.

## Converters: compiling KScript to other languages

A converter would turn a KScript script into the same program in another language, so a team can write the logic once in KScript and ship it where KScript doesn't run. TypeScript comes first, because it runs in browsers, Node, Deno and Bun, and its types fit KScript's typed values well.

```sh
kscript compile lib/pricing.ks.xml --to ts      # writes lib/pricing.ts
kscript compile src/ --to ts --out dist/        # a whole folder
```

The plan:

- **Built on the shared parser.** The converter would read the same syntax tree that the interpreter and the language server use. That way it supports the XML and `.kscript` formats alike, and it reports the same errors as `kscript check` before writing anything.
- **Language first.** The first version would cover what doesn't depend on the machine: values, expressions, `->` methods, `|>` pipes, lambdas, `<fn>`, `<if>`/`<match>`/loops, `<try>`, named arguments, enums and consts. It would also cover typed JSON records, which become TypeScript `interface`s.
- **A small runtime library.** KScript's methods (`->map()`, `->round(digits:)`, date and string helpers) would come from a small `@kscript/runtime` package. Packages would map to the closest native API, such as KScript.IO to `node:fs` and KScript.Net to `fetch`. Elements with no equivalent (terminal prompts, desktop windows) would be reported as unsupported, not guessed at.
- **Readable output.** The generated code should look like code a person would write: the same names, comments carried over, and source maps back to the `.ks.xml` lines.
- **More targets later.** Once the TypeScript target is solid, the same design could add Python, C# or Go. Each target would be a separate emitter behind `--to`.
- **Tested both ways.** Every sample and the language test suite would run both through the interpreter and as compiled output, and the results would have to match.

`kscript compile` already builds programs (`--to exe`, the default; see [Programs](compile.md)), so converters are new `--to` targets of the same command.

This is separate from `kscript convert`, which only switches a script between the XML and `.kscript` formats of KScript itself.

## More for queues

[KScript.Queue](queues.md) keeps jobs in files, in a database or in memory today. When [KScript.Data](data.md) supports databases other than SQLite, the database driver will claim jobs with `SELECT ... FOR UPDATE SKIP LOCKED` on them. After that: a Redis driver, batches of jobs that report when they all finish, and middleware that wraps a job's `handle`, such as rate limits.
