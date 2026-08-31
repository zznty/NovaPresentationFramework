using JetBrains.Annotations;

namespace Nova.FreeType;

[PublicAPI]
public readonly struct FontFaceMetrics(double unitsPerEm, double ascent, double descent, double lineGap, ushort glyphCount)
    : IEquatable<FontFaceMetrics>
{
    public double UnitsPerEm { get; } = unitsPerEm;
    public double Ascent { get; } = ascent;
    public double Descent { get; } = descent;
    public double LineGap { get; } = lineGap;
    public ushort GlyphCount { get; } = glyphCount;

    public bool Equals(FontFaceMetrics other)
    {
        return UnitsPerEm.Equals(other.UnitsPerEm) &&
               Ascent.Equals(other.Ascent) &&
               Descent.Equals(other.Descent) &&
               LineGap.Equals(other.LineGap) &&
               GlyphCount == other.GlyphCount;
    }

    public override bool Equals(object? obj)
    {
        return obj is FontFaceMetrics other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(UnitsPerEm, Ascent, Descent, LineGap, GlyphCount);
    }

    public static bool operator ==(FontFaceMetrics left, FontFaceMetrics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FontFaceMetrics left, FontFaceMetrics right)
    {
        return !left.Equals(right);
    }
}
