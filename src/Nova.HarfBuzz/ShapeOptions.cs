using JetBrains.Annotations;

namespace Nova.HarfBuzz;

[PublicAPI]
public readonly struct ShapeOptions(string language = "en", string script = "Latn", bool rightToLeft = false)
    : IEquatable<ShapeOptions>
{
    public string Language { get; } = language;
    public string Script { get; } = script;
    public bool RightToLeft { get; } = rightToLeft;

    public static ShapeOptions Default { get; } = new("en", "Latn", false);

    public bool Equals(ShapeOptions other)
    {
        return Language == other.Language && Script == other.Script && RightToLeft == other.RightToLeft;
    }

    public override bool Equals(object? obj)
    {
        return obj is ShapeOptions other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Language, Script, RightToLeft);
    }

    public static bool operator ==(ShapeOptions left, ShapeOptions right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ShapeOptions left, ShapeOptions right)
    {
        return !left.Equals(right);
    }
}
