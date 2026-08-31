using Silk.NET.Core;
using Nova.Geometry;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>
/// Swapchain presenter for a windowing surface. The surface is owned by this presenter
/// and destroyed with it. The swapchain is recreated lazily after Resize or on
/// VK_ERROR_OUT_OF_DATE_KHR / VK_SUBOPTIMAL_KHR.
/// </summary>
internal sealed class SurfacePresenter : VulkanPresenterBase
{
    private readonly ISurfaceSource _surface;
    private readonly SurfaceFormatKHR _surfaceFormat;
    private SurfaceHandleKHR _vkSurface;
    private SwapchainHandleKHR _swapchain;
    private ImageHandle[] _images = [];
    private ImageViewHandle[] _views = [];
    private ImageViewHandle[] _msaaViews = [];
    private ImageHandle[] _msaaImages = [];
    private DeviceMemoryHandle[] _msaaMemories = [];
    private FramebufferHandle[] _framebuffers = [];
    private bool _swapchainDirty;

    internal SurfacePresenter(VulkanDevice device, ISurfaceSource surface)
        : base(device, surface.PixelSize)
    {
        _surface = surface;
        if (surface.PixelSize.IsEmpty)
        {
            throw new ArgumentException("The surface reports an empty pixel size.", nameof(surface));
        }

        CreateVulkanSurface();
        if (!QueueFamilySupportsPresent())
        {
            throw new VulkanException("The device queue family does not support presentation to the given surface.");
        }

        _surfaceFormat = PickSurfaceFormat();
        Initialize(_surfaceFormat.Format);
        CreateSwapchain();
    }

    private protected override bool IsSurfacePresenter => true;

    private protected override unsafe uint AcquireImage()
    {
        while (true)
        {
            if (_swapchainDirty)
            {
                RecreateSwapchain();
            }

            SwapchainHandleKHR swapchain = _swapchain;
            SemaphoreHandle acquireSemaphore = AcquireSemaphore;
            uint imageIndex;
            Result result = Vk.AcquireNextImageKHR(Device.Device, swapchain, ulong.MaxValue, acquireSemaphore, default, &imageIndex);
            if (result == Result.ErrorOutOfDateKHR)
            {
                RecreateSwapchain();
                continue;
            }

            if (result == Result.SuboptimalKHR)
            {
                return imageIndex;
            }

            VkApi.Check(result, nameof(Vk.AcquireNextImageKHR));
            return imageIndex;
        }
    }

    private protected override ImageHandle GetColorImage(uint imageIndex)
    {
        return _images[imageIndex];
    }

    private protected override ImageHandle GetMsaaImage(uint imageIndex)
    {
        return _msaaImages[imageIndex];
    }

    private protected override ImageViewHandle GetColorView(uint imageIndex)
    {
        return UseMsaa ? _msaaViews[imageIndex] : _views[imageIndex];
    }

    private protected override ImageViewHandle GetResolveView(uint imageIndex)
    {
        return _views[imageIndex];
    }

    private protected override FramebufferHandle GetFramebuffer(uint imageIndex)
    {
        return _framebuffers[imageIndex];
    }

    private protected override unsafe void PresentFrame(uint imageIndex)
    {
        SwapchainHandleKHR swapchain = _swapchain;
        SemaphoreHandle renderFinishedSemaphore = RenderFinishedFor(imageIndex);
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKHR,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderFinishedSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };
        Result result = Vk.QueuePresentKHR(Device.Queue, &presentInfo);
        if (result is Result.ErrorOutOfDateKHR or Result.SuboptimalKHR)
        {
            _swapchainDirty = true;
            return;
        }

