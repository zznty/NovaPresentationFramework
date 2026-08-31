using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Axis-aligned bounds of a MIL path-geometry stream, computed by flattening curves and
/// unioning figure bounds. Replaces the milcore <c>MilUtility_PathGeometryBounds</c> nest
/// call on Linux (see patches/0005). Matches the caller contract in
/// <c>PathGeometry.GetPathBoundsAsRB</c>:
/// <list type="bullet">
/// <item>fill bounds: union of the flattened point bounds of every fillable figure</item>
/// <item>stroke: every figure's bounds widened by <c>thickness/2</c> (round/join caps not
///   modeled — see report; square caps add the full thickness, miter joins add
///   <c>thickness/2</c> past the corner for right angles)</item>
/// <item>world transform applied to the flattened points before union</item>
/// <item>degenerate single-point figures contribute their point</item>
/// </list>
/// </summary>
[PublicAPI]
public static class PathBounds
{
    /// <summary>Bounds of a single closed/unclosed contour of flattened points.</summary>
    public static Rect OfContour(ReadOnlySpan<Point> contour)
    {
        if (contour.Length == 0)
        {
            return Rect.Empty;
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Point point in contour)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Bounds of a path stream with optional pen thickness and world transform. Non-fillable
    /// (gap) figures are skipped only when <paramref name="skipHollows"/> is set.
    /// </summary>
    public static Rect OfPath(
        ReadOnlySpan<byte> stream,
        double strokeThickness = 0,
        Matrix3x2 world = default,
        bool skipHollows = false)
    {
        if (MilPathData.IsEmpty(stream))
        {
            return Rect.Empty;
        }

        double half = strokeThickness * 0.5;
        bool applyTransform = !world.IsIdentity && world != default;
        Rect? result = null;
        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream, MilPathFlattener.DefaultTolerance);
        foreach (Contour contour in contours)
        {
            if (skipHollows && !contour.IsFilled)
            {
                continue;
            }

            Rect bounds = contour.Bounds(world);
            if (bounds.IsEmpty && contour.ReadOnlySpan.Length == 1)
            {
                // Degenerate single-point figure: contribute the point (or its stroke disk).
                Point point = applyTransform ? world.Transform(contour.ReadOnlySpan[0]) : contour.ReadOnlySpan[0];
                bounds = new Rect(point.X, point.Y, 0, 0);
            }

            if (strokeThickness > 0 && !bounds.IsEmpty)
            {
                bounds = new Rect(
                    bounds.X - half,
                    bounds.Y - half,
                    bounds.Width + strokeThickness,
                    bounds.Height + strokeThickness);
            }

            if (!bounds.IsEmpty)
            {
                result = result is { } current ? Union(current, bounds) : bounds;
            }
        }

        return result ?? Rect.Empty;
    }

    private static Rect Union(Rect a, Rect b)
    {
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        double right = Math.Max(a.Right, b.Right);
        double bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }
}
