using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Geometry;
using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>
/// Owns a logical device, a graphics/compute queue, and the presenters created from it.
/// All Silk.NET unsafe calls stay in this assembly. Not internally synchronized.
/// </summary>
[PublicAPI]
public sealed class VulkanDevice : IDisposable
{
    private const string SwapchainExtensionName = "VK_KHR_swapchain";
    private const string SurfaceExtensionName = "VK_KHR_surface";

    private readonly bool _ownsInstance;
    private readonly List<IVulkanPresenter> _presenters = [];
    private readonly bool _deviceCreated;
    private bool _disposed;

    internal IVk Api { get; private set; } = null!;

    internal DeviceHandle Device { get; private set; }

    internal QueueHandle Queue { get; private set; }

    internal uint QueueFamilyIndex { get; private set; }

    internal PhysicalDeviceHandle PhysicalDevice { get; private set; }

    internal bool SwapchainEnabled { get; private set; }

    internal bool SupportsDynamicRendering { get; private set; }

    public VulkanDevice(VulkanDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Instance = new VulkanInstance(options);
        _ownsInstance = true;
        try
        {
            CreateDevice();
            _deviceCreated = true;
        }
        finally
        {
            if (!_deviceCreated)
            {
                Instance.Dispose();
            }
        }
    }

    public VulkanDevice(VulkanInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!instance.IsCreated)
        {
            throw new ArgumentException("The Vulkan instance is not created.", nameof(instance));
        }

