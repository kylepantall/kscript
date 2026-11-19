using KScript.Language.Analysis;
using KScript.Migration;

using static KScript.Language.Tests.TestHelpers;

namespace KScript.Language.Tests
{
    /// <summary>What kscript migrate writes passes kscript check: no errors, no warnings, imports included.</summary>
    public class MigrationAnalysisTests
    {
        [Theory]
        [InlineData("<def id=\"x\">5</def><echo>$x</echo>")]
        [InlineData("<def id=\"name\">  kyle  </def><echo>$name |> trim() |> upper()</echo>")]
        [InlineData("<def id=\"shout\">fn($s) => $s->upper()->replace('.', '!')</def><echo>'hi.' |> $shout</echo>")]
        [InlineData("<def id=\"n\">0</def><dowhile condition=\"@more_less($n,'lt','3')\"><echo>$n</echo><update to=\"n\">@math('$n+1')</update></dowhile>")]
        [InlineData("<for to=\"i\" while=\"@more_less($i,'lt','4')\" math=\"@math('$i+1')\"><if condition=\"@equals($i,'1')\"><continue/></if><echo>$i</echo></for>")]
        [InlineData("<for to=\"i\" while=\"@more_less($i,'lt','10')\" math=\"@math('$i*2')\"><update to=\"i\">@math('$i+1')</update><echo>$i</echo></for>")]
        [InlineData("<array id=\"arr\">a,b,c</array><ainsert to=\"arr\">d</ainsert><afor from=\"arr\" to=\"item\"><echo>$item @acount('arr')</echo></afor><aecho from=\"arr\" />")]
        [InlineData("<method name=\"greet\" params=\"who,greeting\"><echo>$greet.greeting, $greet.who!</echo></method><call method=\"greet\" args=\"'World', 'Hello'\"/>")]
        [InlineData("<if condition=\"yes\"><def id=\"found\">x</def></if><echo>$found</echo>")]
        [InlineData("<input to=\"name\" type=\"text\">Name? </input><anykey prompt=\"Press a key\"/><echo>Hello, $name!</echo><end/>")]
        [InlineData("<new_dir path=\"out\"/><write to=\"@join_path('out', 'a.txt')\">hi</write><echo>@dir_exists('out') @path('out/a.txt', 'extension')</echo>")]
        [InlineData("<echo>@k('os.platform') @k('user.home') @k('script.path') @datetime('yyyy') @guid() @args()</echo>")]
        public void MigratedScriptsAreClean(string body)
        {
            MigrationResult result = Migrator.Migrate($"<kscript dynamic_defs=\"yes\">{body}</kscript>");
            Assert.True(result.WasV1);
            Assert.Empty(result.Notes);
            DocumentAnalysis a = Analyze(result.Text);
            Assert.True(a.Diagnostics.Count == 0, result.Text + "\n" + Describe(a));
        }

        [Fact]
        public void TheOldExampleScriptIsClean()
        {
            const string oldest = @"<kscript throw_exceptions=""yes"" dynamic_defs=""yes"">
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
            MigrationResult result = Migrator.Migrate(oldest);
            AssertClean(Analyze(result.Text));
        }
    }
}
