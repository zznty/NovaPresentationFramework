using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Vulkan;

[PublicAPI]
public interface IVulkanPresenter : IDisposable
{
    public PixelSize PixelSize { get; }

    public IGpuTexture CreateTexture(TextureUpload upload);

    public void UpdateTexture(TextureHandle texture, int destinationX, int destinationY, TextureUpload upload);

    public void DestroyTexture(TextureHandle texture);

    public void Resize(PixelSize size);

    public void Render(Action<IRasterCommandList> record);

    /// <summary>
    /// Opt-in pixel readback for window (surface) presenters: once enabled, each rendered
    /// frame is copied into a host-visible staging buffer so <see cref="ReadbackRgba"/> can
    /// return it. A no-op for offscreen presenters, where readback is always available.
    /// Throws a <see cref="VulkanException"/> when the window surface does not advertise
    /// transfer-source swapchain image usage.
    /// </summary>
    public void EnableReadback();

    /// <summary>
    /// Returns the pixels of the most recently rendered frame as tightly packed R,G,B,A bytes,
    /// row-major with <c>Width * 4</c> bytes per scanline, top-to-bottom (WPF Y-down). For
    /// window presenters, <see cref="EnableReadback"/> must have been called first and at least
    /// one frame rendered, or an <see cref="InvalidOperationException"/> is thrown.
    /// </summary>
    public ReadOnlyMemory<byte> ReadbackRgba();
}
