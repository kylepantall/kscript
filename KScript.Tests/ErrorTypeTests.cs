using KScript.Http;
using KScript.Pages;
using KScript.Runtime;
using KScript.Testing;

namespace KScript.Tests;

/// <summary>Error types: declaring them, throwing and catching them by type, rethrowing, and their HTTP status.</summary>
public class ErrorTypeTests
{
    private static string Run(string code) => ScriptRunner.Run(code, path: Path.Combine(Path.GetTempPath(), "errors.kscript"));

    private static KScriptException Fails(string code) => Assert.Throws<KScriptException>(() => Run(code));

    private const string Declarations = """
        /// Nothing has that id.
        error PersonNotFound(message: "No person with id {$id}") extends NotFound {
          id: int
        }
        error OutOfStock(status: 409, message: "{$item} is out of stock") {
          item: string
          left: int = 0
        }
        error Plain

        """;

    [Fact]
    public void ThrowingADeclaredErrorFillsInItsMessageFieldsAndStatus()
    {
        string output = Run(Declarations + """
            try {
              throw PersonNotFound(id: 42)
            } catch NotFound as e {
              echo "{ $e.message } | { $e.type } | { $e.status } | { $e.id } | { $e->is(NotFound) } | { $e->is(PersonNotFound) } | { $e->is('Validation') } | { $e->chain() } | { $e->type() }"
            }
            """);
        Assert.Equal("No person with id 42 | PersonNotFound | 404 | 42 | true | true | false | PersonNotFound, NotFound, Error | error\n", output);
    }

    [Fact]
    public void CatchesAreTriedInOrderAndMatchSubtypes()
    {
        string output = Run(Declarations + """
            fn attempt(which) {
              try {
                if $which == 1 {
                  @raise_stock()
                }
                if $which == 2 {
                  x = 'nope'->json()
                }
                @missing_file()
              } catch OutOfStock as e {
                return 'stock: ' + $e.item + ' left ' + $e.left
              } catch Format | IO as e {
                return 'format or io: ' + $e.type
              }
            }
            fn raise_stock() {
              throw OutOfStock(item: "tea")
            }
            fn missing_file() {
              throw FileNotFound "gone"
            }
            echo @attempt(1)
            echo @attempt(2)
            echo @attempt(3)
            """);
        Assert.Equal("stock: tea left 0\nformat or io: Json\nformat or io: FileNotFound\n", output);
    }

    [Fact]
    public void BareThrowRethrowsAndCauseWraps()
    {
        string output = Run(Declarations + """
            error ConfigBroken(message: "Can't read {$path}") { path: string }
            try {
              try {
                throw IO "disk full"
              } catch IO {
                echo "first: { $err.message }"
                throw
              }
            } catch e {
              echo "again: { $e.type } { $e.message } { $e.line }"
            }
            try {
              try {
                throw FileNotFound "no config.json"
              } catch err {
                throw ConfigBroken(path: "config.json", cause: $err)
              }
            } catch ConfigBroken as e {
              echo "{ $e.message } because { $e.cause.type }: { $e.cause.message }"
            }
            saved = $OutOfStock->new(item: "milk", left: 2)
            echo "{ $saved.message } { $saved.status }"
            try {
              throw $saved
            } catch OutOfStock as e {
              echo "saved { $e.left }"
            }
            """);
        Assert.Equal("first: disk full\nagain: IO disk full 13\nCan't read config.json because FileNotFound: no config.json\nmilk is out of stock 409\nsaved 2\n", output);
    }

    [Fact]
    public void ErrorTypesAreValuesWithMembers()
    {
        string output = Run(Declarations + """
            echo "{ $PersonNotFound.name } { $PersonNotFound.extends } { $PersonNotFound.status } { $PersonNotFound.description }"
            echo "{ $OutOfStock.fields->map(fn($f) => $f.name + ':' + $f.type) } { $OutOfStock.fields->count() } { $Plain.extends } { $Plain.status ?? 'none' }"
            echo "{ NotFound.status } { NotFound.extends } { Shape.extends } { $Plain->type() }"
            """);
        Assert.Equal("PersonNotFound NotFound 404 Nothing has that id.\nitem:string, left:int 2 Error none\n404 Error Type error_type\n", output);
    }

