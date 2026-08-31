using JetBrains.Annotations;

namespace Nova.Sdl;

[PublicAPI]
public readonly struct WindowHandle(nint value) : IEquatable<WindowHandle>
{
    public nint Value { get; } = value;

    public bool IsValid => Value != 0;

    public static WindowHandle Invalid { get; } = new(0);

    public bool Equals(WindowHandle other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is WindowHandle other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(WindowHandle left, WindowHandle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(WindowHandle left, WindowHandle right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsValid ? $"WindowHandle({Value:X})" : "WindowHandle.Invalid";
    }
}
