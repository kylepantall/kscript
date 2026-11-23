using KScript.Runtime;

namespace KScript.Tests;

public sealed class RefinementMethodTests
{
    private static string Run(string code) =>
        ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "refinement-methods.kscript"));

    [Theory]
    [InlineData("fn (c) => $c->trim()")]
    [InlineData("fn(c) => $c->trim()")]
    [InlineData("@trim_key")]
    [InlineData("$trim_key_callback")]
    public void NormalizerCallbacksReceiveTheInputBeforeValidationAndTypedMethodCalls(string normalizer)
    {
        string printed = Run($$"""
            fn trim_key(text: string, suffix: string = '') => $text->trim() + $suffix
            trim_key_callback = @trim_key
            type PasskeySessionKey with [string] {
              min: 8
              normalize: {{normalizer}}
              pattern: /^passkey_[a-zA-Z]+$/
              fn break() {
                return $value->count()
              }
            }
            fn key_length(key: PasskeySessionKey) => $key->break()
            key = PasskeySessionKey('  passkey_Login  ')
            echo $key
            echo $key->break()
            echo @key_length($key)
            echo @key_length(' passkey_test ')
            """);
        Assert.Equal("passkey_Login\n13\n13\n12\n", printed);
    }

    [Theory]
    [InlineData("@trim_key($value)")]
    [InlineData("@trim_key")]
    public void NormalizersCanCallTheirOwnLaterMethodInPreferenceToAModuleFunction(string normalizer)
    {
        string printed = Run($$"""
            fn trim_key(key: string) => 'global'
            type PasskeySessionKey with [string] {
              min: 8
              normalize: {{normalizer}}
              pattern: /^passkey_[a-zA-Z]+$/
              fn trim_key(key: string) => $key->trim()
            }
            key = PasskeySessionKey(' passkey_login ')
            echo $key
            echo $key->trim_key(' other ')
            echo PasskeySessionKey->check(' passkey_abc123 ')->count()
            echo @trim_key('outside')
            """);
        Assert.Equal("passkey_login\nother\n1\nglobal\n", printed);
    }

    [Fact]
    public void AnInheritedNormalizerCannotLeakAReceiverThatSkipsNormalization()
    {
        string printed = Run("""
            state = {}
            type Key with [string] {
              fn clean(text: string) {
                state.saved = $this
                return $text->trim()
              }
            }
            type ChildKey with [Key] {
              normalize: @clean
            }
            key = ChildKey(' login ')
            echo "[{ $key }]"
            echo "[{ $state.saved }]"
            echo "[{ ChildKey($state.saved) }]"
            """);
        Assert.Equal("[login]\n[ login ]\n[login]\n", printed);
    }

    [Fact]
    public void IdentityNormalizationPreservesAFunctionAsTheInput()
    {
        string printed = Run("""
            type Callback with [fn] { normalize: $value }
            callback: Callback = fn(x) => $x + 1
            echo $callback(3)
            """);
        Assert.Equal("4\n", printed);
    }

    [Fact]
    public void MethodsUseTheirClosureAndTheNormalizedReceiver()
    {
        string printed = Run("""
            prefix = 'key'
            type KeyText with [string] {
              normalize: $value->trim()
              pattern: /^[a-z]+$/
              fn label(suffix: string = '!') {
                return "{$prefix}:{$value}{$suffix}"
              }
              fn upper() => $value->upper() + ':' + $this->label(suffix: '?')
            }
            key: KeyText = ' login '
            echo $key->label()
            echo $key->upper()
            echo $key |> label('?')
            """);
        Assert.Equal("key:login!\nLOGIN:key:login?\nkey:login?\n", printed);
    }

    [Fact]
    public void InheritedMethodsUseOverridesIncludingThroughATypedParameter()
    {
        string printed = Run("""
            type Ticket with [int] {
              min: 1
              fn number() => $value
              fn label() => "ticket-{$this->number()}"
            }
            type PriorityTicket with [Ticket] {
              max: 100
              fn number() => $value + 10
            }
            fn display(ticket: Ticket) => $ticket->label()
            t = 5 as PriorityTicket
            echo $t->label()
            echo @display($t)
            echo Ticket(5)->label()
            """);
        Assert.Equal("ticket-15\nticket-15\nticket-5\n", printed);
    }

    [Fact]
    public void AnInheritedLibraryMethodUsesTheDeclaringModule()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-refinement-import-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "base.kscript"), """
                echo 'loading library'
                prefix = 'library'
                type TicketCode with [string] { fn show() => $prefix + ':' + $value }
                """);
            string printed = ScriptRunner.Run("""
                import TicketCode from "base.kscript"
                prefix = 'caller'
                type SupportTicket with [TicketCode] { min: 1 }
                echo SupportTicket('one')->show()
                """, path: Path.Combine(dir, "main.kscript"));
            Assert.Equal("loading library\nlibrary:one\n", printed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RecheckingAnAlreadyTypedValuePreservesItsCanonicalForm()
    {
        string printed = Run("""
            type Key with [string] {
              normalize: $value + '!'
              fn show() => $value
            }
            type ChildKey with [Key] { min: 1 }
            fn show(key: Key) => $key->show()
            key = Key('test')
            child = ChildKey($key)
            echo @show($child)
            """);
        Assert.Equal("test!\n", printed);
    }

    [Fact]
    public void MethodBearingValuesSurviveMappingAndKeepTheirBaseOperations()
    {
        string printed = Run("""
            type Alias with [string] { fn title() => $value->upper() }
            type Labels with [Alias[]] { fn first_title() => $value[0]->title() }
            type Row { aliases: Labels; extra: any }
            fn keep(v: any) => $v
            row = Row(aliases: ['one', 'two'], extra: Alias('three'))
            echo $row.aliases->first_title()
            echo $row.aliases->count()
            echo @keep($row.extra)->title()
            echo $row->to_json()
            """);
        Assert.Equal("ONE\n2\nTHREE\n{\"aliases\":[\"one\",\"two\"],\"extra\":\"three\"}\n", printed);
    }

    [Fact]
    public void XmlSyntaxAndEnumBasesCanDeclareMethods()
    {
        string printed = ScriptRunner.RunBody("""
            <enum name="Size" values="small, large"/>
            <type name="ShirtSize" with="Size">
              <fn name="label" value="$value->upper()"/>
            </type>
            <let name="size" type="ShirtSize" value="'SMALL'"/>
            <echo value="$size->label()"/>
            """);
        Assert.Equal("SMALL\n", printed);
    }

    [Fact]
    public void AMethodDoesNotBypassTheBaseOrDerivedRules()
    {
        KScriptException error = Assert.Throws<KScriptException>(() => Run("""
            type Positive with [int] { min: 1; fn double_it() => $value * 2 }
            type Small with [Positive] { max: 9 }
            echo Small(10)->double_it()
            """));
        Assert.Equal("Shape", error.Type);
        Assert.Contains("at most 9", error.Message);
    }
}
