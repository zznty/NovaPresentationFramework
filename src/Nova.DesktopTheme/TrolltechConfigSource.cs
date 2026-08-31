using System.Globalization;
using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// Qt palette and font: <c>~/.config/Trolltech.conf</c>, <c>[qt]</c> group. The 22-role
/// <c>Palette\active</c> CSV carries the exact bevel roles (QPalette Light/Midlight/Dark/
/// Shadow) and the accent slot; <c>font=</c> is a QFont serialization
/// (<c>family,pointSize,…,weight,…</c>); <c>KWinPalette\frame</c> is the real window-frame
/// color. This is the more precise palette+font source on KDE than kdeglobals.
/// </summary>
public sealed class TrolltechConfigSource(string path) : IThemeSource
{
    private const int QPaletteLight = 2;
    private const int QPaletteMidlight = 3;
    private const int QPaletteDark = 4;
    private const int QPaletteShadow = 11;
    private const int QPaletteAccent = 21;

    public string Name => "Trolltech.conf";

    public ThemeData Load()
    {
        var data = new ThemeData();
        string? text = ThemeFile.TryReadText(path);
        if (text is null)
        {
            return data;
        }

        IniFile ini = IniFile.Parse(text);
        RgbColor[]? roles = ParseRoleCsv(ini["qt", "Palette\\active"]);
        if (roles is not null)
        {
            TrySetRole(roles, QPaletteLight, SystemColorIndex.ButtonHighlight, data);
            TrySetRole(roles, QPaletteMidlight, SystemColorIndex.ThreeDLight, data);
            TrySetRole(roles, QPaletteDark, SystemColorIndex.ButtonShadow, data);
            TrySetRole(roles, QPaletteShadow, SystemColorIndex.ThreeDDarkShadow, data);
            if (roles.Length > QPaletteAccent && data.AccentColorRef is null)
            {
                data.AccentColorRef = roles[QPaletteAccent].ToColorRef();
            }
        }

        if (RgbColor.TryParseHex(ini["qt", "KWinPalette\\frame"], out RgbColor frame))
        {
            data.SetColor(SystemColorIndex.WindowFrame, frame);
        }

        ApplyFont(ini["qt", "font"], data);
        return data;
    }

    private static RgbColor[]? ParseRoleCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        var roles = new RgbColor[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!RgbColor.TryParseHex(parts[i], out roles[i]))
            {
                return null;
            }
        }

        return roles;
    }

    private static void TrySetRole(RgbColor[] roles, int role, int slot, ThemeData data)
    {
        if (roles.Length > role)
        {
            data.SetColor(slot, roles[role]);
        }
    }

    private static void ApplyFont(string? serialized, ThemeData data)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return;
        }

        string[] parts = serialized.Split(',');
        if (parts.Length < 5 || string.IsNullOrWhiteSpace(parts[0]))
        {
            return;
        }

        data.FontFamily = parts[0].Trim();
        if (int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pointSize)
            && pointSize > 0)
        {
            data.FontPointSize = pointSize;
        }

        if (int.TryParse(parts[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight)
            && weight > 0)
        {
            data.FontWeight = weight;
        }
    }
}
