using System.Runtime.InteropServices;
using Nova.SystemTheme;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;

namespace Nova.Sdl.Tests;

public sealed partial class SdlHostTests
{
    private const ulong HiddenFlag = 0x0000_0000_0000_0008;
    private const ulong TransparentFlag = 0x0000_0000_4000_0000;
    private const ulong ResizableFlag = 0x0000_0000_0000_0020;
    private const ulong HighPixelDensityFlag = 0x0000_0000_0000_2000;
    private const ulong VulkanFlag = 0x0000_0000_1000_0000;
    private const ulong TooltipFlag = 0x0000_0000_0004_0000;
    private const ulong PopupMenuFlag = 0x0000_0000_0008_0000;

    public SdlHostTests()
    {
        ForceOffscreenDriver();
    }

    [Fact]
    public void Host_Init_ReportsInitialized()
    {
        using SdlHost host = new();
        Assert.True(host.IsInitialized);
    }

    [Fact]
    public void Host_Init_UsesForcedOffscreenDriver()
    {
        using SdlHost host = new();
        unsafe
        {
            Ptr<sbyte> driver = SdlApi.GetCurrentVideoDriver();
            string? name = driver.Native == null ? null : Marshal.PtrToStringUTF8((nint)driver.Native);
            Assert.Equal("offscreen", name);
        }
    }

    [Fact]
    public void Host_Init_IsRefCountedAcrossInstances()
    {
        SdlHost first = new();
        using SdlHost second = new();
        Assert.True(first.IsInitialized);
        Assert.True(second.IsInitialized);

        first.Dispose();

        Assert.False(first.IsInitialized);
        Assert.True(second.IsInitialized);
    }

    [Fact]
    public void Host_Dispose_IsIdempotent()
    {
        SdlHost host = new();
        host.Dispose();
        host.Dispose();
        Assert.False(host.IsInitialized);
    }

    [Fact]
    public void Host_Metrics_ReportSdlOrDocumentedFallback()
    {
        using SdlHost host = new();

        HostTheme.GetWorkArea(out int left, out int top, out int right, out int bottom);
        int cxScreen = HostTheme.GetSystemMetric(SystemMetricIndex.CxScreen);
        int cyScreen = HostTheme.GetSystemMetric(SystemMetricIndex.CyScreen);
        int pixelsPerInch = HostTheme.PixelsPerInch;

        uint primary = SdlApi.GetPrimaryDisplay();
        if (primary == 0)
        {
            // No SDL display (driver without one): hardcoded Classic fallback.
            Assert.Equal((0, 0, 1920, 1080), (left, top, right, bottom));
            Assert.Equal(1920, cxScreen);
            Assert.Equal(1080, cyScreen);
            Assert.Equal(96, pixelsPerInch);
            return;
        }

        // SDL display: work area is the primary usable bounds, screen size the primary bounds.
        var usable = new Silk.NET.SDL.Rect();
        Assert.True(SdlApi.GetDisplayUsableBounds(primary, new Ref<Silk.NET.SDL.Rect>(ref usable)));
        Assert.Equal(usable.X, left);
        Assert.Equal(usable.Y, top);
        Assert.Equal(usable.X + usable.W, right);
        Assert.Equal(usable.Y + usable.H, bottom);

        var bounds = new Silk.NET.SDL.Rect();
        Assert.True(SdlApi.GetDisplayBounds(primary, new Ref<Silk.NET.SDL.Rect>(ref bounds)));
        Assert.Equal(bounds.W, cxScreen);
        Assert.Equal(bounds.H, cyScreen);

        float scale = SdlApi.GetDisplayContentScale(primary);
        int expectedPpi = scale > 0 && float.IsFinite(scale)
            ? Math.Max(96, (int)Math.Round(96 * scale, MidpointRounding.AwayFromZero))
            : 96;
        Assert.Equal(expectedPpi, pixelsPerInch);

        Assert.True(HostTheme.GetSystemMetric(SystemMetricIndex.MonitorCount) >= 1);
        Assert.True(HostTheme.GetSystemMetric(SystemMetricIndex.CxVirtualScreen) > 0);
        Assert.Equal(500, HostTheme.DoubleClickTime);
    }

