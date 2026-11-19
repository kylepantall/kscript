using KScript.Migration;

namespace KScript.Tests;

/// <summary>
/// kscript migrate: KScript 1 scripts, taken from the v1 test suite, converted and run on KScript 2. KScript 1 printed
/// bools as yes and no; KScript 2 prints true and false.
/// </summary>
public class MigrationTests
{
    private static MigrationResult Migrate(string body) =>
        Migrator.Migrate($"<kscript dynamic_defs=\"yes\" throw_exceptions=\"yes\">{body}</kscript>");

    private static string Converted(string body) => Migrate(body).Text;

    private static string RunV1(string body, string input = "")
    {
        MigrationResult result = Migrate(body);
        Assert.True(result.WasV1);
        return ScriptRunner.Run(result.Text, input);
    }

    private static string Echo(string expression, string setup = "") => RunV1($"{setup}<echo>{expression}</echo>").TrimEnd('\n');

    #region Expressions

    [Theory]
    [InlineData("@to_lower('HELLO')", "hello")]
    [InlineData("@to_lower(@to_lower('X')) and @to_lower('Y')", "x and y")]
    [InlineData("@not(@equals('a','b'))", "true")]
    [InlineData("@equals('a', 'A', 'true')", "true")]
    [InlineData("@equals('a', 'A')", "false")]
    [InlineData("@length('hello')", "5")]
    [InlineData("@split('a,b,c', ',', '1')", "b")]
    [InlineData("@split( 'a,b,c' ,  ','  , '1' )", "b")]
    [InlineData("@ifnull(@null(), 'fallback')", "fallback")]
    [InlineData("@contains('hello', 'ell')", "true")]
    [InlineData("@and('yes', 'no')", "false")]
    [InlineData("@any('yes', 'no')", "true")]
    [InlineData("@atindex('hello', '1')", "e")]
    [InlineData("@range('5', '1', '10')", "true")]
    [InlineData("@char('65')", "A")]
    [InlineData("@math(2 * (3 + 4))", "14")]
    [InlineData("@math('7/2')", "3.5")]
    [InlineData("@math('2^10')", "1024")]
    [InlineData("@more_less('1.5','lt','2.25')", "true")]
    [InlineData("@more_less(3, 2, 'mt')", "true")]
    [InlineData("@more_less(3, 'mte', 4)", "false")]
    [InlineData("@iftrue('yes', 'shown')", "shown")]
    [InlineData("@tie('a', 'b')->count()", "2")]
    [InlineData("Email me@example.com (work)", "Email me@example.com (work)")]
    [InlineData("Don't @to_lower('X')", "Don't x")]
    [InlineData("'quoted'", "quoted")]
    [InlineData("A {0} B", "A {0} B")]
    public void Commands(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("'hello'->upper()", "HELLO")]
    [InlineData("'  x  '->trim()->length()", "1")]
    [InlineData("'abc'->reverse()", "cba")]
    [InlineData("'hello'->contains('ell')", "true")]
    [InlineData("'hello'->index_of('l')", "2")]
    [InlineData("'a.b.c'->replace('.', '/')", "a/b/c")]
    [InlineData("'hello'->substring(1, 3)", "ell")]
    [InlineData("'hello'->substring(3)", "lo")]
    [InlineData("'hello'->char_at(1)", "e")]
    [InlineData("'a,b,c'->split(',', 2)", "c")]
    [InlineData("'ab'->repeat(3)", "ababab")]
    [InlineData("'7'->pad_left(3, '0')", "007")]
    [InlineData("'a'->equals('A', 'yes')", "true")]
    [InlineData("5->add(3)", "8")]
    [InlineData("5->sub(7)", "-2")]
    [InlineData("1.5->mul(2)", "3")]
    [InlineData("7->div(2)", "3.5")]
    [InlineData("7->mod(3)", "1")]
    [InlineData("2->pow(10)", "1024")]
    [InlineData("3.14159->round(2)", "3.14")]
    [InlineData("3->lt(4)", "true")]
    [InlineData("4->lte(4)", "true")]
    [InlineData("5->mt(4)", "true")]
    [InlineData("4->mte(5)", "false")]
    [InlineData("5->between(1, 10)", "true")]
    [InlineData("0->is_zero()", "true")]
    [InlineData("5->increment()", "6")]
    [InlineData("5->increment(5)", "10")]
    [InlineData("'yes'->not()", "false")]
    [InlineData("'yes'->and('no')", "false")]
    [InlineData("'no'->or('true')", "true")]
    [InlineData("'yes'->xor('yes')", "false")]
    [InlineData("Total: @math('2+2')->mul(10) units", "Total: 40 units")]
    public void Methods(string expression, string expected)
    {
        Assert.Equal(expected, Echo(expression));
    }

