using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class PathBoundsTests
{
    [Fact]
    public void Bounds_OpenLine_IsLineBounds()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(1, 2, isFilled: true, isClosed: false);
        writer.LineTo(5, 8);
        byte[] stream = writer.Close();

        Rect bounds = PathBounds.OfPath(stream);

        Assert.Equal(1, bounds.X);
        Assert.Equal(2, bounds.Y);
        Assert.Equal(4, bounds.Width);
        Assert.Equal(6, bounds.Height);
    }

    [Fact]
    public void Bounds_ClosedSquare_IsSquareBounds()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 0, isFilled: true, isClosed: true);
        writer.LineTo(10, 0);
        writer.LineTo(10, 10);
        writer.LineTo(0, 10);
        byte[] stream = writer.Close();

        Rect bounds = PathBounds.OfPath(stream);

        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(10, bounds.Width);
        Assert.Equal(10, bounds.Height);
    }

    [Fact]
    public void Bounds_WithStroke_WidensByHalfThickness()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 0, isFilled: true, isClosed: true);
        writer.LineTo(10, 0);
        writer.LineTo(10, 10);
        writer.LineTo(0, 10);
        byte[] stream = writer.Close();

        Rect bounds = PathBounds.OfPath(stream, strokeThickness: 4);

        Assert.Equal(-2, bounds.X);
        Assert.Equal(-2, bounds.Y);
        Assert.Equal(14, bounds.Width);
        Assert.Equal(14, bounds.Height);
    }

    [Fact]
    public void Bounds_EmptyGeometry_IsEmpty()
    {
        var writer = new PathStreamWriter();
        byte[] stream = writer.Close(); // no figures

        Assert.True(MilPathData.IsEmpty(stream));
        Assert.Equal(Rect.Empty, PathBounds.OfPath(stream));
    }

    [Fact]
    public void Bounds_WithTransform_AppliesToCorners()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 0, isFilled: true, isClosed: true);
        writer.LineTo(10, 0);
        writer.LineTo(10, 10);
        writer.LineTo(0, 10);
        byte[] stream = writer.Close();

        Rect bounds = PathBounds.OfPath(stream, world: Matrix3x2.Translate(5, 7));

        Assert.Equal(5, bounds.X);
        Assert.Equal(7, bounds.Y);
        Assert.Equal(10, bounds.Width);
        Assert.Equal(10, bounds.Height);
    }

    [Fact]
    public void Bounds_QuadraticCurve_ExpandsPastEndpoints()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 0, isFilled: true, isClosed: false);
        writer.QuadraticTo(5, 10, 10, 0);
        byte[] stream = writer.Close();

        Rect bounds = PathBounds.OfPath(stream);

        // The curve apex reaches y=5 (quadratic midpoint), so bounds must cover it.
        Assert.InRange(bounds.X, 0, 10);
        Assert.InRange(bounds.Y, 0, 5.5);
        Assert.True(bounds.Height >= 4.9, $"height={bounds.Height}");
    }
}
