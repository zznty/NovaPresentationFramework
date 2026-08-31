using Nova.Vulkan;

namespace Nova.TestSupport;

/// <summary>
/// Vulkan device options for test suites that create and destroy many devices per run.
/// Validation is disabled by default because the Khronos validation layer's
/// <c>GetDispatchDevice</c> hits a libstdc++ <c>__glibcxx_assert_fail</c> and aborts the
/// process under rapid device create/destroy churn (the abort is inside the layer's own
/// dispatch cache — <c>GetDispatchDeviceEP10VkDevice_T.cold</c> — not in our vk usage;
/// reproduced with both the Intel Xe and lavapipe ICDs, with xunit parallelism on and off,
/// and absent entirely when the layer is unloadable). Set <c>NOVA_TEST_VULKAN_VALIDATION=1</c>
/// to re-enable validation for a deliberate run that checks OUR vk usage instead of raster
/// pixels. Nova.Vulkan.Tests keeps validation enabled permanently (its job is Vulkan
/// correctness). The WINDOW path (every Window.Show) was hardcoded to validation-enabled
/// until SdlPresentationSource gated it behind the product switch NOVA_VULKAN_VALIDATION=1
/// (default off); this test switch remains distinct and test-scoped.
/// </summary>
internal static class NovaTestVulkan
{
    public static VulkanDeviceOptions DeviceOptions(IReadOnlyList<string>? extraInstanceExtensions = null)
    {
        return new VulkanDeviceOptions
        {
            Validation = Environment.GetEnvironmentVariable("NOVA_TEST_VULKAN_VALIDATION") == "1"
                ? ValidationMode.Enabled
                : ValidationMode.Disabled,
            ExtraInstanceExtensions = extraInstanceExtensions ?? []
        };
    }
}
