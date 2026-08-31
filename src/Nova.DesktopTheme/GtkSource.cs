using System.Globalization;
using System.Text.RegularExpressions;
using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// GTK theme: <c>~/.config/gtk-3.0/settings.ini</c> (<c>gtk-font-name</c>,
/// <c>gtk-xft-dpi</c>, <c>gtk-application-prefer-dark-theme</c>) plus <c>colors.css</c>
/// (<c>@define-color</c> named colors). Authoritative on GNOME; on KDE these files are KDE's
/// GTK-compat mirror of the same palette. GTK color names are suffixed with the base theme
/// name (<c>theme_bg_color_breeze</c>); lookups accept both forms.
/// </summary>
public sealed class GtkSource(string settingsIniPath, string colorsCssPath) : IThemeSource
{
    private static readonly Regex DefineColor = new(
        "@define-color\\s+([A-Za-z0-9_-]+)\\s+#([0-9a-fA-F]{6})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Name => "GTK";

    public ThemeData Load()
    {
        var data = new ThemeData();
        Dictionary<string, RgbColor>? named = ReadDefineColors(colorsCssPath);
        if (named is not null)
        {
            SetHex(named, data, "theme_bg_color", SystemColorIndex.Window);
            SetHex(named, data, "theme_fg_color", SystemColorIndex.WindowText);
            SetHex(named, data, "theme_bg_color", SystemColorIndex.Background);
            SetHex(named, data, "theme_button_background_normal", SystemColorIndex.ButtonFace);
            SetHex(named, data, "theme_button_foreground_normal", SystemColorIndex.ButtonText);
            SetHex(named, data, "theme_button_decoration_hover", SystemColorIndex.HotLight);
            SetHex(named, data, "theme_selected_bg_color", SystemColorIndex.Highlight);
            SetHex(named, data, "theme_selected_fg_color", SystemColorIndex.HighlightText);
            SetHex(named, data, "theme_selected_bg_color", SystemColorIndex.MenuHighlight);
            SetHex(named, data, "theme_base_color", SystemColorIndex.Menu);
            SetHex(named, data, "theme_fg_color", SystemColorIndex.MenuText);
            SetHex(named, data, "theme_base_color", SystemColorIndex.AppWorkspace);
            SetHex(named, data, "tooltip_background", SystemColorIndex.Info);
            SetHex(named, data, "tooltip_text", SystemColorIndex.InfoText);
            SetHex(named, data, "theme_titlebar_background", SystemColorIndex.ActiveCaption);
            SetHex(named, data, "theme_titlebar_foreground", SystemColorIndex.CaptionText);
            SetHex(named, data, "theme_titlebar_background_backdrop", SystemColorIndex.InactiveCaption);
            SetHex(named, data, "theme_titlebar_foreground_backdrop", SystemColorIndex.InactiveCaptionText);
            SetHex(named, data, "theme_view_hover_decoration_color", SystemColorIndex.ActiveBorder);
            SetHex(named, data, "insensitive_fg_color", SystemColorIndex.GrayText);
            SetHex(named, data, "borders", SystemColorIndex.WindowFrame);
        }

        IniFile settings = ReadIni(settingsIniPath);
        ApplyFont(settings["Settings", "gtk-font-name"], data);
        ApplyDpi(settings["Settings", "gtk-xft-dpi"], data);
        if (bool.TryParse(settings["Settings", "gtk-application-prefer-dark-theme"], out bool dark))
        {
            data.IsDark = dark;
        }

        return data;
    }

    private static Dictionary<string, RgbColor>? ReadDefineColors(string path)
    {
        string? text = ThemeFile.TryReadText(path);
        if (text is null)
        {
            return null;
        }

        var colors = new Dictionary<string, RgbColor>(StringComparer.Ordinal);
        foreach (Match match in DefineColor.Matches(text))
        {
            string name = match.Groups[1].Value;
            if (!RgbColor.TryParseHex(match.Groups[2].Value, out RgbColor color))
            {
                continue;
            }

            colors[name] = color;
            int last = name.LastIndexOf('_');
            if (last > 0)
            {
                // Accept the base name too: theme_bg_color_breeze → theme_bg_color.
                _ = colors.TryAdd(name[..last], color);
            }
        }

        return colors;
    }

    private static void SetHex(
        Dictionary<string, RgbColor> named,
        ThemeData data,
        string colorName,
        int slot)
    {
        if (named.TryGetValue(colorName, out RgbColor color))
        {
            data.SetColor(slot, color);
        }
    }

    private static IniFile ReadIni(string path)
    {
        string? text = ThemeFile.TryReadText(path);
        return text is null ? IniFile.Parse(string.Empty) : IniFile.Parse(text);
    }

    private static void ApplyFont(string? fontName, ThemeData data)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return;
        }

        string[] parts = fontName.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[0].Length == 0)
        {
            return;
        }

        data.FontFamily = parts[0];
        if (parts.Length > 1
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pointSize)
            && pointSize > 0)
        {
            data.FontPointSize = pointSize;
        }
    }

    private static void ApplyDpi(string? dpiValue, ThemeData data)
    {
        // GTK encodes DPI as logical-pixels × 1024 (96 → 98304).
        if (int.TryParse(dpiValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)
            && raw >= 96 * 1024)
        {
            data.PixelsPerInch = raw / 1024;
        }
    }
}