    [Fact]
    public void Window_CreateHidden_ReportsFlagsSizeAndScale()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        Assert.True(window.Handle.IsValid);
        Assert.True(HasFlag(window.Handle, HiddenFlag));
        Assert.True(HasFlag(window.Handle, VulkanFlag));
        Assert.True(HasFlag(window.Handle, ResizableFlag));
        Assert.True(HasFlag(window.Handle, HighPixelDensityFlag));
        Assert.True(window.PixelSize.Width > 0);
        Assert.True(window.PixelSize.Height > 0);
        Assert.True(window.DisplayScale > 0);
    }

    [Fact]
    public void Window_Transparent_SetsSdlTransparentFlag()
    {
        // Regression: WindowOptions.Transparent was never mapped to SDL_WINDOW_TRANSPARENT,
        // so AllowsTransparency windows were silently opaque. The native flag must be set
        // on the SDL window, and the default (opaque) window must NOT carry it.
        using SdlHost host = new();
        using SdlWindow transparent = host.CreateWindow(new WindowOptions
        {
            Title = "Nova.Sdl.Tests",
            Hidden = true,
            Transparent = true
        });
        using SdlWindow opaque = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        Assert.True(transparent.Options.Transparent);
        Assert.True(HasFlag(transparent.Handle, TransparentFlag));
        Assert.False(opaque.Options.Transparent);
        Assert.False(HasFlag(opaque.Handle, TransparentFlag));
    }

    [Fact]
    public void Window_RequiredInstanceExtensions_ContainVkKhrSurface()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        Assert.Contains("VK_KHR_surface", window.RequiredInstanceExtensions);
    }

    [Fact]
    public void Window_StateOps_DoNotThrowOffscreen()
    {
        // The offscreen driver reports "not supported" for window-state operations; SdlWindow
        // treats that as a no-op rather than throwing.
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        using SdlWindow other = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        window.BringToFront();
        window.Minimize();
        window.Maximize();
        window.Restore();
        window.SetParent(other);
        window.SetParent(null);

        Assert.True(window.Handle.IsValid);
        Assert.True(other.Handle.IsValid);
    }

    [Fact]
    public void Window_PopupMenu_IsPopupWithParentWhenDriverSupportsIt()
    {
        using SdlHost host = new();
        using SdlWindow parent = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });
        using SdlWindow popup = host.CreateWindow(new WindowOptions
        {
            Title = "Nova.Sdl.Tests",
            Size = new Nova.Geometry.PixelSize(40, 20),
            Resizable = false,
            Popup = PopupKind.PopupMenu,
            Parent = parent,
            X = 10,
            Y = 12
        });

        Assert.True(popup.Handle.IsValid);
        // IsPopup is the observable truth: either a real SDL popup (POPUP_MENU flag set,
        // parent linked) or an explicit fallback (plain window, flag absent, no parent link).
        Assert.Equal(popup.IsPopup, HasFlag(popup.Handle, PopupMenuFlag));
        if (popup.IsPopup)
        {
            Assert.Equal(parent.Handle, popup.GetParent());
        }
        else
        {
            Assert.Equal(WindowHandle.Invalid, popup.GetParent());
        }
    }

    [Fact]
    public void Window_Tooltip_IsPopupWithTooltipFlag()
    {
        using SdlHost host = new();
        using SdlWindow parent = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });
        using SdlWindow tooltip = host.CreateWindow(new WindowOptions
        {
            Title = "Nova.Sdl.Tests",
            Size = new Nova.Geometry.PixelSize(40, 20),
            Resizable = false,
            Popup = PopupKind.Tooltip,
            Parent = parent
        });

        Assert.True(tooltip.Handle.IsValid);
        Assert.Equal(tooltip.IsPopup, HasFlag(tooltip.Handle, TooltipFlag));
    }

    [Fact]
    public void Window_CaptureMouse_TogglesWithoutThrow()
    {
        // SDL_CaptureMouse: a no-op when the driver does not implement it (offscreen),
        // a real capture toggle on a windowing driver. Either way it must not throw.
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        window.CaptureMouse(true);
        window.CaptureMouse(false);
    }

    [Fact]
    public void Host_GetGlobalMousePosition_ReturnsFinitePoint()
    {
        using SdlHost host = new();
        Nova.Geometry.Point position = host.GetGlobalMousePosition();
        Assert.True(double.IsFinite(position.X));
        Assert.True(double.IsFinite(position.Y));
    }

    [Fact]
    public void Poll_SkipsUnmappedEventsAndReturnsNextMappedEvent()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        // Startup noise (MouseAdded / KeyboardAdded / WindowPixelSizeChanged) is unmapped;
        // drain the queue first so the pushed sequence below is deterministic.
        while (host.Poll(out _))
        {
        }

        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            uint windowId = SdlApi.GetWindowID(sdlWindow);

            // An unmapped event followed by a mapped one: Poll must consume and skip the
            // unmapped event and surface the MouseButtonUp on the same call.
            var unmapped = new Silk.NET.SDL.Event
            {
                Type = (uint)Silk.NET.SDL.EventType.ClipboardUpdate
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref unmapped)));

            var up = new Silk.NET.SDL.Event
            {
                // The union members overlap at offset 0, so the event type must be set
                // through the same union member (MouseButtonEvent.Type) rather than
                // Event.Type before assigning Button (which would overwrite it).
                Button = new Silk.NET.SDL.MouseButtonEvent
                {
                    Type = Silk.NET.SDL.EventType.MouseButtonUp,
                    Timestamp = 0,
                    WindowID = windowId,
                    Which = 0,
                    Button = (byte)Nova.Sdl.MouseButton.Left,
                    Down = false,
                    X = 123,
                    Y = 45
                }
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref up)));
        }

        Assert.True(host.Poll(out SdlEvent ev));
        Assert.Equal(SdlEventKind.MouseButtonUp, ev.Kind);
        Assert.Equal(Nova.Sdl.MouseButton.Left, ev.MouseButton);
        Assert.Equal(new Nova.Geometry.Point(123, 45), ev.Position);

        Assert.False(host.Poll(out _));
    }

    [Fact]
    public void Window_Dispose_IsIdempotent()
    {
        using SdlHost host = new();
        SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });
        Assert.True(window.Handle.IsValid);

        window.Dispose();
        window.Dispose();

        Assert.False(window.Handle.IsValid);
    }

    [Fact]
    public void Poll_WheelNegativeDelta_IsPreserved()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        while (host.Poll(out _))
        {
        }

        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            uint windowId = SdlApi.GetWindowID(sdlWindow);

            // Wheel-down: SDL Y is negative. The map must NOT clamp the sign away — a
            // zeroed delta made wheel-down indistinguishable from no scroll.
            var wheel = new Silk.NET.SDL.Event
            {
                Wheel = new Silk.NET.SDL.MouseWheelEvent
                {
                    Type = Silk.NET.SDL.EventType.MouseWheel,
                    Timestamp = 0,
                    WindowID = windowId,
                    Which = 0,
                    X = 0,
                    Y = -3,
                    MouseX = 12,
                    MouseY = 34
                }
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref wheel)));
        }

        Assert.True(host.Poll(out SdlEvent ev));
        Assert.Equal(SdlEventKind.MouseWheel, ev.Kind);
        Assert.Equal(-3, ev.Delta.Y);
        Assert.Equal(0, ev.Delta.X);
        Assert.Equal(new Nova.Geometry.Point(12, 34), ev.Position);

        Assert.False(host.Poll(out _));
    }

    [Fact]
    public void Poll_WheelPositiveDelta_IsPreserved()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        while (host.Poll(out _))
        {
        }

        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            uint windowId = SdlApi.GetWindowID(sdlWindow);

            var wheel = new Silk.NET.SDL.Event
            {
                Wheel = new Silk.NET.SDL.MouseWheelEvent
                {
                    Type = Silk.NET.SDL.EventType.MouseWheel,
                    Timestamp = 0,
                    WindowID = windowId,
                    Which = 0,
                    X = 2,
                    Y = 3,
                    MouseX = 1,
                    MouseY = 2
                }
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref wheel)));
        }

        Assert.True(host.Poll(out SdlEvent ev));
        Assert.Equal(SdlEventKind.MouseWheel, ev.Kind);
        Assert.Equal(3, ev.Delta.Y);
        Assert.Equal(2, ev.Delta.X);
    }

    [Fact]
    public void Poll_MouseMotionNegativeRelativeDelta_IsPreserved()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });

        while (host.Poll(out _))
        {
        }

        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            uint windowId = SdlApi.GetWindowID(sdlWindow);

            // Relative motion up/left: negative Xrel/Yrel must survive the map (the
            // previous clamp corrupted any delta integrator's direction).
            var motion = new Silk.NET.SDL.Event
            {
                Motion = new Silk.NET.SDL.MouseMotionEvent
                {
                    Type = Silk.NET.SDL.EventType.MouseMotion,
                    Timestamp = 0,
                    WindowID = windowId,
                    Which = 0,
                    X = 100,
                    Y = 100,
                    Xrel = -5,
                    Yrel = -7
                }
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref motion)));
        }

        Assert.True(host.Poll(out SdlEvent ev));
        Assert.Equal(SdlEventKind.MouseMoved, ev.Kind);
        Assert.Equal(-5, ev.Delta.X);
        Assert.Equal(-7, ev.Delta.Y);
        Assert.Equal(new Nova.Geometry.Point(100, 100), ev.Position);
    }

    [Fact]
    public void GlobalMouseAndWindowPosition_ShareDesktopDevicePixelSpace()
    {
        // Coordinate-space pin for popup placement (ToolTip PlacementMode.Mouse): the mouse
        // position consumed by the popup path must be SDL's desktop-relative position in
        // DEVICE PIXELS (SDL_GetGlobalMouseState), the same space SDL window positions use
        // (SDL_GetWindowPosition). A placement path that mixes these with WPF LOGICAL units
        // (or with window-relative coords) double-scales or mis-offsets the popup — this
        // test pins the units so that regression cannot silently land.
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true, X = 17, Y = 23 });

        // Global mouse: host.GetGlobalMousePosition must equal the raw SDL desktop-relative
        // query (device px), with NO logical→device scaling applied.
        float rawMouseX = 0;
        float rawMouseY = 0;
        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            _ = SdlApi.GetGlobalMouseState(new Ref<float>(ref rawMouseX), new Ref<float>(ref rawMouseY));
            Nova.Geometry.Point hostMouse = host.GetGlobalMousePosition();
            Assert.Equal(rawMouseX, hostMouse.X);
            Assert.Equal(rawMouseY, hostMouse.Y);
            Assert.True(double.IsFinite(hostMouse.X) && double.IsFinite(hostMouse.Y));

            // Window position: SdlWindow.Position must equal SDL_GetWindowPosition (desktop
            // device px), the space popups are positioned in via SetBounds.
            int rawWinX = 0;
            int rawWinY = 0;
            Assert.True(SdlApi.GetWindowPosition(sdlWindow, new Ref<int>(ref rawWinX), new Ref<int>(ref rawWinY)));
            Assert.Equal(rawWinX, window.Position.X);
            Assert.Equal(rawWinY, window.Position.Y);
        }

        // Device-pixel derivation of the PlacementMode.Mouse rect with the Linux cursor
        // default (32x32, hotspot 0,0 — see Popup.GetMouseCursorSize Linux branch, patch
        // 0009): the popup must clear the cursor by (32, 34) in the same device-pixel
        // space. This is pure arithmetic on SDL units; no compositor needed.
        double mouseX = window.Position.X + 100;
        double mouseY = window.Position.Y + 80;
        const int cursorWidth = 32;
        const int cursorHeight = 32;
        const int hotX = 0;
        const int hotY = 0;
        double clearWidth = Math.Max(0, cursorWidth - hotX);
        double clearHeight = Math.Max(0, cursorHeight - hotY + 2);
        Assert.Equal(32, clearWidth);
        Assert.Equal(34, clearHeight);

        // A popup placed at the cursor's bottom-right corner (device px) lands 1:1 in the
        // same space — no scale factor is applied between the mouse rect and the popup's
        // SetBounds coordinates.
        double popupX = mouseX + clearWidth;
        double popupY = mouseY + clearHeight - 2;
        Assert.Equal(mouseX + 32, popupX);
        Assert.Equal(mouseY + 32, popupY);
    }

    [Fact]
    public void Host_GetGlobalMousePosition_DerivesFromLastMouseEventNotGlobalQuery()
    {
        // Regression pin for popup placement on Wayland: SDL_GetGlobalMouseState returns
        // (0,0) until some window has pointer focus, so the FIRST popup opened on a fresh
        // window (ContextMenu / ToolTip placement) landed at the origin while a SECOND
        // open — after the first popup had established pointer focus — placed correctly.
        // The placement point must therefore come from the last reported mouse event
        // (window-relative) plus that window's screen origin, independent of any global
        // cursor query. This test drives the exact failing shape: a mouse event is
        // delivered, and GetGlobalMousePosition must return its window-relative position
        // offset by the window position — NOT the raw SDL global-mouse query (0,0 here
        // on offscreen, which has no pointer focus either).
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true, X = 17, Y = 23 });

        // Drain startup noise so the pushed sequence below is deterministic.
        while (host.Poll(out _))
        {
        }

        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            uint windowId = SdlApi.GetWindowID(sdlWindow);

            // The raw SDL global query reports (0,0) here (offscreen has no pointer
            // focus — the Wayland case this pin stands in for).
            float rawGlobalX = -1;
            float rawGlobalY = -1;
            _ = SdlApi.GetGlobalMouseState(new Ref<float>(ref rawGlobalX), new Ref<float>(ref rawGlobalY));
            Assert.Equal(0, rawGlobalX);
            Assert.Equal(0, rawGlobalY);

            // Deliver a mouse motion at a KNOWN window-relative position.
            var motion = new Silk.NET.SDL.Event
            {
                Motion = new Silk.NET.SDL.MouseMotionEvent
                {
                    Type = Silk.NET.SDL.EventType.MouseMotion,
                    Timestamp = 0,
                    WindowID = windowId,
                    Which = 0,
                    X = 100,
                    Y = 80,
                    Xrel = 0,
                    Yrel = 0
                }
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref motion)));
        }

        Assert.True(host.Poll(out SdlEvent ev));
        Assert.Equal(SdlEventKind.MouseMoved, ev.Kind);
        Assert.Equal(new Nova.Geometry.Point(100, 80), ev.Position);

        // Screen point = window-relative event position + window origin (device px).
        Nova.Geometry.Point position = host.GetGlobalMousePosition();
        Assert.Equal(window.Position.X + 100, position.X);
        Assert.Equal(window.Position.Y + 80, position.Y);
        Assert.NotEqual(0, position.X);
        Assert.NotEqual(0, position.Y);
    }

    private static void ForceOffscreenDriver()
    {
        // Environment.SetEnvironmentVariable does not reach libc getenv on this runtime,
        // so SDL would keep its default driver; setenv reaches SDL directly.
        _ = Native.SetEnv("SDL_VIDEO_DRIVER", "offscreen", 1);
        _ = Native.SetEnv("SDL_VIDEODRIVER", "offscreen", 1);
    }

    private static unsafe bool HasFlag(WindowHandle window, ulong flag)
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
}

