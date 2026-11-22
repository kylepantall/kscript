using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using KScript.Runtime;
using KScript.UI;

namespace KScript.Tests;

/// <summary>
/// Avalonia's UI thread is the thread that started it, so every test that touches a control runs on this one
/// thread, one piece of work at a time.
/// </summary>
internal static class AvaloniaThread
{
    private static readonly BlockingCollection<Action> Work = new();

    static AvaloniaThread()
    {
        new Thread(() => { foreach (Action a in Work.GetConsumingEnumerable()) a(); }, 16 * 1024 * 1024) { IsBackground = true, Name = "Avalonia" }.Start();
    }

    public static void Start() => DesktopHost.Start(b => b.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }));

    public static void Run(Action action)
    {
        Exception failure = null;
        using var done = new ManualResetEventSlim();
        Work.Add(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("The Avalonia thread didn't finish.");
        if (failure != null) ExceptionDispatchInfo.Throw(failure);
    }
}

/// <summary>Every control, layout and timer of KScript.UI, drawn headlessly and used.</summary>
[Collection("KScript.UI")]
public class UIDesktopCoverageTests
{
    /// <summary>Runs a window script; <paramref name="use"/> gets the session and its real window on the UI thread.</summary>
    private static string WithWindow(string xml, Action<UISession, WindowView> use)
    {
        UIPackage.Host = s => AvaloniaThread.Run(() =>
        {
            AvaloniaThread.Start();
            var view = new WindowView(s);
            view.Window.Show();
            try { use(s, view); }
            finally
            {
                view.StopTimers();
                view.Window.Close();
            }
        });
        try
        {
            return ScriptRunner.Run(xml);
        }
        finally
        {
            UIPackage.Host = null;
        }
    }

    private static T Control<T>(WindowView view, UINode node) where T : Control => (T)view.ControlFor(node);

    private const string Everything = """
        <KScript.Window title="All" width="400" height="500" background="#101010" padding="8" gap="4">
          <let name="fixed" value="'1'" />
          <let name="agree" value="false" />
          <let name="size" value="'m'" />
          <let name="pick" value="2" />
          <let name="level" value="10" />
          <let name="clicks" value="0" />
          <let name="ticks" value="0" />
          <let name="wide" value="false" />
          <let name="broken" value="false" />
          <let name="timing" value="true" />
          <let name="nothing" value="null" />
          <let name="narrow" value="true" />
          <if test="$broken"><throw message="bad draw" /></if>
          <Column gap="4" align="center" scroll="{ $narrow }">
            <CheckBox bind="agree" disabled="false">I agree</CheckBox>
            <Select bind="size" options="{ small: 's', medium: 'm' }" />
            <Select bind="pick" options="[1, { value: 2, label: 'Two' }, { value: 3 }]" />
            <Select bind="pick" options="$nothing" />
            <Slider bind="level" min="0" max="50" step="5" />
            <Slider bind="level" />
            <Spacer size="20" />
            <Spacer />
            <TextBox bind="fixed" />
            <Box width="40" height="40" border="teal" background="#334155" radius="4" on_click="clicks += 1">
              <Text>Box</Text>
            </Box>
            <Box border="red" />
          </Column>
          <Column align="end"><Text>end</Text></Column>
          <Column align="stretch"><Text>stretch</Text></Column>
          <Column><Text>left</Text></Column>
          <Row wrap="{ $wide }" align="start"><Text>a</Text><Text>b</Text></Row>
          <Row align="end"><Text>c</Text></Row>
          <Row align="stretch"><Text>d</Text></Row>
          <Row><Text>e</Text></Row>
          <Timer every="10" when="$timing" on_tick="ticks += 1" />
          <if test="$narrow"><Timer every="10" on_tick="ticks += 0" /></if>
        </KScript.Window>
        """;

