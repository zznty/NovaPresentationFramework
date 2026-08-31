using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>A signed 2-D delta (relative displacement). Distinct from <see cref="Size"/>,
/// which is non-negative: deltas carry direction, e.g. SDL mouse-motion relative
/// deltas (<c>Xrel</c>/<c>Yrel</c>) and wheel scroll deltas (negative Y scrolls down).
/// Matches the WPF <c>System.Windows.Vector</c> layout (two doubles).</summary>
[PublicAPI]
public readonly struct Vector(double x, double y) : IEquatable<Vector>
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public static Vector Zero { get; } = new(0, 0);

    public bool Equals(Vector other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y);
    }

    public override bool Equals(object? obj)
    {
        return obj is Vector other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public static bool operator ==(Vector left, Vector right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Vector left, Vector right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }
}
