using JetBrains.Annotations;

namespace Nova.MilCmd;

/// <summary>Opaque host font-face token carried on <c>MILCMD_GLYPHRUN_CREATE</c> instead of an <c>IDWriteFont*</c>.</summary>
[PublicAPI]
public readonly struct FontFaceToken(ulong value) : IEquatable<FontFaceToken>
{
    public ulong Value { get; } = value;

    public bool IsNull => Value == 0;

    public static FontFaceToken Null { get; } = new(0);

    public bool Equals(FontFaceToken other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object? obj)
    {
        return obj is FontFaceToken other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public static bool operator ==(FontFaceToken left, FontFaceToken right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FontFaceToken left, FontFaceToken right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return IsNull ? "FontFaceToken.Null" : $"FontFaceToken({Value})";
    }
}
