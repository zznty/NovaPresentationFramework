using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>A 2-D point in local space. Matches the WPF <c>System.Windows.Point</c> wire layout (two doubles).</summary>
[PublicAPI]
public readonly struct Point(double x, double y) : IEquatable<Point>
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public static Point Origin { get; } = new(0, 0);

    public bool Equals(Point other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y);
    }

    public override bool Equals(object? obj)
    {
        return obj is Point other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public static bool operator ==(Point left, Point right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Point left, Point right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}
