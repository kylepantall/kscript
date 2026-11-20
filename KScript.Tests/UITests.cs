using Avalonia;
using Avalonia.Headless;

using KScript.Runtime;
using KScript.UI;

using static KScript.Tests.ScriptRunner;

namespace KScript.Tests;

/// <summary>KScript.UI: windows drawn from KScript and driven by their handlers, without a screen.</summary>
[Collection("KScript.UI")]
public class UITests
{
    /// <summary>Runs a window script; <paramref name="drive"/> plays the person using it. Returns what the script printed.</summary>
    private static string RunWindow(string xml, Action<UISession> drive)
    {
        Exception failure = null;
        UIPackage.Host = s =>
        {
            try { drive(s); }
            catch (Exception ex) { failure = ex; }
        };
        try
        {
            string output = Run(xml);
            if (failure != null) throw new Xunit.Sdk.XunitException("The window failed: " + failure);
            return output;
        }
        finally
        {
            UIPackage.Host = null;
        }
    }

    private const string Counter = """
        <KScript.Window title="Counter {$count}" width="300">
          <let name="count" value="0" />
          <fn name="label" params="n">
            <return value="'Clicked ' + $n + ' times'" />
          </fn>
          <Column gap="8">
            <Text size="24">{ @label($count) }</Text>
            <Row>
              <Button variant="primary" on-click="count += 1">Add one</Button>
              <Button on-click="count = 0" disabled="$count eq 0">Reset</Button>
            </Row>
          </Column>
        </KScript.Window>
        """;

    [Fact]
    public void ButtonsChangeStateAndTheWindowIsRedrawn()
    {
        RunWindow(Counter, s =>
        {
            Assert.Equal("Clicked 0 times", s.FindAll("Text")[0].Text);
            Assert.True(s.Find("Button", "Reset").Flag("disabled"));
            s.Click("Add one");
            s.Click("Add one");
            Assert.Equal("Clicked 2 times", s.FindAll("Text")[0].Text);
            Assert.Equal("Counter 2", s.Tree.String("title"));
            Assert.False(s.Find("Button", "Reset").Flag("disabled"));
            s.Click("Reset");
            Assert.Equal("Clicked 0 times", s.FindAll("Text")[0].Text);
        });
    }

    [Fact]
    public void LoopsAndConditionsBuildTheLayout()
    {
        const string xml = """
            <KScript.Window>
              <let name="items" value="['a', 'b', 'c']" />
              <let name="show" value="false" />
              <Grid columns="2">
                <for each="item" index="i" in="$items">
                  <Box width="10" height="10" background="{ $i eq 0 ? 'red' : 'blue' }" on-click="items = $items->remove_at($i)" />
                </for>
              </Grid>
              <if test="$show"><Text>Shown</Text></if>
              <CheckBox bind="show">Show</CheckBox>
            </KScript.Window>
            """;
        RunWindow(xml, s =>
        {
            List<UINode> boxes = s.FindAll("Box");
            Assert.Equal(3, boxes.Count);
            Assert.Equal("red", boxes[0].String("background"));
            Assert.Empty(s.FindAll("Text"));
            s.SetBound(s.FindAll("CheckBox")[0], true);
            Assert.Single(s.FindAll("Text"));
            s.Fire(s.FindAll("Box")[1], "on-click");
            Assert.Equal(2, s.FindAll("Box").Count);
        });
    }

    [Fact]
    public void BoundInputsRunOnChangeWithTheValue()
    {
        const string xml = """
            <KScript.Window>
              <let name="name" value="''" />
              <let name="log" value="[]" />
              <TextBox bind="name" on-change="log = $log->push($event.value)" on-enter="name = 'done'" />
              <Text>Hello {$name}</Text>
              <Select bind="speed" options="{ Slow: 400, Fast: 40 }" />
              <let name="speed" value="400" />
            </KScript.Window>
            """;
        // <let> after the layout is still setup: setup elements run first wherever they are.
        RunWindow(xml, s =>
        {
            UINode box = s.FindAll("TextBox")[0];
            s.SetBound(box, "Kyle");
            Assert.Equal("Hello Kyle", s.FindAll("Text")[0].Text);
            Assert.Equal("Kyle", s.FindAll("TextBox")[0].Prop("value"));
            s.Fire(s.FindAll("TextBox")[0], "on-enter");
            Assert.Equal("Hello done", s.FindAll("Text")[0].Text);
            Assert.Equal(400d, Values.ToNumber(s.FindAll("Select")[0].Prop("value"), "speed"));
        });
    }

