# The KScript language

This is the reference for KScript 2 scripts: the core language every script and page has, the packages you import for the rest, and a few tips for scripts that run on every OS.

## Variables and output

```xml
<let name="count" value="0" />                      <!-- declare -->
<let name="people">[{ name: 'Joan', age: 82 }]</let> <!-- longer values go in the text -->
<set name="count" value="$count + 1" />             <!-- change; fails if never declared -->
<set name="count" op="+=" value="1" />
<do>$total = $price * $qty; $count += 1</do>
<let name="limit" value="10" const="yes" />
<let name="n" value="$args[0]" type="int" />        <!-- convert, or stop with a clear error -->

<echo>Hello, {$name}! You have { $count } new { $count eq 1 ? 'message' : 'messages' }.</echo>
<echo>$person.name is $person.age</echo>           <!-- bare $name works in text too -->
<echo>$people</echo>                               <!-- a map or list of maps prints as indented JSON -->
```

A map, or a list of maps, on its own in `<echo>` prints as indented JSON, coloured in a terminal (unless `NO_COLOR` is set). Inside text it stays on one line. `<echo format="json">` reformats JSON text, and `->to_json()` gives compact JSON.

Variables belong to the block they are declared in. `{{` and `}}` are literal braces.

## Expressions

`+ - * / % **`, `== !=` (loose: `'3' == 3`), `=== !==` (strict), `lt le gt ge` (or `> >=`), `eq ne`, `and or not`, `??`, `?.`, `a ? b : c`, `in`, list `[1, 2]` and map `{ name: 'Joan' }` literals, `$p.name`, `$list[0]` (negative indexes count from the end), ranges `0..10` and `0..100 step 5` (end excluded), `match $x { 1 => 'one', _ => 'other' }`, `->method()`, `|>` and `fn($x) => …`. Use the word forms in attributes, since XML can't hold a raw `<` or `&&` there.

## Control flow

```xml
<if test="$n lt 10"> … </if>
<elif test="$n lt 100"> … </elif>
<else> … </else>

<for each="p" in="$people"> … </for>
<for each="i" index="n" in="0..10 step 2"> … </for>
<for each="key, value" in="$map"> … </for>
<while test="$queue->count() gt 0"> … <break /> … <continue /> … </while>

<match value="$choice">
  <case is="1"> … </case>
  <case is="2, 3"> … </case>
  <case when="$it gt 9"> … </case>
  <default> … </default>
</match>
```

### Early returns instead of else

Inside a function or a loop, end a branch early instead of writing `<else>`: `<return>` in a function,
`<continue />` or `<break />` in a loop. The main path then stays at the left edge and each special case is dealt with
first.

```xml
<fn name="grade" params="score">
  <if test="$score ge 90">
    <return value="'A'" />
  </if>
  <if test="$score ge 75">
    <return value="'B'" />
  </if>
  <return value="'C'" />
</fn>
```

`kscript check` and the editor warn about `<else>` and `<elif>` with KS6001 there, and at the top of a script when
every branch before already ends with `<return>`, `<break>`, `<continue>`, `<throw>` or `<exit>`. The quick fix
removes the `<else>` (or turns `<elif>` into a separate `<if>`) and, when the chain is the last statement of the
function or loop, adds the `<return />` or `<continue />` for you. Pages, `<live>` blocks and `<for>`…`<else>` (for an
empty list) are not flagged, since there is nothing to return from. KS6001 comes from the default
[standard](standards.md), `kscript:recommended`. To turn it off for a project, set
`"lint": { "rules": { "KS6001": "off" } }` in `kscript.json` (or in a standard your projects share).

## Functions

```xml
<fn name="greet" params="who, greeting = 'Hello'">
  <return value="$greeting + ', ' + $who + '!'" />
</fn>
<fn name="square" params="n: number" value="$n * $n" />

<echo>{ @greet('World') } { 4 |> @square() }</echo>
<call fn="greet" args="'Kyle'" />
<spawn fn="background_job" />
```

Functions can be called before they are declared, recurse, and be passed around: `@square` is the function itself. Lambdas are values too: `<let name="by_age" value="fn($a, $b) => $a.age - $b.age" />`.

### Named arguments

Any call can name its arguments, in any order: built-in commands, methods, your own `<fn>` functions and lambdas.

```xml
<echo>{ @known_folder(name: 'downloads') }</echo>
<echo>{ @greet(greeting: 'Hi', who: 'Kyle') }</echo>
<echo>{ @range(1, step: 2, end: 10) }</echo>
<echo>{ 'abc'->pad_left(char: '*', width: 6) }</echo>
<echo>{ @join_path(base: $folder, ['archive', $name]) }</echo>
```

Positional arguments fill parameters in order, and each `name: value` fills the parameter of that name, so you can skip optional ones in between. A positional argument written after named ones fills the first parameter still empty, which is how `@join_path(base: $folder, [...])` reads. Naming a parameter that doesn't exist, or giving one twice, is an error with a "did you mean" hint. The editor completes parameter names after `(` or `,`.

### Flags

