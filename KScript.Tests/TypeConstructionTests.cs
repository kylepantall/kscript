using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// Calling a type makes a value of it, for every kind of type: Repo($data), Repo(name: 'x'), Age(7), Size('small'),
/// User(name: 'Joan'). A type is named bare, and ->from() and ->new() are gone.
/// </summary>
public sealed class TypeConstructionTests : DataFixture
{
    private const string Types = """
        enum Size { small, large }

        type Age with [int] { min: 0; max: 150 }

        type Repo {
          name: string
          stars: int = 0
        }

        error Missing {
          what: string
        }


        """;

    private static string Runs(string code) => ScriptRunner.Run(Types + code, path: Path.Combine(Path.GetTempPath(), "construct.kscript"));

    private static KScriptException Fails(string code) => Assert.Throws<KScriptException>(() => Runs(code));

    [Theory]
    [InlineData("r = Repo({ name: 'k', stars: '3' })\necho \"$r.name { $r.stars + 1 } { @type($r) }\"", "k 4 Repo\n")]
    [InlineData("r = Repo(name: 'k')\necho \"$r.name $r.stars\"", "k 0\n")]
    [InlineData("r = Repo('{\"name\": \"j\"}')\necho $r.name", "j\n")]
    [InlineData("rs = Repo([{ name: 'a' }, { name: 'b' }])\necho $rs->map(fn($r) => $r.name)->join(',')", "a,b\n")]
    [InlineData("r = { name: 'p' } |> Repo()\necho $r.name", "p\n")]
    [InlineData("echo Age('7') + 1", "8\n")]
    [InlineData("echo Size('small')", "small\n")]
    [InlineData("t = Repo\necho $t->name()\necho @json('{\"name\": \"v\"}', as: $t).name", "Repo\nv\n")]
    [InlineData("echo @json('{\"name\": \"x\"}', as: Repo).name", "x\n")]
    [InlineData("try {\n  throw Missing(what: 'key')\n} catch Missing as e {\n  echo $e.what\n}", "key\n")]
    public void CallingATypeMakesAValue(string code, string printed)
    {
        Assert.Equal(printed, Runs(code));
    }

    [Theory]
    [InlineData("Repo({ stars: 1 })", "name")]
    [InlineData("Repo(stars: 'many', name: 'k')", "stars")]
    [InlineData("Age(200)", "150")]
    [InlineData("Size('huge')", "small")]
    [InlineData("Repo({}, {})", "Repo() takes the data to check (Repo($data)) or its fields by name (Repo(name: ..., stars: ...)).")]
    [InlineData("Repo({}, name: 'k')", "Repo() takes the data to check")]
    [InlineData("Age(1, 2)", "Age() takes one value to check: Age($value).")]
    [InlineData("Size()", "Size() takes one value to check: Size($value).")]
    public void AValueThatDoesNotFitIsAnError(string call, string message)
    {
        Assert.Contains(message, Fails("x = " + call + "\necho $x").Message);
    }

    [Theory]
    [InlineData("Repo->from({ name: 'k' })", "->from() is gone: call the type to make a value of it, Repo($data) or Repo(field: value).")]
    [InlineData("Age->from(7)", "->from() is gone: call the type to make a value of it, Age($value).")]
    [InlineData("Missing->new(what: 'x')", "->new() is gone: call the error type to make an error, Missing(...).")]
    public void FromAndNewAreGone(string call, string message)
    {
        KScriptException e = Fails("x = " + call + "\necho $x");
        Assert.Equal("Retired", e.Type);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void CallingAModelMakesARecordThatIsNotSavedYet()
    {
        string printed = Run("""
            model Note(timestamps) {
              text: string
              pinned: bool = false
            }

            $db->create_table(Note)
            note = Note(text: 'Buy milk')
            echo "{ Note->count() } { $note.text } { $note.pinned }"
            $note->save()
            echo "{ Note->count() } { Note->first().text } { $note.id }"
            saved = Note->create(text: 'Call Joan')
            echo "{ Note->count() } { $saved.id }"
            """);
        Assert.Equal("0 Buy milk false\n1 Buy milk 1\n2 2\n", printed);
    }

    [Fact]
    public void AModelsOptionsWorkInItsBrackets()
    {
        string printed = Run("""
            model Member(table: "people", timestamps, unique: [email], hidden: [secret]) {
              email: email
              secret: string = 'x'
            }

            $db->create_table(Member)
            m = Member->create(email: 'joan@example.com')
            echo $db->tables()->contains('people')
            echo $m.created_at != null
            echo $m->to_json()->contains('secret')
            try {
              Member->create(email: 'joan@example.com')
            } catch e {
              echo $e.type
            }
            """);
        Assert.Equal("true\ntrue\nfalse\nUniqueViolation\n", printed);
    }
}
