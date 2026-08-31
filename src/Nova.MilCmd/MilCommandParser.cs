using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.MilCmd;

/// <summary>
/// Span-first MILCMD / RenderData decoder. Channel commands start with a 4-byte
/// <see cref="MilCommandKind"/>; RenderData records are <c>{int Size; MILCMD Id}</c>
/// plus payload, QWORD-aligned. Handles inside RenderData payloads are 1-based
/// indices into the caller's dependent list (0 = null).
/// </summary>
/// <remarks>
/// Layouts match the WPF generated wire structs (dotnet/wpf pin 1346571e,
/// <c>wgx_commands.cs</c>): Pack=1, little-endian, 4-byte <c>DUCE.ResourceHandle</c>.
/// All size fields on variable-length commands (<c>ChildrenSize</c>, <c>FiguresSize</c>,
/// <c>GradientStopsSize</c>, …) are byte counts of the trailing blob.
/// <c>MILCMD_GLYPHRUN_CREATE</c> always carries <c>GlyphCount</c> advance widths after the
/// glyph indices; per-glyph offsets follow only when the <c>HasOffsets</c> (0x10) flag is set.
/// </remarks>
[PublicAPI]
public static class MilCommandParser
{
    private const ushort GlyphRunFlagHasOffsets = 0x10;

    private const int GlyphRunHeaderSize = 76;

    /// <summary>
    /// Parses a concatenated channel command stream, invoking <paramref name="visitor"/>
    /// for each record. Unknown kinds whose generated struct size is known are skipped and
    /// reported via <see cref="IMilCommandVisitor.VisitUnknown"/>; unknown kinds with no
    /// known wire size throw <see cref="MilParseException"/>.
    /// </summary>
    /// <param name="buffer">Little-endian channel command bytes.</param>
    /// <param name="visitor">Visitor receiving decoded commands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    /// <exception cref="MilParseException">The stream is truncated or contains an unsized command.</exception>
    public static void ParseChannel(ReadOnlySpan<byte> buffer, IMilCommandVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        int offset = 0;
        while (offset < buffer.Length)
        {
            int recordStart = offset;
            uint type = ReadUInt32(buffer, ref offset);
            var kind = (MilCommandKind)type;

            switch (kind)
            {
                case MilCommandKind.TransportSyncFlush:
                    visitor.VisitTransportSyncFlush();
                    break;

                case MilCommandKind.ChannelCreateResource:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        var resourceType = (MilResourceType)ReadUInt32(buffer, ref offset);
                        visitor.VisitChannelCreateResource(handle, resourceType);
                        break;
                    }

                case MilCommandKind.ChannelDeleteResource:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // resource type is ignored at visit
                        visitor.VisitChannelDeleteResource(handle);
                        break;
                    }

                case MilCommandKind.VisualCreate:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualCreate(handle);
                        break;
                    }

