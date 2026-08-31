using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class TessellatorTests
{
    private static readonly Point[] Rect =
    [
        new(0, 0), new(10, 0), new(10, 10), new(0, 10)
    ];

    private static readonly Point[] Chevron =
    [
        new(0, 0), new(10, 0), new(10, 4), new(5, 4), new(5, 10), new(0, 10)
    ];

    [Fact]
    public void Fill_Rect_ProducesTwoTriangles()
    {
        Span<Point> destination = new Point[6];
        int written = Tessellator.Fill(Rect, FillRule.EvenOdd, destination);
        Assert.Equal(6, written);
        foreach (Point p in destination[..written])
        {
            Assert.InRange(p.X, 0, 10);
            Assert.InRange(p.Y, 0, 10);
        }
    }

    [Fact]
    public void Contains_Rect_CenterInsideOutsideFalse()
    {
        Assert.True(Tessellator.Contains(Rect, FillRule.EvenOdd, new Point(5, 5)));
        Assert.True(Tessellator.Contains(Rect, FillRule.NonZero, new Point(5, 5)));
        Assert.False(Tessellator.Contains(Rect, FillRule.EvenOdd, new Point(20, 5)));
        Assert.False(Tessellator.Contains(Rect, FillRule.NonZero, new Point(5, -1)));
    }

    [Fact]
    public void Contains_Rect_OnEdgeCountsAsInside()
    {
        Assert.True(Tessellator.Contains(Rect, FillRule.EvenOdd, new Point(0, 5)));
        Assert.True(Tessellator.Contains(Rect, FillRule.EvenOdd, new Point(10, 0)));
        Assert.True(Tessellator.Contains(Rect, FillRule.NonZero, new Point(5, 10)));
    }

    [Fact]
    public void Fill_Chevron_CoversBodyNotNotch()
    {
        Span<Point> destination = new Point[18];
        int written = Tessellator.Fill(Chevron, FillRule.NonZero, destination);
        Assert.True(written >= 6, $"expected at least 6 points, got {written}");
        Assert.Equal(0, written % 3);
        Assert.True(Tessellator.Contains(Chevron, FillRule.EvenOdd, new Point(2, 7)));
        Assert.True(Tessellator.Contains(Chevron, FillRule.NonZero, new Point(2, 7)));
        Assert.False(Tessellator.Contains(Chevron, FillRule.EvenOdd, new Point(8, 7)));
        Assert.False(Tessellator.Contains(Chevron, FillRule.NonZero, new Point(8, 7)));
    }

    [Fact]
    public void Fill_Triangle_Works()
    {
        ReadOnlySpan<Point> triangle = [new(0, 0), new(5, 10), new(10, 0)];
        Span<Point> destination = new Point[3];
        int written = Tessellator.Fill(triangle, FillRule.EvenOdd, destination);
        Assert.Equal(3, written);
        Assert.True(Tessellator.Contains(triangle, FillRule.EvenOdd, new Point(5, 3)));
        Assert.False(Tessellator.Contains(triangle, FillRule.EvenOdd, new Point(5, 12)));
    }

    [Fact]
    public void Fill_DestinationTooSmall_Throws()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Tessellator.Fill(Rect, FillRule.EvenOdd, new Point[1]));
        Assert.Equal("destination", ex.ParamName);
    }

    [Fact]
    public void Fill_Degenerate_ReturnsZero()
    {
        ReadOnlySpan<Point> twoPoints = [new(0, 0), new(5, 5)];
        Assert.Equal(0, Tessellator.Fill(twoPoints, FillRule.EvenOdd, new Point[6]));
        ReadOnlySpan<Point> repeated = [new(0, 0), new(0, 0), new(0, 0)];
        Assert.Equal(0, Tessellator.Fill(repeated, FillRule.EvenOdd, new Point[9]));
    }

    [Fact]
    public void Fill_UnknownFillRule_Throws()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => Tessellator.Contains(Rect, (FillRule)99, new Point(1, 1)));
        Assert.Equal("fillRule", ex.ParamName);
    }

    [Fact]
    public void Fill_EvenOddWithCollinearVertex_StillCovers()
    {
        ReadOnlySpan<Point> contour =
        [
            new(0, 0), new(5, 0), new(10, 0), new(10, 10), new(0, 10)
        ];
        Span<Point> destination = new Point[9];
        int written = Tessellator.Fill(contour, FillRule.EvenOdd, destination);
        Assert.True(written >= 3, $"expected at least one triangle, got {written}");
        Assert.True(Tessellator.Contains(contour, FillRule.EvenOdd, new Point(5, 5)));
    }
}
