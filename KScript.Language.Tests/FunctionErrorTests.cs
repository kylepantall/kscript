using KScript.Language.Agents;
using KScript.Language.Analysis;
using KScript.Language.Features;

namespace KScript.Language.Tests;

public class FunctionErrorTests
{
    private static readonly LanguageService Service = new(files: EmptyFileSystem.Instance);
    private static string[] Errors(DocumentAnalysis analysis, string name) =>
        AgentDocs.SymbolsOf(analysis, "project.kscript").Single(s => s.Name == name).Throws.ToArray();

    [Theory]
    [InlineData("'Network'")]
    [InlineData("\"Network\"")]
    public void AgentDocsFollowCallsAndLiteralErrorMaps(string name)
    {
        string code = $$"""
            fn wrap() => @read_result({})
            fn read_result(reply: map) {
              throw if $reply->has('e') {
                type: {{name}}
                message: 'bad reply'
              }
              return $reply.r
            }
            fn callback() => @wrap
            fn make_callback() => fn() => @wrap()
            """;
        var analysis = Service.Analyze(code, "project.kscript");
        Assert.Equal(new[] { "Network" }, Errors(analysis, "wrap"));
        Assert.Equal(new[] { "Network" }, Errors(analysis, "read_result"));
        Assert.Empty(Errors(analysis, "callback"));
        Assert.Empty(Errors(analysis, "make_callback"));
    }

    [Fact]
    public void CatchesRemoveMatchingSubtypesAndKeepHandlerAndFinallyErrors()
    {
        const string code = """
            error Offline extends Network {}
            fn remote() { throw Offline 'lost connection' }
            fn consume() {
              try { @remote() } catch Network { return null }
            }
            fn guard(ready: bool) {
              try { @remote() } catch Network when $ready { return null }
            }
            fn translate() {
              try { @remote() } catch Network { throw Validation 'invalid' }
              finally { throw Argument 'cleanup' }
            }
            fn again() {
              try { @remote() } catch err { throw }
            }
            fn rethrow_value() {
              try { @remote() } catch Network as problem { throw $problem }
            }
            """;
        var analysis = Service.Analyze(code, "project.kscript");
        Assert.Empty(Errors(analysis, "consume"));
        Assert.Equal(new[] { "Offline" }, Errors(analysis, "guard"));
        Assert.Equal(new[] { "Argument", "Validation" }, Errors(analysis, "translate"));
        Assert.Equal(new[] { "Offline" }, Errors(analysis, "again"));
        Assert.Equal(new[] { "Offline" }, Errors(analysis, "rethrow_value"));
    }

    [Fact]
    public void MethodsAndDeclarationsApplyTheirHandlersBeforeAFunctionReturns()
    {
        const string code = """
            type Service {
              fn fetch() { throw Network 'offline' }
              fn safe() { throw Validation 'invalid' } catch Validation { return false }
            } catch Network { throw Argument 'access denied' }
            type ServiceChild with [Service] {
              fn retry() { throw Network 'offline' }
            }
            fn inherited_handler(child: ServiceChild) => $child->retry()
            fn fetch_name(service: Service) => $service->fetch()
            fn safe_name(service: Service) => $service->safe()
            fn consume(service: Service) => @fetch_name($service)
            catch Argument { return '' }
            """;
        var analysis = Service.Analyze(code, "project.kscript");
        Assert.Equal(new[] { "Argument" }, Errors(analysis, "fetch_name"));
        Assert.Equal(new[] { "Argument" }, Errors(analysis, "inherited_handler"));
        Assert.Empty(Errors(analysis, "safe_name"));
        Assert.Empty(Errors(analysis, "consume"));
        int call = code.IndexOf("->fetch()", StringComparison.Ordinal);
        Assert.Contains("`Argument`", Service.GetHover(analysis, call + 3).Markdown);
        Assert.Contains("`Argument`", Service.GetSignatureHelp(analysis, call + "->fetch(".Length).Signatures[0].Documentation);
    }

    [Fact]
    public void DeclarationHandlersPropagateUnlessTheyRecoverWithANonNullValue()
    {
        const string code = """
            fn remote() { throw Network 'offline' }
            fn log_only() => @remote()
            catch Network { echo 'log' }
            fn dynamic(value: any) => @remote()
            catch Network { return $value }
            fn typed(fallback: string) => @remote()
            catch Network { return $fallback }
            fn conditional(ready: bool) => @remote()
            catch Network { if $ready { return 'ok' } }
            fn early_null(ready: bool) => @remote()
            catch Network { if $ready { return null }; return 'ok' }
            fn choose(ready: bool) => @remote()
            catch Network { if $ready { return 0 } else { return '' } }
            fn wrapped(value: any) => @remote()
            catch Network { return $value ?? false }
            fn ordinary() {
              try { @remote() } catch Network { echo 'handled' }
            }
            """;
        var analysis = Service.Analyze(code, "project.kscript");
        foreach (string name in new[] { "log_only", "dynamic", "conditional", "early_null" })
            Assert.Equal(new[] { "Network" }, Errors(analysis, name));
        foreach (string name in new[] { "choose", "wrapped", "typed", "ordinary" }) Assert.Empty(Errors(analysis, name));
    }

    [Fact]
    public void DescriptionsAndInferredErrorsAreStableAcrossImportsAndHover()
    {
        string dir = Path.Combine(Path.GetTempPath(), "errors-metadata");
        string source = Path.Combine(dir, "main.kscript");
        const string library = """
            /// Looks up a value.
            /// @throws Network The connection closed.
            fn lookup(id: int) { throw Validation 'missing id' }
            """;
        const string code = """
            import "lib.kscript" as sdk
            fn fetch() => @sdk.lookup(1)
            echo @fetch()
            """;
        var files = new MemoryFiles().Add(Path.Combine(dir, "lib.kscript"), library).Add(source, code);
        var service = new LanguageService(files: files);
        for (int pass = 0; pass < 2; pass++)
        {
            var analysis = service.Analyze(code, source);
            int name = code.IndexOf("lookup(", StringComparison.Ordinal);
            string hover = service.GetHover(analysis, name + 2).Markdown;
            string signature = service.GetSignatureHelp(analysis, name + "lookup(".Length).Signatures[0].Documentation;
            foreach (string docs in new[] { hover, signature, service.GetHover(analysis, name + 2).Markdown })
            {
                Assert.Contains("**May throw**", docs);
                Assert.Contains("`Network` — The connection closed.", docs);
                Assert.Contains("`Validation`", docs);
            }
            Assert.Equal(new[] { "Network", "Validation" }, Errors(analysis, "fetch"));
        }
    }
}
