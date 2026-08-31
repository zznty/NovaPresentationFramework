using JetBrains.Annotations;

namespace Nova.Text;

/// <summary>Identity of one rasterized glyph at a quantized pixel size.</summary>
[PublicAPI]
public readonly struct GlyphId(uint faceId, uint glyphIndex, int pixelSize) : IEquatable<GlyphId>
{
    public uint FaceId { get; } = faceId;

    public uint GlyphIndex { get; } = glyphIndex;

    public int PixelSize { get; } = pixelSize;

    public bool Equals(GlyphId other)
    {
        return FaceId == other.FaceId && GlyphIndex == other.GlyphIndex && PixelSize == other.PixelSize;
    }

    public override bool Equals(object? obj)
    {
        return obj is GlyphId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FaceId, GlyphIndex, PixelSize);
    }

    public static bool operator ==(GlyphId left, GlyphId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GlyphId left, GlyphId right)
    {
        return !left.Equals(right);
    }
}
