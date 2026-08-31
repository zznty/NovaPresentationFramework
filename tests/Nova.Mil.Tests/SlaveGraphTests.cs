using Nova.FontConfig;
using Nova.Geometry;
using Nova.HarfBuzz;
using Nova.MilCmd;
using Nova.TestSupport;
using Nova.Text;
using Nova.Vulkan;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Mil.Tests;

// Validation is Disabled by default via NovaTestVulkan.DeviceOptions() (see that helper for
// the full rationale): the Khronos validation layer's GetDispatchDevice hits a libstdc++
// __glibcxx_assert_fail and aborts the process under rapid Vulkan device create/destroy
// churn. Set NOVA_TEST_VULKAN_VALIDATION=1 to re-enable validation for a deliberate run.
// These tests assert raster pixels/glyphs, not validation output; the layer stays enabled
// in the interactive smoke/dev path.
public sealed class SlaveGraphTests
{
    [Fact]
    public void Rasterize_DrawRectangle_ReadbackShowsRedInsideBlackOutside()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseRectChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        Assert.Equal(255, Channel(pixels, 12, 12, 0));
        Assert.Equal(0, Channel(pixels, 12, 12, 1));
        Assert.Equal(0, Channel(pixels, 12, 12, 2));
        Assert.Equal(255, Channel(pixels, 12, 12, 3));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));
        Assert.Equal(0, Channel(pixels, 30, 30, 0));
    }

    [Fact]
    public void Rasterize_DrawRoundedRectangle_FillAndPen_RendersRingAndInterior()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseRoundedRectFillAndPenChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Fill interior stays red (8,8,32,32 rect, stroke ring centered on its edge).
        Assert.Equal(255, Channel(pixels, 24, 24, 0));
        Assert.Equal(0, Channel(pixels, 24, 24, 2));

        // The blue stroke ring must render on the border (thickness 4, half-width 2):
        // top edge lands on y=8 and left edge on x=8, over the red fill.
        Assert.Equal(255, Channel(pixels, 24, 8, 2));
        Assert.Equal(0, Channel(pixels, 24, 8, 0));
        Assert.Equal(255, Channel(pixels, 8, 24, 2));
        Assert.Equal(0, Channel(pixels, 8, 24, 0));

        // Just outside the ring stays clear.
        Assert.Equal(0, Channel(pixels, 5, 24, 0));
        Assert.Equal(0, Channel(pixels, 5, 24, 2));
    }

    [Fact]
    public void Rasterize_DrawRectangle_PenOnly_RendersRing()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseRectPenOnlyChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // No fill brush: the interior stays clear.
        Assert.Equal(0, Channel(pixels, 24, 24, 0));
        Assert.Equal(0, Channel(pixels, 24, 24, 2));

        // A stroke-only rectangle (Border's border ring path) must still render its ring.
        Assert.Equal(255, Channel(pixels, 24, 8, 2));
        Assert.Equal(255, Channel(pixels, 8, 24, 2));
    }

    [Fact]
    public void Rasterize_NestedVisualWithOffset_ShiftsRect()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseNestedOffsetChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Child draws rect (8,8,16,16) in its own space; child offset (20,0) shifts it to (28,8)-(44,24).
        Assert.Equal(255, Channel(pixels, 30, 12, 0));
        Assert.Equal(0, Channel(pixels, 10, 12, 0));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));
    }

    [Fact]
    public void Rasterize_DrawGlyphRun_WithAtlas_ReadbackShowsGlyph()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using var atlas = new GlyphAtlas(presenter, new PixelSize(64, 64));
        using var shaper = new TextShaper();

        Typeface? typeface = ResolveTypeface(shaper);
        if (typeface is null)
        {
            return; // no system font — skip
        }

        using (typeface)
        {
            Span<PositionedGlyph> shaped = stackalloc PositionedGlyph[8];
            if (shaper.Shape(typeface, "A", 16, ShapeOptions.Default, shaped) == 0)
            {
                return;
            }

            var graph = new SlaveGraph();
            const ulong token = 42;
            graph.RegisterFont(new FontFaceToken(token), typeface);
            ParseGlyphChannel(graph, token, shaped[0].Id.GlyphIndex);
            GlyphAtlas liveAtlas = atlas;
            presenter.Render(queue => graph.Rasterize(queue, liveAtlas));
            ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

            bool anyVisible = false;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                {
                    continue;
                }

                anyVisible = true;
                break;
            }

            Assert.True(anyVisible);
        }
    }

    [Fact]
    public void Rasterize_GlyphRun_FractionalOrigin_SnapsToPixelGrid()
    {
        // Glyph quads are snapped to the device pixel grid: the same glyph run placed at
        // an integer vs a fractional origin must produce byte-identical pixels. Without
        // the snap the bilinear sampler re-interpolates the atlas glyph at the shifted
        // position, softening every edge — the "blurry text" defect (atlas glyphs are
        // rasterized once at an integer pixel size, so fractional placement never gains
        // resolution, it only loses sharpness).
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using var shaper = new TextShaper();
        Typeface? typeface = ResolveTypeface(shaper);
        if (typeface is null)
        {
            return; // no system font — skip
        }

        using (typeface)
        {
            Span<PositionedGlyph> shaped = stackalloc PositionedGlyph[8];
            if (shaper.Shape(typeface, "A", 16, ShapeOptions.Default, shaped) == 0)
            {
                return;
            }

            uint glyphIndex = shaped[0].Id.GlyphIndex;

            byte[] RenderAt(float originX)
            {
                using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
                using var atlas = new GlyphAtlas(presenter, new PixelSize(64, 64));
                var graph = new SlaveGraph();
                const ulong token = 42;
                graph.RegisterFont(new FontFaceToken(token), typeface);
                ParseGlyphChannel(graph, token, glyphIndex, originX);
                GlyphAtlas liveAtlas = atlas;
                presenter.Render(queue => graph.Rasterize(queue, liveAtlas));
                return presenter.ReadbackRgba().ToArray();
            }

            byte[] integerOrigin = RenderAt(10.0f);
            byte[] fractionalOrigin = RenderAt(10.4f);

            bool anyVisible = false;
            for (int i = 0; i < integerOrigin.Length; i += 4)
            {
                if (integerOrigin[i] != 0 || integerOrigin[i + 1] != 0 || integerOrigin[i + 2] != 0)
                {
                    anyVisible = true;
                    break;
                }
            }

            Assert.True(anyVisible, "premise: the glyph must render");
            Assert.True(
                integerOrigin.AsSpan().SequenceEqual(fractionalOrigin),
                "a fractional glyph origin must snap to the same device pixels as the integer origin");
        }
    }

    [Fact]
    public void HitTest_CenterOfRect_ReturnsVisualHandle()
    {
        var graph = new SlaveGraph();
        ParseRectChannel(graph);

        Assert.True(graph.HitTest(new Point(16, 16), out ResourceHandle visual));
        Assert.Equal(new ResourceHandle(1), visual);
        Assert.False(graph.HitTest(new Point(2, 2), out _));
    }

    private static Typeface? ResolveTypeface(TextShaper shaper)
    {
        try
        {
            return shaper.Resolve(new FontQuery("sans-serif"));
        }
        catch (FontConfigException)
        {
            return null;
        }
    }

    private static void ParseRectChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        byte[] blob = DrawRectangleBlob(8, 8, 16, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }

    private static void ParseRoundedRectFillAndPenChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2, 1.0f, 0.0f, 0.0f); // red fill
        CreateBrush(bytes, 3, 0.0f, 0.0f, 1.0f); // blue pen brush
        CreatePen(bytes, 4, 3, 4.0);             // pen -> brush 3, thickness 4

        byte[] blob = DrawRoundedRectangleBlob(8, 8, 32, 32, 0, 0, brushDependent: 1, penDependent: 2);
        AppendRenderData(bytes, 5, blob);

        AppendVisualSetContent(bytes, 1, 5);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(5), [new ResourceHandle(2), new ResourceHandle(4)]);
    }

    private static void ParseRectPenOnlyChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2, 0.0f, 0.0f, 1.0f); // blue pen brush
        CreatePen(bytes, 3, 2, 4.0);             // pen -> brush 2, thickness 4

        byte[] blob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 0, penDependent: 1);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    private static void ParseNestedOffsetChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateVisual(bytes, 2);
        CreateBrush(bytes, 3);

        bytes.UInt32((uint)MilCommandKind.VisualSetOffset);
        bytes.UInt32(2);
        bytes.Double(20);
        bytes.Double(0);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(0);

        byte[] blob = DrawRectangleBlob(8, 8, 16, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 2, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    [Fact]
    public void Rasterize_VisualTransformGroup_ReadbackShowsScaledRect()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseTransformGroupChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // TransformGroup = Translate(30, 10) composed with Scale(2, 2) about the origin.
        // Rect (8,8,16,16) in geometry space scales to (16,16,32,32) then translates to
        // (46,26)-(78,58); a 64x64 canvas clips the right/bottom edge.
        Assert.Equal(255, Channel(pixels, 50, 30, 0));
        Assert.Equal(0, Channel(pixels, 44, 24, 0)); // just outside the scaled rect
        Assert.Equal(0, Channel(pixels, 8, 8, 0)); // unscaled origin stays empty
    }

    private static void ParseTransformGroupChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        // Translate(30, 10) then Scale(2, 2) about the origin, composed left-to-right.
        bytes.UInt32((uint)MilCommandKind.TranslateTransform);
        bytes.UInt32(3);
        bytes.Double(30);
        bytes.Double(10);
        bytes.UInt32(0); // hXAnimations
        bytes.UInt32(0); // hYAnimations

        bytes.UInt32((uint)MilCommandKind.ScaleTransform);
        bytes.UInt32(4);
        bytes.Double(2);
        bytes.Double(2);
        bytes.Double(0); // centerX
        bytes.Double(0); // centerY
        bytes.UInt32(0); // hScaleXAnimations
        bytes.UInt32(0); // hScaleYAnimations
        bytes.UInt32(0); // hCenterXAnimations
        bytes.UInt32(0); // hCenterYAnimations

        // TransformGroup with children [4 (scale), 3 (translate)].
        bytes.UInt32((uint)MilCommandKind.TransformGroup);
        bytes.UInt32(5);
        bytes.UInt32(8); // ChildrenSize: two handles
        bytes.UInt32(4);
        bytes.UInt32(3);

        bytes.UInt32((uint)MilCommandKind.VisualSetTransform);
        bytes.UInt32(1);
        bytes.UInt32(5);

        byte[] blob = DrawRectangleBlob(8, 8, 16, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 6, blob);

        AppendVisualSetContent(bytes, 1, 6);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(6), [new ResourceHandle(2)]);
    }

    private static void ParseGlyphChannel(SlaveGraph graph, ulong token, uint glyphIndex, float originX = 10)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(4);
        bytes.UInt32((uint)MilResourceType.GlyphRun);
        bytes.UInt32((uint)MilCommandKind.GlyphRunCreate);
        bytes.UInt32(4);
        bytes.UInt64(token);
        bytes.UInt16(0); // flags: no offsets
        bytes.UInt16(0); // packing
        bytes.Float(originX);
        bytes.Float(20);
        bytes.Float(16); // emSize
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(10);
        bytes.Double(20);
        bytes.UInt16(1); // glyphCount
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16((ushort)glyphIndex);
        bytes.Float(16);

        var renderData = new Writer();
        renderData.Int32(16);
        renderData.UInt32((uint)MilCommandKind.DrawGlyphRun);
        renderData.UInt32(1); // foreground dependent
        renderData.UInt32(2); // glyphRun dependent
        byte[] blob = renderData.ToArray();
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2), new ResourceHandle(4)]);
    }

    private static void CreateVisual(Writer bytes, uint handle)
    {
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(handle);
        bytes.UInt32((uint)MilResourceType.Visual);
    }

    private static void CreateBrush(Writer bytes, uint handle)
    {
        CreateBrush(bytes, handle, 1.0f, 0.0f, 0.0f);
    }

    private static void CreateBrush(Writer bytes, uint handle, float r, float g, float b)
    {
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(handle);
        bytes.UInt32((uint)MilResourceType.SolidColorBrush);
        bytes.UInt32((uint)MilCommandKind.SolidColorBrush);
        bytes.UInt32(handle);
        bytes.Double(1.0);
        bytes.Float(r);
        bytes.Float(g);
        bytes.Float(b);
        bytes.Float(1.0f);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);
    }

    [Fact]
    public void Rasterize_GradientPen_FallsBackToStrongestStop()
    {
        // Regression: the MIL wire carries no per-vertex pen colors, so a gradient pen is
        // degraded to its strongest stop instead of being silently dropped. Fluent's
        // ButtonBorderBrush is a gradient stroke — dropping it removed card/button borders
        // entirely while Classic/Aero (solid brushes) kept theirs.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseGradientPenChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Pen-only rect (8,8,32,32), thickness 4: the ring pixel must be the strongest
        // stop's color — green (alpha 1.0) wins over red (alpha 0.5).
        Assert.True(Channel(pixels, 24, 8, 1) > 200, $"ring must be green, got g={Channel(pixels, 24, 8, 1)}");
        Assert.True(Channel(pixels, 24, 8, 0) < 60, $"ring must not be red, got r={Channel(pixels, 24, 8, 0)}");
        Assert.Equal(0, Channel(pixels, 24, 24, 1)); // interior stays clear
    }

    [Fact]
    public void Rasterize_GradientFill_RoundedPath_Renders()
    {
        // Regression: a RelativeToBoundingBox gradient filling a flattened rounded-rect path
        // (the Fluent complex-border code path — DrawGeometry(gradient, null, ringGeometry))
        // rendered nothing. The arc flattener appended the arc's start quadrant after its
        // end, folding the contour; the fold defeated the fast tessellation path and the
        // sweep produced no triangles.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };
        ParseRoundedGradientFillChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Blue -> yellow horizontal gradient over the rounded rect (16,8)-(56,40): the left
        // edge is blue, the right edge yellow, the middle blends both channels.
        Assert.True(Channel(pixels, 20, 24, 2) > 200, $"left must be blue, got b={Channel(pixels, 20, 24, 2)}");
        Assert.True(Channel(pixels, 20, 24, 0) < 60, $"left must not be red, got r={Channel(pixels, 20, 24, 0)}");
        Assert.True(Channel(pixels, 52, 24, 0) > 200, $"right must be yellow-red, got r={Channel(pixels, 52, 24, 0)}");
        Assert.True(Channel(pixels, 52, 24, 2) < 60, $"right must not be blue, got b={Channel(pixels, 52, 24, 2)}");
    }

    private static void ParseGradientPenChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        // LinearGradientBrush 2: green (alpha 1.0) then red (alpha 0.5), Absolute mapping.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(0.0); // EndPoint.X
        bytes.Double(1.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(0); // MappingMode: Absolute
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 0.0f, 1.0f, 0.0f, 1.0f); // green, alpha 1.0
        AppendStop(bytes, 1.0, 1.0f, 0.0f, 0.0f, 0.5f); // red, alpha 0.5

        CreatePen(bytes, 3, 2, 4.0); // pen -> gradient brush 2, thickness 4

        byte[] blob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 0, penDependent: 1);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    private static void ParseRoundedGradientFillChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        // LinearGradientBrush 2: blue -> yellow, RelativeToBoundingBox (0,0)-(1,0).
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(1.0); // EndPoint.X
        bytes.Double(0.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 0.0f, 0.0f, 1.0f, 1.0f); // blue
        AppendStop(bytes, 1.0, 1.0f, 1.0f, 0.0f, 1.0f); // yellow

        byte[] blob = DrawRoundedRectangleBlob(16, 8, 40, 32, 4, 4, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }

    [Fact]
    public void Rasterize_GradientFill_RelativeTransform_FlipsAxis()
    {
        // Regression: Fluent's ButtonBorderBrush is an absolute 3px gradient whose
        // RelativeTransform (ScaleTransform ScaleY=-1 CenterY=0.5) puts the darker stop on
        // the card's BOTTOM edge. The relative transform was dropped on the wire, leaving
        // the dark strip on the TOP edge — the "inverted drop shadow" on the cards.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };
        ParseRelativeTransformGradientChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Axis (0,0)-(0,3) flipped about the vertical middle of the rect (0,0,40,32):
        // the axis lands at y 29-32 with the 0.33 dark stop nearest the bottom; below the
        // axis the spread pads to the dark start stop, above it to the light end stop.
        // With the transform dropped the whole picture inverts.
        Assert.True(Channel(pixels, 20, 34, 0) < 60, $"below the axis must pad dark, got r={Channel(pixels, 20, 34, 0)}");
        Assert.True(Channel(pixels, 20, 31, 0) < 200, $"the dark stop must tint the bottom band, got r={Channel(pixels, 20, 31, 0)}");
        Assert.True(Channel(pixels, 20, 10, 0) > 200, $"above the axis must pad light, got r={Channel(pixels, 20, 10, 0)}");
    }

    private static void ParseRelativeTransformGradientChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        // ScaleTransform 2: ScaleX=1, ScaleY=-1, CenterX=0, CenterY=0.5.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.ScaleTransform);
        bytes.UInt32((uint)MilCommandKind.ScaleTransform);
        bytes.UInt32(2);
        bytes.Double(1.0); // scaleX
        bytes.Double(-1.0); // scaleY
        bytes.Double(0.0); // centerX
        bytes.Double(0.5); // centerY
        bytes.UInt32(0); // hAnimations

        // LinearGradientBrush 3: absolute (0,0)-(0,3), stops 0.33 black -> 1.0 white,
        // relative transform -> the ScaleTransform.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(3);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(3);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(0.0); // EndPoint.X
        bytes.Double(3.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(2); // hRelativeTransform -> handle 2
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(0); // MappingMode: Absolute
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.33, 0.0f, 0.0f, 0.0f, 1.0f); // black
        AppendStop(bytes, 1.0, 1.0f, 1.0f, 1.0f, 1.0f); // white

        byte[] blob = DrawRoundedRectangleBlob(0, 0, 40, 32, 4, 4, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    private static void CreatePen(Writer bytes, uint handle, uint brushHandle, double thickness)
    {
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(handle);
        bytes.UInt32((uint)MilResourceType.Pen);
        bytes.UInt32((uint)MilCommandKind.Pen);
        bytes.UInt32(handle);
        bytes.Double(thickness);   // thickness
        bytes.Double(10.0);        // miterLimit
        bytes.UInt32(brushHandle); // brush
        bytes.UInt32(0);           // hThicknessAnimations
        bytes.UInt32(0);           // caps + join (4 uints)
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);           // hDashStyle
    }

    private static byte[] DrawRectangleBlob(double x, double y, double width, double height, uint brushDependent, uint penDependent)
    {
        var renderData = new Writer();
        renderData.Int32(48);
        renderData.UInt32((uint)MilCommandKind.DrawRectangle);
        renderData.Double(x);
        renderData.Double(y);
        renderData.Double(width);
        renderData.Double(height);
        renderData.UInt32(brushDependent);
        renderData.UInt32(penDependent);
        return renderData.ToArray();
    }

    private static byte[] DrawRoundedRectangleBlob(double x, double y, double width, double height, double radiusX, double radiusY, uint brushDependent, uint penDependent)
    {
        var renderData = new Writer();
        renderData.Int32(64);
        renderData.UInt32((uint)MilCommandKind.DrawRoundedRectangle);
        renderData.Double(x);
        renderData.Double(y);
        renderData.Double(width);
        renderData.Double(height);
        renderData.Double(radiusX);
        renderData.Double(radiusY);
        renderData.UInt32(brushDependent);
        renderData.UInt32(penDependent);
        return renderData.ToArray();
    }

    private static void AppendRenderData(Writer bytes, uint handle, byte[] blob)
    {
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(handle);
        bytes.UInt32((uint)MilResourceType.RenderData);
        bytes.UInt32((uint)MilCommandKind.RenderData);
        bytes.UInt32(handle);
        bytes.UInt32((uint)blob.Length);
        bytes.Bytes(blob);
    }

    private static void AppendVisualSetContent(Writer bytes, uint handle, uint content)
    {
        bytes.UInt32((uint)MilCommandKind.VisualSetContent);
        bytes.UInt32(handle);
        bytes.UInt32(content);
    }

    private static void AppendTargetSetRoot(Writer bytes, uint root)
    {
        bytes.UInt32((uint)MilCommandKind.TargetSetRoot);
        bytes.UInt32(0);
        bytes.UInt32(root);
    }

    private static void AppendTargetSetClearColor(Writer bytes, float r = 0.0f, float g = 0.0f, float b = 0.0f, float a = 1.0f)
    {
        bytes.UInt32((uint)MilCommandKind.TargetSetClearColor);
        bytes.UInt32(0);
        bytes.Float(r);
        bytes.Float(g);
        bytes.Float(b);
        bytes.Float(a);
    }

    private static byte Channel(ReadOnlySpan<byte> pixels, int x, int y, int channel)
    {
        return pixels[(((y * 64) + x) * 4) + channel];
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void Int32(int value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void UInt32(uint value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void UInt16(ushort value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void UInt64(ulong value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Float(float value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Double(double value)
        {
            _bytes.AddRange(BitConverter.GetBytes(value));
        }

        public void Bytes(ReadOnlySpan<byte> value)
        {
            _bytes.AddRange(value);
        }

        public byte[] ToArray()
        {
            return [.. _bytes];
        }
    }

    [Fact]
    public void Rasterize_LinearGradient_LeftRedRightBlueWithBlend()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };
        ParseLinearGradientChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Rect (8,8,48,16) filled with red->blue along the horizontal axis. Sample one row:
        // red dominates the left, blue dominates the right, the center is a blend, and green
        // stays zero everywhere (pure red->blue interpolation).
        Assert.Equal(0, Channel(pixels, 16, 12, 1));
        Assert.InRange(Channel(pixels, 10, 12, 0), 200, 255); // left: strong red
        Assert.InRange(Channel(pixels, 10, 12, 2), 0, 40);    // left: little blue
        Assert.InRange(Channel(pixels, 54, 12, 2), 200, 255); // right: strong blue
        Assert.InRange(Channel(pixels, 54, 12, 0), 0, 40);    // right: little red
        Assert.InRange(Channel(pixels, 32, 12, 0), 100, 160); // center: red half
        Assert.InRange(Channel(pixels, 32, 12, 2), 100, 160); // center: blue half
        // Monotonic falloff along the row.
        Assert.True(Channel(pixels, 16, 12, 0) > Channel(pixels, 40, 12, 0));
        Assert.True(Channel(pixels, 16, 12, 2) < Channel(pixels, 40, 12, 2));
    }

    [Fact]
    public void Rasterize_RadialGradient_CenterRedEdgeBlue()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };
        ParseRadialGradientChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Rect (8,8,48,16), radial brush centered at (0.5,0.5) with radius 0.5/0.5 relative:
        // near the center is red (t~0), the right edge approaches blue (t~1), a mid point
        // blends, and green stays zero.
        Assert.Equal(0, Channel(pixels, 32, 16, 1));
        Assert.InRange(Channel(pixels, 32, 16, 0), 200, 255); // near center: red
        Assert.InRange(Channel(pixels, 32, 16, 2), 0, 40);    // near center: not blue
        Assert.InRange(Channel(pixels, 54, 16, 2), 200, 255); // right edge: blue
        Assert.InRange(Channel(pixels, 54, 16, 0), 0, 60);    // right edge: little red
        Assert.InRange(Channel(pixels, 40, 16, 0), 130, 220); // blend zone: red falling
        Assert.InRange(Channel(pixels, 40, 16, 2), 40, 130);  // blend zone: blue rising
    }

    [Fact]
    public void Rasterize_LinearGradient_SemiTransparentStops_BlendOverBackground()
    {
        // Probe: the gradient LUT is baked premultiplied, but every existing gradient test
        // used fully opaque stops. Aero's drop-shadow chrome fades shadows with
        // alpha < 255 stops, so a semi-transparent gradient must blend correctly over the
        // background: a mid-stop pixel is a partial blend (not fully opaque, not fully
        // transparent). Stops: #00000000 (transparent black) -> #FF000000 (opaque black)
        // over an opaque white background.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };

        var bytes = new Writer();
        CreateVisual(bytes, 1);

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(1.0); // EndPoint.X
        bytes.Double(0.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 0.0f, 0.0f, 0.0f, 0.0f); // transparent black
        AppendStop(bytes, 1.0, 0.0f, 0.0f, 0.0f, 1.0f); // opaque black

        byte[] blob = DrawRectangleBlob(8, 8, 48, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 1.0f, 1.0f, 1.0f, 1.0f); // opaque white background
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Mid-stop (t=0.5): black at alpha 0.5 over white -> ~127,127,127. A proper
        // partial blend: clearly not opaque black (< 60) and clearly not the untouched
        // white background (> 200). Alpha stays fully covered (over opaque background).
        Assert.InRange(Channel(pixels, 32, 12, 0), 90, 165);
        Assert.InRange(Channel(pixels, 32, 12, 1), 90, 165);
        Assert.InRange(Channel(pixels, 32, 12, 2), 90, 165);
        Assert.InRange(Channel(pixels, 32, 12, 3), 200, 255);

        // Near the transparent stop: the background shows through (~white).
        Assert.InRange(Channel(pixels, 10, 12, 0), 220, 255);
        Assert.InRange(Channel(pixels, 10, 12, 3), 200, 255);

        // Near the opaque stop: the black is almost fully covering.
        Assert.InRange(Channel(pixels, 54, 12, 0), 0, 45);

        // Monotonic darkening along the row: the alpha ramp is actually applied.
        Assert.True(Channel(pixels, 16, 12, 0) > Channel(pixels, 48, 12, 0));
    }

    [Fact]
    public void Rasterize_LinearGradient_SpreadRepeat_FoldsPastEnd()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };
        ParseLinearGradientChannel(graph, spreadMethod: 2, endX: 0.5);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Axis is half the rect (0..0.5 relative), so t runs 0..2 across the fill: with
        // Repeat the second half of the rect restarts the red->blue ramp, so the right edge
        // is blue again (not padded blue) and the middle folds back to red.
        Assert.InRange(Channel(pixels, 10, 12, 0), 200, 255); // first ramp: red start
        Assert.InRange(Channel(pixels, 54, 12, 2), 200, 255); // folded back to blue
        Assert.InRange(Channel(pixels, 32, 12, 0), 200, 255); // t=1 folds to red
        Assert.InRange(Channel(pixels, 32, 12, 2), 0, 40);
    }

    [Fact]
    public void Rasterize_VisualBrush_RendersInnerVisualContent()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParseVisualBrushChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // VisualBrush of a 16x16 red rect stretched over (8,8,48,32): red fills the rect.
        Assert.Equal(255, Channel(pixels, 30, 20, 0));
        Assert.Equal(0, Channel(pixels, 30, 20, 1));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));   // outside the brush rect stays clear
        Assert.Equal(0, Channel(pixels, 60, 40, 0));
    }

    [Fact]
    public void Rasterize_RotatedRect_ProducesPartialCoverageEdgePixels()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseRotatedRectChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // 24x24 red rect rotated 45 degrees about the canvas center on a WHITE background.
        // MSAA resolve averages sample colors, so edge pixels blend red with white:
        // r stays 255 while g/b rise above 0 — the partial-coverage signature. Without MSAA
        // every edge pixel snaps to pure red or pure white (partial count would be 0).
        int partial = CountColor(pixels, static (r, g, b) => r > 200 && g > 0 && g < 255 && b > 0 && b < 255);
        Assert.True(partial > 0, $"expected MSAA partial-coverage edge pixels, got {partial}");

        // The solid interior still renders pure red and the background stays white.
        Assert.Equal(255, Channel(pixels, 32, 32, 0));
        Assert.Equal(0, Channel(pixels, 32, 32, 1));
        Assert.Equal(255, Channel(pixels, 4, 4, 0)); // background corner stays white
        Assert.Equal(255, Channel(pixels, 4, 4, 1));

        // No regression in the solid fill: the diamond interior is a large red area.
        int solidRed = CountColor(pixels, static (r, g, b) => r > 200 && g < 60 && b < 60);
        Assert.True(solidRed > 200, $"expected a large solid red interior, got {solidRed}");
    }

    private static int CountColor(ReadOnlySpan<byte> pixels, Func<byte, byte, byte, bool> predicate)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (predicate(pixels[i], pixels[i + 1], pixels[i + 2]))
            {
                count++;
            }
        }

        return count;
    }

    private static void ParseRotatedRectChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        // TransformGroup: Translate(-32,-32) * Rotate(45) * Translate(32,32) — rotate the
        // 24x24 rect at (20,20) about the canvas center (row-vector: apply left to right).
        bytes.UInt32((uint)MilCommandKind.TranslateTransform);
        bytes.UInt32(3);
        bytes.Double(-32);
        bytes.Double(-32);
        bytes.UInt32(0); // hXAnimations
        bytes.UInt32(0); // hYAnimations

        bytes.UInt32((uint)MilCommandKind.RotateTransform);
        bytes.UInt32(4);
        bytes.Double(45);
        bytes.Double(0); // centerX
        bytes.Double(0); // centerY
        bytes.UInt32(0); // hAngleAnimations
        bytes.UInt32(0); // hCenterXAnimations
        bytes.UInt32(0); // hCenterYAnimations

        bytes.UInt32((uint)MilCommandKind.TranslateTransform);
        bytes.UInt32(5);
        bytes.Double(32);
        bytes.Double(32);
        bytes.UInt32(0); // hXAnimations
        bytes.UInt32(0); // hYAnimations

        bytes.UInt32((uint)MilCommandKind.TransformGroup);
        bytes.UInt32(6);
        bytes.UInt32(12); // ChildrenSize: three handles
        bytes.UInt32(3);
        bytes.UInt32(4);
        bytes.UInt32(5);

        bytes.UInt32((uint)MilCommandKind.VisualSetTransform);
        bytes.UInt32(1);
        bytes.UInt32(6);

        byte[] blob = DrawRectangleBlob(20, 20, 24, 24, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 7, blob);

        AppendVisualSetContent(bytes, 1, 7);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 1.0f, 1.0f, 1.0f, 1.0f); // white background
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(7), [new ResourceHandle(2)]);
    }

    [Fact]
    public void Rasterize_RotateAboutRectCenter_KeepsSquareInPlace()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseRotateAboutCenterChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // 32x32 red square at (16,16) rotated 90 degrees about its OWN center (32,32): a
        // square maps onto itself, so every interior pixel stays covered and the outside
        // stays clear. FAILS on the old T(c)*R*T(-c) order, which translates the whole
        // square off-canvas (the center is not fixed by that composition).
        Assert.Equal(255, Channel(pixels, 32, 32, 0)); // center must stay fixed
        Assert.Equal(255, Channel(pixels, 20, 32, 0));
        Assert.Equal(255, Channel(pixels, 44, 32, 0));
        Assert.Equal(255, Channel(pixels, 32, 20, 0));
        Assert.Equal(255, Channel(pixels, 32, 44, 0));
        Assert.Equal(0, Channel(pixels, 4, 4, 0));   // outside stays clear
        Assert.Equal(0, Channel(pixels, 60, 60, 0));
    }

    [Fact]
    public void Rasterize_ScaleAboutRectCenter_DoublesInPlace()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseScaleAboutCenterChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // 32x32 red square at (16,16) scaled 2x about its OWN center (32,32): the result is
        // the 64x64 square (0,0)-(64,64) — corners land inside the canvas, center fixed.
        // FAILS on the old T(c)*S*T(-c) order, which maps the square to (64,64)-(128,128),
        // entirely off-canvas.
        Assert.Equal(255, Channel(pixels, 4, 4, 0));   // scaled top-left corner lands in-canvas
        Assert.Equal(255, Channel(pixels, 60, 60, 0)); // scaled bottom-right corner
        Assert.Equal(255, Channel(pixels, 32, 32, 0)); // center must stay fixed
        Assert.Equal(255, Channel(pixels, 32, 16, 0));
    }

    [Fact]
    public void Rasterize_SelfReferentialVisualBrush_TerminatesWithoutPixels()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParseSelfReferentialVisualBrushChannel(graph);

        // Must terminate (no stack overflow): the brush's own visual paints the SAME brush,
        // so the nested instance degrades to no pixels via the re-entrancy guard.
        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // The self-referential content paints nothing, so the outer rect over the opaque
        // black clear color stays black — defined result, no stray pixels.
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            Assert.True(pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0,
                $"unexpected non-black pixel at byte {i}: {pixels[i]},{pixels[i + 1]},{pixels[i + 2]}");
        }
    }

    private static void ParseRotateAboutCenterChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        // RotateTransform with a NON-ZERO center: rotate 90 about the square's own center.
        bytes.UInt32((uint)MilCommandKind.RotateTransform);
        bytes.UInt32(3);
        bytes.Double(90); // angle
        bytes.Double(32); // centerX
        bytes.Double(32); // centerY
        bytes.UInt32(0); // hAngleAnimations
        bytes.UInt32(0); // hCenterXAnimations
        bytes.UInt32(0); // hCenterYAnimations

        bytes.UInt32((uint)MilCommandKind.VisualSetTransform);
        bytes.UInt32(1);
        bytes.UInt32(3);

        byte[] blob = DrawRectangleBlob(16, 16, 32, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(2)]);
    }

    private static void ParseScaleAboutCenterChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        // ScaleTransform with a NON-ZERO center: scale 2x about the square's own center.
        bytes.UInt32((uint)MilCommandKind.ScaleTransform);
        bytes.UInt32(3);
        bytes.Double(2); // scaleX
        bytes.Double(2); // scaleY
        bytes.Double(32); // centerX
        bytes.Double(32); // centerY
        bytes.UInt32(0); // hScaleXAnimations
        bytes.UInt32(0); // hScaleYAnimations
        bytes.UInt32(0); // hCenterXAnimations
        bytes.UInt32(0); // hCenterYAnimations

        bytes.UInt32((uint)MilCommandKind.VisualSetTransform);
        bytes.UInt32(1);
        bytes.UInt32(3);

        byte[] blob = DrawRectangleBlob(16, 16, 32, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(2)]);
    }

    private static void ParseSelfReferentialVisualBrushChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // outer visual
        CreateVisual(bytes, 4); // inner visual (the brush content)

        // VisualBrush 2 referencing inner visual 4.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.VisualBrush);
        bytes.UInt32((uint)MilCommandKind.VisualBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // Viewport.X
        bytes.Double(0.0); // Viewport.Y
        bytes.Double(1.0); // Viewport.Width
        bytes.Double(1.0); // Viewport.Height
        bytes.Double(0.0); // Viewbox.X
        bytes.Double(0.0); // Viewbox.Y
        bytes.Double(1.0); // Viewbox.Width
        bytes.Double(1.0); // Viewbox.Height
        bytes.Double(0.0); // CacheInvalidationThresholdMinimum
        bytes.Double(0.0); // CacheInvalidationThresholdMaximum
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(1); // ViewportUnits: RelativeToBoundingBox
        bytes.UInt32(1); // ViewboxUnits: RelativeToBoundingBox
        bytes.UInt32(0); // hViewportAnimations
        bytes.UInt32(0); // hViewboxAnimations
        bytes.UInt32(1); // Stretch.Fill
        bytes.UInt32(0); // TileMode.None
        bytes.UInt32(0); // AlignmentX.Center
        bytes.UInt32(0); // AlignmentY.Center
        bytes.UInt32(0); // CachingHint
        bytes.UInt32(4); // hVisual

        // Inner visual content: a 16x16 rect painted with the SAME VisualBrush (self-ref).
        byte[] innerBlob = DrawRectangleBlob(0, 0, 16, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 5, innerBlob);
        AppendVisualSetContent(bytes, 4, 5);

        // Outer visual content: a 48x48 rect painted with the VisualBrush.
        byte[] blob = DrawRectangleBlob(8, 8, 48, 48, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
        graph.SetRenderDataDependents(new ResourceHandle(5), [new ResourceHandle(2)]);
    }

    private static void ParseLinearGradientChannel(SlaveGraph graph, int spreadMethod = 0, double endX = 1.0)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(endX); // EndPoint.X
        bytes.Double(0.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32((uint)spreadMethod); // SpreadMethod
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 1.0f, 0.0f, 0.0f, 1.0f); // red
        AppendStop(bytes, 1.0, 0.0f, 0.0f, 1.0f, 1.0f); // blue

        byte[] blob = DrawRectangleBlob(8, 8, 48, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }

    private static void ParseRadialGradientChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.RadialGradientBrush);
        bytes.UInt32((uint)MilCommandKind.RadialGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.5); // Center.X
        bytes.Double(0.5); // Center.Y
        bytes.Double(0.5); // RadiusX
        bytes.Double(0.5); // RadiusY
        bytes.Double(0.5); // GradientOrigin.X
        bytes.Double(0.5); // GradientOrigin.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hCenterAnimations
        bytes.UInt32(0); // hRadiusXAnimations
        bytes.UInt32(0); // hRadiusYAnimations
        bytes.UInt32(0); // hGradientOriginAnimations
        AppendStop(bytes, 0.0, 1.0f, 0.0f, 0.0f, 1.0f); // red
        AppendStop(bytes, 1.0, 0.0f, 0.0f, 1.0f, 1.0f); // blue

        byte[] blob = DrawRectangleBlob(8, 8, 48, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }

    private static void ParseVisualBrushChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // outer visual
        CreateVisual(bytes, 4); // inner visual (the brush content)
        CreateBrush(bytes, 6); // solid red brush for the inner rect

        // Inner visual content: red 16x16 rect at (0,0).
        byte[] innerBlob = DrawRectangleBlob(0, 0, 16, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 5, innerBlob);
        AppendVisualSetContent(bytes, 4, 5);

        // VisualBrush resource referencing inner visual handle 4.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.VisualBrush);
        bytes.UInt32((uint)MilCommandKind.VisualBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // Viewport.X
        bytes.Double(0.0); // Viewport.Y
        bytes.Double(1.0); // Viewport.Width
        bytes.Double(1.0); // Viewport.Height
        bytes.Double(0.0); // Viewbox.X
        bytes.Double(0.0); // Viewbox.Y
        bytes.Double(1.0); // Viewbox.Width
        bytes.Double(1.0); // Viewbox.Height
        bytes.Double(0.0); // CacheInvalidationThresholdMinimum
        bytes.Double(0.0); // CacheInvalidationThresholdMaximum
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(1); // ViewportUnits: RelativeToBoundingBox
        bytes.UInt32(1); // ViewboxUnits: RelativeToBoundingBox
        bytes.UInt32(0); // hViewportAnimations
        bytes.UInt32(0); // hViewboxAnimations
        bytes.UInt32(1); // Stretch.Fill
        bytes.UInt32(0); // TileMode.None
        bytes.UInt32(0); // AlignmentX.Center
        bytes.UInt32(0); // AlignmentY.Center
        bytes.UInt32(0); // CachingHint
        bytes.UInt32(4); // hVisual

        // Outer rect (8,8,48,32) filled with the VisualBrush.
        byte[] blob = DrawRectangleBlob(8, 8, 48, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        // RenderData 3 depends on the VisualBrush (handle 2); RenderData 5 depends on the
        // inner solid brush (handle 6).
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
        graph.SetRenderDataDependents(new ResourceHandle(5), [new ResourceHandle(6)]);
    }

    [Fact]
    public void Rasterize_DropShadow_Direction270_ShadowBelow()
    {
        // WPF's Direction is a math angle (counterclockwise, 90 = up): 270 — the Fluent
        // default — must cast the shadow DOWN in the Y-down raster, not up.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParseDropShadowChannel(graph, direction: 270);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Shadow below the content rect (8,8,32,32) — near the bottom edge y=40.
        byte near = Channel(pixels, 20, 42, 0);
        byte mid = Channel(pixels, 20, 50, 0);
        Assert.True(near > 200, $"shadow below the edge must be strong, got {near}");
        Assert.True(mid > 0, "shadow below must be nonzero");
        Assert.True(mid < near, $"shadow alpha must fall off with distance ({mid} >= {near})");

        // Nothing above the content (y < 8).
        Assert.Equal(0, Channel(pixels, 20, 4, 0));
        Assert.Equal(0, Channel(pixels, 20, 4, 3));
    }

    [Fact]
    public void Rasterize_DropShadow_Direction0_ShadowRightContentCrispAlphaFallsOff()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParseDropShadowChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;
        // Content rect (8,8,32,32) stays crisp and fully opaque red on top.
        Assert.Equal(255, Channel(pixels, 20, 20, 0));
        Assert.Equal(0, Channel(pixels, 20, 20, 1));
        Assert.Equal(255, Channel(pixels, 20, 20, 3));

        // Direction=0 casts the shadow to the RIGHT: nothing on the left of the content.
        Assert.Equal(0, Channel(pixels, 4, 20, 0));
        Assert.Equal(0, Channel(pixels, 4, 20, 3));

        // The shadow is present just right of the content edge (x=40) and its alpha falls
        // off with distance; far right stays clear.
        byte near = Channel(pixels, 42, 20, 0);
        byte mid = Channel(pixels, 50, 20, 0);
        Assert.True(near > 200, $"shadow near the edge should be strong, got {near}");
        Assert.True(mid > 0, "shadow mid should be nonzero");
        Assert.True(mid < near, $"shadow alpha must fall off with distance ({mid} >= {near})");
        Assert.Equal(0, Channel(pixels, 60, 20, 0));
    }

    [Fact]
    public void Rasterize_Blur_HardEdgeBecomesMonotonicRampExceedingBounds()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParseBlurChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // The content core stays red.
        Assert.True(Channel(pixels, 20, 20, 0) > 200, "blurred core should stay red");

        // The hard edge at x=40 becomes a monotonic ramp: red decreases strictly across the
        // edge and remains nonzero beyond the geometry (blur bleed ~ radius).
        byte[] ramp =
        [
            Channel(pixels, 38, 20, 0),
            Channel(pixels, 40, 20, 0),
            Channel(pixels, 42, 20, 0),
            Channel(pixels, 44, 20, 0),
            Channel(pixels, 46, 20, 0)
        ];
        for (int i = 1; i < ramp.Length; i++)
        {
            Assert.True(ramp[i] < ramp[i - 1], $"blur ramp not monotonic at index {i}: {ramp[i - 1]} -> {ramp[i]}");
        }

        Assert.True(ramp[3] > 0, "blur should extend beyond the geometry by roughly the radius");
        Assert.Equal(0, Channel(pixels, 56, 20, 0));
    }

    [Fact]
    public void Rasterize_OpacityMask_GradientAttenuatesRightPreservesLeft()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParseOpacityMaskChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Mask is white->transparent horizontally over the (8,8,32,32) content bounds:
        // t=(x-8)/32, so x=12 keeps ~0.875 alpha (strong red), x=24 keeps ~0.5, and
        // x=36 keeps ~0.125 (heavily attenuated). The transition is monotonic.
        byte left = Channel(pixels, 12, 20, 0);
        byte middle = Channel(pixels, 24, 20, 0);
        byte right = Channel(pixels, 36, 20, 0);
        Assert.True(left > 200, $"high mask alpha region should stay strong red, got {left}");
        Assert.True(right > 0, "low mask alpha region should still show a trace of red");
        Assert.True(right < 60, $"low mask alpha region should be attenuated, got {right}");
        Assert.True(middle < left, $"mask attenuation must be monotonic (middle {middle} >= left {left})");
        Assert.True(right < middle, $"mask attenuation must be monotonic (right {right} >= middle {middle})");

        // Outside the content: clear.
        Assert.Equal(0, Channel(pixels, 4, 20, 0));
        Assert.Equal(0, Channel(pixels, 50, 20, 0));
    }

    [Fact]
    public void Rasterize_ImageBrush_InMaskAndPlain_UploadsPerPresenter()
    {
        // Regression for the presenter-scoped-texture defect behind the WPFGallery "Unknown
        // texture handle" crash: a bitmap first drawn inside an opacity-mask subtree is
        // uploaded on the TRANSIENT offscreen presenter; the SAME brush drawn plainly later
        // in the walk must re-upload on the frame presenter. The old slot-cached handle
        // recorded the offscreen texture into the frame's command list — wrong pixels when
        // handle numbering collides across presenters, a hard crash when it diverges.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        var image = new SixLabors.ImageSharp.Image<Bgra32>(8, 8);
        try
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    image[x, y] = x < 4 ? new Bgra32(255, 0, 0, 255) : new Bgra32(0, 0, 255, 255);
                }
            }

            Nova.Imaging.ManagedWicBitmap? bitmap = null;
            try
            {
                bitmap = new Nova.Imaging.ManagedWicBitmap(image, Nova.Imaging.WicPixelFormat.Bgra32, 96, 96);
                image = null; // ownership transfers to the bitmap (its Dispose disposes the image)
                ParseMaskedAndPlainImageBrushChannel(graph, bitmap);

                presenter.Render(queue => graph.Rasterize(queue, null));
                ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

                // Masked child (8,8,32,32): strong red where the mask is white, attenuated on the right.
                Assert.True(Channel(pixels, 12, 20, 0) > 200, "masked left must stay strong red");
                Assert.True(Channel(pixels, 36, 20, 0) < 60, "masked right must be attenuated");

                // Plain child (40,40,16,16): the full two-tone bitmap — red left, blue right. The blue
                // half is the discriminator: a stale offscreen handle would draw the attenuated masked
                // composite (~25% alpha blue), never a strong blue.
                Assert.True(Channel(pixels, 43, 48, 0) > 200, "plain left must be strong red");
                Assert.True(Channel(pixels, 52, 48, 2) > 200, "plain right must be strong blue");
                Assert.True(Channel(pixels, 52, 48, 0) < 60, "plain right must not be red");
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        finally
        {
            // Reached only if the bitmap constructor threw before taking ownership.
            image?.Dispose();
        }
    }

    [Fact]
    public void Rasterize_Image_NonContiguousBacking_CopiesPixels()
    {
        // Regression for the all-transparent-texture defect behind the blank WPFGallery hero:
        // large decoded images back onto SPLIT row blocks (ImageSharp chunks big buffers), and
        // the old CopyPixels fallback materialized a second image that was ALSO non-contiguous
        // — the second DangerousTryGetSinglePixelMemory failed and the copy was silently
        // SKIPPED, leaving the pooled upload buffer zeroed. The fix copies each row block
        // straight into the destination. The premise (non-contiguity) is asserted so this test
        // can never silently exercise only the contiguous path.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        var image = new SixLabors.ImageSharp.Image<Bgra32>(1923, 696);
        try
        {
            for (int y = 0; y < 696; y++)
            {
                for (int x = 0; x < 1923; x++)
                {
                    image[x, y] = x < 961 ? new Bgra32(255, 0, 0, 255) : new Bgra32(0, 0, 255, 255);
                }
            }

            Nova.Imaging.ManagedWicBitmap? bitmap = null;
            try
            {
                bitmap = new Nova.Imaging.ManagedWicBitmap(image, Nova.Imaging.WicPixelFormat.Bgra32, 96, 96);
                image = null; // ownership transfers to the bitmap (its Dispose disposes the image)

                Assert.False(bitmap.TryGetSinglePixelMemory(out _), "premise: the backing store must be non-contiguous");

                ParseImageBrushChannel(graph, bitmap);

                presenter.Render(queue => graph.Rasterize(queue, null));
                ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

                // 64x64 rect with Stretch.Fill over the two-tone source: left half red, right blue.
                Assert.True(Channel(pixels, 16, 32, 0) > 200, $"left must be strong red, got {Channel(pixels, 16, 32, 0)}");
                Assert.True(Channel(pixels, 48, 32, 2) > 200, $"right must be strong blue, got {Channel(pixels, 48, 32, 2)}");
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    [Fact]
    public void Rasterize_ImageBrush_RoundedRectangle_ClipsCorners()
    {
        // The rounded-rect (and ellipse/path-geometry) fill for an ImageBrush ignored the
        // contour: the brush painted the sharp target rectangle, so a Border whose background
        // is an image showed square corners where Windows rounds them (WPFGallery dashboard
        // hero). The fill must tessellate the contour and map the brush's UV affine to each
        // vertex, cutting the corners without moving the pixels.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph { Presenter = presenter };

        var image = new SixLabors.ImageSharp.Image<Bgra32>(8, 8);
        try
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    image[x, y] = x < 4 ? new Bgra32(255, 0, 0, 255) : new Bgra32(0, 0, 255, 255);
                }
            }

            Nova.Imaging.ManagedWicBitmap? bitmap = null;
            try
            {
                bitmap = new Nova.Imaging.ManagedWicBitmap(image, Nova.Imaging.WicPixelFormat.Bgra32, 96, 96);
                image = null; // ownership transfers to the bitmap (its Dispose disposes the image)
                ParseRoundedImageBrushChannel(graph, bitmap);

                presenter.Render(queue => graph.Rasterize(queue, null));
                ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

                // Inside the contour the brush paints: rounded rect (16,8)-(56,40) radius 4,
                // image stretched over it — left half red, right half blue.
                Assert.True(Channel(pixels, 24, 24, 0) > 200, $"center-left must be red, got r={Channel(pixels, 24, 24, 0)}");
                Assert.True(Channel(pixels, 48, 24, 2) > 200, $"center-right must be blue, got b={Channel(pixels, 48, 24, 2)}");

                // The top-left corner arc is centered at (20,12) with radius 4. Pixel
                // centers, not corners, are sampled: (16,8) has center (16.5,8.5) at
                // distance 4.95 from the arc center — inside the sharp rectangle, outside
                // the arc — so it must stay transparent.
                Assert.True(Channel(pixels, 16, 8, 3) < 10, $"the corner must be transparent, got a={Channel(pixels, 16, 8, 3)} r={Channel(pixels, 16, 8, 0)} b={Channel(pixels, 16, 8, 2)}");

                // Just inside the same arc (center (19.5,10.5), distance 1.58) the brush paints.
                Assert.True(Channel(pixels, 19, 10, 0) > 200, $"inside the arc must paint, got r={Channel(pixels, 19, 10, 0)}");
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static void ParseRoundedImageBrushChannel(SlaveGraph graph, Nova.Imaging.ManagedWicBitmap bitmap)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // root

        // BitmapSource slot 5 (pixels seeded directly, as the DUCE transport does).
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(5);
        bytes.UInt32((uint)MilResourceType.BitmapSource);

        AppendImageBrush(bytes, 4, 5);

        byte[] blob = DrawRoundedRectangleBlob(16, 8, 40, 32, 4, 4, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 6, blob);
        AppendVisualSetContent(bytes, 1, 6);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(6), [new ResourceHandle(4)]);
        graph.SetBitmapSourcePixels(new ResourceHandle(5), bitmap);
    }

    [Fact]
    public void EffectCache_ContentBrushMutation_InvalidatesShadow()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        var bytes = new Writer();
        AppendDropShadowScene(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte before = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);

        // The shadow is the blurred ALPHA silhouette, so the content brush's alpha feeds the
        // shadow: dropping it to 40% must visibly weaken the shadow. The composite cache key
        // must include the brush slot version (reachable through the render-data dependents),
        // or this returns the stale strong shadow.
        var mutation = new Writer();
        UpdateSolidColorBrushAlpha(mutation, 3, 0.4f);
        MilCommandParser.ParseChannel(mutation.ToArray(), graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte after = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);

        Assert.True(before > 200, $"shadow should start strong, got {before}");
        Assert.True(after < before * 0.6, $"shadow must weaken after content alpha drop (before={before} after={after})");
    }

    [Fact]
    public void EffectCache_EffectParamMutation_OpacityChange_InvalidatesShadow()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        var bytes = new Writer();
        AppendDropShadowScene(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte before = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);

        // Opacity multiplies the shadow's alpha in BuildShadowPixels, so it changes the cached
        // TEXTURE. The cache key must include the effect slot version, or this returns the
        // stale full-strength shadow.
        var mutation = new Writer();
        UpdateDropShadowOpacity(mutation, 5, 0.3);
        MilCommandParser.ParseChannel(mutation.ToArray(), graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte after = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);

        Assert.True(before > 200, $"shadow should start strong, got {before}");
        Assert.True(after < before * 0.6, $"shadow must weaken after opacity drop (before={before} after={after})");
    }

    [Fact]
    public void EffectCache_DirectionChange_MovesShadowQuadWithoutRecompute()
    {
        // Direction/ShadowDepth feed ONLY the shadow quad's offset (the cached texture is the
        // direction-independent blurred silhouette), so a Direction change must move the shadow
        // on the very next frame even on a warm cache. This pins that the offset path is
        // recomputed from the CURRENT effect params at draw time, never from the cache key.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        var bytes = new Writer();
        AppendDropShadowScene(bytes); // Direction=0 (right)
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte rightBefore = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);
        byte leftBefore = Channel(presenter.ReadbackRgba().Span, 4, 20, 0);

        var mutation = new Writer();
        UpdateDropShadowDirection(mutation, 5, 180.0);
        MilCommandParser.ParseChannel(mutation.ToArray(), graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte rightAfter = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);
        byte leftAfter = Channel(presenter.ReadbackRgba().Span, 4, 20, 0);

        Assert.True(rightBefore > 200, $"right shadow should exist before, got {rightBefore}");
        Assert.Equal(0, leftBefore);
        Assert.True(leftAfter > 100, $"left shadow should appear after Direction=180, got {leftAfter}");
        Assert.True(rightAfter < 30, $"right shadow should vanish after Direction=180, got {rightAfter}");
    }

    [Fact]
    public void EffectCache_BoundsMutation_ShadowWidens()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        var bytes = new Writer();
        AppendDropShadowScene(bytes); // 32-wide rect
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);

        presenter.Render(queue => graph.Rasterize(queue, null));
        int maxXBefore = MaxRedX(presenter.ReadbackRgba().Span, 64);

        // Grow the rect to 48 wide by re-sending the RenderData resource; the composite (and
        // its bounds) change, so the cached shadow must be replaced.
        var mutation = new Writer();
        AppendRenderData(mutation, 4, DrawRectangleBlob(8, 8, 48, 32, brushDependent: 1, penDependent: 0));
        MilCommandParser.ParseChannel(mutation.ToArray(), graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        int maxXAfter = MaxRedX(presenter.ReadbackRgba().Span, 64);

        Assert.True(maxXAfter > maxXBefore + 10, $"shadow must widen after rect grows (before={maxXBefore} after={maxXAfter})");
    }

    [Fact]
    public void EffectCache_DeleteAndRecreateVisual_NoStaleCacheNoCrash()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        var bytes = new Writer();
        AppendDropShadowScene(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte first = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);
        Assert.True(first > 200, $"shadow should render on first pass, got {first}");

        // Deleting the effected visual destroys its cached composite textures; recreating it
        // with the same handle must produce a fresh cold render, not a stale cache or a
        // use-after-free of the destroyed textures.
        var teardown = new Writer();
        teardown.UInt32((uint)MilCommandKind.VisualRemoveChild);
        teardown.UInt32(1);
        teardown.UInt32(2);
        teardown.UInt32((uint)MilCommandKind.ChannelDeleteResource);
        teardown.UInt32(2);
        teardown.UInt32((uint)MilResourceType.Visual);
        MilCommandParser.ParseChannel(teardown.ToArray(), graph);

        var recreate = new Writer();
        CreateVisual(recreate, 2);
        recreate.UInt32((uint)MilCommandKind.VisualSetEffect);
        recreate.UInt32(2);
        recreate.UInt32(5);
        recreate.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        recreate.UInt32(1);
        recreate.UInt32(2);
        recreate.UInt32(0);
        AppendVisualSetContent(recreate, 2, 4);
        MilCommandParser.ParseChannel(recreate.ToArray(), graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        byte recreated = Channel(presenter.ReadbackRgba().Span, 42, 20, 0);
        Assert.True(recreated > 200, $"shadow must render after delete+recreate, got {recreated}");
    }

    [Fact]
    public void EffectCache_VisualRasterizedBySecondPresenter_DoesNotUseFirstPresentersTexture()
    {
        // Main + popup frames share ONE SlaveGraph but have separate Vulkan presenters; a
        // texture cached on the slot only means something in the presenter that created it.
        // An effected visual rasterized by a second presenter must re-composite on THAT
        // presenter, never draw the first presenter's texture handles into its command list
        // (which would throw "Unknown texture handle" at Render).
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenterA = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using IVulkanPresenter presenterB = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenterA,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };

        ParseDropShadowChannel(graph);

        // Frame 1 on presenter A: cold render, caches the shadow/content textures in A's table.
        presenterA.Render(queue => graph.Rasterize(queue, null));
        byte shadowOnA = Channel(presenterA.ReadbackRgba().Span, 42, 20, 0);
        Assert.True(shadowOnA > 200, $"shadow should render on presenter A, got {shadowOnA}");

        // Frame 2 on presenter B: same graph, same visual slot, same effect. The slot's cache
        // (if any) holds A's handles; B must not draw them. This throws "Unknown texture
        // handle" before the fix.
        presenterB.Render(queue => graph.Rasterize(queue, null, 0, presenterB));
        byte shadowOnB = Channel(presenterB.ReadbackRgba().Span, 42, 20, 0);
        Assert.True(shadowOnB > 200, $"shadow should render on presenter B, got {shadowOnB}");
    }

    /// <summary>Appends the standard shadowed-rect scene: root visual 1, effected child 2,
    /// red brush 3, DropShadowEffect 5 (Depth=10, Direction=0, BlurRadius=3), render data 4
    /// with an (8,8,32,32) red rect, transparent clear.</summary>
    private static void AppendDropShadowScene(Writer bytes)
    {
        CreateVisual(bytes, 1); // root
        CreateVisual(bytes, 2); // effected child
        CreateBrush(bytes, 3, 1.0f, 0.0f, 0.0f); // red fill

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(5);
        bytes.UInt32((uint)MilResourceType.DropShadowEffect);
        bytes.UInt32((uint)MilCommandKind.DropShadowEffect);
        bytes.UInt32(5);
        bytes.Double(10); // ShadowDepth
        bytes.Float(1.0f); // Color scRGB r
        bytes.Float(0.0f); // g
        bytes.Float(0.0f); // b
        bytes.Float(1.0f); // a
        bytes.Double(0); // Direction (right)
        bytes.Double(1); // Opacity
        bytes.Double(3); // BlurRadius
        bytes.UInt32(0); // hShadowDepthAnimations
        bytes.UInt32(0); // hColorAnimations
        bytes.UInt32(0); // hDirectionAnimations
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hBlurRadiusAnimations
        bytes.Int32(0); // RenderingBias.Performance

        bytes.UInt32((uint)MilCommandKind.VisualSetEffect);
        bytes.UInt32(2);
        bytes.UInt32(5);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(0);

        AppendRenderData(bytes, 4, DrawRectangleBlob(8, 8, 32, 32, brushDependent: 1, penDependent: 0));
        AppendVisualSetContent(bytes, 2, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f); // transparent so shadow alpha checks are meaningful
    }

    /// <summary>Re-sends the SolidColorBrush resource for <paramref name="handle"/> with the
    /// given scRGB alpha, other fields unchanged (opacity 1, no transforms).</summary>
    private static void UpdateSolidColorBrushAlpha(Writer bytes, uint handle, float alpha)
    {
        bytes.UInt32((uint)MilCommandKind.SolidColorBrush);
        bytes.UInt32(handle);
        bytes.Double(1.0);
        bytes.Float(1.0f);
        bytes.Float(0.0f);
        bytes.Float(0.0f);
        bytes.Float(alpha);
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // hColorAnimations
    }

    /// <summary>Re-sends the DropShadowEffect resource for <paramref name="handle"/> with the
    /// given Direction, other fields unchanged (Depth=10, red, Opacity=1, BlurRadius=3).</summary>
    private static void UpdateDropShadowDirection(Writer bytes, uint handle, double direction)
    {
        bytes.UInt32((uint)MilCommandKind.DropShadowEffect);
        bytes.UInt32(handle);
        bytes.Double(10); // ShadowDepth
        bytes.Float(1.0f); // r
        bytes.Float(0.0f); // g
        bytes.Float(0.0f); // b
        bytes.Float(1.0f); // a
        bytes.Double(direction);
        bytes.Double(1); // Opacity
        bytes.Double(3); // BlurRadius
        bytes.UInt32(0); // hShadowDepthAnimations
        bytes.UInt32(0); // hColorAnimations
        bytes.UInt32(0); // hDirectionAnimations
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hBlurRadiusAnimations
        bytes.Int32(0); // RenderingBias.Performance
    }

    /// <summary>Re-sends the DropShadowEffect resource for <paramref name="handle"/> with the
    /// given Opacity, other fields unchanged (Depth=10, red, Direction=0, BlurRadius=3).</summary>
    private static void UpdateDropShadowOpacity(Writer bytes, uint handle, double opacity)
    {
        bytes.UInt32((uint)MilCommandKind.DropShadowEffect);
        bytes.UInt32(handle);
        bytes.Double(10); // ShadowDepth
        bytes.Float(1.0f); // r
        bytes.Float(0.0f); // g
        bytes.Float(0.0f); // b
        bytes.Float(1.0f); // a
        bytes.Double(0); // Direction
        bytes.Double(opacity);
        bytes.Double(3); // BlurRadius
        bytes.UInt32(0); // hShadowDepthAnimations
        bytes.UInt32(0); // hColorAnimations
        bytes.UInt32(0); // hDirectionAnimations
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hBlurRadiusAnimations
        bytes.Int32(0); // RenderingBias.Performance
    }

    /// <summary>Rightmost strongly-red pixel (r&gt;200, g&lt;60, b&lt;60) in a 64-wide readback.</summary>
    private static int MaxRedX(ReadOnlySpan<byte> pixels, int width)
    {
        int maxX = -1;
        int height = pixels.Length / 4 / width;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int offset = row + (x * 4);
                if (pixels[offset] > 200 && pixels[offset + 1] < 60 && pixels[offset + 2] < 60)
                {
                    maxX = Math.Max(maxX, x);
                }
            }
        }

        return maxX;
    }

    private static void ParseDropShadowChannel(SlaveGraph graph, double direction = 0)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // root
        CreateVisual(bytes, 2); // effected child
        CreateBrush(bytes, 3, 1.0f, 0.0f, 0.0f); // red fill

        // DropShadowEffect (handle 5): Depth=10, red, Direction, Opacity=1, BlurRadius=3.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(5);
        bytes.UInt32((uint)MilResourceType.DropShadowEffect);
        bytes.UInt32((uint)MilCommandKind.DropShadowEffect);
        bytes.UInt32(5);
        bytes.Double(10); // ShadowDepth
        bytes.Float(1.0f); // Color scRGB r
        bytes.Float(0.0f); // g
        bytes.Float(0.0f); // b
        bytes.Float(1.0f); // a
        bytes.Double(direction); // Direction
        bytes.Double(1); // Opacity
        bytes.Double(3); // BlurRadius
        bytes.UInt32(0); // hShadowDepthAnimations
        bytes.UInt32(0); // hColorAnimations
        bytes.UInt32(0); // hDirectionAnimations
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hBlurRadiusAnimations
        bytes.Int32(0); // RenderingBias.Performance

        bytes.UInt32((uint)MilCommandKind.VisualSetEffect);
        bytes.UInt32(2);
        bytes.UInt32(5);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(0);

        byte[] blob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 4, blob);
        AppendVisualSetContent(bytes, 2, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f); // transparent so shadow alpha checks are meaningful
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    private static void ParseBlurChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // root
        CreateVisual(bytes, 2); // blurred child
        CreateBrush(bytes, 3, 1.0f, 0.0f, 0.0f); // red fill

        // BlurEffect (handle 5): Radius=4, Gaussian, Performance.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(5);
        bytes.UInt32((uint)MilResourceType.BlurEffect);
        bytes.UInt32((uint)MilCommandKind.BlurEffect);
        bytes.UInt32(5);
        bytes.Double(4); // Radius
        bytes.UInt32(0); // hRadiusAnimations
        bytes.Int32(0); // KernelType.Gaussian
        bytes.Int32(0); // RenderingBias.Performance

        bytes.UInt32((uint)MilCommandKind.VisualSetEffect);
        bytes.UInt32(2);
        bytes.UInt32(5);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(0);

        byte[] blob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 4, blob);
        AppendVisualSetContent(bytes, 2, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f); // transparent so shadow alpha checks are meaningful
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    private static void ParseOpacityMaskChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // root
        CreateVisual(bytes, 2); // masked child
        CreateBrush(bytes, 3, 1.0f, 0.0f, 0.0f); // red fill

        // LinearGradientBrush mask (handle 4): white -> transparent along the horizontal axis,
        // RelativeToBoundingBox (mapped over the content bounds), Pad spread.
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(4);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(4);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(1.0); // EndPoint.X
        bytes.Double(0.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 1.0f, 1.0f, 1.0f, 1.0f); // white
        AppendStop(bytes, 1.0, 0.0f, 0.0f, 0.0f, 0.0f); // transparent

        bytes.UInt32((uint)MilCommandKind.VisualSetAlphaMask);
        bytes.UInt32(2);
        bytes.UInt32(4);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(0);

        byte[] blob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 5, blob);
        AppendVisualSetContent(bytes, 2, 5);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f); // transparent so mask alpha checks are meaningful
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(5), [new ResourceHandle(3)]);
    }

    private static void AppendStop(Writer bytes, double position, float r, float g, float b, float a)
    {
        bytes.Double(position);
        bytes.Float(r);
        bytes.Float(g);
        bytes.Float(b);
        bytes.Float(a);
    }

    private static void ParseMaskedAndPlainImageBrushChannel(SlaveGraph graph, Nova.Imaging.ManagedWicBitmap bitmap)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // root
        CreateVisual(bytes, 2); // masked child
        CreateVisual(bytes, 3); // plain child

        // BitmapSource slot 5: the pixels are seeded directly (the DUCE transport does the
        // same via SendCommandBitmapSource -> SetBitmapSourcePixels after resource creation).
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(5);
        bytes.UInt32((uint)MilResourceType.BitmapSource);

        // ImageBrush 4 over the bitmap: MILCMD_IMAGEBRUSH tile-brush fields then hImageSource.
        AppendImageBrush(bytes, 4, 5);

        // LinearGradientBrush mask 6: white -> transparent horizontally (RelativeToBoundingBox).
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(6);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(6);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(1.0); // EndPoint.X
        bytes.Double(0.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 1.0f, 1.0f, 1.0f, 1.0f); // white
        AppendStop(bytes, 1.0, 0.0f, 0.0f, 0.0f, 0.0f); // transparent

        bytes.UInt32((uint)MilCommandKind.VisualSetAlphaMask);
        bytes.UInt32(2);
        bytes.UInt32(6);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(0);

        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(3);
        bytes.UInt32(1);

        byte[] maskedBlob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 7, maskedBlob);
        AppendVisualSetContent(bytes, 2, 7);

        byte[] plainBlob = DrawRectangleBlob(40, 40, 16, 16, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 8, plainBlob);
        AppendVisualSetContent(bytes, 3, 8);

        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f); // transparent so mask alpha checks are meaningful
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(7), [new ResourceHandle(4)]);
        graph.SetRenderDataDependents(new ResourceHandle(8), [new ResourceHandle(4)]);
        graph.SetBitmapSourcePixels(new ResourceHandle(5), bitmap);
    }

    private static void ParseImageBrushChannel(SlaveGraph graph, Nova.Imaging.ManagedWicBitmap bitmap)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1); // root

        // BitmapSource slot 5 (pixels seeded directly, as the DUCE transport does).
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(5);
        bytes.UInt32((uint)MilResourceType.BitmapSource);

        // ImageBrush 4 over the bitmap: MILCMD_IMAGEBRUSH tile-brush fields then hImageSource.
        AppendImageBrush(bytes, 4, 5);

        byte[] blob = DrawRectangleBlob(0, 0, 64, 64, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 6, blob);
        AppendVisualSetContent(bytes, 1, 6);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(6), [new ResourceHandle(4)]);
        graph.SetBitmapSourcePixels(new ResourceHandle(5), bitmap);
    }

    /// <summary>Appends the MILCMD_IMAGEBRUSH resource creation (tile-brush fields then
    /// hImageSource), Stretch=Fill, TileMode=None, RelativeToBoundingBox units.</summary>
    private static void AppendImageBrush(Writer bytes, uint handle, uint imageSource)
    {
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(handle);
        bytes.UInt32((uint)MilResourceType.ImageBrush);
        bytes.UInt32((uint)MilCommandKind.ImageBrush);
        bytes.UInt32(handle);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // Viewport.X
        bytes.Double(0.0); // Viewport.Y
        bytes.Double(1.0); // Viewport.Width
        bytes.Double(1.0); // Viewport.Height
        bytes.Double(0.0); // Viewbox.X
        bytes.Double(0.0); // Viewbox.Y
        bytes.Double(1.0); // Viewbox.Width
        bytes.Double(1.0); // Viewbox.Height
        bytes.Double(0.0); // CacheInvalidationThresholdMinimum
        bytes.Double(0.0); // CacheInvalidationThresholdMaximum
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(1); // ViewportUnits: RelativeToBoundingBox
        bytes.UInt32(1); // ViewboxUnits: RelativeToBoundingBox
        bytes.UInt32(0); // hViewportAnimations
        bytes.UInt32(0); // hViewboxAnimations
        bytes.UInt32(1); // Stretch.Fill
        bytes.UInt32(0); // TileMode.None
        bytes.UInt32(0); // AlignmentX.Center
        bytes.UInt32(0); // AlignmentY.Center
        bytes.UInt32(0); // CachingHint
        bytes.UInt32(imageSource); // hImageSource
    }

    [Fact]
    public void Rasterize_PushOpacityMask_GradientMasksContent()
    {
        // Regression: the DUCE PushOpacityMask/Pop pair (the element-level OpacityMask
        // brush — the dashboard hero's gradient fade) was a no-op, so the masked
        // content rendered at full opacity. The parser now collapses the pair into a
        // masked range and the slave composites the range with the mask's per-pixel
        // alpha.
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph
        {
            Presenter = presenter,
            OffscreenFactory = size => device.CreateOffscreenPresenter(size)
        };
        ParsePushOpacityMaskChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // White -> transparent vertical mask over the red rect: the top stays opaque
        // red, the bottom attenuates toward transparent.

        Assert.True(Channel(pixels, 32, 4, 0) > 200, $"top must be strong red, got r={Channel(pixels, 32, 4, 0)}");
        Assert.True(Channel(pixels, 32, 4, 3) > 200, $"top must be opaque, got a={Channel(pixels, 32, 4, 3)}");
        Assert.True(Channel(pixels, 32, 60, 3) < 30, $"bottom must be attenuated, got a={Channel(pixels, 32, 60, 3)}");
    }

    private static void ParsePushOpacityMaskChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        // LinearGradientBrush 2: white -> transparent, absolute (0,0)-(0,64).
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(0.0); // EndPoint.X
        bytes.Double(64.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(0); // MappingMode: Absolute
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(2 * 24); // GradientStopsSize
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, 1.0f, 1.0f, 1.0f, 1.0f); // white
        AppendStop(bytes, 1.0, 0.0f, 0.0f, 0.0f, 0.0f); // transparent

        // Solid red brush 3.
        CreateBrush(bytes, 3, 1.0f, 0.0f, 0.0f);

        // Render data 4: PushOpacityMask(mask), DrawRectangle(fill), Pop — each op is
        // its own record ({int size; uint kind; payload}).
        var renderData = new Writer();
        renderData.Int32(32);
        renderData.UInt32((uint)MilCommandKind.PushOpacityMask);
        renderData.Float(0); // boundingBoxCacheLocalSpace (ignored at visit)
        renderData.Float(0);
        renderData.Float(1);
        renderData.Float(1);
        renderData.UInt32(1); // mask dependent (the gradient)
        renderData.UInt32(0); // QWORD pad
        renderData.Int32(48);
        renderData.UInt32((uint)MilCommandKind.DrawRectangle);
        renderData.Double(0);
        renderData.Double(0);
        renderData.Double(64);
        renderData.Double(64);
        renderData.UInt32(2); // fill dependent (the red brush)
        renderData.UInt32(0); // pen
        renderData.Int32(8);
        renderData.UInt32((uint)MilCommandKind.Pop);
        AppendRenderData(bytes, 4, renderData.ToArray());

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, 0.0f, 0.0f, 0.0f, 0.0f); // transparent so the mask alpha is observable
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(2), new ResourceHandle(3)]);
    }

    // ---------------------------------------------------------------------
    // sRGB mid-tone encoding: colors arrive on the wire as scRGB (linear)
    // floats; the slave must re-encode them to sRGB before they reach the
    // UNORM raster, so a mid-grey #808080 stores as 128, not linear 55.
    // 0 and 255 are sRGB fixed points, which is why the pure-colour baseline
    // could never observe this class of bug.
    // ---------------------------------------------------------------------

    private const float MidGreyScRgb = 0.2158605f; // scRGB float for sRGB #808080 (linear(128/255))

    [Fact]
    public void Rasterize_MidGreySolidFill_StoresSrgbByte128()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseMidGreyRectChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // sRGB #808080 must store as 128 (the sRGB byte), not the linear byte 55.
        Assert.Equal(128, Channel(pixels, 24, 24, 0));
        Assert.Equal(128, Channel(pixels, 24, 24, 1));
        Assert.Equal(128, Channel(pixels, 24, 24, 2));
        Assert.Equal(255, Channel(pixels, 24, 24, 3));
    }

    [Fact]
    public void Rasterize_MidGreyPen_StoresSrgbByte128OnRing()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseMidGreyPenChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Stroke-only ring of mid-grey: the ring pixel must be sRGB 128.
        Assert.Equal(0, Channel(pixels, 24, 24, 0)); // interior stays clear
        Assert.Equal(128, Channel(pixels, 24, 8, 0)); // top edge of the ring
        Assert.Equal(128, Channel(pixels, 24, 8, 1));
        Assert.Equal(128, Channel(pixels, 24, 8, 2));
    }

    [Fact]
    public void Rasterize_MidGreyGlyph_StoresSrgbByte128()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using var atlas = new GlyphAtlas(presenter, new PixelSize(64, 64));
        using var shaper = new TextShaper();

        Typeface? typeface = ResolveTypeface(shaper);
        if (typeface is null)
        {
            return; // no system font — skip
        }

        using (typeface)
        {
            Span<PositionedGlyph> shaped = stackalloc PositionedGlyph[8];
            if (shaper.Shape(typeface, "A", 16, ShapeOptions.Default, shaped) == 0)
            {
                return;
            }

            var graph = new SlaveGraph();
            const ulong token = 42;
            graph.RegisterFont(new FontFaceToken(token), typeface);
            ParseMidGreyGlyphChannel(graph, token, shaped[0].Id.GlyphIndex);
            GlyphAtlas liveAtlas = atlas;
            presenter.Render(queue => graph.Rasterize(queue, liveAtlas));
            ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

            // Glyph tint is the mid-grey foreground solid: any fully-covered glyph
            // pixel must be sRGB 128 (not 55). Find the glyph's darkest pixel.
            int maxR = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                // Coverage blends toward the grey tint; the densest pixel approaches 128.
                if (pixels[i] > maxR)
                {
                    maxR = pixels[i];
                }
            }

            Assert.InRange(maxR, 120, 135); // sRGB 128 coverage peak, not linear 55
        }
    }

    [Fact]
    public void Rasterize_MidGreyClearColor_StoresSrgbByte128()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        var graph = new SlaveGraph();
        ParseMidGreyClearChannel(graph);

        presenter.Render(queue => graph.Rasterize(queue, null));
        ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;

        // Full-surface clear of mid-grey must store sRGB 128.
        Assert.Equal(128, Channel(pixels, 10, 10, 0));
        Assert.Equal(128, Channel(pixels, 10, 10, 1));
        Assert.Equal(128, Channel(pixels, 10, 10, 2));
        Assert.Equal(255, Channel(pixels, 10, 10, 3));
    }

    [Fact]
    public void Rasterize_SolidAndSingleStopGradient_ProduceSameSrgbByte()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter solidPresenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));
        using IVulkanPresenter gradientPresenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        var solidGraph = new SlaveGraph { Presenter = solidPresenter };
        ParseMidGreyRectChannel(solidGraph);
        solidPresenter.Render(queue => solidGraph.Rasterize(queue, null));

        var gradientGraph = new SlaveGraph { Presenter = gradientPresenter };
        ParseSingleStopGreyGradientChannel(gradientGraph);
        gradientPresenter.Render(queue => gradientGraph.Rasterize(queue, null));

        ReadOnlySpan<byte> solid = solidPresenter.ReadbackRgba().Span;
        ReadOnlySpan<byte> gradient = gradientPresenter.ReadbackRgba().Span;

        // A single-stop gradient of the same grey and a solid fill of that grey must
        // store the identical sRGB byte. This pairing exposed the bug: the gradient
        // LUT was already sRGB-encoded while the solid path was not.
        Assert.Equal(Channel(solid, 24, 24, 0), Channel(gradient, 24, 24, 0));
        Assert.Equal(128, Channel(gradient, 24, 24, 0));
        Assert.Equal(128, Channel(gradient, 24, 24, 1));
        Assert.Equal(128, Channel(gradient, 24, 24, 2));
    }

    private static void ParseMidGreyRectChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2, MidGreyScRgb, MidGreyScRgb, MidGreyScRgb); // mid-grey fill

        byte[] blob = DrawRectangleBlob(8, 8, 48, 48, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }

    private static void ParseMidGreyPenChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2, MidGreyScRgb, MidGreyScRgb, MidGreyScRgb); // mid-grey pen brush
        CreatePen(bytes, 3, 2, 4.0); // pen -> brush 2, thickness 4

        byte[] blob = DrawRectangleBlob(8, 8, 32, 32, brushDependent: 0, penDependent: 1);
        AppendRenderData(bytes, 4, blob);

        AppendVisualSetContent(bytes, 1, 4);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(4), [new ResourceHandle(3)]);
    }

    private static void ParseMidGreyGlyphChannel(SlaveGraph graph, ulong token, uint glyphIndex)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2, MidGreyScRgb, MidGreyScRgb, MidGreyScRgb); // mid-grey foreground
        // Font is registered by the test (graph.RegisterFont(token, typeface)) after parsing,
        // matching the existing ParseGlyphChannel pattern.

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(4);
        bytes.UInt32((uint)MilResourceType.GlyphRun);
        bytes.UInt32((uint)MilCommandKind.GlyphRunCreate);
        bytes.UInt32(4);
        bytes.UInt64(token);
        bytes.UInt16(0); // flags: no offsets
        bytes.UInt16(0); // packing
        bytes.Float(10);
        bytes.Float(20);
        bytes.Float(16); // emSize
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(10);
        bytes.Double(20);
        bytes.UInt16(1); // glyphCount
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16((ushort)glyphIndex);
        bytes.Float(16);

        var renderData = new Writer();
        renderData.Int32(16);
        renderData.UInt32((uint)MilCommandKind.DrawGlyphRun);
        renderData.UInt32(1); // foreground dependent (1-based index -> dependents[0] = brush 2)
        renderData.UInt32(2); // glyphRun dependent (1-based index -> dependents[1] = glyphRun 4)
        byte[] blob = renderData.ToArray();
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2), new ResourceHandle(4)]);
    }

    private static void ParseMidGreyClearChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);
        CreateBrush(bytes, 2);

        byte[] blob = DrawRectangleBlob(8, 8, 48, 48, brushDependent: 0, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes, MidGreyScRgb, MidGreyScRgb, MidGreyScRgb, 1.0f); // mid-grey clear
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }

    private static void ParseSingleStopGreyGradientChannel(SlaveGraph graph)
    {
        var bytes = new Writer();
        CreateVisual(bytes, 1);

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.LinearGradientBrush);
        bytes.UInt32((uint)MilCommandKind.LinearGradientBrush);
        bytes.UInt32(2);
        bytes.Double(1.0); // opacity
        bytes.Double(0.0); // StartPoint.X
        bytes.Double(0.0); // StartPoint.Y
        bytes.Double(1.0); // EndPoint.X
        bytes.Double(0.0); // EndPoint.Y
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hTransform
        bytes.UInt32(0); // hRelativeTransform
        bytes.UInt32(0); // ColorInterpolationMode (sRGB linear)
        bytes.UInt32(1); // MappingMode: RelativeToBoundingBox
        bytes.UInt32(0); // SpreadMethod: Pad
        bytes.UInt32(1 * 24); // GradientStopsSize (one stop)
        bytes.UInt32(0); // hStartPointAnimations
        bytes.UInt32(0); // hEndPointAnimations
        AppendStop(bytes, 0.0, MidGreyScRgb, MidGreyScRgb, MidGreyScRgb, 1.0f); // single mid-grey stop

        byte[] blob = DrawRectangleBlob(8, 8, 48, 48, brushDependent: 1, penDependent: 0);
        AppendRenderData(bytes, 3, blob);

        AppendVisualSetContent(bytes, 1, 3);
        AppendTargetSetRoot(bytes, 1);
        AppendTargetSetClearColor(bytes);
        MilCommandParser.ParseChannel(bytes.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
    }
}
