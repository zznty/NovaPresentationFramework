using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nova.Mil;
using Nova.Sdl;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

/// <summary>
/// Third source file of <see cref="WindowTextBlockTests"/> (same partial class keeps
/// xunit's per-class collection serialized — concurrent SDL window creation races the
/// offscreen driver). Regression tests for the shipped Wayland bug where right-clicking a
/// Button with a ContextMenu blacks out the ENTIRE main window and no context menu is ever
/// visible: the trace shows the ContextMenu opening, a popup <see cref="SdlPresentationSource"/>
/// created (capture moves to the ContextMenu), and a <c>vkDestroySurfaceKHR</c> resolved at
/// exactly that moment.
///
/// The pre-existing ContextMenu test PASSES despite this bug because it (a) explicitly calls
/// <c>popupSource.Present()</c> and (b) never re-checks that the MAIN window still renders
/// after the popup opens. These tests close exactly that blind spot: they drive the REAL
/// app-loop shape from <c>samples/Nova.Smoke/Program.cs</c> (drain SDL into the main source,
/// yield ApplicationIdle, present ONLY the main source) and assert (1) the main window keeps
/// rendering its content across the popup's whole lifetime — opening AND closing the menu —
/// (2) the popup's own frame gets presented by that same loop, and (3) <see cref="DuceRuntime"/>'s
/// shared per-MediaContext channel→graph mapping and the main frame's binding survive popup
/// creation and teardown.
///
/// Both root causes were confirmed against the shipped code and are fixed in-tree:
///  - DuceExports allocated resource handles PER CHANNEL; a second target's out-of-band
///    content root could take a value already owned by the first target's tree, and
///    releasing it deleted a live resource of the other window — the black main window
///    behind a popup (offscreen repro: after the popup closed, the main frame's readback
///    collapsed from 162 distinct colors to a flat 00,00,00,00 clear).
///  - SdlPresentationSource.Present rasterized ONLY the source's own frame, so a popup
///    registered later on the shared channel set was never presented by the app loop
///    (offscreen repro: the popup's readback stayed a flat 00,00,00,00).
///
/// Both tests were RED against the shipped code and are GREEN against the fix
/// (process-wide handle allocation + DuceRuntime.Present rendering every attached frame).
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void MainWindow_KeepsRendering_AfterPopupOpens()
    {
        var button = new Button { Content = "Right-click me", Width = 120, Height = 32 };
        var menuItemCopy = new MenuItem { Header = "Copy label" };
        var menuItemReset = new MenuItem { Header = "Reset label" };
        var menu = new ContextMenu();
        _ = menu.Items.Add(menuItemCopy);
        _ = menu.Items.Add(menuItemReset);
        button.ContextMenu = menu;
        // The right-click gesture sets PlacementTarget to the element; IsOpen alone does not.
        menu.PlacementTarget = button;

        var window = new Window { Width = 320, Height = 200, Content = button };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            var mainSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));

            // Render the main window through the REAL app loop (smoke RunLoop: drain SDL,
            // yield idle, present ONLY the main source) and capture its pixels BEFORE the
            // menu exists.
            RunAppLoopPass(mainSource);
            mainSource.EnableReadback();
            RunAppLoopPass(mainSource);
            RunAppLoopPass(mainSource);
            ReadOnlyMemory<byte> before = mainSource.ReadbackRgba();
            AssertWindowContent(before, "before opening the context menu");

            // Open the ContextMenu the way WPF does on right-click: IsOpen drives
            // Popup.CreateWindow → PopupSecurityHelper.BuildWindow → new SdlPresentationSource.
            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(menu.IsOpen);
            Assert.True(menuItemCopy.IsVisible, "menu item should be realized once the popup is open");

            // Drive the app loop again — the popup is a SECOND source the loop knows nothing
            // about; only the main source is presented here, exactly like the smoke app.
            RunAppLoopPass(mainSource);
            RunAppLoopPass(mainSource);
            ReadOnlyMemory<byte> afterOpen = mainSource.ReadbackRgba();

            // The main window must STILL render its own content while the popup is open:
            // same non-black pixels as before, not a flat clear color.
            AssertWindowContent(afterOpen, "while the context menu is open");
            AssertEquivalentFrames(before, afterOpen);

            // Close the menu the way WPF does on a real click (mouse-up on a menu item
            // releases the popup capture and disposes the popup source), then drive the
            // app loop again. The REAL defect is here: popup teardown blacks the entire
            // main window — the next main-window frame is a flat transparent/black clear
            // (observed 00,00,00,00, 1 distinct color) instead of the window's content.
            CloseMenuViaInput(menu, menuItemCopy);
            RunAppLoopPass(mainSource);
            ReadOnlyMemory<byte> afterClose = mainSource.ReadbackRgba();
            AssertWindowContent(afterClose, "after the context menu closed");
            AssertEquivalentFrames(before, afterClose);
        }
        finally
        {
            if (menu.IsOpen)
            {
                CloseMenuViaInput(menu, menuItemCopy);
            }

            window.Close();
        }
    }

    [Fact]
    public void Popup_IsPresentedByAppLoop_ShowsMenuPixels()
    {
        var button = new Button { Content = "Right-click me", Width = 120, Height = 32 };
        var menuItemCopy = new MenuItem { Header = "Copy label" };
        var menuItemReset = new MenuItem { Header = "Reset label" };
        var menu = new ContextMenu();
        _ = menu.Items.Add(menuItemCopy);
        _ = menu.Items.Add(menuItemReset);
        button.ContextMenu = menu;
        menu.PlacementTarget = button;

        var window = new Window { Width = 320, Height = 200, Content = button };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            var mainSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            RunAppLoopPass(mainSource);

            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(menu.IsOpen);

            SdlPresentationSource popupSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(menuItemCopy));
            Assert.True(ReferenceEquals(popupSource.Owner, mainSource), "popup must be owner-parented to the main window's source");

            // Opt the popup into readback, then drive ONLY the app-shaped loop. The loop has
            // a single source variable and never calls popupSource.Present() — presenting the
            // popup is the loop's job, and today it does not happen (the menu is invisible).
            popupSource.EnableReadback();
            for (int i = 0; i < 4; i++)
            {
                RunAppLoopPass(mainSource);
            }

            ReadOnlyMemory<byte> loopPixels = popupSource.ReadbackRgba();

            // The popup's own frame must show the menu (theme background + item text), i.e.
            // more than the flat transparent/black clear color.
            int distinct = CountDistinctColors(loopPixels.Span);
            Assert.True(distinct >= 2,
                $"popup frame shows only {distinct} distinct color(s) (first pixel " +
                $"{loopPixels.Span[0]:X2},{loopPixels.Span[1]:X2},{loopPixels.Span[2]:X2},{loopPixels.Span[3]:X2}) — " +
                "the app loop never presented the popup, so the context menu is invisible");
        }
        finally
        {
            if (menu.IsOpen)
            {
                CloseMenuViaInput(menu, menuItemCopy);
            }

            window.Close();
        }
    }

    [Fact]
    public void PopupOpen_MainChannelMappingAndBinding_RemainIntact()
    {
        var button = new Button { Content = "Right-click me", Width = 120, Height = 32 };
        var menuItemCopy = new MenuItem { Header = "Copy label" };
        var menuItemReset = new MenuItem { Header = "Reset label" };
        var menu = new ContextMenu();
        _ = menu.Items.Add(menuItemCopy);
        _ = menu.Items.Add(menuItemReset);
        button.ContextMenu = menu;
        menu.PlacementTarget = button;

        var window = new Window { Width = 320, Height = 200, Content = button };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            var mainSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));

            // Capture the main window's identity BEFORE the popup exists: its channel handles
            // (main + out-of-band, shared per MediaContext) and its binding's slave graph.
            object mainGraph = MainFrameGraph(mainSource);
            nint[] mainChannels = ChannelMappingKeys();
            Assert.True(mainChannels.Length >= 1, "main window should register its channel mappings before the popup opens");
            Assert.True(BindingsContain(mainGraph), "main frame binding should be attached before the popup opens");

            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(menu.IsOpen);
            _ = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(menuItemCopy));

            // The popup attaches its own binding on the SHARED per-MediaContext channel set.
            Assert.True(CountBindings() >= 2,
                $"popup opened but only {CountBindings()} binding(s) are attached — the popup frame never registered");

            // The MAIN window's channel→graph mapping must still resolve to the main graph.
            // Commits on the main channel route through DuceRuntime.GraphFor, which has NO
            // sole-binding fallback while two frames are alive — an unregistered channel
            // silently drops every subsequent main-window commit (black window).
            foreach (nint channel in mainChannels)
            {
                object? mapped = ChannelGraph(channel);
                Assert.True(ReferenceEquals(mapped, mainGraph),
                    $"channel 0x{channel:X} maps to {(mapped is null ? "null" : mapped.GetType().Name)} after the popup opened — " +
                    "the shared per-MediaContext channel was unregistered; every subsequent main-window commit is dropped");
                object? resolved = GraphForChannel(channel);
                Assert.True(ReferenceEquals(resolved, mainGraph),
                    $"DuceRuntime.GraphFor(0x{channel:X}) returned {(resolved is null ? "null" : "a different graph")} — " +
                    "the main window's commits no longer reach the slave graph");
            }

            Assert.True(BindingsContain(mainGraph), "the main window's binding was detached when the popup opened");

            // Teardown must likewise leave the SHARED per-MediaContext channel registered:
            // closing the menu disposes the popup source (its binding detaches), but the
            // main window's binding and channel→graph mapping must survive so subsequent
            // main-window commits still reach the slave graph.
            CloseMenuViaInput(menu, menuItemCopy);
            foreach (nint channel in mainChannels)
            {
                object? mappedAfterClose = ChannelGraph(channel);
                Assert.True(ReferenceEquals(mappedAfterClose, mainGraph),
                    $"channel 0x{channel:X} no longer maps to the main window's graph after the popup closed — " +
                    "popup teardown unregistered the shared per-MediaContext channel");
            }

            Assert.True(BindingsContain(mainGraph), "the main window's binding was detached when the popup closed");
        }
        finally
        {
            if (menu.IsOpen)
            {
                CloseMenuViaInput(menu, menuItemCopy);
            }

            window.Close();
        }
    }

    /// <summary>
    /// One pass of the REAL app loop from <c>samples/Nova.Smoke/Program.cs</c> RunLoop:
    /// drain SDL events into the source, yield at ApplicationIdle, then present. The
    /// present step calls the static <see cref="SdlPresentationSource.PresentAll"/> (the
    /// app-loop contract) so every frame on the shared channel set — including popups
    /// registered after the loop started — renders exactly once per iteration. A loop that
    /// presents only the main source's own frame leaves popup windows unrendered.
    /// </summary>
    private static void RunAppLoopPass(SdlPresentationSource source)
    {
        while (source.TryPump(out SdlEvent ev))
        {
            source.Dispatch(ev);
        }

        if (source.IsClosing || source.IsDisposed)
        {
            return;
        }

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        SdlPresentationSource.PresentAll();
    }

    /// <summary>
    /// Closes the ContextMenu the way the app's input path does — a real mouse Down+Up on a
    /// menu item, dispatched through the LIVE popup source. Closing programmatically
    /// (<c>IsOpen = false</c>) skips the mouse-up capture release WPF performs on the real
    /// path: the popup source is disposed immediately, and its disposed
    /// <c>IMouseInputProvider.ReleaseMouseCapture</c> is a no-op, leaving
    /// <c>Mouse.Captured</c> stuck on the menu — which hijacks the input of every later
    /// test in this collection (the poll tests click real buttons through the SDL queue).
    /// </summary>
    private static void CloseMenuViaInput(ContextMenu menu, MenuItem item)
    {
        if (!menu.IsOpen)
        {
            return;
        }

        if (PresentationSource.FromVisual(item) is not SdlPresentationSource popupSource)
        {
            menu.IsOpen = false;
            return;
        }

        System.Windows.Point itemCenter = item.TranslatePoint(
            new System.Windows.Point(item.ActualWidth / 2, item.ActualHeight / 2),
            (UIElement)popupSource.RootVisual);
        var popupHandle = new WindowHandle(popupSource.Handle);
        var itemPoint = new Nova.Geometry.Point(itemCenter.X, itemCenter.Y);
        popupSource.Dispatch(new SdlEvent(
            SdlEventKind.MouseButtonDown,
            popupHandle,
            itemPoint,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Left,
            0,
            null));
        popupSource.Dispatch(new SdlEvent(
            SdlEventKind.MouseButtonUp,
            popupHandle,
            itemPoint,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Left,
            0,
            null));
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        if (menu.IsOpen)
        {
            // The click did not dismiss the menu (abnormal state); close it anyway. The
            // click's CancelCapture release already ran while the popup was alive.
            menu.IsOpen = false;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// A rendered main-window frame must show real content: more than one distinct color
    /// (window background + button chrome + text) and not a black/transparent clear.
    /// </summary>
    private static void AssertWindowContent(ReadOnlyMemory<byte> pixels, string when)
    {
        ReadOnlySpan<byte> p = pixels.Span;
        Assert.False(p.IsEmpty, $"main window readback is empty {when}");

        int distinct = CountDistinctColors(p);
        Assert.True(distinct >= 2,
            $"main window frame {when} has only {distinct} distinct color(s) (first pixel " +
            $"{p[0]:X2},{p[1]:X2},{p[2]:X2},{p[3]:X2}) — the window stopped rendering its content");

        int pixelCount = p.Length / 4;
        int bright = 0;
        for (int i = 0; i < p.Length; i += 4)
        {
            if (p[i] > 0x20 || p[i + 1] > 0x20 || p[i + 2] > 0x20)
            {
                bright++;
            }
        }

        // The window background + button chrome are light (Classic theme); at least a
        // quarter of the frame must carry them. A frame that lost its content is
        // uniformly the (transparent/black) clear color.
        Assert.True(bright >= pixelCount / 4,
            $"main window frame {when} has only {bright} of {pixelCount} non-black pixels — " +
            "the window is black/transparent after the popup opened");
    }

    /// <summary>
    /// Opening a popup must not change the main window's own frame: it is a separate
    /// surface, so the pre-open and post-open readbacks must be effectively identical.
    /// </summary>
    private static void AssertEquivalentFrames(ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after)
    {
        Assert.Equal(before.Length, after.Length);
        ReadOnlySpan<byte> a = before.Span;
        ReadOnlySpan<byte> b = after.Span;
        int differing = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3])
            {
                differing++;
            }
        }

        // Tolerate at most 5% of pixels differing (antialiasing jitter); the bug replaces
        // the whole frame with the flat clear color (≈100%).
        Assert.True(differing * 100 <= a.Length / 4 * 5,
            $"{differing * 4} of {a.Length} bytes differ between the pre-popup and post-popup " +
            "main frames — the main window's content was replaced (black/clear) after the popup opened");
    }

    private static int CountDistinctColors(ReadOnlySpan<byte> p)
    {
        var seen = new HashSet<int>();
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            int key = (p[i] << 24) | (p[i + 1] << 16) | (p[i + 2] << 8) | p[i + 3];
            _ = seen.Add(key);
        }

        return seen.Count;
    }

    /// <summary>The slave graph of the main window's composition frame (via the internal Frame property).</summary>
    private static object MainFrameGraph(SdlPresentationSource source)
    {
        object frame = typeof(SdlPresentationSource)
            .GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(source)!;
        return frame.GetType().GetProperty("Graph")!.GetValue(frame)!;
    }

    private static nint[] ChannelMappingKeys()
    {
        var field = typeof(DuceRuntime).GetField("s_graphsByChannel", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mappings = (System.Collections.IDictionary)field.GetValue(null)!;
        var keys = new nint[mappings.Count];
        int i = 0;
        foreach (object key in mappings.Keys)
        {
            keys[i++] = (nint)key;
        }

        return keys;
    }

    private static object? ChannelGraph(nint channel)
    {
        var field = typeof(DuceRuntime).GetField("s_graphsByChannel", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mappings = (System.Collections.IDictionary)field.GetValue(null)!;
        return mappings.Contains(channel) ? mappings[channel] : null;
    }

    /// <summary>DuceRuntime.GraphFor — the resolver WPF commits route through.</summary>
    private static object? GraphForChannel(nint channel)
    {
        MethodInfo method = typeof(DuceRuntime).GetMethod("GraphFor", BindingFlags.Static | BindingFlags.NonPublic)!;
        return method.Invoke(null, [channel]);
    }

    /// <summary>True when a binding whose slave graph is <paramref name="graph"/> is still attached.</summary>
    private static bool BindingsContain(object graph)
    {
        var field = typeof(DuceRuntime).GetField("s_bindings", BindingFlags.Static | BindingFlags.NonPublic)!;
        var bindings = (System.Collections.IEnumerable)field.GetValue(null)!;
        foreach (object binding in bindings)
        {
            PropertyInfo graphProperty = binding.GetType().GetProperty("Graph", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            if (ReferenceEquals(graphProperty.GetValue(binding), graph))
            {
                return true;
            }
        }

        return false;
    }
}
