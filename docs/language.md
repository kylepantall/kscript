# The KScript language

This is the reference for KScript 2 scripts: the core language every script and page has, the packages you import for the rest, and a few tips for scripts that run on every OS.

## Variables and output

```kscript
count = 0 // declare
people = [{ name: 'Joan', age: 82 }]
count = $count + 1 // change it
count += 1
total = $price * $qty
const limit = 10
n: int = $args[0] // convert, or stop with a clear error

echo "Hello, {$name}! You have { $count } new { $count eq 1 ? 'message' : 'messages' }."
echo "$person.name is $person.age" // bare $name works in text too
echo "$people" // a map or list of maps prints as indented JSON
```

A map, or a list of maps, on its own in `echo` prints as indented JSON, coloured in a terminal (unless `NO_COLOR` is set). Inside text it stays on one line. `echo(format: json)` reformats JSON text, and `->to_json()` gives compact JSON.

Variables belong to the block they are declared in. `{{` and `}}` are literal braces.

`"Double quotes"` fill in `$name` and `{ }` wherever they are written: after `echo`, given to a variable, returned from a function or passed to a call. `'Single quotes'` keep text exactly as written.

```kscript
greeting = "Hello $name, you have { $count * 2 } points." // filled in
label = 'Write $name to use a variable.' // kept as written
route = "/users/{id}" // a { } that doesn't start with $ or @ is plain text
```

In a value, braces are only filled in when they start with `$` or `@`, so routes, patterns such as `"\d{3}"` and translation text such as `"Hello {name}"` keep theirs. The text given to `@sql` is the one exception: its `{ }` are sent as parameters, never pasted in (see [Data](data.md)).

## Expressions

`+ - * / % **`, `== !=` (loose: `'3' == 3`), `=== !==` (strict), `lt le gt ge` (or `> >=`), `eq ne`, `and or not`, `??`, `?.`, `a ? b : c`, `in`, list `[1, 2]` and map `{ name: 'Joan' }` literals, `$p.name`, `$list[0]` (negative indexes count from the end), ranges `0..10` and `0..100 step 5` (end excluded), `match $x { 1 => 'one', _ => 'other' }`, `->method()`, `|>` and `fn($x) => …`. In `.kscript` files `<` and `>` work as written. In XML, use the word forms inside quoted attributes, since XML can't hold a raw `<` there; the [shorthand start tags](#control-flow) accept `<` and `>` as written. Write `and`/`or` rather than `&&`/`||` everywhere.

## Control flow

```kscript
if $n lt 10 {
  // …
} elif $n lt 100 {
  // …
} else {
  // …
}

for p in $people {
  // …
}
for i at n in 0..10 step 2 {
  // …
}
for key, value in $map {
  // …
}
while $queue->count() gt 0 {
  // …
  break
  // …
  continue
  // …
}

match $choice {
  case 1 {
    // …
  }
  case 2, 3 {
    // …
  }
  when $it gt 9 {
    // …
  }
  default {
    // …
  }
}
```

In XML, `<if>`, `<elif>`, `<while>`, `<match>` and `<assert>` take their expression straight in the start tag: `<if $user.is_logged_in>` is short for `<if test="$user.is_logged_in">`, and `<match $choice>` for `<match value="$choice">`. Both forms work in scripts and pages; use the long one when the tag has other attributes (`<assert test="…" message="…" />`). Inside the tag, `<`, `>` and quotes need no escaping, so `<if $n > 3>` and `<if $name == "Joan">` work. A `>` ends the tag unless it has a space on both sides with more of the condition after it, so write `$n > 3`, not `$n>3`. `kscript convert` and the editor's `<if>` snippet use the short form.

Shorthand tags make a `.ks.xml` file KScript markup rather than strict XML: KScript's own parser, formatter and editor support read it, but generic XML tools such as `xmllint` will reject it. Stick to `test="…"` if a file has to stay valid XML.

### Early returns instead of else

Inside a function or a loop, end a branch early instead of writing `else`: `return` in a function,
`continue` or `break` in a loop. The main path then stays at the left edge and each special case is dealt with
first.

```kscript try
fn grade(score: number) {
  if $score ge 90 {
    return 'A'
  }
  if $score ge 75 {
    return 'B'
  }
  return 'C'
}

echo "{ @grade(82) }"
```

`kscript check` and the editor warn about `else` and `elif` with KS6001 there, and at the top of a script when
every branch before already ends with `return`, `break`, `continue`, `throw` or `exit`. The quick fix
removes the `else` (or turns `elif` into a separate `if`) and, when the chain is the last statement of the
function or loop, adds the `return` or `continue` for you. Pages, `<live>` blocks and `for`…`else` (for an
empty list) are not flagged, since there is nothing to return from. KS6001 comes from the default
[standard](standards.md), `kscript:recommended`. To turn it off for a project, set
`"lint": { "rules": { "KS6001": "off" } }` in `kscript.json` (or in a standard your projects share).

### Unused code

