using System.Globalization;
using JetBrains.Annotations;

namespace Nova.DesktopTheme;

/// <summary>
/// A GTK CSS color: sRGB components with straight alpha in [0, 1]. Parses the
/// GTK CSS color grammar (hex with/without alpha, rgb()/rgba(), the common
/// named colors) and the GTK color functions alpha()/shade()/mix()/
/// lighter()/darker(). <c>@define-color</c> references are resolved by the
/// caller before parsing.
/// </summary>
[PublicAPI]
public readonly struct GtkColor(byte r, byte g, byte b, double a = 1.0) : IEquatable<GtkColor>
{
    private static readonly Dictionary<string, GtkColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = FromArgb(0, 0, 0, 0),
        ["black"] = FromArgb(255, 0, 0, 0),
        ["white"] = FromArgb(255, 255, 255, 255),
        ["red"] = FromArgb(255, 255, 0, 0),
        ["green"] = FromArgb(255, 0, 128, 0),
        ["blue"] = FromArgb(255, 0, 0, 255),
        ["yellow"] = FromArgb(255, 255, 255, 0),
        ["gray"] = FromArgb(255, 128, 128, 128),
        ["grey"] = FromArgb(255, 128, 128, 128),
    };

    public byte R { get; } = r;

    public byte G { get; } = g;

    public byte B { get; } = b;

    /// <summary>Alpha in [0, 1].</summary>
    public double A { get; } = a;

    public static GtkColor FromArgb(byte a, byte r, byte g, byte b)
    {
        return new GtkColor(r, g, b, a / 255.0);
    }

    /// <summary>
    /// Parses a GTK CSS color value. Returns null when the value is not a color
    /// (lengths, gradients and other property values pass through the caller).
    /// </summary>
    public static GtkColor? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string v = value.Trim();
        return v.Length == 0
            ? null
            : Named.TryGetValue(v, out GtkColor named)
                ? named
                : v[0] == '#'
                    ? ParseHex(v)
                    : v.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
                        ? ParseRgb(v[4..^1], 1.0)
                        : v.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase)
                            ? ParseRgb(v[5..^1], double.NaN)
                            : null;
    }

    private static GtkColor? ParseHex(string v)
    {
        string digits = v[1..];
        return digits.Length switch
        {
            3 => new GtkColor(
                (byte)(ParseHexByte(digits[0]) * 17),
                (byte)(ParseHexByte(digits[1]) * 17),
                (byte)(ParseHexByte(digits[2]) * 17)),
            4 => FromArgb(
                (byte)(ParseHexByte(digits[3]) * 17),
                (byte)(ParseHexByte(digits[0]) * 17),
                (byte)(ParseHexByte(digits[1]) * 17),
                (byte)(ParseHexByte(digits[2]) * 17)),
            6 => new GtkColor(
                (byte)((ParseHexByte(digits[0]) << 4) | ParseHexByte(digits[1])),
                (byte)((ParseHexByte(digits[2]) << 4) | ParseHexByte(digits[3])),
                (byte)((ParseHexByte(digits[4]) << 4) | ParseHexByte(digits[5]))),
            8 => FromArgb(
                (byte)((ParseHexByte(digits[6]) << 4) | ParseHexByte(digits[7])),
                (byte)((ParseHexByte(digits[0]) << 4) | ParseHexByte(digits[1])),
                (byte)((ParseHexByte(digits[2]) << 4) | ParseHexByte(digits[3])),
                (byte)((ParseHexByte(digits[4]) << 4) | ParseHexByte(digits[5]))),
            _ => null
        };
    }

    private static GtkColor? ParseRgb(string args, double explicitAlpha)
    {
        string[] parts = args.Split(',');
        if (parts.Length < 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ||
            !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) ||
            !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
        {
            return null;
        }

        double alpha = explicitAlpha;
        if (double.IsNaN(alpha) && parts.Length > 3)
        {
            _ = double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out alpha);
        }

        alpha = double.IsNaN(alpha) ? 1.0 : Math.Clamp(alpha, 0.0, 1.0);
        return new GtkColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), alpha);
    }

    private static int ParseHexByte(char c)
    {
        return c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => 0
        };
    }

    /// <summary>GTK shade(): mixes toward white when factor &gt; 1, toward black when &lt; 1.</summary>
    public static GtkColor Shade(GtkColor color, double factor)
    {
        return factor >= 1.0
            ? Mix(color, Named["white"], factor - 1.0)
            : Mix(color, Named["black"], 1.0 - factor);
    }

    /// <summary>Linear mix of two colors by <paramref name="factor"/> (0 = first, 1 = second).</summary>
    public static GtkColor Mix(GtkColor first, GtkColor second, double factor)
    {
        double f = Math.Clamp(factor, 0.0, 1.0);
        return new GtkColor(
            (byte)Math.Round((first.R * (1.0 - f)) + (second.R * f), MidpointRounding.AwayFromZero),
            (byte)Math.Round((first.G * (1.0 - f)) + (second.G * f), MidpointRounding.AwayFromZero),
            (byte)Math.Round((first.B * (1.0 - f)) + (second.B * f), MidpointRounding.AwayFromZero),
            (first.A * (1.0 - f)) + (second.A * f));
    }

    /// <summary>Applies an alpha factor to a color (GTK alpha()).</summary>
    public static GtkColor Alpha(GtkColor color, double alpha)
    {
        return new GtkColor(color.R, color.G, color.B, color.A * Math.Clamp(alpha, 0.0, 1.0));
    }

    public readonly bool Equals(GtkColor other)
    {
        return R == other.R && G == other.G && B == other.B && A.Equals(other.A);
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is GtkColor other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(R, G, B, A);
    }

    public static bool operator ==(GtkColor left, GtkColor right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GtkColor left, GtkColor right)
    {
        return !left.Equals(right);
    }

    public override readonly string ToString()
    {
        return A >= 1.0
            ? $"#{R:x2}{G:x2}{B:x2}"
            : $"rgba({R}, {G}, {B}, {A.ToString("0.###", CultureInfo.InvariantCulture)})";
    }
}
