using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Geometry;
using Nova.Vulkan;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;
using SilkWindow = Silk.NET.SDL.WindowHandle;

namespace Nova.Sdl;

/// <summary>An SDL3 window. Owns the native window; the Vulkan device owns any created VkSurface.</summary>
[PublicAPI]
public sealed class SdlWindow : ISurfaceSource, IDisposable
{
    private const ulong VulkanFlag = 0x0000_0000_1000_0000;
    private const ulong HighPixelDensityFlag = 0x0000_0000_0000_2000;
    private const ulong ResizableFlag = 0x0000_0000_0000_0020;
    private const ulong HiddenFlag = 0x0000_0000_0000_0008;
    private const ulong TransparentFlag = 0x0000_0000_4000_0000;
    private const ulong TooltipFlag = 0x0000_0000_0004_0000;
    private const ulong PopupMenuFlag = 0x0000_0000_0008_0000;

    private SilkWindow _window;
    private int _disposed;
    private bool _mouseCaptured;
    private readonly Dictionary<SystemCursorKind, Silk.NET.SDL.CursorHandle> _cursors = [];
    private Func<Point, HitTestRegion>? _hitTestResolver;
    private GCHandle _selfHandle;

    internal SdlWindow(WindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;

        ulong flags = VulkanFlag | HighPixelDensityFlag;
        if (options.Resizable && options.Popup == PopupKind.None)
        {
            // Popups cannot be resized; SDL treats the request as irrelevant.
            flags |= ResizableFlag;
        }

        if (options.Hidden)
        {
            flags |= HiddenFlag;
        }

        if (options.Transparent)
        {
            // SDL_WINDOW_TRANSPARENT: the compositor blends this window's pixels with
            // per-pixel alpha. The Vulkan presenter pairs it with a premultiplied
            // swapchain composite-alpha mode so the frame's alpha channel reaches the
            // desktop.
            flags |= TransparentFlag;
        }

        try
        {
            if (options.Popup != PopupKind.None)
            {
                IsPopup = CreatePopup(options, flags);
                if (!IsPopup)
                {
                    // The video driver has no popup support (e.g. offscreen): fall back to a
                    // regular window, parented when a parent was supplied. IsPopup stays false
                    // so callers can tell a real popup (compositor grab, auto-dismiss) from the
                    // fallback (plain window with at best a WM parent link).
                    _window = CreateWindowNative(options.Title, options.Size.Width, options.Size.Height, flags);
                    if (options.Parent is not null)
                    {
                        SetParent(options.Parent);
                    }
                }
            }
            else
            {
                _window = CreateWindowNative(options.Title, options.Size.Width, options.Size.Height, flags);
            }

            Handle = new WindowHandle(ToNativeInt(_window));
            SdlId = SdlApi.GetWindowID(_window);
            PixelSize = QueryPixelSize();
            Position = QueryPosition();
            DisplayScale = QueryDisplayScale();
            RequiredInstanceExtensions = QueryRequiredExtensions();

            // SDL disables text input until told otherwise: without this the window never
            // receives TextInput events and typed characters never reach WPF (key events
            // like Enter/Backspace still do, which is why they kept working).
            _ = SdlApi.StartTextInput(_window);
            _selfHandle = GCHandle.Alloc(this);
        }
        catch (SdlException)
        {
            if (_window != default)
            {
                SdlApi.DestroyWindow(_window);
            }

            throw;
        }
    }

    /// <summary>
    /// True when the window was created with <c>SDL_CreatePopupWindow</c> (real popup
    /// semantics: compositor grab / auto-dismiss, no taskbar entry). False for regular
    /// windows and for the fallback used by drivers without popup support.
    /// </summary>
    public bool IsPopup { get; private set; }

    /// <summary>True after <see cref="Dispose"/> has run.</summary>
    public bool IsDisposed => _disposed != 0;

    public WindowOptions Options { get; }

