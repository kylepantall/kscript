using KScript.Data;
using KScript.Http;
using KScript.Pages;
using KScript.Projects;
using KScript.Runtime;

namespace KScript.Tests;

public sealed class ModelFormTests : DataFixture
{
    private const string View = "/edit";
    private const string Action = "/_forms/edit_note";
    private readonly HttpApp app;
    private readonly ModelStore notes;

    public ModelFormTests()
    {
        FileOf("kscript.json", """{ "name": "binding-test", "http": { "auth": { "folder": "storage", "csrf": false } } }""");
        FileOf("models/note.kscript", """
            import KScript.Data
            model Note(guarded: [admin], hidden: [secret]) extends [Timestamps, SoftDeletes] {
              user_id: string
              title: string(max: 15)
              score: int
              admin: bool = false
              secret: string = 'private'
            }
            """);
        FileOf("routes/web.kscript", """
            import KScript.Http
            import KScript.Auth
            Routes {
              Get(path: "/login-test/{id}") {
                @login({ id: $id })
                return 'signed in'
              }
              Get(path: "/edit", view: "edit", middleware: [cache_response:1m], cache: "public, max-age=600")
              ModelUpdate(name: edit_note, model: Note, owner: user_id, fields: [title, score])
            }
            """);
        FileOf("views/edit.html", """<note-editor item="1" />""");
        FileOf("_components/note-editor.html", """
            <form model-update="edit_note" key="{$item}" class="editor">
              <input name="title"><button>Save</button>
            </form>
            """);
        Run("""
            import "models/note.kscript"
            $db->create_table(Note)
            Note->create(user_id: 'alice', title: 'Original', score: 1)
            Note->create(user_id: 'bob', title: 'Other', score: 2)
            Note->create(user_id: 'alice', title: 'Deleted', score: 3)->delete()
            """);
        app = new HttpApp(Project, ProjectConfig.FindAndLoad(Project));
        notes = ModelStore.Load(KScriptHost.ForText(new StringWriter()), Project, "Note", "test");
    }

    [Fact]
    public void ComponentRendersANativeFormAndSavesOnlySubmittedFields()
    {
        var browser = new Browser(app);
        Assert.Equal(200, browser.Get("/login-test/alice").Status);
        PageResult page = browser.Get(View);
        Assert.Contains($"<form method=\"post\" action=\"{Action}\" data-ks-model-form class=\"editor\">", page.Body);
        Assert.Contains("name=\"_key\" value=\"1\"", page.Body);
        Assert.NotEmpty(browser.Token);
        Assert.Equal("private, no-store", page.Headers["Cache-Control"]);
        var other = new Browser(app);
        other.Get("/login-test/bob");
        Assert.Equal("MISS", other.Get(View).Headers["X-Cache"]);
        Assert.NotEmpty(other.Token);
        Assert.NotEqual(browser.Token, other.Token);
        PageResult saved = browser.Submit(View, Action, ("_key", "1"), ("_return", "/edit?tab=notes"), ("title", "Changed"));
        Assert.True(saved.Status == 303, saved.Body);
        Assert.Equal("/edit?tab=notes", saved.Headers["Location"]);
        Assert.Equal("Changed", notes.Find("1")["title"]);
        Assert.Equal(1d, notes.Find("1")["score"]);
        Assert.Equal("/", browser.Submit(View, Action, ("_key", "1"), ("_return", "//example.org"), ("score", "5")).Headers["Location"]);
        Assert.Equal(5d, notes.Find("1")["score"]);
    }

