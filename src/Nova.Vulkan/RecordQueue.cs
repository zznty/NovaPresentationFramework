using Nova.Geometry;

namespace Nova.Vulkan;

/// <summary>
/// One recorded draw: a quad (6 vertices) in device-space doubles, its UVs (for textured
/// draws) or per-vertex gradient coordinates (for gradient draws), its premultiplied color,
/// the texture to sample (invalid = white fill; gradient LUT for gradient draws), and the
/// effective device-space clip it was recorded under.
/// </summary>
internal readonly record struct QuadRecord(
    Point P0,
    Point P1,
    Point P2,
    Point P3,
    Point Uv0,
    Point Uv1,
    Point Uv2,
    Point Uv3,
    float R,
    float G,
    float B,
    float A,
    TextureHandle Texture,
    Rect? Clip,
    bool IsTriangle,
    bool IsGradient,
    GradientKind GradientKind,
    GradientSpreadMethod Spread);

/// <summary>
/// CPU-side command list for one frame. Geometry is recorded in local (double) space;
/// transforms, opacity, and clips are applied immediately, but the presenter converts to
/// clip-space floats only at upload time.
/// </summary>
internal sealed class RecordQueue : IRasterCommandList
{
    private readonly List<QuadRecord> _records = [];
    private readonly Stack<Rect?> _clipStack = new();
    private readonly Stack<double> _opacityStack = new();
    private readonly Stack<Matrix3x2> _transformStack = new();
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private double _opacity = 1.0;
    private Rect? _clip;

    internal IReadOnlyList<QuadRecord> Records => _records;

    internal bool HasClear { get; set; }

    internal ColorRgba ClearColor { get; set; }

    public void Clear(ColorRgba color)
    {
        HasClear = true;
        ClearColor = color;
    }

    public void FillRectangle(Rect rectangle, ColorRgba color)
    {
        var p0 = new Point(rectangle.X, rectangle.Y);
        var p1 = new Point(rectangle.X + rectangle.Width, rectangle.Y);
        var p2 = new Point(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height);
        var p3 = new Point(rectangle.X, rectangle.Y + rectangle.Height);
        AddQuad(p0, p1, p2, p3, Point.Origin, Point.Origin, Point.Origin, Point.Origin, color, TextureHandle.Invalid, isTriangle: false);
    }

    public void FillQuad(Point p0, Point p1, Point p2, Point p3, ColorRgba color)
    {
        AddQuad(p0, p1, p2, p3, Point.Origin, Point.Origin, Point.Origin, Point.Origin, color, TextureHandle.Invalid, isTriangle: false);
    }

    public void FillTriangles(ReadOnlySpan<Point> vertices, ColorRgba color)
    {
        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("Triangle vertex count must be a multiple of 3.", nameof(vertices));
        }

