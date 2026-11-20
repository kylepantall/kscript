using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

using KScript.Runtime;

namespace KScript.UI
{
    /// <summary>The Avalonia application behind KScript windows.</summary>
    internal sealed class UIApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            Name = "KScript";
        }
    }

    /// <summary>Shows a <see cref="UISession"/> as a native window and runs it until it is closed.</summary>
    public static class DesktopHost
    {
        private static bool started;

        /// <summary>Starts Avalonia for this process. Tests call it with a headless platform first.</summary>
        public static void Start(Func<AppBuilder, AppBuilder> platform = null)
        {
            if (started) return;
            AppBuilder builder = AppBuilder.Configure<UIApplication>();
            builder = platform != null ? platform(builder) : builder.UsePlatformDetect();
            builder.SetupWithoutStarting();
            started = true;
        }

        /// <summary>For tests: called with each window <see cref="ShowError"/> and <see cref="Show"/> open, once it is showing.</summary>
        internal static Action<Window> Opened;

        /// <summary>Shows an error in a window of its own and waits for it to be closed: for apps, which have no terminal.</summary>
        public static void ShowError(string title, string message)
        {
            try
            {
                Start();
                var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Menlo, Consolas, monospace"), Margin = new Thickness(16) };
                RunUntilClosed(new Window { Title = title + " stopped", Width = 640, Height = 320, Content = new ScrollViewer { Content = text }, WindowStartupLocation = WindowStartupLocation.CenterScreen });
            }
            catch (Exception)
            {
                // No display (a server, or a terminal over SSH): the error has already been written to stderr.
            }
        }

        /// <summary>Opens the window and blocks until it is closed.</summary>
        internal static void Show(UISession session)
        {
            WindowView view = Open(session, () => Start());
            UIElements.CloseRequested = () => view.Window.Close();
            try
            {
                RunUntilClosed(view.Window);
            }
            finally
            {
                UIElements.CloseRequested = null;
                view.StopTimers();
            }
        }

        /// <summary>Starts Avalonia and builds the window, explaining a missing native library rather than crashing.</summary>
        internal static WindowView Open(UISession session, Action start)
        {
            try
            {
                start();
                return new WindowView(session);
            }
            catch (Exception ex) when (MissingLibrary(ex))
            {
                throw new KScriptException(MissingLibraryMessage, "UI");
            }
        }

        internal const string MissingLibraryMessage =
            "Windows need the UI libraries that ship beside kscript (libSkiaSharp and libHarfBuzzSharp), and they aren't there. " +
            "Run `kscript update --force`, or reinstall with install.sh or install.ps1.";

        /// <summary>True when a native library didn't load: kscript was copied without the files beside it.</summary>
        internal static bool MissingLibrary(Exception ex)
        {
            for (; ex != null; ex = ex.InnerException)
            {
                if (ex is DllNotFoundException) return true;
            }
            return false;
        }

        private static void RunUntilClosed(Window window)
        {
            using var closed = new CancellationTokenSource();
            window.Closed += (_, _) => closed.Cancel();
            window.Show();
            window.Activate();
            Opened?.Invoke(window);
            Dispatcher.UIThread.MainLoop(closed.Token);
        }
    }

    /// <summary>
    /// A window and its controls. After every redraw the controls are updated in place from the new
    /// <see cref="UINode"/> tree: a control stays the same object while its node keeps its type and position, so
    /// focus, typing and half-finished clicks survive a redraw.
    /// </summary>
    public sealed class WindowView
    {
        private readonly UISession session;
        private readonly TextBlock errorBar;
        private readonly StackPanel content;
        private readonly List<(DispatcherTimer Timer, UINode Node)> timers = new List<(DispatcherTimer, UINode)>();
        private bool patching;

        public WindowView(UISession session)
        {
            this.session = session;
            errorBar = new TextBlock
            {
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.Parse("#b91c1c")),
                Padding = new Thickness(12, 8),
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false,
            };
            content = new StackPanel();
            var root = new DockPanel();
            DockPanel.SetDock(errorBar, Dock.Top);
            root.Children.Add(errorBar);
            root.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });

            UINode w = session.Tree;
            Window = new Window
            {
                Title = w.String("title") ?? "KScript",
                Width = w.Number("width") ?? 800,
                Height = w.Number("height") ?? 600,
                CanResize = !w.Props.ContainsKey("resizable") || w.Flag("resizable"),
                Content = root,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            string theme = w.String("theme");
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : theme == "light" ? ThemeVariant.Light : ThemeVariant.Default;
            }
            session.Redrawn += () =>
            {
                if (Dispatcher.UIThread.CheckAccess()) Update();
                else Dispatcher.UIThread.Post(Update);
            };
            Update();
        }

        public Window Window { get; }

        /// <summary>The control drawn for each node, for tests.</summary>
        public Control ControlFor(UINode node) => Find(Window, node);

        private static Control Find(Control c, UINode node)
        {
            if (ReferenceEquals(c.Tag, node)) return c;
            foreach (Control child in Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(c).OfType<Control>())
            {
                Control found = Find(child, node);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Brings the controls in line with the session's latest tree.</summary>
        public void Update()
        {
            patching = true;
            try
            {
                UINode w = session.Tree;
                Window.Title = w.String("title") ?? "KScript";
                if (w.Has("background")) Window.Background = Brush(w.String("background"));
                double padding = w.Number("padding") ?? 16;
                content.Margin = new Thickness(padding);
                content.Spacing = w.Number("gap") ?? 12;
                PatchChildren(content.Children, w.Children.Where(n => n.Type != "Timer").ToList());
                foreach (Control child in content.Children) child.HorizontalAlignment = HorizontalAlignment.Left;
                errorBar.Text = session.Error;
                errorBar.IsVisible = session.Error != null;
            }
            finally
            {
                patching = false;
            }
            SyncTimers();
        }

        private void PatchChildren(Avalonia.Controls.Controls children, IList<UINode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Control existing = i < children.Count ? children[i] : null;
                Control control = Patch(existing, nodes[i]);
                if (existing == null) children.Add(control);
                else if (!ReferenceEquals(existing, control)) children[i] = control;
            }
            while (children.Count > nodes.Count) children.RemoveAt(children.Count - 1);
        }

        private Control Patch(Control existing, UINode node)
        {
            Control control = existing is Control c && c.Tag is UINode old && old.Type == node.Type ? c : Create(node);
            control.Tag = node;
            Apply(control, node);
            return control;
        }

        #region Creating controls

        private Control Create(UINode node)
        {
            switch (node.Type)
            {
                case "Column":
                case "Row":
                case "Grid":
                    return new Border();
                case "Text":
                    return new TextBlock();
                case "Button":
                {
                    var b = new Button();
                    b.Click += (s, e) => Fire(b, "on-click");
                    return b;
                }
                case "TextBox":
                {
                    var t = new TextBox();
                    t.TextChanged += (s, e) => { if (!patching) session.SetBound(NodeOf(t), t.Text ?? string.Empty); };
                    t.KeyDown += (s, e) => { if (e.Key == Key.Enter && !NodeOf(t).Flag("multiline")) Fire(t, "on-enter", new Dictionary<string, object> { ["value"] = t.Text ?? string.Empty }); };
                    return t;
                }
                case "CheckBox":
                {
                    var c = new CheckBox();
                    c.IsCheckedChanged += (s, e) => { if (!patching) session.SetBound(NodeOf(c), c.IsChecked == true); };
                    return c;
                }
                case "Select":
                {
                    var c = new ComboBox { MinWidth = 120 };
                    c.SelectionChanged += (s, e) =>
                    {
                        if (patching || c.SelectedIndex < 0) return;
                        var options = Options(NodeOf(c));
                        if (c.SelectedIndex < options.Count) session.SetBound(NodeOf(c), options[c.SelectedIndex].Value);
                    };
                    return c;
                }
                case "Slider":
                {
                    var s = new Slider { MinWidth = 120 };
                    s.PropertyChanged += (_, e) => { if (!patching && e.Property == RangeBase.ValueProperty) session.SetBound(NodeOf(s), s.Value); };
                    return s;
                }
                case "Box":
                {
                    var b = new Border();
                    b.PointerPressed += (s, e) =>
                    {
                        if (NodeOf(b).Handlers.ContainsKey("on-click") && e.GetCurrentPoint(b).Properties.IsLeftButtonPressed)
                        {
                            e.Handled = true;
                            Fire(b, "on-click");
                        }
                    };
                    return b;
                }
                default:
                    return new Control(); // Spacer: Timer nodes are never drawn
            }
        }

        private static UINode NodeOf(Control c) => (UINode)c.Tag;

        private void Fire(Control c, string handler, Dictionary<string, object> evt = null)
        {
            if (patching) return;
            session.Fire(NodeOf(c), handler, evt);
        }

        #endregion

        #region Applying a node to its control

        private void Apply(Control control, UINode n)
        {
            if (n.Has("width")) control.Width = n.Number("width").Value; else control.ClearValue(Layoutable.WidthProperty);
            if (n.Has("height")) control.Height = n.Number("height").Value; else control.ClearValue(Layoutable.HeightProperty);
            if (n.Has("margin")) control.Margin = new Thickness(n.Number("margin").Value); else control.ClearValue(Layoutable.MarginProperty);
            ToolTip.SetTip(control, n.String("tooltip"));

            switch (control)
            {
                case Border border when n.Type is "Column" or "Row" or "Grid":
                    ApplyBox(border, n);
                {
                    Panel current = border.Child as Panel ?? (border.Child as ScrollViewer)?.Content as Panel;
                    Panel panel = PatchPanel(current, n);
                    if (n.Flag("scroll"))
                    {
                        var scroller = border.Child as ScrollViewer ?? new ScrollViewer();
                        if (!ReferenceEquals(scroller.Content, panel)) scroller.Content = panel;
                        if (!ReferenceEquals(border.Child, scroller)) border.Child = scroller;
                    }
                    else if (!ReferenceEquals(border.Child, panel))
                    {
                        if (border.Child is ScrollViewer old) old.Content = null;
                        border.Child = panel;
                    }
                }
                    break;

                case Border box:
                    ApplyBox(box, n);
                    if (n.Has("border"))
                    {
                        box.BorderBrush = Brush(n.String("border"));
                        box.BorderThickness = new Thickness(1);
                    }
                    box.Cursor = n.Handlers.ContainsKey("on-click") ? new Cursor(StandardCursorType.Hand) : null;
                    if (n.Children.Count == 0) box.Child = null;
                    else
                    {
                        var stack = box.Child as StackPanel ?? new StackPanel { Spacing = 8 };
                        PatchChildren(stack.Children, n.Children);
                        box.Child = stack;
                    }
                    break;

                case TextBlock text:
                    text.Text = n.Text ?? string.Empty;
                    text.FontSize = n.Number("size") ?? 14;
                    text.FontWeight = n.String("weight") switch { "bold" => FontWeight.Bold, "light" => FontWeight.Light, _ => FontWeight.Normal };
                    if (n.Has("color")) text.Foreground = Brush(n.String("color")); else text.ClearValue(TextBlock.ForegroundProperty);
                    if (n.Has("background")) text.Background = Brush(n.String("background")); else text.ClearValue(TextBlock.BackgroundProperty);
                    text.Padding = new Thickness(n.Number("padding") ?? 0);
                    text.TextWrapping = n.Flag("wrap") ? TextWrapping.Wrap : TextWrapping.NoWrap;
                    text.TextAlignment = n.String("text-align") switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left };
                    break;

                case CheckBox check:
                    check.Content = n.Text ?? string.Empty;
                    check.IsChecked = Values.Truthy(n.Prop("value"));
                    check.IsEnabled = !n.Flag("disabled");
                    break;

                case Button button:
                    button.Content = n.Text ?? string.Empty;
                    button.HorizontalContentAlignment = HorizontalAlignment.Center;
                    button.IsEnabled = !n.Flag("disabled");
                    button.Classes.Set("accent", n.String("variant") == "primary");
                    if (n.Has("background")) button.Background = Brush(n.String("background")); else button.ClearValue(TemplatedControl.BackgroundProperty);
                    if (n.Has("padding")) button.Padding = new Thickness(n.Number("padding").Value); else button.ClearValue(TemplatedControl.PaddingProperty);
                    break;

                case TextBox box:
                {
                    string value = Values.ToStr(n.Prop("value"));
                    if (box.Text != value) box.Text = value;
                    box.Watermark = n.String("placeholder");
                    box.AcceptsReturn = n.Flag("multiline");
                    box.IsEnabled = !n.Flag("disabled");
                    break;
                }

                case ComboBox combo:
                {
                    var options = Options(n);
                    var labels = options.Select(o => o.Label).ToList();
                    if (!(combo.ItemsSource is List<string> current) || !current.SequenceEqual(labels)) combo.ItemsSource = labels;
                    combo.SelectedIndex = options.FindIndex(o => Values.LooseEquals(o.Value, n.Prop("value")));
                    combo.IsEnabled = !n.Flag("disabled");
                    break;
                }

                case Slider slider:
                {
                    slider.Minimum = n.Number("min") ?? 0;
                    slider.Maximum = n.Number("max") ?? 100;
                    if (n.Number("step") is double step && step > 0)
                    {
                        slider.TickFrequency = step;
                        slider.SmallChange = step;
                        slider.IsSnapToTickEnabled = true;
                    }
                    double v = Values.ToNumber(n.Prop("value") ?? 0d, "<Slider bind>");
                    if (slider.Value != v) slider.Value = v;
                    slider.IsEnabled = !n.Flag("disabled");
                    break;
                }

                default: // Spacer
                {
                    double size = n.Number("size") ?? 12;
                    control.Width = size;
                    control.Height = size;
                    break;
                }
            }
        }

        private void ApplyBox(Border border, UINode n)
        {
            if (n.Has("background")) border.Background = Brush(n.String("background")); else border.ClearValue(Border.BackgroundProperty);
            border.Padding = new Thickness(n.Number("padding") ?? 0);
            border.CornerRadius = new CornerRadius(n.Number("radius") ?? 0);
        }

        private Panel PatchPanel(Panel existing, UINode n)
        {
            double gap = n.Number("gap") ?? (n.Type == "Grid" ? 0 : 8);
            string align = n.String("align");
            Panel panel;
            switch (n.Type)
            {
                case "Grid":
                {
                    var grid = existing as UniformGrid ?? new UniformGrid();
                    grid.Columns = Math.Max(1, (int)(n.Number("columns") ?? 1));
                    grid.ColumnSpacing = gap;
                    grid.RowSpacing = gap;
                    grid.HorizontalAlignment = HorizontalAlignment.Left;
                    panel = grid;
                    break;
                }
                case "Row" when n.Flag("wrap"):
                {
                    var wrap = existing as WrapPanel ?? new WrapPanel();
                    wrap.ItemSpacing = gap;
                    wrap.LineSpacing = gap;
                    panel = wrap;
                    break;
                }
                default:
                {
                    var stack = existing is StackPanel s && !(existing is WrapPanel) ? s : new StackPanel();
                    stack.Orientation = n.Type == "Row" ? Orientation.Horizontal : Orientation.Vertical;
                    stack.Spacing = gap;
                    panel = stack;
                    break;
                }
            }

            PatchChildren(panel.Children, n.Children.Where(c => c.Type != "Timer").ToList());
            foreach (Control child in panel.Children)
            {
                if (n.Type == "Row")
                {
                    child.VerticalAlignment = align switch { "start" => VerticalAlignment.Top, "end" => VerticalAlignment.Bottom, "stretch" => VerticalAlignment.Stretch, _ => VerticalAlignment.Center };
                }
                else if (n.Type == "Column")
                {
                    child.HorizontalAlignment = align switch { "center" => HorizontalAlignment.Center, "end" => HorizontalAlignment.Right, "stretch" => HorizontalAlignment.Stretch, _ => HorizontalAlignment.Left };
                }
            }
            return panel;
        }

        private static readonly Dictionary<string, IBrush> brushes = new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase);

        private IBrush Brush(string colour)
        {
            if (string.IsNullOrWhiteSpace(colour)) return null;
            if (brushes.TryGetValue(colour, out IBrush brush)) return brush;
            if (!Color.TryParse(colour.Trim(), out Color c))
                throw new KScriptException($"'{colour}' isn't a colour. Use a name such as teal, or #rrggbb.", "Value");
            brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(c);
            brushes[colour] = brush;
            return brush;
        }

        private static List<(string Label, object Value)> Options(UINode n) => n.Prop("options") switch
        {
            Dictionary<string, object> map => map.Select(kv => (kv.Key, kv.Value)).ToList(),
            List<object> list => list.Select(x => x is Dictionary<string, object> o && o.ContainsKey("value")
                ? (Values.ToStr(o.TryGetValue("label", out object l) ? l : o["value"]), o["value"])
                : (Values.ToStr(x), x)).ToList(),
            null => new List<(string, object)>(),
            object other => throw new KScriptException($"<Select options> needs a list or a map, but got {Values.Describe(other)}.", "Type"),
        };

        #endregion

        #region Timers

        private void SyncTimers()
        {
            List<UINode> active = session.Tree.Descendants().Where(n => n.Type == "Timer" && (!n.Props.ContainsKey("when") || n.Flag("when"))).ToList();
            for (int i = 0; i < active.Count; i++)
            {
                UINode node = active[i];
                double every = Math.Max(15, node.Number("every") ?? 1000);
                if (i < timers.Count)
                {
                    var (timer, _) = timers[i];
                    timers[i] = (timer, node);
                    timer.Interval = TimeSpan.FromMilliseconds(every);
                    if (!timer.IsEnabled) timer.Start();
                }
                else
                {
                    int index = i;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(every) };
                    timer.Tick += (_, _) =>
                    {
                        // One tick at a time: the next is scheduled when the redraw after this one is done.
                        timer.Stop();
                        if (index < timers.Count) session.Fire(timers[index].Node, "on-tick");
                    };
                    timers.Add((timer, node));
                    timer.Start();
                }
            }
            while (timers.Count > active.Count)
            {
                timers[^1].Timer.Stop();
                timers.RemoveAt(timers.Count - 1);
            }
        }

        internal void StopTimers()
        {
            foreach (var (timer, _) in timers) timer.Stop();
            timers.Clear();
        }

        #endregion
    }
}
