# Types and validation

KScript has built-in types for text that means something: `email`, `password`, `handle`, `uuid` and `regex`. A value of one of these types is checked and tidied when it is made, and it keeps working as text. You can also make your own types from them. A refinement type adds rules to one type, such as `type Age with [int] { min: 0; max: 150 }`. A joined type puts other types together, such as `type Admin with [User, Auditable]`. A value that passes a type's rules can be used anywhere that type is expected.

```kscript
type Username with [handle] {
  max: 16
  not: [@admin, @root]
  validate: not $value->name()->starts_with('_'), 'A username cannot start with _'
}

fn greet(user: Username) => 'Hi ' + $user->name()

echo @greet(@Joan)
try {
  echo @greet(@admin)
} catch err {
  echo $err.message
}
```

## Text with a meaning of its own

| Type | What it holds | Tidied | Methods |
| --- | --- | --- | --- |
| `email` | An address such as `joan@example.com`. | Spaces are trimmed and the domain is lowercased. | `->local()`, `->domain()` |
| `handle` | `@` followed by letters, digits, `_`, `.` or `-`, up to 30 of them. | Lowercased. | `->name()` (without the `@`) |
| `uuid` | A UUID, versions 1 to 8, plus the nil and max UUIDs. | Lowercased to `8-4-4-4-12`. `urn:uuid:`, braces and 32 hex digits are read too. | `->version()` |
| `regex` | A regular expression with flags `i`, `m`, `s` and `x`. | | `->test()`, `->match()`, `->match_all()`, `->replace()`, `->split()`, `->pattern()`, `->flags()` |
| `password` | A secret. It never prints. | | `->hash()`, `->verify()`, `->needs_rehash()`, `->strength()`, `->strong()`, `->reveal()` |

An email, handle, uuid or regex is still text, and it compares equal to the same text. An email compares with its domain lowercased. It keeps the string methods that make sense for it, and the editor only suggests those:

| Type | String methods it keeps |
| --- | --- |
| `email` | `contains`, `starts_with`, `ends_with`, `index_of`, `length`, `lower`, `upper`, `slice`, `split`, `replace`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `handle` | `contains`, `starts_with`, `ends_with`, `length`, `upper`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `uuid` | `contains`, `starts_with`, `ends_with`, `upper`, `slice`, `split`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `regex` | `contains`, `starts_with`, `ends_with`, `length`, `escape`, `markdown_escape`, `json` |

The rest, such as `->to_date()` on a uuid, are reported in the editor and fail when the script runs. To use one on the text anyway, call `->to_string()` first: `$id->to_string()->to_date()`.

```kscript
email: email = ' Joan.Smith@Example.COM '
echo "$email { $email->local() } at { $email->domain() }"
echo "{ $email == 'Joan.Smith@EXAMPLE.com' }"

who: handle = @Joan_S
echo "$who { $who->name() }"

id = @uuid()
key = @uuid(v: 8, name: 'joan@example.com')
echo "{ $id->version() } { $key->version() } { $key == @uuid(v: 8, name: 'joan@example.com') }"
```

`@uuid()` makes a random version 4 UUID. `@uuid(v: 8)` makes a version 8 UUID that is time-ordered. With `name:`, it is the same UUID every time for that name, and `namespace:` keeps names from different places apart.

A value of the wrong form fails where it is made, with a `Shape` error that says why:

```text
$email doesn't fit email: 'joan@' isn't an email: there's nothing after the @.
```

The editor reports the same problem at the value as you type (KS5012).

## Values without quotes

Some values need no quotes, the same way a regex needs none in JavaScript.

- **Emails, UUIDs and regexes** are literals anywhere: `joan@example.com`, `3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f`, `/^[a-z]+$/i`. The editor checks them as you type.
- **Handles** are written as `@joan` only where a handle is expected: a `handle` variable or parameter, a `not:` or `one_of:` list on a handle type, or next to a handle in `==`. Everywhere else, `@joan` still names the function `joan`.
- **Words** need no quotes where the type is read from text. That covers an enum (`color: Color = red`), a type with a `pattern` (`size: Size = 1920x1080`), and a refinement of text (`slug: Slug = my-post`).

Quoted text always works too: `who: handle = '@joan'`.

