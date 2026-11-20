# Changelog

## Unreleased

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
