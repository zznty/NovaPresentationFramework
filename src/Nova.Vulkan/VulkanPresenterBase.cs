using System.Runtime.InteropServices;
using Nova.Geometry;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>
/// Shared GPU machinery for the offscreen and surface presenters: pipeline, texture
/// registry, vertex staging, per-frame command buffer, and the render loop. All
/// Silk.NET unsafe calls stay here. Not internally synchronized; drive from one thread.
/// </summary>
internal abstract class VulkanPresenterBase(VulkanDevice device, PixelSize size) : IVulkanPresenter
{
    private const uint DescriptorSetsPerPool = 64;
    private const nuint VertexBufferInitialBytes = 16 * 1024;

    private static readonly byte[] WhitePixel = [255, 255, 255, 255];

    private CommandPoolHandle _commandPool;
    private CommandBufferHandle _commandBuffer;
    private FenceHandle _frameFence;

    private DescriptorSetLayoutHandle _descriptorSetLayout;
    private DescriptorPoolHandle _descriptorPool;
    private readonly List<DescriptorPoolHandle> _descriptorPools = [];
    private SamplerHandle _sampler;

    private PipelineLayoutHandle _pipelineLayout;
    private PipelineHandle _pipeline;
    private PipelineLayoutHandle _gradientPipelineLayout;
    private PipelineHandle _gradientPipeline;
    private RenderPassHandle _renderPass;

    private GpuTexture? _whiteTexture;
    private readonly Dictionary<uint, GpuTexture> _textures = [];
    private uint _nextTextureHandle = 1;

    private BufferHandle _vertexBuffer;
    private DeviceMemoryHandle _vertexMemory;
    private unsafe void* _vertexMapped;
    private nuint _vertexCapacity;

    private BufferHandle _readbackBuffer;
    private DeviceMemoryHandle _readbackMemory;
    private unsafe void* _readbackMapped;
    private PixelSize _readbackSize;
    private bool _readbackEnabled;

    private bool _colorTargetInitialized;
    private bool _disposed;

    /// <summary>True when the swapchain images were created with transfer-source usage (required for window readback).</summary>
    internal bool ReadbackSupported { get; set; }

    private protected VulkanDevice Device { get; } = device;

    private protected IVk Vk { get; } = device.Api;

    private protected bool UseDynamicRendering => Device.SupportsDynamicRendering;

    private protected Format ColorFormat { get; private set; }

    private protected PixelSize TargetSize { get; private set; } = size;

    private protected void SetTargetSize(PixelSize newSize)
    {
        TargetSize = newSize;
    }

    private protected SemaphoreHandle AcquireSemaphore { get; private set; }

    private SemaphoreHandle[] _renderFinishedSemaphores = [];

    public PixelSize PixelSize => TargetSize;

    private protected abstract bool IsSurfacePresenter { get; }

    private protected abstract uint AcquireImage();

    private protected abstract ImageHandle GetColorImage(uint imageIndex);

    /// <summary>The multisampled color-attachment view when MSAA is active, else the single-sample view.</summary>
    private protected abstract ImageViewHandle GetColorView(uint imageIndex);

    /// <summary>The multisampled color image (used for the per-frame discard transition).</summary>
    private protected abstract ImageHandle GetMsaaImage(uint imageIndex);

    /// <summary>The single-sample resolve target view (readback/presentation reads this).</summary>
    private protected abstract ImageViewHandle GetResolveView(uint imageIndex);

    private protected abstract FramebufferHandle GetFramebuffer(uint imageIndex);

    private protected abstract void ResizeTarget(PixelSize newSize);

    private protected abstract void DisposeTarget();

    private protected abstract void PresentFrame(uint imageIndex);

    /// <summary>Multisample count of the color attachment (1 = no MSAA).</summary>
    private protected SampleCountFlags SampleCount { get; private set; } = SampleCountFlags.Count1Bit;

    private protected bool UseMsaa => SampleCount != SampleCountFlags.Count1Bit;

    /// <summary>Initializes shared resources; must run after the derived ctor state is ready.</summary>
    private protected void Initialize(Format colorFormat)
    {
        bool initialized = false;
        try
        {
            CreateSynchronizationObjects();
            ColorFormat = colorFormat;
            SampleCount = PickSampleCount();
            CreateDescriptorInfrastructure();
            CreateRenderPass();
            CreatePipeline();
            CreateGradientPipeline();
            _whiteTexture = (GpuTexture)CreateTexture(new TextureUpload(new PixelSize(1, 1), PixelFormat.Rgba8Unorm, WhitePixel, 4));
            EnsureVertexCapacity(VertexBufferInitialBytes);
            initialized = true;
        }
        finally
        {
            if (!initialized)
            {
                Dispose();
            }
        }
    }

    public unsafe IGpuTexture CreateTexture(TextureUpload upload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (upload.Size.IsEmpty)
        {
            throw new ArgumentException("Texture size is empty.", nameof(upload));
        }

        int bytesPerPixel = BytesPerPixel(upload.Format);
        long rowBytes = (long)upload.Size.Width * bytesPerPixel;
        long minimumBytes = (((long)upload.Size.Height - 1) * upload.StrideBytes) + rowBytes;
        if (upload.Pixels.Length < minimumBytes)
        {
            throw new ArgumentException("The pixel buffer is smaller than the texture size and stride require.", nameof(upload));
        }

        Format format = ToVulkanFormat(upload.Format);
        ImageHandle image = CreateImage((uint)upload.Size.Width, (uint)upload.Size.Height, format, ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit);
        DeviceMemoryHandle memory = AllocateMemory(GetImageMemoryRequirements(image), MemoryPropertyFlags.DeviceLocalBit, MemoryPropertyFlags.DeviceLocalBit);
        bool bound = false;
        try
        {
            VkApi.Check(Vk.BindImageMemory(Device.Device, image, memory, 0), nameof(Vk.BindImageMemory));
            bound = true;
        }
        finally
        {
            if (!bound)
            {
                Vk.FreeMemory(Device.Device, memory, null);
                Vk.DestroyImage(Device.Device, image, null);
            }
        }

        ImageViewHandle view = CreateImageView(image, format);
        DescriptorSetHandle descriptorSet = AllocateDescriptorSet();
        UpdateDescriptorSet(descriptorSet, view);

        var texture = new GpuTexture(this, new TextureHandle(_nextTextureHandle++), upload.Size, upload.Format, image, view, memory, descriptorSet);
        bool uploaded = false;
        try
        {
            UploadTexturePixels(texture, upload, 0, 0, ImageLayout.Undefined);
            uploaded = true;
        }
        finally
        {
            if (!uploaded)
            {
                Vk.DestroyImageView(Device.Device, view, null);
                Vk.FreeMemory(Device.Device, memory, null);
                Vk.DestroyImage(Device.Device, image, null);
            }
        }

        _textures.Add(texture.Handle.Value, texture);
        return texture;
    }

