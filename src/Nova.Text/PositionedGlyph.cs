using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Text;

[PublicAPI]
public readonly struct PositionedGlyph(GlyphId id, Point origin, Size advance) : IEquatable<PositionedGlyph>
{
    public GlyphId Id { get; } = id;

    public Point Origin { get; } = origin;

    public Size Advance { get; } = advance;

    public bool Equals(PositionedGlyph other)
    {
        return Id == other.Id && Origin == other.Origin && Advance == other.Advance;
    }

    public override bool Equals(object? obj)
    {
        return obj is PositionedGlyph other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Origin, Advance);
    }

    public static bool operator ==(PositionedGlyph left, PositionedGlyph right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PositionedGlyph left, PositionedGlyph right)
    {
        return !left.Equals(right);
    }
}
