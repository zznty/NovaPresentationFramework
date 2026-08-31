using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Decodes a MIL path-geometry stream (the buffer passed to <c>MilUtility_*</c> nests) into
/// flattened contours, one per figure, preserving the closed/filled flags. The stream layout
/// matches <c>ByteStreamGeometryContext</c>: Poly* segments (with BackSize/Count padding),
/// the fixed 64-byte arc segment, and the compact line/bezier/quadratic forms.
/// </summary>
[PublicAPI]
public static class MilPathFlattener
{
    public const double DefaultTolerance = 0.25;

    private const double HalfPi = Math.PI * 0.5;
    private const double TwoPi = Math.PI * 2.0;

    /// <summary>Flattens every figure of the stream into a contour (closed/filled flags preserved).</summary>
    public static IReadOnlyList<Contour> Flatten(ReadOnlySpan<byte> stream, double tolerance = DefaultTolerance)
    {
        var contours = new List<Contour>();
        if (MilPathData.IsEmpty(stream))
        {
            return contours;
        }

        int offset = MilPathData.GeometryHeaderBytes;
        int end = checked((int)MilPathData.ReadSize(stream));
        if (end > stream.Length)
        {
            end = stream.Length;
        }

        while (offset + MilPathData.FigureHeaderBytes <= end)
        {
            uint figureFlags = MilPathData.ReadUInt32(stream, offset + MilPathData.FigureFlagsOffset);
            uint segmentCount = MilPathData.ReadUInt32(stream, offset + MilPathData.FigureCountOffsetInFigure);
            double startX = MilPathData.ReadDouble(stream, offset + MilPathData.FigureStartOffset);
            double startY = MilPathData.ReadDouble(stream, offset + MilPathData.FigureStartOffset + 8);
            uint figureSize = MilPathData.ReadUInt32(stream, offset + MilPathData.FigureSizeOffset);
            offset += MilPathData.FigureHeaderBytes;

            int figureEnd = checked((int)Math.Min((uint)stream.Length, (uint)offset - MilPathData.FigureHeaderBytes + figureSize));
            bool isFilled = (figureFlags & MilPathData.FigureIsFillable) != 0;
            bool isClosed = (figureFlags & MilPathData.FigureIsClosed) != 0;

            var builder = new PathBuilder();
            builder.MoveTo(new Point(startX, startY));
            double penX = startX;
            double penY = startY;

            int segmentOffset = offset;
            for (uint i = 0; i < segmentCount && segmentOffset < figureEnd; i++)
            {
                if (segmentOffset + 8 > figureEnd)
                {
                    break;
                }

                uint type = MilPathData.ReadUInt32(stream, segmentOffset + MilPathData.SegmentTypeOffset);
                segmentOffset = type switch
                {
                    MilPathData.MilSegmentLine => AppendCompactLine(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    MilPathData.MilSegmentBezier => AppendCompactBezier(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    MilPathData.MilSegmentQuadraticBezier => AppendCompactQuadratic(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    MilPathData.MilSegmentArc => AppendArcSegment(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    MilPathData.MilSegmentPolyLine => AppendPolyLine(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    MilPathData.MilSegmentPolyBezier => AppendPolyBezier(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    MilPathData.MilSegmentPolyQuadraticBezier => AppendPolyQuadratic(stream, segmentOffset, figureEnd, builder, ref penX, ref penY),
                    _ => throw new InvalidOperationException($"Unknown MIL path segment type {type}."),
                };
            }

            if (isClosed)
            {
                builder.Close();
            }

            var contour = new Contour(isClosed, isFilled);
            builder.Flatten(tolerance, contour.Points);

            if (contour.Points.Count == 0)
            {
                // Degenerate single-point figure: contribute its start point.
                contour.Points.Add(new Point(startX, startY));
            }

            contours.Add(contour);
            offset = figureEnd;
        }

        return contours;
    }

    private static int AppendCompactLine(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        int pointOffset = segmentOffset + MilPathData.CompactPointOffset;
        if (pointOffset + 16 > figureEnd)
        {
            return figureEnd;
        }

        Point endPoint = ReadPoint(stream, pointOffset);
        builder.LineTo(endPoint);
        penX = endPoint.X;
        penY = endPoint.Y;
        return segmentOffset + MilPathData.LineSegmentBytes;
    }

    private static int AppendCompactBezier(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        int pointOffset = segmentOffset + MilPathData.CompactPointOffset;
        if (pointOffset + 48 > figureEnd)
        {
            return figureEnd;
        }

        Point c1 = ReadPoint(stream, pointOffset);
        Point c2 = ReadPoint(stream, pointOffset + 16);
        Point endPoint = ReadPoint(stream, pointOffset + 32);
        builder.CubicTo(c1, c2, endPoint);
        penX = endPoint.X;
        penY = endPoint.Y;
        return segmentOffset + MilPathData.BezierSegmentBytes;
    }

    private static int AppendCompactQuadratic(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        int pointOffset = segmentOffset + MilPathData.CompactPointOffset;
        if (pointOffset + 32 > figureEnd)
        {
            return figureEnd;
        }

        Point control = ReadPoint(stream, pointOffset);
        Point endPoint = ReadPoint(stream, pointOffset + 16);
        builder.QuadraticTo(control, endPoint);
        penX = endPoint.X;
        penY = endPoint.Y;
        return segmentOffset + MilPathData.QuadraticSegmentBytes;
    }

    private static int AppendArcSegment(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        if (segmentOffset + MilPathData.ArcSegmentBytes > figureEnd)
        {
            return figureEnd;
        }

        Point endPoint = ReadPoint(stream, segmentOffset + MilPathData.ArcPointOffset);
        double radiusX = MilPathData.ReadDouble(stream, segmentOffset + MilPathData.ArcSizeOffset);
        double radiusY = MilPathData.ReadDouble(stream, segmentOffset + MilPathData.ArcSizeOffset + 8);
        double rotationAngle = MilPathData.ReadDouble(stream, segmentOffset + MilPathData.ArcRotationOffset);
        bool isLargeArc = MilPathData.ReadUInt32(stream, segmentOffset + MilPathData.ArcLargeArcOffset) != 0;
        bool sweepClockwise = MilPathData.ReadUInt32(stream, segmentOffset + MilPathData.ArcSweepOffset) != 0;
        AppendArc(builder, penX, penY, endPoint, radiusX, radiusY, rotationAngle, isLargeArc, sweepClockwise);
        penX = endPoint.X;
        penY = endPoint.Y;
        return segmentOffset + MilPathData.ArcSegmentBytes;
    }

    private static int AppendPolyLine(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        if (segmentOffset + 16 > figureEnd)
        {
            return figureEnd;
        }

        uint count = MilPathData.ReadUInt32(stream, segmentOffset + 12);
        int pointOffset = segmentOffset + MilPathData.SegmentPayloadOffset;
        for (uint j = 0; j < count; j++)
        {
            if (pointOffset + 16 > figureEnd)
            {
                break;
            }

            Point endPoint = ReadPoint(stream, pointOffset);
            builder.LineTo(endPoint);
            penX = endPoint.X;
            penY = endPoint.Y;
            pointOffset += 16;
        }

        return pointOffset;
    }

    private static int AppendPolyBezier(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        if (segmentOffset + 16 > figureEnd)
        {
            return figureEnd;
        }

        // The stream stores poly points contiguously as 16-byte Point values (the writer's
        // GenericPolyToHelper append per point; see ByteStreamGeometryContext) — the count is
        // the RAW point count, NOT the curve count. Group in threes for cubic beziers.
        uint count = MilPathData.ReadUInt32(stream, segmentOffset + 12);
        int pointOffset = segmentOffset + MilPathData.SegmentPayloadOffset;
        for (uint j = 0; j + 2 < count; j += 3)
        {
            if (pointOffset + 48 > figureEnd)
            {
                break;
            }

            Point c1 = ReadPoint(stream, pointOffset);
            Point c2 = ReadPoint(stream, pointOffset + 16);
            Point endPoint = ReadPoint(stream, pointOffset + 32);
            builder.CubicTo(c1, c2, endPoint);
            penX = endPoint.X;
            penY = endPoint.Y;
            pointOffset += 48;
        }

        return pointOffset;
    }

    private static int AppendPolyQuadratic(ReadOnlySpan<byte> stream, int segmentOffset, int figureEnd, PathBuilder builder, ref double penX, ref double penY)
    {
        if (segmentOffset + 16 > figureEnd)
        {
            return figureEnd;
        }

        // Raw 16-byte points; group in pairs for quadratic beziers.
        uint count = MilPathData.ReadUInt32(stream, segmentOffset + 12);
        int pointOffset = segmentOffset + MilPathData.SegmentPayloadOffset;
        for (uint j = 0; j + 1 < count; j += 2)
        {
            if (pointOffset + 32 > figureEnd)
            {
                break;
            }

            Point control = ReadPoint(stream, pointOffset);
            Point endPoint = ReadPoint(stream, pointOffset + 16);
            builder.QuadraticTo(control, endPoint);
            penX = endPoint.X;
            penY = endPoint.Y;
            pointOffset += 32;
        }

        return pointOffset;
    }

    /// <summary>Reads a double-precision point from the stream (the serializer writes full <c>Point</c> values).</summary>
    private static Point ReadPoint(ReadOnlySpan<byte> stream, int offset)
    {
        return new Point(MilPathData.ReadDouble(stream, offset), MilPathData.ReadDouble(stream, offset + 8));
    }

    /// <summary>
    /// Approximates an elliptical arc with sampled points on the true arc (accurate enough
    /// for flattening; the sampled points lie on the ellipse).
    /// </summary>
    private static void AppendArc(
        PathBuilder builder,
        double startX,
        double startY,
        Point endPoint,
        double radiusX,
        double radiusY,
        double rotationAngle,
        bool isLargeArc,
        bool sweepClockwise)
    {
        double angleRadians = rotationAngle * (Math.PI / 180.0);
        double cos = Math.Cos(angleRadians);
        double sin = Math.Sin(angleRadians);

        // Endpoint-to-center conversion (SVG arc convention, same as WPF's ArcSegment).
        double dx = (startX - endPoint.X) * 0.5;
        double dy = (startY - endPoint.Y) * 0.5;
        double x1p = (cos * dx) + (sin * dy);
        double y1p = (-sin * dx) + (cos * dy);
        double rx = Math.Max(Math.Abs(radiusX), 1e-9);
        double ry = Math.Max(Math.Abs(radiusY), 1e-9);
        double x1pSq = x1p * x1p / (rx * rx);
        double y1pSq = y1p * y1p / (ry * ry);
        double lambda = x1pSq + y1pSq;
        if (lambda > 1)
        {
            double scale = Math.Sqrt(lambda);
            rx *= scale;
            ry *= scale;
        }

        double rx2 = rx * rx;
        double ry2 = ry * ry;
        double sign = isLargeArc == sweepClockwise ? -1.0 : 1.0;
        double numerator = Math.Max(0.0, ((rx2 * ry2) - (rx2 * y1p * y1p) - (ry2 * x1p * x1p)) / ((rx2 * y1p * y1p) + (ry2 * x1p * x1p)));
        double coef = sign * Math.Sqrt(numerator);
        double cxp = coef * (rx * y1p / ry);
        double cyp = -coef * (ry * x1p / rx);
        double cxTerm = (cos * cxp) - (sin * cyp);
        double cyTerm = (sin * cxp) + (cos * cyp);
        double cx = cxTerm + ((startX + endPoint.X) * 0.5);
        double cy = cyTerm + ((startY + endPoint.Y) * 0.5);

        double startAngle = AngleOf(new Point((startX - cx) / rx, (startY - cy) / ry));
        double endAngle = AngleOf(new Point((endPoint.X - cx) / rx, (endPoint.Y - cy) / ry));
        double sweep = endAngle - startAngle;
        if (sweepClockwise && sweep < 0)
        {
            sweep += 2 * Math.PI;
        }
        else if (!sweepClockwise && sweep > 0)
        {
            sweep -= 2 * Math.PI;
        }


        if (isLargeArc)
        {
            if (Math.Abs(sweep) < Math.PI)
            {
                sweep = sweepClockwise ? sweep + (2 * Math.PI) : sweep - (2 * Math.PI);
            }
        }

        double stepSpan = Math.PI / 8;
        int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / stepSpan));

        // The flattened arc must begin exactly at the pen position (the previous segment's
        // end) and end exactly at the arc's endpoint, visiting the ellipse's interior axis
        // extrema so the bounds are exact (an inscribed polygon alone under-shoots the true
        // bounds at the quadrant points). Emitting the start quadrant again after the end
        // folds the contour back onto itself (a self-intersection that defeats the
        // tessellator's fast path), so the start and end are emitted explicitly, once.
        Span<double> angles = stackalloc double[steps + 5];
        int angleCount = 0;
        angles[angleCount++] = startAngle;
        for (int i = 1; i < steps; i++)
        {
            angles[angleCount++] = startAngle + (sweep * i / steps);
        }

        for (int q = 0; q < 4; q++)
        {
            double quadrant = q * HalfPi;
            double rawDelta = (quadrant - startAngle) % TwoPi;
            double delta = (rawDelta + TwoPi) % TwoPi;
            double sweepSpan = sweep >= 0 ? sweep : TwoPi + sweep;
            if (delta < 1e-9 || Math.Abs(delta - sweepSpan) < 1e-9)
            {
                continue;
            }

            bool inSweep = sweep >= 0 ? delta <= sweep : delta >= TwoPi + sweep;
            if (inSweep)
            {
                angles[angleCount++] = quadrant;
            }
        }

        angles[..angleCount].Sort();
        if (sweep >= 0)
        {
            for (int i = 0; i < angleCount; i++)
            {
                if (i == 0 || angles[i] - angles[i - 1] > 1e-9)
                {
                    EmitArcPoint(builder, angles[i], rx, ry, cos, sin, cx, cy);
                }
            }

            EmitArcPoint(builder, endAngle, rx, ry, cos, sin, cx, cy);
        }
        else
        {
            for (int i = angleCount - 1; i >= 0; i--)
            {
                if (i == angleCount - 1 || angles[i + 1] - angles[i] > 1e-9)
                {
                    EmitArcPoint(builder, angles[i], rx, ry, cos, sin, cx, cy);
                }
            }

            EmitArcPoint(builder, endAngle, rx, ry, cos, sin, cx, cy);
        }
    }

    private static void EmitArcPoint(PathBuilder builder, double angle, double rx, double ry, double cos, double sin, double cx, double cy)
    {
        double cosAngle = Math.Cos(angle);
        double sinAngle = Math.Sin(angle);
        double x = (rx * cosAngle * cos) - (ry * sinAngle * sin) + cx;
        double y = (rx * cosAngle * sin) + (ry * sinAngle * cos) + cy;
        builder.LineTo(new Point(x, y));
    }

    private static double AngleOf(Point p)
    {
        return Math.Atan2(p.Y, p.X);
    }
}
