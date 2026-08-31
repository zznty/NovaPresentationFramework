using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.MilCmd;

/// <summary>Default visitor. Unknown / later commands are ignored. Override the v1 subset you care about.</summary>
[PublicAPI]
public abstract class MilCommandVisitor : IMilCommandVisitor
{
    public virtual void VisitUnknown(MilCommandKind kind, ReadOnlySpan<byte> payload)
    {
    }

    public virtual void VisitChannelCreateResource(ResourceHandle handle, MilResourceType type)
    {
    }

    public virtual void VisitChannelDeleteResource(ResourceHandle handle)
    {
    }

    public virtual void VisitTransportSyncFlush()
    {
    }

    public virtual void VisitVisualCreate(ResourceHandle handle)
    {
    }

    public virtual void VisitVisualSetOffset(ResourceHandle handle, double offsetX, double offsetY)
    {
    }

    public virtual void VisitVisualSetTransform(ResourceHandle handle, ResourceHandle transform)
    {
    }

    public virtual void VisitVisualSetClip(ResourceHandle handle, ResourceHandle clip)
    {
    }

    public virtual void VisitVisualSetAlpha(ResourceHandle handle, double alpha)
    {
    }

    public virtual void VisitVisualSetContent(ResourceHandle handle, ResourceHandle content)
    {
    }

    public virtual void VisitVisualSetEffect(ResourceHandle handle, ResourceHandle effect)
    {
        _ = handle;
        _ = effect;
    }

    public virtual void VisitVisualSetAlphaMask(ResourceHandle handle, ResourceHandle opacityMask)
    {
        _ = handle;
        _ = opacityMask;
    }

    public virtual void VisitVisualRemoveAllChildren(ResourceHandle handle)
    {
    }

    public virtual void VisitVisualRemoveChild(ResourceHandle handle, ResourceHandle child)
    {
    }

    public virtual void VisitVisualInsertChildAt(ResourceHandle handle, ResourceHandle child, uint index)
    {
    }

    public virtual void VisitTargetSetRoot(ResourceHandle handle, ResourceHandle root)
    {
    }

    public virtual void VisitTargetSetClearColor(ResourceHandle handle, ColorRgba color)
    {
    }

    public virtual void VisitTargetInvalidate(ResourceHandle handle, Rect dirty)
    {
    }

    public virtual void VisitRenderData(ResourceHandle handle, ReadOnlySpan<byte> renderData)
    {
    }

    public virtual void VisitSolidColorBrush(ResourceHandle handle, double opacity, ColorRgba color, ResourceHandle transform)
    {
    }

    public virtual void VisitLinearGradientBrush(
        ResourceHandle handle,
        double opacity,
        Point startPoint,
        Point endPoint,
        BrushMappingMode mappingMode,
        GradientSpreadMethod spreadMethod,
        ReadOnlySpan<GradientStop> stops,
        ResourceHandle transform,
        ResourceHandle relativeTransform)
    {
        _ = handle;
        _ = opacity;
        _ = startPoint;
        _ = endPoint;
        _ = mappingMode;
        _ = spreadMethod;
        _ = stops;
        _ = transform;
        _ = relativeTransform;
    }

    public virtual void VisitRadialGradientBrush(
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
        ResourceHandle relativeTransform)
    {
        _ = handle;
        _ = opacity;
        _ = center;
        _ = radiusX;
        _ = radiusY;
        _ = gradientOrigin;
        _ = mappingMode;
        _ = spreadMethod;
        _ = stops;
        _ = transform;
        _ = relativeTransform;
    }

    public virtual void VisitVisualBrush(
        ResourceHandle handle,
        double opacity,
        Rect viewport,
        Rect viewbox,
        BrushMappingMode viewportUnits,
        BrushMappingMode viewboxUnits,
        Stretch stretch,
        TileMode tileMode,
        ResourceHandle visual,
        ResourceHandle transform)
    {
        _ = handle;
        _ = opacity;
        _ = viewport;
        _ = viewbox;
        _ = viewportUnits;
        _ = viewboxUnits;
        _ = stretch;
        _ = tileMode;
        _ = visual;
        _ = transform;
    }

    public virtual void VisitDropShadowEffect(
        ResourceHandle handle,
        double shadowDepth,
        ColorRgba color,
        double direction,
        double opacity,
        double blurRadius,
        int renderingBias)
    {
        _ = handle;
        _ = shadowDepth;
        _ = color;
        _ = direction;
        _ = opacity;
        _ = blurRadius;
        _ = renderingBias;
    }

