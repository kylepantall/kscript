# What changed

KScript now has one way to write each thing. This page lists every spelling that was renamed or removed, with what to write instead.

Nothing here fails quietly, and no old spelling keeps working. In a `.kscript` file an old spelling is an error that names the new one: the script doesn't run, and the editor and `kscript check` say the same thing. Most come with a fix:

```sh
kscript check --fix          # rewrite what can be rewritten, in every script of the project
kscript check                # then read what is left
```

`kscript check --fix` rewrites the spellings marked **fix** below, and renames the statements and arguments that only changed their name. It works on a file that doesn't run because of them. The rest are errors whose message says what to write, and the editor offers a quick fix for most of them.

What is left as a warning is style, not spelling: an `else` after a `return` (KS6001), unused code (KS6002 to KS6005), a parameter without a type (KS6007), a second type in one file (KS6008), a name that isn't `snake_case` or `PascalCase` (KS6013) and a length of time without its unit (KS6018). XML scripts keep the forms that are XML's own, such as `lt`, comma lists in attributes and `fields="x: int, y: int"`.

## Text, names and values

| Before | Now | |
| --- | --- | --- |
| `Section(title: Results)` | `Section(title: "Results")`: text in an argument goes in quotes, and a bare word is a name | fix (KS6009) |
| `Button(on_click: "count += 1")` | `Button(on_click: count += 1)`, or `on_click: { count += 1 }`: double quotes always make text | |
| `'Hello $name'` | `"Hello { $name }"`: double quotes evaluate expressions in braces. Single quotes keep text as written | |
| `route = "/users/{id}"`, `"\d{3}"`, `"a { color: red }"` | `'/users/{id}'`, `'\d{3}'`, `'a { color: red }'`, or doubled braces (`"/users/{{id}}"`): a `{ }` in double quotes always holds an expression, whatever it opens with, so `"{ User->count() } users"` and `"{ 1 + 2 }"` fill in | |
| `$total = 3`, `$total += 1` | `total = 3`, `total += 1`: a name being set has no `$` | fix (KS6010) |
| `fn($n) => $n * 2` | `fn(n) => $n * 2` | fix (KS6010) |
| `numbered: no`, `new_page: yes` | `numbered: false`, `new_page: true` (or the flag on its own: `new_page`) | fix (KS6011) |
| `spawn(fn: job)`, `tools: lookup` | `spawn(fn: @job)`, `tools: [@lookup]`: a function is named as `@name` | fix (KS6012) |
| `files: "settings, .env"` | `files: ['settings', '.env']`: a list is an array | fix (KS6006) |
| `timeout: 30`, `ttl: '10m'` | `timeout: 30s`, `ttl: 10m`: a length of time has its unit and no quotes | fix (KS6018) |

## Statements and operators

| Before | Now | |
| --- | --- | --- |
| `if $n lt 10`, `ge`, `eq`, `ne` in a `.kscript` file | `if $n < 10`, `>=`, `==`, `!=` (XML keeps the words; `and`, `or` and `not` stay words in both) | fix (KS6012) |
| `} else if $b {` | `} elif $b {` | fix (KS6012) |
| `input(into = name)` | `input(into: name)` | fix (KS6012) |
| `catch NotFound, Conflict as e` | `catch NotFound \| Conflict as e` | fix (KS6012) |
| `catch err when $err.type == 'NotFound'` | `catch NotFound as err` (`when` still adds a test) | |
| `match $n { case 1 { } default { } }` | `match $n { 1 => { } _ => { } }`, the same arms as a match expression | |
| `test(name: "x", each: n, in: [1, 2], index: i) { }` | `for n at i in [1, 2] { test(name: "x {$n}") { } }` | |
| `KScript.Window(title: "Hi") { }`, `KScript.input(...)` | `import KScript.UI`, then `Window(title: "Hi") { }`: a package is named only in an import, and nothing is imported silently | |

## Types, models and errors

If your script compares `->type()` with `'list'`, switch that test to `->is_a(list)`. The type name now includes the element type, such as `'number[]'` or `'any[]'`.

| Before | Now | |
| --- | --- | --- |
| `$Repo`, `as: $Repo`, `$User->create(...)` | `Repo`, `as: Repo`, `User->create(...)`: a type is named bare | fix (KS6014) |
| `Repo->from($data)`, `$Repo->new(name: 'x')`, `$OutOfStock->new(item: 'tea')` | `Repo($data)`, `Repo(name: 'x')`, `OutOfStock(item: 'tea')`: calling the name makes the value | |
| `age: int?` in a type or model | `age?: int` (`T?` stays for a parameter that may be null) | fix (KS6014) |
| `model User { timestamps: true … }` | `model User(timestamps, unique: [email]) { … }`: options go in the brackets | fix (KS6014) |
| `catch QueryError`, `catch ConnectionError` | `catch Query`, `catch Connection`: no type's name ends in `Error` | |