`kscript check` and the editor also point out code that does nothing. In the editor it is drawn faded, with quick
fixes to delete it or to rename it with a leading `_`, which marks it as unused on purpose (`_` names are never
reported).

| Code | What it flags |
| --- | --- |
| KS6002 | A function nothing calls. Calls from the other files of the project count: a library function another script imports and calls, a controller a route file names (`to="people.show"`, or `<Resource to="people">` for its index, store, show, update and destroy), and middleware named in `kscript.json`. A function that only calls itself is still unused. |
| KS6003 | A variable, constant or loop variable whose value is never read, including one that is only ever set. A script's top-level constants are exported like its functions, so they count as used when an importing file uses them. In pages, only a function's own variables are checked, because a layout or component may read the rest. |
| KS6004 | A function parameter the body never reads. For a lambda, only the parameters after the last one it reads, since the caller decides what it is passed: write `fn($_y) => ...` to keep the position. |
| KS6005 | An import nothing uses: an `as` alias, a name in `names="..."`, a whole script, or a package none of whose elements, commands, methods or variables appear. |
| KS6006 | Not unused code, but a style hint: a list of names written as one string in a `.kscript` file (`Middleware(use: "log, cors")`). Write it as an array, `use: [log, cors]`; the quick fix does it for you. XML attributes are text, so `use="log, cors"` is fine there. |

The project is the folder with the nearest `kscript.json` and everything under it (hidden folders, `bin`, `obj` and
`node_modules` are skipped). A script outside a project is checked against the other files in its own folder. A
function that is part of an API used from outside the project can say so with `@public` in its doc comment:

```kscript
/// Kept for scripts that still call it.
/// @public
fn titlecase(text: string) => $text->upper()
```

Like KS6001, these come from the default [standard](standards.md), `kscript:recommended`, as warnings
(`kscript:strict` makes them errors). A project can change or turn off each one in `kscript.json`, such as
`"lint": { "rules": { "KS6004": "off" } }`.

## Functions

```kscript
fn greet(who: string, greeting = 'Hello') {
  return $greeting + ', ' + $who + '!'
}
fn square(n: number) => $n * $n

echo "{ @greet('World') } { 4 |> @square() }"
@greet('Kyle')
spawn(fn: background_job)
```

Functions can be called before they are declared, recurse, and be passed around: `@square` is the function itself. Lambdas are values too: `by_age = fn($a, $b) => $a.age - $b.age`.

### Named arguments

Any call can name its arguments, in any order: built-in commands, methods, your own `fn` functions and lambdas.

```kscript
echo "{ @known_folder(name: 'downloads') }"
echo "{ @greet(greeting: 'Hi', who: 'Kyle') }"
echo "{ @range(1, step: 2, end: 10) }"
echo "{ 'abc'->pad_left(char: '*', width: 6) }"
echo "{ @join_path(base: $folder, ['archive', $name]) }"
```

Positional arguments fill parameters in order, and each `name: value` fills the parameter of that name, so you can skip optional ones in between. A positional argument written after named ones fills the first parameter still empty, which is how `@join_path(base: $folder, [...])` reads. Naming a parameter that doesn't exist, or giving one twice, is an error with a "did you mean" hint. The editor completes parameter names after `(` or `,`.

### Flags

A yes/no parameter can be written as a bare word: `recursive` sets it to yes and `not recursive` to no, so calls read like a sentence.

```kscript
all = @files($folder, pattern: '*.txt', recursive)
echo "{ $data->to_json(not indented) }"
fn save(path: string, overwrite: bool = false) {
  // …
}
@save('out.txt', overwrite)
```

### Enums and consts

`enum` declares a set of named values, and `const` a value that can't change.

```kscript try
enum Size { small, medium, large }
const max_items = 10
fn pack(size: Size, gift: bool = false) => 'Packing ' + $size

echo "{ @pack(large) } { @pack(size: small, gift) } { $Size.medium }"
```

A parameter typed with an enum only takes its values (`'large'` or bare `large`), and the editor suggests them. A list of an enum takes an array of bare values too: with `fn pack_all(sizes: Size[])`, write `@pack_all([large, small])`. Anywhere else, a bare word in an array is an error, because names of variables start with `$` (`[$max_items, 3]`) and functions are passed with `@` (`[@twice]`). Built-in commands have enum parameters too: `@known_folder(downloads)` takes `home`, `desktop`, `documents`, `downloads`, `music`, `pictures`, `videos`, `appdata` or `temp`. `$Size` itself is a map from each name to itself, so `'large' in $Size` checks a value. Top-level `<const>` and `<enum>` declarations are exported to scripts that import the file.

## Typed data

`type` declares the shape of some data, such as an API response. Data that passes through it is checked and turned into a record: a map that knows its type, so the editor completes its fields and a typo like `$repo.stras` is caught. Fields can also be `email`, `password`, `handle`, `uuid` or `regex`, and types can add rules to other types or join them. See [Types and validation](types.md).

