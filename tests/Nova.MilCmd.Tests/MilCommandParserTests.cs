using Nova.Geometry;

namespace Nova.MilCmd.Tests;

public sealed class MilCommandParserTests
{
    [Fact]
    public void ParseChannel_VisualSetOffset_DeliversDoublesAndHandle()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.VisualSetOffset);
        bytes.UInt32(7);
        bytes.Double(1.5);
        bytes.Double(-2.25);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, double offsetX, double offsetY) = Assert.Single(visitor.SetOffsets);
        Assert.Equal(new ResourceHandle(7), handle);
        Assert.Equal(1.5, offsetX, 12);
        Assert.Equal(-2.25, offsetY, 12);
    }

    [Fact]
    public void ParseChannel_MultipleRecords_VisitedInOrder()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.TransportSyncFlush);
        bytes.UInt32((uint)MilCommandKind.VisualCreate);
        bytes.UInt32(3);
        bytes.UInt32((uint)MilCommandKind.VisualSetAlpha);
        bytes.UInt32(3);
        bytes.Double(0.5);
        bytes.UInt32((uint)MilCommandKind.VisualRemoveAllChildren);
        bytes.UInt32(3);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        Assert.Equal(1, visitor.SyncFlushes);
        Assert.Equal(new ResourceHandle(3), Assert.Single(visitor.VisualCreates));
        Assert.Equal(1, visitor.RemoveAllChildren);
        (ResourceHandle handle, double alpha) = Assert.Single(visitor.SetAlphas);
        Assert.Equal(new ResourceHandle(3), handle);
        Assert.Equal(0.5, alpha, 12);
    }

    [Fact]
    public void ParseChannel_VisualInsertChildAt_DeliversChildAndIndex()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.VisualInsertChildAt);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(3);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, ResourceHandle child, uint index) = Assert.Single(visitor.InsertChildAts);
        Assert.Equal(new ResourceHandle(1), handle);
        Assert.Equal(new ResourceHandle(2), child);
        Assert.Equal(3u, index);
    }

    [Fact]
    public void ParseChannel_TargetSetClearColor_DeliversColor()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.TargetSetClearColor);
        bytes.UInt32(9);
        bytes.Float(0.25f);
        bytes.Float(0.5f);
        bytes.Float(0.75f);
        bytes.Float(1.0f);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, ColorRgba color) = Assert.Single(visitor.SetClearColors);
        Assert.Equal(new ResourceHandle(9), handle);
        Assert.Equal(new ColorRgba(0.25f, 0.5f, 0.75f, 1.0f), color);
    }

    [Fact]
    public void ParseChannel_TargetInvalidate_ConvertsRectToWidthHeight()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.TargetInvalidate);
        bytes.UInt32(4);
        bytes.Int32(10);
        bytes.Int32(20);
        bytes.Int32(110);
        bytes.Int32(70);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, Rect dirty) = Assert.Single(visitor.Invalidates);
        Assert.Equal(new ResourceHandle(4), handle);
        Assert.Equal(new Rect(10, 20, 100, 50), dirty);
    }

    [Fact]
    public void ParseChannel_TranslateTransform_SkipsAnimationHandles()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.TranslateTransform);
        bytes.UInt32(5);
        bytes.Double(3.5);
        bytes.Double(-1.5);
        bytes.UInt32(0);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, double x, double y) = Assert.Single(visitor.TranslateTransforms);
        Assert.Equal(new ResourceHandle(5), handle);
        Assert.Equal(3.5, x, 12);
        Assert.Equal(-1.5, y, 12);
    }

    [Fact]
    public void ParseChannel_MatrixTransform_DeliversSixDoubles()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.MatrixTransform);
        bytes.UInt32(6);
        bytes.Double(1);
        bytes.Double(2);
        bytes.Double(3);
        bytes.Double(4);
        bytes.Double(5);
        bytes.Double(6);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, Matrix3x2 matrix) = Assert.Single(visitor.MatrixTransforms);
        Assert.Equal(new ResourceHandle(6), handle);
        Assert.Equal(new Matrix3x2(1, 2, 3, 4, 5, 6), matrix);
    }

    [Fact]
    public void ParseChannel_LineGeometry_DeliversPointsAndTransform()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.LineGeometry);
        bytes.UInt32(8);
        bytes.Double(1);
        bytes.Double(2);
        bytes.Double(3);
        bytes.Double(4);
        bytes.UInt32(9);
        bytes.UInt32(0);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, Point start, Point endPoint, ResourceHandle transform) = Assert.Single(visitor.LineGeometries);
        Assert.Equal(new ResourceHandle(8), handle);
        Assert.Equal(new Point(1, 2), start);
        Assert.Equal(new Point(3, 4), endPoint);
        Assert.Equal(new ResourceHandle(9), transform);
    }

    [Fact]
    public void ParseChannel_RectangleGeometry_DeliversRadiusAndRect()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.RectangleGeometry);
        bytes.UInt32(10);
        bytes.Double(2.5);
        bytes.Double(1.25);
        bytes.Double(100);
        bytes.Double(200);
        bytes.Double(300);
        bytes.Double(400);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, Rect rectangle, double radiusX, double radiusY, ResourceHandle transform) =
            Assert.Single(visitor.RectangleGeometries);
        Assert.Equal(new ResourceHandle(10), handle);
        Assert.Equal(new Rect(100, 200, 300, 400), rectangle);
        Assert.Equal(2.5, radiusX, 12);
        Assert.Equal(1.25, radiusY, 12);
        Assert.Equal(ResourceHandle.Null, transform);
    }

    [Fact]
    public void ParseChannel_SolidColorBrush_DeliversOpacityColorAndTransform()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.SolidColorBrush);
        bytes.UInt32(11);
        bytes.Double(0.8);
        bytes.Float(1.0f);
        bytes.Float(0.0f);
        bytes.Float(0.0f);
        bytes.Float(1.0f);
        bytes.UInt32(0);
        bytes.UInt32(12);
        bytes.UInt32(0);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, double opacity, ColorRgba color, ResourceHandle transform) = Assert.Single(visitor.SolidColorBrushes);
        Assert.Equal(new ResourceHandle(11), handle);
        Assert.Equal(0.8, opacity, 12);
        Assert.Equal(new ColorRgba(1.0f, 0.0f, 0.0f, 1.0f), color);
        Assert.Equal(new ResourceHandle(12), transform);
    }

    [Fact]
    public void ParseChannel_Pen_DeliversBrushThicknessAndMiter()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.Pen);
        bytes.UInt32(13);
        bytes.Double(2.0);
        bytes.Double(1.5);
        bytes.UInt32(14);
        bytes.UInt32(0);
        bytes.UInt32(1);
        bytes.UInt32(1);
        bytes.UInt32(1);
        bytes.UInt32(1);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, ResourceHandle brush, double thickness, double miterLimit) = Assert.Single(visitor.Pens);
        Assert.Equal(new ResourceHandle(13), handle);
        Assert.Equal(new ResourceHandle(14), brush);
        Assert.Equal(2.0, thickness, 12);
        Assert.Equal(1.5, miterLimit, 12);
    }

    [Fact]
    public void ParseChannel_ChannelCreateResource_DeliversType()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(15);
        bytes.UInt32((uint)MilResourceType.Visual);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, MilResourceType type) = Assert.Single(visitor.CreatedResources);
        Assert.Equal(new ResourceHandle(15), handle);
        Assert.Equal(MilResourceType.Visual, type);
    }

    [Fact]
    public void ParseChannel_ChannelDeleteResource_IgnoresType()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.ChannelDeleteResource);
        bytes.UInt32(16);
        bytes.UInt32((uint)MilResourceType.RenderData);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        Assert.Equal(new ResourceHandle(16), Assert.Single(visitor.DeletedResources));
    }

    [Fact]
    public void ParseChannel_RenderDataCommand_PassesBlobToVisitor()
    {
        byte[] blob = [1, 2, 3, 4, 5, 6];
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.RenderData);
        bytes.UInt32(17);
        bytes.UInt32((uint)blob.Length);
        bytes.Bytes(blob);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, byte[] data) = Assert.Single(visitor.RenderDatas);
        Assert.Equal(new ResourceHandle(17), handle);
        Assert.Equal(blob, data);
    }

    [Fact]
    public void ParseChannel_GlyphRunCreate_DeliversTokenAndArrays()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.GlyphRunCreate);
        bytes.UInt32(18);
        bytes.UInt64(0x1122334455667788UL);
        bytes.UInt16(0); // flags: no offsets
        bytes.UInt16(0); // packing
        bytes.Float(10.5f); // originX
        bytes.Float(20.25f); // originY
        bytes.Float(14.5f); // emSize
        bytes.Double(0); // ManagedBounds x
        bytes.Double(0); // y
        bytes.Double(10); // width
        bytes.Double(20); // height
        bytes.UInt16(2); // glyphCount
        bytes.UInt16(0); // packing
        bytes.UInt16(0); // bidi
        bytes.UInt16(0); // packing
        bytes.UInt16(0); // measuring method
        bytes.UInt16(0); // trailing padding
        bytes.UInt16(0x41);
        bytes.UInt16(0x42);
        bytes.Float(12.5f);
        bytes.Float(7.25f);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, FontFaceToken font, Point origin, float emSize, ushort[] glyphs, float[] advances) =
            Assert.Single(visitor.GlyphRuns);
        Assert.Equal(new ResourceHandle(18), handle);
        Assert.Equal(0x1122334455667788UL, font.Value);
        Assert.Equal(new Point(10.5, 20.25), origin);
        Assert.Equal(14.5f, emSize, 5);
        Assert.Equal(new ushort[] { 0x41, 0x42 }, glyphs);
        Assert.Equal(2, advances.Length);
        Assert.Equal(12.5f, advances[0], 5);
        Assert.Equal(7.25f, advances[1], 5);
    }

    [Fact]
    public void ParseChannel_GlyphRunCreate_WithHasOffsets_SkipsOffsetsAfterAdvances()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.GlyphRunCreate);
        bytes.UInt32(18);
        bytes.UInt64(0x99UL);
        bytes.UInt16(0x10); // HasOffsets
        bytes.UInt16(0); // packing
        bytes.Float(0); // originX
        bytes.Float(0); // originY
        bytes.Float(12.0f); // emSize
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(0);
        bytes.UInt16(1); // glyphCount
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0x7);
        bytes.Float(9.0f); // advance
        bytes.Float(1.0f); // offset x
        bytes.Float(2.0f); // offset y

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (_, _, _, _, ushort[] glyphs, float[] advances) = Assert.Single(visitor.GlyphRuns);
        Assert.Equal(new ushort[] { 0x7 }, glyphs);
        Assert.Equal(9.0f, Assert.Single(advances), 5);
    }

    [Fact]
    public void ParseChannel_UnknownFixedKind_ReportsViaVisitUnknown()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.DoubleResource);
        bytes.UInt32(20);
        bytes.Double(42.5);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (MilCommandKind kind, byte[] payload) = Assert.Single(visitor.Unknowns);
        Assert.Equal(MilCommandKind.DoubleResource, kind);
        Assert.Equal(12, payload.Length);
    }

    [Fact]
    public void ParseChannel_VisualSetGuidelineCollection_SkipsFloatArrayNotDoubles()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.VisualSetGuidelineCollection);
        bytes.UInt32(7);
        bytes.UInt16(1);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.UInt16(0);
        bytes.Float(12.5f);
        bytes.UInt32((uint)MilCommandKind.TransportSyncFlush);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (MilCommandKind kind, byte[] payload) = Assert.Single(visitor.Unknowns);
        Assert.Equal(MilCommandKind.VisualSetGuidelineCollection, kind);
        Assert.Equal(16, payload.Length);
        Assert.Equal(1, visitor.SyncFlushes);
    }

    [Fact]
    public void ParseChannel_TransformGroup_DeliversChildren()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.TransformGroup);
        bytes.UInt32(21);
        bytes.UInt32(8); // ChildrenSize: two resource handles
        bytes.UInt32(22);
        bytes.UInt32(23);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, ResourceHandle[] children) = Assert.Single(visitor.TransformGroups);
        Assert.Equal(new ResourceHandle(21), handle);
        Assert.Equal([new ResourceHandle(22), new ResourceHandle(23)], children);
        Assert.Empty(visitor.Unknowns);
    }

    [Fact]
    public void ParseChannel_VisualSetEffect_DeliversEffectHandle()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.VisualSetEffect);
        bytes.UInt32(11);
        bytes.UInt32(42);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, ResourceHandle effect) = Assert.Single(visitor.VisualSetEffects);
        Assert.Equal(new ResourceHandle(11), handle);
        Assert.Equal(new ResourceHandle(42), effect);
    }

    [Fact]
    public void ParseChannel_VisualSetAlphaMask_DeliversMaskHandle()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.VisualSetAlphaMask);
        bytes.UInt32(12);
        bytes.UInt32(43);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, ResourceHandle opacityMask) = Assert.Single(visitor.VisualSetAlphaMasks);
        Assert.Equal(new ResourceHandle(12), handle);
        Assert.Equal(new ResourceHandle(43), opacityMask);
    }

    [Fact]
    public void ParseChannel_BlurEffect_DecodesAllFields()
    {
        // MILCMD_BLUREFFECT (wgx_commands.cs pin 1346571e): Handle(4) Radius(double)
        // hRadiusAnimations(4) KernelType(4) RenderingBias(4). Distinct sentinel values pin
        // every offset: any layout shift breaks the round-trip.
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.BlurEffect);
        bytes.UInt32(0xB2);
        bytes.Double(6.25);
        bytes.UInt32(0); // hRadiusAnimations
        bytes.Int32(1);  // KernelType.Box
        bytes.Int32(0);  // RenderingBias.Performance

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, double radius, int kernelType, int renderingBias) = Assert.Single(visitor.BlurEffects);
        Assert.Equal(new ResourceHandle(0xB2), handle);
        Assert.Equal(6.25, radius, 12);
        Assert.Equal(1, kernelType);
        Assert.Equal(0, renderingBias);
    }

    [Fact]
    public void ParseChannel_DropShadowEffect_DecodesAllFields()
    {
        // MILCMD_DROPSHADOWEFFECT (wgx_commands.cs pin 1346571e): Handle(4) ShadowDepth(double)
        // Color(MilColorF, 4 floats) Direction(double) Opacity(double) BlurRadius(double)
        // 5 animation handles (4 each) RenderingBias(4). Distinct sentinel values pin every
        // offset: any layout shift breaks the round-trip.
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.DropShadowEffect);
        bytes.UInt32(0xA1);
        bytes.Double(-7.5);
        bytes.Float(0.25f);
        bytes.Float(0.5f);
        bytes.Float(0.75f);
        bytes.Float(1.0f);
        bytes.Double(123.25);
        bytes.Double(0.6);
        bytes.Double(4.5);
        bytes.UInt32(0); // hShadowDepthAnimations
        bytes.UInt32(0); // hColorAnimations
        bytes.UInt32(0); // hDirectionAnimations
        bytes.UInt32(0); // hOpacityAnimations
        bytes.UInt32(0); // hBlurRadiusAnimations
        bytes.Int32(1);  // RenderingBias.Quality

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel(bytes.ToArray(), visitor);

        (ResourceHandle handle, double shadowDepth, ColorRgba color, double direction, double opacity, double blurRadius, int renderingBias) = Assert.Single(visitor.DropShadowEffects);
        Assert.Equal(new ResourceHandle(0xA1), handle);
        Assert.Equal(-7.5, shadowDepth, 12);
        Assert.Equal(new ColorRgba(0.25f, 0.5f, 0.75f, 1.0f), color);
        Assert.Equal(123.25, direction, 12);
        Assert.Equal(0.6, opacity, 12);
        Assert.Equal(4.5, blurRadius, 12);
        Assert.Equal(1, renderingBias);
    }

    [Fact]
    public void ParseChannel_UnknownUnsized_Throws()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.BitmapSource);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        MilParseException ex = Assert.Throws<MilParseException>(() => MilCommandParser.ParseChannel(bytes.ToArray(), visitor));
        Assert.Equal(0, ex.Offset);
    }

    [Fact]
    public void ParseChannel_InvalidKindValue_Throws()
    {
        var bytes = new Writer();
        bytes.UInt32(0x8e); // not a declared MilCommandKind

        var visitor = new RecordingVisitor();
        _ = Assert.Throws<MilParseException>(() => MilCommandParser.ParseChannel(bytes.ToArray(), visitor));
    }

    [Fact]
    public void ParseChannel_TruncatedBody_Throws()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.VisualSetOffset);
        bytes.UInt32(1);
        bytes.Double(1.0);
        bytes.Int32(0); // only 4 of the required 8 bytes for offsetY

        var visitor = new RecordingVisitor();
        MilParseException ex = Assert.Throws<MilParseException>(() => MilCommandParser.ParseChannel(bytes.ToArray(), visitor));
        Assert.Equal(16, ex.Offset);
    }

    [Fact]
    public void ParseChannel_TruncatedType_Throws()
    {
        var visitor = new RecordingVisitor();
        MilParseException ex = Assert.Throws<MilParseException>(() => MilCommandParser.ParseChannel([0x1a, 0x00], visitor));
        Assert.Equal(0, ex.Offset);
    }

    [Fact]
    public void ParseChannel_EmptyBuffer_VisitsNothing()
    {
        var visitor = new RecordingVisitor();
        MilCommandParser.ParseChannel([], visitor);

        Assert.Equal(0, visitor.SyncFlushes);
        Assert.Empty(visitor.VisualCreates);
    }

    [Fact]
    public void ParseChannel_NullVisitor_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => MilCommandParser.ParseChannel([], null!));
    }

    [Fact]
    public void ParseRenderData_DrawLine_MapsOneBasedDependents()
    {
        ResourceHandle[] dependents = [new(101), new(202)];

        var bytes = new Writer();
        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawLine);
        bytes.Double(1);
        bytes.Double(2);
        bytes.Double(3);
        bytes.Double(4);
        bytes.UInt32(1); // dependent index 1 -> 101
        bytes.Int32(0); // padding

        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawLine);
        bytes.Double(5);
        bytes.Double(6);
        bytes.Double(7);
        bytes.Double(8);
        bytes.UInt32(2); // dependent index 2 -> 202
        bytes.Int32(0);

        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawLine);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(0);
        bytes.UInt32(0); // null
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), dependents, visitor);

        Assert.Equal(3, visitor.DrawLines.Count);
        Assert.Equal(new ResourceHandle(101), visitor.DrawLines[0].Pen);
        Assert.Equal(new ResourceHandle(202), visitor.DrawLines[1].Pen);
        Assert.Equal(ResourceHandle.Null, visitor.DrawLines[2].Pen);
        Assert.Equal(new Point(1, 2), visitor.DrawLines[0].Start);
        Assert.Equal(new Point(3, 4), visitor.DrawLines[0].EndPoint);
    }

    [Fact]
    public void ParseRenderData_DrawRectangle_UsesQwordAlignedSize()
    {
        var bytes = new Writer();
        bytes.Int32(48); // 8 header + 40 payload, no padding needed
        bytes.UInt32((uint)MilCommandKind.DrawRectangle);
        bytes.Double(10);
        bytes.Double(20);
        bytes.Double(30);
        bytes.Double(40);
        bytes.UInt32(1);
        bytes.UInt32(2);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1), new ResourceHandle(2)], visitor);

        (Rect rectangle, ResourceHandle brush, ResourceHandle pen) = Assert.Single(visitor.DrawRectangles);
        Assert.Equal(new Rect(10, 20, 30, 40), rectangle);
        Assert.Equal(new ResourceHandle(1), brush);
        Assert.Equal(new ResourceHandle(2), pen);
    }

    [Fact]
    public void ParseRenderData_MultipleRecords_VisitedInOrder()
    {
        var bytes = new Writer();
        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawLine);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(1);
        bytes.Double(1);
        bytes.UInt32(0);
        bytes.Int32(0);

        bytes.Int32(16);
        bytes.UInt32((uint)MilCommandKind.PushOpacity);
        bytes.Double(0.5);

        bytes.Int32(8);
        bytes.UInt32((uint)MilCommandKind.Pop);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [], visitor);

        _ = Assert.Single(visitor.DrawLines);
        Assert.Equal(0.5, Assert.Single(visitor.PushOpacities), 12);
        Assert.Equal(1, visitor.Pops);
    }

    [Fact]
    public void ParseRenderData_DrawRoundedRectangle_DeliversDoubles()
    {
        var bytes = new Writer();
        bytes.Int32(64);
        bytes.UInt32((uint)MilCommandKind.DrawRoundedRectangle);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(100);
        bytes.Double(50);
        bytes.Double(4.5);
        bytes.Double(3.5);
        bytes.UInt32(1);
        bytes.UInt32(2);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1), new ResourceHandle(2)], visitor);

        (Rect rectangle, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen) =
            Assert.Single(visitor.DrawRoundedRectangles);
        Assert.Equal(new Rect(0, 0, 100, 50), rectangle);
        Assert.Equal(4.5, radiusX, 12);
        Assert.Equal(3.5, radiusY, 12);
        Assert.Equal(new ResourceHandle(1), brush);
        Assert.Equal(new ResourceHandle(2), pen);
    }

    [Fact]
    public void ParseRenderData_DrawEllipse_DeliversCenterAndRadii()
    {
        var bytes = new Writer();
        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawEllipse);
        bytes.Double(5);
        bytes.Double(6);
        bytes.Double(2.0);
        bytes.Double(1.0);
        bytes.UInt32(1);
        bytes.UInt32(2);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1), new ResourceHandle(2)], visitor);

        (Point center, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen) =
            Assert.Single(visitor.DrawEllipses);
        Assert.Equal(new Point(5, 6), center);
        Assert.Equal(2.0, radiusX, 12);
        Assert.Equal(1.0, radiusY, 12);
        Assert.Equal(new ResourceHandle(1), brush);
        Assert.Equal(new ResourceHandle(2), pen);
    }

    [Fact]
    public void ParseRenderData_DrawGeometry_DeliversThreeDependents()
    {
        var bytes = new Writer();
        bytes.Int32(24);
        bytes.UInt32((uint)MilCommandKind.DrawGeometry);
        bytes.UInt32(1);
        bytes.UInt32(2);
        bytes.UInt32(3);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1), new ResourceHandle(2), new ResourceHandle(3)], visitor);

        (ResourceHandle brush, ResourceHandle pen, ResourceHandle geometry) = Assert.Single(visitor.DrawGeometries);
        Assert.Equal(new ResourceHandle(1), brush);
        Assert.Equal(new ResourceHandle(2), pen);
        Assert.Equal(new ResourceHandle(3), geometry);
    }

    [Fact]
    public void ParseRenderData_DrawGlyphRun_DeliversTwoDependents()
    {
        var bytes = new Writer();
        bytes.Int32(16);
        bytes.UInt32((uint)MilCommandKind.DrawGlyphRun);
        bytes.UInt32(1);
        bytes.UInt32(2);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1), new ResourceHandle(2)], visitor);

        (ResourceHandle foreground, ResourceHandle glyphRun) = Assert.Single(visitor.DrawGlyphRuns);
        Assert.Equal(new ResourceHandle(1), foreground);
        Assert.Equal(new ResourceHandle(2), glyphRun);
    }

    [Fact]
    public void ParseRenderData_PushClipAndTransform_DeliverDependents()
    {
        var bytes = new Writer();
        bytes.Int32(16);
        bytes.UInt32((uint)MilCommandKind.PushClip);
        bytes.UInt32(1);
        bytes.Int32(0);

        bytes.Int32(16);
        bytes.UInt32((uint)MilCommandKind.PushTransform);
        bytes.UInt32(2);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1), new ResourceHandle(2)], visitor);

        Assert.Equal(new ResourceHandle(1), Assert.Single(visitor.PushClips));
        Assert.Equal(new ResourceHandle(2), Assert.Single(visitor.PushTransforms));
    }

    [Fact]
    public void ParseRenderData_PushGuidelineY1Y2AndSet_DoNotThrow()
    {
        var bytes = new Writer();
        bytes.Int32(16);
        bytes.UInt32((uint)MilCommandKind.PushGuidelineY1);
        bytes.Double(12.5);

        bytes.Int32(24);
        bytes.UInt32((uint)MilCommandKind.PushGuidelineY2);
        bytes.Double(1.0);
        bytes.Double(2.0);

        bytes.Int32(16);
        bytes.UInt32((uint)MilCommandKind.PushGuidelineSet);
        bytes.UInt32(1);
        bytes.Int32(0);

        bytes.Int32(8);
        bytes.UInt32((uint)MilCommandKind.Pop);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(7)], visitor);

        Assert.Equal(12.5, Assert.Single(visitor.PushGuidelineY1s), 12);
        (double leading, double offset) = Assert.Single(visitor.PushGuidelineY2s);
        Assert.Equal(1.0, leading, 12);
        Assert.Equal(2.0, offset, 12);
        Assert.Equal(new ResourceHandle(7), Assert.Single(visitor.PushGuidelineSets));
        Assert.Equal(1, visitor.Pops);
    }

    [Fact]
    public void ParseRenderData_SizeTooSmall_Throws()
    {
        var bytes = new Writer();
        bytes.Int32(4);
        bytes.UInt32((uint)MilCommandKind.Pop);

        var visitor = new RecordingVisitor();
        MilParseException ex = Assert.Throws<MilParseException>(() => MilCommandParser.ParseRenderData(bytes.ToArray(), [], visitor));
        Assert.Equal(0, ex.Offset);
    }

    [Fact]
    public void ParseRenderData_SizeNotQwordAligned_Throws()
    {
        var bytes = new Writer();
        bytes.Int32(12);
        bytes.UInt32((uint)MilCommandKind.Pop);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilParseException ex = Assert.Throws<MilParseException>(() => MilCommandParser.ParseRenderData(bytes.ToArray(), [], visitor));
        Assert.Equal(0, ex.Offset);
    }

    [Fact]
    public void ParseRenderData_SizeExceedsRemaining_Throws()
    {
        var bytes = new Writer();
        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawLine);
        bytes.Double(0);
        bytes.Double(0);

        var visitor = new RecordingVisitor();
        MilParseException ex = Assert.Throws<MilParseException>(() => MilCommandParser.ParseRenderData(bytes.ToArray(), [], visitor));
        Assert.Equal(0, ex.Offset);
    }

    [Fact]
    public void ParseRenderData_DependentOutOfRange_Throws()
    {
        var bytes = new Writer();
        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawLine);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(0);
        bytes.UInt32(5);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        _ = Assert.Throws<MilParseException>(
            () => MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(1)], visitor));
    }

    [Fact]
    public void ParseRenderData_UnknownKind_Throws()
    {
        var bytes = new Writer();
        bytes.Int32(48);
        bytes.UInt32((uint)MilCommandKind.DrawVideo);
        bytes.Double(0);
        bytes.Double(0);
        bytes.Double(10);
        bytes.Double(10);
        bytes.UInt32(1);
        bytes.UInt32(0);

        var visitor = new RecordingVisitor();
        _ = Assert.Throws<MilParseException>(() => MilCommandParser.ParseRenderData(bytes.ToArray(), [], visitor));
    }

    [Fact]
    public void ParseRenderData_DrawImage_DeliversRectAndSource()
    {
        var bytes = new Writer();
        bytes.Int32(48); // 8 header + 40 payload: Rect + hImageSource + pad
        bytes.UInt32((uint)MilCommandKind.DrawImage);
        bytes.Double(10);
        bytes.Double(20);
        bytes.Double(30);
        bytes.Double(40);
        bytes.UInt32(1);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(101)], visitor);

        (Rect rectangle, ResourceHandle imageSource) = Assert.Single(visitor.DrawImages);
        Assert.Equal(new Rect(10, 20, 30, 40), rectangle);
        Assert.Equal(new ResourceHandle(101), imageSource);
    }

    [Fact]
    public void ParseRenderData_DrawImageAnimate_SkipsAnimationHandle()
    {
        var bytes = new Writer();
        bytes.Int32(48); // 8 header + 40 payload: Rect + hImageSource + hRectangleAnimations
        bytes.UInt32((uint)MilCommandKind.DrawImageAnimate);
        bytes.Double(1);
        bytes.Double(2);
        bytes.Double(3);
        bytes.Double(4);
        bytes.UInt32(1);
        bytes.UInt32(2);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(201), new ResourceHandle(202)], visitor);

        (Rect rectangle, ResourceHandle imageSource) = Assert.Single(visitor.DrawImages);
        Assert.Equal(new Rect(1, 2, 3, 4), rectangle);
        Assert.Equal(new ResourceHandle(201), imageSource);
    }

    [Fact]
    public void ParseRenderData_DrawDrawing_DeliversDrawingDependent()
    {
        var bytes = new Writer();
        bytes.Int32(16); // 8 header + 8 payload: hDrawing + pad
        bytes.UInt32((uint)MilCommandKind.DrawDrawing);
        bytes.UInt32(4);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(301), new ResourceHandle(302), new ResourceHandle(303), new ResourceHandle(304)], visitor);

        Assert.Equal(new ResourceHandle(304), Assert.Single(visitor.DrawDrawings));
    }

    [Fact]
    public void ParseRenderData_PushOpacityMask_CollapsesBalancedPair_DeliversMaskedRange()
    {
        var bytes = new Writer();
        bytes.Int32(32); // PushOpacityMask: 8 header + 24 payload (MilRectF + hOpacityMask + pad)
        bytes.UInt32((uint)MilCommandKind.PushOpacityMask);
        bytes.Float(0);
        bytes.Float(0);
        bytes.Float(100);
        bytes.Float(50);
        bytes.UInt32(5);
        bytes.Int32(0);

        bytes.Int32(16); // interior op, discarded: PushOpacity(double)
        bytes.UInt32((uint)MilCommandKind.PushOpacity);
        bytes.Double(0.5);

        bytes.Int32(8); // Pop: consumed by the collapse
        bytes.UInt32((uint)MilCommandKind.Pop);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(501), new ResourceHandle(502), new ResourceHandle(503), new ResourceHandle(504), new ResourceHandle(505)], visitor);

        (ResourceHandle mask, ReadOnlyMemory<byte> range, ReadOnlyMemory<ResourceHandle> dependents) = Assert.Single(visitor.MaskedRanges);
        Assert.Equal(new ResourceHandle(505), mask);
        Assert.Equal(bytes.ToArray().AsMemory(32, 16), range);
        Assert.Equal(5, dependents.Length);
        Assert.Empty(visitor.PushOpacityMasks);
        Assert.Empty(visitor.PushOpacities); // interior was discarded, not visited
        Assert.Equal(0, visitor.Pops);       // pop was consumed by the collapse
    }

    [Fact]
    public void ParseRenderData_PushOpacityAnimate_SkipsAnimationHandle()
    {
        var bytes = new Writer();
        bytes.Int32(24); // 8 header + 16 payload: double + hOpacityAnimations + pad
        bytes.UInt32((uint)MilCommandKind.PushOpacityAnimate);
        bytes.Double(0.75);
        bytes.UInt32(7);
        bytes.Int32(0);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(7)], visitor);

        Assert.Equal(0.75, Assert.Single(visitor.PushOpacities), 12);
    }

    [Fact]
    public void ParseRenderData_PushEffect_VisitsAndKeepsPopBalanced()
    {
        var bytes = new Writer();
        bytes.Int32(16); // 8 header + 8 payload: hEffect + hEffectInput
        bytes.UInt32((uint)MilCommandKind.PushEffect);
        bytes.UInt32(6);
        bytes.UInt32(7);

        bytes.Int32(8);
        bytes.UInt32((uint)MilCommandKind.Pop);

        var visitor = new RecordingVisitor();
        MilCommandParser.ParseRenderData(bytes.ToArray(), [new ResourceHandle(6), new ResourceHandle(7)], visitor);

        Assert.Equal(1, visitor.PushEffects);
        Assert.Equal(1, visitor.Pops);
    }

    [Fact]
    public void ParseRenderData_NullVisitor_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => MilCommandParser.ParseRenderData([], [], null!));
    }

    private sealed class RecordingVisitor : MilCommandVisitor
    {
        public List<(ResourceHandle Handle, double OffsetX, double OffsetY)> SetOffsets { get; } = [];

        public List<ResourceHandle> VisualCreates { get; } = [];

        public List<(ResourceHandle Handle, double Alpha)> SetAlphas { get; } = [];

        public List<(ResourceHandle Handle, ResourceHandle Child, uint Index)> InsertChildAts { get; } = [];

        public List<(ResourceHandle Handle, ColorRgba Color)> SetClearColors { get; } = [];

        public List<(ResourceHandle Handle, Rect Dirty)> Invalidates { get; } = [];

        public List<(ResourceHandle Handle, double X, double Y)> TranslateTransforms { get; } = [];

        public List<(ResourceHandle Handle, Matrix3x2 Matrix)> MatrixTransforms { get; } = [];

        public List<(ResourceHandle Handle, Point Start, Point EndPoint, ResourceHandle Transform)> LineGeometries { get; } = [];

        public List<(ResourceHandle Handle, Rect Rectangle, double RadiusX, double RadiusY, ResourceHandle Transform)> RectangleGeometries
        {
            get;
        } = [];

        public List<(ResourceHandle Handle, double Opacity, ColorRgba Color, ResourceHandle Transform)> SolidColorBrushes { get; } = [];

        public List<(ResourceHandle Handle, ResourceHandle Brush, double Thickness, double MiterLimit)> Pens { get; } = [];

        public List<(ResourceHandle Handle, ResourceHandle[] Children)> TransformGroups { get; } = [];

        public List<(ResourceHandle Handle, ResourceHandle Transform, byte[] PathData)> PathGeometries { get; } = [];

        public List<(ResourceHandle Handle, MilResourceType Type)> CreatedResources { get; } = [];

        public List<ResourceHandle> DeletedResources { get; } = [];

        public List<(ResourceHandle Handle, byte[] Data)> RenderDatas { get; } = [];

        public List<(MilCommandKind Kind, byte[] Payload)> Unknowns { get; } = [];

        public List<(ResourceHandle Handle, FontFaceToken Font, Point Origin, float EmSize, ushort[] Glyphs, float[] Advances)> GlyphRuns
        {
            get;
        } = [];

        public List<(Point Start, Point EndPoint, ResourceHandle Pen)> DrawLines { get; } = [];

        public List<(Rect Rectangle, ResourceHandle Brush, ResourceHandle Pen)> DrawRectangles { get; } = [];

        public List<(Rect Rectangle, double RadiusX, double RadiusY, ResourceHandle Brush, ResourceHandle Pen)> DrawRoundedRectangles
        {
            get;
        } = [];

        public List<(Point Center, double RadiusX, double RadiusY, ResourceHandle Brush, ResourceHandle Pen)> DrawEllipses { get; } = [];

        public List<(ResourceHandle Brush, ResourceHandle Pen, ResourceHandle Geometry)> DrawGeometries { get; } = [];

        public List<(ResourceHandle Foreground, ResourceHandle GlyphRun)> DrawGlyphRuns { get; } = [];

        public List<(Rect Rectangle, ResourceHandle ImageSource)> DrawImages { get; } = [];

        public List<ResourceHandle> DrawDrawings { get; } = [];

        public List<ResourceHandle> PushClips { get; } = [];

        public List<ResourceHandle> PushOpacityMasks { get; } = [];

        public List<(ResourceHandle Mask, ReadOnlyMemory<byte> Range, ReadOnlyMemory<ResourceHandle> Dependents)> MaskedRanges { get; } = [];

        public List<double> PushOpacities { get; } = [];

        public List<ResourceHandle> PushTransforms { get; } = [];

        public List<ResourceHandle> PushGuidelineSets { get; } = [];

        public List<double> PushGuidelineY1s { get; } = [];

        public List<(double Leading, double Offset)> PushGuidelineY2s { get; } = [];

        public List<(ResourceHandle Handle, ResourceHandle Effect)> VisualSetEffects { get; } = [];

        public List<(ResourceHandle Handle, ResourceHandle OpacityMask)> VisualSetAlphaMasks { get; } = [];

        public List<(ResourceHandle Handle, double ShadowDepth, ColorRgba Color, double Direction, double Opacity, double BlurRadius, int RenderingBias)> DropShadowEffects
        {
            get;
        } = [];

        public List<(ResourceHandle Handle, double Radius, int KernelType, int RenderingBias)> BlurEffects { get; } = [];

        public int SyncFlushes { get; private set; }

        public int RemoveAllChildren { get; private set; }

        public int PushEffects { get; private set; }

        public int Pops { get; private set; }

        public override void VisitTransportSyncFlush()
        {
            SyncFlushes++;
        }

        public override void VisitVisualCreate(ResourceHandle handle)
        {
            VisualCreates.Add(handle);
        }

        public override void VisitVisualRemoveAllChildren(ResourceHandle handle)
        {
            _ = handle;
            RemoveAllChildren++;
        }

        public override void VisitVisualSetOffset(ResourceHandle handle, double offsetX, double offsetY)
        {
            SetOffsets.Add((handle, offsetX, offsetY));
        }

        public override void VisitVisualSetAlpha(ResourceHandle handle, double alpha)
        {
            SetAlphas.Add((handle, alpha));
        }

        public override void VisitVisualInsertChildAt(ResourceHandle handle, ResourceHandle child, uint index)
        {
            InsertChildAts.Add((handle, child, index));
        }

        public override void VisitTargetSetClearColor(ResourceHandle handle, ColorRgba color)
        {
            SetClearColors.Add((handle, color));
        }

        public override void VisitTargetInvalidate(ResourceHandle handle, Rect dirty)
        {
            Invalidates.Add((handle, dirty));
        }

        public override void VisitTranslateTransform(ResourceHandle handle, double x, double y)
        {
            TranslateTransforms.Add((handle, x, y));
        }

        public override void VisitMatrixTransform(ResourceHandle handle, Matrix3x2 matrix)
        {
            MatrixTransforms.Add((handle, matrix));
        }

        public override void VisitTransformGroup(ResourceHandle handle, ReadOnlySpan<ResourceHandle> children)
        {
            TransformGroups.Add((handle, children.ToArray()));
        }

        public override void VisitPathGeometry(ResourceHandle handle, ResourceHandle transform, ReadOnlySpan<byte> pathData)
        {
            PathGeometries.Add((handle, transform, pathData.ToArray()));
        }

        public override void VisitLineGeometry(ResourceHandle handle, Point start, Point endPoint, ResourceHandle transform)
        {
            LineGeometries.Add((handle, start, endPoint, transform));
        }

        public override void VisitRectangleGeometry(
            ResourceHandle handle,
            Rect rectangle,
            double radiusX,
            double radiusY,
            ResourceHandle transform)
        {
            RectangleGeometries.Add((handle, rectangle, radiusX, radiusY, transform));
        }

        public override void VisitSolidColorBrush(ResourceHandle handle, double opacity, ColorRgba color, ResourceHandle transform)
        {
            SolidColorBrushes.Add((handle, opacity, color, transform));
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
            DropShadowEffects.Add((handle, shadowDepth, color, direction, opacity, blurRadius, renderingBias));
        }

        public override void VisitBlurEffect(ResourceHandle handle, double radius, int kernelType, int renderingBias)
        {
            BlurEffects.Add((handle, radius, kernelType, renderingBias));
        }

        public override void VisitVisualSetEffect(ResourceHandle handle, ResourceHandle effect)
        {
            VisualSetEffects.Add((handle, effect));
        }

        public override void VisitVisualSetAlphaMask(ResourceHandle handle, ResourceHandle opacityMask)
        {
            VisualSetAlphaMasks.Add((handle, opacityMask));
        }

        public override void VisitPen(ResourceHandle handle, ResourceHandle brush, double thickness, double miterLimit)
        {
            Pens.Add((handle, brush, thickness, miterLimit));
        }

        public override void VisitChannelCreateResource(ResourceHandle handle, MilResourceType type)
        {
            CreatedResources.Add((handle, type));
        }

        public override void VisitChannelDeleteResource(ResourceHandle handle)
        {
            DeletedResources.Add(handle);
        }

        public override void VisitRenderData(ResourceHandle handle, ReadOnlySpan<byte> renderData)
        {
            RenderDatas.Add((handle, renderData.ToArray()));
        }

        public override void VisitUnknown(MilCommandKind kind, ReadOnlySpan<byte> payload)
        {
            Unknowns.Add((kind, payload.ToArray()));
        }

        public override void VisitGlyphRunCreate(
            ResourceHandle handle,
            FontFaceToken font,
            Point origin,
            float emSize,
            ReadOnlySpan<ushort> glyphs,
            ReadOnlySpan<float> advances)
        {
            GlyphRuns.Add((handle, font, origin, emSize, glyphs.ToArray(), advances.ToArray()));
        }

        public override void VisitDrawLine(Point start, Point endPoint, ResourceHandle pen)
        {
            DrawLines.Add((start, endPoint, pen));
        }

        public override void VisitDrawRectangle(Rect rectangle, ResourceHandle brush, ResourceHandle pen)
        {
            DrawRectangles.Add((rectangle, brush, pen));
        }

        public override void VisitDrawRoundedRectangle(
            Rect rectangle,
            double radiusX,
            double radiusY,
            ResourceHandle brush,
            ResourceHandle pen)
        {
            DrawRoundedRectangles.Add((rectangle, radiusX, radiusY, brush, pen));
        }

        public override void VisitDrawEllipse(Point center, double radiusX, double radiusY, ResourceHandle brush, ResourceHandle pen)
        {
            DrawEllipses.Add((center, radiusX, radiusY, brush, pen));
        }

        public override void VisitDrawGeometry(ResourceHandle brush, ResourceHandle pen, ResourceHandle geometry)
        {
            DrawGeometries.Add((brush, pen, geometry));
        }

        public override void VisitDrawGlyphRun(ResourceHandle foreground, ResourceHandle glyphRun)
        {
            DrawGlyphRuns.Add((foreground, glyphRun));
        }

        public override void VisitDrawImage(Rect rectangle, ResourceHandle imageSource)
        {
            DrawImages.Add((rectangle, imageSource));
        }

        public override void VisitDrawDrawing(ResourceHandle drawing)
        {
            DrawDrawings.Add(drawing);
        }

        public override void VisitPushClip(ResourceHandle clip)
        {
            PushClips.Add(clip);
        }

        public override void VisitPushOpacityMask(ResourceHandle opacityMask)
        {
            PushOpacityMasks.Add(opacityMask);
        }

        public override void VisitMaskedRange(ResourceHandle mask, ReadOnlyMemory<byte> renderData, ReadOnlyMemory<ResourceHandle> dependents)
        {
            MaskedRanges.Add((mask, renderData, dependents));
        }

        public override void VisitPushOpacity(double opacity)
        {
            PushOpacities.Add(opacity);
        }

        public override void VisitPushTransform(ResourceHandle transform)
        {
            PushTransforms.Add(transform);
        }

        public override void VisitPushGuidelineSet(ResourceHandle guidelines)
        {
            PushGuidelineSets.Add(guidelines);
        }

        public override void VisitPushGuidelineY1(double coordinate)
        {
            PushGuidelineY1s.Add(coordinate);
        }

        public override void VisitPushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            PushGuidelineY2s.Add((leadingCoordinate, offsetToDrivenCoordinate));
        }

        public override void VisitPushEffect()
        {
            PushEffects++;
        }

        public override void VisitPop()
        {
            Pops++;
        }
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
}
