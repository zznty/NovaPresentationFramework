using Nova.SystemTheme;

namespace Nova.DesktopTheme;

/// <summary>
/// Aggregates the fallback chain into a <see cref="DesktopPalette"/> and applies it through
/// <see cref="HostTheme"/>.
///
/// Fallback chain (per the design spec, measured on this box):
///  1. xdg-desktop-portal <c>org.freedesktop.appearance</c> — cross-desktop canonical for
///     dark/light + the REAL accent (float RGB). Always consulted first for those keys.
///  2. <c>kdeglobals</c> <c>[Colors:*]</c>/<c>[WM]</c> — KDE canonical rich palette.
///  3. <c>Trolltech.conf</c> <c>Palette\active</c> — exact bevel roles + the font.
///  4. GTK <c>colors.css</c> + <c>settings.ini</c> — GNOME/GTK canonical palette + font + DPI.
///  5. gsettings/dconf — deliberately NOT used (accent there is name-only on KDE; the spec
///     records the trap). Portal covers the same keys authoritatively.
/// Per-slot merge is first-source-wins; slots no source defines keep the Classic defaults, so
/// a missing file degrades gracefully and never throws.
/// </summary>
public sealed class DesktopThemeApplier(IReadOnlyList<IThemeSource> sources)
{
    /// <summary>Environment variable that opts into the desktop palette. Off by default.</summary>
    public const string PaletteEnvVar = "NOVA_PALETTE";

    /// <summary>Value of <see cref="PaletteEnvVar"/> that enables the desktop palette.</summary>
    public const string PaletteDesktopValue = "desktop";

    /// <summary>
    /// Applier that produced <see cref="LastAppliedProvider"/> (the live-restyle reload
    /// source). <c>null</c> until an <see cref="Apply"/> / <see cref="ApplyToProvider"/> call
    /// created a provider.
    /// </summary>
    public static DesktopThemeApplier? LastAppliedApplier { get; private set; }

    /// <summary>
    /// The most recently applied provider (the live-restyle swap target). <c>null</c> until an
    /// <see cref="Apply"/> / <see cref="ApplyToProvider"/> call created one.
    /// </summary>
    public static DesktopThemeProvider? LastAppliedProvider { get; private set; }

    /// <summary>
    /// True when the <c>Nova.DesktopTheme.Host</c> bridge assembly has been loaded (its module
    /// initializer calls <see cref="MarkBridgeLoaded"/>). The bridge carries the WPF-side
    /// invalidation for live restyle; without it the palette is still applied at startup but
    /// never re-applied on change.
    /// </summary>
    public static bool IsBridgeLoaded => Volatile.Read(ref s_bridgeLoaded) != 0;

    /// <summary>
    /// Called by the <c>Nova.DesktopTheme.Host</c> module initializer so
    /// <see cref="Apply"/> can warn (once) when the opt-in is on but the bridge was never
    /// referenced/loaded — the "silent no-op live restyle" failure mode.
    /// </summary>
    public static void MarkBridgeLoaded()
    {
        Volatile.Write(ref s_bridgeLoaded, 1);
    }

    private static int s_bridgeLoaded;
    private static int s_warnedMissingBridge;

    /// <summary>True when <see cref="PaletteEnvVar"/> equals <see cref="PaletteDesktopValue"/>.</summary>
    public static bool IsEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(PaletteEnvVar),
            PaletteDesktopValue,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds the KDE-first production source chain from the default config paths.</summary>
    public static DesktopThemeApplier CreateDefault(string homeDirectory)
    {
        string configDir = Path.Combine(homeDirectory, ".config");
        return new DesktopThemeApplier(
        [
            new PortalAppearanceSource(DbusPortal.ReadAppearance),
            new KdeGlobalsSource(Path.Combine(configDir, "kdeglobals")),
            new TrolltechConfigSource(Path.Combine(configDir, "Trolltech.conf")),
            new GtkSource(
                Path.Combine(configDir, "gtk-3.0", "settings.ini"),
                Path.Combine(configDir, "gtk-3.0", "colors.css"))
        ]);
    }

    /// <summary>
    /// Applies the desktop palette to <see cref="HostTheme"/>. When the opt-in is off, or no
    /// source produced data, <paramref name="inner"/> is returned unchanged (byte-identical
    /// default path). When on, the returned decorator overrides colors/fonts/DPI.
    /// </summary>
    public static IHostMetrics? Apply(IHostMetrics? inner)
    {
        if (!IsEnabled())
        {
            return inner;
        }

        WarnIfBridgeMissing();
        return ApplyToProvider(inner, CreateDefault(GetHomeDirectory()));
    }

    private static void WarnIfBridgeMissing()
    {
        if (IsBridgeLoaded || Interlocked.Exchange(ref s_warnedMissingBridge, 1) != 0)
        {
            return;
        }

        Console.Error.WriteLine(
            "[Nova.DesktopTheme] NOVA_PALETTE=desktop is set but the Nova.DesktopTheme.Host bridge is not referenced/loaded. " +
            "The desktop palette IS applied at startup, but it will NOT re-apply on change (live restyle no-ops silently). " +
            "Add a reference to Nova.DesktopTheme.Host and start Nova.DesktopTheme.Host.ThemeChangeListener at app startup.");
    }

