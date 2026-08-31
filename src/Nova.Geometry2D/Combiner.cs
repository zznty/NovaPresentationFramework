using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>Boolean geometry operations. Values match WPF <c>GeometryCombineMode</c>.</summary>
[PublicAPI]
public enum GeometryCombineMode
{
    Union = 0,
    Intersect = 1,
    Xor = 2,
    Exclude = 3,
}

/// <summary>
/// Boolean combine of two closed, flattened contours. Output contours are filled with
/// even-odd (a ring is emitted as outer + inner loops). Axis-aligned rectangles take a
/// fast exact path (the WPF <c>GetLayoutClip</c> inputs are always rectangles); other
/// polygons go through Greiner-Hormann clipping.
/// </summary>
[PublicAPI]
public static class Combiner
{
    private const double Epsilon = 1e-9;

    public static IReadOnlyList<Contour> Combine(ReadOnlySpan<Point> a, ReadOnlySpan<Point> b, GeometryCombineMode mode)
    {
        var result = new List<Contour>();

        // Normalize inputs: drop trailing/duplicate points, require a closed loop.
        Span<Point> bufferA = a.Length <= 256 ? stackalloc Point[a.Length + 1] : new Point[a.Length + 1];
        Span<Point> bufferB = b.Length <= 256 ? stackalloc Point[b.Length + 1] : new Point[b.Length + 1];
        NormalizeLoop(a, bufferA, out int aCount);
        NormalizeLoop(b, bufferB, out int bCount);
        ReadOnlySpan<Point> pa = bufferA[..aCount];
        ReadOnlySpan<Point> pb = bufferB[..bCount];

        if (pa.Length < 3 || pb.Length < 3)
        {
            if (mode is GeometryCombineMode.Union or GeometryCombineMode.Xor)
            {
                if (pa.Length >= 3)
                {
                    result.Add(new Contour(isClosed: true, isFilled: true, pa.ToArray()));
                }

                if (pb.Length >= 3)
                {
                    result.Add(new Contour(isClosed: true, isFilled: true, pb.ToArray()));
                }
            }
            else if (mode == GeometryCombineMode.Exclude && pa.Length >= 3)
            {
                result.Add(new Contour(isClosed: true, isFilled: true, pa.ToArray()));
            }

            return result;
        }

        // Rect fast path (axis-aligned inputs are exact and robust).
        if (TryGetRect(pa, out Rect ra) && TryGetRect(pb, out Rect rb))
        {
            CombineRects(ra, rb, mode, result);
            return result;
        }

        // Bounds disjoint: trivial results.
        Rect ba = Bounds(pa);
        Rect bb = Bounds(pb);
        if (!IntersectsOrTouches(ba, bb))
        {
            switch (mode)
            {
                case GeometryCombineMode.Union:
                case GeometryCombineMode.Xor:
                    result.Add(new Contour(isClosed: true, isFilled: true, pa.ToArray()));
                    result.Add(new Contour(isClosed: true, isFilled: true, pb.ToArray()));
                    break;
                case GeometryCombineMode.Intersect:
                    break;
                case GeometryCombineMode.Exclude:
                    result.Add(new Contour(isClosed: true, isFilled: true, pa.ToArray()));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown combine mode.");
            }

            return result;
        }

        GreinerHormann.Combine(pa, pb, mode, result);
        return result;
    }

    private static void CombineRects(Rect a, Rect b, GeometryCombineMode mode, List<Contour> result)
    {
        switch (mode)
        {
            case GeometryCombineMode.Union:
                {
                    if (IntersectsOrTouches(a, b))
                    {
                        result.Add(RectContour(UnionRects(a, b)));
                    }
                    else
                    {
                        result.Add(RectContour(a));
                        result.Add(RectContour(b));
                    }

                    break;
                }

            case GeometryCombineMode.Intersect:
                {
                    Rect intersection = a;
                    intersection = IntersectRect(intersection, b);
                    if (!intersection.IsEmpty)
                    {
                        result.Add(RectContour(intersection));
                    }

                    break;
                }

            case GeometryCombineMode.Exclude:
                SubtractRects(a, b, result);
                break;

            case GeometryCombineMode.Xor:
                SubtractRects(a, b, result);
                SubtractRects(b, a, result);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown combine mode.");
        }
    }

