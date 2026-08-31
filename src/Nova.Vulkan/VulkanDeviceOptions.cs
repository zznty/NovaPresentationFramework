using JetBrains.Annotations;

namespace Nova.Vulkan;

[PublicAPI]
public sealed class VulkanDeviceOptions
{
    public ValidationMode Validation { get; init; } = ValidationMode.Enabled;

    public string ApplicationName { get; init; } = "NovaPresentationFramework";

    public string EngineName { get; init; } = "Nova.Vulkan";

    /// <summary>Instance extensions required by the windowing layer (e.g. VK_KHR_surface + platform WSI).</summary>
    public IReadOnlyList<string> ExtraInstanceExtensions { get; init; } = [];

    /// <summary>Preferred physical-device name substring. Empty selects the first suitable GPU, then CPU.</summary>
    public string PreferredDeviceName { get; init; } = string.Empty;

    public bool PreferIntegratedGpu { get; init; }

    public PresentMode PresentMode { get; init; } = PresentMode.Fifo;
}