                case MilCommandKind.VisualSetOffset:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double offsetX = ReadDouble(buffer, ref offset);
                        double offsetY = ReadDouble(buffer, ref offset);
                        visitor.VisitVisualSetOffset(handle, offsetX, offsetY);
                        break;
                    }

                case MilCommandKind.VisualSetTransform:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualSetTransform(handle, transform);
                        break;
                    }

                case MilCommandKind.VisualSetClip:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle clip = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualSetClip(handle, clip);
                        break;
                    }

                case MilCommandKind.VisualSetAlpha:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double alpha = ReadDouble(buffer, ref offset);
                        visitor.VisitVisualSetAlpha(handle, alpha);
                        break;
                    }

                case MilCommandKind.VisualSetContent:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle content = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualSetContent(handle, content);
                        break;
                    }

                case MilCommandKind.VisualSetEffect:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle effect = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualSetEffect(handle, effect);
                        break;
                    }

                case MilCommandKind.VisualSetAlphaMask:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle opacityMask = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualSetAlphaMask(handle, opacityMask);
                        break;
                    }

                case MilCommandKind.VisualRemoveAllChildren:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualRemoveAllChildren(handle);
                        break;
                    }

                case MilCommandKind.VisualRemoveChild:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle child = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualRemoveChild(handle, child);
                        break;
                    }

                case MilCommandKind.VisualInsertChildAt:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle child = ReadHandle(buffer, ref offset);
                        uint index = ReadUInt32(buffer, ref offset);
                        visitor.VisitVisualInsertChildAt(handle, child, index);
                        break;
                    }

                case MilCommandKind.TargetSetRoot:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle root = ReadHandle(buffer, ref offset);
                        visitor.VisitTargetSetRoot(handle, root);
                        break;
                    }

                case MilCommandKind.TargetSetClearColor:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ColorRgba color = ReadColorRgba(buffer, ref offset);
                        visitor.VisitTargetSetClearColor(handle, color);
                        break;
                    }

                case MilCommandKind.TargetInvalidate:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        int left = ReadInt32(buffer, ref offset);
                        int top = ReadInt32(buffer, ref offset);
                        int right = ReadInt32(buffer, ref offset);
                        int bottom = ReadInt32(buffer, ref offset);
                        var dirty = new Rect(left, top, (double)right - left, (double)bottom - top);
                        visitor.VisitTargetInvalidate(handle, dirty);
                        break;
                    }

                case MilCommandKind.RenderData:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        uint cbData = ReadUInt32(buffer, ref offset);
                        if (cbData > int.MaxValue)
                        {
                            throw new MilParseException("RenderData blob too large", offset - 4);
                        }

                        ReadOnlySpan<byte> renderData = ReadBytes(buffer, ref offset, (int)cbData);
                        visitor.VisitRenderData(handle, renderData);
                        break;
                    }

                case MilCommandKind.TranslateTransform:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double x = ReadDouble(buffer, ref offset);
                        double y = ReadDouble(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // hXAnimations, hYAnimations
                        visitor.VisitTranslateTransform(handle, x, y);
                        break;
                    }

                case MilCommandKind.ScaleTransform:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double scaleX = ReadDouble(buffer, ref offset);
                        double scaleY = ReadDouble(buffer, ref offset);
                        double centerX = ReadDouble(buffer, ref offset);
                        double centerY = ReadDouble(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 4 * sizeof(uint)); // animation handles
                        visitor.VisitScaleTransform(handle, scaleX, scaleY, centerX, centerY);
                        break;
                    }

                case MilCommandKind.RotateTransform:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double angle = ReadDouble(buffer, ref offset);
                        double centerX = ReadDouble(buffer, ref offset);
                        double centerY = ReadDouble(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 3 * sizeof(uint)); // animation handles
                        visitor.VisitRotateTransform(handle, angle, centerX, centerY);
                        break;
                    }

                case MilCommandKind.MatrixTransform:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        Matrix3x2 matrix = ReadMatrix3x2(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hMatrixAnimations
                        visitor.VisitMatrixTransform(handle, matrix);
                        break;
                    }

                case MilCommandKind.LineGeometry:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        Point start = ReadPoint(buffer, ref offset);
                        Point end = ReadPoint(buffer, ref offset);
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // animation handles
                        visitor.VisitLineGeometry(handle, start, end, transform);
                        break;
                    }

                case MilCommandKind.RectangleGeometry:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double radiusX = ReadDouble(buffer, ref offset);
                        double radiusY = ReadDouble(buffer, ref offset);
                        Rect rectangle = ReadRect(buffer, ref offset);
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 3 * sizeof(uint)); // animation handles
                        visitor.VisitRectangleGeometry(handle, rectangle, radiusX, radiusY, transform);
                        break;
                    }

                case MilCommandKind.EllipseGeometry:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double radiusX = ReadDouble(buffer, ref offset);
                        double radiusY = ReadDouble(buffer, ref offset);
                        Point center = ReadPoint(buffer, ref offset);
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 3 * sizeof(uint)); // animation handles
                        visitor.VisitEllipseGeometry(handle, center, radiusX, radiusY, transform);
                        break;
                    }

                case MilCommandKind.SolidColorBrush:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double opacity = ReadDouble(buffer, ref offset);
                        ColorRgba color = ReadColorRgba(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hOpacityAnimations
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // hRelativeTransform, hColorAnimations
                        visitor.VisitSolidColorBrush(handle, opacity, color, transform);
                        break;
                    }

                case MilCommandKind.BlurEffect:
                    {
                        // MILCMD_BLUREFFECT (wgx_commands.cs): Type(4) Handle(4) Radius(double)
                        // hRadiusAnimations(4) KernelType(4) RenderingBias(4).
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double radius = ReadDouble(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hRadiusAnimations
                        int kernelType = ReadInt32(buffer, ref offset);
                        int renderingBias = ReadInt32(buffer, ref offset);
                        visitor.VisitBlurEffect(handle, radius, kernelType, renderingBias);
                        break;
                    }

                case MilCommandKind.DropShadowEffect:
                    {
                        // MILCMD_DROPSHADOWEFFECT (wgx_commands.cs): Type(4) Handle(4)
                        // ShadowDepth(double) Color(MilColorF) Direction(double) Opacity(double)
                        // BlurRadius(double) then 5 animation handles and RenderingBias(4).
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double shadowDepth = ReadDouble(buffer, ref offset);
                        ColorRgba color = ReadColorRgba(buffer, ref offset);
                        double direction = ReadDouble(buffer, ref offset);
                        double opacity = ReadDouble(buffer, ref offset);
                        double blurRadius = ReadDouble(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 5 * sizeof(uint)); // animation handles
                        int renderingBias = ReadInt32(buffer, ref offset);
                        visitor.VisitDropShadowEffect(handle, shadowDepth, color, direction, opacity, blurRadius, renderingBias);
                        break;
                    }

                case MilCommandKind.Pen:
                    {
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double thickness = ReadDouble(buffer, ref offset);
                        double miterLimit = ReadDouble(buffer, ref offset);
                        ResourceHandle brush = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hThicknessAnimations
                        SkipBytes(buffer, ref offset, 4 * sizeof(uint)); // caps + join
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hDashStyle
                        visitor.VisitPen(handle, brush, thickness, miterLimit);
                        break;
                    }

                case MilCommandKind.GlyphRunCreate:
                    ParseGlyphRunCreate(buffer, ref offset, visitor);
                    break;

                case MilCommandKind.TransformGroup:
                    {
                        // MILCMD_TRANSFORMGROUP: Type(4) Handle(4) ChildrenSize(4) then
                        // ChildrenSize bytes of child transform handles.
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        uint childrenSize = ReadUInt32(buffer, ref offset);
                        int childCount = checked((int)(childrenSize / sizeof(uint)));
                        var children = new ResourceHandle[childCount];
                        for (int i = 0; i < childCount; i++)
                        {
                            children[i] = ReadHandle(buffer, ref offset);
                        }

                        visitor.VisitTransformGroup(handle, children);
                        break;
                    }

                case MilCommandKind.PathGeometry:
                    {
                        // MILCMD_PATHGEOMETRY: Type(4) Handle(4) hTransform(4) FillRule(4)
                        // FiguresSize(4) then the serialized MIL path stream (FiguresSize bytes).
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        uint fillRule = ReadUInt32(buffer, ref offset);
                        uint dataSize = ReadUInt32(buffer, ref offset);
                        _ = fillRule;
                        ReadOnlySpan<byte> pathData = ReadBytes(buffer, ref offset, checked((int)dataSize));
                        visitor.VisitPathGeometry(handle, transform, pathData);
                        break;
                    }

                case MilCommandKind.LinearGradientBrush:
                    {
                        // MILCMD_LINEARGRADIENTBRUSH (wgx_commands.cs): fixed fields then
                        // GradientStopsSize bytes of MIL_GRADIENTSTOP (double Position +
                        // MilColorF Color, 24 bytes each).
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double opacity = ReadDouble(buffer, ref offset);
                        Point startPoint = ReadPoint(buffer, ref offset);
                        Point endPoint = ReadPoint(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hOpacityAnimations
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        ResourceHandle relativeTransform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // ColorInterpolationMode
                        var mappingMode = (BrushMappingMode)ReadUInt32(buffer, ref offset);
                        var spreadMethod = (GradientSpreadMethod)ReadUInt32(buffer, ref offset);
                        uint stopsSize = ReadUInt32(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // hStartPointAnimations, hEndPointAnimations
                        GradientStop[] stops = ParseGradientStops(buffer, ref offset, stopsSize);
                        visitor.VisitLinearGradientBrush(handle, opacity, startPoint, endPoint, mappingMode, spreadMethod, stops, transform, relativeTransform);
                        break;
                    }

                case MilCommandKind.RadialGradientBrush:
                    {
                        // MILCMD_RADIALGRADIENTBRUSH (wgx_commands.cs): fixed fields then the
                        // gradient stop blob (see LinearGradientBrush).
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double opacity = ReadDouble(buffer, ref offset);
                        Point center = ReadPoint(buffer, ref offset);
                        double radiusX = ReadDouble(buffer, ref offset);
                        double radiusY = ReadDouble(buffer, ref offset);
                        Point gradientOrigin = ReadPoint(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hOpacityAnimations
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        ResourceHandle relativeTransform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // ColorInterpolationMode
                        var mappingMode = (BrushMappingMode)ReadUInt32(buffer, ref offset);
                        var spreadMethod = (GradientSpreadMethod)ReadUInt32(buffer, ref offset);
                        uint stopsSize = ReadUInt32(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 4 * sizeof(uint)); // hCenterAnimations, hRadiusXAnimations, hRadiusYAnimations, hGradientOriginAnimations
                        GradientStop[] stops = ParseGradientStops(buffer, ref offset, stopsSize);
                        visitor.VisitRadialGradientBrush(handle, opacity, center, radiusX, radiusY, gradientOrigin, mappingMode, spreadMethod, stops, transform, relativeTransform);
                        break;
                    }

                case MilCommandKind.VisualBrush:
                    {
                        // MILCMD_VISUALBRUSH (wgx_commands.cs): tile-brush fields then hVisual.
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double opacity = ReadDouble(buffer, ref offset);
                        Rect viewport = ReadRect(buffer, ref offset);
                        Rect viewbox = ReadRect(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(double)); // CacheInvalidationThresholdMinimum/Maximum
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hOpacityAnimations
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hRelativeTransform
                        var viewportUnits = (BrushMappingMode)ReadUInt32(buffer, ref offset);
                        var viewboxUnits = (BrushMappingMode)ReadUInt32(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // hViewportAnimations, hViewboxAnimations
                        var stretch = (Stretch)ReadUInt32(buffer, ref offset);
                        var tileMode = (TileMode)ReadUInt32(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // AlignmentX, AlignmentY
                        SkipBytes(buffer, ref offset, sizeof(uint)); // CachingHint
                        ResourceHandle visual = ReadHandle(buffer, ref offset);
                        visitor.VisitVisualBrush(handle, opacity, viewport, viewbox, viewportUnits, viewboxUnits, stretch, tileMode, visual, transform);
                        break;
                    }

                case MilCommandKind.ImageBrush:
                    {
                        // MILCMD_IMAGEBRUSH (wgx_commands.cs): tile-brush fields then hImageSource.
                        ResourceHandle handle = ReadHandle(buffer, ref offset);
                        double opacity = ReadDouble(buffer, ref offset);
                        Rect viewport = ReadRect(buffer, ref offset);
                        Rect viewbox = ReadRect(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(double)); // CacheInvalidationThresholdMinimum/Maximum
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hOpacityAnimations
                        ResourceHandle transform = ReadHandle(buffer, ref offset);
                        SkipBytes(buffer, ref offset, sizeof(uint)); // hRelativeTransform
                        var viewportUnits = (BrushMappingMode)ReadUInt32(buffer, ref offset);
                        var viewboxUnits = (BrushMappingMode)ReadUInt32(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // hViewportAnimations, hViewboxAnimations
                        var stretch = (Stretch)ReadUInt32(buffer, ref offset);
                        var tileMode = (TileMode)ReadUInt32(buffer, ref offset);
                        SkipBytes(buffer, ref offset, 2 * sizeof(uint)); // AlignmentX, AlignmentY
                        SkipBytes(buffer, ref offset, sizeof(uint)); // CachingHint
                        ResourceHandle imageSource = ReadHandle(buffer, ref offset);
                        visitor.VisitImageBrush(handle, opacity, viewport, viewbox, viewportUnits, viewboxUnits, stretch, tileMode, imageSource, transform);
                        break;
                    }

                case MilCommandKind.Invalid:
                case MilCommandKind.TransportDestroyResourcesOnChannel:
                case MilCommandKind.PartitionRegisterForNotifications:
                case MilCommandKind.ChannelRequestTier:
                case MilCommandKind.PartitionSetVBlankSyncMode:
                case MilCommandKind.PartitionNotifyPresent:
                case MilCommandKind.ChannelDuplicateHandle:
                case MilCommandKind.D3DImage:
                case MilCommandKind.D3DImagePresent:
                case MilCommandKind.BitmapSource:
                case MilCommandKind.BitmapInvalidate:
                case MilCommandKind.DoubleResource:
                case MilCommandKind.ColorResource:
                case MilCommandKind.PointResource:
                case MilCommandKind.RectResource:
                case MilCommandKind.SizeResource:
                case MilCommandKind.MatrixResource:
                case MilCommandKind.Point3DResource:
                case MilCommandKind.Vector3DResource:
                case MilCommandKind.QuaternionResource:
                case MilCommandKind.MediaPlayer:
                case MilCommandKind.EtwEventResource:
                case MilCommandKind.VisualSetCacheMode:
                case MilCommandKind.VisualSetRenderOptions:
                case MilCommandKind.VisualSetGuidelineCollection:
                case MilCommandKind.VisualSetScrollableAreaClip:
                case MilCommandKind.Viewport3DVisualSetCamera:
                case MilCommandKind.Viewport3DVisualSetViewport:
                case MilCommandKind.Viewport3DVisualSet3DChild:
                case MilCommandKind.Visual3DSetContent:
                case MilCommandKind.Visual3DSetTransform:
                case MilCommandKind.Visual3DRemoveAllChildren:
                case MilCommandKind.Visual3DRemoveChild:
                case MilCommandKind.Visual3DInsertChildAt:
                case MilCommandKind.HwndTargetCreate:
                case MilCommandKind.HwndTargetSuppressLayered:
                case MilCommandKind.TargetUpdateWindowSettings:
                case MilCommandKind.GenericTargetCreate:
                case MilCommandKind.TargetSetFlags:
                case MilCommandKind.HwndTargetDpiChanged:
                case MilCommandKind.DoubleBufferedBitmap:
                case MilCommandKind.DoubleBufferedBitmapCopyForward:
                case MilCommandKind.PartitionNotifyPolicyChangeForNonInteractiveMode:
                case MilCommandKind.DrawLine:
                case MilCommandKind.DrawLineAnimate:
                case MilCommandKind.DrawRectangle:
                case MilCommandKind.DrawRectangleAnimate:
                case MilCommandKind.DrawRoundedRectangle:
                case MilCommandKind.DrawRoundedRectangleAnimate:
                case MilCommandKind.DrawEllipse:
                case MilCommandKind.DrawEllipseAnimate:
                case MilCommandKind.DrawGeometry:
                case MilCommandKind.DrawImage:
                case MilCommandKind.DrawImageAnimate:
                case MilCommandKind.DrawGlyphRun:
                case MilCommandKind.DrawDrawing:
                case MilCommandKind.DrawVideo:
                case MilCommandKind.DrawVideoAnimate:
                case MilCommandKind.PushClip:
                case MilCommandKind.PushOpacityMask:
                case MilCommandKind.PushOpacity:
                case MilCommandKind.PushOpacityAnimate:
                case MilCommandKind.PushTransform:
                case MilCommandKind.PushGuidelineSet:
                case MilCommandKind.PushGuidelineY1:
                case MilCommandKind.PushGuidelineY2:
                case MilCommandKind.PushEffect:
                case MilCommandKind.Pop:
                case MilCommandKind.AxisAngleRotation3D:
                case MilCommandKind.QuaternionRotation3D:
                case MilCommandKind.PerspectiveCamera:
                case MilCommandKind.OrthographicCamera:
                case MilCommandKind.MatrixCamera:
                case MilCommandKind.Model3DGroup:
                case MilCommandKind.AmbientLight:
                case MilCommandKind.DirectionalLight:
                case MilCommandKind.PointLight:
                case MilCommandKind.SpotLight:
                case MilCommandKind.GeometryModel3D:
                case MilCommandKind.MeshGeometry3D:
                case MilCommandKind.MaterialGroup:
                case MilCommandKind.DiffuseMaterial:
                case MilCommandKind.SpecularMaterial:
                case MilCommandKind.EmissiveMaterial:
                case MilCommandKind.Transform3DGroup:
                case MilCommandKind.TranslateTransform3D:
                case MilCommandKind.ScaleTransform3D:
                case MilCommandKind.RotateTransform3D:
                case MilCommandKind.MatrixTransform3D:
                case MilCommandKind.PixelShader:
                case MilCommandKind.ImplicitInputBrush:
                case MilCommandKind.ShaderEffect:
                case MilCommandKind.DrawingImage:
                case MilCommandKind.SkewTransform:
                case MilCommandKind.GeometryGroup:
                case MilCommandKind.CombinedGeometry:
                case MilCommandKind.DrawingBrush:
                case MilCommandKind.BitmapCacheBrush:
                case MilCommandKind.DashStyle:
                case MilCommandKind.GeometryDrawing:
                case MilCommandKind.GlyphRunDrawing:
                case MilCommandKind.ImageDrawing:
                case MilCommandKind.VideoDrawing:
                case MilCommandKind.DrawingGroup:
                case MilCommandKind.GuidelineSet:
                case MilCommandKind.BitmapCache:
                    VisitUnknownOrThrow(buffer, recordStart, ref offset, kind, visitor);
                    break;

                default:
                    throw new MilParseException($"MILCMD 0x{type:x} is not a valid kind", recordStart);
            }
        }
    }

    /// <summary>
    /// Dispatches a channel command that has no v1 visitor: fixed-size commands are skipped via
    /// the size table, variable-size commands via their declared byte counts, and anything else
    /// throws <see cref="MilParseException"/>.
    /// </summary>
    private static void VisitUnknownOrThrow(
        ReadOnlySpan<byte> buffer,
        int recordStart,
        ref int offset,
        MilCommandKind kind,
        IMilCommandVisitor visitor)
    {
        if (TryGetFixedBodySize(kind, out int fixedBodySize))
        {
            ReadOnlySpan<byte> payload = ReadBytes(buffer, ref offset, fixedBodySize);
            visitor.VisitUnknown(kind, payload);
            return;
        }

        if (TrySkipVariableRecord(buffer, recordStart, kind, out ReadOnlySpan<byte> variablePayload))
        {
            offset = recordStart + 4 + variablePayload.Length;
            visitor.VisitUnknown(kind, variablePayload);
            return;
        }

        throw new MilParseException($"MILCMD {(uint)kind:x} has no known wire size", recordStart);
    }

    /// <summary>
    /// Parses a RenderData dependent stream. Each record is
    /// <c>{int Size; uint Id}</c> where <c>Size</c> is the total record length
    /// (header + payload + padding), QWORD aligned. Payload handles are 1-based
    /// indices into <paramref name="dependents"/>; index 0 maps to
    /// <see cref="ResourceHandle.Null"/>.
    /// </summary>
    /// <param name="buffer">Little-endian RenderData bytes (the <c>cbData</c> blob of a RenderData channel command).</param>
    /// <param name="dependents">Channel resource handles referenced by the stream.</param>
    /// <param name="visitor">Visitor receiving decoded draw/push/pop commands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    /// <exception cref="MilParseException">A record header is invalid, a record overruns the buffer, or the stream is truncated.</exception>
    public static void ParseRenderData(
        ReadOnlySpan<byte> buffer,
        ReadOnlySpan<ResourceHandle> dependents,
        IMilCommandVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        // The visitor that receives the VisitMaskedRange deliveries; the parameter is
        // swapped to Discard while skipping the interior of a masked pair.
        IMilCommandVisitor deliverTo = visitor;

        // A PushOpacityMask/Pop pair is collapsed into a single VisitMaskedRange
        // delivery: the parser consumes the pair, skips the ops between them
        // (dispatching to a discard visitor) and hands the raw range to the real
        // visitor so it can render the range offscreen and composite it with the
        // mask. Nested masks are counted; the state spans the whole parse so a pair
        // survives record boundaries (the DUCE contract keeps them inside one
        // record, but the tracking is record-independent).
        int maskDepth = 0;
        int maskStart = 0;
        ResourceHandle maskBrush = default;

        int offset = 0;
        while (offset < buffer.Length)
        {
            int recordStart = offset;
            int size = ReadInt32(buffer, ref offset);
            uint id = ReadUInt32(buffer, ref offset);

            if (size < 8)
            {
                throw new MilParseException("RenderData record smaller than its header", recordStart);
            }

            if (size % 8 != 0)
            {
                throw new MilParseException("RenderData record is not QWORD aligned", recordStart);
            }

            if (size > buffer.Length - recordStart)
            {
                throw new MilParseException("RenderData record exceeds the remaining buffer", recordStart);
            }

            ReadOnlySpan<byte> payload = buffer.Slice(recordStart + 8, size - 8);
            int payloadOffset = 0;

            switch ((MilCommandKind)id)
            {
                case MilCommandKind.DrawLine:
                    {
                        Point start = ReadPoint(payload, ref payloadOffset);
                        Point end = ReadPoint(payload, ref payloadOffset);
                        ResourceHandle pen = ReadDependent(payload, ref payloadOffset, dependents);
                        visitor.VisitDrawLine(start, end, pen);
                        break;
                    }

                case MilCommandKind.DrawRectangle:
                    {
                        Rect rectangle = ReadRect(payload, ref payloadOffset);
                        ResourceHandle brush = ReadDependent(payload, ref payloadOffset, dependents);
                        ResourceHandle pen = ReadDependent(payload, ref payloadOffset, dependents);
                        visitor.VisitDrawRectangle(rectangle, brush, pen);
                        break;
                    }

                case MilCommandKind.DrawRoundedRectangle:
                    {
                        Rect rectangle = ReadRect(payload, ref payloadOffset);
                        double radiusX = ReadDouble(payload, ref payloadOffset);
                        double radiusY = ReadDouble(payload, ref payloadOffset);
                        ResourceHandle brush = ReadDependent(payload, ref payloadOffset, dependents);
                        ResourceHandle pen = ReadDependent(payload, ref payloadOffset, dependents);
                        visitor.VisitDrawRoundedRectangle(rectangle, radiusX, radiusY, brush, pen);
                        break;
                    }

                case MilCommandKind.DrawEllipse:
                    {
                        Point center = ReadPoint(payload, ref payloadOffset);
                        double radiusX = ReadDouble(payload, ref payloadOffset);
                        double radiusY = ReadDouble(payload, ref payloadOffset);
                        ResourceHandle brush = ReadDependent(payload, ref payloadOffset, dependents);
                        ResourceHandle pen = ReadDependent(payload, ref payloadOffset, dependents);
                        visitor.VisitDrawEllipse(center, radiusX, radiusY, brush, pen);
                        break;
                    }

                case MilCommandKind.DrawGeometry:
                    {
                        ResourceHandle brush = ReadDependent(payload, ref payloadOffset, dependents);
                        ResourceHandle pen = ReadDependent(payload, ref payloadOffset, dependents);
                        ResourceHandle geometry = ReadDependent(payload, ref payloadOffset, dependents);
                        visitor.VisitDrawGeometry(brush, pen, geometry);
                        break;
                    }

                case MilCommandKind.DrawGlyphRun:
                    {
                        ResourceHandle foreground = ReadDependent(payload, ref payloadOffset, dependents);
                        ResourceHandle glyphRun = ReadDependent(payload, ref payloadOffset, dependents);
                        visitor.VisitDrawGlyphRun(foreground, glyphRun);
                        break;
                    }

                case MilCommandKind.DrawImage:
                    {
                        Rect rectangle = ReadRect(payload, ref payloadOffset);
                        ResourceHandle imageSource = ReadDependent(payload, ref payloadOffset, dependents);
                        SkipBytes(payload, ref payloadOffset, sizeof(uint)); // QWORD pad
                        visitor.VisitDrawImage(rectangle, imageSource);
                        break;
                    }

                case MilCommandKind.DrawImageAnimate:
                    {
                        Rect rectangle = ReadRect(payload, ref payloadOffset);
                        ResourceHandle imageSource = ReadDependent(payload, ref payloadOffset, dependents);
                        SkipBytes(payload, ref payloadOffset, sizeof(uint)); // hRectangleAnimations is ignored at visit
                        visitor.VisitDrawImage(rectangle, imageSource);
                        break;
                    }

                case MilCommandKind.DrawDrawing:
                    {
                        ResourceHandle drawing = ReadDependent(payload, ref payloadOffset, dependents);
                        SkipBytes(payload, ref payloadOffset, sizeof(uint)); // QWORD pad
                        visitor.VisitDrawDrawing(drawing);
                        break;
                    }

                case MilCommandKind.PushClip:
                    visitor.VisitPushClip(ReadDependent(payload, ref payloadOffset, dependents));
                    break;

                case MilCommandKind.PushOpacityMask:
                    {
                        SkipBytes(payload, ref payloadOffset, 4 * sizeof(float)); // boundingBoxCacheLocalSpace
                        ResourceHandle opacityMask = ReadDependent(payload, ref payloadOffset, dependents);
                        SkipBytes(payload, ref payloadOffset, sizeof(uint)); // QWORD pad

                        // Collapse the pair: the ops between are discarded; the pop
                        // delivers VisitMaskedRange with this mask and the raw range.
                        if (maskDepth == 0)
                        {
                            maskBrush = opacityMask;
                            maskStart = recordStart + 8 + payloadOffset;
                            visitor = MaskedRangeParserState.Discard;
                        }

                        maskDepth++;
                        break;
                    }

                case MilCommandKind.PushOpacity:
                    visitor.VisitPushOpacity(ReadDouble(payload, ref payloadOffset));
                    break;

                case MilCommandKind.PushOpacityAnimate:
                    {
                        double opacity = ReadDouble(payload, ref payloadOffset);
                        SkipBytes(payload, ref payloadOffset, sizeof(uint)); // hOpacityAnimations is ignored at visit
                        SkipBytes(payload, ref payloadOffset, sizeof(uint)); // QWORD pad
                        visitor.VisitPushOpacity(opacity);
                        break;
                    }

                case MilCommandKind.PushTransform:
                    visitor.VisitPushTransform(ReadDependent(payload, ref payloadOffset, dependents));
                    break;

                case MilCommandKind.PushGuidelineSet:
                    visitor.VisitPushGuidelineSet(ReadDependent(payload, ref payloadOffset, dependents));
                    break;

                case MilCommandKind.PushGuidelineY1:
                    visitor.VisitPushGuidelineY1(ReadDouble(payload, ref payloadOffset));
                    break;

                case MilCommandKind.PushGuidelineY2:
                    {
                        double leadingCoordinate = ReadDouble(payload, ref payloadOffset);
                        double offsetToDrivenCoordinate = ReadDouble(payload, ref payloadOffset);
                        visitor.VisitPushGuidelineY2(leadingCoordinate, offsetToDrivenCoordinate);
                        break;
                    }

                case MilCommandKind.PushEffect:
                    SkipBytes(payload, ref payloadOffset, 2 * sizeof(uint)); // hEffect, hEffectInput are ignored at visit
                    visitor.VisitPushEffect();
                    break;

                case MilCommandKind.Pop:
                    if (maskDepth > 0)
                    {
                        maskDepth--;
                        if (maskDepth == 0)
                        {
                            visitor = deliverTo;
                            ReadOnlyMemory<byte> range = buffer[maskStart..recordStart].ToArray();
                            ReadOnlyMemory<ResourceHandle> rangeDependents = dependents.ToArray();
                            deliverTo.VisitMaskedRange(maskBrush, range, rangeDependents);
                        }
                    }
                    else
                    {
                        visitor.VisitPop();
                    }

                    break;

                case MilCommandKind.Invalid:
                case MilCommandKind.TransportSyncFlush:
                case MilCommandKind.TransportDestroyResourcesOnChannel:
                case MilCommandKind.PartitionRegisterForNotifications:
                case MilCommandKind.ChannelRequestTier:
                case MilCommandKind.PartitionSetVBlankSyncMode:
                case MilCommandKind.PartitionNotifyPresent:
                case MilCommandKind.ChannelCreateResource:
                case MilCommandKind.ChannelDeleteResource:
                case MilCommandKind.ChannelDuplicateHandle:
                case MilCommandKind.D3DImage:
                case MilCommandKind.D3DImagePresent:
                case MilCommandKind.BitmapSource:
                case MilCommandKind.BitmapInvalidate:
                case MilCommandKind.DoubleResource:
                case MilCommandKind.ColorResource:
                case MilCommandKind.PointResource:
                case MilCommandKind.RectResource:
                case MilCommandKind.SizeResource:
                case MilCommandKind.MatrixResource:
                case MilCommandKind.Point3DResource:
                case MilCommandKind.Vector3DResource:
                case MilCommandKind.QuaternionResource:
                case MilCommandKind.MediaPlayer:
                case MilCommandKind.RenderData:
                case MilCommandKind.EtwEventResource:
                case MilCommandKind.VisualCreate:
                case MilCommandKind.VisualSetOffset:
                case MilCommandKind.VisualSetTransform:
                case MilCommandKind.VisualSetEffect:
                case MilCommandKind.VisualSetCacheMode:
                case MilCommandKind.VisualSetClip:
                case MilCommandKind.VisualSetAlpha:
                case MilCommandKind.VisualSetRenderOptions:
                case MilCommandKind.VisualSetContent:
                case MilCommandKind.VisualSetAlphaMask:
                case MilCommandKind.VisualRemoveAllChildren:
                case MilCommandKind.VisualRemoveChild:
                case MilCommandKind.VisualInsertChildAt:
                case MilCommandKind.VisualSetGuidelineCollection:
                case MilCommandKind.VisualSetScrollableAreaClip:
                case MilCommandKind.Viewport3DVisualSetCamera:
                case MilCommandKind.Viewport3DVisualSetViewport:
                case MilCommandKind.Viewport3DVisualSet3DChild:
                case MilCommandKind.Visual3DSetContent:
                case MilCommandKind.Visual3DSetTransform:
                case MilCommandKind.Visual3DRemoveAllChildren:
                case MilCommandKind.Visual3DRemoveChild:
                case MilCommandKind.Visual3DInsertChildAt:
                case MilCommandKind.HwndTargetCreate:
                case MilCommandKind.HwndTargetSuppressLayered:
                case MilCommandKind.TargetUpdateWindowSettings:
                case MilCommandKind.GenericTargetCreate:
                case MilCommandKind.TargetSetRoot:
                case MilCommandKind.TargetSetClearColor:
                case MilCommandKind.TargetInvalidate:
                case MilCommandKind.TargetSetFlags:
                case MilCommandKind.HwndTargetDpiChanged:
                case MilCommandKind.GlyphRunCreate:
                case MilCommandKind.DoubleBufferedBitmap:
                case MilCommandKind.DoubleBufferedBitmapCopyForward:
                case MilCommandKind.PartitionNotifyPolicyChangeForNonInteractiveMode:
                case MilCommandKind.DrawLineAnimate:
                case MilCommandKind.DrawRectangleAnimate:
                case MilCommandKind.DrawRoundedRectangleAnimate:
                case MilCommandKind.DrawEllipseAnimate:
                case MilCommandKind.DrawVideo:
                case MilCommandKind.DrawVideoAnimate:
                case MilCommandKind.AxisAngleRotation3D:
                case MilCommandKind.QuaternionRotation3D:
                case MilCommandKind.PerspectiveCamera:
                case MilCommandKind.OrthographicCamera:
                case MilCommandKind.MatrixCamera:
                case MilCommandKind.Model3DGroup:
                case MilCommandKind.AmbientLight:
                case MilCommandKind.DirectionalLight:
                case MilCommandKind.PointLight:
                case MilCommandKind.SpotLight:
                case MilCommandKind.GeometryModel3D:
                case MilCommandKind.MeshGeometry3D:
                case MilCommandKind.MaterialGroup:
                case MilCommandKind.DiffuseMaterial:
                case MilCommandKind.SpecularMaterial:
                case MilCommandKind.EmissiveMaterial:
                case MilCommandKind.Transform3DGroup:
                case MilCommandKind.TranslateTransform3D:
                case MilCommandKind.ScaleTransform3D:
                case MilCommandKind.RotateTransform3D:
                case MilCommandKind.MatrixTransform3D:
                case MilCommandKind.PixelShader:
                case MilCommandKind.ImplicitInputBrush:
                case MilCommandKind.BlurEffect:
                case MilCommandKind.DropShadowEffect:
                case MilCommandKind.ShaderEffect:
                case MilCommandKind.DrawingImage:
                case MilCommandKind.TransformGroup:
                case MilCommandKind.TranslateTransform:
                case MilCommandKind.ScaleTransform:
                case MilCommandKind.SkewTransform:
                case MilCommandKind.RotateTransform:
                case MilCommandKind.MatrixTransform:
                case MilCommandKind.LineGeometry:
                case MilCommandKind.RectangleGeometry:
                case MilCommandKind.EllipseGeometry:
                case MilCommandKind.GeometryGroup:
                case MilCommandKind.CombinedGeometry:
                case MilCommandKind.PathGeometry:
                case MilCommandKind.SolidColorBrush:
                case MilCommandKind.LinearGradientBrush:
                case MilCommandKind.RadialGradientBrush:
                case MilCommandKind.ImageBrush:
                case MilCommandKind.DrawingBrush:
                case MilCommandKind.VisualBrush:
                case MilCommandKind.BitmapCacheBrush:
                case MilCommandKind.DashStyle:
                case MilCommandKind.Pen:
                case MilCommandKind.GeometryDrawing:
                case MilCommandKind.GlyphRunDrawing:
                case MilCommandKind.ImageDrawing:
                case MilCommandKind.VideoDrawing:
                case MilCommandKind.DrawingGroup:
                case MilCommandKind.GuidelineSet:
                case MilCommandKind.BitmapCache:
                    throw new MilParseException($"Unknown RenderData MILCMD 0x{id:x}", recordStart);

                default:
                    throw new MilParseException($"RenderData MILCMD 0x{id:x} is not a valid kind", recordStart);
            }

            offset = recordStart + size;
        }
    }

    private static void ParseGlyphRunCreate(ReadOnlySpan<byte> buffer, ref int offset, IMilCommandVisitor visitor)
    {
        int recordStart = offset - 4; // type already consumed by the caller
        ResourceHandle handle = ReadHandle(buffer, ref offset);
        ulong fontPointer = ReadUInt64(buffer, ref offset);
        ushort flags = ReadUInt16(buffer, ref offset);
        SkipBytes(buffer, ref offset, sizeof(ushort)); // packing before Origin
        float originX = ReadFloat(buffer, ref offset);
        float originY = ReadFloat(buffer, ref offset);
        float emSize = ReadFloat(buffer, ref offset);
        SkipBytes(buffer, ref offset, 4 * sizeof(double)); // ManagedBounds
        ushort glyphCount = ReadUInt16(buffer, ref offset);
        SkipBytes(buffer, ref offset, sizeof(ushort)); // packing after GlyphCount
        SkipBytes(buffer, ref offset, sizeof(ushort)); // BidiLevel
        SkipBytes(buffer, ref offset, sizeof(ushort)); // packing after BidiLevel
        SkipBytes(buffer, ref offset, sizeof(ushort)); // DWriteTextMeasuringMethod
        SkipBytes(buffer, ref offset, GlyphRunHeaderSize - (offset - recordStart)); // trailing padding

        ReadOnlySpan<ushort> glyphs = MemoryMarshal.Cast<byte, ushort>(ReadBytes(buffer, ref offset, glyphCount * sizeof(ushort)));
        ReadOnlySpan<float> advances = MemoryMarshal.Cast<byte, float>(ReadBytes(buffer, ref offset, glyphCount * sizeof(float)));

        if ((flags & GlyphRunFlagHasOffsets) != 0)
        {
            SkipBytes(buffer, ref offset, glyphCount * 2 * sizeof(float));
        }

        var origin = new Point(originX, originY);
        visitor.VisitGlyphRunCreate(handle, new FontFaceToken(fontPointer), origin, emSize, glyphs, advances);
    }

    /// <summary>
    /// Reads the trailing gradient-stop blob of a gradient brush record: a byte-counted
    /// sequence of <c>MIL_GRADIENTSTOP</c> (double Position + MilColorF Color, 24 bytes each).
    /// </summary>
    private static GradientStop[] ParseGradientStops(ReadOnlySpan<byte> buffer, ref int offset, uint stopsSize)
    {
        if (stopsSize % 24 != 0)
        {
            throw new MilParseException($"Gradient stop blob size {stopsSize} is not a multiple of 24", offset);
        }

        int count = checked((int)(stopsSize / 24));
        var stops = new GradientStop[count];
        for (int i = 0; i < count; i++)
        {
            double position = ReadDouble(buffer, ref offset);
            ColorRgba color = ReadColorRgba(buffer, ref offset);
            stops[i] = new GradientStop(position, color);
        }

        return stops;
    }

    /// <summary>
    /// Returns the fixed body size (bytes after the 4-byte type) for channel commands whose
    /// generated struct is fixed-size, or <see langword="false"/> when the command is
    /// variable-length or has no generated struct.
    /// </summary>
    private static bool TryGetFixedBodySize(MilCommandKind kind, out int bodySize)
    {
        switch (kind)
        {
            case MilCommandKind.TransportDestroyResourcesOnChannel:
            case MilCommandKind.PartitionRegisterForNotifications:
            case MilCommandKind.ChannelRequestTier:
            case MilCommandKind.PartitionSetVBlankSyncMode:
            case MilCommandKind.Visual3DRemoveAllChildren:
            case MilCommandKind.PartitionNotifyPolicyChangeForNonInteractiveMode:
                bodySize = 4;
                return true;

            case MilCommandKind.PartitionNotifyPresent:
            case MilCommandKind.HwndTargetSuppressLayered:
            case MilCommandKind.TargetSetFlags:
            case MilCommandKind.EtwEventResource:
            case MilCommandKind.VisualSetEffect:
            case MilCommandKind.VisualSetCacheMode:
            case MilCommandKind.VisualSetAlphaMask:
            case MilCommandKind.Viewport3DVisualSetCamera:
            case MilCommandKind.Viewport3DVisualSet3DChild:
            case MilCommandKind.Visual3DSetContent:
            case MilCommandKind.Visual3DSetTransform:
            case MilCommandKind.Visual3DRemoveChild:
            case MilCommandKind.DrawingImage:
                bodySize = 8;
                return true;

            case MilCommandKind.ChannelDuplicateHandle:
            case MilCommandKind.D3DImagePresent:
            case MilCommandKind.DoubleBufferedBitmapCopyForward:
            case MilCommandKind.DoubleResource:
            case MilCommandKind.Visual3DInsertChildAt:
            case MilCommandKind.GlyphRunDrawing:
                bodySize = 12;
                return true;

            case MilCommandKind.Point3DResource:
            case MilCommandKind.Vector3DResource:
            case MilCommandKind.DoubleBufferedBitmap:
            case MilCommandKind.PixelShader:
            case MilCommandKind.GeometryDrawing:
                bodySize = 16;
                return true;

            case MilCommandKind.D3DImage:
            case MilCommandKind.ColorResource:
            case MilCommandKind.PointResource:
            case MilCommandKind.QuaternionResource:
            case MilCommandKind.SizeResource:
            case MilCommandKind.GeometryModel3D:
            case MilCommandKind.CombinedGeometry:
                bodySize = 20;
                return true;

            case MilCommandKind.BitmapInvalidate:
            case MilCommandKind.HwndTargetDpiChanged:
            case MilCommandKind.QuaternionRotation3D:
            case MilCommandKind.EmissiveMaterial:
            case MilCommandKind.ImplicitInputBrush:
            case MilCommandKind.BlurEffect:
            case MilCommandKind.BitmapCache:
                bodySize = 24;
                return true;

            case MilCommandKind.AmbientLight:
                bodySize = 28;
                return true;

            case MilCommandKind.VisualSetRenderOptions:
            case MilCommandKind.GenericTargetCreate:
            case MilCommandKind.BitmapCacheBrush:
            case MilCommandKind.SpecularMaterial:
            case MilCommandKind.AxisAngleRotation3D:
                bodySize = 32;
                return true;

            case MilCommandKind.RectResource:
            case MilCommandKind.Viewport3DVisualSetViewport:
                bodySize = 36;
                return true;

            case MilCommandKind.VisualSetScrollableAreaClip:
            case MilCommandKind.DiffuseMaterial:
            case MilCommandKind.TranslateTransform3D:
                bodySize = 40;
                return true;

            case MilCommandKind.DirectionalLight:
            case MilCommandKind.RotateTransform3D:
            case MilCommandKind.ImageDrawing:
            case MilCommandKind.VideoDrawing:
                bodySize = 44;
                return true;

            case MilCommandKind.MatrixResource:
            case MilCommandKind.SkewTransform:
                bodySize = 52;
                return true;

            case MilCommandKind.TargetUpdateWindowSettings:
            case MilCommandKind.MatrixTransform3D:
                bodySize = 68;
                return true;

            case MilCommandKind.ScaleTransform3D:
            case MilCommandKind.DropShadowEffect:
                bodySize = 76;
                return true;

            case MilCommandKind.HwndTargetCreate:
                bodySize = 88;
                return true;

            case MilCommandKind.PerspectiveCamera:
            case MilCommandKind.OrthographicCamera:
            case MilCommandKind.PointLight:
                bodySize = 92;
                return true;

            case MilCommandKind.SpotLight:
                bodySize = 132;
                return true;

            case MilCommandKind.MatrixCamera:
                bodySize = 136;
                return true;

            case MilCommandKind.ImageBrush:
            case MilCommandKind.DrawingBrush:
            case MilCommandKind.VisualBrush:
                bodySize = 144;
                return true;

            case MilCommandKind.Invalid:
            case MilCommandKind.TransportSyncFlush:
            case MilCommandKind.ChannelCreateResource:
            case MilCommandKind.ChannelDeleteResource:
            case MilCommandKind.BitmapSource:
            case MilCommandKind.MediaPlayer:
            case MilCommandKind.RenderData:
            case MilCommandKind.VisualCreate:
            case MilCommandKind.VisualSetOffset:
            case MilCommandKind.VisualSetTransform:
            case MilCommandKind.VisualSetClip:
            case MilCommandKind.VisualSetAlpha:
            case MilCommandKind.VisualSetContent:
            case MilCommandKind.VisualRemoveAllChildren:
            case MilCommandKind.VisualRemoveChild:
            case MilCommandKind.VisualInsertChildAt:
            case MilCommandKind.TargetSetRoot:
            case MilCommandKind.VisualSetGuidelineCollection:
            case MilCommandKind.TargetSetClearColor:
            case MilCommandKind.TargetInvalidate:
            case MilCommandKind.GlyphRunCreate:
            case MilCommandKind.DrawLine:
            case MilCommandKind.DrawLineAnimate:
            case MilCommandKind.DrawRectangle:
            case MilCommandKind.DrawRectangleAnimate:
            case MilCommandKind.DrawRoundedRectangle:
            case MilCommandKind.DrawRoundedRectangleAnimate:
            case MilCommandKind.DrawEllipse:
            case MilCommandKind.DrawEllipseAnimate:
            case MilCommandKind.DrawGeometry:
            case MilCommandKind.DrawImage:
            case MilCommandKind.DrawImageAnimate:
            case MilCommandKind.DrawGlyphRun:
            case MilCommandKind.DrawDrawing:
            case MilCommandKind.DrawVideo:
            case MilCommandKind.DrawVideoAnimate:
            case MilCommandKind.PushClip:
            case MilCommandKind.PushOpacityMask:
            case MilCommandKind.PushOpacity:
            case MilCommandKind.PushOpacityAnimate:
            case MilCommandKind.PushTransform:
            case MilCommandKind.PushGuidelineSet:
            case MilCommandKind.PushGuidelineY1:
            case MilCommandKind.PushGuidelineY2:
            case MilCommandKind.PushEffect:
            case MilCommandKind.Pop:
            case MilCommandKind.Model3DGroup:
            case MilCommandKind.MeshGeometry3D:
            case MilCommandKind.MaterialGroup:
            case MilCommandKind.Transform3DGroup:
            case MilCommandKind.ShaderEffect:
            case MilCommandKind.TransformGroup:
            case MilCommandKind.TranslateTransform:
            case MilCommandKind.ScaleTransform:
            case MilCommandKind.RotateTransform:
            case MilCommandKind.MatrixTransform:
            case MilCommandKind.LineGeometry:
            case MilCommandKind.RectangleGeometry:
            case MilCommandKind.EllipseGeometry:
            case MilCommandKind.GeometryGroup:
            case MilCommandKind.PathGeometry:
            case MilCommandKind.SolidColorBrush:
            case MilCommandKind.LinearGradientBrush:
            case MilCommandKind.RadialGradientBrush:
            case MilCommandKind.DashStyle:
            case MilCommandKind.Pen:
            case MilCommandKind.DrawingGroup:
            case MilCommandKind.GuidelineSet:
                goto default;

            default:
                bodySize = 0;
                return false;
        }
    }

    /// <summary>
    /// Skips a variable-length channel command whose trailing blob byte count is declared in
    /// its header. Returns the full record body (after the 4-byte type) via
    /// <paramref name="payload"/>.
    /// </summary>
    private static bool TrySkipVariableRecord(
        ReadOnlySpan<byte> buffer,
        int recordStart,
        MilCommandKind kind,
        out ReadOnlySpan<byte> payload)
    {
        int headerBytes;
        long declaredBytes;
        switch (kind)
        {
            case MilCommandKind.TransformGroup:
            case MilCommandKind.MaterialGroup:
            case MilCommandKind.Transform3DGroup:
                headerBytes = 8;
                declaredBytes = ReadUInt32At(buffer, recordStart, 8);
                break;

            case MilCommandKind.Model3DGroup:
                headerBytes = 12;
                declaredBytes = ReadUInt32At(buffer, recordStart, 12);
                break;

            case MilCommandKind.GeometryGroup:
            case MilCommandKind.PathGeometry:
                headerBytes = 16;
                declaredBytes = ReadUInt32At(buffer, recordStart, 16);
                break;

            case MilCommandKind.DrawingGroup:
                headerBytes = 48;
                declaredBytes = ReadUInt32At(buffer, recordStart, 16);
                break;

            case MilCommandKind.LinearGradientBrush:
                headerBytes = 80;
                declaredBytes = ReadUInt32At(buffer, recordStart, 72);
                break;

            case MilCommandKind.RadialGradientBrush:
                headerBytes = 104;
                declaredBytes = ReadUInt32At(buffer, recordStart, 88);
                break;

            case MilCommandKind.DashStyle:
                headerBytes = 20;
                declaredBytes = ReadUInt32At(buffer, recordStart, 20);
                break;

            case MilCommandKind.GuidelineSet:
                headerBytes = 16;
                declaredBytes = ReadUInt32At(buffer, recordStart, 8) + ReadUInt32At(buffer, recordStart, 12);
                break;

            case MilCommandKind.MeshGeometry3D:
                headerBytes = 20;
                declaredBytes = ReadUInt32At(buffer, recordStart, 8)
                                + ReadUInt32At(buffer, recordStart, 12)
                                + ReadUInt32At(buffer, recordStart, 16)
                                + ReadUInt32At(buffer, recordStart, 20);
                break;

            case MilCommandKind.ShaderEffect:
                headerBytes = 76;
                declaredBytes = ReadUInt32At(buffer, recordStart, 48)
                                + ReadUInt32At(buffer, recordStart, 52)
                                + ReadUInt32At(buffer, recordStart, 56)
                                + ReadUInt32At(buffer, recordStart, 60)
                                + ReadUInt32At(buffer, recordStart, 64)
                                + ReadUInt32At(buffer, recordStart, 68)
                                + ReadUInt32At(buffer, recordStart, 72)
                                + ReadUInt32At(buffer, recordStart, 76);
                break;

            case MilCommandKind.VisualSetGuidelineCollection:
                // MILCMD_VISUAL_SETGUIDELINECOLLECTION is 16 bytes (Type+Handle+countX@8+countY@12).
                // Extra payload is float[countX+countY] — not doubles (exports.cs AppendCommandData).
                headerBytes = 12;
                declaredBytes = (ReadUInt16At(buffer, recordStart, 8)
                                 + ReadUInt16At(buffer, recordStart, 12))
                                * (long)sizeof(float);
                break;

            case MilCommandKind.Invalid:
            case MilCommandKind.TransportSyncFlush:
            case MilCommandKind.TransportDestroyResourcesOnChannel:
            case MilCommandKind.PartitionRegisterForNotifications:
            case MilCommandKind.ChannelRequestTier:
            case MilCommandKind.PartitionSetVBlankSyncMode:
            case MilCommandKind.PartitionNotifyPresent:
            case MilCommandKind.ChannelCreateResource:
            case MilCommandKind.ChannelDeleteResource:
            case MilCommandKind.ChannelDuplicateHandle:
            case MilCommandKind.D3DImage:
            case MilCommandKind.D3DImagePresent:
            case MilCommandKind.BitmapSource:
            case MilCommandKind.BitmapInvalidate:
            case MilCommandKind.DoubleResource:
            case MilCommandKind.ColorResource:
            case MilCommandKind.PointResource:
            case MilCommandKind.RectResource:
            case MilCommandKind.SizeResource:
            case MilCommandKind.MatrixResource:
            case MilCommandKind.Point3DResource:
            case MilCommandKind.Vector3DResource:
            case MilCommandKind.QuaternionResource:
            case MilCommandKind.MediaPlayer:
            case MilCommandKind.RenderData:
            case MilCommandKind.EtwEventResource:
            case MilCommandKind.GlyphRunCreate:
            case MilCommandKind.VisualCreate:
            case MilCommandKind.VisualSetOffset:
            case MilCommandKind.VisualSetTransform:
            case MilCommandKind.VisualSetEffect:
            case MilCommandKind.VisualSetCacheMode:
            case MilCommandKind.VisualSetClip:
            case MilCommandKind.VisualSetAlpha:
            case MilCommandKind.VisualSetRenderOptions:
            case MilCommandKind.VisualSetContent:
            case MilCommandKind.VisualSetAlphaMask:
            case MilCommandKind.VisualRemoveAllChildren:
            case MilCommandKind.VisualRemoveChild:
            case MilCommandKind.VisualInsertChildAt:
            case MilCommandKind.VisualSetScrollableAreaClip:
            case MilCommandKind.Viewport3DVisualSetCamera:
            case MilCommandKind.Viewport3DVisualSetViewport:
            case MilCommandKind.Viewport3DVisualSet3DChild:
            case MilCommandKind.Visual3DSetContent:
            case MilCommandKind.Visual3DSetTransform:
            case MilCommandKind.Visual3DRemoveAllChildren:
            case MilCommandKind.Visual3DRemoveChild:
            case MilCommandKind.Visual3DInsertChildAt:
            case MilCommandKind.HwndTargetCreate:
            case MilCommandKind.HwndTargetSuppressLayered:
            case MilCommandKind.TargetUpdateWindowSettings:
            case MilCommandKind.GenericTargetCreate:
            case MilCommandKind.TargetSetRoot:
            case MilCommandKind.TargetSetClearColor:
            case MilCommandKind.TargetInvalidate:
            case MilCommandKind.TargetSetFlags:
            case MilCommandKind.HwndTargetDpiChanged:
            case MilCommandKind.DoubleBufferedBitmap:
            case MilCommandKind.DoubleBufferedBitmapCopyForward:
            case MilCommandKind.PartitionNotifyPolicyChangeForNonInteractiveMode:
            case MilCommandKind.DrawLine:
            case MilCommandKind.DrawLineAnimate:
            case MilCommandKind.DrawRectangle:
            case MilCommandKind.DrawRectangleAnimate:
            case MilCommandKind.DrawRoundedRectangle:
            case MilCommandKind.DrawRoundedRectangleAnimate:
            case MilCommandKind.DrawEllipse:
            case MilCommandKind.DrawEllipseAnimate:
            case MilCommandKind.DrawGeometry:
            case MilCommandKind.DrawImage:
            case MilCommandKind.DrawImageAnimate:
            case MilCommandKind.DrawGlyphRun:
            case MilCommandKind.DrawDrawing:
            case MilCommandKind.DrawVideo:
            case MilCommandKind.DrawVideoAnimate:
            case MilCommandKind.PushClip:
            case MilCommandKind.PushOpacityMask:
            case MilCommandKind.PushOpacity:
            case MilCommandKind.PushOpacityAnimate:
            case MilCommandKind.PushTransform:
            case MilCommandKind.PushGuidelineSet:
            case MilCommandKind.PushGuidelineY1:
            case MilCommandKind.PushGuidelineY2:
            case MilCommandKind.PushEffect:
            case MilCommandKind.Pop:
            case MilCommandKind.AxisAngleRotation3D:
            case MilCommandKind.QuaternionRotation3D:
            case MilCommandKind.PerspectiveCamera:
            case MilCommandKind.OrthographicCamera:
            case MilCommandKind.MatrixCamera:
            case MilCommandKind.AmbientLight:
            case MilCommandKind.DirectionalLight:
            case MilCommandKind.PointLight:
            case MilCommandKind.SpotLight:
            case MilCommandKind.GeometryModel3D:
            case MilCommandKind.DiffuseMaterial:
            case MilCommandKind.SpecularMaterial:
            case MilCommandKind.EmissiveMaterial:
            case MilCommandKind.TranslateTransform3D:
            case MilCommandKind.ScaleTransform3D:
            case MilCommandKind.RotateTransform3D:
            case MilCommandKind.MatrixTransform3D:
            case MilCommandKind.PixelShader:
            case MilCommandKind.ImplicitInputBrush:
            case MilCommandKind.BlurEffect:
            case MilCommandKind.DropShadowEffect:
            case MilCommandKind.DrawingImage:
            case MilCommandKind.TranslateTransform:
            case MilCommandKind.ScaleTransform:
            case MilCommandKind.SkewTransform:
            case MilCommandKind.RotateTransform:
            case MilCommandKind.MatrixTransform:
            case MilCommandKind.LineGeometry:
            case MilCommandKind.RectangleGeometry:
            case MilCommandKind.EllipseGeometry:
            case MilCommandKind.CombinedGeometry:
            case MilCommandKind.SolidColorBrush:
            case MilCommandKind.ImageBrush:
            case MilCommandKind.DrawingBrush:
            case MilCommandKind.VisualBrush:
            case MilCommandKind.BitmapCacheBrush:
            case MilCommandKind.Pen:
            case MilCommandKind.GeometryDrawing:
            case MilCommandKind.GlyphRunDrawing:
            case MilCommandKind.ImageDrawing:
            case MilCommandKind.VideoDrawing:
            case MilCommandKind.BitmapCache:
                goto default;

            default:
                payload = default;
                return false;
        }

        if (declaredBytes > buffer.Length - (recordStart + 4 + headerBytes))
        {
            throw new MilParseException("Truncated variable-size MILCMD record", recordStart);
        }

        int bodyBytes = headerBytes + (int)declaredBytes;
        payload = buffer.Slice(recordStart + 4, bodyBytes);
        return true;
    }

    private static ResourceHandle ReadDependent(
        ReadOnlySpan<byte> payload,
        ref int offset,
        ReadOnlySpan<ResourceHandle> dependents)
    {
        uint value = ReadUInt32(payload, ref offset);
        if (value == 0)
        {
            return ResourceHandle.Null;
        }

        // Injected tests pass a 1-based dependent table. WPF MarshalToDUCE
        // remaps those indices to channel handles before send.
        if (dependents.IsEmpty)
        {
            return new ResourceHandle(value);
        }

        EnsureDependentInRange(value, dependents.Length, offset - 4);
        return dependents[(int)value - 1];
    }

    private static void EnsureDependentInRange(uint index, int count, int offset)
    {
        if (index > (uint)count)
        {
            throw new MilParseException($"RenderData dependent index {index} out of range", offset);
        }
    }

    private static Point ReadPoint(ReadOnlySpan<byte> buffer, ref int offset)
    {
        double x = ReadDouble(buffer, ref offset);
        double y = ReadDouble(buffer, ref offset);
        return new Point(x, y);
    }

    private static Rect ReadRect(ReadOnlySpan<byte> buffer, ref int offset)
    {
        double x = ReadDouble(buffer, ref offset);
        double y = ReadDouble(buffer, ref offset);
        double width = ReadDouble(buffer, ref offset);
        double height = ReadDouble(buffer, ref offset);
        return new Rect(x, y, width, height);
    }

    private static Matrix3x2 ReadMatrix3x2(ReadOnlySpan<byte> buffer, ref int offset)
    {
        double m11 = ReadDouble(buffer, ref offset);
        double m12 = ReadDouble(buffer, ref offset);
        double m21 = ReadDouble(buffer, ref offset);
        double m22 = ReadDouble(buffer, ref offset);
        double dx = ReadDouble(buffer, ref offset);
        double dy = ReadDouble(buffer, ref offset);
        return new Matrix3x2(m11, m12, m21, m22, dx, dy);
    }

    private static ColorRgba ReadColorRgba(ReadOnlySpan<byte> buffer, ref int offset)
    {
        float r = ReadFloat(buffer, ref offset);
        float g = ReadFloat(buffer, ref offset);
        float b = ReadFloat(buffer, ref offset);
        float a = ReadFloat(buffer, ref offset);
        return new ColorRgba(r, g, b, a);
    }

    private static ResourceHandle ReadHandle(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return new ResourceHandle(ReadUInt32(buffer, ref offset));
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return ReadUnaligned<ushort>(buffer, ref offset);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return ReadUnaligned<uint>(buffer, ref offset);
    }

    private static int ReadInt32(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return ReadUnaligned<int>(buffer, ref offset);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return ReadUnaligned<ulong>(buffer, ref offset);
    }

    private static float ReadFloat(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return ReadUnaligned<float>(buffer, ref offset);
    }

    private static double ReadDouble(ReadOnlySpan<byte> buffer, ref int offset)
    {
        return ReadUnaligned<double>(buffer, ref offset);
    }

    private static T ReadUnaligned<T>(ReadOnlySpan<byte> buffer, ref int offset)
        where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        if (offset > buffer.Length - size)
        {
            throw new MilParseException($"Truncated MILCMD record (need {size} bytes)", offset);
        }

        T value = MemoryMarshal.Read<T>(buffer[offset..]);
        offset += size;
        return value;
    }

    private static ReadOnlySpan<byte> ReadBytes(ReadOnlySpan<byte> buffer, ref int offset, int count)
    {
        if (count < 0 || offset > buffer.Length - count)
        {
            throw new MilParseException($"Truncated MILCMD record (need {count} bytes)", offset);
        }

        ReadOnlySpan<byte> value = buffer.Slice(offset, count);
        offset += count;
        return value;
    }

    private static void SkipBytes(ReadOnlySpan<byte> buffer, ref int offset, int count)
    {
        if (count < 0 || offset > buffer.Length - count)
        {
            throw new MilParseException($"Truncated MILCMD record (need {count} bytes)", offset);
        }

        offset += count;
    }

    private static uint ReadUInt32At(ReadOnlySpan<byte> buffer, int recordStart, int relativeOffset)
    {
        int absolute = recordStart + relativeOffset;
        EnsureAvailable(buffer, absolute, sizeof(uint));
        return MemoryMarshal.Read<uint>(buffer[absolute..]);
    }

    private static ushort ReadUInt16At(ReadOnlySpan<byte> buffer, int recordStart, int relativeOffset)
    {
        int absolute = recordStart + relativeOffset;
        EnsureAvailable(buffer, absolute, sizeof(ushort));
        return MemoryMarshal.Read<ushort>(buffer[absolute..]);
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> buffer, int offset, int size)
    {
        if (offset > buffer.Length - size)
        {
            throw new MilParseException("Truncated MILCMD record", offset);
        }
    }
}

/// <summary>Discards every parsed op while the parser skips the interior of a
/// masked pair (the range is delivered wholesale via VisitMaskedRange).</summary>
internal sealed class DiscardingVisitor : MilCommandVisitor
{
}

/// <summary>Holds the parser's discard visitor instance.</summary>
internal static class MaskedRangeParserState
{
    internal static readonly DiscardingVisitor Discard = new();
}
