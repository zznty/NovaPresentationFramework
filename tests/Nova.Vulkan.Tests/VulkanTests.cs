using Nova.Geometry;
using Nova.TestSupport;
using Silk.NET.Vulkan;

namespace Nova.Vulkan.Tests;

// Validation is opt-in via NovaTestVulkan.DeviceOptions() (see that helper): the Khronos
// validation layer's GetDispatchDevice aborts the process under rapid device create/destroy
// churn. Set NOVA_TEST_VULKAN_VALIDATION=1 to re-enable validation for a deliberate run;
// Device_CreateWithValidation_ReportsDeviceName reflects the switch.
public sealed class VulkanTests
{
    [Fact]
    public void Device_CreateWithValidation_ReportsDeviceName()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());

        bool validationRequested = Environment.GetEnvironmentVariable("NOVA_TEST_VULKAN_VALIDATION") == "1";
        Assert.Equal(validationRequested, device.Instance.ValidationEnabled);
        Assert.True(device.Instance.Handle.IsValid);
        Assert.False(string.IsNullOrEmpty(device.DeviceName));
    }

    [Fact]
    public void Device_Dispose_IsIdempotent()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        device.Dispose();
        device.Dispose();
    }

    [Fact]
    public void Offscreen_ClearRed_ReadbackIsRed()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        presenter.Render(queue => queue.Clear(new ColorRgba(1, 0, 0, 1)));
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();

        Assert.Equal(64 * 64 * 4, pixels.Length);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            Assert.Equal(255, pixels.Span[i]);      // red
            Assert.Equal(0, pixels.Span[i + 1]);    // green
            Assert.Equal(0, pixels.Span[i + 2]);    // blue
            Assert.Equal(255, pixels.Span[i + 3]);  // opaque alpha
        }
    }

    [Fact]
    public void Offscreen_FillRectangle_ReadbackShowsRect()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            queue.FillRectangle(new Rect(8, 8, 16, 16), new ColorRgba(1, 0, 0, 1));
        });
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();

        Assert.Equal(0, Channel(pixels.Span, 4, 4, 0));      // outside the rect
        Assert.Equal(255, Channel(pixels.Span, 12, 12, 0));  // inside the rect
        Assert.Equal(255, Channel(pixels.Span, 23, 8, 0));   // right edge, still inside
        Assert.Equal(255, Channel(pixels.Span, 12, 12, 3));  // alpha opaque
    }

    [Fact]
    public void Offscreen_FillTriangles_ReadbackShowsTriangle()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            Point[] vertices =
            [
                new(8, 8),
                new(40, 8),
                new(8, 40)
            ];
            queue.FillTriangles(vertices, new ColorRgba(0, 1, 0, 1));
        });
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();

        Assert.Equal(255, Channel(pixels.Span, 12, 12, 1));
        Assert.Equal(0, Channel(pixels.Span, 50, 50, 1));
    }

    [Fact]
    public void Offscreen_UpdateTexture_PatchesExistingTexture()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        byte[] black = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        using IGpuTexture texture = presenter.CreateTexture(new TextureUpload(new PixelSize(2, 2), PixelFormat.Rgba8Unorm, black, 8));
        byte[] red = [255, 0, 0, 255];
        presenter.UpdateTexture(texture.Handle, 0, 0, new TextureUpload(new PixelSize(1, 1), PixelFormat.Rgba8Unorm, red, 4));
        TextureHandle textureHandle = texture.Handle;

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            queue.DrawTexturedQuad(
                new Point(0, 0),
                new Point(64, 0),
                new Point(64, 64),
                new Point(0, 64),
                textureHandle,
                Point.Origin,
                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1),
                ColorRgba.White);
        });
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();

        Assert.Equal(255, Channel(pixels.Span, 8, 8, 0));
        Assert.Equal(0, Channel(pixels.Span, 48, 48, 0));
    }

    [Fact]
    public void Offscreen_TransformOpacityClip_Apply()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            queue.PushTransform(Matrix3x2.Translate(20, 20));
            queue.PushOpacity(0.5);
            queue.FillRectangle(new Rect(0, 0, 8, 8), new ColorRgba(1, 0, 0, 1));
            queue.PopOpacity();
            queue.PopTransform();
            queue.PushClip(new Rect(10, 10, 4, 4));
            queue.FillRectangle(new Rect(0, 0, 64, 64), new ColorRgba(0, 0, 1, 1));
            queue.PopClip();
        });
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();

        // Translated red rect covers (20,20)-(28,28) at 50% opacity -> premultiplied color
        // (128, 0, 0); alpha blends to opaque against the opaque black clear.
        Assert.Equal(128, Channel(pixels.Span, 24, 24, 0));
        Assert.Equal(0, Channel(pixels.Span, 24, 24, 1));
        Assert.Equal(0, Channel(pixels.Span, 24, 24, 2));
        Assert.Equal(255, Channel(pixels.Span, 24, 24, 3));

        // Background outside both draws stays black and opaque.
        Assert.Equal(0, Channel(pixels.Span, 4, 4, 0));
        Assert.Equal(255, Channel(pixels.Span, 4, 4, 3));

        // Blue fill is clipped to (10,10)-(14,14).
        Assert.Equal(255, Channel(pixels.Span, 12, 12, 2));
        Assert.Equal(0, Channel(pixels.Span, 16, 16, 2));
    }

    [Fact]
    public void Offscreen_PresenterDispose_IsIdempotent()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(16, 16));
        presenter.Dispose();
        presenter.Dispose();
    }

    [Fact]
    public void Offscreen_TextureUploadDrawDestroy_ReadbackShowsTexture()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        // 2x2 opaque red texture, tightly packed RGBA.
        byte[] pixels = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        using IGpuTexture texture = presenter.CreateTexture(new TextureUpload(new PixelSize(2, 2), PixelFormat.Rgba8Unorm, pixels, 8));
        TextureHandle textureHandle = texture.Handle;

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            queue.DrawTexturedQuad(
                new Point(4, 4),
                new Point(20, 4),
                new Point(20, 20),
                new Point(4, 20),
                textureHandle,
                Point.Origin,
                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1),
                ColorRgba.White);
        });
        ReadOnlyMemory<byte> data = presenter.ReadbackRgba();

        Assert.Equal(255, Channel(data.Span, 8, 8, 0));      // inside textured quad
        Assert.Equal(0, Channel(data.Span, 8, 8, 1));        // green stays zero
        Assert.Equal(0, Channel(data.Span, 40, 40, 0));      // outside stays black

        presenter.DestroyTexture(texture.Handle);
    }

    [Fact]
    public void Offscreen_R8Texture_SamplesAsRedChannelReplicated()
    {
        using var device = new VulkanDevice(NovaTestVulkan.DeviceOptions());
        using IVulkanPresenter presenter = device.CreateOffscreenPresenter(new PixelSize(64, 64));

        // 1x1 R8Unorm coverage texture (glyph atlas format); value 255 = full coverage.
        byte[] pixels = [255];
        using IGpuTexture texture = presenter.CreateTexture(new TextureUpload(new PixelSize(1, 1), PixelFormat.R8Unorm, pixels, 1));
        TextureHandle handle = texture.Handle;

        presenter.Render(queue =>
        {
            queue.Clear(new ColorRgba(0, 0, 0, 1));
            queue.DrawTexturedQuad(
                new Point(8, 8),
                new Point(40, 8),
                new Point(40, 40),
                new Point(8, 40),
                handle,
                Point.Origin,
                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1),
                ColorRgba.White);
        });
        ReadOnlyMemory<byte> data = presenter.ReadbackRgba();

        // With the R->(R,R,R,R) swizzle, coverage 1.0 x white tint = white (not red).
        Assert.Equal(255, Channel(data.Span, 16, 16, 0));
        Assert.Equal(255, Channel(data.Span, 16, 16, 1));
        Assert.Equal(255, Channel(data.Span, 16, 16, 2));
        Assert.Equal(255, Channel(data.Span, 16, 16, 3));
    }

    [Fact]
    public void PickCompositeAlpha_TransparentRequested_PrefersPremultiplied()
    {
        // Regression: PickCompositeAlpha unconditionally preferred OpaqueBit, so a
        // per-pixel-opacity window's swapchain composited opaque and the transparency was
        // silently dropped. When the surface requests per-pixel alpha, the pick must
        // prefer PreMultipliedBit (the pipeline is premultiplied) and never select
        // OpaqueBit while any alpha-compositing mode is available.
        const CompositeAlphaFlagsKHR supported =
            CompositeAlphaFlagsKHR.OpaqueBit |
            CompositeAlphaFlagsKHR.PreMultipliedBit |
            CompositeAlphaFlagsKHR.PostMultipliedBit |
            CompositeAlphaFlagsKHR.InheritBit;
        Assert.Equal(CompositeAlphaFlagsKHR.PreMultipliedBit, SurfacePresenter.PickCompositeAlpha(supported, prefersTransparentComposite: true));

        // No premultiplied: postmultiplied is the alpha-compositing fallback.
        const CompositeAlphaFlagsKHR noPremul =
            CompositeAlphaFlagsKHR.OpaqueBit |
            CompositeAlphaFlagsKHR.PostMultipliedBit |
            CompositeAlphaFlagsKHR.InheritBit;
        Assert.Equal(CompositeAlphaFlagsKHR.PostMultipliedBit, SurfacePresenter.PickCompositeAlpha(noPremul, prefersTransparentComposite: true));

        // Inherit only: the compositor decides.
        Assert.Equal(CompositeAlphaFlagsKHR.InheritBit, SurfacePresenter.PickCompositeAlpha(CompositeAlphaFlagsKHR.InheritBit, prefersTransparentComposite: true));

        // Opaque-only surface: transparency cannot be composited, so opaque is the only
        // option — the honest fallback, never a crash.
        Assert.Equal(CompositeAlphaFlagsKHR.OpaqueBit, SurfacePresenter.PickCompositeAlpha(CompositeAlphaFlagsKHR.OpaqueBit, prefersTransparentComposite: true));
    }

    [Fact]
    public void PickCompositeAlpha_OpaqueWindow_KeepsOpaquePreference()
    {
        // Ordinary windows must stay opaque-preferred (unchanged behavior): a non-opaque
        // composite mode on a fully opaque window risks compositor-dependent artifacts.
        const CompositeAlphaFlagsKHR all =
            CompositeAlphaFlagsKHR.OpaqueBit |
            CompositeAlphaFlagsKHR.PreMultipliedBit |
            CompositeAlphaFlagsKHR.PostMultipliedBit |
            CompositeAlphaFlagsKHR.InheritBit;
        Assert.Equal(CompositeAlphaFlagsKHR.OpaqueBit, SurfacePresenter.PickCompositeAlpha(all, prefersTransparentComposite: false));

        // Without opaque support, the historical fallback order is preserved.
        const CompositeAlphaFlagsKHR noOpaque =
            CompositeAlphaFlagsKHR.PreMultipliedBit |
            CompositeAlphaFlagsKHR.PostMultipliedBit |
            CompositeAlphaFlagsKHR.InheritBit;
        Assert.Equal(CompositeAlphaFlagsKHR.PreMultipliedBit, SurfacePresenter.PickCompositeAlpha(noOpaque, prefersTransparentComposite: false));
    }

    private static byte Channel(ReadOnlySpan<byte> pixels, int x, int y, int channel)
    {
        return pixels[(((y * 64) + x) * 4) + channel];
    }
}
