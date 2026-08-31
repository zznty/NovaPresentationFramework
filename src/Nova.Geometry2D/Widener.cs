using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Stroke widening: converts a flattened polyline or closed contour into fillable outline
/// loops honoring thickness, joins (miter with miter-limit fallback, bevel, round) and caps
/// (flat, square, round, triangle). Output contours are closed loops ready for
/// <see cref="Tessellator.Fill"/> (even-odd).
/// </summary>
[PublicAPI]
public static class Widener
{
    private const double HalfPi = Math.PI * 0.5;
    private const double TwoPi = Math.PI * 2.0;
    private const double QuarterPi = Math.PI * 0.25;
    private const double MinLength = 1e-12;

    /// <summary>
    /// Widens an open polyline into a single closed outline loop. Degenerate inputs (fewer
    /// than two distinct points, non-positive thickness) produce an empty contour.
    /// </summary>
    public static Contour WidenOpen(ReadOnlySpan<Point> polyline, PenStyle pen)
    {
        var result = new Contour(isClosed: true);
        int n = polyline.Length;
        double half = pen.Thickness * 0.5;
        if (n < 2 || half <= 0)
        {
            return result;
        }

        // Normalize out duplicate consecutive points so direction math stays valid.
        Span<Point> points = n <= 512 ? stackalloc Point[n] : new Point[n];
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            if (m > 0 && Approximately(points[m - 1], polyline[i]))
            {
                continue;
            }

            points[m++] = polyline[i];
        }

        if (m < 2)
        {
            return result;
        }

        Span<Point> dirs = (m - 1) <= 512 ? stackalloc Point[m - 1] : new Point[m - 1];
        for (int i = 0; i < m - 1; i++)
        {
            dirs[i] = Normalize(Subtract(points[i + 1], points[i]));
        }

        List<Point> dst = result.Points;

        // Left boundary forward: endpoint offset, then per-vertex joins, then the far-end
        // offset. Each vertex contributes its miter point, bevel pair, or round arc.
        dst.Add(Offset(points[0], Perp(dirs[0]), half));
        for (int i = 1; i < m - 1; i++)
        {
            AppendLeftJoin(dst, points[i], dirs[i - 1], dirs[i], pen, half);
        }

        dst.Add(Offset(points[m - 1], Perp(dirs[m - 2]), half));

        // End cap: from the last left point around points[m-1] to the last right point.
        AppendEndCap(dst, points[m - 1], dirs[m - 2], pen, half);

        // Right boundary backward: far-end right offset, then mirrored joins, then start.
        dst.Add(Offset(points[m - 1], Perp(dirs[m - 2]), -half));
        for (int i = m - 2; i >= 1; i--)
        {
            AppendRightJoin(dst, points[i], dirs[i - 1], dirs[i], pen, half);
        }

        dst.Add(Offset(points[0], Perp(dirs[0]), -half));

