using System.Buffers;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.FreeType;
using Nova.Imaging;
using Nova.Geometry;
using Nova.Geometry2D;
using Nova.MilCmd;
using Nova.Text;
using Nova.Vulkan;

namespace Nova.Mil;

/// <summary>
/// Applies parsed MILCMD / RenderData visits to a resource table and visual tree,
/// then walks that graph onto an <see cref="IRasterCommandList"/>.
/// </summary>
[PublicAPI]
public sealed class SlaveGraph : MilCommandVisitor
{
    private const double FlattenTolerance = 0.25;

    // 4/3 * (sqrt(2) - 1): standard cubic-Bezier approximation constant for circular arcs.
    private const double BezierKappa = 0.5522847498307936;

    private static readonly Dictionary<MilResourceType, SlotKind> ResourceKinds = new()
    {
        [MilResourceType.Visual] = SlotKind.Visual,
        [MilResourceType.SolidColorBrush] = SlotKind.SolidColorBrush,
        [MilResourceType.LinearGradientBrush] = SlotKind.LinearGradientBrush,
        [MilResourceType.RadialGradientBrush] = SlotKind.RadialGradientBrush,
        [MilResourceType.VisualBrush] = SlotKind.VisualBrush,
        [MilResourceType.ImageBrush] = SlotKind.ImageBrush,
        [MilResourceType.TranslateTransform] = SlotKind.TranslateTransform,
        [MilResourceType.ScaleTransform] = SlotKind.ScaleTransform,
        [MilResourceType.RotateTransform] = SlotKind.RotateTransform,
        [MilResourceType.MatrixTransform] = SlotKind.MatrixTransform,
        [MilResourceType.LineGeometry] = SlotKind.LineGeometry,
        [MilResourceType.RectangleGeometry] = SlotKind.RectangleGeometry,
        [MilResourceType.EllipseGeometry] = SlotKind.EllipseGeometry,
        [MilResourceType.Pen] = SlotKind.Pen,
        [MilResourceType.GlyphRun] = SlotKind.GlyphRun,
        [MilResourceType.RenderData] = SlotKind.RenderData,
        [MilResourceType.BitmapSource] = SlotKind.BitmapSource,
        [MilResourceType.Drawing] = SlotKind.Drawing,
        [MilResourceType.DrawingGroup] = SlotKind.Drawing,
        [MilResourceType.BlurEffect] = SlotKind.BlurEffect,
        [MilResourceType.DropShadowEffect] = SlotKind.DropShadowEffect
    };

    private readonly Dictionary<uint, Slot> _resources = [];
    private readonly Dictionary<ulong, Typeface> _fonts = [];
    private uint _nextBorrowedFaceId = 1;
    private readonly Stack<PopKind> _popStack = new();
    private IRasterCommandList? _commands;
    private GlyphAtlas? _atlas;
    private bool _measuring;
    private Rect? _measureBounds;

    /// <summary>
    /// The presenter used to create gradient LUT textures and VisualBrush textures. Set by
    /// the host (or tests) before rasterizing content that uses those brushes.
    /// </summary>
    public IVulkanPresenter? Presenter { get; set; }

    /// <summary>
    /// Creates an offscreen presenter for VisualBrush intermediate rendering. Wired by the
    /// host to <c>VulkanDevice.CreateOffscreenPresenter</c>; required only when a
    /// VisualBrush is rendered.
    /// </summary>
    public Func<PixelSize, IVulkanPresenter>? OffscreenFactory { get; set; }

    // One channel set (the shared MediaContext channel) carries the commands of every
    // composition target on it. Targets are multiplexed by the target resource handle that
    // TargetSetRoot / TargetSetClearColor carry; each frame rasterizes its own target.
    private readonly Dictionary<uint, ResourceHandle> _targetRoots = [];
    private readonly Dictionary<uint, ColorRgba> _targetClearColors = [];
    private uint _activeTarget;

    /// <summary>The root visual of the most recently rasterized target (target 0 before any).</summary>
    public ResourceHandle Root => _targetRoots.TryGetValue(_activeTarget, out ResourceHandle root) ? root : ResourceHandle.Null;

    /// <summary>The clear color of the most recently rasterized target (transparent before any).</summary>
    public ColorRgba ClearColor => _targetClearColors.TryGetValue(_activeTarget, out ColorRgba color) ? color : ColorRgba.Transparent;

    /// <summary>
    /// Associates the 1-based dependent handles referenced inside a RenderData blob with the
    /// resource created by <see cref="VisitRenderData"/>. Must be called before the blob is
    /// replayed by <see cref="Rasterize(IRasterCommandList, GlyphAtlas?)"/>.
    /// </summary>
    public void SetRenderDataDependents(ResourceHandle renderData, ReadOnlySpan<ResourceHandle> dependents)
    {
        Slot slot = EnsureSlot(renderData);
        slot.Kind = SlotKind.RenderData;
        slot.Dependents = dependents.ToArray();
        slot.Version++;
    }

    /// <summary>
    /// Registers the typeface backing a <see cref="FontFaceToken"/> so <c>DrawGlyphRun</c>
    /// can rasterize glyphs into the atlas. Required before glyph runs render.
    /// </summary>
    public void RegisterFont(FontFaceToken token, Typeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        _fonts[token.Value] = typeface;
    }

    private Typeface? ResolveTypefaceFromNativeHandle(FontFaceToken token)
    {
        if (token.IsNull || !FontFace.TryGet((nint)token.Value, out FontFace? face))
        {
            return null;
        }

        var typeface = new Typeface(_nextBorrowedFaceId++, face) { OwnsFace = false };
        _fonts[token.Value] = typeface;
        return typeface;
    }

    /// <summary>Rasterizes the target-0 root; used by tests and single-target graphs.</summary>
    public void Rasterize(IRasterCommandList commands, GlyphAtlas? atlas)
    {
        Rasterize(commands, atlas, 0);
    }

    /// <summary>Rasterizes the root of the given composition target (the target resource handle from
    /// <c>TargetSetRoot</c>). Falls back to the target-0 root so content injected directly
    /// into a graph (tests) renders for window frames whose real target handle differs.
    /// </summary>
    public void Rasterize(IRasterCommandList commands, GlyphAtlas? atlas, uint targetHandle)
    {
        Rasterize(commands, atlas, targetHandle, Presenter);
    }

    /// <summary>
    /// Rasterizes the given target root using <paramref name="presenter"/> for any texture
    /// uploads this pass performs (effects, opacity masks, visual brushes). With multiple
    /// frames (a main window plus popup/tooltip windows) sharing ONE graph, the graph's
    /// shared <see cref="Presenter"/> field is the LAST-wired frame's presenter — a texture
    /// uploaded through it would be recorded into a different frame's command list and that
    /// frame's presenter would fail with an unknown handle. Each frame therefore passes its
    /// OWN presenter so uploads land in the table of the presenter that will render this
    /// pass. The single-frame/tests path (no presenter argument) keeps the field behavior.
    /// </summary>
    public void Rasterize(IRasterCommandList commands, GlyphAtlas? atlas, uint targetHandle, IVulkanPresenter? presenter)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ResourceHandle root = ResolveRoot(targetHandle);
        commands.Clear(ResolveClearColor(targetHandle));
        if (root.IsNull)
        {
            return;
        }

