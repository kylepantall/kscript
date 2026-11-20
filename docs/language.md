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
fn grade(score) {
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

The project is the folder with the nearest `kscript.json` and everything under it (hidden folders, `bin`, `obj` and
`node_modules` are skipped). A script outside a project is checked against the other files in its own folder. A
function that is part of an API used from outside the project can say so with `@public` in its doc comment:

```kscript
/// Kept for scripts that still call it.
/// @public
fn titlecase(text) => $text->upper()
```

Like KS6001, these come from the default [standard](standards.md), `kscript:recommended`, as warnings
(`kscript:strict` makes them errors). A project can change or turn off each one in `kscript.json`, such as
`"lint": { "rules": { "KS6004": "off" } }`.

## Functions

```kscript
fn greet(who, greeting = 'Hello') {
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
fn save(path, overwrite: bool = false) {
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

A parameter typed with an enum only takes its values (`'large'` or bare `large`), and the editor suggests them. Built-in commands have enum parameters too: `@known_folder(downloads)` takes `home`, `desktop`, `documents`, `downloads`, `music`, `pictures`, `videos`, `appdata` or `temp`. `$Size` itself is a map from each name to itself, so `'large' in $Size` checks a value. Top-level `<const>` and `<enum>` declarations are exported to scripts that import the file.

## Typed data

`type` declares the shape of some data, such as an API response. Data that passes through it is checked and turned into a record: a map that knows its type, so the editor completes its fields and a typo like `$repo.stras` is caught.

```kscript
enum Visibility { public, private, internal }
type Owner(fields: "login: string, id: int, avatar_url?: string")

/// A GitHub repository.
type Repo {
  name: string
  /// How many people starred it.
  stars: int from stargazers_count
  owner: Owner
  owner_name: string from owner.login
  topics: string[] = []
  license: string? from license.name
  visibility: Visibility
  updated: date from updated_at
  label => $name + ' (' + $stars + ' stars)'
}
```

A short type fits on one line: `fields` takes `name: type` pairs like `fn` params, with `?` for an optional field and `= value` for a default. Bigger ones list their fields in a block, one `name: type` per line:

| Attribute | What it does |
| --- | --- |
| `type` | A built-in (`string`, `int`, `float`, `number`, `bool`, `date`, `list`, `map`, `fn`, `any`), an `enum`, another `type`, `T[]` for a list, `T?` to allow null. Defaults to `any`. |
| `from` | Where to read the value when it isn't the field's name: `stargazers_count`, `owner.login`, `items[0].name`. Nested data comes out flat. |
| `default` | The value when the data has none. The field is then optional. |
| `optional` | `yes` lets the value be missing or null. So does ending the name or the type with `?`. |
| `value` | Works the field out from the other fields (they are `$name`, `$stars` and so on) instead of reading it. |
| `description` | Shown on hover and in completion. |

On `type`, `sample: data/repo.json` takes the fields from a saved example of the data: objects become nested types, and keys only some list items have become optional. Fields in the block and `fields` replace the sampled fields they name. `extra` says what happens to keys the type doesn't list: `drop` (the default), `keep` or `error`. Types can refer to each other in any order and to themselves, and top-level types are exported to scripts that import the file.

Data meets a type in these places:

```kscript
repo = @fetch($url)->check()->json(as: $Repo) // KScript.Net
repos = @read_json('repos.json', as: $Repo) // KScript.IO; a list maps item by item
user = @json($text, as: $Owner)
user = $Owner->from($data) // a map, a list or JSON text
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

In a `.kscript` file, a type is a block with one field per line, and an enum lists its values in braces (see [the .kscript format](code-format.md)):

```
enum Visibility { public, private, internal }
type Owner(fields: "login: string, id: int")

/// A GitHub repository.
type Repo(extra: error) {
  name: string
  /// How many people starred it.
  stars: int from stargazers_count
  owner_name: string from owner.login
  topics: string[] = []
  license?: string from license.name
  label => $name + ' (' + $stars + ' stars)'
}
```

## Values and methods

Values are strings, numbers, bools, lists, maps, dates, functions and null, plus values packages add (files, HTTP responses). Every value has methods, called with `->` and chained:

```kscript
echo "{ $name->trim()->upper() }"
echo "{ $people->filter(fn($p) => $p.age ge 18)->map(fn($p) => $p.name)->join(', ') }"
echo "{ $people |> sort_by(fn($p) => $p.name) |> first() }"
echo "{ @now()->add_days(7)->format(pattern: 'd MMMM yyyy') }"
```

The editor completes the methods for each type and shows what they do. `@json(text)` and `$value->to_json()` convert to and from JSON.

## Errors

```kscript
try {
  data = @read_json($path)
} catch err when $err.type eq 'FileNotFound' {
  echo "No file at {$path}, using defaults."
} catch err {
  echo "Failed: {$err.message} (line {$err.line})"
} finally {
  echo "done"
}
throw Validation "Age must be positive, got {$age}"
```

Unknown elements, attributes and syntax errors are reported before a script starts, so a typo near the end doesn't leave a job half done.

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
| `KScript.Terminal` | `input(to, type, default, secret)`, `picker` (a file or folder path), `menu(options)`, `confirm`, `pause`, `clear`, `@color(text, colour: 'green')`, `@bold`, `@dim`, `@underline`, `@terminal_width()`, `@is_terminal()` (no when output is piped), `echo(format: markdown)` |
| `KScript.Math` | `@sqrt` `@pow` `@round(x, digits: 2)` `@clamp` `@log` `@sin` `@cos` `@atan2` `@hypot` `@gcd` `@factorial` `@random(low: 1, high: 6)` `@pick` `@shuffle` `@sum` `@average` `@median` `@stdev`, `$pi` `$e` |
| `KScript.IO` | `@read_text` `@read_lines` `@read_json` `@write_text` `@append_text` `@write_json` `@files(folder, pattern: '*.txt', recursive)` `@folders` `@file(path)` `@exists` `@join_path(base: $dir, ['a', 'b.txt'])` `@known_folder(downloads)`, `<write>` `<move>` `<copy>` `<delete>` `<mkdir>`. File values have `->name()` `->extension()` `->size()` `->modified()`… |
| `KScript.Net` | `@fetch(url, options: { method, headers, body, timeout })` returning a response (`->status()` `->ok()` `->json()` `->text()` `->check()`), `download(url, to)`, `@url_encode`, `@query_string` |
| `KScript.Markdown` | `@markdown(text)` and `@read_markdown(path)` (`html`, `title`, `meta` from front matter), `->markdown()` |
| `KScript.Web` | The elements of `kscript serve` pages (below). Pages have KScript.IO, KScript.Markdown and KScript.Web without importing them. |
| `KScript.AI` | `@llm(prompt, options)`, `llm(to, system, schema, tools)`, `@chat(system)` with `->ask()` `->history()` `->reset()`. Claude, with the key from `ANTHROPIC_API_KEY` (see [Language models](#language-models-kscriptai)). |
| `KScript.Style` | Stylesheets written in KScript (`.ks.css`, `.ks.scss`, `.ks.sass`); `@up`, `@down`, `@between`, `@rem`, `@fluid`, `@lighten`, `@darken`, `@mix`, `@alpha`, `@contrast`, `@css_var`, `@css_vars`; `stylesheet(src, to, vars)` (see [Stylesheets](stylesheets.md)). |
| `KScript.Http` | Routes, controllers and middleware for `kscript serve`: a `KScript.Routes { }` file holds `Get` `Post` `Put` `Patch` `Delete` `Any` `Route` `Resource` `Group` `Middleware`. Controllers use `@view`, `@response`, `@redirect`, `@abort` and `@route`. See [Routes, controllers and middleware](http.md). |
| `KScript.Storage` | The app's own folders, in the right place on each platform: `@storage(config)` (also `data`, `cache`, `state`, `logs`) with `->read` `->read_json` `->write` `->write_json` `->append` `->list` `->delete` `->folder` `->path`, plus `@app_name()` and `@app_folders()`. See [App folders](storage.md). |
| `KScript.Cache` | `$cache` (in memory) and `@cache(file)` with `->get` `->put` `->remember(key, fn, ttl: '10m')` `->has` `->forget` `->increment` `->ttl` `->keys` `->flush`. See [Caching](cache.md). |
| `KScript.UI` | Desktop windows: a `KScript.Window { }` block opens one. `Column` `Row` `Grid` `Text` `Button` `TextBox` `CheckBox` `Select` `Slider` `Box` `Spacer` `Timer`, `@close_window()`. See [Desktop apps](desktop.md). |
| `KScript.Test` | `test`, `describe`, `before-each`, `after-each`, `expect`, `assert`, `expect-error`, `capture`, `@run_script`, `@assert`, `@fail`. Test files import it on their own (see [Testing scripts](testing.md)). |

Using something from a package that isn't imported stops with `<input> is part of KScript.Terminal. Add <import package="KScript.Terminal" />`, and the editor offers to add the import. A statement written with the `KScript.` prefix, such as `KScript.input`, imports the package that has it without an `import`. A project can import packages for every script in `kscript.json` (below).

## Language models: KScript.AI

`KScript.AI` lets a script ask Claude. The API key comes from the `ANTHROPIC_API_KEY` environment variable (or a profile saved by `ant auth login`), never from the script. Scripts use `claude-opus-5-5` unless they ask for another model, or `KSCRIPT_AI_MODEL` names one.

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
