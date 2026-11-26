# Testing scripts: kscript test

KScript has a test framework built in. Tests are KScript files whose names end in `.test.kscript` or `.test.ks.xml`, usually kept in a `tests/` folder, and `kscript test` runs them.

From the KScript repository, run `kscript test samples/scripts/tests/language` to exercise the language itself. To try one example, pass its filename instead. The same files appear in VS Code's Test Explorer. CI compares the tests discovered by the language server with the interpreter's results. [Browse all language test scripts](../samples/scripts/tests/language)

| What to try | Runnable examples |
| --- | --- |
| Follow argument binding, recursive calls, and variable scope | [Named and piped calls](../samples/scripts/tests/language/call-binding.test.kscript) · [Recursion and scope](../samples/scripts/tests/language/recursion-and-scope.test.kscript) |
| Step through an expression or a nested loop | [Operator precedence](../samples/scripts/tests/language/expression-operators.test.kscript) · [Ranges and loop metadata](../samples/scripts/tests/language/iteration-boundaries.test.kscript) |
| Give data a type and validate it at the boundary | [Custom literals](../samples/scripts/tests/language/pattern-literals.test.kscript) · [Records parsed from text](../samples/scripts/tests/language/parsed-records.test.kscript) · [Joined types](../samples/scripts/tests/language/joined-types.test.kscript) |
| Transform collections and text | [List transformations](../samples/scripts/tests/language/list-transformations.test.kscript) · [Map updates](../samples/scripts/tests/language/map-updates.test.kscript) · [Regular expressions](../samples/scripts/tests/language/regex-transformations.test.kscript) · [Multiline strings](../samples/scripts/tests/language/string-blocks.test.kscript) |
| Use native values and add methods to a type | [Dates and durations](../samples/scripts/tests/language/dates-and-durations.test.kscript) · [Money and rounding](../samples/scripts/tests/language/money-arithmetic.test.kscript) · [Extension methods](../samples/scripts/tests/language/extension-methods.test.kscript) |
| Follow an error through nested handlers | [Guarded throws](../samples/scripts/tests/language/guarded-errors.test.kscript) · [Catch propagation](../samples/scripts/tests/language/catch-propagation.test.kscript) |

```kscript
// tests/strings.test.kscript
kscript {
  import "../lib/strings.kscript"

  describe(name: "slugify") {
    test(name: "lower-cases and joins words with dashes") {
      expect(@slugify('Hello World'), equals: 'hello-world')
    }

    cases = [
      { text: 'One', slug: 'one' },
      { text: 'Two Words', slug: 'two-words' },
    ]
    for case in $cases {
      test(name: "slug of '{$case.text}'") {
        expect(@slugify($case.text), equals: $case.slug)
      }
    }
  }
}
```

```text
$ kscript test
tests/strings.test.kscript
  ✓ slugify › lower-cases and joins words with dashes (1 ms)
  ✓ slugify › slug of 'One' (0 ms)
  ✗ slugify › slug of 'Two Words' (0 ms)
      Expected @slugify($case.text) to equal 'two-words', but it was 'two words'.
      at tests/strings.test.kscript:14:7

Tests: 1 failed, 2 passed, 3 total
Files: 1 failed, 1 total
Time:  0.08s
```

The exit code is 0 when every test passed, 1 when any test failed, a test file couldn't run, no tests matched or the folder has no test files, and 2 when the command line or `kscript.json` is wrong (an unknown option, a path that is not there), so `kscript test` can gate a CI job. [Exit codes](projects.md#exit-codes) are the same for every command.

## Test files and folders

- A test file is a normal script named `name.test.kscript` or `name.test.ks.xml`. The examples here use the `.kscript` format; switch them to XML to see `<describe>`, `<test>` and `<expect>`. Test files can use KScript.Test without an `import`, and so can the editor.
- `kscript test` with no paths runs every test file in the current project: the folder of the nearest `kscript.json`, or the current folder if there isn't one. Folders whose names start with `.`, and `node_modules`, `bin` and `obj`, are skipped.
- `kscript test tests/cart` runs the test files in a folder, and `kscript test tests/cart.test.kscript` (or just `kscript tests/cart.test.kscript`) runs one file. You can give several paths.
- `kscript make test checkout` creates `tests/checkout.test.kscript` in the project (`--syntax xml` for `.test.ks.xml`), and `kscript make project` starts new projects with the rules of a small to-do app in `lib/`, a script, and tests for both.

