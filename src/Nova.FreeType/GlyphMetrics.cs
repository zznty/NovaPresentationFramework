using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.FreeType;

[PublicAPI]
public readonly struct GlyphMetrics(uint glyphIndex, Size advance, Rect bounds) : IEquatable<GlyphMetrics>
{
    public uint GlyphIndex { get; } = glyphIndex;
    public Size Advance { get; } = advance;
    public Rect Bounds { get; } = bounds;

    public bool Equals(GlyphMetrics other)
    {
        return GlyphIndex == other.GlyphIndex && Advance == other.Advance && Bounds == other.Bounds;
    }

    public override bool Equals(object? obj)
    {
        return obj is GlyphMetrics other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GlyphIndex, Advance, Bounds);
    }

    public static bool operator ==(GlyphMetrics left, GlyphMetrics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GlyphMetrics left, GlyphMetrics right)
    {
        return !left.Equals(right);
    }
}
