using System.Runtime.InteropServices;
using Nova.Geometry;
using Nova.Mil;
using Nova.MilCmd;
using Nova.Sdl;
using Nova.TestSupport;
using Nova.Vulkan;

namespace Nova.Host.Tests;

// Validation is Disabled by default via NovaTestVulkan.DeviceOptions() (see that helper for
// the full rationale): the Khronos validation layer's GetDispatchDevice hits a libstdc++
// __glibcxx_assert_fail and aborts the process under rapid Vulkan device create/destroy
// churn. Set NOVA_TEST_VULKAN_VALIDATION=1 to re-enable validation for a deliberate run.
// These tests assert frame/present behavior, not validation output; the layer stays enabled
// in the interactive smoke/dev path.
public sealed partial class CompositionFrameTests
{
    private const int Size = 64;

    private static VulkanDeviceOptions DeviceOptions => NovaTestVulkan.DeviceOptions();

    public CompositionFrameTests()
    {
        ForceOffscreenDriver();
    }

    [Fact]
    public void CreateOffscreen_LeavesHostAndWindowNull()
    {
        using var frame = CompositionFrame.CreateOffscreen(new PixelSize(Size, Size), DeviceOptions);

        Assert.Null(frame.Host);
        Assert.Null(frame.Window);
        Assert.NotNull(frame.Device);
        Assert.NotNull(frame.Presenter);
        Assert.NotNull(frame.Graph);
        Assert.NotNull(frame.Atlas);
        Assert.Equal(new PixelSize(Size, Size), frame.Presenter.PixelSize);
        Assert.False(frame.Closing);
    }

    [Fact]
    public void CreateOffscreen_Pump_IsNoOp()
    {
        using var frame = CompositionFrame.CreateOffscreen(new PixelSize(Size, Size), DeviceOptions);

        Assert.False(frame.Pump());
        Assert.False(frame.Closing);
    }

    [Fact]
    public void CreateOffscreen_Dispose_IsIdempotent()
    {
        var frame = CompositionFrame.CreateOffscreen(new PixelSize(Size, Size), DeviceOptions);
        frame.Dispose();
        frame.Dispose();
    }

    [Fact]
    public void WindowCtor_HiddenVulkan_PresentDoesNotThrow()
    {
        using var frame = new CompositionFrame(
            new WindowOptions
            {
                Title = "Nova.Host.Tests",
                Size = new PixelSize(Size, Size),
                Hidden = true,
                Resizable = false
            },
            DeviceOptions);

        Assert.NotNull(frame.Host);
        Assert.NotNull(frame.Window);
        Assert.True(frame.Host.IsInitialized);
        Assert.Equal(new PixelSize(Size, Size), frame.Presenter.PixelSize);
        Assert.False(frame.Closing);

        frame.Present();
        frame.Present();
    }

    [Fact]
    public void WindowCtor_Present_ReadbackRgbaShowsContent()
    {
        using var frame = new CompositionFrame(
            new WindowOptions
            {
                Title = "Nova.Host.Tests",
                Size = new PixelSize(Size, Size),
                Hidden = true,
                Resizable = false
            },
            DeviceOptions);
        InjectRedRectangle(frame.Graph);

        // Readback is opt-in for window presenters; enable before rendering.
        frame.Presenter.EnableReadback();

        // Two full acquire/render/present cycles: semaphore reuse and swapchain
        // image ownership only bite on the second+ frame.
        frame.Present();
        frame.Present();
        ReadOnlyMemory<byte> pixels = frame.Presenter.ReadbackRgba();

        Assert.Equal(Size * Size * 4, pixels.Length);
        AssertRed(pixels.Span, 12, 12);
        AssertRed(pixels.Span, 20, 20);
        Assert.Equal(0, Channel(pixels.Span, 0, 0, 0));
        Assert.Equal(0, Channel(pixels.Span, 32, 32, 0));
        Assert.Equal(255, Channel(pixels.Span, 12, 12, 3));
    }

