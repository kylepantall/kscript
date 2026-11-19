# Changelog

## Unreleased

- `.html` pages get KScript `{ }` highlighting again. VS Code opens `.html` files with its `text.html.derivative` grammar, which the page grammar wasn't injected into, so only plain HTML colours showed.
- `<slot />`, `<hero />` and other self-closing elements in pages are no longer marked as errors by the HTML grammar.

## 0.1.0

- First release: diagnostics, completion, hover, signature help, go to definition, references, rename, outline, semantic highlighting and quick fixes for KScript scripts and `kscript serve` pages, using the installed `kscript lsp`.
