using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>Premultiplied or straight RGBA in 0..1. Matches <c>MilColorF</c> field order (r,g,b,a).</summary>
[PublicAPI]
public readonly struct ColorRgba(float r, float g, float b, float a) : IEquatable<ColorRgba>
{
    public float R { get; } = r;
    public float G { get; } = g;
    public float B { get; } = b;
    public float A { get; } = a;

    public static ColorRgba Transparent { get; } = new(0, 0, 0, 0);
    public static ColorRgba Black { get; } = new(0, 0, 0, 1);
    public static ColorRgba White { get; } = new(1, 1, 1, 1);

    public bool Equals(ColorRgba other)
    {
        return R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
    }

    public override bool Equals(object? obj)
    {
        return obj is ColorRgba other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(R, G, B, A);
    }

    public static bool operator ==(ColorRgba left, ColorRgba right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ColorRgba left, ColorRgba right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"rgba({R}, {G}, {B}, {A})";
    }
}
