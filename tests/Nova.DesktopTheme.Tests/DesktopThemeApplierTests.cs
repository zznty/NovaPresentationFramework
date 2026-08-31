using Nova.SystemTheme;

namespace Nova.DesktopTheme.Tests;

public sealed class DesktopThemeApplierTests
{
    private static readonly string FixtureHome = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "kde");

    private static DesktopThemeApplier CreateFixtureApplier()
    {
        return DesktopThemeApplier.CreateDefault(FixtureHome);
    }

    [Fact]
    public void Load_AllSources_MergesPerSlotFirstSourceWins()
    {
        DesktopPalette palette = CreateFixtureApplier().Load();

        // kdeglobals owns the base palette.
        Assert.Equal(0x001E1E1E, palette.Colors[SystemColorIndex.Window]);
        Assert.Equal(0x00444444, palette.Colors[SystemColorIndex.ButtonFace]);
        Assert.Equal(0x00E6A00A, palette.Colors[SystemColorIndex.Highlight]);
        // Trolltech owns exact bevels (kdeglobals does not define them).
        Assert.Equal(0x002E2E2E, palette.Colors[SystemColorIndex.ButtonHighlight]);
        Assert.Equal(0x00151515, palette.Colors[SystemColorIndex.ButtonShadow]);
        // Trolltech KWinPalette\frame beats GTK borders.
        Assert.Equal(0x001E1E1E, palette.Colors[SystemColorIndex.WindowFrame]);
        // Portal owns accent + dark, kdeglobals is the fallback.
        Assert.Equal(0x00E6A00A, palette.AccentColorRef);
        Assert.True(palette.IsDark);
        // Trolltech owns the font.
        SystemFontMetrics font = Assert.NotNull(palette.Font);
        Assert.Equal("Noto Sans", font.FaceName);
        Assert.Equal(-13, font.Height);
        Assert.Equal(400, font.Weight);
    }

    [Fact]
    public void Load_BevelSynthesis_FillsMissingBevelsFromButtonFace()
    {
        // A kdeglobals-only source (no Trolltech) gets synthesized bevels.
        string dir = Path.Combine(Path.GetTempPath(), "nova-detheme-test-" + Guid.NewGuid().ToString("N"));
        string configDir = Path.Combine(dir, ".config");
        _ = Directory.CreateDirectory(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "kdeglobals"),
            "[Colors:Button]\nBackgroundNormal=68,68,68\nForegroundNormal=250,250,250\n");
        try
        {
            var applier = new DesktopThemeApplier(
            [
                new KdeGlobalsSource(Path.Combine(configDir, "kdeglobals"))
            ]);
            DesktopPalette palette = applier.Load();

            Assert.Equal(0x00444444, palette.Colors[SystemColorIndex.ButtonFace]);
            Assert.True(palette.Colors.ContainsKey(SystemColorIndex.ButtonHighlight));
            Assert.True(palette.Colors.ContainsKey(SystemColorIndex.ThreeDLight));
            Assert.True(palette.Colors.ContainsKey(SystemColorIndex.ButtonShadow));
            Assert.True(palette.Colors.ContainsKey(SystemColorIndex.ThreeDDarkShadow));
            Assert.True(palette.Colors[SystemColorIndex.ButtonHighlight] > palette.Colors[SystemColorIndex.ButtonFace]);
            Assert.True(palette.Colors[SystemColorIndex.ButtonShadow] < palette.Colors[SystemColorIndex.ButtonFace]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ApplyToProvider_NoSources_ReturnsInnerUnchanged()
    {
        var inner = new StubMetrics();
        var applier = new DesktopThemeApplier(
        [
            new KdeGlobalsSource("/nonexistent/kdeglobals"),
            new TrolltechConfigSource("/nonexistent/Trolltech.conf"),
            new GtkSource("/nonexistent/settings.ini", "/nonexistent/colors.css")
        ]);
        IHostMetrics? result = DesktopThemeApplier.ApplyToProvider(inner, applier);
        Assert.Same(inner, result);
    }

    [Fact]
    public void ApplyToProvider_NullInner_ProvidesDefaultMetricsFallback()
    {
        var applier = new DesktopThemeApplier(
        [
            new KdeGlobalsSource(Path.Combine(FixtureHome, ".config", "kdeglobals"))
        ]);
        IHostMetrics? result = DesktopThemeApplier.ApplyToProvider(null, applier);
        var typed = Assert.IsType<DesktopThemeProvider>(result);
        Assert.Equal(1920, typed.GetSystemMetric(SystemMetricIndex.CxScreen));
        Assert.Equal(500, typed.DoubleClickTime);
    }

    [Fact]
    public void ApplyToProvider_HostThemeRoundTrip_ResolvesThemedColorsAndFont()
    {
        IHostMetrics? provider = DesktopThemeApplier.ApplyToProvider(null, CreateFixtureApplier());
        Assert.NotNull(provider);
        try
        {
            HostTheme.SetProvider(provider);
            Assert.Equal(0x00E6A00A, HostTheme.GetSysColor(SystemColorIndex.Highlight));
            Assert.Equal(0x00444444, HostTheme.GetSysColor(SystemColorIndex.ButtonFace));
            Assert.Equal(0x001E1E1E, HostTheme.GetSysColor(SystemColorIndex.Window));
            Assert.Equal(0x00DEDEDE, HostTheme.GetSysColor(SystemColorIndex.WindowText));
            Assert.Equal("Noto Sans", HostTheme.NonClient.MessageFont.FaceName);
            Assert.Equal(-13, HostTheme.NonClient.MessageFont.Height);
        }
        finally
        {
            HostTheme.SetProvider(null);
        }
    }

    [Fact]
    public void IsEnabled_Unset_IsFalse()
    {
        string? previous = Environment.GetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, null);
            Assert.False(DesktopThemeApplier.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, previous);
        }
    }

    [Fact]
    public void IsEnabled_DesktopValue_IsTrue()
    {
        string? previous = Environment.GetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, DesktopThemeApplier.PaletteDesktopValue);
            Assert.True(DesktopThemeApplier.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, previous);
        }
    }

    [Fact]
    public void PaletteSwitch_IsOrthogonalToThemeSwitch()
    {
        // The palette axis (NOVA_PALETTE) must compose with the uxtheme chrome axis
        // (NOVA_THEME=classic|aero|aero2, owned by the aero worker): a real KDE user most
        // plausibly wants NOVA_THEME=aero2 WITH the desktop palette. The applier must treat
        // any NOVA_THEME value as irrelevant — it only keys off NOVA_PALETTE.
        string? previousPalette = Environment.GetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar);
        string? previousTheme = Environment.GetEnvironmentVariable("NOVA_THEME");
        try
        {
            Environment.SetEnvironmentVariable("NOVA_THEME", "aero2");
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, null);
            Assert.False(DesktopThemeApplier.IsEnabled(), "NOVA_THEME alone must not enable the palette");

            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, DesktopThemeApplier.PaletteDesktopValue);
            Assert.True(DesktopThemeApplier.IsEnabled(), "NOVA_PALETTE=desktop enables regardless of NOVA_THEME");

            // And the applier produces a themed palette even with NOVA_THEME set.
            IHostMetrics? provider = DesktopThemeApplier.ApplyToProvider(null, CreateFixtureApplier());
            Assert.NotNull(provider);
            HostTheme.SetProvider(provider);
            try
            {
                Assert.Equal(0x00E6A00A, HostTheme.GetSysColor(SystemColorIndex.Highlight));
            }
            finally
            {
                HostTheme.SetProvider(null);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, previousPalette);
            Environment.SetEnvironmentVariable("NOVA_THEME", previousTheme);
        }
    }

    private sealed class StubMetrics : IHostMetrics
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
            return 0;
        }
    }

    [Fact]
    public void MarkBridgeLoaded_Applies_SuppressesWarning()
    {
        string? previous = Environment.GetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, DesktopThemeApplier.PaletteDesktopValue);
            // Fresh state: bridge not loaded yet.
            DesktopThemeApplier.MarkBridgeLoaded();
            Assert.True(DesktopThemeApplier.IsBridgeLoaded);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DesktopThemeApplier.PaletteEnvVar, previous);
        }
    }
}
