namespace Nova.DesktopTheme;

/// <summary>Shared text-file reads for theme sources. All reads are best-effort (never throw).</summary>
internal static class ThemeFile
{
    public static string? TryReadText(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
