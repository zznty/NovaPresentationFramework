using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.MilCmd;

[PublicAPI]
public interface IMilCommandVisitor
{
    public void VisitUnknown(MilCommandKind kind, ReadOnlySpan<byte> payload);

    public void VisitChannelCreateResource(ResourceHandle handle, MilResourceType type);

    public void VisitChannelDeleteResource(ResourceHandle handle);

    public void VisitTransportSyncFlush();

    public void VisitVisualCreate(ResourceHandle handle);

    public void VisitVisualSetOffset(ResourceHandle handle, double offsetX, double offsetY);

    public void VisitVisualSetTransform(ResourceHandle handle, ResourceHandle transform);

    public void VisitVisualSetClip(ResourceHandle handle, ResourceHandle clip);

    public void VisitVisualSetAlpha(ResourceHandle handle, double alpha);

    public void VisitVisualSetContent(ResourceHandle handle, ResourceHandle content);

    public void VisitVisualSetEffect(ResourceHandle handle, ResourceHandle effect);

    public void VisitVisualSetAlphaMask(ResourceHandle handle, ResourceHandle opacityMask);

    public void VisitVisualRemoveAllChildren(ResourceHandle handle);

    public void VisitVisualRemoveChild(ResourceHandle handle, ResourceHandle child);

    public void VisitVisualInsertChildAt(ResourceHandle handle, ResourceHandle child, uint index);

    public void VisitTargetSetRoot(ResourceHandle handle, ResourceHandle root);

    public void VisitTargetSetClearColor(ResourceHandle handle, ColorRgba color);

    public void VisitTargetInvalidate(ResourceHandle handle, Rect dirty);

    public void VisitRenderData(ResourceHandle handle, ReadOnlySpan<byte> renderData);

    public void VisitSolidColorBrush(ResourceHandle handle, double opacity, ColorRgba color, ResourceHandle transform);

    public void VisitLinearGradientBrush(
        ResourceHandle handle,
        double opacity,
        Point startPoint,
        Point endPoint,
        BrushMappingMode mappingMode,
        GradientSpreadMethod spreadMethod,
        ReadOnlySpan<GradientStop> stops,
        ResourceHandle transform,
        ResourceHandle relativeTransform);

    public void VisitRadialGradientBrush(
        ResourceHandle handle,
        double opacity,
        Point center,
        double radiusX,
        double radiusY,
        Point gradientOrigin,
        BrushMappingMode mappingMode,
        GradientSpreadMethod spreadMethod,
        ReadOnlySpan<GradientStop> stops,
        ResourceHandle transform,
        ResourceHandle relativeTransform);

    public void VisitVisualBrush(
        ResourceHandle handle,
        double opacity,
        Rect viewport,
        Rect viewbox,
        BrushMappingMode viewportUnits,
        BrushMappingMode viewboxUnits,
        Stretch stretch,
        TileMode tileMode,
        ResourceHandle visual,
        ResourceHandle transform);

    public void VisitDropShadowEffect(
        ResourceHandle handle,
        double shadowDepth,
        ColorRgba color,
        double direction,
        double opacity,
        double blurRadius,
        int renderingBias);

    public void VisitBlurEffect(
        ResourceHandle handle,
        double radius,
        int kernelType,
        int renderingBias);
    public void VisitImageBrush(
        ResourceHandle handle,
        double opacity,
        Rect viewport,
        Rect viewbox,
        BrushMappingMode viewportUnits,
        BrushMappingMode viewboxUnits,
        Stretch stretch,
        TileMode tileMode,
        ResourceHandle imageSource,
        ResourceHandle transform);

    public void VisitTranslateTransform(ResourceHandle handle, double x, double y);

    public void VisitScaleTransform(ResourceHandle handle, double scaleX, double scaleY, double centerX, double centerY);

    public void VisitRotateTransform(ResourceHandle handle, double angle, double centerX, double centerY);

    public void VisitMatrixTransform(ResourceHandle handle, Matrix3x2 matrix);

    public void VisitTransformGroup(ResourceHandle handle, ReadOnlySpan<ResourceHandle> children);

    public void VisitPathGeometry(ResourceHandle handle, ResourceHandle transform, ReadOnlySpan<byte> pathData);

    public void VisitRectangleGeometry(ResourceHandle handle, Rect rectangle, double radiusX, double radiusY, ResourceHandle transform);

    public void VisitLineGeometry(ResourceHandle handle, Point start, Point endPoint, ResourceHandle transform);

    public void VisitEllipseGeometry(ResourceHandle handle, Point center, double radiusX, double radiusY, ResourceHandle transform);

    public void VisitPen(ResourceHandle handle, ResourceHandle brush, double thickness, double miterLimit);

    public void VisitGlyphRunCreate(
        ResourceHandle handle,
        FontFaceToken font,
        Point origin,
        float emSize,
        ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<float> advances);

    public void VisitDrawLine(Point start, Point endPoint, ResourceHandle pen);

    public void VisitDrawRectangle(Rect rectangle, ResourceHandle brush, ResourceHandle pen);

    public void VisitDrawRoundedRectangle(Rect rectangle, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen);

    public void VisitDrawEllipse(Point center, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen);

    public void VisitDrawGeometry(ResourceHandle brush, ResourceHandle pen, ResourceHandle geometry);

    public void VisitDrawGlyphRun(ResourceHandle foreground, ResourceHandle glyphRun);

    public void VisitDrawImage(Rect rectangle, ResourceHandle imageSource);

    public void VisitDrawDrawing(ResourceHandle drawing);

    public void VisitPushClip(ResourceHandle clip);

    public void VisitPushOpacityMask(ResourceHandle opacityMask);

    /// <summary>
    /// A balanced PushOpacityMask/Pop pair collapsed by the parser: the render-data bytes
    /// between the two (inclusive of neither op) plus the stream's dependents. The parser
    /// delivers this instead of the inline ops so the slave can render the range offscreen
    /// and composite it with the mask brush. Nested masked ranges are delivered as nested
    /// calls; the mask stack is implicit.
    /// </summary>
    public void VisitMaskedRange(ResourceHandle mask, ReadOnlyMemory<byte> renderData, ReadOnlyMemory<ResourceHandle> dependents);

    public void VisitPushOpacity(double opacity);

    public void VisitPushTransform(ResourceHandle transform);

    public void VisitPushGuidelineSet(ResourceHandle guidelines);

    public void VisitPushGuidelineY1(double coordinate);

    public void VisitPushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate);

    public void VisitPushEffect();

    public void VisitPop();
}
