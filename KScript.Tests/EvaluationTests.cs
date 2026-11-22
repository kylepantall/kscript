using System.Globalization;

using KScript.Runtime;
using KScript.Syntax;
using KScript.Syntax.Markup;

namespace KScript.Tests;

/// <summary>Expressions: members, indexes, assignment, calls, pipes and aliases, and what goes wrong with them.</summary>
public sealed class EvaluationTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "kscript-eval-" + Guid.NewGuid().ToString("N"));

    public EvaluationTests()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "lib.ks.xml"), "<kscript><fn name=\"helper\" value=\"1\" /><let name=\"pi2\" value=\"6.28\" const=\"yes\" /></kscript>");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private string Run(string body)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new Interpreter(KScriptHost.ForText(output)).RunText("<kscript>" + body + "</kscript>", Path.Combine(dir, "main.ks.xml"));
        return output.ToString();
    }

    [Theory]
    [InlineData("<echo>{ +'5' } { -'2' } { not 0 }</echo>", "5 -2 true\n")]
    [InlineData("<echo>{ match 5 { 1 => 'a' } ?? 'none' }</echo>", "none\n")]
    [InlineData("<echo>{ 'a' in { a: 1 } } { 'b' in null } { 'b' in 'abc' } { 2 in [1, 2] }</echo>", "true false true true\n")]
    [InlineData("<echo>{ ({ a: 1 } + { b: 2, a: 3 })->to_json() } { [1] + [2] } { 'a' + 1 } { 1 + 'a' }</echo>", "{\"a\":3,\"b\":2} 1, 2 a1 1a\n")]
    [InlineData("<echo>{ @now() + 1 > @now() }</echo>", "true\n")]
    [InlineData("<echo>{ [1, 2][5] ?? 'n' } { [1, 2][-1] } { 'abc'[1] } { 'abc'[-1] } { 'abc'[9] ?? 'n' } { { a: 1 }['a'] }</echo>", "n 2 b c n 1\n")]
    [InlineData("<echo>{ $list.count } { $list.length } { 'abc'.length }</echo>", "3 3 3\n")]
    [InlineData("<import package=\"KScript.Math\" as=\"m\" /><import path=\"lib.ks.xml\" as=\"lib\" /><echo>{ m.pi > 3 } { m.sqrt(16) } { lib.pi2 } { lib.helper() } { @lib.pi2 } { @m.sqrt(9) } { m.e > 2 }</echo>", "true 4 6.28 1 6.28 3 true\n")]
    [InlineData("<let name=\"m\" value=\"{ a: 1 }\" /><do>$m['a'] += 2; $m['b'] ??= 5; $m.c ??= 1; $m.c += 1</do><echo>{ $m->to_json() }</echo>", "{\"a\":3,\"b\":5,\"c\":2}\n")]
    [InlineData("<let name=\"x\" value=\"10\" /><do>$x -= 1; $x *= 2; $x /= 3; $x ??= 4</do><echo>$x</echo>", "6\n")]
    [InlineData("<let name=\"l\" value=\"[1]\" /><do>$l += 2; $l[2] = 5; $l[-1] += 1; $l[0] *= 7</do><echo>{ $l }</echo>", "7, 2, 6\n")]
    [InlineData("<echo>{ 'abc' |> to_upper() } { 'abc' |> @to_upper() } { 'abc' |> upper() }</echo>", "ABC ABC ABC\n")]
    [InlineData("<let name=\"f\" value=\"fn($x) => $x * 2\" /><fn name=\"g\" params=\"x\" value=\"$x * 3\" /><echo>{ 3 |> $f } { 2 |> g() } { g(4) } { to_upper('x') }</echo>", "6 6 12 X\n")]
    [InlineData("<import package=\"KScript.IO\" /><echo>{ @file('a.txt').stem } { @file('a.txt') |> read_text() }</echo>", "a a\n")]
    [InlineData("<echo>{ '5'->round() } { { a: fn() => 1 }.a() }</echo>", "5 1\n")]
    public void Expressions_give_values(string body, string expected)
    {
        Assert.Equal(expected, Run("<let name=\"list\" value=\"['a', 'b', 'c']\" />" + body));
    }

    [Theory]
    [InlineData("<let name=\"n\" value=\"null\" /><echo>{ $n.x }</echo>", "Null", "Can't read .x of null in '$n'. Use ?. if it may be missing: $n?.x")]
    [InlineData("<echo>{ 1 in 5 }</echo>", "Type", "'in' needs a list, map or text on the right")]
    [InlineData("<echo>{ true + 1 }</echo>", "Type", "'+' in 'true + 1' needs a number")]
    [InlineData("<import package=\"KScript.Math\" as=\"m\" /><echo>{ m.nope }</echo>", "Name", "KScript.Math has no 'nope'.")]
    [InlineData("<import package=\"KScript.Math\" as=\"m\" /><echo>{ m.sqr }</echo>", "Name", "Did you mean sqrt?")]
    [InlineData("<import package=\"KScript.Math\" as=\"m\" /><echo>{ @m.nope(1) }</echo>", "Name", "KScript.Math has no command 'nope'.")]
    [InlineData("<import package=\"KScript.Math\" as=\"m\" /><echo>{ @mm.sqrt(16) }</echo>", "Name", "nothing is imported as 'mm'. Did you mean m?")]
    [InlineData("<echo>{ @zz.sqrt(16) }</echo>", "Name", "Import it with <import package=\"...\" as=\"zz\" />")]
    [InlineData("<echo>{ zork }</echo>", "Name", "Unknown name 'zork'. Variables start with $")]
    [InlineData("<let name=\"n\" value=\"null\" /><echo>{ $n[0] }</echo>", "Null", "Can't index into null in '$n[0]'.")]
    [InlineData("<echo>{ 5[0] }</echo>", "Type", "Can't index into a number (5).")]
    [InlineData("<let name=\"s\" value=\"'x'\" /><do>$s.a = 1</do>", "Type", "Only map fields can be set, but $s is the string 'x'.")]
    [InlineData("<let name=\"s\" value=\"'x'\" /><do>$s[0] = 1</do>", "Type", "Only list items and map keys can be set")]
    [InlineData("<let name=\"l\" value=\"[1]\" /><do>$l[3] = 0</do>", "Index", "Index 3 is outside the list (it has 1 items).")]
    [InlineData("<let name=\"x\" value=\"{ a: 1 }\" const=\"yes\" /><do>$x.a = 1</do>", "Const", "$x is const, so it can't be changed.")]
    [InlineData("<do>$undefined += 1</do>", "Name", "$undefined is not defined, so '+=' can't update it.")]
    [InlineData("<let name=\"v\" value=\"5\" /><echo>{ $v() }</echo>", "Type", "$v is a number (5), not a function.")]
    [InlineData("<echo>{ [1] |> 5 }</echo>", "Type", "5 is a number (5), not a function.")]
    [InlineData("<echo>{ @math('1 + 1') }</echo>", "Retired", "@math() was removed in KScript 2.")]
    [InlineData("<echo>{ @sqrt(4) }</echo>", "Import", "@sqrt() is part of KScript.Math.")]
    [InlineData("<echo>{ @to_uper('a') }</echo>", "Name", "Unknown command @to_uper(). Did you mean @to_upper?")]
    [InlineData("<echo>{ zork(1) }</echo>", "Name", "Unknown command @zork().")]
    [InlineData("<echo>{ @to_upper() }</echo>", "Argument", "@to_upper() needs 1 argument: @to_upper(text).")]
    [InlineData("<echo>{ @range() }</echo>", "Argument", "@range() needs at least 1 argument")]
    [InlineData("<echo>{ @now(1, 2) }</echo>", "Argument", "@now() takes at most 0 arguments")]
    [InlineData("<echo>{ @to_upper('a', 'b') }</echo>", "Argument", "@to_upper() takes at most 1 argument: @to_upper(text).")]
    [InlineData("<echo>{ [1]->sum(1, 2, 3) }</echo>", "Argument", "takes at most")]
    [InlineData("<let name=\"l\" value=\"[1]\" /><echo>{ [2] |> $l->count() }</echo>", "Syntax", "After |> write the method name without ->")]
    [InlineData("<echo>{ null->to_upper() }</echo>", "Null", "Can't call ->to_upper() on null.")]
    [InlineData("<echo>{ 5->sqrt() }</echo>", "Import", "->sqrt() on a number is part of KScript.Math.")]
    [InlineData("<echo>{ [1]->srt() }</echo>", "Name", "Lists have no method ->srt(). Did you mean ->sort()?")]
    [InlineData("<echo>{ { a: 1 }->a() }</echo>", "Name", "Maps have no method ->a(). To read a key, write .a or ['a'].")]
    [InlineData("<echo>{ true->x() } { (fn() => 1)->x() } { @now()->x() }</echo>", "Name", "Bools have no method ->x()")]
    [InlineData("<echo>{ (fn() => 1)->x() }</echo>", "Name", "Functions have no method ->x()")]
    [InlineData("<echo>{ @now()->zz() }</echo>", "Name", "Dates have no method ->zz()")]
    [InlineData("<echo>{ @today()->json() }</echo>", "Name", "Dates have no method ->json(). To write it as JSON text, use ->to_json().")]
    [InlineData("<echo>{ @today()->upper() }</echo>", "Name", "Dates have no method ->upper(). Use ->to_string()->upper() to work on its text.")]
    [InlineData("<echo>{ 5->upper() }</echo>", "Name", "Numbers have no method ->upper(). Use ->to_string()->upper() to work on its text.")]
    [InlineData("<echo>{ true->length() }</echo>", "Name", "Bools have no method ->length(). Use ->to_string()->length() to work on its text.")]
    [InlineData("<echo>{ 'a'->zz() }</echo>", "Name", "Text values have no method ->zz()")]
    [InlineData("<echo>{ 1->zz() }</echo>", "Name", "Numbers have no method ->zz()")]
    [InlineData("<echo>{ $config->zz() }</echo>", "Name", "Values of type config have no method ->zz()")]
    [InlineData("<echo>{ 'a'.x }</echo>", "Name", "To call a method, write ->x().")]
    public void Expression_errors_are_typed(string body, string type, string message)
    {
        KScriptException ex = Assert.Throws<KScriptException>(() => Run(body));
        Assert.Equal(type, ex.Type);
        Assert.Contains(message, ex.Message);
    }

    private sealed class StrangeNode : ExprNode
    {
    }

    [Fact]
    public void Nodes_the_parser_never_makes_still_fail_clearly()
    {
        var interpreter = new Interpreter(KScriptHost.ForText(new StringWriter()));
        Module module = interpreter.LoadText("<kscript></kscript>");
        var ctx = new EvalContext(module, null);
        var scope = new Scope();
        string Error(Action action) => Assert.Throws<KScriptException>(action).Message;
        var one = new LiteralNode { Kind = LiteralKind.Number, Value = "1" };

        Assert.Contains("Assignment isn't allowed here", Error(() => interpreter.Evaluate(new AssignmentNode { Name = "x", Target = new VariableNode { Name = "x" }, Operator = "=", Value = one }, scope, ctx)));
        Assert.Contains("This expression has a syntax error", Error(() => interpreter.Evaluate(new ErrorNode(), scope, ctx)));
        Assert.Contains("Can't evaluate StrangeNode", Error(() => interpreter.Evaluate(new StrangeNode(), scope, ctx)));
        Assert.Contains("Unknown operator '%%'", Error(() => interpreter.Evaluate(new BinaryNode { Operator = "%%", Left = one, Right = one }, scope, ctx)));
        Assert.Contains("Only a variable, a map field or a list item can be assigned to", Error(() => interpreter.Assign(new AssignmentNode { Name = "x", Target = one, Operator = "=", Value = one }, scope, ctx, declare: false)));
        Assert.Contains("Unknown assignment operator '%='", Error(() => interpreter.Combine(1.0, "%=", 1.0, one, ctx)));
        Assert.Equal(2.0, interpreter.Combine(1.0, "=", 2.0, one, ctx));
    }

    [Fact]
    public void Package_commands_and_objects_work_as_values()
    {
        Assert.Equal("3 a.txt\n", Run("<import package=\"KScript.Math\" as=\"m\" /><import package=\"KScript.IO\" /><let name=\"f\" value=\"m.sqrt\" /><echo>{ $f(9) } { @file('a.txt')['name'] }</echo>"));
        Assert.Equal("claude-x\n", Run("<import package=\"KScript.AI\" /><echo>{ @chat('s', { model: 'claude-x' }).model }</echo>"));
        Assert.Equal("x", Run("<let name=\"nl\" value=\"false\" /><echo newline=\"{ $nl }\">x</echo>"));
        Assert.Equal("x\n", Run("<echo newline=\"{ 'y' }{ 'es' }\">x</echo>"));
    }
}