    [Fact]
    public void Every_control_is_drawn_and_used()
    {
        WithWindow(Everything, (s, view) =>
        {
            var check = Control<CheckBox>(view, s.FindAll("CheckBox")[0]);
            Assert.Equal("I agree", check.Content);
            check.IsChecked = true;
            Assert.True((bool)Get(s, "agree"));

            var sizes = Control<ComboBox>(view, s.FindAll("Select")[0]);
            Assert.Equal(new[] { "small", "medium" }, (List<string>)sizes.ItemsSource!);
            Assert.Equal(1, sizes.SelectedIndex);
            sizes.SelectedIndex = 0;
            Assert.Equal("s", Get(s, "size"));

            var picks = Control<ComboBox>(view, s.FindAll("Select")[1]);
            Assert.Equal(new[] { "1", "Two", "3" }, (List<string>)picks.ItemsSource!);
            picks.SelectedIndex = 2;
            Assert.Equal(3d, Values.ToNumber(Get(s, "pick"), "pick"));
            Assert.Empty((List<string>)Control<ComboBox>(view, s.FindAll("Select")[2]).ItemsSource!);

            var slider = Control<Slider>(view, s.FindAll("Slider")[0]);
            Assert.Equal(5, slider.TickFrequency);
            slider.Value = 30;
            Assert.Equal(30d, Get(s, "level"));
            Assert.Equal(100, Control<Slider>(view, s.FindAll("Slider")[1]).Maximum);

            Assert.Equal(20, view.ControlFor(s.FindAll("Spacer")[0]).Width);
            Assert.Equal(12, view.ControlFor(s.FindAll("Spacer")[1]).Width);

            // A press on a Box with on_click runs it.
            var box = view.ControlFor(s.FindAll("Box")[0]);
            Dispatcher.UIThread.RunJobs();
            view.Window.UpdateLayout();
            Point at = box.TranslatePoint(new Point(5, 5), view.Window)!.Value;
            view.Window.MouseDown(at, MouseButton.Left);
            view.Window.MouseUp(at, MouseButton.Left);
            Assert.Equal(1d, Values.ToNumber(Get(s, "clicks"), "clicks"));
            // ...and a right click doesn't.
            view.Window.MouseDown(at, MouseButton.Right);
            view.Window.MouseUp(at, MouseButton.Right);
            Assert.Equal(1d, Values.ToNumber(Get(s, "clicks"), "clicks"));

            // Storing into a variable that has gone fails, and says so in the window.
            Control<TextBox>(view, s.FindAll("TextBox")[0]).Text = "2";
            s.SetBound(new UINode("TextBox") { Bind = "ghost", BindScope = new Scope() }, "x");
            Assert.Contains("$ghost is not defined", s.Error);

            // Layouts change kind in place: scroll off, wrap on, the second timer goes.
            s.State.Assign("wide", true);
            s.State.Assign("narrow", false);
            s.Redraw();
            Assert.Null(s.Error);
            Assert.IsType<WrapPanel>(((Border)view.ControlFor(s.FindAll("Row")[0])).Child);

            // A redraw from another thread is posted to the UI thread.
            var other = new Thread(() => s.Redraw());
            other.Start();
            other.Join();
            Dispatcher.UIThread.RunJobs();

            // A layout that fails to draw keeps the last window and shows the error.
            s.State.Assign("broken", true);
            s.Redraw();
            Assert.Contains("bad draw", s.Error);
            s.State.Assign("broken", false);
            s.State.Assign("timing", false);
            s.Redraw();
        });
    }

    private static object Get(UISession s, string name) => s.State.TryGet(name, out object v) ? v : null;

    [Fact]
    public void Bad_colours_and_options_are_reported()
    {
        var ex = Assert.Throws<KScriptException>(() => WithWindow("<KScript.Window><Text color=\"notacolour\">x</Text></KScript.Window>", (s, v) => { }));
        Assert.Contains("isn't a colour", ex.Message);
        ex = Assert.Throws<KScriptException>(() => WithWindow("<KScript.Window><let name=\"x\" value=\"1\" /><Select bind=\"x\" options=\"5\" /></KScript.Window>", (s, v) => { }));
        Assert.Contains("needs a list or a map", ex.Message);
    }

    [Fact]
    public void Shown_windows_tick_and_close_from_the_script()
    {
        const string xml = """
            <kscript>
            <import package="KScript.UI" />
            <KScript.Window title="Ticks">
              <let name="n" value="0" />
              <Text>{ $n }</Text>
              <Timer every="15" on_tick="n += 1" />
              <Timer every="15" when="$n ge 3" on_tick="@close_window()" />
            </KScript.Window>
            <echo>closed</echo>
            </kscript>
            """;
        string output = null;
        // With no test host, the window is shown on the thread that started the script.
        AvaloniaThread.Run(() =>
        {
            AvaloniaThread.Start();
            output = ScriptRunner.Run(xml);
        });
        Assert.Contains("closed", output);
    }

    [Fact]
    public void A_missing_ui_library_says_how_to_get_it()
    {
        var missing = new TypeInitializationException("SkiaSharp.SKObject", new DllNotFoundException("libSkiaSharp"));
        var error = Assert.Throws<KScriptException>(() => DesktopHost.Open(null, () => throw missing));
        Assert.Contains("kscript update --force", error.Message);
        Assert.Equal("UI", error.Type);
        Assert.Throws<InvalidOperationException>(() => DesktopHost.Open(null, () => throw new InvalidOperationException("other")));
        Assert.False(DesktopHost.MissingLibrary(new Exception("x")));
    }

    [Fact]
    public void Errors_get_a_window_of_their_own()
    {
        string shown = null;
        DesktopHost.Opened = w => { shown = w.Title; Dispatcher.UIThread.Post(w.Close); };
        try
        {
            AvaloniaThread.Run(() =>
            {
                AvaloniaThread.Start();
                DesktopHost.ShowError("app.ks.xml", "Line 3: boom");
            });
        }
        finally
        {
            DesktopHost.Opened = null;
        }
        Assert.Equal("app.ks.xml stopped", shown);

        // Off the UI thread (or with no display) the window can't open, and nothing is thrown.
        var other = new Thread(() => DesktopHost.ShowError("x", "y"));
        other.Start();
        Assert.True(other.Join(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Nodes_describe_themselves()
    {
        var node = new UINode("Text") { Text = "hi" };
        Assert.Equal("<Text>hi</Text>", node.ToString());
        Assert.Equal("<Box>", new UINode("Box").ToString());
        Assert.Null(node.Element);
    }
}