    /// <inheritdoc />
    public bool PrefersTransparentComposite => Options.Transparent;

    public WindowHandle Handle { get; private set; }

    public PixelSize PixelSize { get; private set; }

    public Point Position { get; private set; }

    public IReadOnlyList<string> RequiredInstanceExtensions { get; private set; }

    public double DisplayScale { get; private set; }

    internal SdlHost? Host { get; set; }

    internal uint SdlId { get; }

    public SurfaceHandle CreateSurface(InstanceHandle instance)
    {
        if (!instance.IsValid)
        {
            throw new ArgumentException("Instance handle is invalid.", nameof(instance));
        }

        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return CreateSurfaceNative(instance);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_mouseCaptured)
        {
            // Release the SDL-level mouse capture before destroying the window. SDL
            // capture routes every mouse event to the capturing window; destroying it
            // while capture is engaged can leave the compositor delivering events to a
            // dead window, which reads as every remaining window going deaf (the popup
            // closed but the main window no longer receives input).
            _ = SdlApi.CaptureMouse((byte)0);
            _mouseCaptured = false;
        }

        Host?.UnregisterWindow(this);
        if (_selfHandle.IsAllocated)
        {
            unsafe
            {
                _ = SdlApi.SetWindowHitTest(_window, default, null);
            }

            _selfHandle.Free();
        }

        foreach (Silk.NET.SDL.CursorHandle cursor in _cursors.Values)
        {
            SdlApi.DestroyCursor(cursor);
        }

