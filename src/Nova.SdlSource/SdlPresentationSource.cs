using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JetBrains.Annotations;
using Nova.Geometry;
using Nova.Host;
using Nova.Mil;
using Nova.Sdl;
using Nova.Vulkan;

namespace Nova.SdlSource;

/// <summary>
/// SDL3 <see cref="PresentationSource"/>. Constructed from
/// <see cref="HwndSourceParameters"/> so <c>Window.CreateSourceWindow</c>
/// can swap <c>new HwndSource</c> for this type.
/// </summary>
/// <summary>
/// The chrome hit-test classification for a window point, mirroring the SDL hit-test
/// regions (drag, the 8 resize edges/corners, normal). Declared here so PresentationFramework
/// (which cannot reference Nova.Sdl under Arcade's disabled transitive project refs) can
/// drive <see cref="SdlPresentationSource.ConfigureChrome"/>.
/// </summary>
public enum ChromeHitTestRegion
{
    Normal = 0,
    Draggable = 1,
    ResizeTopLeft = 2,
    ResizeTop = 3,
    ResizeTopRight = 4,
    ResizeRight = 5,
    ResizeBottomRight = 6,
    ResizeBottom = 7,
    ResizeBottomLeft = 8,
    ResizeLeft = 9,
}

[PublicAPI]
public sealed class SdlPresentationSource : PresentationSource, IDisposable, IMouseInputProvider, IKeyboardInputProvider
{
    private const int WM_ACTIVATEAPP = 0x001C;
    private const int SdlScanCodeEscape = 41;

    private SizeToContent StoredSizeToContent { get; set; } = SizeToContent.Manual;

    private InputProviderSite? InputSite { get; set; }

    private readonly List<HwndSourceHook> _hooks = [];
    private List<string>? _pendingDropFiles;
    private IntraAppDragContext? _intraAppDrag;

    private DpiScale _currentDpiScale = new(1, 1);

    private System.Windows.Size? _lastAutoResizedDeviceSize;

    private static readonly Lock s_registryGate = new();
    private static readonly Dictionary<IntPtr, SdlPresentationSource> s_sourcesByWindow = [];

    // Process-wide application activation (WM_ACTIVATEAPP semantics): true while some
    // window of this process holds keyboard focus. Driven by SDL focus events + the
    // SDL_GetKeyboardFocus query — see the WindowFocusGained/Lost dispatch cases.
    private static bool s_appActivated;

    // Debounced deactivation (see WindowFocusLost): a pending check that no window of
    // this process holds focus. Cancelled by any WindowFocusGained; fires ~100ms after
    // the last FOCUS_LOST if focus genuinely left the process (a real app switch).
    private static DispatcherTimer? s_deactivationTimer;
    private static SdlPresentationSource? s_deactivationSource;

    static SdlPresentationSource()
    {
        // Register the Linux dispatcher message-loop hooks once. WindowsBase's
        // Dispatcher cannot reference Nova.SdlSource (reference cycle), so the
        // step (block on SDL + dispatch), the present action (render every
        // attached frame), and the wake signal (SDL user event pushed on
        // cross-thread BeginInvoke) are injected at type-load. Until this type
        // loads (the first window is created), the Dispatcher falls back to its
        // BCL wake event, which is fine — the window is created by a queued
        // operation before the first pump matters.
        if (!OperatingSystem.IsWindows())
        {
            Dispatcher.RegisterLinuxEventLoop(
                static timeoutMs => PumpStep(timeoutMs),
                static () => PresentAll(),
                static () => SdlHost.PushWakeEvent());

            RegisterDragLoop();
        }
    }

    // The managed intra-app drag loop (DragDrop.DoDragDrop on Linux, patch
    // 0050): PresentationCore cannot reference Nova.SdlSource, so the loop is
    // injected here.
    private static void RegisterDragLoop()
    {
        System.Windows.DragDrop.LinuxDragLoop = RunManagedDragLoop;
    }

    private static DragDropEffects RunManagedDragLoop(DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects)
    {
        return PresentationSource.FromDependencyObject(dragSource) is not SdlPresentationSource source ||
               source.CompositionTarget.RootVisual is not System.Windows.UIElement root
            ? DragDropEffects.None
            : source.RunIntraAppDragLoop(root, dragSource, data, allowedEffects);
    }

    private DragDropEffects RunIntraAppDragLoop(System.Windows.UIElement root, DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects)
    {
        var frame = new DispatcherFrame();
        var context = new IntraAppDragContext(this, root, dragSource, data, allowedEffects, frame);
        _intraAppDrag = context;
        try
        {
            // Re-enter the dispatcher message loop (the registered PumpStep) for
            // the drag: subsequent SDL mouse events arrive at Dispatch and are
            // routed to the active context instead of the input pipeline.
            Dispatcher.PushFrame(frame);
            return context.Effect;
        }
        finally
        {
            _intraAppDrag = null;
        }
    }

    public SdlPresentationSource(HwndSourceParameters parameters)
        : this(CreateWindowFrame(parameters))
    {
        // The Window puts its WindowFilterMessage (WM_CLOSE → Closing/Close) into the
        // parameters; HwndSource registers it during creation. The SDL source must do
        // the same or the close-requested hook chain is empty and the compositor's
        // close button never reaches the WPF window.
        if (parameters.HwndSourceHook is { } hook)
        {
            AddHook(hook);
        }

        if (!parameters.HasAssignedSize)
        {
            // Mirror HwndSource: a source created without an assigned size sizes itself to
            // its root visual and raises AutoResized as layout changes it.
            StoredSizeToContent = SizeToContent.WidthAndHeight;
        }
    }

    /// <summary>
    /// Creates an SDL popup window (menu or tooltip) parented to <paramref name="owner"/>'s
    /// window, sharing its SDL host and Vulkan device. Used by <c>PopupSecurityHelper</c>
    /// on Linux in place of <c>new HwndSource(param)</c>. The <c>bool</c> overload exists so
    /// PresentationFramework (which cannot reference <c>Nova.Sdl</c> under Arcade's disabled
    /// transitive project references) can build popups without naming <see cref="PopupKind"/>.
    /// </summary>
    /// <param name="parameters">Popup window parameters; <c>PositionX/PositionY</c> are screen coordinates.</param>
    /// <param name="owner">The presentation source of the window the popup is attached to (placement target's source).</param>
    /// <param name="tooltip"><c>true</c> for a tooltip popup (input pass-through), <c>false</c> for a menu popup.</param>
    public SdlPresentationSource(HwndSourceParameters parameters, SdlPresentationSource owner, bool tooltip)
        : this(parameters, owner, tooltip ? PopupKind.Tooltip : PopupKind.PopupMenu)
    {
    }

    /// <summary>
    /// Creates an SDL popup window (menu or tooltip) parented to <paramref name="owner"/>'s
    /// window, sharing its SDL host and Vulkan device. Used by <c>PopupSecurityHelper</c>
    /// on Linux in place of <c>new HwndSource(param)</c>.
    /// </summary>
    /// <param name="parameters">Popup window parameters; <c>PositionX/PositionY</c> are screen coordinates.</param>
    /// <param name="owner">The presentation source of the window the popup is attached to (placement target's source).</param>
    /// <param name="popupKind">Popup window kind; <see cref="PopupKind.Tooltip"/> is inherently input pass-through.</param>
    public SdlPresentationSource(HwndSourceParameters parameters, SdlPresentationSource owner, PopupKind popupKind)
        : this(CreatePopupFrame(parameters, owner, popupKind))
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
        if (parameters.HwndSourceHook is { } hook)
        {
            AddHook(hook);
        }