```kscript
enum Color { red, green, blue }

fn paint(color: Color, by: handle) => 'Painted ' + $color + ' by ' + $by

me: handle = @joan
echo @paint(green, @joan)
echo "{ $me == @joan }"
```

## Passwords

A password prints as `********`. That applies to `echo`, logs, JSON and error messages. It can't be compared with `==` either, because that would leak timing. Hash it to store it, and verify the hash at sign-in:

```kscript
pw: password = 'correct horse battery staple'
echo "Password: $pw"

stored = $pw->hash()
echo "{ $stored->starts_with('$argon2id$') } { $pw->verify($stored) }"

weak: password = 'password1'
echo "{ $weak->strength(min: 12, symbol: true).problems }"
echo "{ $weak->strong() }"
```

| Method | What it does |
| --- | --- |
| `->hash(algorithm: argon2id)` | A salted hash in PHC format. The default is `argon2id`; `bcrypt` (cost 12) works up to 72 bytes. |
| `->verify(hash)` | True if the password matches the hash, in constant time. Both formats are read. |
| `->needs_rehash(hash)` | True when the hash was made with other settings, so it can be upgraded at the next sign-in. |
| `->strength(...)` | `{ score, label, ok, problems }`. Score 0 to 4 estimates how hard it is to guess. |
| `->strong(...)` | True when `strength` finds no problems. |
| `->reveal()` | The text itself, for the rare place that needs it. |

`strength` and `strong` take named rules: `min` (8 by default) and `max` (length), `upper`, `lower`, `digit` and `symbol` (each one needed), `common: false` (allow the most common passwords), `not: [...]` (words it mustn't contain, such as the user's name) and `score` (the lowest score that passes, 3 by default). The same rules can be given to the type, as in `password(min: 12)`, and then a password that breaks them can't be made.

## Arguments where a type is used

A type can take arguments where it is used. They are the same rules a refinement type has, and they last only for that use:

```kscript
work: email(domains: ['example.com']) = joan@example.com
id: uuid(v: 4) = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f
pin: password(min: 4, max: 8, common: false) = '1234'
title: string(min: 1, max: 80) = 'Hello'
echo "$work $id $pin $title"
```

| Type | Arguments |
| --- | --- |
| `uuid` | `v` (a version, or a list of versions) |
| `email` | `domains` (the domains allowed) |
| `password` | `min`, `max`, `upper`, `lower`, `digit`, `symbol`, `common`, `not`, `score`, `algorithm` |
| Any type | `min`, `max`, `pattern`, `one_of`, `not` |

### Project defaults in kscript.json