    [Fact]
    public void BuiltInTypesTakeAMessageAndExtraFields()
    {
        string output = Run("""
            try {
              throw Validation(field: "age", message: "Too young")
            } catch Validation as e {
              echo "{ $e.message } { $e.field } { $e.status }"
            }
            try {
              throw Forbidden("Admins only")
            } catch e {
              echo "{ $e.type } { $e.message } { $e.status }"
            }
            try {
              throw Conflict
            } catch e {
              echo "{ $e.type } { $e.message }"
            }
            try {
              throw NotFound "No page"
            } catch Error as e {
              echo "{ $e.type } { $e.message }"
            }
            try {
              throw
            } catch e {
              echo "{ $e.type } { $e.message }"
            }
            """);
        Assert.Equal("Too young age 422\nForbidden Admins only 403\nConflict Conflict\nNotFound No page\nError Error\n", output);
    }

    [Fact]
    public void UndeclaredTypesStillWork()
    {
        string output = Run("""
            try {
              throw Oops "it broke"
            } catch Oops as e {
              echo "{ $e.type } { $e.message } { $e->chain() } { $e.status ?? 'none' }"
            }
            try {
              throw Oops
            } catch Error as e {
              echo "{ $e.message }"
            }
            """);
        Assert.Equal("Oops it broke Oops, Error none\nOops\n", output);
    }

    [Fact]
    public void XmlFormsWork()
    {
        string output = ScriptRunner.Run("""
            <kscript>
              <error name="Missing" extends="NotFound" status="410" message="{$what} is gone" description="Gone for good.">
                <field name="what" type="string" />
              </error>
              <try>
                <throw value="Missing(what: 'The page')" />
                <catch type="[NotFound, IO]" as="e"><echo>{ $e.message } { $e.status }</echo></catch>
              </try>
              <try>
                <throw type="NotFound" message="custom" />
                <catch type="Missing, NotFound" as="e"><echo>{ $e.message }</echo></catch>
              </try>
              <try>
                <try>
                  <throw type="IO">disk</throw>
                  <catch as="inner"><throw type="Validation" cause="$inner">wrapped</throw></catch>
                </try>
                <catch as="e"><echo>{ $e.message } { $e.cause.message }</echo></catch>
              </try>
              <try>
                <try>
                  <throw type="Oops">raw</throw>
                  <catch as="inner"><throw type="Weird" cause="$inner">ad hoc</throw></catch>
                </try>
                <catch as="e"><echo>{ $e.type } { $e.cause.type }</echo></catch>
              </try>
              <try>
                <throw value="{ type: 'Mapped', message: 'from a map', code: 7 }" />
                <catch as="e"><echo>{ $e.type } { $e.message } { $e.code }</echo></catch>
              </try>
              <try>
                <throw value="'just text'" />
                <catch as="e"><echo>{ $e.type } { $e.message }</echo></catch>
              </try>
              <try>
                <throw value="NotFound" />
                <catch as="e"><echo>{ $e.type }</echo></catch>
              </try>
              <try>
                <throw type="NotFound" cause="{ type: 'Old', message: 'before', note: 1 }" message="after" />
                <catch as="e"><echo>{ $e.cause.type } { $e.cause.note }</echo></catch>
              </try>
              <try>
                <throw value="Validation('x', cause: 'plain text cause')" />
                <catch as="e"><echo>{ $e.cause.message }</echo></catch>
              </try>
            </kscript>
            """);
        Assert.Equal("The page is gone 410\ncustom\nwrapped disk\nWeird Oops\nMapped from a map 7\nError just text\nNotFound\nOld 1\nplain text cause\n", output);
    }

    [Theory]
    [InlineData("throw PersonNotFound()", "PersonNotFound() was given fields that don't fit it:\n  id: needs an int, but it is missing.\nIts fields are: id: int.")]
    [InlineData("throw PersonNotFound(id: 1, idd: 2)", "idd")]
    [InlineData("throw Plain(x: 1)", "Plain has no fields.")]
    [InlineData("throw PersonNotFound(1, 2)", "takes its fields by name, such as PersonNotFound(id: ...)")]
    [InlineData("throw Plain(1, 2)", "such as Plain(message: '...')")]
    [InlineData("throw Validation(cause: 3)", "The cause of a Validation is an error")]
    [InlineData("throw $PersonNotFound", "PersonNotFound has fields, so throw it with them: PersonNotFound(id: ...).")]
    [InlineData("n = 42\nthrow $n", "<throw value> needs an error, such as NotFound(id: 42) or a caught $err, not a number (42).")]
    public void MistakesInThrowingAreExplained(string code, string message)
    {
        Assert.Contains(message, Fails(Declarations + code).Message);
    }

