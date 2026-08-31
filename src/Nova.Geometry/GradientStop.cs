using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>One gradient stop: a color at a 0..1 position along the gradient axis.</summary>
[PublicAPI]
public readonly struct GradientStop(double position, ColorRgba color) : IEquatable<GradientStop>
{
    public double Position { get; } = position;

    public ColorRgba Color { get; } = color;

    public bool Equals(GradientStop other)
    {
        return Position.Equals(other.Position) && Color.Equals(other.Color);
    }

    public override bool Equals(object? obj)
    {
        return obj is GradientStop other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Position, Color);
    }

    public static bool operator ==(GradientStop left, GradientStop right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GradientStop left, GradientStop right)
    {
        return !left.Equals(right);
    }
}
