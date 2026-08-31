using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.DesktopTheme;
using Nova.DesktopTheme.Host;
using Nova.FontConfig;
using Nova.Sdl;
using Nova.SdlSource;
using Nova.SystemTheme;
using Nova.Vulkan;
using SdlApi = Silk.NET.SDL.Sdl;
using SdlEventType = Silk.NET.SDL.EventType;
using SilkWindow = Silk.NET.SDL.WindowHandle;

namespace ControlProbeHarness;

/// <summary>
/// Dev-only control-coverage and feature probe. Modes:
/// <c>all</c> re-execs this binary once per control (per-control process isolation was
/// introduced for a multi-window bug that is now FIXED — <c>TwoTopLevelWindows_AppLoop</c>
/// proves N windows render distinct content in one process — but the isolation is kept so
/// every row's render column is trustworthy and a crashing control cannot take down the
/// sweep). <c>feat all</c> re-execs once per cross-cutting feature. Popup-bearing controls
/// (ContextMenu, ToolTip, ComboBoxOpen, MenuOpen, PopupDirect) additionally open their
/// popup window and read back its OWN pixels via <see cref="SdlPresentationSource.PresentAll"/>.
/// Run manually: <c>dotnet run --project samples/ControlProbeHarness -- all</c>
/// </summary>
internal sealed partial class Program
{
    private const int ProbeWindowWidth = 320;
    private const int ProbeWindowHeight = 240;

    private static readonly FontFamily ProbeFont = new("DejaVu Sans");

    private static Popup? s_probePopup;

