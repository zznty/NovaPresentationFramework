namespace Nova.DesktopTheme;

/// <summary>
/// 24-bit RGB color parsed from a desktop-theme source. Immutable; equality is component-wise.
/// Win32 <c>COLORREF</c> conversion uses the <c>0x00BBGGRR</c> layout.
/// </summary>
public readonly struct RgbColor(byte r, byte g, byte b) : IEquatable<RgbColor>
{
    public byte R { get; } = r;

    public byte G { get; } = g;

    public byte B { get; } = b;

    public static RgbColor White { get; } = new(255, 255, 255);

    public static RgbColor Black { get; } = new(0, 0, 0);

    /// <summary>
    /// Parses a KDE CSV triplet (<c>10,160,230</c>). Returns <c>false</c> on malformed input
    /// (wrong field count, non-digits, or out-of-range channels).
    /// </summary>
    public static bool TryParseCsv(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!byte.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte parsedR)
            || !byte.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte parsedG)
            || !byte.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out byte parsedB))
        {
            return false;
        }

        color = new RgbColor(parsedR, parsedG, parsedB);
        return true;
    }

    /// <summary>Parses a hex color (<c>#0AA0E6</c> or <c>0aa0e6</c>). Returns <c>false</c> on malformed input.</summary>
    public static bool TryParseHex(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text.Trim();
        if (hex[0] == '#')
        {
            hex = hex[1..];
        }

        if (hex.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte parsedR)
            || !byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte parsedG)
            || !byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte parsedB))
        {
            return false;
        }

        color = new RgbColor(parsedR, parsedG, parsedB);
        return true;
    }

    /// <summary>Reconstructs a color from a Win32 <c>COLORREF</c> (<c>0x00BBGGRR</c>).</summary>
    public static RgbColor FromColorRef(int colorRef)
    {
        return new RgbColor((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));
    }

    /// <summary>Win32 <c>COLORREF</c> layout: <c>0x00BBGGRR</c>.</summary>
    public int ToColorRef()
    {
        return (B << 16) | (G << 8) | R;
    }

    /// <summary>Linear blend toward <paramref name="other"/> by <paramref name="amount"/> in [0,1].</summary>
    public RgbColor Blend(RgbColor other, double amount)
    {
        byte Channel(byte from, byte to)
        {
            return (byte)Math.Round(from + ((to - from) * amount), MidpointRounding.AwayFromZero);
        }

        return new RgbColor(Channel(R, other.R), Channel(G, other.G), Channel(B, other.B));
    }

    /// <summary>Blends toward white by <paramref name="amount"/> in [0,1].</summary>
    public RgbColor Lighten(double amount)
    {
        return Blend(White, amount);
    }

    /// <summary>Blends toward black by <paramref name="amount"/> in [0,1].</summary>
    public RgbColor Darken(double amount)
    {
        return Blend(Black, amount);
    }

    public bool Equals(RgbColor other)
    {
        return R == other.R && G == other.G && B == other.B;
    }

    public override bool Equals(object? obj)
    {
        return obj is RgbColor other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(R, G, B);
    }

    public static bool operator ==(RgbColor left, RgbColor right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RgbColor left, RgbColor right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"#{R:X2}{G:X2}{B:X2}";
    }
}
