using JetBrains.Annotations;

namespace Nova.Vulkan;

[PublicAPI]
public sealed class VulkanException : Exception
{
    public VulkanException()
    {
    }

    public VulkanException(string message)
        : base(message)
    {
    }

    public VulkanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public VulkanException(string operation, int result)
        : base($"{operation} failed with VkResult {result}.")
    {
        Result = result;
    }

    public int Result { get; }
}