## How a test file runs

Each file runs in its own interpreter, in two steps:

1. The file's top-level code runs once: its imports, variables, `fn` functions and `describe` blocks. Each `test` it reaches is noted, not run.
2. Each test then runs on its own, in a new scope inside the one it was written in. It sees the file's variables and functions, and the variables of the `describe` blocks around it.

A test passes if its body finishes. It fails at the first check that doesn't hold, or at any error, and the remaining tests still run. Variables a test declares belong to that test. Variables declared outside the tests are shared, so reset them in `before_each`.

What a test prints with `echo` is kept, and shown under the test only when it fails.

## Declaring tests

| Element | Use |
| --- | --- |
| `test(name: …) { }` | A test. `name` can use `{ }`. |
| `for case at i in $cases { test(name: "… {$case.x}") { } }` | One test per item: a `test` inside a `for` loop, the same loop as anywhere else. The loop's variables can be used in the name and the body. (In XML: `<for each="case" at="i" in="$cases"><test name="…">`.) |
| `describe(name: …) { }` | A group. Its name is put in front of each test's name (`cart › adds items`), and its hooks apply to every test inside. Groups can nest. |
| `before_each { }` | Runs before every test in its file or `describe`, in the test's own scope. Outer hooks run first. |
| `after_each { }` | Runs after every test in its file or `describe`, even when the test failed. Inner hooks run first. Use it to delete files a test made. |
| `skip` or `skip: reason` | On a `test` or `describe`: don't run it. It is reported as skipped, with the reason. |
| `only` | On a `test` or `describe`: while you work on something, run only the tests marked `only` in that file. |
| `timeout: 30` | On a `test`: fail it if it runs longer than this many seconds. |

```kscript
describe(name: "a shopping cart") {
  cart = []
  before_each {
    cart = ['apple']
  }

  test(name: "starts with one item") {
    expect($cart, count: 1)
  }

  test(name: "adds items") {
    cart = $cart->push('pear')
    expect($cart, equals: ['apple', 'pear'])
  }
}
```

## Checks

`expect` takes the value to check first, without a name, and one check: `expect($total, equals: 3)`. Every check's value is an expression, so text needs quotes: `equals: 'hello'`.

| Check | Passes when |
| --- | --- |
| `expect($x, equals: 5)` | The values are equal, as `==` compares them: `'5'` equals `5`. Lists and maps are compared item by item, and a failure says where they differ. |
| `not_equals: 5` | They aren't equal. |
| `same: 5` | They are strictly equal (`===`): same type and value. |
| `contains: 'Joan'` | A list has the item, text has the text, or a map has the key. |
| `not_contains: 'Joan'` | The opposite. |
| `matches: '^\d+$'` | Text matches the regular expression. |
| `type: list` | The value is a `string`, `number`, `bool`, `list`, `map`, `date`, `fn` or `null`. |
| `count: 3` | A list or map has that many items, or text has that many characters. |
| `count_at_most: 4` | A list or map has at most that many items, or text has at most that many characters. With `@queries()`, it checks how many queries a test sent. |

`message: …` on any check replaces the usual message.

`@assert(condition, message?)` checks a condition, on a line of its own or inside an expression such as a lambda. When a comparison fails it shows the value of each side:

```text
Expected $order.total == 30 to be true.
  $order.total is 25
```

`@fail(message?)` fails the test straight away.

### Errors

`expect_error { }` fails unless its body stops with an error. `type` checks the error type (an error type that extends it counts too), `contains` checks the message, and `as` keeps the error (`type`, `message`, `line`, `file`, `status`, `cause` and its fields) for more checks:

```kscript
expect_error(type: Validation, contains: "positive", as: err) {
  @set_age(-1)
}
expect($err.line, type: number)
```

For one expression, `throws` is shorter: it passes when working out the value stops with an error of that type.

```kscript
error PersonNotFound extends NotFound

fn find_person(id: int) {
  throw PersonNotFound "No person with id {$id}"
}

expect(@find_person(99), throws: PersonNotFound)
expect('nope'->json(), throws: Format) // Json extends Format
```

A failed check inside the body is still reported as a failure, not taken as the expected error.

### Output and input

