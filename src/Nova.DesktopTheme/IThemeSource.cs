namespace Nova.DesktopTheme;

/// <summary>
/// A desktop-theme data source (kdeglobals, Trolltech.conf, GTK colors.css/settings.ini, the
/// xdg-desktop-portal). Loaders must never throw: a missing or unreadable source yields an
/// empty <see cref="ThemeData"/> (the aggregator fills gaps per the fallback chain).
/// </summary>
public interface IThemeSource
{
    /// <summary>Human-readable source name for diagnostics.</summary>
    public string Name { get; }

    /// <summary>Reads the source; never throws.</summary>
    public ThemeData Load();
}