A type is a block with one field per line, `name: type`:

```kscript try
type User {
  login: string
  id: int
}

type Repo {
  name: string
  stars: int from stargazers_count
  owner: User
}

repo = $Repo->from({ name: 'kscript', stargazers_count: 12, owner: { login: 'kyle', id: 1 } })
echo "$repo.name has $repo.stars stars, owned by $repo.owner.login"
```

`stars: int from stargazers_count` reads the field from a key with another name, and `owner: User` is another type, so the nested data is checked and turned into a `User` record too. `$Repo->from(data)` maps a map, a list of maps or JSON text.

### Field lines

A field line is `name[?][: type] [from path] [= default | => value]`. Each part is optional except the name:

```kscript
enum Visibility { public, private, internal }

/// A GitHub repository.
type Repo {
  name: string
  /// How many people starred it.
  stars: int from stargazers_count
  owner: User
  owner_name: string from owner.login
  topics: string[] = []
  license?: string from license.name
  visibility: Visibility
  updated: date from updated_at
  label => $name + ' (' + $stars + ' stars)'
}
```

| Part | What it does |
| --- | --- |
| `: type` | A built-in (`string`, `int`, `float`, `number`, `bool`, `date`, `list`, `map`, `fn`, `any`, `email`, `password`, `handle`, `uuid`, `regex`), an `enum`, another `type`, `T[]` for a list, `T?` to allow null. Rules can follow in brackets: `string(min: 1)`. Without it the field takes any value. |
| `?` after the name | The value can be missing or null. |
| `from path` | Where to read the value when it isn't the field's name: `stargazers_count`, `owner.login`, `items[0].name`. Nested data comes out flat. Quote a path that has spaces. |
| `= default` | The value when the data has none. The field is then optional. |
| `=> value` | Works the field out from the other fields (they are `$name`, `$stars` and so on) instead of reading it. |
| `///` above the line | Shown on hover and in completion. |

The type itself takes options in brackets: `type Repo(extra: error) { … }`. `extra` says what happens to keys the type doesn't list: `drop` (the default), `keep` or `error`. `sample: data/repo.json` takes the fields from a saved example of the data: objects become nested types, and keys only some list items have become optional. Lines in the block replace the sampled fields they name. Types can refer to each other in any order and to themselves, and top-level types are exported to scripts that import the file.

A small type can also go on one line, with the fields written like `fn` parameters: `type Point(fields: "x: int, y: int")`. `kscript convert` writes it as a block.

In XML, a type is `<type name="Repo">` with a `<field name="stars" type="int" from="stargazers_count" />` element for each line, or a `fields="..."` attribute for the one-line form. Switch the examples to **XML** to see them.

### Where data meets a type

```kscript
repo = @api($url)->check()->json(as: $Repo) // KScript.Net
repos = @read_json('repos.json', as: $Repo) // KScript.IO; a list maps item by item
user = @json($text, as: $User)
user = $User->from($data) // a map, a list or JSON text
repo: Repo = $data // also on const and plain assignment
fn show(repo: Repo, tags: string[]) {
  // …
}
```

`as:` takes the type as a value (`as: $Repo`) or as text (`as: 'Repo[]'`). `@type($repo)` is `'Repo'`. A record still works everywhere a map does, but reading or setting a field the type doesn't have is an error, and setting a field converts the value to the field's type.

When the data doesn't fit, mapping fails with one `Shape` error that lists every problem with its JSON path:

```text
The response from https://api.github.com/... doesn't fit Repo (2 problems):
  $.stargazers_count: needs an int, but got the string 'lots'.
  $.visibility: must be a Visibility (public, private, internal), but got the string 'secret'.
```

In a `catch`, `$err.problems` is a list of `{ path, field, message }` maps. `$Repo->check($data)` returns that list without throwing (empty when the data fits), `$Repo->fields()` lists the fields, and `$Repo->schema()` describes the type as JSON Schema.

## Values and methods

Values are strings, numbers, bools, lists, maps, dates, functions and null, plus values packages add (files, HTTP responses). Every value has methods, called with `->` and chained:

```kscript
echo "{ $name->trim()->upper() }"
echo "{ $people->filter(fn($p) => $p.age ge 18)->map(fn($p) => $p.name)->join(', ') }"
echo "{ $people |> sort_by(fn($p) => $p.name) |> first() }"
echo "{ @now()->add_days(7)->format(pattern: 'd MMMM yyyy') }"
```

The editor completes the methods for each type and shows what they do. `@json(text)` and `$value->to_json()` convert to and from JSON.

### Adding methods to a type: extend

`extend` adds your own methods to a type, built-in or your own. Inside, each `fn` is a method, and `$this` is the value it is called on:

