using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Outline normalization: converts a set of contours into an even-odd-fillable form. The
/// raster fills with even-odd, so self-overlapping or nested contours already fill
/// correctly; this pass only drops degenerate vertices and loops (duplicate points,
/// zero-length edges, fewer-than-3-point loops) so downstream tessellation is safe.
/// </summary>
[PublicAPI]
public static class Outliner
{
    public static IReadOnlyList<Contour> Outline(IEnumerable<Contour> contours)
    {
        ArgumentNullException.ThrowIfNull(contours);
        var result = new List<Contour>();
        foreach (Contour contour in contours)
        {
            if (Outline(contour) is { } cleaned && cleaned.Points.Count >= 3)
            {
                result.Add(cleaned);
            }
        }

        return result;
    }

    /// <summary>Returns a cleaned copy of the contour, or null when it degenerates.</summary>
    public static Contour? Outline(Contour contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        ReadOnlySpan<Point> span = contour.ReadOnlySpan;
        if (span.Length < 3)
        {
            return null;
        }

        var cleaned = new Contour(contour.IsClosed);
        List<Point> dst = cleaned.Points;
        Point? last = null;
        foreach (Point point in span)
        {
            if (last is { } previous && Approximately(previous, point))
            {
                continue;
            }

            dst.Add(point);
            last = point;
        }

        if (contour.IsClosed && dst.Count > 1 && Approximately(dst[0], dst[^1]))
        {
            dst.RemoveAt(dst.Count - 1);
        }

        return dst.Count >= 3 ? cleaned : null;
    }

    private static bool Approximately(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) <= 1e-9 && Math.Abs(a.Y - b.Y) <= 1e-9;
    }
}
