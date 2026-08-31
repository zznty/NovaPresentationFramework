using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Nova.Vulkan;

/// <summary>Low-level Silk.NET.Vulkan helpers. All unsafe interop stays in this assembly.</summary>
internal static class VkApi
{
    internal const uint VulkanApi10 = 0x00400000; // VK_MAKE_API_VERSION(0, 1, 0, 0)
    internal const uint VulkanApi13 = 0x00403000; // VK_MAKE_API_VERSION(0, 1, 3, 0)

    /// <summary>Throws <see cref="VulkanException"/> when the result is not success.</summary>
    internal static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new VulkanException(operation, (int)result);
        }
    }

    internal static unsafe string ReadString(sbyte* value)
    {
        return Marshal.PtrToStringUTF8((nint)value) ?? string.Empty;
    }
}
