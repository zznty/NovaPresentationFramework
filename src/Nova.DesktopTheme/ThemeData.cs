using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// Partial palette contribution from a single <see cref="IThemeSource"/>. Slots a source does
/// not define are simply absent; the aggregator merges per-slot (first source wins) and the
/// provider falls back to the Classic defaults for anything still missing.
/// </summary>
public sealed class ThemeData
{
    private readonly Dictionary<int, RgbColor> _colors = [];

    /// <summary>Colors keyed by <see cref="SystemColorIndex"/>.</summary>
    public IReadOnlyDictionary<int, RgbColor> Colors => _colors;

    public string? FontFamily { get; set; }

    public int? FontPointSize { get; set; }

    public int? FontWeight { get; set; }

    public int? PixelsPerInch { get; set; }

    /// <summary>Accent color in Win32 <c>COLORREF</c> format (<c>0x00BBGGRR</c>).</summary>
    public int? AccentColorRef { get; set; }

    public bool? IsDark { get; set; }

    public void SetColor(int systemColorIndex, RgbColor color)
    {
        _colors[systemColorIndex] = color;
    }
}
