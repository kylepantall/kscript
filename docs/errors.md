# Error codes

Every problem `kscript check` and the editor report has a code, such as `KS4001`. This page says what each one means and how to fix it. In the editor, click the code in an error to open its entry here.

Codes are grouped by what they are about: `KS1` the shape of the file, `KS2` values, `KS3` statements, `KS4` names, `KS5` calls and `KS6` style. A project can turn a code off or change how serious it is: see [Lint standards](standards.md).

## The shape of the file

## KS1001

**Something is written where it can't go.** A quote, bracket or comment was opened and never closed, or two statements share a line.

```kscript wrong KS1001
echo "Hello          // wrong: the text never ends
echo "Hello"         // right
```

A missing quote is taken to end with its line, so the lines after it are still checked.

## KS1002

**An end tag doesn't match its start tag.** In an XML script, `<if>` must end with `</if>`. Check the spelling and the order the tags close in.

## KS1003

**A block is never closed.** Every `{` needs a `}`, and in XML every start tag needs its end tag.

```kscript
for i in 0..3 {
  echo $i
}                    // this line was missing
```

## KS1004

**An argument is written wrongly.** Arguments in brackets are `name: value`, separated by commas: `wait(duration: 2s)`.

## KS1005

**The same argument is given twice.** Keep one of them.

## Values

## KS1006