    private static readonly (string Name, Func<FrameworkElement> Build)[] ControlSpecs =
    [
        ("Border", () => new Border
        {
            Width = 200,
            Height = 100,
            Background = Brushes.Red,
            BorderBrush = Brushes.Blue,
            BorderThickness = new Thickness(4),
            Child = new TextBlock { Text = "border", FontFamily = ProbeFont }
        }),
        ("BorderRounded", () => new Border
        {
            Width = 200,
            Height = 100,
            Background = Brushes.Red,
            BorderBrush = Brushes.Blue,
            BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(20),
            Child = new TextBlock { Text = "rounded", FontFamily = ProbeFont }
        }),
        ("Grid2x2", () =>
        {
            var grid = new Grid { Width = 200, Height = 120 };
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            (Color, int, int)[] cells =
            [
                (Colors.Red, 0, 0),
                (Colors.Green, 0, 1),
                (Colors.Blue, 1, 0),
                (Colors.Yellow, 1, 1)
            ];
            foreach ((Color color, int row, int column) in cells)
            {
                var rect = new Rectangle { Fill = new SolidColorBrush(color) };
                Grid.SetRow(rect, row);
                Grid.SetColumn(rect, column);
                _ = grid.Children.Add(rect);
            }

            return grid;
        }),
        ("StackPanel", () =>
        {
            var panel = new StackPanel { Width = 200 };
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 40, Fill = Brushes.Red });
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 40, Fill = Brushes.Blue });
            return panel;
        }),
        ("WrapPanel", () =>
        {
            var panel = new WrapPanel { Width = 200 };
            for (int i = 0; i < 5; i++)
            {
                _ = panel.Children.Add(new Rectangle
                {
                    Width = 60,
                    Height = 30,
                    Fill = i % 2 == 0 ? Brushes.Red : Brushes.Blue
                });
            }

            return panel;
        }),
        ("UniformGrid", () =>
        {
            var grid = new UniformGrid { Width = 200, Height = 120, Columns = 2 };
            for (int i = 0; i < 4; i++)
            {
                _ = grid.Children.Add(new Rectangle { Fill = i % 2 == 0 ? Brushes.Red : Brushes.Blue });
            }

            return grid;
        }),
        ("DockPanel", () =>
        {
            var panel = new DockPanel { Width = 200, Height = 120 };
            var top = new Rectangle { Height = 30, Fill = Brushes.Red };
            DockPanel.SetDock(top, Dock.Top);
            _ = panel.Children.Add(top);
            _ = panel.Children.Add(new Rectangle { Fill = Brushes.Blue });
            return panel;
        }),
        ("Canvas", () =>
        {
            var canvas = new Canvas { Width = 200, Height = 120, Background = Brushes.White };
            var red = new Rectangle { Width = 60, Height = 40, Fill = Brushes.Red };
            Canvas.SetLeft(red, 10);
            Canvas.SetTop(red, 10);
            _ = canvas.Children.Add(red);
            var blue = new Rectangle { Width = 60, Height = 40, Fill = Brushes.Blue };
            Canvas.SetLeft(blue, 120);
            Canvas.SetTop(blue, 60);
            _ = canvas.Children.Add(blue);
            return canvas;
        }),
        ("Viewbox", () => new Viewbox
        {
            Width = 160,
            Height = 120,
            Child = new Rectangle { Width = 40, Height = 40, Fill = Brushes.Red }
        }),
        ("TextBlock", () => new TextBlock
        {
            Text = "Hi",
            FontFamily = ProbeFont,
            FontSize = 16,
            Foreground = Brushes.Black
        }),
        ("Button", () => new Button { Content = "Click", Width = 80, Height = 32 }),
        ("CheckBox", () => new CheckBox { Content = "Check", IsChecked = true }),
        ("RadioButton", () => new RadioButton { Content = "Radio", IsChecked = true }),
        ("ToggleButton", () => new ToggleButton { Content = "Toggle" }),
        ("TextBoxEmpty", () => new TextBox { Width = 120, Height = 24, Background = Brushes.White }),
        ("TextBoxText", () => new TextBox { Text = "hello", Width = 120, Height = 24, Background = Brushes.White }),
        ("PasswordBox", () => new PasswordBox { Width = 120, Height = 24, Background = Brushes.White }),
        ("Label", () => new Label { Content = "Label text" }),
        ("ListBox", () =>
        {
            var list = new ListBox { Width = 120, Height = 80 };
            _ = list.Items.Add(new ListBoxItem { Content = "A" });
            _ = list.Items.Add(new ListBoxItem { Content = "B" });
            _ = list.Items.Add(new ListBoxItem { Content = "C" });
            return list;
        }),
        ("ListView", () =>
        {
            var view = new ListView { Width = 160, Height = 100 };
            _ = view.Items.Add(new ListViewItem { Content = "one" });
            _ = view.Items.Add(new ListViewItem { Content = "two" });
            _ = view.Items.Add(new ListViewItem { Content = "three" });
            return view;
        }),
        ("TreeView", () =>
        {
            var tree = new TreeView { Width = 160, Height = 100 };
            var root = new TreeViewItem { Header = "root", IsExpanded = true };
            _ = root.Items.Add(new TreeViewItem { Header = "child" });
            _ = tree.Items.Add(root);
            return tree;
        }),
        ("ItemsControl", () =>
        {
            var items = new ItemsControl { Width = 120 };
            _ = items.Items.Add(new TextBlock { Text = "one", FontFamily = ProbeFont });
            _ = items.Items.Add(new TextBlock { Text = "two", FontFamily = ProbeFont });
            _ = items.Items.Add(new TextBlock { Text = "three", FontFamily = ProbeFont });
            return items;
        }),
        ("ScrollViewer", () => new ScrollViewer
        {
            Width = 200,
            Height = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Rectangle { Width = 1000, Height = 1000, Fill = Brushes.Red }
        }),
        ("Slider", () => new Slider { Width = 160, Minimum = 0, Maximum = 100, Value = 50 }),
        ("ProgressBar", () => new ProgressBar { Width = 160, Height = 16, Minimum = 0, Maximum = 100, Value = 50 }),
        ("Separator", () => new Separator { Width = 100, Height = 8 }),
        ("GroupBox", () => new GroupBox
        {
            Header = "Group",
            Content = new TextBlock { Text = "inside", FontFamily = ProbeFont }
        }),
        ("Expander", () => new Expander
        {
            Header = "Expander",
            Content = new TextBlock { Text = "body", FontFamily = ProbeFont }
        }),
        ("TabControl", () =>
        {
            var tabs = new TabControl { Width = 200, Height = 100 };
            var first = new TabItem
            {
                Header = "one",
                Content = new Rectangle { Fill = Brushes.Red }
            };
            _ = tabs.Items.Add(first);
            var second = new TabItem
            {
                Header = "two",
                Content = new Rectangle { Fill = Brushes.Blue }
            };
            _ = tabs.Items.Add(second);
            return tabs;
        }),
        ("ComboBox", () =>
        {
            var combo = new ComboBox { Width = 120, Height = 24 };
            _ = combo.Items.Add("one");
            _ = combo.Items.Add("two");
            _ = combo.Items.Add("three");
            return combo;
        }),
        ("MenuBar", () =>
        {
            var menu = new Menu { Width = 200 };
            var file = new MenuItem { Header = "File" };
            _ = file.Items.Add(new MenuItem { Header = "Open" });
            _ = file.Items.Add(new MenuItem { Header = "Save" });
            _ = menu.Items.Add(file);
            return menu;
        }),
        ("RichTextBox", () => new RichTextBox
        {
            Width = 160,
            Height = 80,
            Document = new FlowDocument(new Paragraph(new Run("rich text")))
        }),
        ("DataGrid", () =>
        {
            var grid = new DataGrid { Width = 200, Height = 120, AutoGenerateColumns = false };
            grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name") });
            grid.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding("Value") });
            grid.ItemsSource = new[] { new ProbeRow("a", 1), new ProbeRow("b", 2) };
            return grid;
        }),
        ("ScrollBar", () => new ScrollBar
        {
            Width = 16,
            Height = 80,
            Minimum = 0,
            Maximum = 100,
            Value = 40,
            Orientation = Orientation.Vertical
        }),
        ("Rectangle", () => new Rectangle { Width = 80, Height = 40, Fill = Brushes.Red }),
        ("Line", () => new Line
        {
            X1 = 0,
            Y1 = 0,
            X2 = 100,
            Y2 = 50,
            Stroke = Brushes.Red,
            StrokeThickness = 4
        }),
        ("PathCurves", () => new System.Windows.Shapes.Path
        {
            Data = new PathGeometry([new PathFigure(new Point(0, 40), [new BezierSegment(new Point(10, 0), new Point(50, 80), new Point(80, 40), true)], true)]),
            Fill = Brushes.Red
        }),
        ("Ellipse", () => new Ellipse { Width = 80, Height = 40, Fill = Brushes.Blue }),
        ("Path", () => new System.Windows.Shapes.Path
        {
            Data = new RectangleGeometry(new Rect(0, 0, 80, 40)),
            Fill = Brushes.Green
        }),
        ("ImageNoSource", () => new Image { Width = 80, Height = 40 }),
        ("ImageBitmap", () => new Image
        {
            Width = 80,
            Height = 40,
            Stretch = Stretch.Fill,
            Source = CreateProbeBitmap()
        }),
        ("PopupDirect", () =>
        {
            var host = new Border
            {
                Width = 200,
                Height = 100,
                Background = Brushes.White,
                Child = new TextBlock { Text = "popup host", FontFamily = ProbeFont }
            };
            s_probePopup = new Popup
            {
                AllowsTransparency = true,
                PlacementTarget = host,
                Placement = PlacementMode.Bottom,
                Child = new Border
                {
                    Width = 120,
                    Height = 60,
                    Background = Brushes.Yellow,
                    Child = new TextBlock { Text = "popup!", FontFamily = ProbeFont }
                }
            };
            return host;
        }),
        ("ContextMenu", () =>
        {
            var button = new Button { Content = "right-click", Width = 100, Height = 32 };
            var menu = new ContextMenu();
            _ = menu.Items.Add(new MenuItem { Header = "Copy" });
            _ = menu.Items.Add(new MenuItem { Header = "Paste" });
            button.ContextMenu = menu;
            return button;
        }),
        ("ToolTip", () => new Button
        {
            Content = "hover",
            Width = 100,
            Height = 32,
            ToolTip = new ToolTip { Content = "tooltip text" }
        }),
        ("ComboBoxOpen", () =>
        {
            var combo = new ComboBox { Width = 120, Height = 24 };
            _ = combo.Items.Add("one");
            _ = combo.Items.Add("two");
            _ = combo.Items.Add("three");
            return combo;
        }),
        ("MenuOpen", () =>
        {
            var menu = new Menu();
            var file = new MenuItem { Header = "File" };
            _ = file.Items.Add(new MenuItem { Header = "Open" });
            _ = file.Items.Add(new MenuItem { Header = "Save" });
            _ = menu.Items.Add(file);
            return menu;
        })
    ];

    /// <summary>Popup openers keyed by control name; the value is (open, close).</summary>
    private static readonly Dictionary<string, (Action<FrameworkElement> Open, Action<FrameworkElement> Close)> PopupActions = new()
    {
        ["ContextMenu"] = (
            control =>
            {
                var menu = ((Button)control).ContextMenu!;
                menu.PlacementTarget = control;
                menu.IsOpen = true;
            },
            control => ((Button)control).ContextMenu!.IsOpen = false),
        ["ToolTip"] = (OpenToolTip, CloseToolTip),
        ["ComboBoxOpen"] = (
            control => ((ComboBox)control).IsDropDownOpen = true,
            control => ((ComboBox)control).IsDropDownOpen = false),
        ["MenuOpen"] = (
            control => ((MenuItem)((Menu)control).Items[0]).IsSubmenuOpen = true,
            control => ((MenuItem)((Menu)control).Items[0]).IsSubmenuOpen = false),
        ["PopupDirect"] = (
            _ => s_probePopup!.IsOpen = true,
            _ => s_probePopup!.IsOpen = false)
    };

    private static void OpenToolTip(FrameworkElement control)
    {
        var tip = (ToolTip)((Button)control).ToolTip;
        tip.PlacementTarget = control;
        // Deliberately NO explicit Placement: the ToolTip default is PlacementMode.Mouse,
        // which previously crashed on user32 GetCursor (Popup.GetMouseCursorSize). The
        // Linux cursor-size default (patch 0009) makes the default path work.
        tip.IsOpen = true;
    }

    private static void CloseToolTip(FrameworkElement control)
    {
        if (((Button)control).ToolTip is ToolTip tip)
        {
            tip.IsOpen = false;
        }
    }

    private static readonly string[] FeatureNames =
    [
        "diag",
        "solidfill",
        "midtone",
        "pathgeometry",
        "stroke",
        "glyphrun",
        "clip",
        "opacity",
        "transform",
        "image",
        "lineargradient",
        "radialgradient",
        "visualbrush",
        "imagebrush",
        "rounded",
        "antialias",
        "rtb",
        "animation",
        "dispatchertimer",
        "binding",
        "command",
        "style",
        "trigger",
        "sizetocontent",
        "dpi",
        "rtl",
        "textwrap",
        "wheel",
        "tabfocus",
        "dragselect",
        "multiwindow",
        "windowstate",
        "systemparams",
        "systemcolors",
        "borderstroke",
        "dropshadow",
        "blur",
        "opacitymask",
        "fluenttheme",
        "fluentglyph",
        "fluentshadow",
        "detheme",
        "detheme-live"
    ];

    public static int Main(string[] args)
    {
        _ = Native.SetEnv("SDL_VIDEO_DRIVER", "offscreen", 1);
        _ = Native.SetEnv("SDL_VIDEODRIVER", "offscreen", 1);

        // Best-effort registration of the bundled Fluent icon font (fonts/NovaFluentIcons.ttf
        // is copied to the output directory). Best-effort so non-glyph probes still run on a
        // broken bundle; the glyph probes/tests fail loudly on tofu when it is missing.
        string iconFont = System.IO.Path.Combine(AppContext.BaseDirectory, "fonts", "NovaFluentIcons.ttf");
        if (File.Exists(iconFont))
        {
            try
            {
                FontConfigLibrary.RegisterAppFont(iconFont);
            }
            catch (FontConfigException)
            {
                Console.Error.WriteLine($"warn: fontconfig rejected bundled icon font '{iconFont}'");
            }
        }

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine($"{nameof(ControlProbeHarness)} --list | <control-name> | all | feat <name> | feat all | caret ({ControlSpecs.Length} controls, {FeatureNames.Length} features)");
            return 0;
        }

        if (args[0] == "--list")
        {
            foreach ((string name, _) in ControlSpecs)
            {
                Console.WriteLine(name);
            }

            return 0;
        }

        if (args[0] == "all")
        {
            return RunAll();
        }

        if (args[0] == "caret")
        {
            return RunCaretProbe();
        }

        if (args[0] == "feat")
        {
            if (args.Length < 2 || args[1] == "--list")
            {
                Console.WriteLine(string.Join(Environment.NewLine, FeatureNames));
                return 0;
            }

            return args[1] == "all" ? RunAllFeatures() : RunFeature(args[1]);
        }

        string? row = ProbeOne(args[0]);
        if (row is null)
        {
            Console.Error.WriteLine($"unknown control '{args[0]}'");
            return 2;
        }

        Console.WriteLine(row);
        return 0;
    }

    // ---------------------------------------------------------------------
    // Control sweep
    // ---------------------------------------------------------------------

    private static int RunAll()
    {
        string dotnet = Environment.ProcessPath ?? "dotnet";
        var rows = new List<string>();
        foreach ((string name, _) in ControlSpecs)
        {
            var startInfo = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(name);
            using var process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            string line = output.Trim();
            if (process.ExitCode != 0 || line.Length == 0)
            {
                line = $"{name,-14} | -    | -                                          | n/a  | -         | -          | run-failed exit={process.ExitCode} err={ShortMessage(error)}";
            }

            rows.Add(line);
        }

        string header = "control        | built | layout                                    | colors | px0       | popup      | first failure";
        string table = header + Environment.NewLine + string.Join(Environment.NewLine, rows);
        Console.WriteLine(table);
        File.WriteAllText("/tmp/nova-control-coverage.txt", table + Environment.NewLine);
        return 0;
    }

    private static string? ProbeOne(string name)
    {
        (string _, Func<FrameworkElement> build) = ControlSpecs.FirstOrDefault(spec => spec.Name == name);
        if (build is null)
        {
            return null;
        }

        FrameworkElement? control = null;
        Window? window = null;
        string layout = "-";
        int colors = -1;
        string firstPixel = "-";
        string failure = "-";

        Exception? error = Capture(() => control = build());
        string stage = "construction";
        if (error is null)
        {
            window = new Window { Title = "probe-" + name, Width = ProbeWindowWidth, Height = ProbeWindowHeight };
            error = Capture(() =>
            {
                window.Content = control;
                window.Show();
                window.UpdateLayout();
            });
            stage = "layout";
            if (error is null && control is not null)
            {
                layout = FormatSize(control.ActualWidth, control.ActualHeight)
                    + " desired=" + FormatSize(control.DesiredSize.Width, control.DesiredSize.Height);
            }
        }

        if (error is null && window is not null)
        {
            if (PresentationSource.FromVisual(window) is SdlPresentationSource source)
            {
                IVulkanPresenter? presenter = null;
                error = Capture(() =>
                {
                    presenter = GetWindowPresenter(source);
                    presenter.EnableReadback();
                });
                stage = "readback";
                if (error is null)
                {
                    error = Capture(() =>
                    {
                        FlushDispatcher();
                        source.Present();
                        source.Present();
                    });
                    stage = "render";
                    if (error is null)
                    {
                        error = Capture(() =>
                        {
                            ReadOnlyMemory<byte> pixels = presenter!.ReadbackRgba();
                            colors = CountDistinctColors(pixels);
                            firstPixel = FormatPixel(pixels.Span);
                        });
                        stage = "readback";
                    }
                }
            }
            else
            {
                error = new InvalidOperationException("PresentationSource.FromVisual did not return SdlPresentationSource.");
                stage = "layout";
            }
        }

        string popupColumn = "-";
        if (error is null && window is not null && PopupActions.TryGetValue(name, out (Action<FrameworkElement> Open, Action<FrameworkElement> Close) popup))
        {
            Exception? popupError = Capture(() => popupColumn = ProbePopup(popup.Open, popup.Close, control!, window));
            if (popupError is not null)
            {
                popupColumn = "err:" + ShortMessage(popupError.Message);
                if (Environment.GetEnvironmentVariable("NOVA_PROBE_FULL") == "1")
                {
                    Console.Error.WriteLine($"popup-error [{name}]: {popupError}");
                }
            }
        }

        if (error is not null)
        {
            failure = stage + ": " + error.GetType().Name + ": " + ShortMessage(error.Message) + " @ " + TopFrame(error);
        }

        try
        {
            window?.Close();
        }
        catch (Exception closeError) when (closeError is not OutOfMemoryException and not StackOverflowException)
        {
            // The probe never aborts; a failed Close is folded into the row only.
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-14} | {(control is not null ? "yes" : "no"),-4} | {layout,-42} | {(colors < 0 ? "n/a" : colors.ToString(CultureInfo.InvariantCulture)),-5} | {firstPixel,-9} | {popupColumn,-10} | {failure}");
    }

    /// <summary>
    /// Opens the popup associated with a control, reads back the popup window's OWN pixels
    /// (via <see cref="SdlPresentationSource.PresentAll"/> — the app-loop contract), and
    /// reports distinct colors + first pixel. Returns "-" when no popup source appears.
    /// </summary>
    private static string ProbePopup(Action<FrameworkElement> open, Action<FrameworkElement> close, FrameworkElement control, Window window)
    {
        open(control);
        FlushDispatcher();

        var mainSource = (SdlPresentationSource)PresentationSource.FromVisual(window);
        SdlPresentationSource? popupSource = FindPopupSource(mainSource);
        if (popupSource is null)
        {
            close(control);
            FlushDispatcher();
            return "no-source";
        }

        try
        {
            popupSource.EnableReadback();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = popupSource.ReadbackRgba();
            return CountDistinctColors(pixels) + " " + FormatPixel(pixels.Span);
        }
        finally
        {
            close(control);
            FlushDispatcher();
        }
    }

    private static SdlPresentationSource? FindPopupSource(SdlPresentationSource main)
    {
        SdlPresentationSource? any = null;
        foreach (PresentationSource candidate in PresentationSource.CurrentSources)
        {
            if (candidate is SdlPresentationSource ps && !ReferenceEquals(ps, main))
            {
                if (ReferenceEquals(ps.Owner, main))
                {
                    return ps;
                }

                any ??= ps;
            }
        }

        return any;
    }

    // ---------------------------------------------------------------------
    // Feature battery
    // ---------------------------------------------------------------------

    private static int RunAllFeatures()
    {
        string dotnet = Environment.ProcessPath ?? "dotnet";
        var rows = new List<string>();
        foreach (string name in FeatureNames)
        {
            var startInfo = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("feat");
            startInfo.ArgumentList.Add(name);
            using var process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            string line = output.Trim();
            if (process.ExitCode != 0 || line.Length == 0)
            {
                line = $"feat:{name}: run-failed exit={process.ExitCode} err={ShortMessage(error)}";
            }

            rows.Add(line);
        }

        string table = string.Join(Environment.NewLine, rows);
        Console.WriteLine(table);
        File.WriteAllText("/tmp/nova-feature-coverage.txt", table + Environment.NewLine);
        return 0;
    }

    private static int RunFeature(string name)
    {
        int exit = name switch
        {
            "diag" => FeatDiag(),
            "solidfill" => FeatSolidFill(),
            "midtone" => FeatMidtone(),
            "pathgeometry" => FeatPathGeometry(),
            "stroke" => FeatStroke(),
            "glyphrun" => FeatGlyphRun(),
            "clip" => FeatClip(),
            "opacity" => FeatOpacity(),
            "transform" => FeatTransform(),
            "image" => FeatImage(),
            "lineargradient" => FeatLinearGradient(),
            "radialgradient" => FeatRadialGradient(),
            "visualbrush" => FeatVisualBrush(),
            "imagebrush" => FeatImageBrush(),
            "rounded" => FeatRounded(),
            "antialias" => FeatAntiAlias(),
            "rtb" => FeatRenderTargetBitmap(),
            "animation" => FeatAnimation(),
            "dispatchertimer" => FeatDispatcherTimer(),
            "binding" => FeatBinding(),
            "command" => FeatCommand(),
            "style" => FeatStyle(),
            "trigger" => FeatTrigger(),
            "sizetocontent" => FeatSizeToContent(),
            "dpi" => FeatDpi(),
            "rtl" => FeatRtl(),
            "textwrap" => FeatTextWrap(),
            "wheel" => FeatWheel(),
            "tabfocus" => FeatTabFocus(),
            "dragselect" => FeatDragSelect(),
            "multiwindow" => FeatMultiWindow(),
            "windowstate" => FeatWindowState(),
            "systemparams" => FeatSystemParams(),
            "systemcolors" => FeatSystemColors(),
            "detheme" => FeatDetheme(),
            "detheme-live" => FeatDethemeLive(),
            "borderstroke" => FeatBorderStroke(),
            "dropshadow" => FeatDropShadow(),
            "blur" => FeatBlur(),
            "opacitymask" => FeatOpacityMask(),
            "fluenttheme" => FeatFluentTheme(),
            "fluentglyph" => FeatFluentGlyphs(),
            "fluentshadow" => FeatFluentShadow(),
            _ => -1
        };
        return exit;
    }

    private static int FeatDiag()
    {
        // Full-cover red rect: establishes readback size vs window size and the content
        // origin/flip so every other probe's bounding-box numbers are interpretable.
        Window? window = null;
        try
        {
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = new Rectangle { Fill = Brushes.Red } };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            (int red, int minX, int minY, int maxX, int maxY) = FindColor(pixels.Span, source.PixelWidth, IsRed);
            Console.WriteLine(
                $"feat:diag: pixelSize={source.PixelWidth}x{source.PixelHeight} readback={pixels.Length / 4}px " +
                $"redCount={red} redBBox=({minX},{minY})-({maxX},{maxY}) px0={FormatPixel(pixels.Span)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatSolidFill()
    {
        Window? window = null;
        try
        {
            var panel = new StackPanel { Width = 200 };
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 100, Fill = Brushes.Red });
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 60, Fill = Brushes.Blue });
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = panel };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            Console.WriteLine($"feat:solidfill: red={red} blue={blue}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    /// <summary>
    /// Mid-tone sRGB encoding probe: 0 and 255 are fixed points of the sRGB-to-linear
    /// transform, so the entire pure-colour baseline cannot see a mid-tone error. This
    /// row renders sRGB #808080 through every solid-consuming surface (fill, pen,
    /// glyph tint, clear) plus a single-stop gradient of the same colour, and reports
    /// the stored bytes. A correct pipeline stores 128 (the sRGB byte) everywhere;
    /// the pre-fix pipeline stored 55 (the linear byte) for the solid-consuming paths
    /// and 128 for the gradient (whose LUT was already sRGB-encoded).
    /// </summary>
    private static int FeatMidtone()
    {
        Window? window = null;
        try
        {
            var midGrey = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
            var solid = new Rectangle { Width = 200, Height = 50, Fill = midGrey };
            var gradient = new Rectangle { Width = 200, Height = 50, Fill = new LinearGradientBrush(Color.FromRgb(0x80, 0x80, 0x80), Color.FromRgb(0x80, 0x80, 0x80), 0) };
            var stroked = new Rectangle { Width = 200, Height = 50, Fill = Brushes.White, Stroke = midGrey, StrokeThickness = 6 };
            var text = new TextBlock { Text = "M", FontFamily = ProbeFont, FontSize = 32, Foreground = midGrey };
            var panel = new StackPanel { Width = 200, Orientation = Orientation.Vertical };
            _ = panel.Children.Add(solid);    // y 0-50
            _ = panel.Children.Add(gradient); // y 50-100
            _ = panel.Children.Add(stroked);  // y 100-150
            _ = panel.Children.Add(text);     // y 150+
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = panel };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlySpan<byte> pixels = source.ReadbackRgba().Span;
            int width = source.PixelWidth;

            // Sample the exact centers of the solid rect and the gradient rect.
            int solidByte = ReadPixelByte(pixels, width, 100, 25);
            int gradientByte = ReadPixelByte(pixels, width, 100, 75);
            int strokeByte = ReadPixelByte(pixels, width, 100, 103); // stroked rect interior edge
            Console.WriteLine($"feat:midtone: solidFill={solidByte} gradient={gradientByte} stroke={strokeByte} (expect 128 solid/gradient/stroke, not 55)");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int ReadPixelByte(ReadOnlySpan<byte> pixels, int width, int x, int y)
    {
        int i = ((y * width) + x) * 4;
        return i + 3 < pixels.Length ? pixels[i] : -1;
    }

    private static int FeatPathGeometry()
    {
        Window? window = null;
        try
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(0, 40), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new BezierSegment(new Point(10, 0), new Point(50, 80), new Point(80, 40), true));
            geometry.Figures.Add(figure);
            var path = new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.Red };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = path };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int minX, int minY, int maxX, int maxY) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsRed);
            Console.WriteLine($"feat:pathgeometry: red={red} bbox=({minX},{minY})-({maxX},{maxY})");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatStroke()
    {
        Window? window = null;
        try
        {
            var panel = new StackPanel { Width = 200 };
            _ = panel.Children.Add(new Rectangle
            {
                Width = 120,
                Height = 60,
                Fill = Brushes.Red,
                Stroke = Brushes.Blue,
                StrokeThickness = 6
            });
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = panel };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            Console.WriteLine($"feat:stroke: red={red} blueStroke={blue}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatGlyphRun()
    {
        Window? window = null;
        try
        {
            var text = new TextBlock
            {
                Text = "Hello Nova",
                FontFamily = ProbeFont,
                FontSize = 20,
                Foreground = Brushes.Black
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = text };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int dark, int minX, int minY, int maxX, int maxY) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsDark);
            Console.WriteLine($"feat:glyphrun: dark={dark} bbox=({minX},{minY})-({maxX},{maxY}) textDesired={text.DesiredSize}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatClip()
    {
        Window? window = null;
        try
        {
            var rect = new Rectangle
            {
                Width = 200,
                Height = 100,
                Fill = Brushes.Red,
                Clip = new RectangleGeometry(new Rect(0, 0, 100, 100))
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int minX, int minY, int maxX, int maxY) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsRed);
            int width = maxX >= 0 ? maxX - minX + 1 : 0;
            Console.WriteLine($"feat:clip: red={red} bboxWidth={width} bbox=({minX},{minY})-({maxX},{maxY})");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatOpacity()
    {
        Window? window = null;
        try
        {
            var rect = new Rectangle { Width = 200, Height = 100, Fill = Brushes.Red, Opacity = 0.5 };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int blended, _, _, _, _) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsHalfRedOverWhite);
            Console.WriteLine($"feat:opacity: halfRedOverWhite={blended}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatTransform()
    {
        Window? window = null;
        try
        {
            var rect = new Rectangle
            {
                Width = 40,
                Height = 40,
                Fill = Brushes.Red,
                RenderTransform = new ScaleTransform(2, 2),
                RenderTransformOrigin = new Point(0, 0)
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int minX, int minY, int maxX, int maxY) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsRed);
            int width = maxX >= 0 ? maxX - minX + 1 : 0;
            int height = maxY >= 0 ? maxY - minY + 1 : 0;
            Console.WriteLine($"feat:transform: red={red} bbox={width}x{height} at ({minX},{minY})");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatImage()
    {
        Window? window = null;
        try
        {
            var image = new Image { Width = 80, Height = 40, Stretch = Stretch.Fill, Source = CreateProbeBitmap() };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = image };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            Console.WriteLine($"feat:image: actual={image.ActualWidth}x{image.ActualHeight} bitmapPixels red={red} blue={blue}");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.WriteLine($"feat:image: EXCEPTION {error.GetType().FullName}: {ShortMessage(error.Message)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatLinearGradient()
    {
        Window? window = null;
        try
        {
            var brush = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
            var rect = new Rectangle { Width = 200, Height = 100, Fill = brush };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            Console.WriteLine($"feat:lineargradient: red={red} blue={blue}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatRadialGradient()
    {
        Window? window = null;
        try
        {
            var brush = new RadialGradientBrush(Colors.Red, Colors.Blue);
            var rect = new Rectangle { Width = 200, Height = 100, Fill = brush };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            Console.WriteLine($"feat:radialgradient: red={red} blue={blue}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatVisualBrush()
    {
        Window? window = null;
        try
        {
            var inner = new Rectangle { Width = 40, Height = 40, Fill = Brushes.Red };
            var rect = new Rectangle { Width = 120, Height = 80, Fill = new VisualBrush(inner) };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, _, _, _, _) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsRed);
            Console.WriteLine($"feat:visualbrush: red={red}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatImageBrush()
    {
        Window? window = null;
        try
        {
            var rect = new Rectangle
            {
                Width = 120,
                Height = 80,
                Fill = new ImageBrush(CreateProbeBitmap()) { Stretch = Stretch.Fill }
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            Console.WriteLine($"feat:imagebrush: red={red} blue={blue}");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.WriteLine($"feat:imagebrush: EXCEPTION {error.GetType().FullName}: {ShortMessage(error.Message)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatRounded()
    {
        Window? window = null;
        try
        {
            var border = new Border
            {
                Width = 200,
                Height = 100,
                Background = Brushes.Red,
                CornerRadius = new CornerRadius(20)
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = border };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            (int red, int minX, int minY, int maxX, int maxY) = FindColor(pixels.Span, source.PixelWidth, IsRed);
            int width = maxX >= 0 ? maxX - minX + 1 : 0;
            int height = maxY >= 0 ? maxY - minY + 1 : 0;
            Console.WriteLine($"feat:rounded: red={red} bbox={width}x{height} at ({minX},{minY}) (plain rect would be 200x100 at the content origin)");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatAntiAlias()
    {
        Window? window = null;
        try
        {
            var rect = new Rectangle
            {
                Width = 120,
                Height = 120,
                Fill = Brushes.Red,
                RenderTransform = new RotateTransform(45),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, int minX, int minY, int maxX, int maxY) = FindColor(pixels.Span, width, IsRed);
            // Partial-coverage edge pixels: red channel high but green/blue NOT at the
            // hard-edge values (0 or 255). No MSAA => every pixel is either full or absent.
            int partial = CountColor(pixels.Span, width, static (r, g, b) => r > 200 && g > 0 && g < 255 && b > 0 && b < 255);
            Console.WriteLine($"feat:antialias: red={red} bbox=({minX},{minY})-({maxX},{maxY}) partialEdgePixels={partial}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatRenderTargetBitmap()
    {
        try
        {
            var rtb = new RenderTargetBitmap(64, 48, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (DrawingContext dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 32, 32));
            }

            rtb.Render(drawing);
            var buffer = new byte[64 * 48 * 4];
            rtb.CopyPixels(buffer, 64 * 4, 0);
            int red = 0;
            for (int i = 0; i + 3 < buffer.Length; i += 4)
            {
                if (buffer[i] > 128 && buffer[i + 1] < 64 && buffer[i + 2] < 64)
                {
                    red++;
                }
            }

            Console.WriteLine($"feat:rtb: size={rtb.PixelWidth}x{rtb.PixelHeight} red={red}");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.WriteLine($"feat:rtb: EXCEPTION {error.GetType().FullName}: {ShortMessage(error.Message)}");
            return 0;
        }
    }

    private static int FeatAnimation()
    {
        Window? window = null;
        try
        {
            var button = new Button { Content = "anim", Width = 20, Height = 20 };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = button };
            window.Show();
            window.UpdateLayout();

            var storyboard = new Storyboard { Duration = TimeSpan.FromMilliseconds(300) };
            var animation = new DoubleAnimation(20, 200, TimeSpan.FromMilliseconds(300));
            Storyboard.SetTarget(animation, button);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Button.WidthProperty));
            storyboard.Children.Add(animation);
            storyboard.Begin(window);

            var samples = new List<double>();
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 1500)
            {
                FlushDispatcher();
                SdlPresentationSource.PresentAll();
                samples.Add(button.ActualWidth);
                Thread.Sleep(20);
            }

            double min = samples.Min();
            double max = samples.Max();
            Console.WriteLine($"feat:animation: samples={samples.Count} first={samples[0]:F1} min={min:F1} max={max:F1} last={samples[^1]:F1}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatDispatcherTimer()
    {
        Window? window = null;
        try
        {
            window = new Window { Width = 200, Height = 80 };
            window.Show();
            int ticks = 0;
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.ApplicationIdle,
                (_, _) => ticks++,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 700)
            {
                FlushDispatcher();
                Thread.Sleep(15);
            }

            timer.Stop();
            Console.WriteLine($"feat:dispatchertimer: ticks={ticks} elapsed={stopwatch.ElapsedMilliseconds}ms");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatBinding()
    {
        Window? window = null;
        try
        {
            var vm = new ProbeViewModel { Name = "before" };
            var text = new TextBlock();
            _ = text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProbeViewModel.Name)) { Source = vm });
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = text };
            window.Show();
            window.UpdateLayout();
            FlushDispatcher();
            string before = text.Text;
            vm.Name = "after";
            FlushDispatcher();
            Console.WriteLine($"feat:binding: before='{before}' after='{text.Text}'");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatCommand()
    {
        Window? window = null;
        try
        {
            bool executed = false;
            var command = new ProbeCommand(() => executed = true);
            var button = new Button { Content = "go", Width = 80, Height = 32, Command = command };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = button };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            FlushDispatcher();
            uint windowId = GetWindowId(source);
            Point center = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
            Point device = source.CompositionTarget.TransformToDevice.Transform(center);
            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, (int)Math.Round(device.X), (int)Math.Round(device.Y), (byte)Nova.Sdl.MouseButton.Left);
            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, (int)Math.Round(device.X), (int)Math.Round(device.Y), (byte)Nova.Sdl.MouseButton.Left);
            PumpPass(source);
            Console.WriteLine($"feat:command: executed={executed} canExecute={command.CanExecute(null)} buttonEnabled={button.IsEnabled}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatStyle()
    {
        Window? window = null;
        try
        {
            var style = new Style(typeof(Rectangle));
            style.Setters.Add(new Setter(Rectangle.FillProperty, Brushes.Red));
            var rect = new Rectangle { Width = 100, Height = 60, Style = style };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, _, _, _, _) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsRed);
            Console.WriteLine($"feat:style: red={red}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatTrigger()
    {
        Window? window = null;
        try
        {
            var vm = new ProbeViewModel { Name = "x", Flag = false };
            var style = new Style(typeof(Rectangle));
            style.Setters.Add(new Setter(Rectangle.FillProperty, Brushes.Red));
            var trigger = new DataTrigger
            {
                Binding = new Binding(nameof(ProbeViewModel.Flag)) { Source = vm },
                Value = true
            };
            trigger.Setters.Add(new Setter(Rectangle.FillProperty, Brushes.Blue));
            style.Triggers.Add(trigger);
            var rect = new Rectangle { Width = 100, Height = 60, Style = style };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            int width = source.PixelWidth;
            (int redBefore, _, _, _, _) = FindColor(source.ReadbackRgba().Span, width, IsRed);
            vm.Flag = true;
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int redAfter, _, _, _, _) = FindColor(source.ReadbackRgba().Span, width, IsRed);
            (int blueAfter, _, _, _, _) = FindColor(source.ReadbackRgba().Span, width, IsBlue);
            Console.WriteLine($"feat:trigger: redBefore={redBefore} redAfter={redAfter} blueAfter={blueAfter}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatSizeToContent()
    {
        Window? window = null;
        try
        {
            var border = new Border { Width = 100, Height = 50, Background = Brushes.Red };
            window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = border };
            window.Show();
            window.UpdateLayout();
            FlushDispatcher();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            bool placement = source.GetPlacement(out int x, out int y, out int w, out int h);
            Console.WriteLine(
                $"feat:sizetocontent: placement={placement} {w}x{h} pixelSize={source.PixelWidth}x{source.PixelHeight} " +
                $"windowWidth={window.Width.ToString("F1", CultureInfo.InvariantCulture)} windowHeight={window.Height.ToString("F1", CultureInfo.InvariantCulture)} " +
                $"content={border.ActualWidth.ToString("F1", CultureInfo.InvariantCulture)}x{border.ActualHeight.ToString("F1", CultureInfo.InvariantCulture)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatDpi()
    {
        Window? window = null;
        try
        {
            window = new Window { Width = 320, Height = 200 };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            Matrix transformToDevice = source.CompositionTarget.TransformToDevice;
            Console.WriteLine($"feat:dpi: transformToDevice=({transformToDevice.M11.ToString("F3", CultureInfo.InvariantCulture)},{transformToDevice.M22.ToString("F3", CultureInfo.InvariantCulture)}) pixelSize={source.PixelWidth}x{source.PixelHeight}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatRtl()
    {
        Window? window = null;
        try
        {
            var text = new TextBlock
            {
                Text = "مرحبا بالعالم",
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = ProbeFont,
                FontSize = 20,
                Foreground = Brushes.Black
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = text };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int dark, int minX, int minY, int maxX, int maxY) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsDark);
            Console.WriteLine($"feat:rtl: dark={dark} bbox=({minX},{minY})-({maxX},{maxY}) desired={text.DesiredSize} actual={text.ActualWidth.ToString("F1", CultureInfo.InvariantCulture)}x{text.ActualHeight.ToString("F1", CultureInfo.InvariantCulture)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatTextWrap()
    {
        Window? window = null;
        try
        {
            var text = new TextBlock
            {
                Text = "the quick brown fox jumps over the lazy dog",
                Width = 90,
                FontFamily = ProbeFont,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Black
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = text };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int dark, _, _, _, _) = FindColor(source.ReadbackRgba().Span, source.PixelWidth, IsDark);
            Console.WriteLine($"feat:textwrap: dark={dark} desired={text.DesiredSize} actual={text.ActualWidth.ToString("F1", CultureInfo.InvariantCulture)}x{text.ActualHeight.ToString("F1", CultureInfo.InvariantCulture)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatWheel()
    {
        Window? window = null;
        try
        {
            var scroller = new ScrollViewer
            {
                Width = 200,
                Height = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Rectangle { Width = 100, Height = 1000, Fill = Brushes.Red }
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = scroller };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            FlushDispatcher();
            uint windowId = GetWindowId(source);
            Point center = scroller.TranslatePoint(new Point(scroller.ActualWidth / 2, scroller.ActualHeight / 2), window);
            PushWheel(windowId, (int)center.X, (int)center.Y, -3);
            PumpPass(source);
            PumpPass(source);
            Console.WriteLine($"feat:wheel: verticalOffset={scroller.VerticalOffset.ToString("F1", CultureInfo.InvariantCulture)} scrollable={scroller.ScrollableHeight} delta=(-3)");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatTabFocus()
    {
        Window? window = null;
        try
        {
            var box1 = new TextBox { Width = 120, Height = 24 };
            var box2 = new TextBox { Width = 120, Height = 24 };
            var panel = new StackPanel();
            _ = panel.Children.Add(box1);
            _ = panel.Children.Add(box2);
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = panel };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            FlushDispatcher();
            _ = box1.Focus();
            PumpPass(source);
            uint windowId = GetWindowId(source);
            uint tab = (uint)KeyInterop.VirtualKeyFromKey(Key.Tab);
            PushKey(SdlEventType.KeyDown, windowId, down: true, tab);
            PushKey(SdlEventType.KeyUp, windowId, down: false, tab);
            PumpPass(source);
            Console.WriteLine($"feat:tabfocus: box1Focused={Keyboard.FocusedElement == box1} box2Focused={Keyboard.FocusedElement == box2} box2Within={box2.IsKeyboardFocusWithin}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatDragSelect()
    {
        Window? window = null;
        try
        {
            var box = new TextBox { Text = "hello world hello", Width = 200, Height = 24, Background = Brushes.White };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = box };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            FlushDispatcher();
            _ = box.Focus();
            PumpPass(source);
            uint windowId = GetWindowId(source);
            Point start = box.TranslatePoint(new Point(12, box.ActualHeight / 2), window);
            Point end = box.TranslatePoint(new Point(80, box.ActualHeight / 2), window);
            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, (int)start.X, (int)start.Y, (byte)Nova.Sdl.MouseButton.Left);
            PumpPass(source);
            PushMotion(windowId, (int)end.X, (int)end.Y);
            PumpPass(source);
            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, (int)end.X, (int)end.Y, (byte)Nova.Sdl.MouseButton.Left);
            PumpPass(source);
            Console.WriteLine($"feat:dragselect: selectionLength={box.SelectionLength} caretIndex={box.CaretIndex} captured={Mouse.Captured is null}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatMultiWindow()
    {
        Window? windowA = null;
        Window? windowB = null;
        try
        {
            windowA = new Window { Width = 200, Height = 120, Content = new Rectangle { Fill = Brushes.Red } };
            windowB = new Window { Width = 200, Height = 120, Content = new Rectangle { Fill = Brushes.Blue } };
            windowA.Show();
            windowB.Show();
            windowA.UpdateLayout();
            windowB.UpdateLayout();
            var sourceA = (SdlPresentationSource)PresentationSource.FromVisual(windowA);
            var sourceB = (SdlPresentationSource)PresentationSource.FromVisual(windowB);
            sourceA.EnableReadback();
            sourceB.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            int widthA = sourceA.PixelWidth;
            int widthB = sourceB.PixelWidth;
            (int redA, _, _, _, _) = FindColor(sourceA.ReadbackRgba().Span, widthA, IsRed);
            (int blueB, _, _, _, _) = FindColor(sourceB.ReadbackRgba().Span, widthB, IsBlue);
            (int redB, _, _, _, _) = FindColor(sourceB.ReadbackRgba().Span, widthB, IsRed);
            Console.WriteLine($"feat:multiwindow: windowA red={redA} windowB blue={blueB} windowB red={redB}");
            return 0;
        }
        finally
        {
            windowA?.Close();
            windowB?.Close();
        }
    }

    private static int FeatWindowState()
    {
        Window? window = null;
        try
        {
            window = new Window { Width = 320, Height = 200 };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            FlushDispatcher();
            window.WindowState = WindowState.Maximized;
            FlushDispatcher();
            WindowState afterMaximize = window.WindowState;
            window.WindowState = WindowState.Minimized;
            FlushDispatcher();
            WindowState afterMinimize = window.WindowState;
            window.WindowState = WindowState.Normal;
            FlushDispatcher();
            _ = source.GetPlacement(out int x, out int y, out int w, out int h);
            Console.WriteLine($"feat:windowstate: afterMaximize={afterMaximize} afterMinimize={afterMinimize} placement={w}x{h} pixelSize={source.PixelWidth}x{source.PixelHeight}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatBorderStroke()
    {
        Window? window = null;
        try
        {
            // Border with NO child and NO text: is BorderBrush rendered at all?
            var border = new Border
            {
                Width = 120,
                Height = 80,
                Background = Brushes.Red,
                BorderBrush = Brushes.Blue,
                BorderThickness = new Thickness(4)
            };
            window = new Window { Width = 200, Height = 140, Content = border };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int blue, _, _, _, _) = FindColor(pixels.Span, width, IsBlue);
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            Console.WriteLine($"feat:borderstroke: red={red} blueBorder={blue}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatDropShadow()
    {
        Window? window = null;
        try
        {
            // Blue content with a red shadow cast to the RIGHT (Direction=0). The shadow must
            // appear right of the content, never left, and the content must stay crisp on top.
            var rect = new Rectangle
            {
                Width = 100,
                Height = 50,
                Fill = Brushes.Blue,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Red,
                    ShadowDepth = 12,
                    Direction = 0,
                    BlurRadius = 4,
                    Opacity = 1
                }
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, int rMinX, int rMinY, int rMaxX, int rMaxY) = FindColor(pixels.Span, width, IsSoftRed);
            (int blue, int bMinX, int bMinY, int bMaxX, int bMaxY) = FindColor(pixels.Span, width, IsBlue);
            // Direction=0: visible shadow (soft red over white) starts right of the content's
            // left edge and extends past its right edge; the content stays crisp blue on top.
            bool right = red > 0 && blue > 0 && rMinX > bMinX && rMaxX > bMaxX;
            Console.WriteLine(
                $"feat:dropshadow: red={red} redBBox=({rMinX},{rMinY})-({rMaxX},{rMaxY}) " +
                $"blue={blue} blueBBox=({bMinX},{bMinY})-({bMaxX},{bMaxY}) shadowRight={right}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatBlur()
    {
        Window? window = null;
        try
        {
            // Red content with BlurEffect.Radius=8: the hard edge becomes a gradient, and the
            // blurred extent exceeds the original 120x60 geometry by roughly the radius.
            var rect = new Rectangle
            {
                Width = 120,
                Height = 60,
                Fill = Brushes.Red,
                Effect = new BlurEffect { Radius = 8 }
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, _, _, _, _) = FindColor(pixels.Span, width, IsRed);
            (int soft, int sMinX, int sMinY, int sMaxX, int sMaxY) = FindColor(pixels.Span, width, IsSoftRed);
            bool bleeds = soft > 0 && (sMaxX - sMinX) > 120 && (sMaxY - sMinY) > 60;
            Console.WriteLine(
                $"feat:blur: red={red} softRed={soft} softBBox=({sMinX},{sMinY})-({sMaxX},{sMaxY}) " +
                $"bleedsBeyond120x60={bleeds}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatOpacityMask()
    {
        Window? window = null;
        try
        {
            // Red content masked by a horizontal white->transparent linear gradient: the left
            // (mask alpha ~1) stays red, the right (mask alpha ~0) fades toward the white
            // background. The fully-red region must not reach the content's right edge.
            var rect = new Rectangle
            {
                Width = 120,
                Height = 60,
                Fill = Brushes.Red,
                OpacityMask = new LinearGradientBrush(
                    [new GradientStop(Colors.White, 0), new GradientStop(Colors.Transparent, 1)],
                    new Point(0, 0),
                    new Point(1, 0))
            };
            window = new Window { Width = ProbeWindowWidth, Height = ProbeWindowHeight, Content = rect };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int red, int rMinX, int rMinY, int rMaxX, int rMaxY) = FindColor(pixels.Span, width, IsRed);
            (int soft, _, _, _, _) = FindColor(pixels.Span, width, IsSoftRed);
            // The mask's low-alpha (right) region must be attenuated: the fully-red region's
            // right edge stays left of the content's right edge, and a faded transition exists.
            bool attenuated = red > 0 && soft > 0 && (rMaxX - rMinX) < 120;
            Console.WriteLine(
                $"feat:opacitymask: red={red} redBBox=({rMinX},{rMinY})-({rMaxX},{rMaxY}) softRed={soft} " +
                $"attenuatedRight={attenuated}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    /// <summary>The 18 PUA codepoints the Fluent theme references via
    /// <c>{DynamicResource SymbolThemeFontFamily}</c> (extracted from Styles/*.xaml).</summary>
    private static readonly string[] FluentIconCodepoints =
    [
        "\uE70D", "\uE70E", "\uE72A", "\uE72B", "\uE73E", "\uE76B", "\uE76C", "\uE787", "\uE894",
        "\uE915", "\uE9AE", "\uEDD9", "\uEDDA", "\uEDDB", "\uEDDC", "\uF08E", "\uF090", "\uF169"
    ];

    private static int FeatFluentTheme()
    {
        Window? buttonWindow = null;
        Window? textBoxWindow = null;
        Window? checkBoxWindow = null;
        try
        {
            // Classic and Aero2 baselines in THIS process via the inherited uxtheme opt-in,
            // BEFORE any Application exists (windows render fine without one). The Fluent
            // comparison is RELATIVE to these live measurements, so it survives the sRGB
            // encode fix (which shifts every mid-tone absolute value).
            HostTheme.SetTheme("classic");
            FireThemeChanged();
            buttonWindow = ProbeFluentWindow(new Button { Content = "Go", FontFamily = ProbeFont });
            (int classicR, int classicG, int classicB, int classicCount, int classicDistinct) = Dominant(buttonWindow);
            buttonWindow.Close();
            buttonWindow = null;

            HostTheme.SetTheme("aero2");
            FireThemeChanged();
            buttonWindow = ProbeFluentWindow(new Button { Content = "Go", FontFamily = ProbeFont });
            (int aero2R, int aero2G, int aero2B, int aero2Count, int aero2Distinct) = Dominant(buttonWindow);
            buttonWindow.Close();
            buttonWindow = null;
            RestoreClassicTheme();

            // Stock WPF Application + ThemeMode.Light: ThemeManager merges the Fluent
            // dictionaries into Application.Current.Resources at index 0.
            Application app = new();
            try
            {
                var fonts = new ResourceDictionary { ["SymbolThemeFontFamily"] = new FontFamily("Nova Fluent Icons") };
                app.Resources.MergedDictionaries.Add(fonts); // index 1: reverse lookup wins over index 0
                app.ThemeMode = ThemeMode.Light;

                // Dictionary-source assertion (the test asserts; here it is reported).
                string source = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source?.ToString().Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) == true)
                    ?.Source?.ToString() ?? "NONE";

                buttonWindow = ProbeFluentWindow(new Button { Content = "Go", FontFamily = ProbeFont });
                (int bR, int bG, int bB, int bCount, int bDistinct) = Dominant(buttonWindow);

                textBoxWindow = ProbeFluentWindow(new TextBox { Text = "abc", FontFamily = ProbeFont });
                (int tR, int tG, int tB, int tCount, int tDistinct) = Dominant(textBoxWindow);

                checkBoxWindow = ProbeFluentWindow(new CheckBox { Content = "on", FontFamily = ProbeFont, IsChecked = true });
                (int cR, int cG, int cB, int cCount, int cDistinct) = Dominant(checkBoxWindow);

                Console.WriteLine(
                    $"feat:fluenttheme: dictionarySource={source} " +
                    $"classicDom=({classicR},{classicG},{classicB})x{classicCount} " +
                    $"aero2Dom=({aero2R},{aero2G},{aero2B})x{aero2Count} " +
                    $"buttonDom=({bR},{bG},{bB})x{bCount} distinct={bDistinct} " +
                    $"textBoxDom=({tR},{tG},{tB})x{tCount} distinct={tDistinct} " +
                    $"checkBoxDom=({cR},{cG},{cB})x{cCount} distinct={cDistinct}");
                // Gate (structural, survives the sRGB fix): the Fluent dictionary must be the
                // loaded theme, and the Fluent Button face must be measurably LIGHTER than
                // Classic's and Aero2's measured in this same process (Fluent Light uses a
                // translucent white fill; Classic/Aero2 use grey fills).
                if (!source.Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"fluenttheme-GATE: dictionary source is not Fluent: {source}");
                    return 1;
                }

                if (bR <= classicR)
                {
                    Console.Error.WriteLine($"fluenttheme-GATE: Fluent button ({bR}) not lighter than Classic ({classicR})");
                    return 1;
                }

                if (bR <= aero2R)
                {
                    Console.Error.WriteLine($"fluenttheme-GATE: Fluent button ({bR}) not lighter than Aero2 ({aero2R})");
                    return 1;
                }

                if (classicR == aero2R)
                {
                    Console.Error.WriteLine($"fluenttheme-GATE: Classic ({classicR}) and Aero2 ({aero2R}) chrome identical — theme switch broken");
                    return 1;
                }

                return 0;
            }
            finally
            {
                app.Shutdown();
            }
        }
        finally
        {
            buttonWindow?.Close();
            textBoxWindow?.Close();
            checkBoxWindow?.Close();
        }
    }

    /// <summary>Renders every Fluent icon codepoint through the EXACT theme chain
    /// (<c>{DynamicResource SymbolThemeFontFamily}</c>, like the DataGrid sort carets and the
    /// other 140 refs) and counts dark pixels per glyph. Tofu (missing glyph) renders a hollow
    /// box with far fewer dark pixels than the filled glyph.</summary>
    private static int FeatFluentGlyphs()
    {
        Application app = new();
        Window? window = null;
        try
        {
            var fonts = new ResourceDictionary { ["SymbolThemeFontFamily"] = new FontFamily("Nova Fluent Icons") };
            app.Resources.MergedDictionaries.Add(fonts);
            app.ThemeMode = ThemeMode.Light;

            var text = new TextBlock
            {
                FontSize = 48,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            // The exact theme chain: DynamicResource SymbolThemeFontFamily resolves the
            // app-level override ("Nova Fluent Icons") ahead of the theme dictionary.
            text.SetResourceReference(TextBlock.FontFamilyProperty, "SymbolThemeFontFamily");
            window = ProbeFluentWindow(text);
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();

            var rows = new List<string>();
            var glyphDarkCounts = new Dictionary<string, int>();
            foreach (string glyph in FluentIconCodepoints)
            {
                text.Text = glyph;
                window.UpdateLayout();
                FlushDispatcher();
                SdlPresentationSource.PresentAll();
                SdlPresentationSource.PresentAll();
                ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
                int dark = CountColor(pixels.Span, source.PixelWidth, IsDark);
                glyphDarkCounts[glyph] = dark;
                rows.Add($"U+{(int)glyph[0]:X4}={dark}");
            }

            Console.WriteLine("feat:fluentglyph: " + string.Join(" ", rows));
            // Gate: every codepoint must render dark pixels (no blanks). The DataGrid sort
            // carets F090/F08E additionally render as SOLID filled carets (>= 200 dark px at
            // 48px; a missing-glyph tofu box measures ~138) with mirror-similar counts.
            foreach (string glyph in FluentIconCodepoints)
            {
                if (glyphDarkCounts.TryGetValue(glyph, out int dark) && dark == 0)
                {
                    Console.Error.WriteLine($"fluentglyph-GATE: U+{(int)glyph[0]:X4} rendered blank (0 dark px)");
                    return 1;
                }
            }

            int up = glyphDarkCounts["\uF090"];
            int down = glyphDarkCounts["\uF08E"];
            if (up < 200 || down < 200)
            {
                Console.Error.WriteLine($"fluentglyph-GATE: DataGrid carets are tofu (F090={up}, F08E={down}, need >= 200)");
                return 1;
            }

            if (Math.Abs(up - down) > up / 3)
            {
                Console.Error.WriteLine($"fluentglyph-GATE: mirror carets must have similar counts (F090={up}, F08E={down})");
                return 1;
            }

            return 0;
        }
        finally
        {
            window?.Close();
            app.Shutdown();
        }
    }

    /// <summary>Renders the Fluent ToolTip (which carries a DropShadowEffect in its template)
    /// and verifies shadow pixels spread outside the solid tooltip content bbox.</summary>
    private static int FeatFluentShadow()
    {
        Application app = new();
        Window? window = null;
        try
        {
            var fonts = new ResourceDictionary { ["SymbolThemeFontFamily"] = new FontFamily("Nova Fluent Icons") };
            app.Resources.MergedDictionaries.Add(fonts);
            app.ThemeMode = ThemeMode.Light;

            // A window with a button whose ToolTip gets the Fluent ToolTip style.
            var button = new Button { Content = "tip", FontFamily = ProbeFont, ToolTip = new ToolTip { Content = "shadowed" } };
            window = ProbeFluentWindow(button);
            FlushDispatcher();

            var tip = (ToolTip)button.ToolTip;
            tip.PlacementTarget = button;
            tip.IsOpen = true;
            FlushDispatcher();

            var mainSource = (SdlPresentationSource)PresentationSource.FromVisual(window);
            SdlPresentationSource? popupSource = FindPopupSource(mainSource);
            if (popupSource is null)
            {
                Console.WriteLine("feat:fluentshadow: no-popup-source main=" + mainSource.PixelWidth);
                return 0;
            }

            try
            {
                popupSource.EnableReadback();
                SdlPresentationSource.PresentAll();
                SdlPresentationSource.PresentAll();
                ReadOnlyMemory<byte> pixels = popupSource.ReadbackRgba();
                int width = popupSource.PixelWidth;
                // The tooltip content is solid; the shadow is semi-dark pixels around it.
                // Dark-ish pixels (the #202020 shadow at 40% over the window) spread beyond
                // the solid content bbox.
                (int dark, int minX, int minY, int maxX, int maxY) = FindColor(pixels.Span, width, IsDark);
                int contentBBox = (maxX - minX + 1) * (maxY - minY + 1);
                Console.WriteLine(
                    $"feat:fluentshadow: dark={dark} bbox={contentBBox}px " +
                    $"bbox=({minX},{minY})-({maxX},{maxY}) popup={popupSource.PixelWidth}x{popupSource.PixelHeight} " +
                    $"px0={FormatPixel(pixels.Span)}");
                // Gate: the Fluent ToolTip template's DropShadowEffect must draw shadow pixels
                // (dark spread) beyond the solid tooltip content bbox.
                if (dark == 0)
                {
                    Console.Error.WriteLine("fluentshadow-GATE: no dark shadow pixels rendered");
                    return 1;
                }

                if (contentBBox <= dark)
                {
                    Console.Error.WriteLine($"fluentshadow-GATE: shadow does not spread beyond content (bbox {contentBBox} vs {dark} dark px)");
                    return 1;
                }

                return 0;
            }
            finally
            {
                tip.IsOpen = false;
                FlushDispatcher();
            }
        }
        finally
        {
            window?.Close();
            app.Shutdown();
        }
    }

    private static Window ProbeFluentWindow(FrameworkElement control)
    {
        var window = new Window { Width = 200, Height = 100, Content = control };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static (int R, int G, int B, int Count, int Distinct) Dominant(Window window)
    {
        var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
        source.EnableReadback();
        FlushDispatcher();
        SdlPresentationSource.PresentAll();
        SdlPresentationSource.PresentAll();
        ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
        ReadOnlySpan<byte> p = pixels.Span;
        var counts = new Dictionary<int, int>();
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            int key = (p[i] << 16) | (p[i + 1] << 8) | p[i + 2];
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        int bestKey = -1;
        int bestCount = -1;
        foreach (KeyValuePair<int, int> kv in counts)
        {
            if (kv.Value > bestCount)
            {
                bestKey = kv.Key;
                bestCount = kv.Value;
            }
        }

        return ((byte)(bestKey >> 16), (byte)(bestKey >> 8), (byte)bestKey, bestCount, counts.Count);
    }

    private static int FeatSystemParams()
    {
        string glass;
        try
        {
            glass = SystemParameters.IsGlassEnabled.ToString();
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            glass = "THROWS " + error.GetType().Name;
        }

        Console.WriteLine(
            $"feat:systemparams: screen={SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight} " +
            $"workArea={SystemParameters.WorkArea} caretWidth={SystemParameters.CaretWidth} " +
            $"wheelScrollLines={SystemParameters.WheelScrollLines} borderWidth={SystemParameters.BorderWidth} glass={glass}");
        return 0;
    }

    private static int FeatSystemColors()
    {
        Console.WriteLine(
            $"feat:systemcolors: window={SystemColors.WindowColor} control={SystemColors.ControlColor} " +
            $"highlight={SystemColors.HighlightColor} menu={SystemColors.MenuColor} " +
            $"windowText={SystemColors.WindowTextColor} controlText={SystemColors.ControlTextColor}");
        return 0;
    }

    private static int FeatDetheme()
    {
        // Desktop-palette opt-in (NOVA_PALETTE=desktop) pointing at the committed fixture home.
        // Apply the theme BEFORE any window/WPF interaction, exactly as a real app would at
        // startup: this is what keeps the SystemColors statics from memoizing Classic values
        // first. The SdlHost ctor re-applies the same env-driven theme when the window opens.
        Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, DesktopThemeApplier.PaletteDesktopValue);
        Environment.SetEnvironmentVariable(
            "NOVA_DESKTOP_THEME_HOME",
            System.IO.Path.Combine(AppContext.BaseDirectory, "detheme-fixtures", "kde"));
        HostTheme.SetProvider(DesktopThemeApplier.ApplyToProvider(
            null,
            DesktopThemeApplier.CreateDefault(System.IO.Path.Combine(AppContext.BaseDirectory, "detheme-fixtures", "kde"))));

        // Pixel proof via Rectangle fills bound to the SystemColors brushes — the same brush
        // instances Classic.xaml resolves through SystemColors.*Key DynamicResources. The
        // window background is forced BLACK so the three themed swatches stand alone.
        // NOTE: the readback asserts the LINEARIZED values, not the sRGB source values: this
        // host's Vulkan presenter decodes sRGB→linear without re-encoding (a pre-existing
        // quirk that only affects non-pure colors, which is why the all-pure baseline never
        // saw it). #1E1E1E→#030303, #444444→#0F0F0F, #0AA0E6→#015ACA.
        SolidColorBrush windowBrush = SystemColors.WindowBrush;       // #1E1E1E (kdeglobals Window)
        SolidColorBrush controlBrush = SystemColors.ControlBrush;     // #444444 (kdeglobals Button)
        SolidColorBrush highlightBrush = SystemColors.HighlightBrush; // #0AA0E6 (kdeglobals Selection)

        Window? window = null;
        try
        {
            var panel = new StackPanel { Width = 200 };
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 40, Fill = windowBrush });
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 40, Fill = controlBrush });
            _ = panel.Children.Add(new Rectangle { Width = 200, Height = 40, Fill = highlightBrush });
            window = new Window
            {
                Width = ProbeWindowWidth,
                Height = ProbeWindowHeight,
                Background = Brushes.Black,
                Content = panel
            };
            window.Show();
            window.UpdateLayout();
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            FlushDispatcher();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            int width = source.PixelWidth;
            (int windowPx, _, _, _, _) = FindColor(pixels.Span, width, static (r, g, b) => r is >= 2 and <= 5 && g is >= 2 and <= 5 && b is >= 2 and <= 5);
            (int controlPx, _, _, _, _) = FindColor(pixels.Span, width, static (r, g, b) => r is >= 10 and <= 20 && g is >= 10 and <= 20 && b is >= 10 and <= 20);
            (int highlightPx, _, _, _, _) = FindColor(pixels.Span, width, static (r, g, b) => r <= 4 && g is >= 84 and <= 96 && b is >= 196 and <= 210);
            string all = string.Join(" ", CountAllColors(pixels.Span).Select(static pair => pair.Key + "=" + pair.Value));
            Console.WriteLine(
                $"feat:detheme: windowPx={windowPx} controlPx={controlPx} highlightPx={highlightPx} all={all} " +
                $"window={SystemColors.WindowColor} control={SystemColors.ControlColor} " +
                $"highlight={SystemColors.HighlightColor} menu={SystemColors.MenuColor} " +
                $"windowText={SystemColors.WindowTextColor} controlText={SystemColors.ControlTextColor} " +
                $"rawWindow=0x{HostTheme.GetSysColor(SystemColorIndex.Window):X8} " +
                $"rawControl=0x{HostTheme.GetSysColor(SystemColorIndex.ButtonFace):X8} " +
                $"rawHighlight=0x{HostTheme.GetSysColor(SystemColorIndex.Highlight):X8} " +
                $"font={SystemFonts.MessageFontFamily.Source} size={SystemFonts.MessageFontSize.ToString("F1", CultureInfo.InvariantCulture)}");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.WriteLine($"feat:detheme: EXCEPTION {error.GetType().FullName}: {ShortMessage(error.Message)}");
            return 0;
        }
        finally
        {
            window?.Close();
        }
    }

    private static int FeatDethemeLive()
    {
        // Live-restyle proof: apply theme A, prime SystemColors, change the source file,
        // trigger the bridge's ApplyLive (the same call the file watcher / portal signal
        // routes to), and assert SystemColors re-resolves — the DynamicResource re-eval path.
        string home = System.IO.Path.Combine(
            AppContext.BaseDirectory, "detheme-fixtures", "kde");
        Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, DesktopThemeApplier.PaletteDesktopValue);
        Environment.SetEnvironmentVariable("NOVA_DESKTOP_THEME_HOME", home);
        HostTheme.SetProvider(DesktopThemeApplier.ApplyToProvider(null, DesktopThemeApplier.CreateDefault(home)));

        string configFile = System.IO.Path.Combine(home, ".config", "kdeglobals");
        string original = File.ReadAllText(configFile);
        string changed = original.Replace(
            "BackgroundNormal=30,30,30",
            "BackgroundNormal=68,68,68",
            StringComparison.Ordinal);
        try
        {
            // Prime: apply + invalidate so SystemColors reflects theme A, then read before.
            _ = DesktopThemeHost.ApplyLive();
            var before = SystemColors.WindowColor.ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(configFile, changed);
            bool applied = DesktopThemeHost.ApplyLive();
            var after = SystemColors.WindowColor.ToString(CultureInfo.InvariantCulture);
            Console.WriteLine(
                $"feat:detheme-live: applied={applied} before={before} after={after} " +
                $"rawWindow=0x{HostTheme.GetSysColor(SystemColorIndex.Window):X8} " +
                $"active={DesktopThemeHost.IsActive()} provider={DesktopThemeApplier.LastAppliedProvider is not null} " +
                $"colors={DesktopThemeApplier.LastAppliedProvider?.Palette.Colors.Count}");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            Console.WriteLine($"feat:detheme-live: EXCEPTION {error.GetType().FullName}: {ShortMessage(error.Message)}");
            return 0;
        }
        finally
        {
            File.WriteAllText(configFile, original);
        }
    }

    // ---------------------------------------------------------------------
    // Shared probe plumbing
    // ---------------------------------------------------------------------

    private static BitmapSource CreateProbeBitmap()
    {
        const int size = 4;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = ((y * size) + x) * 4;
                bool left = x < 2;
                pixels[offset] = left ? (byte)255 : (byte)0;      // B
                pixels[offset + 1] = 0;                            // G
                pixels[offset + 2] = left ? (byte)0 : (byte)255;   // R
                pixels[offset + 3] = 255;                          // A
            }
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private static Dictionary<string, int> CountAllColors(ReadOnlySpan<byte> pixels)
    {
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            string key = "#"
                + pixels[i].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[i + 1].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[i + 2].ToString("X2", CultureInfo.InvariantCulture);
            _ = counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        return counts;
    }

    private static (int Count, int MinX, int MinY, int MaxX, int MaxY) FindColor(
        ReadOnlySpan<byte> pixels,
        int width,
        Func<byte, byte, byte, bool> match)
    {
        int count = 0;
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        if (width <= 0)
        {
            return (0, -1, -1, -1, -1);
        }

        int height = pixels.Length / 4 / width;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int offset = row + (x * 4);
                if (match(pixels[offset], pixels[offset + 1], pixels[offset + 2]))
                {
                    count++;
                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }
        }

        return count == 0 ? (0, -1, -1, -1, -1) : (count, minX, minY, maxX, maxY);
    }

    private static int CountColor(ReadOnlySpan<byte> pixels, int width, Func<byte, byte, byte, bool> match)
    {
        return FindColor(pixels, width, match).Count;
    }

    private static bool IsRed(byte r, byte g, byte b)
    {
        return r > 200 && g < 60 && b < 60;
    }

    /// <summary>Red blended toward white (blurred edge or low mask alpha): red-dominant but
    /// with elevated green/blue from the white background.</summary>
    private static bool IsSoftRed(byte r, byte g, byte b)
    {
        return r > 180 && g is > 60 and < 220 && b is > 60 and < 220 && r >= g + 25 && r >= b + 25;
    }

    private static bool IsBlue(byte r, byte g, byte b)
    {
        return b > 200 && r < 60 && g < 60;
    }

    private static bool IsDark(byte r, byte g, byte b)
    {
        return r < 128 && g < 128 && b < 128;
    }

    private static bool IsHalfRedOverWhite(byte r, byte g, byte b)
    {
        return r > 220 && g is > 80 and < 190 && b is > 80 and < 190;
    }

    private static void PumpPass(SdlPresentationSource source)
    {
        while (source.TryPump(out SdlEvent ev))
        {
            source.Dispatch(ev);
        }

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    private static unsafe uint GetWindowId(SdlPresentationSource source)
    {
        var silkWindow = new SilkWindow((void*)source.Handle);
        return SdlApi.GetWindowID(silkWindow);
    }

    private static void PushButton(SdlEventType type, uint windowId, bool down, int x, int y, byte button)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Button = new Silk.NET.SDL.MouseButtonEvent
            {
                Type = type,
                WindowID = windowId,
                Which = 1,
                Button = button,
                Down = down,
                Clicks = 1,
                X = x,
                Y = y
            }
        };
        if (!SdlApi.PushEvent(new Silk.NET.Core.Ref<Silk.NET.SDL.Event>(ref ev)))
        {
            throw new InvalidOperationException("SdlApi.PushEvent failed (mouse button).");
        }
    }

    private static void PushMotion(uint windowId, int x, int y)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Motion = new Silk.NET.SDL.MouseMotionEvent
            {
                Type = SdlEventType.MouseMotion,
                WindowID = windowId,
                Which = 1,
                X = x,
                Y = y,
                Xrel = 0,
                Yrel = 0
            }
        };
        if (!SdlApi.PushEvent(new Silk.NET.Core.Ref<Silk.NET.SDL.Event>(ref ev)))
        {
            throw new InvalidOperationException("SdlApi.PushEvent failed (mouse motion).");
        }
    }

    private static void PushWheel(uint windowId, int mouseX, int mouseY, int yDelta)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Wheel = new Silk.NET.SDL.MouseWheelEvent
            {
                Type = SdlEventType.MouseWheel,
                WindowID = windowId,
                Which = 1,
                X = 0,
                Y = yDelta,
                MouseX = mouseX,
                MouseY = mouseY
            }
        };
        if (!SdlApi.PushEvent(new Silk.NET.Core.Ref<Silk.NET.SDL.Event>(ref ev)))
        {
            throw new InvalidOperationException("SdlApi.PushEvent failed (mouse wheel).");
        }
    }

    private static void PushKey(SdlEventType type, uint windowId, bool down, uint keyCode)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Key = new Silk.NET.SDL.KeyboardEvent
            {
                Type = type,
                WindowID = windowId,
                Which = 1,
                Key = keyCode,
                Down = down,
                Repeat = false
            }
        };
        if (!SdlApi.PushEvent(new Silk.NET.Core.Ref<Silk.NET.SDL.Event>(ref ev)))
        {
            throw new InvalidOperationException("SdlApi.PushEvent failed (keyboard).");
        }
    }

    /// <summary>
    /// Caret-visibility probe: focuses a TextBox in a shown offscreen window, presents, reads
    /// back, and scans the box row for a dark (caret-colored) vertical line. Prints the box
    /// geometry and the dark columns so a 1px caret at the left padding is verifiable.
    /// </summary>
    private static int RunCaretProbe()
    {
        var window = new Window { Width = 320, Height = 120 };
        var box = new TextBox { Width = 200, Height = 24, FontFamily = ProbeFont };
        window.Content = box;
        window.Show();
        try
        {
            window.UpdateLayout();
            bool focused = box.IsKeyboardFocusWithin || box.Focus();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            IVulkanPresenter presenter = GetWindowPresenter(source);
            presenter.EnableReadback();
            source.Present();
            source.Present();
            ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();
            int width = source.PixelWidth;
            int boxY = (int)box.TranslatePoint(new Point(0, box.ActualHeight / 2), window).Y;
            var nonWhite = new List<string>();
            for (int x = 0; x < width; x++)
            {
                int rowOffset = boxY * width;
                int byteOffset = (rowOffset + x) * 4;
                byte r = pixels.Span[byteOffset];
                byte g = pixels.Span[byteOffset + 1];
                byte b = pixels.Span[byteOffset + 2];
                if (r < 250 || g < 250 || b < 250)
                {
                    nonWhite.Add($"{x}:{r},{g},{b}");
                }
            }

            Console.WriteLine($"caret-probe: focused={focused} boxY={boxY} caretWidth={SystemParameters.CaretWidth} nonWhiteOnRow={nonWhite.Count} {string.Join(" ", nonWhite.Take(12))}");            // Also test a manual adorner: if a red adorner rect renders, the adorner layer IS
            // rasterized and only the caret's specifics are at fault; if not, the adorner
            // layer content never reaches the MIL slave.
            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(box);
            if (layer is not null)
            {
                var redAdorner = new TestAdorner(box);
                layer.Add(redAdorner);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                source.Present();
                source.Present();
                ReadOnlyMemory<byte> withAdorner = presenter.ReadbackRgba();
                int redPixels = 0;
                for (int i = 0; i + 3 < withAdorner.Length; i += 4)
                {
                    if (withAdorner.Span[i] > 200 && withAdorner.Span[i + 1] < 60 && withAdorner.Span[i + 2] < 60)
                    {
                        redPixels++;
                    }
                }

                Console.WriteLine($"caret-probe: redAdornerOnBox={redPixels}");
            }

            // Red adorner on the TextBoxView (the RenderScope) layer — where the caret lives.
            object frameObj = typeof(SdlPresentationSource).GetProperty("Frame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source)!;
            _ = frameObj;
            FrameworkElement? textBoxView = FindTextBoxView(box);
            if (textBoxView is not null)
            {
                AdornerLayer? viewLayer = AdornerLayer.GetAdornerLayer(textBoxView);
                if (viewLayer is not null)
                {
                    var viewRed = new TestAdorner(textBoxView);
                    viewLayer.Add(viewRed);
                    Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
                    Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                    source.Present();
                    source.Present();
                    ReadOnlyMemory<byte> withViewAdorner = presenter.ReadbackRgba();
                    int viewRedPixels = 0;
                    for (int i = 0; i + 3 < withViewAdorner.Length; i += 4)
                    {
                        if (withViewAdorner.Span[i] > 200 && withViewAdorner.Span[i + 1] < 60 && withViewAdorner.Span[i + 2] < 60)
                        {
                            viewRedPixels++;
                        }
                    }

                    Console.WriteLine($"caret-probe: redAdornerOnTextBoxView={viewRedPixels}");
                }
            }

            // Green adorner with a CHILD UIElement (like the caret's CaretSubElement) on both layers.
            static int CountGreen(ReadOnlyMemory<byte> data)
            {
                int green = 0;
                for (int i = 0; i + 3 < data.Length; i += 4)
                {
                    if (data.Span[i] < 60 && data.Span[i + 1] > 200 && data.Span[i + 2] < 60)
                    {
                        green++;
                    }
                }

                return green;
            }

            AdornerLayer? boxLayer = AdornerLayer.GetAdornerLayer(box);
            if (boxLayer is not null)
            {
                var childRed = new ChildAdorner(box);
                boxLayer.Add(childRed);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                source.Present();
                source.Present();
                Console.WriteLine($"caret-probe: greenChildAdornerOnBox={CountGreen(presenter.ReadbackRgba())}");
            }

            if (textBoxView is not null && AdornerLayer.GetAdornerLayer(textBoxView) is { } viewLayer2)
            {
                var childView = new ChildAdorner(textBoxView);
                viewLayer2.Add(childView);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                source.Present();
                source.Present();
                Console.WriteLine($"caret-probe: greenChildAdornerOnTextBoxView={CountGreen(presenter.ReadbackRgba())}");
            }

            Console.WriteLine($"caret-probe: nonWhiteOnRow={nonWhite.Count} {string.Join(" ", nonWhite.Take(12))}");
            return nonWhite.Count > 0 ? 0 : 1;
        }
        finally
        {
            window.Close();
        }
    }

    private static FrameworkElement? FindTextBoxView(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child.GetType().Name == "TextBoxView")
            {
                return (FrameworkElement)child;
            }

            if (FindTextBoxView(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private sealed class TestAdorner(UIElement adorned) : Adorner(adorned)
    {
        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 20, 20));
        }
    }

    private sealed class ChildAdorner : Adorner
    {
        private readonly ChildVisual _child = new();

        public ChildAdorner(UIElement adorned)
            : base(adorned)
        {
            AddVisualChild(_child);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index)
        {
            return _child;
        }

        private sealed class ChildVisual : UIElement
        {
            protected override void OnRender(DrawingContext drawingContext)
            {
                drawingContext.DrawRectangle(Brushes.Green, null, new Rect(0, 0, 20, 20));
            }
        }
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return error;
        }
    }

    private static IVulkanPresenter GetWindowPresenter(SdlPresentationSource source)
    {
        object frame = typeof(SdlPresentationSource)
            .GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(source)!;
        return (IVulkanPresenter)frame.GetType().GetProperty("Presenter")!.GetValue(frame)!;
    }

    private static void FireThemeChanged()
    {
        var type = typeof(System.Windows.Application).Assembly.GetType("System.Windows.SystemResources", throwOnError: true)!;
        _ = type.GetMethod("OnThemeChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, null);
    }

    private static void RestoreClassicTheme()
    {
        HostTheme.SetTheme("classic");
        FireThemeChanged();
        var systemParameters = typeof(System.Windows.Application).Assembly.GetType("System.Windows.SystemParameters", throwOnError: true)!;
        _ = systemParameters.GetMethod("InvalidateDerivedThemeRelatedProperties", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
    }

    private static void FlushDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    private static int CountDistinctColors(ReadOnlyMemory<byte> pixels)
    {
        var seen = new HashSet<int>();
        ReadOnlySpan<byte> span = pixels.Span;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            int color = span[i] | (span[i + 1] << 8) | (span[i + 2] << 16) | (span[i + 3] << 24);
            _ = seen.Add(color);
            if (seen.Count > 16)
            {
                return seen.Count;
            }
        }

        return seen.Count;
    }

    private static string FormatPixel(ReadOnlySpan<byte> pixels)
    {
        return pixels.Length < 4
            ? "empty"
            : "#" + pixels[0].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[1].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[2].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[3].ToString("X2", CultureInfo.InvariantCulture);
    }

    private static string FormatSize(double width, double height)
    {
        return width.ToString("F1", CultureInfo.InvariantCulture) + "x" + height.ToString("F1", CultureInfo.InvariantCulture);
    }

    private static string ShortMessage(string? message)
    {
        string single = (message ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ');
        return single.Length <= 110 ? single : single[..110] + "...";
    }

    private static string TopFrame(Exception error)
    {
        string? first = error.StackTrace?.Split('\n').FirstOrDefault(static line => line.Contains(')', StringComparison.Ordinal));
        return (first ?? "no-stack").Trim();
    }

    private static partial class Native
    {
        [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8)]
        [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.SafeDirectories)]
        internal static partial int SetEnv(string name, string value, int overwrite);
    }
}

/// <summary>Binding row for the DataGrid probe.</summary>
internal sealed record ProbeRow(string Name, int Value);

/// <summary>INotifyPropertyChanged view model for the binding/trigger probes.</summary>
internal sealed class ProbeViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public string Name
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
        }
    }
    = string.Empty;

    public bool Flag
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Flag)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Minimal ICommand for the command probe.</summary>
internal sealed class ProbeCommand(Action execute) : ICommand
{
    private readonly Action _execute = execute;

    public bool CanExecute(object? parameter)
    {
        return true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
        }

        remove
        {
        }
    }
}
