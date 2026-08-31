using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class MilPathFlattenerTests
{
    [Fact]
    public void Flatten_BezierFigure_NonDegenerateContourWithExpectedBounds()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 40, isFilled: true, isClosed: true);
        writer.BezierTo(10, 0, 50, 80, 80, 40);
        byte[] stream = writer.Close();

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream);

        _ = Assert.Single(contours);
        Assert.True(contours[0].IsClosed);
        Assert.True(contours[0].ReadOnlySpan.Length > 2, "a curve must flatten to more than two points");

        Rect bounds = contours[0].Bounds();
        // The cubic's control points are (10,0),(50,80): the curve stays within their hull
        // and spans both endpoints (0,40) and (80,40).
        Assert.True(bounds.Left <= 0 && bounds.Right >= 80, $"x-range wrong: {bounds}");
        Assert.True(bounds.Top <= 40 && bounds.Bottom >= 40, $"y-range wrong: {bounds}");
        Assert.True(bounds.Width > 0 && bounds.Height > 0, "curve bounds must be non-zero");
    }

    [Fact]
    public void Flatten_OpenLine_OpenContour()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(1, 2, isFilled: true, isClosed: false);
        writer.LineTo(5, 8);
        byte[] stream = writer.Close();

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream);

        _ = Assert.Single(contours);
        Assert.False(contours[0].IsClosed);
        Assert.Equal(2, contours[0].ReadOnlySpan.Length);
    }

    [Fact]
    public void Flatten_ArcSegment_NonZeroBounds()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 0, isFilled: true, isClosed: false);
        writer.ArcTo(10, 0, radiusX: 10, radiusY: 10, rotation: 0, isLargeArc: false, sweepClockwise: true);
        byte[] stream = writer.Close();

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream);

        Assert.True(contours[0].ReadOnlySpan.Length > 2);
        Rect bounds = contours[0].Bounds();
        // A chord of 10 on a circle of radius 10 subtends 60 degrees; the minor arc's apex
        // sits 10 - sqrt(75) ~ 1.34 from the chord.
        Assert.True(Math.Abs(bounds.Width - 10) < 0.01, $"width={bounds.Width}");
        Assert.Equal(10 - Math.Sqrt(75), bounds.Height, 2);
        Assert.True(bounds.Right >= 10 - 1e-6, $"right={bounds.Right}");
    }

    [Fact]
    public void Flatten_TwoFigures_TwoContours()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 0, isFilled: true, isClosed: true);
        writer.LineTo(10, 0);
        writer.LineTo(10, 10);
        writer.EndFigure();
        writer.BeginFigure(20, 20, isFilled: true, isClosed: false);
        writer.LineTo(30, 30);
        byte[] stream = writer.Close();

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream);

        Assert.Equal(2, contours.Count);
        Assert.True(contours[0].IsClosed);
        Assert.False(contours[1].IsClosed);
    }
}