        _cursors.Clear();
        SdlApi.DestroyWindow(_window);
        _window = default;
        Handle = WindowHandle.Invalid;
    }

    /// <summary>
    /// Sets the window icon from BGRA pixel rows (WPF PixelFormats.Bgra32 — the byte
    /// order SDL_PIXELFORMAT_ARGB8888 expects). Compositors may ignore it (Wayland has no
    /// universal window-icon surface), but SDL requests it where supported.
    /// </summary>
    public unsafe void SetWindowIcon(int width, int height, int stride, ReadOnlySpan<byte> bgraPixels)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (width <= 0 || height <= 0 || bgraPixels.Length < stride * height)
        {
            return;
        }

        fixed (byte* pixels = bgraPixels)
        {
            // SDL_PIXELFORMAT_ARGB8888 (0x16362004): memory order B,G,R,A — exactly WPF Bgra32.
            const uint Argb8888 = 0x16362004;
            Silk.NET.SDL.Surface* surface = SdlApi.CreateSurfaceFrom(width, height, (Silk.NET.SDL.PixelFormat)Argb8888, pixels, stride);
            if (surface is null)
            {
                return;
            }

            try
            {
                _ = SdlApi.SetWindowIcon(_window, surface);
            }
            finally
            {
                SdlApi.DestroySurface(surface);
            }
        }
    }

    /// <summary>
    /// Shows or hides the compositor decorations. A borderless window relies on
    /// <see cref="SetHitTest"/> for the drag and resize regions.
    /// </summary>
    public void SetBordered(bool bordered)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _ = SdlApi.SetWindowBordered(_window, (byte)(bordered ? 1 : 0));
    }

    /// <summary>
    /// Installs a hit-test resolver: SDL asks the callback what a window region means
    /// (drag, resize edges, normal) so a borderless window keeps compositor-native
    /// moving and resizing. <see langword="null"/> disables hit-testing. The callback
    /// must be cheap (the docs demand no allocations — it fires on pointer motion).
    /// </summary>
    public unsafe void SetHitTest(Func<Point, HitTestRegion>? resolver)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _hitTestResolver = resolver;
        _ = SdlApi.SetWindowHitTest(_window, resolver is null ? default : HitTestHandle, (void*)GCHandle.ToIntPtr(_selfHandle));
    }

    /// <summary>
    /// Switches the window to a system cursor. <see langword="null"/> hides the cursor.
    /// SDL3 applies the active cursor to the window under the mouse, so popups share the
    /// same cursor as the window they hover.
    /// </summary>
    public void SetCursor(SystemCursorKind? kind)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (kind is null)
        {
            _ = SdlApi.HideCursor();
            return;
        }

        if (!_cursors.TryGetValue(kind.Value, out Silk.NET.SDL.CursorHandle cursor))
        {
            cursor = SdlApi.CreateSystemCursor(ToSdlSystemCursor(kind.Value));
            if (cursor.Equals(default))
            {
                return;
            }

            _cursors[kind.Value] = cursor;
        }

        _ = SdlApi.ShowCursor();
        _ = SdlApi.SetCursor(cursor);
    }

    private static Silk.NET.SDL.HitTest HitTestHandle
    {
        get
        {
            unsafe
            {
                return new Silk.NET.SDL.HitTest(&HandleHitTest);
            }
        }
    }


    [UnmanagedCallersOnly]
    private static unsafe Silk.NET.SDL.HitTestResult HandleHitTest(SilkWindow window, Silk.NET.SDL.Point* area, void* data)
    {
        _ = window;
        Func<Point, HitTestRegion>? resolver =
            data is null ? null : ((SdlWindow)GCHandle.FromIntPtr((nint)data).Target!)._hitTestResolver;
        return resolver is null
            ? Silk.NET.SDL.HitTestResult.Normal
            : (Silk.NET.SDL.HitTestResult)resolver(new Point(area->X, area->Y));
    }

    private static Silk.NET.SDL.SystemCursor ToSdlSystemCursor(SystemCursorKind kind)
    {
        return kind switch
        {
            SystemCursorKind.Default => Silk.NET.SDL.SystemCursor.Default,
            SystemCursorKind.Text => Silk.NET.SDL.SystemCursor.Text,
            SystemCursorKind.Wait => Silk.NET.SDL.SystemCursor.Wait,
            SystemCursorKind.Crosshair => Silk.NET.SDL.SystemCursor.Crosshair,
            SystemCursorKind.Progress => Silk.NET.SDL.SystemCursor.Progress,
            SystemCursorKind.ResizeNwse => Silk.NET.SDL.SystemCursor.NwseResize,
            SystemCursorKind.ResizeNesw => Silk.NET.SDL.SystemCursor.NeswResize,
            SystemCursorKind.ResizeEw => Silk.NET.SDL.SystemCursor.EwResize,
            SystemCursorKind.ResizeNs => Silk.NET.SDL.SystemCursor.NsResize,
            SystemCursorKind.ResizeN => Silk.NET.SDL.SystemCursor.NResize,
            SystemCursorKind.ResizeNe => Silk.NET.SDL.SystemCursor.NeResize,
            SystemCursorKind.ResizeE => Silk.NET.SDL.SystemCursor.EResize,
            SystemCursorKind.ResizeSe => Silk.NET.SDL.SystemCursor.SeResize,
            SystemCursorKind.ResizeS => Silk.NET.SDL.SystemCursor.SResize,
            SystemCursorKind.ResizeSw => Silk.NET.SDL.SystemCursor.SwResize,
            SystemCursorKind.ResizeW => Silk.NET.SDL.SystemCursor.WResize,
            SystemCursorKind.ResizeNw => Silk.NET.SDL.SystemCursor.NwResize,
            SystemCursorKind.Move => Silk.NET.SDL.SystemCursor.Move,
            SystemCursorKind.NotAllowed => Silk.NET.SDL.SystemCursor.NotAllowed,
            SystemCursorKind.Hand => Silk.NET.SDL.SystemCursor.Pointer,
            _ => Silk.NET.SDL.SystemCursor.Default,
        };
    }

    /// <summary>
    /// Requests a window position. Coordinates are screen coordinates; for popup windows
    /// SDL wants them relative to the parent, so they are translated here. Wayland (and the
    /// offscreen driver) refuse positioning for regular windows; managed
    /// <see cref="Position"/> still updates.
    /// </summary>
    public void SetPosition(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.SetWindowPosition(_window, ToPopupOffset(x, isX: true), ToPopupOffset(y, isX: false)) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }

        Position = new Point(x, y);
    }

    public void SetPixelSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!SdlApi.SetWindowSize(_window, width, height) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }

        PixelSize = new PixelSize(width, height);
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.ShowWindow(_window))
        {
            throw Error();
        }
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.HideWindow(_window))
        {
            throw Error();
        }
    }

    /// <summary>
    /// Raises the window above other windows and requests input focus. A no-op when the video
    /// driver does not implement it (e.g. the offscreen driver used by tests).
    /// </summary>
    public void BringToFront()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.RaiseWindow(_window) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }
    }

    /// <summary>
    /// Requests that the window be minimized to an iconic representation. A no-op when the video
    /// driver does not implement it (e.g. the offscreen driver used by tests).
    /// </summary>
    public void Minimize()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.MinimizeWindow(_window) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }
    }

    /// <summary>
    /// Requests that the window be made as large as possible. A no-op when the video driver does
    /// not implement it (e.g. the offscreen driver used by tests).
    /// </summary>
    public void Maximize()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.MaximizeWindow(_window) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }
    }

    /// <summary>
    /// Requests that the size and position of a minimized or maximized window be restored. A no-op
    /// when the video driver does not implement it (e.g. the offscreen driver used by tests).
    /// </summary>
    public void Restore()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.RestoreWindow(_window) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }
    }

    /// <summary>
    /// Sets the parent (owner) of this window; <c>null</c> unparents it. Maps to
    /// <c>SDL_SetWindowParent</c>, the SDL3 analog of Win32 <c>GWL_HWNDPARENT</c>: a regular window
    /// becomes a child of the parent in the WM's sense (hidden/shown with it, taskbar grouping).
    /// Popup windows cannot change parents. A no-op when the video driver does not implement it
    /// (e.g. the offscreen driver used by tests).
    /// </summary>
    public void SetParent(SdlWindow? parent)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (!SdlApi.SetWindowParent(_window, parent?._window ?? default) && !IsUnsupported(out SdlException error))
        {
            throw error;
        }
    }

    /// <summary>
    /// The native parent window, or <see cref="WindowHandle.Invalid"/> when the window is
    /// unparented or the video driver does not track parents (e.g. offscreen).
    /// </summary>
    public WindowHandle GetParent()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return new WindowHandle(ToNativeInt(SdlApi.GetWindowParent(_window)));
    }

    /// <summary>
    /// Captures or releases the mouse (<c>SDL_CaptureMouse</c>). While captured, mouse
    /// events keep being delivered to this window even when the pointer is outside it,
    /// which WPF menus rely on for click-outside-to-dismiss. A no-op when the video
    /// driver does not implement it (e.g. offscreen).
    /// </summary>
    public void CaptureMouse(bool capture)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        bool engaged = SdlApi.CaptureMouse(capture ? (byte)1 : (byte)0) != 0;
        if (!engaged && !IsUnsupported(out SdlException error))
        {
            throw error;
        }

        // Remember whether THIS window engaged the (process-global) SDL mouse capture so
        // Dispose can release it before the window is destroyed. Only record a successful
        // call: a driver that rejects capture (e.g. offscreen, or popup windows on some
        // backends) never engages it, so there is nothing to release later.
        if (engaged)
        {
            _mouseCaptured = capture;
        }
    }

    /// <summary>
    /// Creates the window with <c>SDL_CreatePopupWindow</c> (offset relative to the parent
    /// origin). Returns false when the driver does not support popup windows, leaving
    /// <see cref="_window"/> default so the caller can fall back.
    /// </summary>
    private bool CreatePopup(WindowOptions options, ulong flags)
    {
        if (options.Parent is null)
        {
            return false;
        }

        flags |= options.Popup == PopupKind.PopupMenu ? PopupMenuFlag : TooltipFlag;
        SilkWindow window = SdlApi.CreatePopupWindow(
            options.Parent._window,
            options.X,
            options.Y,
            options.Size.Width,
            options.Size.Height,
            flags);
        if (window == default)
        {
            return false;
        }

        _window = window;
        return true;
    }

    internal void RefreshMetrics()
    {
        PixelSize = QueryPixelSize();
        Position = QueryPosition();
        DisplayScale = QueryDisplayScale();
    }

    private Point QueryPosition()
    {
        int x = 0;
        int y = 0;
        if (!SdlApi.GetWindowPosition(_window, new Ref<int>(ref x), new Ref<int>(ref y)))
        {
            throw Error();
        }

        // Popup positions are parent-relative; convert back to screen coordinates so the
        // managed Position contract is uniform.
        if (IsPopup && Options.Parent is { } parent)
        {
            x += (int)parent.Position.X;
            y += (int)parent.Position.Y;
        }

        return new Point(x, y);
    }

    private int ToPopupOffset(int screenValue, bool isX)
    {
        return !IsPopup || Options.Parent is not { } parent
            ? screenValue
            : isX
                ? screenValue - (int)parent.Position.X
                : screenValue - (int)parent.Position.Y;
    }
    private PixelSize QueryPixelSize()
    {
        int width = 0;
        int height = 0;
        return SdlApi.GetWindowSizeInPixels(_window, new Ref<int>(ref width), new Ref<int>(ref height))
            ? new PixelSize(width, height)
            : throw Error();
    }

    private double QueryDisplayScale()
    {
        float scale = SdlApi.GetWindowDisplayScale(_window);
        return scale > 0 ? scale : 1;
    }

    private unsafe SurfaceHandle CreateSurfaceNative(InstanceHandle instance)
    {
        ulong surface = 0;
        return SdlApi.VulkanCreateSurface(_window, (void*)instance.Value, null, &surface) != 0
            ? new SurfaceHandle(surface)
            : throw Error();
    }

    private static unsafe SilkWindow CreateWindowNative(string title, int width, int height, ulong flags)
    {
        nint titlePointer = Marshal.StringToCoTaskMemUTF8(title);
        try
        {
            SilkWindow window = SdlApi.CreateWindow((sbyte*)titlePointer, width, height, flags);
            return window == default ? throw Error() : window;
        }
        finally
        {
            Marshal.FreeCoTaskMem(titlePointer);
        }
    }

    private static unsafe string[] QueryRequiredExtensions()
    {
        uint count = 0;
        Ptr2D<sbyte> extensions = SdlApi.VulkanGetInstanceExtensions(new Ref<uint>(ref count));
        if (extensions.Native == null)
        {
            throw Error();
        }

        string[] names = new string[count];
        Span<Ptr<sbyte>> pointers = extensions.AsSpan((int)count);
        for (int i = 0; i < pointers.Length; i++)
        {
            names[i] = Marshal.PtrToStringUTF8((nint)pointers[i].Native) ?? string.Empty;
        }

        return names;
    }

    private static unsafe nint ToNativeInt(SilkWindow window)
    {
        return (nint)window.Handle;
    }

    /// <summary>
    /// True when the video driver / compositor refuses an optional window op.
    /// Offscreen reports "not supported"; Wayland reports "cannot position"
    /// for <c>SDL_SetWindowPosition</c> on non-popup windows.
    /// </summary>
    private static bool IsUnsupported(out SdlException error)
    {
        error = Error();
        string message = error.Message;
        return message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot position", StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe SdlException Error()
    {
        Ptr<sbyte> error = SdlApi.GetError();
        string message = error.Native == null ? "SDL operation failed." : Marshal.PtrToStringUTF8((nint)error.Native) ?? "SDL operation failed.";
        return new SdlException(message);
    }
}