    [Theory]
    [InlineData("$name |> trim() |> upper()", "<def id=\"name\">  kyle  </def>", "KYLE")]
    [InlineData("$csv |> @split(',', '1')", "<def id=\"csv\">a,b,c</def>", "b")]
    [InlineData("$name |> fn($s) => $s->lower()->reverse()", "<def id=\"name\">KYLE</def>", "elyk")]
    [InlineData("'hi.' |> $shout", "<def id=\"shout\">fn($s) => $s->upper()->replace('.', '!')</def>", "HI!")]
    [InlineData("$add->call(2, 3)", "<def id=\"add\">fn($a, $b) => $a->add($b)</def>", "5")]
    [InlineData("$v |> fn($n) => ($n->mul(2) |> fn($m) => $m->add(1))", "<def id=\"v\">10</def>", "21")]
    [InlineData("@not(@equals($a->upper(), 'x'))", "<def id=\"a\">b</def>", "true")]
    [InlineData("[$name->trim()->upper()]", "<def id=\"name\">  kyle  </def>", "[KYLE]")]
    [InlineData("$i->lt(10)", "<def id=\"i\">5</def>", "true")]
    [InlineData("@to_lower('$greeting')", "<def id=\"greeting\">HELLO WORLD</def>", "hello world")]
    [InlineData("@length($csv)", "<def id=\"csv\">a,b,c</def>", "5")]
    [InlineData("@split($csv, ',', '1')", "<def id=\"csv\">a,b,c</def>", "b")]
    [InlineData("@to_lower($q)", "<def id=\"q\">IT'S</def>", "it's")]
    [InlineData("$x $xy", "<def id=\"x\">5</def><def id=\"xy\">7</def>", "5 7")]
    [InlineData("$s->upper() $t->lower()", "<def id=\"s\">Hello</def><def id=\"t\">World</def>", "HELLO world")]
    public void ExpressionsWithVariables(string expression, string setup, string expected)
    {
        Assert.Equal(expected, Echo(expression, setup));
    }

    [Fact]
    public void ArrayLookupsBecomeIndexesAndMembers()
    {
        Assert.Contains("$people[$i].age gt $max", Converted("<if condition=\"@more_less(~people[$i]['age'],'mt',$max)\"></if>"));
        Assert.Contains("value=\"$i lt 3\"", Converted("<def id=\"ok\">@more_less($i,'lt','3')</def>"));
    }

    #endregion

    #region Elements

    [Theory]
    [InlineData("replace", "2")]
    [InlineData("append", "12")]
    [InlineData("add", "3")]
    public void Update(string type, string expected)
    {
        Assert.Equal(expected + "\n", RunV1($"<def id=\"x\">1</def><update to=\"x\" type=\"{type}\">2</update><echo>$x</echo>"));
    }

    [Fact]
    public void DefmAndInput()
    {
        Assert.Equal("same same\n", RunV1("<defm ids=\"a,b\">same</defm><echo>$a $b</echo>"));
        Assert.EndsWith("Hello, Kyle!\n", RunV1("<input to=\"name\" type=\"text\">Name? </input><echo>Hello, $name!</echo>", "Kyle\n"));
    }

    [Theory]
    [InlineData("yes", "A\n")]
    [InlineData("no", "")]
    [InlineData("@equals('a','a')", "A\n")]
    public void If(string condition, string expected)
    {
        Assert.Equal(expected, RunV1($"<if condition=\"{condition}\"><echo>A</echo></if>"));
    }

