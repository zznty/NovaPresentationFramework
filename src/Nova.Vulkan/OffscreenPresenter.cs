using Nova.Geometry;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>Headless presenter rendering into an R8G8B8A8 color attachment with readback support.</summary>
internal sealed class OffscreenPresenter : VulkanPresenterBase
{
    private ImageHandle _image;
    private ImageViewHandle _imageView;
    private DeviceMemoryHandle _imageMemory;
    private ImageHandle _msaaImage;
    private ImageViewHandle _msaaView;
    private DeviceMemoryHandle _msaaMemory;
    private FramebufferHandle _framebuffer;

    internal OffscreenPresenter(VulkanDevice device, PixelSize size)
        : base(device, size)
    {
        Initialize(Format.R8G8B8A8Unorm);
        CreateColorTarget();
    }

    private protected override bool IsSurfacePresenter => false;

    private protected override uint AcquireImage()
    {
        return 0;
    }

    private protected override ImageHandle GetColorImage(uint imageIndex)
    {
        _ = imageIndex;
        return _image;
    }

    private protected override ImageHandle GetMsaaImage(uint imageIndex)
    {
        _ = imageIndex;
        return _msaaImage;
    }

    private protected override ImageViewHandle GetColorView(uint imageIndex)
    {
        _ = imageIndex;
        return UseMsaa ? _msaaView : _imageView;
    }

    private protected override ImageViewHandle GetResolveView(uint imageIndex)
    {
        _ = imageIndex;
        return _imageView;
    }

    private protected override FramebufferHandle GetFramebuffer(uint imageIndex)
    {
        _ = imageIndex;
        return _framebuffer;
    }

    private protected override void PresentFrame(uint imageIndex)
    {
        throw new NotSupportedException("Offscreen presenters do not present to a surface.");
    }

    private protected override void ResizeTarget(PixelSize newSize)
    {
        DestroyColorTarget();
        CreateColorTarget();
    }

    private protected override void DisposeTarget()
    {
        DestroyColorTarget();
    }

    private void CreateColorTarget()
    {
        uint width = (uint)TargetSize.Width;
        uint height = (uint)TargetSize.Height;
        ImageHandle image = CreateImage(width, height, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit);
        DeviceMemoryHandle memory = AllocateMemory(GetImageMemoryRequirements(image), MemoryPropertyFlags.DeviceLocalBit, MemoryPropertyFlags.DeviceLocalBit);
        VkApi.Check(Vk.BindImageMemory(Device.Device, image, memory, 0), nameof(Vk.BindImageMemory));
        ImageViewHandle view = CreateImageView(image, Format.R8G8B8A8Unorm);
        _image = image;
        _imageMemory = memory;
        _imageView = view;

        if (UseMsaa)
        {
            ImageHandle msaaImage = CreateImage(width, height, Format.R8G8B8A8Unorm, ImageUsageFlags.ColorAttachmentBit, SampleCount);
            DeviceMemoryHandle msaaMemory = AllocateMemory(GetImageMemoryRequirements(msaaImage), MemoryPropertyFlags.DeviceLocalBit, MemoryPropertyFlags.DeviceLocalBit);
            VkApi.Check(Vk.BindImageMemory(Device.Device, msaaImage, msaaMemory, 0), nameof(Vk.BindImageMemory));
            ImageViewHandle msaaView = CreateImageView(msaaImage, Format.R8G8B8A8Unorm);
            _msaaImage = msaaImage;
            _msaaMemory = msaaMemory;
            _msaaView = msaaView;
        }

        if (!UseDynamicRendering)
        {
            _framebuffer = CreateFramebuffer(UseMsaa ? _msaaView : view, view, width, height);
        }
    }

    private unsafe void DestroyColorTarget()
    {
        if (_framebuffer != default)
        {
            Vk.DestroyFramebuffer(Device.Device, _framebuffer, null);
            _framebuffer = default;
        }

        if (_msaaView != default)
        {
            Vk.DestroyImageView(Device.Device, _msaaView, null);
            _msaaView = default;
        }

        if (_msaaMemory != default)
        {
            Vk.FreeMemory(Device.Device, _msaaMemory, null);
            _msaaMemory = default;
        }

        if (_msaaImage != default)
        {
            Vk.DestroyImage(Device.Device, _msaaImage, null);
            _msaaImage = default;
        }

        if (_imageView != default)
        {
            Vk.DestroyImageView(Device.Device, _imageView, null);
            _imageView = default;
        }

        if (_imageMemory != default)
        {
            Vk.FreeMemory(Device.Device, _imageMemory, null);
            _imageMemory = default;
        }

        if (_image == default)
        {
            return;
        }

        Vk.DestroyImage(Device.Device, _image, null);
        _image = default;
    }
}