    [Fact]
    public void GeneratedModelComponentUsesTheNamedBindingFieldsAndSaves()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        Assert.True(KScript.Scaffolding.MakeCommand.Run(new[] {
            "component", "NoteEditor", "--model", "Note", "--model-update", "edit_note", "--autosave", "--dir", Project,
        }, output, errors) == 0, errors.ToString());
        FileOf("views/edit.html", """
            <import path="../models/note.kscript" />
            <note-editor record="{Note({ id: 1, user_id: 'alice', title: 'Original', score: 1 })}" />
            """);
        var browser = new Browser(app);
        Assert.Equal(200, browser.Get("/login-test/alice").Status);
        PageResult page = browser.Get(View);
        Assert.True(page.Status == 200, page.Body);
        Assert.Contains("name=\"title\" value=\"Original\"", page.Body);
        Assert.Contains("name=\"score\" value=\"1\"", page.Body);
        Assert.Contains("data-ks-autosave=\"500\"", page.Body);
        Assert.Equal(303, browser.Submit(View, Action, ("_key", "1"), ("title", "Changed")).Status);
        Assert.Equal("Changed", notes.Find("1")["title"]);
    }

    [Fact]
    public void EverySubmissionChecksSessionCsrfOwnershipAndModelRules()
    {
        var browser = new Browser(app);
        Assert.Contains(browser.Submit(View, Action, ("_key", "1"), ("score", "5")).Status, new[] { 302, 401 });
        Assert.Equal(200, browser.Get("/login-test/alice").Status);
        Assert.Equal(419, browser.Send("POST", Action, new() { ["_key"] = "1", ["score"] = "9" }).Status);
        foreach (string key in new[] { "2", "3", "99" })
            Assert.Equal(404, browser.Submit(View, Action, ("_key", key), ("title", "Changed")).Status);
        Assert.Equal(422, browser.Submit(View, Action, ("_key", "1"), ("title", "Changed"), ("admin", "true")).Status);
        Assert.Equal(422, browser.Submit(View, Action, ("_key", "1"), ("title", "Changed"), ("score", "words")).Status);
        Assert.Equal(422, browser.Submit(View, Action, ("_key", "1"), ("title", "Longer than fifteen characters")).Status);
        Assert.Equal(405, browser.Get(Action).Status);
        Assert.Equal("Original", notes.Find("1")["title"]);
        Assert.Equal("Other", notes.Find("2")["title"]);
        Assert.Null(notes.Find("3"));
    }

    [Theory]
    [InlineData("", "500")]
    [InlineData("=\"1.25s\"", "1250")]
    public void AutosaveUsesTheSameBindingAndReturnsModelValidationErrors(string duration, string milliseconds)
    {
        FileOf("_components/note-editor.html", $"<form model-update=\"edit_note\" key=\"1\" autosave{duration}><input name=\"title\"><button>Save</button></form>");
        var browser = new Browser(app);
        browser.Get("/login-test/alice");
        PageResult page = browser.Get(View);
        Assert.Contains($"data-ks-autosave=\"{milliseconds}\"", page.Body);
        Assert.Contains("role=\"status\"", page.Body);
        Assert.Contains("/_ks/live.js", page.Body);
        var headers = new Dictionary<string, object> { ["x-kscript-model-form"] = "save" };
        var form = new Dictionary<string, object> { ["_key"] = "1", ["title"] = "Updated", ["_token"] = browser.Token };
        PageResult saved = browser.Send("POST", Action, form, headers);
        Assert.Equal(204, saved.Status);
        Assert.Empty(saved.Body);
        Assert.Equal("Updated", notes.Find("1")["title"]);
        form["title"] = "Over the model limit";
        PageResult invalid = browser.Send("POST", Action, form, headers);
        Assert.Equal(422, invalid.Status);
        Assert.StartsWith("text/plain", invalid.ContentType);
        Assert.Equal("Updated", notes.Find("1")["title"]);
        form["_token"] = "";
        Assert.Equal(419, browser.Send("POST", Action, form, headers).Status);
    }

    [Fact]
    public void OwnershipIsCheckedAgainAfterThePageWasRendered()
    {
        var browser = new Browser(app);
        browser.Get("/login-test/alice");
        browser.Get(View);
        string token = browser.Token;
        notes.Update(notes.Find("1"), new Dictionary<string, object> { ["user_id"] = "bob" });
        PageResult response = browser.Send("POST", Action, new() { ["_key"] = "1", ["_token"] = token, ["score"] = "9" });
        Assert.Equal(404, response.Status);
        Assert.Equal(1d, notes.Find("1")["score"]);
    }

    [Fact]
    public void ProtectedFieldsCannotBeExposedEvenByAFormDeclaration()
    {
        foreach (string field in new[] { "user_id", "admin", "secret", "id", "created_at", "deleted_at", "missing" })
            Assert.Throws<KScriptException>(() => notes.CheckFormFields("user_id", new HashSet<string> { field }));
    }
}
