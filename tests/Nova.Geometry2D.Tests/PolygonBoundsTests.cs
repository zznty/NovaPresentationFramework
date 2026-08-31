using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class PolygonBoundsTests
{
    private static byte Line => 0x01; // MILCoreSegFlags.SegTypeLine
    private static byte Bezier => 0x02; // MILCoreSegFlags.SegTypeBezier
    private static byte ClosedLine => 0x01 | 0x10; // SegTypeLine | SegClosed

    [Fact]
    public void Line_NoPen_ExactSegmentBounds()
    {
        Rect bounds = PolygonBounds.OfPoints(
            [new Point(0, 0), new Point(100, 50)],
            [Line],
            strokeThickness: 0,
            PenLineCap.Flat, PenLineCap.Flat, PenLineJoin.Miter, 10,
            default);

        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(100, bounds.Width);
        Assert.Equal(50, bounds.Height);
    }

    [Fact]
    public void Line_WithPen_WidensByHalfThickness()
    {
        Rect bounds = PolygonBounds.OfPoints(
            [new Point(0, 0), new Point(100, 50)],
            [Line],
            strokeThickness: 4,
            PenLineCap.Flat, PenLineCap.Flat, PenLineJoin.Miter, 10,
            default);

        Assert.Equal(-2, bounds.X, 6);
        Assert.Equal(-2, bounds.Y, 6);
        Assert.Equal(104, bounds.Width, 6);
        Assert.Equal(54, bounds.Height, 6);
    }

    [Fact]
    public void ClosedSquare_NoPen_ExactBounds()
    {
        Rect bounds = PolygonBounds.OfPoints(
            [new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)],
            [ClosedLine, Line, Line, Line],
            strokeThickness: 0,
            PenLineCap.Flat, PenLineCap.Flat, PenLineJoin.Miter, 10,
            default);

        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(10, bounds.Width);
        Assert.Equal(10, bounds.Height);
    }

    [Fact]
    public void BezierControlPoints_ExpandBounds()
    {
        // A single bezier segment: 1 start point + 3 control points.
        Rect bounds = PolygonBounds.OfPoints(
            [new Point(0, 40), new Point(10, 0), new Point(50, 80), new Point(80, 40)],
            [Bezier],
            strokeThickness: 0,
            PenLineCap.Flat, PenLineCap.Flat, PenLineJoin.Miter, 10,
            default);

        // The control hull bounds the curve.
        Assert.Equal(0, bounds.X);
        Assert.True(bounds.Top <= 0, $"top={bounds.Top}");
        Assert.Equal(80, bounds.Right, 6);
        Assert.True(bounds.Bottom >= 80, $"bottom={bounds.Bottom}");
    }

    [Fact]
    public void Square_WithWorldTransform_AppliesGeometryThenWorld()
    {
        // geometry matrix: scale(2); world matrix: translate(5, 7).
        var geometry = Matrix3x2.Scale(2, 2);
        var world = Matrix3x2.Translate(5, 7);
        Matrix3x2 combined = Matrix3x2.Multiply(geometry, world);

        Rect bounds = PolygonBounds.OfPoints(
            [new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)],
            [ClosedLine, Line, Line, Line],
            strokeThickness: 0,
            PenLineCap.Flat, PenLineCap.Flat, PenLineJoin.Miter, 10,
            combined);

        // (0,0)-(10,10) scaled 2x -> (0,0)-(20,20), then translated -> (5,7)-(25,27).
        Assert.Equal(5, bounds.X, 6);
        Assert.Equal(7, bounds.Y, 6);
        Assert.Equal(20, bounds.Width, 6);
        Assert.Equal(20, bounds.Height, 6);
    }
}
