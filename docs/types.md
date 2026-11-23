# Types and validation

KScript has built-in types for text that means something, called native types: `email`, `url`, `ip`, `country`, `duration`, `color` and [two dozen more](#more-native-types). A value of one of these types is checked and tidied when it is made, and it keeps working as text. You can also make your own types from them. A refinement type adds rules to one type, such as `type Age with [int] { min: 0; max: 150 }`. A joined type puts other types together, such as `type Admin with [User, Auditable]`. A value that passes a type's rules can be used anywhere that type is expected.

```kscript
type Username with [handle] {
  max: 16
  not: [@admin, @root]
  validate: not $value.name->starts_with('_'), 'A username cannot start with _'
}

fn greet(user: Username) => 'Hi ' + $user.name

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
| `email` | An address such as `joan@example.com`. | Spaces are trimmed and the domain is lowercased. | `.local`, `.domain` |
| `handle` | `@` followed by letters, digits, `_`, `.` or `-`, up to 30 of them. | Lowercased. | `.name` (without the `@`) |
| `uuid` | A UUID, versions 1 to 8, plus the nil and max UUIDs. | Lowercased to `8-4-4-4-12`. `urn:uuid:`, braces and 32 hex digits are read too. | `.version` |
| `regex` | A regular expression with flags `i`, `m`, `s` and `x`. | | `->test()`, `->match()`, `->match_all()`, `->replace()`, `->split()`, `.pattern`, `.flags` |
| `password` | A secret. It never prints. | | `->hash()`, `->verify()`, `->needs_rehash()`, `->strength()`, `->is_strong()`, `->reveal()` |

A native value is still text, and it compares equal to the same text once tidied. An email compares with its domain lowercased. It keeps the string methods that make sense for it, and the editor only suggests those:

| Type | String methods it keeps |
| --- | --- |
| `email` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `lower`, `upper`, `slice`, `split`, `replace`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `handle` | `contains`, `starts_with`, `ends_with`, `count`, `upper`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `uuid` | `contains`, `starts_with`, `ends_with`, `upper`, `slice`, `split`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `regex` | `contains`, `starts_with`, `ends_with`, `count`, `escape`, `markdown_escape`, `json` |
| `url` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `replace`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `domain` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `upper`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `ip` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `cidr` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `mac` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `upper`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `slug` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `upper`, `replace`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `slash_path` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `lower`, `upper`, `replace`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `semver` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `mime` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `hex` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `upper`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `base64` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `jwt` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `json` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `phone` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `country` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `currency` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `locale` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `timezone` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `split`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `duration` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `date_format` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `cron` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `color` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `upper`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |
| `iban` | `contains`, `starts_with`, `ends_with`, `index_of`, `count`, `slice`, `matches`, `truncate`, `pad_left`, `pad_right`, `escape`, `url_encode`, `markdown_escape`, `json` |

A `port`, `card` or `password` keeps none. The rest, such as `->to_date()` on a uuid, are reported in the editor and fail when the script runs. To use one on the text anyway, call `->to_string()` first: `$id->to_string()->to_date()`.

```kscript
email: email = ' Joan.Smith@Example.COM '
echo "$email { $email.local } at { $email.domain }"
echo "{ $email == 'Joan.Smith@EXAMPLE.com' }"

who: handle = @Joan_S
echo "$who { $who.name }"

id = @uuid()
key = @uuid(v: 8, name: 'joan@example.com')
echo "{ $id.version } { $key.version } { $key == @uuid(v: 8, name: 'joan@example.com') }"
```

`@uuid()` makes a random version 4 UUID. `@uuid(v: 8)` makes a version 8 UUID that is time-ordered. With `name:`, it is the same UUID every time for that name, and `namespace:` keeps names from different places apart.

A value of the wrong form fails where it is made, with a `Shape` error that says why:

```text
$email doesn't fit email: 'joan@' isn't an email: there's nothing after the @.
```

The editor reports the same problem at the value as you type (KS5012).

## More native types

These work the same way as `email` and `uuid`: a value is checked and tidied where it is made, the editor checks values written in code (KS5012), and each type has methods of its own. The names are lowercase only, so a script's own `Color` or `Duration` type is never confused with them.

```kscript
site: url = https://example.com:8443/docs?page=2
echo "{ $site.host } { $site.port } { $site.query.page }"
echo $site->with_query({ page: 3 })

home: ip = 192.168.0.12
echo "{ $home.is_private } { $home in '192.168.0.0/16' }"

v: semver = 1.4.2
echo "{ $v->satisfies('^1.2.0') } { $v->bump('minor') }"

uk: country = 'United Kingdom'
echo "$uk { $uk.name } { $uk.currency->format(1234.5) } { $uk.flag }"

wait: duration = 1h30m
echo "{ $wait.minutes } { $wait + 45m }"

brand: color = '#ff8800'
echo "{ $brand->lighten(0.5) } { $brand->contrast_ratio(white) }"

pay: card = '4242 4242 4242 4242'
echo "$pay { $pay.brand }"
```

### Web and network

| Type | What it holds | Tidied | Methods |
| --- | --- | --- | --- |
| `url` | An absolute `http`, `https`, `ftp`, `ws` or `wss` URL. | The scheme and host are lowercased. | `.scheme`, `.host`, `.port`, `.path`, `.query`, `.fragment`, `.origin`, `->with_query(values)`, `->join(path)` |
| `domain` | A domain name such as `api.example.co.uk`. International names work. | Lowercased, without a trailing dot. | `.tld`, `.labels`, `->is_subdomain_of(domain)`, `->ascii()` |
| `ip` | An IPv4 or IPv6 address. | Written the standard way: `2001:db8::1`. | `.version`, `->in(cidr)`, `.is_private`, `.is_loopback` |
| `cidr` | A block of addresses, such as `10.0.0.0/8`. | Host bits are cleared: `10.1.2.3/8` is `10.0.0.0/8`. | `->contains(ip)`, `.network`, `.prefix`, `.size` |
| `mac` | A network card's MAC address. | Six lowercase pairs with `:`. | |
| `port` | A port number from 1 to 65535. It is a number, not text. | | |

### Identifiers and formats

| Type | What it holds | Tidied | Methods |
| --- | --- | --- | --- |
| `slug` | Lowercase words joined by `-`, for URLs. `@slugify(text)` makes one. | | `.words` |
| `slash_path` | Names joined by `/`: a page, menu or category path such as `/docs/getting-started` or `/My Apps/Notes`. It isn't a file: files are read with `@file(path)`. See [slash paths](#slash-paths). | It always starts with `/` (`home` is `/home`). Spaces around each name, empty parts (`//`), `.` and a trailing `/` are dropped; `..` goes up one. Case is kept. | `.segments`, `.name`, `.parent`, `.depth`, `.is_root`, `->join(more)`, `->is_within(other)` |
| `semver` | A semantic version such as `1.4.2` or `2.0.0-beta.1`. | A leading `v` is dropped. | `.major`, `.minor`, `.patch`, `.prerelease`, `->compare(other)`, `->satisfies(range)`, `->bump(part)` |
| `mime` | A media type such as `application/json; charset=utf-8`. | The type is lowercased. | `.main_type`, `.subtype`, `.essence`, `.params`, `->is('text/*')` |
| `hex` | Hexadecimal digits, such as a hash. | Lowercased; `0x` is dropped. | `.bytes`, `->to_number()` |
| `base64` | Base64 text, standard or URL-safe. `$text->to_base64()` makes one. | | `->decode()`, `.bytes` |
| `jwt` | A JSON Web Token. Its signature isn't checked, so don't trust its claims for sign-in. | | `.header`, `.claims`, `.expires`, `.is_expired` |
| `json` | Text that holds valid JSON. | | `->json()` |

### People and places

| Type | What it holds | Tidied | Methods |
| --- | --- | --- | --- |
| `phone` | A phone number in E.164: `+` then the country code and number. | Spaces, dots, dashes, brackets and a `(0)` are dropped: `+44 (0)7700 900123` is `+447700900123`. | `.country_code`, `.national`, `->tel()` |
| `country` | An ISO 3166 country. Two-letter and three-letter codes and English names are read. | Two capitals: `GB`. | `.name`, `.alpha3`, `.currency`, `.flag` |
| `currency` | An ISO 4217 currency code. | Capitals: `GBP`. | `.name`, `.symbol`, `.digits`, `->format(amount)` |
| `locale` | A language tag such as `en-GB` or `pt-BR`. | Its usual case: `en_gb` is `en-GB`. | `.language`, `.region`, `.name`, `.native_name`, `->format(value)` |
| `timezone` | An IANA time zone such as `Europe/London`, or `UTC`. | Its usual case. | `->offset(at?)`, `->now()`, `->convert(time)`, `->is_dst(at?)` |

### Time

| Type | What it holds | Tidied | Methods |
| --- | --- | --- | --- |
| `duration` | A length of time: `1h30m`, `90s`, `2d`, ISO 8601 such as `PT1H30M`, or a number of seconds. | The short form: `90m` is `1h30m`. | `.seconds`, `.minutes`, `.hours`, `.ms`, `->iso()`, `->after(time?)`, `->before(time?)`, `->plus(other)` |
| `date_format` | How a date is written: `dd-MM-yyyy`, `d MMMM yyyy` or `HH:mm`. Every letter must mean something, so `YYYY-DD` fails with a hint. See [date formats](#date-formats). | | `.tokens`, `.has_date`, `.has_time`, `.example`, `->format(date)`, `->parse(text)` |
| `cron` | A cron schedule of five fields, or `@daily`, `@hourly` and the like. | Lowercased. | `->next(after?)`, `->next_times(count, after?)` |
| `color` | `#rgb`, `#rrggbb`, `#rrggbbaa`, `rgb(...)` or a CSS colour name. | `#rrggbb`, or `#rrggbbaa` with transparency. | `->rgb()`, `->hex()`, `->css()`, `->mix(other, amount?)`, `->lighten(amount?)`, `->darken(amount?)`, `.luminance`, `->contrast_ratio(other)` |

### Money

| Type | What it holds | Tidied | Methods |
| --- | --- | --- | --- |
| `iban` | A bank account number, checked with its mod-97 digits. | Capitals without spaces. | `.country`, `->formatted()`, `.bban` |
| `card` | A payment card number, checked with the Luhn digit. Like a password, it prints masked: `•••• 4242`, in logs and JSON too. | Spaces and dashes are dropped. | `.last4`, `.brand`, `->reveal()` |
| `money` | An amount in a currency: `GBP 9.50`, `9.50 GBP`, `£9.50`, `$12` (USD), `€3.50`, `¥1200` (JPY). The decimal point is always `.`. | The code, a space and the currency's decimal places: `£9.5` is `GBP 9.50`. | `.amount`, `.currency`, `->format(locale?)`, `->add(other, rate?)`, `->split(parts)`, `->allocate(ratios)` |

A compact amount such as `GBP1.0` is a money literal. Use an uppercase ISO currency code followed immediately by an amount. It works in assignments, lists and function arguments. For a negative amount, write `-GBP1.0`. An explicitly typed variable also accepts text such as `price: money = 'GBP 1.00'`.

Addition returns the left amount's currency. To add euros to pounds, supply the number of pounds per euro. You can set the rate once for the running host.

```kscript
@set_exchange_rate(from: 'EUR', to: 'GBP', rate: 0.80)
price = GBP1.00
extra = EUR0.75
echo $price + $extra                  // GBP 1.60
echo $price->add($extra, rate: 0.92)  // GBP 1.69
```

To share rates across fresh runs, put them in your project's `config.yaml` or `config.json`. The outer key names the incoming currency and the inner key names the receiving currency.

```yaml
money:
  exchange_rates:
    EUR:
      GBP: 0.80
```

You can also attach a quote to a particular balance with `price: money(rates: { EUR: 0.85 }) = GBP1.00`. For each addition, KScript uses the first available rate in this order.

1. An explicit `rate:` in `->add()`, for one call.
2. The receiving value's `money(rates: ...)` table.
3. A rate registered with `@set_exchange_rate()` for this run.
4. The project's `$config.money.exchange_rates` setting.

`+`, `+=`, `->add()` and `money[]->total()` use the same rule. A list accumulates in its first amount's currency. Each incoming amount rounds to the receiving currency's smallest unit before addition, with ties rounded away from zero. A missing direction raises `Type`, even when the reverse or an indirect pair exists. Subtraction, comparison and division between two money amounts still require the same currency.

### Arguments for the newer types

| Type | Arguments |
| --- | --- |
| `url` | `schemes` (the schemes allowed, such as `['https']`), `hosts` (the hosts allowed) |
| `ip` | `version` (4 or 6) |
| `hex` | `bytes` (how many bytes it must hold) |
| `phone` | `countries` (the calling codes allowed, such as `['+44', '+353']`) |
| `money` | `rates` (a map from incoming ISO currency codes to positive numeric rates for incoming additions) |

```kscript
api: url(schemes: ['https'], hosts: ['api.example.com']) = https://api.example.com/v1
office: ip(version: 4) = 10.0.0.1
echo "$api $office"
```

## Operators on native types

A native type says which operators work on its values, and only those work. A `duration` adds, subtracts, scales and compares. A `semver` compares by version. An `email` takes none, so `$email + 1` stops with a message that says so. The editor reads the same list, so it knows what `1h + 30m` gives, and warns about `$version * 2` before the script runs.

```kscript
wait: duration = 1h30m
echo $wait + 15m                // 1h45m
echo $wait * 2                  // 3h
echo $wait / 30m                // 3
echo 90s < 2m                   // true
echo @now() + $wait > @now()    // true

v: semver = 1.4.2
echo $v < '1.10.0'              // true: versions compare by number, not as text

price: money = 'GBP 9.50'
echo $price * 3                 // GBP 28.50
echo $price + 'GBP 0.50'        // GBP 10.00

home: ip = 10.0.0.1
echo $home + 5                  // 10.0.0.6
echo $home in '10.0.0.0/8'      // true
```

Text next to a native value is read as that type, so `$wait + '15m'` and `$v < '1.10.0'` work. Joining text with `+` still joins text: `'Wait ' + $wait`.

| Type | Operators | `<` `>` and sorting | `for` | False when |
| --- | --- | --- | --- | --- |
| `duration` | `duration + duration`, `duration - duration`, `duration * number`, `number * duration`, `duration / number` (a duration), `duration / duration` (a number), `duration % duration`, `date + duration`, `duration + date`, `date - duration`, `-duration`, `date..date step duration` | by length | | `0s` |
| `money` | `money + money` (mixed currencies need a directed rate), `money - money` (one currency), `money * number`, `number * money`, `money / number` (rounded to the currency's smallest unit), `money / money` (a number), `-money` | by amount, in one currency | | the amount is 0 |
| `ip` | `ip + number`, `ip - number`, `ip - ip` (a number), `ip..ip step number` | by address, IPv4 first | | |
| `cidr` | `ip in cidr`, `cidr in cidr` | by network, then prefix | its addresses (up to 65,536) | |
| `semver` | | by version: `1.0.0-rc.1 < 1.0.0 < 1.10.0`. `==` ignores build metadata. | | |
| `port` | the number operators (a port is a number) | by number | | |
| `email`, `handle`, `uuid`, `url`, `domain`, `mac`, `slug`, `mime`, `hex`, `country`, `currency`, `locale`, `timezone` | | alphabetical | | |
| `password` | | none, and no `==` either: use `->verify()` | | |
| `regex`, `base64`, `jwt`, `json`, `phone`, `cron`, `color`, `iban`, `card` | | none | | |

A type without an order stops `<` and `->sort()` with a message. Sort by a key instead: `$colors->sort_by(fn(c) => $c.luminance)`. A few types compare by meaning rather than spelling: `json` ignores spacing and key order, `base64` ignores padding, `cron` compares the times it runs (`@daily == '0 0 * * *'`), and `color` compares the colour (`orange == '#ffa500'`).

Ranges of dates step by a duration, and default to a day:

```kscript
start = '2026-11-23'->to_date()
for day in $start..$start + 5d {
  echo $day->format('ddd')      // Mon to Fri
}
```

### Lists of one native type

A list whose items are all one native type has that type's list methods as well as every list's. `->only()` and `->without()` take a filter the type declares, or a type name, written without quotes:

```kscript
versions: semver[] = [1.0.0, 1.1.0-rc.1, 1.1.0-beta.2, 1.0.10]
echo $versions->only(releases)               // 1.0.0, 1.0.10
echo $versions->without(release_candidates)  // 1.0.0, 1.1.0-beta.2, 1.0.10
echo $versions->latest()                     // 1.0.10, the highest release
echo $versions->max()                        // 1.1.0-rc.1, prereleases included

fees: money[] = ['GBP 9.50', 'GBP 0.50']
echo $fees->total()                          // GBP 10.00

a: semver = 2.0.0
mixed = [$a, 'hello', 3]
echo $mixed->only(semver)->only(releases)    // 2.0.0
echo $mixed->without(string)                 // 2.0.0, 3
echo 'hello'->is_a(string)                   // true
```

| Type | Filters | List methods |
| --- | --- | --- |
| `semver` | `releases`, `prereleases`, `release_candidates`, `betas`, `alphas` | `->latest()` |
| `money` | | `->total()` |

An explicitly typed empty list keeps its element type. Filtering also keeps the type when every item is removed. For example, `$mixed->only(semver)->only(releases)` returns `[]` when no versions match. On an empty `semver[]`, `->latest()` returns `null`. On an empty `money[]`, `->total()` returns `null` because there is no currency to sum.

A list of a native type can be written without quotes too, as above: `[1.0.0, 1.1.0-rc.1]`.

## Values without quotes

Some values need no quotes, the same way a regex needs none in JavaScript.

- **Durations** are literals anywhere: `30s`, `500ms`, `1h30m`. See [lengths of time](language.md#values-and-methods).
- **Emails, URLs, UUIDs and regexes** are literals anywhere: `joan@example.com`, `https://example.com/docs`, `3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f`, `/^[a-z]+$/i`. The editor checks them as you type. A URL starts with `http://`, `https://`, `ftp://`, `ws://` or `wss://`, so its `//` is never a comment, and it ends at a space, a comma, a bracket or `->`.
- **Slash paths** are literals anywhere when each name is letters, digits, `_`, `-`, `.`, `~` or `%`: `/docs/getting-started`, `/v1/users/12`. A slash path that is also a regex, such as `/blog/` or `/a/i`, is a regex where no type is declared, and a slash path where one is. A path with spaces in its names, such as `/My Apps/Notes`, needs quotes, except as the whole value of a declared `slash_path` (`menu: slash_path = /My Apps/Notes`). `$name` and `{ expression }` inside one are filled in, as they are in double quotes.
- **Handles** are written as `@joan` only where a handle is expected: a `handle` variable or parameter, a `not:` or `one_of:` list on a handle type, or next to a handle in `==`. Everywhere else, `@joan` still names the function `joan`.
- **Words** need no quotes where the type is read from text. That covers an enum (`color: Color = red`), a type with a `pattern` (`size: Size = 1920x1080`), a refinement of text (`slug: Slug = my-post`) and the native types: `domain = example.com`, `ip = 10.0.0.1`, `cidr = 10.0.0.0/8`, `timezone = Europe/London`, `semver = 1.4.2`, `duration = 1h30m`, `color = red`, `country = GB` and the rest. Method and function arguments of those types work the same way: `$brand->contrast_ratio(white)`, `@allow(10.0.0.0/8)`, `@release(1.4.2-beta.1)`.

Quoted text always works too: `who: handle = '@joan'`.

```kscript
enum Color { red, green, blue }

fn paint(color: Color, by: handle) => 'Painted ' + $color + ' by ' + $by

me: handle = @joan
echo @paint(green, @joan)
echo "{ $me == @joan }"
```

### Recognizing a custom literal by its pattern

A visible refinement of `string`, or of a native type written as a word (`domain`, `semver`, `timezone` and the like), can give an unquoted word its type. Declare a `pattern` on the refinement itself. The pattern must match the whole word, with its usual regex flags. A word can join names and numbers with `-`, `+`, `/` or `.`, with no spaces: `post = my-first-post`, `host = api.corp.com`, `v = 1.4.2`. Numbers alone are still arithmetic (`1+2` is 3), and when no type fits the whole word, no part of it is read as a type on its own.

```kscript
type PasskeySessionKey with [string] {
  min: 8
  normalize: $value->trim()
  pattern: /^passkey_[a-zA-Z]+$/
}

key = passkey_abc
echo $key  // passkey_abc
```

KScript selects the type before running its normalization and validation. If several types match the word, the checker and runtime name all the candidates. Choose one with `key = passkey_abc as PasskeySessionKey` or a typed assignment such as `key: PasskeySessionKey = passkey_abc`. The checker also warns when visible types declare identical regexes with the same flags. Recognized names, flags, and enum arguments keep their existing meanings.

Quotes keep text as a string. To check a computed value, write `key = "passkey_{$kind}" as PasskeySessionKey`. A repeated word reuses its type lookup while the same declarations are visible. A computed cast evaluates its input on every call. For a short repeated string, KScript can reuse a successful conversion governed entirely by static rules. With a custom normalizer or validator, it runs that code again and can reuse a successful regex match on the resulting text.

## Slash paths

A `slash_path` is a list of names joined by `/`: where a page sits on a site, a menu or category path, a route segment or a key in a tree. It reads like a URL path, but it never touches the disk. A file or folder is a `path:` argument and `@file(path)`, so the type has its own name.

```kscript
menu: slash_path = /My Applications/Something
site: slash_path = /my-website
echo "{ $menu.name } { $menu.parent } { $menu.depth } { $menu.segments }"
echo "{ $site.parent.is_root } { $site->join('blog/../about') }"

docs = /docs/guides/start
echo "{ $docs->is_within(/docs) } { $docs.parent.name }"

fn open(page: slash_path) => "Opening $page"
echo @open(/help/contact)
```

A bare slash path fills in values the way double quotes do: `$name` and `{ expression }` are worked out, and a name in braces, such as `{id}`, stays as written because it is a route parameter. A value with `/` in it adds more names. Text becomes a slash path wherever one is expected:

```kscript
id = 42
section = 'Guides'
echo /people/$id/edit
echo /docs/{ $section->lower() }/start
echo /people/{id}

note = { path: 'home' }
where: slash_path = $note.path
echo $where
```

A route's `path:` is a slash path too, so `Get(path: /people/{id})` and `Get(path: "/people/{id}")` are the same route. A route never fills in values: `{id}` there is matched against the URL. The project templates write route paths without quotes.

The value is tidied when it is made: `' / My  Apps // Notes / '` becomes `/My Apps/Notes`, `/docs/./guides/../start` becomes `/docs/start`, and `/blog/` becomes `/blog`. A slash path always starts with `/`, so text without one gets it: `'home'` and `'home/'` are both `/home`, and `/` on its own (or `''`) is the top. Case is kept, so `/Docs` and `/docs` are different paths. `..` can't go above `/`, and `\` isn't a separator, so `'C:\Apps'` fails with the reason.

| Field or method | What it gives |
| --- | --- |
| `.segments` | The names in order: `['My Applications', 'Something']`. `/` has none. |
| `.name` | The last name: `Something`, or `''` for `/`. |
| `.parent` | The path one up, as a `slash_path`: `/My Applications`, then `/`. It is null for `/`. |
| `.depth` | How many names it has: `2`. |
| `.is_root` | True for `/` itself. |
| `->join(more)` | More names on the end, tidied: `/docs->join('guides/../start')` is `/docs/start`. A `/` at the start of `more` is ignored. |
| `->is_within(other)` | True when it is the other path or under it. Names are compared exactly. |

The editor checks slash paths written in code (KS5012), suggests their fields and methods, and colours `/docs/start` as a value.

## Date formats

A `date_format` says how a date is written. It is checked when it is made, so a slip such as `YYYY` (years are `yyyy`) or `DD` (days are `dd`) fails where it is written, not as odd output later. `->format()` on a date and `->to_date()` on text take one, quoted or not:

```kscript
uk: date_format = dd-MM-yyyy
day = '2026-11-22T16:30:00'->to_date()
echo "{ $day->format($uk) } { $day->format(yyyy-MM-dd) } { $day->format('d MMMM yyyy, HH:mm') }"
echo "{ '22-11-2026'->to_date(dd-MM-yyyy)->format(dddd) } { $uk.example } { $uk.has_time }"
```

| Letters | Mean |
| --- | --- |
| `d`, `dd`, `ddd`, `dddd` | The day: `7`, `07`, `Sat`, `Saturday` |
| `M`, `MM`, `MMM`, `MMMM` | The month: `3`, `03`, `Mar`, `March` |
| `yy`, `yyyy` | The year: `26`, `2026` |
| `H`, `HH`, `h`, `hh`, `tt` | The hour on a 24-hour clock, on a 12-hour clock, and `AM` or `PM` |
| `m`, `mm`, `s`, `ss`, `fff` | Minutes, seconds and milliseconds |
| `zzz`, `K` | The offset from UTC (`+01:00`) and the time zone (`Z` for UTC) |

Anything else that is a letter goes in quotes, such as `'at' HH:mm`, apart from `T` and `Z`, which stand for themselves in ISO 8601 (`yyyy-MM-ddTHH:mm:ssZ`). Spaces and punctuation are kept as written. A format of one letter is a standard .NET format: `o` is the round-trip ISO 8601 form, and `d` on its own is the short date. A format without quotes can't hold spaces or a `:` when it is an argument; write `'d MMMM yyyy'` and `'HH:mm'` there, or declare it first (`at: date_format = d MMMM yyyy, HH:mm`).

`.tokens` lists its parts (`['dd', '-', 'MM', '-', 'yyyy']`), `.example` shows Saturday 7 March 2026 at 14:05:09 written that way, and `->parse(text)` reads text written that way. The editor suggests the letters as you type a date format.

## Passwords

A password prints as `********`. That applies to `echo`, logs, JSON and error messages. It can't be compared with `==` either, because that would leak timing. Hash it to store it, and verify the hash at sign-in:

```kscript
pw: password = 'correct horse battery staple'
echo "Password: $pw"

stored = $pw->hash()
echo "{ $stored->starts_with('$argon2id$') } { $pw->verify($stored) }"

weak: password = 'password1'
echo "{ $weak->strength(min: 12, symbol: true).problems }"
echo "{ $weak->is_strong() }"
```

| Method | What it does |
| --- | --- |
| `->hash(algorithm: argon2id)` | A salted hash in PHC format. The default is `argon2id`; `bcrypt` (cost 12) works up to 72 bytes. |
| `->verify(hash)` | True if the password matches the hash, in constant time. Both formats are read. |
| `->needs_rehash(hash)` | True when the hash was made with other settings, so it can be upgraded at the next sign-in. |
| `->strength(...)` | `{ score, label, ok, problems }`. Score 0 to 4 estimates how hard it is to guess. |
| `->is_strong(...)` | True when `strength` finds no problems. |
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

Write each rule on its own line, or separate rules on one line with a semicolon. A comma after a rule value, such as `min: 8,`, is a syntax error. A `validate` rule accepts a comma between its test and its message.

| Rule | What it checks |
| --- | --- |
| `min`, `max` | A number's value, or the length of text or a list. |
| `pattern` | Text must match. It is a regex literal such as `/^[a-z]+$/i`, or quoted text. |
| `one_of` | The value must be one of these. |
| `not` | The value must not be one of these. |
| `validate: test, 'message'` | Any expression, with the value as `$value`. Several can be given; each one that fails adds its message. |
| `normalize: expression` | Evaluates an expression with the base value in `$value`. If the expression supplies a function, KScript passes the base value to that function. The rules check the returned value. |

A value is converted to the base type first, then normalized, then checked against the rules. One that fails reports every rule it broke. Rules on a field work the same way: `title: string(min: 1)`.

Calling a type makes a value of it, checked the same way: `Age(42)`, `Slug(' Hello-World ')`. Every declared type works like this (`Repo($data)`, `Size('small')`), and its name is always written bare, without `$`.

### Checked conversions with `as`

Use `expression as Type` when you need a typed value inside an expression. KScript evaluates the expression once, then applies the same conversion, normalization and validation as a typed assignment. A failed check raises an error at the cast.

```kscript
type PasskeySessionKey with [string] {
  min: 8
  normalize: fn(c) => $c->trim()
  pattern: /^passkey_[a-zA-Z]+$/
  fn length() {
    return $value->count()
  }
}

fn key_length(key: PasskeySessionKey) => $key->count()

kind = 'login'
session_key = "passkey_{$kind}" as PasskeySessionKey
echo $session_key  // passkey_login
echo $session_key->count()  // 13
echo $session_key->length()  // 13
echo @key_length($session_key)  // 13
```

The opening slash comes before the `^` anchor in a regex literal. Here the normalizer receives the string before the pattern runs. You can also write `normalize: $value->trim()` directly.

A helper can live on the type itself. The normalizer can call it with `@trim_key($value)` or pass the function as `normalize: @trim_key`. Both forms use the input string. KScript finds helpers declared later in the block, inherited methods, and functions declared in the surrounding module. A method on the type takes precedence over a module function of the same name.

```kscript
type KeyText with [string] {
  normalize: @trim_key($value)
  fn trim_key(key: string) => $key->trim()
}
echo KeyText('  passkey_Login  ')  // passkey_Login
```

A `PasskeySessionKey` supports the methods of `string` and its own methods. Inside a refinement method, `$value` is the underlying value and `$this` is the refined receiver, so `$this->length()` can call another method on the type. A refinement inherits methods from its base and can replace one by declaring the same name. Use `$value` to call the underlying built-in method. You can also pass the key to a typed function such as `key_length`. In a record method, `$this.field` reads a field of the record.

The target can also be a built-in type (`$text as int`), a type with local rules (`$name as string(min: 2)`), or a list (`$rows as Repo[]`). An optional target such as `PasskeySessionKey?` accepts null and validates every non-null value. Parenthesize an arithmetic expression to convert its result, as in `($a + $b) as int`.

### Your own native types

`kscript make native` writes a refinement type to `natives/` that works like a built-in one. Every script in the project can use it without an import, the same way the `extensions/` folder works. Values of it are written without quotes where the type reads text, they are checked wherever the type is used (assignments, arguments, a call of the type and request bodies), and the editor checks the values written in code:

```sh
kscript make native Sku --pattern '/^[A-Z]{3}-\d{4}$/' --normalize '$value->trim()->upper()'
kscript make native Percent --base int --min 0 --max 100
kscript make native Username --base handle --max 16 --not @admin,@root
kscript make native CompanySite --base url --validate "\$value.host->ends_with('example.com'), 'Use a company site'"
```

The first one writes `natives/sku.kscript`:

```kscript
/// Sku: a native type built on string. A value that breaks a rule fails where it is made, and the editor checks the values written in code.
type Sku with [string] {
  normalize: $value->trim()->upper()
  pattern: /^[A-Z]{3}-\d{4}$/
  // Rules: normalize, pattern, min, max, one_of, not and validate. Add methods with fn.
}
```

Then, in any script in the project:

```kscript
sku: Sku = abc-1234   // ABC-1234
fn price(of: Sku) => 10
```

`--base` selects the base type, which defaults to `string`. It accepts native types such as `url`, `domain` and `country`, or another refinement. The rules are `--pattern`, `--min`, `--max`, `--one-of a,b`, `--not a,b`, `--normalize` and `--validate "test, 'message'"`. Declare methods with `fn` in the type body and call them with `->`. In an XML project, the file is a `<type>` element in `natives/sku.ks.xml`.

The types and enums in every file under `natives/` (subfolders too) are the project's own native types. `"natives"` in [kscript.json](projects.md#project-settings-kscriptjson) names other folders or globs, and `kscript make native` writes to the first folder it lists:

```json
{ "natives": ["src/types", "shared/**/*.kscript"] }
```

A type a script declares itself wins over a project type of the same name.

### Native types from packages

A package adds a native type with `p.NativeType(new IsbnType())`, where `IsbnType` is a subclass of `NativeType` (KScript.Core). The base class makes a type say everything the language does with its values, so none can be left out: its name, description and example, how text becomes a value (`Parse`), how it is written without quotes, what `==` means (`Equality`), whether `<` and sorting work (`Ordering`), whether a `for` loop can walk it (`Iteration`), its operators (`DeclareOperators`), its fields and methods (`DeclareMembers`), the filters and methods of a list of it (`DeclareListMembers`), the string methods it keeps and the values the editor offers. "None" is a fine answer to any of them, written as an empty body or `Ordering.None`. `DurationType` is the one to copy. Once the package is imported, the type works like a built-in one: values without quotes, checks in the editor, `->type()` and OpenAPI schemas. Native type names are lowercase, and a name that is taken can't be reused.

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

admin = Admin({ name: 'Joan', email: 'joan@example.com', created: '2026-01-05' })
echo "{ @welcome($admin) } { User->is($admin) } { $admin.level }"
```

When two bases have a field with the same name but different types, the joined type must declare that field itself. The same goes for a method two bases both have: declare it in the joined type to choose. In the editor, the lightbulb on a joined type offers to override each method it gets from its bases, and writes the stub. `User->is($admin)` says whether a value is a User, and `User->is(Admin)` whether a type is built on User. A `validate:` rule in a type with fields sees the fields as `$name`, `$email` and so on.

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
echo "{ /\d+/->match_all('3 cats, 12 dogs')->map(fn(m) => $m.text) }"
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
  <validate test="not $value.name->starts_with('_')" message="A username cannot start with _" />
</type>
<type name="Admin" with="User, Auditable">
  <field name="level" type="int" default="1" />
</type>
<let name="who" type="Username" value="@joan" />
```

`kscript convert` turns either form into the other.

## Ideas for later

These would work the same way and could be added next: `date_range`, and phone numbers written the national way with a default region. Tell us which you would use first.
