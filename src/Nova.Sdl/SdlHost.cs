using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.DesktopTheme;
using Nova.SystemTheme;
using Silk.NET.Core;
using Silk.NET.SDL;
using Point = Nova.Geometry.Point;
using Size = Nova.Geometry.Size;
using Vector = Nova.Geometry.Vector;
using SdlApi = Silk.NET.SDL.Sdl;
using SilkWindow = Silk.NET.SDL.WindowHandle;

namespace Nova.Sdl;

/// <summary>
/// Process-wide SDL3 lifetime. All Silk.NET.SDL unsafe calls stay in this assembly.
/// Not thread-safe: create, poll and dispose on a single thread (SDL_Init/Quit must run on the main thread).
/// </summary>
[PublicAPI]
public sealed class SdlHost : IDisposable
{
    /// <summary>SDL_INIT_VIDEO — implies SDL_INIT_EVENTS.</summary>
    private const uint InitVideo = 0x00000020;

    private static int s_instanceCount;

    private static readonly Lock s_wakeGate = new();
    private static uint s_wakeEventType;
    private static bool s_wakeEventRegistered;

    /// <summary>
    /// Wakes a thread blocked in <see cref="WaitEventTimeout"/> by pushing a
    /// registered user event onto the process-global SDL queue. The dispatcher
    /// frame loop uses this to interrupt an idle SDL wait when a cross-thread
    /// operation is enqueued (Linux has no Win32 PostMessage wakeup). No-op when
    /// SDL is not initialized. The pushed event has no mapping, so the consuming
    /// pump skips it (it is never delivered as input).
    /// </summary>
    public static void PushWakeEvent()
    {
        if (s_instanceCount == 0)
        {
            return;
        }

        if (!Volatile.Read(ref s_wakeEventRegistered))
        {
            lock (s_wakeGate)
            {
                if (!s_wakeEventRegistered)
                {
                    s_wakeEventType = SdlApi.RegisterEvents(1);
                    Volatile.Write(ref s_wakeEventRegistered, true);
                }
            }
        }

        var raw = new Event { Type = s_wakeEventType };
        _ = SdlApi.PushEvent(new Ref<Event>(ref raw));
    }

    private readonly List<SdlWindow> _windows = [];
    private readonly Dictionary<uint, SdlWindow> _windowsById = [];
    private Point _lastMousePosition;
    private SdlWindow? _lastMouseWindow;
    private int _disposed;

    public SdlHost()
    {
        if (Interlocked.Increment(ref s_instanceCount) == 1 && !SdlApi.Init(InitVideo))
        {
            _ = Interlocked.Decrement(ref s_instanceCount);
            throw Error();
        }

        IsInitialized = true;

        // Desktop palette opt-in (NOVA_PALETTE=desktop): decorates the SDL metrics provider
        // with colors/fonts/DPI from the DE sources; with the opt-in off this is a no-op.
        HostTheme.SetProvider(DesktopThemeApplier.Apply(new SdlHostMetrics()));
    }

    public bool IsInitialized { get; private set; }

    public SdlWindow CreateWindow(WindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var window = new SdlWindow(options);
        _windows.Add(window);
        _windowsById.Add(window.SdlId, window);
        window.Host = this;
        return window;
    }

