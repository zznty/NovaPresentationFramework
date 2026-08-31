using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Vulkan;

/// <summary>
/// Safe command queue for one frame. Geometry is recorded in local (double) space;
/// the presenter converts to clip-space floats immediately before upload.
/// </summary>
[PublicAPI]
public interface IRasterCommandList
{
    public void Clear(ColorRgba color);

    public void FillRectangle(Rect rectangle, ColorRgba color);

    public void FillQuad(Point p0, Point p1, Point p2, Point p3, ColorRgba color);

    public void FillTriangles(ReadOnlySpan<Point> vertices, ColorRgba color);

    /// <summary>
    /// Fills triangles with a gradient. <paramref name="gradientCoords"/> carries one
    /// gradient-space coordinate per vertex: for <see cref="GradientKind.Linear"/> the x
    /// component is the 0..1 parameter along the gradient axis (y ignored); for
    /// <see cref="GradientKind.Radial"/> the coordinate is the position offset from the
    /// gradient origin scaled by 1/radius, and the parameter is its length.
    /// <paramref name="lut"/> is a 1-D gradient LUT (256x1 RGBA8, premultiplied) sampled at
    /// the folded parameter. <paramref name="tint"/> is a premultiplied color multiplier
    /// (brush opacity).
    /// </summary>
    public void FillGradientTriangles(
        ReadOnlySpan<Point> vertices,
        ReadOnlySpan<Point> gradientCoords,
        TextureHandle lut,
        GradientKind kind,
        GradientSpreadMethod spread,
        ColorRgba tint);

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
        ColorRgba tint);

    /// <summary>
    /// Fills arbitrary triangles with a texture. <paramref name="uvs"/> carries one
    /// texture-relative coordinate per vertex (parallel to <paramref name="vertices"/>);
    /// the count must be a multiple of 3. Same sampling/bind semantics as
    /// <see cref="DrawTexturedQuad"/>.
    /// </summary>
    public void DrawTexturedTriangles(
        ReadOnlySpan<Point> vertices,
        ReadOnlySpan<Point> uvs,
        TextureHandle texture,
        ColorRgba tint);

    public void PushClip(Rect rectangle);

    public void PopClip();

    public void PushOpacity(double opacity);

    public void PopOpacity();

    public void PushTransform(Matrix3x2 transform);

    public void PopTransform();
}