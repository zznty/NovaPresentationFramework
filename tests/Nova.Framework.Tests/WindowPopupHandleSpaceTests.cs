using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nova.Geometry;
using Nova.Mil;
using Nova.Sdl;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

/// <summary>
/// Fourth source file of <see cref="WindowTextBlockTests"/> (same partial class keeps
/// xunit's per-class collection serialized). Mechanism-level regression probes for the
/// Wayland bug where right-clicking a Button with a ContextMenu blacked out the entire
/// main window. The root cause (fixed in-tree): <c>DuceExports</c> allocated resource
/// handles PER CHANNEL, but all channels of a MediaContext feed ONE shared
/// <see cref="Nova.Mil.SlaveGraph"/> keyed by handle VALUE. A second target's out-of-band
/// content root therefore reused the value already owned by the first target's content
/// tree (observed: both main and popup roots carried value 2), and releasing the popup's
/// out-of-band root deleted the LIVE resource of the main window — the next main-window
/// frame rasterized nothing and rendered the flat clear color. With the process-wide
/// handle allocator, every resource (including each content root) gets a unique value,
/// so popup teardown can never delete another window's tree.
///
/// These tests assert the mechanism directly (the popup's root handle value must not be
/// the main window's, and the main window's root resource must survive popup teardown)
/// plus the observable behavior (main window readback keeps its content, and a
/// reopened menu still renders through the app-shaped loop).
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void PopupClose_DoesNotDeleteMainContentRoot()
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
            RunLoopPassAll(mainSource);

            uint mainTarget = PopupTargetHandle(mainSource);
            uint mainRoot = PopupRootValue(mainSource, mainTarget);
            Assert.NotEqual(0u, mainRoot);
            Assert.True(PopupGraphResources().Contains(mainRoot), "main window's content root should exist before the popup opens");

            menu.IsOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(menu.IsOpen);
            var popupSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(menuItemCopy));
            RunLoopPassAll(mainSource);

            uint popupTarget = PopupTargetHandle(popupSource);
            uint popupRoot = PopupRootValue(popupSource, popupTarget);
            Assert.NotEqual(mainTarget, popupTarget);
            // The pre-fix per-channel allocator handed the popup's out-of-band content root
            // the same VALUE as the main window's root (both "2" in their own handle
            // spaces); the shared graph could not tell them apart.
            Assert.NotEqual(mainRoot, popupRoot);
            Assert.True(PopupGraphResources().Contains(mainRoot), "main content root alive while popup open");
            Assert.True(PopupGraphResources().Contains(popupRoot), "popup content root alive while popup open");

            // Close the menu through the live input path, then drain: popup teardown
            // releases the popup's out-of-band content root.
            CloseMenuViaInput(menu, menuItemCopy);
            RunLoopPassAll(mainSource);

            // Pre-fix, that release deleted main's root resource (same value), and the main
            // window went black. With unique handle values it must survive.
            Assert.True(PopupGraphResources().Contains(mainRoot),
                "closing the popup deleted the main window's content root — the shared " +
                "graph could not distinguish the popup's out-of-band root from the main " +
                "window's root (per-channel handle allocation)");
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
    public void Popup_ReopenCycle_MainAndMenuKeepRendering()
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
            RunLoopPassAll(mainSource);
            mainSource.EnableReadback();
            RunLoopPassAll(mainSource);
            RunLoopPassAll(mainSource);
            ReadOnlyMemory<byte> before = mainSource.ReadbackRgba();
            AssertWindowContent(before, "before the first menu open");

            for (int cycle = 1; cycle <= 2; cycle++)
            {
                menu.IsOpen = true;
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(menu.IsOpen, $"menu should open on cycle {cycle}");
                var popupSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(menuItemCopy));

                // The app loop knows only the main source; the popup must still render.
                popupSource.EnableReadback();
                for (int i = 0; i < 3; i++)
                {
                    RunLoopPassAll(mainSource);
                }

                ReadOnlyMemory<byte> popupPixels = popupSource.ReadbackRgba();
                Assert.True(CountDistinctColors(popupPixels.Span) >= 2,
                    $"reopened menu (cycle {cycle}) shows only {CountDistinctColors(popupPixels.Span)} " +
                    "distinct color(s) — the popup frame was not presented by the app loop");

                ReadOnlyMemory<byte> duringOpen = mainSource.ReadbackRgba();
                AssertWindowContent(duringOpen, $"while the menu is open (cycle {cycle})");
                AssertEquivalentFrames(before, duringOpen);

                CloseMenuViaInput(menu, menuItemCopy);
                RunLoopPassAll(mainSource);

                ReadOnlyMemory<byte> afterClose = mainSource.ReadbackRgba();
                AssertWindowContent(afterClose, $"after the menu closed (cycle {cycle})");
                AssertEquivalentFrames(before, afterClose);
            }
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

    /// <summary>The composition frame's target resource handle (GENERICRENDERTARGET) for a source.</summary>
    private static uint PopupTargetHandle(SdlPresentationSource source)
    {
        object frame = typeof(SdlPresentationSource)
            .GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(source)!;
        return (uint)frame.GetType().GetProperty("TargetHandle")!.GetValue(frame)!;
    }

    /// <summary>
    /// The root resource the graph associates with <paramref name="targetHandle"/> (the
    /// value of the <c>TargetSetRoot</c> command, i.e. the content-root visual handle).
    /// </summary>
    private static uint PopupRootValue(SdlPresentationSource source, uint targetHandle)
    {
        object graph = MainFrameGraph(source);
        var roots = (System.Collections.IDictionary)graph
            .GetType()
            .GetField("_targetRoots", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(graph)!;
        return roots.Contains(targetHandle)
            ? ((ResourceHandle)roots[targetHandle]!).Value
            : 0u;
    }

    /// <summary>The shared graph's resource table (keyed by handle value).</summary>
    private static System.Collections.IDictionary PopupGraphResources()
    {
        // The graph is shared per MediaContext; the main source's graph is the shared one.
        object frame = typeof(SdlPresentationSource)
            .GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(PresentationSource.CurrentSources.OfType<SdlPresentationSource>().First())!;
        object graph = frame.GetType().GetProperty("Graph")!.GetValue(frame)!;
        return (System.Collections.IDictionary)graph
            .GetType()
            .GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(graph)!;
    }

    /// <summary>
    /// One pass of the app-shaped loop that presents EVERY live frame once: drain the
    /// source, yield ApplicationIdle, then <c>SdlPresentationSource.PresentAll()</c>.
    /// Distinct from the frame-local <c>Present()</c> contract so popup windows registered
    /// later on the shared channel set still render.
    /// </summary>
    private static void RunLoopPassAll(SdlPresentationSource source)
    {
        while (source.TryPump(out SdlEvent ev))
        {
            source.Dispatch(ev);
        }

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        SdlPresentationSource.PresentAll();
    }

    [Fact]
    public void TwoTopLevelWindows_AppLoop_PresentsEachFrameOnce_RendersDistinctContent()
    {
        var windowA = new Window
        {
            Width = 200,
            Height = 120,
            Content = new TextBlock { Text = "WINDOW A", FontSize = 24, Margin = new Thickness(12) }
        };
        var windowB = new Window
        {
            Width = 200,
            Height = 120,
            Content = new TextBlock { Text = "WINDOW B", FontSize = 24, Margin = new Thickness(12) }
        };
        windowA.Show();
        windowB.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            var sourceA = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(windowA));
            var sourceB = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(windowB));
            Assert.NotEqual(sourceA.Handle, sourceB.Handle);
            Assert.Equal(2, CountBindings()); // two top-level windows, two frames

            // A counting fake binding: every present-all pass invokes EACH attached frame's
            // present callback exactly once. With one PresentAll per iteration, three
            // iterations must produce exactly three invocations.
            int presentPasses = 0;
            int fakeBinding = DuceRuntime.Attach((SlaveGraph)MainFrameGraph(sourceA), () => presentPasses++);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    RunTwoWindowLoopPass(sourceA, sourceB);
                }

                Assert.Equal(3, presentPasses);
            }
            finally
            {
                DuceRuntime.Detach(fakeBinding);
            }

            // The frame-local Present() must NOT invoke other frames' present callbacks: a
            // multi-window loop presenting each source in turn renders every frame exactly
            // once. Attach a counting binding; frame-local Present must not touch it, and a
            // present-all pass must invoke it exactly once.
            int frameLocalPasses = 0;
            int frameLocalBinding = DuceRuntime.Attach((SlaveGraph)MainFrameGraph(sourceA), () => frameLocalPasses++);
            try
            {
                sourceA.Present(); // frame-local: only window A's own frame renders
                Assert.Equal(0, frameLocalPasses);
                SdlPresentationSource.PresentAll();
                Assert.Equal(1, frameLocalPasses);
            }
            finally
            {
                DuceRuntime.Detach(frameLocalBinding);
            }

            // Both windows render their OWN distinct content through the app loop.
            sourceA.EnableReadback();
            sourceB.EnableReadback();
            RunTwoWindowLoopPass(sourceA, sourceB);
            RunTwoWindowLoopPass(sourceA, sourceB);
            ReadOnlyMemory<byte> pixelsA = sourceA.ReadbackRgba();
            ReadOnlyMemory<byte> pixelsB = sourceB.ReadbackRgba();
            Assert.True(CountDistinctColors(pixelsA.Span) >= 2, "window A must render its own content");
            Assert.True(CountDistinctColors(pixelsB.Span) >= 2, "window B must render its own content");
            bool distinct = false;
            for (int i = 0; i + 3 < pixelsA.Length && !distinct; i += 4)
            {
                distinct = pixelsA.Span[i] != pixelsB.Span[i]
                    || pixelsA.Span[i + 1] != pixelsB.Span[i + 1]
                    || pixelsA.Span[i + 2] != pixelsB.Span[i + 2];
            }

            Assert.True(distinct, "the two windows' frames must differ (each renders its own tree)");
        }
        finally
        {
            windowA.Close();
            windowB.Close();
        }
    }

    /// <summary>Pumps both sources, yields idle, then presents every frame once.</summary>
    private static void RunTwoWindowLoopPass(SdlPresentationSource sourceA, SdlPresentationSource sourceB)
    {
        while (sourceA.TryPump(out SdlEvent ev))
        {
            sourceA.Dispatch(ev);
        }

        while (sourceB.TryPump(out SdlEvent ev))
        {
            sourceB.Dispatch(ev);
        }

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        SdlPresentationSource.PresentAll();
    }
}
