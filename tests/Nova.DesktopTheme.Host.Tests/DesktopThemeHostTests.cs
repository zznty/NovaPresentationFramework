using Nova.SystemTheme;

namespace Nova.DesktopTheme.Host.Tests;

/// <summary>
/// End-to-end live-restyle proof: a real WPF <c>SystemColors</c> read, a source change, and
/// the bridge's invalidation making a subsequent read return the NEW color. This is the test
/// that would fail if the bridge were never loaded / the cache were never cleared: the second
/// <c>SystemColors.WindowBrush</c> read returns the memoized first color (MEASURED upstream
/// memoization), proving the invalidation actually ran.
/// </summary>
public sealed partial class DesktopThemeHostTests : IDisposable
{
    private readonly string _configDir;
    private readonly string _previousHome;
    private readonly string? _previousPalette;

    public DesktopThemeHostTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "nova-detheme-host-test-" + Guid.NewGuid().ToString("N"));
        string home = _configDir;
        string configDir = Path.Combine(home, ".config");
        string gtkDir = Path.Combine(configDir, "gtk-3.0");
        _ = Directory.CreateDirectory(gtkDir);
        File.WriteAllText(
            Path.Combine(configDir, "kdeglobals"),
            "[Colors:Window]\nBackgroundNormal=30,30,30\nForegroundNormal=222,222,222\n[Colors:Button]\nBackgroundNormal=68,68,68\nForegroundNormal=250,250,250\n[Colors:Selection]\nBackgroundNormal=10,160,230\nForegroundNormal=255,255,255\n");
        File.WriteAllText(
            Path.Combine(configDir, "Trolltech.conf"),
            "[qt]\nfont=\"Noto Sans,10,-1,0,400,0,0,0,0,0,0,0,0,0,0,1,,0,0\"\n");
        File.WriteAllText(Path.Combine(gtkDir, "settings.ini"), "[Settings]\n");
        File.WriteAllText(Path.Combine(gtkDir, "colors.css"), string.Empty);

        _previousHome = Environment.GetEnvironmentVariable("NOVA_DESKTOP_THEME_HOME")!;
        _previousPalette = Environment.GetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar);
        Environment.SetEnvironmentVariable("NOVA_DESKTOP_THEME_HOME", _configDir);
        Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, DesktopThemeApplier.PaletteDesktopValue);
    }

    public void Dispose()
    {
        HostTheme.SetProvider(null);
        Environment.SetEnvironmentVariable("NOVA_DESKTOP_THEME_HOME", _previousHome);
        Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, _previousPalette);
        try
        {
            Directory.Delete(_configDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ApplyLive_SourceChanged_SystemColorsReResolve()
    {
        // Apply theme A (window #1E1E1E) through the same path a real app uses.
        IHostMetrics? provider = DesktopThemeApplier.ApplyToProvider(
            null,
            DesktopThemeApplier.CreateDefault(_configDir));
        Assert.NotNull(provider);
        HostTheme.SetProvider(provider);
        Assert.True(DesktopThemeHost.IsActive());

        // Prime the memoized cache with theme A.
        System.Windows.Media.Color first = System.Windows.SystemColors.WindowColor;
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E), first);

        // Change the source: window becomes #444444.
        File.WriteAllText(
            Path.Combine(_configDir, ".config", "kdeglobals"),
            "[Colors:Window]\nBackgroundNormal=68,68,68\nForegroundNormal=222,222,222\n[Colors:Button]\nBackgroundNormal=68,68,68\nForegroundNormal=250,250,250\n[Colors:Selection]\nBackgroundNormal=10,160,230\nForegroundNormal=255,255,255\n");

        // Live-restyle: reload + swap + invalidate.
        Assert.True(DesktopThemeHost.ApplyLive());

        // Without InvalidateCache this read would return the memoized #1E1E1E.
        System.Windows.Media.Color second = System.Windows.SystemColors.WindowColor;
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44), second);
    }

    [Fact]
    public void ApplyLive_NoActiveProvider_ReturnsFalse()
    {
        HostTheme.SetProvider(null);
        // Reset the process-wide statics by applying an empty palette (nothing to apply).
        _ = DesktopThemeApplier.ApplyToProvider(
            null,
            new DesktopThemeApplier(
            [
                new KdeGlobalsSource(Path.Combine(_configDir, ".config", "kdeglobals-missing")),
                new TrolltechConfigSource(Path.Combine(_configDir, ".config", "Trolltech-missing.conf"))
            ]));
        Assert.False(DesktopThemeApplier.LastAppliedProvider is not null);
        Assert.False(DesktopThemeHost.IsActive());
        Assert.False(DesktopThemeHost.ApplyLive());
    }

    [Fact]
    public void ApplyLive_SourceDeleted_YieldsEmptyAndKeepsCurrent()
    {
        // Re-apply the fresh fixture first so the process-wide statics are reset.
        IHostMetrics? provider = DesktopThemeApplier.ApplyToProvider(
            null,
            DesktopThemeApplier.CreateDefault(_configDir));
        Assert.NotNull(provider);
        HostTheme.SetProvider(provider);
        // Clear any SystemColors memoization from prior tests.
        _ = DesktopThemeHost.ApplyLive();
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E), System.Windows.SystemColors.WindowColor);

        // Remove every source; Reload yields an empty palette → ApplyLive keeps the current.
        File.Delete(Path.Combine(_configDir, ".config", "kdeglobals"));
        File.Delete(Path.Combine(_configDir, ".config", "Trolltech.conf"));
        Assert.False(DesktopThemeHost.ApplyLive());
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E), System.Windows.SystemColors.WindowColor);
    }
}