    [Fact]
    public void IfCheckforNoAndElse()
    {
        Assert.Equal("ran\n", RunV1("<if condition=\"no\" checkfor=\"no\"><echo>ran</echo></if>"));
        Assert.Equal("B\n", RunV1("<if condition=\"no\"><echo>A</echo></if><else><echo>B</echo></else>"));
        Assert.Equal("1\n", RunV1("<def id=\"n\">0</def><if condition=\"@equals(@math('$n'),'0')\"><update to=\"n\">@math('$n+1')</update></if><echo>$n</echo>"));
    }

    [Fact]
    public void ForLoopsBecomeRanges()
    {
        const string loop = "<for to=\"i\" while=\"@more_less($i,'lt','3')\" math=\"@math('$i+1')\"><echo>$i</echo></for>";
        Assert.Contains("<for each=\"i\" in=\"0..3\">", Converted(loop));
        Assert.Equal("0\n1\n2\n", RunV1(loop));
        Assert.Equal("done\n", RunV1("<for to=\"i\" while=\"@more_less($i,'lt','0')\" math=\"@math('$i+1')\"><echo>never</echo></for><echo>done</echo>"));
        Assert.Equal("0\n2\n4\n", RunV1("<for to=\"i\" while=\"$i->lt(6)\" math=\"$i->add(2)\"><echo>$i</echo></for>"));
        Assert.Equal("1\n2\n", RunV1("<for to=\"i\" while=\"@more_less($i,'lte','2')\" math=\"$i->increment()\"><if condition=\"$i->is_zero()\"><continue/></if><echo>$i</echo></for>"));
    }

    [Fact]
    public void ForLoopsKeepTheVariableTheyStartFrom()
    {
        Assert.Equal("9\n", RunV1("<def id=\"i\">9</def><for to=\"i\" while=\"@more_less($i,'lt','12')\" math=\"@math('$i+1')\"></for><echo>$i</echo>"));
        Assert.Equal("9\n10\n11\n", RunV1("<def id=\"i\">9</def><for to=\"i\" while=\"@more_less($i,'lt','12')\" math=\"@math('$i+1')\"><echo>$i</echo></for>"));
    }

    [Fact]
    public void BreakAndContinue()
    {
        Assert.Equal("0\n1\n", RunV1("<for to=\"i\" while=\"@more_less($i,'lt','5')\" math=\"@math('$i+1')\"><if condition=\"@equals($i,'2')\"><break/></if><echo>$i</echo></for>"));
        Assert.Equal("a0\na1\n", RunV1(
            "<for to=\"i\" while=\"@more_less($i,'lt','2')\" math=\"@math('$i+1')\">" +
            "<for to=\"j\" while=\"@more_less($j,'lt','5')\" math=\"@math('$j+1')\"><break/><echo>never</echo></for>" +
            "<echo>a$i</echo></for>"));
        Assert.Equal("0\n2\n3\n", RunV1("<for to=\"i\" while=\"@more_less($i,'lt','4')\" math=\"@math('$i+1')\"><if condition=\"@equals($i,'1')\"><continue/></if><echo>$i</echo></for>"));
    }

    [Fact]
    public void LoopsThatChangeTheirCounterBecomeWhileLoops()
    {
        const string loop = "<for to=\"i\" while=\"@more_less($i,'lt','10')\" math=\"@math('$i*2')\"><update to=\"i\">@math('$i+1')</update><echo>$i</echo></for>";
        string converted = Converted(loop);
        Assert.Contains("<while test=\"$i lt 10\">", converted);
        Assert.Equal("1\n3\n7\n", RunV1(loop));
    }

    [Fact]
    public void DoWhileAndForever()
    {
        Assert.Equal("0\n1\n2\n", RunV1("<def id=\"n\">0</def><dowhile condition=\"@more_less($n,'lt','3')\"><echo>$n</echo><update to=\"n\">@math('$n+1')</update></dowhile>"));
        Assert.Equal("3\n", RunV1("<def id=\"n\">0</def><forever><update to=\"n\">@math('$n+1')</update><if condition=\"@equals($n,'3')\"><break/></if></forever><echo>$n</echo>"));
        Assert.Equal("once\n", RunV1("<forever><echo>once</echo><end/></forever><echo>never</echo>"));
    }

