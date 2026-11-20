# The .kscript format (experimental)

`.kscript` files are KScript written with braces and new lines instead of XML tags. The language, the runtime and
every package are the same: the parser (`KScript.Syntax/Markup/CodeParser.cs`) builds the exact element tree the
XML parser builds, so `kscript`, `kscript check` and the language server treat both alike. The file extension picks
the parser; `.ks.xml` scripts are unchanged, and the two kinds can import each other.

```
import KScript.IO

/// Lower-cases text and joins words with dashes.
/// @param text The text to change.
fn slugify(text) => $text->lower()->replace(' ', '-')

people = [{ name: 'Joseph', age: 28 }, { name: 'Joan', age: 82 }]
for p in $people {
  if $p.age < 18 {
    echo "$p.name is not an adult"
    continue
  }
  echo "{ @slugify($p.name) } is an adult"
}
```

Samples: `samples/code-format/` (rewrites of `samples/kscript2/`) and `samples/life-lab/desktop.kscript`.

## Assignment

There is one way to assign: `x = 3`. The first assignment declares `x` in the current block; after that, the same
statement changes it, including from inside a nested block or a function. Other operators update an existing variable:
`total += 1`, `name ??= 'guest'`, `point.x = 3`, `list[0] = 'a'`.

```
total = 0
for item in $cart {
  total += $item.price      // changes the total above
  line = $item.name         // new: only exists inside the loop
}
const tax = 0.2             // can't be changed
count: int = $raw           // declared with a type (converted, or an error)
```

`let` and `set` aren't used in .kscript files; writing them is an error that says to drop the word. In XML they keep
their meaning (`<let>` declares in the current scope, `<set>` changes), and `kscript convert` picks the right one.

## Statements

| .kscript | XML |
| --- | --- |
| `n = 3` (first time) | `<let name="n" value="3" />` |
| `n = 4`, `total += 1` (after) | `<set name="n" value="4" />`, `<set name="total" op="+=" value="1" />` |
| `const config = { retries: 3 }` | `<let name="config" const="yes" value="…" />` |
| `count: int = $raw` | `<let name="count" type="int" value="$raw" />` |
| `@greet('Hi')` | `<do>@greet('Hi')</do>` |
| `echo "Hello, $name"` | `<echo>Hello, $name</echo>` |
| `echo $list[1]`, `echo(format: json) $data` | `<echo value="$list[1]" />` |
| `if a { } elif b { } else { }` (also `else if`) | `<if test>`, `<elif test>`, `<else>` |
| `for item in $list { }`, `for key, value in $map { }` | `<for each in>` |
| `for item at i in $list { }` | `<for each="item" index="i" in="$list">` |
| `while test { }`, `break`, `continue` | `<while test>`, `<break/>`, `<continue/>` |
| `match $x { case 1, 2 { } when $x > 9 { } default { } }` | `<match value>`, `<case is>`, `<case when>`, `<default>` |
| `fn greet(who, hi = 'Hello') { }` | `<fn name="greet" params="who, hi = 'Hello'">` |
| `fn double(x: int) => $x * 2` | `<fn name="double" params="x: int" value="$x * 2" />` |
| `return $x`, `exit 1` | `<return value="$x" />`, `<exit code="1" />` |
| `try { } catch err when test { } catch { } finally { }` | `<try>` with `<catch as when>` and `<finally>` inside |
| `throw Validation "Bad {$x}"` | `<throw type="Validation">Bad {$x}</throw>` |
| `import KScript.Math as math` | `<import package="KScript.Math" as="math" />` |
| `import "lib/s.kscript" as s` | `<import src="lib/s.kscript" as="s" />` |
| `import slugify from "lib/s.kscript"` | `<import src="lib/s.kscript" names="slugify" />` |
| `enum Size { small, medium, large }` | `<enum name="Size" values="small, medium, large" />` |
| `type Repo(extra: error) { … }` | `<type name="Repo" extra="error">` with `<field>`s inside |
| `stars: int from stargazers_count` (in a type) | `<field name="stars" type="int" from="stargazers_count" />` |
| `topics?: string[] = []`, `label => $name + '!'` | `<field name="topics?" type="string[]" default="[]" />`, `<field name="label" value="$name + '!'" />` |

