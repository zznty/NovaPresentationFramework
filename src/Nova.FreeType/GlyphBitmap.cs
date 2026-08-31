using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.FreeType;

[PublicAPI]
public readonly struct GlyphBitmap : IEquatable<GlyphBitmap>
{
    public GlyphBitmap(PixelSize size, int left, int top, int pitch, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Size = size;
        Left = left;
        Top = top;
        Pitch = pitch;
        Pixels = pixels;
    }

    public PixelSize Size { get; }
    public int Left { get; }
    public int Top { get; }
    public int Pitch { get; }
    public ReadOnlyMemory<byte> Pixels { get; }

    public bool Equals(GlyphBitmap other)
    {
        return Size == other.Size && Left == other.Left && Top == other.Top && Pitch == other.Pitch && Pixels.Equals(other.Pixels);
    }

    public override bool Equals(object? obj)
    {
        return obj is GlyphBitmap other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Size, Left, Top, Pitch, Pixels);
    }

    public static bool operator ==(GlyphBitmap left, GlyphBitmap right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GlyphBitmap left, GlyphBitmap right)
    {
        return !left.Equals(right);
    }
}
