using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests;

/// <summary>
/// email, password, handle, uuid and regex on the NativeType contract: what ==, the comparisons and sorting do for
/// each, the operators none of them take, and that their values, fields and methods work as before.
/// </summary>
public class NativeOperatorsSemanticTests
{
    private static string Code(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "semantic-operators-test.kscript")).TrimEnd('\n');

    private static string Fails(string code) => Assert.Throws<KScriptException>(() => Code(code)).Message;

    [Theory]
    // Ordering.ByText: emails, handles and uuids compare and sort by their tidied text.
    [InlineData("a: email = 'ann@example.com'\nb: email = 'bob@example.com'\necho \"{ $a < $b } { $b > $a } { $a <= 'ann@EXAMPLE.com' } { $a >= $b }\"", "true true true false")]
    [InlineData("list = [bob@example.com, ann@example.com, cy@example.com]\necho $list->sort()", "ann@example.com, bob@example.com, cy@example.com")]
    [InlineData("a: handle = @ann\nb: handle = @Bob\necho \"{ $a < $b } { $b > $a } { $a <= '@ANN' }\"", "true true true")]
    [InlineData("list = ['@sam', '@ann', '@kim']->map(fn(h) => @f($h))\necho $list->sort()", "@ann, @kim, @sam")]
    [InlineData("a: uuid = 00000000-0000-0000-0000-000000000000\nb: uuid = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho \"{ $a < $b } { $b >= $a }\"", "true true")]
    [InlineData("a: uuid = '3F2A8C1E-9B7D-4C2A-8E1F-0A9B8C7D6E5F'\necho \"{ $a <= '3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f' }\"", "true")]
    // Equality.ByText: the same once tidied.
    [InlineData("e = joan@example.com\necho \"{ $e == 'joan@EXAMPLE.com' } { $e != 'sam@example.com' }\"", "true true")]
    [InlineData("u: uuid = '3F2A8C1E-9B7D-4C2A-8E1F-0A9B8C7D6E5F'\necho \"{ $u == 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f }\"", "true")]
    [InlineData("echo \"{ /a+/i == /a+/i } { /a+/i == /a+/ } { /a+/ == 'a+' }\"", "true false true")]
    // No Falsy: every value is true, even the nil UUID.
    [InlineData("u: uuid = 00000000-0000-0000-0000-000000000000\necho \"{ $u ? 'yes' : 'no' }\"", "yes")]
    [InlineData("e = joan@example.com\necho \"{ $e and true }\"", "true")]
    // Text still joins with +, and in still finds text.
    [InlineData("e = joan@example.com\necho \"{ 'to: ' + $e }\"", "to: joan@example.com")]
    [InlineData("h: handle = @joan\necho \"{ $h + '!' }\"", "@joan!")]
    [InlineData("echo \"{ joan@example.com in [joan@example.com, sam@example.com] }\"", "true")]
    // ->test is how text is checked against a regex.
    [InlineData("echo \"{ /^[a-z]+$/i->test('Hello') }\"", "true")]
    public void Works(string code, string expected) => Assert.Equal(expected, Code("fn f(h: handle) => $h\n" + code));

    [Theory]
    // No arithmetic on any of them.
    [InlineData("e = joan@example.com\necho \"{ $e + 1 }\"", "Email values have no '+'")]
    [InlineData("e = joan@example.com\necho \"{ $e - $e }\"", "Email values have no '-'")]
    [InlineData("h: handle = @joan\necho \"{ $h * 2 }\"", "Handle values have no '*'")]
    [InlineData("u: uuid = 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f\necho \"{ $u + 1 }\"", "Uuid values have no '+'")]
    [InlineData("echo \"{ /a/ * 2 }\"", "Regex values have no '*'")]
    [InlineData("e = joan@example.com\necho \"{ -$e }\"", "'-'")]
    // Ordering.None: regexes have no order.
    [InlineData("echo \"{ /a/ < /b/ }\"", "Regex values have no order, so '<' can't compare them")]
    [InlineData("echo [/b/, /a/]->sort()", "Regex values have no order")]
    // A comparison needs both sides to be the type.
    [InlineData("e = joan@example.com\necho \"{ $e < 'not an email' }\"", "both sides need to be an email")]
    // No ranges, and a for loop can't walk them.
    [InlineData("a = ann@example.com\nb = bob@example.com\necho $a..$b", "Email values don't make ranges")]
    [InlineData("e = joan@example.com\nfor x in $e {\n  echo $x\n}", "email values can't be walked")]
    public void FailsWithMessage(string code, string expected) => Assert.Contains(expected, Fails(code));

    [Theory]
    // Equality.Never and Ordering.None for passwords: the existing message, which never shows the text.
    [InlineData("p: password = 'hunter2'\nok = $p == 'hunter2'")]
    [InlineData("p: password = 'hunter2'\nq: password = 'hunter2'\nok = $p != $q")]
    [InlineData("p: password = 'hunter2'\nok = $p >= 'a'")]
    [InlineData("p: password = 'hunter2'\nq: password = 'zzz'\necho [$p, $q]->sort()")]
    public void PasswordsAreNeverCompared(string code)
    {
        string message = Fails(code);
        Assert.Contains("Passwords can't be compared with operators. Use $password->verify($hash)", message);
        Assert.DoesNotContain("hunter2", message);
    }

    [Theory]
    [InlineData("p: password = 'hunter2'\necho \"{ $p + 'x' }\"", "********x")]
    [InlineData("p: password = 'hunter2'\necho \"{ $p } { $p->reveal()->count() } { $p->verify($p->hash()) }\"", "******** 7 true")]
    public void PasswordsStayMasked(string code, string expected) => Assert.Equal(expected, Code(code));

    [Fact]
    public void PasswordArithmeticFailsWithoutShowingIt()
    {
        string message = Fails("p: password = 'hunter2'\necho \"{ $p * 2 }\"");
        Assert.Contains("Password values have no '*'", message);
        Assert.DoesNotContain("hunter2", message);
    }

    [Fact]
    public void ConversionMessagesAreUnchanged()
    {
        Assert.Contains("needs an email, but got the string 'joan': it has no @", Fails("fn f(e: email) => $e\necho @f('joan')"));
        Assert.Contains("needs a handle, but got the string 'joan': it must start with @, as in @joan", Fails("fn f(h: handle) => $h\necho @f('joan')"));
        Assert.Contains("needs a password, but got text that is empty", Fails("fn f(p: password) => 1\necho @f('')"));
        Assert.Contains("needs a password (text), but got a number", Fails("fn f(p: password) => 1\necho @f(12)"));
        Assert.Contains("needs a uuid, but got the string 'abc'", Fails("fn f(u: uuid) => $u\necho @f('abc')"));
        Assert.Contains("needs a regex, but got the string '('", Fails("fn f(r: regex) => $r\necho @f('(')"));
    }

    [Fact]
    public void DeclarationsReachTheEditor()
    {
        foreach (string name in new[] { "email", "password", "handle", "uuid", "regex" })
        {
            NativeTypeInfo info = NativeTypes.Get(name);
            Assert.Empty(info.Operators);
            Assert.Null(info.Iterates);
            Assert.Null(info.Falsy);
            Assert.NotNull(NativeTypeRegistry.Get(name));
        }
        Assert.Equal("never", NativeTypes.Get("password").Equality);
        Assert.Equal("none", NativeTypes.Get("password").Ordering);
        Assert.True(NativeTypes.Get("password").Secret);
        Assert.Equal("text", NativeTypes.Get("email").Ordering);
        Assert.Equal("text", NativeTypes.Get("handle").Ordering);
        Assert.Equal("text", NativeTypes.Get("uuid").Ordering);
        Assert.Equal("none", NativeTypes.Get("regex").Ordering);
        Assert.Equal("handle", NativeTypes.Get("handle").RawKind);
        Assert.Equal(new[] { "email", "password", "handle", "uuid", "regex", "url" }, NativeTypes.Names.Take(6));
    }

    [Fact]
    public void ConvertGivesTheValueClasses()
    {
        Assert.True(SemanticTypes.TryConvert("email", " Joan@Example.COM ", out object email, out _));
        Assert.Equal("Joan@example.com", Assert.IsType<EmailAddress>(email).Text);
        Assert.True(SemanticTypes.TryConvert("handle", "@Joan", out object handle, out _));
        Assert.Equal("joan", Assert.IsType<HandleName>(handle).Name);
        Assert.True(SemanticTypes.TryConvert("uuid", "3F2A8C1E9B7D4C2A8E1F0A9B8C7D6E5F", out object uuid, out _));
        Assert.Equal(4, Assert.IsType<UuidValue>(uuid).Version);
        Assert.True(SemanticTypes.TryConvert("regex", "/a+/i", out object regex, out _));
        Assert.Equal("i", Assert.IsType<RegexValue>(regex).Flags);
        Assert.True(SemanticTypes.TryConvert("password", "s3cret", out object password, out _));
        Assert.Equal("********", Assert.IsType<PasswordValue>(password).ToText());
        Assert.Same(email, SemanticTypes.TryConvert("email", email, out object same, out _) ? same : null);
        Assert.False(SemanticTypes.TryConvert("email", "nope", out _, out string reason));
        Assert.Equal("it has no @", reason);
    }
}
