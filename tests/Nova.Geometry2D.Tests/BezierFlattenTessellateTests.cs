using Nova.Geometry;

namespace Nova.Geometry2D.Tests;

public sealed class BezierFlattenTessellateTests
{
    [Fact]
    public void Flatten_BezierFigure_Tessellates()
    {
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 40, isFilled: true, isClosed: true);
        writer.BezierTo(10, 0, 50, 80, 80, 40);
        byte[] stream = writer.Close();

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream);
        Assert.True(contours.Count == 1, $"contours={contours.Count}");
        Contour contour = contours[0];
        Assert.True(contour.IsClosed, "figure must be closed");
        Assert.True(contour.IsFilled, "figure must be fillable");

        Point[] triangles = new Point[contour.ReadOnlySpan.Length * 3];
        int written = Tessellator.Fill(contour.ReadOnlySpan, FillRule.EvenOdd, triangles);
        System.Console.WriteLine($"pts: {string.Join(" ", contour.ReadOnlySpan.ToArray().Select(p => $"({p.X:F1},{p.Y:F1})"))}");
        Assert.True(written > 0, $"tessellation wrote 0 triangles for {contour.ReadOnlySpan.Length} points");
    }

    [Fact]
    public void Flatten_OpenLineFigure_TessellatesAsClosedLoop()
    {
        // An open figure is not fillable; the slave skips it. This guards the closed case.
        var writer = new PathStreamWriter();
        writer.BeginFigure(0, 40, isFilled: false, isClosed: true);
        writer.BezierTo(10, 0, 50, 80, 80, 40);
        byte[] stream = writer.Close();

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(stream);
        Assert.True(contours.Count == 1);
        Assert.False(contours[0].IsFilled);
    }
}
