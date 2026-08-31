using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Axis-aligned bounds of the polygon/type-list representation WPF passes to
/// <c>MilUtility_PolygonBounds</c> (the <c>LineGeometry</c>, <c>RectangleGeometry</c> and
/// <c>EllipseGeometry</c> bounds helper). Types use the <c>MILCoreSegFlags</c> encoding:
/// <c>type &amp; 3</c> is line (consumes 1 point) or bezier (consumes 3 points); the first
/// type carries <c>SegClosed</c> when the figure is closed. Bezier control points are in the
/// hull so they bound the curve; stroke inflation honors caps and miter-join reach.
/// </summary>
[PublicAPI]
public static class PolygonBounds
{
    private const uint SegTypeMask = 0x3;
    private const uint SegTypeLine = 0x1;
    private const uint SegTypeBezier = 0x2;
    private const double Epsilon = 1e-9;

    public static Rect OfPoints(
        ReadOnlySpan<Point> points,
        ReadOnlySpan<byte> types,
        double strokeThickness,
        PenLineCap startCap,
        PenLineCap endCap,
        PenLineJoin join,
        double miterLimit,
        Matrix3x2 world)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        if (points.Length == 0)
        {
            return Rect.Empty;
        }

        // Collect the walked vertices (for miter reach) and the bounding hull.
        bool applyTransform = !world.IsIdentity && world != default;
        Span<Point> vertices = points.Length <= 512 ? stackalloc Point[points.Length + 1] : new Point[points.Length + 1];
        Span<bool> curved = points.Length <= 512 ? stackalloc bool[points.Length + 1] : new bool[points.Length + 1];
        int vertexCount = 0;

        void Include(Point p)
        {
            Point t = applyTransform ? world.Transform(p) : p;
            minX = Math.Min(minX, t.X);
            minY = Math.Min(minY, t.Y);
            maxX = Math.Max(maxX, t.X);
            maxY = Math.Max(maxY, t.Y);
        }

        if (points.Length > 0)
        {
            Include(points[0]);
            vertices[vertexCount] = points[0];
            curved[vertexCount] = false;
            vertexCount++;
        }

        int pointIndex = 1;
        for (int i = 0; i < types.Length; i++)
        {
            uint segType = types[i] & SegTypeMask;
            if (segType == SegTypeLine)
            {
                if (pointIndex >= points.Length)
                {
                    break;
                }

                Point p = points[pointIndex++];
                Include(p);
                vertices[vertexCount] = p;
                curved[vertexCount] = false;
                vertexCount++;
            }
            else if (segType == SegTypeBezier)
            {
                if (pointIndex + 2 >= points.Length)
                {
                    break;
                }

                Point c1 = points[pointIndex++];
                Point c2 = points[pointIndex++];
                Point end = points[pointIndex++];
                Include(c1);
                Include(c2);
                Include(end);
                vertices[vertexCount] = end;
                curved[vertexCount] = true;
                vertexCount++;
            }
            else
            {
                break; // unknown segment type: stop the walk
            }
        }

        if (minX > maxX)
        {
            return Rect.Empty;
        }

        // Stroke inflation: half thickness all around; miter joins extend the corners; square
        // and triangle caps extend the ends by half thickness along the stroke.
        if (strokeThickness > 0)
        {
            double half = strokeThickness * 0.5;
            double inflation = half;
            if (join == PenLineJoin.Miter && vertexCount >= 3)
            {
                double maxMiterRatio = 1.0;
                int last = vertexCount - 1;
                for (int i = 0; i < vertexCount; i++)
                {
                    Point prev = vertices[(i + vertexCount - 1) % vertexCount];
                    Point cur = vertices[i];
                    Point next = vertices[(i + 1) % vertexCount];
                    bool prevCurved = curved[(i + vertexCount - 1) % vertexCount];
                    bool curCurved = curved[i];
                    if (prevCurved || curCurved)
                    {
                        continue;
                    }

                    double ratio = MiterRatio(prev, cur, next);
                    if (ratio > maxMiterRatio)
                    {
                        maxMiterRatio = ratio;
                    }
                }

                if (miterLimit > 1.0 && maxMiterRatio > miterLimit)
                {
                    maxMiterRatio = miterLimit;
                }

                if (maxMiterRatio > 1.0)
                {
                    inflation = half * maxMiterRatio;
                }
            }

            minX -= inflation;
            minY -= inflation;
            maxX += inflation;
            maxY += inflation;

            // Square/triangle caps extend the stroke half thickness past the endpoints.
            if (startCap is PenLineCap.Square or PenLineCap.Triangle)
            {
                ExtendEnd(vertices[0], vertices.Length > 1 ? vertices[1] : vertices[0], half, ref minX, ref minY, ref maxX, ref maxY);
            }

            if (endCap is PenLineCap.Square or PenLineCap.Triangle)
            {
                ExtendEnd(vertices[vertexCount - 1], vertexCount >= 2 ? vertices[vertexCount - 2] : vertices[0], half, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Miter ratio (miter length / half thickness) at a corner.</summary>
    private static double MiterRatio(Point prev, Point cur, Point next)
    {
        double ax = cur.X - prev.X;
        double ay = cur.Y - prev.Y;
        double bx = next.X - cur.X;
        double by = next.Y - cur.Y;
        double lenA = Math.Sqrt((ax * ax) + (ay * ay));
        double lenB = Math.Sqrt((bx * bx) + (by * by));
        if (lenA < Epsilon || lenB < Epsilon)
        {
            return 1.0;
        }

        double dot = ((ax * bx) + (ay * by)) / (lenA * lenB);
        double sinHalf = Math.Sqrt(Math.Max(0.0, (1.0 - Math.Clamp(dot, -1.0, 1.0)) * 0.5));
        return sinHalf < Epsilon ? 1.0 : 1.0 / sinHalf;
    }

    private static void ExtendEnd(Point end, Point inward, double half, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        double dx = end.X - inward.X;
        double dy = end.Y - inward.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < Epsilon)
        {
            return;
        }

        double sx = dx / length * half;
        double sy = dy / length * half;
        minX = Math.Min(minX, end.X + sx);
        maxX = Math.Max(maxX, end.X + sx);
        minY = Math.Min(minY, end.Y + sy);
        maxY = Math.Max(maxY, end.Y + sy);
    }
}
