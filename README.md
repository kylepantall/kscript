<h1>KScript is a developing project.</h1>

KScript allows the parsing of a XML KScript document into a functional scripting language.
An example of a KScript document:

<hr />

<h2>Script Example</h2>
<p>For additional information, include: print_info="yes" attribute within the <kscript> document open tag.</p>

<hr />

```xml
<kscript dynamic_defs="yes">
  <!-- By default KScript allows Dynamic defs, meaning they'll be defined automatically if they haven't already upon usage -->
  <!-- <def id="entered_name"/> -->
  
  <!-- Obtains input with the prompt "What's your name?" and stores the text input to variable 'entered_name' -->
  <input to="entered_name" type="text">What's your name?</input>
  
  <!-- Outputs 'entered_name' variable to the console -->
  <echo>Hello, $entered_name!</echo>

</kscript>
```

<hr />

<h2>Building and running</h2>

KScript targets .NET 8 and runs on Windows, Linux and macOS. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download), then:

```sh
dotnet build KScript.sln
dotnet test KScript.sln

# Run a script
dotnet run --project KScriptApp -- "path/to/script.xml"
```

To produce a standalone executable for a platform:

```sh
dotnet publish KScriptApp -c Release -r linux-x64   # or win-x64, osx-arm64, ...
```

Command line options:

| Option | Description |
| --- | --- |
| `-s` | Quiet: don't print the script timing, even if the script sets `quiet="no"`. |
| `-nowait` | Don't wait for a key press when the script finishes (never waits when input is piped). |
| `-cai` | Clear the console after each input. |
| `-all` | Print information about all available KScript objects. |
| `-lang=<culture>` | Set the script language/culture. |
| `-guid` | Print a new GUID and exit. |
| `-root` | Open the KScript install directory. |
| `-admin` | Restart with administrator privileges (Windows only; use `sudo` elsewhere). |

Anything after the script path is available to the script through `@args()`.

<h2>Writing portable scripts</h2>

- Build paths with `@join_path($dir, 'file.txt')` rather than hard-coding `\` or `/`.
- `@k('os.platform')` returns `windows`, `linux` or `macos`; `@k('path.separator')`, `@k('user.home')` and `@k('temp.directory')` are also available.
- Numbers are always parsed and printed with `.` as the decimal separator, whatever the machine's language.
- `<registry>` and `@registry` only work on Windows and report a clear error elsewhere.

<h2>Command syntax</h2>

Commands are written `@name(argument, argument, ...)`. Arguments can be `'quoted strings'`, nested commands or plain text, and spaces around arguments are ignored:

```xml
<echo>@to_lower(@split('A,B,C', ',', '1'))</echo>   <!-- b -->
<echo>@math(2 * (3 + 4))</echo>                     <!-- 14 -->
```

An `@` that isn't followed by a name and `(` is plain text, so e-mail addresses and apostrophes in text are safe.

<h2>Methods, pipes and lambdas</h2>

Any value (a variable, a `'quoted string'`, a number or a command's result) can have methods called on it with `->`. Calls can be chained and can take arguments:

```xml
<echo>$name->trim()->upper()</echo>
<echo>'a.b.c'->replace('.', '/')</echo>
<echo>3.14159->round(2)</echo>
<echo>@math('2+2')->mul(10)</echo>
<for to="i" while="$i->lt(10)" math="$i->increment()"> ... </for>
```

Values are typed on use: a value is a number if it parses as one, a bool if it is yes/no/true/false, and a string otherwise (`$x->type()` tells you which). Calling a number method on text gives a clear error. On a variable, `increment()`/`decrement()` also update the variable; on other values they just return the result.

| Kind | Methods |
| --- | --- |
| Any | `type` `length` `is_number` `is_bool` `is_empty` `has_value` `is_url` `equals(x, ignore_case?)` `default(x)` `call(args...)` |
| String | `upper` `lower` `trim` `trim_start` `trim_end` `reverse` `contains(x)` `starts_with(x)` `ends_with(x)` `index_of(x)` `replace(a, b)` `substring(start, len?)` `char_at(i)` `split(sep, i)` `count(x)` `repeat(n)` `pad_left(w, c?)` `pad_right(w, c?)` |
| Number | `add` `sub` `mul` `div` `mod` `pow` `round(d?)` `floor` `ceil` `abs` `min` `max` `lt` `lte` `mt` `mte` `between(a, b)` `is_zero` `not_zero` `positive` `negative` `increment(by?)` `decrement(by?)` |
| Bool | `not` `and(x)` `or(x)` `xor(x)` |

`|>` passes a value on to a method, a command (as its first argument) or a lambda. Lambdas are written `fn($x) => expression`:

```xml
<echo>$name |> trim() |> upper()</echo>
<echo>$csv |> @split(',', '1')</echo>
<echo>$name |> fn($s) => $s->lower()->reverse()</echo>

<def id="shout">fn($s) => $s->upper()->replace('.', '!')</def>
<echo>'hi.' |> $shout</echo>
<echo>$shout->call('hey.')</echo>
```

A lambda body is one expression; use brackets to group: `fn($n) => ($n->mul(2) |> fn($m) => $m->add(1))`. Lambda parameters are only visible inside the lambda.

Variables are resolved after a line is parsed, so values containing `,`, `)` or `'` are always passed through intact and are never run as commands.

<h2>Additional Information</h2>
Further documentation is still within development. 
