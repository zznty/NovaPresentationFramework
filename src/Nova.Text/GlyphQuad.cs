using JetBrains.Annotations;
using Nova.Geometry;
using Nova.Vulkan;

namespace Nova.Text;

/// <summary>One packed glyph. UV is in 0..1 atlas space. Bearing is in pixels (FreeType left/top).</summary>
[PublicAPI]
public readonly struct GlyphQuad(
    TextureHandle texture,
    Rect uv,
    PixelSize size,
    int bearingX,
    int bearingY) : IEquatable<GlyphQuad>
{
    public TextureHandle Texture { get; } = texture;

    public Rect Uv { get; } = uv;

    public PixelSize Size { get; } = size;

    public int BearingX { get; } = bearingX;

    public int BearingY { get; } = bearingY;

    public bool Equals(GlyphQuad other)
    {
        return Texture == other.Texture &&
               Uv == other.Uv &&
               Size == other.Size &&
               BearingX == other.BearingX &&
               BearingY == other.BearingY;
    }

    public override bool Equals(object? obj)
    {
        return obj is GlyphQuad other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Texture, Uv, Size, BearingX, BearingY);
    }

    public static bool operator ==(GlyphQuad left, GlyphQuad right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GlyphQuad left, GlyphQuad right)
    {
        return !left.Equals(right);
    }
}
