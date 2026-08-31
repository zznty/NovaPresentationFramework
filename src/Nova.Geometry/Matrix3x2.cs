using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>Affine 3x2 matrix. Matches <c>MilMatrix3x2D</c> field order (M11, M12, M21, M22, OffsetX, OffsetY).</summary>
[PublicAPI]
public readonly struct Matrix3x2(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    : IEquatable<Matrix3x2>
{
    public double M11 { get; } = m11;
    public double M12 { get; } = m12;
    public double M21 { get; } = m21;
    public double M22 { get; } = m22;
    public double OffsetX { get; } = offsetX;
    public double OffsetY { get; } = offsetY;

    public static Matrix3x2 Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => Equals(Identity);

    public Point Transform(Point point)
    {
        return new Point((M11 * point.X) + (M21 * point.Y) + OffsetX, (M12 * point.X) + (M22 * point.Y) + OffsetY);
    }

    public static Matrix3x2 Translate(double offsetX, double offsetY)
    {
        return new Matrix3x2(1, 0, 0, 1, offsetX, offsetY);
    }

    public static Matrix3x2 Scale(double scaleX, double scaleY)
    {
        return new Matrix3x2(scaleX, 0, 0, scaleY, 0, 0);
    }

    public static Matrix3x2 Multiply(Matrix3x2 a, Matrix3x2 b)
    {
        return new Matrix3x2(
            (a.M11 * b.M11) + (a.M12 * b.M21),
            (a.M11 * b.M12) + (a.M12 * b.M22),
            (a.M21 * b.M11) + (a.M22 * b.M21),
            (a.M21 * b.M12) + (a.M22 * b.M22),
            (a.OffsetX * b.M11) + (a.OffsetY * b.M21) + b.OffsetX,
            (a.OffsetX * b.M12) + (a.OffsetY * b.M22) + b.OffsetY);
    }

    public bool Equals(Matrix3x2 other)
    {
        return M11.Equals(other.M11) && M12.Equals(other.M12) &&
               M21.Equals(other.M21) && M22.Equals(other.M22) &&
               OffsetX.Equals(other.OffsetX) && OffsetY.Equals(other.OffsetY);
    }

    public override bool Equals(object? obj)
    {
        return obj is Matrix3x2 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);
    }

    public static bool operator ==(Matrix3x2 left, Matrix3x2 right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Matrix3x2 left, Matrix3x2 right)
    {
        return !left.Equals(right);
    }
}