`capture(into: out) { }` runs its body and puts what it printed in `$out` (lines end in `\n`, without the last one). `input` gives prompts such as `input` their answers, as text or a list of lines:

```kscript
capture(into: out, input: ['Joan', 'yes']) {
  @sign_up()
}
expect($out, contains: 'Welcome, Joan')
```

### Whole scripts

`@run_script(path, { args, input })` runs another script on its own, as `kscript path args…` would, and returns `{ output, code, error }`: what it printed, its exit code (3 when it stopped with an error), and the error or `null`. The path is relative to the test file. The script gets the project's packages, but not KScript.Test.

```kscript
test(name: "hello.kscript greets the name it is given") {
  run = @run_script('../scripts/hello.kscript', options: { args: ['Ellie'] })
  expect($run.code, equals: 0)
  expect($run.output, equals: 'Hello, Ellie!')
}
```

Use it for scripts that do their work at the top level or call `exit`. A test that calls `exit` itself fails.

### Pages

`@visit(path)` (from KScript.Web) renders a page of the project as a browser gets it: the site is `serve.root` in the nearest kscript.json. Its live handlers run on the server as they would in a browser, so a test can click through a page:

- `->click(text)` runs the on-click handler of the button or link with that text (or `aria-label` or `title`).
- `->change(text)` runs the on-change handler of the input whose `<label>` has that text, such as a checkbox.
- `->fill(field, value)` types into the input bound to that field, and `->submit(text?)` runs a form's on-submit.
- `.text` is the words on the page, `.html` its HTML, and `->classes(text)` the classes of the element whose own text that is.

Each of these returns the page, so they chain. A page that isn't there is a `NotFound` error.

```kscript
import KScript.Web

test(name: "adding a task clears the box") {
  page = @visit('/tasks')->fill('draft', 'Write the docs')->submit()
  expect($page.text, contains: 'Write the docs')
  expect($page->classes('Write the docs'), not_contains: 'done')
}
```

samples/task-board has a page whose buttons are all handler shorthands, with a test for each.

## Command line

```sh
kscript test                              # every test in the project
kscript test tests/cart tests/api.test.kscript
kscript test --filter "cart › adds"       # only tests whose full name contains the text (any case)
kscript test --timeout 30                 # seconds per test (0: no limit)
kscript test --junit test-results.xml     # also write a JUnit XML report
```

Colours are used when the output is a terminal; set `NO_COLOR` to turn them off or `FORCE_COLOR` to keep them. Ctrl+C stops the run after the current test.

`--junit` writes the results in the JUnit XML format that GitHub Actions reporters, GitLab, Jenkins and Azure Pipelines can show. A failed check is a `<failure>`, another error is an `<error>`, and a file that couldn't run is reported as an `<error>` on a test named `(load)`.

## Settings in kscript.json

```jsonc
{
  "packages": ["KScript.IO"],            // imported by every script, including tests
  "test": {
    "include": ["tests/**/*.test.kscript"],  // default: **/*.test.kscript and **/*.test.ks.xml
    "exclude": ["tests/slow"],              // default: _templates, node_modules, .git
    "timeout": 10,                          // seconds per test; 0 for no limit (default 10)
    "packages": ["KScript.Terminal"],       // imported by test files only
    "env": "test",                          // the environment tests run in (KSCRIPT_ENV)
    "database": "memory"                    // KScript.Data: a database in memory per test file, or config
  }
}
```

`timeout: …` on a test wins over `--timeout`, which wins over `test.timeout`.

