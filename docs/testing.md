# Testing scripts: kscript test

KScript has a test framework built in. Tests are KScript files whose names end in `.test.ks.xml`, usually kept in a `tests/` folder, and `kscript test` runs them.

```xml
<!-- tests/strings.test.ks.xml -->
<kscript>
  <import src="../lib/strings.ks.xml" />

  <describe name="slugify">
    <test name="lower-cases and joins words with dashes">
      <expect value="@slugify('Hello World')" equals="'hello-world'" />
    </test>

    <test name="slug of '{$case.text}'" each="case" in="[
      { text: 'One', slug: 'one' },
      { text: 'Two Words', slug: 'two-words' },
    ]">
      <expect value="@slugify($case.text)" equals="$case.slug" />
    </test>
  </describe>
</kscript>
```

```text
$ kscript test
tests/strings.test.ks.xml
  ✓ slugify › lower-cases and joins words with dashes (1 ms)
  ✓ slugify › slug of 'One' (0 ms)
  ✗ slugify › slug of 'Two Words' (0 ms)
      Expected @slugify($case.text) to equal 'two-words', but it was 'two words'.
      at tests/strings.test.ks.xml:14:7

Tests: 1 failed, 2 passed, 3 total
Files: 1 failed, 1 total
Time:  0.08s
```

The exit code is 0 when every test passed and 1 when any test failed, a test file couldn't run, or no tests were found, so `kscript test` can gate a CI job.

## Test files and folders

- A test file is a normal script named `name.test.ks.xml` (or `name.test.xml`). Test files can use KScript.Test without an `<import>`, and so can the editor.
- `kscript test` with no paths runs every test file in the current project: the folder of the nearest `kscript.json`, or the current folder if there isn't one. Folders whose names start with `.`, and `node_modules`, `bin` and `obj`, are skipped.
- `kscript test tests/cart` runs the test files in a folder, and `kscript test tests/cart.test.ks.xml` (or just `kscript tests/cart.test.ks.xml`) runs one file. You can give several paths.
- `kscript make test checkout` creates `tests/checkout.test.ks.xml` in the project, and `kscript make project` starts new projects with the rules of a small to-do app in `lib/`, a script, and tests for both.

## How a test file runs

Each file runs in its own interpreter, in two steps:

1. The file's top-level code runs once: its imports, `<let>` variables, `<fn>` functions and `<describe>` blocks. Each `<test>` it reaches is noted, not run.
2. Each test then runs on its own, in a new scope inside the one it was written in. It sees the file's variables and functions, and the variables of the `<describe>` blocks around it.

A test passes if its body finishes. It fails at the first check that doesn't hold, or at any error, and the remaining tests still run. Variables a test declares with `<let>` belong to that test. Variables declared outside the tests are shared, so reset them in `<before-each>`.

What a test prints with `<echo>` is kept, and shown under the test only when it fails.

## Declaring tests

| Element | Use |
| --- | --- |
| `<test name="…">` | A test. `name` can use `{ }`. |
| `<test name="… {$case.x}" each="case" in="$cases" index="i">` | One test per item of a list (or of a map, as `{ key, value }`). The item is in `$case`, and `index` names a variable for its position. |
| `<describe name="…">` | A group. Its name is put in front of each test's name (`cart › adds items`), and its hooks apply to every test inside. Groups can nest. |
| `<before-each>` | Runs before every test in its file or `<describe>`, in the test's own scope. Outer hooks run first. |
| `<after-each>` | Runs after every test in its file or `<describe>`, even when the test failed. Inner hooks run first. Use it to delete files a test made. |
| `skip="yes"` or `skip="reason"` | On a `<test>` or `<describe>`: don't run it. It is reported as skipped, with the reason. |
| `only="yes"` | On a `<test>` or `<describe>`: while you work on something, run only the tests marked `only` in that file. |
| `timeout="30"` | On a `<test>`: fail it if it runs longer than this many seconds. |

