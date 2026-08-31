using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>Integer pixel extent. Used only for swapchain / texture / atlas sizes, never for MIL geometry.</summary>
[PublicAPI]
public readonly struct PixelSize : IEquatable<PixelSize>
{
    public PixelSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public bool IsEmpty => Width == 0 || Height == 0;

    public bool Equals(PixelSize other)
    {
        return Width == other.Width && Height == other.Height;
    }

    public override bool Equals(object? obj)
    {
        return obj is PixelSize other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height);
    }

    public static bool operator ==(PixelSize left, PixelSize right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(PixelSize left, PixelSize right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"{Width}x{Height}px";
    }
}
