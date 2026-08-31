using JetBrains.Annotations;

namespace Nova.FontConfig;

[PublicAPI]
public readonly struct FontMatch : IEquatable<FontMatch>
{
    public FontMatch(string family, string filePath, int faceIndex, int weight, int slant, int width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        Family = family;
        FilePath = filePath;
        FaceIndex = faceIndex;
        Weight = weight;
        Slant = slant;
        Width = width;
    }

    public string Family { get; }
    public string FilePath { get; }
    public int FaceIndex { get; }
    public int Weight { get; }
    public int Slant { get; }
    public int Width { get; }

    public bool Equals(FontMatch other)
    {
        return Family == other.Family &&
               FilePath == other.FilePath &&
               FaceIndex == other.FaceIndex &&
               Weight == other.Weight &&
               Slant == other.Slant &&
               Width == other.Width;
    }

    public override bool Equals(object? obj)
    {
        return obj is FontMatch other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Family, FilePath, FaceIndex, Weight, Slant, Width);
    }

    public static bool operator ==(FontMatch left, FontMatch right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FontMatch left, FontMatch right)
    {
        return !left.Equals(right);
    }
}
