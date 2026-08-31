using JetBrains.Annotations;

namespace Nova.Geometry2D;

/// <summary>Line join style. Values match WPF's <c>MIL_PEN_JOIN</c> for the nest bridge.</summary>
[PublicAPI]
public enum PenLineJoin
{
    Miter = 0,
    Bevel = 1,
    Round = 2,
}

/// <summary>Line cap style. Values match WPF's <c>MIL_PEN_CAP</c> for the nest bridge.</summary>
[PublicAPI]
public enum PenLineCap
{
    Flat = 0,
    Square = 1,
    Round = 2,
    Triangle = 3,
}

/// <summary>
/// Immutable stroke description used by <see cref="Widener"/> and the polygon bounds helpers.
/// Values mirror the native <c>MIL_PEN_DATA</c> fields the WPF nests pass across.
/// </summary>
[PublicAPI]
public readonly struct PenStyle(double thickness, PenLineJoin join, PenLineCap startCap, PenLineCap endCap, double miterLimit = 10.0)
    : IEquatable<PenStyle>
{
    /// <summary>Stroke width (positive).</summary>
    public double Thickness { get; } = thickness;

    /// <summary>Miter limit ratio (miter length / half thickness); exceeding it falls back to a bevel.</summary>
    public double MiterLimit { get; } = miterLimit;

    public PenLineJoin Join { get; } = join;

    public PenLineCap StartCap { get; } = startCap;

    public PenLineCap EndCap { get; } = endCap;

    /// <summary>Flat-capped, miter-joined pen of the given thickness (WPF defaults).</summary>
    public static PenStyle Flat(double thickness)
    {
        return new PenStyle(thickness, PenLineJoin.Miter, PenLineCap.Flat, PenLineCap.Flat);
    }

    public bool Equals(PenStyle other)
    {
        return Thickness.Equals(other.Thickness) && MiterLimit.Equals(other.MiterLimit) &&
               Join == other.Join && StartCap == other.StartCap && EndCap == other.EndCap;
    }

    public override bool Equals(object? obj)
    {
        return obj is PenStyle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Thickness, MiterLimit, Join, StartCap, EndCap);
    }

    public static bool operator ==(PenStyle left, PenStyle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PenStyle left, PenStyle right)
    {
        return !left.Equals(right);
    }
}