```kscript try
extend date {
  /// The date as text for an API: 2026-11-21.
  fn json() => $this->format('yyyy-MM-dd')
  fn is_weekend() => $this->format('ddd') in ['Sat', 'Sun']
}

extend list {
  fn second() => $this[1]
}

extend string {
  fn slug() => $this->lower()->trim()->replace(' ', '-')
}

echo "{ @date('2026-11-21')->json() } { @date('2026-11-21')->is_weekend() }"
echo "{ ['a', 'b', 'c']->second() } { 'Hello World'->slug() } { 'Hello World' |> slug() }"
```

You can extend `string`, `number` (or `int` and `float`), `bool`, `list`, `map`, `fn`, `date`, `email`, `password`, `handle`, `uuid` and `regex`, a package's values such as `file` or `response`, and your own `type`s, whose methods see `$this` (a record) and its fields as `$this.name`. Methods take parameters, defaults and named arguments like any `fn`.

A method can't take the name of one the type already has, so a typo or a clash never changes what a built-in method does. To replace one on purpose, write `override`:

```kscript
extend date {
  override fn to_json() => '"' + $this->format('yyyy-MM-dd') + '"'
}
```

In XML, `extend` is `<extend type="date">` holding `<fn>` elements, and `override fn` is `<fn override="yes">`.

**Where extensions apply.** A file's own `extend` blocks apply in that file and in every file that imports it. In a project (a folder with `kscript.json`), every file in the `extensions/` folder applies to every script, test and page in the project, with no import needed. Split them up however you like, one file per type for example:

```text
extensions/
  date.kscript        extend date { … }
  list.kscript        extend list { … }
  text/
    slug.kscript      extend string { … }
```

`kscript make extension date --methods json,is_weekend` writes `extensions/date.kscript` to start from. To keep them elsewhere, list folders or globs in `kscript.json`, which replaces the default folder: `"extensions": ["extensions", "shared/**/*.kscript"]`. Two extension files adding the same method to the same type is an error that names both. A script's own `extend` (or an import's) wins over the project's.

The editor completes and checks extension methods only where they apply: in a file that neither declares, imports nor (through the project) gets `->json()` on dates, `@today()->json()` is still an unknown method. Hover shows the method's `///` docs and which file adds it, and go to definition opens that file.

## Errors

```kscript
try {
  data = @read_json($path)
} catch FileNotFound {
  echo "No file at {$path}, using defaults."
} catch err {
  echo "Failed: {$err.message} (line {$err.line})"
} finally {
  echo "done"
}
throw Validation "Age must be positive, got {$age}"
```

`catch` takes the error types it handles, joined by `|`, and `as` names the error: `catch FileNotFound | Permission as e`. A catch with only a lower-case name (`catch err`) handles every error. `when` adds a test: `catch Network as e when $e.status eq 503`. Catches are tried in order and the first that fits runs. Error type names start with a capital letter, which is how `catch` tells them from the variable's name.

Unknown elements, attributes and syntax errors are reported before a script starts, so a typo near the end doesn't leave a job half done.

An error nothing catches is printed as a KScript error, with where it happened and its type, and the script exits with code 3:

```text
   ERROR  app.kscript:2:11: Name: Unknown command @nope().
```

kscript shows every failure this way, in scripts, `kscript serve` error pages and all the other commands, never as a .NET exception. A failure that comes from the computer rather than the script gets the error type that fits (`FileNotFound`, `Access`, `IO`, `Network` and so on), and one kscript didn't expect is an `Internal` error, which is a bug worth [reporting](https://github.com/kylepantall/kscript/issues). To see the .NET exception and stack trace behind an error, run with `--debug` (before the script's path: `kscript --debug app.kscript`) or set `KSCRIPT_DEBUG=1`, which also works for `kscript serve` and compiled programs.

### Error types

