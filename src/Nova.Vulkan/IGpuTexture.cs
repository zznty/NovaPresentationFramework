using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Vulkan;

[PublicAPI]
public interface IGpuTexture : IDisposable
{
    public TextureHandle Handle { get; }

    public PixelSize Size { get; }

    public PixelFormat Format { get; }
}
