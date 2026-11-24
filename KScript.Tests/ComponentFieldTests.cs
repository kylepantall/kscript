using KScript.Pages;
using KScript.Runtime;

namespace KScript.Tests;

public class ComponentFieldTests
{
    private const string Source = """
        import KScript.Web

        type NoteEditor extends Component {
          label: string
          title: string
          count: int
          done: bool
          labels: string[]
          options: map
          description?: string
          color: color = 'yellow'
          revision: int = 3

          fn increment() {
            count += 1
          }

          fn render(): Component {
            return ```html
            <p>{$label} {$count} {$revision} {$color}</p>
            ```
          }
        }
        """;

    private static TempSite Site(string source = Source) => new(("_components/note-editor.kscript", source));

    private static ComponentTemplate Mount(PageContext ctx, Dictionary<string, object> props) =>
        ctx.Runtime.RenderComponent(ctx, "_components/note-editor.kscript", "note-editor", props, out _);

    [Fact]
    public void Each_instance_has_its_own_typed_defaults_and_supplied_props()
    {
        using var site = Site();
        var ctx = new PageContext(site.Root);
        var first = Mount(ctx, new() { ["label"] = "First" });
        var second = Mount(ctx, new() { ["label"] = "Second" });
        Assert.True(first.Scope.TryGet("this", out object self));
        var record = Assert.IsType<KScript.Runtime.Record>(self);
        Assert.Equal("First", record["label"]);
        Assert.Equal(string.Empty, record["title"]);
        Assert.Equal(0d, record["count"]);
        Assert.Equal(false, record["done"]);
        Assert.Null(record["description"]);
        Assert.Equal(3d, record["revision"]);
        Assert.Equal("#ffff00", Values.ToStr(record["color"]));
        Assert.True(second.Scope.TryGet("labels", out object labels));
        Assert.Empty(Assert.IsType<List<object>>(labels));
        Assert.NotSame(record["labels"], labels);
        Assert.True(second.Scope.TryGet("options", out object options));
        Assert.Empty(Assert.IsType<Dictionary<string, object>>(options));
        Assert.NotSame(record["options"], options);

        ctx.Runtime.Execute("count = '41'", first.Scope, first.Module, declare: false);
        ctx.Runtime.Evaluate("$this->increment()", first.Scope, first.Module);
        Assert.Equal(42d, record["count"]);
        Assert.True(first.Scope.TryGet("count", out object count));
        Assert.Equal(42d, count);
        Assert.True(second.Scope.TryGet("count", out object untouched));
        Assert.Equal(0d, untouched);
    }

    [Fact]
    public void Callers_can_seed_fields_and_omit_primitive_inputs()
    {
        using var site = Site();
        site.Write("index.html", "<note-editor label=\"Editor\" count=\"7\" />");
        Assert.Contains("<p>Editor 7 3 #ffff00</p>", site.Render("index.html").Body);
        site.Write("index.html", "<note-editor />");
        string html = site.Render("index.html").Body;
        Assert.Contains("<p> 0 3 #ffff00</p>", html);
        Assert.DoesNotContain("data-ks-live", html);
    }

    [Fact]
    public void An_uninitialized_domain_type_explains_how_to_supply_a_default()
    {
        using var site = Site(Source.Replace("color: color = 'yellow'", "color: color"));
        var ctx = new PageContext(site.Root);
        var error = Assert.Throws<PageException>(() => Mount(ctx, new() { ["label"] = "Editor" }));
        Assert.Contains("NoteEditor.color needs an initial value for color", error.Message);
        var seeded = Mount(ctx, new() { ["color"] = "green" });
        Assert.True(seeded.Scope.TryGet("color", out object color));
        Assert.Equal("#008000", Values.ToStr(color));
    }

    [Fact]
    public void Mapped_fields_use_the_supplied_path_before_a_type_default()
    {
        using var site = Site(Source.Replace("label: string", "label: string from details.caption"));
        var ctx = new PageContext(site.Root);
        var component = Mount(ctx, new() { ["details"] = new Dictionary<string, object> { ["caption"] = "Mapped" } });
        Assert.True(component.Scope.TryGet("label", out object label));
        Assert.Equal("Mapped", label);
        Assert.True(Mount(ctx, new()).Scope.TryGet("label", out label));
        Assert.Equal(string.Empty, label);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("any", false)]
    [InlineData("any?", true)]
    [InlineData("color", false)]
    [InlineData("color[]", true)]
    [InlineData("string(min: 1)", false)]
    public void Required_prop_detection_uses_the_component_initializer(string type, bool hasDefault) =>
        Assert.Equal(hasDefault, TemplateContract.HasImplicitFieldDefault(TypeRef.Parse(type)));
}
