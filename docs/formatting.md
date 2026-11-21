# Formatting: kscript fmt

`kscript fmt` tidies scripts (`.ks.xml`, `.ks`, `.kscript` and `.xml` files that start with `<kscript>`) and the `.html` pages that `kscript serve` runs. The editor uses the same formatter for **Format Document** and format on save, so the command line and every editor agree.

```sh
kscript fmt                     # format every script and page in the current project
kscript fmt scripts/ index.html # format these files and folders
kscript fmt --check             # change nothing; list unformatted files and exit 1 if there are any (for CI)
```

A folder covers the same files as `kscript check`: scripts everywhere, and `.html` pages in folders that look like a site (`_layout.html`, `_components/`, `_layouts/` or `kscript.json`). Inside a project, `format.include` and `format.exclude` in `kscript.json` decide instead. Markdown pages are never reformatted, because their whitespace is part of the Markdown.

## What it changes

The formatter only touches whitespace that can't change what a file does.

### .kscript files

- Blocks are indented by nesting, and so are the lines inside a `( )`, `[ ]` or `{ }` that spans several lines. A statement continued on the next line (after `+`, `,` or `=`, or before `|>` or `->`) goes one level further in.
- Each statement goes on its own line: a one-line block such as `for i in 1..3 { echo $i }` is opened up, and statements joined with `;` are split.
- `else`, `elif`, `catch` and `finally` go on the line of the `}` before them: `} else {`.
- Spacing inside a line: one space after commas and colons (`add(1, 2)`, `{ a: 1 }`), around `=` and `=>`, before a block's `{` and before a `//` comment at the end of a line.
- A call, array or map that runs past `printWidth` puts one item per line.
- Trailing spaces are removed, runs of blank lines shrink to `maxBlankLines` (and go entirely at the start and end of a block), and the file ends with exactly one newline.

```kscript
x=[3 ,1,2]
for i in $x { echo $i }
if $x->count() > 2{
echo 'big'
}
else {
echo 'small'
}
```

becomes

```kscript
x = [3, 1, 2]
for i in $x {
  echo $i
}
if $x->count() > 2 {
  echo 'big'
} else {
  echo 'small'
}
```

Text in quotes, comments (`//`, `///` and `/* */`), enum values and match cases written on one line (`label = match $n { 1 => 'one', _ => 'many' }`) are kept as they are. After formatting, `kscript fmt` reads the file again and checks that it is the same program, statement for statement; if it isn't, the file is left alone with an error (please report it).

### XML scripts and pages

- Elements are indented by how deeply they're nested, and each statement in a script goes on its own line.
- Start tags are tidied: one space between attributes, and `<let name="x"/>` becomes `<let name="x" />`. A tag with several attributes that would run past `printWidth` gets one attribute per line, and so does a tag you already wrote that way.
- Trailing spaces are removed, runs of blank lines shrink to `maxBlankLines`, and the file ends with exactly one newline.

```xml
<kscript>
<let name="n" value="3"/>
    <for each="i" in="1..$n"><echo>$i</echo></for>
</kscript>
```

becomes

```xml
<kscript>
  <let name="n" value="3" />
  <for each="i" in="1..$n">
    <echo>$i</echo>
  </for>
</kscript>
```

## What it keeps