    [Fact]
    public void WindowCtor_ReadbackWithoutEnable_ThrowsNamingTheSwitch()
    {
        using var frame = new CompositionFrame(
            new WindowOptions
            {
                Title = "Nova.Host.Tests",
                Size = new PixelSize(Size, Size),
                Hidden = true,
                Resizable = false
            },
            DeviceOptions);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => frame.Presenter.ReadbackRgba());
        Assert.Contains("EnableReadback", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateOffscreen_RedRectInGraph_ReadbackShowsRed()
    {
        using var frame = CompositionFrame.CreateOffscreen(new PixelSize(Size, Size), DeviceOptions);
        InjectRedRectangle(frame.Graph);

        frame.Present();
        ReadOnlyMemory<byte> pixels = frame.Presenter.ReadbackRgba();

        Assert.Equal(Size * Size * 4, pixels.Length);
        AssertRed(pixels.Span, 12, 12);
        AssertRed(pixels.Span, 20, 20);
        Assert.Equal(0, Channel(pixels.Span, 0, 0, 0));
        Assert.Equal(0, Channel(pixels.Span, 32, 32, 0));
    }

    private static void InjectRedRectangle(SlaveGraph graph)
    {
        const uint visual = 1;
        const uint brush = 2;
        const uint renderData = 3;

        var channel = new Writer();
        channel.UInt32((uint)MilCommandKind.ChannelCreateResource);
        channel.UInt32(visual);
        channel.UInt32((uint)MilResourceType.Visual);
        channel.UInt32((uint)MilCommandKind.ChannelCreateResource);
        channel.UInt32(brush);
        channel.UInt32((uint)MilResourceType.SolidColorBrush);
        channel.UInt32((uint)MilCommandKind.ChannelCreateResource);
        channel.UInt32(renderData);
        channel.UInt32((uint)MilResourceType.RenderData);
        channel.UInt32((uint)MilCommandKind.SolidColorBrush);
        channel.UInt32(brush);
        channel.Double(1.0); // opacity
        channel.Float(1); // red
        channel.Float(0); // green
        channel.Float(0); // blue
        channel.Float(1); // alpha
        channel.UInt32(0); // hOpacityAnimations
        channel.UInt32(0); // transform
        channel.UInt32(0); // hRelativeTransform
        channel.UInt32(0); // hColorAnimations
        byte[] blob = DrawRectangleBlob();
        channel.UInt32((uint)MilCommandKind.RenderData);
        channel.UInt32(renderData);
        channel.UInt32((uint)blob.Length);
        channel.Bytes(blob);
        channel.UInt32((uint)MilCommandKind.VisualSetContent);
        channel.UInt32(visual);
        channel.UInt32(renderData);
        channel.UInt32((uint)MilCommandKind.TargetSetRoot);
        channel.UInt32(0);
        channel.UInt32(visual);

        MilCommandParser.ParseChannel(channel.ToArray(), graph);
        graph.SetRenderDataDependents(new ResourceHandle(renderData), [new ResourceHandle(brush)]);
    }

    private static byte[] DrawRectangleBlob()
    {
        var blob = new Writer();
        blob.Int32(48); // 8 header + 4 doubles + 2 dependent handles
        blob.UInt32((uint)MilCommandKind.DrawRectangle);
        blob.Double(8); // x
        blob.Double(8); // y
        blob.Double(16); // width
        blob.Double(16); // height
        blob.UInt32(1); // brush dependent index (1-based) -> dependents[0]
        blob.UInt32(0); // pen: null
        return blob.ToArray();
    }

    private static void AssertRed(ReadOnlySpan<byte> pixels, int x, int y)
    {
        Assert.Equal(255, Channel(pixels, x, y, 0));
        Assert.Equal(0, Channel(pixels, x, y, 1));
        Assert.Equal(0, Channel(pixels, x, y, 2));
        Assert.Equal(255, Channel(pixels, x, y, 3));
    }

    private static byte Channel(ReadOnlySpan<byte> pixels, int x, int y, int channel)
    {
        return pixels[(((y * Size) + x) * 4) + channel];
    }

    private static void ForceOffscreenDriver()
    {
        // Environment.SetEnvironmentVariable does not reach libc getenv on this runtime,
        // so SDL would keep its default driver; setenv reaches SDL directly.
        _ = Native.SetEnv("SDL_VIDEO_DRIVER", "offscreen", 1);
        _ = Native.SetEnv("SDL_VIDEODRIVER", "offscreen", 1);
    }

    private static partial class Native
    {
        [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SetEnv(string name, string value, int overwrite);
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