    public virtual void VisitBlurEffect(
        ResourceHandle handle,
        double radius,
        int kernelType,
        int renderingBias)
    {
        _ = handle;
        _ = radius;
        _ = kernelType;
        _ = renderingBias;
    }

    public virtual void VisitImageBrush(
        ResourceHandle handle,
        double opacity,
        Rect viewport,
        Rect viewbox,
        BrushMappingMode viewportUnits,
        BrushMappingMode viewboxUnits,
        Stretch stretch,
        TileMode tileMode,
        ResourceHandle imageSource,
        ResourceHandle transform)
    {
        _ = handle;
        _ = opacity;
        _ = viewport;
        _ = viewbox;
        _ = viewportUnits;
        _ = viewboxUnits;
        _ = stretch;
        _ = tileMode;
        _ = imageSource;
        _ = transform;
    }

    public virtual void VisitTranslateTransform(ResourceHandle handle, double x, double y)
    {
    }

    public virtual void VisitScaleTransform(ResourceHandle handle, double scaleX, double scaleY, double centerX, double centerY)
    {
    }

    public virtual void VisitRotateTransform(ResourceHandle handle, double angle, double centerX, double centerY)
    {
    }

    public virtual void VisitMatrixTransform(ResourceHandle handle, Matrix3x2 matrix)
    {
    }

    public virtual void VisitTransformGroup(ResourceHandle handle, ReadOnlySpan<ResourceHandle> children)
    {
    }

    public virtual void VisitPathGeometry(ResourceHandle handle, ResourceHandle transform, ReadOnlySpan<byte> pathData)
    {
    }

    public virtual void VisitRectangleGeometry(ResourceHandle handle, Rect rectangle, double radiusX, double radiusY, ResourceHandle transform)
    {
    }

    public virtual void VisitLineGeometry(ResourceHandle handle, Point start, Point endPoint, ResourceHandle transform)
    {
    }

    public virtual void VisitEllipseGeometry(ResourceHandle handle, Point center, double radiusX, double radiusY, ResourceHandle transform)
    {
    }

    public virtual void VisitPen(ResourceHandle handle, ResourceHandle brush, double thickness, double miterLimit)
    {
    }

    public virtual void VisitGlyphRunCreate(
        ResourceHandle handle,
        FontFaceToken font,
        Point origin,
        float emSize,
        ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<float> advances)
    {
    }

    public virtual void VisitDrawLine(Point start, Point endPoint, ResourceHandle pen)
    {
    }

    public virtual void VisitDrawRectangle(Rect rectangle, ResourceHandle brush, ResourceHandle pen)
    {
    }

    public virtual void VisitDrawRoundedRectangle(Rect rectangle, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen)
    {
    }

    public virtual void VisitDrawEllipse(Point center, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen)
    {
    }

    public virtual void VisitDrawGeometry(ResourceHandle brush, ResourceHandle pen, ResourceHandle geometry)
    {
    }

    public virtual void VisitDrawGlyphRun(ResourceHandle foreground, ResourceHandle glyphRun)
    {
    }

    public virtual void VisitDrawImage(Rect rectangle, ResourceHandle imageSource)
    {
        _ = rectangle;
        _ = imageSource;
    }

    public virtual void VisitDrawDrawing(ResourceHandle drawing)
    {
        _ = drawing;
    }

    public virtual void VisitPushClip(ResourceHandle clip)
    {
    }

    public virtual void VisitPushOpacityMask(ResourceHandle opacityMask)
    {
        _ = opacityMask;
    }

    public virtual void VisitMaskedRange(ResourceHandle mask, ReadOnlyMemory<byte> renderData, ReadOnlyMemory<ResourceHandle> dependents)
    {
        _ = mask;
        _ = renderData;
        _ = dependents;
    }

    public virtual void VisitPushOpacity(double opacity)
    {
    }

    public virtual void VisitPushTransform(ResourceHandle transform)
    {
    }

    public virtual void VisitPushGuidelineSet(ResourceHandle guidelines)
    {
        _ = guidelines;
    }

    public virtual void VisitPushGuidelineY1(double coordinate)
    {
        _ = coordinate;
    }

    public virtual void VisitPushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        _ = leadingCoordinate;
        _ = offsetToDrivenCoordinate;
    }

    public virtual void VisitPushEffect()
    {
    }

    public virtual void VisitPop()
    {
    }
}