        IVulkanPresenter? previousPresenter = Presenter;
        Presenter = presenter ?? previousPresenter;
        _activeTarget = targetHandle;
        _commands = commands;
        _atlas = atlas;
        try
        {

            WalkVisual(root);
        }
        finally
        {
            Presenter = previousPresenter;
            _commands = null;
            _atlas = null;
            while (_popStack.Count > 0)
            {
                _ = _popStack.Pop();
            }
        }
    }

    private ResourceHandle ResolveRoot(uint targetHandle)
    {
        if (_targetRoots.TryGetValue(targetHandle, out ResourceHandle root) && !root.IsNull)
        {
            return root;
        }

        // The frame's TargetHandle is its GENERICRENDERTARGET handle, but the graph keys the
        // root by the content-root VISUAL handle (the value TargetSetRoot carried). If the
        // target handle has no direct entry, use the sole registered root — with one target
        // per graph they coincide.
        if (_targetRoots.Count == 1)
        {
            foreach (ResourceHandle candidate in _targetRoots.Values)
            {
                if (!candidate.IsNull)
                {
                    return candidate;
                }
            }
        }

        return _targetRoots.TryGetValue(0, out ResourceHandle legacy) ? legacy : ResourceHandle.Null;
    }

    private ColorRgba ResolveClearColor(uint targetHandle)
    {
        return _targetClearColors.TryGetValue(targetHandle, out ColorRgba color)
            ? color
            : _targetClearColors.TryGetValue(0, out ColorRgba legacy)
                ? legacy
                : ColorRgba.Transparent;
    }

    public bool HitTest(Point point, out ResourceHandle visual)
    {
        visual = ResourceHandle.Null;
        return !Root.IsNull && HitTestVisual(Root, point, Matrix3x2.Identity, out visual);
    }

    public override void VisitChannelCreateResource(ResourceHandle handle, MilResourceType type)
    {
        Slot slot = EnsureSlot(handle);
        if (ResourceKinds.TryGetValue(type, out SlotKind kind))
        {
            slot.Kind = kind;
        }
    }

    public override void VisitChannelDeleteResource(ResourceHandle handle)
    {
        if (_resources.Remove(handle.Value, out Slot? removed))
        {
            // Effect composites are cached per (presenter, visual): destroy every presenter's
            // copy, each on its owning presenter.
            DestroyCachedTextures(handle.Value);

            // The per-presenter texture caches (gradient LUT, VisualBrush) hold this slot's
            // textures on every presenter that painted it; the textures themselves are freed
            // when their owning presenter disposes, so only the cache entries are dropped.
            RemovePresenterCacheEntries(_gradientLuts, handle.Value);
            RemovePresenterCacheEntries(_visualTextures, handle.Value);

            // Bitmap textures are created on whatever presenter was active when the bitmap
            // was first drawn (a frame presenter or a transient offscreen effect target), so
            // destroy every presenter's copy, each on its owning presenter.
            DestroyBitmapTextures(handle.Value);

            removed.Bitmap?.Dispose();
        }
    }

    private static void RemovePresenterCacheEntries<T>(Dictionary<IVulkanPresenter, Dictionary<uint, T>> cache, uint slotHandle)
    {
        foreach (KeyValuePair<IVulkanPresenter, Dictionary<uint, T>> perPresenter in cache)
        {
            _ = perPresenter.Value.Remove(slotHandle);
        }
    }

    public override void VisitVisualCreate(ResourceHandle handle)
    {
        EnsureSlot(handle).Kind = SlotKind.Visual;
    }

    public override void VisitVisualSetOffset(ResourceHandle handle, double offsetX, double offsetY)
    {
        Slot slot = EnsureSlot(handle);
        slot.Offset = new Point(offsetX, offsetY);
        slot.Version++;
    }

    public override void VisitVisualSetTransform(ResourceHandle handle, ResourceHandle transform)
    {
        Slot slot = EnsureSlot(handle);
        slot.Transform = transform;
        slot.Version++;
    }

    public override void VisitVisualSetClip(ResourceHandle handle, ResourceHandle clip)
    {
        Slot slot = EnsureSlot(handle);
        slot.Clip = clip;
        slot.Version++;
    }

    public override void VisitVisualSetAlpha(ResourceHandle handle, double alpha)
    {
        Slot slot = EnsureSlot(handle);
        slot.Alpha = alpha;
        slot.Version++;
    }

    public override void VisitVisualSetContent(ResourceHandle handle, ResourceHandle content)
    {
        Slot slot = EnsureSlot(handle);
        slot.Content = content;
        slot.Version++;
        slot.ContentVersion++;
    }

    public override void VisitVisualSetEffect(ResourceHandle handle, ResourceHandle effect)
    {
        Slot slot = EnsureSlot(handle);
        slot.Effect = effect;
        slot.Version++;
        slot.ContentVersion++;
    }

    public override void VisitVisualSetAlphaMask(ResourceHandle handle, ResourceHandle opacityMask)
    {
        Slot slot = EnsureSlot(handle);
        slot.OpacityMask = opacityMask;
        slot.Version++;
        slot.ContentVersion++;
    }

    public override void VisitVisualRemoveAllChildren(ResourceHandle handle)
    {
        Slot slot = EnsureSlot(handle);
        slot.Children.Clear();
        slot.Version++;
        slot.ContentVersion++;
    }

    public override void VisitVisualRemoveChild(ResourceHandle handle, ResourceHandle child)
    {
        Slot slot = EnsureSlot(handle);
        _ = slot.Children.Remove(child);
        slot.Version++;
        slot.ContentVersion++;
    }

    public override void VisitVisualInsertChildAt(ResourceHandle handle, ResourceHandle child, uint index)
    {
        List<ResourceHandle> children = EnsureSlot(handle).Children;
        children.Insert((int)Math.Min(index, (uint)children.Count), child);
        Slot slot = EnsureSlot(handle);
        slot.Version++;
        slot.ContentVersion++;
    }

    public override void VisitTargetSetRoot(ResourceHandle handle, ResourceHandle root)
    {
        // Milcore: the target's root is the composition-target's content-root VISUAL, whose
        // handle equals the target's handle (same value duplicated from the out-of-band
        // channel). Each target's subtree hangs off its own content root, so key the root by
        // that handle; a later target keeps its own tree.
        _targetRoots[handle.Value] = root;
    }

    public override void VisitTargetSetClearColor(ResourceHandle handle, ColorRgba color)
    {
        // Colors arrive as scRGB (linear) floats on the MIL wire. The raster targets are
        // UNORM images with no automatic decode, so the value stored must be sRGB-encoded
        // to display correctly — same conversion the gradient LUT bake applies per stop.
        _targetClearColors[handle.Value] = SrgbEncode(color);
    }

    public override void VisitTargetInvalidate(ResourceHandle handle, Rect dirty)
    {
    }

    public override void VisitRenderData(ResourceHandle handle, ReadOnlySpan<byte> renderData)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.RenderData;
        slot.Blob = renderData.ToArray();
        slot.Version++;
    }

    public override void VisitSolidColorBrush(ResourceHandle handle, double opacity, ColorRgba color, ResourceHandle transform)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.SolidColorBrush;
        slot.Opacity = opacity;
        slot.Color = color;
        slot.Transform = transform;
        slot.Version++;
    }

    public override void VisitLinearGradientBrush(
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
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.LinearGradientBrush;
        slot.Opacity = opacity;
        slot.Start = startPoint;
        slot.End = endPoint;
        slot.MappingMode = mappingMode;
        slot.SpreadMethod = spreadMethod;
        slot.Stops = stops.ToArray();
        slot.Transform = transform;
        slot.RelativeTransform = relativeTransform;
        slot.Version++;
    }

    public override void VisitRadialGradientBrush(
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
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.RadialGradientBrush;
        slot.Opacity = opacity;
        slot.Center = center;
        slot.RadiusX = radiusX;
        slot.RadiusY = radiusY;
        slot.GradientOrigin = gradientOrigin;
        slot.MappingMode = mappingMode;
        slot.SpreadMethod = spreadMethod;
        slot.Stops = stops.ToArray();
        slot.Transform = transform;
        slot.RelativeTransform = relativeTransform;
        slot.Version++;
    }

    public override void VisitVisualBrush(
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
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.VisualBrush;
        slot.Opacity = opacity;
        slot.Viewport = viewport;
        slot.Viewbox = viewbox;
        slot.ViewportUnits = viewportUnits;
        slot.ViewboxUnits = viewboxUnits;
        slot.Stretch = stretch;
        slot.TileMode = tileMode;
        slot.Visual = visual;
        slot.Transform = transform;
        slot.Version++;
    }

    public override void VisitDropShadowEffect(
        ResourceHandle handle,
        double shadowDepth,
        ColorRgba color,
        double direction,
        double opacity,
        double blurRadius,
        int renderingBias)
    {
        // The wire color is scRGB (MilColorF via ColorToMilColorF); store sRGB so the
        // shadow composite uses the WPF-visible color (matches the gradient-LUT path).
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.DropShadowEffect;
        slot.EffectShadowDepth = shadowDepth;
        slot.EffectColor = new ColorRgba(ScRgbToSrgb(color.R), ScRgbToSrgb(color.G), ScRgbToSrgb(color.B), color.A);
        slot.EffectDirection = direction;
        slot.EffectOpacity = opacity;
        slot.EffectBlurRadius = blurRadius;
        slot.EffectRenderingBias = renderingBias;
        slot.Version++;
    }

    public override void VisitBlurEffect(ResourceHandle handle, double radius, int kernelType, int renderingBias)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.BlurEffect;
        slot.EffectRadius = radius;
        slot.EffectKernelType = kernelType;
        slot.EffectRenderingBias = renderingBias;
        slot.Version++;
    }

    public override void VisitImageBrush(
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
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.ImageBrush;
        slot.Opacity = opacity;
        slot.Viewport = viewport;
        slot.Viewbox = viewbox;
        slot.ViewportUnits = viewportUnits;
        slot.ViewboxUnits = viewboxUnits;
        slot.Stretch = stretch;
        slot.TileMode = tileMode;
        slot.ImageSource = imageSource;
        slot.Transform = transform;
    }

    public override void VisitTranslateTransform(ResourceHandle handle, double x, double y)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.TranslateTransform;
        slot.X = x;
        slot.Y = y;
        slot.Version++;
    }

    public override void VisitScaleTransform(ResourceHandle handle, double scaleX, double scaleY, double centerX, double centerY)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.ScaleTransform;
        slot.ScaleX = scaleX;
        slot.ScaleY = scaleY;
        slot.CenterX = centerX;
        slot.CenterY = centerY;
        slot.Version++;
    }

    public override void VisitRotateTransform(ResourceHandle handle, double angle, double centerX, double centerY)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.RotateTransform;
        slot.Angle = angle;
        slot.CenterX = centerX;
        slot.CenterY = centerY;
        slot.Version++;
    }

    public override void VisitMatrixTransform(ResourceHandle handle, Matrix3x2 matrix)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.MatrixTransform;
        slot.Matrix = matrix;
        slot.Version++;
    }

    public override void VisitTransformGroup(ResourceHandle handle, ReadOnlySpan<ResourceHandle> children)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.TransformGroup;
        slot.TransformChildren = children.ToArray();
        slot.Version++;
    }

    public override void VisitPathGeometry(ResourceHandle handle, ResourceHandle transform, ReadOnlySpan<byte> pathData)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.PathGeometry;
        slot.Transform = transform;
        slot.PathData = pathData.ToArray();
        slot.Version++;
    }

    public override void VisitRectangleGeometry(ResourceHandle handle, Rect rectangle, double radiusX, double radiusY, ResourceHandle transform)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.RectangleGeometry;
        slot.Rectangle = rectangle;
        slot.RadiusX = radiusX;
        slot.RadiusY = radiusY;
        slot.Transform = transform;
        slot.Version++;
    }

    public override void VisitLineGeometry(ResourceHandle handle, Point start, Point endPoint, ResourceHandle transform)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.LineGeometry;
        slot.Start = start;
        slot.End = endPoint;
        slot.Transform = transform;
        slot.Version++;
    }

    public override void VisitEllipseGeometry(ResourceHandle handle, Point center, double radiusX, double radiusY, ResourceHandle transform)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.EllipseGeometry;
        slot.Center = center;
        slot.RadiusX = radiusX;
        slot.RadiusY = radiusY;
        slot.Transform = transform;
        slot.Version++;
    }

    public override void VisitPen(ResourceHandle handle, ResourceHandle brush, double thickness, double miterLimit)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.Pen;
        slot.Brush = brush;
        slot.Thickness = thickness;
        _ = miterLimit;
        slot.Version++;
    }

    public override void VisitGlyphRunCreate(
        ResourceHandle handle,
        FontFaceToken font,
        Point origin,
        float emSize,
        ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<float> advances)
    {
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.GlyphRun;
        slot.Font = font;
        slot.Origin = origin;
        slot.EmSize = emSize;
        slot.Glyphs = glyphs.ToArray();
        slot.Advances = advances.ToArray();
        slot.Version++;
    }

    public override void VisitDrawLine(Point start, Point endPoint, ResourceHandle pen)
    {
        if (_measuring)
        {
            UnionMeasure(new Rect(
                Math.Min(start.X, endPoint.X),
                Math.Min(start.Y, endPoint.Y),
                Math.Abs(endPoint.X - start.X),
                Math.Abs(endPoint.Y - start.Y)));
            return;
        }

        if (_commands is null || pen.IsNull || !TryResolvePen(pen, out ColorRgba color, out double thickness))
        {
            return;
        }

        StrokeLine(_commands, start, endPoint, thickness, color);
    }

    public override void VisitDrawRectangle(Rect rectangle, ResourceHandle brush, ResourceHandle pen)
    {
        if (_measuring)
        {
            UnionMeasure(rectangle);
            return;
        }

        if (_commands is null)
        {
            return;
        }

        IRasterCommandList commands = _commands;
        if (TryResolveBrush(brush, out BrushPaint paint) && !rectangle.IsEmpty)
        {
            FillRectangle(commands, rectangle, paint);
        }

        if (!pen.IsNull && TryResolvePen(pen, out ColorRgba penColor, out double thickness))
        {
            StrokeAxisAlignedRect(commands, rectangle, thickness, penColor);
        }
    }

    public override void VisitDrawRoundedRectangle(Rect rectangle, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen)
    {
        if (_measuring)
        {
            UnionMeasure(rectangle);
            return;
        }

        if (_commands is null)
        {
            return;
        }

        IRasterCommandList commands = _commands;
        if (TryResolveBrush(brush, out BrushPaint paint))
        {
            FillRoundedRectangle(commands, rectangle, radiusX, radiusY, paint);
        }

        if (!pen.IsNull && TryResolvePen(pen, out ColorRgba penColor, out double thickness))
        {
            StrokeRoundedRectangle(commands, rectangle, radiusX, radiusY, thickness, penColor);
        }
    }

    public override void VisitDrawEllipse(Point center, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen)
    {
        if (_measuring)
        {
            UnionMeasure(new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2));
            return;
        }

        if (_commands is null)
        {
            return;
        }

        IRasterCommandList commands = _commands;
        if (TryResolveBrush(brush, out BrushPaint paint))
        {
            FillEllipseGeometry(commands, center, radiusX, radiusY, paint);
        }

        if (!pen.IsNull && TryResolvePen(pen, out ColorRgba penColor, out double thickness))
        {
            StrokeEllipse(commands, center, radiusX, radiusY, thickness, penColor);
        }
    }

    public override void VisitDrawGeometry(ResourceHandle brush, ResourceHandle pen, ResourceHandle geometry)
    {
        if (_measuring)
        {
            UnionMeasureGeometry(geometry);
            return;
        }

        if (_commands is null || !_resources.TryGetValue(geometry.Value, out Slot? slot))
        {
            return;
        }

        // A shape with only a Stroke (Fill = null) still draws the pen. Resolve the brush
        // optionally: absent brush + absent pen is a no-op, but absent brush + pen must
        // still stroke the geometry outline.
        bool hasBrush = TryResolveBrush(brush, out BrushPaint paint);
        if (!hasBrush && pen.IsNull)
        {
            return;
        }

        DrawGeometryContent(_commands, slot, hasBrush ? paint : default, pen, hasBrush);
    }

    public override void VisitDrawImage(Rect rectangle, ResourceHandle imageSource)
    {
        if (_measuring)
        {
            UnionMeasure(rectangle);
            return;
        }

        Slot? slot = null;
        if (_commands is not null)
        {
            _ = _resources.TryGetValue(imageSource.Value, out slot);
        }

        if (_commands is null || slot is null || slot.Kind != SlotKind.BitmapSource || slot.Bitmap is null)
        {
            return;
        }

        // The Image control computes the destination rectangle (Stretch applied in layout),
        // so draw the whole bitmap into `rectangle` with full-texture UVs.
        TextureHandle texture = EnsureBitmapTexture(imageSource.Value, slot);
        if (!texture.IsValid)
        {
            return;
        }

        ColorRgba tint = WithOpacity(ColorRgba.White, slot.Opacity);
        _commands.DrawTexturedQuad(
            new Point(rectangle.X, rectangle.Y),
            new Point(rectangle.Right, rectangle.Y),
            new Point(rectangle.Right, rectangle.Bottom),
            new Point(rectangle.X, rectangle.Bottom),
            texture,
            new Point(0, 0),
            new Point(1, 0),
            new Point(1, 1),
            new Point(0, 1),
            tint);
    }

    /// <summary>
    /// Stores the decoded bitmap pixels for a BitmapSource resource slot (called from the DUCE
    /// transport's <c>SendCommandBitmapSource</c>). Ownership of the bitmap transfers to the
    /// slot: a replaced or released slot disposes it.
    /// </summary>
    public void SetBitmapSourcePixels(ResourceHandle handle, Nova.Imaging.ManagedWicBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        Slot slot = EnsureSlot(handle);
        slot.Kind = SlotKind.BitmapSource;
        slot.Bitmap?.Dispose();
        slot.Bitmap = bitmap;
        DestroyBitmapTextures(handle.Value);
        slot.Version++;
    }

    public override void VisitDrawDrawing(ResourceHandle drawing)
    {
        if (_measuring || _commands is null)
        {
            return;
        }

        // Nested drawing replay is not wired yet. A missing or non-drawing slot
        // degrades to no pixels; the placeholder never throws for DrawDrawing.
        if (drawing.IsNull ||
            !_resources.TryGetValue(drawing.Value, out Slot? slot) ||
            slot.Kind != SlotKind.Drawing)
        {
            return;
        }
    }

    public override void VisitDrawGlyphRun(ResourceHandle foreground, ResourceHandle glyphRun)
    {
        if (_measuring || _commands is null || _atlas is null)
        {
            return;
        }

        if (!TryResolveSolidColor(foreground, out ColorRgba tint) ||
            !_resources.TryGetValue(glyphRun.Value, out Slot? run) ||
            run.Kind != SlotKind.GlyphRun)
        {
            return;
        }

        if (!_fonts.TryGetValue(run.Font.Value, out Typeface? typeface))
        {
            typeface = ResolveTypefaceFromNativeHandle(run.Font);
            if (typeface is null)
            {
                return;
            }
        }

        GlyphAtlas atlas = _atlas;
        IRasterCommandList commands = _commands;
        double x = run.Origin.X;
        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            GlyphQuad quad = atlas.GetOrAdd(typeface.FaceId, typeface.Face, run.Glyphs[i], run.EmSize);
            // Snap the quad to the device pixel grid. Glyphs are rasterized once at an
            // integer pixel size, and the bilinear sampler softens their edges whenever
            // the quad lands on fractional coordinates — the source of the blurred text
            // (every Linux text rasterizer either snaps like this or re-rasterizes per
            // subpixel position). The advance accumulation stays fractional, so
            // inter-glyph spacing keeps its ideal average and each glyph's placement
            // error is at most half a pixel.
            double px = Math.Round(x + quad.BearingX, MidpointRounding.AwayFromZero);
            double py = Math.Round(run.Origin.Y - quad.BearingY, MidpointRounding.AwayFromZero);
            var p0 = new Point(px, py);
            var p1 = new Point(p0.X + quad.Size.Width, p0.Y);
            var p2 = new Point(p0.X + quad.Size.Width, p0.Y + quad.Size.Height);
            var p3 = new Point(p0.X, p0.Y + quad.Size.Height);
            commands.DrawTexturedQuad(
                p0,
                p1,
                p2,
                p3,
                quad.Texture,
                UvTopLeft(quad.Uv),
                UvTopRight(quad.Uv),
                UvBottomRight(quad.Uv),
                UvBottomLeft(quad.Uv),
                tint);
            x += i < run.Advances.Length ? run.Advances[i] : 0;
        }
    }

    public override void VisitPushClip(ResourceHandle clip)
    {
        if (_measuring || _commands is null)
        {
            return;
        }

        if (ResolveClipRect(clip) is not { } clipRect)
        {
            return;
        }

        _popStack.Push(PopKind.Clip);
        _commands.PushClip(clipRect);
    }

    public override void VisitPushOpacityMask(ResourceHandle opacityMask)
    {
        // The parser collapses PushOpacityMask/Pop pairs into VisitMaskedRange, so this
        // op is never delivered by the live transport; it remains a no-op for the
        // visitor contract (kept balanced by the pop-stack machinery elsewhere).
        _ = opacityMask;
    }

    public override void VisitMaskedRange(ResourceHandle mask, ReadOnlyMemory<byte> renderData, ReadOnlyMemory<ResourceHandle> dependents)
    {
        if (_measuring)
        {
            // The measure pass unions the range's op extents (nested masked ranges
            // recurse through this branch).
            MilCommandParser.ParseRenderData(renderData.Span, dependents.Span, this);
            return;
        }

        if (_commands is null || Presenter is null || OffscreenFactory is null)
        {
            return;
        }

        // The masked region's bounds = the union of the range's op extents: re-walk the
        // range in measure mode against a fresh measure accumulator.
        bool savedMeasuring = _measuring;
        Rect? savedBounds = _measureBounds;
        _measureBounds = null;
        _measuring = true;
        try
        {
            MilCommandParser.ParseRenderData(renderData.Span, dependents.Span, this);
        }
        finally
        {
            _measuring = savedMeasuring;
        }

        Rect bounds = _measureBounds ?? Rect.Empty;
        _measureBounds = savedBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        PixelSize size = TargetSize(bounds, 0);
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        ReadOnlyMemory<byte> content = RenderMaskedRangeToTexture(renderData, dependents, bounds, size);
        ReadOnlyMemory<byte> maskAlpha = ComputeMaskAlpha(mask, bounds, size);
        if (content.Length == 0 || maskAlpha.Length == 0)
        {
            // Mask brush kind we cannot sample: degrade to the plain range so the
            // content still renders rather than disappearing.
            MilCommandParser.ParseRenderData(renderData.Span, dependents.Span, this);
            return;
        }

        byte[] masked = ApplyMaskAlpha(content.Span, maskAlpha.Span, size.Width, size.Height);
        TextureHandle texture = UploadCachedTexture(size, masked);
        DrawTextureQuad(_commands, texture, bounds.Left, bounds.Top, size.Width, size.Height);
    }

    /// <summary>
    /// Renders a masked render-data range into an offscreen presenter sized to
    /// <paramref name="size"/> and returns its premultiplied RGBA readback. The range's
    /// coordinates are translated so <paramref name="bounds"/>.TopLeft lands at the target
    /// origin. Glyph runs inside the range upload into an atlas owned by the offscreen
    /// presenter (the frame's atlas textures are unknown to it).
    /// </summary>
    private ReadOnlyMemory<byte> RenderMaskedRangeToTexture(
        ReadOnlyMemory<byte> renderData,
        ReadOnlyMemory<ResourceHandle> dependents,
        Rect bounds,
        PixelSize size)
    {
        IVulkanPresenter offscreen = OffscreenFactory!(size);
        using (offscreen)
        {
            using var atlas = new GlyphAtlas(offscreen, new PixelSize(512, 512));
            IRasterCommandList? savedCommands = _commands;
            GlyphAtlas? savedAtlas = _atlas;
            IVulkanPresenter? savedPresenter = Presenter;
            int savedDepth = _popStack.Count;
            _atlas = atlas;
            Presenter = offscreen;
            try
            {
                offscreen.Render(commands =>
                {
                    _commands = commands;
                    commands.Clear(ColorRgba.Transparent);
                    commands.PushTransform(Matrix3x2.Translate(-bounds.Left, -bounds.Top));
                    MilCommandParser.ParseRenderData(renderData.Span, dependents.Span, this);
                    commands.PopTransform();
                });
            }
            finally
            {
                while (_popStack.Count > savedDepth)
                {
                    _ = _popStack.Pop();
                }

                _commands = savedCommands;
                _atlas = savedAtlas;
                Presenter = savedPresenter;
            }

            ReadOnlyMemory<byte> pixels = offscreen.ReadbackRgba();

            // Everything the nested walk cached on the offscreen presenter dies with it.
            ForgetPresenter(offscreen);
            return pixels;
        }
    }

    public override void VisitPushOpacity(double opacity)
    {
        if (_measuring || _commands is null)
        {
            return;
        }

        _popStack.Push(PopKind.Opacity);
        _commands.PushOpacity(opacity);
    }

    public override void VisitPushTransform(ResourceHandle transform)
    {
        if (_measuring || _commands is null)
        {
            return;
        }

        _popStack.Push(PopKind.Transform);
        _commands.PushTransform(ResolveTransform(transform));
    }

    public override void VisitPushGuidelineSet(ResourceHandle guidelines)
    {
        _ = guidelines;
        if (_measuring || _commands is null)
        {
            return;
        }

        _popStack.Push(PopKind.Guideline);
    }

    public override void VisitPushGuidelineY1(double coordinate)
    {
        _ = coordinate;
        if (_measuring || _commands is null)
        {
            return;
        }

        _popStack.Push(PopKind.Guideline);
    }

    public override void VisitPushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
    {
        _ = leadingCoordinate;
        _ = offsetToDrivenCoordinate;
        if (_measuring || _commands is null)
        {
            return;
        }

        _popStack.Push(PopKind.Guideline);
    }

    public override void VisitPushEffect()
    {
        if (_measuring || _commands is null)
        {
            return;
        }

        // Effect rasterization is not wired yet; keep a no-op stack kind so Pop stays balanced.
        _popStack.Push(PopKind.Effect);
    }

    public override void VisitPop()
    {
        if (_measuring || _commands is null || _popStack.Count == 0)
        {
            return;
        }

        switch (_popStack.Pop())
        {
            case PopKind.Clip:
                _commands.PopClip();
                break;
            case PopKind.Opacity:
                _commands.PopOpacity();
                break;
            case PopKind.Transform:
                _commands.PopTransform();
                break;
            case PopKind.Guideline:
            case PopKind.OpacityMask:
            case PopKind.Effect:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_popStack), "Unknown pop kind.");
        }
    }

    private void WalkVisual(ResourceHandle handle)
    {
        if (!_resources.TryGetValue(handle.Value, out Slot? visual) || visual.Kind != SlotKind.Visual)
        {
            return;
        }

        IRasterCommandList commands = _commands!;
        commands.PushTransform(ResolveTransform(visual.Transform));
        commands.PushTransform(Matrix3x2.Translate(visual.Offset.X, visual.Offset.Y));
        commands.PushOpacity(visual.Alpha);
        Rect? clip = ResolveClipRect(visual.Clip);
        if (clip is { } clipRect)
        {
            commands.PushClip(clipRect);
        }

        DrawVisualLocalContent(handle, visual);

        if (clip is not null)
        {
            commands.PopClip();
        }

        commands.PopOpacity();
        commands.PopTransform();
        commands.PopTransform();
    }

    /// <summary>
    /// Renders a visual's own content and children, applying its effect and opacity mask when
    /// present. Without either, this is the plain subtree walk.
    /// </summary>
    private void DrawVisualLocalContent(ResourceHandle handle, Slot visual)
    {
        if (visual.Effect.IsNull && visual.OpacityMask.IsNull)
        {
            if (!visual.Content.IsNull)
            {
                RenderContent(visual.Content);
            }

            foreach (ResourceHandle child in visual.Children)
            {
                WalkVisual(child);
            }

            return;
        }

        DrawEffectedLocalContent(handle, visual);
    }

    /// <summary>
    /// Renders the visual's subtree (content + children) into an offscreen target, applies the
    /// effect / opacity mask on CPU, and composites the result back into the main command list
    /// as textured quads. Reuses the VisualBrush render-to-texture machinery (offscreen
    /// presenter + readback + texture upload); the offscreen presenter resolves its MSAA color
    /// attachment into the single-sample readback target, so the sampled pixels are resolved
    /// before any CPU filtering.
    /// </summary>
    private void DrawEffectedLocalContent(ResourceHandle handle, Slot visual)
    {
        if (Presenter is null || OffscreenFactory is null || _commands is null)
        {
            return;
        }

        // The effect applies to the visual's own subtree in local space; the visual's
        // transform/offset/alpha/clip are already on the command stack and composite the quads.
        Rect? content = MeasureVisualBounds(handle);
        if (content is not { } bounds || bounds.IsEmpty)
        {
            return;
        }

        if (!visual.Effect.IsNull && _resources.TryGetValue(visual.Effect.Value, out Slot? effect))
        {
            if (effect.Kind == SlotKind.DropShadowEffect)
            {
                DrawDropShadow(handle.Value, visual, effect, bounds);
                return;
            }

            if (effect.Kind == SlotKind.BlurEffect)
            {
                DrawBlur(handle.Value, visual, effect, bounds);
                return;
            }

            // ShaderEffect and any other effect kind are not implemented (ShaderEffect carries
            // precompiled ps_2_0/ps_3_0 bytecode that is out of scope). Degrade to the plain
            // subtree so the content still renders; the effect itself is dropped, never faked.
        }

        if (!visual.OpacityMask.IsNull)
        {
            DrawOpacityMask(handle.Value, visual, bounds);
            return;
        }

        // Effect present but unimplemented and no mask: fall back to the plain subtree walk.
        if (!visual.Content.IsNull)
        {
            RenderContent(visual.Content);
        }

        foreach (ResourceHandle child in visual.Children)
        {
            WalkVisual(child);
        }
    }

    /// <summary>
    /// DropShadowEffect: renders the subtree, blurs its alpha silhouette, tints it with the
    /// shadow color/opacity, draws it offset by (Depth, Direction) under the crisp content.
    /// </summary>
    private void DrawDropShadow(uint handle, Slot visual, Slot effect, Rect bounds)
    {
        double depth = Math.Max(0, effect.EffectShadowDepth);
        double directionRad = effect.EffectDirection * (Math.PI / 180.0);
        double offsetX = depth * Math.Cos(directionRad);
        // WPF's Direction is a math angle (counterclockwise, 90 = up); the raster's Y axis
        // points down, so the screen offset negates the sine: 270 (the Fluent default)
        // casts the shadow DOWN, 90 casts it up.
        double offsetY = -depth * Math.Sin(directionRad);
        ColorRgba color = effect.EffectColor;
        double opacity = Math.Clamp(effect.EffectOpacity, 0.0, 1.0);
        (float[] kernel, int radius) = GaussianKernel(effect.EffectBlurRadius);

        int pad = radius;
        PixelSize size = TargetSize(bounds, pad);
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        double x0 = bounds.Left - pad;
        double y0 = bounds.Top - pad;
        IRasterCommandList commands = _commands!;
        ulong key = ComputeEffectVersion(visual, effect);
        if (TryGetEffectCache(handle, EffectCacheKindDropShadow, key, bounds, size, out EffectCacheEntry cache))
        {
            // Warm: the composite pixels are unchanged; only the quads' positions derive from
            // the current bounds/offset, so an idle window costs two textured draws.
            DrawTextureQuad(commands, cache.TextureB, x0 + offsetX, y0 + offsetY, size.Width, size.Height);
            DrawTextureQuad(commands, cache.TextureA, x0, y0, size.Width, size.Height);
            return;
        }

        ReadOnlyMemory<byte> content = RenderLocalContentToTexture(visual, bounds, size, pad);
        if (content.Length == 0)
        {
            return;
        }

        byte[] shadow = BuildShadowPixels(content.Span, size.Width, size.Height, kernel, radius, color, opacity);
        TextureHandle contentTexture = UploadCachedTexture(size, content.Span);
        TextureHandle shadowTexture = UploadCachedTexture(size, shadow);
        StoreEffectCache(handle, EffectCacheKindDropShadow, key, bounds, size, contentTexture, shadowTexture);

        // Shadow first (under), then the crisp content on top; the premultiplied GPU blend
        // composes them in order.
        DrawTextureQuad(commands, shadowTexture, x0 + offsetX, y0 + offsetY, size.Width, size.Height);
        DrawTextureQuad(commands, contentTexture, x0, y0, size.Width, size.Height);
    }

    /// <summary>
    /// BlurEffect: renders the subtree and applies a separable (horizontal then vertical)
    /// convolution over all four premultiplied channels. Gaussian and Box kernels share the
    /// same two-pass core; the blur bleeds up to the kernel radius beyond the content bounds.
    /// </summary>
    private void DrawBlur(uint handle, Slot visual, Slot effect, Rect bounds)
    {
        (float[] kernel, int radius) = effect.EffectKernelType == BoxKernelType
            ? BoxKernel(effect.EffectRadius)
            : GaussianKernel(effect.EffectRadius);

        int pad = radius;
        PixelSize size = TargetSize(bounds, pad);
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        ulong key = ComputeEffectVersion(visual, effect);
        if (TryGetEffectCache(handle, EffectCacheKindBlur, key, bounds, size, out EffectCacheEntry cache))
        {
            DrawTextureQuad(_commands!, cache.TextureA, bounds.Left - pad, bounds.Top - pad, size.Width, size.Height);
            return;
        }

        ReadOnlyMemory<byte> content = RenderLocalContentToTexture(visual, bounds, size, pad);
        if (content.Length == 0)
        {
            return;
        }

        byte[] blurred = BlurPixels(content.Span, size.Width, size.Height, kernel, radius);
        TextureHandle blurredTexture = UploadCachedTexture(size, blurred);
        StoreEffectCache(handle, EffectCacheKindBlur, key, bounds, size, blurredTexture, TextureHandle.Invalid);
        DrawTextureQuad(_commands!, blurredTexture, bounds.Left - pad, bounds.Top - pad, size.Width, size.Height);
    }

    /// <summary>
    /// OpacityMask: renders the subtree offscreen, then attenuates the content's premultiplied
    /// channels by the mask brush's per-pixel alpha (computed on CPU — solid, linear-gradient
    /// and radial-gradient masks, honoring mapping mode, brush transform, spread and opacity).
    /// </summary>
    private void DrawOpacityMask(uint handle, Slot visual, Rect bounds)
    {
        PixelSize size = TargetSize(bounds, 0);
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        ReadOnlyMemory<byte> content = RenderLocalContentToTexture(visual, bounds, size, 0);
        ReadOnlyMemory<byte> mask = ComputeMaskAlpha(visual.OpacityMask, bounds, size);
        if (content.Length == 0 || mask.Length == 0)
        {
            // Mask brush kind we cannot sample (or no content): degrade to the plain subtree
            // so the content still renders rather than disappearing.
            if (!visual.Content.IsNull)
            {
                RenderContent(visual.Content);
            }

            foreach (ResourceHandle child in visual.Children)
            {
                WalkVisual(child);
            }

            return;
        }

        ulong key = ComputeEffectVersion(visual, null);
        if (TryGetEffectCache(handle, EffectCacheKindOpacityMask, key, bounds, size, out EffectCacheEntry cache))
        {
            DrawTextureQuad(_commands!, cache.TextureA, bounds.Left, bounds.Top, size.Width, size.Height);
            return;
        }

        byte[] masked = ApplyMaskAlpha(content.Span, mask.Span, size.Width, size.Height);
        TextureHandle maskedTexture = UploadCachedTexture(size, masked);
        StoreEffectCache(handle, EffectCacheKindOpacityMask, key, bounds, size, maskedTexture, TextureHandle.Invalid);
        DrawTextureQuad(_commands!, maskedTexture, bounds.Left, bounds.Top, size.Width, size.Height);
    }

    /// <summary>
    /// Computes the per-pixel alpha of an opacity-mask brush over <paramref name="bounds"/>
    /// as an RGBA buffer (rgb = 0, a = mask value). Replicates the gradient parameter and
    /// spread folding used by the GPU gradient path, so solid, linear-gradient and
    /// radial-gradient masks match the rasterized brush.
    /// </summary>
    private ReadOnlyMemory<byte> ComputeMaskAlpha(ResourceHandle maskBrush, Rect bounds, PixelSize size)
    {
        if (maskBrush.IsNull || !_resources.TryGetValue(maskBrush.Value, out Slot? slot) ||
            slot.Kind is not (SlotKind.SolidColorBrush or SlotKind.LinearGradientBrush or SlotKind.RadialGradientBrush))
        {
            return Array.Empty<byte>();
        }

        var mask = new byte[size.Width * size.Height * 4];
        if (slot.Kind == SlotKind.SolidColorBrush)
        {
            // TryResolveBrush applies the brush opacity into the color alpha.
            if (!TryResolveBrush(maskBrush, out BrushPaint paint))
            {
                return Array.Empty<byte>();
            }

            byte a = ToByte(paint.Color.A);
            for (int i = 0; i < size.Width * size.Height; i++)
            {
                mask[(i * 4) + 3] = a;
            }

            return mask;
        }

        (Point start, Point end) = ResolveLinearAxis(slot, bounds);
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len2 = (dx * dx) + (dy * dy);
        (Point origin, double rx, double ry) = ResolveRadial(slot, bounds);
        bool linear = slot.Kind == SlotKind.LinearGradientBrush;
        double opacity = Math.Clamp(slot.Opacity, 0.0, 1.0);
        // Stop colors arrive as scRGB floats; interpolate in sRGB like the baked gradient LUT.
        var srgbStops = new (double Position, ColorRgba Color)[slot.Stops.Length];
        for (int i = 0; i < slot.Stops.Length; i++)
        {
            ColorRgba c = slot.Stops[i].Color;
            srgbStops[i] = (slot.Stops[i].Position, new ColorRgba(ScRgbToSrgb(c.R), ScRgbToSrgb(c.G), ScRgbToSrgb(c.B), c.A));
        }

        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                double px = bounds.Left + x;
                double py = bounds.Top + y;
                double t;
                if (linear)
                {
                    t = len2 > 0 ? (((px - start.X) * dx) + ((py - start.Y) * dy)) / len2 : 0;
                }
                else
                {
                    double gx = rx > 0 ? (px - origin.X) / rx : 0;
                    double gy = ry > 0 ? (py - origin.Y) / ry : 0;
                    t = Math.Sqrt((gx * gx) + (gy * gy));
                }

                t = FoldGradientParameter(t, slot.SpreadMethod);
                float alpha = SampleGradientStops(srgbStops, t).A;
                int index = ((y * size.Width) + x) * 4;
                mask[index + 3] = ToByte(alpha * (float)opacity);
            }
        }

        return mask;
    }

    private static double FoldGradientParameter(double t, GradientSpreadMethod spread)
    {
        switch (spread)
        {
            case GradientSpreadMethod.Pad:
                return Math.Clamp(t, 0.0, 1.0);
            case GradientSpreadMethod.Repeat:
                return t - Math.Floor(t);
            case GradientSpreadMethod.Reflect:
                double f = t - Math.Floor(t);
                return f < 0.5 ? f * 2.0 : (1.0 - f) * 2.0;
            default:
                return Math.Clamp(t, 0.0, 1.0);
        }
    }

    private static PixelSize TargetSize(Rect bounds, int pad)
    {
        return new PixelSize(
            (int)Math.Ceiling(bounds.Width + (pad * 2)),
            (int)Math.Ceiling(bounds.Height + (pad * 2)));
    }

    /// <summary>
    /// Renders the visual's subtree (content + children) into an offscreen presenter sized to
    /// <paramref name="size"/> and returns its resolved premultiplied RGBA readback. The content
    /// bounds are translated so <paramref name="bounds"/>.TopLeft lands at
    /// (<paramref name="pad"/>, <paramref name="pad"/>) in the target.
    /// </summary>
    private ReadOnlyMemory<byte> RenderLocalContentToTexture(Slot visual, Rect bounds, PixelSize size, int pad)
    {
        IVulkanPresenter offscreen = OffscreenFactory!(size);
        using (offscreen)
        {
            // The subtree may contain glyph runs; those must be uploaded into an atlas owned by
            // THIS offscreen presenter, not the frame's atlas (whose textures live on the frame
            // presenter and would be unknown to the offscreen presenter's command list). A nested
            // effect's uploads also go to the offscreen presenter while its list is active.
            using var atlas = new GlyphAtlas(offscreen, new PixelSize(512, 512));
            IVulkanPresenter? savedPresenter = Presenter;
            Presenter = offscreen;
            try
            {
                offscreen.Render(commands =>
                {
                    commands.Clear(ColorRgba.Transparent);
                    commands.PushTransform(Matrix3x2.Translate(pad - bounds.Left, pad - bounds.Top));
                    RasterizeLocalContentInto(commands, atlas, visual);
                    commands.PopTransform();
                });
            }
            finally
            {
                Presenter = savedPresenter;
            }

            ReadOnlyMemory<byte> pixels = offscreen.ReadbackRgba();

            // Everything the nested walk cached on the offscreen presenter dies with it.
            ForgetPresenter(offscreen);
            return pixels;
        }
    }

    /// <summary>Walks a visual's content + children into a different command list (offscreen target).</summary>
    private void RasterizeLocalContentInto(IRasterCommandList commands, GlyphAtlas? atlas, Slot visual)
    {
        IRasterCommandList? savedCommands = _commands;
        GlyphAtlas? savedAtlas = _atlas;
        int savedDepth = _popStack.Count;
        _commands = commands;
        _atlas = atlas;
        try
        {
            if (!visual.Content.IsNull)
            {
                RenderContent(visual.Content);
            }

            foreach (ResourceHandle child in visual.Children)
            {
                WalkVisual(child);
            }
        }
        finally
        {
            while (_popStack.Count > savedDepth)
            {
                _ = _popStack.Pop();
            }

            _commands = savedCommands;
            _atlas = savedAtlas;
        }
    }

    private const int EffectCacheKindDropShadow = 1;
    private const int EffectCacheKindBlur = 2;
    private const int EffectCacheKindOpacityMask = 3;

    /// <summary>Uploads an effect-composite texture. Cached per (presenter, visual slot), NOT
    /// per-frame: destroyed on cache replacement or <c>VisitChannelDeleteResource</c>.</summary>
    private TextureHandle UploadCachedTexture(PixelSize size, ReadOnlySpan<byte> pixels)
    {
        IGpuTexture texture = Presenter!.CreateTexture(new TextureUpload(size, PixelFormat.Rgba8Unorm, pixels, size.Width * 4));

        SetTextureSize(Presenter, texture.Handle.Value, size);
        return texture.Handle;
    }

    /// <summary>Cache hit for (current presenter, visual slot): a composite THIS presenter
    /// already rendered with a matching invalidation key/bounds/size. Textures are always
    /// stored as a complete pair, so a kind match with a valid TextureA is a usable warm
    /// composite.</summary>
    private bool TryGetEffectCache(uint slotHandle, int kind, ulong key, Rect bounds, PixelSize size, out EffectCacheEntry entry)
    {
        entry = default;
        return Presenter is not null &&
            _effectCaches.TryGetValue(Presenter, out Dictionary<uint, EffectCacheEntry>? perPresenter) &&
            perPresenter.TryGetValue(slotHandle, out entry) &&
            entry.Kind == kind &&
            entry.Key == key &&
            entry.Bounds == bounds &&
            entry.Size == size &&
            entry.TextureA.IsValid;
    }

    private void StoreEffectCache(uint slotHandle, int kind, ulong key, Rect bounds, PixelSize size, TextureHandle a, TextureHandle b)
    {
        if (Presenter is not { } presenter)
        {
            return;
        }

        if (!_effectCaches.TryGetValue(presenter, out Dictionary<uint, EffectCacheEntry>? perPresenter))
        {
            perPresenter = [];
            _effectCaches.Add(presenter, perPresenter);
        }

        // Replace only THIS presenter's entry: another presenter's copy is still valid there
        // (its textures were created on that presenter) and is cleaned by its own re-render or
        // by slot deletion.
        if (perPresenter.TryGetValue(slotHandle, out EffectCacheEntry previous))
        {
            DestroyEntryTextures(presenter, previous);
        }

        perPresenter[slotHandle] = new EffectCacheEntry
        {
            Kind = kind,
            Key = key,
            Bounds = bounds,
            Size = size,
            TextureA = a,
            TextureB = b
        };
    }

    /// <summary>Destroys a visual's cached effect-composite textures on EVERY presenter that
    /// rendered it, destroying each entry's textures on the presenter that OWNS them — with
    /// main+popup frames sharing one graph, the current presenter is not necessarily the
    /// owner. Safe on any slot.</summary>
    private void DestroyCachedTextures(uint slotHandle)
    {
        foreach (KeyValuePair<IVulkanPresenter, Dictionary<uint, EffectCacheEntry>> perPresenter in _effectCaches)
        {
            if (perPresenter.Value.Remove(slotHandle, out EffectCacheEntry entry))
            {
                DestroyEntryTextures(perPresenter.Key, entry);
            }
        }
    }

    private void DestroyEntryTextures(IVulkanPresenter owner, EffectCacheEntry entry)
    {
        if (entry.TextureA.IsValid)
        {
            owner.DestroyTexture(entry.TextureA);
            RemoveTextureSize(owner, entry.TextureA.Value);
        }

        if (entry.TextureB.IsValid)
        {
            owner.DestroyTexture(entry.TextureB);
            RemoveTextureSize(owner, entry.TextureB.Value);
        }
    }

    /// <summary>Destroys a bitmap slot's textures on EVERY presenter that uploaded it, each
    /// on its owning presenter. A slot drawn inside an offscreen effect/VisualBrush walk is
    /// uploaded on that transient presenter, not on the frame's, so the current
    /// <see cref="Presenter"/> field is not necessarily the owner.</summary>
    private void DestroyBitmapTextures(uint slotHandle)
    {
        foreach (KeyValuePair<IVulkanPresenter, Dictionary<uint, TextureHandle>> perPresenter in _bitmapTextures)
        {
            if (perPresenter.Value.Remove(slotHandle, out TextureHandle texture))
            {
                perPresenter.Key.DestroyTexture(texture);
                RemoveTextureSize(perPresenter.Key, texture.Value);
            }
        }
    }

    /// <summary>Drops every per-presenter cache entry owned by <paramref name="presenter"/>.
    /// Called when a transient offscreen presenter is about to be disposed (its textures are
    /// dead, its handles must never be resolved by a later walk, and leaving the dictionaries
    /// keyed by it would grow one entry per offscreen render) and by a closed window's frame
    /// disposal (Nova.Host CompositionFrame.Dispose): the graph is shared by main+popup frames,
    /// so a dead frame's keys must be forgotten before the presenter is disposed, or the next
    /// resource release would destroy textures on the disposed presenter.</summary>
    public void ForgetPresenter(IVulkanPresenter presenter)
    {
        _ = _gradientLuts.Remove(presenter);
        _ = _visualTextures.Remove(presenter);
        _ = _effectCaches.Remove(presenter);
        _ = _bitmapTextures.Remove(presenter);
        _ = _textureSizes.Remove(presenter);
    }

    private void SetTextureSize(IVulkanPresenter presenter, uint handle, PixelSize size)
    {
        if (!_textureSizes.TryGetValue(presenter, out Dictionary<uint, PixelSize>? perPresenter))
        {
            perPresenter = [];
            _textureSizes.Add(presenter, perPresenter);
        }

        perPresenter[handle] = size;
    }

    private bool TryGetTextureSize(IVulkanPresenter presenter, uint handle, out PixelSize size)
    {
        size = default;
        return _textureSizes.TryGetValue(presenter, out Dictionary<uint, PixelSize>? perPresenter)
            && perPresenter.TryGetValue(handle, out size);
    }

    private void RemoveTextureSize(IVulkanPresenter presenter, uint handle)
    {
        if (_textureSizes.TryGetValue(presenter, out Dictionary<uint, PixelSize>? perPresenter))
        {
            _ = perPresenter.Remove(handle);
        }
    }

    /// <summary>
    /// Computes the invalidation key for a visual's effect composite: everything the composite
    /// pixels depend on — the effect's own parameters, the visual's subtree wiring, the
    /// render-data blob and every resource it references (recursively through brush/geometry
    /// transforms, pen brushes and VisualBrush visuals), the opacity-mask brush, and each
    /// child's full composite. The effect visual's OWN offset/transform/alpha are NOT included:
    /// the composite is drawn in local space and the parent applies layout via the command
    /// stack, so a move redraws the cached quads without recomputing pixels. A stable key on an
    /// idle window (WPF sends no commands) makes the effect path two textured draws per frame.
    /// </summary>
    private ulong ComputeEffectVersion(Slot visual, Slot? effect)
    {
        _versionVisited.Clear();
        ulong version = effect is null ? 0ul : effect.Version;
        version = (version * 31) + visual.ContentVersion;
        version = AddRenderDataVersion(version, visual, _versionVisited);
        foreach (ResourceHandle child in visual.Children)
        {
            if (_resources.TryGetValue(child.Value, out Slot? childSlot) && childSlot.Kind == SlotKind.Visual)
            {
                version = (version * 31) + ChildCompositeVersion(child, childSlot, _versionVisited);
            }
        }

        if (!visual.OpacityMask.IsNull && _resources.TryGetValue(visual.OpacityMask.Value, out Slot? maskSlot))
        {
            version = (version * 31) + DependentVersion(visual.OpacityMask, maskSlot, _versionVisited);
        }

        return version;
    }
    private ulong AddRenderDataVersion(ulong version, Slot visual, HashSet<uint> visited)
    {
        if (visual.Content.IsNull || !_resources.TryGetValue(visual.Content.Value, out Slot? content) || content.Kind != SlotKind.RenderData)
        {
            return version;
        }

        version = (version * 31) + content.Version;
        foreach (ResourceHandle dependent in content.Dependents)
        {
            if (!dependent.IsNull && _resources.TryGetValue(dependent.Value, out Slot? dependentSlot))
            {
                version = (version * 31) + DependentVersion(dependent, dependentSlot, visited);
            }
        }

        return version;
    }

    /// <summary>A child's contribution to the parent composite: its full version (layout
    /// included — children render at their own offset/transform/alpha inside the parent's local
    /// space) plus its render-data content and descendants.</summary>
    private ulong ChildCompositeVersion(ResourceHandle handle, Slot visual, HashSet<uint> visited)
    {
        if (!visited.Add(handle.Value))
        {
            return 0;
        }

        ulong version = visual.Version;
        version = AddRenderDataVersion(version, visual, visited);
        foreach (ResourceHandle child in visual.Children)
        {
            if (_resources.TryGetValue(child.Value, out Slot? childSlot) && childSlot.Kind == SlotKind.Visual)
            {
                version = (version * 31) + ChildCompositeVersion(child, childSlot, visited);
            }
        }

        return version;
    }

    /// <summary>The version of a resource reachable from a composite (brushes, geometries,
    /// pens, glyph runs, VisualBrush visuals and their referenced transforms), guarding cycles
    /// via <paramref name="visited"/>.</summary>
    private ulong DependentVersion(ResourceHandle handle, Slot slot, HashSet<uint> visited)
    {
        if (!visited.Add(handle.Value))
        {
            return 0;
        }

        ulong version = slot.Version;
        switch (slot.Kind)
        {
            case SlotKind.SolidColorBrush:
            case SlotKind.LinearGradientBrush:
            case SlotKind.RadialGradientBrush:
                if (!slot.Transform.IsNull && _resources.TryGetValue(slot.Transform.Value, out Slot? brushTransform))
                {
                    version = (version * 31) + DependentVersion(slot.Transform, brushTransform, visited);
                }

                break;
            case SlotKind.VisualBrush:
                if (!slot.Transform.IsNull && _resources.TryGetValue(slot.Transform.Value, out Slot? vbTransform))
                {
                    version = (version * 31) + DependentVersion(slot.Transform, vbTransform, visited);
                }

                if (!slot.Visual.IsNull && _resources.TryGetValue(slot.Visual.Value, out Slot? vbVisual) && vbVisual.Kind == SlotKind.Visual)
                {
                    version = (version * 31) + ChildCompositeVersion(slot.Visual, vbVisual, visited);
                }

                break;
            case SlotKind.LineGeometry:
            case SlotKind.RectangleGeometry:
            case SlotKind.EllipseGeometry:
            case SlotKind.PathGeometry:
                if (!slot.Transform.IsNull && _resources.TryGetValue(slot.Transform.Value, out Slot? geometryTransform))
                {
                    version = (version * 31) + DependentVersion(slot.Transform, geometryTransform, visited);
                }

                break;
            case SlotKind.Pen:
                if (!slot.Brush.IsNull && _resources.TryGetValue(slot.Brush.Value, out Slot? penBrush))
                {
                    version = (version * 31) + DependentVersion(slot.Brush, penBrush, visited);
                }

                break;
            case SlotKind.ImageBrush:
                if (!slot.Transform.IsNull && _resources.TryGetValue(slot.Transform.Value, out Slot? imageBrushTransform))
                {
                    version = (version * 31) + DependentVersion(slot.Transform, imageBrushTransform, visited);
                }

                if (!slot.ImageSource.IsNull && _resources.TryGetValue(slot.ImageSource.Value, out Slot? imageSourceSlot))
                {
                    version = (version * 31) + DependentVersion(slot.ImageSource, imageSourceSlot, visited);
                }

                break;
            case SlotKind.Unknown:
            case SlotKind.Visual:
            case SlotKind.TranslateTransform:
            case SlotKind.ScaleTransform:
            case SlotKind.RotateTransform:
            case SlotKind.MatrixTransform:
            case SlotKind.TransformGroup:
            case SlotKind.GlyphRun:
            case SlotKind.RenderData:
            case SlotKind.BitmapSource:
            case SlotKind.Drawing:
            case SlotKind.BlurEffect:
            case SlotKind.DropShadowEffect:
                break;
            default:
                break;
        }

        return version;
    }

    private static void DrawTextureQuad(IRasterCommandList commands, TextureHandle texture, double x, double y, double width, double height)
    {
        commands.DrawTexturedQuad(
            new Point(x, y),
            new Point(x + width, y),
            new Point(x + width, y + height),
            new Point(x, y + height),
            texture,
            new Point(0, 0),
            new Point(1, 0),
            new Point(1, 1),
            new Point(0, 1),
            ColorRgba.White);
    }

    /// <summary>Builds the shadow texture: blurred alpha of the content, tinted by the shadow
    /// color and opacity, premultiplied.</summary>
    private static byte[] BuildShadowPixels(ReadOnlySpan<byte> content, int width, int height, ReadOnlySpan<float> kernel, int radius, ColorRgba color, double opacity)
    {
        int pixelCount = width * height;
        var alpha = new float[pixelCount];
        for (int i = 0; i < pixelCount; i++)
        {
            alpha[i] = content[(i * 4) + 3] / 255f;
        }

        if (radius > 0)
        {
            SeparableBlurInPlace(alpha, width, height, kernel, radius);
        }

        var shadow = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            float a = alpha[i] * color.A * (float)opacity;
            shadow[(i * 4) + 0] = ToByte(color.R * a);
            shadow[(i * 4) + 1] = ToByte(color.G * a);
            shadow[(i * 4) + 2] = ToByte(color.B * a);
            shadow[(i * 4) + 3] = ToByte(a);
        }

        return shadow;
    }

    /// <summary>Applies a separable blur to all four premultiplied channels of an RGBA8 buffer.</summary>
    private static byte[] BlurPixels(ReadOnlySpan<byte> content, int width, int height, ReadOnlySpan<float> kernel, int radius)
    {
        int pixelCount = width * height;
        if (radius <= 0)
        {
            return content.ToArray();
        }

        var blurred = new byte[pixelCount * 4];
        var channel = new float[pixelCount];
        for (int c = 0; c < 4; c++)
        {
            for (int i = 0; i < pixelCount; i++)
            {
                channel[i] = content[(i * 4) + c] / 255f;
            }

            SeparableBlurInPlace(channel, width, height, kernel, radius);
            for (int i = 0; i < pixelCount; i++)
            {
                blurred[(i * 4) + c] = ToByte(channel[i]);
            }
        }

        return blurred;
    }

    /// <summary>Attenuates premultiplied content by the mask's alpha channel per pixel.</summary>
    private static byte[] ApplyMaskAlpha(ReadOnlySpan<byte> content, ReadOnlySpan<byte> mask, int width, int height)
    {
        int pixelCount = width * height;
        var result = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            float m = mask[(i * 4) + 3] / 255f;
            result[(i * 4) + 0] = ToByte(content[(i * 4) + 0] / 255f * m);
            result[(i * 4) + 1] = ToByte(content[(i * 4) + 1] / 255f * m);
            result[(i * 4) + 2] = ToByte(content[(i * 4) + 2] / 255f * m);
            result[(i * 4) + 3] = ToByte(content[(i * 4) + 3] / 255f * m);
        }

        return result;
    }

    private static (float[] Kernel, int Radius) GaussianKernel(double blurRadius)
    {
        double sigma = Math.Max(blurRadius / 2.0, 0.5);
        int radius = Math.Clamp((int)Math.Ceiling(sigma * 3.0), 0, MaxBlurRadius);
        if (radius == 0)
        {
            return ([], 0);
        }

        var kernel = new float[(radius * 2) + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            double w = Math.Exp(-(i * i) / (2 * sigma * sigma));
            kernel[i + radius] = (float)w;
            sum += w;
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] = (float)(kernel[i] / sum);
        }

        return (kernel, radius);
    }

    private static (float[] Kernel, int Radius) BoxKernel(double radius)
    {
        int r = Math.Clamp((int)Math.Ceiling(Math.Max(radius, 0.0)), 0, MaxBlurRadius);
        if (r == 0)
        {
            return ([], 0);
        }

        var kernel = new float[(r * 2) + 1];
        Array.Fill(kernel, 1.0f / kernel.Length);
        return (kernel, r);
    }

    /// <summary>Separable two-pass convolution (horizontal then vertical). The clamped-edge
    /// math.Clamp is hoisted out of the interior: taps that can never leave [0, width/height)
    /// run unclamped (vectorizable) and only the edge tails clamp — bit-identical output.</summary>
    private static void SeparableBlurInPlace(float[] channel, int width, int height, ReadOnlySpan<float> kernel, int radius)
    {
        var tmp = new float[channel.Length];
        int maxX = width - 1;
        int maxY = height - 1;
        int midXStart = Math.Min(radius, width);
        int midXEnd = Math.Max(0, maxX - radius);
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int x = 0;
            for (; x < midXStart; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    acc += channel[row + Math.Clamp(x + k, 0, maxX)] * kernel[k + radius];
                }

                tmp[row + x] = acc;
            }

            for (; x <= midXEnd; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    acc += channel[row + x + k] * kernel[k + radius];
                }

                tmp[row + x] = acc;
            }

            for (; x < width; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    acc += channel[row + Math.Clamp(x + k, 0, maxX)] * kernel[k + radius];
                }

                tmp[row + x] = acc;
            }
        }

        int midYStart = Math.Min(radius, height);
        int midYEnd = Math.Max(0, maxY - radius);
        int yy = 0;
        for (; yy < midYStart; yy++)
        {
            int row = yy * width;
            for (int x = 0; x < width; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    acc += tmp[(Math.Clamp(yy + k, 0, maxY) * width) + x] * kernel[k + radius];
                }

                channel[row + x] = acc;
            }
        }

        for (; yy <= midYEnd; yy++)
        {
            int row = yy * width;
            for (int x = 0; x < width; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    acc += tmp[((yy + k) * width) + x] * kernel[k + radius];
                }

                channel[row + x] = acc;
            }
        }

        for (; yy < height; yy++)
        {
            int row = yy * width;
            for (int x = 0; x < width; x++)
            {
                float acc = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    acc += tmp[(Math.Clamp(yy + k, 0, maxY) * width) + x] * kernel[k + radius];
                }

                channel[row + x] = acc;
            }
        }
    }

    private const int MaxBlurRadius = 32;

    /// <summary>WPF <c>KernelType.Box</c> (0 = Gaussian, 1 = Box) as it arrives on the wire.</summary>
    private const int BoxKernelType = 1;

    // Effect composites are CACHED on the visual slot keyed by an invalidation version (see
    // ComputeEffectVersion); the offscreen render + CPU blur run only when the key changes
    // (idle windows and unrelated-sibling changes hit the cache). This scratch set guards the
    // version walk against VisualBrush cycles.
    private readonly HashSet<uint> _versionVisited = [];

    /// <summary>Gradient LUT textures cached per (presenter, brush): the graph is shared by
    /// main+popup frames with distinct Vulkan presenters, and a LUT handle only means
    /// something in the presenter that created it.</summary>
    private readonly Dictionary<IVulkanPresenter, Dictionary<uint, TextureHandle>> _gradientLuts = [];

    /// <summary>VisualBrush textures cached per (presenter, brush), same cross-presenter
    /// rationale as <see cref="_gradientLuts"/>.</summary>
    private readonly Dictionary<IVulkanPresenter, Dictionary<uint, (TextureHandle Texture, PixelSize Size)>> _visualTextures = [];

    /// <summary>Effect-composite textures cached per (presenter, visual slot), same
    /// cross-presenter rationale as <see cref="_gradientLuts"/>. The invalidation key keeps
    /// the versioning the effects workstream proved; the presenter dimension keeps a texture
    /// handle meaningful only in the presenter that created it.</summary>
    private readonly Dictionary<IVulkanPresenter, Dictionary<uint, EffectCacheEntry>> _effectCaches = [];

    /// <summary>One visual's cached effect composite on ONE presenter. Stored/cleared as a
    /// complete pair (TextureB is Invalid for Blur/OpacityMask).</summary>
    private struct EffectCacheEntry
    {
        public int Kind; // 0 = none, 1 = drop shadow, 2 = blur, 3 = opacity mask
        public ulong Key;
        public Rect Bounds;
        public PixelSize Size;
        public TextureHandle TextureA; // content (shadow) / blurred / masked
        public TextureHandle TextureB; // shadow-only (drop shadow), else Invalid
    }

    private void RenderContent(ResourceHandle handle)
    {
        if (_resources.TryGetValue(handle.Value, out Slot? slot) && slot.Kind == SlotKind.RenderData)
        {
            ReplayRenderData(slot);
        }
    }

    private void ReplayRenderData(Slot renderData)
    {
        int savedDepth = _popStack.Count;
        try
        {
            MilCommandParser.ParseRenderData(renderData.Blob, renderData.Dependents, this);
        }
        finally
        {
            while (_popStack.Count > savedDepth)
            {
                _ = _popStack.Pop();
            }
        }
    }

    private void DrawGeometryContent(IRasterCommandList commands, Slot geometry, BrushPaint fill, ResourceHandle pen, bool hasFill = true)
    {
        Matrix3x2 transform = ResolveTransform(geometry.Transform);
        bool pushTransform = !transform.IsIdentity;
        if (pushTransform)
        {
            commands.PushTransform(transform);
        }

        ColorRgba penColor = default;
        double penThickness = 0;
        bool hasPen = !pen.IsNull && TryResolvePen(pen, out penColor, out penThickness);


        if (geometry.Kind == SlotKind.RectangleGeometry)
        {
            if (hasFill)
            {
                if (geometry.RadiusX > 0 || geometry.RadiusY > 0)
                {
                    FillRoundedRectangle(commands, geometry.Rectangle, geometry.RadiusX, geometry.RadiusY, fill);
                }
                else if (!geometry.Rectangle.IsEmpty)
                {
                    FillRectangle(commands, geometry.Rectangle, fill);
                }
            }

            if (hasPen)
            {
                if (geometry.RadiusX > 0 || geometry.RadiusY > 0)
                {
                    StrokeRoundedRectangle(commands, geometry.Rectangle, geometry.RadiusX, geometry.RadiusY, penThickness, penColor);
                }
                else
                {
                    StrokeAxisAlignedRect(commands, geometry.Rectangle, penThickness, penColor);
                }
            }
        }
        else if (geometry.Kind == SlotKind.EllipseGeometry)
        {
            if (hasFill)
            {
                FillEllipseGeometry(commands, geometry.Center, geometry.RadiusX, geometry.RadiusY, fill);
            }

            if (hasPen)
            {
                StrokeEllipse(commands, geometry.Center, geometry.RadiusX, geometry.RadiusY, penThickness, penColor);
            }
        }
        else if (geometry.Kind == SlotKind.LineGeometry)
        {
            if (hasPen)
            {
                StrokeLine(commands, geometry.Start, geometry.End, penThickness, penColor);
            }
        }
        else if (geometry.Kind == SlotKind.PathGeometry)
        {
            DrawPathGeometry(commands, geometry, hasFill, fill, hasPen, penThickness, penColor);
        }

        if (pushTransform)
        {
            commands.PopTransform();
        }
    }

    /// <summary>
    /// Renders a MIL path stream: flatten via Nova.Geometry2D, tessellate the fill, and
    /// widen the stroke outline with <see cref="Widener"/> honoring joins/caps.
    /// </summary>
    private void DrawPathGeometry(IRasterCommandList commands, Slot geometry, bool hasFill, BrushPaint fill, bool hasPen, double penThickness, ColorRgba penColor)
    {
        if (geometry.PathData.Length == 0)
        {
            return;
        }

        IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(geometry.PathData, MilPathFlattener.DefaultTolerance);
        if (hasFill)
        {
            foreach (Contour contour in contours)
            {
                if (!contour.IsFilled || contour.ReadOnlySpan.Length < 3)
                {
                    continue;
                }

                FillContour(commands, contour.ReadOnlySpan, contour.Bounds(), fill);
            }
        }

        if (hasPen)
        {
            var pen = new PenStyle(penThickness, PenLineJoin.Miter, PenLineCap.Flat, PenLineCap.Flat);
            foreach (Contour contour in contours)
            {
                if (contour.IsClosed)
                {
                    (Contour outer, Contour? inner) = Widener.WidenClosed(contour.ReadOnlySpan, pen);
                    if (inner is not null)
                    {
                        FillTessellatedRing(commands, outer, inner, penColor);
                    }
                    else
                    {
                        FillTessellatedContour(commands, outer.ReadOnlySpan, penColor);
                    }
                }
                else if (contour.ReadOnlySpan.Length >= 2)
                {
                    Contour outline = Widener.WidenOpen(contour.ReadOnlySpan, pen);
                    FillTessellatedContour(commands, outline.ReadOnlySpan, penColor);
                }
            }
        }
    }

    /// <summary>
    /// Fills the ring between an outer and inner loop (a stroked closed contour) using
    /// even-odd across both, so the interior stays hollow.
    /// </summary>
    private static void FillTessellatedRing(IRasterCommandList commands, Contour outer, Contour inner, ColorRgba color)
    {
        Contour[] rings = [outer, inner];
        int required = Tessellator.FillPathRequired(rings, FillRule.EvenOdd);
        if (required <= 0)
        {
            return;
        }

        Point[] triangles = new Point[required];
        int written = Tessellator.FillPath(rings, FillRule.EvenOdd, triangles);
        if (written > 0)
        {
            commands.FillTriangles(triangles.AsSpan(0, written), color);
        }
    }

    private static void StrokeRoundedRectangle(IRasterCommandList commands, Rect rect, double radiusX, double radiusY, double thickness, ColorRgba color)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        double rx = Math.Min(Math.Max(0, radiusX), rect.Width * 0.5);
        double ry = Math.Min(Math.Max(0, radiusY), rect.Height * 0.5);
        if (rx <= 0 || ry <= 0)
        {
            StrokeAxisAlignedRect(commands, rect, thickness, color);
            return;
        }

        PathBuilder path = new();
        BuildRoundedRect(path, rect, rx, ry);
        List<Point> contour = [];
        path.Flatten(FlattenTolerance, contour);
        (Contour outer, Contour? inner) = Widener.WidenClosed(
            CollectionsMarshal.AsSpan(contour),
            new PenStyle(thickness, PenLineJoin.Miter, PenLineCap.Flat, PenLineCap.Flat));
        if (inner is not null)
        {
            FillTessellatedRing(commands, outer, inner, color);
        }
        else
        {
            FillTessellatedContour(commands, outer.ReadOnlySpan, color);
        }
    }

    private static void StrokeEllipse(IRasterCommandList commands, Point center, double radiusX, double radiusY, double thickness, ColorRgba color)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            return;
        }

        PathBuilder path = new();
        BuildEllipse(path, center, radiusX, radiusY);
        List<Point> contour = [];
        path.Flatten(FlattenTolerance, contour);
        (Contour outer, Contour? inner) = Widener.WidenClosed(
            CollectionsMarshal.AsSpan(contour),
            new PenStyle(thickness, PenLineJoin.Miter, PenLineCap.Flat, PenLineCap.Flat));
        if (inner is not null)
        {
            FillTessellatedRing(commands, outer, inner, color);
        }
        else
        {
            FillTessellatedContour(commands, outer.ReadOnlySpan, color);
        }
    }

    private enum BrushKind
    {
        Solid,
        LinearGradient,
        RadialGradient,
        Visual,
        Image
    }

    private readonly record struct BrushPaint(BrushKind Kind, ColorRgba Color, ResourceHandle Handle, double Opacity);

    private bool TryResolveBrush(ResourceHandle handle, out BrushPaint brush)
    {
        brush = default;
        if (handle.IsNull || !_resources.TryGetValue(handle.Value, out Slot? slot))
        {
            return false;
        }

        if (slot.Kind == SlotKind.SolidColorBrush)
        {
            brush = new BrushPaint(BrushKind.Solid, WithOpacity(SrgbEncode(slot.Color), slot.Opacity), ResourceHandle.Null, slot.Opacity);
            return true;
        }

        if (slot.Kind is SlotKind.LinearGradientBrush or SlotKind.RadialGradientBrush or SlotKind.VisualBrush or SlotKind.ImageBrush)
        {
            BrushKind kind = slot.Kind == SlotKind.LinearGradientBrush
                ? BrushKind.LinearGradient
                : slot.Kind == SlotKind.RadialGradientBrush
                    ? BrushKind.RadialGradient
                    : slot.Kind == SlotKind.ImageBrush
                        ? BrushKind.Image
                        : BrushKind.Visual;
            brush = new BrushPaint(kind, default, handle, slot.Opacity);
            return true;
        }

        return false;
    }

    /// <summary>Resolves a brush that must be a plain color (pens, glyph tints).</summary>
    private bool TryResolveSolidColor(ResourceHandle handle, out ColorRgba color)
    {
        color = default;
        if (handle.IsNull ||
            !_resources.TryGetValue(handle.Value, out Slot? slot) ||
            slot.Kind != SlotKind.SolidColorBrush)
        {
            return false;
        }

        color = WithOpacity(SrgbEncode(slot.Color), slot.Opacity);
        return true;
    }

    private bool TryResolvePen(ResourceHandle handle, out ColorRgba color, out double thickness)
    {
        color = default;
        thickness = 0;
        if (handle.IsNull ||
            !_resources.TryGetValue(handle.Value, out Slot? slot) ||
            slot.Kind != SlotKind.Pen ||
            slot.Thickness <= 0)
        {
            return false;
        }

        thickness = slot.Thickness;
        if (TryResolveSolidColor(slot.Brush, out color))
        {
            return true;
        }

        // The MIL wire carries no per-vertex pen colors, so a gradient pen cannot be
        // stroked with its true gradient. Degrade to the gradient's strongest stop
        // instead of silently dropping the stroke — Fluent's ButtonBorderBrush is a
        // 3px absolute gradient, and dropping it removed card and button borders
        // entirely while Classic/Aero (solid brushes) kept theirs.
        if (slot.Brush.IsNull ||
            !_resources.TryGetValue(slot.Brush.Value, out Slot? brushSlot) ||
            brushSlot.Kind is not (SlotKind.LinearGradientBrush or SlotKind.RadialGradientBrush) ||
            brushSlot.Stops.Length == 0)
        {
            return false;
        }

        ColorRgba strongest = brushSlot.Stops[^1].Color;
        foreach (GradientStop stop in brushSlot.Stops)
        {
            if (stop.Color.A > strongest.A)
            {
                strongest = stop.Color;
            }
        }

        if (strongest.A <= 0)
        {
            return false;
        }

        color = WithOpacity(SrgbEncode(strongest), brushSlot.Opacity);
        return true;
    }

    private Matrix3x2 ResolveTransform(ResourceHandle handle)
    {
        if (handle.IsNull || !_resources.TryGetValue(handle.Value, out Slot? slot))
        {
            return Matrix3x2.Identity;
        }

        Matrix3x2 result = Matrix3x2.Identity;
        if (slot.Kind == SlotKind.TranslateTransform)
        {
            result = Matrix3x2.Translate(slot.X, slot.Y);
        }
        else if (slot.Kind == SlotKind.ScaleTransform)
        {
            result = ScaleAround(slot.ScaleX, slot.ScaleY, slot.CenterX, slot.CenterY);
        }
        else if (slot.Kind == SlotKind.RotateTransform)
        {
            result = RotateAround(slot.Angle, slot.CenterX, slot.CenterY);
        }
        else if (slot.Kind == SlotKind.MatrixTransform)
        {
            result = slot.Matrix;
        }
        else if (slot.Kind == SlotKind.TransformGroup)
        {
            // WPF composes a TransformGroup's children left-to-right as matrix multiplies.
            foreach (ResourceHandle child in slot.TransformChildren)
            {
                result = Matrix3x2.Multiply(result, ResolveTransform(child));
            }
        }

        return result;
    }

    private static Matrix3x2 ScaleAround(double scaleX, double scaleY, double centerX, double centerY)
    {
        // Row-vector convention (p' = p * M; M1*M2 applies M1 first): scale about a center is
        // T(-c) * S * T(c) — translate to the origin, scale, translate back. Matches WPF's
        // Matrix.CreateScaling(scaleX, scaleY, centerX, centerY).
        return Matrix3x2.Multiply(
            Matrix3x2.Translate(-centerX, -centerY),
            Matrix3x2.Multiply(Matrix3x2.Scale(scaleX, scaleY), Matrix3x2.Translate(centerX, centerY)));
    }

    private static Matrix3x2 RotateAround(double angle, double centerX, double centerY)
    {
        double radians = angle * (Math.PI / 180.0);
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        var rotation = new Matrix3x2(cos, sin, -sin, cos, 0, 0);
        // Row-vector convention: rotate about a center is T(-c) * R * T(c). Matches WPF's
        // Matrix.CreateRotationRadians(angle, centerX, centerY).
        return Matrix3x2.Multiply(
            Matrix3x2.Translate(-centerX, -centerY),
            Matrix3x2.Multiply(rotation, Matrix3x2.Translate(centerX, centerY)));
    }

    private Rect? ResolveClipRect(ResourceHandle handle)
    {
        return handle.IsNull ||
            !_resources.TryGetValue(handle.Value, out Slot? slot) ||
            slot.Kind != SlotKind.RectangleGeometry ||
            slot.Rectangle.IsEmpty ||
            !ResolveTransform(slot.Transform).IsIdentity
            ? null
            : slot.Rectangle;
    }

    private bool HitTestVisual(ResourceHandle handle, Point point, Matrix3x2 parentTransform, out ResourceHandle hit)
    {
        hit = ResourceHandle.Null;
        if (!_resources.TryGetValue(handle.Value, out Slot? visual) || visual.Kind != SlotKind.Visual)
        {
            return false;
        }

        var local = Matrix3x2.Multiply(
            Matrix3x2.Translate(visual.Offset.X, visual.Offset.Y),
            ResolveTransform(visual.Transform));
        var accumulated = Matrix3x2.Multiply(parentTransform, local);
        for (int i = visual.Children.Count - 1; i >= 0; i--)
        {
            if (HitTestVisual(visual.Children[i], point, accumulated, out hit))
            {
                return true;
            }
        }

        if (visual.Content.IsNull)
        {
            return false;
        }

        if (ComputeContentBounds(visual.Content) is not { } bounds || bounds.IsEmpty)
        {
            return false;
        }

        if (!TransformContains(accumulated, bounds, point))
        {
            return false;
        }

        hit = handle;
        return true;
    }

    private Rect? ComputeContentBounds(ResourceHandle handle)
    {
        if (!_resources.TryGetValue(handle.Value, out Slot? slot))
        {
            return null;
        }

        if (slot.Kind == SlotKind.RectangleGeometry)
        {
            return slot.Rectangle.IsEmpty ? null : slot.Rectangle;
        }

        if (slot.Kind != SlotKind.RenderData)
        {
            return null;
        }

        _measuring = true;
        _measureBounds = null;
        try
        {
            MilCommandParser.ParseRenderData(slot.Blob, slot.Dependents, this);
            return _measureBounds;
        }
        finally
        {
            _measuring = false;
        }
    }

    private void UnionMeasure(Rect rect)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        _measureBounds = _measureBounds is { } current ? Union(current, rect) : rect;
    }

    private void UnionMeasureGeometry(ResourceHandle geometry)
    {
        if (!_resources.TryGetValue(geometry.Value, out Slot? slot))
        {
            return;
        }

        if (slot.Kind == SlotKind.RectangleGeometry)
        {
            if (!slot.Rectangle.IsEmpty)
            {
                UnionMeasure(slot.Rectangle);
            }
        }
        else if (slot.Kind == SlotKind.EllipseGeometry)
        {
            UnionMeasure(new Rect(
                slot.Center.X - slot.RadiusX,
                slot.Center.Y - slot.RadiusY,
                slot.RadiusX * 2,
                slot.RadiusY * 2));
        }
        else if (slot.Kind == SlotKind.LineGeometry)
        {
            UnionMeasure(new Rect(
                Math.Min(slot.Start.X, slot.End.X),
                Math.Min(slot.Start.Y, slot.End.Y),
                Math.Abs(slot.End.X - slot.Start.X),
                Math.Abs(slot.End.Y - slot.Start.Y)));
        }
        else if (slot.Kind == SlotKind.PathGeometry && slot.PathData.Length > 0)
        {
            IReadOnlyList<Contour> contours = MilPathFlattener.Flatten(slot.PathData, MilPathFlattener.DefaultTolerance);
            foreach (Contour contour in contours)
            {
                UnionMeasure(contour.Bounds());
            }
        }
    }

    private Slot EnsureSlot(ResourceHandle handle)
    {
        if (_resources.TryGetValue(handle.Value, out Slot? slot))
        {
            return slot;
        }

        slot = new Slot();
        _resources.Add(handle.Value, slot);
        return slot;
    }

    private static void FillRoundedRectangle(IRasterCommandList commands, Rect rect, double radiusX, double radiusY, ColorRgba color)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        if (radiusX <= 0 || radiusY <= 0)
        {
            commands.FillRectangle(rect, color);
            return;
        }

        PathBuilder path = new();
        BuildRoundedRect(path, rect, radiusX, radiusY);
        List<Point> contour = [];
        path.Flatten(FlattenTolerance, contour);
        FillTessellatedContour(commands, CollectionsMarshal.AsSpan(contour), color);
    }

    private void FillRoundedRectangle(IRasterCommandList commands, Rect rect, double radiusX, double radiusY, BrushPaint brush)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        if (brush.Kind == BrushKind.Solid)
        {
            FillRoundedRectangle(commands, rect, radiusX, radiusY, brush.Color);
            return;
        }

        if (radiusX <= 0 || radiusY <= 0)
        {
            FillRectangle(commands, rect, brush);
            return;
        }

        PathBuilder path = new();
        BuildRoundedRect(path, rect, radiusX, radiusY);
        List<Point> contour = [];
        path.Flatten(FlattenTolerance, contour);
        FillContour(commands, CollectionsMarshal.AsSpan(contour), rect, brush);
    }

    private static void FillEllipseGeometry(IRasterCommandList commands, Point center, double radiusX, double radiusY, ColorRgba color)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            return;
        }

        PathBuilder path = new();
        BuildEllipse(path, center, radiusX, radiusY);
        List<Point> contour = [];
        path.Flatten(FlattenTolerance, contour);
        FillTessellatedContour(commands, CollectionsMarshal.AsSpan(contour), color);
    }

    private void FillEllipseGeometry(IRasterCommandList commands, Point center, double radiusX, double radiusY, BrushPaint brush)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            return;
        }

        if (brush.Kind == BrushKind.Solid)
        {
            FillEllipseGeometry(commands, center, radiusX, radiusY, brush.Color);
            return;
        }

        PathBuilder path = new();
        BuildEllipse(path, center, radiusX, radiusY);
        List<Point> contour = [];
        path.Flatten(FlattenTolerance, contour);
        FillContour(commands, CollectionsMarshal.AsSpan(contour), new Rect(center.X - radiusX, center.Y - radiusY, radiusX * 2, radiusY * 2), brush);
    }

    /// <summary>Fills a rectangle with any brush kind. Gradient fills use the rect's own
    /// bounds as the mapping box; visual brushes stretch their content over it.</summary>
    private void FillRectangle(IRasterCommandList commands, Rect rectangle, BrushPaint brush)
    {
        if (brush.Kind == BrushKind.Solid)
        {
            commands.FillRectangle(rectangle, brush.Color);
            return;
        }

        if (brush.Kind == BrushKind.Visual)
        {
            DrawVisualBrushFill(commands, rectangle, brush);
            return;
        }

        if (brush.Kind == BrushKind.Image)
        {
            DrawImageBrushFill(commands, rectangle, brush);
            return;
        }

        var p0 = new Point(rectangle.X, rectangle.Y);
        var p1 = new Point(rectangle.Right, rectangle.Y);
        var p2 = new Point(rectangle.Right, rectangle.Bottom);
        var p3 = new Point(rectangle.X, rectangle.Bottom);
        Point[] triangles = [p0, p1, p2, p0, p2, p3];
        FillGradientTriangles(commands, triangles, rectangle, brush);
    }

    private static void FillTessellatedContour(IRasterCommandList commands, ReadOnlySpan<Point> contour, ColorRgba color)
    {
        if (contour.Length < 3)
        {
            return;
        }

        int required = Tessellator.FillRequired(contour, FillRule.EvenOdd);
        if (required <= 0)
        {
            return;
        }

        Point[] triangles = new Point[required];
        int written = Tessellator.Fill(contour, FillRule.EvenOdd, triangles);
        if (written > 0)
        {
            commands.FillTriangles(triangles.AsSpan(0, written), color);
        }
    }

    /// <summary>Tessellates a contour and fills it with any brush kind.</summary>
    private void FillContour(IRasterCommandList commands, ReadOnlySpan<Point> contour, Rect bounds, BrushPaint brush)
    {
        if (contour.Length < 3)
        {
            return;
        }

        if (brush.Kind == BrushKind.Solid)
        {
            FillTessellatedContour(commands, contour, brush.Color);
            return;
        }

        if (brush.Kind == BrushKind.Visual)
        {
            DrawVisualBrushFill(commands, bounds, brush);
            return;
        }

        if (brush.Kind == BrushKind.Image)
        {
            DrawImageBrushFill(commands, bounds, brush, contour);
            return;
        }

        int required = Tessellator.FillRequired(contour, FillRule.EvenOdd);
        if (required <= 0)
        {
            return;
        }

        Point[] triangles = new Point[required];
        int written = Tessellator.Fill(contour, FillRule.EvenOdd, triangles);
        if (written > 0)
        {
            FillGradientTriangles(commands, triangles.AsSpan(0, written), bounds, brush);
        }
    }

    private void FillGradientTriangles(IRasterCommandList commands, ReadOnlySpan<Point> vertices, Rect bounds, BrushPaint brush)
    {
        if (Presenter is null || !_resources.TryGetValue(brush.Handle.Value, out Slot? slot))
        {
            return;
        }

        if (slot.Kind is not (SlotKind.LinearGradientBrush or SlotKind.RadialGradientBrush))
        {
            return;
        }

        TextureHandle lut = EnsureGradientLut(slot, brush.Handle.Value);
        if (!lut.IsValid)
        {
            return;
        }

        GradientKind kind = slot.Kind == SlotKind.LinearGradientBrush ? GradientKind.Linear : GradientKind.Radial;
        Point[] coords = ComputeGradientCoords(vertices, bounds, slot);
        commands.FillGradientTriangles(vertices, coords, lut, kind, slot.SpreadMethod, WithOpacity(ColorRgba.White, brush.Opacity));
    }

    /// <summary>
    /// Bakes a gradient brush's stops into a 256x1 premultiplied RGBA8 LUT and uploads it as a
    /// texture (cached on the slot until the brush is redefined). Colors are interpolated in
    /// sRGB (WPF's default <c>ColorInterpolationMode.SRgbLinearInterpolation</c>).
    /// </summary>
    private TextureHandle EnsureGradientLut(Slot slot, uint brushHandle)
    {
        if (Presenter is null)
        {
            return TextureHandle.Invalid;
        }

        // Main+popup frames share ONE graph but each has its own Vulkan presenter; a gradient
        // LUT is a texture in the presenter's table, so the cache must be per-presenter (a
        // slot-cached handle created on the popup presenter is unknown to the main presenter).
        if (_gradientLuts.TryGetValue(Presenter, out Dictionary<uint, TextureHandle>? perPresenter)
            && perPresenter.TryGetValue(brushHandle, out TextureHandle cached))
        {
            return cached;
        }

        byte[] pixels = BakeGradientLut(slot.Stops);
        IGpuTexture texture = Presenter.CreateTexture(new TextureUpload(new PixelSize(GradientLutSize, 1), PixelFormat.Rgba8Unorm, pixels, GradientLutSize * 4));
        if (!_gradientLuts.TryGetValue(Presenter, out perPresenter))
        {
            perPresenter = [];
            _gradientLuts.Add(Presenter, perPresenter);
        }

        perPresenter[brushHandle] = texture.Handle;
        return texture.Handle;
    }

    private const int GradientLutSize = 256;

    private static byte[] BakeGradientLut(ReadOnlySpan<GradientStop> stops)
    {
        var lut = new byte[GradientLutSize * 4];
        if (stops.Length == 0)
        {
            return lut;
        }

        // Stop colors arrive as scRGB floats; convert to sRGB once so the interpolation
        // happens in sRGB space (the WPF SRgbLinearInterpolation default).
        var srgb = new (double Position, ColorRgba Color)[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            ColorRgba c = stops[i].Color;
            srgb[i] = (stops[i].Position, new ColorRgba(ScRgbToSrgb(c.R), ScRgbToSrgb(c.G), ScRgbToSrgb(c.B), c.A));
        }

        Array.Sort(srgb, (a, b) => a.Position.CompareTo(b.Position));
        for (int i = 0; i < GradientLutSize; i++)
        {
            double t = i / (double)(GradientLutSize - 1);
            ColorRgba color = SampleGradientStops(srgb, t);
            lut[(i * 4) + 0] = ToByte(color.R * color.A);
            lut[(i * 4) + 1] = ToByte(color.G * color.A);
            lut[(i * 4) + 2] = ToByte(color.B * color.A);
            lut[(i * 4) + 3] = ToByte(color.A);
        }

        return lut;
    }

    private static ColorRgba SampleGradientStops(ReadOnlySpan<(double Position, ColorRgba Color)> stops, double t)
    {
        if (t <= stops[0].Position)
        {
            return stops[0].Color;
        }

        if (t >= stops[^1].Position)
        {
            return stops[^1].Color;
        }

        for (int i = 1; i < stops.Length; i++)
        {
            double hi = stops[i].Position;
            if (t > hi)
            {
                continue;
            }

            double lo = stops[i - 1].Position;
            double span = hi - lo;
            double f = span > 0 ? (t - lo) / span : 0;
            (double _, ColorRgba loColor) = stops[i - 1];
            (double _, ColorRgba hiColor) = stops[i];
            return new ColorRgba(
                (float)Lerp(loColor.R, hiColor.R, f),
                (float)Lerp(loColor.G, hiColor.G, f),
                (float)Lerp(loColor.B, hiColor.B, f),
                (float)Lerp(loColor.A, hiColor.A, f));
        }

        return stops[^1].Color;
    }

    private static float ScRgbToSrgb(float v)
    {
        float clamped = Math.Clamp(v, 0.0f, 1.0f);
        return clamped <= 0.0031308f ? clamped * 12.92f : (1.055f * MathF.Pow(clamped, 1.0f / 2.4f)) - 0.055f;
    }

    /// <summary>
    /// Converts an scRGB (linear) color to sRGB-encoded components, leaving alpha untouched.
    /// The raster targets are UNORM images with no automatic decode, so every color written
    /// to them must be sRGB-encoded to display correctly. This mirrors the per-stop
    /// conversion the gradient LUT bake applies (see <see cref="EnsureGradientLut"/>), so
    /// solid fills, pens, glyph tints, and clear colors match gradients byte-for-byte for
    /// the same source color.
    /// </summary>
    private static ColorRgba SrgbEncode(ColorRgba color)
    {
        return new ColorRgba(ScRgbToSrgb(color.R), ScRgbToSrgb(color.G), ScRgbToSrgb(color.B), color.A);
    }

    private static double Lerp(double a, double b, double f)
    {
        return a + ((b - a) * f);
    }

    private static byte ToByte(float v)
    {
        return (byte)Math.Round(Math.Clamp(v, 0.0f, 1.0f) * 255.0f);
    }

    /// <summary>
    /// Computes one gradient-space coordinate per vertex: for linear gradients the
    /// normalized projection onto the start-&gt;end axis (unfolded; the shader folds the
    /// spread), for radial gradients the position offset from the gradient origin scaled
    /// by 1/radius (the shader takes its length). The mapping-mode bounding box is
    /// <paramref name="bounds"/>. The brush transform (if any) maps the gradient geometry.
    /// </summary>
    private Point[] ComputeGradientCoords(ReadOnlySpan<Point> vertices, Rect bounds, Slot brush)
    {
        var coords = new Point[vertices.Length];
        if (brush.Kind == SlotKind.LinearGradientBrush)
        {
            (Point start, Point end) = ResolveLinearAxis(brush, bounds);
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double len2 = (dx * dx) + (dy * dy);
            for (int i = 0; i < vertices.Length; i++)
            {
                double t = len2 > 0 ? (((vertices[i].X - start.X) * dx) + ((vertices[i].Y - start.Y) * dy)) / len2 : 0;
                coords[i] = new Point(t, 0);
            }
        }
        else
        {
            (Point origin, double rx, double ry) = ResolveRadial(brush, bounds);
            for (int i = 0; i < vertices.Length; i++)
            {
                double gx = rx > 0 ? (vertices[i].X - origin.X) / rx : 0;
                double gy = ry > 0 ? (vertices[i].Y - origin.Y) / ry : 0;
                coords[i] = new Point(gx, gy);
            }
        }

        return coords;
    }

    private (Point Start, Point End) ResolveLinearAxis(Slot brush, Rect bounds)
    {
        Point start = brush.Start;
        Point end = brush.End;
        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox)
        {
            start = MapRelative(start, bounds);
            end = MapRelative(end, bounds);
        }

        Matrix3x2 relative = ResolveTransform(brush.RelativeTransform);
        if (!relative.IsIdentity)
        {
            // The relative transform operates on the brush content mapped into the 0-1 box
            // over the painted bounds (WPF GradientBrush.RelativeTransform semantics).
            start = ApplyRelativeTransform(start, bounds, relative);
            end = ApplyRelativeTransform(end, bounds, relative);
        }

        Matrix3x2 transform = ResolveTransform(brush.Transform);
        if (!transform.IsIdentity)
        {
            start = transform.Transform(start);
            end = transform.Transform(end);
        }

        return (start, end);
    }

    private (Point Origin, double RadiusX, double RadiusY) ResolveRadial(Slot brush, Rect bounds)
    {
        Point origin = brush.GradientOrigin;
        double radiusX = brush.RadiusX;
        double radiusY = brush.RadiusY;
        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox)
        {
            origin = MapRelative(origin, bounds);
            radiusX *= bounds.Width;
            radiusY *= bounds.Height;
        }

        Matrix3x2 relative = ResolveTransform(brush.RelativeTransform);
        if (!relative.IsIdentity)
        {
            origin = ApplyRelativeTransform(origin, bounds, relative);
            radiusX *= Length(relative, new Point(1, 0));
            radiusY *= Length(relative, new Point(0, 1));
        }

        Matrix3x2 transform = ResolveTransform(brush.Transform);
        if (!transform.IsIdentity)
        {
            origin = transform.Transform(origin);
        }

        return (origin, radiusX, radiusY);
    }

    private static Point ApplyRelativeTransform(Point point, Rect bounds, Matrix3x2 relative)
    {
        Point p = new(
            bounds.Width > 0 ? (point.X - bounds.X) / bounds.Width : 0,
            bounds.Height > 0 ? (point.Y - bounds.Y) / bounds.Height : 0);
        p = relative.Transform(p);
        return new Point((p.X * bounds.Width) + bounds.X, (p.Y * bounds.Height) + bounds.Y);
    }

    private static double Length(Matrix3x2 matrix, Point vector)
    {
        Point transformed = matrix.Transform(vector);
        return Math.Sqrt((transformed.X * transformed.X) + (transformed.Y * transformed.Y));
    }

    private static Point MapRelative(Point relative, Rect bounds)
    {
        return new Point(bounds.X + (relative.X * bounds.Width), bounds.Y + (relative.Y * bounds.Height));
    }

    /// <summary>
    /// Paints a rectangle with an ImageBrush: the referenced BitmapSource's decoded pixels are
    /// uploaded once as a premultiplied-RGBA texture (cached on the brush slot) and the viewbox
    /// content is mapped onto the target rectangle honoring Stretch and TileMode — the same
    /// tile-brush semantics as <see cref="DrawVisualBrushFill"/>, but sourced from a bitmap
    /// instead of a rendered visual.
    /// </summary>
    private void DrawImageBrushFill(IRasterCommandList commands, Rect target, BrushPaint brush, ReadOnlySpan<Point> contour = default)
    {
        if (Presenter is null ||
            !_resources.TryGetValue(brush.Handle.Value, out Slot? slot) ||
            slot.Kind != SlotKind.ImageBrush)
        {
            return;
        }

        // Resolve the brush's bitmap source resource slot.
        Slot? sourceSlot = null;
        if (!slot.ImageSource.IsNull)
        {
            _ = _resources.TryGetValue(slot.ImageSource.Value, out sourceSlot);
        }

        if (slot.ImageSource.IsNull || sourceSlot is null ||
            sourceSlot.Kind != SlotKind.BitmapSource ||
            sourceSlot.Bitmap is null)
        {
            return;
        }

        TextureHandle texture = EnsureBitmapTexture(slot.ImageSource.Value, sourceSlot);
        if (!texture.IsValid || !TryGetTextureSize(Presenter, texture.Value, out PixelSize contentSize))
        {
            return;
        }

        Rect source = ResolveViewbox(slot, contentSize);
        if (source.IsEmpty)
        {
            return;
        }

        Rect baseTile = ResolveViewport(slot, target);
        ColorRgba tint = WithOpacity(ColorRgba.White, brush.Opacity);

        if (slot.TileMode == TileMode.None)
        {
            if (TryComputeTileQuad(baseTile, source, target, contentSize, slot.Stretch, out Point p0, out Point p1, out Point p2, out Point p3, out Point uv0, out Point uv1, out Point uv2, out Point uv3))
            {
                // A contour (rounded rect, ellipse, path geometry) clips the brush: the
                // quad alone would paint the sharp target rectangle over the rounded
                // geometry. Tessellate the contour and carry the quad's affine UV map to
                // each vertex, so the pixels land exactly where the quad would put them.
                if (contour.Length >= 3)
                {
                    DrawImageBrushContoured(commands, contour, texture, p0, uv0, p1, uv1, p3, uv3, tint);
                    return;
                }

                commands.DrawTexturedQuad(p0, p1, p2, p3, texture, uv0, uv1, uv2, uv3, tint);
            }

            return;
        }

        // Tiled: emit one quad per tile covering the target, alternating flips per row/column.
        double tileW = baseTile.Width > 0 ? baseTile.Width : target.Width;
        double tileH = baseTile.Height > 0 ? baseTile.Height : target.Height;
        if (tileW <= 0 || tileH <= 0)
        {
            return;
        }

        int firstCol = (int)Math.Floor(target.Left / tileW);
        int firstRow = (int)Math.Floor(target.Top / tileH);
        int lastCol = (int)Math.Ceiling(target.Right / tileW) - 1;
        int lastRow = (int)Math.Ceiling(target.Bottom / tileH) - 1;
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = firstCol; col <= lastCol; col++)
            {
                var tile = new Rect(col * tileW, row * tileH, tileW, tileH);
                Rect clipped = Intersect(target, tile);
                if (clipped.IsEmpty)
                {
                    continue;
                }

                bool flipX = (slot.TileMode == TileMode.FlipX || slot.TileMode == TileMode.FlipXY) && (col & 1) != 0;
                bool flipY = (slot.TileMode == TileMode.FlipY || slot.TileMode == TileMode.FlipXY) && (row & 1) != 0;
                Point uv0 = new(flipX ? 1 : 0, flipY ? 1 : 0);
                Point uv1 = new(flipX ? 0 : 1, flipY ? 1 : 0);
                Point uv3 = new(flipX ? 1 : 0, flipY ? 0 : 1);
                double u0 = (clipped.Left - tile.Left) / tileW;
                double u1 = (clipped.Right - tile.Left) / tileW;
                double v0 = (clipped.Top - tile.Top) / tileH;
                double v1 = (clipped.Bottom - tile.Top) / tileH;
                double left = Lerp(uv0.X, uv1.X, u0);
                double right = Lerp(uv0.X, uv1.X, u1);
                double top = Lerp(uv0.Y, uv3.Y, v0);
                double bottom = Lerp(uv0.Y, uv3.Y, v1);
                commands.DrawTexturedQuad(
                    new Point(clipped.Left, clipped.Top),
                    new Point(clipped.Right, clipped.Top),
                    new Point(clipped.Right, clipped.Bottom),
                    new Point(clipped.Left, clipped.Bottom),
                    texture,
                    new Point(left, top),
                    new Point(right, top),
                    new Point(right, bottom),
                    new Point(left, bottom),
                    tint);
            }
        }
    }

    /// <summary>
    /// Fills a tessellated contour with an ImageBrush's texture. The brush mapping is the
    /// same affine as the single-quad path (its corners <paramref name="p0"/>/<paramref name="p1"/>/
    /// <paramref name="p3"/> and UV corners <paramref name="uv0"/>/<paramref name="uv1"/>/
    /// <paramref name="uv3"/> define it), so each triangle vertex samples exactly where the
    /// equivalent quad would: the contour clips the brush without moving its pixels.
    /// </summary>
    private static void DrawImageBrushContoured(
        IRasterCommandList commands,
        ReadOnlySpan<Point> contour,
        TextureHandle texture,
        Point p0,
        Point uv0,
        Point p1,
        Point uv1,
        Point p3,
        Point uv3,
        ColorRgba tint)
    {
        int required = Tessellator.FillRequired(contour, FillRule.EvenOdd);
        if (required <= 0)
        {
            return;
        }

        Point[] triangles = new Point[required];
        int written = Tessellator.Fill(contour, FillRule.EvenOdd, triangles);
        if (written <= 0)
        {
            return;
        }

        double destWidth = p1.X - p0.X;
        double destHeight = p3.Y - p0.Y;
        if (destWidth <= 0 || destHeight <= 0)
        {
            return;
        }

        double duDx = (uv1.X - uv0.X) / destWidth;
        double dvDy = (uv3.Y - uv0.Y) / destHeight;
        Point[] uvs = new Point[written];
        for (int i = 0; i < written; i++)
        {
            uvs[i] = new Point(
                uv0.X + ((triangles[i].X - p0.X) * duDx),
                uv0.Y + ((triangles[i].Y - p0.Y) * dvDy));
        }

        commands.DrawTexturedTriangles(triangles.AsSpan(0, written), uvs, texture, tint);
    }

    /// <summary>
    /// Uploads the decoded bitmap pixels of a BitmapSource slot as a premultiplied-RGBA
    /// texture, cached per (presenter, slot): the graph is shared by main+popup frames and
    /// transient offscreen effect/VisualBrush targets, each with its own Vulkan presenter, so
    /// a handle cached on a different presenter is unknown to this one. The straight Bgra32
    /// backing store is premultiplied into a pooled buffer once, then copied to the GPU
    /// staging buffer — the <c>Image&lt;Bgra32&gt;</c> stays alive as the slot's backing
    /// store, so there is no detached per-frame managed array.
    /// </summary>
    private TextureHandle EnsureBitmapTexture(uint slotHandle, Slot slot)
    {
        IVulkanPresenter? presenter = Presenter;
        if (presenter is null || slot.Bitmap is null)
        {
            return TextureHandle.Invalid;
        }

        if (_bitmapTextures.TryGetValue(presenter, out Dictionary<uint, TextureHandle>? perPresenter)
            && perPresenter.TryGetValue(slotHandle, out TextureHandle cached))
        {
            return cached;
        }

        var size = new PixelSize(slot.Bitmap.PixelWidth, slot.Bitmap.PixelHeight);
        if (size.IsEmpty)
        {
            return TextureHandle.Invalid;
        }

        int stride = size.Width * 4;
        int byteCount = stride * size.Height;
        byte[] pooled = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            slot.Bitmap.CopyPixels(0, 0, size.Width, size.Height, WicPixelFormat.Prgba32, pooled.AsSpan(0, byteCount), stride);
            IGpuTexture texture = presenter.CreateTexture(new TextureUpload(size, PixelFormat.Rgba8Unorm, pooled.AsSpan(0, byteCount), stride));
            if (perPresenter is null)
            {
                perPresenter = [];
                _bitmapTextures.Add(presenter, perPresenter);
            }

            perPresenter[slotHandle] = texture.Handle;
            SetTextureSize(presenter, texture.Handle.Value, size);
            return texture.Handle;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pooled);
        }
    }

    /// <summary>
    /// Paints a rectangle with a VisualBrush: the referenced visual is rendered once to an
    /// offscreen presenter, read back, and uploaded as a texture; the viewbox content is
    /// then mapped onto the target rectangle honoring Stretch (Fill/None/Uniform) and
    /// TileMode (repeating base tiles with optional flips).
    /// </summary>
    private void DrawVisualBrushFill(IRasterCommandList commands, Rect target, BrushPaint brush)
    {
        if (Presenter is null || OffscreenFactory is null ||
            !_resources.TryGetValue(brush.Handle.Value, out Slot? slot) ||
            slot.Kind != SlotKind.VisualBrush)
        {
            return;
        }

        TextureHandle texture = EnsureVisualBrushTexture(slot, brush.Handle.Value);
        if (!texture.IsValid || !TryGetTextureSize(Presenter, texture.Value, out PixelSize contentSize))
        {
            return;
        }

        Rect source = ResolveViewbox(slot, contentSize);
        if (source.IsEmpty)
        {
            return;
        }

        Rect baseTile = ResolveViewport(slot, target);
        ColorRgba tint = WithOpacity(ColorRgba.White, brush.Opacity);

        if (slot.TileMode == TileMode.None)
        {
            if (TryComputeTileQuad(baseTile, source, target, contentSize, slot.Stretch, out Point p0, out Point p1, out Point p2, out Point p3, out Point uv0, out Point uv1, out Point uv2, out Point uv3))
            {
                commands.DrawTexturedQuad(p0, p1, p2, p3, texture, uv0, uv1, uv2, uv3, tint);
            }

            return;
        }

        // Tiled: emit one quad per tile covering the target, alternating flips per row/column.
        double tileW = baseTile.Width > 0 ? baseTile.Width : target.Width;
        double tileH = baseTile.Height > 0 ? baseTile.Height : target.Height;
        if (tileW <= 0 || tileH <= 0)
        {
            return;
        }

        int firstCol = (int)Math.Floor(target.Left / tileW);
        int firstRow = (int)Math.Floor(target.Top / tileH);
        int lastCol = (int)Math.Ceiling(target.Right / tileW) - 1;
        int lastRow = (int)Math.Ceiling(target.Bottom / tileH) - 1;
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = firstCol; col <= lastCol; col++)
            {
                var tile = new Rect(col * tileW, row * tileH, tileW, tileH);
                Rect clipped = Intersect(target, tile);
                if (clipped.IsEmpty)
                {
                    continue;
                }

                bool flipX = (slot.TileMode == TileMode.FlipX || slot.TileMode == TileMode.FlipXY) && (col & 1) != 0;
                bool flipY = (slot.TileMode == TileMode.FlipY || slot.TileMode == TileMode.FlipXY) && (row & 1) != 0;
                Point uv0 = new(flipX ? 1 : 0, flipY ? 1 : 0);
                Point uv1 = new(flipX ? 0 : 1, flipY ? 1 : 0);
                Point uv3 = new(flipX ? 1 : 0, flipY ? 0 : 1);
                // Sample only the part of the tile that lies inside the target. The clipped
                // sub-rectangle is axis-aligned, so its UVs are separable bilinear corners.
                double u0 = (clipped.Left - tile.Left) / tileW;
                double u1 = (clipped.Right - tile.Left) / tileW;
                double v0 = (clipped.Top - tile.Top) / tileH;
                double v1 = (clipped.Bottom - tile.Top) / tileH;
                double left = Lerp(uv0.X, uv1.X, u0);
                double right = Lerp(uv0.X, uv1.X, u1);
                double top = Lerp(uv0.Y, uv3.Y, v0);
                double bottom = Lerp(uv0.Y, uv3.Y, v1);
                commands.DrawTexturedQuad(
                    new Point(clipped.Left, clipped.Top),
                    new Point(clipped.Right, clipped.Top),
                    new Point(clipped.Right, clipped.Bottom),
                    new Point(clipped.Left, clipped.Bottom),
                    texture,
                    new Point(left, top),
                    new Point(right, top),
                    new Point(right, bottom),
                    new Point(left, bottom),
                    tint);
            }
        }
    }

    /// <summary>
    /// Maps one viewbox tile onto the target rectangle with the given stretch and returns the
    /// destination quad + UVs. <paramref name="source"/> is in texture pixel space.
    /// </summary>
    private static bool TryComputeTileQuad(
        Rect baseTile,
        Rect source,
        Rect target,
        PixelSize contentSize,
        Stretch stretch,
        out Point p0,
        out Point p1,
        out Point p2,
        out Point p3,
        out Point uv0,
        out Point uv1,
        out Point uv2,
        out Point uv3)
    {
        p0 = p1 = p2 = p3 = uv0 = uv1 = uv2 = uv3 = default;
        if (baseTile.IsEmpty || source.IsEmpty || source.Width <= 0 || source.Height <= 0 ||
            contentSize.Width <= 0 || contentSize.Height <= 0)
        {
            return false;
        }

        double tileW = baseTile.Width;
        double tileH = baseTile.Height;
        Rect dest = baseTile;
        if (stretch == Stretch.Fill)
        {
            dest = target;
        }
        else if (stretch == Stretch.Uniform)
        {
            double scale = Math.Min(target.Width / tileW, target.Height / tileH);
            dest = new Rect(
                target.X + ((target.Width - (tileW * scale)) * 0.5),
                target.Y + ((target.Height - (tileH * scale)) * 0.5),
                tileW * scale,
                tileH * scale);
        }
        else if (stretch == Stretch.UniformToFill)
        {
            double scale = Math.Max(target.Width / tileW, target.Height / tileH);
            dest = new Rect(
                target.X + ((target.Width - (tileW * scale)) * 0.5),
                target.Y + ((target.Height - (tileH * scale)) * 0.5),
                tileW * scale,
                tileH * scale);
        }

        p0 = new Point(dest.X, dest.Y);
        p1 = new Point(dest.Right, dest.Y);
        p2 = new Point(dest.Right, dest.Bottom);
        p3 = new Point(dest.X, dest.Bottom);
        // UVs are texture-relative: `source` is the viewbox in texture-pixel space, so
        // normalize by the TEXTURE size, not the destination base-tile size. (The old code
        // divided by baseTile.Width/Height, which sampled only the top-left corner of the
        // texture when the destination was larger than the source.)
        double su0 = source.X / contentSize.Width;
        double su1 = source.Right / contentSize.Width;
        double sv0 = source.Y / contentSize.Height;
        double sv1 = source.Bottom / contentSize.Height;
        uv0 = new Point(su0, sv0);
        uv1 = new Point(su1, sv0);
        uv2 = new Point(su1, sv1);
        uv3 = new Point(su0, sv1);
        return true;
    }

    private static Rect ResolveViewbox(Slot slot, PixelSize contentSize)
    {
        return slot.Viewbox.IsEmpty
            ? new Rect(0, 0, contentSize.Width, contentSize.Height)
            : slot.ViewboxUnits == BrushMappingMode.RelativeToBoundingBox
                ? new Rect(
                    slot.Viewbox.X * contentSize.Width,
                    slot.Viewbox.Y * contentSize.Height,
                    slot.Viewbox.Width * contentSize.Width,
                    slot.Viewbox.Height * contentSize.Height)
                : slot.Viewbox;
    }

    private static Rect ResolveViewport(Slot slot, Rect target)
    {
        return slot.Viewport.IsEmpty
            ? new Rect(0, 0, target.Width, target.Height)
            : slot.ViewportUnits == BrushMappingMode.RelativeToBoundingBox
                ? new Rect(
                    slot.Viewport.X * target.Width,
                    slot.Viewport.Y * target.Height,
                    slot.Viewport.Width * target.Width,
                    slot.Viewport.Height * target.Height)
                : slot.Viewport;
    }

    /// <summary>
    /// Renders the VisualBrush's visual to an offscreen presenter once and uploads the result
    /// as a texture (cached on the slot until the brush is redefined). The offscreen target is
    /// sized to the visual's content bounds and the content is translated so its top-left
    /// corner lands at the texture origin.
    /// </summary>
    private TextureHandle EnsureVisualBrushTexture(Slot slot, uint brushHandle)
    {
        if (Presenter is null || OffscreenFactory is null || slot.Visual.IsNull)
        {
            return TextureHandle.Invalid;
        }

        // Per-presenter cache: the graph is shared by main+popup frames, each with its own
        // Vulkan presenter, so a texture cached on a different presenter is unknown here.
        if (_visualTextures.TryGetValue(Presenter, out Dictionary<uint, (TextureHandle Texture, PixelSize Size)>? perPresenter)
            && perPresenter.TryGetValue(brushHandle, out (TextureHandle Texture, PixelSize Size) cached))
        {
            SetTextureSize(Presenter, cached.Texture.Value, cached.Size);
            return cached.Texture;
        }

        // A VisualBrush whose own visual paints the same brush would recurse forever;
        // degrade to no pixels for the nested instance instead of overflowing the stack.
        if (slot.VisualTextureRendering)
        {
            return TextureHandle.Invalid;
        }

        Rect? content = MeasureVisualBounds(slot.Visual);
        if (content is not { } bounds || bounds.IsEmpty)
        {
            return TextureHandle.Invalid;
        }

        var size = new PixelSize((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
        if (size.Width <= 0 || size.Height <= 0)
        {
            return TextureHandle.Invalid;
        }

        slot.VisualTextureRendering = true;
        try
        {
            IVulkanPresenter offscreen = OffscreenFactory(size);
            using (offscreen)
            {
                // The visual may paint glyph runs; give the offscreen presenter its own atlas so
                // those uploads land in its texture table (the frame atlas belongs to the frame
                // presenter). Nested effect uploads follow the active presenter too.
                using var atlas = new GlyphAtlas(offscreen, new PixelSize(512, 512));
                IVulkanPresenter? savedPresenter = Presenter;
                Presenter = offscreen;
                try
                {
                    offscreen.Render(commands =>
                    {
                        commands.Clear(ColorRgba.Transparent);
                        commands.PushTransform(Matrix3x2.Translate(-bounds.Left, -bounds.Top));
                        RasterizeVisualInto(commands, atlas, slot.Visual);
                        commands.PopTransform();
                    });
                }
                finally
                {
                    Presenter = savedPresenter;
                }

                ReadOnlyMemory<byte> pixels = offscreen.ReadbackRgba();
                IGpuTexture texture = Presenter.CreateTexture(new TextureUpload(size, PixelFormat.Rgba8Unorm, pixels.Span, size.Width * 4));
                SetTextureSize(Presenter, texture.Handle.Value, size);
                if (!_visualTextures.TryGetValue(Presenter, out perPresenter))
                {
                    perPresenter = [];
                    _visualTextures.Add(Presenter, perPresenter);
                }

                perPresenter[brushHandle] = (texture.Handle, size);

                // Everything the nested walk cached on the offscreen presenter dies with it.
                ForgetPresenter(offscreen);
                return texture.Handle;
            }
        }
        finally
        {
            slot.VisualTextureRendering = false;
        }
    }

    /// <summary>Bitmap textures cached per (presenter, BitmapSource slot), same cross-presenter
    /// rationale as <see cref="_gradientLuts"/>: a slot drawn inside an offscreen effect or
    /// VisualBrush walk is uploaded on that transient presenter, and its handle means nothing
    /// to any other presenter (the frame's own walk re-uploads the bitmap).</summary>
    private readonly Dictionary<IVulkanPresenter, Dictionary<uint, TextureHandle>> _bitmapTextures = [];

    /// <summary>Texture pixel sizes keyed per (presenter, texture handle): handle numbering
    /// restarts on each presenter, so a raw handle means a different texture on a different
    /// presenter and the size lookup must be scoped to the owner.</summary>
    private readonly Dictionary<IVulkanPresenter, Dictionary<uint, PixelSize>> _textureSizes = [];

    /// <summary>Walks a visual into a different command list (VisualBrush offscreen target).</summary>
    private void RasterizeVisualInto(IRasterCommandList commands, GlyphAtlas? atlas, ResourceHandle visualHandle)
    {
        IRasterCommandList? savedCommands = _commands;
        GlyphAtlas? savedAtlas = _atlas;
        int savedDepth = _popStack.Count;
        _commands = commands;
        _atlas = atlas;
        try
        {
            WalkVisual(visualHandle);
        }
        finally
        {
            while (_popStack.Count > savedDepth)
            {
                _ = _popStack.Pop();
            }

            _commands = savedCommands;
            _atlas = savedAtlas;
        }
    }

    /// <summary>
    /// Measures the content bounds of a visual (its RenderData content and children) in the
    /// visual's own coordinate space. Used to size VisualBrush offscreen targets.
    /// </summary>
    private Rect? MeasureVisualBounds(ResourceHandle handle)
    {
        if (!_resources.TryGetValue(handle.Value, out Slot? visual) || visual.Kind != SlotKind.Visual)
        {
            return null;
        }

        _measuring = true;
        _measureBounds = null;
        try
        {
            MeasureVisualContent(visual);
            return _measureBounds;
        }
        finally
        {
            _measuring = false;
        }
    }

    private void MeasureVisualContent(Slot visual)
    {
        if (!visual.Content.IsNull && _resources.TryGetValue(visual.Content.Value, out Slot? content) && content.Kind == SlotKind.RenderData)
        {
            MilCommandParser.ParseRenderData(content.Blob, content.Dependents, this);
        }

        foreach (ResourceHandle child in visual.Children)
        {
            if (_resources.TryGetValue(child.Value, out Slot? childVisual) && childVisual.Kind == SlotKind.Visual)
            {
                MeasureVisualContent(childVisual);
            }
        }
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        double left = Math.Max(a.Left, b.Left);
        double top = Math.Max(a.Top, b.Top);
        double right = Math.Min(a.Right, b.Right);
        double bottom = Math.Min(a.Bottom, b.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static void BuildRoundedRect(PathBuilder path, Rect rect, double radiusX, double radiusY)
    {
        double x = rect.X;
        double y = rect.Y;
        double right = rect.Right;
        double bottom = rect.Bottom;
        double rx = Math.Min(Math.Max(0, radiusX), rect.Width * 0.5);
        double ry = Math.Min(Math.Max(0, radiusY), rect.Height * 0.5);
        if (rx <= 0 || ry <= 0)
        {
            path.MoveTo(new Point(x, y));
            path.LineTo(new Point(right, y));
            path.LineTo(new Point(right, bottom));
            path.LineTo(new Point(x, bottom));
            path.Close();
            return;
        }

        double arcControlX = rx * (1 - BezierKappa);
        double arcControlY = ry * (1 - BezierKappa);
        path.MoveTo(new Point(x + rx, y));
        path.LineTo(new Point(right - rx, y));
        path.CubicTo(
            new Point(right - arcControlX, y),
            new Point(right, y + arcControlY),
            new Point(right, y + ry));
        path.LineTo(new Point(right, bottom - ry));
        path.CubicTo(
            new Point(right, bottom - arcControlY),
            new Point(right - arcControlX, bottom),
            new Point(right - rx, bottom));
        path.LineTo(new Point(x + rx, bottom));
        path.CubicTo(
            new Point(x + arcControlX, bottom),
            new Point(x, bottom - arcControlY),
            new Point(x, bottom - ry));
        path.LineTo(new Point(x, y + ry));
        path.CubicTo(
            new Point(x, y + arcControlY),
            new Point(x + arcControlX, y),
            new Point(x + rx, y));
        path.Close();
    }

    private static void BuildEllipse(PathBuilder path, Point center, double radiusX, double radiusY)
    {
        double cx = center.X;
        double cy = center.Y;
        double controlX = radiusX * BezierKappa;
        double controlY = radiusY * BezierKappa;
        path.MoveTo(new Point(cx + radiusX, cy));
        path.CubicTo(
            new Point(cx + radiusX, cy + controlY),
            new Point(cx + controlX, cy + radiusY),
            new Point(cx, cy + radiusY));
        path.CubicTo(
            new Point(cx - controlX, cy + radiusY),
            new Point(cx - radiusX, cy + controlY),
            new Point(cx - radiusX, cy));
        path.CubicTo(
            new Point(cx - radiusX, cy - controlY),
            new Point(cx - controlX, cy - radiusY),
            new Point(cx, cy - radiusY));
        path.CubicTo(
            new Point(cx + controlX, cy - radiusY),
            new Point(cx + radiusX, cy - controlY),
            new Point(cx + radiusX, cy));
        path.Close();
    }

    private static void StrokeAxisAlignedRect(IRasterCommandList commands, Rect rect, double thickness, ColorRgba color)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        double half = thickness * 0.5;
        commands.FillRectangle(new Rect(rect.X - half, rect.Y - half, rect.Width + thickness, thickness), color);
        commands.FillRectangle(new Rect(rect.X - half, rect.Bottom - half, rect.Width + thickness, thickness), color);
        commands.FillRectangle(new Rect(rect.X - half, rect.Y + half, thickness, Math.Max(0, rect.Height - thickness)), color);
        commands.FillRectangle(new Rect(rect.Right - half, rect.Y + half, thickness, Math.Max(0, rect.Height - thickness)), color);
    }

    private static void StrokeLine(IRasterCommandList commands, Point start, Point endPoint, double thickness, ColorRgba color)
    {
        double dx = endPoint.X - start.X;
        double dy = endPoint.Y - start.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0)
        {
            return;
        }

        double half = thickness * 0.5;
        double scale = half / length;
        double px = -dy * scale;
        double py = dx * scale;
        commands.FillQuad(
            new Point(start.X + px, start.Y + py),
            new Point(endPoint.X + px, endPoint.Y + py),
            new Point(endPoint.X - px, endPoint.Y - py),
            new Point(start.X - px, start.Y - py),
            color);
    }

    private static bool TransformContains(Matrix3x2 transform, Rect bounds, Point point)
    {
        Point p0 = transform.Transform(new Point(bounds.Left, bounds.Top));
        Point p1 = transform.Transform(new Point(bounds.Right, bounds.Top));
        Point p2 = transform.Transform(new Point(bounds.Right, bounds.Bottom));
        Point p3 = transform.Transform(new Point(bounds.Left, bounds.Bottom));
        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return point.X >= minX && point.X < maxX && point.Y >= minY && point.Y < maxY;
    }

    private static Rect Union(Rect a, Rect b)
    {
        double left = Math.Min(a.Left, b.Left);
        double top = Math.Min(a.Top, b.Top);
        double right = Math.Max(a.Right, b.Right);
        double bottom = Math.Max(a.Bottom, b.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static ColorRgba WithOpacity(ColorRgba color, double opacity)
    {
        double alpha = Math.Clamp(color.A * opacity, 0.0, 1.0);
        return new ColorRgba(color.R, color.G, color.B, (float)alpha);
    }

    private static Point UvTopLeft(Rect uv)
    {
        return new Point(uv.X, uv.Y);
    }

    private static Point UvTopRight(Rect uv)
    {
        return new Point(uv.X + uv.Width, uv.Y);
    }

    private static Point UvBottomRight(Rect uv)
    {
        return new Point(uv.X + uv.Width, uv.Y + uv.Height);
    }

    private static Point UvBottomLeft(Rect uv)
    {
        return new Point(uv.X, uv.Y + uv.Height);
    }

    private enum PopKind
    {
        Clip,
        Opacity,
        Transform,
        Guideline,
        OpacityMask,
        Effect
    }

    private enum SlotKind
    {
        Unknown,
        Visual,
        SolidColorBrush,
        LinearGradientBrush,
        RadialGradientBrush,
        VisualBrush,
        ImageBrush,
        TranslateTransform,
        ScaleTransform,
        RotateTransform,
        MatrixTransform,
        TransformGroup,
        LineGeometry,
        RectangleGeometry,
        EllipseGeometry,
        PathGeometry,
        Pen,
        GlyphRun,
        RenderData,
        BitmapSource,
        Drawing,
        BlurEffect,
        DropShadowEffect
    }

    private sealed class Slot
    {
        public SlotKind Kind = SlotKind.Unknown;
        public Point Offset;
        public ResourceHandle Transform;
        public ResourceHandle RelativeTransform;
        public ResourceHandle Clip;
        public double Alpha = 1.0;
        public ResourceHandle Content;
        public readonly List<ResourceHandle> Children = [];
        public double Opacity = 1.0;
        public ColorRgba Color;
        public double X;
        public double Y;
        public double ScaleX;
        public double ScaleY;
        public double CenterX;
        public double CenterY;
        public double Angle;
        public Matrix3x2 Matrix;
        public ResourceHandle[] TransformChildren = [];
        public byte[] PathData = [];
        public Rect Rectangle;
        public double RadiusX;
        public double RadiusY;
        public Point Start;
        public Point End;
        public Point Center;
        public Point GradientOrigin;
        public double Thickness;
        public ResourceHandle Brush;
        public FontFaceToken Font;
        public Point Origin;
        public float EmSize;
        public ushort[] Glyphs = [];
        public float[] Advances = [];
        public byte[] Blob = [];
        public ResourceHandle[] Dependents = [];
        public GradientStop[] Stops = [];
        public BrushMappingMode MappingMode;
        public GradientSpreadMethod SpreadMethod;
        public Rect Viewport;
        public Rect Viewbox;
        public BrushMappingMode ViewportUnits;
        public BrushMappingMode ViewboxUnits;
        public Stretch Stretch;
        public TileMode TileMode;
        public ResourceHandle Visual;
        public bool VisualTextureRendering;
        public ResourceHandle Effect;
        public ResourceHandle OpacityMask;
        public double EffectShadowDepth;
        public ColorRgba EffectColor;
        public double EffectDirection;
        public double EffectOpacity;
        public double EffectBlurRadius;
        public double EffectRadius;
        public int EffectKernelType;
        public int EffectRenderingBias;

        /// <summary>Bumped by any render-affecting change to this slot (brushes, transforms,
        /// geometries, pens, glyph runs, effect params, render-data blobs, and for visuals
        /// also offset/transform/alpha). Feeds the effect-composite cache key.</summary>
        public ulong Version;

        /// <summary>Visual only: bumped by subtree-wiring changes (SetContent, SetEffect,
        /// SetAlphaMask, child insert/remove). The effect composite is rendered in the visual's
        /// LOCAL space, so the visual's own layout changes (offset/transform/alpha, tracked by
        /// <see cref="Version"/>) do NOT invalidate the cached composite — they are applied by
        /// the parent's command stack at draw time.</summary>
        public ulong ContentVersion;

        // Effect-composite cache state lives per (presenter, visual) in SlaveGraph._effectCaches
        // (same cross-presenter rationale as the per-presenter gradient/VisualBrush caches).
        public ResourceHandle ImageSource;
        public Nova.Imaging.ManagedWicBitmap? Bitmap;
    }
}