    [Fact]
    public void Arrays()
    {
        Assert.Equal("a\nb\nc\n", RunV1("<array id=\"arr\">a,b,c</array><afor from=\"arr\" to=\"item\"><echo>$item</echo></afor>"));
        Assert.Equal("b\n", RunV1("<array id=\"arr\">a,b,c</array><echo>@array('arr','1')</echo>"));
        Assert.Equal("3\n", RunV1("<array id=\"arr\">a,b</array><ainsert to=\"arr\">c</ainsert><echo>@acount('arr')</echo>"));
        Assert.Equal("[0] - a\n[1] - c\n", RunV1("<array id=\"arr\">a,b,c</array><aremove from=\"arr\" index=\"1\" /><aecho from=\"arr\" />"));
    }

    [Fact]
    public void MethodsBecomeFunctions()
    {
        Assert.Equal("before\nHi Bob\n", RunV1("<method name=\"greet\" params=\"who\"><echo>Hi $greet.who</echo></method><echo>before</echo><call method=\"greet\" args=\"Bob\"/>"));
        Assert.Equal("Hello, World!\n", RunV1("<method name=\"greet\" params=\"who,greeting\"><echo>$greet.greeting, $greet.who!</echo></method><call method=\"greet\" args=\"'World', 'Hello'\"/>"));
        Assert.Equal("[a, b] [x]\n", RunV1("<method name=\"show\" params=\"first,second\"><echo>[$show.first] [$show.second]</echo></method><call method=\"show\" args=\"'a, b', @to_lower('X')\"/>"));
    }

    [Fact]
    public void MultiArrayLookupInALoop()
    {
        const string script = @"
<marray id=""people"">
  <item><item key=""age"">28</item><item key=""name"">Joseph</item></item>
  <item><item key=""age"">82</item><item key=""name"">Joan</item></item>
  <item><item key=""age"">12</item><item key=""name"">Ellie</item></item>
</marray>
<def id=""max"">0</def>
<def id=""who"">0</def>
<for to=""i"" math=""@math('$i+1')"" while=""@more_less($i,'lt',@acount('~people'))"">
  <if condition=""@more_less(~people[$i]['age'],'mt',$max)"">
    <update to=""max"">~people[$i]['age']</update>
    <update to=""who"">$i</update>
  </if>
</for>
<echo>~people[$who]['name'] is $max</echo>";
        Assert.Equal("Joan is 82\n", RunV1(script));
    }

    [Fact]
    public void VariablesFirstSetInsideABlockAreDeclaredAtTheTop()
    {
        const string body = "<if condition=\"yes\"><def id=\"found\">x</def></if><echo>$found</echo>";
        Assert.Contains("<let name=\"found\" value=\"null\" />", Converted(body));
        Assert.Equal("x\n", RunV1(body));
    }

    [Fact]
    public void EchoOptions()
    {
        Assert.Equal("a b\n", RunV1("<echo trail_newline=\"no\">a </echo><echo>b</echo>"));
        Assert.Equal("x\n", RunV1("<echo trim=\"yes\">   x   </echo>"));
    }

    [Fact]
    public void TerminalAndFileElementsImportTheirPackages()
    {
        string converted = Converted("<anykey prompt=\"Press a key\"/><new_dir path=\"out\"/><end/>");
        Assert.Contains("<import package=\"KScript.Terminal\" />", converted);
        Assert.Contains("<import package=\"KScript.IO\" />", converted);
        Assert.Contains("<pause>Press a key</pause>", converted);
        Assert.Contains("<mkdir path=\"'out'\" />", converted);
        Assert.Contains("<exit />", converted);
    }

