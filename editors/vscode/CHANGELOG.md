# Changelog

## Unreleased

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