public sealed partial class DesktopThemeHostTests
{
    [Fact]
    public void AdwaitaTheme_Load_InjectsMetricsAndTemplates()
    {
        System.Windows.ResourceDictionary theme = new GtkThemeDictionary();

        // The bridge injects the structural metrics from DesktopThemeHost.Metrics.
        GtkControlMetrics button = DesktopThemeHost.Metrics.Controls["button"];
        Assert.Equal(new System.Windows.CornerRadius(button.BorderRadius), theme["Adwaita.Button.Radius"]);
        Assert.Equal(button.PaddingLeft, ((System.Windows.Thickness)theme["Adwaita.Button.Padding"]!).Left);

        // The templates exist and the metrics resources they read are present.
        _ = Assert.IsType<System.Windows.Style>(theme[typeof(System.Windows.Controls.Button)]);
        _ = Assert.IsType<System.Windows.Media.SolidColorBrush>(theme["Adwaita.Accent.Background"]);
    }
}


public sealed partial class DesktopThemeHostTests
{
    [Fact]
    public void Probe_ButtonForeground()
    {
        System.Windows.ResourceDictionary dict = new GtkThemeDictionary();
        Console.WriteLine($"PROBE buttonFg={dict["Adwaita.Button.Foreground"]} textFg={dict["Adwaita.Text.Foreground"]}");
    }
}

public sealed partial class DesktopThemeHostTests
{
    [Fact]
    public void Probe_AnimationValues()
    {
        var dict = new GtkThemeDictionary();
        foreach (object? value in dict.MergedDictionaries[0].Values)
        {
            if (value is not System.Windows.Style { TargetType.Name: "Button" } style)
            {
                continue;
            }

            foreach (System.Windows.SetterBase setterBase in style.Setters)
            {
                if (setterBase is System.Windows.Setter { Value: System.Windows.Controls.ControlTemplate template })
                {
                    foreach (System.Windows.TriggerBase trigger in template.Triggers)
                    {
                        if (trigger is not System.Windows.Trigger { Property.Name: "IsMouseOver" } hover)
                        {
                            continue;
                        }

                        foreach (System.Windows.TriggerAction action in hover.EnterActions)
                        {
                            var sb = (System.Windows.Media.Animation.BeginStoryboard)action;
                            foreach (System.Windows.Media.Animation.ColorAnimation anim in sb.Storyboard.Children.Cast<System.Windows.Media.Animation.ColorAnimation>())
                            {
                                Console.WriteLine($"PROBE enter to={anim.To} dur={anim.Duration.TimeSpan.Milliseconds} target={System.Windows.Media.Animation.Storyboard.GetTargetName(anim)} prop={System.Windows.Media.Animation.Storyboard.GetTargetProperty(anim)}");
                            }
                        }

                        foreach (System.Windows.TriggerAction action in hover.ExitActions)
                        {
                            var sb = (System.Windows.Media.Animation.BeginStoryboard)action;
                            foreach (System.Windows.Media.Animation.ColorAnimation anim in sb.Storyboard.Children.Cast<System.Windows.Media.Animation.ColorAnimation>())
                            {
                                Console.WriteLine($"PROBE exit to={anim.To} dur={anim.Duration.TimeSpan.Milliseconds} target={System.Windows.Media.Animation.Storyboard.GetTargetName(anim)}");
                            }
                        }
                    }
                }
            }
        }
    }
}
