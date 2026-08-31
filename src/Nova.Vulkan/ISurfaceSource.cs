using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Vulkan;

/// <summary>Windowing-owned surface factory. SDL implements this; Vulkan never talks to SDL.</summary>
[PublicAPI]
public interface ISurfaceSource
{
    public PixelSize PixelSize { get; }

    /// <summary>
    /// True when the window's pixels are composited with per-pixel alpha (e.g. an SDL
    /// window created with <c>SDL_WINDOW_TRANSPARENT</c>), so the swapchain must use a
    /// non-opaque composite-alpha mode. False for ordinary opaque windows.
    /// </summary>
    public bool PrefersTransparentComposite { get; }

    public IReadOnlyList<string> RequiredInstanceExtensions { get; }

    public SurfaceHandle CreateSurface(InstanceHandle instance);
}
