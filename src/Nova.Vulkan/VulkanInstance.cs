using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>
/// Owns a VkInstance and an optional validation messenger. All Silk.NET unsafe calls stay inside this type.
/// Not internally synchronized; a single thread must own the instance and everything derived from it.
/// </summary>
[PublicAPI]
public sealed class VulkanInstance : IDisposable
{
    private const string KhronosValidationLayerName = "VK_LAYER_KHRONOS_validation";
    private const string DebugUtilsExtensionName = "VK_EXT_debug_utils";

    internal IVk Api { get; private set; }

    internal Silk.NET.Vulkan.InstanceHandle SilkInstance { get; private set; }
    private DebugUtilsMessengerHandleEXT _messenger;
    private bool _disposed;

    public VulkanInstance(VulkanDeviceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Api = Vk.Create();
        CreateInstance();
    }

    public VulkanDeviceOptions Options { get; }

    public InstanceHandle Handle { get; private set; }

    public bool ValidationEnabled { get; private set; }

    internal bool IsCreated => SilkInstance != default;

    internal uint ApiVersion { get; private set; }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_messenger != default)
        {
            Api.DestroyDebugUtilsMessengerEXT(SilkInstance, _messenger, null);
            _messenger = default;
        }

        if (SilkInstance != default)
        {
            Api.DestroyInstance(SilkInstance, null);
            SilkInstance = default;
        }

        ((IDisposable)Api).Dispose();
    }

    private unsafe void CreateInstance()
    {
        List<string> enabledLayers = new(1);
        List<string> enabledExtensions = [.. Options.ExtraInstanceExtensions];
        if (Options.Validation == ValidationMode.Enabled)
        {
            uint layerCount = 0;
            VkApi.Check(Api.EnumerateInstanceLayerProperties(&layerCount, null), nameof(Api.EnumerateInstanceLayerProperties));
            LayerProperties* layers = stackalloc LayerProperties[(int)layerCount];
            VkApi.Check(Api.EnumerateInstanceLayerProperties(&layerCount, layers), nameof(Api.EnumerateInstanceLayerProperties));
            bool layerFound = false;
            for (int i = 0; i < layerCount; i++)
            {
                sbyte* name = (sbyte*)&layers[i].LayerName;
                if (VkApi.ReadString(name) != KhronosValidationLayerName)
                {
                    continue;
                }

                layerFound = true;
                break;
            }

            if (!layerFound)
            {
                throw new VulkanException(
                    $"{KhronosValidationLayerName} is not installed on this system; install the Vulkan validation layers or use ValidationMode.Disabled.");
            }

            enabledLayers.Add(KhronosValidationLayerName);
            enabledExtensions.Add(DebugUtilsExtensionName);
        }

        uint extensionCount = 0;
        VkApi.Check(Api.EnumerateInstanceExtensionProperties(null, &extensionCount, null), nameof(Api.EnumerateInstanceExtensionProperties));
        ExtensionProperties* extensions = stackalloc ExtensionProperties[(int)extensionCount];
        VkApi.Check(Api.EnumerateInstanceExtensionProperties(null, &extensionCount, extensions), nameof(Api.EnumerateInstanceExtensionProperties));
        foreach (string required in enabledExtensions)
        {
            bool found = false;
            for (int i = 0; i < extensionCount; i++)
            {
                sbyte* name = (sbyte*)&extensions[i].ExtensionName;
                if (VkApi.ReadString(name) != required)
                {
                    continue;
                }

                found = true;
                break;
            }

            if (!found)
            {
                throw new VulkanException($"Instance extension '{required}' is not available on this system.");
            }
        }

        nint applicationNamePtr = Marshal.StringToCoTaskMemUTF8(Options.ApplicationName);
        nint engineNamePtr = Marshal.StringToCoTaskMemUTF8(Options.EngineName);
        sbyte** layerPointers = stackalloc sbyte*[enabledLayers.Count];
        sbyte** extensionPointers = stackalloc sbyte*[enabledExtensions.Count];
        uint usedApiVersion = VkApi.VulkanApi13;
        try
        {
            for (int i = 0; i < enabledLayers.Count; i++)
            {
                layerPointers[i] = (sbyte*)Marshal.StringToCoTaskMemUTF8(enabledLayers[i]);
            }

            for (int i = 0; i < enabledExtensions.Count; i++)
            {
                extensionPointers[i] = (sbyte*)Marshal.StringToCoTaskMemUTF8(enabledExtensions[i]);
            }

            var applicationInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (sbyte*)applicationNamePtr,
                ApplicationVersion = 1,
                PEngineName = (sbyte*)engineNamePtr,
                EngineVersion = 1,
                ApiVersion = VkApi.VulkanApi13
            };
            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &applicationInfo,
                EnabledLayerCount = (uint)enabledLayers.Count,
                PpEnabledLayerNames = layerPointers,
                EnabledExtensionCount = (uint)enabledExtensions.Count,
                PpEnabledExtensionNames = extensionPointers
            };

            Silk.NET.Vulkan.InstanceHandle instance;
            Result result = Api.CreateInstance(&createInfo, null, &instance);
            if (result == Result.ErrorIncompatibleDriver)
            {
                usedApiVersion = VkApi.VulkanApi10;
                applicationInfo.ApiVersion = VkApi.VulkanApi10;
                result = Api.CreateInstance(&createInfo, null, &instance);
            }

            VkApi.Check(result, nameof(Api.CreateInstance));
            SilkInstance = instance;
            Handle = new InstanceHandle((nint)instance.Handle);
            ApiVersion = usedApiVersion;
        }
        finally
        {
            for (int i = 0; i < enabledLayers.Count; i++)
            {
                Marshal.FreeCoTaskMem((nint)layerPointers[i]);
            }

            for (int i = 0; i < enabledExtensions.Count; i++)
            {
                Marshal.FreeCoTaskMem((nint)extensionPointers[i]);
            }

            Marshal.FreeCoTaskMem(applicationNamePtr);
            Marshal.FreeCoTaskMem(engineNamePtr);
        }

        ValidationEnabled = Options.Validation == ValidationMode.Enabled;
        if (ValidationEnabled)
        {
            CreateDebugMessenger();
        }
    }

    private unsafe void CreateDebugMessenger()
    {
        var createInfo = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoEXT,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBit
                | DebugUtilsMessageSeverityFlagsEXT.InfoBit
                | DebugUtilsMessageSeverityFlagsEXT.WarningBit
                | DebugUtilsMessageSeverityFlagsEXT.ErrorBit,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBit
                | DebugUtilsMessageTypeFlagsEXT.ValidationBit
                | DebugUtilsMessageTypeFlagsEXT.PerformanceBit,
            PfnUserCallback = new DebugUtilsMessengerCallbackEXT(&DebugUtilsCallback)
        };

        DebugUtilsMessengerHandleEXT messenger;
        VkApi.Check(Api.CreateDebugUtilsMessengerEXT(SilkInstance, &createInfo, null, &messenger), nameof(Api.CreateDebugUtilsMessengerEXT));
        _messenger = messenger;
    }

    [UnmanagedCallersOnly]
    private static unsafe uint DebugUtilsCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        _ = type;
        _ = userData;
        string? message = data->PMessage is null ? null : VkApi.ReadString(data->PMessage);
        Console.Error.WriteLine($"[Vulkan:{severity}] {message}");
        return 0; // VK_FALSE: keep going
    }
}
