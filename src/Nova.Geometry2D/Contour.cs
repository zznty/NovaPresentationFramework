using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// A flattened contour: an ordered point list plus a closed flag and fillability. Open
/// contours are stroked polylines; closed contours are fillable loops. Points are double
/// precision (float only at GPU upload, per repo convention).
/// </summary>
[PublicAPI]
public sealed class Contour
{
    public Contour(bool isClosed, bool isFilled = true)
    {
        IsClosed = isClosed;
        IsFilled = isFilled;
    }

    public Contour(bool isClosed, bool isFilled, IEnumerable<Point> points)
    {
        IsClosed = isClosed;
        IsFilled = isFilled;
        Points.AddRange(points);
    }

    public bool IsClosed { get; }

    /// <summary>True when the figure contributes to fill (the native <c>IsFillable</c> flag).</summary>
    public bool IsFilled { get; }

    internal List<Point> Points { get; } = [];

    /// <summary>Mutable span over the points (for low-allocation consumers).</summary>
    public Span<Point> Span => CollectionsMarshal.AsSpan(Points);

    public ReadOnlySpan<Point> ReadOnlySpan => CollectionsMarshal.AsSpan(Points);

    /// <summary>Applies an affine transform to every point in place (a default/zero matrix is a no-op).</summary>
    public void Transform(Matrix3x2 matrix)
    {
        if (matrix.IsIdentity || matrix == default)
        {
            return;
        }

        Span<Point> span = Span;
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = matrix.Transform(span[i]);
        }
    }

    /// <summary>Axis-aligned bounds of the contour's points (transform applied to the input first if given).</summary>
    public Rect Bounds(Matrix3x2 transform = default)
    {
        ReadOnlySpan<Point> span = ReadOnlySpan;
        if (span.Length == 0)
        {
            return Rect.Empty;
        }

        // A default (zero) matrix means "no transform", matching the WPF nest convention
        // where an unset world matrix is passed as zero-initialized memory.
        bool applyTransform = !transform.IsIdentity && transform != default;
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Point point in span)
        {
            Point transformed = applyTransform ? transform.Transform(point) : point;
            minX = Math.Min(minX, transformed.X);
            minY = Math.Min(minY, transformed.Y);
            maxX = Math.Max(maxX, transformed.X);
            maxY = Math.Max(maxY, transformed.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
