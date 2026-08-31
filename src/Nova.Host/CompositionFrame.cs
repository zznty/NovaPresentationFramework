using JetBrains.Annotations;
using Nova.Geometry;
using Nova.Mil;
using Nova.Sdl;
using Nova.Text;
using Nova.Vulkan;

namespace Nova.Host;

/// <summary>
/// One SDL window plus a Vulkan presenter and a <see cref="SlaveGraph"/>.
/// Does not reference PresentationCore. Pump events, then <see cref="Present"/>.
/// </summary>
[PublicAPI]
public sealed class CompositionFrame : IDisposable
{
    private const int AtlasPageSize = 512;

    private int _disposed;
    private readonly bool _ownsHost = true;
    private readonly bool _ownsDevice = true;

    private CompositionFrame()
    {
    }

    public CompositionFrame(WindowOptions windowOptions, VulkanDeviceOptions deviceOptions)
        : this()
    {
        ArgumentNullException.ThrowIfNull(windowOptions);
        ArgumentNullException.ThrowIfNull(deviceOptions);

        Host = new SdlHost();
        try
        {
            Window = Host.CreateWindow(windowOptions);
            Device = new VulkanDevice(WithWindowExtensions(deviceOptions, Window.RequiredInstanceExtensions));
            Presenter = Device.CreatePresenter(Window);
            Graph = new SlaveGraph();
            WireBrushResources(Graph);
            Atlas = new GlyphAtlas(Presenter, new PixelSize(AtlasPageSize, AtlasPageSize));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a frame whose window shares an existing <see cref="SdlHost"/> and
    /// <see cref="VulkanDevice"/> (popup windows). The caller owns and must dispose
    /// <paramref name="host"/> and <paramref name="device"/> after this frame.
    /// </summary>
    public CompositionFrame(SdlHost host, VulkanDevice device, WindowOptions windowOptions)
        : this()
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(windowOptions);

        Host = host;
        Device = device;
        _ownsHost = false;
        _ownsDevice = false;
        try
        {
            Window = Host.CreateWindow(windowOptions);
            Presenter = Device.CreatePresenter(Window);
            Graph = new SlaveGraph();
            WireBrushResources(Graph);
            Atlas = new GlyphAtlas(Presenter, new PixelSize(AtlasPageSize, AtlasPageSize));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public SdlHost? Host { get; private set; }

    public SdlWindow? Window { get; private set; }

    public VulkanDevice Device { get; private set; } = null!;

    public IVulkanPresenter Presenter { get; private set; } = null!;

    public SlaveGraph Graph { get; private set; } = null!;

    public GlyphAtlas Atlas { get; private set; } = null!;

    /// <summary>
    /// The composition target resource handle this frame's window is bound to (from
    /// <c>TargetSetRoot</c>). Zero for frames not registered with a WPF composition target
    /// (offscreen tests, direct graph injection).
    /// </summary>
    public uint TargetHandle { get; private set; }

    public void SetTargetHandle(uint handle)
    {
        TargetHandle = handle;
    }

    /// <summary>Resizes the swapchain presenter to the given window size.</summary>
    public void ResizePresenter(PixelSize size)
    {
        Presenter.Resize(size);
    }

    public void AdoptGraph(SlaveGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!ReferenceEquals(Graph, graph))
        {
            Graph = graph;
            WireBrushResources(graph);
        }
    }

    private void WireBrushResources(SlaveGraph graph)
    {
        graph.Presenter = Presenter;
        graph.OffscreenFactory = size => Device.CreateOffscreenPresenter(size);
    }

    /// <summary>True once <see cref="Pump"/> has consumed a quit or close-requested event.</summary>
    public bool Closing { get; private set; }

    /// <summary>
    /// Creates a headless frame: no SDL host, no window, offscreen presenter.
    /// <see cref="Pump"/> is a no-op returning <c>false</c>; <see cref="Host"/> and
    /// <see cref="Window"/> stay <see langword="null"/>.
    /// </summary>
    public static CompositionFrame CreateOffscreen(PixelSize size, VulkanDeviceOptions deviceOptions)
    {
        ArgumentNullException.ThrowIfNull(deviceOptions);
        if (size.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var frame = new CompositionFrame();
        try
        {
            frame.Device = new VulkanDevice(deviceOptions);
            frame.Presenter = frame.Device.CreateOffscreenPresenter(size);
            frame.Graph = new SlaveGraph();
            frame.WireBrushResources(frame.Graph);
            frame.Atlas = new GlyphAtlas(frame.Presenter, new PixelSize(AtlasPageSize, AtlasPageSize));
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        return frame;
    }

    /// <summary>
    /// Pumps one SDL event. Returns <c>false</c> when no event was consumed, when the
    /// frame is offscreen, when a quit event or a close-requested event for this frame's
    /// own window arrived (which sets <see cref="Closing"/>), or after that point. Events
    /// for other windows are consumed and returned so the presentation source can route
    /// them to their owning source. Window-resize events resize the presenter only when
    /// they name this frame's window.
    /// </summary>
    public bool Pump()
    {
        return TryPump(out _);
    }

    /// <summary>
    /// Same as <see cref="Pump"/> but yields the consumed event so a PresentationSource
    /// can feed WPF <c>InputManager</c>.
    /// </summary>
    public bool TryPump(out SdlEvent ev)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ev = default;
        if (Closing || Host is null)
        {
            return false;
        }

        if (!Host.Poll(out ev))
        {
            ev = default;
            return false;
        }

        return ProcessEvent(ev, out ev);
    }

    /// <summary>
    /// Same as <see cref="TryPump"/> but blocks up to <paramref name="timeoutMs"/>
    /// milliseconds for the next event. Returns <c>false</c> when the wait timed
    /// out, when the frame is offscreen/closing, or when a quit / own-window
    /// close-requested event was consumed (which sets <see cref="Closing"/>).
    /// </summary>
    public bool WaitEventTimeout(int timeoutMs, out SdlEvent ev)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ev = default;
        if (Closing || Host is null)
        {
            return false;
        }

        if (!Host.WaitEventTimeout(timeoutMs, out ev))
        {
            ev = default;
            return false;
        }

        return ProcessEvent(ev, out ev);
    }

    private bool ProcessEvent(SdlEvent ev, out SdlEvent result)
    {
        if (ev.Kind == SdlEventKind.Quit)
        {
            Closing = true;
            result = default;
            return false;
        }

        if (ev.Kind == SdlEventKind.WindowCloseRequested && IsOwnWindow(ev.Window))
        {
            Closing = true;
            result = default;
            return false;
        }

        if (ev.Kind == SdlEventKind.WindowResized && IsOwnWindow(ev.Window) && Window is not null)
        {
            Presenter.Resize(Window.PixelSize);
        }

        result = ev;
        return true;
    }

    /// <summary>
    /// Marks this frame as closing. Used when a close-requested event for this frame's
    /// window is consumed by another frame's pump and routed back to this frame's source.
    /// </summary>
    public void RequestClose()
    {
        Closing = true;
    }

    private bool IsOwnWindow(WindowHandle window)
    {
        return !window.IsValid || (Window is not null && window == Window.Handle);
    }

    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        Presenter.Render(commands => Graph.Rasterize(commands, Atlas, TargetHandle, Presenter));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // The graph may be shared with other frames (popups adopt the channel graph): its
        // per-presenter caches must not keep entries keyed by this frame's dying presenter,
        // or a later resource release would destroy textures on the disposed presenter.
        if (Graph is not null && Presenter is not null)
        {
            Graph.ForgetPresenter(Presenter);
        }

        DisposeCreated(Atlas);
        DisposeCreated(Presenter);
        if (_ownsDevice)
        {
            DisposeCreated(Device);
        }

        DisposeCreated(Window);
        if (_ownsHost)
        {
            DisposeCreated(Host);
        }
    }

    private static void DisposeCreated(IDisposable? value)
    {
        value?.Dispose();
    }

    private static VulkanDeviceOptions WithWindowExtensions(
        VulkanDeviceOptions deviceOptions,
        IReadOnlyList<string> windowExtensions)
    {
        return new VulkanDeviceOptions
        {
            Validation = deviceOptions.Validation,
            ApplicationName = deviceOptions.ApplicationName,
            EngineName = deviceOptions.EngineName,
            ExtraInstanceExtensions = [.. deviceOptions.ExtraInstanceExtensions
                .Concat(windowExtensions)
                .Distinct()],
            PreferredDeviceName = deviceOptions.PreferredDeviceName,
            PreferIntegratedGpu = deviceOptions.PreferIntegratedGpu,
            PresentMode = deviceOptions.PresentMode
        };
    }
}
