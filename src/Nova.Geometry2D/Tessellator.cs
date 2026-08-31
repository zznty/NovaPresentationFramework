using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// CPU tessellation of flattened contours into a triangle list (multiples of 3 points).
/// Simple (non-self-intersecting) single contours take a fast ear-clipping path; anything
/// else — self-intersecting contours, holes, multiple contours — goes through the
/// sweep-line tessellator (GLU/libtess2 algorithm, ported from LibTessDotNet), which
/// handles coincident vertices and arbitrary winding rules exactly.
/// </summary>
[PublicAPI]
public static class Tessellator
{
    private const double Epsilon = 1e-9;

    /// <summary>
    /// Number of points (multiples of 3) <see cref="Fill"/> would write for
    /// <paramref name="contour"/>. Exact for simple contours; for self-intersecting
    /// contours it runs the sweep once to count.
    /// </summary>
    public static int FillRequired(ReadOnlySpan<Point> contour, FillRule fillRule)
    {
        if (contour.Length < 3)
        {
            return 0;
        }

        Span<Point> buffer = contour.Length <= 512 ? stackalloc Point[contour.Length + 1] : new Point[contour.Length + 1];
        int m = CleanPoints(contour, buffer);
        if (m < 3)
        {
            return 0;
        }

        ReadOnlySpan<Point> cleaned = buffer[..m];
        return !IsSelfIntersecting(cleaned)
            ? (m - 2) * 3
            : SweepCount(cleaned, fillRule);
    }

    /// <summary>
    /// Number of points <see cref="FillPath"/> would write for the contour set (the sweep
    /// runs once to count).
    /// </summary>
    public static int FillPathRequired(ReadOnlySpan<Contour> contours, FillRule fillRule)
    {
        var tess = new Tess.Tess();
        foreach (Contour contour in contours)
        {
            if (contour.ReadOnlySpan.Length >= 3)
            {
                tess.AddContour(contour.ReadOnlySpan);
            }
        }

        return tess.TessellateCount(ToWindingRule(fillRule));
    }

    public static int Fill(ReadOnlySpan<Point> contour, FillRule fillRule, Span<Point> destination)
    {
        if (contour.Length < 3)
        {
            return 0;
        }

        Span<Point> buffer = contour.Length <= 512 ? stackalloc Point[contour.Length + 1] : new Point[contour.Length + 1];
        int m = CleanPoints(contour, buffer);
        if (m < 3)
        {
            return 0;
        }

        ReadOnlySpan<Point> cleaned = buffer[..m];
        if (!IsSelfIntersecting(cleaned))
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((m - 2) * 3, destination.Length, nameof(destination));
            return ClipEars(cleaned, fillRule, destination);
        }

