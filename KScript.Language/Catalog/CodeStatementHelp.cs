using KScript.Metadata;

namespace KScript.Language.Catalog;

/// <summary>Code syntax for statements whose XML representation has a different argument layout.</summary>
public static class CodeStatementHelp
{
    public static bool OptionsFollowName(string name) => name is "type" or "model" or "error" or "job";

    public static IEnumerable<AttributeInfo> Options(ElementInfo element)
    {
        if (OptionsFollowName(element.Name))
            return element.Attributes.Where(attribute => attribute.Name is not ("name" or "with" or "extends" or "fields"));
        return For(element.Name) == null ? element.Attributes : Enumerable.Empty<AttributeInfo>();
    }

    public static string For(string name) => name switch
    {
        "throw" => """Raises an error when the statement runs. Use `throw Validation "Invalid age"` for a message, or `throw Error(message: 'Oops', value: $age, cause: $original)` for named fields. To throw a payload map, write `throw $payload`. Inside a catch, bare `throw` rethrows the current error. Use `throw if condition { ... }` for a conditional throw.""",
        "try" => "Runs statements with one or more error handlers and optional cleanup. Write `try { ... } catch Validation as err { ... }`.",
        "catch" => "Recovers from errors. Use `@load() catch Validation => $err.message` for a fallback value or append `catch Validation { return $err.message }` to a declaration. Omit the type to catch any error.",
        "finally" => "Runs cleanup after the try body and any catch. Append `finally { ... }` after the preceding block.",
        "if" => "Runs a block when its condition is true. Write `if $ready { ... }`, followed by optional `elif condition { ... }` and `else { ... }` branches.",
        "guard" => "Checks a precondition. Write `guard $ready else { return }`. The failure block leaves with return, throw, break, continue or exit.",
        "elif" => "Tests another condition after an if or elif branch. Write `elif $ready { ... }`.",
        "else" => "Runs the fallback branch. Write `else { ... }` after an if, elif, or for block.",
        "while" => "Repeats while its condition is true. Write `while $remaining > 0 { ... }`.",
        "for" => "Loops over values. Write `for item in $items { ... }`.",
        "match" => "Selects the first matching arm. Write `match $n { 0 => 'zero', _ => 'other' }`.",
        "case" or "when" or "default" => "Use a match arm such as `0 => 'zero'`, or `_ => 'other'` for the fallback.",
        "break" => "Exits the enclosing loop. Write `break`.",
        "continue" => "Starts the next iteration of the enclosing loop. Write `continue`.",
        "return" => "Leaves the current function. Write `return $result` to send a value to the caller, or `return` to return null.",
        "exit" => "Stops the script. Write `exit 3` to set the process exit status or `exit` for status zero.",
        "fn" => """Declares a function. Write `fn greet(name: string) => "Hi {$name}"` or give it a statement body with braces. Call it with `@greet('Joan')`.""",
        "type" => "Declares a type. Write `type Person { name: string }` for a record, or `type Key with [string] { min: 3 }` for a refinement.",
        "model" => "Declares a persisted record. Write `model Person(key: id) { id: int }`. Model options follow the name and fields go in the body.",
        "error" => "Declares an error type. Write `error InvalidAge extends Validation { age: int }`, then `throw InvalidAge(age: $age)`.",
        "enum" => "Declares a set of named values. Write `enum Size { small, large }`.",
        "job" => "Declares a background job. Put its fields and a `fn handle() { ... }` method inside `job SendMessage { ... }`.",
        "extend" => "Adds methods to a type. Write `extend string { fn example() => $this }`.",
        "policy" => "Says who may do what to a model's records. Write `policy for Post { fn update(user: User, post: Post) => $user.id == $post.author_id }`.",
        "import" => """Imports a package with `import KScript.IO`, or another script with `import "./helpers.kscript"`.""",
        "const" => "Defines a constant. Write `const limit = 10`.",
        "let" or "set" or "do" => "Write an assignment like `total = 10` or a standalone call such as `@save($total)`.",
        "field" => "Write a field inside the type or model body, such as `name: string` or `due?: date`.",
        _ => null,
    };
}
