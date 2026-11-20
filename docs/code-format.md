# The .kscript format (experimental)

`.kscript` files are KScript written with braces and new lines instead of XML tags. The language, the runtime and
every package are the same: the parser (`KScript.Syntax/Markup/CodeParser.cs`) builds the exact element tree the
XML parser builds, so `kscript`, `kscript check` and the language server treat both alike. The file extension picks
the parser; `.ks.xml` scripts are unchanged, and the two kinds can import each other.

```
import KScript.IO

/// Lower-cases text and joins words with dashes.
fn slugify(text) => $text->lower()->replace(' ', '-')

let people = [{ name: 'Joseph', age: 28 }, { name: 'Joan', age: 82 }]
for p in $people {
  if $p.age >= 18 {
    echo "{ @slugify($p.name) } is an adult"
  } else {
    echo "$p.name is not"
  }
}
```

Samples: `samples/code-format/` (rewrites of `samples/kscript2/`) and `samples/life-lab/desktop.kscript`.

## Statements

| .kscript | XML |
| --- | --- |
| `let n = 3` | `<let name="n" value="3" />` |
| `const config = { retries: 3 }` | `<let name="config" const="yes" value="…" />` |
| `let count: int = $raw` | `<let name="count" type="int" value="$raw" />` |
| `set total += 1` | `<set name="total" op="+=" value="1" />` |
| `$x += 1`, `@greet('Hi')` | `<do>$x += 1</do>` |
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
- `//` and `/* */` are comments. `///` lines before a `fn`, `let` or `const` become its description.

## Not done yet

- The VS Code extension still colours `.kscript` files with the XML grammar (errors, hover and go to definition
  already work through the language server).
- No formatter for this format.
