# The .kscript format

`.kscript` files are KScript written with braces and new lines instead of XML tags. The language, the runtime and
every package are the same: the parser (`KScript.Syntax/Markup/CodeParser.cs`) builds the exact element tree the
XML parser builds, so `kscript`, `kscript check` and the language server treat both alike. The file extension picks
the parser; `.ks.xml` scripts are unchanged, and the two kinds can import each other.

`kscript make project`, `kscript make script` and `kscript make test` write `.kscript` files; pass `--format xml`, or set
`"make": { "format": "xml" }` in `kscript.json`, to get `.ks.xml` instead.

```kscript try
import KScript.IO

/// Lower-cases text and joins words with dashes.
/// @param text The text to change.
fn slugify(text: string) => $text->lower()->replace(' ', '-')

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
| `const config = { retries: 3 }` | `<let name="config" const="true" value="…" />` |
| `count: int = $raw` | `<let name="count" type="int" value="$raw" />` |
| `@greet('Hi')` | `<do>@greet('Hi')</do>` |
| `echo "Hello, $name"` | `<echo>Hello, $name</echo>` |
| `echo $list[1]`, `echo(format: json) $data` | `<echo value="$list[1]" />` |
| `if a { } elif b { } else { }` (also `else if`) | `<if test>`, `<elif test>`, `<else>` |
| `for item in $list { }`, `for key, value in $map { }` | `<for each in>` |
| `for item at i in $list { }` | `<for each="item" index="i" in="$list">` |
| `while test { }`, `break`, `continue` | `<while test>`, `<break/>`, `<continue/>` |
| `match $x { case 1, 2 { } when $x > 9 { } default { } }` | `<match value>`, `<case is>`, `<case when>`, `<default>` |
| `fn greet(who: string, hi = 'Hello') { }` | `<fn name="greet" params="who: string, hi = 'Hello'">` |
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

| `type Admin with [User, Auditable] { … }` | `<type name="Admin" with="User, Auditable">` |
| `type Age with [int] { min: 0; max: 150 }` | `<type name="Age" with="int" min="0" max="150" />` |
| `validate: $value > 0, 'too small'` (in a type) | `<validate test="$value > 0" message="too small" />` |
| `normalize: $value->lower()` (in a type) | `<normalize value="$value->lower()" />` |
| `fn area() => $w * $h` (in a type) | `<fn name="area" value="$w * $h" />` inside the `<type>` |

A field line in a `type` block is `name[?][: type] [from path] [= default | => value]`; quote the path if it has spaces. See [Field lines](language.md#field-lines) for what each part does. A type's block can also hold rule lines (`min:`, `max:`, `pattern:`, `one_of:`, `not:`, `parse:`, `validate:` and `normalize:`) and `fn` methods. A line such as `max: int` is still a field: a rule's value is a value, not a type. See [Types and validation](types.md) for the rules.

Any other name is an element: `name(attribute: value, ...) "text" { children }`, each part optional.

```
input(to: old_dir) "Move files from: "
Row(gap: 8, wrap) {
  Button(variant: primary, on-click: "playing = not $playing") "{ $playing ? 'Pause' : 'Play' }"
}
```

## Long strings and blocks of text

End a line inside a string with `\` to carry the string on to the next line. The new line and the next line's
indentation aren't part of the string, so a long string can follow the code's indentation:

```kscript try
name = 'Kyle'
echo "Hello { $name }. KScript reads your whole script, checks it, \
  and only then runs it."
```

That prints one line. It works in `"text"` and in `'expression strings'`, and keeps any spaces before the `\`. Write
`\\` for a backslash of your own at the end of a line of text. `kscript fmt` uses it to wrap a string that runs past
`printWidth` (the [`wrapStrings`](formatting.md) rule).

For text that should keep its new lines, such as a query, a template or an equation, use a block fenced with three
backticks, as in Markdown:

````kscript
echo ```
  Hi { $name },
  your order has shipped.
  ```

query = ```sql
  SELECT * FROM users
  WHERE active = 1
  ```

repo = ```<Repo>
  { "name": "kscript", "stars": 12 }
  ```
````

- The block runs from the opening fence to a line that starts with the same number of backticks. Use four (or more)
  when the text holds three.
- The closing fence's indentation is taken off every line, so the block sits at the code's indentation and keeps any
  indentation inside it. A line indented less than the closing fence is an error. The new lines just inside the
  fences aren't part of the text.
- A word after the fence names the language: `text`, `sql`, `html`, `css`, `js` (or `javascript`), `json`, `md` (or
  `markdown`), `latex` (or `tex`), `yaml` (or `yml`) or `xml`. It changes nothing at run time; editors use it to
  highlight the block. Any other word is an error, so a typo such as ```` ```textc ```` is caught.
