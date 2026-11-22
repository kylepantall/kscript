using KScript.Runtime;
using KScript.Metadata;

namespace KScript.Tests;

public sealed class PatternLiteralTests
{
    private static string Run(string code) =>
        ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "pattern-words.kscript")).TrimEnd('\n');

    [Fact]
    public void BareWordsUseTheWholePatternAndNormalConversion()
    {
        string result = Run("""
            type PasskeySessionKey with [string] {
              pattern: /passkey|passkey_[a-z]+$/i
              normalize: $value->lower()
            }
            fn show(value) => $value
            type Row { name: string }
            key = passkey_Register
            map = { key: passkey_Login }
            echo $key
            echo $map.key
            echo @show(passkey_Register)
            echo @show(value: passkey_Login)
            echo @show([passkey_Login, passkey_Register])->join(',')
            echo Row(name: passkey_Login).name
            echo 'passkey_Login'
            """);
        Assert.Equal("passkey_register\npasskey_login\npasskey_register\npasskey_login\npasskey_login,passkey_register\npasskey_login\npasskey_Login", result);
    }

    [Theory]
    [InlineData("type Key with [string] { pattern: /passkey_[a-z]+$/ }", "prefix_passkey_login")]
    [InlineData("type Key with [string] { one_of: [passkey_login] }", "passkey_login")]
    [InlineData("type Key with [email] { pattern: /passkey_[a-z]+/ }", "passkey_login")]
    [InlineData("type Key with [string] { pattern: /[a-z]+/ }", "step")]
    [InlineData("type Key with [string] { pattern: /passkey_[a-z]+/ }\npasskey_login = 5", "passkey_login")]
    [InlineData("type Key with [string] { pattern: /passkey_[a-z]+/ }\nfn passkey_login() => 1", "passkey_login")]
    public void ExistingNameErrorsAndUnmatchedBareWordsArePreserved(string declarations, string word)
    {
        var error = Assert.Throws<KScriptException>(() => Run(declarations + "\nvalue = " + word));
        var original = Assert.Throws<KScriptException>(() => Run("value = " + word));
        Assert.Equal(original.Type, error.Type);
    }

    [Fact]
    public void AmbiguityDoesNotExecuteUserRulesAndTypedParametersTakePriority()
    {
        string result = Run("""
            calls = 0
            fn lower(text) {
              calls += 1
              return $text->lower()
            }
            type Zebra with [string] { pattern: /key_.+/i; normalize: @lower($value) }
            type Alpha with [string] { pattern: /key_.+/i; normalize: @lower($value) }
            fn explicit(value: Alpha) => $value
            fn many(values: Alpha[]) => $values->join(',')
            type Row { name: Zebra }
            try {
              ambiguous = key_Blue
            } catch e {
              echo $e.message
            }
            echo $calls
            echo @explicit(key_Blue)
            echo @many([key_Red])
            echo Row(name: key_Green).name
            echo Zebra(key_White)
            echo key_Purple as Alpha
            echo $calls
            """);
        Assert.Contains("Alpha, Zebra", result);
        Assert.Contains("as Alpha", result);
        Assert.EndsWith("\n0\nkey_blue\nkey_red\nkey_green\nkey_white\nkey_purple\n5", result);
    }

    [Fact]
    public void AncestorNormalizersAndValidatorsRerunForCachedWords()
    {
        string result = Run("""
            ending = 'a'
            allowed = true
            type Base with [string] {
              normalize: $value + $ending
              validate: $allowed, 'word rejected'
            }
            type Key with [Base] { pattern: /key_[a-z]+/ }
            fn read() => key_demo
            echo @read()
            ending = 'b'
            echo @read()
            allowed = false
            try {
              echo @read()
            } catch e {
              echo $e.message
            }
            """);
        Assert.StartsWith("key_demoa\nkey_demob\n", result);
        Assert.Contains("word rejected", result);
    }

    [Fact]
    public void ValidationFailurePreservesTheSelectedTypesError()
    {
        const string script = """
            type Key with [string] { pattern: /key_[a-z]+/; min: 10 }
            fn show(value) => $value
            echo @show(key_x)
            """;
        KScriptException error = Assert.Throws<KScriptException>(() => Run(script));
        Assert.Equal("Shape", error.Type);
        Assert.Contains("at least 10", error.Message);
    }

    [Fact]
    public void ExplicitBareCastChecksItsTargetAndPreservesNamesAndOtherOperandForms()
    {
        Assert.Equal("small\nabc\nxyz", Run("""
            enum Size { small, large }
            type Token with [string] { pattern: /[a-z]+/ }
            text = 'xyz'
            echo small as Size
            echo abc as Token
            echo $text as Token
            """));
        const string key = "type Token with [string] { pattern: /key_[a-z]+/ }\n";
        Assert.Equal("Shape", Assert.Throws<KScriptException>(() => Run(key + "value = other as Token")).Type);
        Assert.Equal("Name", Assert.Throws<KScriptException>(() => Run(key + "other = 7\nvalue = other as Token")).Type);
        Assert.Equal("Name", Assert.Throws<KScriptException>(() => Run(key + "value = other as int")).Type);
        Assert.Equal("Name", Assert.Throws<KScriptException>(() => Run(key + "value = other as string")).Type);
    }

    [Fact]
    public void ScopeCacheSharesVisibleDecisionsAndInvalidatesMissesAndShadows()
    {
        var parent = new Scope();
        Scope child = parent.Child();
        var rules = TypeRules.FromOptions(new Dictionary<string, object> { ["pattern"] = "/key_[a-z]+/" }, out _);
        var key = new RefinedType("Key", TypeRef.Parse("string"), rules, null, parent, null);
        const string word = "key_login";
        var unrelated = TypeRules.FromOptions(new Dictionary<string, object> { ["pattern"] = "/other/" }, out _);
        parent.Declare("OtherWord", new RefinedType("OtherWord", TypeRef.Parse("string"), unrelated, null, parent, null));
        Assert.Null(Interpreter.InferredLiteral(word, child, null));
        parent.Declare(key.Name, key);
        PatternLiteral literal = Interpreter.InferredLiteral(word, parent, null);
        Assert.NotNull(literal);
        string longWord = "key_" + new string('a', 512);
        PatternLiteral uncached = Interpreter.InferredLiteral(longWord, parent, null);
        Assert.Equal(longWord, uncached.Convert(new Interpreter()));
        Assert.NotSame(uncached, Interpreter.InferredLiteral(longWord, parent, null));
        child.Declare("arg", 123);
        Assert.Same(literal, Interpreter.InferredLiteral(word, child, null));
        parent.Declare("Alias", key);
        Assert.Same(literal, Interpreter.InferredLiteral(word, child, null));
        child.Declare(key.Name, 17);
        Assert.Null(Interpreter.InferredLiteral(word, child, null));
        Assert.Same(literal, Interpreter.InferredLiteral(word, parent, null));
        child.Assign(key.Name, key);
        Assert.NotNull(Interpreter.InferredLiteral(word, child, null));
        var other = new RefinedType("Other", TypeRef.Parse("string"), rules, null, parent, null);
        parent.Declare(other.Name, other);
        Assert.Equal("Type", Assert.Throws<KScriptException>(() => Interpreter.InferredLiteral(word, child, null)).Type);
        Assert.Null(Interpreter.InferredLiteral(word, new Scope(), null));
    }

    [Fact]
    public void OverloadsTryEveryContextualMeaningBeforeConvertingAPatternLiteralOnce()
    {
        var interpreter = new Interpreter();
        Module module = interpreter.LoadText("""
            calls = 0
            fn lower(text) {
              calls += 1
              return $text->lower()
            }
            type Key with [string] { pattern: /key_.+/; normalize: @lower($value) }
            """, Path.Combine(Path.GetTempPath(), "pattern-overloads.kscript"));
        interpreter.Run(module);
        var word = new BareWord("key_Demo", false, false) { IsIdentifier = true };
        var call = new CallContext(interpreter, "read", new object[] { word }, scope: module.Globals, module: module);

        CallContext Bind(params string[] signatures) => ArgumentBinder.Bind(
            new CallableInfo(CallableKind.Command, "read", "", signatures.Select(Signature.Parse).ToArray()), call);
        Assert.Equal("key_Demo", Bind("value", "name: key_Demo|other").Arg(0));
        Assert.True(module.Globals.TryGet("calls", out object calls));
        Assert.Equal(0d, calls);
        Assert.Equal("key_demo", Bind("value, required", "value").Arg(0));
        Assert.True(module.Globals.TryGet("calls", out calls));
        Assert.Equal(1d, calls);
    }

    [Fact]
    public void AChangedProjectStringDefaultIsCheckedEvenAfterCaching()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-pattern-defaults-").FullName;
        try
        {
            string config = Path.Combine(dir, "kscript.json");
            File.WriteAllText(config, "{}");
            var interpreter = new Interpreter();
            var module = new Module(interpreter, Path.Combine(dir, "word.kscript"), null, null);
            var scope = new Scope();
            var rules = TypeRules.FromOptions(new Dictionary<string, object> { ["pattern"] = "/key_[a-z]+/" }, out _);
            var type = new RefinedType("Key", TypeRef.Parse("string"), rules, null, scope, module);
            scope.Declare(type.Name, type);
            PatternLiteral literal = Interpreter.InferredLiteral("key_login", scope, module);
            Assert.Equal("key_login", literal.Convert(interpreter));
            DateTime timestamp = File.GetLastWriteTimeUtc(config);
            File.WriteAllText(config, """{"types":{"string":{"min":30}}}""");
            File.SetLastWriteTimeUtc(config, timestamp.AddSeconds(2));
            Assert.Contains("at least 30", Assert.Throws<KScriptException>(() => literal.Convert(interpreter)).Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
