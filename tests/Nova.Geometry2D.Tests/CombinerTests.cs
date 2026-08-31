using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class CombinerTests
{
    private static Point[] Rect(double x, double y, double w, double h)
    {
        return [new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h)];
    }

    [Fact]
    public void Union_TwoOverlappingRects_ExactUnionRect()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 10, 10), Rect(5, 5, 10, 10), GeometryCombineMode.Union);

        _ = Assert.Single(result);
        Rect bounds = result[0].Bounds();
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(15, bounds.Width);
        Assert.Equal(15, bounds.Height);
    }

    [Fact]
    public void Intersect_TwoOverlappingRects_ExactIntersectionRect()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 10, 10), Rect(5, 5, 10, 10), GeometryCombineMode.Intersect);

        _ = Assert.Single(result);
        Rect bounds = result[0].Bounds();
        Assert.Equal(5, bounds.X);
        Assert.Equal(5, bounds.Y);
        Assert.Equal(5, bounds.Width);
        Assert.Equal(5, bounds.Height);
    }

    [Fact]
    public void Exclude_OverlappingRects_AreaIsDifference()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 10, 10), Rect(5, 5, 10, 10), GeometryCombineMode.Exclude);

        double area = result.Sum(ContourArea);
        // 100 - 25.
        Assert.Equal(75, area, 6);
    }

    [Fact]
    public void Xor_OverlappingRects_AreaIsSymmetricDifference()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 10, 10), Rect(5, 5, 10, 10), GeometryCombineMode.Xor);

        double area = result.Sum(ContourArea);
        // 100 + 100 - 2*25.
        Assert.Equal(150, area, 6);
    }

    [Fact]
    public void Union_DisjointRects_TwoContours()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 5, 5), Rect(20, 20, 5, 5), GeometryCombineMode.Union);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Intersect_DisjointRects_Empty()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 5, 5), Rect(20, 20, 5, 5), GeometryCombineMode.Intersect);

        Assert.Empty(result);
    }

    [Fact]
    public void Exclude_DisjointRects_ReturnsFirst()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 5, 5), Rect(20, 20, 5, 5), GeometryCombineMode.Exclude);

        _ = Assert.Single(result);
        Assert.Equal(25, ContourArea(result[0]), 6);
    }

    [Fact]
    public void Union_OneRectInsideAnother_OuterOnly()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 10, 10), Rect(2, 2, 4, 4), GeometryCombineMode.Union);

        _ = Assert.Single(result);
        Assert.Equal(100, ContourArea(result[0]), 6);
    }

    [Fact]
    public void Intersect_OneRectInsideAnother_InnerRect()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(0, 0, 10, 10), Rect(2, 2, 4, 4), GeometryCombineMode.Intersect);

        _ = Assert.Single(result);
        Rect bounds = result[0].Bounds();
        Assert.Equal(2, bounds.X);
        Assert.Equal(2, bounds.Y);
        Assert.Equal(4, bounds.Width);
        Assert.Equal(4, bounds.Height);
    }

    [Fact]
    public void Exclude_RectFullyCovered_Empty()
    {
        IReadOnlyList<Contour> result = Combiner.Combine(Rect(2, 2, 4, 4), Rect(0, 0, 10, 10), GeometryCombineMode.Exclude);

        Assert.Empty(result);
    }

    private static double ContourArea(Contour contour)
    {
        ReadOnlySpan<Point> span = contour.ReadOnlySpan;
        double sum = 0;
        for (int i = 0; i < span.Length; i++)
        {
            Point a = span[i];
            Point b = span[(i + 1) % span.Length];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return Math.Abs(sum) * 0.5;
    }
}
