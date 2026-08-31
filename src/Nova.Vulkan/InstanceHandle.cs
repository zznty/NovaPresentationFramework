using JetBrains.Annotations;

namespace Nova.Vulkan;

/// <summary>VkInstance owned by <see cref="VulkanInstance"/>. Zero is invalid.</summary>
[PublicAPI]
public readonly struct InstanceHandle(nint value) : IEquatable<InstanceHandle>
{
    public nint Value { get; } = value;

    public bool IsValid => Value != 0;

    public static InstanceHandle Invalid { get; } = new(0);

    public bool Equals(InstanceHandle other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is InstanceHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(InstanceHandle left, InstanceHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(InstanceHandle left, InstanceHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsValid ? $"InstanceHandle({Value:X})" : "InstanceHandle.Invalid";
    }
}
