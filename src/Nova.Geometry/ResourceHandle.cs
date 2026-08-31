using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>
/// DUCE resource handle. WPF stores this as a 32-bit value on the wire
/// (<c>DUCE.ResourceHandle</c>). Zero is the null handle.
/// </summary>
[PublicAPI]
public readonly struct ResourceHandle(uint value) : IEquatable<ResourceHandle>
{
    public uint Value { get; } = value;

    public bool IsNull => Value == 0;

    public static ResourceHandle Null { get; } = new(0);

    public bool Equals(ResourceHandle other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is ResourceHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(ResourceHandle left, ResourceHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourceHandle left, ResourceHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsNull ? "ResourceHandle.Null" : $"ResourceHandle({Value})";
    }
}