    [Fact]
    public void TimersTickWhileTheirConditionHolds()
    {
        const string xml = """
            <KScript.Window>
              <let name="frame" value="0" />
              <Timer every="50" when="$frame lt 3" on-tick="frame += 1" />
              <Text>{$frame}</Text>
            </KScript.Window>
            """;
        RunWindow(xml, s =>
        {
            UINode timer = s.FindAll("Timer")[0];
            Assert.Equal(50d, timer.Number("every"));
            while (s.FindAll("Timer")[0].Flag("when")) s.Fire(s.FindAll("Timer")[0], "on-tick");
            Assert.Equal("3", s.FindAll("Text")[0].Text);
        });
    }

    [Fact]
    public void HandlerErrorsAreShownAndTheWindowCarriesOn()
    {
        const string xml = """
            <KScript.Window>
              <let name="n" value="1" />
              <Button on-click="n = $n / $missing">Break</Button>
              <Button on-click="n += 1">Fix</Button>
              <Text>{$n}</Text>
            </KScript.Window>
            """;
        RunWindow(xml, s =>
        {
            s.Click("Break");
            Assert.Contains("missing", s.Error);
            s.Click("Fix");
            Assert.Null(s.Error);
            Assert.Equal("2", s.FindAll("Text")[0].Text);
        });
    }

    [Fact]
    public void TheScriptCarriesOnAfterTheWindowCloses()
    {
        const string xml = """
            <kscript>
              <import package="KScript.UI" />
              <let name="answer" value="0" />
              <KScript.Window>
                <Button on-click="answer = 42">Answer</Button>
              </KScript.Window>
              <echo>answer: $answer</echo>
            </kscript>
            """;
        Assert.Equal("answer: 42\n", RunWindow(xml, s => s.Click("Answer")));
    }

    [Fact]
    public void ControlsOnlyWorkInsideAWindow()
    {
        KScriptException ex = Fails("<Button>Hi</Button>", "KScript.UI");
        Assert.Contains("inside <KScript.Window>", ex.Message);
    }

    [Fact]
    public void BindNeedsADeclaredVariable()
    {
        KScriptException ex = Assert.Throws<KScriptException>(() => RunWindow("<KScript.Window><TextBox bind=\"nope\" /></KScript.Window>", s => { }));
        Assert.Contains("$nope isn't declared", ex.Message);
    }
}

/// <summary>The real Avalonia controls, drawn headlessly.</summary>
[Collection("KScript.UI")]
public class UIDesktopTests
{
    [Fact]
    public void TheWindowShowsControlsAndPatchesThemInPlace()
    {
        const string xml = """
            <KScript.Window title="Cells" width="320" height="240" theme="dark">
              <let name="on" value="[false, false, false, false]" />
              <let name="name" value="''" />
              <Column gap="6">
                <Text weight="bold">{ $on->filter(fn($x) => $x)->count() } on</Text>
                <Grid columns="2" gap="2">
                  <for each="v" index="i" in="$on">
                    <Box width="20" height="20" background="{ $v ? '#22c55e' : '#1e293b' }" on-click="on = $on->map(fn($x, $j) => $j eq $i ? not $x : $x)" />
                  </for>
                </Grid>
                <TextBox bind="name" placeholder="Name" />
                <Button variant="primary" on-click="on = [true, true, true, true]">All</Button>
              </Column>
            </KScript.Window>
            """;
        Exception failure = null;
        string summary = null;
        UIPackage.Host = s => AvaloniaThread.Run(() =>
        {
            try
            {
                AvaloniaThread.Start();
                var view = new WindowView(s);
                view.Window.Show();
                var text = (Avalonia.Controls.TextBlock)view.ControlFor(s.FindAll("Text")[0]);
                Assert.Equal("0 on", text.Text);
                Assert.Equal("Cells", view.Window.Title);

                var button = (Avalonia.Controls.Button)view.ControlFor(s.Find("Button", "All"));
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                // Same control, new text: the redraw patched it rather than replacing it.
                Assert.Same(text, view.ControlFor(s.FindAll("Text")[0]));
                Assert.Equal("4 on", text.Text);

                var box = (Avalonia.Controls.TextBox)view.ControlFor(s.FindAll("TextBox")[0]);
                box.Text = "Kyle";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(s.State.TryGet("name", out object typed) && (string)typed == "Kyle");
                Assert.Same(box, view.ControlFor(s.FindAll("TextBox")[0]));

                using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(view.Window);
                Assert.NotNull(frame);
                summary = $"{frame.PixelSize.Width}x{frame.PixelSize.Height}";
                view.Window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        try
        {
            ScriptRunner.Run(xml);
        }
        finally
        {
            UIPackage.Host = null;
        }
        if (failure != null) throw new Xunit.Sdk.XunitException("The window failed: " + failure);
        Assert.Equal("320x240", summary);
    }
}