        VkApi.Check(result, nameof(Vk.QueuePresentKHR));
    }

    private protected override void ResizeTarget(PixelSize newSize)
    {
        _ = newSize;
        _swapchainDirty = true;
    }

    private protected override unsafe void DisposeTarget()
    {
        DestroySwapchain();
        if (_vkSurface == default)
        {
            return;
        }

        Vk.DestroySurfaceKHR(Device.Instance.SilkInstance, _vkSurface, null);
        _vkSurface = default;
    }

    private unsafe void CreateVulkanSurface()
    {
        SurfaceHandle surfaceHandle = _surface.CreateSurface(Device.Instance.Handle);
        if (!surfaceHandle.IsValid)
        {
            throw new VulkanException("The surface source returned an invalid surface.");
        }

        _vkSurface = new SurfaceHandleKHR((void*)(nint)surfaceHandle.Value);
    }

    private bool QueueFamilySupportsPresent()
    {
        uint supported = 0;
        Ref<uint> supportedRef = new(ref supported);
        VkApi.Check(Vk.GetPhysicalDeviceSurfaceSupportKHR(Device.PhysicalDevice, Device.QueueFamilyIndex, _vkSurface, supportedRef), nameof(Vk.GetPhysicalDeviceSurfaceSupportKHR));
        return supported != 0;
    }

    private unsafe SurfaceFormatKHR PickSurfaceFormat()
    {
        uint count = 0;
        Ref<uint> countRef = new(ref count);
        VkApi.Check(Vk.GetPhysicalDeviceSurfaceFormatsKHR(Device.PhysicalDevice, _vkSurface, countRef, default), nameof(Vk.GetPhysicalDeviceSurfaceFormatsKHR));
        int formatCount = (int)count;
        if (formatCount == 0)
        {
            throw new VulkanException("The surface supports no color formats.");
        }


        SurfaceFormatKHR* formats = stackalloc SurfaceFormatKHR[formatCount];
        VkApi.Check(Vk.GetPhysicalDeviceSurfaceFormatsKHR(Device.PhysicalDevice, _vkSurface, &count, formats), nameof(Vk.GetPhysicalDeviceSurfaceFormatsKHR));
        for (int i = 0; i < formatCount; i++)
        {
            if (formats[i].Format == Format.B8G8R8A8Unorm)
            {
                return formats[i];
            }
        }

        return formats[0];
    }

    private unsafe void CreateSwapchain()
    {
        SurfaceCapabilitiesKHR capabilities;
        VkApi.Check(Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(Device.PhysicalDevice, _vkSurface, &capabilities), nameof(Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR));

        PixelSize requested = _surface.PixelSize;
        if (requested.IsEmpty)
        {
            throw new VulkanException("The surface reports an empty pixel size.");
        }

        Extent2D extent = capabilities.CurrentExtent.Width != uint.MaxValue
            ? capabilities.CurrentExtent
            : new Extent2D
            {
                Width = Math.Clamp((uint)requested.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width),
                Height = Math.Clamp((uint)requested.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height)
            };

        if (extent.Width == 0 || extent.Height == 0)
        {
            throw new VulkanException("The surface reports a zero extent; the window is not ready for presentation.");
        }

        SetTargetSize(new PixelSize((int)extent.Width, (int)extent.Height));

        uint imageCount = capabilities.MinImageCount + 1;
        if (capabilities.MaxImageCount > 0)
        {
            imageCount = Math.Min(imageCount, capabilities.MaxImageCount);
        }

        PresentModeKHR presentMode = PickPresentMode();
        // Transfer-source usage is requested only when the surface advertises it, so window
        // creation never fails for readback's sake; ReadbackRgba/EnableReadback throw instead
        // when the capability is missing and readback is actually asked for.
        ReadbackSupported = (capabilities.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) != 0;
        ImageUsageFlags imageUsage = ImageUsageFlags.ColorAttachmentBit;
        if (ReadbackSupported)
        {
            imageUsage |= ImageUsageFlags.TransferSrcBit;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKHR,
            Surface = _vkSurface,
            MinImageCount = imageCount,
            ImageFormat = _surfaceFormat.Format,
            ImageColorSpace = _surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = imageUsage,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = PickCompositeAlpha(capabilities.SupportedCompositeAlpha, _surface.PrefersTransparentComposite),
            PresentMode = presentMode,
            Clipped = true
        };
        SwapchainHandleKHR swapchain;
        VkApi.Check(Vk.CreateSwapchainKHR(Device.Device, &createInfo, null, &swapchain), nameof(Vk.CreateSwapchainKHR));
        _swapchain = swapchain;

        uint imageCountResult = 0;
        VkApi.Check(Vk.GetSwapchainImagesKHR(Device.Device, swapchain, &imageCountResult, null), nameof(Vk.GetSwapchainImagesKHR));
        ImageHandle* images = stackalloc ImageHandle[(int)imageCountResult];
        VkApi.Check(Vk.GetSwapchainImagesKHR(Device.Device, swapchain, &imageCountResult, images), nameof(Vk.GetSwapchainImagesKHR));
        _images = new ImageHandle[imageCountResult];
        _views = new ImageViewHandle[imageCountResult];
        _framebuffers = new FramebufferHandle[imageCountResult];
        if (UseMsaa)
        {
            _msaaViews = new ImageViewHandle[imageCountResult];
            _msaaImages = new ImageHandle[imageCountResult];
            _msaaMemories = new DeviceMemoryHandle[imageCountResult];
        }

        for (int i = 0; i < imageCountResult; i++)
        {
            _images[i] = images[i];
            _views[i] = CreateImageView(images[i], _surfaceFormat.Format);
            ImageViewHandle msaaView = _views[i];
            if (UseMsaa)
            {
                ImageHandle msaaImage = CreateImage(extent.Width, extent.Height, _surfaceFormat.Format, ImageUsageFlags.ColorAttachmentBit, SampleCount);
                DeviceMemoryHandle msaaMemory = AllocateMemory(GetImageMemoryRequirements(msaaImage), MemoryPropertyFlags.DeviceLocalBit, MemoryPropertyFlags.DeviceLocalBit);
                VkApi.Check(Vk.BindImageMemory(Device.Device, msaaImage, msaaMemory, 0), nameof(Vk.BindImageMemory));
                msaaView = CreateImageView(msaaImage, _surfaceFormat.Format);
                _msaaImages[i] = msaaImage;
                _msaaViews[i] = msaaView;
                _msaaMemories[i] = msaaMemory;
            }

            if (!UseDynamicRendering)
            {
                _framebuffers[i] = CreateFramebuffer(msaaView, _views[i], extent.Width, extent.Height);
            }
        }

        EnsureRenderFinishedSemaphores(_images.Length);
        _swapchainDirty = false;
    }

    private unsafe PresentModeKHR PickPresentMode()
    {
        uint count = 0;
        VkApi.Check(Vk.GetPhysicalDeviceSurfacePresentModesKHR(Device.PhysicalDevice, _vkSurface, &count, null), nameof(Vk.GetPhysicalDeviceSurfacePresentModesKHR));
        PresentModeKHR* modes = stackalloc PresentModeKHR[(int)count];
        VkApi.Check(Vk.GetPhysicalDeviceSurfacePresentModesKHR(Device.PhysicalDevice, _vkSurface, &count, modes), nameof(Vk.GetPhysicalDeviceSurfacePresentModesKHR));
        PresentModeKHR requested = Device.Options.PresentMode switch
        {
            PresentMode.Mailbox => PresentModeKHR.Mailbox,
            PresentMode.Immediate => PresentModeKHR.Immediate,
            PresentMode.Fifo => PresentModeKHR.Fifo,
            _ => PresentModeKHR.Fifo
        };
        for (int i = 0; i < count; i++)
        {
            if (modes[i] == requested)
            {
                return requested;
            }
        }

        return PresentModeKHR.Fifo;
    }

    internal static CompositeAlphaFlagsKHR PickCompositeAlpha(CompositeAlphaFlagsKHR supported, bool prefersTransparentComposite)
    {
        if (prefersTransparentComposite)
        {
            // Per-pixel-alpha window: the compositor must blend the swapchain image with
            // the desktop, so an alpha-compositing mode is required. The pipeline is
            // premultiplied (shader outputs color*alpha, blend is One/OneMinusSrcAlpha),
            // so PreMultipliedBit is exact; PostMultipliedBit still alpha-composites
            // (the compositor unpremultiplies), Inherit lets the compositor choose, and
            // OpaqueBit is only the last resort when the surface supports nothing else.
            return (supported & CompositeAlphaFlagsKHR.PreMultipliedBit) != 0
                ? CompositeAlphaFlagsKHR.PreMultipliedBit
                : (supported & CompositeAlphaFlagsKHR.PostMultipliedBit) != 0
                    ? CompositeAlphaFlagsKHR.PostMultipliedBit
                    : (supported & CompositeAlphaFlagsKHR.InheritBit) != 0
                        ? CompositeAlphaFlagsKHR.InheritBit
                        : CompositeAlphaFlagsKHR.OpaqueBit;
        }

        // Ordinary opaque window: keep the historical preference for OpaqueBit — a
        // non-opaque composite mode on a fully opaque window is where
        // compositor-dependent artifacts (fringe/halo) come from.
        return (supported & CompositeAlphaFlagsKHR.OpaqueBit) != 0
            ? CompositeAlphaFlagsKHR.OpaqueBit
            : (supported & CompositeAlphaFlagsKHR.PreMultipliedBit) != 0
                ? CompositeAlphaFlagsKHR.PreMultipliedBit
                : (supported & CompositeAlphaFlagsKHR.PostMultipliedBit) != 0
                    ? CompositeAlphaFlagsKHR.PostMultipliedBit
                    : CompositeAlphaFlagsKHR.InheritBit;
    }

    private void RecreateSwapchain()
    {
        WaitQueueIdle();
        DestroySwapchain();
        CreateSwapchain();
    }

    private unsafe void DestroySwapchain()
    {
        if (_framebuffers.Length > 0)
        {
            foreach (FramebufferHandle framebuffer in _framebuffers)
            {
                if (framebuffer != default)
                {
                    Vk.DestroyFramebuffer(Device.Device, framebuffer, null);
                }
            }

            _framebuffers = [];
        }

        if (_views.Length > 0)
        {
            foreach (ImageViewHandle view in _views)
            {
                if (view != default)
                {
                    Vk.DestroyImageView(Device.Device, view, null);
                }
            }

            _views = [];
        }

        if (_msaaViews.Length > 0)
        {
            foreach (ImageViewHandle view in _msaaViews)
            {
                if (view != default)
                {
                    Vk.DestroyImageView(Device.Device, view, null);
                }
            }

            _msaaViews = [];
        }

        if (_msaaMemories.Length > 0)
        {
            foreach (DeviceMemoryHandle memory in _msaaMemories)
            {
                if (memory != default)
                {
                    Vk.FreeMemory(Device.Device, memory, null);
                }
            }

            _msaaMemories = [];
        }

        if (_msaaImages.Length > 0)
        {
            foreach (ImageHandle image in _msaaImages)
            {
                if (image != default)
                {
                    Vk.DestroyImage(Device.Device, image, null);
                }
            }

            _msaaImages = [];
        }

        _images = [];
        if (_swapchain == default)
        {
            return;
        }

        Vk.DestroySwapchainKHR(Device.Device, _swapchain, null);
        _swapchain = default;
    }
}
