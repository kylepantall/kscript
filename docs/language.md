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

## Values and methods

Values are strings, numbers, bools, lists, maps, dates, functions and null, plus values packages add (files, HTTP responses). Every value has methods, called with `->` and chained:

```xml
<echo>{ $name->trim()->upper() }</echo>
<echo>{ $people->filter(fn($p) => $p.age ge 18)->map(fn($p) => $p.name)->join(', ') }</echo>
<echo>{ $people |> sort_by(fn($p) => $p.name) |> first() }</echo>
<echo>{ @now()->add_days(7)->format('d MMMM yyyy') }</echo>
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
| `KScript.Terminal` | `<input to type default secret>`, `<picker>` (a file or folder path), `<menu options>`, `<confirm>`, `<pause>`, `<clear>`, `@color(text, 'green')`, `@bold`, `@dim`, `@underline`, `@terminal_width()`, `@is_terminal()` (no when output is piped), `<echo format="markdown">` |
| `KScript.Math` | `@sqrt` `@pow` `@round(x, digits)` `@clamp` `@log` `@sin` `@cos` `@atan2` `@hypot` `@gcd` `@factorial` `@random(low, high)` `@pick` `@shuffle` `@sum` `@average` `@median` `@stdev`, `$pi` `$e` |
| `KScript.IO` | `@read_text` `@read_lines` `@read_json` `@write_text` `@append_text` `@write_json` `@files(folder, '*.txt', recursive)` `@folders` `@file(path)` `@exists` `@join_path` `@known_folder('downloads')`, `<write>` `<move>` `<copy>` `<delete>` `<mkdir>`. File values have `->name()` `->extension()` `->size()` `->modified()`… |
| `KScript.Net` | `@fetch(url, { method, headers, body, timeout })` returning a response (`->status()` `->ok()` `->json()` `->text()` `->check()`), `<download url to>`, `@url_encode`, `@query_string` |
| `KScript.Markdown` | `@markdown(text)` and `@read_markdown(path)` (`html`, `title`, `meta` from front matter), `->markdown()` |
| `KScript.Web` | The elements of `kscript serve` pages (below). Pages have KScript.IO, KScript.Markdown and KScript.Web without importing them. |
| `KScript.UI` | Desktop windows: a script whose root is `<KScript.Window>` opens one. `<Column>` `<Row>` `<Grid>` `<Text>` `<Button>` `<TextBox>` `<CheckBox>` `<Select>` `<Slider>` `<Box>` `<Spacer>` `<Timer>`, `@close_window()`. See [Desktop apps](desktop.md). |
| `KScript.Test` | `<test>`, `<describe>`, `<before-each>`, `<after-each>`, `<expect>`, `<assert>`, `<expect-error>`, `<capture>`, `@run_script`, `@assert`, `@fail`. Test files import it on their own (see [Testing scripts](testing.md)). |

Using something from a package that isn't imported stops with `<input> is part of KScript.Terminal. Add <import package="KScript.Terminal" />`, and the editor offers to add the import. An element written with the `KScript.` prefix, such as `<KScript.input>`, imports the package that has it without an `<import>`. A project can import packages for every script in `kscript.json` (below).

## Writing portable scripts

- Build paths with `@join_path($dir, 'file.txt')` (KScript.IO) rather than hard-coding `\` or `/`.
- `$os.name` is `windows`, `linux` or `macos`, and `$os.home`, `$os.temp` and `$os.separator` are there too; `$env.HOME` reads an environment variable.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.