        for (int i = 0; i < vertices.Length; i += 3)
        {
            AddQuad(
                vertices[i],
                vertices[i + 1],
                vertices[i + 2],
                vertices[i + 2],
                Point.Origin,
                Point.Origin,
                Point.Origin,
                Point.Origin,
                color,
                TextureHandle.Invalid,
                isTriangle: true);
        }
    }

    public void DrawTexturedQuad(
        Point p0,
        Point p1,
        Point p2,
        Point p3,
        TextureHandle texture,
        Point uv0,
        Point uv1,
        Point uv2,
        Point uv3,
        ColorRgba tint)
    {
        if (!texture.IsValid)
        {
            throw new ArgumentException("Texture handle is invalid.", nameof(texture));
        }

        AddQuad(p0, p1, p2, p3, uv0, uv1, uv2, uv3, tint, texture, isTriangle: false);
    }

    public void DrawTexturedTriangles(
        ReadOnlySpan<Point> vertices,
        ReadOnlySpan<Point> uvs,
        TextureHandle texture,
        ColorRgba tint)
    {
        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("Triangle vertex count must be a multiple of 3.", nameof(vertices));
        }

        if (uvs.Length != vertices.Length)
        {
            throw new ArgumentException("UV count must equal the triangle vertex count.", nameof(uvs));
        }

        if (!texture.IsValid)
        {
            throw new ArgumentException("Texture handle is invalid.", nameof(texture));
        }

        for (int i = 0; i < vertices.Length; i += 3)
        {
            AddQuad(
                vertices[i],
                vertices[i + 1],
                vertices[i + 2],
                vertices[i + 2],
                uvs[i],
                uvs[i + 1],
                uvs[i + 2],
                uvs[i + 2],
                tint,
                texture,
                isTriangle: true);
        }
    }

    public void FillGradientTriangles(
        ReadOnlySpan<Point> vertices,
        ReadOnlySpan<Point> gradientCoords,
        TextureHandle lut,
        GradientKind kind,
        GradientSpreadMethod spread,
        ColorRgba tint)
    {
        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("Triangle vertex count must be a multiple of 3.", nameof(vertices));
        }

        if (vertices.Length != gradientCoords.Length)
        {
            throw new ArgumentException("Gradient coordinate count must match vertex count.", nameof(gradientCoords));
        }

        if (!lut.IsValid)
        {
            throw new ArgumentException("Gradient LUT handle is invalid.", nameof(lut));
        }

        for (int i = 0; i < vertices.Length; i += 3)
        {
            AddGradientTriangle(
                vertices[i],
                vertices[i + 1],
                vertices[i + 2],
                gradientCoords[i],
                gradientCoords[i + 1],
                gradientCoords[i + 2],
                lut,
                kind,
                spread,
                tint);
        }
    }

    public void PushClip(Rect rectangle)
    {
        Point p0 = _transform.Transform(new Point(rectangle.X, rectangle.Y));
        Point p1 = _transform.Transform(new Point(rectangle.X + rectangle.Width, rectangle.Y));
        Point p2 = _transform.Transform(new Point(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height));
        Point p3 = _transform.Transform(new Point(rectangle.X, rectangle.Y + rectangle.Height));
        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        var clip = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        _clipStack.Push(_clip);
        _clip = _clip is { } current ? Intersect(current, clip) : clip;
    }

    public void PopClip()
    {
        _clip = _clipStack.Count > 0 ? _clipStack.Pop() : null;
    }

    public void PushOpacity(double opacity)
    {
        _opacityStack.Push(_opacity);
        _opacity = Math.Clamp(_opacity * opacity, 0.0, 1.0);
    }

    public void PopOpacity()
    {
        _opacity = _opacityStack.Count > 0 ? _opacityStack.Pop() : 1.0;
    }

    public void PushTransform(Matrix3x2 transform)
    {
        _transformStack.Push(_transform);
        _transform = Matrix3x2.Multiply(_transform, transform);
    }

    public void PopTransform()
    {
        _transform = _transformStack.Count > 0 ? _transformStack.Pop() : Matrix3x2.Identity;
    }

    private void AddQuad(
        Point p0,
        Point p1,
        Point p2,
        Point p3,
        Point uv0,
        Point uv1,
        Point uv2,
        Point uv3,
        ColorRgba color,
        TextureHandle texture,
        bool isTriangle)
    {
        double alpha = Math.Clamp(color.A * _opacity, 0.0, 1.0);
        _records.Add(new QuadRecord(
            _transform.Transform(p0),
            _transform.Transform(p1),
            _transform.Transform(p2),
            _transform.Transform(p3),
            uv0,
            uv1,
            uv2,
            uv3,
            (float)(color.R * alpha),
            (float)(color.G * alpha),
            (float)(color.B * alpha),
            (float)alpha,
            texture,
            _clip,
            isTriangle,
            IsGradient: false,
            GradientKind.Linear,
            GradientSpreadMethod.Pad));
    }

    private void AddGradientTriangle(
        Point p0,
        Point p1,
        Point p2,
        Point g0,
        Point g1,
        Point g2,
        TextureHandle lut,
        GradientKind kind,
        GradientSpreadMethod spread,
        ColorRgba tint)
    {
        double alpha = Math.Clamp(tint.A * _opacity, 0.0, 1.0);
        _records.Add(new QuadRecord(
            _transform.Transform(p0),
            _transform.Transform(p1),
            _transform.Transform(p2),
            _transform.Transform(p2),
            g0,
            g1,
            g2,
            g2,
            (float)(tint.R * alpha),
            (float)(tint.G * alpha),
            (float)(tint.B * alpha),
            (float)alpha,
            lut,
            _clip,
            IsTriangle: true,
            IsGradient: true,
            kind,
            spread));
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        double left = Math.Max(a.Left, b.Left);
        double top = Math.Max(a.Top, b.Top);
        double right = Math.Min(a.Right, b.Right);
        double bottom = Math.Min(a.Bottom, b.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
