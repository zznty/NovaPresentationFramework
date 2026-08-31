using JetBrains.Annotations;

namespace Nova.Vulkan;

/// <summary>Owned GPU texture. Zero is invalid.</summary>
[PublicAPI]
public readonly struct TextureHandle(uint value) : IEquatable<TextureHandle>
{
    public uint Value { get; } = value;

    public bool IsValid => Value != 0;

    public static TextureHandle Invalid { get; } = new(0);

    public bool Equals(TextureHandle other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextureHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(TextureHandle left, TextureHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextureHandle left, TextureHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsValid ? $"TextureHandle({Value})" : "TextureHandle.Invalid";
    }
}