A field line in a `type` block is `name[?][: type] [from path] [= default | => value]`; quote the path if it has spaces. See [Typed data](language.md#typed-data) for what each part does.

Any other name is an element: `name(attribute: value, ...) "text" { children }`, each part optional.

```
input(to: old_dir) "Move files from: "
Row(gap: 8, wrap) {
  Button(variant: primary, on-click: "playing = not $playing") "{ $playing ? 'Pause' : 'Play' }"
}
```

## Rules

- A statement ends at a new line, `;` or the `}` that closes its block. It carries on when a bracket is open, the
  line ends with an operator (`|>`, `->`, `+`, `,`, `and`, ...), or the next line starts with `|>`, `->`, `?`, `:`,
  `.`, `and` or `or`.
- In the head of `if`, `elif`, `for`, `while`, `match`, `case` and `when`, the first `{` outside brackets opens the
  block. Wrap a map literal in parentheses there.
- `"double quotes"` are text, exactly like XML element text or an attribute value: `{ }` and `$name` interpolate where
  the element interpolates, and `\"` and `\\` are the only escapes. `'single quotes'` are string literals inside
  expressions, as before.
- In `name(...)` a value runs to the next `,` or `)` outside brackets; quote it to include a comma. A bare name such
  as `wrap` means `wrap="yes"`.
- Expressions are kept as written, so raw `<` and `>` work (`lt` and `gt` still do).
- The file is the body of an implied `<kscript>`. A file whose only statement is a `KScript.*` element (such as
  `KScript.Window(...) { }`) uses that element as the root.
- `//` and `/* */` are comments.

## Doc comments

`///` lines before a `fn`, `const`, `type`, `enum` or a type's field document it. Editors show the text in hover, completion and signature help.
The text is Markdown, with tags:

```
/// Greets someone politely.
///
/// @param who The person to greet.
/// @param greeting How to start, such as 'Hi'.
/// @returns The greeting, ending with '!'.
/// @example
///   @greet('Kyle')   // Hello, Kyle!
/// @see @welcome
fn greet(who, greeting = 'Hello') => $greeting + ', ' + $who + '!'

/// @deprecated Use @greet instead.
fn hello(who) => @greet($who)
```

`kscript check` and the editor warn about a `@param` that names no parameter (KS4007) and about calls to a
`@deprecated` function (KS4008). XML scripts write the same text in a `<!--- ... -->` comment (three dashes) just
before the element, or in its `description` attribute.

## Converting

`kscript convert` writes the other format next to the original, which is left unchanged:

```
kscript convert tour.ks.xml          # writes tour.kscript
kscript convert tour.kscript         # writes tour.ks.xml
kscript convert scripts/ --to kscript   # every XML script in a folder (tests are skipped)
kscript convert tour.ks.xml --stdout    # print instead of writing
```

Options: `--to xml|kscript`, `--out file`, `--stdout`, `--force` (replace an existing file). Comments, doc comments
and blank lines are kept, and expressions are copied as written. Where one meaning has two spellings, the converter
picks one (`<call fn="greet" args="'x'" />` becomes `@greet('x')`; `x = 3` becomes `<let>` or `<set>`; `const x = 1`
becomes `<const>`), so the first conversion may tidy a file, and converting back and forth after that changes nothing.
It warns when something can't be carried over exactly, such as a `<let>` inside a block that hides an outer variable
of the same name.

Converting keeps a project working in the new format:

- An import of a script that is converted in the same run, or already has a converted version, points at the new
  file (`<import src="lib/tasks.ks.xml" />` becomes `import "lib/tasks.kscript"`). An import of a script that stays
  in the old format is kept, with a note; the two formats can import each other.
- File names in comments and strings that name a converted script are renamed too (`@run_script('hello.ks.xml')`),
  and one-line XML examples in comments are written the .kscript way.
- Converting a folder also converts its tests (`name.test.ks.xml` becomes `name.test.kscript`), renames converted
  files in its `.html` and `.md` pages, and updates the project's `kscript.json`: `"main"` and other file names, and
  each `*.ks.xml` include pattern gains a `*.kscript` twin.

```
kscript convert . --to kscript     # the whole project
```

Test files can be written in this format: `name.test.kscript` files get KScript.Test without an import and run with
`kscript test` like XML ones.

```
import "../lib/strings.kscript"

describe(name: slugify) {
  test(name: joins words with dashes) {
    expect(value: @slugify('Hello World'), equals: 'hello-world')
  }
}
```

## Editors

The VS Code extension colours `.kscript` files with their own grammar (language "KScript (code)"), and errors,
hover, completion and go to definition come from the language server as for XML scripts.

## Not done yet

- No formatter for this format: `kscript fmt` and Format Document leave `.kscript` files as they are.