    [Fact]
    public void AnUncaughtErrorStopsTheScriptWithItsLocation()
    {
        KScriptException ex = Fails(Declarations + "throw PersonNotFound(id: 7)");
        Assert.Equal("PersonNotFound", ex.Type);
        Assert.Equal(10, ex.Line);
        Assert.Equal(404, ErrorTypes.StatusOf(ex));
        Assert.Contains("errors.kscript:10:1: PersonNotFound: No person with id 7", ex.ToString());
    }

    [Theory]
    [InlineData("error lower", "start with a capital letter")]
    [InlineData("error NotFound", "NotFound is a built-in error type")]
    [InlineData("error Bad(status: 99)", "isn't an HTTP status")]
    [InlineData("error Bad(status: abc)", "isn't an HTTP status")]
    [InlineData("error Bad { message: string }", "'message' is part of every error")]
    [InlineData("error Bad extends Nope\nthrow Bad()", "extends 'Nope', which isn't an error type. Declare it")]
    [InlineData("error Bad extends Notfoundd\nthrow Bad()", "Did you mean NotFound?")]
    [InlineData("error A extends B\nerror B extends A\nthrow A()", "extends itself through B")]
    public void DeclarationMistakesAreExplained(string code, string message)
    {
        Assert.Contains(message, Fails(code).Message);
    }

    [Fact]
    public void ThrowValueCantAlsoHaveATypeOrMessage()
    {
        KScriptException missing = ScriptRunner.Fails("<error name=\"Missing\" fields=\"what: string\" /><throw type=\"Missing\" message=\"x\" />");
        Assert.Equal("Argument", missing.Type);
        Assert.Contains("Missing() was given fields that don't fit it", missing.Message);
        Assert.Contains("<throw value> throws the error it is given", ScriptRunner.Fails("<throw value=\"NotFound()\" type=\"X\" />").Message);
        Assert.Contains("<throw value> throws the error it is given", ScriptRunner.Fails("<throw value=\"NotFound()\">text</throw>").Message);
        Assert.Contains("cause is the error that led to this one", ScriptRunner.Fails("<throw type=\"Oops\" cause=\"3\" />").Message);
        Assert.Contains("->is() takes an error type", ScriptRunner.Fails("<try><throw /><catch as=\"e\"><echo>{ $e->is(3) }</echo></catch></try>").Message);
    }