Dividing by zero raises `Math` and a position outside a list raises `Index`. Both are built-in types now, so `catch Math` and `catch Index` work and are known to the editor.

## Statements that were renamed

| Before | Now |
| --- | --- |
| `before-each`, `after-each` | `before_each`, `after_each` |
| `expect-error` | `expect_error` |
| `log-provider` | `log_provider` |
| `css-text` | `css_text` |
| `marray` | `collection` |

## Statements that became commands

An action is a command, so its result can be used in an expression.

| Before | Now |
| --- | --- |
| the `write` statement | `@write_text(path, text)`, `@append_text(path, text)` or `@write_json(path, value)` (KScript.IO) |
| the `move` and `copy` statements | `@move(from, to)`, `@copy(from, to)` (KScript.IO) |
| the `delete` statement | `@delete(path)` (KScript.IO) |
| the `mkdir` statement | `@create_folder(path)` (KScript.IO) |
| the `assert` statement | `@assert(condition, message?)` (KScript.Test) |
| the `stylesheet` statement | `@stylesheet(path, vars?)`, which returns the CSS: `@write_text('site.css', @stylesheet('theme.ks.css'))` (KScript.Style) |
| the `dispatch` statement | the job's own method: `SendWelcome(user_id: 7)->dispatch()`, with `delay:`, `at:`, `queue:` or `connection:` (KScript.Queue) |
| the `download` statement | `@download(url, to)`, which returns the path of the file it saved (KScript.Net) |
| the `redirect` statement | `return @redirect(/thanks)` in a controller (KScript.Http); `<respond redirect="/thanks" />` in a page |

## Arguments