A yes/no parameter can be written as a bare word: `recursive` sets it to yes and `not recursive` to no, so calls read like a sentence.

```xml
<let name="all" value="@files($folder, pattern: '*.txt', recursive)" />
<echo>{ $data->to_json(not indented) }</echo>
<fn name="save" params="path, overwrite: bool = false">…</fn>
<do>@save('out.txt', overwrite)</do>
```

### Enums and consts

`<enum>` declares a set of named values, and `<const>` a value that can't change.

```xml
<enum name="Size" values="small, medium, large" />
<const name="max_items" value="10" />
<fn name="pack" params="size: Size, gift: bool = false" value="'Packing ' + $size" />

<echo>{ @pack(large) } { @pack(size: small, gift) } { $Size.medium }</echo>
```

A parameter typed with an enum only takes its values (`'large'` or bare `large`), and the editor suggests them. Built-in commands have enum parameters too: `@known_folder(downloads)` takes `home`, `desktop`, `documents`, `downloads`, `music`, `pictures`, `videos`, `appdata` or `temp`. `$Size` itself is a map from each name to itself, so `'large' in $Size` checks a value. Top-level `<const>` and `<enum>` declarations are exported to scripts that import the file.

## Typed data

`<type>` declares the shape of some data, such as an API response. Data that passes through it is checked and turned into a record: a map that knows its type, so the editor completes its fields and a typo like `$repo.stras` is caught.

```xml
<enum name="Visibility" values="public, private, internal" />
<type name="Owner" fields="login: string, id: int, avatar_url?: string" />

<type name="Repo" description="A GitHub repository.">
  <field name="name" type="string" />
  <field name="stars" type="int" from="stargazers_count" description="How many people starred it." />
  <field name="owner" type="Owner" />
  <field name="owner_name" type="string" from="owner.login" />
  <field name="topics" type="string[]" default="[]" />
  <field name="license" type="string?" from="license.name" />
  <field name="visibility" type="Visibility" />
  <field name="updated" type="date" from="updated_at" />
  <field name="label" value="$name + ' (' + $stars + ' stars)'" />
</type>
```

A short type fits on one line: `fields` takes `name: type` pairs like `fn` params, with `?` for an optional field and `= value` for a default. Bigger ones use `<field>` elements:

| Attribute | What it does |
| --- | --- |
| `type` | A built-in (`string`, `int`, `float`, `number`, `bool`, `date`, `list`, `map`, `fn`, `any`), an `<enum>`, another `<type>`, `T[]` for a list, `T?` to allow null. Defaults to `any`. |
| `from` | Where to read the value when it isn't the field's name: `stargazers_count`, `owner.login`, `items[0].name`. Nested data comes out flat. |
| `default` | The value when the data has none. The field is then optional. |
| `optional` | `yes` lets the value be missing or null. So does ending the name or the type with `?`. |
| `value` | Works the field out from the other fields (they are `$name`, `$stars` and so on) instead of reading it. |
| `description` | Shown on hover and in completion. |

On `<type>`, `sample="data/repo.json"` takes the fields from a saved example of the data: objects become nested types, and keys only some list items have become optional. `<field>`s and `fields` replace the sampled fields they name. `extra` says what happens to keys the type doesn't list: `drop` (the default), `keep` or `error`. Types can refer to each other in any order and to themselves, and top-level types are exported to scripts that import the file.

Data meets a type in these places:

```xml
<let name="repo" value="@fetch($url)->check()->json(as: Repo)" />   <!-- KScript.Net -->
<let name="repos" value="@read_json('repos.json', as: Repo)" />     <!-- KScript.IO; a list maps item by item -->
<let name="user" value="@json($text, as: Owner)" />
<let name="user" value="$Owner->from($data)" />                    <!-- a map, a list or JSON text -->
<let name="repo" type="Repo" value="$data" />                       <!-- also on <const> and <set> -->
<fn name="show" params="repo: Repo, tags: string[]">…</fn>
```

`as:` takes the type bare (`as: Repo`), as a value (`as: $Repo`) or as text (`as: 'Repo[]'`). `@type($repo)` is `'Repo'`. A record still works everywhere a map does, but reading or setting a field the type doesn't have is an error, and setting a field converts the value to the field's type.

When the data doesn't fit, mapping fails with one `Shape` error that lists every problem with its JSON path:

```text
The response from https://api.github.com/... doesn't fit Repo (2 problems):
  $.stargazers_count: needs an int, but got the string 'lots'.
  $.visibility: must be a Visibility (public, private, internal), but got the string 'secret'.
```

In a `<catch>`, `$err.problems` is a list of `{ path, field, message }` maps. `$Repo->check($data)` returns that list without throwing (empty when the data fits), `$Repo->fields()` lists the fields, and `$Repo->schema()` describes the type as JSON Schema.

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

```xml
<echo>{ $name->trim()->upper() }</echo>
<echo>{ $people->filter(fn($p) => $p.age ge 18)->map(fn($p) => $p.name)->join(', ') }</echo>
<echo>{ $people |> sort_by(fn($p) => $p.name) |> first() }</echo>
<echo>{ @now()->add_days(7)->format(pattern: 'd MMMM yyyy') }</echo>
```