A declaration in `kscript.rules.kscript` is invalid. Use `Style(...)`, `Lint(...)` or `Format(...)` with literal arguments. Check the supported arguments and examples in [Project rules](formatting.md#project-rules).

## KS2001

**A value isn't written correctly.** The message says what was expected. Common causes are a line that ends with an operator, a bracket that is never closed, and using `&&` or `||` where KScript expects `and` or `or`.

```kscript wrong KS2001
total = 5 +          // wrong: nothing after the +
total = 5 + 1        // right
```

## KS2002

**`=` is used where a value is expected.** To compare, write `==`. To give a variable a value, put the assignment on a line of its own.

```kscript wrong KS2002
if $count = 1 { }    // wrong
if $count == 1 { }   // right
```

## KS2003

**Two statements run together, or a statement does nothing.** Put each statement on its own line, or separate them with `;`. A statement on its own must do something: assign, call or return. `$total + 1`, or `@boards.max_file_size` on its own line, reads a value and drops it, and `@greet` without brackets names the function without calling it. The quick fixes call it (`@greet()`) or remove the line.

## KS2004

**A bare word is used as a value.** Variables are read with `$`, and text needs quotes.

```kscript
name = Kyle          // wrong
name = 'Kyle'        // right: text
name = $other        // right: another variable
```

## KS2005

**Single quotes keep text as written.** `"Double quotes"` evaluate expressions in `{ }`, such as `{ $name }`. `'Single quotes'` never do, so a variable named inside them is not filled in. The quick fix changes the quotes and adds interpolation braces.

```kscript wrong KS2005
name = 'Joan'
greeting = 'Hello $name'         // warning: holds the text "Hello $name"
greeting = "Hello { $name }"     // right: Hello Joan
label = 'Costs $5'               // fine: $5 is not a variable
```

The same code warns when SQL text is filled in before it reaches `@sql`, because the values are then pasted into the SQL instead of being sent as parameters:

```kscript wrong KS2005
import KScript.Data

id = 42
query = "select * from users where id = {$id}"   // filled in here
rows = @sql($query)                              // warning: nothing left to send as a parameter
rows = @sql("select * from users where id = {$id}")   // right: {$id} is a parameter
```

## Statements

## KS3001

**KScript has no statement with this name.** Often it is a typo, or a word from another language.

| You wrote | KScript writes |
| --- | --- |
| `print "hi"`, `console.log("hi")` | `echo "hi"` |
| `function greet() { }`, `def greet()` | `fn greet() { }` |
| `foreach x in $list { }` | `for x in $list { }` |
| `switch $x { }` | `match $x { }` |
| `x++` | `x += 1` |

## KS3002

**A word that isn't needed, or that KScript 2 removed.** In a `.kscript` file, `x = 3` declares `x` the first time and changes it after that, so `let`, `set` and `var` are dropped. In an XML script, the message names what replaced a KScript 1 element. A statement that was renamed says its new name in both formats (`before-each is now before_each.`), and `kscript check --fix` renames it.

## KS3003

**A statement has no argument with this name.** The message lists the ones it has.

## KS3004

**An argument KScript 2 removed, or one that was renamed.** The message names what replaced it: `'to' is now into.`, `'must-exist' is now must_exist`, `'src' is now path.` A renamed argument has a quick fix, and `kscript check --fix` applies it. See [Names of statements and arguments](language.md#names-of-statements-and-arguments).

## KS3005

**A required argument is missing.** For example `for` needs something to loop over: `for item in $list { }`.

## KS3006

**An argument has a value it can't take.** The message lists the values it accepts.

## KS3007

**A statement is in the wrong place.** `return` only works inside `fn`, `break` and `continue` inside a loop, `case` inside `match`, and `elif` and `else` straight after the `}` of an `if`.

## KS3008

**Something a block can't hold.** In an XML script, loose text is ignored unless it is inside an element that prints it, such as `<echo>`. An `extend` block holds `fn` methods only.

## KS3009

**Something from a package that isn't imported.** Add the import at the top of the script. The quick fix does it for you.

```kscript
import KScript.IO

text = @read_text('notes.txt')
```

## KS3010

**An import names a package that doesn't exist.** Check the spelling: packages are named like `KScript.IO` and `KScript.Math`.

## Names

## KS4001

**A variable is read before it has a value.** Usually a typo, and the message suggests the closest name. Otherwise give it a value first.

```kscript
name = 'Joan'
echo "Hello { $nme }"    // wrong
echo "Hello { $name }"   // right
```

## KS4002

**A variable is updated before it has a value.** `count += 1` needs `count` to exist. Give it a starting value first: `count = 0`.

## KS4003

**A constant is given a new value.** A `const` can't change. Use a plain variable if it needs to.

## KS4004

**The same name is declared twice.** Two functions, types or parameters share a name. Rename one of them.

## KS4005

**A function that doesn't exist is named.** Check the spelling, or declare it: `fn greet(who: string) { }`.

## KS4006

**A KScript 1 way of naming a variable.** Variables are always written `$name`.

## KS4007

**A doc comment describes a parameter the function doesn't have.** Match the name after `@param` to a parameter.

## KS4008

**Something marked `@deprecated` is used.** Its doc comment says what to use instead.

## Calls

## KS5001

**A command that doesn't exist is called.** Commands start with `@`, including your own functions: `@greet('World')`. Some things are methods in KScript and not commands: the length of a list is `$list->count()`.

## KS5002

**A command KScript 2 removed.** The message names what replaced it.

## KS5003

**A method the value doesn't have.** After `->`, the editor lists the methods the value has. Methods are always called with brackets: `$name->upper()`.

## KS5004

**A call has too few or too many arguments.** The message shows what the function takes. An argument with `?` or a default can be left out.

## KS5005

**A value is the wrong type for what is done with it.** For example `->upper()` on a number. Convert it first: `$n->to_string()->upper()`.

A callback parameter declared `action: fn` needs a function. Passing `action: null` produces an error on the argument. Pass a reference such as `action: @on_connect` or a lambda such as `action: fn(session) => @read_status($session)`. In a script function, `action: fn? = null` explicitly accepts a missing or null callback.

The same code warns about arithmetic and arguments that can only fail when the script runs:

```kscript wrong KS5005
name = 'Joan'
total = $name - 1            // warning: '-' needs a number here, but $name holds the text 'Joan'
total = [1, 2] + 3           // warning: + joins two lists. Use ->push(3)
echo 'ab'->repeat('x')       // warning: the 'count' argument needs a number
```

Text that reads as a number is converted (`'5' - 1` is 4), `+` joins text and `'ab' * 3` repeats it, so none of those warn. The check only speaks up when the value is certain: text written in the code, or a variable that only ever holds it.

## KS5006

**A planned feature is used.** `render`, `on` and `@dom()` (scripts that run in the browser) are described in the reference but no package runs them yet, so a script that uses one stops there. The check reports each use.

## KS5007

**A call names an argument the function doesn't have.** The message lists its parameters.

## KS5008

**A named argument is given twice, or a word is used as a flag on a parameter that isn't true/false.** Give each argument once, and give other parameters a value: `name: value`.

## KS5009

**An argument takes one of a fixed set of words, and this isn't one of them.** The message lists them.

The same code reports an ability a [policy](auth.md#policies) doesn't have, as in `@can(publsh, $post)`.

## KS5010

**A field the value doesn't have.** The records of a `type` only have the fields it declares.

A map written in the code is checked too. Reading a field a map doesn't have gives nothing, so a near miss of one it does have is almost always a typo:

```kscript wrong KS5010
person = { name: 'Joan', age: 82 }
echo $person.nmae            // warning: Did you mean name?
echo $person.email ?? '-'    // fine: not close to a field, so it may be optional
```

Text and numbers have no fields at all, so `$name.upper` stops the script. Call the method instead: `$name->upper()`.

The same code reports a [private](types.md#private-fields-and-methods) field or method used outside its type. Use one of the type's other methods, or take `private` off the member.

## KS5011

**An error type that isn't declared.** Declare it with `error Name { }`, or use a built-in one such as `Validation`, `NotFound` or `IO`.

A built-in type that was renamed is an error, with a fix that writes the new name: `QueryError` is now `Query`, and `ConnectionError` is now `Connection`.

## KS5012

**A value doesn't fit its declared type.** For example an `email` without an `@`, or a number outside the range a type allows.

## KS5013

**A column the model doesn't have**, named in a [KScript.Data](data.md) query, `->create()` or `->update()`: `User->where(nmae: 'Joan')` or `->order_by(agee)`. The message suggests the closest field.

## KS5014

**A model the migrations don't match.** The model's table, as its fields say it should be, isn't what the files in `database/migrations` make. Run `kscript db diff` to write the migration, or `kscript db diff --pretend` to see it first. See [Migrations](data.md#migrations).

## KS5015

**Two visible literal types declare the same pattern.** The checker warns at the declaration or import that brings them together. You can use both types explicitly. If a bare word matches more than one visible pattern, the word itself gets a KS5012 error naming the candidates. See [recognizing a custom literal by its pattern](types.md#recognizing-a-custom-literal-by-its-pattern).

## KS5016

**A known error can escape the script.** The checker reports the error types at the top-level statement that can raise them. A helper may pass an error to its caller. Add a matching `catch` at the level that can handle it. A catch on a function, method or type propagates the error when it falls through or returns `null`. Ordinary `try` blocks can handle an error and continue.

The checker follows resolved function and method calls and their documented `@throws` types. Calls through dynamically selected callbacks may raise additional errors. Test bodies run under the test runner, and `expect_error` handles errors while checking the expected outcome.

## KS5017

**An `@inject()` takes a record of a model that has a [policy](auth.md#policies), and nothing asks the policy.** The record reaches the component or type whoever is asking. Write the ability to ask, or check where the record is used:

```kscript wrong KS5017
import KScript.Data
import KScript.Auth

model Post {
  title: string
  published: bool = false
}

policy for Post {
  fn read(user: map?, post: Post) => $post.published or $user != null
}

fn heading() => @inject(Post).title    // warning: Post has a policy
```

`@inject(Post, can: read)` asks the policy as the record is taken, and answers `403` when it says no. As an input's default it is `construct({ post: Post = @inject(can: read) })`.

## Style

The KS6xxx codes come in two kinds.

**Old spellings are errors.** KS6006, KS6009, KS6010, KS6011, KS6012 and KS6014 each mark a spelling that was replaced by one that is kept. In a `.kscript` file the script doesn't run: the engine stops with the same message `kscript check` gives, and no standard or `lint.rules` setting turns it down. `kscript check --fix` rewrites every one of them. XML scripts keep the forms that are XML's own (`lt` and `ge`, a comma list in an attribute, `fields="x: int, y: int"`), and get a warning where a rule applies to both forms (KS6010).

**Style rules are warnings.** KS6001 to KS6005, KS6007, KS6008, KS6013 and KS6018 can be adjusted through a [standard](standards.md).

## KS6001

**An `else` where an early `return`, `continue` or `break` reads better.** See [Early returns instead of else](language.md#early-returns-instead-of-else).

## KS6002

**A function nothing calls.** Remove it, or start its name with `_` to keep it on purpose.

## KS6003

**A variable whose value is never read.** Remove it, or start its name with `_`.

## KS6004

**A parameter the function never reads.** Remove it, or start its name with `_`.

## KS6005

**An import nothing uses.** Remove it.

## KS6006

**A list written as one string.** An error in a `.kscript` file. Write an array: `use: [log, cors]` for names, `to: ['paper.pdf', 'paper.md']` for text, `fields: [x: int, y: int]` for fields. `kscript check --fix` rewrites it.

## KS6007

**A function parameter with no type.** Write one, so callers and the editor know what it takes: `fn cached(key: string, compute: fn)`. When every call in the file passes the same type, the editor shows it beside the name and the quick fix writes it in. A parameter with a default (`count = 0`) takes its type from the default, and functions inside a `job` are left alone, because the queue calls them.

## KS6008

**A second type in one file.** In a project, each type, model, enum and error type goes in its own file (`types/`, `models/`, `errors/`, or `requests/` for request bodies) and is imported where it is used. The message names the file to move it to. A [component](components.md#types-that-belong-to-a-component) may keep the types only it uses inside its own block. See [One type per file](projects.md#where-files-go-the-recommended-layout).

## KS6009

**Text written as a bare word.** An error in a `.kscript` file. In an argument list, quotes make text and everything else is code, so text goes in quotes: `Section(title: "Results")`, `Document(to: 'paper.pdf')`. A bare word is a name: a variable to fill (`to: answer`), a function, a type or one of a fixed set of words (`align: right`). `kscript check --fix` adds the quotes.

## KS6010

**A name declared with `$`.** An error in a `.kscript` file, and a warning in XML. `$` reads a value; a name being declared or assigned is written bare. Write `total = 3` and `total += 1`, not `$total = 3`, `fn(n) => $n * 2`, not `fn($n) => $n * 2`, and `fn double(n) => $n * 2`, not `fn double($n)`. This holds in XML too (`<do>total += 1</do>`). `kscript check --fix` rewrites it.

## KS6011

**yes/no where true/false is meant.** An error in a `.kscript` file. Booleans are `true` and `false` everywhere in `.kscript`: `Section(numbered: false)`. A flag on its own (`Section(numbered)`) means true. `kscript check --fix` rewrites it.

## KS6012

**A second spelling.** An error in a `.kscript` file. Each of these has one form to write: `elif` (not `else if`), `key: value` in arguments (not `key = value`), `catch NotFound | Conflict` (not a comma), and symbols for comparisons in `.kscript` (`<`, `>=`, `==`, not `lt`, `ge`, `eq`; XML keeps the words). Logic stays as words: `and`, `or`, `not`. A function is named as `@name` in an argument, as it is in an expression: `spawn(fn: @job)`, `llm(tools: [@lookup])`, `Get(to: @people.show)`, not a bare `job`. `kscript check --fix` rewrites each one.

## KS6013

**A name that breaks the naming rules.** A warning: the script runs. Functions, variables, fields and arguments are `snake_case`; types, models, enums and error types are `PascalCase`. The editor renames a variable everywhere the file uses it with the quick fix; a function, a parameter, a field or a type has no quick fix, because other files and callers may name it, so use the editor's rename. `kscript check --fix` leaves names alone.

## KS6014

**A type written the long way.** An error in a `.kscript` file. Each of these has one form, and `kscript check --fix` rewrites the other:

- A type, model, enum, error or job is named bare, without `$`: `$text->json(as: Repo)`, `User->create(name: 'Joan')`. `$` reads a variable.
- An optional field is marked on its name, `age?: int`, not on its type (`age: int?`). `T?` stays for a parameter that may be null: `fn show(repo: Repo?)`.
- A model's options go in its brackets, `model User(timestamps, unique: [email]) { }`, not as lines of its block. The block holds fields, `validate` and `fn`.

Calling the name makes a value of every kind of type: `Repo($data)`, `Repo(name: 'x')`, `Age(7)`, `Size('small')`, `NotFound(id: 42)`, `SendWelcome(user_id: 7)`. `->from()` and `->new()` are gone, and using one is an error (KS5003) whose fix writes the call.

## KS6018

**A length of time without its unit.** A length of time is a duration: a number and a unit, with no quotes. Write `timeout: 30s`, `ttl: 10m` and `wait(duration: 500ms)`, not `timeout: 30` or `ttl: '10m'`. The units are `ms`, `s`, `m`, `h`, `d` and `w`, and they can be joined: `1h30m`. A plain number still means seconds, so this is a warning and the fix adds `s`. `wait(500ms)` takes its one argument without the name. The check reads arguments written with their name: a time given without it (`wait(5)`) or as a key of a map (`options: { timeout: 30 }`) is not flagged, and is still seconds.

## KS6019

**An unbraced reference inside double quotes.** This is a syntax error. In a `.kscript` file, write `"Hi { $name }"` to mark where an expression starts and ends. Use **Wrap in interpolation braces** or run `kscript check --fix` to update older scripts. Use single quotes to keep characters such as `$` literally.