public sealed partial class SdlHostTests
{
    [Fact]
    public void Poll_DropFileEvent_MapsPathAndPosition()
    {
        using SdlHost host = new();
        using SdlWindow window = host.CreateWindow(new WindowOptions { Title = "Nova.Sdl.Tests", Hidden = true });
        while (host.Poll(out _))
        {
        }

        unsafe
        {
            var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)window.Handle.Value);
            uint windowId = SdlApi.GetWindowID(sdlWindow);
            byte[] path = "file.txt"u8.ToArray();
            fixed (byte* p = path)
            {
                var drop = new Silk.NET.SDL.Event
                {
                    Drop = new Silk.NET.SDL.DropEvent
                    {
                        Type = Silk.NET.SDL.EventType.DropFile,
                        WindowID = windowId,
                        X = 12.5f,
                        Y = 34.5f,
                        Data = (sbyte*)p
                    }
                };
                Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref drop)));
            }
        }

        Assert.True(host.Poll(out SdlEvent ev));
        Assert.Equal(SdlEventKind.DropFile, ev.Kind);
        Assert.Equal("file.txt", ev.Text);
        Assert.Equal(new Nova.Geometry.Point(12.5, 34.5), ev.Position);
    }
}

public sealed partial class SdlHostTests
{
    [Fact]
    public void MimeClipboard_SetOnly_NoRead()
    {
        using SdlHost host = new();
        bool set = SdlHost.SetClipboardData(new Dictionary<string, byte[]> { ["image/png"] = [1, 2, 3] });
        Assert.True(set);
    }