The editor completes the methods for each type and shows what they do. `@json(text)` and `$value->to_json()` convert to and from JSON.

## Errors

```xml
<try>
  <let name="data" value="@read_json($path)" />
  <catch as="err" when="$err.type eq 'FileNotFound'">
    <echo>No file at {$path}, using defaults.</echo>
  </catch>
  <catch as="err"><echo>Failed: {$err.message} (line {$err.line})</echo></catch>
  <finally><echo>done</echo></finally>
</try>
<throw type="Validation" message="Age must be positive, got {$age}" />
```

Unknown elements, attributes and syntax errors are reported before a script starts, so a typo near the end doesn't leave a job half done.

## Packages

KScript.Core (everything above) is always there. Everything else comes from a package, imported at the top of the script, so a script only loads what it uses: a terminal script never loads the web server.

```xml
<kscript>
  <import package="KScript.Terminal" />
  <import package="KScript.Math" as="math" />      <!-- commands stay under the prefix: @math.sqrt(2) -->
  <import src="lib/strings.ks.xml" as="str" />      <!-- another script's top-level <fn> and <let const> -->
  <import src="lib/strings.ks.xml" names="slugify" />
  …
</kscript>
```

| Package | What it adds |
| --- | --- |
| `KScript.Terminal` | `<input to type default secret>`, `<picker>` (a file or folder path), `<menu options>`, `<confirm>`, `<pause>`, `<clear>`, `@color(text, colour: 'green')`, `@bold`, `@dim`, `@underline`, `@terminal_width()`, `@is_terminal()` (no when output is piped), `<echo format="markdown">` |
| `KScript.Math` | `@sqrt` `@pow` `@round(x, digits: 2)` `@clamp` `@log` `@sin` `@cos` `@atan2` `@hypot` `@gcd` `@factorial` `@random(low: 1, high: 6)` `@pick` `@shuffle` `@sum` `@average` `@median` `@stdev`, `$pi` `$e` |
| `KScript.IO` | `@read_text` `@read_lines` `@read_json` `@write_text` `@append_text` `@write_json` `@files(folder, pattern: '*.txt', recursive)` `@folders` `@file(path)` `@exists` `@join_path(base: $dir, ['a', 'b.txt'])` `@known_folder(downloads)`, `<write>` `<move>` `<copy>` `<delete>` `<mkdir>`. File values have `->name()` `->extension()` `->size()` `->modified()`… |
| `KScript.Net` | `@fetch(url, options: { method, headers, body, timeout })` returning a response (`->status()` `->ok()` `->json()` `->text()` `->check()`), `<download url to>`, `@url_encode`, `@query_string` |
| `KScript.Markdown` | `@markdown(text)` and `@read_markdown(path)` (`html`, `title`, `meta` from front matter), `->markdown()` |
| `KScript.Web` | The elements of `kscript serve` pages (below). Pages have KScript.IO, KScript.Markdown and KScript.Web without importing them. |
| `KScript.Http` | Routes, controllers and middleware for `kscript serve`: a file whose root is `<KScript.Routes>` holds `<Get>` `<Post>` `<Put>` `<Patch>` `<Delete>` `<Any>` `<Route>` `<Resource>` `<Group>` `<Middleware>`. Controllers use `@view`, `@response`, `@redirect`, `@abort` and `@route`. See [Routes, controllers and middleware](http.md). |
| `KScript.UI` | Desktop windows: a script whose root is `<KScript.Window>` opens one. `<Column>` `<Row>` `<Grid>` `<Text>` `<Button>` `<TextBox>` `<CheckBox>` `<Select>` `<Slider>` `<Box>` `<Spacer>` `<Timer>`, `@close_window()`. See [Desktop apps](desktop.md). |
| `KScript.Test` | `<test>`, `<describe>`, `<before-each>`, `<after-each>`, `<expect>`, `<assert>`, `<expect-error>`, `<capture>`, `@run_script`, `@assert`, `@fail`. Test files import it on their own (see [Testing scripts](testing.md)). |

Using something from a package that isn't imported stops with `<input> is part of KScript.Terminal. Add <import package="KScript.Terminal" />`, and the editor offers to add the import. An element written with the `KScript.` prefix, such as `<KScript.input>`, imports the package that has it without an `<import>`. A project can import packages for every script in `kscript.json` (below).

## Writing portable scripts

- Build paths with `@join_path(base: $dir, ['reports', 'file.txt'])` (KScript.IO) rather than hard-coding `\` or `/`.
- Find the user's folders with `@known_folder(downloads)` (KScript.IO): `home`, `desktop`, `documents`, `downloads`, `music`, `pictures`, `videos`, `appdata` or `temp`. It follows each system's own settings, such as Windows' Downloads location and Linux's `user-dirs.dirs`.
- `$os.name` is `windows`, `linux` or `macos`, and `$os.home`, `$os.temp` and `$os.separator` are there too; `$env.HOME` reads an environment variable.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.