        // Start cap: back around points[0] to the first left point (closes the loop).
        AppendStartCap(dst, points[0], dirs[0], pen, half);
        return result;
    }

    /// <summary>
    /// Widens a closed contour into an outer loop plus an inner loop (the ring). When the
    /// contour is narrower than the stroke the inner loop collapses and is omitted. Fill
    /// both loops with the same color and even-odd to get a correct ring.
    /// </summary>
    public static (Contour Outer, Contour? Inner) WidenClosed(ReadOnlySpan<Point> contour, PenStyle pen)
    {
        var outer = new Contour(isClosed: true);
        double half = pen.Thickness * 0.5;
        int n = contour.Length;
        if (n < 3 || half <= 0)
        {
            return (outer, null);
        }

        Span<Point> points = n <= 512 ? stackalloc Point[n] : new Point[n];
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            if (m > 0 && Approximately(points[m - 1], contour[i]))
            {
                continue;
            }

            points[m++] = contour[i];
        }

        if (m < 3)
        {
            return (outer, null);
        }

        // Orientation: for a CCW contour the interior lies on the left of each edge (Perp
        // points inside); the outer loop offsets against the interior, the inner along it.
        double area = SignedArea(points[..m]);
        double orientation = area >= 0 ? 1.0 : -1.0;

        BuildClosedLoop(points[..m], -half, orientation, pen, outer.Points);
        Contour inner = new(isClosed: true);
        BuildClosedLoop(points[..m], half, orientation, pen, inner.Points);

        // The inner loop is only valid when it stays a proper same-orientation loop; a
        // contour narrower than the stroke inverts or collapses it.
        ReadOnlySpan<Point> innerSpan = inner.ReadOnlySpan;
        if (innerSpan.Length >= 3)
        {
            double innerArea = SignedArea(innerSpan);
            if (Math.Sign(innerArea) == Math.Sign(area) && Math.Abs(innerArea) > 1e-9 * half * half)
            {
                return (outer, inner);
            }
        }

        return (outer, null);
    }

    private static void BuildClosedLoop(ReadOnlySpan<Point> points, double offset, double orientation, PenStyle pen, List<Point> dst)
    {
        int n = points.Length;
        Span<Point> dirs = n <= 512 ? stackalloc Point[n] : new Point[n];
        for (int i = 0; i < n; i++)
        {
            dirs[i] = Normalize(Subtract(points[(i + 1) % n], points[i]));
            if (IsZero(dirs[i]))
            {
                dirs[i] = dirs[(i + n - 1) % n];
            }
        }

        Span<Point> miter = n <= 512 ? stackalloc Point[n] : new Point[n];
        Span<bool> bevel = n <= 512 ? stackalloc bool[n] : new bool[n];
        for (int i = 0; i < n; i++)
        {
            Point inDir = dirs[(i + n - 1) % n];
            Point outDir = dirs[i];
            Point interior = Add(Perp(inDir), Perp(outDir));
            if (orientation < 0)
            {
                interior = Scale(interior, -1);
            }

            Point bisector = Normalize(interior);
            if (IsZero(bisector))
            {
                bisector = orientation > 0 ? Perp(inDir) : Scale(Perp(inDir), -1);
            }

            double dot = (inDir.X * outDir.X) + (inDir.Y * outDir.Y);
            double cosHalf = Math.Sqrt(Math.Max(0.0, (1.0 + dot) * 0.5));
            double sinHalf = Math.Sqrt(Math.Max(0.0, (1.0 - dot) * 0.5));
            double miterRatio = sinHalf < 1e-9 ? 1.0 : 1.0 / Math.Max(sinHalf, 1e-9);
            bevel[i] = miterRatio > Math.Max(pen.MiterLimit, 1.0);

            double scale = Math.Max(1.0, 1.0 / Math.Max(cosHalf, 1e-3));
            miter[i] = Offset(points[i], bisector, offset * scale);
        }

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            count += bevel[i] ? 2 : 1;
        }

        dst.Clear();
        dst.Capacity = Math.Max(dst.Capacity, count);
        for (int i = 0; i < n; i++)
        {
            if (bevel[i])
            {
                Point inDir = dirs[(i + n - 1) % n];
                Point outDir = dirs[i];
                Point nIn = orientation > 0 ? Perp(inDir) : Scale(Perp(inDir), -1);
                Point nOut = orientation > 0 ? Perp(outDir) : Scale(Perp(outDir), -1);
                dst.Add(Offset(points[i], nIn, offset));
                dst.Add(Offset(points[i], nOut, offset));
            }
            else
            {
                dst.Add(miter[i]);
            }
        }
    }

    /// <summary>Left join at an interior vertex between the incoming and outgoing edges.</summary>
    private static void AppendLeftJoin(List<Point> dst, Point vertex, Point inDir, Point outDir, PenStyle pen, double half)
    {
        Point nIn = Perp(inDir);
        Point nOut = Perp(outDir);
        Point leftOut = Offset(vertex, nIn, half);
        Point leftIn = Offset(vertex, nOut, half);
        switch (pen.Join)
        {
            case PenLineJoin.Miter:
                {
                    double dot = (inDir.X * outDir.X) + (inDir.Y * outDir.Y);
                    double sinHalf = Math.Sqrt(Math.Max(0.0, (1.0 - dot) * 0.5));
                    double miterRatio = sinHalf < 1e-9 ? 1.0 : 1.0 / Math.Max(sinHalf, 1e-9);
                    if (miterRatio > Math.Max(pen.MiterLimit, 1.0))
                    {
                        // Miter limit exceeded: bevel (straight edge between the two offsets).
                        dst.Add(leftOut);
                        dst.Add(leftIn);
                        break;
                    }

                    double cosHalf = Math.Sqrt(Math.Max(0.0, (1.0 + dot) * 0.5));
                    Point bisector = Normalize(Add(nIn, nOut));
                    if (IsZero(bisector))
                    {
                        dst.Add(leftOut);
                        dst.Add(leftIn);
                        break;
                    }

                    dst.Add(Offset(vertex, bisector, half / Math.Max(cosHalf, 1e-9)));
                    break;
                }

            case PenLineJoin.Bevel:
                dst.Add(leftOut);
                dst.Add(leftIn);
                break;

            case PenLineJoin.Round:
                dst.Add(leftOut);
                AppendArc(dst, vertex, nIn, nOut, half);
                dst.Add(leftIn);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(pen), pen.Join, "Unknown pen join.");
        }
    }

    /// <summary>Right join at an interior vertex (mirror of the left join).</summary>
    private static void AppendRightJoin(List<Point> dst, Point vertex, Point inDir, Point outDir, PenStyle pen, double half)
    {
        Point nIn = Perp(inDir);
        Point nOut = Perp(outDir);
        Point rightOut = Offset(vertex, nIn, -half);
        Point rightIn = Offset(vertex, nOut, -half);
        switch (pen.Join)
        {
            case PenLineJoin.Miter:
                {
                    double dot = (inDir.X * outDir.X) + (inDir.Y * outDir.Y);
                    double sinHalf = Math.Sqrt(Math.Max(0.0, (1.0 - dot) * 0.5));
                    double miterRatio = sinHalf < 1e-9 ? 1.0 : 1.0 / Math.Max(sinHalf, 1e-9);
                    if (miterRatio > Math.Max(pen.MiterLimit, 1.0))
                    {
                        dst.Add(rightOut);
                        dst.Add(rightIn);
                        break;
                    }

                    double cosHalf = Math.Sqrt(Math.Max(0.0, (1.0 + dot) * 0.5));
                    Point bisector = Normalize(Add(nIn, nOut));
                    if (IsZero(bisector))
                    {
                        dst.Add(rightOut);
                        dst.Add(rightIn);
                        break;
                    }

                    dst.Add(Offset(vertex, bisector, -half / Math.Max(cosHalf, 1e-9)));
                    break;
                }

            case PenLineJoin.Bevel:
                dst.Add(rightOut);
                dst.Add(rightIn);
                break;

            case PenLineJoin.Round:
                dst.Add(rightOut);
                AppendArc(dst, vertex, Scale(nIn, -1), Scale(nOut, -1), half);
                dst.Add(rightIn);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(pen), pen.Join, "Unknown pen join.");
        }
    }

    /// <summary>
    /// Emits arc points around <paramref name="center"/> from the ray at <paramref name="from"/>
    /// to the ray at <paramref name="to"/> (both unit normals), radius <paramref name="radius"/>.
    /// Sweeps the signed turn angle so the join follows the corner outside.
    /// </summary>
    private static void AppendArc(List<Point> dst, Point center, Point from, Point to, double radius)
    {
        double startAngle = Math.Atan2(from.Y, from.X);
        double endAngle = Math.Atan2(to.Y, to.X);
        double sweep = endAngle - startAngle;
        if (sweep > Math.PI)
        {
            sweep -= TwoPi;
        }
        else if (sweep < -Math.PI)
        {
            sweep += TwoPi;
        }

        int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / QuarterPi));
        for (int i = 1; i < steps; i++)
        {
            double angle = startAngle + (sweep * i / steps);
            dst.Add(Offset(center, new Point(Math.Cos(angle), Math.Sin(angle)), radius));
        }
    }

    /// <summary>End cap at the last vertex: from the left side to the right side along direction <paramref name="dir"/>.</summary>
    private static void AppendEndCap(List<Point> dst, Point end, Point dir, PenStyle pen, double half)
    {
        switch (pen.EndCap)
        {
            case PenLineCap.Flat:
                break;

            case PenLineCap.Square:
                {
                    Point n = Perp(dir);
                    Point forward = Scale(dir, half);
                    dst.Add(Add(Offset(end, n, half), forward));
                    dst.Add(Add(Offset(end, n, -half), forward));
                    break;
                }

            case PenLineCap.Round:
                AppendCapArc(dst, end, dir, half, capAtStart: false);
                break;

            case PenLineCap.Triangle:
                dst.Add(Offset(end, dir, half));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(pen), pen.EndCap, "Unknown pen cap.");
        }
    }

    /// <summary>Start cap at the first vertex: from the right side to the left side (closes the loop).</summary>
    private static void AppendStartCap(List<Point> dst, Point start, Point dir, PenStyle pen, double half)
    {
        switch (pen.StartCap)
        {
            case PenLineCap.Flat:
                break;

            case PenLineCap.Square:
                {
                    Point n = Perp(dir);
                    Point backward = Scale(dir, -half);
                    dst.Add(Add(Offset(start, n, -half), backward));
                    dst.Add(Add(Offset(start, n, half), backward));
                    break;
                }

            case PenLineCap.Round:
                AppendCapArc(dst, start, dir, half, capAtStart: true);
                break;

            case PenLineCap.Triangle:
                dst.Add(Offset(start, Scale(dir, -1), half));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(pen), pen.StartCap, "Unknown pen cap.");
        }
    }

    /// <summary>
    /// Round cap: semicircle around <paramref name="end"/> from the left side to the right
    /// side (end cap, bulging +dir) or from the right side to the left side (start cap,
    /// bulging -dir).
    /// </summary>
    private static void AppendCapArc(List<Point> dst, Point end, Point dir, double half, bool capAtStart)
    {
        double baseAngle = Math.Atan2(dir.Y, dir.X);
        double startAngle = capAtStart ? baseAngle - HalfPi : baseAngle + HalfPi;
        double sweep = -Math.PI;
        int steps = 8;
        for (int i = 1; i < steps; i++)
        {
            double angle = startAngle + (sweep * i / steps);
            dst.Add(Offset(end, new Point(Math.Cos(angle), Math.Sin(angle)), half));
        }
    }

    private static Point Add(Point a, Point b)
    {
        return new Point(a.X + b.X, a.Y + b.Y);
    }

    private static Point Subtract(Point a, Point b)
    {
        return new Point(a.X - b.X, a.Y - b.Y);
    }

    private static Point Scale(Point a, double s)
    {
        return new Point(a.X * s, a.Y * s);
    }

    private static Point Perp(Point v)
    {
        return new Point(-v.Y, v.X);
    }

    private static Point Offset(Point p, Point normal, double distance)
    {
        return new Point(p.X + (normal.X * distance), p.Y + (normal.Y * distance));
    }

    private static Point Normalize(Point v)
    {
        double length = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
        return length < MinLength ? Point.Origin : new Point(v.X / length, v.Y / length);
    }

    private static bool IsZero(Point v)
    {
        return Math.Abs(v.X) < MinLength && Math.Abs(v.Y) < MinLength;
    }

    private static bool Approximately(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
    }

    /// <summary>Signed area (positive = counter-clockwise) of a closed loop.</summary>
    private static double SignedArea(ReadOnlySpan<Point> contour)
    {
        double sum = 0;
        for (int i = 0; i < contour.Length; i++)
        {
            Point a = contour[i];
            Point b = contour[(i + 1) % contour.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum * 0.5;
    }
}
