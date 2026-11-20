# Getting started

This page walks through KScript in ten minutes. Every example with a **Run** button runs on your computer when you're reading it with `kscript docs`, so change them and see what happens. Each one can be shown as XML or in the brace-style `.kscript` format: the two are the same language.

If you don't have `kscript` yet, see [Install](install.md).

## Your first script

A script is a file that ends in `.ks.xml` (or `.kscript`). Save this as `hello.ks.xml`:

```xml try
<kscript>
  <echo>Hello, World!</echo>
</kscript>
```

Then run it:

```sh
kscript hello.ks.xml
```

In the examples below the `<kscript>` around the script is left out, since every script has it. The docs add it when you run one.

## Variables

`<let>` declares a variable and `<set>` changes it. Values are numbers, text, yes/no, lists, maps and functions. In text, `$name` and `{ }` put a value in.

```xml try
<let name="name" value="'Joan'" />
<let name="age" value="82" />
<echo>$name is $age.</echo>

<set name="age" value="$age + 1" />
<echo>Next year { $name } will be { $age }.</echo>
```

Attribute values are expressions, so text needs quotes inside them: `value="'Joan'"`. Element text, like the text of `<echo>`, is already text.

## Lists and maps

Lists are written `[1, 2, 3]` and maps `{ name: 'Joan', age: 82 }`. Every value has methods, called with `->`.

```xml try
<let name="people" value="[{ name: 'Joan', age: 82 }, { name: 'Sam', age: 35 }, { name: 'Ada', age: 36 }]" />

<echo>{ $people->count() } people</echo>
<echo>Oldest: { $people->max_by(fn($p) => $p.age).name }</echo>
<echo>Names: { $people->map(fn($p) => $p.name)->join(', ') }</echo>
<echo>Over 35: { $people->filter(fn($p) => $p.age gt 35)->map(fn($p) => $p.name) }</echo>
```

`fn($p) => $p.age` is a lambda: a small function you pass to a method. `|>` pipes a value into the next call, which reads well for longer chains:

```xml try
<let name="words" value="'the quick brown fox jumps over the lazy dog'" />
<echo>{ $words->split(' ') |> unique() |> sort() |> join(', ') }</echo>
```

The [reference](/reference) lists every method of [strings](/reference/methods/string), [lists](/reference/methods/list), [maps](/reference/methods/map), [numbers](/reference/methods/number) and [dates](/reference/methods/date).

## Decisions and loops

```xml try
<for each="n" in="1..16">
  <if test="$n % 15 eq 0">
    <echo>FizzBuzz</echo>
  </if>
  <elif test="$n % 3 eq 0">
    <echo>Fizz</echo>
  </elif>
  <elif test="$n % 5 eq 0">
    <echo>Buzz</echo>
  </elif>
  <else>
    <echo>$n</echo>
  </else>
</for>
```

`1..16` counts from 1 up to 15 (the end is left out). Use the word forms `lt`, `le`, `gt`, `ge` and `eq` in attributes, because XML can't hold a raw `<`.

`<match>` picks one case:

```xml try
<let name="day" value="'Sat'" />
<match value="$day">
  <case is="'Sat', 'Sun'"><echo>Weekend</echo></case>
  <default><echo>Weekday</echo></default>
</match>
```

## Functions

```xml try
<fn name="greet" params="who, greeting = 'Hello'">
  <return value="$greeting + ', ' + $who + '!'" />
</fn>
<fn name="square" params="n: number" value="$n * $n" />

<echo>{ @greet('World') }</echo>
<echo>{ @greet('Kyle', greeting: 'Hi') }</echo>
<echo>{ 7 |> @square() }</echo>
```

Your own functions and the built-in commands are both called with `@`. Arguments can be named, like `greeting: 'Hi'`.

## Errors

```xml try
<fn name="check_age" params="age">
  <if test="$age lt 0">
    <throw type="Validation" message="Age must be positive, got {$age}" />
  </if>
  <return value="$age" />
</fn>

<try>
  <do>@check_age(-1)</do>
  <catch as="err">
    <echo>{ $err.type }: { $err.message }</echo>
  </catch>
</try>
```

## Packages

Everything above is KScript.Core, which every script has. The rest comes in packages you import: files and folders, the terminal, maths, HTTP, web pages, desktop windows and more. See the [package list](/reference#packages).

```xml try
<import package="KScript.Math" as="math" />
<import package="KScript.IO" />

<echo>√2 is { @math.sqrt(2)->round(digits: 4) }</echo>

<do>@write_text('notes.txt', 'Buy milk')</do>
<echo>notes.txt says: { @read_text('notes.txt') }</echo>
```

Examples in the docs run in their own empty folder, so writing files here is safe.

## Asking for input

KScript.Terminal reads from the keyboard. In the docs, click **Input** and type the answers, one per line, before you run.

```xml try
<import package="KScript.Terminal" />

<input to="name" default="'stranger'">What's your name? </input>
<echo>Hello, $name!</echo>
```

## The .kscript format

The same language can be written with braces and new lines instead of tags. Switch any example above to **.kscript** to see it, or write one directly:

```kscript try
people = [{ name: 'Joan', age: 82 }, { name: 'Sam', age: 35 }]
for p in $people {
  if $p.age gt 40 {
    echo "$p.name is over 40"
    continue
  }
  echo "$p.name is { $p.age }"
}
```

`kscript convert` turns a file into the other format. More in [the .kscript format](code-format.md).

## Where next

- [The language](language.md): the whole language, in detail.
- [Web pages](web.md) and [routes and APIs](http.md): websites and JSON APIs with `kscript serve`.
- [Desktop apps](desktop.md): windows with buttons and text boxes.
- [Testing](testing.md): tests for your scripts with `kscript test`.
- [The command line](/cli): every `kscript` command.
- [The playground](/playground): a blank page to try things in.
