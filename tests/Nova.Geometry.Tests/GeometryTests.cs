namespace Nova.Geometry.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void Point_Equality_UsesBothComponents()
    {
        Assert.Equal(new Point(1.5, 2.25), new Point(1.5, 2.25));
        Assert.NotEqual(new Point(1.5, 2.25), new Point(1.5, 2.5));
    }

    [Fact]
    public void Size_Empty_WhenNonPositive()
    {
        Assert.True(new Size(0, 10).IsEmpty);
        Assert.True(new Size(10, 0).IsEmpty);
        Assert.False(new Size(10, 10).IsEmpty);
    }

    [Fact]
    public void Rect_Contains_HalfOpen()
    {
        var rect = new Rect(10, 20, 30, 40);
        Assert.True(rect.Contains(new Point(10, 20)));
        Assert.True(rect.Contains(new Point(39.9, 59.9)));
        Assert.False(rect.Contains(new Point(40, 20)));
        Assert.False(rect.Contains(new Point(10, 60)));
        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
    }

    [Fact]
    public void Rect_FromMilLeftTopRightBottom_PreservesDoubles()
    {
        const double left = 1.25;
        const double top = 2.5;
        const double right = 10.75;
        const double bottom = 20.125;
        var rect = new Rect(left, top, right - left, bottom - top);
        Assert.Equal(left, rect.X);
        Assert.Equal(top, rect.Y);
        Assert.Equal(9.5, rect.Width);
        Assert.Equal(17.625, rect.Height);
    }

    [Fact]
    public void Matrix3x2_Multiply_AppliesTranslateThenScale()
    {
        var translate = Matrix3x2.Translate(10, 20);
        var scale = Matrix3x2.Scale(2, 3);
        var combined = Matrix3x2.Multiply(translate, scale);
        Assert.Equal(new Point(30, 75), combined.Transform(new Point(5, 5)));
    }

    [Fact]
    public void Matrix3x2_Identity_LeavesPointUnchanged()
    {
        var point = new Point(3.5, -1.25);
        Assert.Equal(point, Matrix3x2.Identity.Transform(point));
        Assert.True(Matrix3x2.Identity.IsIdentity);
    }

    [Fact]
    public void ResourceHandle_Zero_IsNull()
    {
        Assert.True(ResourceHandle.Null.IsNull);
        Assert.False(new ResourceHandle(7).IsNull);
        Assert.Equal(new ResourceHandle(7), new ResourceHandle(7));
    }

    [Fact]
    public void PixelSize_RejectsNegative()
    {
        ArgumentOutOfRangeException width = Assert.Throws<ArgumentOutOfRangeException>(() => new PixelSize(-1, 1));
        ArgumentOutOfRangeException height = Assert.Throws<ArgumentOutOfRangeException>(() => new PixelSize(1, -1));
        Assert.Equal("width", width.ParamName);
        Assert.Equal("height", height.ParamName);
        Assert.True(new PixelSize(0, 8).IsEmpty);
        Assert.Equal("16x9px", new PixelSize(16, 9).ToString());
    }

    [Fact]
    public void ColorRgba_Equality()
    {
        Assert.Equal(new ColorRgba(1, 0, 0, 1), new ColorRgba(1, 0, 0, 1));
        Assert.NotEqual(ColorRgba.Black, ColorRgba.White);
    }
}

public sealed class ProjectionBoundsTests
{
    [Fact]
    public void Compute_Identity_ReturnsBoxBounds()
    {
        Rect bounds = ProjectionBounds.Compute(
            1, 0, 0,
            0, 1, 0,
            0, 0, 0,
            0, 0, 1,
            10, 20, 30, 40, 50, 60);

        Assert.Equal(new Rect(10, 20, 40, 50), bounds);
    }

    [Fact]
    public void Compute_ScaleAndOffset_TransformsCorners()
    {
        // 2x scale + (100, 50) offset: the box (10,20)-(30,40) maps to (120,90)-(160,130).
        Rect bounds = ProjectionBounds.Compute(
            2, 0, 0,
            0, 2, 0,
            0, 0, 0,
            100, 50, 1,
            10, 20, 0, 20, 20, 10);

        Assert.Equal(new Rect(120, 90, 40, 40), bounds);
    }

    [Fact]
    public void Compute_PerspectiveDivide_AppliesW()
    {
        // w = z + 1: the box at z=1 divides by 2.
        Rect bounds = ProjectionBounds.Compute(
            10, 0, 0,
            0, 10, 0,
            0, 0, 1,
            0, 0, 1,
            0, 0, 1, 2, 2, 0);

        Assert.Equal(new Rect(0, 0, 10, 10), bounds);
    }

    [Fact]
    public void Compute_AllCornersOnCameraPlane_ReturnsEmpty()
    {
        // w = 0 for every corner.
        Rect bounds = ProjectionBounds.Compute(
            1, 0, 0,
            0, 1, 0,
            0, 0, 0,
            0, 0, 0,
            0, 0, 0, 1, 1, 1);

        Assert.Equal(Rect.Empty, bounds);
    }

    [Fact]
    public void Compute_DegenerateProjection_ReturnsEmpty()
    {
        // The whole box projects to a single point.
        Rect bounds = ProjectionBounds.Compute(
            0, 0, 0,
            0, 0, 0,
            0, 0, 0,
            5, 6, 1,
            0, 0, 0, 10, 10, 10);

        Assert.Equal(Rect.Empty, bounds);
    }
}
