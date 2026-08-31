using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// KDE canonical palette: <c>~/.config/kdeglobals</c> <c>[Colors:*]</c> and <c>[WM]</c> groups,
/// plus <c>[ColorEffects:Disabled]</c> for the disabled (gray) text color. This is the single
/// most complete plain-file palette source on a KDE box; the spec measured the values below
/// live on this machine and they agree byte-for-byte with Trolltech.conf and the portal.
/// </summary>
public sealed class KdeGlobalsSource(string path) : IThemeSource
{
    public string Name => "kdeglobals";

    public ThemeData Load()
    {
        var data = new ThemeData();
        string? text = ThemeFile.TryReadText(path);
        if (text is null)
        {
            return data;
        }

        IniFile ini = IniFile.Parse(text);
        SetCsv(ini, data, "Colors:Window", "BackgroundNormal", SystemColorIndex.Window);
        SetCsv(ini, data, "Colors:Window", "ForegroundNormal", SystemColorIndex.WindowText);
        SetCsv(ini, data, "Colors:Window", "ForegroundInactive", SystemColorIndex.GrayText);
        SetCsv(ini, data, "Colors:Window", "DecorationHover", SystemColorIndex.InactiveBorder);
        SetCsv(ini, data, "Colors:Button", "BackgroundNormal", SystemColorIndex.ButtonFace);
        SetCsv(ini, data, "Colors:Button", "ForegroundNormal", SystemColorIndex.ButtonText);
        SetCsv(ini, data, "Colors:Button", "DecorationHover", SystemColorIndex.HotLight);
        SetCsv(ini, data, "Colors:Button", "DecorationHover", SystemColorIndex.ActiveBorder);
        SetCsv(ini, data, "Colors:Button", "BackgroundNormal", SystemColorIndex.ScrollBar);
        SetCsv(ini, data, "Colors:Button", "BackgroundNormal", SystemColorIndex.MenuBar);
        SetCsv(ini, data, "Colors:Selection", "BackgroundNormal", SystemColorIndex.Highlight);
        SetCsv(ini, data, "Colors:Selection", "ForegroundNormal", SystemColorIndex.HighlightText);
        SetCsv(ini, data, "Colors:Selection", "BackgroundNormal", SystemColorIndex.MenuHighlight);
        SetCsv(ini, data, "Colors:View", "BackgroundNormal", SystemColorIndex.Menu);
        SetCsv(ini, data, "Colors:View", "ForegroundNormal", SystemColorIndex.MenuText);
        SetCsv(ini, data, "Colors:View", "BackgroundNormal", SystemColorIndex.Background);
        SetCsv(ini, data, "Colors:View", "BackgroundNormal", SystemColorIndex.AppWorkspace);
        SetCsv(ini, data, "Colors:Tooltip", "BackgroundNormal", SystemColorIndex.Info);
        SetCsv(ini, data, "Colors:Tooltip", "ForegroundNormal", SystemColorIndex.InfoText);
        SetCsv(ini, data, "WM", "activeBackground", SystemColorIndex.ActiveCaption);
        SetCsv(ini, data, "WM", "activeForeground", SystemColorIndex.CaptionText);
        SetCsv(ini, data, "WM", "inactiveBackground", SystemColorIndex.InactiveCaption);
        SetCsv(ini, data, "WM", "inactiveForeground", SystemColorIndex.InactiveCaptionText);
        SetCsv(ini, data, "WM", "activeBackground", SystemColorIndex.GradientActiveCaption);
        SetCsv(ini, data, "WM", "inactiveBackground", SystemColorIndex.GradientInactiveCaption);

        // Accent is stored in the palette's accent slot (consumed by the WPF accent seam);
        // the portal is consulted first for it, so kdeglobals only fills when absent.
        if (data.AccentColorRef is null
            && ini.TryGetValue("Colors:Selection", "BackgroundNormal", out string? selection)
            && RgbColor.TryParseCsv(selection, out RgbColor accent))
        {
            data.AccentColorRef = accent.ToColorRef();
        }

        return data;
    }

    private static void SetCsv(IniFile ini, ThemeData data, string section, string key, int slot)
    {
        if (ini.TryGetValue(section, key, out string? raw) && RgbColor.TryParseCsv(raw, out RgbColor color))
        {
            data.SetColor(slot, color);
        }
    }
}
