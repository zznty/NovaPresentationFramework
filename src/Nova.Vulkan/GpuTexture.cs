using Nova.Geometry;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>Owned GPU texture created by a presenter. Dispose is idempotent.</summary>
internal sealed class GpuTexture(
    VulkanPresenterBase owner,
    TextureHandle handle,
    PixelSize size,
    PixelFormat format,
    ImageHandle image,
    ImageViewHandle view,
    DeviceMemoryHandle memory,
    DescriptorSetHandle descriptorSet) : IGpuTexture
{
    public TextureHandle Handle { get; } = handle;

    public PixelSize Size { get; } = size;

    public PixelFormat Format { get; } = format;

    internal ImageHandle Image { get; } = image;

    internal ImageViewHandle View { get; } = view;

    internal DeviceMemoryHandle Memory { get; } = memory;

    internal DescriptorSetHandle DescriptorSet { get; } = descriptorSet;

    internal bool IsDestroyed { get; set; }

    public void Dispose()
    {
        if (IsDestroyed)
        {
            return;
        }

        owner.DestroyTextureResources(this);
        IsDestroyed = true;
    }
}