Argument names are `snake_case`. A hyphenated name is an error that says the new one: `max-tokens` is `max_tokens`, `must-exist` is `must_exist`, `new-page` is `new_page`, `on-click` is `on_click`. (On an HTML tag in a page, `on-click` keeps HTML's hyphen.)

| Statement | Before | Now |
| --- | --- | --- |
| `input`, `picker`, `menu`, `confirm`, `llm`, `capture`, `config`, `call` | `to: name` | `into: name`: the variable a result goes into. `to` is only ever a destination |
| `for`, `menu` | `index: i` | `at: i` (`for item at i in $list`) |
| `import`, `include`, `layout`, `Markdown`, `Figure`, `Bibliography` | `src` | `path` |
| `type` | `sample` | `path` |
| `Table` (KScript.Document) | `data`, `headers`, `align: "lrr"` | `rows`, `columns: { Name: 'name' }`, `align: ['left', 'right']`, as in a terminal `table` |
| `wait` | `ms`, `seconds` | `duration: 500ms`, or the value on its own: `wait(500ms)` |
| `test` | `each`, `in`, `index` | a `for` loop round the test (above) |
| `@color`, `log_provider` | `colour` | `color` |

## Commands

| Before | Now |
| --- | --- |
| `@to_upper($t)`, `@to_lower($t)` | `$t->upper()`, `$t->lower()` |
| `@string($v)`, `@number($t)`, `@date($t)`, `@type($v)` | `$v->to_string()`, `$t->to_number()`, `$t->to_date()`, `$v->type()` |
| `@json($t)`, `@to_json($v)`, `@yaml($t)`, `@to_yaml($v)` | `$t->json()`, `$v->to_json()`, `$t->yaml()`, `$v->to_yaml()` |
| `@escape($t)`, `@base64($t)`, `@url_encode($t)`, `@url_decode($t)` | `$t->escape()`, `$t->to_base64()`, `$t->url_encode()`, `$t->url_decode()` |
| `@pow($a, $b)` | `$a ** $b` |
| `@guid()` | `@uuid()` |
| `@contrast(...)` | `@contrast_color(color, dark?, light?)` |
| `@signed_in()` | `@is_signed_in()` |
| `@log(8, 2)` | `@logarithm(8, 2)` (`log` is the statement that writes a log entry) |
| `@config('database.port', 5432)` | `$config.database.port`, or `$config->get('database.port', 5432)` |
| `@exit(1)`, `@error('Too short')` | `exit 1`, `throw Validation "Too short"` |
| `@transaction(...)` | the `transaction` statement, or `$db->transaction(fn)` |

## Methods and fields

A plain fact about a value is a field, read with a dot and no brackets: `$file.name`, `$file.size`, `$response.status`, `$date.year`. Calling one (`$file->name()`) is an error that says so. Counting is `->count()` on every value.

| On | Before | Now |
| --- | --- | --- |
| a list, a map, text | `->length()` | `->count()` |
| text | `$text->is_empty()` to accept empty or whitespace-only text | Use `$text->is_blank()` for that check. `$text->is_empty()` now tests for zero characters. |
| a list | `->where(fn)`, `->append(item)` | `->filter(fn)`, `->push(item)` |
| a list | `$items->count_of($value)` to count strict matches | Use `$items->count_of(fn(x) => $x === $value)` for strict matches. Passing a value now compares with `==`. |
| a number | `->pow(2)` | `$n ** 2` |
| a response | `->ok()` | `.is_ok` |
| an ip | `->private()`, `->loopback()` | `.is_private`, `.is_loopback` |
| a jwt | `->expired()` | `.is_expired` |
| a database | `->in_transaction()` | `.is_in_transaction` |
| a password | `->strong()` | `->is_strong()` |
| a logger | `->enabled(level)` | `->is_enabled(level)` |
| a timezone | `->dst()` | `->is_dst()` |
| a cache | `->put(key, value)`, `->forget(key)`, `->flush()` | `->set(key, value)`, `->remove(key)`, `->clear()` |
| a session | `->put(key, value)`, `->forget(key)` | `->set(key, value)`, `->remove(key)` |
| a storage folder | `->path(name)` | `->path_of(name)`; `.path` is the folder itself |
| a color | `->contrast(other)` | `->contrast_ratio(other)` |
| a query, a model | `->avg(column)` | `->average(column)` |
| json | `->parse()` | `->json()` |

## The command line

| Command | Before | Now |
| --- | --- | --- |
| `kscript check`, `kscript queue` | `--format text\|json` | `--output text\|json` |
| `kscript check` | `--warnaserror` | `--warnings-as-errors` |
| `kscript openapi` | `--yaml`, `--json` | `--output yaml`, `--output json` |
| `kscript make`, `kscript docs`, `kscript mcp` | `--format kscript\|xml` | `--syntax kscript\|xml` |
| `kscript convert` | `--to kscript\|xml` | `--syntax kscript\|xml` |
| `kscript make` | `kscript make:component Hero` | `kscript make component Hero` |

`--output` is always how the result is printed, and `--syntax` is always which of the two forms a script is written in. Exit codes are the same for every command: 1 when a check or a test failed, 2 for a mistake on the command line or in the settings, 3 when a script stopped with an error.

## kscript.json

Keys are `snake_case`. An old key is an error that names the new one.

| Before | Now |
| --- | --- |
| `lint.warningsAsErrors` | `lint.warnings_as_errors` |
| `format.tabWidth`, `format.printWidth` | `format.tab_width`, `format.print_width` |
| `format.maxBlankLines`, `format.endOfLine` | `format.max_blank_lines`, `format.end_of_line` |
| `format.rules.statementPerLine`, `tidyTags`, `wrapAttributes` | `format.rules.statement_per_line`, `tidy_tags`, `wrap_attributes` |
| `format.rules.trimTrailingWhitespace`, `collapseBlankLines`, `finalNewline` | `format.rules.trim_trailing_whitespace`, `collapse_blank_lines`, `final_newline` |
| `format.rules.braceStyle`, `wrapStrings`, `wrapChains`, `wrapBodies`, `wrapComments` | `format.rules.brace_style`, `wrap_strings`, `wrap_chains`, `wrap_bodies`, `wrap_comments` |
| `make.format` | `make.syntax` |

## Names

Your own names follow the same rules as the built-in ones, and a name that breaks them is a warning (KS6013): functions, variables, fields and arguments are `snake_case`, and types, models, enums and error types are `PascalCase`. Rename `fn sendEmail()` to `fn send_email()` and `type repo` to `type Repo`.

The built-in names can no longer drift from these rules. The engine checks each statement, argument, command, method and field as a package registers it, and refuses one that breaks a rule, so a new package can't bring back a hyphenated argument or a second name for `->count()`. [Working on KScript](contributing.md) lists the rules.

## Files

| Before | Now |
| --- | --- |
| `hello.ks`, or a plain `hello.xml` that starts with `<kscript>` | `hello.ks.xml` for the XML form, `hello.kscript` for the default form |
| `name.test.xml` | `name.test.ks.xml` or `name.test.kscript` |
| files written by `kscript make` named in `snake_case` | `kebab-case` file names: `middleware/request-id.kscript`, `lib/notes-client.kscript` |

`kscript convert` turns a script from one form into the other, and renames the imports that point at it. See [The .kscript format](code-format.md).
