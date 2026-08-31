using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Vulkan;

[PublicAPI]
public readonly ref struct TextureUpload
{
    public TextureUpload(PixelSize size, PixelFormat format, ReadOnlySpan<byte> pixels, int strideBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strideBytes);

        Size = size;
        Format = format;
        Pixels = pixels;
        StrideBytes = strideBytes;
    }

    public PixelSize Size { get; }
    public PixelFormat Format { get; }
    public ReadOnlySpan<byte> Pixels { get; }
    public int StrideBytes { get; }
}
