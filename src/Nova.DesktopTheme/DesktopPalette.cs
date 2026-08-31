using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// Normalized desktop palette after merging the fallback chain. Only slots some source (or
/// bevel synthesis) actually defined are present; the provider falls back to the Classic
/// defaults for everything else, so the palette never fabricates values.
/// </summary>
public sealed class DesktopPalette(
    IReadOnlyDictionary<int, int> colors,
    SystemFontMetrics? font,
    int? pixelsPerInch,
    int? accentColorRef,
    bool? isDark)
{
    private readonly Dictionary<int, int> _colors = new(colors);

    /// <summary>Colors keyed by <see cref="SystemColorIndex"/>, values in Win32 COLORREF format.</summary>
    public IReadOnlyDictionary<int, int> Colors => _colors;

    /// <summary>Desktop font, or <c>null</c> when no source defined one.</summary>
    public SystemFontMetrics? Font { get; } = font;

    public int? PixelsPerInch { get; } = pixelsPerInch;

    /// <summary>Accent color in Win32 COLORREF format, or <c>null</c> when unknown.</summary>
    public int? AccentColorRef { get; } = accentColorRef;

    public bool? IsDark { get; } = isDark;

    /// <summary>True when no source produced any color, font, or DPI — the "not a theme" case.</summary>
    public bool IsEmpty => _colors.Count == 0 && Font is null && PixelsPerInch is null;

    /// <summary>Gets a defined slot's COLORREF. Returns <c>false</c> when the slot is not backed by a source.</summary>
    public bool TryGetColor(int systemColorIndex, out int colorRef)
    {
        return _colors.TryGetValue(systemColorIndex, out colorRef);
    }

    /// <summary>NONCLIENTMETRICS with the desktop font and the Classic chrome metrics.</summary>
    public static NonClientMetrics ToNonClientMetrics(SystemFontMetrics font)
    {
        return new NonClientMetrics(
            borderWidth: 1,
            scrollWidth: 17,
            scrollHeight: 17,
            captionWidth: 36,
            captionHeight: 23,
            smallCaptionWidth: 22,
            smallCaptionHeight: 22,
            menuWidth: 17,
            menuHeight: 20,
            captionFont: font,
            smallCaptionFont: font,
            menuFont: font,
            statusFont: font,
            messageFont: font);
    }
}
