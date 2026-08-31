namespace Nova.DesktopTheme;

/// <summary>
/// Minimal KDE/Qt/glib INI reader (BCL only, ~200 lines by design — no dependency). Handles
/// <c>[Section]</c> groups, <c>Key=value</c> pairs, <c>;</c>/<c>#</c> comment lines, quoted
/// values (<c>font="Noto Sans,10,…"</c>), UTF-8 BOM, CRLF/LF, and KDE's <c>\;</c> escape in
/// values. Backslashes in keys are literal path separators (<c>Palette\active</c>,
/// <c>KWinPalette\frame</c>), not escapes. Malformed lines are skipped; the parser never
/// throws on input content.
/// </summary>
public sealed class IniFile
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

    private IniFile()
    {
    }

    /// <summary>Parses <paramref name="text"/> into a lookup; never throws on malformed lines.</summary>
    public static IniFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var file = new IniFile();
        string section = string.Empty;
        foreach (string rawLine in text.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                continue;
            }

            int equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            string key = line[..equals].Trim();
            string value = Unquote(line[(equals + 1)..].Trim());
            file._entries[Qualify(section, key)] = value;
        }

        return file;
    }

    /// <summary>Gets a value; sections and keys are case-insensitive, missing pairs yield <c>null</c>.</summary>
    public string? this[string section, string key] => TryGetValue(section, key, out string? value) ? value : null;

    /// <summary>Gets a value; sections and keys are case-insensitive.</summary>
    public bool TryGetValue(string section, string key, out string? value)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(Qualify(section, key), out value);
    }

    private static string Qualify(string section, string key)
    {
        return section.Length == 0 ? key : section + "\u0000" + key;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return value.Replace("\\;", ";", StringComparison.Ordinal);
    }
}
