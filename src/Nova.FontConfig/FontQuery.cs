using JetBrains.Annotations;

namespace Nova.FontConfig;

[PublicAPI]
public readonly struct FontQuery : IEquatable<FontQuery>
{
    public FontQuery(string family, int weight = 80, int slant = 0, int width = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        Family = family;
        Weight = weight;
        Slant = slant;
        Width = width;
    }

    public string Family { get; }
    public int Weight { get; }
    public int Slant { get; }
    public int Width { get; }

    public bool Equals(FontQuery other)
    {
        return Family == other.Family && Weight == other.Weight && Slant == other.Slant && Width == other.Width;
    }

    public override bool Equals(object? obj)
    {
        return obj is FontQuery other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Family, Weight, Slant, Width);
    }

    public static bool operator ==(FontQuery left, FontQuery right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FontQuery left, FontQuery right)
    {
        return !left.Equals(right);
    }
}