        return Sweep(cleaned, fillRule, destination);
    }

    private static int Sweep(ReadOnlySpan<Point> contour, FillRule fillRule, Span<Point> destination)
    {
        var tess = new Tess.Tess();
        tess.AddContour(contour);
        return tess.Tessellate(destination, ToWindingRule(fillRule));
    }

    /// <summary>
    /// Tessellates a set of contours together under the fill rule (holes and overlaps are
    /// resolved by the sweep). Writes triangles (3 points each) into
    /// <paramref name="destination"/>; use <see cref="FillPathRequired"/> to size it.
    /// </summary>
    public static int FillPath(ReadOnlySpan<Contour> contours, FillRule fillRule, Span<Point> destination)
    {
        var tess = new Tess.Tess();
        foreach (Contour contour in contours)
        {
            if (contour.ReadOnlySpan.Length >= 3)
            {
                tess.AddContour(contour.ReadOnlySpan);
            }
        }

        return tess.Tessellate(destination, ToWindingRule(fillRule));
    }

    public static bool Contains(ReadOnlySpan<Point> contour, FillRule fillRule, Point point)
    {
        int winding = 0;
        for (int i = 0; i < contour.Length; i++)
        {
            Point a = contour[i];
            Point b = contour[(i + 1) % contour.Length];
            if (OnSegment(a, b, point))
            {
                return true;
            }

            if (a.Y <= point.Y)
            {
                if (b.Y > point.Y && Cross(a, b, point) > 0)
                {
                    winding++;
                }
            }
            else if (b.Y <= point.Y && Cross(a, b, point) < 0)
            {
                winding--;
            }
        }

        return fillRule switch
        {
            FillRule.EvenOdd => (winding & 1) != 0,
            FillRule.NonZero => winding != 0,
            _ => throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "Unknown fill rule."),
        };
    }

    private static Tess.WindingRule ToWindingRule(FillRule fillRule)
    {
        return fillRule switch
        {
            FillRule.EvenOdd => Tess.WindingRule.EvenOdd,
            FillRule.NonZero => Tess.WindingRule.NonZero,
            _ => throw new ArgumentOutOfRangeException(nameof(fillRule), fillRule, "Unknown fill rule."),
        };
    }

    private static int SweepCount(ReadOnlySpan<Point> contour, FillRule fillRule)
    {
        var tess = new Tess.Tess();
        tess.AddContour(contour);
        return tess.TessellateCount(ToWindingRule(fillRule));
    }

    private static int CleanPoints(ReadOnlySpan<Point> contour, Span<Point> buffer)
    {
        // Drop duplicate consecutive and wrap-around points (ear clipping and the sweep both
        // require a clean loop).
        int m = 0;
        for (int i = 0; i < contour.Length; i++)
        {
            if (m > 0 && ApproximatelyEqual(buffer[m - 1], contour[i]))
            {
                continue;
            }

            buffer[m++] = contour[i];
        }

        if (m > 1 && ApproximatelyEqual(buffer[0], buffer[m - 1]))
        {
            m--;
        }

        return m;
    }

    private static int ClipEars(ReadOnlySpan<Point> contour, FillRule fillRule, Span<Point> destination)
    {
        List<Point> polygon = [.. contour];
        if (PolygonArea(polygon) < 0)
        {
            polygon.Reverse();
        }

        if (fillRule == FillRule.NonZero)
        {
            RemoveCollinearVertices(polygon);
        }

        int written = 0;
        while (polygon.Count > 3)
        {
            if (!ClipOneEar(polygon, destination, ref written))
            {
                return 0;
            }
        }

        destination[written++] = polygon[0];
        destination[written++] = polygon[1];
        destination[written++] = polygon[2];
        return written;
    }

    private static bool ClipOneEar(List<Point> polygon, Span<Point> destination, ref int written)
    {
        int count = polygon.Count;
        for (int i = 0; i < count; i++)
        {
            Point prev = polygon[(i + count - 1) % count];
            Point cur = polygon[i];
            Point next = polygon[(i + 1) % count];
            if (Cross(prev, cur, next) <= 0)
            {
                continue;
            }

            if (polygon.Count > 3 && AnyPointInsideTriangle(prev, cur, next, polygon, i))
            {
                continue;
            }

            destination[written++] = prev;
            destination[written++] = cur;
            destination[written++] = next;
            polygon.RemoveAt(i);
            return true;
        }

        return false;
    }

    private static bool AnyPointInsideTriangle(Point a, Point b, Point c, List<Point> polygon, int earIndex)
    {
        int count = polygon.Count;
        int previous = (earIndex + count - 1) % count;
        int next = (earIndex + 1) % count;
        for (int i = 0; i < count; i++)
        {
            if (i == earIndex || i == previous || i == next || !PointInTriangle(polygon[i], a, b, c))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool PointInTriangle(Point p, Point a, Point b, Point c)
    {
        double d1 = Cross(a, b, p);
        double d2 = Cross(b, c, p);
        double d3 = Cross(c, a, p);
        bool hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static void RemoveCollinearVertices(List<Point> polygon)
    {
        for (int i = 0; i < polygon.Count;)
        {
            Point prev = polygon[(i + polygon.Count - 1) % polygon.Count];
            Point cur = polygon[i];
            Point next = polygon[(i + 1) % polygon.Count];
            if (Cross(prev, cur, next) == 0)
            {
                polygon.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>True when any pair of non-adjacent edges properly crosses or touches (a vertex on another edge).</summary>
    private static bool IsSelfIntersecting(ReadOnlySpan<Point> polygon)
    {
        int n = polygon.Length;
        if (n < 4)
        {
            return false;
        }

        for (int i = 0; i < n; i++)
        {
            Point a1 = polygon[i];
            Point a2 = polygon[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                {
                    continue;
                }

                Point b1 = polygon[j];
                Point b2 = polygon[(j + 1) % n];
                if (SegmentsProperlyCross(a1, a2, b1, b2))
                {
                    return true;
                }

                if (VertexOnEdge(a1, a2, b1) || VertexOnEdge(a1, a2, b2) ||
                    VertexOnEdge(b1, b2, a1) || VertexOnEdge(b1, b2, a2))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool VertexOnEdge(Point edgeStart, Point edgeEnd, Point vertex)
    {
        if (ApproximatelyEqual(vertex, edgeStart) || ApproximatelyEqual(vertex, edgeEnd))
        {
            return false;
        }

        double cross = ((edgeEnd.X - edgeStart.X) * (vertex.Y - edgeStart.Y)) - ((edgeEnd.Y - edgeStart.Y) * (vertex.X - edgeStart.X));
        return Math.Abs(cross) <= Epsilon &&
               (Math.Min(edgeStart.X, edgeEnd.X) - Epsilon) <= vertex.X && vertex.X <= (Math.Max(edgeStart.X, edgeEnd.X) + Epsilon) &&
               (Math.Min(edgeStart.Y, edgeEnd.Y) - Epsilon) <= vertex.Y && vertex.Y <= (Math.Max(edgeStart.Y, edgeEnd.Y) + Epsilon);
    }

    private static bool SegmentsProperlyCross(Point a1, Point a2, Point b1, Point b2)
    {
        double d1 = Cross2(b2.X - b1.X, b2.Y - b1.Y, a1.X - b1.X, a1.Y - b1.Y);
        double d2 = Cross2(b2.X - b1.X, b2.Y - b1.Y, a2.X - b1.X, a2.Y - b1.Y);
        double d3 = Cross2(a2.X - a1.X, a2.Y - a1.Y, b1.X - a1.X, b1.Y - a1.Y);
        double d4 = Cross2(a2.X - a1.X, a2.Y - a1.Y, b2.X - a1.X, b2.Y - a1.Y);
        return ((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) &&
               ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon));
    }

    private static double Cross2(double ax, double ay, double bx, double by)
    {
        return (ax * by) - (ay * bx);
    }

    private static double PolygonArea(List<Point> polygon)
    {
        double twiceArea = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point a = polygon[i];
            Point b = polygon[(i + 1) % polygon.Count];
            twiceArea += (a.X * b.Y) - (b.X * a.Y);
        }

        return twiceArea * 0.5;
    }

    private static double Cross(Point a, Point b, Point c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static bool OnSegment(Point a, Point b, Point p)
    {
        return Cross(a, b, p) == 0
            && Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X)
            && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);
    }

    private static bool ApproximatelyEqual(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) <= Epsilon && Math.Abs(a.Y - b.Y) <= Epsilon;
    }
}
