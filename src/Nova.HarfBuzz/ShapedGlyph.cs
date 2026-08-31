using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.HarfBuzz;

[PublicAPI]
public readonly struct ShapedGlyph(uint glyphIndex, uint cluster, Point offset, Size advance)
    : IEquatable<ShapedGlyph>
{
    public uint GlyphIndex { get; } = glyphIndex;
    public uint Cluster { get; } = cluster;
    public Point Offset { get; } = offset;
    public Size Advance { get; } = advance;

    public bool Equals(ShapedGlyph other)
    {
        return GlyphIndex == other.GlyphIndex && Cluster == other.Cluster && Offset == other.Offset && Advance == other.Advance;
    }

    public override bool Equals(object? obj)
    {
        return obj is ShapedGlyph other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GlyphIndex, Cluster, Offset, Advance);
    }

    public static bool operator ==(ShapedGlyph left, ShapedGlyph right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ShapedGlyph left, ShapedGlyph right)
    {
        return !left.Equals(right);
    }
}