    public void UpdateTexture(TextureHandle texture, int destinationX, int destinationY, TextureUpload upload)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationX);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationY);
        if (!_textures.TryGetValue(texture.Value, out GpuTexture? existing))
        {
            throw new ArgumentException($"Unknown texture handle {texture.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}.", nameof(texture));
        }

        if (existing.Format != upload.Format)
        {
            throw new ArgumentException("Upload format does not match the texture.", nameof(upload));
        }

        if (upload.Size.IsEmpty)
        {
            throw new ArgumentException("Upload size is empty.", nameof(upload));
        }

        if (destinationX + upload.Size.Width > existing.Size.Width ||
            destinationY + upload.Size.Height > existing.Size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationX), "Upload rectangle is outside the texture.");
        }

        int bytesPerPixel = BytesPerPixel(upload.Format);
        long rowBytes = (long)upload.Size.Width * bytesPerPixel;
        long minimumBytes = (((long)upload.Size.Height - 1) * upload.StrideBytes) + rowBytes;
        if (upload.Pixels.Length < minimumBytes)
        {
            throw new ArgumentException("The pixel buffer is smaller than the texture size and stride require.", nameof(upload));
        }

        UploadTexturePixels(existing, upload, destinationX, destinationY, ImageLayout.ShaderReadOnlyOptimal);
    }

    public void DestroyTexture(TextureHandle texture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_textures.Remove(texture.Value, out GpuTexture? removed))
        {
            return;
        }

        removed.Dispose();
    }

    public void Resize(PixelSize newSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (newSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(newSize));
        }

        if (newSize == TargetSize)
        {
            return;
        }

        TargetSize = newSize;
        _colorTargetInitialized = false;
        ResizeTarget(newSize);
    }

    public void Render(Action<IRasterCommandList> record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(record);
        var queue = new RecordQueue();
        record(queue);
        RenderQueue(queue);
    }

    public unsafe ReadOnlyMemory<byte> ReadbackRgba()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsSurfacePresenter)
        {
            return ReadbackSurfaceRgba();
        }

        int width = TargetSize.Width;
        int height = TargetSize.Height;
        ulong byteCount = (ulong)width * (ulong)height * 4;
        BufferHandle buffer = CreateBuffer(
            byteCount,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out DeviceMemoryHandle memory,
            out void* mapped);
        try
        {
            ImageHandle image = GetColorImage(0);
            RunCommands(commandBuffer =>
            {
                ImageLayout oldLayout = _colorTargetInitialized ? ImageLayout.ColorAttachmentOptimal : ImageLayout.Undefined;
                AccessFlags srcAccess = _colorTargetInitialized ? AccessFlags.ColorAttachmentWriteBit : AccessFlags.None;
                PipelineStageFlags srcStage = _colorTargetInitialized ? PipelineStageFlags.ColorAttachmentOutputBit : PipelineStageFlags.TopOfPipeBit;
                TransitionImage(commandBuffer, image, oldLayout, ImageLayout.TransferSrcOptimal, srcAccess, AccessFlags.TransferReadBit, srcStage, PipelineStageFlags.TransferBit);
                var region = new BufferImageCopy
                {
                    BufferOffset = 0,
                    BufferRowLength = 0,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
                    ImageExtent = new Extent3D { Width = (uint)width, Height = (uint)height, Depth = 1 }
                };
                Vk.CmdCopyImageToBuffer(commandBuffer, image, ImageLayout.TransferSrcOptimal, buffer, 1, &region);
                TransitionImage(commandBuffer, image, ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal, AccessFlags.TransferReadBit, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.TransferBit, PipelineStageFlags.ColorAttachmentOutputBit);
            });

            _colorTargetInitialized = true;
            byte[] result = new byte[checked((int)byteCount)];
            Marshal.Copy((nint)mapped, result, 0, result.Length);
            return result;
        }
        finally
        {
            Vk.DestroyBuffer(Device.Device, buffer, null);
            Vk.FreeMemory(Device.Device, memory, null);
        }
    }

    /// <summary>
    /// Opt-in window-presenter readback: allocates the host-visible staging buffer and adds a
    /// copy of every presented frame to the render submission. Offscreen presenters ignore this
    /// (readback is their purpose). Throws when the surface cannot create transfer-source
    /// swapchain images.
    /// </summary>
    public void EnableReadback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSurfacePresenter)
        {
            return;
        }

        if (!ReadbackSupported)
        {
            throw new VulkanException("The window surface does not support transfer-source swapchain image usage, so window-presenter readback is unavailable.");
        }

        _readbackEnabled = true;
        EnsureReadbackStaging(TargetSize);
    }

    /// <summary>
    /// Window-presenter readback: returns the bytes of the most recently presented frame.
    /// The frame's copy into the staging buffer happens inside the render submission (see
    /// <see cref="CopyColorToReadbackStaging"/>), so the frame fence already guarantees the
    /// copy is complete; this method only maps and normalizes. Requires
    /// <see cref="EnableReadback"/> to have been called and at least one frame rendered.
    /// </summary>
    private unsafe ReadOnlyMemory<byte> ReadbackSurfaceRgba()
    {
        if (!_readbackEnabled)
        {
            throw new InvalidOperationException("Window-presenter readback is disabled. Call IVulkanPresenter.EnableReadback() before rendering to enable it.");
        }

        if (!ReadbackSupported)
        {
            throw new VulkanException("The window surface does not support transfer-source swapchain image usage, so window-presenter readback is unavailable.");
        }

        if (_readbackBuffer == default)
        {
            throw new InvalidOperationException("No frame has been presented yet; render at least one frame before reading back pixels.");
        }

        ulong byteCount = (ulong)_readbackSize.Width * (ulong)_readbackSize.Height * 4;
        byte[] result = new byte[checked((int)byteCount)];
        Marshal.Copy((nint)_readbackMapped, result, 0, result.Length);
        NormalizeByteOrder(result, ColorFormat);
        return result;
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WaitQueueIdle();
        DisposeTarget();
        foreach (GpuTexture texture in _textures.Values)
        {
            texture.Dispose();
        }

        _textures.Clear();
        _whiteTexture?.Dispose();
        _whiteTexture = null;
        if (_vertexBuffer != default)
        {
            Vk.DestroyBuffer(Device.Device, _vertexBuffer, null);
            Vk.FreeMemory(Device.Device, _vertexMemory, null);
            _vertexBuffer = default;
        }

        if (_readbackBuffer != default)
        {
            Vk.DestroyBuffer(Device.Device, _readbackBuffer, null);
            Vk.FreeMemory(Device.Device, _readbackMemory, null);
            _readbackBuffer = default;
        }

        if (_pipeline != default)
        {
            Vk.DestroyPipeline(Device.Device, _pipeline, null);
            _pipeline = default;
        }

        if (_pipelineLayout != default)
        {
            Vk.DestroyPipelineLayout(Device.Device, _pipelineLayout, null);
            _pipelineLayout = default;
        }

        if (_gradientPipeline != default)
        {
            Vk.DestroyPipeline(Device.Device, _gradientPipeline, null);
            _gradientPipeline = default;
        }

        if (_gradientPipelineLayout != default)
        {
            Vk.DestroyPipelineLayout(Device.Device, _gradientPipelineLayout, null);
            _gradientPipelineLayout = default;
        }

        if (_renderPass != default)
        {
            Vk.DestroyRenderPass(Device.Device, _renderPass, null);
            _renderPass = default;
        }

        if (_sampler != default)
        {
            Vk.DestroySampler(Device.Device, _sampler, null);
            _sampler = default;
        }

        foreach (DescriptorPoolHandle pool in _descriptorPools)
        {
            Vk.DestroyDescriptorPool(Device.Device, pool, null);
        }

        _descriptorPools.Clear();
        if (_descriptorSetLayout != default)
        {
            Vk.DestroyDescriptorSetLayout(Device.Device, _descriptorSetLayout, null);
            _descriptorSetLayout = default;
        }

        DestroyRenderFinishedSemaphores();

        if (AcquireSemaphore != default)
        {
            Vk.DestroySemaphore(Device.Device, AcquireSemaphore, null);
            AcquireSemaphore = default;
        }

        if (_frameFence != default)
        {
            Vk.DestroyFence(Device.Device, _frameFence, null);
            _frameFence = default;
        }

        if (_commandBuffer != default)
        {
            CommandBufferHandle commandBuffer = _commandBuffer;
            Vk.FreeCommandBuffers(Device.Device, _commandPool, 1, &commandBuffer);
            _commandBuffer = default;
        }

        if (_commandPool == default)
        {
            return;
        }

        Vk.DestroyCommandPool(Device.Device, _commandPool, null);
        _commandPool = default;
    }

    internal unsafe void DestroyTextureResources(GpuTexture texture)
    {
        if (texture.IsDestroyed)
        {
            return;
        }

        if (texture.View != default)
        {
            Vk.DestroyImageView(Device.Device, texture.View, null);
        }

        if (texture.Image != default)
        {
            Vk.DestroyImage(Device.Device, texture.Image, null);
        }

        if (texture.Memory != default)
        {
            Vk.FreeMemory(Device.Device, texture.Memory, null);
        }
    }

    private unsafe void RenderQueue(RecordQueue queue)
    {
        uint imageIndex = AcquireImage();
        ImageHandle colorImage = GetColorImage(imageIndex);
        ImageViewHandle colorView = GetColorView(imageIndex);

        EnsureVertexCapacity((nuint)(queue.Records.Count * 6 * sizeof(RasterVertex)));
        List<DrawCommand> draws = new(queue.Records.Count);
        byte* vertexBase = (byte*)_vertexMapped;
        uint firstVertex = 0;
        foreach (QuadRecord record in queue.Records)
        {
            DescriptorSetHandle descriptorSet;
            if (record.Texture.IsValid)
            {
                if (!_textures.TryGetValue(record.Texture.Value, out GpuTexture? texture))
                {
                    throw new ArgumentException($"Unknown texture handle {record.Texture.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}.", nameof(queue));
                }

                descriptorSet = texture.DescriptorSet;
            }
            else
            {
                descriptorSet = _whiteTexture!.DescriptorSet;
            }

            if (!ComputeScissor(record.Clip, TargetSize.Width, TargetSize.Height, out Rect2D scissor))
            {
                draws.Add(new DrawCommand(firstVertex, 0, descriptorSet, scissor, record.IsGradient, (int)record.GradientKind, (int)record.Spread));
                continue;
            }

            RasterVertex* vertices = (RasterVertex*)(vertexBase + (firstVertex * (nuint)sizeof(RasterVertex)));
            uint vertexCount = record.IsTriangle ? 3u : 6u;
            WriteQuad(vertices, record, TargetSize.Width, TargetSize.Height, record.IsTriangle);
            draws.Add(new DrawCommand(firstVertex, vertexCount, descriptorSet, scissor, record.IsGradient, (int)record.GradientKind, (int)record.Spread));
            firstVertex += vertexCount;
        }

        VkApi.Check(Vk.ResetCommandBuffer(_commandBuffer, CommandBufferResetFlags.None), nameof(Vk.ResetCommandBuffer));
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VkApi.Check(Vk.BeginCommandBuffer(_commandBuffer, &beginInfo), nameof(Vk.BeginCommandBuffer));
        CommandBufferHandle commandBuffer = _commandBuffer;

        ImageLayout initialLayout = IsSurfacePresenter ? ImageLayout.Undefined : (_colorTargetInitialized ? ImageLayout.ColorAttachmentOptimal : ImageLayout.Undefined);
        if (initialLayout != ImageLayout.ColorAttachmentOptimal)
        {
            TransitionImage(commandBuffer, colorImage, initialLayout, ImageLayout.ColorAttachmentOptimal, AccessFlags.None, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit);
        }

        if (UseMsaa)
        {
            // The multisampled attachment is discarded every frame (contents cleared), so an
            // Undefined->ColorAttachmentOptimal transition with no source access is always legal.
            TransitionImage(commandBuffer, GetMsaaImage(imageIndex), ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal, AccessFlags.None, AccessFlags.ColorAttachmentWriteBit, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.ColorAttachmentOutputBit);
        }

        var clearValue = new ClearValue();
        if (queue.HasClear)
        {
            ColorRgba color = queue.ClearColor;
            clearValue.Color.Float32[0] = color.R;
            clearValue.Color.Float32[1] = color.G;
            clearValue.Color.Float32[2] = color.B;
            clearValue.Color.Float32[3] = color.A;
        }

        var renderArea = new Rect2D
        {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = new Extent2D { Width = (uint)TargetSize.Width, Height = (uint)TargetSize.Height }
        };

        if (UseDynamicRendering)
        {
            var attachment = new RenderingAttachmentInfo
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = colorView,
                ImageLayout = ImageLayout.ColorAttachmentOptimal,
                LoadOp = AttachmentLoadOp.Clear,
                StoreOp = UseMsaa ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store,
                ClearValue = clearValue
            };
            if (UseMsaa)
            {
                attachment.ResolveMode = ResolveModeFlags.AverageBit;
                attachment.ResolveImageView = GetResolveView(imageIndex);
                attachment.ResolveImageLayout = ImageLayout.ColorAttachmentOptimal;
            }

            var renderingInfo = new RenderingInfo
            {
                SType = StructureType.RenderingInfo,
                RenderArea = renderArea,
                LayerCount = 1,
                ColorAttachmentCount = 1,
                PColorAttachments = &attachment
            };
            Vk.CmdBeginRendering(commandBuffer, &renderingInfo);
        }
        else
        {
            var renderPassBegin = new RenderPassBeginInfo
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = _renderPass,
                Framebuffer = GetFramebuffer(imageIndex),
                RenderArea = renderArea,
                ClearValueCount = 1,
                PClearValues = &clearValue
            };
            Vk.CmdBeginRenderPass(commandBuffer, &renderPassBegin, SubpassContents.Inline);
        }

        // The clip-space Y axis of the contract (ndcY = 1 - y/height*2) is top-down,
        // so the viewport is flipped vertically: ndcY = +1 lands on framebuffer row 0.
        var viewport = new Viewport
        {
            X = 0,
            Y = TargetSize.Height,
            Width = TargetSize.Width,
            Height = -TargetSize.Height,
            MinDepth = 0,
            MaxDepth = 1
        };
        Vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);
        Rect2D fullScissor = renderArea;
        Vk.CmdSetScissor(commandBuffer, 0, 1, &fullScissor);
        Vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);
        BufferHandle vertexBuffer = _vertexBuffer;
        ulong vertexOffset = 0;
        Vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &vertexOffset);

        DescriptorSetHandle lastSet = default;
        PipelineHandle currentPipeline = default;
        int* gradientParams = stackalloc int[2];
        foreach (DrawCommand draw in draws)
        {
            if (draw.VertexCount == 0)
            {
                continue;
            }

            PipelineHandle pipeline = draw.IsGradient ? _gradientPipeline : _pipeline;
            PipelineLayoutHandle layout = draw.IsGradient ? _gradientPipelineLayout : _pipelineLayout;
            if (pipeline != currentPipeline)
            {
                Vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
                currentPipeline = pipeline;
            }

            if (draw.IsGradient)
            {
                gradientParams[0] = draw.GradientKind;
                gradientParams[1] = draw.Spread;
                Vk.CmdPushConstants(commandBuffer, _gradientPipelineLayout, ShaderStageFlags.FragmentBit, 0, 8, gradientParams);
            }

            if (draw.DescriptorSet != lastSet)
            {
                DescriptorSetHandle set = draw.DescriptorSet;
                Vk.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Graphics, layout, 0, 1, &set, 0, null);
                lastSet = set;
            }

            Rect2D scissor = draw.Scissor;
            Vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
            Vk.CmdDraw(commandBuffer, draw.VertexCount, 1, draw.FirstVertex, 0);
        }

        if (UseDynamicRendering)
        {
            Vk.CmdEndRendering(commandBuffer);
        }
        else
        {
            Vk.CmdEndRenderPass(commandBuffer);
        }

        if (IsSurfacePresenter)
        {
            if (_readbackEnabled)
            {
                // Copy the rendered image into the persistent host-visible staging buffer while
                // the application still owns it, then hand it to the presentation engine.
                CopyColorToReadbackStaging(commandBuffer, colorImage);
            }
            else
            {
                // Default (readback off): the original single layout transition, nothing else.
                TransitionImage(commandBuffer, colorImage, ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKHR, AccessFlags.ColorAttachmentWriteBit, AccessFlags.None, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.BottomOfPipeBit);
            }
        }
        else
        {
            _colorTargetInitialized = true;
        }

        VkApi.Check(Vk.EndCommandBuffer(commandBuffer), nameof(Vk.EndCommandBuffer));

        FenceHandle frameFence = _frameFence;
        VkApi.Check(Vk.ResetFences(Device.Device, 1, &frameFence), nameof(Vk.ResetFences));
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        if (IsSurfacePresenter)
        {
            PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
            SemaphoreHandle acquireSemaphore = AcquireSemaphore;
            SemaphoreHandle renderFinishedSemaphore = RenderFinishedFor(imageIndex);
            submit.WaitSemaphoreCount = 1;
            submit.PWaitSemaphores = &acquireSemaphore;
            submit.PWaitDstStageMask = &waitStage;
            submit.SignalSemaphoreCount = 1;
            submit.PSignalSemaphores = &renderFinishedSemaphore;
        }

        VkApi.Check(Vk.QueueSubmit(Device.Queue, 1, &submit, frameFence), nameof(Vk.QueueSubmit));
        if (IsSurfacePresenter)
        {
            PresentFrame(imageIndex);
        }

        VkApi.Check(Vk.WaitForFences(Device.Device, 1, &frameFence, 1, ulong.MaxValue), nameof(Vk.WaitForFences));
    }

    /// <summary>
    /// Records the swapchain image -&gt; host-visible staging copy for window-presenter readback.
    /// Runs inside the frame submission while the image is still owned by the application, so
    /// the readback never touches a presented image and needs no extra acquire/present round trip.
    /// The image leaves the render pass in <see cref="ImageLayout.ColorAttachmentOptimal"/> and
    /// is left in <see cref="ImageLayout.PresentSrcKHR"/> for <c>QueuePresentKHR</c>.
    /// </summary>
    private unsafe void CopyColorToReadbackStaging(CommandBufferHandle commandBuffer, ImageHandle colorImage)
    {
        if (!ReadbackSupported)
        {
            throw new VulkanException("The window surface does not support transfer-source swapchain image usage, so window-presenter readback is unavailable.");
        }

        EnsureReadbackStaging(TargetSize);
        uint width = (uint)TargetSize.Width;
        uint height = (uint)TargetSize.Height;
        TransitionImage(
            commandBuffer,
            colorImage,
            ImageLayout.ColorAttachmentOptimal,
            ImageLayout.TransferSrcOptimal,
            AccessFlags.ColorAttachmentWriteBit,
            AccessFlags.TransferReadBit,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.TransferBit);
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D { X = 0, Y = 0, Z = 0 },
            ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
        };
        Vk.CmdCopyImageToBuffer(commandBuffer, colorImage, ImageLayout.TransferSrcOptimal, _readbackBuffer, 1, &region);
        TransitionImage(
            commandBuffer,
            colorImage,
            ImageLayout.TransferSrcOptimal,
            ImageLayout.PresentSrcKHR,
            AccessFlags.TransferReadBit,
            AccessFlags.None,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.BottomOfPipeBit);
    }

    private unsafe void EnsureReadbackStaging(PixelSize size)
    {
        if (_readbackBuffer != default && _readbackSize == size)
        {
            return;
        }

        if (_readbackBuffer != default)
        {
            Vk.DestroyBuffer(Device.Device, _readbackBuffer, null);
            Vk.FreeMemory(Device.Device, _readbackMemory, null);
            _readbackBuffer = default;
        }

        ulong byteCount = (ulong)size.Width * (ulong)size.Height * 4;
        _readbackBuffer = CreateBuffer(
            byteCount,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out _readbackMemory,
            out _readbackMapped);
        _readbackSize = size;
    }

    private static unsafe void WriteQuad(RasterVertex* vertices, QuadRecord record, int width, int height, bool triangle)
    {
        vertices[0] = new RasterVertex(NdcX(record.P0.X, width), NdcY(record.P0.Y, height), (float)record.Uv0.X, (float)record.Uv0.Y, record.R, record.G, record.B, record.A);
        vertices[1] = new RasterVertex(NdcX(record.P1.X, width), NdcY(record.P1.Y, height), (float)record.Uv1.X, (float)record.Uv1.Y, record.R, record.G, record.B, record.A);
        vertices[2] = new RasterVertex(NdcX(record.P2.X, width), NdcY(record.P2.Y, height), (float)record.Uv2.X, (float)record.Uv2.Y, record.R, record.G, record.B, record.A);
        if (triangle)
        {
            return;
        }

        vertices[3] = new RasterVertex(NdcX(record.P0.X, width), NdcY(record.P0.Y, height), (float)record.Uv0.X, (float)record.Uv0.Y, record.R, record.G, record.B, record.A);
        vertices[4] = new RasterVertex(NdcX(record.P2.X, width), NdcY(record.P2.Y, height), (float)record.Uv2.X, (float)record.Uv2.Y, record.R, record.G, record.B, record.A);
        vertices[5] = new RasterVertex(NdcX(record.P3.X, width), NdcY(record.P3.Y, height), (float)record.Uv3.X, (float)record.Uv3.Y, record.R, record.G, record.B, record.A);
    }

    private static float NdcX(double x, int width)
    {
        return (float)((x / width * 2.0) - 1.0);
    }

    private static float NdcY(double y, int height)
    {
        return (float)(1.0 - (y / height * 2.0));
    }

    private static bool ComputeScissor(Rect? clip, int width, int height, out Rect2D scissor)
    {
        if (clip is not { } rectangle)
        {
            scissor = new Rect2D
            {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = new Extent2D { Width = (uint)width, Height = (uint)height }
            };
            return true;
        }

        int left = Math.Clamp((int)Math.Ceiling(rectangle.Left), 0, width);
        int top = Math.Clamp((int)Math.Ceiling(rectangle.Top), 0, height);
        int right = Math.Clamp((int)Math.Floor(rectangle.Right), 0, width);
        int bottom = Math.Clamp((int)Math.Floor(rectangle.Bottom), 0, height);
        if (right <= left || bottom <= top)
        {
            scissor = default;
            return false;
        }

        scissor = new Rect2D
        {
            Offset = new Offset2D { X = left, Y = top },
            Extent = new Extent2D { Width = (uint)(right - left), Height = (uint)(bottom - top) }
        };
        return true;
    }

    private unsafe void RunCommands(Action<CommandBufferHandle> record)
    {
        CommandBufferHandle commandBuffer = _commandBuffer;
        VkApi.Check(Vk.ResetCommandBuffer(commandBuffer, CommandBufferResetFlags.None), nameof(Vk.ResetCommandBuffer));
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        VkApi.Check(Vk.BeginCommandBuffer(commandBuffer, &beginInfo), nameof(Vk.BeginCommandBuffer));
        record(commandBuffer);
        VkApi.Check(Vk.EndCommandBuffer(commandBuffer), nameof(Vk.EndCommandBuffer));
        FenceHandle frameFence = _frameFence;
        VkApi.Check(Vk.ResetFences(Device.Device, 1, &frameFence), nameof(Vk.ResetFences));
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        VkApi.Check(Vk.QueueSubmit(Device.Queue, 1, &submit, frameFence), nameof(Vk.QueueSubmit));
        VkApi.Check(Vk.WaitForFences(Device.Device, 1, &frameFence, 1, ulong.MaxValue), nameof(Vk.WaitForFences));
    }

    private unsafe void TransitionImage(CommandBufferHandle commandBuffer, ImageHandle image, ImageLayout oldLayout, ImageLayout newLayout, AccessFlags srcAccess, AccessFlags dstAccess, PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = uint.MaxValue,
            DstQueueFamilyIndex = uint.MaxValue,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };
        Vk.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, DependencyFlags.None, 0, null, 0, null, 1, &barrier);
    }

    private unsafe BufferHandle CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags required, MemoryPropertyFlags preferred, out DeviceMemoryHandle memory, out void* mapped)
    {
        var createInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };
        BufferHandle buffer;
        VkApi.Check(Vk.CreateBuffer(Device.Device, &createInfo, null, &buffer), nameof(Vk.CreateBuffer));
        memory = AllocateMemory(size, required, preferred);
        bool bound = false;
        try
        {
            VkApi.Check(Vk.BindBufferMemory(Device.Device, buffer, memory, 0), nameof(Vk.BindBufferMemory));
            bound = true;
        }
        finally
        {
            if (!bound)
            {
                Vk.FreeMemory(Device.Device, memory, null);
                Vk.DestroyBuffer(Device.Device, buffer, null);
            }
        }

        mapped = null;
        if ((required & MemoryPropertyFlags.HostVisibleBit) == 0)
        {
            return buffer;
        }

        void* data;
        VkApi.Check(Vk.MapMemory(Device.Device, memory, 0, size, MemoryMapFlags.None, &data), nameof(Vk.MapMemory));
        mapped = data;

        return buffer;
    }

    private protected unsafe DeviceMemoryHandle AllocateMemory(ulong size, MemoryPropertyFlags required, MemoryPropertyFlags preferred)
    {
        PhysicalDeviceMemoryProperties properties;
        Vk.GetPhysicalDeviceMemoryProperties(Device.PhysicalDevice, &properties);
        int fallbackIndex = -1;
        int bestIndex = -1;
        for (int i = 0; i < (int)properties.MemoryTypeCount; i++)
        {
            MemoryPropertyFlags flags = properties.MemoryTypes[i].PropertyFlags;
            if ((flags & required) != required)
            {
                continue;
            }

            if (fallbackIndex < 0)
            {
                fallbackIndex = i;
            }

            if ((flags & preferred) != preferred)
            {
                continue;
            }

            bestIndex = i;
            break;
        }

        int index = bestIndex >= 0 ? bestIndex : fallbackIndex;
        if (index < 0)
        {
            throw new VulkanException($"No memory type supports the required property flags {required}.");
        }

        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = (uint)index
        };
        DeviceMemoryHandle memory;
        VkApi.Check(Vk.AllocateMemory(Device.Device, &allocateInfo, null, &memory), nameof(Vk.AllocateMemory));
        return memory;
    }

    private protected unsafe ulong GetImageMemoryRequirements(ImageHandle image)
    {
        MemoryRequirements requirements;
        Vk.GetImageMemoryRequirements(Device.Device, image, &requirements);
        return requirements.Size;
    }

    private unsafe void EnsureVertexCapacity(nuint requiredBytes)
    {
        if (_vertexCapacity >= requiredBytes)
        {
            return;
        }

        nuint newCapacity = Math.Max(requiredBytes, Math.Max(VertexBufferInitialBytes, _vertexCapacity * 2));
        if (_vertexBuffer != default)
        {
            Vk.DestroyBuffer(Device.Device, _vertexBuffer, null);
            Vk.FreeMemory(Device.Device, _vertexMemory, null);
        }

        BufferHandle buffer = CreateBuffer(
            newCapacity,
            BufferUsageFlags.VertexBufferBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out DeviceMemoryHandle memory,
            out void* mapped);
        _vertexBuffer = buffer;
        _vertexMemory = memory;
        _vertexMapped = mapped;
        _vertexCapacity = newCapacity;
    }

    private unsafe void UploadTexturePixels(
        GpuTexture texture,
        TextureUpload upload,
        int destinationX,
        int destinationY,
        ImageLayout sourceLayout)
    {
        int bytesPerPixel = BytesPerPixel(upload.Format);
        uint width = (uint)upload.Size.Width;
        uint height = (uint)upload.Size.Height;
        ulong bufferSize = (ulong)upload.StrideBytes * height;
        uint bufferRowLength = upload.StrideBytes == width * bytesPerPixel ? 0u : (uint)(upload.StrideBytes / bytesPerPixel);
        BufferHandle buffer = CreateBuffer(
            bufferSize,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            out DeviceMemoryHandle memory,
            out void* mapped);
        try
        {
            fixed (byte* source = upload.Pixels)
            {
                byte* destination = (byte*)mapped;
                for (int row = 0; row < (int)height; row++)
                {
                    Buffer.MemoryCopy(
                        source + ((nint)row * upload.StrideBytes),
                        destination + ((nint)row * upload.StrideBytes),
                        (long)bufferSize,
                        upload.StrideBytes);
                }
            }

            RunCommands(commandBuffer =>
            {
                AccessFlags sourceAccess = sourceLayout == ImageLayout.ShaderReadOnlyOptimal
                    ? AccessFlags.ShaderReadBit
                    : AccessFlags.None;
                PipelineStageFlags sourceStage = sourceLayout == ImageLayout.ShaderReadOnlyOptimal
                    ? PipelineStageFlags.FragmentShaderBit
                    : PipelineStageFlags.TopOfPipeBit;
                TransitionImage(commandBuffer, texture.Image, sourceLayout, ImageLayout.TransferDstOptimal, sourceAccess, AccessFlags.TransferWriteBit, sourceStage, PipelineStageFlags.TransferBit);
                var region = new BufferImageCopy
                {
                    BufferOffset = 0,
                    BufferRowLength = bufferRowLength,
                    BufferImageHeight = 0,
                    ImageSubresource = new ImageSubresourceLayers
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        MipLevel = 0,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    ImageOffset = new Offset3D { X = destinationX, Y = destinationY, Z = 0 },
                    ImageExtent = new Extent3D { Width = width, Height = height, Depth = 1 }
                };
                Vk.CmdCopyBufferToImage(commandBuffer, buffer, texture.Image, ImageLayout.TransferDstOptimal, 1, &region);
                TransitionImage(commandBuffer, texture.Image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit);
            });
        }
        finally
        {
            Vk.DestroyBuffer(Device.Device, buffer, null);
            Vk.FreeMemory(Device.Device, memory, null);
        }
    }

    private unsafe void CreateSynchronizationObjects()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = Device.QueueFamilyIndex
        };
        CommandPoolHandle commandPool;
        VkApi.Check(Vk.CreateCommandPool(Device.Device, &poolInfo, null, &commandPool), nameof(Vk.CreateCommandPool));
        _commandPool = commandPool;

        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };
        CommandBufferHandle commandBuffer;
        VkApi.Check(Vk.AllocateCommandBuffers(Device.Device, &allocateInfo, &commandBuffer), nameof(Vk.AllocateCommandBuffers));
        _commandBuffer = commandBuffer;

        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        FenceHandle fence;
        VkApi.Check(Vk.CreateFence(Device.Device, &fenceInfo, null, &fence), nameof(Vk.CreateFence));
        _frameFence = fence;

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        SemaphoreHandle semaphore;
        VkApi.Check(Vk.CreateSemaphore(Device.Device, &semaphoreInfo, null, &semaphore), nameof(Vk.CreateSemaphore));
        AcquireSemaphore = semaphore;
    }

    /// <summary>
    /// One binary semaphore per swapchain image. Present holds the signaled
    /// render-finished semaphore until that image is re-acquired; a single
    /// semaphore reused across images trips VUID-vkQueueSubmit-pSignalSemaphores-00067.
    /// </summary>
    private protected unsafe void EnsureRenderFinishedSemaphores(int imageCount)
    {
        if (_renderFinishedSemaphores.Length == imageCount)
        {
            return;
        }

        DestroyRenderFinishedSemaphores();
        if (imageCount <= 0)
        {
            return;
        }

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var semaphores = new SemaphoreHandle[imageCount];
        for (int i = 0; i < imageCount; i++)
        {
            SemaphoreHandle semaphore;
            VkApi.Check(Vk.CreateSemaphore(Device.Device, &semaphoreInfo, null, &semaphore), nameof(Vk.CreateSemaphore));
            semaphores[i] = semaphore;
        }

        _renderFinishedSemaphores = semaphores;
    }

    private protected SemaphoreHandle RenderFinishedFor(uint imageIndex)
    {
        return imageIndex < (uint)_renderFinishedSemaphores.Length
            ? _renderFinishedSemaphores[imageIndex]
            : throw new VulkanException($"No render-finished semaphore for swapchain image {imageIndex}.");
    }

    private unsafe void DestroyRenderFinishedSemaphores()
    {
        for (int i = 0; i < _renderFinishedSemaphores.Length; i++)
        {
            SemaphoreHandle semaphore = _renderFinishedSemaphores[i];
            if (semaphore != default)
            {
                Vk.DestroySemaphore(Device.Device, semaphore, null);
            }
        }

        _renderFinishedSemaphores = [];
    }

    private protected void WaitQueueIdle()
    {
        VkApi.Check(Vk.QueueWaitIdle(Device.Queue), nameof(Vk.QueueWaitIdle));
    }

    private unsafe void CreateDescriptorInfrastructure()
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };
        DescriptorSetLayoutHandle setLayout;
        VkApi.Check(Vk.CreateDescriptorSetLayout(Device.Device, &layoutInfo, null, &setLayout), nameof(Vk.CreateDescriptorSetLayout));
        _descriptorSetLayout = setLayout;

        CreateDescriptorPool();

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipLodBias = 0,
            AnisotropyEnable = false,
            MinLod = 0,
            MaxLod = 0
        };
        SamplerHandle sampler;
        VkApi.Check(Vk.CreateSampler(Device.Device, &samplerInfo, null, &sampler), nameof(Vk.CreateSampler));
        _sampler = sampler;
    }

    private unsafe void CreateDescriptorPool()
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = DescriptorSetsPerPool
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.None,
            MaxSets = DescriptorSetsPerPool,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize
        };
        DescriptorPoolHandle pool;
        VkApi.Check(Vk.CreateDescriptorPool(Device.Device, &poolInfo, null, &pool), nameof(Vk.CreateDescriptorPool));
        _descriptorPool = pool;
        _descriptorPools.Add(pool);
    }

    private unsafe DescriptorSetHandle AllocateDescriptorSet()
    {
        DescriptorSetLayoutHandle setLayout = _descriptorSetLayout;
        var allocateInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout
        };
        DescriptorSetHandle set;
        Result result = Vk.AllocateDescriptorSets(Device.Device, &allocateInfo, &set);
        if (result == Result.ErrorOutOfPoolMemory)
        {
            CreateDescriptorPool();
            allocateInfo.DescriptorPool = _descriptorPool;
            result = Vk.AllocateDescriptorSets(Device.Device, &allocateInfo, &set);
        }

        VkApi.Check(result, nameof(Vk.AllocateDescriptorSets));
        return set;
    }

    private unsafe void UpdateDescriptorSet(DescriptorSetHandle set, ImageViewHandle view)
    {
        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _sampler,
            ImageView = view,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo
        };
        Vk.UpdateDescriptorSets(Device.Device, 1, &write, 0, null);
    }

    private protected unsafe ImageHandle CreateImage(uint width, uint height, Format format, ImageUsageFlags usage, SampleCountFlags samples = SampleCountFlags.Count1Bit)
    {
        var createInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D { Width = width, Height = height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = samples,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };
        ImageHandle image;
        VkApi.Check(Vk.CreateImage(Device.Device, &createInfo, null, &image), nameof(Vk.CreateImage));
        return image;
    }

    private protected unsafe ImageViewHandle CreateImageView(ImageHandle image, Format format)
    {
        var subresourceRange = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };
        // R8Unorm is the glyph-atlas format: sample it as (r, r, r, r) so the fragment
        // shader's texture * tint produces premultiplied coverage x tint color.
        bool redSwizzle = format == Format.R8Unorm;
        var components = new ComponentMapping
        {
            R = redSwizzle ? ComponentSwizzle.R : ComponentSwizzle.Identity,
            G = redSwizzle ? ComponentSwizzle.R : ComponentSwizzle.Identity,
            B = redSwizzle ? ComponentSwizzle.R : ComponentSwizzle.Identity,
            A = redSwizzle ? ComponentSwizzle.R : ComponentSwizzle.Identity
        };
        var createInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = components,
            SubresourceRange = subresourceRange
        };
        ImageViewHandle view;
        VkApi.Check(Vk.CreateImageView(Device.Device, &createInfo, null, &view), nameof(Vk.CreateImageView));
        return view;
    }

    private unsafe ShaderModuleHandle CreateShaderModule(ReadOnlySpan<uint> spirv)
    {
        var createInfo = new ShaderModuleCreateInfo
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint)(spirv.Length * sizeof(uint))
        };
        fixed (uint* code = spirv)
        {
            createInfo.PCode = code;
            ShaderModuleHandle module;
            VkApi.Check(Vk.CreateShaderModule(Device.Device, &createInfo, null, &module), nameof(Vk.CreateShaderModule));
            return module;
        }
    }

    /// <summary>Picks a supported multisample count for the color format (4x preferred, else 2x, else 1x).</summary>
    private unsafe SampleCountFlags PickSampleCount()
    {
        var formatInfo = new PhysicalDeviceImageFormatInfo2
        {
            SType = StructureType.PhysicalDeviceImageFormatInfo2,
            Format = ColorFormat,
            Type = ImageType.Type2D,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit
        };
        var properties = new ImageFormatProperties2
        {
            SType = StructureType.ImageFormatProperties2
        };
        Result result = Vk.GetPhysicalDeviceImageFormatProperties2(Device.PhysicalDevice, &formatInfo, &properties);
        if (result != Result.Success)
        {
            return SampleCountFlags.Count1Bit;
        }

        SampleCountFlags supported = properties.ImageFormatProperties.SampleCounts;
        return (supported & SampleCountFlags.Count4Bit) != 0
            ? SampleCountFlags.Count4Bit
            : (supported & SampleCountFlags.Count2Bit) != 0
                ? SampleCountFlags.Count2Bit
                : SampleCountFlags.Count1Bit;
    }

    private unsafe void CreateRenderPass()
    {
        if (UseDynamicRendering)
        {
            return;
        }

        AttachmentDescription* attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = ColorFormat,
            Samples = SampleCount,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = UseMsaa ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = UseMsaa ? ImageLayout.ColorAttachmentOptimal : ImageLayout.ColorAttachmentOptimal
        };
        if (UseMsaa)
        {
            attachments[1] = new AttachmentDescription
            {
                Format = ColorFormat,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.DontCare,
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.ColorAttachmentOptimal,
                FinalLayout = ImageLayout.ColorAttachmentOptimal
            };
        }

        var colorReference = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };
        AttachmentReference resolveReference = UseMsaa
            ? new AttachmentReference { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal }
            : default;
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PResolveAttachments = UseMsaa ? &resolveReference : null
        };
        var createInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = UseMsaa ? 2u : 1u,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass
        };
        RenderPassHandle renderPass;
        VkApi.Check(Vk.CreateRenderPass(Device.Device, &createInfo, null, &renderPass), nameof(Vk.CreateRenderPass));
        _renderPass = renderPass;
    }

    private protected unsafe FramebufferHandle CreateFramebuffer(ImageViewHandle colorView, ImageViewHandle resolveView, uint width, uint height)
    {
        ImageViewHandle* views = stackalloc ImageViewHandle[2];
        views[0] = colorView;
        views[1] = resolveView;
        var createInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = UseMsaa ? 2u : 1u,
            PAttachments = views,
            Width = width,
            Height = height,
            Layers = 1
        };
        FramebufferHandle framebuffer;
        VkApi.Check(Vk.CreateFramebuffer(Device.Device, &createInfo, null, &framebuffer), nameof(Vk.CreateFramebuffer));
        return framebuffer;
    }

    private unsafe void CreatePipeline()
    {
        ShaderModuleHandle vertexModule = CreateShaderModule(Shaders.VertexSpirv);
        ShaderModuleHandle fragmentModule = CreateShaderModule(Shaders.FragmentSpirv);
        try
        {
            fixed (byte* mainName = Shaders.MainEntryPointName)
            {
                DescriptorSetLayoutHandle setLayout = _descriptorSetLayout;
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &setLayout
                };
                PipelineLayoutHandle pipelineLayout;
                VkApi.Check(Vk.CreatePipelineLayout(Device.Device, &layoutInfo, null, &pipelineLayout), nameof(Vk.CreatePipelineLayout));
                _pipelineLayout = pipelineLayout;

                _pipeline = CreateGraphicsPipeline(vertexModule, fragmentModule, _pipelineLayout, mainName);
            }
        }
        finally
        {
            Vk.DestroyShaderModule(Device.Device, vertexModule, null);
            Vk.DestroyShaderModule(Device.Device, fragmentModule, null);
        }
    }

    /// <summary>
    /// Creates the gradient pipeline: the same vertex format and blending as the main
    /// pipeline, but with a push-constant range (isRadial + spread) and the gradient
    /// fragment shader. The push constant is set per draw in <see cref="RenderQueue"/>.
    /// </summary>
    private unsafe void CreateGradientPipeline()
    {
        ShaderModuleHandle vertexModule = CreateShaderModule(Shaders.VertexSpirv);
        ShaderModuleHandle fragmentModule = CreateShaderModule(Shaders.GradientFragmentSpirv);
        try
        {
            fixed (byte* mainName = Shaders.MainEntryPointName)
            {
                var pushConstantRange = new PushConstantRange
                {
                    StageFlags = ShaderStageFlags.FragmentBit,
                    Offset = 0,
                    Size = 8 // two ints: isRadial, spread
                };
                DescriptorSetLayoutHandle setLayout = _descriptorSetLayout;
                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    SetLayoutCount = 1,
                    PSetLayouts = &setLayout,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstantRange
                };
                PipelineLayoutHandle pipelineLayout;
                VkApi.Check(Vk.CreatePipelineLayout(Device.Device, &layoutInfo, null, &pipelineLayout), nameof(Vk.CreatePipelineLayout));
                _gradientPipelineLayout = pipelineLayout;

                _gradientPipeline = CreateGraphicsPipeline(vertexModule, fragmentModule, _gradientPipelineLayout, mainName);
            }
        }
        finally
        {
            Vk.DestroyShaderModule(Device.Device, vertexModule, null);
            Vk.DestroyShaderModule(Device.Device, fragmentModule, null);
        }
    }

    private unsafe PipelineHandle CreateGraphicsPipeline(
        ShaderModuleHandle vertexModule,
        ShaderModuleHandle fragmentModule,
        PipelineLayoutHandle pipelineLayout,
        byte* mainName)
    {
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertexModule,
            PName = (sbyte*)mainName
        };
        stages[1] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragmentModule,
            PName = (sbyte*)mainName
        };

        VertexInputBindingDescription* bindings = stackalloc VertexInputBindingDescription[1];
        bindings[0] = new VertexInputBindingDescription
        {
            Binding = 0,
            Stride = (uint)sizeof(RasterVertex),
            InputRate = VertexInputRate.Vertex
        };
        VertexInputAttributeDescription* attributes = stackalloc VertexInputAttributeDescription[3];
        attributes[0] = new VertexInputAttributeDescription { Location = 0, Binding = 0, Format = Format.R32G32Sfloat, Offset = 0 };
        attributes[1] = new VertexInputAttributeDescription { Location = 1, Binding = 0, Format = Format.R32G32Sfloat, Offset = 8 };
        attributes[2] = new VertexInputAttributeDescription { Location = 2, Binding = 0, Format = Format.R32G32B32A32Sfloat, Offset = 16 };

        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = 1,
            PVertexBindingDescriptions = bindings,
            VertexAttributeDescriptionCount = 3,
            PVertexAttributeDescriptions = attributes
        };
        var inputAssembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = PrimitiveTopology.TriangleList,
            PrimitiveRestartEnable = false
        };
        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };
        var rasterization = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill,
            CullMode = CullModeFlags.None,
            FrontFace = FrontFace.CounterClockwise,
            LineWidth = 1.0f
        };
        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = SampleCount
        };
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = true,
            SrcColorBlendFactor = BlendFactor.One,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
        };
        var colorBlend = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo,
            AttachmentCount = 1,
            PAttachments = &blendAttachment
        };
        DynamicState* dynamicStates = stackalloc DynamicState[2];
        dynamicStates[0] = DynamicState.Viewport;
        dynamicStates[1] = DynamicState.Scissor;
        var dynamicState = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo,
            DynamicStateCount = 2,
            PDynamicStates = dynamicStates
        };

        var createInfo = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &inputAssembly,
            PViewportState = &viewportState,
            PRasterizationState = &rasterization,
            PMultisampleState = &multisample,
            PColorBlendState = &colorBlend,
            PDynamicState = &dynamicState,
            Layout = pipelineLayout,
            RenderPass = _renderPass,
            Subpass = 0
        };
        if (UseDynamicRendering)
        {
            Format colorFormat = ColorFormat;
            var rendering = new PipelineRenderingCreateInfo
            {
                SType = StructureType.PipelineRenderingCreateInfo,
                ColorAttachmentCount = 1,
                PColorAttachmentFormats = &colorFormat
            };
            createInfo.PNext = &rendering;
            createInfo.RenderPass = default;
        }

        PipelineHandle pipeline;
        VkApi.Check(Vk.CreateGraphicsPipelines(Device.Device, default, 1, &createInfo, null, &pipeline), nameof(Vk.CreateGraphicsPipelines));
        return pipeline;
    }

    /// <summary>
    /// Normalizes readback bytes to the documented R,G,B,A contract of
    /// <see cref="IVulkanPresenter.ReadbackRgba"/>. The offscreen target is
    /// <c>R8G8B8A8Unorm</c> and needs no conversion; the swapchain is
    /// typically <c>B8G8R8A8Unorm</c>, whose raw bytes are B,G,R,A.
    /// </summary>
    private static void NormalizeByteOrder(byte[] pixels, Format format)
    {
        if (format == Format.R8G8B8A8Unorm)
        {
            return;
        }

        if (format != Format.B8G8R8A8Unorm)
        {
            throw new NotSupportedException($"Readback byte order is not defined for swapchain format {format}.");
        }

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }

    private static int BytesPerPixel(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8Unorm => 1,
            PixelFormat.Bgra8Unorm => 4,
            PixelFormat.Rgba8Unorm => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
        };
    }

    private static Format ToVulkanFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.R8Unorm => Format.R8Unorm,
            PixelFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
            PixelFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
        };
    }

    private readonly record struct DrawCommand(uint FirstVertex, uint VertexCount, DescriptorSetHandle DescriptorSet, Rect2D Scissor, bool IsGradient, int GradientKind, int Spread);
}