Declare your own error types with `error`. Fields work as in a [`type`](#typed-data), the message can use them, and `extends` says what kind of error it is:

```kscript try
/// No person has that id.
error PersonNotFound(message: "No person with id {$id}") extends NotFound {
  id: int
}
error OutOfStock(status: 409, message: "{$item} is out of stock") {
  item: string
  left: int = 0
}

fn find_person(id: int) {
  throw PersonNotFound(id: $id)
}

try {
  @find_person(42)
} catch NotFound as e {
  echo "{ $e.message } ({ $e.type }, status { $e.status })"
}
```

- **Throwing.** `throw PersonNotFound(id: 42)` makes the error and throws it. Its arguments are its fields, by name, plus `message:` (instead of the type's message) and `cause:` (the error that led to this one). A field without a default must be given, and the editor flags missing and unknown fields. `throw Validation "Too short"` still works for a type with no fields, and `throw Validation(field: 'name', message: 'Too short')` adds fields to a built-in one.
- **Catching.** `catch NotFound` catches `PersonNotFound` too, because it extends `NotFound`. In the catch, `$e.id` reads a field, and the editor completes the fields of the types the catch names. Every error has `type`, `message`, `line`, `column`, `file`, `status` and `cause`. `$e->is(NotFound)` checks the type in code, and `$e->chain()` lists it: `['PersonNotFound', 'NotFound', 'Error']`.
- **Rethrowing and wrapping.** A bare `throw` inside a catch rethrows the error it is handling, unchanged. `throw $e` does the same with a saved error. To add context, wrap it: `throw ConfigBroken(path: $path, cause: $e)`, then `$err.cause.message` reads the original.
- **Making one without throwing.** `$OutOfStock->new(item: 'tea')` gives the error as a value, to return or throw later.
- **Status.** `status` is the HTTP status a [KScript.Http route](http.md#errors-and-status-codes) answers with when nothing catches the error. A type inherits it, and its fields and message, from the type it extends.
- **Exports.** Top-level error types are exported, like functions and types, so `import "errors/payment-failed.kscript"` brings them in. `kscript make error PaymentFailed --status 402 --extends Validation --fields "order: int"` writes one to `errors/`.

The built-in error types, which KScript itself raises, form a tree that `catch` follows:

| Type | Extends | Raised for |
| --- | --- | --- |
| `Error` | | Every error. `catch Error` catches everything. |
| `Syntax`, `Name`, `Null`, `Argument`, `Value`, `Const` | `Error` | Mistakes in a script: bad syntax, an unknown name, null where a value is needed, wrong arguments, a bad value, changing a const. |
| `Type` | `Error` | A value of the wrong type. `Shape` extends it, for data that doesn't fit a `type` (`$e.problems` lists each problem). |
| `Format` | `Error` | Text in the wrong format. `Json` and `Yaml` extend it. |
| `IO` | `Error` | Files. `FileNotFound`, `Permission` and `Access` extend it. |
| `Network` | `Error` | Requests. `Timeout` extends it, and `$e.status` is the response's status. |
| `Import`, `Config`, `Input`, `Limit`, `Context`, `Platform`, `Cancelled`, `Retired`, `Http`, `AI`, `Assertion` | `Error` | Imports, settings, input, limits such as recursion, the wrong place, the OS, a stopped script, KScript 1 features, KScript.Http apps, language models, failed test checks. |
| `Validation` (422), `BadRequest` (400), `Unauthorized` (401), `Forbidden` (403), `NotFound` (404), `Conflict` (409) | `Error` | For your own code to throw or extend. The number is the HTTP status. |

A name in `catch` or `throw` that is neither declared nor built in still works (as a kind of `Error`), but the editor warns about it (KS5011), since it is usually a typo.

## Settings: $config

Every script can read settings without an import. `$config` reads `config.json`, `config.yaml` (or `config.yml`) and `.env` from the script's folder the first time it is used:

```yaml
# config.yaml
database:
  host: localhost
  port: 5432
features: [search, cart]
```

```kscript
echo "{$config.database.host}:{$config.database.port}"
timeout = @config('http.timeout', 30) // a path, and a default when it isn't set
if $config->has('features') {
  echo "{ $config.features->join(', ') }"
}
key = $config->require('api.key') // stops with a clear message when it's missing
```

**Environments.** `kscript --env staging script.kscript` (or `KSCRIPT_ENV=staging`, or `config(env: staging)`) layers the environment's files over the base files. Later files win, and maps merge field by field:

1. `config.json`, `config.yaml`, `config.yml`, then `.env`
2. `config.staging.json`, `config.staging.yaml`, `config.staging.yml`, then `.env.staging`
3. `staging.json`, `staging.yaml`, `staging.yml`
4. with `config.user` in `kscript.json` (or `config(user: yes)`), the person's own `config.json`, `config.yaml` or `config.yml` from the app's config folder (see [App folders](storage.md#the-persons-own-settings-configuser))
5. environment variables with the prefix, when there is one

**Environment variables.** With a prefix, variables override the files, and `__` nests: with `config(prefix: APP_)`, `APP_DATABASE__HOST=db.internal` sets `database.host`. Names don't depend on case, so `$config.database.host` and `$config.DATABASE.HOST` are the same setting. Values such as `true`, `false` and `8080` become booleans and numbers.

**.env files** hold `NAME=value` lines, with `#` comments, `export` in front if you like, `'single quotes'` taken as written, and `"double quotes"` that allow `\n` and can span lines. `${NAME}` and `${NAME:-default}` use another variable, in .env files and in text in JSON and YAML files. Values from .env files also become environment variables unless one is already set, so `@env('ANTHROPIC_API_KEY')` and KScript.AI find a key kept in `.env`.

**`config`** reads the settings again with other options. Without `to`, it changes `$config`:

```kscript
config(env: production, prefix: SHOP_, files: "settings, secrets.yaml?, .env", root: conf, defaults: { server: { port: 8080 } }, require: "database.host, api.key")
config(files: themes/dark.json, to: theme) // a separate set of settings in $theme
```

`files` lists the base files (a name without an extension means .json, .yaml and .yml, and a name ending in `?` may be missing). `$config->environment()` gives the environment, `$config->sources()` lists the files that were read, `$config->all()` gives every setting as a map, and `$config->reload()` reads them again. `@yaml(text)`, `@to_yaml(value)` and `@parse_env(text)` work on text, and KScript.IO adds `@read_yaml(path)`, `@read_env(path)` and `@write_yaml(path, value)`.

Pages served by `kscript serve` read settings from the site folder. They never change environment variables, and `kscript serve` never serves `config.*` files or any `.yaml`, `.yml` or `.env` file. Keep secrets in `.env` or in YAML, because a JSON file named after an environment (such as `staging.json`) is served like any other file.

## Logging: $log

Every script has a logger. Entries at `info` and above go to the terminal (stderr) until you set up somewhere else for them:

```kscript
log "Import started"
log(level: warn, category: payments, fields: { order: $order.id }) "Card declined for {$customer.name}"
$log->info('Saved', { id: $id, size: $bytes })

db = $log->named('db')->with({ host: $config.database.host })
$db->debug('Connected') // category db, with a host field
if $log->enabled('debug') {
  $log->debug(@to_json($everything))
}

try {
  @read_json('missing.json')
} catch err {
  $log->error('Could not read the file', $err) // error, error_type and error_line fields
}
```

Levels are `trace`, `debug`, `info`, `warn`, `error` and `fatal`. **Providers** decide where entries go, and are usually set in config:

```yaml
# config.yaml
logging:
  level: info                 # the lowest level written (default info)
  levels:
    db: debug                 # per category; db also covers db.queries
  providers:
    - type: console           # stderr, coloured at a terminal; stream: stdout, format: json, time: HH:mm:ss.fff
      level: warn
    - type: file              # text, or json lines; roll: daily writes app-2026-11-20.log
      path: logs/app.log
      format: json
    - fn: notify              # a fn in the script, called with each entry
      level: error
    - type: http              # from KScript.Net: POSTs entries as JSON, batch at a time
      url: https://logs.example.com/ingest
      headers: { Authorization: "Bearer ${LOG_TOKEN}" }
      batch: 20
```

`log-provider(…)` adds one from a script, with the same options (`replace` removes the ones already set up, including the default console one). A `memory` provider keeps entries for `$log->entries()`, which is handy in tests. Entries that a provider holds back, such as an http batch, are sent when the script ends or on `$log->flush()`. A provider that fails is reported once on stderr, and the others carry on.

Packages add provider types with `p.LogProvider("type", description, create)`, and programs that run KScript can add their own with `host.Logs.AddProvider(provider, level)`, where a provider implements `ILogProvider`.

## Translations: @t()

Put translations in a `locales` folder next to the script (or in the site folder): `locales/en.json`, `locales/fr.yaml`, or a folder per locale such as `locales/de/shop.json`, whose keys start with `shop.`.

```yaml
# locales/fr.yaml
hello: Bonjour, {name} !
cart:
  items:
    "=0": Votre panier est vide
    one: "{count} article"
    other: "{count} articles"
```

```kscript
locale(set: fr)
echo "{ @t('hello', { name: $user.name }) }" // Bonjour, Ann !
echo "{ @t('cart.items', { count: $cart->count() }) }" // 3 articles
echo "{ @format_currency($total, 'EUR') } { @format_date(@today(), 'long') }" // 9,50 € 20 novembre 2026
```

- **Plurals.** With a `count`, `@t()` picks `zero`, `one`, `two`, `few`, `many` or `other` by the language's own rules. These are the CLDR rules for English, French, Portuguese, Russian, Ukrainian, Polish, Czech, Arabic, Hebrew, Irish, Welsh, Japanese, Chinese, Korean and more. An exact form such as `=0` wins.
- **Fallbacks.** A missing translation is looked for in `fr-CA`, then `fr`, then the default locale (`en` unless set). If it isn't anywhere, you get the key itself, and a warning is logged once under the `i18n` category.
- **The locale** comes from `locale(set: fr)`, from `locale(from: …)` (the best available match for a list or an Accept-Language header), from `$config.i18n.locale`, from `KSCRIPT_LOCALE`, or from the computer's language. `@locale()` gives it, and `@locales()` lists the locales there are files for.
- **Pages** pick it in this order: a path prefix (`/fr/about` serves `about.html` in French when there are French translations), `?lang=fr`, a `lang` cookie, and then the browser's Accept-Language. `@locale_path($request.path, 'de')` gives the link to the same page in German. The default locale has no prefix.
- **Formatting.** These follow the locale, or one you pass as the last argument:
  - `@format_number(1234.5)` gives 1,234.5 or 1 234,5;
  - `@format_percent(0.25)`;
  - `@format_currency(9.5, 'GBP')`;
  - `@format_date(date, 'short' | 'medium' | 'long' | 'full' | 'time' | 'datetime' | 'd MMMM yyyy')`.
- **Settings:** `i18n: { folder: lang, default: en-GB, locale: fr }` in config, or `translations(folder: lang, default: en-GB)`. `@has_t(key)` checks whether a key has a translation, and `@plural(count)` gives the plural category.

## Packages

KScript.Core (everything above) is always there. Everything else comes from a package, imported at the top of the script, so a script only loads what it uses: a terminal script never loads the web server.

```kscript
import KScript.Terminal
import KScript.Math as math // commands stay under the prefix: @math.sqrt(2)
import "lib/strings.kscript" as str // another script's top-level functions and constants
import slugify from "lib/strings.kscript"
// …
```

| Package | What it adds |
| --- | --- |
| `KScript.Terminal` | `input(to, type, default, secret)` (type can be `email`, `handle`, `uuid` or `password`), `picker` (a file or folder path), `menu(options)`, `confirm`, `pause`, `clear`, `@color(text, colour: 'green')`, `@bold`, `@dim`, `@underline`, `@terminal_width()`, `@is_terminal()` (no when output is piped), `echo(format: markdown)`, and the kscript command line's own look: `info`, `success`, `warn`, `alert(type: error)`, `heading`, `detail(name)`, `bullets(items)`, `task(name) { }`, `table(rows, columns, headers, align)` (see [Terminal output](#terminal-output)) |
| `KScript.Math` | `@sqrt` `@pow` `@round(x, digits: 2)` `@clamp` `@log` `@sin` `@cos` `@atan2` `@hypot` `@gcd` `@factorial` `@random(low: 1, high: 6)` `@pick` `@shuffle` `@sum` `@average` `@median` `@stdev`, `$pi` `$e` |
| `KScript.IO` | `@read_text` `@read_lines` `@read_json` `@write_text` `@append_text` `@write_json` `@files(folder, pattern: '*.txt', recursive)` `@folders` `@file(path)` `@exists` `@join_path(base: $dir, ['a', 'b.txt'])` `@known_folder(downloads)`, `<write>` `<move>` `<copy>` `<delete>` `<mkdir>`. File values have `->name()` `->extension()` `->size()` `->modified()`… |
| `KScript.Net` | `@api(url, options: { method, headers, body, timeout })` (a path such as `'/api/notes'` asks the project's own routes, see [Calling your own API](http.md#calling-your-own-api)) returning a response (`->status()` `->ok()` `->json()` `->text()` `->check()`), `download(url, to)`, `@url_encode`, `@query_string` |
| `KScript.Markdown` | `@markdown(text)` and `@read_markdown(path)` (`html`, `title`, `meta` from front matter), `->markdown()` |
| `KScript.Web` | The elements of `kscript serve` pages (below). Pages have KScript.IO, KScript.Markdown and KScript.Web without importing them. |
| `KScript.AI` | `@llm(prompt, options)`, `llm(to, system, schema, tools)`, `@chat(system)` with `->ask()` `->history()` `->reset()`. Claude, with the key from `ANTHROPIC_API_KEY` (see [Language models](#language-models-kscriptai)). |
| `KScript.Style` | Stylesheets written in KScript (`.ks.css`, `.ks.scss`, `.ks.sass`); `@up`, `@down`, `@between`, `@rem`, `@fluid`, `@lighten`, `@darken`, `@mix`, `@alpha`, `@contrast`, `@css_var`, `@css_vars`; `stylesheet(src, to, vars)` (see [Stylesheets](stylesheets.md)). |
| `KScript.Http` | Routes, controllers and middleware for `kscript serve`: a `KScript.Routes { }` file holds `Get` `Post` `Put` `Patch` `Delete` `Any` `Route` `Resource` `Group` `Middleware`. Controllers use `@view`, `@response`, `@redirect`, `@abort` and `@route`. See [Routes, controllers and middleware](http.md). |
| `KScript.Storage` | The app's own folders, in the right place on each platform: `@storage(config)` (also `data`, `cache`, `state`, `logs`) with `->read` `->read_json` `->write` `->write_json` `->append` `->list` `->delete` `->folder` `->path`, plus `@app_name()` and `@app_folders()`. See [App folders](storage.md). |
| `KScript.Cache` | `$cache` (in memory) and `@cache(file)` with `->get` `->put` `->remember(key, fn, ttl: '10m')` `->has` `->forget` `->increment` `->ttl` `->keys` `->flush`. See [Caching](cache.md). |
| `KScript.Auth` | Sign-in for `kscript serve` apps: `@register`, `@attempt`, `@login`, `@logout`, `@user()`, `@signed_in()`, `$session` (`->get` `->put` `->flash` …), `@users()`, `@csrf_field()`, `@reset_token`, `@reset_password`, `@api_token`, and the `auth` and `guest` middleware. See [Sign-in](auth.md). |
| `KScript.Document` | `KScript.Document(template: paper, to: "paper.pdf") { }` with `Section`, `Paragraph`, `List`, `Table`, `Figure`, `Math`, `Bibliography` and more; `Letter` and `Invoice`; `->save` `->pages` `->words` `->markdown` `->latex` `->html`. See [Documents](documents.md). |
| `KScript.UI` | Desktop windows: a `KScript.Window { }` block opens one. `Column` `Row` `Grid` `Text` `Button` `TextBox` `CheckBox` `Select` `Slider` `Box` `Spacer` `Timer`, `@close_window()`. See [Desktop apps](desktop.md). |
| `KScript.Test` | `test`, `describe`, `before-each`, `after-each`, `expect`, `assert`, `expect-error`, `capture`, `@run_script`, `@assert`, `@fail`. Test files import it on their own (see [Testing scripts](testing.md)). |

Using something from a package that isn't imported stops with `<input> is part of KScript.Terminal. Add <import package="KScript.Terminal" />`, and the editor offers to add the import. A statement written with the `KScript.` prefix, such as `KScript.input`, imports the package that has it without an `import`. A project can import packages for every script in `kscript.json` (below).

### Terminal output

KScript.Terminal prints the same way the `kscript` commands do, so a script's output looks like the rest of the tool. Colours are only used at a terminal: piped output, files and tests get the same text without them, and `NO_COLOR`, `FORCE_COLOR`, `--ansi` and `--no-ansi` work as they do for `kscript` itself.

```kscript
import KScript.Terminal

people = [{ name: 'Joan', age: 82 }, { name: 'Sam', age: 9 }]
heading "Report"
detail(name: People) "{ $people->length() }"
task(name: "Adding up") {
  total = 1 + 2
}
table(rows: $people)
info "Wrote [report.txt]."
alert(type: error) "The file [data.csv] is empty."
```

```text
Report
  People .................................................... 2
  Adding up .......................................... 2ms DONE
  ┌──────┬─────┐
  │ name │ age │
  ├──────┼─────┤
  │ Joan │  82 │
  │ Sam  │   9 │
  └──────┴─────┘
   INFO  Wrote [report.txt].
   ERROR  The file [data.csv] is empty.
```

`[bracketed]` parts of a label are shown in bold. `alert(type: error)` writes to stderr. Numbers in a table line up on the right; `align: ['left', 'right']` sets each column.

## Language models: KScript.AI

`KScript.AI` lets a script ask Claude. The API key comes from the `ANTHROPIC_API_KEY` environment variable (or a token in `ANTHROPIC_AUTH_TOKEN`, or the profile `ant auth login` saved, when the `ant` command is installed), never from the script. `ANTHROPIC_BASE_URL` sends requests to another host, such as a proxy. Scripts use `claude-opus-5-5` unless they ask for another model, or `KSCRIPT_AI_MODEL` names one.

```kscript
import KScript.AI

// One question, one answer.
echo "{ @llm('Write a haiku about XML.') }"

// A prompt written as a template, with a system prompt.
customer = { name: 'Ann' }
message = 'My order is late arrived.'
llm(to: reply, system: You answer customer emails politely and briefly.) "{$customer.name} wrote: {$message}"

// Structured output: the answer is a map with these fields.
report = 'The login page spins forever on Safari.'
llm(to: ticket, schema: { category: 'bug|feature|question', urgency: 'int 1-5', summary: 'one line' }) "Triage this report: {$report}"
echo "{$ticket.category} ({$ticket.urgency}): {$ticket.summary}"

// Tools: the model may call your fn functions, and sees what they return.
/// The shipping status of an order.
fn order_status(id: int) => $id == 42 ? 'shipped' : 'unknown'
llm(to: answer, tools: order_status) "Where is order 42?"

// A conversation that remembers what was said.
chat = @chat('You are a patient maths tutor.')
echo "{ $chat->ask('What is a prime number?') }"
echo "{ $chat->ask('Is 91 one?') }"
```

- **`@llm(prompt, options)`**: `options` is a model name, or a map with `model`, `system`, `schema`, `tools` (a list such as `[@order_status]`) and `max_tokens` (16,000 by default).
- **Schemas**: each field is required. `'string'`, `'number'`, `'int'`, `'bool'` and `'date'` name a type, `'int 1-5'` gives a range, `'a|b|c'` lists the choices, a map is a nested object, `['string']` is a list, and any other text describes a string field.
- **Tools** are `fn` functions. Their `params` (with types such as `id: int`) and `description` tell the model how to call them. If a tool fails, the model is told the error and can carry on.
- **Errors** have the type `AI`, so a `try` with `catch e` can check `$e.type` and handle a missing key, a refusal, or an answer cut off by `max_tokens`. When Claude's safety checks decline a request, the API first retries it on a fallback model.

## Writing portable scripts

- Build paths with `@join_path(base: $dir, ['reports', 'file.txt'])` (KScript.IO) rather than hard-coding `\` or `/`.
- Find the user's folders with `@known_folder(downloads)` (KScript.IO): `home`, `desktop`, `documents`, `downloads`, `music`, `pictures`, `videos`, `appdata` or `temp`. It follows each system's own settings, such as Windows' Downloads location and Linux's `user-dirs.dirs`.
- `$os.name` is `windows`, `linux` or `macos`, and `$os.home`, `$os.temp` and `$os.separator` are there too; `$env.HOME` reads an environment variable.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.
