using Nova.SystemTheme;

namespace Nova.DesktopTheme.Tests;

public sealed class GtkSourceTests
{
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "fixtures", "kde", ".config", "gtk-3.0");

    [Fact]
    public void Load_Fixture_ProducesGtkPaletteFontDpi()
    {
        var source = new GtkSource(
            Path.Combine(FixtureDir, "settings.ini"),
            Path.Combine(FixtureDir, "colors.css"));
        ThemeData data = source.Load();

        Assert.Equal(0x001E1E1E, data.Colors[SystemColorIndex.Window].ToColorRef());
        Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.WindowText].ToColorRef());
        Assert.Equal(0x00444444, data.Colors[SystemColorIndex.ButtonFace].ToColorRef());
        Assert.Equal(0x00FAFAFA, data.Colors[SystemColorIndex.ButtonText].ToColorRef());
        Assert.Equal(0x00E6A00A, data.Colors[SystemColorIndex.Highlight].ToColorRef());
        Assert.Equal(0x00FFFFFF, data.Colors[SystemColorIndex.HighlightText].ToColorRef());
        Assert.Equal(0x00323232, data.Colors[SystemColorIndex.Info].ToColorRef());
        Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.InfoText].ToColorRef());
        Assert.Equal(0x00333333, data.Colors[SystemColorIndex.ActiveCaption].ToColorRef());
        Assert.Equal(0x00424242, data.Colors[SystemColorIndex.InactiveCaption].ToColorRef());
        Assert.Equal(0x00EAEAEA, data.Colors[SystemColorIndex.InactiveCaptionText].ToColorRef());
        Assert.Equal(0x005D5D5D, data.Colors[SystemColorIndex.GrayText].ToColorRef());
        Assert.Equal(0x00444444, data.Colors[SystemColorIndex.WindowFrame].ToColorRef());
        Assert.Equal("Noto Sans", data.FontFamily);
        Assert.Equal(10, data.FontPointSize);
        Assert.Equal(96, data.PixelsPerInch);
        Assert.True(data.IsDark);
    }

    [Fact]
    public void Load_SuffixedColorNames_ResolveToBaseNames()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nova-detheme-test-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "colors.css"),
            "@define-color theme_bg_color_breeze #1e1e1e;\n@define-color theme_fg_color_breeze #dedede;\n");
        File.WriteAllText(Path.Combine(dir, "settings.ini"), "[Settings]\n");
        try
        {
            var source = new GtkSource(Path.Combine(dir, "settings.ini"), Path.Combine(dir, "colors.css"));
            ThemeData data = source.Load();
            Assert.Equal(0x001E1E1E, data.Colors[SystemColorIndex.Window].ToColorRef());
            Assert.Equal(0x00DEDEDE, data.Colors[SystemColorIndex.WindowText].ToColorRef());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFiles_YieldEmptyData()
    {
        var source = new GtkSource("/nonexistent/settings.ini", "/nonexistent/colors.css");
        ThemeData data = source.Load();
        Assert.Empty(data.Colors);
        Assert.Null(data.FontFamily);
        Assert.Null(data.IsDark);
    }
}
