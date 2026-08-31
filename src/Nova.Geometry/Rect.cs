using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>
/// Axis-aligned rectangle in local space. Matches the WPF <c>System.Windows.Rect</c>
/// wire layout (X, Y, Width, Height as doubles). MIL <c>MilRectD</c> is left/top/right/bottom
/// and is converted at the parser boundary.
/// </summary>
[PublicAPI]
public readonly struct Rect(double x, double y, double width, double height) : IEquatable<Rect>
{
    public Rect(Point location, Size size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width;
    public double Height { get; } = height;

    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public Point Location => new(X, Y);

    public Size Size => new(Width, Height);

    public static Rect Empty { get; } = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(Point point)
    {
        return point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }

    public bool Equals(Rect other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is Rect other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }

    public static bool operator ==(Rect left, Rect right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Rect left, Rect right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"{X},{Y} {Width}x{Height}";
    }
}
