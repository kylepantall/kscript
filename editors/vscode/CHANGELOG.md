# Changelog

## Unreleased

- Suggestions everywhere: completion now answers in every place you can write something, in `.kscript` and XML files alike. That covers blank lines and statement starts, inside a statement (`y = @round(‸)`), right after `=`, `echo`, `return`, `if`, `in`, an operator or `|>`, match cases, `} else`, parameter types (`fn f(a: ‸)`), `import ‸` (packages), `import a, ‸ from "lib.kscript"` (the file's functions, constants, types and enums), `KScript.‸` (`KScript.Routes`, `KScript.Window`), route handler bodies (`$request`, `$json`, `$query`, path `{parameters}`) and the empty text of `<echo>` (Ctrl+Space).
- Only what works is suggested: no planned features, no XML-style `name="..."` arguments in `.kscript` files (they get `name: value`), only cases inside `match`, and no `$lib.` for an import alias. Picking a command or element from a package the script doesn't import adds the import line for you.
- Docs in your format: in a `.kscript` file, hover and completion docs name elements the `.kscript` way and show examples converted to `.kscript`. Element names in descriptions are no longer swallowed as HTML ("in its file or `describe`").
- Statement snippets in both formats: `if`, `elif`, `else`, `for`, `while`, `fn`, `try`, `catch`, `match`, `case`, `type`, `enum`, `describe`, `test`, `Get`/`Post` routes and more.
- VS Code's own word and snippet suggestions are off in KScript files, so only KScript's suggestions show (no unrelated snippets from other extensions). Typing `>` at the end of a start tag in a `.ks.xml` file adds the end tag (`kscript.autoCloseTags`).
- Completion while typing: a word at the start of a statement now offers keywords (`echo`, `if`, `for`, `fn`, `return`, ...), `@commands`, your functions and variables, with no trigger character needed. In XML scripts a bare word on its own line offers elements and adds the `<`. In expressions, typing `e` now matches `@env` and `$email` (they filter on the bare name).
- Enter on an empty `///` line in a `.kscript` file ends the doc comment instead of starting another `///` line.
- Shorthand conditions: `<if $user.is_logged_in>`, `<elif …>`, `<while …>`, `<match …>` and `<assert … />` are highlighted, checked and completed like `test="…"`, in scripts and pages. The `<if>`, `<elif>`, `<while>` and `<match>` snippets use the short form.
- The language server restarts by itself when the `kscript` binary changes (after `kscript update`, a reinstall, or a rebuild of a source `KScript.dll`), so the editor stops showing errors from the old version. A notice names the new version.
- When the `kscript` it finds is an older release than the extension, the extension says so and offers to run `kscript update`.
- Standards: `kscript.json` and standard files (`kscript-standard.json`, `*.kscript-standard.json`) are validated and completed, including `"standard"`, `"extends"` and the built-in `kscript:recommended` and `kscript:strict`. Editing a standard file re-checks open files straight away.
- `.kscript` files (the brace-style format) get their own language, "KScript (code)", with a grammar for keywords, `x = ...` assignments, `"text { interpolation }"`, `'strings'`, `$variables`, `@commands`, `name(attr: value)` elements and `///` doc comments with `@param`/`@returns` tags. Errors, hover and completion come from the language server as before; formatting leaves these files alone.
- Formatting: **Format Document** and format on save tidy scripts and `kscript serve` pages with the same formatter as `kscript fmt`: indentation by nesting, one statement per line, tidy start tags, no trailing spaces. Text, attribute values, comments, CDATA and `<pre>`/`<textarea>`/`<script>`/`<style>` content are kept. The style comes from the `format` section of `kscript.json`, which the extension now validates and completes, or else from your editor's tab settings. Each rule can be turned off in `format.rules`, and `<!-- kscript-fmt: off -->` / `<!-- kscript-fmt: on -->` or `<!-- kscript-fmt-ignore -->` leave parts of a file alone.
- Types everywhere: functions know what they return (from `value="..."` or their `<return>`s, including imported ones), so `<let name="p" value="@person()" />` offers `$p.name`. Parameters take the type of their default (`count = 0`), `match` expressions the type of their arms, and a variable declared as `null` takes the type of the first value `<set>` gives it.
- Inlay hints show types you haven't written: `<let name="people": list<{ name: string, age: number }>`, `<for each="p": { name, age }>` and `params="x: int" → number`. Turn them off with `editor.inlayHints.enabled`.
- Hovers show structural types (`{ name: string, address: { city: string } }`, `list<number>`), and method completions show what each method returns, with the value's own methods first.
- `$name->` and `$name.` complete in plain element text too (`<echo>Hi $person.‸</echo>`), not only inside `{ }`.
- Notebook-style blocks: a thin line separates the top-level blocks of a script (each comment-headed section, or each function, loop and `if` when there are no comments). Set `kscript.blocks.dividers` to turn it off, or change the `kscript.blockDivider` colour. Elements and comments fold.
- Highlighting: control-flow tags (`<if>`, `<for>`, `<return>`...) are coloured as keywords and declarations (`<let>`, `<fn>`, `<import>`) as storage types; type names in `params` and `type="..."` are coloured as types; the text of `<echo format="json">` is highlighted as JSON.
- Completion pops up as you type inside attribute values (VS Code turns quick suggestions off in strings by default).

- Tests: **KScript: Run Tests** runs `kscript test` for the workspace, and test files (`*.test.ks.xml`) get a Run Tests button in the editor title bar. Test files know the KScript.Test elements (`<test>`, `<describe>`, `<expect>`, `<assert>`, `<expect-error>`, `<capture>`) without an import, and `kscript.json` completes the new `test` settings.
- Completion knows more about your data: `@read_json('data/people.json')` reads the file, so `$person.` offers `name`, `age` and so on, and nested objects and lists work too.
- Lambda parameters take the type of the list's items: in `$people->filter(fn($p) => $p.age gt 18)`, `$p.` completes its fields. This also works for `|>` pipes, `reduce` and `sort`, and `->map(fn($p) => $p.name)` is known to be a list of text.
- Components: `<person-card ` offers the component's props, which are the variables its file uses without declaring. Completing a component name fills in every prop.
- `.html` pages get KScript `{ }` highlighting again. VS Code opens `.html` files with its `text.html.derivative` grammar, which the page grammar wasn't injected into, so only plain HTML colours showed.
- `<slot />`, `<hero />` and other self-closing elements in pages are no longer marked as errors by the HTML grammar.
- `kscript.json` is validated against a bundled schema, with completion and hover for its settings, and may contain comments.
- The language server applies the project's `kscript.json` lint rules and excluded paths.

## 0.1.0

- First release: diagnostics, completion, hover, signature help, go to definition, references, rename, outline, semantic highlighting and quick fixes for KScript scripts and `kscript serve` pages, using the installed `kscript lsp`.