```xml
<describe name="a shopping cart">
  <let name="cart" value="[]" />
  <before-each>
    <set name="cart" value="['apple']" />
  </before-each>

  <test name="starts with one item">
    <expect value="$cart" count="1" />
  </test>

  <test name="adds items">
    <set name="cart" value="$cart->push('pear')" />
    <expect value="$cart" equals="['apple', 'pear']" />
  </test>
</describe>
```

## Checks

`<expect>` takes the value to check and one check. Every check's value is an expression, so text needs quotes: `equals="'hello'"`.

| Check | Passes when |
| --- | --- |
| `<expect value="$x" equals="5" />` | The values are equal, as `==` compares them: `'5'` equals `5`. Lists and maps are compared item by item, and a failure says where they differ. |
| `not-equals="5"` | They aren't equal. |
| `same="5"` | They are strictly equal (`===`): same type and value. |
| `contains="'Joan'"` | A list has the item, text has the text, or a map has the key. |
| `not-contains="'Joan'"` | The opposite. |
| `matches="'^\d+$'"` | Text matches the regular expression. |
| `type="list"` | The value is a `string`, `number`, `bool`, `list`, `map`, `date`, `fn` or `null`. |
| `count="3"` | A list or map has that many items, or text has that many characters. |

`message="…"` on any check replaces the usual message.

`<assert test="…" />` checks a condition. When a comparison fails it shows the value of each side:

```text
Expected $order.total eq 30 to be true.
  $order.total is 25
```

`@assert(condition, message?)` does the same inside an expression, such as a lambda, and `@fail(message?)` fails the test straight away.

### Errors

`<expect-error>` fails unless its body stops with an error. `type` checks the error type, `contains` checks the message, and `as` keeps the error (`type`, `message`, `line`, `file`) for more checks:

```xml
<expect-error type="Validation" contains="positive" as="err">
  <call fn="set_age" args="-1" />
</expect-error>
<expect value="$err.line" type="number" />
```

A failed check inside the body is still reported as a failure, not taken as the expected error.

### Output and input

`<capture to="out">` runs its body and puts what it printed in `$out` (lines end in `\n`, without the last one). `input` gives prompts such as `<input>` their answers, as text or a list of lines:

```xml
<capture to="out" input="['Joan', 'yes']">
  <call fn="sign_up" />
</capture>
<expect value="$out" contains="'Welcome, Joan'" />
```

### Whole scripts

`@run_script(path, { args, input })` runs another script on its own, as `kscript path args…` would, and returns `{ output, code, error }`: what it printed, its exit code (3 when it stopped with an error), and the error or `null`. The path is relative to the test file. The script gets the project's packages, but not KScript.Test.

```xml
<test name="hello.ks.xml greets the name it is given">
  <let name="run" value="@run_script('../scripts/hello.ks.xml', options: { args: ['Ellie'] })" />
  <expect value="$run.code" equals="0" />
  <expect value="$run.output" equals="'Hello, Ellie!'" />
</test>
```

Use it for scripts that do their work at the top level or call `<exit>`. A test that calls `<exit>` itself fails.

## Command line

```sh
kscript test                              # every test in the project
kscript test tests/cart tests/api.test.ks.xml
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
    "include": ["tests/**/*.test.ks.xml"],  // default: **/*.test.ks.xml and **/*.test.xml
    "exclude": ["tests/slow"],              // default: _templates, node_modules, .git
    "timeout": 10,                          // seconds per test; 0 for no limit (default 10)
    "packages": ["KScript.Terminal"]        // imported by test files only
  }
}
```

`timeout="…"` on a test wins over `--timeout`, which wins over `test.timeout`.

## In the editor

The language server treats `*.test.ks.xml` files as importing KScript.Test, so `<test>`, `<expect>` and the rest get completion, hover and checking, and `$case` from `each` is known inside the test. In VS Code, **KScript: Run Tests** runs `kscript test` for the workspace, and test files have a run button in the editor title bar.

`<expect>`, `<assert>` and `<capture>` also work in ordinary scripts that `<import package="KScript.Test" />`: a failed check stops the script with an `Assertion` error. `<test>` and `<describe>` only work under `kscript test`.