    /// <summary>Difference a\b decomposed into disjoint axis-aligned rectangles (0-4).</summary>
    private static void SubtractRects(Rect a, Rect b, List<Contour> result)
    {
        if (a.IsEmpty || b.IsEmpty || !IntersectsOrTouches(a, b))
        {
            if (!a.IsEmpty)
            {
                result.Add(RectContour(a));
            }

            return;
        }

        Rect intersection = a;
        intersection = IntersectRect(intersection, b);
        if (intersection.IsEmpty)
        {
            result.Add(RectContour(a));
            return;
        }

        if (AlmostEqual(intersection.Left, a.Left) && AlmostEqual(intersection.Top, a.Top) &&
            AlmostEqual(intersection.Right, a.Right) && AlmostEqual(intersection.Bottom, a.Bottom))
        {
            return; // b covers a entirely
        }

        // Up to four slabs around the intersection.
        if (intersection.Top > a.Top)
        {
            result.Add(RectContour(new Rect(a.Left, a.Top, a.Width, intersection.Top - a.Top)));
        }

        if (intersection.Bottom < a.Bottom)
        {
            result.Add(RectContour(new Rect(a.Left, intersection.Bottom, a.Width, a.Bottom - intersection.Bottom)));
        }

        if (intersection.Left > a.Left)
        {
            result.Add(RectContour(new Rect(a.Left, intersection.Top, intersection.Left - a.Left, intersection.Height)));
        }

        if (intersection.Right < a.Right)
        {
            result.Add(RectContour(new Rect(intersection.Right, intersection.Top, a.Right - intersection.Right, intersection.Height)));
        }
    }

    private static Contour RectContour(Rect rect)
    {
        return new Contour(
            isClosed: true,
            isFilled: true,
            [
                new Point(rect.Left, rect.Top),
                new Point(rect.Right, rect.Top),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Left, rect.Bottom),
            ]);
    }

    private static Rect UnionRects(Rect a, Rect b)
    {
        return new Rect(
            Math.Min(a.Left, b.Left),
            Math.Min(a.Top, b.Top),
            Math.Max(a.Right, b.Right) - Math.Min(a.Left, b.Left),
            Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top));
    }

    private static Rect IntersectRect(Rect a, Rect b)
    {
        double left = Math.Max(a.Left, b.Left);
        double top = Math.Max(a.Top, b.Top);
        double right = Math.Min(a.Right, b.Right);
        double bottom = Math.Min(a.Bottom, b.Bottom);
        return right <= left || bottom <= top ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }

    private static bool IntersectsOrTouches(Rect a, Rect b)
    {
        return a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;
    }

    /// <summary>True when the contour is an axis-aligned rectangle (4 points, right angles).</summary>
    private static bool TryGetRect(ReadOnlySpan<Point> contour, out Rect rect)
    {
        rect = default;
        if (contour.Length != 4)
        {
            return false;
        }

        Point p0 = contour[0];
        Point p1 = contour[1];
        Point p2 = contour[2];
        Point p3 = contour[3];
        bool axisAligned =
            (AlmostEqual(p0.Y, p1.Y) && AlmostEqual(p1.X, p2.X) && AlmostEqual(p2.Y, p3.Y) && AlmostEqual(p3.X, p0.X)) ||
            (AlmostEqual(p0.X, p1.X) && AlmostEqual(p1.Y, p2.Y) && AlmostEqual(p2.X, p3.X) && AlmostEqual(p3.Y, p0.Y));
        if (!axisAligned)
        {
            return false;
        }

        double left = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double top = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double right = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double bottom = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        rect = new Rect(left, top, right - left, bottom - top);
        return true;
    }

    /// <summary>Removes duplicate consecutive (and wrap-around) points into <paramref name="buffer"/>; returns the count.</summary>
    private static void NormalizeLoop(ReadOnlySpan<Point> contour, Span<Point> buffer, out int count)
    {
        int m = 0;
        for (int i = 0; i < contour.Length; i++)
        {
            if (m > 0 && AlmostEqual(buffer[m - 1], contour[i]))
            {
                continue;
            }

            buffer[m++] = contour[i];
        }

        // Drop the wrap-around duplicate.
        if (m > 1 && AlmostEqual(buffer[0], buffer[m - 1]))
        {
            m--;
        }

        count = m;
    }

    private static Rect Bounds(ReadOnlySpan<Point> contour)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Point p in contour)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool AlmostEqual(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) <= Epsilon && Math.Abs(a.Y - b.Y) <= Epsilon;
    }

    private static bool AlmostEqual(double a, double b)
    {
        return Math.Abs(a - b) <= Epsilon;
    }
}