    [Fact]
    public void ErrorTypesCanExtendOnesDeclaredLaterAndAreExported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-errors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "errors.kscript"), "error Child extends Parent { id: int }\nerror Parent(status: 418, message: \"teapot {$id}\") { id: int }\n");
            File.WriteAllText(Path.Combine(dir, "main.kscript"), "import \"errors.kscript\"\ntry {\n  throw Child(id: 3)\n} catch Parent as e {\n  echo \"{ $e.message } { $e.status }\"\n}\n");
            var output = new StringWriter { NewLine = "\n" };
            new Interpreter(KScriptHost.ForText(output, "")).RunFile(Path.Combine(dir, "main.kscript"));
            Assert.Equal("teapot 3 418\n", output.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ErrorValuesHelpers()
    {
        var error = new KScriptException("boom", "Network");
        Assert.True(ErrorTypes.Matches(error, "Error"));
        Assert.True(ErrorTypes.Matches(error, " network "));
        Assert.False(ErrorTypes.Matches(error, "IO"));
        Assert.Null(ErrorTypes.StatusOf(error));
        Assert.Equal(new[] { "Weird", "Error" }, ErrorTypes.Chain(new KScriptException("x", "Weird")));
        Assert.Null(ErrorTypes.Find("", null));
        Assert.Null(ErrorTypes.Find("Nope", null));
        Assert.Equal("Network", ErrorTypes.Find("network", null).Name);
        Assert.Null(ErrorTypes.FindExact("network", null));
        Assert.Contains("Validation", ErrorTypes.Names(null));
        Assert.True(error.ToValue().Is("Network"));
        Assert.Null(error.Cause);
        Assert.Null(error.Declared);
        ErrorType notFound = ErrorTypes.BuiltIn["NotFound"];
        Assert.True(notFound.IsBuiltIn);
        Assert.True(notFound.IsA("error"));
        Assert.False(notFound.TryGetMember("nope", out _));
        Assert.False(notFound.TryCallMethod(null, "nope", out _));
        Assert.Equal("NotFound", notFound.ToText());
        Assert.Equal("Error", ErrorTypes.FromMap(new Dictionary<string, object> { ["message"] = null }).Type);
    }

    [Fact]
    public void TestsCheckErrorTypes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kscript-error-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "errors.test.kscript");
            File.WriteAllText(file, Declarations + """
                fn find(id: int) {
                  throw PersonNotFound(id: $id)
                }
                test(name: "subtypes count") {
                  expect_error(type: NotFound, as: e) {
                    @find(1)
                  }
                  expect(value: $e.id, equals: 1)
                  expect(value: $e, type: error)
                }
                test(name: "throws") {
                  expect(value: @find(2), throws: PersonNotFound)
                  expect(value: 'nope'->json(), throws: Format)
                }
                test(name: "throws the wrong type") {
                  expect(value: @find(3), throws: IO)
                }
                test(name: "throws nothing") {
                  expect(value: 1 + 1, throws: IO)
                }
                test(name: "expect_error the wrong type") {
                  expect_error(type: IO) {
                    @find(4)
                  }
                }
                test(name: "a failed check is not the error") {
                  expect(value: @fail('inner'), throws: Error)
                }
                test(name: "unless it asks for Assertion") {
                  expect(value: @fail('inner'), throws: Assertion)
                }
                """);
            var output = new StringWriter();
            int code = TestCommand.Run(new[] { file }, output, new StringWriter());
            string text = output.ToString();
            Assert.Equal(1, code);
            Assert.Contains("4 failed, 3 passed", text);
            Assert.Contains("Expected @find(3) to throw an IO error, but it threw PersonNotFound: No person with id 3", text);
            Assert.Contains("Expected 1 + 1 to throw an IO error, but it was 2.", text);
            Assert.Contains("Expected an IO error, but got PersonNotFound: No person with id 4", text);
            Assert.Contains("inner", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

/// <summary>KScript.Http answers an uncaught error with the status its type carries.</summary>
public class ErrorStatusTests
{
    private const string Controller = """
        error PersonNotFound(message: "No person with id {$id}") extends NotFound {
          id: int
        }
        error PaymentFailed(status: 402)

        fn show(id: int) {
          throw PersonNotFound(id: $id)
        }
        fn pay() {
          throw PaymentFailed()
        }
        fn forbid() {
          throw Forbidden "Admins only"
        }
        fn caught() {
          try {
            throw Conflict "x"
          } catch Conflict {
            return { ok: true }
          }
        }
        fn crash() {
          throw Oops "bad"
        }
        """;

    private const string Routes = """
        KScript.Routes {
          import "controllers/people.kscript" as people
          Get(path: "/people/{id}", to: @people.show, where: "id: int")
          Get(path: "/pay", to: @people.pay)
          Get(path: "/forbid", to: @people.forbid)
          Get(path: "/caught", to: @people.caught)
          Get(path: "/crash", to: @people.crash)
        }
        """;

    private static (HttpApp App, TempSite Site, StringWriter Log) Start()
    {
        var site = new TempSite(("controllers/people.kscript", Controller), ("routes.kscript", Routes), ("api.kscript", Routes));
        var log = new StringWriter();
        return (new HttpApp(site.Root, null, log), site, log);
    }

    private static PageResult Get(HttpApp app, string path) =>
        app.Handle(new Dictionary<string, object> { ["method"] = "GET", ["path"] = path, ["body"] = string.Empty, ["headers"] = new Dictionary<string, object>(), ["query"] = new Dictionary<string, object>() });

    [Fact]
    public void PagesAnswerWithTheStatus()
    {
        var (app, site, log) = Start();
        using (site)
        {
            PageResult notFound = Get(app, "/people/7");
            Assert.Equal(404, notFound.Status);
            Assert.Contains("No person with id 7", notFound.Body);
            Assert.DoesNotContain("people.kscript", notFound.Body);
            Assert.Equal(402, Get(app, "/pay").Status);
            Assert.Equal(403, Get(app, "/forbid").Status);
            Assert.Equal(200, Get(app, "/caught").Status);
            PageResult crash = Get(app, "/crash");
            Assert.Equal(500, crash.Status);
            Assert.Contains("people.kscript", crash.Body);
            Assert.Contains("404 PersonNotFound: No person with id 7", log.ToString());
        }
    }

    [Fact]
    public void ApiRoutesAnswerJsonWithTheTypeAndFields()
    {
        var (app, site, _) = Start();
        using (site)
        {
            PageResult notFound = Get(app, "/api/people/7");
            Assert.Equal(404, notFound.Status);
            Assert.Equal("{\"error\":\"No person with id 7\",\"status\":404,\"type\":\"PersonNotFound\",\"id\":7}", notFound.Body);
            Assert.Equal("{\"error\":\"PaymentFailed\",\"status\":402,\"type\":\"PaymentFailed\"}", Get(app, "/api/pay").Body);
        }
    }
}