`"types"` in [kscript.json](projects.md#project-settings-kscriptjson) sets arguments for every use of a built-in type in the project. Arguments written where the type is used win:

```jsonc
{
  "types": {
    "uuid": { "v": 4 },
    "password": { "min": 12, "symbol": true, "algorithm": "argon2id" },
    "email": { "domains": ["example.com"] }
  }
}
```

## Refinement types

A type built on one built-in type, or on another refinement type, adds rules to it. It has no fields: its values are the base type's values that pass the rules.

```kscript
type Age with [int] { min: 0; max: 150 }

type Slug with [string] {
  pattern: /^[a-z0-9]+(-[a-z0-9]+)*$/
  normalize: $value->trim()->lower()
}

type Username with [handle] {
  max: 16
  not: [@admin, @root]
}

age: Age = 42
slug: Slug = ' Hello-World '
echo "$age $slug"
try {
  too_old: Age = 200
} catch err {
  echo $err.message
}
```

| Rule | What it checks |
| --- | --- |
| `min`, `max` | A number's value, or the length of text or a list. |
| `pattern` | Text must match. It is a regex literal such as `/^[a-z]+$/i`, or quoted text. |
| `one_of` | The value must be one of these. |
| `not` | The value must not be one of these. |
| `validate: test, 'message'` | Any expression, with the value as `$value`. Several can be given; each one that fails adds its message. |
| `normalize: expression` | Changes the value before the rules check it, with the value as `$value`. |

A value is converted to the base type first, then normalized, then checked against the rules. One that fails reports every rule it broke. Rules on a field work the same way: `title: string(min: 1)`.

## Joined types

A type built on other types with fields has all of their fields, rules and methods, merged left to right. Fields it declares itself win. A value of the joined type fits wherever one of its bases is expected:

```kscript
type User {
  name: string
  email: email
}
type Auditable {
  created: date
  validate: $created <= @now(), 'created is in the future'
}
type Admin with [User, Auditable] {
  level: int = 1
}

fn welcome(user: User) => 'Welcome ' + $user.name

admin = $Admin->from({ name: 'Joan', email: 'joan@example.com', created: '2026-01-05' })
echo "{ @welcome($admin) } { $User->is($admin) } { $admin.level }"
```

When two bases have a field with the same name but different types, the joined type must declare that field itself. `$User->is($admin)` says whether a value is a User, and `$User->is($Admin)` whether a type is built on User. A `validate:` rule in a type with fields sees the fields as `$name`, `$email` and so on.

## Types read from text

A type with fields and a `pattern` can be made from text. The pattern's named groups fill the fields of the same name, and each one is converted to its field's type. `fn` inside a type adds a method, which sees the fields:

```kscript
import KScript.Math

enum Unit { cm, in, mm }

/// A size such as H4cmW12in: height 4 cm, width 12 inches.
type Dimensions {
  pattern: /^H(?<height>[\d.]+)(?<height_unit>cm|in|mm)W(?<width>[\d.]+)(?<width_unit>cm|in|mm)$/i
  height: number
  height_unit: Unit
  width: number
  width_unit: Unit
  fn width_in(unit: Unit) => @convert_length($width, $width_unit, $unit)
}

size: Dimensions = H4cmW12in
echo "{ $size.width } { $size.width_unit } is { $size->width_in(cm) } cm"
```

When the text needs more than a pattern, `parse:` gives an expression that returns a map of the fields. It sees the text as `$value` and, with a pattern, the match as `$match`.

## Regular expressions

A regex literal is written between slashes, with its flags after them. Named groups come back by name:

```kscript
date = /(?<year>\d{4})-(?<month>\d\d)/
found = $date->match('Due 2026-11')
echo "{ $found.groups.year } { $date->test('no date') }"
echo "{ $date->replace('2026-11', '${month}/${year}') }"
echo "{ /\s*,\s*/->split('tea, coffee ,milk') }"
echo "{ /\d+/->match_all('3 cats, 12 dogs')->map(fn($m) => $m.text) }"
```

`->match()` returns `{ text, index, groups, captures }`, or null when nothing matches. A regex that takes longer than a second to match stops with an error, so a bad pattern can't hang a server. In XML, write `<` inside an attribute or text as `&lt;`.

## Built-in functions that take these types

String methods that work with patterns take a regex literal, with its flags, or text holding a pattern. Either way, a match that takes longer than a second stops with an error:

```kscript
title = 'Hello  World, hello'
echo "{ $title->matches(/^hello/i) } { $title->find_all(/hello/i) }"
echo "{ $title->replace(/\s+/, ' ') } { 'tea, coffee ,milk'->split(/\s*,\s*/) }"
echo "{ $title->replace_pattern(/(?<w>world)/i, '[${w}]') }"
```

`->replace()` and `->split()` still take plain text too, which is matched as it is written. The `list` method of a storage folder takes a wildcard such as `'*.json'` or a regex matched against each name, such as `/^notes\/.*\.md$/`.

In KScript.Terminal, `input` reads the new types: `type="email"`, `handle` or `uuid` asks again with the reason when the answer doesn't fit, and `type="password"` hides what is typed and keeps the answer as a password.

## In XML

In XML, the same types are attributes and elements of `<type>`:

```xml
<type name="Username" with="handle" max="16" not="@admin, @root">
  <validate test="not $value->name()->starts_with('_')" message="A username cannot start with _" />
</type>
<type name="Admin" with="User, Auditable">
  <field name="level" type="int" default="1" />
</type>
<let name="who" type="Username" value="@joan" />
```

`kscript convert` turns either form into the other.

## Ideas for later

These would work the same way and could be added next: `url` (with `->host()` and `->path()`), `phone` (E.164, with a region), `ip` (v4 and v6, with `->in(cidr)`), `slug`, `date_range` and `money`. Tell us which you would use first.
