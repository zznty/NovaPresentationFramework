using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Sdl;
using Nova.SdlSource;

namespace Nova.Smoke;

/// <summary>
/// Example host: stock <see cref="Window"/> + Button + TextBlock through
/// <see cref="SdlPresentationSource"/>. Linux has no HWND pump —
/// this loop polls SDL and presents. Do not call <c>Dispatcher.Run</c>
/// (<c>PushFrame</c>/<c>GetMessageW</c> still HWND). Set <c>NOVA_SMOKE_TRACE=1</c>
/// to enable opt-in interactive tracing (prefix <c>trace:</c>) of the input path:
/// startup layout facts, per-pass drain counts, mapped events, and mouse state after
/// every mouse event.
/// </summary>
internal static class Program
{
    private static bool s_trace;
    private static int s_clicks;
    private static Window s_window = null!;
    private static Button s_button = null!;

    /// <summary>Shows a Window, pumps SDL once or until close, then exits.</summary>
    /// <returns>0 on success, 1 if the PresentationSource is not SDL.</returns>
    public static int Main()
    {
        s_trace = Environment.GetEnvironmentVariable("NOVA_SMOKE_TRACE") == "1";

        var text = new TextBlock
        {
            Text = "Hi",
            FontFamily = new FontFamily("DejaVu Sans"),
            FontSize = 24,
            Margin = new Thickness(8)
        };
        s_button = new Button
        {
            Content = "Click",
            Width = 80,
            Height = 32,
            Margin = new Thickness(8)
        };
        s_button.Click += (_, _) =>
        {
            s_clicks++;
            text.Text = $"clicks={s_clicks}";
        };

        var menuItemCopy = new MenuItem { Header = "Copy label" };
        menuItemCopy.Click += (_, _) =>
        {
            text.Text = "menu: copy";
            Trace("CONTEXT copy invoked");
        };
        var menuItemReset = new MenuItem { Header = "Reset label" };
        menuItemReset.Click += (_, _) =>
        {
            text.Text = "Hi";
            Trace("CONTEXT reset invoked");
        };
        var contextMenu = new ContextMenu();
        _ = contextMenu.Items.Add(menuItemCopy);
        _ = contextMenu.Items.Add(menuItemReset);
        contextMenu.Opened += (_, _) => Trace("CONTEXT opened");
        contextMenu.Closed += (_, _) => Trace("CONTEXT closed");
        s_button.ContextMenu = contextMenu;

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        _ = panel.Children.Add(s_button);
        _ = panel.Children.Add(text);

        s_window = new Window
        {
            Title = "Nova smoke",
            Width = 320,
            Height = 200,
            Content = panel
        };
        s_window.Show();

        if (PresentationSource.FromVisual(s_window) is not SdlPresentationSource source)
        {
            Console.Error.WriteLine("PresentationSource.FromVisual is not SdlPresentationSource.");
            return 1;
        }

        Console.WriteLine($"visible={s_window.IsVisible} textDesired={text.DesiredSize} source={source.GetType().Name}");

        if (s_trace)
        {
            TraceStartup(source, s_window, s_button, text);
            HookButtonTrace(s_button);
            contextMenu.Opened += (_, _) => Trace($"CONTEXT opened popupSource={PresentationSource.FromVisual(contextMenu)?.GetType().Name} handle={(PresentationSource.FromVisual(contextMenu) as SdlPresentationSource)?.Handle:X}");
            contextMenu.Closed += (_, _) => Trace("CONTEXT closed");
            // Popup source lifecycle: the popup SdlPresentationSource is created by
            // PopupSecurityHelper and disposed when the menu closes. Trace both ends so a
            // "menu opened but nothing rendered" report shows whether the popup source ever
            // existed, what kind of window it got, and whether teardown ran.
            foreach (SdlPresentationSource popup in AllSources())
            {
                if (popup.Owner is not null)
                {
                    HookPopupTrace(popup);
                }
            }

            PresentationSource.AddSourceChangedHandler(contextMenu, OnMenuSourceChanged);
        }

        // Headless CI: present several frames then exit. Interactive: omit NOVA_SMOKE_ONCE.
        // One Present is not enough: swapchain semaphore reuse and destroy-while-in-use
        // only show up on the second+ frame / Close.
        int remaining = Environment.GetEnvironmentVariable("NOVA_SMOKE_ONCE") == "1" ? 4 : -1;
        RunLoop(source, remaining);
        s_window.Close();
        return 0;
    }