- **Text is never rewrapped.** Multi-line element text keeps its own layout and moves to the new indent as a block. That is safe because KScript removes the indentation that the lines of element text share, so `<echo>` prints the same thing.
- **Attribute values** are kept exactly as written, including values that span several lines.
- **Comments** are kept; a multi-line comment moves as a block.
- **Raw content** is kept exactly, trailing spaces included: CDATA sections, and in pages the content of `<pre>`, `<textarea>`, `<script>` and `<style>`. `<markdown>` keeps its trailing spaces (two spaces at the end of a line are a line break in Markdown). Add your own elements to `format.preserve`, or use the [ignore comments](#leaving-part-of-a-file-alone).
- **Pages render the same.** In a page, a line break only goes where there was already whitespace or next to a block-level element such as `<div>` or `<li>`, so inline elements written side by side (`<b>a</b><i>b</i>`) stay together. An inline row written on one line, like a `<nav>` of links, stays on one line. In script elements whose content is text, such as `<echo>`, no whitespace is added between child elements either.
- `<!doctype>`, `<?xml ?>`, a byte order mark and the file's line endings (unless `endOfLine` says otherwise) are kept.

A file with markup errors (an unclosed element, a stray `<`) is left alone: `kscript fmt` reports the first error and exits 1, and the editor makes no change. Fix the error, which `kscript check` and the editor also show, and format again.

## Settings

Put style settings under `"format"` in `kscript.json`. Every setting is optional:

```jsonc
{
  "format": {
    "indent": 2,              // spaces per level, or "tab"
    "tabWidth": 2,            // with "indent": "tab", the columns a tab counts as
    "printWidth": 120,        // wrap a start tag's attributes past this column
    "maxBlankLines": 1,       // blank lines kept in a row
    "endOfLine": "auto",      // auto (keep each file's), lf or crlf
    "preserve": ["pre-like"], // elements whose content is kept exactly
    "rules": {},              // turn rules off, e.g. { "statementPerLine": false }; see below
    "enabled": true,          // false: no formatting anywhere in this project
    "include": ["**/*.ks.xml", "**/*.ks", "**/*.kscript", "**/*.html"],
    "exclude": ["_templates", "node_modules", ".git"]
  }
}
```

These settings can also come from a [standard](standards.md), so several projects can share one style: put them under `"format"` in a standard file and name it with `"standard"` in `kscript.json`. The default standard, `kscript:recommended`, writes out the values above. Anything `kscript.json` sets itself wins over its standard.

In the editor, the editor's own tab settings (`editor.tabSize`, `editor.insertSpaces`) are used for the indent when neither `kscript.json` nor its standard sets it. When a project or its standard sets `format.indent`, that wins, so everyone on the project gets the same result.

## Turning rules off

Each thing the formatter does is a rule you can turn off under `format.rules` (`true`/`false`, or `"on"`/`"off"`). Every rule is on unless you list it:

| Rule | What it does | When it's off |
| --- | --- | --- |
| `indent` | Indents elements by nesting. | Lines keep the indentation they have. |
| `statementPerLine` | Puts each statement (and block-level element) on its own line. | Line breaks only change where there already was whitespace, so one-liners like `<for ...><echo>$i</echo></for>` stay. |
| `tidyTags` | One space between attributes, `<x />` for self-closing tags. | Start tags are kept exactly as written. |
| `wrapAttributes` | One attribute per line for long start tags, and for tags already written that way. | Tags are never wrapped or joined. |
| `trimTrailingWhitespace` | Removes spaces at the ends of lines of text. | They're kept. |
| `collapseBlankLines` | Shrinks runs of blank lines to `maxBlankLines`. | Blank lines are kept. |
| `finalNewline` | Ends the file with exactly one newline. | The file ends as it did. |
| `spacing` | `.kscript` files: one space after commas and colons, around `=` and `=>`, and before a block's `{`. | Spacing inside lines is kept. |
| `braceStyle` | `.kscript` files: `} else {`, `} catch {` on one line. | `else` and `catch` stay on the line they're on. |
| `wrapStrings` | `.kscript` files: breaks a string that runs past `printWidth` at a space, ending the line with `\` so the break isn't part of the string. | Long strings stay on one line. |

In `.kscript` files, `tidyTags` does nothing, `wrapAttributes` wraps long calls, arrays and maps, and `statementPerLine` opens up one-line blocks and splits `;`.

```jsonc
{
  "format": {
    "rules": { "statementPerLine": false, "wrapAttributes": false }
  }
}
```

`"enabled": false` turns formatting off for the whole project: `kscript fmt` skips its files and Format Document makes no changes. `format.exclude` leaves particular files and folders alone.

## Leaving part of a file alone

Comments switch the formatter off for part of a file:

```xml
<kscript>
  <!-- kscript-fmt: off -->
  <let name="grid" value="[
      [1, 0, 0],
      [0, 1, 0],
      [0, 0, 1] ]" />
  <!-- kscript-fmt: on -->

  <!-- kscript-fmt-ignore: kept on one line on purpose -->
  <for each="i" in="1..3"><echo>$i</echo></for>
</kscript>
```

- Everything from `<!-- kscript-fmt: off -->` to `<!-- kscript-fmt: on -->` is kept exactly. Without an `on`, it runs to the end of the element the comment is in (or the end of the file).
- `<!-- kscript-fmt-ignore -->` keeps the element (or comment) right after it exactly. Anything after `kscript-fmt-ignore` in the comment is yours, for saying why.
- Only the comment's own line is re-indented, so the region moves with the code around it.

In `.kscript` files the same comments are written with `//`. `// kscript-fmt: off` runs to `// kscript-fmt: on` or the end of the block it is in, and `// kscript-fmt-ignore` keeps the next statement, with any block it opens:

```kscript
// kscript-fmt-ignore: a grid reads better this way
grid = [1, 0, 0,
        0, 1, 0,
        0, 0, 1]
```

## In the editor

The VS Code extension is the default formatter for KScript files, so **Format Document** (Shift+Alt+F, or Shift+Option+F on macOS) works straight away. To format on save:

```json
"[kscript]": { "editor.formatOnSave": true },
"[kscript-code]": { "editor.formatOnSave": true }
```

VS Code has its own HTML formatter, which doesn't know about KScript elements or `{ }`. To use KScript's for pages, pick **KScript** when VS Code asks which formatter to use, or set it for a site's workspace:

```json
"[html]": { "editor.defaultFormatter": "kylepantall.kscript" }
```

Other editors get formatting from `kscript lsp` (the LSP `textDocument/formatting` request). In CI, run `kscript fmt --check`.