- A type in angle brackets parses the block: `` ```<json> `` is the parsed JSON, and `` ```<Repo> `` maps it onto a
  `type`, with the same checks (and `Shape` errors) as `->json(as: $Repo)`.
- As an element's text (after `echo`, `Paragraph`, `Math`, ...) a block is the same as `"text"`: `{ }` interpolates
  where the element interpolates, and `Math` keeps its braces. In an expression it is a string, like `'single quotes'`.
  No escapes apply inside a block.
- Code can carry on after the closing fence on the same line: `` ``` ) `` or `` ```->trim() ``.

## Rules

- A statement ends at a new line, `;` or the `}` that closes its block. It carries on when a bracket is open, the
  line ends with an operator (`|>`, `->`, `+`, `,`, `and`, ...), or the next line starts with `|>`, `->`, `?`, `:`,
  `.`, `and` or `or`.
- In the head of `if`, `elif`, `for`, `while`, `match`, `case` and `when`, the first `{` outside brackets opens the
  block. Wrap a map literal in parentheses there.
- `"double quotes"` are text, exactly like XML element text or an attribute value: `{ }` and `$name` interpolate where
  the element interpolates, and in an expression too (`greeting = "Hello $name"`), where a `{ }` is filled in when it
  starts with `$` or `@`. `\"` and `\\` are the only escapes (plus a `\` at the end of a line, which joins it to
  the next: see [Long strings and blocks of text](#long-strings-and-blocks-of-text)). `'single quotes'` are string literals inside
  expressions and are kept exactly as written.
- In `name(...)` a value runs to the next `,` or `)` outside brackets; quote it to include a comma. A bare name such
  as `wrap` means `wrap="true"`.
- A list of names is an array: `Middleware(use: [secure_headers, json_only])`, `Resource(..., only: [index, show])`.
  The XML form writes the same list as text, `use="secure_headers, json_only"`, and `kscript convert` turns one into
  the other. The old string form (`use: "a, b"`) still works in `.kscript`, with a KS6006 hint to use the array.
- Expressions are kept as written, so raw `<` and `>` work (`lt` and `gt` still do).
- The file is the body of an implied `<kscript>`, so `kscript { }` around it is optional. A file whose only
  statement is `kscript { }` or a `KScript.*` element (such as `KScript.Window(...) { }`) uses that element as the
  root. `import` goes at that top level, not inside another block.
- `//` and `/* */` are comments. Either can go on its own lines, at the end of a line, or inside an expression
  that spans lines (`[1, /* first */ 2]`). `kscript convert` keeps them; XML has one kind of comment, so they come
  back from XML as `//` lines.

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
fn greet(who: string, greeting = 'Hello') => $greeting + ', ' + $who + '!'

/// @deprecated Use @greet instead.
fn hello(who: string) => @greet($who)
```

`@arg` and `@argument` are other names for `@param`, and `@return` for `@returns`. In the editor, typing `@` at the
start of a doc line suggests the tags (with an `@param name` for each parameter not described yet), typing a name
after `@param` or `@arg` suggests the parameters, and hovering a tag says what it means.

`kscript check` and the editor warn about a `@param` that names no parameter (KS4007) and about calls to a
`@deprecated` function (KS4008). `@public` marks a function used from outside the project, so it is never reported as
[unused](language.md#unused-code) (KS6002). `@llm`, `@prefer`, `@avoid`, `@pitfall` and `@agent hide` are notes for
coding agents, which `kscript agents` writes into AGENTS.md (see [Coding agents](agents.md)). XML scripts write the same text in a `<!--- ... -->` comment (three dashes) just
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

## Formatting

`kscript fmt` and Format Document tidy `.kscript` files: indentation, one statement per line, `} else {`, and spacing
after commas and colons and around `=`. The rules, the settings in `kscript.json` and the `// kscript-fmt: off` comments
are described in [Formatting](formatting.md).
