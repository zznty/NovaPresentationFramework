using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>Host-visible pixel layouts used by the raster and font atlas.</summary>
[PublicAPI]
public enum PixelFormat
{
    R8Unorm,
    Bgra8Unorm,
    Rgba8Unorm
}
