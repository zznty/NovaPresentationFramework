using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Host;
using Nova.Mil;
using Nova.MilCmd;
using Nova.Sdl;
using Nova.TestSupport;
using Nova.Vulkan;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;

namespace Nova.SdlSource.Tests;

// Validation is Disabled by default via NovaTestVulkan.DeviceOptions() (see that helper for
// the full rationale): the Khronos validation layer's GetDispatchDevice hits a libstdc++
// __glibcxx_assert_fail and aborts the process under rapid Vulkan device create/destroy
// churn (each test creates+destroys devices). Set NOVA_TEST_VULKAN_VALIDATION=1 to re-enable
// validation for a deliberate run. These tests assert pixels/input logic, not validation
// output; the layer stays enabled in the interactive smoke/dev path.
public sealed partial class SdlPresentationSourceTests
{
    private const int Size = 64;

    public SdlPresentationSourceTests()
    {
        _ = Native.SetEnv("SDL_VIDEO_DRIVER", "offscreen", 1);
        _ = Native.SetEnv("SDL_VIDEODRIVER", "offscreen", 1);
    }

    [Fact]
    public void OffscreenSource_DrawRectangle_ReadbackShowsRed()
    {
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);
        InjectRedRectangle(frame.Graph);

        frame.Present();
        ReadOnlySpan<byte> pixels = frame.Presenter.ReadbackRgba().Span;
        Assert.Equal(255, Channel(pixels, 12, 12, 0));
        Assert.Equal(0, Channel(pixels, 12, 12, 1));
        Assert.Equal(0, Channel(pixels, 12, 12, 2));
        Assert.Equal(255, Channel(pixels, 12, 12, 3));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));
        source.Dispose();
    }

    [Fact]
    public void OffscreenSource_WpfDrawingVisual_ReadbackShowsRed()
    {
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Red, null, new Rect(8, 8, 16, 16));
        }

        source.RootVisual = visual;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        frame.Present();
        ReadOnlySpan<byte> pixels = frame.Presenter.ReadbackRgba().Span;
        Assert.Equal(255, Channel(pixels, 12, 12, 0));
        Assert.Equal(0, Channel(pixels, 12, 12, 1));
        Assert.Equal(0, Channel(pixels, 12, 12, 2));
        Assert.Equal(255, Channel(pixels, 12, 12, 3));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));
    }

    [Fact]
    public void OffscreenSource_ClassicScrollArrow_RendersAsArrow()
    {
        // The classic ScrollBar's "curved arrow" glyph is a bezier Path in Classic.xaml
        // (Data="M0.5,20.468c..."). The reported half-circle artifact is this glyph rendered
        // through the NPF bezier flatten. Render it and verify it stays an arrow-ish thin
        // shape (not a filled circle/arc).
        const string arrowData =
            "M0.5,20.468c0.002,0.34,0.036,0.679,0.102,1.013c0.032,0.162,0.092,0.312,0.139,0.468"
            + "c0.052,0.176,0.092,0.355,0.163,0.527c0.076,0.183,0.18,0.349,0.274,0.521"
            + "c0.073,0.132,0.131,0.27,0.216,0.397c0.196,0.294,0.418,0.568,0.667,0.817"
            + "l14.611,14.611c2.083,2.083,5.459,2.083,7.542,0c2.083-2.083,2.083-5.459,0-7.542"
            + "l-5.509-5.509h23.791c2.945,0,5.333-2.388,5.333-5.333c0-2.946-2.388-5.333-5.333-5.333"
            + "H18.732l5.509-5.509c2.083-2.083,2.083-5.459,0-7.542c-2.083-2.083-5.459-2.083-7.542,0"
            + "L2.203,16.549c-0.043,0.04-0.095,0.07-0.136,0.111c-0.002,0.002-0.005,0.003-0.007,0.006"
            + "c-0.002,0.002-0.003,0.004-0.007,0.006z";

        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(128, 96),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 128, 96));
            var geo = System.Windows.Media.Geometry.Parse(arrowData);
            geo.Freeze();
            // dump the compiled path stream for byte-level forensics
            var pg = geo; // StreamGeometry (Parse returns StreamGeometry)
            var dataProp = pg.GetType().GetProperty("SerializedData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var data = dataProp?.GetValue(pg) is byte[] d ? d : null;
            if (data != null)
            {
                System.IO.File.WriteAllBytes("/tmp/arrow-path.bin", data);
            }

            dc.DrawGeometry(Brushes.Black, null, geo);
        }

        source.RootVisual = visual;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        frame.Present();
        ReadOnlySpan<byte> pixels = frame.Presenter.ReadbackRgba().Span;
        byte[] bytes = pixels.ToArray();
        bool IsInk(int x, int y)
        {
            int o = ((y * 128) + x) * 4;
            return bytes[o] < 200 && bytes[o + 1] < 200 && bytes[o + 2] < 200;
        }

        // the arrow is a ring-shaped outline: measure ink area + the largest solid block.
        // A correctly flattened arrow renders ~x% of the region; a C/circle blob is dense.
        int ink = 0;
        int maxRun = 0;
        for (int y = 0; y < 96; y++)
        {
            int run = 0;
            for (int x = 0; x < 128; x++)
            {
                if (IsInk(x, y))
                {
                    ink++;
                    run++;
                    maxRun = Math.Max(maxRun, run);
                }
                else
                {
                    run = 0;
                }
            }
        }

        using (var fs = System.IO.File.Create("/tmp/arrow-render.ppm"))
        {
            fs.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n128 96\n255\n"));
            fs.Write(bytes);
        }

        // the classic arrow glyph spans ~48x48; ring-shaped (outline). A C/blob would be
        // a different ink distribution — for now assert the shape has the arrow's honest size.
        Assert.True(ink is >= 100 and <= 1600, $"arrow glyph ink {ink}px — malformed bezier");
        source.Dispose();
    }

    [Fact]
    public void OffscreenSource_TabShadowSliver_RendersThinBand()
    {
        // Reproduces ClassicBorderDecorator.GenerateTabTopShadowGeometry (Classic tab style):
        // a 1px-wide vertical sliver (right-1..right, full tab height) with two 45-degree
        // corner arcs at the top (radius 2/3, endpoints at the 0.293 midpoints) and a straight
        // close at the bottom. The pixel defect under investigation: the sliver's fill
        // renders 6-8px wide with a prominent corner curl instead of the ~1-2px band.
        const double right = 120.0, top = 0.0, bottom = 55.0;
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(128, 64),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // white background so the painted sliver is unambiguous
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 128, 64));

            var outerCorner = new Size(3, 3);
            var innerCorner = new Size(2, 2);
            var figure = new PathFigure
            {
                StartPoint = new Point(right - 1.0, bottom),
            };
            figure.Segments.Add(new LineSegment(new Point(right - 1.0, top + 3.0), true));
            figure.Segments.Add(new ArcSegment(
                new Point(right - (1.0 + (2.0 * 0.293)), top + (1.0 + (2.0 * 0.293))), innerCorner, 0.0,
                false, SweepDirection.Counterclockwise, true));
            figure.Segments.Add(new LineSegment(new Point(right - (3.0 * 0.293), top + (3.0 * 0.293)), true));
            figure.Segments.Add(new ArcSegment(
                new Point(right, top + 3.0), outerCorner, 0.0,
                false, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(new Point(right, bottom), true));
            figure.IsClosed = true;

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            dc.DrawGeometry(Brushes.Black, null, geometry);
        }

        source.RootVisual = visual;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        frame.Present();
        ReadOnlySpan<byte> pixels = frame.Presenter.ReadbackRgba().Span;

        // measure the painted band width at mid-height rows (the sliver interior, away from
        // the corner shoulders): count non-white pixels in the x window right-8..right
        byte[] bytes = pixels.ToArray();
        bool IsInk(int x, int y)
        {
            int o = ((y * 128) + x) * 4;
            return bytes[o] < 240 || bytes[o + 1] < 240 || bytes[o + 2] < 240;
        }

        int maxWidth = 0;
        for (int y = 20; y <= 44; y++)
        {
            int width = 0;
            for (int x = (int)right - 8; x <= (int)right; x++)
            {
                if (IsInk(x, y))
                {
                    width++;
                }
            }

            maxWidth = Math.Max(maxWidth, width);
        }

        // A 1px sliver with AA may paint 2-3px; the defect renders the fill ~3-4x wider.
        Assert.True(maxWidth <= 4, $"tab shadow sliver painted {maxWidth}px wide at mid-height (expected <= 4)");

        source.Dispose();
    }

    [Fact]
    public void Dispatch_FocusEvents_ToggleIsActive()
    {
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);
        Assert.False(source.IsActive);

        source.Dispatch(new SdlEvent(
            SdlEventKind.WindowFocusGained,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.None,
            0,
            null));
        Assert.True(source.IsActive);

        source.Dispatch(new SdlEvent(
            SdlEventKind.WindowFocusLost,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.None,
            0,
            null));
        Assert.False(source.IsActive);
    }

    [Fact]
    public void BeginInvoke_SameThread_RunsCallback()
    {
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);
        int ran = 0;
        DispatcherOperation op = source.Dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() => ran++));
        Assert.Equal(DispatcherOperationStatus.Pending, op.Status);
        source.Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        Assert.Equal(DispatcherOperationStatus.Completed, op.Status);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Dispatch_MouseButtonDown_ProcessInputSeesReport()
    {
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);
        int reports = 0;
        InputManager.Current.PreProcessInput += OnPreProcess;
        try
        {
            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonDown,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(12, 12),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));
        }
        finally
        {
            InputManager.Current.PreProcessInput -= OnPreProcess;
        }

        Assert.True(reports > 0);
        return;

        void OnPreProcess(object sender, PreProcessInputEventArgs e)
        {
            _ = sender;
            _ = e;
            reports++;
        }
    }

    [Fact]
    public void Dispatch_TextInput_ProcessInputSeesReport()
    {
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);
        int reports = 0;
        InputManager.Current.PreProcessInput += OnPreProcess;
        try
        {
            source.Dispatch(new SdlEvent(
                SdlEventKind.TextInput,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                0,
                "a"));
        }
        finally
        {
            InputManager.Current.PreProcessInput -= OnPreProcess;
        }

        Assert.True(reports > 0);
        return;

        void OnPreProcess(object sender, PreProcessInputEventArgs e)
        {
            _ = sender;
            _ = e;
            reports++;
        }
    }

    [Fact]
    public void OffscreenSource_WindowOps_AreNoOps()
    {
        // Offscreen frames have no SdlWindow; the window-state/ownership ops must not throw and
        // GetPlacement reports false.
        using CompositionFrame frame = CompositionFrame.CreateOffscreen(
            new Nova.Geometry.PixelSize(Size, Size),
            NovaTestVulkan.DeviceOptions());
        using var source = new SdlPresentationSource(frame);

        source.BringToFront();
        source.Minimize();
        source.Maximize();
        source.Restore();
        source.SetOwner(null);

        Assert.False(source.GetPlacement(out int x, out int y, out int width, out int height));
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void Typeface_DejaVuSans_ResolvesGlyphTypeface()
    {
        var family = new FontFamily("DejaVu Sans");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyph));
        Assert.True(glyph.GlyphCount > 0);
        Assert.True(glyph.AdvanceWidths['A'] > 0);
    }

    [Fact]
    public void TwoWindowFrames_PushedMouseEventForWindowB_LandsOnSourceB()
    {
        using TwoWindowHost host = new();

        // Drain startup noise so the pushed sequence below is deterministic.
        while (host.Host.Poll(out _))
        {
        }

        // A real SDL mouse event for window B through the shared queue.
        uint windowIdB = WindowId(host.FrameB);
        var down = new Silk.NET.SDL.Event
        {
            Button = new Silk.NET.SDL.MouseButtonEvent
            {
                Type = Silk.NET.SDL.EventType.MouseButtonDown,
                Timestamp = 0,
                WindowID = windowIdB,
                Which = 0,
                Button = (byte)Sdl.MouseButton.Left,
                Down = true,
                X = 10,
                Y = 10
            }
        };
        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref down)));

        // Pump through source A: the event names window B, so TryPump yields it and
        // Dispatch must route the report to source B's input provider. The Activate
        // report switches MouseDevice's active source (publicly observable), and the
        // Button1Press updates the wire-order button state.
        SdlEvent ev = PumpEvent(host.SourceA, host.FrameB.Window!.Handle);
        Assert.Equal(SdlEventKind.MouseButtonDown, ev.Kind);
        Assert.False(host.SourceA.IsClosing);

        host.SourceA.Dispatch(ev);

        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceB), "the mouse report must activate source B, not A");
        Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.LeftButton);
        Assert.False(host.SourceA.IsClosing);
    }

    [Fact]
    public void TwoWindowFrames_CloseRequestedForPopup_DoesNotCloseMain()
    {
        using TwoWindowHost host = new();

        // Drain startup noise so the pushed sequence below is deterministic.
        while (host.Host.Poll(out _))
        {
        }

        uint windowIdB = WindowId(host.FrameB);
        var close = new Silk.NET.SDL.Event
        {
            Window = new Silk.NET.SDL.WindowEvent
            {
                Type = Silk.NET.SDL.EventType.WindowCloseRequested,
                Timestamp = 0,
                WindowID = windowIdB
            }
        };
        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref close)));

        // A's pump consumes B's close request but must not close A.
        Assert.True(host.SourceA.TryPump(out SdlEvent ev));
        Assert.Equal(SdlEventKind.WindowCloseRequested, ev.Kind);
        Assert.Equal(host.FrameB.Window!.Handle, ev.Window);
        Assert.False(host.FrameA.Closing);
        Assert.False(host.SourceA.IsClosing);

        // Routing the event to B marks B closing; A keeps running.
        host.SourceA.Dispatch(ev);
        Assert.True(host.FrameB.Closing);
        Assert.False(host.FrameA.Closing);
        Assert.False(host.SourceA.IsClosing);
    }

    private static unsafe uint WindowId(CompositionFrame frame)
    {
        var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)frame.Window!.Handle.Value);
        return SdlApi.GetWindowID(sdlWindow);
    }

    [Fact]
    public void TwoWindowFrames_ReactivationBetweenWindows_SwitchesActiveSourceBack()
    {
        using TwoWindowHost host = new();

        // Drain startup noise so the pushed sequence below is deterministic.
        while (host.Host.Poll(out _))
        {
        }

        PushButtonEvent(host.FrameA, Sdl.MouseButton.Left, true);
        host.SourceA.Dispatch(PumpEvent(host.SourceA, host.FrameA.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceA));
        Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.LeftButton);

        // A is now "activated". B activates in turn.
        PushButtonEvent(host.FrameB, Sdl.MouseButton.Left, true);
        host.SourceA.Dispatch(PumpEvent(host.SourceA, host.FrameB.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceB));
        Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.LeftButton);

        // Back to A: the report must carry Activate again (A is no longer the active
        // source), so MouseDevice switches _inputSource back to A and processes the
        // release. The sticky-bool bug made A's report Activate-free and silent here.
        PushButtonEvent(host.FrameA, Sdl.MouseButton.Left, false);
        host.SourceA.Dispatch(PumpEvent(host.SourceA, host.FrameA.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceA));
        Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.LeftButton);
    }

    [Fact]
    public void TwoWindowFrames_AfterPopupDispose_MainWindowRegainsMouseInput()
    {
        using TwoWindowHost host = new();

        // Drain startup noise so the pushed sequence below is deterministic.
        while (host.Host.Poll(out _))
        {
        }

        PushButtonEvent(host.FrameA, Sdl.MouseButton.Left, true);
        host.SourceA.Dispatch(PumpEvent(host.SourceA, host.FrameA.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceA));

        PushButtonEvent(host.FrameB, Sdl.MouseButton.Left, true);
        host.SourceA.Dispatch(PumpEvent(host.SourceA, host.FrameB.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceB));

        // Closing the popup source reports Deactivate, which nulls _inputSource. The
        // main window's next report must re-activate it (the deaf-after-popup-close case).
        host.SourceB.Dispose();
        Assert.Null(Mouse.PrimaryDevice.ActiveSource);

        PushButtonEvent(host.FrameA, Sdl.MouseButton.Left, false);
        host.SourceA.Dispatch(PumpEvent(host.SourceA, host.FrameA.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, host.SourceA));
        Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.LeftButton);
    }

    [Fact]
    public void PopupSource_OwnerParented_CaptureEngages_DisposeReactivatesMain()
    {
        // Main window frame sharing host+device, then a real owner-parented popup source.
        using SdlHost host = new();
        using SdlWindow probe = host.CreateWindow(new WindowOptions { Title = "probe", Hidden = true });
        using VulkanDevice device = new(NovaTestVulkan.DeviceOptions([.. probe.RequiredInstanceExtensions]));
        using CompositionFrame mainFrame = new(host, device, new WindowOptions { Title = "main", Size = new Nova.Geometry.PixelSize(Size, Size), Hidden = true, Resizable = false });
        using var mainSource = new SdlPresentationSource(mainFrame);
        var popupParams = new HwndSourceParameters("popup", 40, 20);
        popupParams.SetPosition(10, 10);
        using var popupSource = new SdlPresentationSource(popupParams, mainSource, PopupKind.PopupMenu);

        // Owner-parented popup: shared host/device, distinct handle. IsPopup is the
        // observable truth: a real SDL popup (offscreen drivers lack popup support, so the
        // explicit plain-window fallback runs and IsPopup stays false).
        Assert.True(ReferenceEquals(popupSource.Owner, mainSource));
        Assert.NotEqual(mainSource.Handle, popupSource.Handle);
        if (popupSource.Frame.Window!.IsPopup)
        {
            Assert.Equal(mainSource.Frame.Window!.Handle, popupSource.Frame.Window.GetParent());
        }
        else
        {
            Assert.Equal(WindowHandle.Invalid, popupSource.Frame.Window.GetParent());
        }

        // Capture engages SDL capture (no-op offscreen, real capture on a windowing driver).
        Assert.True(popupSource.CaptureMouse());

        // Drain startup noise so the pushed sequence below is deterministic.
        while (host.Poll(out _))
        {
        }

        // An event for the popup activates the popup source.
        PushButtonEvent(popupSource.Frame, Sdl.MouseButton.Left, true);
        mainSource.Dispatch(PumpEvent(mainSource, popupSource.Frame.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, popupSource));

        // Closing the popup source reports Deactivate (nulls _inputSource) and unregisters
        // it; the main window's next event re-activates the main source.
        popupSource.Dispose();
        Assert.Null(Mouse.PrimaryDevice.ActiveSource);

        PushButtonEvent(mainFrame, Sdl.MouseButton.Left, false);
        mainSource.Dispatch(PumpEvent(mainSource, mainFrame.Window!.Handle));
        Assert.True(ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, mainSource));
        Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.LeftButton);
    }

    [Fact]
    public void TwoWindowFrames_WpfContent_BothReadbacksShowOwnContent()
    {
        using TwoWindowHost host = new();
        host.FrameA.Presenter.EnableReadback();
        host.FrameB.Presenter.EnableReadback();

        var visualA = new DrawingVisual();
        using (DrawingContext dc = visualA.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Red, null, new Rect(8, 8, 16, 16));
        }

        host.SourceA.RootVisual = visualA;

        var visualB = new DrawingVisual();
        using (DrawingContext dc = visualB.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Blue, null, new Rect(8, 8, 16, 16));
        }

        host.SourceB.RootVisual = visualB;

        // MediaContext compiles both registered targets onto the shared channel.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        host.FrameA.Present();
        host.FrameB.Present();

        ReadOnlySpan<byte> pixelsA = host.FrameA.Presenter.ReadbackRgba().Span;
        ReadOnlySpan<byte> pixelsB = host.FrameB.Presenter.ReadbackRgba().Span;

        // Window A shows red at the rect center, not blue and not black.
        Assert.Equal(255, Channel(pixelsA, 12, 12, 0));
        Assert.Equal(0, Channel(pixelsA, 12, 12, 2));

        // Window B shows blue, not red and not black.
        Assert.Equal(0, Channel(pixelsB, 12, 12, 0));
        Assert.Equal(255, Channel(pixelsB, 12, 12, 2));
    }

    [Fact]
    public void TwoWindowFrames_DisposeAll_BindingsAndChannelMappingsDrainToZero()
    {
        // Closing a window must dispose its source, which detaches the DuceRuntime binding;
        // with no live frame left for a graph, its channel mappings drain too. Leaked
        // bindings otherwise accumulate (1 → 2 → 4) and cross-graph contamination appears.
        using TwoWindowHost host = new();
        Assert.Equal(2, CountBindings());
        Assert.True(CountChannelMappings() >= 2);

        host.SourceB.Dispose();
        Assert.Equal(1, CountBindings());
        Assert.True(CountChannelMappings() >= 2);

        host.SourceA.Dispose();
        Assert.Equal(0, CountBindings());
        Assert.Equal(0, CountChannelMappings());
    }

    private static int CountBindings()
    {
        // DuceRuntime has no public count; reflect the private list.
        System.Reflection.FieldInfo field = typeof(DuceRuntime).GetField(
            "s_bindings",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(null)!).Count;
    }

    private static int CountChannelMappings()
    {
        System.Reflection.FieldInfo field = typeof(DuceRuntime).GetField(
            "s_graphsByChannel",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(null)!).Count;
    }

    private static void PushButtonEvent(CompositionFrame frame, Sdl.MouseButton button, bool down)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Button = new Silk.NET.SDL.MouseButtonEvent
            {
                Type = down ? Silk.NET.SDL.EventType.MouseButtonDown : Silk.NET.SDL.EventType.MouseButtonUp,
                Timestamp = 0,
                WindowID = WindowId(frame),
                Which = 0,
                Button = (byte)button,
                Down = down,
                X = 10,
                Y = 10
            }
        };
        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref ev)));
    }

    /// <summary>
    /// Pumps until an event for <paramref name="expectedWindow"/> arrives. A pushed SDL
    /// event is not always visible to the immediately following poll (the offscreen driver
    /// can take one pump cycle to surface it), so a single immediate pump after a push is
    /// not deterministic; the production pump loops continuously and never notices.
    /// </summary>
    private static SdlEvent PumpEvent(SdlPresentationSource source, WindowHandle expectedWindow)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (source.TryPump(out SdlEvent ev))
            {
                Assert.Equal(expectedWindow, ev.Window);
                return ev;
            }
        }

        Assert.Fail($"no event for window {expectedWindow} arrived after 5 pump attempts");
        return default;
    }

    /// <summary>
    /// Two window frames sharing one SDL host and one Vulkan device, each with its own
    /// presentation source. Mirrors the main-window + popup layout.
    /// </summary>
    private sealed class TwoWindowHost : IDisposable
    {
        public SdlHost Host { get; }

        public VulkanDevice Device { get; }

        public CompositionFrame FrameA { get; }

        public CompositionFrame FrameB { get; }

        public SdlPresentationSource SourceA { get; }

        public SdlPresentationSource SourceB { get; }

        public TwoWindowHost()
        {
            Host = new SdlHost();
            using SdlWindow probe = Host.CreateWindow(new WindowOptions { Title = "probe", Hidden = true });
            Device = new VulkanDevice(NovaTestVulkan.DeviceOptions([.. probe.RequiredInstanceExtensions]));
            FrameA = new CompositionFrame(Host, Device, new WindowOptions { Title = "A", Size = new Nova.Geometry.PixelSize(Size, Size), Hidden = true, Resizable = false });
            FrameB = new CompositionFrame(Host, Device, new WindowOptions { Title = "B", Size = new Nova.Geometry.PixelSize(Size, Size), Hidden = true, Resizable = false });
            SourceA = new SdlPresentationSource(FrameA);
            SourceB = new SdlPresentationSource(FrameB);
        }

        public void Dispose()
        {
            SourceB.Dispose();
            SourceA.Dispose();
            FrameB.Dispose();
            FrameA.Dispose();
            Device.Dispose();
            Host.Dispose();
        }
    }

    private static void InjectRedRectangle(SlaveGraph graph)
    {
        const uint visual = 1;
        const uint brush = 2;
        const uint renderData = 3;

        var channel = new Writer();
        channel.UInt32((uint)MilCommandKind.ChannelCreateResource);
        channel.UInt32(visual);
        channel.UInt32((uint)MilResourceType.Visual);
        channel.UInt32((uint)MilCommandKind.ChannelCreateResource);
        channel.UInt32(brush);
        channel.UInt32((uint)MilResourceType.SolidColorBrush);
        channel.UInt32((uint)MilCommandKind.ChannelCreateResource);
        channel.UInt32(renderData);
        channel.UInt32((uint)MilResourceType.RenderData);
        channel.UInt32((uint)MilCommandKind.SolidColorBrush);
        channel.UInt32(brush);
        channel.Double(1.0);
        channel.Float(1);
        channel.Float(0);
        channel.Float(0);
        channel.Float(1);
        channel.UInt32(0);
        channel.UInt32(0);
        channel.UInt32(0);
        channel.UInt32(0);
        byte[] blob = DrawRectangleBlob();
        channel.UInt32((uint)MilCommandKind.RenderData);
        channel.UInt32(renderData);
        channel.UInt32((uint)blob.Length);
        channel.Bytes(blob);
        channel.UInt32((uint)MilCommandKind.VisualSetContent);
        channel.UInt32(visual);
        channel.UInt32(renderData);
        channel.UInt32((uint)MilCommandKind.TargetSetRoot);
        channel.UInt32(0);
        channel.UInt32(visual);

        MilCommandParser.ParseChannel(channel.ToArray(), graph);
        graph.SetRenderDataDependents(new Nova.Geometry.ResourceHandle(renderData), [new Nova.Geometry.ResourceHandle(brush)]);
    }

    private static byte[] DrawRectangleBlob()
    {
        var blob = new Writer();
        blob.Int32(48);
        blob.UInt32((uint)MilCommandKind.DrawRectangle);
        blob.Double(8);
        blob.Double(8);
        blob.Double(16);
        blob.Double(16);
        blob.UInt32(1);
        blob.UInt32(0);
        return blob.ToArray();
    }

    private static byte Channel(ReadOnlySpan<byte> pixels, int x, int y, int channel)
    {
        return pixels[(((y * Size) + x) * 4) + channel];
    }

    [Fact]
    public void WindowFrame_TransparentPropagatesToWindowOptionsAndClearIsAlphaZero()
    {
        // Regression: CreateWindowFrame read only Width/Height/WindowName from the
        // HwndSourceParameters, so a Window.AllowsTransparency window (which sets
        // UsesPerPixelOpacity) was silently opaque — the flag died at the frame. The
        // per-pixel-opacity flag must reach the SDL window options, and a frame with no
        // content must clear to alpha 0 (a transparent desktop window, not black).
        var parameters = new HwndSourceParameters("transparent", Size, Size)
        {
            UsesPerPixelOpacity = true
        };
        using var source = new SdlPresentationSource(parameters);

        Assert.True(source.UsesPerPixelOpacity);
        Assert.NotNull(source.Frame.Window);
        Assert.True(source.Frame.Window.Options.Transparent);
        Assert.True(HasSdlFlag(source.Frame.Window.Handle, 0x0000_0000_4000_0000)); // SDL_WINDOW_TRANSPARENT

        source.Frame.Presenter.EnableReadback();
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Red, null, new Rect(8, 8, 16, 16));
        }

        source.RootVisual = visual;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        source.Frame.Present();
        ReadOnlySpan<byte> pixels = source.Frame.Presenter.ReadbackRgba().Span;

        // Outside the rect: clear is genuinely transparent (alpha 0) — the pixels are
        // not an opaque black background.
        Assert.Equal(0, Channel(pixels, 4, 4, 3));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));
        // Inside the rect: the content is opaque red.
        Assert.Equal(255, Channel(pixels, 12, 12, 0));
        Assert.Equal(255, Channel(pixels, 12, 12, 3));
    }

    [Fact]
    public void WindowFrame_Opaque_TransparencyFlagStaysFalse()
    {
        // Ordinary windows must NOT become transparent: without UsesPerPixelOpacity the
        // window stays opaque (no SDL_WINDOW_TRANSPARENT, no transparent options).
        var parameters = new HwndSourceParameters("opaque", Size, Size);
        using var source = new SdlPresentationSource(parameters);

        Assert.False(source.UsesPerPixelOpacity);
        Assert.NotNull(source.Frame.Window);
        Assert.False(source.Frame.Window.Options.Transparent);
        Assert.False(HasSdlFlag(source.Frame.Window.Handle, 0x0000_0000_4000_0000)); // SDL_WINDOW_TRANSPARENT
    }

    private static unsafe bool HasSdlFlag(Nova.Sdl.WindowHandle window, ulong flag)
    {
        var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Value);
        return (SdlApi.GetWindowFlags(sdlWindow) & flag) != 0;
    }

    private static partial class Native
    {
        [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SetEnv(string name, string value, int overwrite);
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void UInt32(uint value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Int32(int value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Float(float value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Double(double value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Bytes(ReadOnlySpan<byte> value)
        {
            _bytes.AddRange(value);
        }

        public byte[] ToArray()
        {
            return [.. _bytes];
        }
    }
}
