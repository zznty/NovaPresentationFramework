using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class PathBuilderTests
{
    [Fact]
    public void Flatten_Square_AppendsPointsInBounds()
    {
        PathBuilder builder = new();
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(1, 0));
        builder.LineTo(new Point(1, 1));
        builder.LineTo(new Point(0, 1));
        builder.Close();
        List<Point> points = [];
        builder.Flatten(0.1, points);
        Assert.True(points.Count is 4 or 5, $"expected 4 or 5 points, got {points.Count}");
        Assert.All(points, p => Assert.InRange(p.X, 0, 1));
        Assert.All(points, p => Assert.InRange(p.Y, 0, 1));
        Assert.Equal(new Point(0, 0), points[0]);
        Assert.Equal(new Point(0, 0), points[^1]);
    }

    [Fact]
    public void Flatten_RejectsNonPositiveTolerance()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PathBuilder().Flatten(0, []));
        Assert.Equal("tolerance", ex.ParamName);
    }

    [Fact]
    public void Flatten_CubicQuarterCircle_StaysNearArc()
    {
        const double tolerance = 0.01;
        const double k = 0.5522847498307936;
        PathBuilder builder = new();
        builder.MoveTo(new Point(1, 0));
        builder.CubicTo(new Point(1, k), new Point(k, 1), new Point(0, 1));
        List<Point> points = [];
        builder.Flatten(tolerance, points);
        Assert.True(points.Count > 4, $"expected more than 4 points, got {points.Count}");
        Assert.Equal(new Point(1, 0), points[0]);
        Assert.Equal(new Point(0, 1), points[^1]);
        foreach (Point p in points)
        {
            double radius = Math.Sqrt((p.X * p.X) + (p.Y * p.Y));
            Assert.InRange(radius, 1 - (2 * tolerance), 1 + (2 * tolerance));
        }
    }

    [Fact]
    public void Flatten_Quadratic_MatchesCubicConversion()
    {
        PathBuilder builder = new();
        builder.MoveTo(new Point(0, 0));
        builder.QuadraticTo(new Point(5, 10), new Point(10, 0));
        List<Point> points = [];
        builder.Flatten(0.1, points);
        Assert.True(points.Count > 3, $"expected more than 3 points, got {points.Count}");
        Assert.Equal(new Point(0, 0), points[0]);
        Assert.Equal(new Point(10, 0), points[^1]);
    }

    [Fact]
    public void Flatten_DestinationIsAppended()
    {
        PathBuilder builder = new();
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(1, 0));
        List<Point> points = [new(9, 9)];
        builder.Flatten(0.1, points);
        Assert.Equal(3, points.Count);
        Assert.Equal(new Point(9, 9), points[0]);
        Assert.Equal(new Point(0, 0), points[1]);
        Assert.Equal(new Point(1, 0), points[2]);
    }

    [Fact]
    public void Flatten_EmptyMoveTo_IgnoresEmptyFigure()
    {
        PathBuilder builder = new();
        builder.MoveTo(new Point(5, 5));
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(1, 0));
        List<Point> points = [];
        builder.Flatten(0.1, points);
        Assert.Equal(2, points.Count);
        Assert.Equal(new Point(0, 0), points[0]);
    }

    [Fact]
    public void LineTo_WithoutMoveTo_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => new PathBuilder().LineTo(new Point(1, 1)));
        Assert.Contains("MoveTo", ex.Message, StringComparison.Ordinal);
    }
}
