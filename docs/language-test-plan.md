# Language test coverage plan

The v0.15.1 baseline runs 214 script cases across 40 suites. The protocol smoke test opens those suites and compares five negative fixtures with CLI diagnostics. Keep those fixtures and add coverage where several language features meet.

## 1. Close the import diagnostic gap

A three-file cycle `a → b → c → a` currently passes `kscript check --warnings-as-errors` with zero diagnostics. The live language server publishes empty diagnostics for all three files. Running `a.kscript` exits 3 and reports an `Import` error on the closing import. The runtime already covers direct self-import.

- Check in a minimal three-file fixture and assert the cycle-closing span, severity and error category. Require CLI and LSP diagnostic agreement and preserve the runtime error. Open from each entry point, break the cycle in an unsaved dependency, then reintroduce it. Verify stale diagnostics clear and return.
- Add a diamond with a visible module initializer to establish the execution count. Cross direct and qualified imports with repeated imports and different relative spellings. Include missing, deleted and recreated dependencies and exercise actual watched-file events.

## 2. Cover interacting language features

| Area | Factors to vary | Observable result |
| --- | --- | --- |
| Values and types | Native, refined and record types. Valid, null and invalid values. Scalar, optional and collection positions. Constructor, argument, return, assignment and JSON boundaries. | Exact value after normalization, correct inherited or custom method, and exact error when conversion fails. |
| Calls and scope | Positional, named, default and callback arguments. Local, qualified and imported calls. Receiver methods and nested closures. | Target symbol, binding order, captured value and side-effect count. |
| Errors and cleanup | Explicit throw, failed validation and called-function error. Method, type and caller handlers. Matching, mismatching, recovery and rethrow. | Handler order, error identity and payload, cleanup count, stdout, stderr and exit status. |
| Literal parsing and operators | Bare, quoted and interpolated literals. Numeric, duration and money contexts. Same-currency, configured conversion and missing rate. | Parse and inferred type, precedence, exact value, rounding and error. |
| Editing and navigation | LF and CRLF. ASCII, combining characters and astral characters. Range edit, full replacement, close and reopen, and disk events. | Exact UTF-16 diagnostic and semantic token spans, go-to-definition and rename targets, and the result of applying a quick fix. |
| Format and delivery | Code and XML for constructs both formats support. Original, formatted, converted and compiled execution. Multi-file modules and embedded assets where supported. | Formatter idempotence, matching results and errors, and a compiled executable that runs from outside the source tree. |

Start with a mixed multi-file fixture that combines qualified imports, refined methods, named/default arguments, a callback and interpolation. Run it before and after formatting and conversion, then through the existing compilation smoke jobs. Add a Unicode/CRLF protocol sequence that edits a dependency containing interpolation and verifies diagnostics, navigation, rename and quick-fix ranges. Use real VS Code for the corresponding Problems and navigation workflow.

## 3. Measure combinations and keep CI fast

1. Give each case a stable ID. Record factor values and explicit expected results alongside it. Use runnable `*.test.kscript` and `*.test.ks.xml` files for language behavior, and reuse their source in CLI and live JSON-RPC checks where applicable. Compare CLI and LSP file, code, severity, message and normalized start/end positions. Assert editor behavior separately where it depends on unsaved state or UI.
2. Enumerate all combinations for small truth, dispatch and catch tables. For larger matrices, require every feasible pair in each table above and report the covered and total pair counts. Record excluded pairs with the syntax or runtime constraint that makes them invalid. Keep intentionally invalid combinations as explicit negative cases.
3. Add targeted triples for import resolution × receiver scope × call binding, throw origin × catch scope × handler outcome, edit encoding/EOL × document lifecycle × editor operation, and literal context × currency relationship × operator. Add a named end-to-end script for each interaction that needs four or more factors to reproduce.
4. Use deterministic generation with bounded case counts. Assert independent expected values in example cases. Use formatter idempotence and interpreted/converted/compiled equivalence as additional properties. Print the seed, factor values and source on failure. Reduce generated failures to named fixtures before fixing them.
5. Run the fixed corpus and pair-coverage check in PR CI with CLI, formatter and live LSP checks. Run UI-specific scenarios in the existing VS Code job and selected executable parity checks in the platform matrix. Put larger seed sweeps and lifecycle permutations in scheduled or manually triggered CI.

Each phase is complete when its named cases run in CI, every required pair and targeted triple is accounted for, and failures identify the case and the observable result that differed. Keep the current corpus as the baseline so an expanding matrix cannot hide a lost regression.
