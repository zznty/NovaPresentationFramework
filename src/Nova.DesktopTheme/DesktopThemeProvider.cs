using JetBrains.Annotations;
using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// <see cref="IHostMetrics"/> decorator that overrides colors, fonts, and DPI from a
/// <see cref="DesktopPalette"/> while delegating every other metric query to the inner
/// provider (the SDL display metrics). Queries for slots the palette does not back return
/// <c>null</c>, which <see cref="HostTheme"/> resolves to the Classic defaults — the
/// "must not throw, never returns a fabricated value" contract.
/// </summary>
[PublicAPI]
public sealed class DesktopThemeProvider(IHostMetrics inner, DesktopPalette palette) : IHostMetrics
{
    private DesktopPalette _palette = palette;

    /// <summary>The palette currently applied; re-read after a live restyle.</summary>
    public DesktopPalette Palette => Volatile.Read(ref _palette);

    /// <summary>Swaps the palette (live-restyle path); the inner metrics provider is retained.</summary>
    public void ReplacePalette(DesktopPalette replacement)
    {
        Volatile.Write(ref _palette, replacement);
    }

    public int PixelsPerInch => Palette.PixelsPerInch ?? inner.PixelsPerInch;

    public int DoubleClickTime => inner.DoubleClickTime;

    public void GetWorkArea(out int left, out int top, out int right, out int bottom)
    {
        inner.GetWorkArea(out left, out top, out right, out bottom);
    }

    public int GetSystemMetric(int index)
    {
        return inner.GetSystemMetric(index);
    }

    public int? GetSysColor(int index)
    {
        return Palette.TryGetColor(index, out int colorRef) ? colorRef : null;
    }

    public NonClientMetrics? NonClient => Palette.Font is SystemFontMetrics font
        ? DesktopPalette.ToNonClientMetrics(font)
        : null;
}
