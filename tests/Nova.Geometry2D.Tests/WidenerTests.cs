using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class WidenerTests
{
    private static readonly PenStyle FlatPen2 = new(2, PenLineJoin.Miter, PenLineCap.Flat, PenLineCap.Flat);

    [Fact]
    public void WidenOpen_HorizontalLine_FlatCaps_ExactOutlineRect()
    {
        Contour outline = Widener.WidenOpen([new Point(0, 0), new Point(10, 0)], FlatPen2);

        Assert.True(outline.IsClosed);
        Rect bounds = outline.Bounds();
        // Stroke thickness 2: the outline is exactly [0,10] x [-1,1].
        Assert.Equal(0, bounds.X);
        Assert.Equal(-1, bounds.Y, 6);
        Assert.Equal(10, bounds.Width);
        Assert.Equal(2, bounds.Height, 6);
    }

    [Fact]
    public void WidenOpen_HorizontalLine_SquareCaps_ExtendHalfThickness()
    {
        var pen = new PenStyle(2, PenLineJoin.Miter, PenLineCap.Square, PenLineCap.Square);
        Contour outline = Widener.WidenOpen([new Point(0, 0), new Point(10, 0)], pen);

        Rect bounds = outline.Bounds();
        // Square caps extend half the thickness past each endpoint.
        Assert.Equal(-1, bounds.X, 6);
        Assert.Equal(-1, bounds.Y, 6);
        Assert.Equal(12, bounds.Width);
        Assert.Equal(2, bounds.Height, 6);
    }

    [Fact]
    public void WidenOpen_HorizontalLine_RoundCaps_ArcReachesHalfThickness()
    {
        var pen = new PenStyle(2, PenLineJoin.Miter, PenLineCap.Round, PenLineCap.Round);
        Contour outline = Widener.WidenOpen([new Point(0, 0), new Point(10, 0)], pen);

        Rect bounds = outline.Bounds();
        Assert.Equal(-1, bounds.X, 6);
        Assert.Equal(-1, bounds.Y, 6);
        Assert.Equal(12, bounds.Width);
        Assert.Equal(2, bounds.Height, 6);
    }

    [Fact]
    public void WidenOpen_SlantedLine_BoundsCoverBothEndpoints()
    {
        Contour outline = Widener.WidenOpen([new Point(0, 0), new Point(10, 10)], FlatPen2);

        Rect bounds = outline.Bounds();
        Assert.True(bounds.Left <= 0 && bounds.Top <= 0, "outline must cover the start point");
        Assert.True(bounds.Right >= 10 && bounds.Bottom >= 10, "outline must cover the end point");
        // Thickness 2 over a 45-degree line adds sqrt(2)~1.414 of reach in each axis.
        Assert.True(bounds.Left >= -1.5 && bounds.Top >= -1.5);
        Assert.True(bounds.Right <= 11.5 && bounds.Bottom <= 11.5);
    }

    [Fact]
    public void WidenClosed_Square_RingHasExactOuterAndInnerBounds()
    {
        (Contour outer, Contour? inner) = Widener.WidenClosed(
            [new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)],
            FlatPen2);

        Rect outerBounds = outer.Bounds();
        Assert.Equal(-1, outerBounds.X, 6);
        Assert.Equal(-1, outerBounds.Y, 6);
        Assert.Equal(12, outerBounds.Width);
        Assert.Equal(12, outerBounds.Height, 6);

        Assert.NotNull(inner);
        Rect innerBounds = inner.Bounds();
        Assert.Equal(1, innerBounds.X, 6);
        Assert.Equal(1, innerBounds.Y, 6);
        Assert.Equal(8, innerBounds.Width);
        Assert.Equal(8, innerBounds.Height, 6);
    }

    [Fact]
    public void WidenClosed_ThinContour_DropsCollapsedInnerLoop()
    {
        // 1-wide contour stroked with thickness 2: the inner loop cannot exist.
        (Contour outer, Contour? inner) = Widener.WidenClosed(
            [new Point(0, 0), new Point(1, 0), new Point(1, 10), new Point(0, 10)],
            FlatPen2);

        Assert.True(outer.ReadOnlySpan.Length >= 3);
        Assert.Null(inner);
    }

    [Fact]
    public void WidenOpen_RightAngleJoin_MiterReachesPastCorner()
    {
        // L-shape: (0,0)->(10,0)->(10,10), miter join, thickness 2.
        Contour outline = Widener.WidenOpen([new Point(0, 0), new Point(10, 0), new Point(10, 10)], FlatPen2);

        Rect bounds = outline.Bounds();
        // The mitered outer corner is at (11, -1); the vertical band reaches y=10.
        Assert.Equal(0, bounds.X);
        Assert.Equal(-1, bounds.Y, 6);
        Assert.True(bounds.Right is >= 11 and <= 11.01, $"right={bounds.Right}");
        Assert.Equal(11, bounds.Height, 6);
    }
}
