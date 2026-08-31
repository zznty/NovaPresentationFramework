using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>A non-negative 2-D size. Matches the WPF <c>System.Windows.Size</c> wire layout (two doubles).</summary>
[PublicAPI]
public readonly struct Size(double width, double height) : IEquatable<Size>
{
    public double Width { get; } = width;
    public double Height { get; } = height;

    public static Size Empty { get; } = new(0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Equals(Size other)
    {
        return Width.Equals(other.Width) && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is Size other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height);
    }

    public static bool operator ==(Size left, Size right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Size left, Size right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"{Width}x{Height}";
    }
}
