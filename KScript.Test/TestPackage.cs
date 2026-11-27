using System.Text.RegularExpressions;

using KScript.Metadata;
using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Testing
{
    /// <summary>
    /// KScript.Test: tests for scripts, run with <c>kscript test</c>. Test files (<c>name.test.ks.xml</c>) import it
    /// without an &lt;import&gt;. &lt;test&gt; and &lt;describe&gt; declare tests; @assert(), &lt;expect&gt; and
    /// &lt;expect_error&gt; check results; &lt;capture&gt; and <c>@run_script()</c> collect what a script prints.
    /// </summary>
    public sealed class TestPackage : KScriptPackage
    {
        public override string Name => "KScript.Test";

        public override string Description => "Tests run by kscript test: <test>, <describe>, <expect>, <expect_error>, <capture> and @assert().";

        /// <summary>The error type of a failed check, as <c>&lt;catch when="$err.type eq 'Assertion'"&gt;</c> sees it.</summary>
        public const string AssertionType = "Assertion";

        /// <summary>The attributes of &lt;expect&gt; that say what to check, one per element.</summary>
        internal static readonly string[] Matchers = { "equals", "not_equals", "same", "contains", "not_contains", "matches", "type", "count", "count_at_most", "throws" };

        protected override void Register(PackageBuilder p)
        {
            var skip = new A("skip", K.Template, "Skip it: skip=\"true\", or a reason such as skip=\"needs a network\".");
            var only = new A("only", K.Bool, "true runs only the tests marked only in this file, while you work on them.");

            p.Element(new ElementInfo("test", "A test. Its body runs on its own when kscript test runs the file, and it passes if nothing in it fails. Inside a <for> loop it is one test per item.", ContentKind.Statements,
                new A("name", K.Template, "What the test checks, shown in the results. In a <for> loop, `{ }` can use the loop's variables.", true),
                skip, only,
                new A("timeout", K.Expression, "Fail the test if it runs longer than this: 30s, 2m (the default is 10s, or test.timeout in kscript.json). A plain number is seconds.") { Type = Duration.TypeName })
            {
                Context = ElementContext.Script,
                Example = "<fn name=\"add\" params=\"a, b\" value=\"$a + $b\" />\n<test name=\"adds two numbers\">\n  <expect value=\"@add(2, 3)\" equals=\"5\" />\n</test>\n<for each=\"n\" in=\"[1, 2, 3]\">\n  <test name=\"doubles {$n}\">\n    <expect value=\"@add($n, $n)\" equals=\"$n * 2\" />\n  </test>\n</for>",
            }, Test);

            p.Element(new ElementInfo("describe", "A group of tests. Its name goes in front of each test's name, and its <before_each> and <after_each> apply to every test inside.", ContentKind.Statements,
                new A("name", K.Template, "What the group is about.", true),
                skip, only)
            {
                Context = ElementContext.Script,
                Example = "<describe name=\"lists\">\n  <test name=\"push adds to the end\">\n    <expect value=\"[1]->push(2)\" equals=\"[1, 2]\" />\n  </test>\n</describe>",
            }, Describe);

            p.Element(new ElementInfo("before_each", "Runs before every test in its file or <describe>, in the test's own scope. Use it to reset shared variables with <set>.", ContentKind.Statements)
            {
                Context = ElementContext.Script,
                Example = "<let name=\"cart\" value=\"[]\" />\n<before_each>\n  <set name=\"cart\" value=\"[]\" />\n</before_each>",
            }, run => Hook(run, before: true));

            p.Element(new ElementInfo("after_each", "Runs after every test in its file or <describe>, even when the test fails. Use it to tidy up files a test made.", ContentKind.Statements)
            {
                Context = ElementContext.Script,
                Example = "<after_each>\n  <set name=\"cart\" value=\"[]\" />\n</after_each>",
            }, run => Hook(run, before: false));

            p.Element(new ElementInfo("expect", "Checks a value and fails the test if the check doesn't hold. Give the value and one check, such as equals=\"5\".", ContentKind.Empty,
                new A("value", K.Expression, "The value to check.", true),
                new A("equals", K.Expression, "It equals this (== for text and numbers, item by item for lists and maps)."),
                new A("not_equals", K.Expression, "It doesn't equal this."),
                new A("same", K.Expression, "It is strictly equal (===): same type and value."),
                new A("contains", K.Expression, "A list has this item, text has this text, or a map has this key."),
                new A("not_contains", K.Expression, "A list doesn't have this item, text doesn't have this text, or a map doesn't have this key."),
                new A("matches", K.Expression, "Text matches this regular expression, such as '^\\d+$'."),
                new A("type", K.Enum, "Its type is this.", false, "string", "number", "bool", "list", "map", "date", "fn", "error", "null"),
                new A("count", K.Expression, "A list, map or text has this many items or characters."),
                new A("count_at_most", K.Expression, "A list, map or text has at most this many items or characters: `expect(@queries(), count_at_most: 4)`."),
                new A("throws", K.ErrorType, "Working out the value stops with an error of this type, or one that extends it: throws=\"NotFound\"."),
                new A("message", K.Template, "What to say when it fails, instead of the usual message."))
            {
                Context = ElementContext.Script,
                Example = "<expect value=\"2 + 3\" equals=\"5\" />\n<expect value=\"$names\" contains=\"'Joan'\" />\n<expect value=\"$id\" matches=\"'^[a-z0-9-]+$'\" />\n<expect value=\"'nope'->json()\" throws=\"Json\" />",
            }, Expect);

            p.Element(new ElementInfo("expect_error", "Fails the test unless its body stops with an error. `type` and `contains` check which error; `as` keeps it for more checks.", ContentKind.Statements,
                new A("type", K.ErrorType, "The error type it should be (or a type that extends it), such as Validation, FileNotFound or an <error>."),
                new A("contains", K.Template, "Text the error message should contain."),
                new A("as", K.DeclareVariable, "A variable for the error (type, message, line, and the fields of <throw>), declared after the element."))
            {
                Context = ElementContext.Script,
                Example = "<expect_error type=\"Validation\" contains=\"positive\">\n  <call fn=\"set_age\" args=\"-1\" />\n</expect_error>",
            }, ExpectError);

            p.Element(new ElementInfo("capture", "Runs its body and keeps what it prints in a variable instead of showing it. `input` gives it lines to read.", ContentKind.Statements,
                new A("into", K.DeclareVariable, "The variable that receives the output (line endings are \\n, without the last one).", true),
                new A("input", K.Expression, "What <input> and other prompts read: text, or a list of lines."))
            {
                Context = ElementContext.Script,
                Example = "<capture into=\"out\">\n  <call fn=\"greet\" args=\"'Joan'\" />\n</capture>\n<expect value=\"$out\" equals=\"'Hello, Joan!'\" />",
            }, Capture);

            TestHelpers.Register(p, AssertionType);

            p.Command("fail", "message?", "Fails the test now.", KType.Null, c =>
                throw new KScriptException(c.Has(0) ? c.Str(0) : "@fail() was called.", AssertionType));
            p.Examples(
                ("@with_clock", "<test name=\"a new year starts on 1 January\">\n  <let name=\"year\" value=\"@with_clock('2027-01-01'->to_date(), fn() => @today().year)\" />\n  <expect value=\"$year\" equals=\"2027\" />\n</test>"),
                ("@with_environment", "<test name=\"reads the region from the environment\">\n  <let name=\"region\" value=\"@with_environment({ REGION: 'eu-west' }, fn() => @env('REGION'))\" />\n  <expect value=\"$region\" equals=\"'eu-west'\" />\n</test>"),
                ("@assert_response", "<test name=\"the API lists people\">\n  <do>@assert_response(@api('http://localhost:8080/api/people'), status: 200, headers: { 'Content-Type': 'application/json' })</do>\n</test>"),
                ("@fail", "<test name=\"every price is positive\">\n  <for each=\"price\" in=\"[3, 5, 8]\">\n    <if $price le 0>\n      <do>@fail('Found a price of {$price}')</do>\n    </if>\n  </for>\n</test>"));

            p.Command("assert", "condition, message?", "Fails the test unless the condition is true. A failed comparison shows the value of each side. message is what to say instead.", KType.Bool, c =>
            {
                if (Values.Truthy(c.Arg(0))) return true;
                throw new KScriptException(c.Has(1) && c.Arg(1) != null ? c.Str(1) : ExplainFalse(c), AssertionType);
            }, "<do>@assert(2 + 3 eq 5)</do>\n<do>@assert([1, 2]->count() gt 0, 'The cart should not be empty')</do>");

            p.Command(new CallableInfo(CallableKind.Command, "run_script", "Runs another script on its own and returns what happened: { output, code, error }. `output` is what it printed, `code` its exit code (3 when it stopped with an error) and `error` the error, or null.",
                new[] { Signature.Parse("path, options?") })
            {
                ReturnType = KType.Map,
                Example = "<let name=\"result\" value=\"@run_script('../scripts/hello.ks.xml', { args: ['Joan'], input: ['yes'] })\" />\n<expect value=\"$result.output\" contains=\"'Hello, Joan'\" />\n<expect value=\"$result.code\" equals=\"0\" />",
            }, RunScript);
        }

        #region Collecting tests

        private static TestSession Session(ElementRun run)
        {
            TestSession session = TestSession.Of(run.Host);
            if (session == null)
                throw run.Error($"<{run.Name}> only works in a test file run by kscript test. Run: kscript test {(run.Module.Path != null ? Path.GetFileName(run.Module.Path) : "your-file.test.kscript")}", null, "Syntax");
            if (session.Running != null)
                throw run.Error($"<{run.Name}> can't be inside a test. Put it at the top of the file or in a <describe>.", null, "Syntax");
            return session;
        }

        /// <summary>skip="yes" gives "", skip="no" gives null, and any other text is the reason.</summary>
        private static string SkipReason(ElementRun run)
        {
            if (!run.Has("skip")) return null;
            string text = run.Template("skip")?.Trim() ?? string.Empty;
            return Values.ParseBool(text) switch
            {
                true => string.Empty,
                false => null,
                _ => text,
            };
        }

        private static bool? Describe(ElementRun run)
        {
            TestSession session = Session(run);
            var group = new TestGroup(run.RequiredTemplate("name"), session.Current)
            {
                Skip = SkipReason(run) ?? session.Current.Skip,
                Only = run.Flag("only") || session.Current.Only,
            };
            session.Current = group;
            try
            {
                run.RunChildren();
            }
            finally
            {
                session.Current = group.Parent;
            }
            return null;
        }

        private static bool? Hook(ElementRun run, bool before)
        {
            TestSession session = Session(run);
            var hook = new TestHook { Element = run.Element, Module = run.Module };
            (before ? session.Current.BeforeEach : session.Current.AfterEach).Add(hook);
            return null;
        }

        private static bool? Test(ElementRun run)
        {
            TestSession session = Session(run);
            TestGroup group = session.Current;
            string skip = SkipReason(run) ?? group.Skip;
            bool only = run.Flag("only") || group.Only;
            double? timeout = run.Seconds("timeout");
            int line = run.Module.PositionOf(run.Element.Span.Start).Line;
            List<string> groups = group.Chain().Where(g => g.Name != null).Select(g => g.Name).ToList();

            void Add(Scope scope, string name) => session.Tests.Add(new TestCase
            {
                Name = name,
                Groups = groups,
                Skip = skip,
                Only = only,
                Timeout = timeout,
                Line = line,
                Element = run.Element,
                Scope = scope,
                Module = run.Module,
                Group = group,
            });

            // Inside a for loop the element runs once per item, so each run adds a test with the item in its scope.
            Add(run.Scope, run.RequiredTemplate("name"));
            return null;
        }

        #endregion

        #region Checks

        private static KScriptException Failure(ElementRun run, Wording message, string attribute = null) =>
            run.Error(run.Template("message") is string custom && custom.Length > 0 ? custom : message, attribute, AssertionType);

        private static readonly HashSet<string> Comparisons = new HashSet<string>(StringComparer.Ordinal) { "==", "!=", "===", "!==", "<", "<=", ">", ">=", "in" };

        /// <summary>"Expected $total eq 30 to be true.", then the value of each side of a comparison.</summary>
        private static string ExplainFalse(CallContext c)
        {
            ExprNode condition = c.Site?.Argument(0);
            string source = c.Site?.TextOf(condition);
            if (source == null) return $"@assert() got {Show(c.Arg(0))}, which isn't true.";
            var lines = new List<string> { $"Expected {source} to be true." };

            IEnumerable<ExprNode> sides = condition is BinaryNode b && Comparisons.Contains(b.Operator) ? new[] { b.Left, b.Right }
                : condition is UnaryNode u ? new[] { u.Operand }
                : condition is BinaryNode logical && (logical.Operator == "and" || logical.Operator == "&&" || logical.Operator == "or" || logical.Operator == "||") ? new[] { logical.Left, logical.Right }
                : new[] { condition };
            foreach (ExprNode side in sides)
            {
                if (side == null || side is LiteralNode) continue;
                string what = c.Site.TextOf(side);
                object value;
                try
                {
                    value = c.Interpreter.Evaluate(side, c.Scope, c.Site.Source);
                }
                catch (KScriptException ex)
                {
                    lines.Add($"  {what} fails: {ex.Message}");
                    continue;
                }
                lines.Add($"  {what} is {Show(value)}");
            }
            return string.Join("\n", lines);
        }

        private static bool? Expect(ElementRun run)
        {
            string[] given = Matchers.Where(run.Has).ToArray();
            if (given.Length != 1)
                throw run.Error(given.Length == 0
                    ? Wording.Of($"<expect> needs one check, such as equals=\"5\". Checks: {string.Join(", ", Matchers)}.")
                    : Wording.Of($"<expect> takes one check, but has {string.Join(" and ", given)}. Write one <expect> for each."), given.Length > 1 ? given[1] : null, "Syntax");
            string matcher = given[0];
            if (matcher == "throws") return ExpectThrows(run);
            object actual = run.Eval("value");
            string subject = run.Raw("value").Trim();
            string got = $"but it was {Show(actual)}";

            switch (matcher)
            {
                case "equals":
                {
                    object expected = run.Eval("equals");
                    if (!Values.LooseEquals(actual, expected)) throw Failure(run, $"Expected {subject} to equal {Show(expected)}, {got}.{Difference(actual, expected)}", "value");
                    break;
                }
                case "not_equals":
                {
                    object expected = run.Eval("not_equals");
                    if (Values.LooseEquals(actual, expected)) throw Failure(run, $"Expected {subject} not to equal {Show(expected)}.", "value");
                    break;
                }
                case "same":
                {
                    object expected = run.Eval("same");
                    if (!Values.StrictEquals(actual, expected))
                        throw Failure(run, $"Expected {subject} to be {Show(expected)} ({Values.TypeOf(expected)}), {got} ({Values.TypeOf(actual)}).", "value");
                    break;
                }
                case "contains":
                case "not_contains":
                {
                    object item = run.Eval(matcher);
                    bool has = Contains(run, actual, item, matcher);
                    if (matcher == "contains" && !has) throw Failure(run, $"Expected {subject} to contain {Show(item)}, {got}.", "value");
                    if (matcher == "not_contains" && has) throw Failure(run, $"Expected {subject} not to contain {Show(item)}, {got}.", "value");
                    break;
                }
                case "matches":
                {
                    string pattern = Values.ToStr(run.Eval("matches"));
                    Regex regex;
                    try
                    {
                        regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
                    }
                    catch (ArgumentException ex)
                    {
                        throw run.Error($"matches=\"...\" isn't a valid regular expression: {ex.Message}", "matches", "Syntax");
                    }
                    if (actual is not string s || !regex.IsMatch(s)) throw Failure(run, $"Expected {subject} to match /{pattern}/, {got}.", "value");
                    break;
                }
                case "type":
                {
                    string expected = run.Raw("type").Trim();
                    string type = actual is ErrorValue ? "error" : Values.TypeOf(actual);
                    if (!string.Equals(type, expected, StringComparison.OrdinalIgnoreCase)) throw Failure(run, $"Expected {subject} to be a {expected}, but it was {Show(actual)}, a {type}.", "value");
                    break;
                }
                case "count":
                case "count_at_most":
                {
                    bool most = matcher == "count_at_most";
                    double expected = Values.ToNumber(run.Eval(matcher), run.Module.ArgumentHint("expect", matcher));
                    int? count = actual switch
                    {
                        List<object> l => l.Count,
                        Dictionary<string, object> m => m.Count,
                        string s => s.Length,
                        _ => null,
                    };
                    if (count == null) throw Failure(run, $"Expected {subject} to have {Values.FormatNumber(expected)} items, but it was {Show(actual)}, which has no count.", "value");
                    string unit = actual is string ? "characters" : "items";
                    if (most && count > expected) throw Failure(run, $"Expected {subject} to have at most {Values.FormatNumber(expected)} {unit}, but it has {count}: {Show(actual)}.", "value");
                    if (!most && count != expected) throw Failure(run, $"Expected {subject} to have {Values.FormatNumber(expected)} {unit}, but it has {count}: {Show(actual)}.", "value");
                    break;
                }
            }
            return null;
        }

        private static bool Contains(ElementRun run, object container, object item, string matcher) => container switch
        {
            List<object> list => list.Any(x => Values.LooseEquals(x, item)),
            string s => s.Contains(Values.ToStr(item), StringComparison.Ordinal),
            Dictionary<string, object> map => map.ContainsKey(Values.ToStr(item)),
            _ => throw Failure(run, $"{matcher}=\"...\" checks a list, text or a map, but {run.Raw("value").Trim()} is {Show(container)}.", "value"),
        };

        /// <summary>For lists and maps, the first place they differ.</summary>
        private static string Difference(object actual, object expected)
        {
            if (actual is List<object> a && expected is List<object> e)
            {
                for (int i = 0; i < Math.Max(a.Count, e.Count); i++)
                {
                    if (i >= a.Count) return $"\n  It is missing item {i}: {Show(e[i])}";
                    if (i >= e.Count) return $"\n  It has an extra item {i}: {Show(a[i])}";
                    if (!Values.LooseEquals(a[i], e[i])) return $"\n  Item {i} is {Show(a[i])}, not {Show(e[i])}";
                }
            }
            if (actual is Dictionary<string, object> am && expected is Dictionary<string, object> em)
            {
                foreach (var kv in em)
                {
                    if (!am.TryGetValue(kv.Key, out object v)) return $"\n  It has no '{kv.Key}' (expected {Show(kv.Value)})";
                    if (!Values.LooseEquals(v, kv.Value)) return $"\n  '{kv.Key}' is {Show(v)}, not {Show(kv.Value)}";
                }
                string extra = am.Keys.FirstOrDefault(k => !em.ContainsKey(k));
                if (extra != null) return $"\n  It has an extra '{extra}': {Show(am[extra])}";
            }
            return string.Empty;
        }

        private static bool? ExpectError(ElementRun run)
        {
            string type = run.Raw("type")?.Trim();
            string contains = run.Template("contains");
            string asName = run.VariableName("as", required: false);
            KScriptException caught = null;
            try
            {
                run.RunChildren();
            }
            catch (KScriptException ex) when (ex.Type != "Cancelled" && (ex.Type != AssertionType || string.Equals(type, AssertionType, StringComparison.OrdinalIgnoreCase)))
            {
                caught = ex;
            }

            string wanted = string.IsNullOrEmpty(type) ? "an error" : $"{Article(type)} {type} error";
            if (caught == null) throw Failure(run, $"Expected {wanted}, but the body ran without one.");
            if (!string.IsNullOrEmpty(type) && !ErrorTypes.Matches(caught, type))
                throw Failure(run, $"Expected {wanted}, but got {caught.Type}: {caught.Message}");
            if (!string.IsNullOrEmpty(contains) && !caught.Message.Contains(contains, StringComparison.Ordinal))
                throw Failure(run, $"Expected the error message to contain '{contains}', but it was: {caught.Message}");
            if (asName != null) run.Scope.Declare(asName, caught.ToValue());
            return null;
        }

        /// <summary><c>&lt;expect value="@find(99)" throws="NotFound" /&gt;</c>: working out the value must fail with that type.</summary>
        private static bool? ExpectThrows(ElementRun run)
        {
            string type = run.Raw("throws").Trim();
            string subject = run.Raw("value").Trim();
            object actual;
            try
            {
                actual = run.Eval("value");
            }
            catch (KScriptException ex) when (ex.Type != "Cancelled" && (ex.Type != AssertionType || string.Equals(type, AssertionType, StringComparison.OrdinalIgnoreCase)))
            {
                if (!ErrorTypes.Matches(ex, type)) throw Failure(run, $"Expected {subject} to throw {Article(type)} {type} error, but it threw {ex.Type}: {ex.Message}", "throws");
                return null;
            }
            throw Failure(run, $"Expected {subject} to throw {Article(type)} {type} error, but it was {Show(actual)}.", "value");
        }

        private static string Article(string word) => "AEIOU".Contains(char.ToUpperInvariant(word[0])) ? "an" : "a";

        private static bool? Capture(ElementRun run)
        {
            string name = run.VariableName("into");
            TextReader input = run.Has("input") ? InputReader(run.Eval("input")) : null;
            var output = new StringWriter();
            using (run.Host.CaptureOutput(output))
            using (input != null ? run.Host.ProvideInput(input) : null)
            {
                run.RunChildren();
            }
            run.Scope.Declare(name, Tidy(output.ToString()));
            return null;
        }

        private static TextReader InputReader(object input) => new StringReader(input switch
        {
            null => string.Empty,
            List<object> lines => string.Join("\n", lines.Select(Values.ToStr)) + "\n",
            _ => Values.ToStr(input),
        });

        /// <summary>\n line endings, without the newline at the end.</summary>
        internal static string Tidy(string output) => output.Replace("\r\n", "\n").TrimEnd('\n');

        #endregion

        #region @run_script

        private static object RunScript(CallContext c)
        {
            string path = c.Str(0);
            string full = Path.GetFullPath(Path.Combine(c.Module?.Directory ?? c.Host.WorkingDirectory, path));
            var options = c.Has(1) && c.Arg(1) is Dictionary<string, object> map ? map : new Dictionary<string, object>();
            if (c.Has(1) && c.Arg(1) is not Dictionary<string, object>)
                throw new KScriptException($"@run_script() takes its options as a map, such as {{ args: ['a'], input: 'yes' }}, but got {Values.Describe(c.Arg(1))}.", "Argument");
            foreach (string key in options.Keys)
            {
                if (key != "args" && key != "input")
                    throw new KScriptException($"@run_script() has no option '{key}'. Options: args, input.", "Argument");
            }

            var output = new StringWriter();
            var host = new KScriptHost(output, output, InputReader(options.TryGetValue("input", out object input) ? input : null))
            {
                Arguments = options.TryGetValue("args", out object args) && args is List<object> list ? list.Select(Values.ToStr).ToList()
                    : args == null ? Array.Empty<string>() : new[] { Values.ToStr(args) },
                WorkingDirectory = Path.GetDirectoryName(full),
            };
            var runOptions = new InterpreterOptions { Cancellation = c.Interpreter.Options.Cancellation, MaxCallDepth = c.Interpreter.Options.MaxCallDepth };
            runOptions.DefaultPackages.AddRange(c.Interpreter.Options.DefaultPackages.Where(n => !string.Equals(PackageRegistry.Normalise(n), "KScript.Test", StringComparison.OrdinalIgnoreCase)));

            int code;
            object error = null;
            try
            {
                code = new Interpreter(host, c.Interpreter.Packages, runOptions).RunFile(full);
            }
            catch (KScriptException ex) when (ex.Type != "Cancelled")
            {
                code = 3;
                error = ex.ToValue();
            }
            return new Dictionary<string, object>
            {
                ["output"] = Tidy(output.ToString()),
                ["code"] = (double)code,
                ["error"] = error,
            };
        }

        #endregion

        /// <summary>A value as it would be written in a script: 'text', 42, [1, 2], { a: 1 }, null.</summary>
        internal static string Show(object value)
        {
            string text = value switch
            {
                null => "null",
                string s => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n") + "'",
                bool b => b ? "true" : "false",
                double d => Values.FormatNumber(d),
                List<object> l => "[" + string.Join(", ", l.Select(Show)) + "]",
                Dictionary<string, object> m => m.Count == 0 ? "{}" : "{ " + string.Join(", ", m.Select(kv => $"{kv.Key}: {Show(kv.Value)}")) + " }",
                ICallable fn => $"fn {fn.Name}",
                _ => Values.ToStr(value),
            };
            return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
        }
    }
}
