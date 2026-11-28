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

    private static KScriptException TypeFails(string code) => Assert.Throws<KScriptException>(() => Runs(code));

    [Theory]
    [InlineData("r = Repo({ name: 'k', stars: '3' })\necho \"{ $r.name } { $r.stars + 1 } { $r->type() }\"", "k 4 Repo\n")]
    [InlineData("r = Repo(name: 'k')\necho \"{ $r.name } { $r.stars }\"", "k 0\n")]
    [InlineData("r = Repo('{\"name\": \"j\"}')\necho $r.name", "j\n")]
    [InlineData("rs = Repo([{ name: 'a' }, { name: 'b' }])\necho $rs->map(fn(r) => $r.name)->join(',')", "a,b\n")]
    [InlineData("r = { name: 'p' } |> Repo()\necho $r.name", "p\n")]
    [InlineData("echo Age('7') + 1", "8\n")]
    [InlineData("echo Size('small')", "small\n")]
    [InlineData("t = Repo\necho $t.name\necho '{\"name\": \"v\"}'->json(as: $t).name", "Repo\nv\n")]
    [InlineData("echo '{\"name\": \"x\"}'->json(as: Repo).name", "x\n")]
    [InlineData("try {\n  throw Missing(what: 'key')\n} catch Missing as e {\n  echo $e.what\n}", "key\n")]
    public void CallingATypeMakesAValue(string code, string printed)
    {
        Assert.Equal(printed, Runs(code));
    }

    [Fact]
    public void CastEvaluatesOnceThenNormalizesAndValidatesTheResult()
    {
        string printed = Runs("""
            type PasskeySessionKey with [string] {
              min: 8
              normalize: $value->trim()
              pattern: /^passkey_[a-zA-Z]+$/
            }
            fn key_for(kind) {
              echo 'called'
              return " passkey_{$kind} "
            }
            key = @key_for('login') as PasskeySessionKey
            echo $key
            """);
        Assert.Equal("called\npasskey_login\n", printed);
    }

    [Theory]
    [InlineData("echo '41' as int + 1", "42\n")]
    [InlineData("echo 'small' as Size", "small\n")]
    [InlineData("items = [{name: 'arc'}] as Repo[]\necho $items[0].name", "arc\n")]
    [InlineData("echo null as Age? == null", "true\n")]
    [InlineData("echo 'ok' as string(min: 2)", "ok\n")]
    public void CastUsesTheDeclaredTarget(string code, string printed) =>
        Assert.Equal(printed, Runs(code));

    [Theory]
    [InlineData("'200' as Age?", "Shape", "150")]
    [InlineData("[{stars: 1}] as Repo[]", "Shape", "name")]
    [InlineData("'x' as string(min: 2)", "Shape", "2")]
    [InlineData("null as MissingType?", "Type", "Unknown type 'MissingType'")]
    [InlineData("[] as MissingType[]", "Type", "Unknown type 'MissingType'")]
    [InlineData("[] as string(typo: 1)[]", "Type", "typo")]
    public void InvalidCastFailsEvenForNullableOrListTargets(string expression, string errorType, string message)
    {
        KScriptException error = TypeFails("value = " + expression);
        Assert.Equal(errorType, error.Type);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void ARepeatedTextCastReusesOnlyTheSameInputAndDeclaration()
    {
        var interpreter = new Interpreter(KScriptHost.ForText(new StringWriter()));
        var expression = KScript.Syntax.ExpressionParser.ParseExpression("$input as Key").Root;
        var scope = new Scope();
        var baseType = TypeRef.Parse("string");
        var rules = TypeRules.FromOptions(new Dictionary<string, object> { ["pattern"] = "/key_[a-z]+/" }, out _);
        scope.Declare("Key", new RefinedType("Key", baseType, rules, null, scope, null));
        string first = new string("key_login".ToCharArray());
        scope.Declare("input", first);
        Assert.Same(first, interpreter.Evaluate(expression, scope, default));

        string repeated = new string(first.ToCharArray());
        scope.Assign("input", repeated);
        Assert.NotSame(first, repeated);
        Assert.Same(first, interpreter.Evaluate(expression, scope, default));

        scope.Assign("input", "invalid");
        Assert.Equal("Shape", Assert.Throws<KScriptException>(() => interpreter.Evaluate(expression, scope, default)).Type);
        var strict = TypeRules.FromOptions(new Dictionary<string, object> { ["min"] = 30 }, out _);
        scope.Declare("Key", new RefinedType("Key", baseType, strict, null, scope, null));
        scope.Assign("input", repeated);
        Assert.Equal("Shape", Assert.Throws<KScriptException>(() => interpreter.Evaluate(expression, scope, default)).Type);
    }

    [Fact]
    public void RepeatedComputedTextRechecksRulesThatReadState()
    {
        string output = Runs("""
            type Key with [string] {
              normalize: $value->trim()
              pattern: /^passkey_[a-z]+$/
              validate: $allowed, 'disabled'
            }
            allowed = true
            fn make(kind) => " passkey_{$kind} " as Key
            echo @make('login')
            allowed = false
            try {
              echo @make('login')
            } catch err {
              echo $err.type
            }
            """);
        Assert.Equal("passkey_login\nShape\n", output);
    }

    [Theory]
    [InlineData("/a|abc/", "abc", true)]
    [InlineData("/a|abc/", "zabc", false)]
    [InlineData("/a|abc/", "abcd", false)]
    [InlineData("/aBc/mi", "ABC", true)]
    [InlineData("/a|ab # trailing comment/x", "ab", true)]
    [InlineData("/(?x) a # trailing comment/", "a", true)]
    public void ARecognizingPatternCoversTheWholeToken(string pattern, string word, bool expected)
    {
        Assert.True(RegexValue.TryParse(pattern, out var regex, out _));
        Assert.Equal(expected, regex.MatchesWholeWord(word));
    }

    [Fact]
    public void XmlExpressionsSupportTheSameCast()
    {
        Assert.Equal("42\n", ScriptRunner.Run("""<kscript><echo value="'41' as int + 1" /></kscript>"""));
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
        Assert.Contains(message, TypeFails("x = " + call + "\necho $x").Message);
    }

    [Theory]
    [InlineData("Repo->from({ name: 'k' })", "->from() is gone: call the type to make a value of it, Repo($data) or Repo(field: value).")]
    [InlineData("Age->from(7)", "->from() is gone: call the type to make a value of it, Age($value).")]
    [InlineData("Missing->new(what: 'x')", "->new() is gone: call the error type to make an error, Missing(...).")]
    public void FromAndNewAreGone(string call, string message)
    {
        KScriptException e = TypeFails("x = " + call + "\necho $x");
        Assert.Equal("Retired", e.Type);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void CallingAModelMakesARecordThatIsNotSavedYet()
    {
        string printed = Run("""
            model Note extends Timestamps {
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
            model Member(table: "people", unique: [email], hidden: [secret]) extends Timestamps {
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
