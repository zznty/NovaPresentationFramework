using Nova.Geometry;
using Nova.MilCmd;
using Nova.TestSupport;
using Nova.Vulkan;

namespace Nova.Mil.Tests;

public sealed class DuceChannelTests
{
    private const int Size = 64;

    [Fact]
    public void VersionCheck_And_CreateChannel_ReportsSameThread()
    {
        Assert.Equal(DuceExports.SOk, DuceExports.VersionCheck(DuceExports.ExpectedMilSdkVersion));
        Assert.Equal(DuceExports.EFail, DuceExports.VersionCheck(0));

        Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
        Assert.NotEqual(0, connection);
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint channel));
        Assert.NotEqual(0, channel);
        Assert.Equal(DuceExports.SOk, DuceExports.GetMarshalType(channel, out int marshalType));
        Assert.Equal(DuceExports.MarshalTypeSameThread, marshalType);

        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
    }

    [Fact]
    public void CreateOrAddRef_ThenRelease_TracksRefCount()
    {
        Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint channel));

        uint handle = 0;
        Assert.Equal(DuceExports.SOk, DuceExports.CreateOrAddRef(channel, (uint)MilResourceType.Visual, ref handle));
        // Handle values come from ONE process-wide allocator (all channels feed the same
        // shared SlaveGraph, which keys resources by value); the first value is 1 only when
        // no other test in the process allocated first.
        Assert.NotEqual(0u, handle);
        Assert.Equal(DuceExports.SOk, DuceExports.GetRefCount(channel, handle, out uint count));
        Assert.Equal(1u, count);

        Assert.Equal(DuceExports.SOk, DuceExports.CreateOrAddRef(channel, (uint)MilResourceType.Visual, ref handle));
        Assert.Equal(DuceExports.SOk, DuceExports.GetRefCount(channel, handle, out count));
        Assert.Equal(2u, count);

        Assert.Equal(DuceExports.SOk, DuceExports.Release(channel, handle, out int deleted));
        Assert.Equal(0, deleted);
        Assert.Equal(DuceExports.SOk, DuceExports.Release(channel, handle, out deleted));
        Assert.Equal(1, deleted);

        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
    }

    [Fact]
    public unsafe void Commit_DrawRectangle_PresentReadbackShowsRed()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(Size, Size));
        var graph = new SlaveGraph();
        _ = DuceRuntime.Attach(graph, () => presenter.Render(queue => graph.Rasterize(queue, null)));
        try
        {
            Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
            Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint channel));

            byte[] records = RectChannelBytes();
            fixed (byte* data = records)
            {
                Assert.Equal(
                    DuceExports.SOk,
                    DuceExports.SendCommand(data, (uint)records.Length, sendInSeparateBatch: false, channel));
            }

            Assert.Equal(DuceExports.SOk, DuceExports.CloseBatch(channel));
            Assert.Equal(DuceExports.SOk, DuceExports.CommitChannel(channel));
            graph.SetRenderDataDependents(new ResourceHandle(3), [new ResourceHandle(2)]);
            Assert.Equal(DuceExports.SOk, DuceExports.Present(connection));

            ReadOnlySpan<byte> pixels = presenter.ReadbackRgba().Span;
            Assert.Equal(255, Channel(pixels, 12, 12, 0));
            Assert.Equal(0, Channel(pixels, 12, 12, 1));
            Assert.Equal(0, Channel(pixels, 12, 12, 2));
            Assert.Equal(255, Channel(pixels, 12, 12, 3));
            Assert.Equal(0, Channel(pixels, 4, 4, 0));
            Assert.Equal(0, Channel(pixels, 30, 30, 0));

            Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
            Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
        }
        finally
        {
            DuceRuntime.Detach();
        }
    }

    [Fact]
    public unsafe void PeekNextMessage_YieldsCapsThenPresented()
    {
        Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint channel));

        DuceMessage message = default;
        Assert.Equal(
            DuceExports.SOk,
            DuceExports.PeekNextMessage(channel, &message, (nuint)sizeof(DuceMessage), out int retrieved));
        Assert.Equal(1, retrieved);
        Assert.Equal(DuceExports.MessageCaps, message.Type);

        Assert.Equal(DuceExports.SOk, DuceExports.Present(connection));
        Assert.Equal(
            DuceExports.SOk,
            DuceExports.PeekNextMessage(channel, &message, (nuint)sizeof(DuceMessage), out retrieved));
        Assert.Equal(1, retrieved);
        Assert.Equal(DuceExports.MessagePresented, message.Type);

        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
    }

    [Fact]
    public unsafe void SyncFlush_EnqueuesSyncFlushReply()
    {
        Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint channel));

        DuceMessage message = default;
        Assert.Equal(
            DuceExports.SOk,
            DuceExports.PeekNextMessage(channel, &message, (nuint)sizeof(DuceMessage), out _));

        Assert.Equal(DuceExports.SOk, DuceExports.SyncFlush(channel));
        Assert.Equal(
            DuceExports.SOk,
            DuceExports.PeekNextMessage(channel, &message, (nuint)sizeof(DuceMessage), out int retrieved));
        Assert.Equal(1, retrieved);
        Assert.Equal(DuceExports.MessageSyncFlushReply, message.Type);

        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
    }

    [Fact]
    public void DuplicateHandle_AllocatesDistinctValueOnTargetChannel()
    {
        Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint source));
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint target));
        uint handle = 0;
        Assert.Equal(DuceExports.SOk, DuceExports.CreateOrAddRef(source, (uint)MilResourceType.Visual, ref handle));
        uint duplicate = 0;
        Assert.Equal(DuceExports.SOk, DuceExports.DuplicateHandle(source, handle, target, ref duplicate));
        // The duplicate is a FRESH value from the shared handle space, never an alias of the
        // source's value: every channel feeds the same value-keyed SlaveGraph, so aliasing
        // would collide two live resources (the black-window popup bug). WPF's
        // MultiChannelResource expects the duplicate to be valid on the target channel with
        // the source's type — that is preserved.
        Assert.NotEqual(handle, duplicate);
        Assert.Equal(DuceExports.SOk, DuceExports.GetRefCount(target, duplicate, out uint count));
        Assert.Equal(1u, count);
        Assert.Equal(DuceExports.SOk, DuceExports.GetRefCount(source, handle, out count));
        Assert.Equal(1u, count);
        uint secondDuplicate = 0;
        Assert.Equal(DuceExports.SOk, DuceExports.DuplicateHandle(source, handle, target, ref secondDuplicate));
        Assert.NotEqual(duplicate, secondDuplicate);
        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(source));
        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(target));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
    }

    [Fact]
    public void UnimplementedExport_ReturnsENotImplemented()
    {
        uint duplicate = 0;
        Assert.Equal(DuceExports.EInvalidArg, DuceExports.DuplicateHandle(0, 0, 0, ref duplicate));
        Assert.Equal(DuceExports.ENotImplemented, DuceExports.SendCommandMedia(0, 0, 0, false));
    }

    [Fact]
    public void SendCommandBitmapSource_NullSource_ReturnsEInvalidArg()
    {
        // Imaging is implemented on this host: SendCommandBitmapSource no longer returns
        // E_NOTIMPL. A null bitmap source (handle 0) is an argument error.
        Assert.Equal(DuceExports.EInvalidArg, DuceExports.SendCommandBitmapSource(0, 0, 0));
    }

    [Fact]
    public void Disconnect_IsIdempotent()
    {
        Assert.Equal(DuceExports.SOk, DuceExports.CreateConnection(false, out nint connection));
        Assert.Equal(DuceExports.SOk, DuceExports.CreateChannel(connection, 0, out nint channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DestroyChannel(channel));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(connection));
        Assert.Equal(DuceExports.SOk, DuceExports.DisconnectConnection(0));
    }

    private static byte[] RectChannelBytes()
    {
        var bytes = new Writer();
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(1);
        bytes.UInt32((uint)MilResourceType.Visual);
        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(2);
        bytes.UInt32((uint)MilResourceType.SolidColorBrush);
        bytes.UInt32((uint)MilCommandKind.SolidColorBrush);
        bytes.UInt32(2);
        bytes.Double(1.0);
        bytes.Float(1.0f);
        bytes.Float(0.0f);
        bytes.Float(0.0f);
        bytes.Float(1.0f);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);
        bytes.UInt32(0);

        var renderData = new Writer();
        renderData.Int32(48);
        renderData.UInt32((uint)MilCommandKind.DrawRectangle);
        renderData.Double(8);
        renderData.Double(8);
        renderData.Double(16);
        renderData.Double(16);
        renderData.UInt32(1);
        renderData.UInt32(0);
        byte[] blob = renderData.ToArray();

        bytes.UInt32((uint)MilCommandKind.ChannelCreateResource);
        bytes.UInt32(3);
        bytes.UInt32((uint)MilResourceType.RenderData);
        bytes.UInt32((uint)MilCommandKind.RenderData);
        bytes.UInt32(3);
        bytes.UInt32((uint)blob.Length);
        bytes.Bytes(blob);
        bytes.UInt32((uint)MilCommandKind.VisualSetContent);
        bytes.UInt32(1);
        bytes.UInt32(3);
        bytes.UInt32((uint)MilCommandKind.TargetSetRoot);
        bytes.UInt32(0);
        bytes.UInt32(1);
        bytes.UInt32((uint)MilCommandKind.TargetSetClearColor);
        bytes.UInt32(0);
        bytes.Float(0.0f);
        bytes.Float(0.0f);
        bytes.Float(0.0f);
        bytes.Float(1.0f);
        return bytes.ToArray();
    }

    private static byte Channel(ReadOnlySpan<byte> pixels, int x, int y, int channel)
    {
        return pixels[(((y * Size) + x) * 4) + channel];
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
