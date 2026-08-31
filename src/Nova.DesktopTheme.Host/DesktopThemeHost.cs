namespace Nova.DesktopTheme.Host;

/// <summary>
/// Applies a freshly-loaded desktop palette to the live WPF session: re-reads the sources,
/// swaps the palette into the active <see cref="DesktopThemeProvider"/>, clears the
/// SystemColors/SystemParameters caches, and walks every PresentationSource's root tree so
/// <c>DynamicResource {SystemColors.*Key}</c> re-evaluates. This is the WPF side of live
/// restyle; it must run on the WPF dispatcher thread (mirrors the WM_THEMECHANGED flow).
/// Requires the IVT from PresentationFramework (patch 0016) — that is the ONLY reason this
/// type is separate from <see cref="DesktopThemeApplier"/>.
///
/// <b>Consuming applications MUST reference <c>Nova.DesktopTheme.Host</c> for live restyle
/// to function.</b> This assembly is deliberately NOT referenced by the Framework build chain
/// (that would be a cycle: patched Framework → Nova.SdlSource → Nova.Sdl → Nova.DesktopTheme
/// → Framework). An app that references only <c>Nova.DesktopTheme</c> still gets the desktop
/// palette applied at startup (via the <c>Nova.Sdl</c> composition in <c>SdlHost</c>), but the
/// file-watcher/portal-signal → <c>ApplyLive</c> re-application never runs. When
/// <c>NOVA_PALETTE=desktop</c> is set and this bridge was never loaded, <see cref="DesktopThemeApplier.Apply"/>
/// prints a one-time warning to stderr. To wire live restyle, reference this assembly and start
/// <see cref="ThemeChangeListener"/> at app startup.
/// </summary>
public static class DesktopThemeHost
{
    static DesktopThemeHost()
    {
        // Marks the bridge as wired so DesktopThemeApplier.Apply can warn (once) when the
        // opt-in is on but this assembly was never referenced/used — the silent live-restyle
        // no-op failure mode. Runs the first time the app touches this type (IsActive,
        // ApplyLive, or ThemeChangeListener construction).
        DesktopThemeApplier.MarkBridgeLoaded();
    }

    /// <summary>
    /// Re-reads the palette from the applier that produced the currently-active provider and
    /// re-applies it live. Returns <c>true</c> when a theme was applied (or re-applied), and
    /// <c>false</c> when no desktop provider is active (opt-in off or nothing applied yet).
    /// </summary>
    public static bool ApplyLive()
    {
        DesktopThemeApplier? applier = DesktopThemeApplier.LastAppliedApplier;
        DesktopThemeProvider? provider = DesktopThemeApplier.LastAppliedProvider;
        if (applier is null || provider is null)
        {
            return false;
        }

        DesktopPalette palette = applier.Reload();
        if (palette.IsEmpty)
        {
            return false;
        }

        provider.ReplacePalette(palette);
        Metrics = GtkThemeMetrics.Load();

        // The Windows WM_THEMECHANGED flow: clear the color/brush memoization, clear the
        // parameters cache + derived theme properties, then walk the live trees.
        _ = System.Windows.SystemColors.InvalidateCache();
        System.Windows.SystemParameters.InvalidateCache();
        System.Windows.SystemParameters.InvalidateDerivedThemeRelatedProperties();
        InvalidateLiveTrees();
        return true;
    }

    /// <summary>True when a desktop palette provider is currently active.</summary>
    public static bool IsActive()
    {
        return DesktopThemeApplier.LastAppliedProvider is not null;
    }

    /// <summary>
    /// The structural DE theme metrics (Adwaita defaults + the active GTK CSS theme
    /// overrides). Refreshed on <see cref="ApplyLive"/>. The control template set consumes
    /// these values for radius/padding/border sizing; colors stay in the palette provider.
    /// </summary>
    public static GtkThemeMetrics Metrics { get; private set; } = GtkThemeMetrics.Load();

    /// <summary>Re-reads the GTK CSS metrics without touching the palette provider.</summary>
    public static void ReloadMetrics()
    {
        Metrics = GtkThemeMetrics.Load();
    }

    private static void InvalidateLiveTrees()
    {
        // Replicates SystemResources.InvalidateTreeResources(SysColorsOrSettingsChangeInfo):
        // walk every PresentationSource's root visual and invalidate its resources so the
        // DynamicResource expressions re-resolve against the fresh SystemColors.
        foreach (System.Windows.PresentationSource source in System.Windows.PresentationSource.CurrentSources)
        {
            if (source.IsDisposed || source.RootVisual is not System.Windows.FrameworkElement root)
            {
                continue;
            }

            System.Windows.TreeWalkHelper.InvalidateOnResourcesChange(
                root,
                fce: null,
                System.Windows.ResourcesChangeInfo.SysColorsOrSettingsChangeInfo);
        }
    }
}
