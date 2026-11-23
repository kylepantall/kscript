using KScript.Runtime;

namespace KScript.Tests;

public sealed class DeclarationCatchTests
{
    private static string Run(string code) =>
        ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "declaration-catches.kscript"));

    [Fact]
    public void StatelessServicesCanBeConstructedWithNoArgumentsOrAnEmptyMap()
    {
        string output = Run("""
            type Service { fn read() { throw Network "offline" } }
            catch Network { return 'cached' }
            type Child with [Service] {}
            echo Service()->read()
            echo Service({})->read()
            echo Child()->read()
            """);
        Assert.Equal("cached\ncached\ncached\n", output);
    }

    [Theory]
    [InlineData("type Empty {}")]
    [InlineData("type Empty {} catch Network { return 'unused' }")]
    public void ATypeNeedsFieldsOrMethods(string source) =>
        Assert.Equal("Syntax", Assert.Throws<KScriptException>(() => Run(source)).Type);

    [Theory]
    [InlineData("")]
    [InlineData("return")]
    [InlineData("return null")]
    [InlineData("return $fallback")]
    public void MissingRecoveryPreservesTheOriginalErrorAndSkipsSiblings(string recovery)
    {
        string output = Run($$"""
            fallback = null
            error Unavailable(status: 503) extends Network { code: string }
            type Service {
              fn read() {
                try { throw NotFound "original cause" }
                catch err { throw Unavailable(code: 'denied', message: 'original message', cause: $err) }
              } catch Network {
                echo 'method observed'
                {{recovery}}
              } catch Network { return 'wrong method clause' }
            } catch Network {
              echo 'type observed'
              {{recovery}}
            } catch Network { return 'wrong type clause' }
            try { Service()->read() }
            catch Unavailable as err {
              echo "{$err.type} {$err.status} {$err.code}: {$err.message}; {$err.cause.message}"
            }
            """);
        Assert.Equal("method observed\ntype observed\nUnavailable 503 denied: original message; original cause\n", output);
    }

    [Fact]
    public void FalsyNonNullFallbacksRecoverAndOrdinaryTryCatchesMayFinishOrReturnNull()
    {
        string output = Run("""
            fn recover(fallback) { throw Network "offline" } catch Network { return $fallback }
            for fallback in [false, 0, '', [], {}] { echo @recover($fallback)->to_json() }
            fn nullable() {
              try { throw Network "offline" } catch Network { return null }
            }
            try { throw Network "offline" } catch Network { echo 'observed' }
            echo @nullable() == null
            """);
        Assert.Equal("false\n0\n\"\"\n[]\n{}\nobserved\ntrue\n", output);
    }

    [Fact]
    public void FunctionsCanFilterAndReturnFromBodyOrArgumentBindingErrors()
    {
        string output = Run("""
            error Offline extends Network
            fn unavailable() { throw Offline "down" }
            fn resolve(mode: string) => @unavailable() catch Network as e when $mode == 'cache' {
              return $mode + ': ' + $e.message
            } catch Network {
              return $err.type
            }
            fn numeric(n: int) => $n catch err { return 'invalid' }
            echo @resolve('cache')
            echo @resolve('off')
            echo @numeric(2)
            echo @numeric('oops')
            """);
        Assert.Equal("cache: down\nOffline\n2\ninvalid\n", output);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("model")]
    public void MethodCatchRethrowsToReceiverAndThenCaller(string declaration)
    {
        string code = $$"""
            import KScript.Data
            {{declaration}} Endpoint {
              label: string
              fn read() { throw Network "offline" } catch Network as e {
                echo 'method: ' + $e.message
                throw
              }
            } catch Network as e {
              echo $this.label + ': ' + $e.message
              throw
            }
            try {
              Endpoint(label: 'type')->read()
            } catch Network as e {
              echo 'caller: ' + $e.message
            }
            """;
        Assert.Equal("method: offline\ntype: offline\ncaller: offline\n", Run(code));
    }

    [Fact]
    public void InheritedCatchUsesItsOwnModuleAndTheActualRefinedReceiver()
    {
        string dir = Directory.CreateTempSubdirectory("kscript-catch-library-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "base.kscript"), """
                prefix = 'library'
                fn offline() { throw Network "offline" }
                type BaseKey with [string] {
                  fn read() { throw Network "offline" }
                  fn label() => 'base'
                } catch Network {
                  return $prefix + ':' + $this->label() + ':' + $value
                }
                type BaseService {
                  name: string
                  fn read() => @offline()
                } catch Network {
                  return $prefix + ':' + $this.name
                }
                """);
            string result = ScriptRunner.Run("""
                import BaseKey, BaseService from "base.kscript"
                prefix = 'caller'
                type ChildKey with [BaseKey] { fn label() => 'child' }
                type ChildService with [BaseService] { }
                echo ChildKey('login')->read()
                echo ChildService(name: 'service')->read()
                """, path: Path.Combine(dir, "main.kscript"));
            Assert.Equal("library:child:login\nlibrary:service\n", result);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void XmlFunctionsAndTypeCatchesUseTheSameHandlerOrder()
    {
        Assert.Equal("local: down\n", ScriptRunner.RunBody("""
            <fn name="lookup"><throw type="Network" message="down"/></fn>
            <type name="Endpoint" fields="label: string">
              <fn name="read" value="@lookup()"><catch type="Network"><throw/></catch></fn>
              <catch type="Network" as="caught"><return value="$label + ': ' + $caught.message"/></catch>
            </type>
            <echo value="Endpoint(label: 'local')->read()"/>
            """));
    }
}