`test.env` is the environment the tests run in. The API and fullstack [project templates](projects.md#an-api---template-api) use it in `config/config.kscript` to select an in-memory database. An explicitly listed YAML source can use a matching variant, such as `config.test.yaml`.

`kscript test --env staging` and a `KSCRIPT_ENV` that is already set win over `test.env`.

Tests that use [KScript.Data](data.md#testing-with-a-database) get a database of their own in memory whatever the config says, unless `test.database` is `config`. Each test's changes are undone when it ends. This includes users backed by a model in `http.auth.users`. A route a test calls with `@api()` uses the same database, in the test's transaction: the route sees the records the test made, the test sees what the route saved, and both are undone together.

Each test file also gets temporary app folders for file-backed auth, including configured `http.auth.folder` stores, and for `@storage()`. Commands and local `@api()` calls share those folders within the file. The runner removes them when the file finishes, including on failure. Use a fixture inside the test file or a setup hook when tests need seeded users or files.

### Testing an API

`@api(/api/notes)` sends a request to the project's own routes without starting a server (see [Calling your own API](http.md#calling-your-own-api)), so a test checks what a client would get:

```kscript
import KScript.Net

test(name: "a note is saved") {
  added = @api(/api/notes, post: { title: 'Ship it' })
  expect($added.status, equals: 201)
  expect(@api(/api/notes/$added->json().id)->json().title, equals: 'Ship it')
}
```

### Testing who may do what

`@acting_as()` from [KScript.Auth](auth.md#policies) sets who `@user()`, `@can()` and `@authorize()` ask about, until the test ends. Pass `null` to act as a guest. Routes the test calls with `@api(/...)` see the same user, including routes behind `auth`, `auth:session` or `auth:token` middleware, so a signed-in route needs no token in a test:

```kscript
import KScript.Auth
import KScript.Net

test(name: "the dashboard greets me") {
  @acting_as(@register('ada@example.test', 'correct-horse-battery-9'))
  expect(@api(/dashboard).status, equals: 200)
}
```

To test cookie-keyed behaviour, such as a guest's board or a remembered preference, send the cookie yourself: `@api(/, options: { headers: { Cookie: 'sb_guest=0f8f' } })`. The response's `.cookies` shows the cookies it set.


```kscript
import KScript.Auth
import KScript.Data
import "../models/post.kscript"

test(name: "only the author may update a post") {
  post = Post->create(title: 'Mine', author_id: 1)

  @acting_as({ id: 1 })
  expect(@can(update, $post), equals: true)

  @acting_as({ id: 2 })
  expect(@can(update, $post), equals: false)
}
```

## Workflows that exercise several language features

The repository tracks coverage gaps, combination targets and shared CLI, language-server and editor checks in the [language test plan](language-test-plan.md).

Run `kscript test samples/scripts/tests/language/checkout-workflow.test.kscript` to follow a mixed-currency invoice from line validation through discounts, settlement and refunds. The tests assert amounts in both currencies, recover the original error from a missing quote and check the penny retained after a partial refund. [Checkout script](../samples/scripts/tests/language/checkout-workflow.test.kscript)

Run `kscript test samples/scripts/tests/language/dispatch-workflow.test.kscript` to follow incoming JSON through typed records, team-name normalization, priority sorting and queue selection. The same policy handles an unknown team by selecting a fallback or propagating the lookup error. [Dispatch script](../samples/scripts/tests/language/dispatch-workflow.test.kscript)

The [refined string suite](../samples/scripts/tests/language/refined-string-values.test.kscript) checks bare static keys, quoted interpolation, trimming and pattern failures. The [license issuer suite](../samples/scripts/tests/language/license-issuer-guards.test.kscript) exercises three validation guards and checks what the caller sees on success and failure.

## In the editor

The language server treats `*.test.kscript` and `*.test.ks.xml` files as importing KScript.Test, so `test`, `expect` and the rest get completion, hover and checking, and the variables of a `for` loop round a test are known inside it. In VS Code, **KScript: Run Tests** runs `kscript test` for the workspace, and test files have a run button in the editor title bar.

`expect`, `@assert` and `capture` also work in ordinary scripts that `import KScript.Test`: a failed check stops the script with an `Assertion` error. `test` and `describe` only work under `kscript test`.

### Comparing checker and editor diagnostics

Use `kscript check path/to/script.kscript --output json` for the diagnostic code, message, severity and source range on disk. In VS Code, **Problems** shows diagnostics for the current buffer, including unsaved edits. Set `"kscript.trace.server": "verbose"` to see the language server's `textDocument/publishDiagnostics` notifications in the trace output. CLI lines and columns start at one. LSP lines and columns start at zero.

From the repository root, after building KScript in Release mode and installing the extension's npm dependencies, run the same protocol checks as CI:

```sh
node editors/vscode/test/smoke.js KScriptApp/bin/Release/net8.0/KScript.dll
```

The test opens every language suite and helper. For the [diagnostic fixtures](../editors/vscode/test/fixtures/diagnostics), it checks the expected source spans, compares the server's messages and severities to the CLI report, and verifies that a correction clears the old diagnostics before saving the file.
