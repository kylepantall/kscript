using System.Globalization;

using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Runtime
{
    /// <summary>
    /// KScript.Core: the language itself. Every script has it without importing it: variables, branches, loops,
    /// functions, errors, imports and &lt;echo&gt;, plus the methods on text, numbers, lists, maps and dates.
    /// </summary>
    public sealed partial class CorePackage : KScriptPackage
    {
        public override string Name => "KScript.Core";

        public override string Description => "The language itself: variables, branches, loops, functions, errors, imports and echo. Always imported.";

        protected override void Register(PackageBuilder p)
        {
            RegisterElements(p);
            RegisterCommands(p);
            RegisterVariables(p);
            RegisterMethods(p);
            RegisterConfig(p);
            RegisterLogging(p);
            RegisterI18n(p);
            RegisterExamples(p);
            RegisterAgentNotes(p);
        }

        #region Elements

        private static readonly A Test = new A("test", K.Expression, "The condition, for example `$n < 10 and $name != ''` (in an XML attribute, `$n lt 10 and $name ne ''`).", true);

        private static void RegisterElements(PackageBuilder p)
        {
            p.Element(new ElementInfo("kscript", "The root of a script. Everything the script does goes inside.", ContentKind.Statements)
            {
                Context = ElementContext.Script,
                Example = "<kscript>\n  <echo>Hello!</echo>\n</kscript>",
            }, run =>
            {
                run.RunChildren();
                return null;
            });
            p.Element(new ElementInfo("let", "Declares a variable in the current scope. The value goes in `value`, or in the element text for longer values. Without `name`, the text is assignment statements such as `x = 3`.",
                ContentKind.Expression,
                new A("name", K.DeclareVariable, "The variable to declare."),
                new A("value", K.Expression, "The initial value. Can be written as the element's text instead."),
                new A("type", K.TypeName, "Convert the value to this type, or fail early: a built-in such as int, an <enum>, a <type>, T[] for a list, T? to allow null.", false, Values.DeclarableTypes.ToArray()),
                new A("const", K.Bool, "true makes a later <set> an error."))
            {
                Example = "<let name=\"count\" value=\"0\" />\n<let name=\"people\">[{ name: 'Joan', age: 82 }]</let>\n<let>x = 3</let>",
            }, run => Assign(run, declare: true));

            p.Element(new ElementInfo("const", "Declares a value that can't change: the same as <let const=\"true\">. Top-level consts are exported to scripts that import this one.",
                ContentKind.Expression,
                new A("name", K.DeclareVariable, "The constant to declare.", true),
                new A("value", K.Expression, "Its value. Can be written as the element's text instead."),
                new A("type", K.TypeName, "Convert the value to this type, or fail early: a built-in such as int, an <enum>, a <type>, T[] for a list, T? to allow null.", false, Values.DeclarableTypes.ToArray()),
                new A("description", K.Text, "What the constant is for, shown on hover."))
            {
                Example = "<const name=\"max_size\" value=\"10 * 1024\" />",
            }, run =>
            {
                string name = run.VariableName("name");
                object value = run.Has("value") ? run.Eval("value") : run.EvalContent();
                string type = run.Raw("type");
                if (type != null) value = Shapes.Convert(run.Interpreter, value, type, "$" + name, run.Scope, run.Module);
                run.Scope.Declare(name, value, isConst: true);
                return null;
            });

            p.Element(new ElementInfo("enum", "Declares a set of named values. `Size.small` is 'small', and a parameter typed with it (`params=\"size: Size\"`) only takes those values, which calls can write bare: `@pack(large)`.",
                ContentKind.Empty,
                new A("name", K.DeclareVariable, "The enum's name, used as a parameter type and as Name (Size.small).", true),
                new A("values", K.Text, "The values, as a list: `[small, medium, large]` (in XML, `small, medium, large`).", true) { NameList = true },
                new A("description", K.Text, "What the enum is for, shown on hover."))
            {
                Example = "<enum name=\"Size\" values=\"small, medium, large\" />\n<fn name=\"pack\" params=\"size: Size\" value=\"'Packing ' + $size\" />\n<echo>{ @pack(large) }</echo>",
            }, run =>
            {
                string name = run.VariableName("name");
                List<string> names = KScript.Metadata.NameList.Names(run.Raw("values"));
                if (names.Count == 0) throw run.Error($"<enum name=\"{name}\"> needs its values: values=\"a, b, c\".", "values", "Syntax");
                string bad = names.FirstOrDefault(v => !Lexer.IsIdentStart(v[0]) || !v.All(Lexer.IsIdentChar));
                if (bad != null) throw run.Error($"'{bad}' can't be an enum value: use letters, digits and _, starting with a letter.", "values", "Syntax");
                string twice = names.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
                if (twice != null) throw run.Error($"<enum name=\"{name}\"> lists '{twice}' twice.", "values", "Syntax");
                run.Scope.Declare(name, new EnumType(name, names), isConst: true);
                return null;
            });

            p.Element(new ElementInfo("type", "Declares the shape of some data, such as an API response. `->json(as: Repo)`, `Repo($data)`, `<let type=\"Repo\">` and `repo: Repo` parameters check data against it and turn it into a record: a map whose fields the editor knows. Every problem is reported at once, with its JSON path.",
                ContentKind.Statements,
                new A("name", K.DeclareVariable, "The type's name, used as `type=\"Repo\"`, `as: Repo` and Repo.", true),
                new A("fields", K.Fields, "A short type's fields on one line, like fn params: `login: string, id: int, avatar_url?: string`. Bigger types list their fields one per line instead (<field> elements)."),
                new A("path", K.Path, "A JSON file holding a sample of the data. Its keys become fields; <field> elements and `fields` replace the ones they name."),
                new A("extra", K.Enum, "Keys in the data that the type doesn't list: drop them (the default), keep them, or make them an error.", false, "drop", "keep", "error"),
                new A("with", K.TypeNames, "Other types this one is made from. Types with fields are joined: `with=\"User, Auditable\"` has all their fields and rules, and fits wherever they do. One built-in type, such as `with=\"int\"` or `with=\"handle\"`, makes a type with rules instead of fields: min, max, pattern, one_of, not, <validate> and <normalize>.", false, Values.DeclarableTypes.ToArray()),
                new A("min", K.Text, "The smallest value allowed: a number for numbers, a date for dates, the length for text (a handle's name without its @), the count for lists. Needs with=\"...\" naming one built-in type."),
                new A("max", K.Text, "The largest value allowed: a number for numbers, a date for dates, the length for text, the count for lists. Needs with=\"...\" naming one built-in type."),
                new A("pattern", K.Text, "A regular expression the value's text must match: `^[a-z0-9-]+$`. On a type with fields, its named groups fill the fields of the same name, so the type can be made from text."),
                new A("parse", K.Expression, "On a type with fields: an expression that turns text into a map of the fields, with the text as $value and the pattern's match (if any) as $match."),
                new A("one_of", K.Text, "The only values allowed: `gb, ie` or `['gb', 'ie']`. Compared after converting and normalising."),
                new A("not", K.Text, "Values that aren't allowed, such as reserved names: `['@admin', '@root']`."),
                new A("description", K.Text, "What the type is, shown on hover."))
            {
                Example = "<type name=\"User\">\n  <field name=\"login\" type=\"string\" />\n  <field name=\"id\" type=\"int\" />\n</type>\n<type name=\"Repo\">\n  <field name=\"name\" type=\"string\" />\n  <field name=\"stars\" type=\"int\" from=\"stargazers_count\" />\n  <field name=\"owner\" type=\"User\" />\n</type>\n<let name=\"repo\" value=\"Repo({ name: 'kscript', stargazers_count: 12, owner: { login: 'kyle', id: 1 } })\" />\n<echo>$repo.name has $repo.stars stars</echo>",
            }, run =>
            {
                IKObject type = Shapes.Declare(run);
                run.Scope.Declare(type.ToText(), type, isConst: true);
                return null;
            });

            p.Element(new ElementInfo("error", "Declares an error type. `throw NotFound(id: 42)` raises one, `catch NotFound as e` catches it and the error types that extend it, and `$e.id` reads its fields. A `status` (its own or a parent's) is the HTTP status a KScript.Http route answers with when nothing catches it.",
                ContentKind.Statements,
                new A("name", K.DeclareVariable, "The error type's name, starting with a capital letter: NotFound, PaymentFailed.", true),
                new A("extends", K.ErrorType, "The error type it is a kind of, such as Validation or another <error>. `catch` of that type catches this one too, and it inherits its fields, message and status. Defaults to Error."),
                new A("message", K.Template, "The message, with the fields as variables: `No {$kind} with id {$id}`. `throw NotFound(id: 1, message: '...')` gives one instead."),
                new A("status", K.Text, "The HTTP status a KScript.Http route answers with, such as 404. Inherited from the type it extends."),
                new A("fields", K.Fields, "The fields on one line, like fn params: `id: int, kind: string = 'item'`."),
                new A("description", K.Text, "What the error means, shown on hover."))
            {
                Example = "<error name=\"PersonNotFound\" extends=\"NotFound\" fields=\"id: int\" message=\"No person with id {$id}\" />\n<try>\n  <throw value=\"PersonNotFound(id: 42)\" />\n  <catch type=\"NotFound\" as=\"e\">\n    <echo>{ $e.message } ({ $e.status })</echo>\n  </catch>\n</try>",
            }, run =>
            {
                ErrorType type = DeclareError(run);
                run.Scope.Declare(type.Name, type, isConst: true);
                return null;
            });

            p.Element(new ElementInfo("field", "One field of a <type>, <error>, <model> or <job>: in a .kscript file, one line of the block, `stars: int from stargazers_count`.", ContentKind.Empty,
                new A("name", K.Text, "The field's name. End it with ? to make it optional.", true),
                new A("type", K.TypeName, "The field's type: a built-in such as string or int, an <enum>, another <type>, T[] for a list, T? to allow null. Defaults to any.", false, Values.DeclarableTypes.ToArray()),
                new A("from", K.Text, "Where to read the value, when it isn't the field's name: `stargazers_count`, `owner.login`, `items[0].name`."),
                new A("default", K.Expression, "The value when the data has none (makes the field optional)."),
                new A("optional", K.Bool, "true lets the value be missing or null."),
                new A("value", K.Expression, "Works the field out from the others (they are $name) instead of reading it: `$first + ' ' + $last`."),
                new A("min", K.Text, "The smallest value allowed: a number, a date, or the length of text or a list."),
                new A("max", K.Text, "The largest value allowed: a number, a date, or the length of text or a list."),
                new A("pattern", K.Text, "A regular expression the value's text must match."),
                new A("one_of", K.Text, "The only values allowed: `gb, ie` or `['gb', 'ie']`."),
                new A("not", K.Text, "Values that aren't allowed, such as reserved names."),
                new A("description", K.Text, "What the field holds, shown on hover and completion."))
            {
                MustBeInside = new[] { "type", "error", "model", "job" },
            }, run => throw run.Error($"<field> must be directly inside <type>, <error>, <model> or <job>.", null, "Syntax"));

            p.Element(new ElementInfo("validate", "A rule a <type> checks: the test must be true. In a type made from one other type, the value is `$value`. In a type with fields, each field is a variable (`$end > $start`). Every rule that fails is reported, with its message.", ContentKind.Empty,
                new A("test", K.Expression, "What must be true: `$value % 5 == 0`, `$end > $start`.", true),
                new A("message", K.Text, "What to say when it isn't, such as `must end after it starts`."))
            {
                MustBeInside = new[] { "type", "model" },
                Example = "<type name=\"Even\" with=\"int\">\n  <validate test=\"$value % 2 == 0\" message=\"must be even\" />\n</type>\n<echo>{ Even(4) }</echo>",
            }, run => throw run.Error($"<validate> must be directly inside <type>.", null, "Syntax"));

            p.Element(new ElementInfo("normalize", "Cleans up a value before a <type>'s rules check it, such as trimming or lower-casing. The value is `$value`; the result is converted to the type again.", ContentKind.Empty,
                new A("value", K.Expression, "The cleaned-up value: `$value->trim()->lower()`.", true))
            {
                MustBeInside = new[] { "type" },
                Example = "<type name=\"Slug\" with=\"string\" pattern=\"^[a-z0-9-]+$\">\n  <normalize value=\"$value->trim()->lower()\" />\n</type>\n<echo>{ Slug(' Hello-World ') }</echo>",
            }, run => throw run.Error($"<normalize> must be directly inside <type>.", null, "Syntax"));

            p.Element(new ElementInfo("set", "Changes an existing variable. Fails if the variable was never declared (a typo guard) or is const. Without `name`, the text is assignment statements.",
                ContentKind.Expression,
                new A("name", K.VariableReference, "The variable to change."),
                new A("value", K.Expression, "The new value. Can be written as the element's text instead."),
                new A("op", K.Enum, "Update instead of replace: op=\"+=\" value=\"1\".", false, "=", "+=", "-=", "*=", "/=", "??="),
                new A("type", K.TypeName, "Convert the value to this type, or fail: a built-in such as int, an <enum>, a <type>, T[] for a list, T? to allow null.", false, Values.DeclarableTypes.ToArray()))
            {
                Example = "<let name=\"total\" value=\"0\" />\n<let name=\"count\" value=\"0\" />\n<set name=\"total\" value=\"$total + 2.50 * 3\" />\n<set>count += 1</set>\n<echo>$count item, $total</echo>",
            }, run => Assign(run, declare: false));

            p.Element(new ElementInfo("do", "Runs assignment statements and calls, separated by `;`.", ContentKind.AssignmentStatements)
            {
                Example = "<let name=\"total\" value=\"10\" />\n<do>total += 5; total *= 2</do>\n<echo>$total</echo>",
            }, run =>
            {
                // In a .kscript file x = 3 declares x if it isn't in scope yet.
                run.RunStatements(declare: false, declareNew: run.Module.Document?.Syntax == SourceSyntax.Code);
                return null;
            });

            p.Element(new ElementInfo("echo", "Prints its text. `{ expr }` inserts a value; `$name` and `$name->method()` work directly.", ContentKind.Template,
                new A("value", K.Expression, "Print this value instead of the text."),
                new A("format", K.Enum, "How to print: text (the default), json (indented and, in a terminal, coloured), or markdown (with KScript.Terminal). A map or nested list on its own is printed as json.", false, "text", "json", "markdown"),
                new A("newline", K.Bool, "no leaves the cursor at the end of the line."))
            {
                Example = "<let name=\"name\" value=\"'Joan'\" />\n<let name=\"items\" value=\"['tea', 'cake']\" />\n<echo>Hello, $name! You have { $items->count() } items.</echo>",
            }, Echo);

            p.Element(new ElementInfo("if", "Runs its body when the test is true. Follow it with <elif> and <else> for other cases.", ContentKind.Statements, Test)
            {
                Example = "<let name=\"score\" value=\"80\" />\n<if $score ge 90>\n  <echo>A</echo>\n</if>\n<elif $score ge 75>\n  <echo>B</echo>\n</elif>\n<else>\n  <echo>C</echo>\n</else>",
            }, run =>
            {
                bool ok = Values.Truthy(run.Eval("test"));
                if (ok) run.RunChildren();
                return ok;
            });

            p.Element(new ElementInfo("elif", "Runs when the earlier <if>/<elif> tests were false and this test is true.", ContentKind.Statements, Test)
            {
                MustFollow = new[] { "if", "elif" },
            }, run =>
            {
                if (run.Chain == null) throw run.Error($"<elif> must come right after an <if> or <elif>.", null, "Syntax");
                if (run.Chain == true) return true;
                bool ok = Values.Truthy(run.Eval("test"));
                if (ok) run.RunChildren();
                return ok;
            });

            p.Element(new ElementInfo("else", "Runs when the earlier <if>/<elif> tests were all false, or after a <for> that had nothing to loop over.", ContentKind.Statements)
            {
                MustFollow = new[] { "if", "elif", "for" },
            }, run =>
            {
                if (run.Chain == null) throw run.Error($"<else> must come right after an <if>, <elif> or <for>.", null, "Syntax");
                if (run.Chain == false) run.RunChildren();
                return null;
            });

            p.Element(new ElementInfo("for", "Loops over a list, a map or a range such as 0..10 (end exclusive) or 0..100 step 5. `$loop` holds index, first, last and count.", ContentKind.Statements,
                new A("each", K.DeclareVariables, "The loop variable, or `key, value` for a map.", true),
                new A("in", K.Expression, "What to loop over: a list, a map, a range, a number (0 up to it) or text (its characters).", true),
                new A("at", K.DeclareVariable, "A variable for the position, from 0."))
            {
                IsLoop = true,
                Example = "<let name=\"people\" value=\"[{ name: 'Joan' }, { name: 'Sam' }]\" />\n<for each=\"p\" in=\"$people\">\n  <echo>{$p.name}</echo>\n</for>\n<for each=\"i\" in=\"0..10\"><echo>$i</echo></for>\n<let name=\"settings\" value=\"{ host: 'localhost', port: 8080 }\" />\n<for each=\"key, value\" in=\"$settings\"><echo>$key = $value</echo></for>",
            }, For);

            p.Element(new ElementInfo("while", "Loops while the test is true. `test=\"true\"` loops until <break/>.", ContentKind.Statements, Test)
            {
                IsLoop = true,
                Example = "<let name=\"n\" value=\"0\" />\n<while $n lt 3>\n  <set name=\"n\" value=\"$n + 1\" />\n</while>\n<echo>$n</echo>",
            }, run =>
            {
                while (Values.Truthy(run.Eval("test")))
                {
                    try
                    {
                        run.RunChildren();
                    }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { }
                }
                return null;
            });

            p.Element(new ElementInfo("break", "Leaves the innermost loop.", ContentKind.Empty), _ => throw new BreakSignal());
            p.Element(new ElementInfo("continue", "Skips to the next turn of the innermost loop.", ContentKind.Empty), _ => throw new ContinueSignal());

            p.Element(new ElementInfo("match", "Compares one value against several cases. Inside, `$it` is the value.", ContentKind.Statements,
                new A("value", K.Expression, "The value to compare.", true))
            {
                Example = "<let name=\"choice\" value=\"3\" />\n<match $choice>\n  <case is=\"1\"><echo>One</echo></case>\n  <case is=\"2, 3\"><echo>Two or three</echo></case>\n  <case when=\"$choice gt 9\"><echo>Big</echo></case>\n  <default><echo>Unknown</echo></default>\n</match>",
            }, Match);

            p.Element(new ElementInfo("case", "A branch of <match>: runs when the value equals one of `is`, or when `when` is true (with both, the value must match and `when` must be true). In a .kscript file a branch is an arm: `1, 2 => ...`, `_ when $n > 9 => ...`.", ContentKind.Statements,
                new A("is", K.ExpressionList, "Values to compare with, comma separated."),
                new A("when", K.Expression, "A condition."))
            {
                MustBeInside = new[] { "match" },
            }, run => throw run.Error($"<case> must be directly inside <match>.", null, "Syntax"));

            p.Element(new ElementInfo("default", "The branch of <match> that runs when no case matched. In a .kscript file it is the arm `_ => ...`.", ContentKind.Statements)
            {
                MustBeInside = new[] { "match" },
            }, run => throw run.Error($"<default> must be directly inside <match>.", null, "Syntax"));

            p.Element(new ElementInfo("extend", "Adds methods to a type: a built-in one such as date, string or list, a package's (file, response), or a <type>. Each <fn> inside becomes a method, with the value it is called on as $this: `extend date { fn json() => $this->to_json() }` makes `@today()->json()` work. Scripts that import the file get the methods too, and in a project every file in the extensions folder applies everywhere.",
                ContentKind.Statements,
                new A("type", K.Text, "The type to add methods to: date, string, number, bool, list, map, fn, email, handle, uuid, regex, password, a package's type such as file or response, or a <type>'s name.", true),
                new A("description", K.Text, "What the methods are for, shown on hover."))
            {
                Example = "<extend type=\"date\">\n  <fn name=\"is_weekend\" value=\"$this->format('ddd') in ['Sat', 'Sun']\" />\n</extend>\n<echo>{ @today()->is_weekend() }</echo>",
            }, run =>
            {
                Extensions.Declare(run);
                return null;
            });

            p.Element(new ElementInfo("fn", "Declares a function, called like a command: `@name(args)`, or piped: `$x |> @name()`. Functions can be called before the line that declares them.", ContentKind.Statements,
                new A("name", K.DeclareFunction, "The function's name.", true),
                new A("params", K.Parameters, "Parameters, with optional types and defaults: `who, greeting = 'Hello'` or `count: int = 1`."),
                new A("value", K.Expression, "For one-line functions: the result, instead of a body."),
                new A("override", K.Bool, "Inside <extend>: true replaces the type's built-in method of the same name. Without it, reusing a built-in method's name is an error."),
                new A("description", K.Text, "What the function does. Shown in hover, and to a model when used as an <llm> tool."))
            {
                Example = "<fn name=\"greet\" params=\"who: string, greeting = 'Hello'\">\n  <return value=\"$greeting + ', ' + $who + '!'\" />\n</fn>\n<echo>@greet('World')</echo>",
            }, run =>
            {
                // Declared when the block it is in starts (so it can be called before it's written).
                if (!run.Scope.HasOwn(run.VariableName("name"))) run.Interpreter.DeclareFunction(run.Element, run.Scope, run.Module);
                return null;
            });

            p.Element(new ElementInfo("return", "Ends the function with a value.", ContentKind.Empty,
                new A("value", K.Expression, "The result. Without it the function returns null."))
            {
                MustBeInside = new[] { "fn" },
            }, run => throw new ReturnSignal(run.Eval("value", null)));

            p.Element(new ElementInfo("call", "Calls a function for its effect, optionally keeping the result.", ContentKind.Empty,
                new A("fn", K.FunctionReference, "The function (or command) to call: `@greet` (in XML, fn=\"greet\").", true),
                new A("args", K.ExpressionList, "Arguments, comma separated."),
                new A("into", K.DeclareVariable, "A variable for the result."))
            {
                Example = "<fn name=\"greet\" params=\"who: string\"><echo>Hello, $who!</echo></fn>\n<call fn=\"greet\" args=\"'World'\" />",
            }, Call);

            p.Element(new ElementInfo("spawn", "Runs a function in the background. The script waits for it before it ends.", ContentKind.Empty,
                new A("fn", K.FunctionReference, "The function to run: `@count_to` (in XML, fn=\"count_to\").", true),
                new A("args", K.ExpressionList, "Arguments, comma separated."))
            {
                Example = "<fn name=\"count_to\" params=\"n: int\"><echo>Counted to { @range(1, $n + 1)->last() }</echo></fn>\n<spawn fn=\"count_to\" args=\"1000\" />\n<echo>Started counting</echo>",
            }, run =>
            {
                ICallable fn = FindFunction(run, run.RequiredRaw("fn"));
                run.Interpreter.Spawn(fn, run.Has("args") ? run.EvalList("args") : new List<object>(), run.Module);
                return null;
            });

            p.Element(new ElementInfo("try", "Runs its body, handing errors to its <catch> elements. <finally> always runs.", ContentKind.Statements)
            {
                Example = "<let name=\"text\" value=\"'not json'\" />\n<try>\n  <let name=\"data\" value=\"$text->json()\" />\n  <catch as=\"err\" when=\"$err.type eq 'Json'\">\n    <echo>That isn't JSON: {$err.message}</echo>\n  </catch>\n  <finally><echo>done</echo></finally>\n</try>",
            }, Try);

            p.Element(new ElementInfo("catch", "Handles matching errors in a try block, a function call, or calls to methods of a type or model. The nearest matching handler runs first. The bound error exposes its type, message, location, status, cause and custom fields. A bare throw passes it to the next enclosing handler.", ContentKind.Statements,
                new A("as", K.DeclareVariable, "A variable for the error. Defaults to err."),
                new A("when", K.Expression, "Only handle errors for which this is true, such as `$err.status == 404`."),
                new A("type", K.ErrorType, "Only handle errors of these types, or types that extend them: `NotFound`, `[IO, Network]`. Error catches everything."))
            {
                MustBeInside = new[] { "try", "fn", "type", "model" },
            }, run => null);

            p.Element(new ElementInfo("finally", "Runs after the <try> body and any <catch>, whether or not there was an error.", ContentKind.Statements)
            {
                MustBeInside = new[] { "try" },
            }, run => null);

            p.Element(new ElementInfo("throw", "Raises an error, which a <catch> can handle. Without a <try>, the script stops with the message. `value` throws an error made from a type (`NotFound(id: 42)`) or a caught one again; a bare <throw /> inside a <catch> rethrows the error it is handling.", ContentKind.Template,
                new A("type", K.ErrorType, "The error type, such as Validation or an <error>. Defaults to Error."),
                new A("message", K.Template, "What went wrong. Can be written as the element's text instead.") { SatisfiedByContent = true },
                new A("value", K.Expression, "The error to throw: `NotFound(id: 42)`, `Validation('Too short')`, or a caught `$err`."),
                new A("cause", K.Expression, "The error that led to this one, such as the $err of the <catch> around it. It is $e.cause on the new error."))
            {
                Example = "<let name=\"age\" value=\"-1\" />\n<try>\n  <throw type=\"Validation\" message=\"Age must be positive, got {$age}\" />\n  <catch as=\"err\"><echo>{ $err.type }: { $err.message }</echo></catch>\n</try>",
            }, Throw);

            p.Element(new ElementInfo("import", "Imports a package (`package=\"KScript.Terminal\"`), or the top-level <fn> functions and <let const> values of another script (`path`). Goes at the top of the script.", ContentKind.Empty,
                new A("package", K.Text, "A package such as KScript.Terminal, KScript.Math, KScript.IO or KScript.Net."),
                new A("path", K.Path, "A script to import, relative to this file."),
                new A("as", K.DeclareVariable, "Use the import under a prefix: `@math.sqrt(2)`, `str.slugify()`."),
                new A("names", K.FunctionReferences, "Import only these names from a script, callable directly: `@slugify()`."))
            {
                Example = "<import package=\"KScript.Terminal\" />\n<import package=\"KScript.Math\" as=\"math\" />\n<import path=\"lib/strings.ks.xml\" as=\"str\" />",
            }, run =>
            {
                if (!run.Has("package") && !run.Has("path")) throw run.Error($"<import> needs package=\"...\" (such as KScript.Terminal) or path=\"...\" (a script file).", null, "Syntax");
                if (run.Has("path")) run.Interpreter.ImportScript(run.Element, run.Scope, run.Module);
                return null;
            });

            p.Element(new ElementInfo("exit", "Ends the script, with an exit code for the shell (0 means success).", ContentKind.Empty,
                new A("code", K.Expression, "The exit code. Defaults to 0."))
            {
                Example = "<exit code=\"1\" />",
            }, run => throw new ExitSignal((int)run.Number("code", 0)));

            p.Element(new ElementInfo("wait", "Pauses the script.", ContentKind.Empty,
                new A("duration", K.Expression, "How long to wait: 500ms, 2s, 1m30s. A plain number is seconds.", true) { Type = Duration.TypeName })
            {
                Example = "<wait duration=\"1.5s\" />",
            }, run =>
            {
                double ms = (run.Seconds("duration") ?? 0) * 1000;
                if (ms > 0) Task.Delay(TimeSpan.FromMilliseconds(ms), run.Interpreter.Options.Cancellation).GetAwaiter().GetResult();
                return null;
            });

            p.Element(new ElementInfo("collection", "Builds a list or map from nested <item> elements, for data that reads better as markup than as a literal.", ContentKind.Statements,
                new A("name", K.DeclareVariable, "The variable to declare.", true),
                new A("const", K.Bool, "true makes a later <set> an error."))
            {
                Example = "<collection name=\"person\">\n  <item key=\"name\">Joan</item>\n  <item key=\"age\">82</item>\n</collection>",
            }, run =>
            {
                object value = Items(run, run.Element);
                run.Scope.Declare(run.VariableName("name"), value, run.Flag("const"));
                return null;
            });

            p.Element(new ElementInfo("item", "An entry of <collection>. With `key` it is a map entry, without it a list item. Its text is the value (numbers become numbers); nested items make a list or map.", ContentKind.Template,
                new A("key", K.Text, "The key in the enclosing map."),
                new A("value", K.Expression, "The value as an expression, instead of text."))
            {
                MustBeInside = new[] { "collection", "item" },
            }, run => throw run.Error($"<item> must be inside <collection>.", null, "Syntax"), ownsContent: true);
        }

        private static bool? Assign(ElementRun run, bool declare)
        {
            if (!run.Has("name"))
            {
                if (!run.HasContent)
                    throw run.Error($"<{run.Name}> needs a name and value, e.g. <{run.Name} name=\"count\" value=\"0\" />, or a statement like <{run.Name}>count = 0</{run.Name}>.", null, "Syntax");
                run.RunStatements(declare);
                return null;
            }

            string name = run.VariableName("name");
            string type = run.Raw("type");
            object value = run.Has("value") ? run.EvalTyped("value", type) : run.EvalContentTyped(type);
            if (type != null) value = Shapes.Convert(run.Interpreter, value, type, "$" + name, run.Scope, run.Module);

            string op = run.Raw("op")?.Trim();
            if (op != null && op != "=")
            {
                if (!run.Scope.TryGet(name, out object current)) throw run.Error($"${name} is not defined, so op=\"{op}\" can't update it.", "name", "Name");
                value = run.Interpreter.Combine(current, op, value);
            }

            if (declare) run.Scope.Declare(name, value, run.Flag("const"));
            else run.Scope.Assign(name, value);
            return null;
        }

        private static bool? Echo(ElementRun run)
        {
            string format = run.Raw("format")?.Trim() ?? "text";
            string text;
            bool json = format == "json";
            if (run.Has("value"))
            {
                object value = run.Eval("value");
                // A map or a nested list on its own is printed as indented JSON.
                json |= format == "text" && JsonText.IsStructured(value);
                text = json ? JsonText.Pretty(value) : Values.ToStr(value);
            }
            else if (format == "json")
            {
                // JSON written out in the element is used as it is; otherwise the text is filled in first ({ } and $name).
                string raw = run.Content?.Value?.Trim();
                text = JsonText.Pretty(raw != null && JsonText.IsValid(raw) ? Values.ParseJson(raw) : Values.ParseJson(run.Text()));
            }
            else
            {
                string structured = null;
                bool single = format == "text" && run.ContentIsSingleValue;
                text = run.Text(value =>
                {
                    if (single && JsonText.IsStructured(value))
                    {
                        structured = JsonText.Pretty(value);
                        return string.Empty;
                    }
                    return Values.ToStr(value);
                });
                if (structured != null)
                {
                    text = structured;
                    json = true;
                }
            }
            if (json && run.Host.UseColour) text = JsonText.Colourize(text);

            if (format != "text" && format != "json")
            {
                if (!run.Module.EchoFormats.TryGetValue(format, out EchoFormatter formatter))
                {
                    KScriptPackage owner = run.Interpreter.Packages.LoadAll().FirstOrDefault(pk => pk.EchoFormats.ContainsKey(format));
                    throw run.Error(owner != null
                        ? (run.Module.IsCode ? $"echo(format: {format}) needs {owner.Name}." : $"<echo format=\"{format}\"> needs {owner.Name}.") + $" Add {run.Module.ImportHint(owner.Name)} at the top of the script."
                        : $"Unknown echo format '{format}'.", "format", "Import");
                }
                text = formatter(text, run.Interpreter);
            }

            if (run.Flag("newline", true)) run.Host.WriteLine(text);
            else run.Host.Write(text);
            return null;
        }

        private static IEnumerable<object> NativeItems(ElementRun run, object source)
        {
            try
            {
                return NativeTypeRegistry.TryItems(source, out IEnumerable<object> items) ? items : null;
            }
            catch (KScriptException ex) when (ex.Line == 0)
            {
                throw run.Error(ex.Message, "in", ex.Type);
            }
        }

        private static bool? For(ElementRun run)
        {
            string[] names = run.RequiredRaw("each").Split(',').Select(n => n.Trim().TrimStart('$')).Where(n => n.Length > 0).ToArray();
            if (names.Length == 0 || names.Length > 2) throw run.Error($"each=\"...\" names the loop variable (each=\"item\"), or a key and value for a map (each=\"key, value\").", "each", "Syntax");
            string indexName = run.VariableName("at", required: false);
            object source = Values.Unwrap(run.Eval("in"));

            List<(object Item, object Key)> items = source switch
            {
                null => new List<(object, object)>(),
                // A native type says whether a loop can walk it: a cidr block gives its addresses.
                IKObject when NativeItems(run, source) is IEnumerable<object> walked => walked.Select(x => (x, (object)null)).ToList(),
                List<object> list => list.Select(x => (x, (object)null)).ToList(),
                Dictionary<string, object> map => map.Select(kv => (names.Length > 1 ? kv.Value : new Dictionary<string, object> { ["key"] = kv.Key, ["value"] = kv.Value }, (object)kv.Key)).ToList(),
                double n => Enumerable.Range(0, Math.Max(0, (int)n)).Select(x => ((object)(double)x, (object)null)).ToList(),
                string s => s.Select(ch => ((object)ch.ToString(), (object)null)).ToList(),
                _ => throw run.Error($"<for> can't loop over {Values.Describe(source)}.", "in", "Type"),
            };

            if (names.Length == 2 && source is not Dictionary<string, object>)
            {
                // each="i, item" over a list: the index and the item.
                items = items.Select((x, i) => (x.Item, (object)(double)i)).ToList();
            }

            for (int i = 0; i < items.Count; i++)
            {
                Scope iteration = run.Scope.Child();
                if (names.Length == 2)
                {
                    iteration.Declare(names[0], items[i].Key);
                    iteration.Declare(names[1], items[i].Item);
                }
                else
                {
                    iteration.Declare(names[0], items[i].Item);
                }
                if (indexName != null) iteration.Declare(indexName, (double)i);
                iteration.Declare("loop", new Dictionary<string, object>
                {
                    ["index"] = (double)i,
                    ["first"] = i == 0,
                    ["last"] = i == items.Count - 1,
                    ["count"] = (double)items.Count,
                });
                try
                {
                    run.RunChildren(iteration);
                }
                catch (BreakSignal) { break; }
                catch (ContinueSignal) { }
            }
            return items.Count > 0;
        }

        private static bool? Match(ElementRun run)
        {
            object value = run.Eval("value");
            Scope scope = run.Scope.Child();
            scope.Declare("it", value);
            var evalRun = run;
            foreach (MarkupElement c in run.ChildElements)
            {
                string kind = c.Name;
                bool hit;
                if (kind == "default")
                {
                    hit = true;
                }
                else if (kind == "case")
                {
                    DecodedText isText = c.GetAttribute("is")?.Value;
                    DecodedText whenText = c.GetAttribute("when")?.Value;
                    if (isText == null && whenText == null)
                    {
                        throw new KScriptException($"<case> needs is=\"...\" or when=\"...\".", "Syntax") { Offset = c.Span.Start, OffsetModule = run.Module };
                    }
                    // With both, the value must be one of `is` and `when` must hold: 1, 2 when $ready => ...
                    hit = (isText == null || run.Interpreter.EvaluateList(isText, scope, run.Module).Any(o => Values.Unwrap(o) is List<object> options ? options.Any(x => Values.LooseEquals(x, value)) : Values.LooseEquals(o, value)))
                        && (whenText == null || Values.Truthy(run.Interpreter.EvaluateText(whenText, scope, run.Module)));
                }
                else
                {
                    throw new KScriptException($"Only <case> and <default> can go inside <match>, not <{kind}>.", "Syntax") { Offset = c.Span.Start, OffsetModule = run.Module };
                }

                if (hit)
                {
                    run.RunElementChildren(c, scope.Child());
                    return null;
                }
            }
            return null;
        }

        private static ICallable FindFunction(ElementRun run, string name)
        {
            name = name.Trim().TrimStart('@', '$');
            if (run.Scope.TryGet(name, out object v) && Values.Unwrap(v) is ICallable fn) return fn;
            if (run.Module.Commands.TryGetValue(name, out CommandDefinition cmd))
                return new NativeFunction(cmd.Name, call => run.Interpreter.InvokeCommand(cmd, call));
            int dot = name.IndexOf('.');
            if (dot > 0 && run.Module.Aliases.TryGetValue(name.Substring(0, dot), out ImportNamespace ns) && ns.TryGet(name.Substring(dot + 1), out object member))
            {
                if (Values.Unwrap(member) is ICallable c) return c;
                if (member is CommandDefinition d) return new NativeFunction(d.Name, call => run.Interpreter.InvokeCommand(d, call));
            }
            string close = Values.Closest(name, run.Scope.Names().Where(n => run.Scope.TryGet(n, out object f) && Values.Unwrap(f) is ICallable));
            throw run.Error(Wording.Of($"There is no function called {name}.") + (close != null ? $" Did you mean {close}?" : Wording.Of($" Declare it with <fn name=\"{name}\">.")), "fn", "Name");
        }

        private static bool? Call(ElementRun run)
        {
            ICallable fn = FindFunction(run, run.RequiredRaw("fn"));
            List<object> args = run.Has("args") ? run.EvalList("args") : new List<object>();
            object result = run.Interpreter.Call(fn, args, null, run.Scope, run.Module);
            if (run.Has("into")) run.Scope.Declare(run.VariableName("into"), result);
            return null;
        }

        private static bool? Try(ElementRun run)
        {
            List<MarkupElement> catches = run.ChildElements.Where(e => e.Name == "catch").ToList();
            List<MarkupElement> finallies = run.ChildElements.Where(e => e.Name == "finally").ToList();
            List<MarkupNode> body = run.Element.Children.Where(n => !(n is MarkupElement e && (e.Name == "catch" || e.Name == "finally"))).ToList();
            foreach (MarkupElement c in catches)
            {
                // A catch of a renamed type would never match: say so before anything runs.
                if (c.GetAttributeValue("type") is string named) RefuseRenamed(run, c, named);
            }

            try
            {
                run.Run(body, run.Scope.Child());
            }
            catch (KScriptException ex)
            {
                Interpreter.Located(ex, run.Module, run.Element.Span.Start);
                try
                {
                    if (!catches.Any(c => CatchClauses.Handle(run.Interpreter, c, run.Scope, run.Module, ex))) throw;
                }
                finally
                {
                    if (finallies.Count > 0) RunFinally(run, finallies);
                }
                return null;
            }
            catch (ControlSignal)
            {
                if (finallies.Count > 0) RunFinally(run, finallies);
                throw;
            }
            if (finallies.Count > 0) RunFinally(run, finallies);
            return null;
        }

        private static bool? Throw(ElementRun run)
        {
            if (run.Has("value"))
            {
                if (run.Has("type") || run.Has("message") || run.HasContent)
                    throw run.Error($"<throw value> throws the error it is given. Put its type and message there: value=\"Validation('Too short')\".", "value", "Syntax");
                throw ThrownValue(run, run.Eval("value"));
            }
            string message = run.Has("message") ? run.Template("message") : run.Text();
            string typeName = run.Raw("type")?.Trim();
            object cause = run.Has("cause") ? run.Eval("cause") : null;
            if (typeName == null && string.IsNullOrWhiteSpace(message) && cause == null && ErrorTypes.Handling is KScriptException handling) throw handling;
            ErrorType type = ErrorTypes.Find(typeName ?? ErrorTypes.Root, run.Scope);
            if (type == null)
            {
                if (ErrorTypes.RenamedMessage(typeName) is string renamed) throw run.Error(renamed, "type", "Retired");
                // An error type nobody declared still works, as a kind of Error (the editor warns about it).
                KScriptException adhoc = run.Error(string.IsNullOrWhiteSpace(message) ? typeName : message, null, typeName);
                if (cause != null) adhoc = WithCause(adhoc, cause, run);
                throw adhoc;
            }
            var named = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(message)) named["message"] = message;
            if (cause != null) named["cause"] = cause;
            if (named.Count == 0 && !type.IsBuiltIn && type.Fields.Fields.Any(f => !f.Optional))
            {
                // throw PersonNotFound names the type alone, as a value does: say how to give its fields.
                throw run.Error($"{type.Name} has fields, so throw it with them: {type.Name}({string.Join(", ", type.Fields.Fields.Where(f => !f.Optional).Select(f => f.Name + ": ..."))}).", "type", "Argument");
            }
            try
            {
                throw type.Create(run.Interpreter, Array.Empty<object>(), named);
            }
            catch (KScriptException ex) when (ex.Declared == null)
            {
                throw run.Error(ex.Words, "type", ex.Type);
            }
        }

        private static KScriptException WithCause(KScriptException ex, object cause, ElementRun run)
        {
            cause = Values.Unwrap(cause);
            KScriptException inner = cause is ErrorValue e ? e.Exception : cause is Dictionary<string, object> map ? ErrorTypes.FromMap(map)
                : throw run.Error($"cause is the error that led to this one, such as $err in a catch, not {Values.Describe(cause)}.", "cause", "Argument");
            var wrapped = new KScriptException(ex.Words, ex.Type, inner) { Offset = ex.Offset, OffsetModule = ex.OffsetModule };
            foreach (var kv in ex.Fields) wrapped.Fields[kv.Key] = kv.Value;
            return wrapped;
        }

        /// <summary>What <c>throw $value</c> throws: a caught error again, an error made with NotFound(...), or a map or text.</summary>
        private static KScriptException ThrownValue(ElementRun run, object value) => Values.Unwrap(value) switch
        {
            ErrorValue e => e.Exception,
            ErrorType t => t.IsBuiltIn || t.Fields.Fields.All(f => f.Optional) ? Located(run, t.Create(run.Interpreter, Array.Empty<object>(), new Dictionary<string, object>()))
                : throw run.Error($"{t.Name} has fields, so throw it with them: {t.Name}({string.Join(", ", t.Fields.Fields.Where(f => !f.Optional).Select(f => f.Name + ": ..."))}).", "value", "Argument"),
            Dictionary<string, object> map when map.ContainsKey("message") => Located(run, ErrorTypes.FromMap(map)),
            string text => run.Error(text),
            _ => throw run.Error(new Wording($"<throw value> needs an error, such as NotFound(id: 42) or a caught $err, not {Values.Describe(value)}.",
                    $"throw needs an error, such as NotFound(id: 42) or a caught $err, not {Values.Describe(value)}."), "value", "Type"),
        };

        private static KScriptException Located(ElementRun run, KScriptException ex)
        {
            ex.Offset ??= run.Element.Span.Start;
            ex.OffsetModule ??= run.Module;
            return ex;
        }

        /// <summary>The names in a catch's type: <c>NotFound</c>, <c>IO, Network</c> or <c>[IO, Network]</c>.</summary>
        private static void RefuseRenamed(ElementRun run, MarkupElement element, string types)
        {
            foreach (string name in ErrorNames(types))
            {
                if (ErrorTypes.RenamedMessage(name) is string message && ErrorTypes.Find(name, run.Scope) == null)
                    throw Interpreter.Located(new KScriptException(message, "Retired"), run.Module, element.Span.Start);
            }
        }

        internal static IEnumerable<string> ErrorNames(string text) =>
            text.Trim().TrimStart('[').TrimEnd(']').Split(',', '|').Select(t => t.Trim()).Where(t => t.Length > 0);

        /// <summary>Builds the error type an &lt;error&gt; element declares.</summary>
        private static ErrorType DeclareError(ElementRun run)
        {
            string name = run.VariableName("name");
            if (!char.IsUpper(name[0])) throw run.Error($"Error type names start with a capital letter, so catch can tell them from variables: {char.ToUpperInvariant(name[0]) + name.Substring(1)}.", "name", "Syntax");
            if (ErrorTypes.BuiltIn.ContainsKey(name)) throw run.Error($"{name} is a built-in error type. Give yours another name, and extend it: <error name=\"My{name}\" extends=\"{name}\">.", "name", "Syntax");
            int? status = null;
            if (run.Raw("status") is string statusText)
            {
                if (!int.TryParse(statusText.Trim(), out int code) || code < 100 || code > 599)
                    throw run.Error($"status=\"{statusText}\" isn't an HTTP status. Use a number from 100 to 599, such as 404.", "status", "Syntax");
                status = code;
            }
            var fields = new RecordType(name, run.Raw("description"), ExtraKeys.Error, run.Scope, run.Module);
            Shapes.AddFields(run, fields);
            string reserved = fields.Fields.Select(f => f.Name).FirstOrDefault(f => f is "type" or "message" or "cause" or "status" or "line" or "column" or "file");
            if (reserved != null) throw run.Error($"'{reserved}' is part of every error, so <error name=\"{name}\"> can't have a field with that name.", "fields", "Syntax");
            string parent = run.Raw("extends")?.Trim();
            if (parent != null && parent.Length == 0) parent = null;
            var type = new ErrorType(name, parent ?? ErrorTypes.Root, status, run.Raw("description"), run.Element.GetAttribute("message")?.Value, fields, run.Scope, run.Module);
            // Check what it extends now when that is already declared; one declared further down is found on first use.
            if (parent != null && ErrorTypes.Find(parent, run.Scope) != null) _ = type.Parent;
            return type;
        }

        private static void RunFinally(ElementRun run, List<MarkupElement> finallies)
        {
            foreach (MarkupElement f in finallies) run.RunElementChildren(f, run.Scope.Child());
        }

        private static object Items(ElementRun run, MarkupElement parent)
        {
            List<MarkupElement> children = parent.ChildElements.ToList();
            foreach (MarkupElement c in children)
            {
                if (c.Name != "item") throw new KScriptException($"Only <item> can go inside <{parent.Name}>, not <{c.Name}>.", "Syntax") { Offset = c.Span.Start, OffsetModule = run.Module };
            }
            bool keyed = children.Any(c => c.GetAttribute("key") != null);
            if (keyed)
            {
                var map = new Dictionary<string, object>();
                foreach (MarkupElement c in children)
                {
                    string key = c.GetAttributeValue("key") ?? throw new KScriptException($"Every <item> in a map needs a key, or none of them should have one (for a list).", "Syntax") { Offset = c.Span.Start, OffsetModule = run.Module };
                    map[key] = ItemValue(run, c);
                }
                return map;
            }
            return children.Select(c => ItemValue(run, c)).ToList();
        }

        private static object ItemValue(ElementRun run, MarkupElement item)
        {
            if (item.ChildElements.Any()) return Items(run, item);
            if (item.GetAttribute("value")?.Value is DecodedText v) return run.Interpreter.EvaluateText(v, run.Scope, run.Module);
            DecodedText content = run.Interpreter.ContentOf(run.Module, item);
            if (content == null) return string.Empty;
            string text = TextLayout.Tidy(run.Interpreter.Interpolate(content, run.Scope, run.Module, bareReferences: true));
            return Values.TryNumber(text, out double n) ? n : text;
        }

        #endregion

        #region Commands

        private static readonly Random Rng = new Random();

        private static void RegisterCommands(PackageBuilder p)
        {
            p.Command("set_exchange_rate", "from: currency, to: currency, rate: number", "Sets a rate for this run. One unit of from buys rate units of to. Overrides the project's money.exchange_rates entry for this direction.", KType.Null, c =>
            {
                MoneyExchangeRates.Set(c.Host, Values.ToStr(c.Arg(0, "from")), Values.ToStr(c.Arg(1, "to")), c.Arg(2, "rate"));
                return null;
            }, "@set_exchange_rate(from: 'EUR', to: 'GBP', rate: 0.80)");
            p.Command("now", "", "The current date and time.", KType.Date, c => DateTime.Now, "@now()->format('d MMMM yyyy')");
            p.Command("today", "", "Today's date, at midnight.", KType.Date, c => DateTime.Today);
            p.Command("range", "start, end?, step?", "Numbers from start up to (not including) end; with one argument, from 0. start..end is the short form when the step is 1.", KType.List, c =>
            {
                double start = c.Count > 1 ? c.Num(0) : 0;
                double end = c.Count > 1 ? c.Num(1) : c.Num(0);
                double step = c.Num(2, end >= start ? 1 : -1);
                if (step == 0) throw c.Error("@range() can't have a step of 0.", "Argument");
                var list = new List<object>();
                for (double x = start; step > 0 ? x < end : x > end; x += step)
                {
                    list.Add(x);
                    if (list.Count > 10_000_000) throw c.Error("The range is too large.", "Limit");
                }
                return list;
            });
            p.Command("uuid", "v?: int, name?: string, namespace?: string", "A new UUID. Version 4 (the default) is random. Version 8 is custom: with a name it is a SHA-256 hash of the namespace and the name, so the same name always gives the same UUID; without one it starts with the time, so newer ones sort later.", KType.Uuid,
                c => UuidValue.New(c.Int(0, 4), c.Arg(1) == null ? null : c.Str(1), c.Arg(2) == null ? null : c.Str(2)), "@uuid().version");
            p.Command("min", "values...", "The smallest of the arguments (or of one list).", KType.Any, c => MinMax(c, min: true));
            p.Command("max", "values...", "The largest of the arguments (or of one list).", KType.Any, c => MinMax(c, min: false));
            p.Command("char_of", "code: number", "The character with this character code: @char_of(65) is A.", KType.String, c => char.ConvertFromUtf32(c.Int(0)));
            p.Command("env", "name, default?", "An environment variable, or default when it isn't set.", KType.String, c =>
            {
                string name = c.Str(0);
                return Environment.GetEnvironmentVariable(name)
                    ?? (c.Host.ConfigEnvironment?.TryGetValue(name, out string configured) == true ? configured : null)
                    ?? (c.Has(1) ? c.Arg(1) : null);
            });
        }

        private static object MinMax(CallContext c, bool min)
        {
            IEnumerable<object> items = c.Count == 1 && Values.Unwrap(c.Arg(0)) is List<object> list ? list : c.Args;
            List<object> all = items.ToList();
            if (all.Count == 0) return null;
            return all.Aggregate((a, b) => min ? (Values.Compare(a, b, "min") <= 0 ? a : b) : (Values.Compare(a, b, "max") >= 0 ? a : b));
        }

        private static void RegisterVariables(PackageBuilder p)
        {
            p.Variable("args", KType.List, "The command line arguments after the script path.", (i, m) => i.Host.Arguments.Cast<object>().ToList());
            p.Variable("env", KType.Map, "Environment variables: $env.HOME, $env['PATH'].", (i, m) =>
            {
                var map = new Dictionary<string, object>();
                if (i.Host.ConfigEnvironment != null)
                    foreach (var entry in i.Host.ConfigEnvironment) map[entry.Key] = entry.Value;
                foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) map[(string)e.Key] = (string)e.Value;
                return map;
            });
            p.Variable("os", KType.Map, "The computer: $os.name (windows, macos or linux), $os.user, $os.machine, $os.home, $os.temp, $os.separator, $os.newline.", (i, m) => new Dictionary<string, object>
            {
                ["name"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "other",
                ["user"] = Environment.UserName,
                ["machine"] = Environment.MachineName,
                ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ["temp"] = Path.GetTempPath(),
                ["separator"] = Path.DirectorySeparatorChar.ToString(),
                ["newline"] = Environment.NewLine,
            });
            p.Variable("script", KType.Map, "This script: $script.path, $script.dir, $script.name.", (i, m) => new Dictionary<string, object>
            {
                ["path"] = m.Path,
                ["dir"] = m.Directory,
                ["name"] = m.Path != null ? Path.GetFileName(m.Path) : null,
            });
        }

        #endregion
    }
}
