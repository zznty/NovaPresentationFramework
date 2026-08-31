using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Vulkan;

/// <summary>Device vertex. Coordinates are already converted to clip space floats.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public readonly struct RasterVertex(float x, float y, float u, float v, float r, float g, float b, float a) : IEquatable<RasterVertex>
{
    public float X { get; } = x;
    public float Y { get; } = y;
    public float U { get; } = u;
    public float V { get; } = v;
    public float R { get; } = r;
    public float G { get; } = g;
    public float B { get; } = b;
    public float A { get; } = a;

    public bool Equals(RasterVertex other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y) && U.Equals(other.U) && V.Equals(other.V)
            && R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
    }

    public override bool Equals(object? obj)
    {
        return obj is RasterVertex other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, U, V, R, G, B, A);
    }

    public static bool operator ==(RasterVertex left, RasterVertex right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RasterVertex left, RasterVertex right)
    {
        return !left.Equals(right);
    }
}
