# Getting started

This page walks through KScript in ten minutes. Every example with a **Run** button runs on your computer when you're reading it with `kscript docs`, so change them and see what happens. Examples are shown in the `.kscript` format, and each one can be switched to XML: the two are the same language.

If you don't have `kscript` yet, see [Install](install.md).

## Your first script

A script is a file that ends in `.kscript`. Save this as `hello.kscript`:

```kscript try
echo "Hello, World!"
```

Then run it:

```sh
kscript hello.kscript
```

A script runs from top to bottom, one statement per line. `//` starts a comment.

## Variables

`name = value` declares a variable, and assigning to it again changes it. Values are numbers, text, yes/no, lists, maps and functions. Read a variable with `$name`. In text, `$name` and `{ }` put a value in.

```kscript try
name = 'Joan'
age = 82
echo "$name is $age."

age = $age + 1
echo "Next year { $name } will be { $age }."
```

After `=`, a value is an expression, so text needs quotes. `"Double quotes"` fill in `$name` and `{ }` wherever they are written, and `'single quotes'` keep the text exactly as written:

```kscript try
name = 'Joan'
greeting = "Hello $name, you are { 80 + 2 }."
label = 'Write $name to use a variable.'
echo $greeting
echo $label
```

## Lists and maps

Lists are written `[1, 2, 3]` and maps `{ name: 'Joan', age: 82 }`. Every value has methods, called with `->`.

```kscript try
people = [{ name: 'Joan', age: 82 }, { name: 'Sam', age: 35 }, { name: 'Ada', age: 36 }]

echo "{ $people->count() } people"
echo "Oldest: { $people->max_by(fn($p) => $p.age).name }"
echo "Names: { $people->map(fn($p) => $p.name)->join(', ') }"
echo "Over 35: { $people->filter(fn($p) => $p.age gt 35)->map(fn($p) => $p.name) }"
```

`fn($p) => $p.age` is a lambda: a small function you pass to a method. `|>` pipes a value into the next call, which reads well for longer chains:

```kscript try
words = 'the quick brown fox jumps over the lazy dog'
echo "{ $words->split(' ') |> unique() |> sort() |> join(', ') }"
```

The [reference](/reference) lists every method of [strings](/reference/methods/string), [lists](/reference/methods/list), [maps](/reference/methods/map), [numbers](/reference/methods/number) and [dates](/reference/methods/date).

## Decisions and loops

```kscript try
for n in 1..16 {
  if $n % 15 eq 0 {
    echo "FizzBuzz"
  } elif $n % 3 eq 0 {
    echo "Fizz"
  } elif $n % 5 eq 0 {
    echo "Buzz"
  } else {
    echo "$n"
  }
}
```

`1..16` counts from 1 up to 15 (the end is left out). Comparisons can be written as words (`lt`, `le`, `gt`, `ge`, `eq`) or as `<`, `>=` and so on.

`match` picks one case:

```kscript try
day = 'Sat'
match $day {
  case 'Sat', 'Sun' {
    echo "Weekend"
  }
  default {
    echo "Weekday"
  }
}
```

## Functions

```kscript try
fn greet(who: string, greeting = 'Hello') {
  return $greeting + ', ' + $who + '!'
}
fn square(n: number) => $n * $n

echo "{ @greet('World') }"
echo "{ @greet('Kyle', greeting: 'Hi') }"
echo "{ 7 |> @square() }"
```

Your own functions and the built-in commands are both called with `@`. Arguments can be named, like `greeting: 'Hi'`.

## Errors

```kscript try
fn check_age(age: int) {
  if $age lt 0 {
    throw Validation "Age must be positive, got {$age}"
  }
  return $age
}

try {
  @check_age(-1)
} catch err {
  echo "{ $err.type }: { $err.message }"
}
```

## Packages

Everything above is KScript.Core, which every script has. The rest comes in packages you import: files and folders, the terminal, maths, HTTP, web pages, desktop windows and more. See the [package list](/reference#packages).

```kscript try
import KScript.Math as math
import KScript.IO

echo "√2 is { @math.sqrt(2)->round(digits: 4) }"

@write_text('notes.txt', 'Buy milk')
echo "notes.txt says: { @read_text('notes.txt') }"
```

Examples in the docs run in their own empty folder, so writing files here is safe.

## Asking for input

KScript.Terminal reads from the keyboard. In the docs, click **Input** and type the answers, one per line, before you run.

```kscript try
import KScript.Terminal

input(into: name, default: 'stranger') "What's your name? "
echo "Hello, $name!"
```

## The .kscript format and XML

Everything above can also be written as XML, which looks like HTML: `name = 'Joan'` is `<let name="name" value="'Joan'" />`, and `echo "Hi"` is `<echo>Hi</echo>`. Switch any example to **XML** to see it, or use the switch at the top of the page. XML scripts end in `.ks.xml`, and the two kinds can import each other.

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