    /// <summary>
    /// Poll SDL, dispatch input, drain idle, present.
    /// <paramref name="remaining"/> is the number of Present calls when ≥ 0;
    /// negative means run until close.
    /// </summary>
    /// <param name="source">SDL presentation source created by <see cref="Window.Show"/>.</param>
    /// <param name="remaining">Presents left, or −1 for interactive.</param>
    private static void RunLoop(SdlPresentationSource source, int remaining)
    {
        int pass = 0;
        do
        {
            int consumed = 0;
            try
            {
                while (source.TryPump(out SdlEvent ev))
                {
                    consumed++;
                    Trace($"pass {pass} event Kind={ev.Kind} Button={ev.MouseButton} Pos={ev.Position}");
                    source.Dispatch(ev);
                    if (IsMouseEvent(ev.Kind))
                    {
                        TraceMouseState($"after {ev.Kind}");
                    }
                }

                Trace($"pass {pass} drained {consumed} event(s)");

                if (source.IsClosing || source.IsDisposed)
                {
                    return;
                }

                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                // Present every live frame (main window + popups) once per iteration; a
                // frame-local Present() would never render popup/tooltip windows, whose
                // present callbacks only DuceRuntime can reach.
                SdlPresentationSource.PresentAll();
                TracePresented(pass);
            }
            catch (Exception ex)
            {
                // Never swallow: a swallowed exception here is exactly why this bug class was
                // invisible. Trace the full failure then rethrow so the smoke run reports it.
                Trace($"pass {pass} EXCEPTION {ex.GetType().FullName}: {ex.Message}");
                Trace(ex.StackTrace ?? "(no stack)");
                throw;
            }

            if (remaining > 0)
            {
                remaining--;
            }

            pass++;
        }
        while (remaining != 0);
    }

    /// <summary>
    /// Per pass, how many frames were presented, by which source/handle, and whether each
    /// frame's target root actually resolved (rendered) or was skipped. Uses the same
    /// reflection the framework tests use (the source registry and the slave graph's
    /// target-root table); no product changes.
    /// </summary>
    private static void TracePresented(int pass)
    {
        if (!s_trace)
        {
            return;
        }

        var sources = AllSources();
        if (sources.Count == 0)
        {
            Trace($"pass {pass} presented frames=0 (no live sources)");
            return;
        }

        int presented = 0;
        var details = new List<string>(sources.Count);
        foreach (SdlPresentationSource s in sources)
        {
            if (s.IsDisposed)
            {
                details.Add($"{FormatSource(s)} disposed");
                continue;
            }

            presented++;
            details.Add($"{FormatSource(s)} target=0x{s.FrameTargetHandle:X} root={DescribeRoot(s)}");
        }

        Trace($"pass {pass} presented frames={presented} [ {string.Join(" | ", details)} ]");
    }

    private static string FormatSource(SdlPresentationSource s)
    {
        string kind = s.IsPopupWindow ? "popup" : "main";
        string owner = s.Owner is null ? string.Empty : $" owner=0x{s.Owner.Handle:X}";
        return $"{kind}@{s.Handle:X}{owner}";
    }