    [Fact]
    public void WaitIntervals()
    {
        Assert.Contains("<wait ms=\"250\" />", Converted("<wait interval=\"250ms\"/>"));
        Assert.Contains("<wait seconds=\"2\" />", Converted("<wait interval=\"2s\"/>"));
    }

    #endregion

    #region Notes and files

    [Fact]
    public void WhatCantBeConvertedIsMarkedAndListed()
    {
        MigrationResult result = Migrate("\n  <registry subkey=\"x\" />\n  <echo>@xpath('a', 'b')</echo>\n");
        Assert.Contains(result.Notes, n => n.Line == 2 && n.Message.Contains("<registry>"));
        Assert.Contains(result.Notes, n => n.Line == 3 && n.Message.Contains("@xpath()"));
        Assert.Contains("<!-- kscript migrate: <registry>", result.Text);
    }

    [Fact]
    public void KScript2ScriptsAreLeftAlone()
    {
        const string v2 = "<kscript>\n  <let name=\"x\" value=\"1\" />\n  <echo>{ $x + 1 }</echo>\n</kscript>";
        MigrationResult result = Migrator.Migrate(v2);
        Assert.False(result.WasV1);
        Assert.Equal(v2, result.Text);
    }

    [Fact]
    public void CommentsAndLayoutAreKept()
    {
        const string v1 = "<kscript dynamic_defs=\"yes\">\n  <!-- the total -->\n  <def id=\"total\">0</def>\n\n  <echo>$total</echo>\n</kscript>";
        Assert.Equal("<kscript>\n  <!-- the total -->\n  <let name=\"total\" value=\"0\" />\n\n  <echo>$total</echo>\n</kscript>", Migrator.Migrate(v1).Text);
    }

    [Fact]
    public void TheExampleScriptsFromKScript1()
    {
        const string oldest = @"<kscript throw_exceptions=""no"" dynamic_defs=""yes"">
    <marray id=""people"">
        <item><item key=""age"">28</item><item key=""name"">Joseph</item></item>
        <item><item key=""age"">82</item><item key=""name"">Joan</item></item>
    </marray>
    <def id=""max"">0</def>
    <def id=""maxPerson"">0</def>
    <for to=""i"" math=""@math('$i+1')"" while=""@more_less($i,'lt',@acount('~people'))"">
        <if condition=""@more_less(~people[$i]['age'],'mt',$max)"">
            <update to=""max"">~people[$i]['age']</update>
            <update to=""maxPerson"">$i</update>
        </if>
    </for>
    <echo>~people[$maxPerson]['name'] is the oldest at $max years old.</echo>
</kscript>";
        Assert.Equal("Joan is the oldest at 82 years old.\n", ScriptRunner.Run(Migrator.Migrate(oldest).Text));
    }

    [Fact]
    public void TheCommandRewritesFilesAndKeepsABackup()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-migrate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            string old = Path.Combine(dir, "sub", "old.xml");
            File.WriteAllText(old, "<kscript dynamic_defs=\"yes\"><def id=\"x\">1</def><echo>$x</echo></kscript>");
            File.WriteAllText(Path.Combine(dir, "new.ks.xml"), "<kscript><echo>hi</echo></kscript>");
            File.WriteAllText(Path.Combine(dir, "data.xml"), "<people />");

            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, MigrateCommand.Run(new[] { dir }, output, error));
            Assert.Equal("<kscript><let name=\"x\" value=\"1\" /><echo>$x</echo></kscript>", File.ReadAllText(old));
            Assert.True(File.Exists(old + MigrateCommand.BackupSuffix));
            Assert.Equal("<people />", File.ReadAllText(Path.Combine(dir, "data.xml")));
            Assert.Contains("1 script migrated, 0 notes, 1 already KScript 2.", error.ToString());

            output = new StringWriter();
            File.WriteAllText(old, File.ReadAllText(old + MigrateCommand.BackupSuffix));
            Assert.Equal(0, MigrateCommand.Run(new[] { old, "--dry-run" }, output, new StringWriter()));
            Assert.Equal("<kscript><let name=\"x\" value=\"1\" /><echo>$x</echo></kscript>", output.ToString().TrimEnd());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    #endregion
}