    [Fact]
    public void MimeClipboard_RoundTripsImageAndText()
    {
        using SdlHost host = new();
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4];
        bool set = SdlHost.SetClipboardData(new Dictionary<string, byte[]>
        {
            ["image/png"] = png,
            ["text/plain"] = "hello"u8.ToArray()
        });
        Assert.True(set);
        Assert.True(SdlHost.HasClipboardData("image/png"));
        Assert.True(SdlHost.HasClipboardData("text/plain"));
        Assert.False(SdlHost.HasClipboardData("image/jpeg"));

        Assert.Equal(png, SdlHost.GetClipboardData("image/png"));
        Assert.Equal("hello"u8.ToArray(), SdlHost.GetClipboardData("text/plain"));
        Assert.Null(SdlHost.GetClipboardData("image/jpeg"));
    }

    [Fact]
    public void MimeClipboard_ReplacementReleasesPreviousClaim()
    {
        using SdlHost host = new();
        Assert.True(SdlHost.SetClipboardData(new Dictionary<string, byte[]> { ["image/png"] = [1, 2, 3] }));
        Assert.True(SdlHost.SetClipboardData(new Dictionary<string, byte[]> { ["text/plain"] = [9, 9] }));
        Assert.False(SdlHost.HasClipboardData("image/png"));
        Assert.Equal([9, 9], SdlHost.GetClipboardData("text/plain"));
    }
}

public sealed partial class SdlHostTests
{

}
