using JetBrains.Annotations;

namespace Nova.Vulkan;

/// <summary>VkSurfaceKHR owned by the device that created it. Zero is invalid.</summary>
[PublicAPI]
public readonly struct SurfaceHandle(ulong value) : IEquatable<SurfaceHandle>
{
    public ulong Value { get; } = value;

    public bool IsValid => Value != 0;

    public static SurfaceHandle Invalid { get; } = new(0);

    public bool Equals(SurfaceHandle other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is SurfaceHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(SurfaceHandle left, SurfaceHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SurfaceHandle left, SurfaceHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsValid ? $"SurfaceHandle({Value:X})" : "SurfaceHandle.Invalid";
    }
}