        Instance = instance;
        _ownsInstance = false;
        CreateDevice();
    }

    public VulkanInstance Instance { get; }

    public string DeviceName { get; private set; } = string.Empty;

    public bool IsCpuDevice { get; private set; }

    internal VulkanDeviceOptions Options => Instance.Options;

    /// <summary>Creates a swapchain presenter for the given windowing surface.</summary>
    public IVulkanPresenter CreatePresenter(ISurfaceSource surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SwapchainEnabled)
        {
            throw new VulkanException(
                "The device was created without VK_KHR_swapchain. Include the windowing instance extensions (at least VK_KHR_surface) in VulkanDeviceOptions.ExtraInstanceExtensions before creating the device.");
        }

        var presenter = new SurfacePresenter(this, surface);
        _presenters.Add(presenter);
        return presenter;
    }

    /// <summary>Creates an offscreen render target; required for headless rendering and readback.</summary>
    public IVulkanPresenter CreateOffscreenPresenter(PixelSize size)
    {
        if (size.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var presenter = new OffscreenPresenter(this, size);
        _presenters.Add(presenter);
        return presenter;
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (int i = _presenters.Count - 1; i >= 0; i--)
        {
            _presenters[i].Dispose();
        }

        _presenters.Clear();
        if (Device != default)
        {
            Api.DestroyDevice(Device, null);
            Device = default;
        }

        if (_ownsInstance)
        {
            Instance.Dispose();
        }
    }

    private unsafe void CreateDevice()
    {
        Api = Instance.Api;

        uint physicalDeviceCount = 0;
        Ref<uint> physicalDeviceCountRef = new(ref physicalDeviceCount);
        VkApi.Check(Api.EnumeratePhysicalDevices(Instance.SilkInstance, physicalDeviceCountRef, default), nameof(Api.EnumeratePhysicalDevices));
        int deviceCount = (int)physicalDeviceCount;
        if (deviceCount == 0)
        {
            throw new VulkanException("No Vulkan physical devices are available.");
        }

        PhysicalDeviceHandle* physicalDevices = stackalloc PhysicalDeviceHandle[deviceCount];
        VkApi.Check(Api.EnumeratePhysicalDevices(Instance.SilkInstance, &physicalDeviceCount, physicalDevices), nameof(Api.EnumeratePhysicalDevices));
        PhysicalDevice = PickPhysicalDevice(physicalDevices, (uint)deviceCount, out string deviceName, out bool isCpu, out uint apiVersion);
        DeviceName = deviceName;
        IsCpuDevice = isCpu;
        SupportsDynamicRendering = Instance.ApiVersion >= VkApi.VulkanApi13 && apiVersion >= VkApi.VulkanApi13;

        QueueFamilyIndex = PickQueueFamily();
        string[] deviceExtensions = BuildDeviceExtensions();

        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = QueueFamilyIndex,
            QueueCount = 1
        };
        float priority = 1.0f;
        queueCreateInfo.PQueuePriorities = &priority;

        sbyte** extensionPointers = stackalloc sbyte*[deviceExtensions.Length];
        try
        {
            for (int i = 0; i < deviceExtensions.Length; i++)
            {
                extensionPointers[i] = (sbyte*)Marshal.StringToCoTaskMemUTF8(deviceExtensions[i]);
            }

            var createInfo = new DeviceCreateInfo
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = 1,
                PQueueCreateInfos = &queueCreateInfo,
                EnabledExtensionCount = (uint)deviceExtensions.Length,
                PpEnabledExtensionNames = extensionPointers,
                PEnabledFeatures = null
            };
            if (SupportsDynamicRendering)
            {
                var dynamicRenderingFeatures = new PhysicalDeviceDynamicRenderingFeatures
                {
                    SType = StructureType.PhysicalDeviceDynamicRenderingFeatures,
                    DynamicRendering = true
                };
                createInfo.PNext = &dynamicRenderingFeatures;
            }

            DeviceHandle device;
            VkApi.Check(Api.CreateDevice(PhysicalDevice, &createInfo, null, &device), nameof(Api.CreateDevice));
            Device = device;

            QueueHandle queue;
            Api.GetDeviceQueue(Device, QueueFamilyIndex, 0, &queue);
            Queue = queue;
        }
        finally
        {
            for (int i = 0; i < deviceExtensions.Length; i++)
            {
                Marshal.FreeCoTaskMem((nint)extensionPointers[i]);
            }
        }
    }

    private unsafe PhysicalDeviceHandle PickPhysicalDevice(PhysicalDeviceHandle* devices, uint count, out string name, out bool isCpu, out uint apiVersion)
    {
        PhysicalDeviceHandle best = default;
        name = string.Empty;
        isCpu = false;
        apiVersion = 0;
        int bestScore = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            PhysicalDeviceProperties properties;
            Api.GetPhysicalDeviceProperties(devices[i], &properties);
            sbyte* deviceNamePtr = (sbyte*)&properties.DeviceName;
            string deviceName = VkApi.ReadString(deviceNamePtr);

            int score = ScoreDevice(properties.DeviceType, deviceName, Instance.Options);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            best = devices[i];
            name = deviceName;
            isCpu = properties.DeviceType == PhysicalDeviceType.Cpu;
            apiVersion = properties.ApiVersion;
        }

        return best;
    }

    private static int ScoreDevice(PhysicalDeviceType type, string name, VulkanDeviceOptions options)
    {
        if (!string.IsNullOrEmpty(options.PreferredDeviceName)
            && !name.Contains(options.PreferredDeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return int.MinValue;
        }

        bool preferIntegrated = options.PreferIntegratedGpu;
        int score = type switch
        {
            PhysicalDeviceType.DiscreteGpu => preferIntegrated ? 100 : 400,
            PhysicalDeviceType.IntegratedGpu => preferIntegrated ? 400 : 200,
            PhysicalDeviceType.VirtualGpu => 100,
            PhysicalDeviceType.Cpu => 50,
            PhysicalDeviceType.Other => 0,
            _ => 0
        };
        return score;
    }

    private unsafe uint PickQueueFamily()
    {
        uint count = 0;
        Api.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &count, null);
        QueueFamilyProperties* families = stackalloc QueueFamilyProperties[(int)count];
        Api.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &count, families);

        uint graphicsAndCompute = uint.MaxValue;
        uint graphicsOnly = uint.MaxValue;
        for (uint i = 0; i < count; i++)
        {
            QueueFlags flags = families[i].QueueFlags;
            bool hasGraphics = (flags & QueueFlags.GraphicsBit) != 0;
            bool hasCompute = (flags & QueueFlags.ComputeBit) != 0;
            if (hasGraphics && hasCompute)
            {
                graphicsAndCompute = i;
                break;
            }

            if (hasGraphics && graphicsOnly == uint.MaxValue)
            {
                graphicsOnly = i;
            }
        }

        uint family = graphicsAndCompute != uint.MaxValue ? graphicsAndCompute : graphicsOnly;
        return family != uint.MaxValue
            ? family
            : throw new VulkanException("No physical device queue family supports graphics operations.");
    }

    private unsafe string[] BuildDeviceExtensions()
    {
        bool wantsSurface = Instance.Options.ExtraInstanceExtensions.Contains(SurfaceExtensionName, StringComparer.Ordinal);
        if (!wantsSurface)
        {
            SwapchainEnabled = false;
            return [];
        }

        uint count = 0;
        VkApi.Check(Api.EnumerateDeviceExtensionProperties(PhysicalDevice, null, &count, null), nameof(Api.EnumerateDeviceExtensionProperties));
        ExtensionProperties* extensions = stackalloc ExtensionProperties[(int)count];
        VkApi.Check(Api.EnumerateDeviceExtensionProperties(PhysicalDevice, null, &count, extensions), nameof(Api.EnumerateDeviceExtensionProperties));
        bool supported = false;
        for (int i = 0; i < count; i++)
        {
            sbyte* name = (sbyte*)&extensions[i].ExtensionName;
            if (VkApi.ReadString(name) != SwapchainExtensionName)
            {
                continue;
            }

            supported = true;
            break;
        }

        if (!supported)
        {
            throw new VulkanException($"The selected physical device does not support {SwapchainExtensionName}.");
        }

        SwapchainEnabled = true;
        return [SwapchainExtensionName];
    }
}