    /// <summary>
    /// Whether the frame's target root resolved in the shared slave graph: "rendered" when a
    /// root exists for the frame's target handle, "skipped" when the graph has no root for it
    /// (a skipped frame rasterizes only the clear color — an invisible window).
    /// </summary>
    private static string DescribeRoot(SdlPresentationSource s)
    {
        try
        {
            object frame = typeof(SdlPresentationSource)
                .GetProperty("Frame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(s)!;
            object graph = frame.GetType().GetProperty("Graph")!.GetValue(frame)!;
            uint targetHandle = s.FrameTargetHandle;
            var roots = (System.Collections.IDictionary)graph.GetType()
                .GetField("_targetRoots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(graph)!;
            return roots.Contains(targetHandle)
                ? $"rendered(root=0x{(uint)roots[targetHandle]!.GetType().GetProperty("Value")!.GetValue(roots[targetHandle])!:X})"
                : roots.Count == 1
                    ? "rendered(sole-root fallback)"
                    : "SKIPPED(no root for target)";
        }
        catch (System.Reflection.TargetInvocationException)
        {
            // Reflection surfaced the frame/graph property accessor failure; the frame may be
            // mid-dispose on another thread. Report the root as unknown rather than crashing
            // the diagnostic.
            return "root-unknown";
        }
    }

    /// <summary>
    /// Live SdlPresentationSource registry (main + popups), via the private
    /// <c>s_sourcesByWindow</c> table. The registry is populated at source construction and
    /// drained at Dispose, so it doubles as a popup lifecycle sensor.
    /// </summary>
    private static List<SdlPresentationSource> AllSources()
    {
        System.Reflection.FieldInfo registry = typeof(SdlPresentationSource).GetField(
            "s_sourcesByWindow",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var sources = (System.Collections.IDictionary)registry.GetValue(null)!;
        var result = new List<SdlPresentationSource>(sources.Count);
        foreach (object value in sources.Values)
        {
            if (value is SdlPresentationSource s)
            {
                result.Add(s);
            }
        }

        // The registry key is the window handle; a popup that fell back to a regular window
        // offscreen is indistinguishable from the main window there, so sort by handle so the
        // output is stable across passes.
        result.Sort(static (a, b) => a.Handle.ToInt64().CompareTo(b.Handle.ToInt64()));
        return result;
    }

    /// <summary>
    /// Traces a popup source's lifecycle: creation facts (window kind, owner) and the
    /// disposal that fires when the menu closes. Attached to any owner-parented source.
    /// </summary>
    private static void HookPopupTrace(SdlPresentationSource popup)
    {
        string kind = popup.IsPopupWindow ? "popup-window" : "fallback-window";
        Trace($"POPUP created handle=0x{popup.Handle:X} kind={kind} owner=0x{popup.Owner?.Handle:X} size={popup.PixelWidth}x{popup.PixelHeight}");
        popup.Disposed += (_, _) => Trace($"POPUP disposed handle=0x{popup.Handle:X}");
    }

    /// <summary>
    /// Traces the ContextMenu's presentation source appearing (popup created) and leaves
    /// the per-source lifecycle hook attached so the disposal at menu close is traced too.
    /// Fires for popups created after startup (the startup registry scan only sees sources
    /// that already exist).
    /// </summary>
    private static void OnMenuSourceChanged(object sender, SourceChangedEventArgs e)
    {
        if (e.NewSource is SdlPresentationSource s && !s.IsDisposed)
        {
            Trace($"CONTEXT source handle=0x{s.Handle:X} popup={s.IsPopupWindow} owner=0x{s.Owner?.Handle:X}");
            HookPopupTrace(s);
        }
    }

    private static bool IsMouseEvent(SdlEventKind kind)
    {
        return kind is SdlEventKind.MouseMoved or SdlEventKind.MouseButtonDown or SdlEventKind.MouseButtonUp or SdlEventKind.MouseWheel;
    }

    private static void Trace(string line)
    {
        if (s_trace)
        {
            Console.WriteLine("trace: " + line);
        }
    }

    /// <summary>Raw layout facts at startup: pixel/logical size, effective scale, layout rects.</summary>
    private static void TraceStartup(SdlPresentationSource source, Window window, Button button, TextBlock text)
    {
        window.UpdateLayout();
        double scale = source.CompositionTarget.TransformToDevice.M11;
        Rect buttonRoot = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
        Rect textRoot = text.TransformToAncestor(window).TransformBounds(new Rect(text.RenderSize));
        Rect buttonDevice = TransformRect(source.CompositionTarget.TransformToDevice, buttonRoot);
        Rect textDevice = TransformRect(source.CompositionTarget.TransformToDevice, textRoot);
        Trace($"startup window pixel={source.PixelWidth}x{source.PixelHeight} logical={window.ActualWidth}x{window.ActualHeight} scale={scale} " +
            $"button root={buttonRoot} device={buttonDevice} text root={textRoot} device={textDevice}");
    }

    private static Rect TransformRect(Matrix matrix, Rect rect)
    {
        Point topLeft = matrix.Transform(rect.TopLeft);
        Point bottomRight = matrix.Transform(rect.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    private static void TraceMouseState(string when)
    {
        Trace($"{when}: Captured={Mouse.Captured} DirectlyOver={Mouse.DirectlyOver} pos={Mouse.GetPosition(s_window)} " +
            $"IsPressed={s_button.IsPressed} LeftButton={Mouse.PrimaryDevice.LeftButton} clicks={s_clicks}");
    }

    private static void HookButtonTrace(Button button)
    {
        button.AddHandler(Button.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnButtonMouseButton), true);
        button.AddHandler(Button.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnButtonMouseButton), true);
        button.AddHandler(Button.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnButtonMouseButton), true);
        button.AddHandler(Button.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnButtonMouseButton), true);
        button.AddHandler(UIElement.GotMouseCaptureEvent, new MouseEventHandler(OnButtonMouse), true);
        button.AddHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnButtonMouse), true);
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnButtonClick), true);
    }

    private static void OnButtonMouseButton(object sender, MouseButtonEventArgs e)
    {
        Trace($"EVT {e.RoutedEvent.Name} sender={TypeName(sender)} orig={TypeName(e.OriginalSource)} handled={e.Handled} " +
            $"btn={e.ChangedButton} state={e.ButtonState} pos={Mouse.GetPosition(s_window)}");
    }

    private static void OnButtonMouse(object sender, MouseEventArgs e)
    {
        Trace($"EVT {e.RoutedEvent.Name} sender={TypeName(sender)} orig={TypeName(e.OriginalSource)} handled={e.Handled} pos={Mouse.GetPosition(s_window)}");
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        Trace($"EVT {e.RoutedEvent.Name} sender={TypeName(sender)} orig={TypeName(e.OriginalSource)} handled={e.Handled} clicks={s_clicks}");
    }

    private static string TypeName(object? value)
    {
        return value?.GetType().Name ?? "null";
    }
}
