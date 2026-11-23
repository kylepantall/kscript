namespace KScript.Tests;

public sealed class RefinementBaseTests
{
    private static string Run(string program) =>
        ScriptRunner.Run(program, path: Path.Combine(Path.GetTempPath(), "refinement-base.kscript"));

    [Fact]
    public void NumbersKeepMethodsThroughCollectionOperationsAndCompareByBaseValue()
    {
        string output = Run("""
            type Score with [int] { fn next() => $value + 1 }
            type Scores with [Score[]] { fn best() => $value->max() }
            fn twice(n: number) => $n * 2
            scores = Scores([0, 7])
            for score in $scores {
              echo $score->next()
            }
            more = []->push($scores->best())->map(fn(x) => $x)
            echo $more[0]->next()
            echo @twice($more[0])
            echo $scores[0]->or('fallback')
            echo $scores[1] === 7
            echo $scores->contains('7')
            echo { scores: $scores }->to_yaml()->yaml()->to_json()
            """);
        Assert.Equal("1\n8\n8\n14\nfallback\ntrue\ntrue\n{\"scores\":[0,7]}\n", output);
    }

    [Fact]
    public void NativeListFilteringKeepsEachRefinementAndTypedHandlersUseItsBase()
    {
        string output = Run("""
            type Release with [semver] { fn label() => 'v' + $value->to_string() }
            type Tagged with [semver] { fn label() => 'tagged' }
            type Amount with [number] { fn twice() => $value * 2 }
            versions = [Release('1.0.0'), Release('2.0.0-rc.1')]
            echo $versions->only(releases)[0]->label()
            echo $versions->latest()->label()
            same: semver = '1.0.0'
            echo [Release($same), Tagged($same)]->latest()->label()
            color: color = red
            echo $color->lighten(Amount(0.5))
            """);
        Assert.Equal("v1.0.0\nv1.0.0\ntagged\n#ff8080\n", output);
    }
}