        if (!parameters.HasAssignedSize)
        {
            StoredSizeToContent = SizeToContent.WidthAndHeight;
        }
    }

    internal SdlPresentationSource(CompositionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Frame = frame;
        CompositionTarget = new SdlCompositionTarget(Frame);
        Handle = Frame.Window?.Handle.Value ?? IntPtr.Zero;
        if (Handle != IntPtr.Zero)
        {
            RegisterSource(this);
        }

        IsActive = Frame.Window is not null;
        AddSource();
        InputSite = InputManager.Current.RegisterInputProvider(this);

    }

    /// <summary>
    /// Backing composition frame. Internal so PresentationFramework does not need a
    /// <c>Nova.Host</c> reference (Arcade disables transitive project refs).
    /// </summary>
    internal CompositionFrame Frame { get; private set; }

    /// <summary>
    /// The source this popup is parented to, or <c>null</c> for a top-level source.
    /// </summary>
    public SdlPresentationSource? Owner { get; }

    public IntPtr Handle { get; private set; }

    public bool IsActive { get; private set; }

    public int PixelLeft => Frame.Window is { } window ? (int)window.Position.X : 0;

    public int PixelTop => Frame.Window is { } window ? (int)window.Position.Y : 0;

    public int PixelWidth => Frame.Window?.PixelSize.Width ?? 0;

    public int PixelHeight => Frame.Window?.PixelSize.Height ?? 0;

    /// <summary>
    /// Whether this source's window was created with per-pixel opacity
    /// (<c>HwndSourceParameters.UsesPerPixelOpacity</c>, set by
    /// <c>Window.AllowsTransparency</c>): the SDL window carries
    /// <c>SDL_WINDOW_TRANSPARENT</c> and the swapchain composites with a non-opaque
    /// (premultiplied) alpha mode. Mirrors <see cref="HwndSource.UsesPerPixelOpacity"/>.
    /// </summary>
    public bool UsesPerPixelOpacity => Frame.Window?.Options.Transparent ?? false;

    public void SetBounds(int x, int y, int width, int height, bool move, bool resize)
    {
        SdlWindow? window = Frame.Window;
        if (window is null)
        {
            return;
        }

        if (move)
        {
            window.SetPosition(x, y);
        }

        if (resize && width > 0 && height > 0)
        {
            window.SetPixelSize(width, height);
            // The swapchain presenter tracks the window size; without this the presenter
            // keeps the size it was created at (1×1 for popups, whose HwndSourceParameters
            // default width/height is 1). The main window relies on SDL WindowResized
            // events; popups are positioned programmatically via SetBounds.
            Frame.ResizePresenter(window.PixelSize);
            if (IsLayoutActive() && StoredSizeToContent != SizeToContent.WidthAndHeight)
            {
                SetLayoutSize();
            }
        }
    }

    public void Show()
    {
        Frame.Window?.Show();
    }

    /// <summary>
    /// Registers a virtual message hook, mirroring <c>HwndSource.AddHook</c>. Hooks are
    /// invoked in reverse registration order when a window event is translated to a
    /// Win32-style message (<see cref="DispatchMessageHook"/>). Popup uses this for
    /// <c>WM_ACTIVATEAPP</c> (close on app deactivate) and <c>WM_WINDOWPOSCHANGING</c>.
    /// </summary>
    public void AddHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ObjectDisposedException.ThrowIf(DisposedFlag, this);
        _hooks.Add(hook);
    }

    /// <summary>Removes a hook added with <see cref="AddHook"/>.</summary>
    public void RemoveHook(HwndSourceHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _ = _hooks.Remove(hook);
    }

    /// <summary>
    /// Invokes the registered hooks for a Win32-style message, mirroring
    /// <c>HwndSource</c>'s hook dispatch. Returns the first non-zero result; hooks may
    /// mark the message handled.
    /// </summary>
    public IntPtr DispatchMessageHook(int message, IntPtr wParam, IntPtr lParam)
    {
        IntPtr result = IntPtr.Zero;
        for (int i = _hooks.Count - 1; i >= 0; i--)
        {
            bool handled = false;
            IntPtr hookResult = _hooks[i](Handle, message, wParam, lParam, ref handled);
            if (hookResult != IntPtr.Zero)
            {
                result = hookResult;
            }

            if (handled)
            {
                break;
            }
        }

        return result;
    }

    public void Hide()
    {
        Frame.Window?.Hide();
    }

    /// <summary>
    /// Starts the deactivation debounce: report WM_ACTIVATEAPP(0) only if, ~100ms from
    /// now, no window of this process still holds keyboard focus. The grace window covers
    /// an intra-process focus hand-off (owner → just-opened popup) whose focus-lost and
    /// focus-gained arrive in separate compositor round-trips; a genuine switch to
    /// another application leaves no window focused and is reported after the window.
    /// </summary>
    private static void ScheduleDeactivation(SdlPresentationSource source)
    {
        if (s_deactivationTimer is not null)
        {
            return;
        }

        s_deactivationSource = source;
        s_deactivationTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        s_deactivationTimer.Tick += static (_, _) => ConfirmDeactivation();
        s_deactivationTimer.Start();
    }

    /// <summary>Cancels a pending deactivation report (some window of this process regained focus).</summary>
    private static void CancelDeactivation()
    {
        s_deactivationTimer?.Stop();
        s_deactivationTimer = null;
        s_deactivationSource = null;
    }

    private static void ConfirmDeactivation()
    {
        SdlPresentationSource? source = s_deactivationSource;
        s_deactivationTimer = null;
        s_deactivationSource = null;
        if (!s_appActivated || source is null)
        {
            return;
        }

        // Deliver on the source that lost focus; if it was disposed meanwhile (the menu
        // closed by another path), fall back to any live source so Application.OnDeactivated
        // and popup auto-close still see the app-level deactivation.
        SdlPresentationSource? deliver = source.DisposedFlag ? FirstLiveSource() : source;
        if (deliver is null)
        {
            s_appActivated = false;
            return;
        }

        if (deliver.Frame.Host is not { } host)
        {
            s_appActivated = false;
            return;
        }

        // Confirm: still no window of this process holds focus.
        if (host.HasKeyboardFocus())
        {
            return;
        }

        s_appActivated = false;

        // Windows delivers WM_ACTIVATEAPP to EVERY top-level window of the process:
        // popups register their close-on-deactivate hook (PopupFilterMessage) on their
        // own SDL source, and child-popup security hooks live on the owner's source —
        // delivering only to the focus-losing window leaves an open popup behind.
        List<SdlPresentationSource> live = [];
        lock (s_registryGate)
        {
            foreach (SdlPresentationSource candidate in s_sourcesByWindow.Values)
            {
                if (!candidate.DisposedFlag)
                {
                    live.Add(candidate);
                }
            }
        }

        foreach (SdlPresentationSource recipient in live)
        {
            _ = recipient.DispatchMessageHook(WM_ACTIVATEAPP, IntPtr.Zero, IntPtr.Zero);
        }

        // Popups that survive the deactivation (StaysOpen=true) must still stop
        // floating above the other application: hide the popup surfaces with the
        // rest of the window group (Windows stacking parity — see SetPopupsVisible).
        SetPopupsVisible(false);
    }

    private static SdlPresentationSource? FirstLiveSource()
    {
        lock (s_registryGate)
        {
            foreach (SdlPresentationSource source in s_sourcesByWindow.Values)
            {
                if (!source.DisposedFlag)
                {
                    return source;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The source that should own a modal dialog: the keyboard-focused window's source, or
    /// any live source. Returns <see langword="null"/> when no window exists (a dialog
    /// cannot run then — the SDL dialog callback needs the event pump, which only a live
    /// window drives).
    /// </summary>
    public static SdlPresentationSource? FromActiveWindow()
    {
        lock (s_registryGate)
        {
            foreach (SdlPresentationSource source in s_sourcesByWindow.Values)
            {
                if (source.DisposedFlag)
                {
                    continue;
                }

                if (source.Frame.Host?.HasKeyboardFocus() == true)
                {
                    return source;
                }
            }
        }

        return FirstLiveSource();
    }

    /// <summary>
    /// One pass of the Linux dispatcher message loop (registered via
    /// <see cref="Dispatcher.RegisterLinuxEventLoop"/>): block up to
    /// <paramref name="timeoutMs"/> (negative = unbounded) for SDL input and
    /// dispatch queued events to their sources. Presenting is the dispatcher
    /// loop's job — it calls the registered present action only after a drain
    /// that ran Render-priority+ work — so an idle app performs no presents.
    /// Always returns true — the loop exits only when
    /// <c>DispatcherFrame.Continue</c> goes false (Application shutdown), never
    /// from a pump-side condition.
    /// </summary>
    private static bool PumpStep(int timeoutMs)
    {
        SdlPresentationSource? source = FirstLiveSource();
        if (source is null)
        {
            // No window yet (or all disposed): nothing to wait on. The dispatcher
            // drains the queue each pass; a bounded yield keeps the pre-window
            // phase responsive without spinning.
            System.Threading.Thread.Sleep(timeoutMs < 0 ? 15 : Math.Min(timeoutMs, 15));
            return true;
        }

        if (source.Frame.WaitEventTimeout(timeoutMs, out SdlEvent ev))
        {
            // Dispatch each queued event against a FRESH layout: a fast drag batches
            // several motion events per loop pass, and the stock layout update runs in
            // the render message — a control whose next event consumes positions
            // against the previous event's un-applied arrange (the Slider thumb's
            // drag-delta feedback) oscillates: the WPFGallery "simple slider" value
            // flickered 100<->0 on fast drags. UpdateLayout is a no-op when the
            // LayoutManager has nothing pending.
            while (true)
            {
                source.Dispatch(ev);
                if (source.CompositionTarget.RootVisual is System.Windows.UIElement root)
                {
                    root.UpdateLayout();
                }

                if (!source.TryPump(out SdlEvent next))
                {
                    break;
                }

                ev = next;
            }
        }
        else if (source.IsClosing)
        {
            // A quit or this window's close-requested was consumed: there is
            // nothing left to pump from this frame. Yield (never busy-spin).
            System.Threading.Thread.Sleep(timeoutMs < 0 ? 15 : Math.Min(timeoutMs, 15));
        }

        return true;
    }

    /// <summary>
    /// Brings the window above other windows and requests input focus. No-op when the SDL video
    /// driver does not implement it (e.g. offscreen). Named <c>BringToFront</c> because CA1030
    /// flags any public method whose name starts with "raise".
    /// </summary>
    public void BringToFront()
    {
        Frame.Window?.BringToFront();
    }

    /// <summary>Requests that the window be minimized. No-op when the SDL video driver does not implement it.</summary>
    public void Minimize()
    {
        Frame.Window?.Minimize();
    }

    /// <summary>Requests that the window be maximized. No-op when the SDL video driver does not implement it.</summary>
    public void Maximize()
    {
        Frame.Window?.Maximize();
    }

    /// <summary>Requests that a minimized or maximized window be restored. No-op when the SDL video driver does not implement it.</summary>
    public void Restore()
    {
        Frame.Window?.Restore();
    }

    /// <summary>
    /// Sets the owner (parent) of this window; <c>null</c> removes the owner. Maps to
    /// <c>SDL_SetWindowParent</c> on the backing window.
    /// </summary>
    public void SetOwner(SdlPresentationSource? owner)
    {
        Frame.Window?.SetParent(owner?.Frame.Window);
    }

    /// <summary>
    /// Gets the window placement (screen position and pixel size) from the backing SDL window.
    /// Returns <c>false</c> when there is no backing window.
    /// </summary>
    public bool GetPlacement(out int x, out int y, out int width, out int height)
    {
        if (Frame.Window is { } window)
        {
            x = (int)window.Position.X;
            y = (int)window.Position.Y;
            width = window.PixelSize.Width;
            height = window.PixelSize.Height;
            return true;
        }

        x = 0;
        y = 0;
        width = 0;
        height = 0;
        return false;
    }

    /// <summary>Current global mouse position in screen coordinates from the SDL host, or (0,0) with no host.</summary>
    public System.Windows.Point GetGlobalMousePosition()
    {
        Nova.Geometry.Point position = Frame.Host?.GetGlobalMousePosition() ?? Nova.Geometry.Point.Origin;
        return new System.Windows.Point(position.X, position.Y);
    }

    /// <summary>True when the backing window is an SDL popup (menu/tooltip) rather than a regular window.</summary>
    public bool IsPopupWindow => Frame.Window?.IsPopup ?? false;

    /// <summary>The composition target resource handle this source's frame rasterizes (diagnostic).</summary>
    public uint FrameTargetHandle => Frame.TargetHandle;

    /// <summary>True after SDL quit or close-requested.</summary>
    public bool IsClosing => Frame.Closing;

    /// <summary>Pump one SDL event and map it. Returns false when the queue is empty or closing.</summary>
    public bool TryPump(out SdlEvent ev)
    {
        return Frame.TryPump(out ev);
    }

    /// <summary>
    /// Rasterizes ONLY this source's own frame onto its presenter. A popup/child source is
    /// a separate frame; presenting the main source does not render popups. To render every
    /// live frame in one pass — the app-loop contract — call the static
    /// <see cref="PresentAll"/> instead. <c>Present</c> is kept frame-local so a
    /// multi-window loop that presents each source in turn renders every frame exactly once
    /// per iteration (a loop of N sources calling <c>Present()</c> must not multiply GPU
    /// work by N).
    /// </summary>
    public void Present()
    {
        ObjectDisposedException.ThrowIf(DisposedFlag, this);
        Frame.Present();
    }

    /// <summary>
    /// Rasterizes every live composition frame attached to the shared channel set — this
    /// source's window, any popup/tooltip windows, and any other top-level windows in the
    /// process. The host/app loop calls this ONCE per iteration (typically with the main
    /// source's <c>TryPump</c>/<c>Dispatch</c> loop), instead of calling each source's
    /// <see cref="Present"/> in turn; the popup frames' present callbacks are only
    /// reachable through the shared DuceRuntime binding table, so a loop that presents only
    /// the main source would leave popups unrendered.
    /// </summary>
    public static void PresentAll()
    {
        DuceRuntime.Present();
    }

    /// <summary>
    /// Opts this window's presenter into pixel readback (see
    /// <c>IVulkanPresenter.EnableReadback</c>). A no-op for offscreen presenters.
    /// </summary>
    public void EnableReadback()
    {
        Frame.Presenter.EnableReadback();
    }

    /// <summary>Returns the most recently rendered frame as R,G,B,A bytes (see <c>IVulkanPresenter.ReadbackRgba</c>).</summary>
    public ReadOnlyMemory<byte> ReadbackRgba()
    {
        return Frame.Presenter.ReadbackRgba();
    }

    public event EventHandler? Disposed;

    public event EventHandler? SizeToContentChanged;

    /// <summary>Mirrors <c>HwndSource.AutoResized</c>: raised when the source resizes itself to its content.</summary>
    public event AutoResizedEventHandler? AutoResized;

    /// <summary>Mirrors <c>HwndSource.DpiChanged</c>: raised when the window's display scale changes.</summary>
    public event HwndDpiChangedEventHandler? DpiChanged;

    public SizeToContent SizeToContent
    {
        get => StoredSizeToContent;
        set
        {
            if (StoredSizeToContent == value)
            {
                return;
            }

            StoredSizeToContent = value;
            SizeToContentChanged?.Invoke(this, EventArgs.Empty);
            if (IsLayoutActive())
            {
                SetLayoutSize();
            }
        }
    }

    private bool DisposedFlag { get; set; }

    private Visual? StoredRoot { get; set; }

    public override bool IsDisposed => DisposedFlag;

    public override Visual RootVisual
    {
        get => StoredRoot ?? null!;
        set
        {
            if (StoredRoot == value)
            {
                return;
            }

            Visual? old = StoredRoot;
            if (value is not null)
            {
                StoredRoot = value;
                if (value is UIElement newRoot)
                {
                    newRoot.LayoutUpdated += OnLayoutUpdated;
                }

                CompositionTarget.RootVisual = value;
                UIElement.PropagateResumeLayout(null, value);
            }
            else
            {
                StoredRoot = null;
                CompositionTarget.RootVisual = null;
            }

            if (old is not null)
            {
                if (old is UIElement oldRoot)
                {
                    oldRoot.LayoutUpdated -= OnLayoutUpdated;
                }

                UIElement.PropagateSuspendLayout(old);
            }

            RootChanged(old, StoredRoot);
            if (IsLayoutActive())
            {
                SetLayoutSize();
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new DispatcherOperationCallback(FireContentRendered), this);
            }
            else
            {
                InputManager.SafeCurrentNotifyHitTestInvalidated();
            }
        }
    }

    public new SdlCompositionTarget CompositionTarget { get; private set; }

    protected override CompositionTarget GetCompositionTargetCore()
    {
        return CompositionTarget;
    }

    internal override IInputProvider GetInputProvider(Type inputDevice)
    {
        return inputDevice == typeof(MouseDevice) || inputDevice == typeof(KeyboardDevice)
            ? this
            : null!;
    }

    private bool IsLayoutActive()
    {
        return StoredRoot is UIElement && !CompositionTarget.IsDisposed;
    }

    private void SetLayoutSize()
    {
        if (StoredRoot is not UIElement root)
        {
            return;
        }

        root.InvalidateMeasure();
        System.Windows.Size constraint;
        if (StoredSizeToContent == SizeToContent.WidthAndHeight)
        {
            constraint = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);
        }
        else
        {
            System.Windows.Size fromSource = SizeFromSource();
            constraint = new System.Windows.Size(
                StoredSizeToContent == SizeToContent.Width ? double.PositiveInfinity : fromSource.Width,
                StoredSizeToContent == SizeToContent.Height ? double.PositiveInfinity : fromSource.Height);
        }

        root.Measure(constraint);
        System.Windows.Size arrange;
        if (StoredSizeToContent == SizeToContent.WidthAndHeight)
        {
            arrange = root.DesiredSize;
        }
        else
        {
            System.Windows.Size fromSource = SizeFromSource();
            arrange = StoredSizeToContent switch
            {
                SizeToContent.Manual => fromSource,
                SizeToContent.Width => new System.Windows.Size(root.DesiredSize.Width, fromSource.Height),
                SizeToContent.Height => new System.Windows.Size(fromSource.Width, root.DesiredSize.Height),
                SizeToContent.WidthAndHeight => root.DesiredSize,
                _ => fromSource
            };
        }

        root.Arrange(new System.Windows.Rect(new System.Windows.Point(), arrange));
        root.UpdateLayout();

        if (Frame.Window is { } layoutWindow && layoutWindow.Options.Popup != PopupKind.None)
        {
            // Popups are created hidden and must be mapped at their content size: Wayland
            // xdg_popup sizes are decided when the surface is mapped, and the WPF popup
            // machinery never applies a size on Linux (SetPopupPos is position-only). Size
            // the SDL window and its presenter to the laid-out content synchronously, so
            // Popup.ShowWindow maps a correctly-sized popup with no oversized flash. This
            // covers the real popup AND the plain-window fallback used by drivers without
            // popup support (the fallback keeps Popup in its WindowOptions).
            System.Windows.Point desiredDevice = CompositionTarget.TransformToDevice.Transform((System.Windows.Point)arrange);
            int popupWidth = Math.Max(1, (int)Math.Round(desiredDevice.X));
            int popupHeight = Math.Max(1, (int)Math.Round(desiredDevice.Y));
            if (popupWidth != layoutWindow.PixelSize.Width || popupHeight != layoutWindow.PixelSize.Height)
            {
                layoutWindow.SetPixelSize(popupWidth, popupHeight);
                Frame.ResizePresenter(layoutWindow.PixelSize);
            }
        }
        else if (Frame.Window is { } topWindow && StoredSizeToContent != SizeToContent.Manual)
        {
            // Top-level Window SizeToContent: WPF's Window pushes its SizeToContent value
            // here via SourceWindowHelper (HwndSourceSizeToContent → _sourceWindow.
            // SizeToContent), and on Windows HwndSource resizes the HWND from the same
            // arrange size. On Linux nothing resized the SDL window, so a SizeToContent
            // window stayed at the CreateWindowFrame fallback (800×600 — an STC window
            // has NaN Width/Height, so the parameter size is always 0). Resize the SDL
            // window and its presenter to the laid-out content, in device pixels.
            System.Windows.Point desiredDevice = CompositionTarget.TransformToDevice.Transform((System.Windows.Point)arrange);
            int topWidth = Math.Max(1, (int)Math.Round(desiredDevice.X));
            int topHeight = Math.Max(1, (int)Math.Round(desiredDevice.Y));
            if (topWidth != topWindow.PixelSize.Width || topHeight != topWindow.PixelSize.Height)
            {
                topWindow.SetPixelSize(topWidth, topHeight);
                Frame.ResizePresenter(topWindow.PixelSize);
            }
        }
    }

    private System.Windows.Size SizeFromSource()
    {
        int width = PixelWidth > 0 ? PixelWidth : 800;
        int height = PixelHeight > 0 ? PixelHeight : 600;
        var device = new System.Windows.Point(width, height);
        System.Windows.Point logical = CompositionTarget.TransformFromDevice.Transform(device);
        return new System.Windows.Size(logical.X, logical.Y);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!IsLayoutActive() || StoredSizeToContent == SizeToContent.Manual)
        {
            return;
        }

        if (StoredRoot is not UIElement root)
        {
            return;
        }

        System.Windows.Point desiredDevice = CompositionTarget.TransformToDevice.Transform((System.Windows.Point)root.DesiredSize);
        var deviceSize = new System.Windows.Size(Math.Max(0, desiredDevice.X), Math.Max(0, desiredDevice.Y));
        if (_lastAutoResizedDeviceSize is { } last && last == deviceSize)
        {
            return;
        }

        _lastAutoResizedDeviceSize = deviceSize;
        SetLayoutSize();
        AutoResized?.Invoke(this, new AutoResizedEventArgs(deviceSize));
    }

    public void Dispose()
    {
        if (DisposedFlag)
        {
            return;
        }

        // Flag first, reports second. The flag rejects NEW work during teardown
        // (CaptureMouse/AddHook/Present fail fast) while the reports below still reach
        // MouseDevice — they do not consult IsDisposed, and MouseDevice clears its capture
        // state from them without calling back into this provider.
        DisposedFlag = true;

        // Report deactivation while the source can still be identified: MouseDevice
        // clears _inputSource and releases any capture held through this source. The
        // report is honored when this source is the active input source, and — after the
        // MouseDevice capture-ownership gate — also when this source merely holds mouse
        // capture (a menu closed by keyboard/programmatically, or dismissed by the
        // compositor, while the active source is elsewhere). Without it the mouse stays
        // bound to the dead popup and the remaining windows go deaf.
        Report(new RawMouseInputReport(
            InputMode.Foreground,
            Environment.TickCount,
            this,
            RawMouseActions.Deactivate,
            0,
            0,
            0,
            IntPtr.Zero));

        if (Handle != IntPtr.Zero)
        {
            UnregisterSource(this);
        }

        InputSite?.Dispose();
        InputSite = null;
        RemoveSource();
        CompositionTarget.Dispose();
        Frame.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispatch(SdlEvent ev)
    {
        // Multi-window routing: SDL events carry the window they were delivered to. Events
        // for a different window are forwarded to that window's source so mouse/keyboard
        // reports reach the right PresentationSource. Events without a window (offscreen
        // frames, synthetic tests) stay local.
        if (ev.Window.IsValid && ev.Window.Value != Handle)
        {
            if (TryGetSource(ev.Window.Value, out SdlPresentationSource? target) && !ReferenceEquals(target, this))
            {
                target.Dispatch(ev);
            }

            return;
        }

        switch (ev.Kind)
        {
            case SdlEventKind.WindowFocusGained:
                IsActive = true;
                // WM_ACTIVATEAPP is PROCESS-level activation, not per-window focus: Win32
                // sends it when the whole application is activated/deactivated on the
                // desktop. SDL only gives per-window focus events, so derive the transition
                // from SDL_GetKeyboardFocus: the app is active iff SOME window of this
                // process holds focus. Raising WM_ACTIVATEAPP(0) merely because one of our
                // own windows lost focus to another of our windows (the main window losing
                // focus to a just-opened ContextMenu popup) makes PopupFilterMessage close
                // the freshly-opened menu — the menu that flickers open and shut and can
                // never reopen. Any of our windows gaining focus cancels a pending
                // deactivation and, on the app-inactive → app-active transition, reports
                // activation promptly.
                CancelDeactivation();
                if (!s_appActivated && Frame.Host?.HasKeyboardFocus() == true)
                {
                    s_appActivated = true;
                    SetPopupsVisible(true);
                    _ = DispatchMessageHook(WM_ACTIVATEAPP, new IntPtr(1), IntPtr.Zero);
                }

                break;
            case SdlEventKind.WindowFocusLost:
                IsActive = false;
                // See WindowFocusGained: report deactivation only when NO window of this
                // process holds focus anymore. The check is DEBOUNCED, not synchronous:
                // on a real compositor the focus hand-off between two windows of the same
                // process (the owner losing focus to a just-opened popup) is not atomic —
                // Wayland delivers keyboard_leave and keyboard_enter as separate protocol
                // callbacks, so SDL_GetKeyboardFocus() can transiently return null between
                // them. A single synchronous sample here would fire a spurious
                // WM_ACTIVATEAPP(0) and close the just-opened menu. Only report
                // deactivation if, ~100ms later, no window of the process holds focus
                // (a genuine switch to another application).
                if (s_appActivated && Frame.Host?.HasKeyboardFocus() != true)
                {
                    ScheduleDeactivation(this);
                }

                break;
            case SdlEventKind.WindowDisplayChanged:
                double scale = Frame.Window?.DisplayScale ?? 1.0;
                if (Math.Abs(scale - _currentDpiScale.DpiScaleX) > double.Epsilon)
                {
                    DpiScale oldDpi = _currentDpiScale;
                    _currentDpiScale = new DpiScale(scale, scale);
                    DpiChanged?.Invoke(this, new HwndDpiChangedEventArgs(oldDpi, _currentDpiScale, System.Windows.Rect.Empty));
                }

                CompositionTarget.ApplyScale(scale);
                break;
            case SdlEventKind.MouseMoved:
                if (_intraAppDrag is { } moveDrag)
                {
                    // Mid-drag moves feed the drag loop, not the input pipeline
                    // (Windows: OLE consumes them the same way).
                    moveDrag.OnMove(new System.Windows.Point(ev.Position.X, ev.Position.Y));
                    break;
                }

                goto case SdlEventKind.MouseButtonDown;
            case SdlEventKind.MouseButtonDown:
            case SdlEventKind.MouseButtonUp:
            case SdlEventKind.MouseWheel:
                if (ev.Kind == SdlEventKind.MouseButtonUp && ev.MouseButton == Nova.Sdl.MouseButton.Left && _intraAppDrag is { } upDrag)
                {
                    upDrag.OnDrop(new System.Windows.Point(ev.Position.X, ev.Position.Y));

                    // Fall through: report the release so MouseDevice's button
                    // state stays honest after the drag loop consumed the moves.
                }

                RawMouseActions actions = MapMouseActions(ev);
                if (!ReferenceEquals(Mouse.PrimaryDevice.ActiveSource, this))
                {
                    // MouseDevice switches _inputSource only on an Activate report. A sticky
                    // per-source bool goes stale when another window activates in between
                    // (popup over main): the main window's later reports would carry no
                    // Activate and be silently dropped. Activate whenever this source is not
                    // currently the mouse device's active source.
                    actions |= RawMouseActions.Activate;
                }

                Report(new RawMouseInputReport(
                    InputMode.Foreground,
                    Environment.TickCount,
                    this,
                    actions,
                    (int)ev.Position.X,
                    (int)ev.Position.Y,
                    WheelDelta(ev),
                    IntPtr.Zero));
                break;
            case SdlEventKind.KeyDown:
                if (_intraAppDrag is { } escapeDrag && ev.KeyScanCode == SdlScanCodeEscape)
                {
                    escapeDrag.Cancel();
                    break;
                }

                goto case SdlEventKind.KeyUp;
            case SdlEventKind.KeyUp:
                {
                    int wpfKey = MapSdlKeyToWpfKey((int)ev.KeyScanCode);
                    Report(new RawKeyboardInputReport(
                        this,
                        InputMode.Foreground,
                        Environment.TickCount,
                        ev.Kind == SdlEventKind.KeyDown ? RawKeyboardActions.KeyDown : RawKeyboardActions.KeyUp,
                        wpfKey,
                        isExtendedKey: false,
                        isSystemKey: false,
                        wpfKey,
                        IntPtr.Zero));
                    break;
                }
            case SdlEventKind.TextInput:
                if (!string.IsNullOrEmpty(ev.Text))
                {
                    foreach (char ch in ev.Text)
                    {
                        Report(new RawTextInputReport(
                            this,
                            InputMode.Foreground,
                            Environment.TickCount,
                            isDeadCharacter: false,
                            isSystemCharacter: false,
                            isControlCharacter: char.IsControl(ch),
                            ch));
                    }
                }

                break;
            case SdlEventKind.DropBegin:
                _pendingDropFiles?.Clear();
                break;
            case SdlEventKind.DropFile:
                if (!string.IsNullOrEmpty(ev.Text))
                {
                    _pendingDropFiles ??= [];
                    _pendingDropFiles.Add(ev.Text);
                }

                break;
            case SdlEventKind.DropComplete:
                // The compositor delivered a file drop: raise the WPF drag events at
                // the drop position (patch 0047). The OLE drag-drop pipeline has no
                // Linux host, so the events are raised directly on the hit-tested
                // target with a FileDrop DataObject.
                if (_pendingDropFiles is { Count: > 0 } files &&
                    CompositionTarget.RootVisual is System.Windows.UIElement root)
                {
                    System.Windows.Point position = new(ev.Position.X, ev.Position.Y);
                    MS.Internal.DragDropInterop.RaiseFileDrop(root, [.. files], position);
                }

                _pendingDropFiles?.Clear();
                break;
            case SdlEventKind.DropPosition:
                break;
            case SdlEventKind.DropText:
                break;
            case SdlEventKind.Quit:
                break;
            case SdlEventKind.WindowCloseRequested:
                // Reached when another source's pump consumed this window's close-requested
                // and routed it here; the owning frame must be marked closing either way.
                // Also route WM_CLOSE through the WPF window's hook chain (Window.WmClose →
                // Closing/Closed), so clicking the SDL close button actually closes the window.
                _ = DispatchMessageHook(0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                Frame.RequestClose();
                break;
            case SdlEventKind.WindowMaximized:
                // WM_SIZE wParam = SIZE_MAXIMIZED (2): Window.WmSizeChanged raises StateChanged
                // and tracks IsMaximized, so the WPF window state follows the WM's maximize.
                _ = DispatchMessageHook(0x0005 /* WM_SIZE */, new IntPtr(2), IntPtr.Zero);
                break;
            case SdlEventKind.WindowMinimized:
                _ = DispatchMessageHook(0x0005 /* WM_SIZE */, new IntPtr(1), IntPtr.Zero); // SIZE_MINIMIZED
                break;
            case SdlEventKind.WindowRestored:
                _ = DispatchMessageHook(0x0005 /* WM_SIZE */, IntPtr.Zero, IntPtr.Zero); // SIZE_RESTORED
                break;
            case SdlEventKind.WindowResized:
                if (IsLayoutActive() && StoredSizeToContent != SizeToContent.WidthAndHeight)
                {
                    SetLayoutSize();
                }

                break;
            case SdlEventKind.WindowExposed:
                // The Wayland backend throttles interactive-resize configures to the
                // frame-callback cadence and uses the exposure event to nudge the client
                // to commit a frame ("ensure forward progress"). Re-lay out (which
                // schedules the render pass; the normal drain then presents) instead of
                // presenting the recorded frame directly: a blind PresentAll races the
                // swapchain recreation and flashes black.
                if (IsLayoutActive())
                {
                    SetLayoutSize();
                }

                break;
            case SdlEventKind.WindowMoved:
                break;
            default:
                break;
        }
    }

    private static void Report(InputReport report)
    {
        var args = new InputReportEventArgs(null, report)
        {
            RoutedEvent = InputManager.PreviewInputReportEvent
        };
        _ = InputManager.Current.ProcessInput(args);
    }

    private static void RegisterSource(SdlPresentationSource source)
    {
        lock (s_registryGate)
        {
            s_sourcesByWindow[source.Handle] = source;
        }
    }

    private static void UnregisterSource(SdlPresentationSource source)
    {
        lock (s_registryGate)
        {
            _ = s_sourcesByWindow.Remove(source.Handle);
        }
    }

    private static bool TryGetSource(IntPtr windowHandle, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SdlPresentationSource? source)
    {
        lock (s_registryGate)
        {
            return s_sourcesByWindow.TryGetValue(windowHandle, out source);
        }
    }

    /// <summary>
    /// Shows or hides every live popup window (menus, dropdowns, tooltips). Windows
    /// popups are owned windows: when the app deactivates (alt-tab) the whole window
    /// group — popups included — stacks behind the other application, and the popups
    /// come back with the app. SDL popup windows are separate top-level surfaces and
    /// would keep floating above the other app after the focus switch, so the runtime
    /// hides them on app deactivation and shows them again on reactivation. The WPF
    /// Popup state is untouched — a hidden popup is still open and simply not visible.
    /// Popups that CLOSE on deactivation (StaysOpen=false, the WM_ACTIVATEAPP hook)
    /// dispose their source, so the registry only ever contains live popups here.
    /// </summary>
    private static void SetPopupsVisible(bool visible)
    {
        lock (s_registryGate)
        {
            foreach (SdlPresentationSource source in s_sourcesByWindow.Values)
            {
                if (!source.DisposedFlag && source.Frame.Window is { IsPopup: true } window)
                {
                    if (visible)
                    {
                        window.Show();
                    }
                    else
                    {
                        window.Hide();
                    }
                }
            }
        }
    }

    /// <summary>
    /// The Vulkan validation mode for window and popup frames. Defaults to
    /// <see cref="ValidationMode.Disabled"/>: the Khronos validation layer is a
    /// development tool with substantial per-frame CPU cost, and on the Xe/MESA Linux
    /// stack its dispatch cache aborts the process under device create/destroy churn —
    /// a shipping app must never pay either cost implicitly.
    /// Set <c>NOVA_VULKAN_VALIDATION=1</c> to enable validation on every window this
    /// process creates (a deliberate validation run of the window path).
    /// This product switch is deliberately distinct from the test-only
    /// <c>NOVA_TEST_VULKAN_VALIDATION</c> consumed by <c>NovaTestVulkan</c>: a deployed
    /// app can enable validation without implying test machinery, and suites that create
    /// many devices keep their own opt-out. The popup path shares the main window's
    /// Vulkan device, so this single decision covers popup frames too.
    /// </summary>
    private static ValidationMode WindowValidationMode()
    {
        return Environment.GetEnvironmentVariable("NOVA_VULKAN_VALIDATION") == "1"
            ? ValidationMode.Enabled
            : ValidationMode.Disabled;
    }

    private static CompositionFrame CreateWindowFrame(HwndSourceParameters parameters)
    {
        int width = parameters.Width > 0 ? parameters.Width : 800;
        int height = parameters.Height > 0 ? parameters.Height : 600;
        return new CompositionFrame(
            new WindowOptions
            {
                Title = parameters.WindowName ?? "Nova",
                Size = new PixelSize(width, height),
                Hidden = true,
                Resizable = true,
                // Window.AllowsTransparency → CreateHwndSourceParameters sets
                // UsesPerPixelOpacity; without this read the flag died here and the
                // window was silently opaque. It becomes SDL_WINDOW_TRANSPARENT +
                // premultiplied swapchain composite alpha.
                Transparent = parameters.UsesPerPixelOpacity
            },
            new VulkanDeviceOptions
            {
                Validation = WindowValidationMode()
            });
    }

    private static CompositionFrame CreatePopupFrame(HwndSourceParameters parameters, SdlPresentationSource owner, PopupKind popupKind)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(owner.Frame.Host);
        ArgumentNullException.ThrowIfNull(owner.Frame.Device);
        SdlWindow ownerWindow = owner.Frame.Window ?? throw new InvalidOperationException("The popup owner has no SDL window.");

        // HwndSourceParameters("") defaults to 1×1; a 1×1 window constrains layout so the
        // menu content can never measure to its real size. Start at a generous size; the
        // window is CREATED HIDDEN and SetLayoutSize resizes it to the measured content
        // before Popup.ShowWindow maps it (Popup's own AutoResized → SetPopupPos path only
        // repositions — it never applies a size on Linux, so without the synchronous sizing
        // the popup would be shown at this placeholder size and stay there, which is the
        // reported "popup not sized to the context menu" bug).
        int width = parameters.Width > 1 ? parameters.Width : 800;
        int height = parameters.Height > 1 ? parameters.Height : 600;
        // Shares the owner's Vulkan device, so the popup's validation mode is the main
        // window's (see WindowValidationMode) — no separate device decision here.
        // SDL popup window coordinates are PARENT-RELATIVE. The parameters carry SCREEN
        // coordinates (the Popup's placement origin, which may be the persisted positionInfo
        // on reopens); convert to parent-relative here exactly like SdlWindow.SetPosition's
        // ToPopupOffset, so the rebuilt popup lands at the right screen position even when
        // Popup later skips SetPopupPos (placement unchanged => no reposition).
        return new CompositionFrame(
            owner.Frame.Host,
            owner.Frame.Device,
            new WindowOptions
            {
                Title = parameters.WindowName ?? "Nova",
                Size = new PixelSize(width, height),
                Hidden = true,
                Resizable = false,
                Popup = popupKind,
                Parent = ownerWindow,
                // Popup content carries its own alpha (rounded Fluent corners, drop
                // shadows): the popup window must composite per-pixel or the rounded
                // corners show as a black square behind the content.
                Transparent = true,
                X = parameters.PositionX - (int)ownerWindow.Position.X,
                Y = parameters.PositionY - (int)ownerWindow.Position.Y
            });
    }

    /// <summary>
    /// Maps an SDL3 keycode (SDLK_*) to the WPF <see cref="Key"/> value the keyboard
    /// report carries (the Win32 virtual-key numbering WPF's gestures and editing
    /// commands match against). Letters, digits and the ASCII-range keys differ or
    /// collide between the two vocabularies, so only exact-equality values pass through.
    /// </summary>
    private static int MapSdlKeyToWpfKey(int sdlKey)
    {
        return sdlKey switch
        {
            // SDLK_a..SDLK_z (97-122) -> Key.A..Key.Z (65-90).
            >= 'a' and <= 'z' => sdlKey - 32,
            0x40000050 => 37, // SDLK_LEFT -> Left
            0x40000052 => 38, // SDLK_UP -> Up
            0x4000004F => 39, // SDLK_RIGHT -> Right
            0x40000051 => 40, // SDLK_DOWN -> Down
            127 => 46, // SDLK_DELETE -> Delete
            0x4000004A => 36, // SDLK_HOME -> Home
            0x4000004D => 35, // SDLK_END -> End
            0x4000004B => 33, // SDLK_PAGEUP -> PageUp
            0x4000004E => 34, // SDLK_PAGEDOWN -> PageDown
            0x40000049 => 45, // SDLK_INSERT -> Insert
            0x400000E0 => 162, // SDLK_LCTRL -> LeftCtrl
            0x400000E4 => 163, // SDLK_RCTRL -> RightCtrl
            0x400000E1 => 160, // SDLK_LSHIFT -> LeftShift
            0x400000E5 => 161, // SDLK_RSHIFT -> RightShift
            0x400000E2 => 164, // SDLK_LALT -> LeftAlt
            0x400000E6 => 165, // SDLK_RALT -> RightAlt
            0x400000E3 => 91, // SDLK_LGUI -> LWin
            0x400000E7 => 92, // SDLK_RGUI -> RWin
            >= 0x4000003A and <= 0x40000045 => sdlKey - 0x4000003A + 112, // F1-F12
            _ => sdlKey,
        };
    }

    bool IInputProvider.ProvidesInputForRootVisual(Visual v)
    {
        return StoredRoot == v;
    }

    void IInputProvider.NotifyDeactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Sets the window icon from BGRA rows. Called by Window.UpdateIcon on Linux in place
    /// of the Win32 WM_SETICON/ExtractIconEx path.
    /// </summary>
    public void SetWindowIcon(int width, int height, int stride, ReadOnlySpan<byte> bgraPixels)
    {
        Frame.Window?.SetWindowIcon(width, height, stride, bgraPixels);
    }

    /// <summary>
    /// Shows a synchronous native message box (SDL3; the zenity backend on Linux) owned by
    /// this window. Returns the pressed button's id, or <see langword="null"/> when the box
    /// could not be shown. Called by MessageBox.ShowCore on Linux in place of the Win32
    /// MessageBox.
    /// </summary>
    public int? ShowMessageBox(
        string? title,
        string message,
        MessageBoxIconKind icon,
        IReadOnlyList<MessageBoxButtonDefinition> buttons)
    {
        return Nova.Sdl.SdlMessageBox.Show(
            Frame.Window,
            title,
            message,
            (Nova.Sdl.MessageBoxIconKind)icon,
            [.. buttons.Select(b => new Nova.Sdl.MessageBoxButtonDefinition(b.Id, b.Label, b.IsDefault, b.IsEscape))]);
    }

    /// <summary>
    /// The source whose synthetic handle equals <paramref name="handle"/> (a WindowInteropHelper
    /// handle on the SDL runtime), or the focused window's source, or any live source.
    /// </summary>
    public static SdlPresentationSource? FromHandleOrActive(IntPtr handle)
    {
        lock (s_registryGate)
        {
            if (handle != IntPtr.Zero && s_sourcesByWindow.TryGetValue(handle, out SdlPresentationSource? byHandle))
            {
                return byHandle;
            }
        }

        return FromActiveWindow();
    }

    /// <summary>
    /// Starts a native file/folder dialog (the XDG Desktop Portal via SDL3) owned by this
    /// window and returns its result channel. The caller keeps the dispatcher pumping until
    /// <see cref="FileDialogSession.Completed"/> fires. Called by CommonDialog on Linux in
    /// place of the Win32 common-item dialog.
    /// </summary>
    public FileDialogSession ShowFileDialog(
        FileDialogKind kind,
        FileDialogFilter[]? filters,
        string? initialDirectory,
        bool allowMany)
    {
        Nova.Sdl.FileDialogSession inner = Nova.Sdl.SdlFileDialog.Show(
            (Nova.Sdl.FileDialogKind)kind,
            Frame.Window,
            filters is null ? null : [.. filters.Select(f => new Nova.Sdl.FileDialogFilter(f.Name, f.Pattern))],
            string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory,
            allowMany);
        var session = new FileDialogSession(inner);
        inner.Completed += (_, e) => session.RaiseCompleted();
        return session;
    }

    /// <summary>
    /// Applies a custom chrome geometry: the window becomes borderless and the resolver
    /// classifies each pointer point as a drag region, a resize edge/corner or the client
    /// area. Called by WindowChromeWorker on Linux in place of the Win32 WM_NCHITTEST
    /// machinery; <see langword="null"/> resolver restores the normal compositor chrome.
    /// </summary>
    public void ConfigureChrome(Func<System.Windows.Point, ChromeHitTestRegion>? resolver)
    {
        if (Frame.Window is not { } window)
        {
            return;
        }

        window.SetBordered(resolver is null);
        window.SetHitTest(resolver is null ? null : p => (Nova.Sdl.HitTestRegion)resolver(new System.Windows.Point(p.X, p.Y)));
    }

    bool IMouseInputProvider.SetCursor(Cursor cursor)
    {
        if (Frame.Window is { } window)
        {
            window.SetCursor(ToSystemCursor(cursor.CursorType));
        }

        return true;
    }

    private static SystemCursorKind? ToSystemCursor(CursorType cursorType)
    {
        return cursorType switch
        {
            CursorType.None => null,
            CursorType.No => null,
            CursorType.Arrow => SystemCursorKind.Default,
            CursorType.AppStarting => SystemCursorKind.Progress,
            CursorType.Cross => SystemCursorKind.Crosshair,
            CursorType.Help => SystemCursorKind.Hand,
            CursorType.IBeam => SystemCursorKind.Text,
            CursorType.SizeAll => SystemCursorKind.Move,
            CursorType.SizeNESW => SystemCursorKind.ResizeNesw,
            CursorType.SizeNS => SystemCursorKind.ResizeNs,
            CursorType.SizeNWSE => SystemCursorKind.ResizeNwse,
            CursorType.SizeWE => SystemCursorKind.ResizeEw,
            CursorType.UpArrow => SystemCursorKind.ResizeN,
            CursorType.Wait => SystemCursorKind.Wait,
            CursorType.Hand => SystemCursorKind.Hand,
            CursorType.Pen => SystemCursorKind.Crosshair,
            CursorType.ScrollNS => SystemCursorKind.ResizeNs,
            CursorType.ScrollWE => SystemCursorKind.ResizeEw,
            CursorType.ScrollAll => SystemCursorKind.Move,
            CursorType.ScrollN => SystemCursorKind.ResizeN,
            CursorType.ScrollS => SystemCursorKind.ResizeS,
            CursorType.ScrollW => SystemCursorKind.ResizeW,
            CursorType.ScrollE => SystemCursorKind.ResizeE,
            CursorType.ScrollNW => SystemCursorKind.ResizeNw,
            CursorType.ScrollNE => SystemCursorKind.ResizeNe,
            CursorType.ScrollSW => SystemCursorKind.ResizeSw,
            CursorType.ScrollSE => SystemCursorKind.ResizeSe,
            CursorType.ArrowCD => SystemCursorKind.Default,
            _ => SystemCursorKind.Default,
        };
    }

    /// <summary>
    /// Captures the mouse at the SDL level so events keep flowing to this window while the
    /// pointer is outside it — what WPF menus rely on for click-outside-to-dismiss. The
    /// offscreen driver treats it as a no-op; a windowing driver really captures.
    /// </summary>
    public bool CaptureMouse()
    {
        if (IsDisposed)
        {
            return false;
        }

        Frame.Window?.CaptureMouse(true);
        return true;
    }

    bool IMouseInputProvider.CaptureMouse()
    {
        return CaptureMouse();
    }

    /// <summary>
    /// Toggles input pass-through for popups. The SDL popup window kind (tooltip vs menu) is
    /// decided ONCE at creation from <c>Popup.HitTestable</c> (see the 0009 BuildWindow
    /// mapping): SDL has no way to flip a popup kind in place, and recreating the window
    /// mid-show destroys the popup's composition target, whose out-of-band resource release
    /// used to clobber the shared channel graph. Popup.SetHitTestable still toggles the
    /// managed <c>_popupRoot.IsHitTestVisible</c>; the window kind itself never needs to
    /// change. Regular windows are always hit-testable. Kept as a method so the
    /// PopupSecurityHelper hook stays source-compatible.
    /// </summary>
    public void SetHitTestable(bool hitTestable)
    {
        _ = hitTestable;
        if (Frame.Window is not { IsPopup: true })
        {
            return;
        }

        // The popup kind was fixed at creation; nothing to flip in place.
    }

    void IMouseInputProvider.ReleaseMouseCapture()
    {
        // Deliberately NO IsDisposed guard: PopupSecurityHelper.DestroyWindow disposes the
        // popup source BEFORE Popup.ReleasePopupCapture runs (DestroyWindowImpl first, then
        // ReleasePopupCapture), so this can be called while or after teardown. MouseDevice
        // clears its capture state from the CancelCapture report without calling back into
        // the provider (ChangeMouseCapture mutates its own fields), so reporting from a
        // disposing source is safe — and is exactly what releases a capture that would
        // otherwise stay pinned to the dead popup, hijacking every later mouse event.
        if (Frame.Window is { } window && !window.IsDisposed)
        {
            window.CaptureMouse(false);
        }

        Report(new RawMouseInputReport(
            InputMode.Foreground,
            Environment.TickCount,
            this,
            RawMouseActions.CancelCapture,
            0,
            0,
            0,
            IntPtr.Zero));
    }

    int IMouseInputProvider.GetIntermediatePoints(IInputElement relativeTo, System.Windows.Point[] points)
    {
        _ = relativeTo;
        _ = points;
        return -1;
    }

    bool IKeyboardInputProvider.AcquireFocus(bool checkOnly)
    {
        _ = checkOnly;
        return !IsDisposed;
    }

    internal static RawMouseActions MapMouseActions(SdlEvent ev)
    {
        return ev.Kind switch
        {
            SdlEventKind.MouseMoved => RawMouseActions.AbsoluteMove,
            SdlEventKind.MouseButtonDown => RawMouseActions.AbsoluteMove | ButtonPress(ev.MouseButton),
            SdlEventKind.MouseButtonUp => RawMouseActions.AbsoluteMove | ButtonRelease(ev.MouseButton),
            SdlEventKind.MouseWheel => RawMouseActions.AbsoluteMove | RawMouseActions.VerticalWheelRotate,
            SdlEventKind.Quit => RawMouseActions.None,
            SdlEventKind.WindowCloseRequested => RawMouseActions.None,
            SdlEventKind.WindowMaximized => RawMouseActions.None,
            SdlEventKind.WindowMinimized => RawMouseActions.None,
            SdlEventKind.WindowRestored => RawMouseActions.None,
            SdlEventKind.WindowResized => RawMouseActions.None,
            SdlEventKind.WindowMoved => RawMouseActions.None,
            SdlEventKind.WindowFocusGained => RawMouseActions.None,
            SdlEventKind.WindowFocusLost => RawMouseActions.None,
            SdlEventKind.WindowExposed => RawMouseActions.None,
            SdlEventKind.WindowDisplayChanged => RawMouseActions.None,
            SdlEventKind.KeyDown => RawMouseActions.None,
            SdlEventKind.KeyUp => RawMouseActions.None,
            SdlEventKind.TextInput => RawMouseActions.None,
            SdlEventKind.DropBegin => RawMouseActions.None,
            SdlEventKind.DropFile => RawMouseActions.None,
            SdlEventKind.DropText => RawMouseActions.None,
            SdlEventKind.DropComplete => RawMouseActions.None,
            SdlEventKind.DropPosition => RawMouseActions.None,
            _ => RawMouseActions.None
        };
    }

    private static RawMouseActions ButtonPress(Sdl.MouseButton button)
    {
        return button switch
        {
            Sdl.MouseButton.None => RawMouseActions.None,
            Sdl.MouseButton.Left => RawMouseActions.Button1Press,
            Sdl.MouseButton.Middle => RawMouseActions.Button3Press,
            Sdl.MouseButton.Right => RawMouseActions.Button2Press,
            Sdl.MouseButton.X1 => RawMouseActions.Button4Press,
            Sdl.MouseButton.X2 => RawMouseActions.Button5Press,
            _ => RawMouseActions.None
        };
    }

    private static RawMouseActions ButtonRelease(Sdl.MouseButton button)
    {
        return button switch
        {
            Sdl.MouseButton.None => RawMouseActions.None,
            Sdl.MouseButton.Left => RawMouseActions.Button1Release,
            Sdl.MouseButton.Middle => RawMouseActions.Button3Release,
            Sdl.MouseButton.Right => RawMouseActions.Button2Release,
            Sdl.MouseButton.X1 => RawMouseActions.Button4Release,
            Sdl.MouseButton.X2 => RawMouseActions.Button5Release,
            _ => RawMouseActions.None
        };
    }

    private static int WheelDelta(SdlEvent ev)
    {
        return ev.Kind == SdlEventKind.MouseWheel ? (int)(ev.Delta.Y * 120) : 0;
    }

    /// <summary>
    /// The state of one managed intra-app drag (DragDrop.DoDragDrop on Linux).
    /// Tracks the hit-tested target, negotiates effects through the routed
    /// drag events, and completes the pushed dispatcher frame on drop/cancel.
    /// </summary>
    private sealed class IntraAppDragContext
    {
        private readonly SdlPresentationSource _source;
        private readonly System.Windows.UIElement _root;
        private readonly DependencyObject _dragSource;
        private readonly IDataObject _data;
        private readonly DragDropEffects _allowedEffects;
        private readonly DispatcherFrame _frame;
        private DependencyObject? _target;
        private bool _entered;
        private bool _completed;

        internal IntraAppDragContext(SdlPresentationSource source, System.Windows.UIElement root, DependencyObject dragSource, IDataObject data, DragDropEffects allowedEffects, DispatcherFrame frame)
        {
            _source = source;
            _root = root;
            _dragSource = dragSource;
            _data = data;
            _allowedEffects = allowedEffects;
            _frame = frame;
            Effect = allowedEffects;
        }

        internal DragDropEffects Effect { get; private set; }

        internal void OnMove(System.Windows.Point position)
        {
            // The pump drains the queue in batches: after a cancel/drop completes
            // the frame, a same-batch trailing event must not re-enter the loop.
            if (_completed)
            {
                return;
            }

            // QueryContinueDrag lets handlers veto or cancel mid-drag (Windows
            // parity); raised at the drag source (the window root) like OLE does.
            var query = new QueryContinueDragEventArgs(escapePressed: false, DragDropKeyStates.LeftMouseButton)
            {
                RoutedEvent = DragDrop.QueryContinueDragEvent
            };
            if (_dragSource is UIElement uiElement)
            {
                uiElement.RaiseEvent(query);
            }
            else if (_dragSource is ContentElement contentElement)
            {
                contentElement.RaiseEvent(query);
            }
            else if (_dragSource is UIElement3D uiElement3D)
            {
                uiElement3D.RaiseEvent(query);
            }
            if (query.Action == DragAction.Cancel)
            {
                Cancel();
                return;
            }

            DependencyObject target = _root.InputHitTest(position) as DependencyObject ?? _root;
            if (!ReferenceEquals(target, _target))
            {
                if (_target is not null)
                {
                    MS.Internal.DragDropInterop.RaiseDragLeave(_target, _data, position, Effect);
                }

                _target = target;
                Effect = MS.Internal.DragDropInterop.RaiseDragEnter(target, _data, position, _allowedEffects);
                _entered = true;
            }
            else if (_entered)
            {
                Effect = MS.Internal.DragDropInterop.RaiseDragOver(target, _data, position, Effect);
            }

            RaiseGiveFeedback();
        }

        // Windows parity: OLE renders the effect cursor during the drag. The
        // managed loop raises GiveFeedback and applies the default cursor unless
        // a handler opted into custom cursors (UseDefaultCursors = false).
        private void RaiseGiveFeedback()
        {
            var feedback = new GiveFeedbackEventArgs(Effect, useDefaultCursors: true)
            {
                RoutedEvent = DragDrop.GiveFeedbackEvent
            };
            if (_dragSource is UIElement uiElement)
            {
                uiElement.RaiseEvent(feedback);
            }
            else if (_dragSource is ContentElement contentElement)
            {
                contentElement.RaiseEvent(feedback);
            }
            else if (_dragSource is UIElement3D uiElement3D)
            {
                uiElement3D.RaiseEvent(feedback);
            }

            if (feedback.UseDefaultCursors)
            {
                Cursor cursor = Effect switch
                {
                    DragDropEffects.Copy => Cursors.Cross,
                    DragDropEffects.Move => Cursors.Arrow,
                    DragDropEffects.Link => Cursors.Hand,
                    DragDropEffects.Scroll => Cursors.Arrow,
                    DragDropEffects.All => Cursors.Arrow,
                    DragDropEffects.None => Cursors.No,
                    _ => Cursors.Arrow
                };
                _ = ((IMouseInputProvider)_source).SetCursor(cursor);
            }
        }

        internal void OnDrop(System.Windows.Point position)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            Mouse.UpdateCursor();
            DependencyObject target = _root.InputHitTest(position) as DependencyObject ?? _root;
            if (!ReferenceEquals(target, _target))
            {
                if (_target is not null)
                {
                    MS.Internal.DragDropInterop.RaiseDragLeave(_target, _data, position, Effect);
                }

                _target = target;
                Effect = MS.Internal.DragDropInterop.RaiseDragEnter(target, _data, position, _allowedEffects);
                _entered = true;
            }

            if (_entered && _target is not null)
            {
                Effect = MS.Internal.DragDropInterop.RaiseDrop(_target, _data, position, Effect);
            }

            _frame.Continue = false;
        }

        internal void Cancel()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            if (_entered && _target is not null)
            {
                MS.Internal.DragDropInterop.RaiseDragLeave(_target, _data, new System.Windows.Point(0, 0), Effect);
            }

            Effect = DragDropEffects.None;
            Mouse.UpdateCursor();
            _frame.Continue = false;
        }
    }
}
