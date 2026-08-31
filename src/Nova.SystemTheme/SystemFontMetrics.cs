using JetBrains.Annotations;

namespace Nova.SystemTheme;

/// <summary>Win32 <c>LOGFONT</c> fields WPF reads from non-client metrics.</summary>
[PublicAPI]
public readonly struct SystemFontMetrics(string faceName, int height, int weight) : IEquatable<SystemFontMetrics>
{
    public string FaceName { get; } = faceName;
    public int Height { get; } = height;
    public int Weight { get; } = weight;

    public bool Equals(SystemFontMetrics other)
    {
        return FaceName == other.FaceName && Height == other.Height && Weight == other.Weight;
    }

    public override bool Equals(object? obj)
    {
        return obj is SystemFontMetrics other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FaceName, Height, Weight);
    }

    public static bool operator ==(SystemFontMetrics left, SystemFontMetrics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SystemFontMetrics left, SystemFontMetrics right)
    {
        return !left.Equals(right);
    }
}