    /// <summary>Applies a specific theme instance; used by tests and the live-restyle path.</summary>
    public static IHostMetrics? ApplyToProvider(IHostMetrics? inner, DesktopThemeApplier applier)
    {
        ArgumentNullException.ThrowIfNull(applier);
        DesktopPalette palette = applier.Load();
        if (palette.IsEmpty)
        {
            // No theme data: deactivate the desktop palette entirely (a source that
            // disappeared should not leave a stale palette applied).
            LastAppliedApplier = null;
            LastAppliedProvider = null;
            return inner;
        }

        var provider = new DesktopThemeProvider(inner ?? new DefaultMetrics(), palette);
        LastAppliedApplier = applier;
        LastAppliedProvider = provider;
        return provider;
    }

    /// <summary>Merges the source chain per-slot (first source wins) into a palette.</summary>
    public DesktopPalette Load()
    {
        var merged = new ThemeData();
        foreach (IThemeSource source in sources)
        {
            Merge(merged, source.Load());
        }

        if (merged.Colors.Count > 0)
        {
            SynthesizeBevels(merged);
        }

        var colors = new Dictionary<int, int>(merged.Colors.Count);
        foreach ((int slot, RgbColor color) in merged.Colors)
        {
            colors[slot] = color.ToColorRef();
        }

        SystemFontMetrics? font = ToFontMetrics(merged.FontFamily, merged.FontPointSize, merged.FontWeight);
        return new DesktopPalette(colors, font, merged.PixelsPerInch, merged.AccentColorRef, merged.IsDark);
    }

    /// <summary>
    /// Re-reads all sources and returns a fresh palette (live-restyle path: file watchers and
    /// the portal signal call this, then swap it into the active provider).
    /// </summary>
    public DesktopPalette Reload()
    {
        return Load();
    }

    private static void Merge(ThemeData target, ThemeData contribution)
    {
        foreach ((int slot, RgbColor color) in contribution.Colors)
        {
            if (!target.Colors.ContainsKey(slot))
            {
                target.SetColor(slot, color);
            }
        }

        target.FontFamily ??= contribution.FontFamily;
        target.FontPointSize ??= contribution.FontPointSize;
        target.FontWeight ??= contribution.FontWeight;
        target.PixelsPerInch ??= contribution.PixelsPerInch;
        target.AccentColorRef ??= contribution.AccentColorRef;
        target.IsDark ??= contribution.IsDark;
    }

    private static void SynthesizeBevels(ThemeData data)
    {
        if (!data.Colors.TryGetValue(SystemColorIndex.ButtonFace, out RgbColor face))
        {
            return;
        }

        if (!data.Colors.ContainsKey(SystemColorIndex.ButtonHighlight))
        {
            data.SetColor(SystemColorIndex.ButtonHighlight, face.Lighten(0.30));
        }

        if (!data.Colors.ContainsKey(SystemColorIndex.ThreeDLight))
        {
            data.SetColor(SystemColorIndex.ThreeDLight, face.Lighten(0.15));
        }

        if (!data.Colors.ContainsKey(SystemColorIndex.ButtonShadow))
        {
            data.SetColor(SystemColorIndex.ButtonShadow, face.Darken(0.20));
        }

        if (!data.Colors.ContainsKey(SystemColorIndex.ThreeDDarkShadow))
        {
            data.SetColor(SystemColorIndex.ThreeDDarkShadow, face.Darken(0.35));
        }
    }

    private static SystemFontMetrics? ToFontMetrics(string? family, int? pointSize, int? weight)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        // LOGFONT height from point size: lfHeight = -MulDiv(pointSize, LOGPIXELSY(96), 72).
        int height = -MulDiv(pointSize ?? 10, 96, 72);
        return new SystemFontMetrics(family, height, weight ?? 400);
    }

    private static int MulDiv(int number, int numerator, int denominator)
    {
        return (int)((long)number * numerator / denominator);
    }

    private static string GetHomeDirectory()
    {
        // Testability seam: NOVA_DESKTOP_THEME_HOME points the whole chain at a fixture
        // directory (harness/CI runs the same binary against canned config files).
        string? fixtureHome = Environment.GetEnvironmentVariable("NOVA_DESKTOP_THEME_HOME");
        if (!string.IsNullOrWhiteSpace(fixtureHome))
        {
            return fixtureHome;
        }

        string? home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        string? userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        return string.IsNullOrWhiteSpace(userProfile) ? "." : userProfile;
    }

    private sealed class DefaultMetrics : IHostMetrics
    {
        public int PixelsPerInch => 96;

        public int DoubleClickTime => 500;

        public void GetWorkArea(out int left, out int top, out int right, out int bottom)
        {
            left = 0;
            top = 0;
            right = 1920;
            bottom = 1080;
        }

        public int GetSystemMetric(int index)
        {
            return index switch
            {
                SystemMetricIndex.CxScreen or SystemMetricIndex.CxVirtualScreen => 1920,
                SystemMetricIndex.CyScreen or SystemMetricIndex.CyVirtualScreen => 1080,
                SystemMetricIndex.MousePresent => 1,
                SystemMetricIndex.MouseButtons => 5,
                SystemMetricIndex.MouseWheelPresent => 1,
                SystemMetricIndex.MonitorCount => 1,
                SystemMetricIndex.SameDisplayFormat => 1,
                _ => 0
            };
        }
    }
}
