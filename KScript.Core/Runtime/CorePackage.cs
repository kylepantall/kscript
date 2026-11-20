using System.Globalization;

using KScript.Metadata;
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
        }

        #region Elements

        private static readonly A Test = new A("test", K.Expression, "The condition, for example `$n lt 10 and $name ne ''`.", true);

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
                new A("type", K.Enum, "Convert the value to this type, or fail early.", false, Values.DeclarableTypes),
                new A("const", K.Bool, "yes makes a later <set> an error."))
            {
                Example = "<let name=\"count\" value=\"0\" />\n<let name=\"people\">[{ name: 'Joan', age: 82 }]</let>\n<let>x = 3</let>",
            }, run => Assign(run, declare: true));

            p.Element(new ElementInfo("set", "Changes an existing variable. Fails if the variable was never declared (a typo guard) or is const. Without `name`, the text is assignment statements.",
                ContentKind.Expression,
                new A("name", K.VariableReference, "The variable to change."),
                new A("value", K.Expression, "The new value. Can be written as the element's text instead."),
                new A("op", K.Enum, "Update instead of replace: op=\"+=\" value=\"1\".", false, "=", "+=", "-=", "*=", "/=", "??="),
                new A("type", K.Enum, "Convert the value to this type, or fail.", false, Values.DeclarableTypes))
            {
                Example = "<set name=\"total\" value=\"$total + $price * $qty\" />\n<set>count += 1</set>",
            }, run => Assign(run, declare: false));

            p.Element(new ElementInfo("do", "Runs assignment statements and calls, separated by `;`.", ContentKind.AssignmentStatements)
            {
                Example = "<do>$x = 3; $total += $price * $qty</do>",
            }, run =>
            {
                run.RunStatements(declare: false);
                return null;
            });

            p.Element(new ElementInfo("echo", "Prints its text. `{ expr }` inserts a value; `$name` and `$name->method()` work directly.", ContentKind.Template,
                new A("value", K.Expression, "Print this value instead of the text."),
                new A("format", K.Enum, "How to print: text (the default), json (indented and, in a terminal, coloured), or markdown (with KScript.Terminal). A map or nested list on its own is printed as json.", false, "text", "json", "markdown"),
                new A("newline", K.Bool, "no leaves the cursor at the end of the line."))
            {
                Example = "<echo>Hello, $name! You have { $items->count() } items.</echo>",
            }, Echo);

            p.Element(new ElementInfo("if", "Runs its body when the test is true. Follow it with <elif> and <else> for other cases.", ContentKind.Statements, Test)
            {
                Example = "<if test=\"$score ge 90\">\n  <echo>A</echo>\n</if>\n<elif test=\"$score ge 75\">\n  <echo>B</echo>\n</elif>\n<else>\n  <echo>C</echo>\n</else>",
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
                if (run.Chain == null) throw run.Error("<elif> must come right after an <if> or <elif>.", null, "Syntax");
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
                if (run.Chain == null) throw run.Error("<else> must come right after an <if>, <elif> or <for>.", null, "Syntax");
                if (run.Chain == false) run.RunChildren();
                return null;
            });

            p.Element(new ElementInfo("for", "Loops over a list, a map or a range such as 0..10 (end exclusive) or 0..100 step 5. `$loop` holds index, first, last and count.", ContentKind.Statements,
                new A("each", K.DeclareVariables, "The loop variable, or `key, value` for a map.", true),
                new A("in", K.Expression, "What to loop over: a list, a map, a range, a number (0 up to it) or text (its characters).", true),
                new A("index", K.DeclareVariable, "A variable for the position, from 0."))
            {
                IsLoop = true,
                Example = "<for each=\"p\" in=\"$people\">\n  <echo>{$p.name}</echo>\n</for>\n<for each=\"i\" in=\"0..10\"><echo>$i</echo></for>\n<for each=\"key, value\" in=\"$config\"><echo>$key = $value</echo></for>",
            }, For);

            p.Element(new ElementInfo("while", "Loops while the test is true. `test=\"true\"` loops until <break/>.", ContentKind.Statements, Test)
            {
                IsLoop = true,
                Example = "<while test=\"$n lt 3\">\n  <set name=\"n\" value=\"$n + 1\" />\n</while>",
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
                Example = "<match value=\"$choice\">\n  <case is=\"1\"><echo>One</echo></case>\n  <case is=\"2, 3\"><echo>Two or three</echo></case>\n  <case when=\"$choice gt 9\"><echo>Big</echo></case>\n  <default><echo>Unknown</echo></default>\n</match>",
            }, Match);

            p.Element(new ElementInfo("case", "A branch of <match>: runs when the value equals one of `is`, or when `when` is true.", ContentKind.Statements,
                new A("is", K.ExpressionList, "Values to compare with, comma separated."),
                new A("when", K.Expression, "A condition."))
            {
                MustBeInside = new[] { "match" },
            }, run => throw run.Error("<case> must be directly inside <match>.", null, "Syntax"));

            p.Element(new ElementInfo("default", "The branch of <match> that runs when no case matched.", ContentKind.Statements)
            {
                MustBeInside = new[] { "match" },
            }, run => throw run.Error("<default> must be directly inside <match>.", null, "Syntax"));

            p.Element(new ElementInfo("fn", "Declares a function, called like a command: `@name(args)`, or piped: `$x |> @name()`. Functions can be called before the line that declares them.", ContentKind.Statements,
                new A("name", K.DeclareFunction, "The function's name.", true),
                new A("params", K.Parameters, "Parameters, with optional types and defaults: `who, greeting = 'Hello'` or `count: int = 1`."),
                new A("value", K.Expression, "For one-line functions: the result, instead of a body."),
                new A("description", K.Text, "What the function does. Shown in hover, and to a model when used as an <llm> tool."))
            {
                Example = "<fn name=\"greet\" params=\"who, greeting = 'Hello'\">\n  <return value=\"$greeting + ', ' + $who + '!'\" />\n</fn>\n<echo>@greet('World')</echo>",
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
                new A("fn", K.FunctionReference, "The function (or command) to call.", true),
                new A("args", K.ExpressionList, "Arguments, comma separated."),
                new A("to", K.DeclareVariable, "A variable for the result."))
            {
                Example = "<call fn=\"greet\" args=\"'World'\" />",
            }, Call);

            p.Element(new ElementInfo("spawn", "Runs a function in the background. The script waits for it before it ends.", ContentKind.Empty,
                new A("fn", K.FunctionReference, "The function to run.", true),
                new A("args", K.ExpressionList, "Arguments, comma separated."))
            {
                Example = "<spawn fn=\"download_all\" args=\"$urls\" />",
            }, run =>
            {
                ICallable fn = FindFunction(run, run.RequiredRaw("fn"));
                run.Interpreter.Spawn(fn, run.Has("args") ? run.EvalList("args") : new List<object>(), run.Module);
                return null;
            });

            p.Element(new ElementInfo("try", "Runs its body, handing errors to its <catch> elements. <finally> always runs.", ContentKind.Statements)
            {
                Example = "<try>\n  <let name=\"data\" value=\"@json($text)\" />\n  <catch as=\"err\" when=\"$err.type eq 'Format'\">\n    <echo>That isn't JSON: {$err.message}</echo>\n  </catch>\n  <finally><echo>done</echo></finally>\n</try>",
            }, Try);

            p.Element(new ElementInfo("catch", "Handles an error thrown in the <try> around it. `as` names the error: $err.type, $err.message, $err.line.", ContentKind.Statements,
                new A("as", K.DeclareVariable, "A variable for the error."),
                new A("when", K.Expression, "Only handle errors for which this is true, such as `$err.type eq 'FileNotFound'`."),
                new A("type", K.Text, "Only handle errors of this type (comma separated)."))
            {
                MustBeInside = new[] { "try" },
            }, run => null);

            p.Element(new ElementInfo("finally", "Runs after the <try> body and any <catch>, whether or not there was an error.", ContentKind.Statements)
            {
                MustBeInside = new[] { "try" },
            }, run => null);

            p.Element(new ElementInfo("throw", "Raises an error, which a <catch> can handle. Without a <try>, the script stops with the message.", ContentKind.Template,
                new A("type", K.Text, "The error type, such as Validation. Defaults to Error."),
                new A("message", K.Template, "What went wrong. Can be written as the element's text instead.") { SatisfiedByContent = true })
            {
                Example = "<throw type=\"Validation\" message=\"Age must be positive, got {$age}\" />",
            }, run =>
            {
                string message = run.Has("message") ? run.Template("message") : run.Text();
                throw run.Error(string.IsNullOrWhiteSpace(message) ? "Error" : message, null, run.Raw("type")?.Trim() ?? "Error");
            });

            p.Element(new ElementInfo("import", "Imports a package (`package=\"KScript.Terminal\"`), or the top-level <fn> functions and <let const> values of another script (`src`). Goes at the top of the script.", ContentKind.Empty,
                new A("package", K.Text, "A package such as KScript.Terminal, KScript.Math, KScript.IO or KScript.Net."),
                new A("src", K.Path, "A script to import, relative to this file."),
                new A("as", K.DeclareVariable, "Use the import under a prefix: `@math.sqrt(2)`, `str.slugify()`."),
                new A("names", K.FunctionReferences, "Import only these names from a script, callable directly: `@slugify()`."))
            {
                Example = "<import package=\"KScript.Terminal\" />\n<import package=\"KScript.Math\" as=\"math\" />\n<import src=\"lib/strings.ks.xml\" as=\"str\" />",
            }, run =>
            {
                if (!run.Has("package") && !run.Has("src")) throw run.Error("<import> needs package=\"...\" (such as KScript.Terminal) or src=\"...\" (a script file).", null, "Syntax");
                if (run.Has("src")) run.Interpreter.ImportScript(run.Element, run.Scope, run.Module);
                return null;
            });

            p.Element(new ElementInfo("exit", "Ends the script, with an exit code for the shell (0 means success).", ContentKind.Empty,
                new A("code", K.Expression, "The exit code. Defaults to 0."))
            {
                Example = "<exit code=\"1\" />",
            }, run => throw new ExitSignal((int)run.Number("code", 0)));

            p.Element(new ElementInfo("wait", "Pauses the script.", ContentKind.Empty,
                new A("ms", K.Expression, "How long to wait, in milliseconds."),
                new A("seconds", K.Expression, "How long to wait, in seconds."))
            {
                Example = "<wait seconds=\"1.5\" />",
            }, run =>
            {
                double ms = run.Has("seconds") ? run.Number("seconds", 0) * 1000 : run.Number("ms", 0);
                if (ms > 0) Task.Delay(TimeSpan.FromMilliseconds(ms), run.Interpreter.Options.Cancellation).GetAwaiter().GetResult();
                return null;
            });

            p.Element(new ElementInfo("marray", "Builds a list or map from nested <item> elements, for data that reads better as markup than as a literal.", ContentKind.Statements,
                new A("name", K.DeclareVariable, "The variable to declare.", true),
                new A("const", K.Bool, "yes makes a later <set> an error."))
            {
                Example = "<marray name=\"person\">\n  <item key=\"name\">Joan</item>\n  <item key=\"age\">82</item>\n</marray>",
            }, run =>
            {
                object value = Items(run, run.Element);
                run.Scope.Declare(run.VariableName("name"), value, run.Flag("const"));
                return null;
            });

            p.Element(new ElementInfo("item", "An entry of <marray>. With `key` it is a map entry, without it a list item. Its text is the value (numbers become numbers); nested items make a list or map.", ContentKind.Template,
                new A("key", K.Text, "The key in the enclosing map."),
                new A("value", K.Expression, "The value as an expression, instead of text."))
            {
                MustBeInside = new[] { "marray", "item" },
            }, run => throw run.Error("<item> must be inside <marray>.", null, "Syntax"), ownsContent: true);
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
            object value = run.Has("value") ? run.Eval("value") : run.EvalContent();
            string type = run.Raw("type");
            if (type != null) value = Values.ConvertTo(value, type, "$" + name);

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
                        ? $"<echo format=\"{format}\"> needs {owner.Name}. Add <import package=\"{owner.Name}\" /> at the top of the script."
                        : $"Unknown echo format '{format}'.", "format", "Import");
                }
                text = formatter(text, run.Interpreter);
            }

            if (run.Flag("newline", true)) run.Host.WriteLine(text);
            else run.Host.Write(text);
            return null;
        }

        private static bool? For(ElementRun run)
        {
            string[] names = run.RequiredRaw("each").Split(',').Select(n => n.Trim().TrimStart('$')).Where(n => n.Length > 0).ToArray();
            if (names.Length == 0 || names.Length > 2) throw run.Error("each=\"...\" names the loop variable (each=\"item\"), or a key and value for a map (each=\"key, value\").", "each", "Syntax");
            string indexName = run.VariableName("index", required: false);
            object source = run.Eval("in");

            List<(object Item, object Key)> items = source switch
            {
                null => new List<(object, object)>(),
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
                    if (c.GetAttribute("is")?.Value is DecodedText isText)
                    {
                        hit = run.Interpreter.EvaluateList(isText, scope, run.Module).Any(o => o is List<object> options ? options.Any(x => Values.LooseEquals(x, value)) : Values.LooseEquals(o, value));
                    }
                    else if (c.GetAttribute("when")?.Value is DecodedText whenText)
                    {
                        hit = Values.Truthy(run.Interpreter.EvaluateText(whenText, scope, run.Module));
                    }
                    else
                    {
                        throw new KScriptException("<case> needs is=\"...\" or when=\"...\".", "Syntax") { Offset = c.Span.Start, OffsetModule = run.Module };
                    }
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
            if (run.Scope.TryGet(name, out object v) && v is ICallable fn) return fn;
            if (run.Module.Commands.TryGetValue(name, out CommandDefinition cmd))
                return new NativeFunction(cmd.Name, call => run.Interpreter.InvokeCommand(cmd, call));
            int dot = name.IndexOf('.');
            if (dot > 0 && run.Module.Aliases.TryGetValue(name.Substring(0, dot), out ImportNamespace ns) && ns.TryGet(name.Substring(dot + 1), out object member))
            {
                if (member is ICallable c) return c;
                if (member is CommandDefinition d) return new NativeFunction(d.Name, call => run.Interpreter.InvokeCommand(d, call));
            }
            string close = Values.Closest(name, run.Scope.Names().Where(n => run.Scope.TryGet(n, out object f) && f is ICallable));
            throw run.Error($"There is no function called {name}." + (close != null ? $" Did you mean {close}?" : " Declare it with <fn name=\"" + name + "\">."), "fn", "Name");
        }

        private static bool? Call(ElementRun run)
        {
            ICallable fn = FindFunction(run, run.RequiredRaw("fn"));
            List<object> args = run.Has("args") ? run.EvalList("args") : new List<object>();
            object result = run.Interpreter.Call(fn, args, null, run.Scope, run.Module);
            if (run.Has("to")) run.Scope.Declare(run.VariableName("to"), result);
            return null;
        }

        private static bool? Try(ElementRun run)
        {
            List<MarkupElement> catches = run.ChildElements.Where(e => e.Name == "catch").ToList();
            List<MarkupElement> finallies = run.ChildElements.Where(e => e.Name == "finally").ToList();
            List<MarkupNode> body = run.Element.Children.Where(n => !(n is MarkupElement e && (e.Name == "catch" || e.Name == "finally"))).ToList();

            try
            {
                run.Run(body, run.Scope.Child());
            }
            catch (KScriptException ex)
            {
                Interpreter.Located(ex, run.Module, run.Element.Span.Start);
                bool handled = false;
                foreach (MarkupElement c in catches)
                {
                    Scope scope = run.Scope.Child();
                    string name = c.GetAttributeValue("as")?.Trim().TrimStart('$') ?? "err";
                    scope.Declare(name, ex.ToValue());
                    if (c.GetAttributeValue("type") is string types && !types.Split(',').Any(t => string.Equals(t.Trim(), ex.Type, StringComparison.OrdinalIgnoreCase))) continue;
                    if (c.GetAttribute("when")?.Value is DecodedText when && !Values.Truthy(run.Interpreter.EvaluateText(when, scope, run.Module))) continue;
                    handled = true;
                    try
                    {
                        run.RunElementChildren(c, scope);
                    }
                    finally
                    {
                        if (finallies.Count > 0) RunFinally(run, finallies);
                    }
                    break;
                }
                if (!handled)
                {
                    if (finallies.Count > 0) RunFinally(run, finallies);
                    throw;
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
                    string key = c.GetAttributeValue("key") ?? throw new KScriptException("Every <item> in a map needs a key, or none of them should have one (for a list).", "Syntax") { Offset = c.Span.Start, OffsetModule = run.Module };
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
            p.Command("json", "text", "Parses JSON text into a value.", KType.Any, c => Values.ParseJson(c.Str(0)), "@json('{ \"a\": 1 }').a");
            p.Command("to_json", "value, indented?", "A value as JSON text.", KType.String, c => Values.ToJson(c.Arg(0), c.Bool(1)));
            p.Command("now", "", "The current date and time.", KType.Date, c => DateTime.Now, "@now()->format('d MMMM yyyy')");
            p.Command("today", "", "Today's date, at midnight.", KType.Date, c => DateTime.Today);
            p.Command("date", "text", "Parses a date such as 2026-11-19 or 2026-11-19 14:30.", KType.Date, c =>
                DateTime.TryParse(c.Str(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime d) ? d : throw c.Error($"'{c.Str(0)}' is not a date.", "Format"));
            p.Command("range", "start, end?, step?", "Numbers from start up to (not including) end; with one argument, from 0. The same as start..end.", KType.List, c =>
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
            p.Command("guid", "", "A new random GUID.", KType.String, c => Guid.NewGuid().ToString());
            p.Command("number", "value, default?", "Converts a value to a number (or returns default when it isn't one).", KType.Number, c =>
                Values.TryNumber(c.Arg(0), out double n) ? n : c.Has(1) ? c.Arg(1) : throw c.Error($"{Values.Describe(c.Arg(0))} is not a number.", "Type"));
            p.Command("string", "value", "Converts a value to text.", KType.String, c => Values.ToStr(c.Arg(0)));
            p.Command("type", "value", "The type of a value: string, number, bool, list, map, fn, date or null.", KType.String, c => Values.TypeOf(c.Arg(0)));
            p.Command("min", "values...", "The smallest of the arguments (or of one list).", KType.Any, c => MinMax(c, min: true));
            p.Command("max", "values...", "The largest of the arguments (or of one list).", KType.Any, c => MinMax(c, min: false));
            p.Command("to_lower", "text", "The text in lower case.", KType.String, c => c.Str(0).ToLowerInvariant());
            p.Command("to_upper", "text", "The text in upper case.", KType.String, c => c.Str(0).ToUpperInvariant());
            p.Command("char_of", "code", "The character with this character code: @char_of(65) is A.", KType.String, c => char.ConvertFromUtf32(c.Int(0)));
            p.Command("escape", "text", "HTML-escapes text.", KType.String, c => Values.HtmlEscape(c.Str(0)));
            p.Command("env", "name, default?", "An environment variable, or default when it isn't set.", KType.String, c =>
                Environment.GetEnvironmentVariable(c.Str(0)) ?? (c.Has(1) ? c.Arg(1) : null));
            p.Command("exit", "code?", "Ends the script with an exit code.", KType.Null, c => throw new ExitSignal(c.Int(0, 0)));
            p.Command("error", "message, type?", "Raises an error, like <throw>, from inside an expression.", KType.Null, c => throw new KScriptException(c.Str(0), c.Str(1, "Error")));
        }

        private static object MinMax(CallContext c, bool min)
        {
            IEnumerable<object> items = c.Count == 1 && c.Arg(0) is List<object> list ? list : c.Args;
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