    /// <summary>
    /// Current mouse position in screen coordinates (device pixels), derived from the
    /// LAST reported mouse event: its window-relative coordinates plus that window's
    /// screen origin. Wayland has no global-cursor query — <c>SDL_GetGlobalMouseState</c>
    /// returns (0,0) until some window has pointer focus (and a faked window-position
    /// offset even then), so the first popup opened on a fresh window placed at the
    /// origin. Deriving from the last event is focus-independent and yields exactly the
    /// value SDL synthesizes for its "global" query once focus exists — the second
    /// right-click's correct placement, made deterministic for the first. Falls back to
    /// <c>SDL_GetGlobalMouseState</c> only before the first mouse event has arrived
    /// (X11/offscreen, where the query is meaningful; the offscreen driver reports the
    /// origin).
    /// </summary>
    public Point GetGlobalMousePosition()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_lastMouseWindow is { } window)
        {
            return new Point(_lastMousePosition.X + window.Position.X, _lastMousePosition.Y + window.Position.Y);
        }

        float x = 0;
        float y = 0;
        _ = SdlApi.GetGlobalMouseState(new Ref<float>(ref x), new Ref<float>(ref y));
        return new Point(x, y);
    }

    /// <summary>
    /// True when any window created through this host currently holds the keyboard focus
    /// (<c>SDL_GetKeyboardFocus</c>). This is the authoritative answer to "is our
    /// PROCESS the active application?" — per-window focus events are NOT: a context-menu
    /// popup taking focus from the main window (both ours) must not read as the app losing
    /// activation. Returns false while the focused window is another application's.
    /// </summary>
    public bool HasKeyboardFocus()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return SdlApi.GetKeyboardFocus() != default;
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the system clipboard (SDL_SetClipboardText, UTF-8).
    /// The WPF Clipboard's Linux branch calls this in place of the Win32 OLE clipboard.
    /// </summary>
    public static void SetClipboardText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        unsafe
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(text);
            fixed (byte* utf8Ptr = utf8)
            {
                _ = SdlApi.SetClipboardText((sbyte*)utf8Ptr);
            }
        }
    }

    /// <summary>
    /// Reads the system clipboard as text (SDL_GetClipboardText, UTF-8). Returns an empty
    /// string when the clipboard is empty or held by another process.
    /// </summary>
    public static string GetClipboardText()
    {
        unsafe
        {
            sbyte* text = SdlApi.GetClipboardTextRaw();
            if (text == default)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;
            }
            finally
            {
                SdlApi.Free(text);
            }
        }
    }

    private sealed record ClipboardEntry(string Mime, byte[] Data, GCHandle Pin);

    private sealed class ClipboardClaim
    {
        public long Token;
        public ClipboardEntry[] Entries = [];
        public GCHandle[] MimePins = [];
    }

    private static readonly Lock s_clipboardGate = new();
    private static long s_clipboardToken;
    private static ClipboardEntry[]? s_clipboardEntries;
    private static readonly List<ClipboardClaim> s_clipboardClaims = [];

    [UnmanagedCallersOnly]
    private static unsafe void* ClipboardDataCallback(void* userdata, sbyte* mimeType, nuint* size)
    {
        if (mimeType is null || size is null)
        {
            return null;
        }

        _ = userdata;
        string mime = Marshal.PtrToStringUTF8((nint)mimeType) ?? string.Empty;
        lock (s_clipboardGate)
        {
            if (s_clipboardEntries is { } entries)
            {
                foreach (ref readonly ClipboardEntry entry in entries.AsSpan())
                {
                    if (string.Equals(entry.Mime, mime, StringComparison.Ordinal))
                    {
                        // SDL3 frees the returned pointer (SDL_free == libc free), so it
                        // must come from the native heap, not from a pinned GC buffer.
                        byte[] data = entry.Data;
                        void* copy = NativeMemory.Alloc((nuint)data.Length);
                        data.CopyTo(new Span<byte>(copy, data.Length));
                        *size = (nuint)data.Length;
                        return copy;
                    }
                }
            }
        }

        return null;
    }

    [UnmanagedCallersOnly]
    private static unsafe void ClipboardCleanupCallback(void* userdata)
    {
        // SDL3 may defer the cleanup of a replaced claim until after a newer claim
        // has gone live, so the token gates only the store clear; the pins belong
        // to the claim and are freed here exactly once per claim.
        if (userdata is null)
        {
            return;
        }

        GCHandle tokenPin = GCHandle.FromIntPtr((nint)userdata);
        long token = (long)tokenPin.Target!;
        lock (s_clipboardGate)
        {
            int claimIndex = s_clipboardClaims.FindIndex(claim => claim.Token == token);
            if (claimIndex >= 0)
            {
                ClipboardClaim claim = s_clipboardClaims[claimIndex];
                foreach (ref readonly ClipboardEntry entry in claim.Entries.AsSpan())
                {
                    if (entry.Pin.IsAllocated)
                    {
                        entry.Pin.Free();
                    }
                }

                foreach (GCHandle mimePin in claim.MimePins)
                {
                    if (mimePin.IsAllocated)
                    {
                        mimePin.Free();
                    }
                }

                s_clipboardClaims.RemoveAt(claimIndex);
            }

            if (token == s_clipboardToken)
            {
                s_clipboardEntries = null;
            }
        }

        tokenPin.Free();
    }

    /// <summary>
    /// Offers <paramref name="data"/> to the system clipboard under the given mime types
    /// (SDL_SetClipboardData). The set replaces any previous clipboard content. SDL copies
    /// each buffer when a consumer requests its mime, so the buffers stay pinned until the
    /// clipboard is replaced or the host shuts down. Use standard mime names such as
    /// "text/plain", "image/png" or "text/uri-list".
    /// </summary>
    public static bool SetClipboardData(IReadOnlyDictionary<string, byte[]> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count == 0)
        {
            throw new ArgumentException("At least one mime type is required.", nameof(data));
        }

        ClipboardEntry[] entries = new ClipboardEntry[data.Count];
        int index = 0;
        foreach ((string mime, byte[] bytes) in data)
        {
            ArgumentException.ThrowIfNullOrEmpty(mime);
            if (bytes.Length == 0)
            {
                throw new ArgumentException($"Clipboard data for '{mime}' must not be empty.", nameof(data));
            }

            entries[index++] = new ClipboardEntry(mime, bytes, GCHandle.Alloc(bytes, GCHandleType.Pinned));
        }

        long token;
        lock (s_clipboardGate)
        {
            token = ++s_clipboardToken;
            s_clipboardEntries = entries;
        }

        lock (s_clipboardGate)
        {
            s_clipboardClaims.Add(new ClipboardClaim { Token = token, Entries = entries });
        }

        GCHandle tokenPin = GCHandle.Alloc(token, GCHandleType.Pinned);

        unsafe
        {
            // SDL_SetClipboardData takes a null-terminated mime list; the pins live only
            // for the duration of the call (SDL copies the list).
            sbyte*[] pointers = new sbyte*[entries.Length];
            GCHandle[] stringPins = new GCHandle[entries.Length];
            byte[][] utf8 = new byte[entries.Length][];
            for (int i = 0; i < entries.Length; i++)
            {
                utf8[i] = System.Text.Encoding.UTF8.GetBytes(entries[i].Mime + '\0');
                stringPins[i] = GCHandle.Alloc(utf8[i], GCHandleType.Pinned);
                pointers[i] = (sbyte*)stringPins[i].AddrOfPinnedObject();
            }

            fixed (sbyte** mimeList = pointers)
            {
                byte ok = SdlApi.SetClipboardData(
                    new ClipboardDataCallback((delegate* unmanaged<void*, sbyte*, nuint*, void*>)&ClipboardDataCallback),
                    new ClipboardCleanupCallback((delegate* unmanaged<void*, void>)&ClipboardCleanupCallback),
                    userdata: (void*)GCHandle.ToIntPtr(tokenPin),
                    mimeList,
                    (nuint)entries.Length);
                // SDL may read the mime list lazily, so its pins outlive this call
                // and are released with the claim.
                lock (s_clipboardGate)
                {
                    s_clipboardClaims[^1].MimePins = stringPins;
                }

                if (ok == 0)
                {
                    tokenPin.Free();
                    lock (s_clipboardGate)
                    {
                        _ = s_clipboardClaims.RemoveAll(claim => claim.Token == token);
                        s_clipboardEntries = null;
                    }

                    return false;
                }

                return true;
            }
        }
    }

    /// <summary>
    /// Reads the system clipboard under <paramref name="mimeType"/> (SDL_GetClipboardData).
    /// Returns null when the clipboard is empty, held by another process, or lacks the mime.
    /// </summary>
    public static byte[]? GetClipboardData(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        unsafe
        {
            fixed (byte* utf8Ptr = System.Text.Encoding.UTF8.GetBytes(mimeType + '\0'))
            {
                nuint size = 0;
                void* data = SdlApi.GetClipboardData((sbyte*)utf8Ptr, &size);
                if (data is null)
                {
                    return null;
                }

                try
                {
                    byte[] result = new byte[(int)size];
                    fixed (byte* dst = result)
                    {
                        Buffer.MemoryCopy(data, dst, (long)size, (long)size);
                    }

                    return result;
                }
                finally
                {
                    SdlApi.Free(data);
                }
            }
        }
    }

    /// <summary>
    /// Reports whether the system clipboard currently carries <paramref name="mimeType"/>
    /// (SDL_HasClipboardData).
    /// </summary>
    public static bool HasClipboardData(string mimeType)
    {
        ArgumentNullException.ThrowIfNull(mimeType);
        unsafe
        {
            fixed (byte* utf8Ptr = System.Text.Encoding.UTF8.GetBytes(mimeType + '\0'))
            {
                return SdlApi.HasClipboardData((sbyte*)utf8Ptr) != 0;
            }
        }
    }

    /// <summary>
    /// Pumps the SDL event queue and maps the next event to <see cref="SdlEvent"/>. Returns
    /// <c>false</c> only when the queue is empty. Events with no <see cref="SdlEventKind"/>
    /// mapping (e.g. MouseAdded, KeyboardAdded, ClipboardUpdate) are consumed and skipped
    /// so later queued input is not truncated by an unmapped event. Sign-agnostic deltas
    /// (mouse motion, wheel) are clamped to non-negative values because <see cref="Size"/>
    /// forbids negative components.
    /// </summary>
    public bool Poll(out SdlEvent ev)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var raw = new Event();
        while (SdlApi.PollEvent(new Ref<Event>(ref raw)))
        {
            if (MapOne(raw, out ev))
            {
                return true;
            }
        }

        ev = default;
        return false;
    }

    /// <summary>
    /// Blocks for the next SDL event, then maps it like <see cref="Poll"/>. A
    /// negative <paramref name="timeoutMs"/> waits indefinitely (until an event —
    /// including a pushed wake event — arrives). Returns <c>false</c> when the
    /// wait timed out without an event. Used by the dispatcher message loop to
    /// replace <c>GetMessageW</c>'s blocking wait without busy-polling.
    /// </summary>
    public bool WaitEventTimeout(int timeoutMs, out SdlEvent ev)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ev = default;

        var raw = new Event();
        if (timeoutMs < 0)
        {
            if (!SdlApi.WaitEvent(new Ref<Event>(ref raw)))
            {
                return false;
            }
        }
        else if (!SdlApi.WaitEventTimeout(new Ref<Event>(ref raw), timeoutMs))
        {
            return false;
        }

        // The first raw event did not map (or was stale, e.g. a wake event):
        // drain the rest without blocking so a mapped event is not lost.
        return MapOne(raw, out ev) || Poll(out ev);
    }

    private bool MapOne(in Event raw, out SdlEvent ev)
    {
        var type = (EventType)raw.Type;
        if (type is >= EventType.WindowFirst and <= EventType.WindowLast
            && _windowsById.TryGetValue(raw.Window.WindowID, out SdlWindow? window)
            && type is EventType.WindowResized or EventType.WindowPixelSizeChanged or EventType.WindowDisplayScaleChanged)
        {
            window.RefreshMetrics();
        }

        SdlEvent? mapped = MapEvent(raw);
        if (mapped is null)
        {
            ev = default;
            return false;
        }

        // Track the last mouse position (window-relative device px) and the window it
        // was reported in, so GetGlobalMousePosition can derive a screen point without
        // SDL_GetGlobalMouseState (unreliable on Wayland — see its doc comment). Only
        // live windows are recorded: UnregisterWindow removes destroyed windows, so a
        // stale event for a gone window never updates the last position.
        if (mapped.Value is { Kind: SdlEventKind.MouseMoved or SdlEventKind.MouseButtonDown or SdlEventKind.MouseButtonUp or SdlEventKind.MouseWheel } mouseEvent
            && _windowsById.TryGetValue(MouseWindowId(raw, type), out SdlWindow? mouseWindow))
        {
            _lastMousePosition = mouseEvent.Position;
            _lastMouseWindow = mouseWindow;
        }

        // A window-scoped event whose window no longer resolves (SDL_GetWindowFromID
        // returns null because the window was destroyed while the event was queued) is
        // stale and must be dropped here: its window is gone, and delivering it into
        // whichever source polls next would misroute it. The classic casualty is a
        // reopened ContextMenu: the closed menu's queued WindowFocusLost arrives with
        // an invalid window handle, the new popup's pump treats it as its own,
        // WPF translates it to WM_ACTIVATEAPP, and PopupFilterMessage answers by
        // closing the freshly-reopened menu. Quit is the only genuinely windowless
        // event. (Synthetic events constructed by tests with WindowHandle.Invalid
        // bypass Poll entirely — they are dispatched straight into a source.)
        if (mapped.Value.Kind != SdlEventKind.Quit && !mapped.Value.Window.IsValid)
        {
            ev = default;
            return false;
        }

        ev = mapped.Value;
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        while (_windows.Count > 0)
        {
            _windows[0].Dispose();
        }

        if (Interlocked.Decrement(ref s_instanceCount) == 0)
        {
            SdlApi.Quit();
        }

        IsInitialized = false;
    }

    internal void UnregisterWindow(SdlWindow window)
    {
        _ = _windowsById.Remove(window.SdlId);
        _ = _windows.Remove(window);
        window.Host = null;
    }

    private static SdlEvent? MapEvent(in Event raw)
    {
        return raw.Type switch
        {
            (uint)EventType.Quit => SdlEvent.Quit(),
            (uint)EventType.WindowCloseRequested => MapWindowEvent(raw.Window, SdlEventKind.WindowCloseRequested),
            (uint)EventType.WindowMaximized => MapWindowEvent(raw.Window, SdlEventKind.WindowMaximized),
            (uint)EventType.WindowMinimized => MapWindowEvent(raw.Window, SdlEventKind.WindowMinimized),
            (uint)EventType.WindowRestored => MapWindowEvent(raw.Window, SdlEventKind.WindowRestored),
            (uint)EventType.WindowResized => MapWindowEvent(raw.Window, SdlEventKind.WindowResized),
            (uint)EventType.WindowMoved => MapWindowEvent(raw.Window, SdlEventKind.WindowMoved),
            (uint)EventType.WindowFocusGained => MapWindowEvent(raw.Window, SdlEventKind.WindowFocusGained),
            (uint)EventType.WindowFocusLost => MapWindowEvent(raw.Window, SdlEventKind.WindowFocusLost),
            (uint)EventType.WindowExposed => MapWindowEvent(raw.Window, SdlEventKind.WindowExposed),
            (uint)EventType.WindowDisplayChanged => MapWindowEvent(raw.Window, SdlEventKind.WindowDisplayChanged),
            (uint)EventType.MouseMotion => MapMouseEvent(raw.Motion),
            (uint)EventType.MouseButtonDown => MapButtonEvent(raw.Button, SdlEventKind.MouseButtonDown),
            (uint)EventType.MouseButtonUp => MapButtonEvent(raw.Button, SdlEventKind.MouseButtonUp),
            (uint)EventType.MouseWheel => MapWheelEvent(raw.Wheel),
            (uint)EventType.KeyDown => MapKeyEvent(raw.Key, SdlEventKind.KeyDown),
            (uint)EventType.KeyUp => MapKeyEvent(raw.Key, SdlEventKind.KeyUp),
            (uint)EventType.TextInput => MapTextEvent(raw.Text),
            (uint)EventType.DropBegin => MapDropEvent(raw.Drop, SdlEventKind.DropBegin),
            (uint)EventType.DropFile => MapDropEvent(raw.Drop, SdlEventKind.DropFile),
            (uint)EventType.DropText => MapDropEvent(raw.Drop, SdlEventKind.DropText),
            (uint)EventType.DropComplete => MapDropEvent(raw.Drop, SdlEventKind.DropComplete),
            (uint)EventType.DropPosition => MapDropEvent(raw.Drop, SdlEventKind.DropPosition),
            _ => null
        };
    }

    private static SdlEvent MapWindowEvent(in WindowEvent raw, SdlEventKind kind)
    {
        Point position = kind == SdlEventKind.WindowMoved ? new Point(raw.Data1, raw.Data2) : Point.Origin;
        Vector delta = kind == SdlEventKind.WindowResized ? new Vector(raw.Data1, raw.Data2) : Vector.Zero;
        return new SdlEvent(kind, WindowFromId(raw.WindowID), position, delta, default, 0, null);
    }

    /// <summary>
    /// The window ID carried by a mouse-bearing raw event. The event union members
    /// overlap at offset 0, so each member's WindowID field must be read through the
    /// member whose layout matches the event type.
    /// </summary>
    private static uint MouseWindowId(in Event raw, EventType type)
    {
        return (uint)type switch
        {
            (uint)EventType.MouseMotion => raw.Motion.WindowID,
            (uint)EventType.MouseButtonDown or (uint)EventType.MouseButtonUp => raw.Button.WindowID,
            (uint)EventType.MouseWheel => raw.Wheel.WindowID,
            _ => 0
        };
    }

    private static SdlEvent MapMouseEvent(in MouseMotionEvent raw)
    {
        // Signed motion deltas: negative Xrel/Yrel mean left/up and must survive the map —
        // a consumer that integrates relative motion relies on the direction.
        return new SdlEvent(SdlEventKind.MouseMoved, WindowFromId(raw.WindowID), new Point(raw.X, raw.Y), new Vector(raw.Xrel, raw.Yrel), default, 0, null);
    }

    private static SdlEvent MapButtonEvent(in MouseButtonEvent raw, SdlEventKind kind)
    {
        return new SdlEvent(kind, WindowFromId(raw.WindowID), new Point(raw.X, raw.Y), Vector.Zero, (MouseButton)raw.Button, 0, null);
    }

    private static SdlEvent MapWheelEvent(in MouseWheelEvent raw)
    {
        // SDL wheel deltas are signed: Y > 0 scrolls up, Y < 0 scrolls down. Clamping the
        // sign away made wheel-down indistinguishable from no scroll.
        return new SdlEvent(SdlEventKind.MouseWheel, WindowFromId(raw.WindowID), new Point(raw.MouseX, raw.MouseY), new Vector(raw.X, raw.Y), default, 0, null);
    }

    private static SdlEvent MapKeyEvent(in KeyboardEvent raw, SdlEventKind kind)
    {
        return new SdlEvent(kind, WindowFromId(raw.WindowID), Point.Origin, Vector.Zero, default, raw.Key, null);
    }

    private static SdlEvent MapTextEvent(in TextInputEvent raw)
    {
        return new SdlEvent(SdlEventKind.TextInput, WindowFromId(raw.WindowID), Point.Origin, Vector.Zero, default, 0, ReadText(raw));
    }

    /// <summary>Maps an SDL drop event (file path / text / position / batch boundary):
    /// the payload lands in the event's Text field, the drop position in Position.</summary>
    private static unsafe SdlEvent MapDropEvent(in DropEvent raw, SdlEventKind kind)
    {
        return new SdlEvent(kind, WindowFromId(raw.WindowID), new Point(raw.X, raw.Y), Vector.Zero, default, 0, ReadUtf8(raw.Data));
    }

    private static unsafe string? ReadUtf8(sbyte* text)
    {
        return text == null ? null : Marshal.PtrToStringUTF8((nint)text);
    }

    private static unsafe WindowHandle WindowFromId(uint id)
    {
        SilkWindow window = SdlApi.GetWindowFromID(id);
        return new WindowHandle((nint)window.Handle);
    }

    private static unsafe string? ReadText(in TextInputEvent raw)
    {
        return raw.Text == null ? null : Marshal.PtrToStringUTF8((nint)raw.Text);
    }

    private static unsafe SdlException Error()
    {
        Ptr<sbyte> error = SdlApi.GetError();
        string message = error.Native == null ? "SDL operation failed." : Marshal.PtrToStringUTF8((nint)error.Native) ?? "SDL operation failed.";
        return new SdlException(message);
    }
}
